using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Karobar.Background;
using Karobar.Host;
using Karobar.Printing;
using Xunit;
using static Karobar.Tests.Wave9And10LiveHttpIntegrationTests;

namespace Karobar.Tests;

public class WhatsAppShareSystemTests
{
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string Body)> Requests { get; } = new();
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

        public StubHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
            => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            Requests.Add((request.Method, request.RequestUri?.ToString() ?? "", body));
            return _responder(request, body);
        }
    }

    [Fact]
    public void WhatsAppPackaging_And_SessionPreservation_VerifyNodeBaileysAndExclusions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var buildPs1 = File.ReadAllText(Path.Combine(repoRoot, "native-win", "build-native-win.ps1"));
        buildPs1.Should().Contain(@"runtime\node");
        buildPs1.Should().Contain(@"runtime\whatsapp-agent");
        buildPs1.Should().Contain("@whiskeysockets/baileys");
        buildPs1.Should().Contain("WA_AGENT_RUNTIME_OK");

        var iss = File.ReadAllText(Path.Combine(repoRoot, "native-win", "installer", "Karobar-Native-Setup.iss"));
        iss.Should().Contain(@"storage\app\whatsapp-agent\*");
        iss.Should().Contain(@"storage\app\.app-key");

        KarobarDesktopBridge.IsProtectedPatchPath("storage/app/whatsapp-agent/auth/creds.json").Should().BeTrue();
        KarobarDesktopBridge.IsProtectedPatchPath(@"storage\app\whatsapp-agent\auth\creds.json").Should().BeTrue();
        KarobarDesktopBridge.IsProtectedPatchPath("storage/app/.app-key").Should().BeTrue();
    }

    [Fact]
    public async Task DocumentShareEngine_TextDispatch_CountryCodes_And_MetaCloud24hTemplateFallback_WorkCleanly()
    {
        var handler = new StubHttpHandler((req, body) =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.EndsWith("/send-text", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"message_id\":\"local_txt_123\"}", Encoding.UTF8, "application/json")
                };
            }
            if (url.Contains("/media", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"media_999\"}", Encoding.UTF8, "application/json")
                };
            }
            if (url.Contains("/messages", StringComparison.OrdinalIgnoreCase))
            {
                // Accept template fallback (check BEFORE "type":"document" because template header also embeds a document parameter)
                if (body.Contains("\"type\":\"template\"", StringComparison.Ordinal) &&
                    body.Contains("\"name\":\"invoice_pdf_utility\"", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"messages\":[{\"id\":\"wamid.tpl_ok\"}]}", Encoding.UTF8, "application/json")
                    };
                }
                // Reject free-form document outside 24h window
                if (body.Contains("\"type\":\"document\"", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"error\":{\"code\":131047,\"message\":\"Re-engagement message\"}}", Encoding.UTF8, "application/json")
                    };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        var engine = new DocumentShareEngine(null, new PdfDocumentGenerator(), http);

        // 1. Multi-country E.164 normalization
        engine.NormalizePhone("0300-1234567", "92").Should().Be("923001234567");
        engine.NormalizePhone("050-1234567", "971").Should().Be("971501234567");
        engine.NormalizePhone("07700 900123", "44").Should().Be("447700900123");

        // 2. Local Agent /send-text dispatch
        var localCfg = new WhatsAppConfiguration
        {
            Enabled = true,
            Provider = "local_agent",
            ApiUrl = "http://127.0.0.1:3001",
            AccessToken = "karobar_wa_testkey",
            DefaultCountryCode = "92"
        };
        var (txtProv, txtMsgId) = await engine.DispatchTextViaWhatsAppApiAsync(localCfg, "923001234567", "Refill ready!");
        txtProv.Should().Be("local_agent");
        txtMsgId.Should().Be("local_txt_123");

        // 3. Meta Cloud 24h window template fallback
        var metaCfg = new WhatsAppConfiguration
        {
            Enabled = true,
            Provider = "meta_cloud",
            AccessToken = "EAAG_TEST",
            PhoneNumberId = "105928374651029",
            ApiVersion = "v21.0",
            TemplateName = "invoice_pdf_utility",
            TemplateLanguage = "en_US",
            AutoFallback = false
        };
        var (metaProv, metaId) = await engine.DispatchPdfViaWhatsAppApiAsync(
            metaCfg, "923001234567", Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"), "Invoice.pdf", "Caption");
        metaProv.Should().Be("meta_cloud");
        metaId.Should().Be("media_999");
        handler.Requests.Should().Contain(r => r.Url.Contains("/messages") && r.Body.Contains("\"type\":\"template\""));
    }

    [Fact]
    public async Task WhatsAppLiveHttp_CustomTemplates_CountryCode_RefillText_And_SidecarErrors_PassVerification()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var token = await LoginAsync(client, "qa-w910-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 1. Configure custom bilingual WhatsApp templates + non-92 country code (971) + Meta Cloud enabled
            var updResp = await client.PostAsync("/api/settings", JsonBody(new
            {
                store_name = "Al-Shifa Pharmacy QA",
                whatsapp_enabled = true,
                whatsapp_provider = "meta_cloud",
                whatsapp_access_token = "EAAG_QA_TOKEN_12345",
                whatsapp_default_country_code = "971",
                whatsapp_message_en = "CUSTOM_EN|{store_name}|{invoice_no}|{total}|{note}",
                whatsapp_message_ur = "CUSTOM_UR|{store_name}|{invoice_no}|{total}",
                note_en = "No return on cold chain items."
            }));
            updResp.StatusCode.Should().Be(HttpStatusCode.OK, await updResp.Content.ReadAsStringAsync());

            // 2. Seed a customer with UAE local number + a sale invoice and check /api/share/metadata
            var custId = await db.InsertAsync("customers", new Dictionary<string, object?>
            {
                ["name"] = "QA WhatsApp Customer",
                ["phone"] = "0501234567",
                ["is_regular"] = 1
            });
            var invId = await db.InsertAsync("sale_invoices", new Dictionary<string, object?>
            {
                ["customer_id"] = custId,
                ["user_id"] = 1,
                ["posted_number"] = "SI-WA-999",
                ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                ["gross_amount"] = 1250m,
                ["total"] = 1250m,
                ["total_receive"] = 1250m,
                ["sale_type"] = "retail",
                ["invoice_type"] = "debit",
                ["status"] = "posted"
            });

            var metaResp = await client.GetAsync($"/api/share/metadata?type=sale_invoice&id={invId}");
            metaResp.StatusCode.Should().Be(HttpStatusCode.OK, await metaResp.Content.ReadAsStringAsync());
            using (var mDoc = JsonDocument.Parse(await metaResp.Content.ReadAsStringAsync()))
            {
                var root = mDoc.RootElement;
                root.GetProperty("normalized_phone").GetString().Should().Be("971501234567");
                root.GetProperty("whatsapp_text_en").GetString().Should().Contain("CUSTOM_EN|Al-Shifa Pharmacy QA|SI-WA-999|Rs. 1,250.00|No return on cold chain items.");
                root.GetProperty("whatsapp_text_ur").GetString().Should().Contain("CUSTOM_UR|Al-Shifa Pharmacy QA|SI-WA-999|Rs. 1,250.00");
                root.GetProperty("whatsapp_api_configured").GetBoolean().Should().BeTrue();
                root.GetProperty("whatsapp_provider").GetString().Should().Be("meta_cloud");
            }

            // 3. Verify refill-reminder-preview reads REAL whatsapp_enabled & whatsapp_provider (never hardcoded false/wa_me!)
            var refillPreviewResp = await client.GetAsync($"/api/customers/{custId}/refill-reminder-preview");
            refillPreviewResp.StatusCode.Should().Be(HttpStatusCode.OK, await refillPreviewResp.Content.ReadAsStringAsync());
            using (var rDoc = JsonDocument.Parse(await refillPreviewResp.Content.ReadAsStringAsync()))
            {
                rDoc.RootElement.GetProperty("whatsapp_enabled").GetBoolean().Should().BeTrue();
                rDoc.RootElement.GetProperty("whatsapp_provider").GetString().Should().Be("meta_cloud");
                rDoc.RootElement.GetProperty("normalized_phone").GetString().Should().Be("971501234567");
            }

            // 4. Disable Direct API -> verify Browser Mode refill reminder returns clean text-only wa_me_fallback without pdf_base64
            await client.PostAsync("/api/settings", JsonBody(new { whatsapp_enabled = false }));
            var refillSendResp = await client.PostAsync("/api/share/whatsapp", JsonBody(new
            {
                type = "regular_refill_reminder",
                id = custId,
                customer_id = custId,
                phone = "0501234567",
                message = "Your monthly medicines are ready at Al-Shifa Pharmacy."
            }));
            refillSendResp.StatusCode.Should().Be(HttpStatusCode.OK, await refillSendResp.Content.ReadAsStringAsync());
            using (var sDoc = JsonDocument.Parse(await refillSendResp.Content.ReadAsStringAsync()))
            {
                sDoc.RootElement.GetProperty("mode").GetString().Should().Be("wa_me_fallback");
                sDoc.RootElement.GetProperty("wa_me_url").GetString().Should().StartWith("https://wa.me/971501234567?text=");
                sDoc.RootElement.TryGetProperty("pdf_base64", out _).Should().BeFalse("text refill reminders must not generate/send unnecessary PDF base64");
            }

            // 5. Verify LocalWhatsAppSidecarManager reports ok=false with clear error when agent script is missing
            var emptyRoot = Path.Combine(Path.GetTempPath(), $"karobar-wa-missing-{Guid.NewGuid():N}");
            Directory.CreateDirectory(emptyRoot);
            try
            {
                using var isolatedMgr = new LocalWhatsAppSidecarManager(emptyRoot);
                // If AppContext.BaseDirectory doesn't contain whatsapp-agent.mjs, StartAsync must report ok=false when script is absent
                if (isolatedMgr.ResolveAgentScriptPath() == null)
                {
                    var startRes = await isolatedMgr.StartAsync(null, null, enableInSettings: false);
                    startRes["ok"].Should().Be(false);
                    startRes["status"].Should().Be("error");
                }
            }
            finally
            {
                try { Directory.Delete(emptyRoot, recursive: true); } catch { }
            }
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }
}
