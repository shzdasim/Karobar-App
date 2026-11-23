using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/storage-locations")]
[Authorize]
public class StorageLocationController : ApiControllerBase
{
    private static readonly string[] ViewPermissions =
    [
        "storage-location.manage", "storage-location.view", "product.view", "product.create", "product.update",
        "batch-expiry.view", "batch-expiry.update",
        "purchase-invoice.view", "purchase-invoice.create",
        "sale-invoice.view", "sale-invoice.create",
        "stocktake.view", "stock-adjustment.view", "stock-adjustment.create",
        "report.current-stock.view", "report.near-expiry-product.view"
    ];

    private static readonly string[] ManagePermissions =
    [
        "storage-location.manage", "product.create", "product.update", "batch-expiry.update", "stock-adjustment.create"
    ];

    public StorageLocationController(KarobarDb db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery(Name = "active_only")] string? activeOnlyRaw = null)
    {
        if (!await CanAsync(ViewPermissions)) return Forbidden();
        if (!await Db.HasTableAsync("storage_locations")) return Ok(Array.Empty<object>());

        var activeOnly = !string.IsNullOrWhiteSpace(activeOnlyRaw) &&
            (activeOnlyRaw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             activeOnlyRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             activeOnlyRaw.Equals("yes", StringComparison.OrdinalIgnoreCase));
        var where = activeOnly ? "WHERE sl.is_active = 1" : "";
        var items = await Db.QueryListAsync($@"
            SELECT sl.*,
                   (SELECT COUNT(1) FROM products p WHERE p.storage_location_id = sl.id) AS products_count,
                   (SELECT COUNT(1) FROM batches b WHERE b.storage_location_id = sl.id) AS batches_count
            FROM storage_locations sl
            {where}
            ORDER BY sl.is_default DESC, sl.name ASC");

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync(ManagePermissions)) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var rawCode = GetString(body, "code")?.Trim() ?? "";
        var type = GetString(body, "type", "shop") ?? "shop";
        var defaultRack = GetString(body, "default_rack")?.Trim();
        var isDefault = GetBool(body, "is_default", false);
        var isActive = GetBool(body, "is_active", true);
        var notes = GetString(body, "notes")?.Trim();

        await Db.GetTableColumnsAsync("storage_locations");

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);

        long id;
        try
        {
            string code;
            if (string.IsNullOrEmpty(rawCode))
            {
                var slug = Regex.Replace(name.ToUpperInvariant(), @"[^A-Z0-9]+", "-").Trim('-');
                if (string.IsNullOrEmpty(slug)) slug = "LOC";
                var baseCode = slug.Length > 20 ? slug[..20] : slug;
                code = baseCode;
                int counter = 1;
                while (await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM storage_locations WHERE code = @code", new { code }, conn, tx) > 0)
                {
                    counter++;
                    code = $"{baseCode}-{counter}";
                }
            }
            else
            {
                code = rawCode.ToUpperInvariant();
                var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM storage_locations WHERE code = @code", new { code }, conn, tx);
                if (exists > 0)
                {
                    await tx.RollbackAsync();
                    return Unprocessable("The code has already been taken.", new Dictionary<string, string[]>
                    {
                        ["code"] = ["The code has already been taken."]
                    });
                }
            }

            if (isDefault)
            {
                await conn.ExecuteAsync("UPDATE storage_locations SET is_default = 0 WHERE is_default = 1", transaction: tx);
            }

            id = await Db.InsertAsync("storage_locations", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["code"] = code,
                ["type"] = type,
                ["default_rack"] = string.IsNullOrEmpty(defaultRack) ? null : defaultRack,
                ["is_default"] = isDefault,
                ["is_active"] = isActive,
                ["notes"] = string.IsNullOrEmpty(notes) ? null : notes
            }, conn, tx);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        var location = await Db.QuerySingleOrDefaultAsync(@"
            SELECT sl.*,
                   (SELECT COUNT(1) FROM products p WHERE p.storage_location_id = sl.id) AS products_count,
                   (SELECT COUNT(1) FROM batches b WHERE b.storage_location_id = sl.id) AS batches_count
            FROM storage_locations sl
            WHERE sl.id = @id", new { id });

        return StatusCode(201, location);
    }

    [HttpPut("{id:long}")]
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync(ManagePermissions)) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM storage_locations WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Storage location not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        var code = (GetString(body, "code") ?? "").Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }
        if (string.IsNullOrEmpty(code))
        {
            return Unprocessable("The code field is required.", new Dictionary<string, string[]>
            {
                ["code"] = ["The code field is required."]
            });
        }

        await Db.GetTableColumnsAsync("storage_locations");

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);

        try
        {
            var lockedExisting = await Db.QuerySingleOrDefaultAsync("SELECT * FROM storage_locations WHERE id = @id", new { id }, conn, tx);
            if (lockedExisting == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Storage location not found." });
            }

            var dup = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM storage_locations WHERE code = @code AND id != @id", new { code, id }, conn, tx);
            if (dup > 0)
            {
                await tx.RollbackAsync();
                return Unprocessable("The code has already been taken.", new Dictionary<string, string[]>
                {
                    ["code"] = ["The code has already been taken."]
                });
            }

            var isDefault = body.ContainsKey("is_default") ? GetBool(body, "is_default") : (lockedExisting["is_default"] is true or 1 or 1L);
            var isActive = body.ContainsKey("is_active") ? GetBool(body, "is_active", true) : (lockedExisting["is_active"] is true or 1 or 1L);
            var type = GetString(body, "type") ?? lockedExisting["type"]?.ToString() ?? "shop";
            var defaultRack = body.ContainsKey("default_rack") ? (GetString(body, "default_rack")?.Trim() is { Length: > 0 } dr ? dr : null) : lockedExisting["default_rack"]?.ToString();
            var notes = body.ContainsKey("notes") ? (GetString(body, "notes")?.Trim() is { Length: > 0 } nt ? nt : null) : lockedExisting["notes"]?.ToString();

            if (isDefault)
            {
                await conn.ExecuteAsync("UPDATE storage_locations SET is_default = 0 WHERE id != @id", new { id }, tx);
            }

            await Db.UpdateAsync("storage_locations", id, new Dictionary<string, object?>
            {
                ["name"] = name,
                ["code"] = code,
                ["type"] = type,
                ["default_rack"] = defaultRack,
                ["is_default"] = isDefault,
                ["is_active"] = isActive,
                ["notes"] = notes
            }, "id", conn, tx);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        var location = await Db.QuerySingleOrDefaultAsync(@"
            SELECT sl.*,
                   (SELECT COUNT(1) FROM products p WHERE p.storage_location_id = sl.id) AS products_count,
                   (SELECT COUNT(1) FROM batches b WHERE b.storage_location_id = sl.id) AS batches_count
            FROM storage_locations sl
            WHERE sl.id = @id", new { id });

        return Ok(location);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync(ManagePermissions)) return Forbidden();

        var hasProdLocCol = await Db.HasColumnAsync("products", "storage_location_id");
        var hasBatchLocCol = await Db.HasTableAsync("batches") && await Db.HasColumnAsync("batches", "storage_location_id");

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);

        try
        {
            var lockedExisting = await Db.QuerySingleOrDefaultAsync("SELECT * FROM storage_locations WHERE id = @id", new { id }, conn, tx);
            if (lockedExisting == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Storage location not found." });
            }

            if (lockedExisting["is_default"] is true or 1 or 1L)
            {
                await tx.RollbackAsync();
                return Unprocessable("Cannot delete the default storage location. Mark another location as default first.", new Dictionary<string, string[]>
                {
                    ["storage_location"] = ["Cannot delete the default storage location. Mark another location as default first."]
                });
            }

            var fallbackId = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM storage_locations WHERE is_default = 1 AND id != @id LIMIT 1", new { id }, tx)
                ?? await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM storage_locations WHERE id != @id ORDER BY id ASC LIMIT 1", new { id }, tx);

            if (hasProdLocCol)
            {
                await conn.ExecuteAsync("UPDATE products SET storage_location_id = @fallbackId WHERE storage_location_id = @id", new { fallbackId, id }, tx);
            }
            if (hasBatchLocCol)
            {
                await conn.ExecuteAsync("UPDATE batches SET storage_location_id = @fallbackId WHERE storage_location_id = @id", new { fallbackId, id }, tx);
            }

            await conn.ExecuteAsync("DELETE FROM storage_locations WHERE id = @id", new { id }, tx);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return Ok(new { message = "Storage location deleted successfully." });
    }
}
