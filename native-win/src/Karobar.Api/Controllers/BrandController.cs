using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/brands")]
public class BrandController : ApiControllerBase
{
    private static readonly string[] LookupPermissions =
    [
        "brand.view", "product.view", "product.create", "product.update",
        "barcode.view", "barcode.print", "batch-expiry.view",
        "purchase-order.view", "purchase-order.create", "purchase-order.update",
        "stocktake.view", "stocktake.create", "stocktake.update",
        "report.current-stock.view", "report.reorder.view", "report.near-expiry-product.view"
    ];

    public BrandController(KarobarDb db) : base(db)
    {
    }

    public static Dictionary<string, object?> NormalizeExpiryPolicyPayload(IDictionary<string, object?> validated)
    {
        var result = new Dictionary<string, object?>(validated, StringComparer.OrdinalIgnoreCase);

        var policy = result.TryGetValue("expiry_return_policy", out var pObj) && pObj != null
            ? pObj.ToString()!.Trim()
            : "before_expiry";

        if (policy != "before_expiry" && policy != "accepts_expired" && policy != "non_returnable")
        {
            policy = "before_expiry";
        }

        int months;
        int days;

        if (policy == "accepts_expired" || policy == "non_returnable")
        {
            months = 0;
            days = 0;
        }
        else
        {
            int? parsedMonths = null;
            if (result.TryGetValue("expiry_return_months", out var mObj) && mObj != null && !string.IsNullOrWhiteSpace(mObj.ToString()))
            {
                if (int.TryParse(mObj.ToString(), out var mVal)) parsedMonths = Math.Max(0, mVal);
            }

            int? parsedDays = null;
            if (result.TryGetValue("expiry_return_days", out var dObj) && dObj != null && !string.IsNullOrWhiteSpace(dObj.ToString()))
            {
                if (int.TryParse(dObj.ToString(), out var dVal)) parsedDays = Math.Max(0, dVal);
            }

            if (parsedMonths.HasValue && parsedMonths.Value > 0 && (!parsedDays.HasValue || parsedDays.Value == 0))
            {
                parsedDays = parsedMonths.Value == 12 ? 365 : (parsedMonths.Value == 24 ? 730 : parsedMonths.Value * 30);
            }
            else if (parsedDays.HasValue && parsedDays.Value > 0 && (!parsedMonths.HasValue || parsedMonths.Value == 0))
            {
                parsedMonths = parsedDays.Value == 365 ? 12 : (parsedDays.Value == 730 ? 24 : (int)Math.Round(parsedDays.Value / 30.0));
            }
            else if (!parsedMonths.HasValue && !parsedDays.HasValue)
            {
                parsedMonths = 3;
                parsedDays = 90;
            }

            months = parsedMonths ?? 0;
            days = parsedDays ?? 0;
        }

        result["expiry_return_policy"] = policy;
        result["expiry_return_months"] = months;
        result["expiry_return_days"] = days;

        if (result.TryGetValue("expiry_return_notes", out var nObj))
        {
            result["expiry_return_notes"] = nObj != null ? nObj.ToString()!.Trim() : null;
        }

        return result;
    }

    public static string FormatExpiryPolicyLabel(string? policy, int? months, int? days)
    {
        var pol = string.IsNullOrWhiteSpace(policy) ? "before_expiry" : policy!;
        if (pol == "accepts_expired") return "Accepts Expired Products";
        if (pol == "non_returnable") return "Non-Returnable";

        if (months.HasValue && months.Value > 0)
        {
            var m = months.Value;
            if (m % 12 == 0)
            {
                var years = m / 12;
                return years == 1 ? "1 Year Before Expiry" : $"{years} Years Before Expiry";
            }
            return m == 1 ? "1 Month Before Expiry" : $"{m} Months Before Expiry";
        }

        if (days.HasValue && days.Value > 0)
        {
            var d = days.Value;
            if (d == 365) return "1 Year Before Expiry";
            if (d == 730) return "2 Years Before Expiry";
            if (d % 30 == 0)
            {
                var calcMonths = d / 30;
                return calcMonths == 1 ? "1 Month Before Expiry" : $"{calcMonths} Months Before Expiry";
            }
            return $"{d} Days Before Expiry";
        }

        return "3 Months Before Expiry";
    }

