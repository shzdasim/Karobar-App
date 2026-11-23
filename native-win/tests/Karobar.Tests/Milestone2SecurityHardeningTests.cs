using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Api.Helpers;
using Karobar.Background;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Karobar.Tests;

public class Milestone2SecurityHardeningTests
{
    // Valid minimal binary payloads for PNG, JPEG, WebP, and PDF
    private static readonly byte[] ValidPngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static readonly byte[] ValidJpegBytes =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46,
        0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48,
        0x00, 0x48, 0x00, 0x00, 0xFF, 0xD9
    ];

    private static readonly byte[] ValidWebpBytes =
    [
        (byte)'R', (byte)'I', (byte)'F', (byte)'F',
        0x14, 0x00, 0x00, 0x00,
        (byte)'W', (byte)'E', (byte)'B', (byte)'P',
        (byte)'V', (byte)'P', (byte)'8', (byte)' ',
        0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

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
            var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m2_{Guid.NewGuid():N}");
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

        public ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRootEnv);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static DefaultHttpContext MakeJsonHttpContext(long userId, object? payload = null)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            ], "Sanctum"))
        };
        ctx.Items["KarobarUserId"] = userId;
        if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload);
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            ctx.Request.ContentType = "application/json";
        }
        return ctx;
    }

    private static DefaultHttpContext MakeMultipartHttpContext(
        long userId,
        Dictionary<string, string>? fields = null,
        string? fileField = null,
        string? fileName = null,
        string? contentType = null,
        byte[]? fileBytes = null)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            ], "Sanctum"))
        };
        ctx.Items["KarobarUserId"] = userId;
        ctx.Request.ContentType = "multipart/form-data; boundary=----KarobarTestBoundary";

        var formDict = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        if (fields != null)
        {
            foreach (var kv in fields)
            {
                formDict[kv.Key] = new StringValues(kv.Value);
            }
        }

        var files = new FormFileCollection();
        if (fileField != null && fileName != null && fileBytes != null)
        {
            var stream = new MemoryStream(fileBytes);
            var ff = new FormFile(stream, 0, fileBytes.Length, fileField, fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType ?? "application/octet-stream"
            };
            files.Add(ff);
        }

        ctx.Request.Form = new Microsoft.AspNetCore.Http.FormCollection(formDict, files);
        return ctx;
    }

    private static int GetStatus(IActionResult result)
    {
        return (result as IStatusCodeActionResult)?.StatusCode ?? 0;
    }

    private static JsonDocument ParseBody(IActionResult result)
    {
        var obj = result as ObjectResult;
        obj.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(obj!.Value));
    }

    // =========================================================================
    // ISS-01: RBAC Privilege Escalation
    // =========================================================================

    [Fact]
    public async Task ISS01_UserController_BlocksNonAdminDirectPermissionAssignmentAndPinProbing()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@test.local", "Admin", rawOrHashedPin: OperatorPinService.HashPin("9182"));
        var managerId = await env.SeedUserAsync(
            "UserManager",
            "mgr@test.local",
            "UserManager",
            directPermissions: ["user.view", "user.create", "user.update"]);
        await env.EnsurePermissionAsync("settings.update");
        await env.EnsurePermissionAsync("role.sync.permissions");

        var pinService = new OperatorPinService(env.Db);
        UserController MakeCtrl(long uid, object? payload) => new(env.Db, pinService)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid, payload) }
        };

        // 1. Non-admin with user.create attempting Store with direct permissions -> 403 and user NOT created
        var storeEscalation = await MakeCtrl(managerId, new
        {
            name = "Escalated",
            email = "escalated@test.local",
            password = "Password123!",
            roles = new[] { "UserManager" },
            permissions = new[] { "settings.update", "role.sync.permissions" }
        }).Store();
        GetStatus(storeEscalation).Should().Be(403);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM users WHERE email = 'escalated@test.local'")).Should().Be(0);

        // 2. Non-admin creating normal user with empty permissions -> 201
        var storeOk = await MakeCtrl(managerId, new
        {
            name = "Normal Staff",
            email = "staff@test.local",
            password = "Password123!",
            roles = Array.Empty<string>(),
            permissions = Array.Empty<string>()
        }).Store();
        GetStatus(storeOk).Should().Be(201);
        long staffId;
        using (var doc = ParseBody(storeOk))
        {
            staffId = doc.RootElement.GetProperty("id").GetInt64();
        }

        // 3. Non-admin with user.update attempting Update to grant direct permissions -> 403 and unchanged
        var updateEscalation = await MakeCtrl(managerId, new
        {
            name = "Staff Renamed",
            email = "staff@test.local",
            permissions = new[] { "settings.update" }
        }).Update(staffId);
        GetStatus(updateEscalation).Should().Be(403);
        (await env.Db.GetUserDirectPermissionsAsync(staffId)).Should().BeEmpty();
        var staffRow = await env.Db.QuerySingleOrDefaultAsync("SELECT name FROM users WHERE id = @id", new { id = staffId });
        staffRow!["name"]!.ToString().Should().Be("Normal Staff");

        // 4. Non-admin calling Update with unchanged permissions: [] (UserForm.jsx pattern) -> 200
        var updateBenign = await MakeCtrl(managerId, new
        {
            name = "Normal Staff Updated",
            email = "staff@test.local",
            permissions = Array.Empty<string>()
        }).Update(staffId);
        GetStatus(updateBenign).Should().Be(200);

        // 5. Non-admin cannot probe Admin PIN via 422 on Update(adminId) -> 403 before PIN validation
        var probeAdminPin = await MakeCtrl(managerId, new
        {
            name = "Probe",
            email = "admin@test.local",
            pin_code = "9182"
        }).Update(adminId);
        GetStatus(probeAdminPin).Should().Be(403);

        // 6. Admin CAN assign direct permissions in Store and Update, and non-admin echoing existing permissions succeeds
        var adminGrant = await MakeCtrl(adminId, new
        {
            name = "Normal Staff Updated",
            email = "staff@test.local",
            permissions = new[] { "user.view" }
        }).Update(staffId);
        GetStatus(adminGrant).Should().Be(200);
        (await env.Db.GetUserDirectPermissionsAsync(staffId))
            .Select(p => p["name"]!.ToString()).Should().ContainSingle("user.view");

        var nonAdminEchoSame = await MakeCtrl(managerId, new
        {
            name = "Staff Safe Edit",
            email = "staff@test.local",
            permissions = new[] { "user.view" }
        }).Update(staffId);
        GetStatus(nonAdminEchoSame).Should().Be(200);
        (await env.Db.GetUserDirectPermissionsAsync(staffId))
            .Select(p => p["name"]!.ToString()).Should().ContainSingle("user.view");
    }

    [Fact]
    public async Task ISS01_RoleController_BlocksNonAdminPermissionSyncOnStoreAndUpdate()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@role.local", "Admin");
        var roleMgrId = await env.SeedUserAsync(
            "RoleMgr",
            "rolemgr@role.local",
            "RoleMgr",
            directPermissions: ["role.view", "role.create", "role.update", "role.sync.permissions"]);
        await env.EnsurePermissionAsync("user.manage");
        await env.EnsurePermissionAsync("settings.update");

        RoleController MakeCtrl(long uid, object? payload) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid, payload) }
        };

        // 1. Non-admin Store with permissions -> 403 and role NOT inserted
        var storeEsc = await MakeCtrl(roleMgrId, new
        {
            name = "SuperPowerRole",
            permissions = new[] { "user.manage", "settings.update" }
        }).Store();
        GetStatus(storeEsc).Should().Be(403);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM roles WHERE name = 'SuperPowerRole'")).Should().Be(0);

        // 2. Non-admin Store without permissions -> 201
        var storeOk = await MakeCtrl(roleMgrId, new
        {
            name = "ClerkRole",
            permissions = Array.Empty<string>()
        }).Store();
        GetStatus(storeOk).Should().Be(201);
        long clerkRoleId;
        using (var doc = ParseBody(storeOk))
        {
            clerkRoleId = doc.RootElement.GetProperty("id").GetInt64();
        }

        // 3. Admin grants "user.manage" or "role.view" to ClerkRole via Update -> 200
        var adminSync = await MakeCtrl(adminId, new
        {
            name = "ClerkRole",
            permissions = new[] { "role.view" }
        }).Update(clerkRoleId);
        GetStatus(adminSync).Should().Be(200);

        // 4. Non-admin Update trying to add "user.manage" -> 403 and role unchanged
        var updateEsc = await MakeCtrl(roleMgrId, new
        {
            name = "ClerkRoleHijacked",
            permissions = new[] { "role.view", "user.manage" }
        }).Update(clerkRoleId);
        GetStatus(updateEsc).Should().Be(403);
        var roleRow = await env.Db.QuerySingleOrDefaultAsync("SELECT name FROM roles WHERE id = @id", new { id = clerkRoleId });
        roleRow!["name"]!.ToString().Should().Be("ClerkRole");

        // 5. Non-admin Update renaming while keeping exact existing permissions (RoleForm.jsx pattern) -> 200
        var renameOnly = await MakeCtrl(roleMgrId, new
        {
            name = "JuniorClerk",
            permissions = new[] { "role.view" }
        }).Update(clerkRoleId);
        GetStatus(renameOnly).Should().Be(200);
        var updatedRole = await env.Db.QuerySingleOrDefaultAsync("SELECT name FROM roles WHERE id = @id", new { id = clerkRoleId });
        updatedRole!["name"]!.ToString().Should().Be("JuniorClerk");
    }

    [Fact]
    public async Task ISS01_TradeSchemeController_SeparatesPosViewFromDedicatedSchemeMutations()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@scheme.local", "Admin");
        var cashierId = await env.SeedUserAsync(
            "Cashier",
            "cashier@scheme.local",
            "Cashier",
            directPermissions:
            [
                "sale-invoice.view", "sale-invoice.create", "sale-invoice.update",
                "sale-invoice.discount-override", "product.view", "product.create", "product.update",
                "barcode-pos.view", "barcode-pos.create"
            ]);
        var schemeMgrId = await env.SeedUserAsync(
            "SchemeMgr",
            "schememgr@scheme.local",
            "SchemeMgr",
            directPermissions: ["trade-scheme.manage"]);

        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "M2 Cat" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "M2 Brand" });
        var suppId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "M2 Supp" });
        var prodId = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "M2-PRD-1",
            ["name"] = "M2 Test Med",
            ["barcode"] = "M2BAR001",
            ["pack_size"] = 10,
            ["narcotic"] = "no",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = suppId
        });

        object BuildSchemeBody(string name) => new
        {
            name,
            scheme_type = "slab",
            target_type = "product",
            target_id = prodId,
            bonus_type = "unit",
            sale_type = "all",
            is_active = true,
            slabs = new[] { new { min_qty = 10, bonus_qty = 1 } }
        };

        TradeSchemeController MakeCtrl(long uid, object? payload = null) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid, payload) }
        };

        // 1. Admin creates a scheme -> 201
        var adminCreate = await MakeCtrl(adminId, BuildSchemeBody("Admin Bonus")).Store();
        GetStatus(adminCreate).Should().Be(201);
        long schemeId;
        using (var doc = ParseBody(adminCreate))
        {
            schemeId = doc.RootElement.GetProperty("scheme").GetProperty("id").GetInt64();
        }

        // 2. Cashier keeps read/POS lookup access -> 200
        GetStatus(await MakeCtrl(cashierId).ActiveLookup()).Should().Be(200);
        GetStatus(await MakeCtrl(cashierId).Index()).Should().Be(200);
        GetStatus(await MakeCtrl(cashierId).Show(schemeId)).Should().Be(200);

        // 3. Cashier is blocked from ALL mutations -> 403
        GetStatus(await MakeCtrl(cashierId, BuildSchemeBody("Free Giveaway")).Store()).Should().Be(403);
        GetStatus(await MakeCtrl(cashierId, BuildSchemeBody("Tampered")).Update(schemeId)).Should().Be(403);
        GetStatus(await MakeCtrl(cashierId).ToggleActive(schemeId)).Should().Be(403);
        GetStatus(await MakeCtrl(cashierId).Destroy(schemeId)).Should().Be(403);

        // 4. Dedicated trade-scheme.manage user succeeds -> 201 / 200
        var mgrCreate = await MakeCtrl(schemeMgrId, BuildSchemeBody("Manager Scheme")).Store();
        GetStatus(mgrCreate).Should().Be(201);
        long mgrSchemeId;
        using (var doc = ParseBody(mgrCreate))
        {
            mgrSchemeId = doc.RootElement.GetProperty("scheme").GetProperty("id").GetInt64();
        }
        GetStatus(await MakeCtrl(schemeMgrId, BuildSchemeBody("Manager Scheme V2")).Update(mgrSchemeId)).Should().Be(200);
        GetStatus(await MakeCtrl(schemeMgrId).ToggleActive(mgrSchemeId)).Should().Be(200);
        GetStatus(await MakeCtrl(schemeMgrId).Destroy(mgrSchemeId)).Should().Be(200);
    }

    // =========================================================================
    // ISS-02: Operator PIN Hashing, Redaction & Brute-Force Lockout
    // =========================================================================

    [Fact]
    public async Task ISS02_OperatorPins_AreHashedRedactedMigratedAndUniquenessChecked()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        OperatorPinService.ResetRateLimitState();

        var adminId = await env.SeedUserAsync("Admin", "admin@pin.local", "Admin");
        var empId = await env.Db.InsertAsync("employees", new Dictionary<string, object?>
        {
            ["employee_code"] = "EMP-LEGACY",
            ["name"] = "Legacy Emp",
            ["status"] = "active",
            ["pin_code"] = "4329" // legacy plaintext row
        });

        var pinService = new OperatorPinService(env.Db);
        UserController MakeUserCtrl(long uid, object? payload = null) => new(env.Db, pinService)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid, payload) }
        };
        OperatorPinController MakePinCtrl(long uid, object? payload = null) => new(env.Db, pinService)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid, payload) }
        };

        // 1. UserController.Store hashes pin_code with PBKDF2-SHA256 and redacts it from response (returning has_pin: true)
        var storeRes = await MakeUserCtrl(adminId, new
        {
            name = "Cashier PIN",
            email = "cashierpin@pin.local",
            password = "Password123!",
            pin_code = "5824",
            roles = Array.Empty<string>(),
            permissions = Array.Empty<string>()
        }).Store();
        GetStatus(storeRes).Should().Be(201);

        long cashierId;
        using (var doc = ParseBody(storeRes))
        {
            var json = doc.RootElement.GetRawText();
            json.Should().NotContain("pin_code");
            json.Should().NotContain("5824");
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeTrue();
            cashierId = doc.RootElement.GetProperty("id").GetInt64();
        }

        var storedUserPin = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @id", new { id = cashierId });
        OperatorPinService.IsHashedPin(storedUserPin).Should().BeTrue();
        storedUserPin.Should().NotBe("5824");
        OperatorPinService.VerifyPin("5824", storedUserPin).Should().BeTrue();
        OperatorPinService.VerifyPin("9999", storedUserPin).Should().BeFalse();

        // 2. Directory, Index, Show, Update, Assign, SyncRoles, SyncPermissions NEVER leak pin_code
        var dirRes = await MakePinCtrl(adminId).Directory();
        GetStatus(dirRes).Should().Be(200);
        using (var doc = ParseBody(dirRes))
        {
            var raw = doc.RootElement.GetRawText();
            raw.Should().NotContain("pin_code");
            raw.Should().NotContain("5824");
            raw.Should().NotContain("4329");
            raw.Should().NotContain("$pbkdf2-sha256$");
            doc.RootElement.GetProperty("users").EnumerateArray()
                .First(u => u.GetProperty("id").GetInt64() == cashierId)
                .GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }

        GetStatus(await MakeUserCtrl(adminId).Index()).Should().Be(200);
        using (var doc = ParseBody(await MakeUserCtrl(adminId).Index()))
        {
            doc.RootElement.GetRawText().Should().NotContain("pin_code");
        }
        using (var doc = ParseBody(await MakeUserCtrl(adminId).Show(cashierId)))
        {
            doc.RootElement.GetRawText().Should().NotContain("pin_code");
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }

        // 3. UserController.Update with pin_code: null (UserForm.jsx) preserves existing PIN unless clear_pin: true
        var editWithoutClear = await MakeUserCtrl(adminId, new
        {
            name = "Cashier PIN Edited",
            email = "cashierpin@pin.local",
            pin_code = (string?)null
        }).Update(cashierId);
        GetStatus(editWithoutClear).Should().Be(200);
        using (var doc = ParseBody(editWithoutClear))
        {
            doc.RootElement.GetRawText().Should().NotContain("pin_code");
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }
        var stillHashed = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @id", new { id = cashierId });
        OperatorPinService.VerifyPin("5824", stillHashed).Should().BeTrue();

        // 4. ValidateUniquePinAsync rejects collisions against BOTH hashed (5824) and legacy plaintext (4329) rows
        var dupHashedAct = async () => await pinService.ValidateUniquePinAsync("5824");
        await dupHashedAct.Should().ThrowAsync<OperatorPinException>();

        var dupLegacyAct = async () => await pinService.ValidateUniquePinAsync("4329");
        await dupLegacyAct.Should().ThrowAsync<OperatorPinException>();

        var generatedPin = await pinService.GenerateUniquePinAsync();
        generatedPin.Should().MatchRegex(@"^\d{4}$");
        generatedPin.Should().NotBe("5824");
        generatedPin.Should().NotBe("4329");

        // 5. OperatorPinController.Assign stores hash and returns has_pin (not pin_code)
        var assignRes = await MakePinCtrl(adminId, new { type = "user", id = cashierId, pin_code = "7418" }).Assign();
        GetStatus(assignRes).Should().Be(200);
        using (var doc = ParseBody(assignRes))
        {
            doc.RootElement.GetRawText().Should().NotContain("pin_code");
            doc.RootElement.GetRawText().Should().NotContain("7418");
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeTrue();
        }

        // 6. Legacy plaintext employee row (4329) verifies and transparently upgrades to PBKDF2-SHA256
        var resolvedLegacy = await pinService.ResolveOperatorAsync("4329", adminId, "sale_invoice");
        resolvedLegacy["verified_via_pin"].Should().Be(true);
        resolvedLegacy["operator_name"].Should().Be("Legacy Emp");

        var upgradedEmpPin = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM employees WHERE id = @id", new { id = empId });
        OperatorPinService.IsHashedPin(upgradedEmpPin).Should().BeTrue();
        OperatorPinService.VerifyPin("4329", upgradedEmpPin).Should().BeTrue();

        // 7. clear_pin: true clears PIN on UserController.Update
        var clearRes = await MakeUserCtrl(adminId, new
        {
            name = "Cashier PIN Cleared",
            email = "cashierpin@pin.local",
            clear_pin = true
        }).Update(cashierId);
        GetStatus(clearRes).Should().Be(200);
        using (var doc = ParseBody(clearRes))
        {
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeFalse();
        }
        var clearedDbPin = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @id", new { id = cashierId });
        clearedDbPin.Should().BeNull();
    }

    [Fact]
    public async Task ISS02_ResolveOperatorAsync_EnforcesBruteForceLockoutAndRecoveryWithInjectedClock()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        OperatorPinService.ResetRateLimitState();

        var hashedPin = OperatorPinService.HashPin("6391");
        var userId = await env.SeedUserAsync("Operator", "op@lock.local", "Admin", rawOrHashedPin: hashedPin);
        await env.Db.InsertAsync("settings", new Dictionary<string, object?>
        {
            ["store_name"] = "Pin Lock Store",
            ["require_operator_pin"] = 1,
            ["operator_pin_modules"] = "[\"sale_invoice\"]"
        });

        var now = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
        OperatorPinService MakeSvc() => new(env.Db, () => now);

        // 1. Empty PIN prompts do NOT consume failed attempt budget
        for (int i = 0; i < 6; i++)
        {
            var emptyAct = async () => await MakeSvc().ResolveOperatorAsync("", userId, "sale_invoice");
            var ex = (await emptyAct.Should().ThrowAsync<OperatorPinException>()).Which;
            ex.StatusCode.Should().Be(422);
        }

        // 2. First 4 bad PIN guesses -> 422
        for (int i = 0; i < 4; i++)
        {
            var badAct = async () => await MakeSvc().ResolveOperatorAsync("0000", userId, "sale_invoice");
            var ex = (await badAct.Should().ThrowAsync<OperatorPinException>()).Which;
            ex.StatusCode.Should().Be(422);
        }

        // 3. 5th bad PIN guess within 2 minutes -> 429 lockout
        var fifthAct = async () => await MakeSvc().ResolveOperatorAsync("0000", userId, "sale_invoice");
        var lockEx = (await fifthAct.Should().ThrowAsync<OperatorPinException>()).Which;
        lockEx.StatusCode.Should().Be(429);
        lockEx.ResponsePayload["retry_after_seconds"].Should().Be(60);

        // 4. Even a VALID PIN on a fresh OperatorPinService(db) instance is blocked with 429 while locked out
        now = now.AddSeconds(30);
        var blockedValidAct = async () => await MakeSvc().ResolveOperatorAsync("6391", userId, "sale_invoice");
        var blockedEx = (await blockedValidAct.Should().ThrowAsync<OperatorPinException>()).Which;
        blockedEx.StatusCode.Should().Be(429);
        Convert.ToInt32(blockedEx.ResponsePayload["retry_after_seconds"]).Should().Be(30);

        // 5. After 60s lockout expires (+31s), valid PIN succeeds and resets bucket
        now = now.AddSeconds(31);
        var ok = await MakeSvc().ResolveOperatorAsync("6391", userId, "sale_invoice");
        ok["verified_via_pin"].Should().Be(true);
        ok["user_id"].Should().Be(userId);
    }

    // =========================================================================
    // ISS-08: Uploads, Receipt XSS & Deletion Containment
    // =========================================================================

    [Fact]
    public async Task ISS08_SafeFileStorageHelper_BlocksTraversalAndSymlinks()
    {
        await using var env = await IsolatedEnv.CreateAsync();

        var outsideFile = Path.Combine(env.TempRoot, "outside-secret.txt");
        await File.WriteAllTextAsync(outsideFile, "TOP_SECRET");

        var receiptsDir = env.Db.StoragePath("app/public/expense-receipts");
        Directory.CreateDirectory(receiptsDir);
        var validReceipt = Path.Combine(receiptsDir, "legit.png");
        await File.WriteAllBytesAsync(validReceipt, ValidPngBytes);

        // Traversal, drive/backslash, URL, null-byte, and prefix mismatch must all fail
        SafeFileStorageHelper.TryResolveContainedStoragePath(env.Db, "expense-receipts/../../outside-secret.txt", "expense-receipts", out _).Should().BeFalse();
        SafeFileStorageHelper.TryResolveContainedStoragePath(env.Db, "..\\outside-secret.txt", "expense-receipts", out _).Should().BeFalse();
        SafeFileStorageHelper.TryResolveContainedStoragePath(env.Db, "expense-receipts/legit.png\0.jpg", "expense-receipts", out _).Should().BeFalse();
        SafeFileStorageHelper.TryResolveContainedStoragePath(env.Db, "https://evil.com/x.png", "expense-receipts", out _).Should().BeFalse();
        SafeFileStorageHelper.TryResolveContainedStoragePath(env.Db, "products/legit.png", "expense-receipts", out _).Should().BeFalse();
        SafeFileStorageHelper.TryResolveContainedStoragePath(env.Db, "/etc/passwd", "expense-receipts", out _).Should().BeFalse();

        // Traversal deletion must NEVER delete outsideFile
        SafeFileStorageHelper.TryDeleteContainedStorageFile(env.Db, "expense-receipts/../../outside-secret.txt", "expense-receipts").Should().BeFalse();
        File.Exists(outsideFile).Should().BeTrue();

        // Symlink inside contained folder pointing outside must be rejected
        var symlinkPath = Path.Combine(receiptsDir, "sym.png");
        try
        {
            File.CreateSymbolicLink(symlinkPath, outsideFile);
            SafeFileStorageHelper.TryResolveContainedStoragePath(env.Db, "expense-receipts/sym.png", "expense-receipts", out _).Should().BeFalse();
            SafeFileStorageHelper.TryDeleteContainedStorageFile(env.Db, "expense-receipts/sym.png", "expense-receipts").Should().BeFalse();
            File.Exists(outsideFile).Should().BeTrue();
        }
        catch (UnauthorizedAccessException)
        {
            // Some restricted filesystems disallow symlink creation; path containment tests above still apply.
        }

        // Valid contained file resolves and deletes cleanly
        SafeFileStorageHelper.TryResolveContainedStoragePath(env.Db, "expense-receipts/legit.png", "expense-receipts", out var resolved).Should().BeTrue();
        Path.GetFullPath(resolved).Should().Be(Path.GetFullPath(validReceipt));
        SafeFileStorageHelper.TryDeleteContainedStorageFile(env.Db, "expense-receipts/legit.png", "expense-receipts").Should().BeTrue();
        File.Exists(validReceipt).Should().BeFalse();
    }

    [Fact]
    public async Task ISS08_ExpenseController_ValidatesUploadsNeutralizesReceiptXssAndContainsDelete()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@exp.local", "Admin");
        var expCatId = await env.Db.InsertAsync("expense_categories", new Dictionary<string, object?>
        {
            ["name"] = "Utilities",
            ["is_active"] = 1
        });

        ExpenseController MakeCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        var baseFields = new Dictionary<string, string>
        {
            ["expense_category_id"] = expCatId.ToString(),
            ["amount"] = "150.00",
            ["date"] = "2026-11-23",
            ["payment_method"] = "cash",
            ["payee"] = "Power Co"
        };

        // 1. Reject .html, .svg, .js, spoofed .png with HTML bytes, and >5MB files with 422
        foreach (var (badName, badMime, badBytes) in new (string, string, byte[])[]
        {
            ("xss.html", "text/html", Encoding.UTF8.GetBytes("<script>alert(1)</script>")),
            ("xss.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<svg onload=alert(1)/>")),
            ("evil.js", "application/javascript", Encoding.UTF8.GetBytes("alert(1);")),
            ("spoof.png", "image/png", Encoding.UTF8.GetBytes("<html><body>not a png</body></html>")),
            ("toobig.png", "image/png", ValidPngBytes.Concat(new byte[5 * 1024 * 1024 + 10]).ToArray())
        })
        {
            var badCtx = MakeMultipartHttpContext(adminId, baseFields, "receipt", badName, badMime, badBytes);
            var badRes = await MakeCtrl(badCtx).Store();
            GetStatus(badRes).Should().Be(422, $"file {badName} must be rejected");
        }
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM expenses")).Should().Be(0);

        // 2. Accept valid PDF and valid PNG
        var pdfCtx = MakeMultipartHttpContext(adminId, baseFields, "receipt", "bill.pdf", "application/pdf", ValidPdfBytes);
        var pdfStoreRes = await MakeCtrl(pdfCtx).Store();
        GetStatus(pdfStoreRes).Should().Be(201);

        long expenseId;
        string firstReceiptPath;
        using (var doc = ParseBody(pdfStoreRes))
        {
            var expEl = doc.RootElement.GetProperty("expense");
            expenseId = expEl.GetProperty("id").GetInt64();
            firstReceiptPath = expEl.GetProperty("receipt_path").GetString()!;
        }
        firstReceiptPath.Should().StartWith("expense-receipts/").And.EndWith(".pdf");
        var firstFullPath = env.Db.StoragePath($"app/public/{firstReceiptPath}");
        File.Exists(firstFullPath).Should().BeTrue();

        // 3. Receipt serves valid PDF as attachment with nosniff + sandbox CSP
        var receiptCtx = MakeJsonHttpContext(adminId);
        var receiptRes = await MakeCtrl(receiptCtx).Receipt(expenseId);
        var phys = receiptRes.Should().BeOfType<PhysicalFileResult>().Subject;
        phys.ContentType.Should().Be("application/pdf");
        phys.FileDownloadName.Should().EndWith(".pdf");
        receiptCtx.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        receiptCtx.Response.Headers["Content-Security-Policy"].ToString().Should().Be("sandbox; default-src 'none';");

        // 4. Replacing receipt on Update deletes old file safely and stores new PNG
        var pngCtx = MakeMultipartHttpContext(adminId, baseFields, "receipt", "bill.png", "image/png", ValidPngBytes);
        var updateRes = await MakeCtrl(pngCtx).Update(expenseId);
        GetStatus(updateRes).Should().Be(200);
        File.Exists(firstFullPath).Should().BeFalse();

        // 5. Legacy unsafe HTML file in DB is refused with 403 on Receipt; traversal is refused with 404 and untouched on Update
        var legacyHtmlRel = "expense-receipts/legacy-xss.html";
        await File.WriteAllTextAsync(env.Db.StoragePath($"app/public/{legacyHtmlRel}"), "<script>alert(1)</script>");
        await env.Db.ExecuteAsync("UPDATE expenses SET receipt_path = @p WHERE id = @id", new { p = legacyHtmlRel, id = expenseId });
        GetStatus(await MakeCtrl(MakeJsonHttpContext(adminId)).Receipt(expenseId)).Should().Be(403);

        var outsideTarget = Path.Combine(env.TempRoot, "do-not-delete.txt");
        await File.WriteAllTextAsync(outsideTarget, "KEEP_ME");
        await env.Db.ExecuteAsync("UPDATE expenses SET receipt_path = 'expense-receipts/../../do-not-delete.txt' WHERE id = @id", new { id = expenseId });
        GetStatus(await MakeCtrl(MakeJsonHttpContext(adminId)).Receipt(expenseId)).Should().Be(404);

        var removeCtx = MakeJsonHttpContext(adminId, new { remove_receipt = true });
        GetStatus(await MakeCtrl(removeCtx).Update(expenseId)).Should().Be(200);
        File.Exists(outsideTarget).Should().BeTrue();
    }

    [Fact]
    public async Task ISS08_ProductAndBrandControllers_ValidateUploadsAndContainDeletions()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@prod.local", "Admin");
        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "Cat1" });
        var suppId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Supp1" });

        BrandController MakeBrandCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
        ProductController MakeProdCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        // 1. BrandController rejects PDF, SVG, HTML, spoofed PNG
        foreach (var (badName, badMime, badBytes) in new (string, string, byte[])[]
        {
            ("brand.pdf", "application/pdf", ValidPdfBytes),
            ("brand.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<svg/>")),
            ("brand.html", "text/html", Encoding.UTF8.GetBytes("<html/>")),
            ("spoof.png", "image/png", Encoding.UTF8.GetBytes("<script>alert(1)</script>"))
        })
        {
            var badCtx = MakeMultipartHttpContext(adminId, new() { ["name"] = "BadBrand" }, "image", badName, badMime, badBytes);
            GetStatus(await MakeBrandCtrl(badCtx).Store()).Should().Be(422);
        }
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM brands")).Should().Be(0);

        // 2. BrandController accepts valid WebP, replaces with JPEG (deleting WebP), and deletes JPEG on Destroy
        var brandWebpCtx = MakeMultipartHttpContext(adminId, new() { ["name"] = "GoodBrand" }, "image", "logo.webp", "image/webp", ValidWebpBytes);
        var brandStoreRes = await MakeBrandCtrl(brandWebpCtx).Store();
        GetStatus(brandStoreRes).Should().Be(201);
        long brandId;
        string brandWebpRel;
        using (var doc = ParseBody(brandStoreRes))
        {
            brandId = doc.RootElement.GetProperty("id").GetInt64();
            brandWebpRel = doc.RootElement.GetProperty("image").GetString()!;
        }
        var brandWebpFull = env.Db.StoragePath($"app/public/{brandWebpRel}");
        File.Exists(brandWebpFull).Should().BeTrue();

        var brandJpgCtx = MakeMultipartHttpContext(adminId, new() { ["name"] = "GoodBrand" }, "image", "logo.jpg", "image/jpeg", ValidJpegBytes);
        var brandUpdateRes = await MakeBrandCtrl(brandJpgCtx).Update(brandId);
        GetStatus(brandUpdateRes).Should().Be(200);
        File.Exists(brandWebpFull).Should().BeFalse();

        string brandJpgRel;
        using (var doc = ParseBody(brandUpdateRes))
        {
            brandJpgRel = doc.RootElement.GetProperty("image").GetString()!;
        }
        var brandJpgFull = env.Db.StoragePath($"app/public/{brandJpgRel}");
        File.Exists(brandJpgFull).Should().BeTrue();

        // Create second brand for Product tests, then delete GoodBrand
        var brand2Id = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "ProdBrand" });
        GetStatus(await MakeBrandCtrl(MakeJsonHttpContext(adminId)).Destroy(brandId)).Should().Be(204);
        File.Exists(brandJpgFull).Should().BeFalse();

        // 3. ProductController rejects PDF/SVG/spoofed image with 422 without inserting row
        var prodFields = new Dictionary<string, string>
        {
            ["product_code"] = "PRD-M2-1",
            ["name"] = "Valid Med",
            ["barcode"] = "1234567890123",
            ["pack_size"] = "10",
            ["narcotic"] = "no",
            ["category_id"] = catId.ToString(),
            ["brand_id"] = brand2Id.ToString(),
            ["supplier_id"] = suppId.ToString()
        };
        GetStatus(await MakeProdCtrl(MakeMultipartHttpContext(adminId, prodFields, "image", "x.pdf", "application/pdf", ValidPdfBytes)).Store())
            .Should().Be(422);
        GetStatus(await MakeProdCtrl(MakeMultipartHttpContext(adminId, prodFields, "image", "x.png", "image/png", Encoding.UTF8.GetBytes("<html>x</html>"))).Store())
            .Should().Be(422);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM products")).Should().Be(0);

        // 4. ProductController accepts PNG, replaces with WebP (deleting PNG), and cleans up WebP on Destroy
        var prodStoreRes = await MakeProdCtrl(MakeMultipartHttpContext(adminId, prodFields, "image", "med.png", "image/png", ValidPngBytes)).Store();
        GetStatus(prodStoreRes).Should().Be(201);
        long prodId;
        string prodPngRel;
        using (var doc = ParseBody(prodStoreRes))
        {
            prodId = doc.RootElement.GetProperty("id").GetInt64();
            prodPngRel = doc.RootElement.GetProperty("image").GetString()!;
        }
        var prodPngFull = env.Db.StoragePath($"app/public/{prodPngRel}");
        File.Exists(prodPngFull).Should().BeTrue();

        var prodUpdRes = await MakeProdCtrl(MakeMultipartHttpContext(adminId, prodFields, "image", "med.webp", "image/webp", ValidWebpBytes)).Update(prodId);
        GetStatus(prodUpdRes).Should().Be(200);
        File.Exists(prodPngFull).Should().BeFalse();
        string prodWebpRel;
        using (var doc = ParseBody(prodUpdRes))
        {
            prodWebpRel = doc.RootElement.GetProperty("image").GetString()!;
        }
        var prodWebpFull = env.Db.StoragePath($"app/public/{prodWebpRel}");
        File.Exists(prodWebpFull).Should().BeTrue();

        GetStatus(await MakeProdCtrl(MakeJsonHttpContext(adminId)).Destroy(prodId)).Should().Be(200);
        File.Exists(prodWebpFull).Should().BeFalse();

        // 5. BulkDestroy also deletes contained image and preserves outside files on traversal
        var outsideBulk = Path.Combine(env.TempRoot, "bulk-keep.txt");
        await File.WriteAllTextAsync(outsideBulk, "KEEP");
        var p2Fields = new Dictionary<string, string>(prodFields)
        {
            ["product_code"] = "PRD-M2-2",
            ["name"] = "Bulk Med 2",
            ["barcode"] = "1234567890124"
        };
        var p2Res = await MakeProdCtrl(MakeMultipartHttpContext(adminId, p2Fields, "image", "p2.png", "image/png", ValidPngBytes)).Store();
        GetStatus(p2Res).Should().Be(201);
        long p2Id;
        string p2ImgRel;
        using (var doc = ParseBody(p2Res))
        {
            p2Id = doc.RootElement.GetProperty("id").GetInt64();
            p2ImgRel = doc.RootElement.GetProperty("image").GetString()!;
        }
        var p2ImgFull = env.Db.StoragePath($"app/public/{p2ImgRel}");
        File.Exists(p2ImgFull).Should().BeTrue();

        var p3Id = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "PRD-M2-3",
            ["name"] = "Bulk Med 3",
            ["barcode"] = "1234567890125",
            ["pack_size"] = 10,
            ["narcotic"] = "no",
            ["category_id"] = catId,
            ["brand_id"] = brand2Id,
            ["supplier_id"] = suppId,
            ["image"] = "products/../../bulk-keep.txt"
        });

        var bulkRes = await MakeProdCtrl(MakeJsonHttpContext(adminId, new { product_ids = new[] { p2Id, p3Id } })).BulkDestroy();
        GetStatus(bulkRes).Should().Be(200);
        File.Exists(p2ImgFull).Should().BeFalse();
        File.Exists(outsideBulk).Should().BeTrue();
    }

    // =========================================================================
    // ISS-10: SettingController Mass Assignment & FBR URL Parity
    // =========================================================================

    [Fact]
    public async Task ISS10_SettingController_BlocksProtectedKeysIgnoresUnknownAndEnforcesFbrUrlAllowlist()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@set.local", "Admin");

        // Seed initial protected settings values
        var licenseSvc = new LicenseService(env.Db);
        var backupSvc = new BackupService(env.Db, new SqliteExportService(env.Db));
        SettingController MakeCtrl(object? payload = null) => new(env.Db, licenseSvc)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId, payload) }
        };
        GetStatus(await MakeCtrl().Show()).Should().Be(200);

        await env.Db.UpdateAsync("settings", 1, new Dictionary<string, object?>
        {
            ["network_server_token"] = "ORIG-SECRET-TOKEN",
            ["terminal_auto_authorize"] = 0,
            ["legal_accepted_version"] = "1.0.0",
            ["smtp_password"] = "keep-smtp-secret",
            ["fbr_bearer_token"] = "keep-fbr-secret"
        });

        // 1. Protected keys rejected with 422 + errors dict in both Update and CompleteSetup
        foreach (var protectedKey in new[]
        {
            "network_server_token",
            "terminal_auto_authorize",
            "terms_accepted_at",
            "privacy_accepted_at",
            "legal_accepted_version",
            "setup_completed_at",
            "logo_path",
            "created_at",
            "updated_at"
        })
        {
            var updPayload = new Dictionary<string, object?>
            {
                ["store_name"] = "Hijacked Store",
                [protectedKey] = protectedKey == "terminal_auto_authorize" ? 1 : "tampered"
            };
            var updRes = await MakeCtrl(updPayload).Update();
            GetStatus(updRes).Should().Be(422, $"Update must reject {protectedKey}");
            using (var doc = ParseBody(updRes))
            {
                doc.RootElement.TryGetProperty("errors", out var errs).Should().BeTrue();
                errs.TryGetProperty(protectedKey, out _).Should().BeTrue();
            }

            var setupRes = await MakeCtrl(updPayload).CompleteSetup(backupSvc);
            GetStatus(setupRes).Should().Be(422, $"CompleteSetup must reject {protectedKey}");
        }

        // Verify original protected values remained intact
        var rowAfterProtected = await env.Db.QuerySingleOrDefaultAsync("SELECT * FROM settings WHERE id = 1");
        rowAfterProtected!["network_server_token"]!.ToString().Should().Be("ORIG-SECRET-TOKEN");
        Convert.ToInt32(rowAfterProtected["terminal_auto_authorize"]).Should().Be(0);
        rowAfterProtected["legal_accepted_version"]!.ToString().Should().Be("1.0.0");

        // 2. SSRF / non-official FBR URLs rejected with 422 + errors in both Update and CompleteSetup
        foreach (var (mode, badUrl) in new[]
        {
            ("digital_invoicing", "http://169.254.169.254/latest/meta-data/"),
            ("digital_invoicing", "https://evil.example.com/steal"),
            ("digital_invoicing", "https://gw.fbr.gov.pk:9999/di_data/v1/di/postinvoicedata"),
            ("tier1_pos_ims", "http://192.168.1.50:8524/api/IMSFiscal/GetInvoiceNumberByModel")
        })
        {
            var badFbrRes = await MakeCtrl(new
            {
                store_name = "My Pharmacy",
                fbr_mode = mode,
                fbr_sandbox_url = badUrl
            }).Update();
            GetStatus(badFbrRes).Should().Be(422);
            using (var doc = ParseBody(badFbrRes))
            {
                doc.RootElement.GetProperty("errors").TryGetProperty("fbr_sandbox_url", out _).Should().BeTrue();
            }

            var badSetupFbr = await MakeCtrl(new
            {
                store_name = "My Pharmacy",
                fbr_mode = mode,
                fbr_production_url = badUrl
            }).CompleteSetup(backupSvc);
            GetStatus(badSetupFbr).Should().Be(422);
            using (var doc = ParseBody(badSetupFbr))
            {
                doc.RootElement.GetProperty("errors").TryGetProperty("fbr_production_url", out _).Should().BeTrue();
            }
        }

        // 3. Legitimate draft update: preserves secrets on blank string, allows blank FBR URL, allows valid FBR URL, ignores unknown keys
        var okDraft = await MakeCtrl(new
        {
            store_name = "Draft Pharmacy",
            is_setup_completed = false,
            smtp_password = "",
            currency_decimal_places = "",
            fbr_mode = "digital_invoicing",
            fbr_sandbox_url = "https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata_sb",
            fbr_production_url = "",
            clear_fbr_bearer_token = true,
            unknown_mass_assign_probe = "ignored",
            backup_is_automated = false
        }).Update();
        GetStatus(okDraft).Should().Be(200);

        var rowAfterDraft = await env.Db.QuerySingleOrDefaultAsync("SELECT * FROM settings WHERE id = 1");
        rowAfterDraft!["store_name"]!.ToString().Should().Be("Draft Pharmacy");
        Convert.ToInt32(rowAfterDraft["is_setup_completed"]).Should().Be(0);
        rowAfterDraft["smtp_password"]!.ToString().Should().Be("keep-smtp-secret");
        rowAfterDraft["fbr_bearer_token"].Should().BeNull();
        rowAfterDraft["fbr_sandbox_url"]!.ToString().Should().Be("https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata_sb");

        // 4. Legitimate CompleteSetup with tier1_pos_ims loopback URL succeeds (clearing prior DI sandbox URL)
        var okComplete = await MakeCtrl(new
        {
            store_name = "Final Pharmacy",
            fbr_mode = "tier1_pos_ims",
            fbr_sandbox_url = "",
            fbr_production_url = "http://127.0.0.1:8524/api/IMSFiscal/GetInvoiceNumberByModel",
            seed_default_accounts = true,
            seed_default_locations = true
        }).CompleteSetup(backupSvc);
        GetStatus(okComplete).Should().Be(200);

        var rowAfterSetup = await env.Db.QuerySingleOrDefaultAsync("SELECT * FROM settings WHERE id = 1");
        rowAfterSetup!["store_name"]!.ToString().Should().Be("Final Pharmacy");
        Convert.ToInt32(rowAfterSetup["is_setup_completed"]).Should().Be(1);
        rowAfterSetup["fbr_production_url"]!.ToString().Should().Be("http://127.0.0.1:8524/api/IMSFiscal/GetInvoiceNumberByModel");
    }

    // =========================================================================
    // Iteration 2 Remediation Regression Tests (Challenges 1-6 & Reviewer F1/F3)
    // =========================================================================

    [Fact]
    public async Task ISS01_ISS02_Iter2_OperatorPinAssign_BlocksAdminTamper_EnforcesAssignRoles_PreservesCase_And_ClearPinPrecedence()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        OperatorPinService.ResetRateLimitState();

        var adminId = await env.SeedUserAsync("Admin", "admin@iter2.local", "Admin", rawOrHashedPin: OperatorPinService.HashPin("9182"));
        var adminEmpId = await env.Db.InsertAsync("employees", new Dictionary<string, object?>
        {
            ["employee_code"] = "EMP-ADM",
            ["user_id"] = adminId,
            ["name"] = "Admin Emp",
            ["status"] = "active",
            ["pin_code"] = OperatorPinService.HashPin("9182")
        });

        await env.EnsurePermissionAsync("settings.update");
        await env.EnsurePermissionAsync("sale-invoice.view");
        await env.EnsurePermissionAsync("user.view");
        await env.EnsurePermissionAsync("user.create");
        await env.EnsurePermissionAsync("user.update");
        await env.EnsurePermissionAsync("user.assign.roles");
        await env.EnsurePermissionAsync("hr.manage");

        var branchAdminRoleId = await env.EnsureRoleAsync("BranchAdmin");
        var settingsUpdatePermId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM permissions WHERE name = 'settings.update'");
        await env.Db.InsertAsync("role_has_permissions", new Dictionary<string, object?>
        {
            ["role_id"] = branchAdminRoleId,
            ["permission_id"] = settingsUpdatePermId
        });

        var cashierRoleId = await env.EnsureRoleAsync("Cashier");
        var saleViewPermId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM permissions WHERE name = 'sale-invoice.view'");
        await env.Db.InsertAsync("role_has_permissions", new Dictionary<string, object?>
        {
            ["role_id"] = cashierRoleId,
            ["permission_id"] = saleViewPermId
        });

        // Non-admin with user.update + hr.manage (NO user.assign.roles)
        var mgrId = await env.SeedUserAsync(
            "UserMgr",
            "mgr@iter2.local",
            "UserManager",
            directPermissions: ["user.view", "user.create", "user.update", "hr.manage"]);

        // Non-admin with user.assign.roles + sale-invoice.view (does NOT hold settings.update)
        var assignerId = await env.SeedUserAsync(
            "RoleAssigner",
            "assigner@iter2.local",
            "UserManager",
            directPermissions: ["user.view", "user.create", "user.update", "user.assign.roles", "sale-invoice.view"]);

        var staffId = await env.SeedUserAsync("Staff", "staff@iter2.local", "Cashier", rawOrHashedPin: OperatorPinService.HashPin("4567"));

        var pinService = new OperatorPinService(env.Db);
        UserController MakeUserCtrl(long uid, object? payload = null) => new(env.Db, pinService)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid, payload) }
        };
        OperatorPinController MakePinCtrl(long uid, object? payload = null) => new(env.Db, pinService)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid, payload) }
        };

        // 1. Challenge 1: Non-admin cannot assign OR clear Admin user PIN or Admin-linked employee PIN via OperatorPinController.Assign
        GetStatus(await MakePinCtrl(mgrId, new { type = "user", id = adminId, pin_code = "7777" }).Assign()).Should().Be(403);
        GetStatus(await MakePinCtrl(mgrId, new { type = "user", id = adminId, pin_code = (string?)null }).Assign()).Should().Be(403);
        GetStatus(await MakePinCtrl(mgrId, new { type = "employee", id = adminEmpId, pin_code = "7777" }).Assign()).Should().Be(403);
        GetStatus(await MakePinCtrl(mgrId, new { type = "employee", id = adminEmpId, pin_code = (string?)null }).Assign()).Should().Be(403);

        var adminStoredPin = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @id", new { id = adminId });
        OperatorPinService.VerifyPin("9182", adminStoredPin).Should().BeTrue();
        var adminEmpPin = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM employees WHERE id = @id", new { id = adminEmpId });
        OperatorPinService.VerifyPin("9182", adminEmpPin).Should().BeTrue();

        // Admin CAN assign Admin-linked PIN and non-admin CAN assign non-admin PIN
        GetStatus(await MakePinCtrl(adminId, new { type = "user", id = adminId, pin_code = "7777" }).Assign()).Should().Be(200);
        GetStatus(await MakePinCtrl(mgrId, new { type = "user", id = staffId, pin_code = "3456" }).Assign()).Should().Be(200);

        // 2. Challenge 2: UserController.Store/Update/SyncRoles/BulkAction enforce user.assign.roles, block self-role modification, and block unheld role permissions
        var storeNoAssignPerm = await MakeUserCtrl(mgrId, new
        {
            name = "BlockedRoleUser",
            email = "blockedrole@iter2.local",
            password = "Password123!",
            roles = new[] { "Cashier" }
        }).Store();
        GetStatus(storeNoAssignPerm).Should().Be(403);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM users WHERE email = 'blockedrole@iter2.local'")).Should().Be(0);

        // Self-role escalation via Update without user.assign.roles -> 403
        GetStatus(await MakeUserCtrl(mgrId, new
        {
            name = "UserMgr",
            email = "mgr@iter2.local",
            roles = new[] { "BranchAdmin" }
        }).Update(mgrId)).Should().Be(403);
        (await env.Db.UserCanAsync(mgrId, "settings.update")).Should().BeFalse();

        // Update another user changing roles without user.assign.roles -> 403
        GetStatus(await MakeUserCtrl(mgrId, new
        {
            name = "Staff",
            email = "staff@iter2.local",
            roles = new[] { "BranchAdmin" }
        }).Update(staffId)).Should().Be(403);

        // Benign UserForm.jsx save echoing unchanged roles without user.assign.roles -> 200
        GetStatus(await MakeUserCtrl(mgrId, new
        {
            name = "Staff Renamed",
            email = "staff@iter2.local",
            roles = new[] { "Cashier" }
        }).Update(staffId)).Should().Be(200);

        // SyncRoles & BulkAction(assign_role) without user.assign.roles -> 403
        GetStatus(await MakeUserCtrl(mgrId, new { roles = new[] { "BranchAdmin" } }).SyncRoles(mgrId)).Should().Be(403);
        GetStatus(await MakeUserCtrl(mgrId, new { action = "assign_role", role = "Cashier", user_ids = new[] { staffId } }).BulkAction()).Should().Be(403);

        // Non-admin WITH user.assign.roles cannot modify OWN roles (403) and cannot assign role with unheld permissions (403)
        GetStatus(await MakeUserCtrl(assignerId, new
        {
            name = "RoleAssigner",
            email = "assigner@iter2.local",
            roles = new[] { "Cashier" }
        }).Update(assignerId)).Should().Be(403);

        GetStatus(await MakeUserCtrl(assignerId, new
        {
            name = "EscUser",
            email = "escuser@iter2.local",
            password = "Password123!",
            roles = new[] { "BranchAdmin" }
        }).Store()).Should().Be(403);

        GetStatus(await MakeUserCtrl(assignerId, new
        {
            name = "Staff Renamed",
            email = "staff@iter2.local",
            roles = new[] { "BranchAdmin" }
        }).Update(staffId)).Should().Be(403);

        // Non-admin WITH user.assign.roles CAN create user with role whose permissions caller holds
        GetStatus(await MakeUserCtrl(assignerId, new
        {
            name = "ValidCashier",
            email = "validcashier@iter2.local",
            password = "Password123!",
            roles = new[] { "Cashier" }
        }).Store()).Should().Be(201);

        // 3. Challenge 4: BulkAction(assign_role) with differing case ("cashier" vs DB "Cashier") assigns canonical role without wiping
        var bulkCaseRes = await MakeUserCtrl(assignerId, new
        {
            action = "assign_role",
            role = "cashier",
            user_ids = new[] { staffId }
        }).BulkAction();
        GetStatus(bulkCaseRes).Should().Be(200);
        using (var doc = ParseBody(bulkCaseRes))
        {
            doc.RootElement.GetProperty("processed").GetInt32().Should().Be(1);
        }
        (await env.Db.GetUserRoleNamesAsync(staffId)).Should().Contain("Cashier");

        // 4. Reviewer Minor Finding 3: clear_pin: true takes precedence over simultaneous non-empty/invalid pin_code
        var clearPrecedenceRes = await MakeUserCtrl(adminId, new
        {
            name = "Staff Renamed",
            email = "staff@iter2.local",
            clear_pin = true,
            pin_code = "8912"
        }).Update(staffId);
        GetStatus(clearPrecedenceRes).Should().Be(200);
        using (var doc = ParseBody(clearPrecedenceRes))
        {
            doc.RootElement.GetProperty("has_pin").GetBoolean().Should().BeFalse();
        }
        (await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @id", new { id = staffId })).Should().BeNull();
    }

    [Fact]
    public async Task ISS02_Iter2_ResolveOperatorAsync_PreservesMismatchedEmployeePin_And_BoundsConcurrentBurst()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        OperatorPinService.ResetRateLimitState();

        // 1. Challenge 5a: User (legacy "1111") + linked employee (legacy "2222") -> logging in as "1111" upgrades ONLY user, preserving "2222"
        var userId = await env.SeedUserAsync("LinkedUser", "linked@iter2.local", "Cashier", rawOrHashedPin: "1111");
        var empId = await env.Db.InsertAsync("employees", new Dictionary<string, object?>
        {
            ["employee_code"] = "EMP-MISMATCH",
            ["user_id"] = userId,
            ["name"] = "Linked Emp",
            ["status"] = "active",
            ["pin_code"] = "2222"
        });

        await env.Db.InsertAsync("settings", new Dictionary<string, object?>
        {
            ["store_name"] = "Pin Burst Store",
            ["require_operator_pin"] = 1,
            ["operator_pin_modules"] = "[\"sale_invoice\"]"
        });

        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        OperatorPinService MakeSvc() => new(env.Db, () => now);

        var resolved = await MakeSvc().ResolveOperatorAsync("1111", userId, "sale_invoice");
        resolved["verified_via_pin"].Should().Be(true);

        var upgradedUserPin = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @id", new { id = userId });
        OperatorPinService.IsHashedPin(upgradedUserPin).Should().BeTrue();
        OperatorPinService.VerifyPin("1111", upgradedUserPin).Should().BeTrue();

        var preservedEmpPin = await env.Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM employees WHERE id = @id", new { id = empId });
        OperatorPinService.VerifyPin("2222", preservedEmpPin).Should().BeTrue();
        OperatorPinService.VerifyPin("1111", preservedEmpPin).Should().BeFalse();

        // 2. Challenge 5b: Concurrent burst attack (12 simultaneous bad guesses + 1 valid guess) strictly bounds pre-lockout guesses to 4 x 422
        for (int i = 0; i < 12; i++)
        {
            await env.SeedUserAsync($"Dummy{i}", $"dummy{i}@iter2.local", "Cashier", rawOrHashedPin: OperatorPinService.HashPin((3000 + i).ToString("D4")));
        }

        OperatorPinService.ResetRateLimitState();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task<int>>();

        for (int i = 0; i < 13; i++)
        {
            var guess = i == 12 ? "1111" : "9999";
            tasks.Add(Task.Run(async () =>
            {
                await startGate.Task;
                try
                {
                    await MakeSvc().ResolveOperatorAsync(guess, userId, "sale_invoice", scopeKey: "burst-scope");
                    return 200;
                }
                catch (OperatorPinException ex)
                {
                    return ex.StatusCode;
                }
            }));
        }

        startGate.SetResult();
        var statuses = await Task.WhenAll(tasks);

        // Mathematical bound under bucket.Gate serialization: if the single valid guess enters before lockout
        // (after 0..4 bad guesses), ClearAttemptBucket allows <= 4 pre-clear + <= 4 post-clear = <= 8 x 422 and >= 4 x 429;
        // if 5 bad guesses enter first, lockout engages and all remaining calls (including valid) receive 429 (4 x 422, 9 x 429).
        var okCount = statuses.Count(s => s == 200);
        var badCount = statuses.Count(s => s == 422);
        var lockedCount = statuses.Count(s => s == 429);
        okCount.Should().BeLessThanOrEqualTo(1);
        (okCount + badCount + lockedCount).Should().Be(13);
        if (okCount == 1)
        {
            badCount.Should().BeLessThanOrEqualTo(8);
            lockedCount.Should().BeGreaterThanOrEqualTo(4);
            (badCount + lockedCount).Should().Be(12);
        }
        else
        {
            badCount.Should().Be(4);
            lockedCount.Should().Be(9);
        }

        // Now run a pure 12-bad-guess burst to deterministically verify exact 4 x 422 and 8 x 429
        OperatorPinService.ResetRateLimitState();
        var badGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var badTasks = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            await badGate.Task;
            try
            {
                await MakeSvc().ResolveOperatorAsync("9999", userId, "sale_invoice", scopeKey: "pure-bad-burst");
                return 200;
            }
            catch (OperatorPinException ex)
            {
                return ex.StatusCode;
            }
        })).ToList();

        badGate.SetResult();
        var badStatuses = await Task.WhenAll(badTasks);
        badStatuses.Count(s => s == 422).Should().Be(4);
        badStatuses.Count(s => s == 429).Should().Be(8);
    }

    [Fact]
    public async Task ISS10_Iter2_SettingController_BlocksTwoStepFbrBypass_And_ValidatesWhatsAppApiUrl()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@set2.local", "Admin");

        var licenseSvc = new LicenseService(env.Db);
        var backupSvc = new BackupService(env.Db, new SqliteExportService(env.Db));
        SettingController MakeCtrl(object? payload = null) => new(env.Db, licenseSvc)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId, payload) }
        };
        GetStatus(await MakeCtrl().Show()).Should().Be(200);

        // 1. Challenge 3: 2-step FBR mode-switch SSRF bypass (tier1_pos_ims loopback URL -> switch to digital_invoicing without sending fbr_production_url) -> 422
        GetStatus(await MakeCtrl(new
        {
            store_name = "FBR Store",
            fbr_mode = "tier1_pos_ims",
            fbr_production_url = "http://127.0.0.1:18960/api/users"
        }).Update()).Should().Be(200);

        var twoStepUpd = await MakeCtrl(new
        {
            fbr_mode = "digital_invoicing",
            fbr_environment = "production"
        }).Update();
        GetStatus(twoStepUpd).Should().Be(422);
        using (var doc = ParseBody(twoStepUpd))
        {
            doc.RootElement.GetProperty("errors").TryGetProperty("fbr_production_url", out _).Should().BeTrue();
        }

        var twoStepSetup = await MakeCtrl(new
        {
            store_name = "FBR Store",
            fbr_mode = "digital_invoicing",
            fbr_environment = "production"
        }).CompleteSetup(backupSvc);
        GetStatus(twoStepSetup).Should().Be(422);
        using (var doc = ParseBody(twoStepSetup))
        {
            doc.RootElement.GetProperty("errors").TryGetProperty("fbr_production_url", out _).Should().BeTrue();
        }

        // Clearing incompatible URL during mode switch succeeds
        GetStatus(await MakeCtrl(new
        {
            fbr_mode = "digital_invoicing",
            fbr_environment = "production",
            fbr_production_url = ""
        }).Update()).Should().Be(200);

        // Reverse 2-step: DI sandbox URL stored -> switch to tier1_pos_ims without clearing fbr_sandbox_url -> 422
        GetStatus(await MakeCtrl(new
        {
            fbr_mode = "digital_invoicing",
            fbr_sandbox_url = "https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata_sb"
        }).Update()).Should().Be(200);

        var revTwoStep = await MakeCtrl(new { fbr_mode = "tier1_pos_ims" }).Update();
        GetStatus(revTwoStep).Should().Be(422);
        using (var doc = ParseBody(revTwoStep))
        {
            doc.RootElement.GetProperty("errors").TryGetProperty("fbr_sandbox_url", out _).Should().BeTrue();
        }

        // 2. Reviewer Major Finding 1 / Challenge 3: whatsapp_api_url SSRF & local_agent key exfiltration validation
        foreach (var (provider, badWaUrl) in new[]
        {
            ("custom_http", "http://169.254.169.254/latest/meta-data/"),
            ("custom_http", "http://[::ffff:169.254.169.254]/latest/meta-data/"),
            ("custom_http", "http://[fe80::1]/"),
            ("custom_http", "http://0.0.0.0:3002"),
            ("custom_http", "http://metadata.google.internal/computeMetadata/v1/"),
            ("custom_http", "ftp://127.0.0.1:3002"),
            ("custom_http", "http://user:pass@127.0.0.1:3002"),
            ("local_agent", "https://evil.example.com/steal-key"),
            ("local_agent", "http://192.168.1.50:3002")
        })
        {
            var badWaRes = await MakeCtrl(new
            {
                store_name = "WA Store",
                whatsapp_provider = provider,
                whatsapp_api_url = badWaUrl
            }).Update();
            GetStatus(badWaRes).Should().Be(422, $"Update must reject whatsapp_api_url={badWaUrl} under {provider}");
            using (var doc = ParseBody(badWaRes))
            {
                doc.RootElement.GetProperty("errors").TryGetProperty("whatsapp_api_url", out _).Should().BeTrue();
            }

            var badWaSetup = await MakeCtrl(new
            {
                store_name = "WA Store",
                whatsapp_provider = provider,
                whatsapp_api_url = badWaUrl
            }).CompleteSetup(backupSvc);
            GetStatus(badWaSetup).Should().Be(422, $"CompleteSetup must reject whatsapp_api_url={badWaUrl} under {provider}");
        }

        // Valid custom_http URL succeeds, then switching whatsapp_provider back to local_agent without updating whatsapp_api_url -> 422
        GetStatus(await MakeCtrl(new
        {
            whatsapp_provider = "custom_http",
            whatsapp_api_url = "https://wa.example.com/api"
        }).Update()).Should().Be(200);

        var switchToLocalAgent = await MakeCtrl(new { whatsapp_provider = "local_agent" }).Update();
        GetStatus(switchToLocalAgent).Should().Be(422);
        using (var doc = ParseBody(switchToLocalAgent))
        {
            doc.RootElement.GetProperty("errors").TryGetProperty("whatsapp_api_url", out _).Should().BeTrue();
        }

        // Valid local_agent loopback URL succeeds
        GetStatus(await MakeCtrl(new
        {
            whatsapp_provider = "local_agent",
            whatsapp_api_url = "http://127.0.0.1:3002"
        }).Update()).Should().Be(200);
    }

    [Fact]
    public async Task ISS08_Iter2_ZeroByteUploads_Return422AcrossAllUploadEndpoints()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@zero.local", "Admin");

        var expCatId = await env.Db.InsertAsync("expense_categories", new Dictionary<string, object?> { ["name"] = "Ops", ["is_active"] = 1 });
        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "Cat0" });
        var suppId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Supp0" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "SeedBrand" });
        var prodId = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "PRD-ZERO-1",
            ["name"] = "Seed Med",
            ["barcode"] = "9999999999991",
            ["pack_size"] = 10,
            ["narcotic"] = "no",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = suppId
        });

        var expFields = new Dictionary<string, string>
        {
            ["expense_category_id"] = expCatId.ToString(),
            ["amount"] = "50.00",
            ["date"] = "2026-11-23",
            ["payment_method"] = "cash"
        };

        ExpenseController MakeExpCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
        ProductController MakeProdCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
        BrandController MakeBrandCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
        var licenseSvc = new LicenseService(env.Db);
        var backupSvc = new BackupService(env.Db, new SqliteExportService(env.Db));
        SettingController MakeSetCtrl(DefaultHttpContext ctx) => new(env.Db, licenseSvc)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        // Create a valid expense first so we can test ExpenseController.Update with 0-byte file
        var validExpRes = await MakeExpCtrl(MakeMultipartHttpContext(adminId, expFields, "receipt", "ok.png", "image/png", ValidPngBytes)).Store();
        GetStatus(validExpRes).Should().Be(201);
        long expId;
        using (var doc = ParseBody(validExpRes))
        {
            expId = doc.RootElement.GetProperty("expense").GetProperty("id").GetInt64();
        }

        // Expense Store & Update 0-byte -> 422
        GetStatus(await MakeExpCtrl(MakeMultipartHttpContext(adminId, expFields, "receipt", "zero.html", "text/html", [])).Store()).Should().Be(422);
        GetStatus(await MakeExpCtrl(MakeMultipartHttpContext(adminId, expFields, "receipt", "zero.png", "image/png", [])).Update(expId)).Should().Be(422);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM expenses")).Should().Be(1);

        // Brand Store & Update 0-byte -> 422
        GetStatus(await MakeBrandCtrl(MakeMultipartHttpContext(adminId, new() { ["name"] = "ZeroBrand" }, "image", "zero.png", "image/png", [])).Store()).Should().Be(422);
        GetStatus(await MakeBrandCtrl(MakeMultipartHttpContext(adminId, new() { ["name"] = "SeedBrand" }, "image", "zero.png", "image/png", [])).Update(brandId)).Should().Be(422);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM brands")).Should().Be(1);

        // Product Store & Update 0-byte -> 422
        var prodFields = new Dictionary<string, string>
        {
            ["product_code"] = "PRD-ZERO-2",
            ["name"] = "Zero Med",
            ["barcode"] = "9999999999992",
            ["pack_size"] = "10",
            ["narcotic"] = "no",
            ["category_id"] = catId.ToString(),
            ["brand_id"] = brandId.ToString(),
            ["supplier_id"] = suppId.ToString()
        };
        GetStatus(await MakeProdCtrl(MakeMultipartHttpContext(adminId, prodFields, "image", "zero.png", "image/png", [])).Store()).Should().Be(422);
        GetStatus(await MakeProdCtrl(MakeMultipartHttpContext(adminId, prodFields, "image", "zero.png", "image/png", [])).Update(prodId)).Should().Be(422);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM products")).Should().Be(1);

        // Setting Update & CompleteSetup 0-byte logo -> 422
        GetStatus(await MakeSetCtrl(MakeMultipartHttpContext(adminId, new() { ["store_name"] = "Zero Store" }, "logo", "zero.png", "image/png", [])).Update()).Should().Be(422);
        GetStatus(await MakeSetCtrl(MakeMultipartHttpContext(adminId, new() { ["store_name"] = "Zero Store" }, "logo", "zero.png", "image/png", [])).CompleteSetup(backupSvc)).Should().Be(422);
    }

    // =========================================================================
    // Iteration 3 Remediation Regression Tests (TestWhatsApp SSRF & Empty/Null Mode/Provider)
    // =========================================================================

    private sealed class CountingHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<Uri?> RequestedUris { get; } = new();

        public CountingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
        {
            _responder = responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"message_id\":\"wa_mock_1\"}", Encoding.UTF8, "application/json")
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri);
            return Task.FromResult(_responder(request));
        }
    }

    [Fact]
    public async Task ISS10_Iter3_TestWhatsApp_BlocksSsrfVectorsWithZeroOutboundRequests()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@wa3.local", "Admin");

        var licenseSvc = new LicenseService(env.Db);
        var pdfGen = new PdfDocumentGenerator();

        SettingController MakeTestWaCtrl(CountingHttpHandler handler, object payload)
        {
            var httpClient = new HttpClient(handler);
            var sidecar = new LocalWhatsAppSidecarManager(env.TempRoot, httpClient);
            var shareEngine = new DocumentShareEngine(env.TempRoot, pdfGen, httpClient);
            return new SettingController(env.Db, licenseSvc, sidecar, shareEngine, pdfGen)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId, payload) }
            };
        }

        // Seed settings row first
        GetStatus(await new SettingController(env.Db, licenseSvc)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
        }.Show()).Should().Be(200);

        // 1. All SSRF & invalid URL vectors must return 422 with errors["whatsapp_api_url"] and dispatch ZERO outbound HTTP requests
        var invalidVectors = new (string? Provider, string BadUrl)[]
        {
            ("custom_http", "http://169.254.169.254/latest/meta-data/"),
            ("custom_http", "http://[::ffff:169.254.169.254]/"),
            ("custom_http", "http://[fe80::1]/"),
            ("custom_http", "http://0.0.0.0:3000"),
            ("custom_http", "http://[::]:3000"),
            ("custom_http", "file:///etc/passwd"),
            ("custom_http", "https://user:pass@example.com"),
            ("custom_http", "http://metadata.google.internal/computeMetadata/v1/"),
            ("waha", "http://169.254.169.254/latest/meta-data/"),
            ("evolution_api", "http://169.254.169.254/latest/meta-data/"),
            ("whapi_cloud", "http://169.254.169.254/latest/meta-data/"),
            ("wasender", "http://169.254.169.254/latest/meta-data/"),
            ("ultramsg", "http://169.254.169.254/latest/meta-data/"),
            ("green_api", "http://169.254.169.254/latest/meta-data/"),
            ("meta_cloud", "http://169.254.169.254/latest/meta-data/"),
            ("local_agent", "https://evil.example.com/steal"),
            ("local_agent", "http://192.168.1.50:3002"),
            ("", "https://evil.example.com/hook"),
            (null, "https://evil.example.com/hook")
        };

        foreach (var (provider, badUrl) in invalidVectors)
        {
            var handler = new CountingHttpHandler();
            var payload = new Dictionary<string, object?>
            {
                ["whatsapp_provider"] = provider,
                ["whatsapp_api_url"] = badUrl,
                ["whatsapp_access_token"] = "SECRET_TOKEN",
                ["test_phone"] = "03001234567"
            };
            var res = await MakeTestWaCtrl(handler, payload).TestWhatsApp();
            GetStatus(res).Should().Be(422, $"TestWhatsApp must reject provider='{provider ?? "<null>"}' url='{badUrl}'");
            handler.RequestedUris.Should().BeEmpty($"0 outbound HTTP requests allowed for provider='{provider ?? "<null>"}' url='{badUrl}'");
            using var doc = ParseBody(res);
            doc.RootElement.GetProperty("errors").TryGetProperty("whatsapp_api_url", out _).Should().BeTrue();
        }

        // 2. Legitimate custom_http & local_agent requests succeed and dispatch expected HTTP calls
        var okCustomHandler = new CountingHttpHandler();
        var okCustomRes = await MakeTestWaCtrl(okCustomHandler, new
        {
            whatsapp_provider = "custom_http",
            whatsapp_api_url = "https://wa.example.com/hook",
            whatsapp_access_token = "SECRET_TOKEN",
            test_phone = "03001234567"
        }).TestWhatsApp();
        GetStatus(okCustomRes).Should().Be(200);
        okCustomHandler.RequestedUris.Should().ContainSingle(u => u != null && u.ToString() == "https://wa.example.com/hook");

        var okLocalHandler = new CountingHttpHandler();
        var okLocalRes = await MakeTestWaCtrl(okLocalHandler, new
        {
            whatsapp_provider = "",
            whatsapp_api_url = "http://127.0.0.1:3002",
            test_phone = "03001234567"
        }).TestWhatsApp();
        GetStatus(okLocalRes).Should().Be(200);
        okLocalHandler.RequestedUris.Should().Contain(u => u != null && u.ToString() == "http://127.0.0.1:3002/send-document");
    }

    [Fact]
    public async Task ISS10_Iter3_SettingController_NormalizesEmptyAndNullFbrModeAndWhatsAppProvider()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@mode3.local", "Admin");

        var licenseSvc = new LicenseService(env.Db);
        var backupSvc = new BackupService(env.Db, new SqliteExportService(env.Db));
        SettingController MakeCtrl(object? payload = null) => new(env.Db, licenseSvc)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId, payload) }
        };
        GetStatus(await MakeCtrl().Show()).Should().Be(200);

        // 1. Store a tier1_pos_ims loopback URL
        GetStatus(await MakeCtrl(new
        {
            store_name = "Mode3 Store",
            fbr_mode = "tier1_pos_ims",
            fbr_environment = "production",
            fbr_production_url = "http://127.0.0.1:18960/api/users"
        }).Update()).Should().Be(200);

        // 2. Sending fbr_mode: "" or fbr_mode: null must normalize to "digital_invoicing" and reject stored loopback URL with 422 (no bypass, no 500 SQLite NOT NULL failure)
        foreach (var badModePayload in new object[]
        {
            new Dictionary<string, object?> { ["fbr_mode"] = "", ["fbr_environment"] = "production" },
            new Dictionary<string, object?> { ["fbr_mode"] = null, ["fbr_environment"] = "production" }
        })
        {
            var res = await MakeCtrl(badModePayload).Update();
            GetStatus(res).Should().Be(422);
            using var doc = ParseBody(res);
            doc.RootElement.GetProperty("errors").TryGetProperty("fbr_production_url", out _).Should().BeTrue();
        }

        // Verify CompleteSetup also rejects fbr_mode: "" / null when tier1_pos_ims loopback URL is stored
        var setupEmptyMode = await MakeCtrl(new Dictionary<string, object?>
        {
            ["store_name"] = "Mode3 Store",
            ["fbr_mode"] = ""
        }).CompleteSetup(backupSvc);
        GetStatus(setupEmptyMode).Should().Be(422);

        // 3. Clearing fbr_production_url while passing fbr_mode: null cleanly normalizes fbr_mode to "digital_invoicing" (200)
        GetStatus(await MakeCtrl(new Dictionary<string, object?>
        {
            ["fbr_mode"] = null,
            ["fbr_production_url"] = ""
        }).Update()).Should().Be(200);
        var rowAfterNullMode = await env.Db.QuerySingleOrDefaultAsync("SELECT fbr_mode FROM settings WHERE id = 1");
        rowAfterNullMode!["fbr_mode"]!.ToString().Should().Be("digital_invoicing");

        // Same for fbr_mode: ""
        GetStatus(await MakeCtrl(new Dictionary<string, object?>
        {
            ["fbr_mode"] = ""
        }).Update()).Should().Be(200);
        var rowAfterEmptyMode = await env.Db.QuerySingleOrDefaultAsync("SELECT fbr_mode FROM settings WHERE id = 1");
        rowAfterEmptyMode!["fbr_mode"]!.ToString().Should().Be("digital_invoicing");

        // 4. whatsapp_provider: "" or null with non-loopback whatsapp_api_url must normalize to "local_agent" and return 422
        foreach (var badProv in new string?[] { "", "   ", null })
        {
            var waRes = await MakeCtrl(new Dictionary<string, object?>
            {
                ["whatsapp_provider"] = badProv,
                ["whatsapp_api_url"] = "https://evil.com/hook"
            }).Update();
            GetStatus(waRes).Should().Be(422);
            using var doc = ParseBody(waRes);
            doc.RootElement.GetProperty("errors").TryGetProperty("whatsapp_api_url", out _).Should().BeTrue();
        }

        // 5. Two-step check: custom_http + external URL -> switch whatsapp_provider to "" or null without changing URL -> 422
        GetStatus(await MakeCtrl(new
        {
            whatsapp_provider = "custom_http",
            whatsapp_api_url = "https://wa.example.com/hook"
        }).Update()).Should().Be(200);

        GetStatus(await MakeCtrl(new Dictionary<string, object?>
        {
            ["whatsapp_provider"] = ""
        }).Update()).Should().Be(422);

        GetStatus(await MakeCtrl(new Dictionary<string, object?>
        {
            ["whatsapp_provider"] = null
        }).Update()).Should().Be(422);

        // 6. Legitimate normalization: whatsapp_provider: null with empty/loopback URL -> 200 and persists "local_agent"
        GetStatus(await MakeCtrl(new Dictionary<string, object?>
        {
            ["whatsapp_provider"] = null,
            ["whatsapp_api_url"] = "http://127.0.0.1:3002"
        }).Update()).Should().Be(200);
        var rowAfterNullProv = await env.Db.QuerySingleOrDefaultAsync("SELECT whatsapp_provider FROM settings WHERE id = 1");
        rowAfterNullProv!["whatsapp_provider"]!.ToString().Should().Be("local_agent");
    }
}
