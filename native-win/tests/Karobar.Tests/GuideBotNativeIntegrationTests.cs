using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Karobar.Background;
using Karobar.Data;
using Karobar.Host;
using Karobar.Host.GuideBot;
using Karobar.Host.Mascot;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Karobar.Tests.Wave9And10LiveHttpIntegrationTests;

namespace Karobar.Tests;

public class GuideBotNativeIntegrationTests
{
    [Fact]
    public async Task WaveG1ToG5_CubeQuotaGuard_KnowledgeRetriever_15TaskTypes_MascotFsm_And_NativeBridge_PassVerification()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var token = await LoginAsync(client, "qa-w910-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 1. Verify CubeQuotaGuard has ZERO EF Core `.Date` ValueConverter crash when API keys are configured!
            using (var scope = app.Services.CreateScope())
            {
                var efDb = scope.ServiceProvider.GetRequiredService<KarobarDbContext>();
                var quotaGuard = scope.ServiceProvider.GetRequiredService<CubeQuotaGuard>();
                var snap = new CubeSettingSnapshot
                {
                    CubeAiEnabled = true,
                    CubeGroqEnabled = true,
                    CubeGroqApiKey = "gsk_test_dummy_key_1234567890",
                    CubeGroqModel = "llama-3.3-70b-versatile",
                    CubeGroqDailyLimit = 2
                };

                var fp = CubeQuotaGuard.ComputeFingerprint(true, snap.CubeGroqModel, snap.CubeGroqDailyLimit, snap.CubeGroqApiKey);
                var (can1, status1, _, _, limit1) = await quotaGuard.InspectProviderAsync(efDb, "groq", snap);
                can1.Should().BeTrue();
                status1.Should().Be("ready");
                limit1.Should().Be(2);

                await quotaGuard.RecordAttemptAsync(efDb, "groq", fp);
                await quotaGuard.RecordSuccessAsync(efDb, "groq", fp);
                await quotaGuard.RecordAttemptAsync(efDb, "groq", fp);

                // Daily limit 2 reached -> must cleanly report quota_exhausted (no crash!)
                var (can2, status2, _, _, _) = await quotaGuard.InspectProviderAsync(efDb, "groq", snap);
                can2.Should().BeFalse();
                status2.Should().Be("quota_exhausted");

                // Permanent disable + config fingerprint change auto-reset
                await quotaGuard.RecordPermanentDisableAsync(efDb, "gemini", "invalid_key", 401, "invalid_api_key", "old_fp");
                var gemSnap = new CubeSettingSnapshot
                {
                    CubeAiEnabled = true,
                    CubeGeminiEnabled = true,
                    CubeGeminiApiKey = "AIza_new_rotated_key_999999999",
                    CubeGeminiModel = "gemini-2.5-flash",
                    CubeGeminiDailyLimit = 250
                };
                var (canGem, statusGem, _, _, _) = await quotaGuard.InspectProviderAsync(efDb, "gemini", gemSnap);
                canGem.Should().BeTrue("changing API key/config fingerprint must auto-reset disabled_until_reconfigured");
                statusGem.Should().Be("ready");
            }

            // 2. Verify CubeKnowledgeRetriever loads real 716KB cube-knowledge.json (80 screens, 53 intents) + accumulates learned store
            var repoRoot = FindRepoRoot();
            var retriever = new CubeKnowledgeRetriever(repoRoot);
            retriever.GetScreens().Count.Should().BeGreaterOrEqualTo(50, "real resources/assistant/cube-knowledge.json must be indexed");
            retriever.GetScreenByRoute("/sale-invoices/pos").Should().NotBeNull();

            var liveBoundary = retriever.DetectLiveRecordQuery("Aaj kitna munafa aur stock kitna hai?");
            liveBoundary.Should().NotBeNull("Roman Urdu live-data query must trigger privacy boundary");
            retriever.DetectLiveRecordQuery("How to check stock report?").Should().BeNull("procedural 'how to check' must NOT be blocked");

            var tempLearnedRetriever = new CubeKnowledgeRetriever(tempRoot);
            var (l1, _) = tempLearnedRetriever.RecordLearnedImprovement("How to park POS bill?", "/sale-invoices/pos", "Park Bill", "بل ہولڈ", "Press F8 to park bill.", "بل ہولڈ کے لیے F8 دبائیں۔", "groq");
            var (l2, _) = tempLearnedRetriever.RecordLearnedImprovement("How to add bonus units?", "/purchase-invoices/create", "Bonus Units", "بونس یونٹس", "Enter bonus in GRN.", "بونس درج کریں۔", "gemini");
            l1.Should().BeTrue();
            l2.Should().BeTrue();
            var learnedJson = await File.ReadAllTextAsync(Path.Combine(tempRoot, "storage", "app", "cube-learned-knowledge.json"));
            learnedJson.Should().Contain("park_pos_bill").And.Contain("add_bonus_units", "learned knowledge must accumulate, not overwrite");

            // 3. Verify 633-entry DRAP Salt Dictionary & Exact ExtractBrandCore (zero false positives!)
            CubeTaskExecutorService.PharmacySaltDictionary.Count.Should().BeGreaterOrEqualTo(450);
            CubeTaskExecutorService.ResolveFormulationFromProductName("Panadol CF Tablet 100s").Should().Be("Paracetamol + Chlorpheniramine + Pseudoephedrine");
            CubeTaskExecutorService.ResolveFormulationFromProductName("Calpol 120mg/5ml Syrup").Should().Be("Paracetamol", "must NOT false-match 'alp' -> Alprazolam");
            CubeTaskExecutorService.ResolveFormulationFromProductName("Pampers Baby Diaper Large").Should().BeNull("non-medicine items must be skipped");

            // 4. Verify live HTTP `/api/cube/execute-task`: exact `preview_only: true` contract + master entity create/update + delete block
            var brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "GSK QA Brand" });
            var catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "Tablets QA" });
            var supId = await db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Muller QA Supplier" });
            await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = "PRD-GB-001",
                ["barcode"] = "896999000111",
                ["name"] = "Panadol Extra 500mg Tab",
                ["formulation"] = "",
                ["pack_size"] = 10,
                ["brand_id"] = brandId,
                ["category_id"] = catId,
                ["supplier_id"] = supId,
                ["pack_purchase_price"] = 200m,
                ["pack_sale_price"] = 250m
            });

            var previewResp = await client.PostAsync("/api/cube/execute-task", JsonBody(new
            {
                task_type = "bulk_autofill_missing_formulations",
                preview_only = true,
                batch_size = 20,
                filter_query = "Panadol Extra"
            }));
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK, await previewResp.Content.ReadAsStringAsync());
            using (var pvDoc = JsonDocument.Parse(await previewResp.Content.ReadAsStringAsync()))
            {
                var pr = pvDoc.RootElement.GetProperty("task_meta").GetProperty("preview_result");
                pr.GetProperty("total_missing").GetInt32().Should().BeGreaterOrEqualTo(1);
                pr.GetProperty("resolvable_in_preview").GetInt32().Should().BeGreaterOrEqualTo(1);
                pr.GetProperty("preview_items").GetArrayLength().Should().BeGreaterOrEqualTo(1);
                pr.GetProperty("records").GetArrayLength().Should().BeGreaterOrEqualTo(1);
            }

            // Create product via structured task (confirmed) + update brand via structured task
            var createProdResp = await client.PostAsync("/api/cube/execute-task", JsonBody(new
            {
                task_type = "create_products",
                name = "Augmentin 625mg Tab",
                pack_size = 6,
                category = "Tablets QA",
                brand = "GSK QA Brand",
                supplier = "Muller QA Supplier",
                pack_sale_price = 420,
                user_confirmed = true
            }));
            createProdResp.StatusCode.Should().Be(HttpStatusCode.OK, await createProdResp.Content.ReadAsStringAsync());
            (await createProdResp.Content.ReadAsStringAsync()).Should().Contain("Created Product: Augmentin 625mg Tab");

            var updBrandResp = await client.PostAsync("/api/cube/execute-task", JsonBody(new
            {
                task_type = "update_brands",
                id = brandId,
                new_name = "GSK Pakistan Updated",
                user_confirmed = true
            }));
            updBrandResp.StatusCode.Should().Be(HttpStatusCode.OK, await updBrandResp.Content.ReadAsStringAsync());
            (await updBrandResp.Content.ReadAsStringAsync()).Should().Contain("Updated");

            // Conversational multi-step wizard over `/api/cube/ask`: `/add-brand` -> staged preview -> `"yes"` commits!
            var chatStage = await client.PostAsync("/api/cube/ask", JsonBody(new
            {
                question = "/add-brand name: Getz Pharma QA",
                language = "en",
                current_route = "/brands"
            }));
            chatStage.StatusCode.Should().Be(HttpStatusCode.OK, await chatStage.Content.ReadAsStringAsync());
            (await chatStage.Content.ReadAsStringAsync()).Should().Contain("awaiting_permission");

            var chatConfirm = await client.PostAsync("/api/cube/ask", JsonBody(new
            {
                question = "yes",
                language = "en",
                current_route = "/brands",
                user_confirmed = true
            }));
            chatConfirm.StatusCode.Should().Be(HttpStatusCode.OK, await chatConfirm.Content.ReadAsStringAsync());
            (await chatConfirm.Content.ReadAsStringAsync()).Should().Contain("Created Getz Pharma QA");

            // Native background task status endpoint
            var bgStatus = await client.GetAsync("/api/cube/bg-task/status");
            bgStatus.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Verify Native Bottom Mascot FSM, Preferences Store, Preload Bridge, and Installer Packaging
            var mascotPrefs = new MascotPreferencesStore(Path.Combine(tempRoot, "mascot-appdata"));
            mascotPrefs.Current.Enabled.Should().BeTrue();
            mascotPrefs.Current.Skin.Should().Be("cube");
            mascotPrefs.Update(p => { p.Skin = "bot"; p.RoamingEnabled = false; });
            var reloadedPrefs = new MascotPreferencesStore(Path.Combine(tempRoot, "mascot-appdata"));
            reloadedPrefs.Current.Skin.Should().Be("bot");
            reloadedPrefs.Current.RoamingEnabled.Should().BeFalse();

            var fsm = new MascotRoamingController(seed: 42);
            fsm.ClampToViewport(1440);
            // Simulate 46 seconds idle -> must enter Sleep
            for (var i = 0; i < 120; i++) fsm.Tick(0.4, -1, -1, 1440, 900);
            fsm.IsSleeping.Should().BeTrue("mascot must enter sleep after 45s idle");
            // User activity -> must Wake then Backflip
            fsm.NotifyUserActivity();
            fsm.ActiveTrick.Should().Be(MascotTrick.Wake);
            fsm.Tick(0.3, 500, 500, 1440, 900);
            fsm.Tick(0.3, 500, 500, 1440, 900);
            fsm.ActiveTrick.Should().Be(MascotTrick.Backflip);

            // Focus collision avoidance -> must scurry away from overlapping bottom input
            var beforeX = fsm.CurrentX;
            fsm.AvoidFocusedRect((int)beforeX - 10, 820, (int)beforeX + 120, 860, 1440, 900);
            fsm.IsMoving.Should().BeTrue();
            Math.Abs(fsm.TargetX - beforeX).Should().BeGreaterOrEqualTo(100);

            // Hover pill check
            fsm.IsHovered = true;
            fsm.GetPillBadgeText().Should().Be("Ask Guide Bot");

            // Preload bridge & build-native-win.ps1 packaging verification
            var preloadJs = KarobarPreloadScript.GetPreloadScript(false);
            preloadJs.Should().Contain("isNative: true");
            preloadJs.Should().Contain("toggleGuideBot");
            preloadJs.Should().Contain("setMascotPrefs");
            preloadJs.Should().Contain("toggle-guide-bot");
            preloadJs.Should().Contain("guide-focus-rect");

            var ps1Content = await File.ReadAllTextAsync(Path.Combine(repoRoot, "native-win", "build-native-win.ps1"));
            ps1Content.Should().Contain(@"resources\assistant", "Installer and OTA patch must package resources/assistant/cube-knowledge.json");
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
            if (Directory.Exists(Path.Combine(dir, "resources", "assistant"))) return dir;
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