    public static Dictionary<string, object?> EvaluateBatchIntimation(
        string? expiryDateStr,
        string? policy,
        int policyMonths,
        int policyDays)
    {
        var today = DateTime.UtcNow.Date;
        DateTime expiry;
        if (string.IsNullOrWhiteSpace(expiryDateStr) || !DateTime.TryParse(expiryDateStr, out expiry))
        {
            expiry = today;
        }
        else
        {
            expiry = expiry.Date;
        }

        var daysRemaining = (int)(expiry - today).TotalDays;
        var pol = string.IsNullOrWhiteSpace(policy) ? "before_expiry" : policy!;

        if (pol == "non_returnable")
        {
            return new Dictionary<string, object?>
            {
                ["days_remaining"] = daysRemaining,
                ["return_deadline_date"] = null,
                ["days_until_return_deadline"] = null,
                ["intimation_status"] = daysRemaining < 0 ? "expired_non_returnable" : "non_returnable",
                ["is_intimation_due"] = false
            };
        }

        if (pol == "accepts_expired")
        {
            var isDue = daysRemaining <= 30;
            return new Dictionary<string, object?>
            {
                ["days_remaining"] = daysRemaining,
                ["return_deadline_date"] = expiry.ToString("yyyy-MM-dd"),
                ["days_until_return_deadline"] = daysRemaining,
                ["intimation_status"] = daysRemaining < 0 ? "expired_returnable" : (daysRemaining <= 30 ? "intimate_now" : "safe"),
                ["is_intimation_due"] = isDue
            };
        }

        var effectiveDays = policyDays > 0
            ? policyDays
            : (policyMonths > 0 ? (policyMonths == 12 ? 365 : (policyMonths == 24 ? 730 : policyMonths * 30)) : 90);

        var deadline = policyMonths > 0
            ? expiry.AddMonths(-policyMonths)
            : expiry.AddDays(-effectiveDays);

        var daysUntilDeadline = (int)(deadline - today).TotalDays;

        string status;
        bool due;
        if (daysRemaining < 0)
        {
            status = "expired_window_missed";
            due = true;
        }
        else if (daysUntilDeadline < 0)
        {
            status = "return_window_open";
            due = true;
        }
        else if (daysUntilDeadline <= 30)
        {
            status = "intimate_now";
            due = true;
        }
        else
        {
            status = "safe";
            due = false;
        }

        return new Dictionary<string, object?>
        {
            ["days_remaining"] = daysRemaining,
            ["return_deadline_date"] = deadline.ToString("yyyy-MM-dd"),
            ["days_until_return_deadline"] = daysUntilDeadline,
            ["intimation_status"] = status,
            ["is_intimation_due"] = due
        };
    }

