using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Background;
using Karobar.Core.Entities;
using Karobar.Core.Security;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Karobar.Tests;

public class Milestone4SecurityAndHygieneTests
{
    private static readonly byte[] ValidPdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");

    private sealed class IsolatedEnv : IAsyncDisposable
    {
        public string TempRoot { get; }
        public string DbPath { get; }
        public KarobarDb Db { get; }
        private readonly string? _prevRootEnv;

        private IsolatedEnv(string tempRoot, string dbPath, KarobarDb db, string? prevRootEnv)
        {
            TempRoot = tempRoot;
            DbPath = dbPath;
            Db = db;
            _prevRootEnv = prevRootEnv;
        }

        public static async Task<IsolatedEnv> CreateAsync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m4a_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));

            var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);

            var dbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            KarobarDb.FlushSchemaCache();
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: dbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var db = new KarobarDb(explicitSqlitePath: dbPath);
            return new IsolatedEnv(tempRoot, dbPath, db, prevRoot);
        }

        public KarobarDbContext CreateEfContext()
        {
            var opts = new DbContextOptionsBuilder<KarobarDbContext>()
                .UseSqlite($"Data Source={DbPath}")
                .Options;
            return new KarobarDbContext(opts);
        }

        public async Task<long> EnsureRoleAsync(string roleName)
        {
            var existing = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM roles WHERE LOWER(name) = LOWER(@name) LIMIT 1",
                new { name = roleName });
            if (existing != null) return Convert.ToInt64(existing["id"]);

            return await Db.InsertAsync("roles", new Dictionary<string, object?>
            {
                ["name"] = roleName,
                ["guard_name"] = "sanctum"
            });
        }

        public async Task EnsurePermissionAsync(string permName)
        {
            var existing = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM permissions WHERE LOWER(name) = LOWER(@name) LIMIT 1",
                new { name = permName });
            if (existing != null) return;

            await Db.InsertAsync("permissions", new Dictionary<string, object?>
            {
                ["name"] = permName,
                ["guard_name"] = "sanctum"
            });
        }

        public async Task<long> SeedUserAsync(
            string name,
            string email,
            string roleName,
            IEnumerable<string>? directPermissions = null,
            string? rawOrHashedPin = null)
        {
            await EnsureRoleAsync(roleName);
            var uid = await Db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["email"] = email,
                ["password"] = "$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi",
                ["status"] = "active",
                ["pin_code"] = rawOrHashedPin
            });
            await Db.SyncUserRolesAsync(uid, [roleName]);

            if (directPermissions != null)
            {
                var list = directPermissions.ToList();
                foreach (var p in list) await EnsurePermissionAsync(p);
                await Db.SyncUserPermissionsAsync(uid, list);
            }

            return uid;
        }

        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRootEnv);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); } catch { }
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(string Url, string? AuthHeader, string Body)> Requests { get; } = new();

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
        {
            _responder = responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            Requests.Add((request.RequestUri?.ToString() ?? "", request.Headers.Authorization?.ToString(), body));
            return _responder(request);
        }
    }

    private static DefaultHttpContext MakeJsonHttpContext(long userId, object? payload = null, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Sanctum"))
        };
        if (payload != null)
        {
            var json = payload is string s ? s : JsonSerializer.Serialize(payload);
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            ctx.Request.ContentType = "application/json";
        }
        return ctx;
    }

    private static HrPayrollController MakeHrCtrl(KarobarDb db, long userId, object? payload = null, params string[] roles)
    {
        return new HrPayrollController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(userId, payload, roles) }
        };
    }

    private static int GetStatus(IActionResult res) => res switch
    {
        ObjectResult o when o.StatusCode.HasValue => o.StatusCode.Value,
        OkObjectResult or OkResult => 200,
        StatusCodeResult sc => sc.StatusCode,
        _ => 200
    };

    private static string SerializeResult(IActionResult res)
    {
        var val = res is ObjectResult o ? o.Value : null;
        return JsonSerializer.Serialize(val);
    }

    private static void AssertNoPinCodeKeyAnywhere(string json, string context)
    {
        using var doc = JsonDocument.Parse(json);
        AssertNoPinCodeElement(doc.RootElement, context);
    }

    private static void AssertNoPinCodeElement(JsonElement el, string context)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                prop.Name.Should().NotBe("pin_code", $"raw pin_code must never be serialized ({context})");
                AssertNoPinCodeElement(prop.Value, context);
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                AssertNoPinCodeElement(item, context);
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "native-win", "Karobar.Native.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Karobar-App-ERP-v2 repository root.");
    }

    private static HttpMessageHandler? ExtractUnderlyingHandler(HttpMessageInvoker invoker)
    {
        var field = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(invoker) as HttpMessageHandler;
    }

    [Fact]
    public async Task F_M4_01_HrPayroll_RedactsPinCodeEverywhere_And_RequiresClearPinToWipe()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@hr.local", "Admin", rawOrHashedPin: OperatorPinService.HashPin("4821"));

        var cashAccId = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "Cash In Hand",
            ["code"] = "CASH-01",
            ["type"] = "cash",
            ["current_balance"] = 100000m,
            ["is_active"] = 1,
            ["is_default"] = 1
        });

        // 1. StoreEmployee with PIN hashes PIN in DB and redacts pin_code -> has_pin: true
        var storeWithPinRes = await MakeHrCtrl(env.Db, adminId, new
        {
            name = "Ayesha Khan",
            employee_code = "EMP-4001",
            pin_code = "7391",
            basic_salary = 30000m
        }, "Admin").StoreEmployee();
        GetStatus(storeWithPinRes).Should().Be(201);
        var storeWithPinJson = SerializeResult(storeWithPinRes);
        AssertNoPinCodeKeyAnywhere(storeWithPinJson, "StoreEmployee(with PIN)");
        using (var doc = JsonDocument.Parse(storeWithPinJson))
        {
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }

        var emp1Id = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM employees WHERE employee_code = 'EMP-4001'");
        var storedHash = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM employees WHERE id = @id", new { id = emp1Id });
        OperatorPinService.IsHashedPin(storedHash).Should().BeTrue();
        OperatorPinService.VerifyPin("7391", storedHash).Should().BeTrue();

        // 2. StoreEmployee without PIN -> has_pin: false
        var storeNoPinRes = await MakeHrCtrl(env.Db, adminId, new
        {
            name = "Bilal Ahmed",
            employee_code = "EMP-4002",
            basic_salary = 20000m
        }, "Admin").StoreEmployee();
        GetStatus(storeNoPinRes).Should().Be(201);
        var storeNoPinJson = SerializeResult(storeNoPinRes);
        AssertNoPinCodeKeyAnywhere(storeNoPinJson, "StoreEmployee(without PIN)");
        using (var doc = JsonDocument.Parse(storeNoPinJson))
        {
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeFalse();
        }
        var emp2Id = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM employees WHERE employee_code = 'EMP-4002'");

        // 3. Employees list redacts pin_code -> has_pin
        var listRes = await MakeHrCtrl(env.Db, adminId, roles: "Admin").Employees();
        GetStatus(listRes).Should().Be(200);
        var listJson = SerializeResult(listRes);
        AssertNoPinCodeKeyAnywhere(listJson, "Employees");
        using (var doc = JsonDocument.Parse(listJson))
        {
            var arr = doc.RootElement.EnumerateArray().ToList();
            arr.Should().HaveCount(2);
            arr.Single(e => e.GetProperty("employee_code").GetString() == "EMP-4001").GetProperty("has_pin").GetBoolean().Should().BeTrue();
            arr.Single(e => e.GetProperty("employee_code").GetString() == "EMP-4002").GetProperty("has_pin").GetBoolean().Should().BeFalse();
        }

        // 4. UpdateEmployee with empty/null pin_code WITHOUT clear_pin: true MUST preserve existing PIN
        var updBlankRes = await MakeHrCtrl(env.Db, adminId, new
        {
            employee_code = "EMP-4001",
            name = "Ayesha Khan Updated",
            pin_code = ""
        }, "Admin").UpdateEmployee(emp1Id);
        GetStatus(updBlankRes).Should().Be(200);
        AssertNoPinCodeKeyAnywhere(SerializeResult(updBlankRes), "UpdateEmployee(blank pin)");
        var afterBlankHash = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM employees WHERE id = @id", new { id = emp1Id });
        OperatorPinService.VerifyPin("7391", afterBlankHash).Should().BeTrue("blank pin_code without clear_pin must preserve existing PIN");

        var updNullRes = await MakeHrCtrl(env.Db, adminId, new
        {
            employee_code = "EMP-4001",
            name = "Ayesha Khan Updated 2",
            pin_code = (string?)null
        }, "Admin").UpdateEmployee(emp1Id);
        GetStatus(updNullRes).Should().Be(200);
        var afterNullHash = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM employees WHERE id = @id", new { id = emp1Id });
        OperatorPinService.VerifyPin("7391", afterNullHash).Should().BeTrue("null pin_code without clear_pin must preserve existing PIN");

        // 5. UpdateEmployee with clear_pin: true MUST clear PIN
        var updClearRes = await MakeHrCtrl(env.Db, adminId, new
        {
            employee_code = "EMP-4001",
            name = "Ayesha Khan Cleared",
            clear_pin = true
        }, "Admin").UpdateEmployee(emp1Id);
        GetStatus(updClearRes).Should().Be(200);
        var updClearJson = SerializeResult(updClearRes);
        AssertNoPinCodeKeyAnywhere(updClearJson, "UpdateEmployee(clear_pin)");
        using (var doc = JsonDocument.Parse(updClearJson))
        {
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeFalse();
        }
        (await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM employees WHERE id = @id", new { id = emp1Id })).Should().BeNullOrEmpty();

        // Restore PIN for subsequent payroll/attendance leak checks
        var restoreRes = await MakeHrCtrl(env.Db, adminId, new
        {
            employee_code = "EMP-4001",
            name = "Ayesha Khan",
            pin_code = "7391"
        }, "Admin").UpdateEmployee(emp1Id);
        GetStatus(restoreRes).Should().Be(200);

        // 6. SaveAttendance & SaveBulkAttendance redact pin_code and commit atomically
        var attRes = await MakeHrCtrl(env.Db, adminId, new
        {
            employee_id = emp1Id,
            date = "2026-11-10",
            status = "present"
        }, "Admin").SaveAttendance();
        GetStatus(attRes).Should().Be(200);
        var attJson = SerializeResult(attRes);
        AssertNoPinCodeKeyAnywhere(attJson, "SaveAttendance");
        using (var doc = JsonDocument.Parse(attJson))
        {
            doc.RootElement.GetProperty("employee").GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }

        var bulkAttRes = await MakeHrCtrl(env.Db, adminId, new
        {
            date = "2026-11-11",
            entries = new object[]
            {
                new { employee_id = emp1Id, status = "present" },
                new { employee_id = emp2Id, status = "late", late_minutes = 15 }
            }
        }, "Admin").SaveBulkAttendance();
        GetStatus(bulkAttRes).Should().Be(200);
        var savedBulkCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM employee_attendances WHERE date = '2026-11-11'");
        savedBulkCount.Should().Be(2);

        // 7. StoreAdvance redacts pin_code -> has_pin
        var advRes = await MakeHrCtrl(env.Db, adminId, new
        {
            employee_id = emp1Id,
            type = "advance",
            amount = 2500m,
            date = "2026-11-12",
            payment_method = "cash",
            account_id = cashAccId
        }, "Admin").StoreAdvance();
        GetStatus(advRes).Should().Be(201);
        var advJson = SerializeResult(advRes);
        AssertNoPinCodeKeyAnywhere(advJson, "StoreAdvance");
        using (var doc = JsonDocument.Parse(advJson))
        {
            doc.RootElement.GetProperty("employee").GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }

        // 8. GeneratePayrollRun, PayrollRuns, Overview, UpdatePayrollItem, PayPayrollItem, PayAllInRun all redact pin_code
        var genRes = await MakeHrCtrl(env.Db, adminId, new
        {
            period_month = "2026-11",
            auto_deduct_advances = true
        }, "Admin").GeneratePayrollRun();
        GetStatus(genRes).Should().Be(201);
        AssertNoPinCodeKeyAnywhere(SerializeResult(genRes), "GeneratePayrollRun");

        var runsRes = await MakeHrCtrl(env.Db, adminId, roles: "Admin").PayrollRuns();
        GetStatus(runsRes).Should().Be(200);
        AssertNoPinCodeKeyAnywhere(SerializeResult(runsRes), "PayrollRuns");

        var overviewRes = await MakeHrCtrl(env.Db, adminId, roles: "Admin").Overview("2026-11", "2026-11-11");
        GetStatus(overviewRes).Should().Be(200);
        AssertNoPinCodeKeyAnywhere(SerializeResult(overviewRes), "Overview");

        var runId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM payroll_runs WHERE period_month = '2026-11'");
        var item1Id = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM payroll_items WHERE payroll_run_id = @runId AND employee_id = @empId", new { runId, empId = emp1Id });

        var updItemRes = await MakeHrCtrl(env.Db, adminId, new { other_bonus = 500m }, "Admin").UpdatePayrollItem(item1Id);
        GetStatus(updItemRes).Should().Be(200);
        var updItemJson = SerializeResult(updItemRes);
        AssertNoPinCodeKeyAnywhere(updItemJson, "UpdatePayrollItem");
        using (var doc = JsonDocument.Parse(updItemJson))
        {
            doc.RootElement.GetProperty("employee").GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }

        var payItemRes = await MakeHrCtrl(env.Db, adminId, new { payment_method = "cash", account_id = cashAccId }, "Admin").PayPayrollItem(item1Id);
        GetStatus(payItemRes).Should().Be(200);
        var payItemJson = SerializeResult(payItemRes);
        AssertNoPinCodeKeyAnywhere(payItemJson, "PayPayrollItem");
        using (var doc = JsonDocument.Parse(payItemJson))
        {
            doc.RootElement.GetProperty("employee").GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }

        var payAllRes = await MakeHrCtrl(env.Db, adminId, new { payment_method = "cash", account_id = cashAccId }, "Admin").PayAllInRun(runId);
        GetStatus(payAllRes).Should().Be(200);
        AssertNoPinCodeKeyAnywhere(SerializeResult(payAllRes), "PayAllInRun");
    }

    [Fact]
    public async Task F_M4_01_HrPayroll_BlocksNonAdminLinkingOrModifyingAdminUserAndPin()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminHash = OperatorPinService.HashPin("4821");
        var adminId = await env.SeedUserAsync("Admin", "admin@guard.local", "Admin", rawOrHashedPin: adminHash);
        var hrMgrId = await env.SeedUserAsync("HR Manager", "hrmgr@guard.local", "Manager", ["hr.manage"]);
        var staffUserId = await env.SeedUserAsync("Cashier", "cashier@guard.local", "Cashier");

        // Seed an employee linked to Admin (as Admin)
        var createAdminEmpRes = await MakeHrCtrl(env.Db, adminId, new
        {
            name = "Admin Employee",
            employee_code = "EMP-ADM-1",
            user_id = adminId
        }, "Admin").StoreEmployee();
        GetStatus(createAdminEmpRes).Should().Be(201);
        var adminEmpId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM employees WHERE employee_code = 'EMP-ADM-1'");

        // 1. Non-admin HR manager cannot StoreEmployee linked to Admin user (with or without pin_code)
        var linkRes = await MakeHrCtrl(env.Db, hrMgrId, new
        {
            name = "Hijack Admin",
            employee_code = "EMP-HIJACK",
            user_id = adminId,
            pin_code = "9512"
        }, "Manager").StoreEmployee();
        GetStatus(linkRes).Should().Be(403);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM employees WHERE employee_code = 'EMP-HIJACK'")).Should().Be(0);

        // 2. Non-admin HR manager cannot UpdateEmployee on Admin-linked employee to change PIN, clear PIN, or unlink user_id
        var changePinRes = await MakeHrCtrl(env.Db, hrMgrId, new
        {
            employee_code = "EMP-ADM-1",
            name = "Admin Employee",
            pin_code = "9512"
        }, "Manager").UpdateEmployee(adminEmpId);
        GetStatus(changePinRes).Should().Be(403);

        var clearPinRes = await MakeHrCtrl(env.Db, hrMgrId, new
        {
            employee_code = "EMP-ADM-1",
            name = "Admin Employee",
            clear_pin = true
        }, "Manager").UpdateEmployee(adminEmpId);
        GetStatus(clearPinRes).Should().Be(403);

        var unlinkRes = await MakeHrCtrl(env.Db, hrMgrId, new
        {
            employee_code = "EMP-ADM-1",
            name = "Admin Employee",
            user_id = (long?)null
        }, "Manager").UpdateEmployee(adminEmpId);
        GetStatus(unlinkRes).Should().Be(403);

        // Also cannot re-link a non-admin employee onto an Admin user_id
        var nonAdminEmpRes = await MakeHrCtrl(env.Db, hrMgrId, new
        {
            name = "Regular Cashier",
            employee_code = "EMP-STAFF-1",
            user_id = staffUserId,
            pin_code = "6284"
        }, "Manager").StoreEmployee();
        GetStatus(nonAdminEmpRes).Should().Be(201);
        var staffEmpId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM employees WHERE employee_code = 'EMP-STAFF-1'");

        var relinkToAdminRes = await MakeHrCtrl(env.Db, hrMgrId, new
        {
            employee_code = "EMP-STAFF-1",
            name = "Regular Cashier",
            user_id = adminId
        }, "Manager").UpdateEmployee(staffEmpId);
        GetStatus(relinkToAdminRes).Should().Be(403);

        // Verify Admin PIN and linkage remained intact throughout
        var adminUserPinAfter = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @id", new { id = adminId });
        OperatorPinService.VerifyPin("4821", adminUserPinAfter).Should().BeTrue();
        var adminEmpUserIdAfter = await env.Db.ExecuteScalarAsync<long?>("SELECT user_id FROM employees WHERE id = @id", new { id = adminEmpId });
        adminEmpUserIdAfter.Should().Be(adminId);
    }

    [Fact]
    public async Task F_M4_02_FbrFiscalizationEngine_BlocksSsrfBeforeTryCatch_And_Rejects3xxRedirects()
    {
        // 1. Default HttpClientHandler disables auto-redirect
        var defaultEngine = new FbrFiscalizationEngine();
        var httpClientField = typeof(FbrFiscalizationEngine).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        var defaultHttp = httpClientField!.GetValue(defaultEngine) as HttpClient;
        var defaultHandler = ExtractUnderlyingHandler(defaultHttp!) as HttpClientHandler;
        defaultHandler.Should().NotBeNull();
        defaultHandler!.AllowAutoRedirect.Should().BeFalse();

        // 2. Shared FbrEndpointValidator parity with FbrController
        FbrEndpointValidator.IsAllowedFbrEndpointUrl("https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata", "digital_invoicing", out _, out _)
            .Should().BeTrue();
        FbrEndpointValidator.IsAllowedFbrEndpointUrl("http://127.0.0.1:8524/api/IMSFiscal/GetInvoiceNumberByModel", "tier1_pos_ims", out _, out _)
            .Should().BeTrue();
        FbrEndpointValidator.IsAllowedFbrEndpointUrl("http://169.254.169.254/latest/meta-data/", "digital_invoicing", out _, out _)
            .Should().BeFalse();
        FbrEndpointValidator.IsAllowedFbrEndpointUrl("https://user:pass@gw.fbr.gov.pk/post", "digital_invoicing", out _, out _)
            .Should().BeFalse();

        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@fbr302.local", "Admin");
        var custId = await env.Db.InsertAsync("customers", new Dictionary<string, object?>
        {
            ["name"] = "FBR Test Customer"
        });
        await using var efDb = env.CreateEfContext();

        var ssrfSetting = new Setting
        {
            StoreName = "SSRF QA Store",
            FbrEnabled = true,
            FbrEnvironment = "sandbox",
            FbrMode = "digital_invoicing",
            FbrSandboxUrl = "http://169.254.169.254/latest/meta-data/",
            FbrBearerToken = "SECRET_FBR_BEARER",
            FbrSellerNtn = "1234567",
            FbrPosId = "966130"
        };
        efDb.Settings.Add(ssrfSetting);

        var inv = new SaleInvoice
        {
            UserId = (int)adminId,
            CustomerId = (int)custId,
            PostedNumber = "SI-FBR-1",
            Date = DateTime.UtcNow,
            GrossAmount = 100m,
            Total = 100m,
            TotalReceive = 100m,
            Status = "posted",
            FbrSyncStatus = "pending"
        };
        efDb.SaleInvoices.Add(inv);
        await efDb.SaveChangesAsync();

        var ret = new SaleReturn
        {
            UserId = (int)adminId,
            CustomerId = (int)custId,
            PostedNumber = "SR-FBR-1",
            SaleInvoiceId = inv.Id,
            Date = DateTime.UtcNow,
            GrossTotal = 50m,
            Total = 50m,
            FbrSyncStatus = "pending"
        };
        efDb.SaleReturns.Add(ret);
        await efDb.SaveChangesAsync();

        // 3. Even in sandbox mode, SSRF URLs in FiscalizeSaleInvoiceAsync & FiscalizeSaleReturnAsync MUST fail with 0 HTTP requests and NEVER simulate sandbox synced
        var zeroHttpHandler = new StubHttpHandler();
        using var zeroHttp = new HttpClient(zeroHttpHandler);
        var engine = new FbrFiscalizationEngine(zeroHttp);

        var invRes = await engine.FiscalizeSaleInvoiceAsync(efDb, inv.Id, force: true);
        invRes["ok"].Should().Be(false);
        invRes["status"].Should().Be("failed");
        inv.FbrSyncStatus.Should().Be("failed");
        inv.FbrInvoiceNumber.Should().BeNullOrEmpty();

        var retRes = await engine.FiscalizeSaleReturnAsync(efDb, ret.Id, force: true);
        retRes["ok"].Should().Be(false);
        retRes["status"].Should().Be("failed");
        ret.FbrSyncStatus.Should().Be("failed");
        ret.FbrInvoiceNumber.Should().BeNullOrEmpty();

        // 4. TestConnectionAsync rejects SSRF URLs and overrides before making HTTP requests
        var ssrfTestVectors = new[]
        {
            ("digital_invoicing", "http://169.254.169.254/latest/meta-data/"),
            ("digital_invoicing", "https://attacker.example/steal"),
            ("tier1_pos_ims", "http://192.168.1.50:8524/api/IMSFiscal"),
            ("digital_invoicing", "https://user:pass@gw.fbr.gov.pk/di_data/v1/di/postinvoicedata")
        };
        foreach (var (mode, badUrl) in ssrfTestVectors)
        {
            var connRes = await engine.TestConnectionAsync(new Setting
            {
                FbrEnvironment = "sandbox",
                FbrMode = mode,
                FbrSandboxUrl = badUrl,
                FbrBearerToken = "SECRET_FBR_BEARER",
                FbrSellerNtn = "1234567",
                FbrPosId = "966130"
            });
            connRes["ok"].Should().Be(false);
            connRes["status"].Should().Be("invalid_url");
        }

        var overrideSsrfRes = await engine.TestConnectionAsync(
            new Setting
            {
                FbrEnvironment = "sandbox",
                FbrMode = "digital_invoicing",
                FbrBearerToken = "SECRET_FBR_BEARER",
                FbrSellerNtn = "1234567"
            },
            new Dictionary<string, string?> { ["fbr_production_url"] = "https://attacker.example/steal" });
        overrideSsrfRes["ok"].Should().Be(false);
        overrideSsrfRes["status"].Should().Be("invalid_url");

        zeroHttpHandler.Requests.Should().BeEmpty("zero outbound HTTP calls are allowed for SSRF FBR URLs");

        // 5. HTTP 302 redirect response is NOT treated as reachable in TestConnectionAsync or FbrController.TestConnection
        var redirectHandler = new StubHttpHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://attacker.example/steal-token");
            return r;
        });
        using var redirectHttp = new HttpClient(redirectHandler);
        var redirectEngine = new FbrFiscalizationEngine(redirectHttp);

        var conn302 = await redirectEngine.TestConnectionAsync(new Setting
        {
            FbrEnvironment = "production",
            FbrMode = "digital_invoicing",
            FbrBearerToken = "SECRET_FBR_BEARER",
            FbrSellerNtn = "1234567"
        });
        conn302["ok"].Should().Be(false);
        conn302["status"].Should().Be("gateway_error");
        conn302["http_status"].Should().Be(302);
        redirectHandler.Requests.Should().HaveCount(1, "redirect must not be auto-followed");

        var fbrCtrl = new FbrController(env.Db, redirectEngine, efDb)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeJsonHttpContext(adminId, new
                {
                    fbr_environment = "production",
                    fbr_mode = "digital_invoicing",
                    fbr_bearer_token = "SECRET_FBR_BEARER",
                    fbr_seller_ntn = "1234567"
                }, "Admin")
            }
        };
        var ctrl302Res = await fbrCtrl.TestConnection();
        GetStatus(ctrl302Res).Should().Be(422);
    }

    [Fact]
    public async Task PRT_01_DocumentShareEngine_EnforcesSsrfAllowlist_DisablesRedirects_And_NormalizesProvider()
    {
        // 1. Default HttpClientHandler disables auto-redirect
        var defaultEngine = new DocumentShareEngine();
        var httpClientField = typeof(DocumentShareEngine).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        var defaultHttp = httpClientField!.GetValue(defaultEngine) as HttpClient;
        var defaultHandler = ExtractUnderlyingHandler(defaultHttp!) as HttpClientHandler;
        defaultHandler.Should().NotBeNull();
        defaultHandler!.AllowAutoRedirect.Should().BeFalse();

        // 2. IsAllowedWhatsAppApiUrl positive & negative vectors
        DocumentShareEngine.IsAllowedWhatsAppApiUrl("http://127.0.0.1:3002", "local_agent", out _, out _).Should().BeTrue();
        DocumentShareEngine.IsAllowedWhatsAppApiUrl("http://127.0.0.1:3002", "meta_cloud", out _, out _).Should().BeTrue();
        DocumentShareEngine.IsAllowedWhatsAppApiUrl("http://192.168.1.50:3000", "waha", out _, out _).Should().BeTrue();
        DocumentShareEngine.IsAllowedWhatsAppApiUrl("https://wa.example.com/hook", "custom_http", out _, out _).Should().BeTrue();

        var ssrfVectors = new (string? Provider, string BadUrl)[]
        {
            ("custom_http", "http://169.254.169.254/latest/meta-data/"),
            ("custom_http", "http://[::ffff:169.254.169.254]/"),
            ("custom_http", "http://[fe80::1]/"),
            ("custom_http", "http://0.0.0.0:3000"),
            ("custom_http", "http://[::]:3000"),
            ("custom_http", "http://metadata.google.internal/computeMetadata/v1/"),
            ("custom_http", "http://metadata.internal/latest/"),
            ("custom_http", "https://user:pass@example.com"),
            ("custom_http", "file:///etc/passwd"),
            ("waha", "http://169.254.169.254/"),
            ("evolution_api", "http://169.254.169.254/"),
            ("whapi_cloud", "http://169.254.169.254/"),
            ("wasender", "http://169.254.169.254/"),
            ("ultramsg", "http://169.254.169.254/"),
            ("green_api", "http://169.254.169.254/"),
            ("meta_cloud", "http://169.254.169.254/"),
            ("local_agent", "https://evil.example.com/steal"),
            ("local_agent", "http://192.168.1.50:3002"),
            ("", "https://evil.example.com/hook"),
            (null, "https://evil.example.com/hook")
        };

        var zeroHttpHandler = new StubHttpHandler();
        using var zeroHttp = new HttpClient(zeroHttpHandler);
        var engine = new DocumentShareEngine(null, new PdfDocumentGenerator(), zeroHttp);

        foreach (var (provider, badUrl) in ssrfVectors)
        {
            DocumentShareEngine.IsAllowedWhatsAppApiUrl(badUrl, provider, out _, out _)
                .Should().BeFalse($"provider='{provider ?? "<null>"}' url='{badUrl}' must be rejected");

            var cfg = new WhatsAppConfiguration
            {
                Enabled = true,
                Provider = provider!,
                ApiUrl = badUrl,
                AccessToken = "SECRET_WA_TOKEN",
                PhoneNumberId = "105928374651029",
                AutoFallback = false
            };

            Func<Task> pdfAct = () => engine.DispatchPdfViaWhatsAppApiAsync(cfg, "923001234567", ValidPdfBytes, "Inv.pdf", "Test");
            await pdfAct.Should().ThrowAsync<InvalidOperationException>($"DispatchPdf must reject provider='{provider ?? "<null>"}' url='{badUrl}'");

            Func<Task> txtAct = () => engine.DispatchTextViaWhatsAppApiAsync(cfg, "923001234567", "Hello");
            await txtAct.Should().ThrowAsync<InvalidOperationException>($"DispatchText must reject provider='{provider ?? "<null>"}' url='{badUrl}'");
        }

        zeroHttpHandler.Requests.Should().BeEmpty("zero outbound HTTP calls may occur for blocked WhatsApp URLs");

        // 3. meta_cloud with default ApiUrl (http://127.0.0.1:3002) continues to dispatch to graph.facebook.com normally
        var okHandler = new StubHttpHandler(req =>
        {
            var u = req.RequestUri?.ToString() ?? "";
            if (u.Contains("/media"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"media_m4a_ok\"}", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"messages\":[{\"id\":\"wamid.ok\"}]}", Encoding.UTF8, "application/json")
            };
        });
        using var okHttp = new HttpClient(okHandler);
        var okEngine = new DocumentShareEngine(null, new PdfDocumentGenerator(), okHttp);
        var metaDefaultCfg = new WhatsAppConfiguration
        {
            Enabled = true,
            Provider = "meta_cloud",
            AccessToken = "EAAG_OK",
            PhoneNumberId = "105928374651029",
            AutoFallback = false
        };
        var (prov, mid) = await okEngine.DispatchPdfViaWhatsAppApiAsync(metaDefaultCfg, "923001234567", ValidPdfBytes, "Inv.pdf", "Caption");
        prov.Should().Be("meta_cloud");
        mid.Should().Be("media_m4a_ok");
        okHandler.Requests.Should().Contain(r => r.Url.StartsWith("https://graph.facebook.com/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ISS_23_CoreHygiene_LegacyGuideRemoved_InstallerCertOptIn_And_GitignoreHardened()
    {
        var repoRoot = FindRepoRoot();

        // 1. public/server-guide.html removed & no unpinned external CDN scripts in public/*.html
        var serverGuidePath = Path.Combine(repoRoot, "public", "server-guide.html");
        File.Exists(serverGuidePath).Should().BeFalse("public/server-guide.html must be removed");

        var publicDir = Path.Combine(repoRoot, "public");
        foreach (var htmlFile in Directory.GetFiles(publicDir, "*.html", SearchOption.AllDirectories))
        {
            var html = await File.ReadAllTextAsync(htmlFile);
            html.Should().NotContain("cdn.tailwindcss.com", $"no external CDN scripts allowed in {htmlFile}");
            html.Should().NotContain("unpkg.com", $"no external CDN scripts allowed in {htmlFile}");
            html.Should().NotContain("@babel/standalone", $"no external CDN scripts allowed in {htmlFile}");
        }

        // Preserve required public assets and developer cert file
        File.Exists(Path.Combine(publicDir, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(publicDir, "version.json")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "native-win", "assets", "MalikAsimShahzad-KarobarERP.cer")).Should().BeTrue();

        // 2. Karobar-Native-Setup.iss gates certutil -addstore behind unchecked opt-in task
        var issPath = Path.Combine(repoRoot, "native-win", "installer", "Karobar-Native-Setup.iss");
        var iss = await File.ReadAllTextAsync(issPath);
        iss.Should().Contain("Name: \"installrootcert\"");
        iss.Should().Contain("Flags: unchecked");
        iss.Split("Tasks: installrootcert;").Length.Should().BeGreaterThanOrEqualTo(3, "both Root and TrustedPublisher certutil entries must require Tasks: installrootcert;");

        // 3. Root .gitignore explicitly ignores runtime secrets/sessions
        var gitignore = await File.ReadAllTextAsync(Path.Combine(repoRoot, ".gitignore"));
        gitignore.Should().Contain("/storage/app/.app-key");
        gitignore.Should().Contain("/storage/app/whatsapp-agent/");
    }
}
