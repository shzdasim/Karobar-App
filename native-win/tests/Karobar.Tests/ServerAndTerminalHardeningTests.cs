using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Karobar.Host;
using Xunit;
using static Karobar.Tests.Wave9And10LiveHttpIntegrationTests;

namespace Karobar.Tests;

public class ServerAndTerminalHardeningTests
{
    [Fact]
    public async Task ServerAndTerminals_MultiCounterWalConcurrency_PairingToken_Blocking_Diagnostics_And_FirewallRule_PassVerification()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var token = await LoginAsync(client, "qa-w910-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 1. Fetch server health summary & pairing token via GET /api/terminals
            var indexResp = await client.GetAsync("/api/terminals");
            indexResp.StatusCode.Should().Be(HttpStatusCode.OK, await indexResp.Content.ReadAsStringAsync());
            string pairingToken;
            using (var doc = JsonDocument.Parse(await indexResp.Content.ReadAsStringAsync()))
            {
                var server = doc.RootElement.GetProperty("server");
                pairingToken = server.GetProperty("network_server_token").GetString() ?? "";
                pairingToken.Should().StartWith("KRB-NET-");
                server.GetProperty("db_driver").GetString().Should().Be("sqlite");
            }

            // 2. Verify live Diagnostics (all 6 real checks: database WAL, cache_sync, sequences, storage, network, firewall)
            var diagResp = await client.GetAsync("/api/server/diagnostics");
            diagResp.StatusCode.Should().Be(HttpStatusCode.OK, await diagResp.Content.ReadAsStringAsync());
            using (var diagDoc = JsonDocument.Parse(await diagResp.Content.ReadAsStringAsync()))
            {
                var checks = diagDoc.RootElement.GetProperty("checks").EnumerateArray().ToList();
                checks.Count.Should().Be(6);
                var ids = checks.Select(c => c.GetProperty("id").GetString()).ToList();
                ids.Should().Contain(new[] { "database", "cache_sync", "sequences", "storage", "network", "firewall" });

                var dbCheck = checks.First(c => c.GetProperty("id").GetString() == "database");
                dbCheck.GetProperty("status").GetString().Should().Be("ok");
                dbCheck.GetProperty("summary").GetString().Should().Contain("WAL");
            }

            // 3. Verify Troubleshoot actions (run_migrations, resync_sequences, force_sync_pulse)
            foreach (var action in new[] { "run_migrations", "resync_sequences", "force_sync_pulse" })
            {
                var tResp = await client.PostAsync("/api/server/troubleshoot", JsonBody(new { action }));
                tResp.StatusCode.Should().Be(HttpStatusCode.OK, await tResp.Content.ReadAsStringAsync());
            }

            // 4. Verify downloadable terminal config JSON includes mode=terminal + pairingToken
            var cfgResp = await client.GetAsync("/api/server/terminal-config?name=Counter%202");
            cfgResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var cfgDoc = JsonDocument.Parse(await cfgResp.Content.ReadAsStringAsync()))
            {
                cfgDoc.RootElement.GetProperty("mode").GetString().Should().Be("terminal");
                cfgDoc.RootElement.GetProperty("terminalName").GetString().Should().Be("Counter 2");
                cfgDoc.RootElement.GetProperty("pairingToken").GetString().Should().Be(pairingToken);
            }

            // 5. Register a Counter Terminal using Pairing Token on anonymous /api/terminals/ping
            var counterUid = "TERM-COUNTER2-QA99";
            using (var anonClient = new HttpClient { BaseAddress = client.BaseAddress })
            {
                using var pingReq = new HttpRequestMessage(HttpMethod.Post, "/api/terminals/ping");
                pingReq.Headers.TryAddWithoutValidation("X-Terminal-Id", counterUid);
                pingReq.Headers.TryAddWithoutValidation("X-Terminal-Name", "Counter 2 QA");
                pingReq.Headers.TryAddWithoutValidation("X-Terminal-Hostname", "COUNTER2-PC");
                pingReq.Headers.TryAddWithoutValidation("X-Terminal-Mode", "terminal");
                pingReq.Headers.TryAddWithoutValidation("X-Network-Token", pairingToken);
                pingReq.Content = JsonBody(new { });

                var pingResp = await anonClient.SendAsync(pingReq);
                pingResp.StatusCode.Should().Be(HttpStatusCode.OK, await pingResp.Content.ReadAsStringAsync());
                using var pDoc = JsonDocument.Parse(await pingResp.Content.ReadAsStringAsync());
                pDoc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
                pDoc.RootElement.GetProperty("terminal").GetProperty("terminal_uid").GetString().Should().Be(counterUid);
                pDoc.RootElement.GetProperty("terminal").GetProperty("is_authorized").GetBoolean().Should().BeTrue();
            }