    public static void AttachPolicyLabel(Dictionary<string, object?> brand)
    {
        var pol = brand.TryGetValue("expiry_return_policy", out var p) ? p?.ToString() : "before_expiry";
        int? months = brand.TryGetValue("expiry_return_months", out var m) && m != null && int.TryParse(m.ToString(), out var mv) ? mv : 3;
        int? days = brand.TryGetValue("expiry_return_days", out var d) && d != null && int.TryParse(d.ToString(), out var dv) ? dv : 90;
        brand["expiry_policy_label"] = FormatExpiryPolicyLabel(pol, months, days);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q = "", [FromQuery] int limit = 50)
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        q = (q ?? "").Trim();
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 10000);

        var sql = @"
            SELECT id, name, expiry_return_policy, expiry_return_months, expiry_return_days, expiry_return_notes
            FROM brands";
        var param = new DynamicParameters();
        if (!string.IsNullOrEmpty(q))
        {
            sql += " WHERE name LIKE @q";
            param.Add("q", $"%{q}%");
        }
        sql += " ORDER BY name ASC LIMIT @limit";
        param.Add("limit", limit);

        var items = await Db.QueryListAsync(sql, param);
        foreach (var item in items) AttachPolicyLabel(item);
        return Ok(items);
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "per_page")] int perPage = 25,
        [FromQuery] int page = 1,
        [FromQuery(Name = "q_name")] string? qName = null,
        [FromQuery] string? q = null,
        [FromQuery] string? policy = "all")
    {
        if (!await CanAsync("brand.view")) return Forbidden();

        perPage = Math.Clamp(perPage <= 0 ? 25 : perPage, 1, 2000);
        page = Math.Max(1, page);
        var search = (qName ?? q ?? "").Trim();
        var policyFilter = (policy ?? "all").Trim();

        var where = new List<string>();
        var param = new DynamicParameters();

        if (!string.IsNullOrEmpty(search))
        {
            where.Add("b.name LIKE @q");
            param.Add("q", $"%{search}%");
        }

        if (!string.IsNullOrEmpty(policyFilter) && policyFilter != "all")
        {
            if (policyFilter is "before_expiry" or "accepts_expired" or "non_returnable")
            {
                where.Add("b.expiry_return_policy = @pol");
                param.Add("pol", policyFilter);
            }
            else
            {
                var m = Regex.Match(policyFilter, @"^before_(\d+)$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var months))
                {
                    where.Add("b.expiry_return_policy = 'before_expiry' AND b.expiry_return_months = @months");
                    param.Add("months", months);
                }
            }
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        var selectSql = $@"
            SELECT b.id, b.name, b.image,
                   b.expiry_return_policy, b.expiry_return_months, b.expiry_return_days, b.expiry_return_notes,
                   (SELECT COUNT(1) FROM products p WHERE p.brand_id = b.id) AS products_count
            FROM brands b
            {whereClause}
            ORDER BY b.name ASC";

        var countSql = $"SELECT COUNT(1) FROM brands b {whereClause}";

        var result = await Db.PaginateAsync(selectSql, countSql, param, page, perPage, async items =>
        {
            var brandIds = items.Select(i => Convert.ToInt64(i["id"] ?? 0)).Where(id => id > 0).ToArray();
            var batchesByBrand = new Dictionary<long, List<Dictionary<string, object?>>>();

            if (brandIds.Length > 0 && await Db.HasTableAsync("batches"))
            {
                var batchRows = await Db.QueryListAsync(@"
                    SELECT p.brand_id, b.expiry_date, b.quantity
                    FROM batches b
                    INNER JOIN products p ON p.id = b.product_id
                    WHERE p.brand_id IN @brandIds AND b.expiry_date IS NOT NULL AND b.quantity > 0",
                    new { brandIds });

                foreach (var br in batchRows)
                {
                    var bid = Convert.ToInt64(br["brand_id"] ?? 0);
                    if (!batchesByBrand.TryGetValue(bid, out var list))
                    {
                        list = new List<Dictionary<string, object?>>();
                        batchesByBrand[bid] = list;
                    }
                    list.Add(br);
                }
            }

            foreach (var brand in items)
            {
                AttachPolicyLabel(brand);
                var bid = Convert.ToInt64(brand["id"] ?? 0);
                var dueCount = 0;
                var dueUnits = 0;
                if (batchesByBrand.TryGetValue(bid, out var brandBatches))
                {
                    var pol = brand["expiry_return_policy"]?.ToString() ?? "before_expiry";
                    var months = brand["expiry_return_months"] != null ? Convert.ToInt32(brand["expiry_return_months"]) : 3;
                    var days = brand["expiry_return_days"] != null ? Convert.ToInt32(brand["expiry_return_days"]) : 90;

                    foreach (var bRow in brandBatches)
                    {
                        var eval = EvaluateBatchIntimation(bRow["expiry_date"]?.ToString(), pol, months, days);
                        if (eval["is_intimation_due"] is true)
                        {
                            dueCount++;
                            dueUnits += Convert.ToInt32(bRow["quantity"] ?? 0);
                        }
                    }
                }
                brand["intimation_due_count"] = dueCount;
                brand["intimation_due_units"] = dueUnits;
            }

            return items;
        });

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("brand.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";

        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM brands WHERE name = @name", new { name });
        if (exists > 0)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name has already been taken."]
            });
        }

        string? imagePath = null;
        if (Request.HasFormContentType && Request.Form.Files.GetFile("image") is { Length: > 0 } file)
        {
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? ".jpg";
            var dir = Db.StoragePath("app/public/brands");
            Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid()}{ext}";
            await using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create);
            await file.CopyToAsync(fs);
            imagePath = $"brands/{fileName}";
        }

        var rawPayload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["expiry_return_policy"] = GetString(body, "expiry_return_policy"),
            ["expiry_return_months"] = GetString(body, "expiry_return_months"),
            ["expiry_return_days"] = GetString(body, "expiry_return_days"),
            ["expiry_return_notes"] = GetString(body, "expiry_return_notes")
        };

        var normalized = NormalizeExpiryPolicyPayload(rawPayload);
        if (imagePath != null) normalized["image"] = imagePath;

        var id = await Db.InsertAsync("brands", normalized);
        var brand = await Db.QuerySingleOrDefaultAsync("SELECT * FROM brands WHERE id = @id", new { id });
        if (brand != null) AttachPolicyLabel(brand);

        return StatusCode(201, brand);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("brand.view")) return Forbidden();

        var brand = await Db.QuerySingleOrDefaultAsync(@"
            SELECT b.*, (SELECT COUNT(1) FROM products p WHERE p.brand_id = b.id) AS products_count
            FROM brands b WHERE b.id = @id", new { id });

        if (brand == null) return NotFound(new { message = "Brand not found." });

        AttachPolicyLabel(brand);

        var intimationBatches = new List<Dictionary<string, object?>>();
        if (await Db.HasTableAsync("batches"))
        {
            var rows = await Db.QueryListAsync(@"
                SELECT b.id AS batch_id, b.batch_number, b.expiry_date, b.quantity,
                       p.id AS product_id, p.name AS product_name, p.product_code, p.pack_size,
                       COALESCE(s.name, '') AS supplier_name
                FROM batches b
                INNER JOIN products p ON p.id = b.product_id
                LEFT JOIN suppliers s ON s.id = p.supplier_id
                WHERE p.brand_id = @id AND b.expiry_date IS NOT NULL AND b.quantity > 0
                ORDER BY b.expiry_date ASC
                LIMIT 150", new { id });

            var pol = brand["expiry_return_policy"]?.ToString() ?? "before_expiry";
            var months = brand["expiry_return_months"] != null ? Convert.ToInt32(brand["expiry_return_months"]) : 3;
            var days = brand["expiry_return_days"] != null ? Convert.ToInt32(brand["expiry_return_days"]) : 90;

            foreach (var r in rows)
            {
                var eval = EvaluateBatchIntimation(r["expiry_date"]?.ToString(), pol, months, days);
                foreach (var kv in eval) r[kv.Key] = kv.Value;
                intimationBatches.Add(r);
            }
        }

        brand["intimation_batches"] = intimationBatches;
        return Ok(brand);
    }

    [HttpPut("{id:long}")]
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("brand.update")) return Forbidden();

        var brand = await Db.QuerySingleOrDefaultAsync("SELECT * FROM brands WHERE id = @id", new { id });
        if (brand == null) return NotFound(new { message = "Brand not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";

        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM brands WHERE name = @name AND id != @id", new { name, id });
        if (exists > 0)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name has already been taken."]
            });
        }

        var updateData = new Dictionary<string, object?>
        {
            ["name"] = name
        };

        if (body.ContainsKey("expiry_return_policy") || body.ContainsKey("expiry_return_months") ||
            body.ContainsKey("expiry_return_days") || body.ContainsKey("expiry_return_notes"))
        {
            var merged = new Dictionary<string, object?>
            {
                ["expiry_return_policy"] = body.ContainsKey("expiry_return_policy") ? GetString(body, "expiry_return_policy") : brand["expiry_return_policy"] ?? "before_expiry",
                ["expiry_return_months"] = body.ContainsKey("expiry_return_months") ? GetString(body, "expiry_return_months") : brand["expiry_return_months"] ?? 3,
                ["expiry_return_days"] = body.ContainsKey("expiry_return_days") ? GetString(body, "expiry_return_days") : brand["expiry_return_days"] ?? 90,
                ["expiry_return_notes"] = body.ContainsKey("expiry_return_notes") ? GetString(body, "expiry_return_notes") : brand["expiry_return_notes"]
            };

            var norm = NormalizeExpiryPolicyPayload(merged);
            foreach (var kv in norm) updateData[kv.Key] = kv.Value;
        }

        if (Request.HasFormContentType && Request.Form.Files.GetFile("image") is { Length: > 0 } file)
        {
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? ".jpg";
            var dir = Db.StoragePath("app/public/brands");
            Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid()}{ext}";
            await using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create);
            await file.CopyToAsync(fs);
            updateData["image"] = $"brands/{fileName}";
        }

        await Db.UpdateAsync("brands", id, updateData);
        var fresh = await Db.QuerySingleOrDefaultAsync("SELECT * FROM brands WHERE id = @id", new { id });
        if (fresh != null) AttachPolicyLabel(fresh);

        return Ok(fresh);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("brand.delete")) return Forbidden();

        var brand = await Db.QuerySingleOrDefaultAsync("SELECT * FROM brands WHERE id = @id", new { id });
        if (brand == null) return NotFound(new { message = "Brand not found." });

        var prodCount = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM products WHERE brand_id = @id", new { id });
        if (prodCount > 0)
        {
            return Unprocessable("Cannot delete: brand is used by one or more products.");
        }

        await Db.ExecuteAsync("DELETE FROM brands WHERE id = @id", new { id });
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        if (!await CanAsync("brand.export")) return Forbidden();

        var rows = await Db.QueryListAsync(@"
            SELECT name, expiry_return_policy, expiry_return_months, expiry_return_days, expiry_return_notes, image
            FROM brands
            ORDER BY name ASC");

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("name,expiry_return_policy,expiry_return_months,expiry_return_days,expiry_return_notes,image");

        foreach (var b in rows)
        {
            var line = string.Join(",",
                EscapeCsv(SanitizeCsvCell(b["name"])),
                EscapeCsv(SanitizeCsvCell(b["expiry_return_policy"] ?? "before_expiry")),
                EscapeCsv((b["expiry_return_months"] ?? 3).ToString()!),
                EscapeCsv((b["expiry_return_days"] ?? 90).ToString()!),
                EscapeCsv(SanitizeCsvCell(b["expiry_return_notes"])),
                EscapeCsv(SanitizeCsvCell(b["image"])));
            sb.AppendLine(line);
        }

        var fileName = $"brands_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=UTF-8", fileName);
    }

    internal static string SanitizeCsvCell(object? val)
    {
        var str = val?.ToString() ?? "";
        if (!string.IsNullOrEmpty(str) && (str[0] is '=' or '+' or '-' or '@' or '\t' or '\r') && !Regex.IsMatch(str, @"^[+-]?\d+(\.\d+)?$"))
        {
            return "'" + str;
        }
        return str;
    }

    internal static string EscapeCsv(string val)
    {
        if (val.Contains('"') || val.Contains(',') || val.Contains('\n') || val.Contains('\r'))
        {
            return "\"" + val.Replace("\"", "\"\"") + "\"";
        }
        return val;
    }
}