            // 6. Block the terminal -> Verify HTTP 403 TERMINAL_UNAUTHORIZED -> Re-authorize -> Verify 200 OK
            var termRow = await db.QuerySingleOrDefaultAsync("SELECT id, hostname FROM terminals WHERE terminal_uid = @uid", new { uid = counterUid });
            termRow.Should().NotBeNull();
            termRow!["hostname"]?.ToString().Should().Be("COUNTER2-PC", "remote terminal hostname must be preserved cleanly");
            var termId = Convert.ToInt64(termRow["id"]);

            var blockResp = await client.PutAsync($"/api/terminals/{termId}", JsonBody(new { is_authorized = false }));
            blockResp.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var blockedReq = new HttpRequestMessage(HttpMethod.Get, "/api/sync/pulse"))
            {
                blockedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                blockedReq.Headers.TryAddWithoutValidation("X-Terminal-Id", counterUid);
                var blockedResp = await client.SendAsync(blockedReq);
                blockedResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "blocked terminal must receive HTTP 403");
                var blockedBody = await blockedResp.Content.ReadAsStringAsync();
                blockedBody.Should().Contain("TERMINAL_UNAUTHORIZED");
            }

            var authResp = await client.PutAsync($"/api/terminals/{termId}", JsonBody(new { is_authorized = true }));
            authResp.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var okReq = new HttpRequestMessage(HttpMethod.Get, "/api/sync/pulse"))
            {
                okReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                okReq.Headers.TryAddWithoutValidation("X-Terminal-Id", counterUid);
                var okResp = await client.SendAsync(okReq);
                okResp.StatusCode.Should().Be(HttpStatusCode.OK, "re-authorized terminal must immediately regain access");
            }

            // 7. Multi-Counter SQLite WAL Write Concurrency Stress Test (10 simultaneous parallel writers!)
            var parallelTasks = Enumerable.Range(1, 10).Select(async i =>
            {
                var id = await db.InsertAsync("brands", new Dictionary<string, object?>
                {
                    ["name"] = $"Concurrent LAN Brand #{i}-{Guid.NewGuid():N}"
                });
                id.Should().BeGreaterThan(0);
            }).ToArray();
            await Task.WhenAll(parallelTasks);

            // 8. Verify NetworkConfigManager pairingToken persistence & TestServerConnectionAsync with pairingTokenOverride
            var netMgr = new NetworkConfigManager(tempRoot, 18960, Path.Combine(tempRoot, "net-appdata"));
            var savedCfg = netMgr.SaveNetworkConfig(new NetworkConfigUpdate
            {
                Mode = "terminal",
                ServerUrl = client.BaseAddress!.ToString().TrimEnd('/'),
                TerminalName = "Counter 3 QA",
                PairingToken = pairingToken
            });
            savedCfg.PairingToken.Should().Be(pairingToken);

            var testConn = await netMgr.TestServerConnectionAsync(
                client.BaseAddress!.ToString().TrimEnd('/'),
                "Counter 3 QA",
                "2.0.1",
                pairingTokenOverride: pairingToken);
            testConn.Ok.Should().BeTrue(testConn.Error ?? "");

            // 9. Verify Inno Setup installer firewall rule covers local LAN subnet across all Windows network profiles
            var repoRoot = FindRepoRoot();
            var issText = await File.ReadAllTextAsync(Path.Combine(repoRoot, "native-win", "installer", "Karobar-Native-Setup.iss"));
            issText.Should().Contain("remoteip=localsubnet profile=any", "LAN firewall rule must work even when Windows defaults Wi-Fi/Ethernet to Public profile");
            issText.Should().Contain("PairingTokenEdit");
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "native-win", "installer"))) return dir;
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
