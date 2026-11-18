using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Karobar.Services;

public class ProductImportService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;
    public const int ChunkSize = 1000;

    private readonly KarobarDb _db;

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["product_code"] = ["product_code", "code", "sku", "productcode", "prod_code"],
        ["name"] = ["name", "product", "product_name", "title"],
        ["formulation"] = ["formulation", "formula"],
        ["description"] = ["description", "desc", "details"],
        ["pack_size"] = ["pack_size", "units_per_pack", "units", "uom_qty", "pack"],
        ["quantity"] = ["quantity", "qty", "stock", "onhand"],
        ["pack_purchase_price"] = ["pack_purchase_price", "ppp", "purchase_price_pack", "pack_cost"],
        ["pack_sale_price"] = ["pack_sale_price", "psp", "sale_price_pack"],
        ["unit_purchase_price"] = ["unit_purchase_price", "upp", "purchase_price_unit", "unit_cost", "cost"],
        ["unit_sale_price"] = ["unit_sale_price", "usp", "sale_price_unit", "unit_price", "price"],
        ["avg_price"] = ["avg_price", "average_price", "weighted_avg", "wac", "wa_cost"],
        ["margin"] = ["margin", "gross_margin_pct"],
        ["narcotic"] = ["narcotic", "is_narcotic", "controlled"],
        ["max_discount"] = ["max_discount", "maxdisc", "max_discount_pct"],
        ["category_id"] = ["category_id", "cat_id"],
        ["brand_id"] = ["brand_id"],
        ["supplier_id"] = ["supplier_id", "vendor_id"],
        ["category"] = ["category", "category_name", "cat", "cat_name"],
        ["brand"] = ["brand", "brand_name"],
        ["supplier"] = ["supplier", "supplier_name", "vendor", "vendor_name"],
        ["rack"] = ["rack", "shelf", "bin"],
        ["barcode"] = ["barcode", "ean", "upc"]
    };

    public ProductImportService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, string>> StoreUploadedAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName)?.TrimStart('.').ToLowerInvariant() ?? "csv";
        if (string.IsNullOrEmpty(ext)) ext = "csv";
        if (ext != "csv" && ext != "txt")
        {
            throw new InvalidOperationException("Only CSV files are supported.");
        }

        var token = Guid.NewGuid().ToString();
        var dir = _db.StoragePath("app/imports/products");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{token}.csv");

        await using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(fs);
        }

        return new Dictionary<string, string>
        {
            ["token"] = token,
            ["path"] = filePath
        };
    }

    public string ResolveStoredPath(string token)
    {
        var primary = _db.StoragePath($"app/imports/products/{token}.csv");
        if (File.Exists(primary)) return primary;
        return _db.StoragePath($"imports/products/{token}.csv");
    }

    public async Task<Dictionary<string, object?>> ValidateCsvAsync(string absolutePath, string delimiter = ",", bool allowCreateRefs = true)
    {
        delimiter = BrandImportService.NormalizeDelimiter(delimiter);
        var (header, colIdx, usedDelimiter) = ReadAndMapHeader(absolutePath, delimiter);

        if (colIdx.Count == 0 || !colIdx.ContainsKey("product_code") || !colIdx.ContainsKey("name") || !colIdx.ContainsKey("pack_size"))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["message"] = "CSV must contain a header with columns: product_code, name, pack_size (order/aliases allowed).",
                ["detected_header"] = header
            };
        }

        var existingProds = await _db.QueryListAsync("SELECT id, product_code, barcode FROM products");
        var codeToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var barcodeToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in existingProds)
        {
            var id = p.TryGetValue("id", out var idv) && idv != null ? Convert.ToInt64(idv) : 0;
            var code = (p.TryGetValue("product_code", out var cv) ? cv?.ToString() : null)?.Trim() ?? "";
            var bc = (p.TryGetValue("barcode", out var bv) ? bv?.ToString() : null)?.Trim() ?? "";
            if (!string.IsNullOrEmpty(code)) codeToId[code.ToLowerInvariant()] = id;
            if (!string.IsNullOrEmpty(bc)) barcodeToCode[bc.ToLowerInvariant()] = code.ToLowerInvariant();
        }

        var (catNameToId, brandNameToId, suppNameToId, catIds, brandIds, suppIds) = await PreloadLookupsAsync();

        var rows = BrandImportService.ReadAllNonEmptyRows(absolutePath, usedDelimiter, sanitizeFormulas: false);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int total = 0, valid = 0, invalid = 0;
        var invalidSamples = new List<Dictionary<string, object?>>();
        var validSamples = new List<Dictionary<string, object?>>();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            int rowNo = i + 1;

            var data = ExtractRowData(row, colIdx);
            var (errors, warnings, _) = await RowIssuesAsync(
                data, codeToId, barcodeToCode, seenCodes, seenBarcodes,
                catNameToId, brandNameToId, suppNameToId,
                catIds, brandIds, suppIds,
                allowCreateRefs, true, null, null);

            total++;
            if (errors.Count > 0)
            {
                invalid++;
                if (invalidSamples.Count < PreviewInvalidLimit)
                {
                    invalidSamples.Add(new Dictionary<string, object?>
                    {
                        ["row"] = rowNo,
                        ["data"] = data,
                        ["errors"] = errors,
                        ["warnings"] = warnings
                    });
                }
            }
            else
            {
                valid++;
                var lc = (data["product_code"]?.ToString() ?? "").ToLowerInvariant();
                var lb = data["barcode"]?.ToString()?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(lc)) seenCodes.Add(lc);
                if (!string.IsNullOrEmpty(lb)) seenBarcodes.Add(lb);

                if (validSamples.Count < PreviewValidLimit)
                {
                    validSamples.Add(new Dictionary<string, object?>
                    {
                        ["row"] = rowNo,
                        ["data"] = data,
                        ["warnings"] = warnings
                    });
                }
            }
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["used_delimiter"] = usedDelimiter,
            ["header"] = header,
            ["header_map"] = colIdx,
            ["total"] = total,
            ["valid"] = valid,
            ["invalid"] = invalid,
            ["invalid_samples"] = invalidSamples,
            ["valid_samples"] = validSamples
        };
    }

    public async Task<Dictionary<string, int>> CommitAsync(string absolutePath, bool insertValidOnly = true, string delimiter = ",", bool allowCreateRefs = true)
    {
        delimiter = BrandImportService.NormalizeDelimiter(delimiter);
        var (_, colIdx, usedDelimiter) = ReadAndMapHeader(absolutePath, delimiter);
        if (colIdx.Count == 0 || !colIdx.ContainsKey("product_code") || !colIdx.ContainsKey("name") || !colIdx.ContainsKey("pack_size"))
        {
            throw new InvalidOperationException("CSV header not recognized (need product_code, name, pack_size).");
        }

        var existingProds = await _db.QueryListAsync("SELECT id, product_code, barcode, quantity FROM products");
        var codeToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var codeToQty = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var barcodeToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in existingProds)
        {
            var id = p.TryGetValue("id", out var idv) && idv != null ? Convert.ToInt64(idv) : 0;
            var code = (p.TryGetValue("product_code", out var cv) ? cv?.ToString() : null)?.Trim() ?? "";
            var bc = (p.TryGetValue("barcode", out var bv) ? bv?.ToString() : null)?.Trim() ?? "";
            var qty = p.TryGetValue("quantity", out var qv) && qv != null ? Convert.ToInt32(qv) : 0;

            if (!string.IsNullOrEmpty(code))
            {
                var lc = code.ToLowerInvariant();
                codeToId[lc] = id;
                codeToQty[lc] = qty;
            }
            if (!string.IsNullOrEmpty(bc))
            {
                barcodeToCode[bc.ToLowerInvariant()] = code.ToLowerInvariant();
            }
        }

        var (catNameToId, brandNameToId, suppNameToId, catIds, brandIds, suppIds) = await PreloadLookupsAsync();
        var stats = new Dictionary<string, int>
        {
            ["total"] = 0,
            ["valid"] = 0,
            ["invalid"] = 0,
            ["committed"] = 0,
            ["new"] = 0,
            ["existing"] = 0
        };

        var rows = BrandImportService.ReadAllNonEmptyRows(absolutePath, usedDelimiter, sanitizeFormulas: false);

        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var data = ExtractRowData(row, colIdx);
            stats["total"]++;

            var (errors, _, normalized) = await RowIssuesAsync(
                data, codeToId, barcodeToCode,
                new HashSet<string>(), new HashSet<string>(),
                catNameToId, brandNameToId, suppNameToId,
                catIds, brandIds, suppIds,
                allowCreateRefs, false, conn, tx);

            if (errors.Count > 0 || normalized == null)
            {
                stats["invalid"]++;
                if (insertValidOnly) continue;
                throw new InvalidOperationException("Import aborted due to validation error.");
            }

            stats["valid"]++;
            FillDerivedPrices(normalized);

            var upp = normalized.TryGetValue("unit_purchase_price", out var uppObj) && uppObj is double u ? u : 0.0;
            if (!normalized.TryGetValue("avg_price", out var avgObj) || avgObj == null)
            {
                normalized["avg_price"] = upp;
            }

            var usp = normalized.TryGetValue("unit_sale_price", out var uspObj) && uspObj is double s ? s : 0.0;
            var avg = normalized.TryGetValue("avg_price", out var aObj) && aObj is double a ? a : 0.0;
            normalized["margin"] = usp > 0 ? Math.Round(((usp - avg) / usp) * 100.0, 2) : 0.0;

            var codeStr = normalized["product_code"]?.ToString() ?? "";
            var lc = codeStr.ToLowerInvariant();
            var hasExplicitQty = data.TryGetValue("quantity", out var rawQ) && rawQ != null && !string.IsNullOrWhiteSpace(rawQ.ToString());

            if (codeToId.TryGetValue(lc, out var existingProdId) && existingProdId > 0)
            {
                stats["existing"]++;
                if (!hasExplicitQty && codeToQty.TryGetValue(lc, out var existingQty))
                {
                    normalized["quantity"] = existingQty;
                }
                await _db.UpdateAsync("products", existingProdId, normalized, "id", conn, tx);
            }
            else
            {
                var dbExistingId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM products WHERE LOWER(product_code) = @lc LIMIT 1",
                    new { lc },
                    tx);

                if (dbExistingId.HasValue)
                {
                    if (codeToId.ContainsKey(lc)) stats["existing"]++;
                    if (!hasExplicitQty && codeToQty.TryGetValue(lc, out var existingQty))
                    {
                        normalized["quantity"] = existingQty;
                    }
                    await _db.UpdateAsync("products", dbExistingId.Value, normalized, "id", conn, tx);
                    codeToId[lc] = dbExistingId.Value;
                }
                else
                {
                    stats["new"]++;
                    var newId = await _db.InsertAsync("products", normalized, conn, tx);
                    codeToId[lc] = newId;
                }
            }

            stats["committed"]++;
        }

        await tx.CommitAsync();
        return stats;
    }

    private (List<string?>? Header, Dictionary<string, int> ColIdx, string UsedDelimiter) ReadAndMapHeader(string path, string delimiter)
    {
        var rows = BrandImportService.ReadAllNonEmptyRows(path, delimiter, maxRows: 1, sanitizeFormulas: false);
        var header = rows.FirstOrDefault();
        var colIdx = header != null ? MapHeaderToCanonical(header) : new Dictionary<string, int>();
        if (colIdx.Count > 0) return (header, colIdx, delimiter);

        var auto = BrandImportService.GuessDelimiterFromSample(path);
        if (auto != delimiter)
        {
            var autoRows = BrandImportService.ReadAllNonEmptyRows(path, auto, maxRows: 1, sanitizeFormulas: false);
            var autoHeader = autoRows.FirstOrDefault();
            var autoColIdx = autoHeader != null ? MapHeaderToCanonical(autoHeader) : new Dictionary<string, int>();
            if (autoColIdx.Count > 0) return (autoHeader, autoColIdx, auto);
        }

        return (header, new Dictionary<string, int>(), delimiter);
    }

    private static Dictionary<string, int> MapHeaderToCanonical(List<string?> header)
    {
        var lower = header.Select(h => (h ?? "").Trim().TrimStart('\uFEFF').ToLowerInvariant()).ToList();
        var map = new Dictionary<string, int>();

        foreach (var (key, aliasList) in Aliases)
        {
            foreach (var alias in aliasList)
            {
                var i = lower.IndexOf(alias);
                if (i >= 0)
                {
                    map[key] = i;
                    break;
                }
            }
        }

        if (!map.ContainsKey("product_code") || !map.ContainsKey("name") || !map.ContainsKey("pack_size"))
        {
            return new Dictionary<string, int>();
        }

        return map;
    }

    private static string? SanitizeCsvFormula(string? value)
    {
        if (value == null) return null;
        var str = value.Trim();
        if (str.Length == 0) return "";
        if (Regex.IsMatch(str, @"^[=+\-@]") && !double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            return "'" + str;
        }
        return str;
    }

    private static Dictionary<string, object?> ExtractRowData(List<string?> row, Dictionary<string, int> colIdx)
    {
        string? G(string k) => BrandImportService.GetCell(row, colIdx, k);

        return new Dictionary<string, object?>
        {
            ["product_code"] = SanitizeCsvFormula(G("product_code")) ?? "",
            ["name"] = SanitizeCsvFormula(G("name")) ?? "",
            ["formulation"] = SanitizeCsvFormula(G("formulation")),
            ["description"] = SanitizeCsvFormula(G("description")),
            ["pack_size"] = G("pack_size"),
            ["quantity"] = G("quantity"),
            ["pack_purchase_price"] = G("pack_purchase_price"),
            ["pack_sale_price"] = G("pack_sale_price"),
            ["unit_purchase_price"] = G("unit_purchase_price"),
            ["unit_sale_price"] = G("unit_sale_price"),
            ["avg_price"] = G("avg_price"),
            ["narcotic"] = G("narcotic"),
            ["max_discount"] = G("max_discount"),
            ["category_id"] = G("category_id"),
            ["brand_id"] = G("brand_id"),
            ["supplier_id"] = G("supplier_id"),
            ["category"] = SanitizeCsvFormula(G("category")),
            ["brand"] = SanitizeCsvFormula(G("brand")),
            ["supplier"] = SanitizeCsvFormula(G("supplier")),
            ["rack"] = SanitizeCsvFormula(G("rack")),
            ["barcode"] = SanitizeCsvFormula(G("barcode"))
        };
    }

    private async Task<(Dictionary<string, string> Errors, Dictionary<string, string> Warnings, Dictionary<string, object?>? Normalized)> RowIssuesAsync(
        Dictionary<string, object?> data,
        Dictionary<string, long> codeToId,
        Dictionary<string, string> barcodeToCode,
        HashSet<string> seenCodes,
        HashSet<string> seenBarcodes,
        Dictionary<string, long> catNameToId,
        Dictionary<string, long> brandNameToId,
        Dictionary<string, long> suppNameToId,
        HashSet<long> catIds,
        HashSet<long> brandIds,
        HashSet<long> suppIds,
        bool allowCreateRefs,
        bool strict,
        IDbConnection? conn,
        IDbTransaction? tx)
    {
        var errors = new Dictionary<string, string>();
        var warnings = new Dictionary<string, string>();

        var code = (data["product_code"]?.ToString() ?? "").Trim();
        var name = (data["name"]?.ToString() ?? "").Trim();

        if (string.IsNullOrEmpty(code)) errors["product_code"] = "product_code is required.";
        if (string.IsNullOrEmpty(name)) errors["name"] = "name is required.";
        if (!string.IsNullOrEmpty(code) && code.Length > 64) warnings["product_code"] = "Unusually long code.";
        if (!string.IsNullOrEmpty(name) && name.Length > 255) errors["name_len"] = "Name too long (max 255).";

        var packSize = ToInt(data["pack_size"]);
        if (packSize == null || packSize <= 0)
        {
            errors["pack_size"] = "pack_size must be a positive integer.";
        }

        foreach (var k in new[] { "quantity", "max_discount" })
        {
            var raw = data[k]?.ToString();
            if (!string.IsNullOrEmpty(raw))
            {
                if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                {
                    errors[k] = $"{k} must be numeric.";
                }
                else if (k == "quantity" && (int)num < 0)
                {
                    errors[k] = "quantity cannot be negative.";
                }
            }
        }

        foreach (var k in new[] { "pack_purchase_price", "pack_sale_price", "unit_purchase_price", "unit_sale_price", "avg_price" })
        {
            var raw = data[k]?.ToString();
            if (!string.IsNullOrEmpty(raw) && !double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            {
                errors[k] = $"{k} must be a number.";
            }
        }

        var narc = NormalizeYesNo(data["narcotic"]);

        var barcode = data["barcode"]?.ToString()?.Trim();
        if (!string.IsNullOrEmpty(barcode))
        {
            var lb = barcode.ToLowerInvariant();
            var lc = code.ToLowerInvariant();
            if (strict && seenBarcodes.Contains(lb))
            {
                errors["barcode_dup_file"] = "Duplicate barcode within file.";
            }
            if (barcodeToCode.TryGetValue(lb, out var ownerCode) && !string.Equals(ownerCode, lc, StringComparison.OrdinalIgnoreCase))
            {
                errors["barcode_conflict"] = "Barcode already used by another product.";
            }
        }
        else
        {
            barcode = null;
        }

        if (strict && !string.IsNullOrEmpty(code))
        {
            var lc = code.ToLowerInvariant();
            if (seenCodes.Contains(lc))
            {
                errors["code_dup_file"] = "Duplicate product_code within file.";
            }
        }

        var categoryId = await ResolveIdOrNameAsync(
            data["category_id"]?.ToString(), data["category"]?.ToString(),
            catNameToId, catIds, "categories", "category", allowCreateRefs, errors, warnings, conn, tx);

        var brandId = await ResolveIdOrNameAsync(
            data["brand_id"]?.ToString(), data["brand"]?.ToString(),
            brandNameToId, brandIds, "brands", "brand", allowCreateRefs, errors, warnings, conn, tx);

        var supplierId = await ResolveIdOrNameAsync(
            data["supplier_id"]?.ToString(), data["supplier"]?.ToString(),
            suppNameToId, suppIds, "suppliers", "supplier", allowCreateRefs, errors, warnings, conn, tx);

        if (errors.Count > 0)
        {
            return (errors, warnings, null);
        }

        var rawFormulation = NullIfEmpty(data["formulation"]?.ToString());
        string? canonicalFormulation = null;
        if (rawFormulation != null)
        {
            canonicalFormulation = !strict
                ? await ResolveOrCreateFormulationAsync(rawFormulation, conn, tx)
                : NormalizeFormulationName(rawFormulation);
        }

        var normalized = new Dictionary<string, object?>
        {
            ["product_code"] = code,
            ["name"] = name,
            ["formulation"] = canonicalFormulation,
            ["description"] = NullIfEmpty(data["description"]?.ToString()),
            ["pack_size"] = packSize!.Value,
            ["quantity"] = ToInt(data["quantity"]) ?? 0,
            ["pack_purchase_price"] = ToFloat(data["pack_purchase_price"]),
            ["pack_sale_price"] = ToFloat(data["pack_sale_price"]),
            ["unit_purchase_price"] = ToFloat(data["unit_purchase_price"]),
            ["unit_sale_price"] = ToFloat(data["unit_sale_price"]),
            ["avg_price"] = ToFloat(data["avg_price"]),
            ["narcotic"] = narc ? "yes" : "no",
            ["max_discount"] = ToInt(data["max_discount"]),
            ["category_id"] = categoryId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supplierId,
            ["rack"] = NullIfEmpty(data["rack"]?.ToString()),
            ["barcode"] = barcode
        };

        return (errors, warnings, normalized);
    }

    private static void FillDerivedPrices(Dictionary<string, object?> r)
    {
        var pack = r.TryGetValue("pack_size", out var ps) && ps != null ? Convert.ToDouble(ps) : 0.0;
        var upp = r.TryGetValue("unit_purchase_price", out var u) && u is double uv ? (double?)uv : null;
        var usp = r.TryGetValue("unit_sale_price", out var s) && s is double sv ? (double?)sv : null;
        var ppp = r.TryGetValue("pack_purchase_price", out var p) && p is double pv ? (double?)pv : null;
        var psp = r.TryGetValue("pack_sale_price", out var sp) && sp is double spv ? (double?)spv : null;

        if (pack > 0)
        {
            if (upp == null && ppp != null) r["unit_purchase_price"] = Math.Round(ppp.Value / pack, 4);
            if (usp == null && psp != null) r["unit_sale_price"] = Math.Round(psp.Value / pack, 4);
            if (ppp == null && upp != null) r["pack_purchase_price"] = Math.Round(upp.Value * pack, 4);
            if (psp == null && usp != null) r["pack_sale_price"] = Math.Round(usp.Value * pack, 4);
        }
    }

    private async Task<long?> ResolveIdOrNameAsync(
        string? idRaw,
        string? nameRaw,
        Dictionary<string, long> nameToId,
        HashSet<long> existingIds,
        string tableName,
        string label,
        bool create,
        Dictionary<string, string> errors,
        Dictionary<string, string> warnings,
        IDbConnection? conn,
        IDbTransaction? tx)
    {
        if (!string.IsNullOrEmpty(idRaw))
        {
            if (!long.TryParse(idRaw, out var id))
            {
                errors[$"{label}_id"] = $"{label}_id must be numeric.";
                return null;
            }
            if (id > 0 && existingIds.Contains(id))
            {
                return id;
            }
            errors[$"{label}_id"] = $"{char.ToUpperInvariant(label[0])}{label[1..]} id not found.";
            return null;
        }

        var name = NullIfEmpty(nameRaw);
        if (name == null) return null;

        var ln = name.ToLowerInvariant();
        if (nameToId.TryGetValue(ln, out var cachedId))
        {
            return cachedId;
        }

        if (create)
        {
            var newId = await _db.InsertAsync(tableName, new Dictionary<string, object?> { ["name"] = name }, conn, tx);
            nameToId[ln] = newId;
            existingIds.Add(newId);
            warnings[$"{label}_created"] = $"{char.ToUpperInvariant(label[0])}{label[1..]} created: {name}";
            return newId;
        }

        errors[label] = $"{char.ToUpperInvariant(label[0])}{label[1..]} not found: {name}";
        return null;
    }

    private async Task<(
        Dictionary<string, long> CatNameToId,
        Dictionary<string, long> BrandNameToId,
        Dictionary<string, long> SuppNameToId,
        HashSet<long> CatIds,
        HashSet<long> BrandIds,
        HashSet<long> SuppIds)> PreloadLookupsAsync()
    {
        var cats = await _db.QueryListAsync("SELECT id, name FROM categories");
        var brands = await _db.QueryListAsync("SELECT id, name FROM brands");
        var supps = await _db.QueryListAsync("SELECT id, name FROM suppliers");

        var catMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var brandMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var suppMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var catIds = new HashSet<long>();
        var brandIds = new HashSet<long>();
        var suppIds = new HashSet<long>();

        foreach (var r in cats)
        {
            var id = Convert.ToInt64(r["id"] ?? 0);
            var n = (r["name"]?.ToString() ?? "").Trim();
            if (id > 0) catIds.Add(id);
            if (!string.IsNullOrEmpty(n)) catMap[n.ToLowerInvariant()] = id;
        }
        foreach (var r in brands)
        {
            var id = Convert.ToInt64(r["id"] ?? 0);
            var n = (r["name"]?.ToString() ?? "").Trim();
            if (id > 0) brandIds.Add(id);
            if (!string.IsNullOrEmpty(n)) brandMap[n.ToLowerInvariant()] = id;
        }
        foreach (var r in supps)
        {
            var id = Convert.ToInt64(r["id"] ?? 0);
            var n = (r["name"]?.ToString() ?? "").Trim();
            if (id > 0) suppIds.Add(id);
            if (!string.IsNullOrEmpty(n)) suppMap[n.ToLowerInvariant()] = id;
        }

        return (catMap, brandMap, suppMap, catIds, brandIds, suppIds);
    }

    public static string NormalizeFormulationName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) return "";
        return Regex.Replace(trimmed, @"\s+", " ");
    }

    public async Task<string?> ResolveOrCreateFormulationAsync(string? rawName, IDbConnection? conn = null, IDbTransaction? tx = null)
    {
        var clean = NormalizeFormulationName(rawName);
        if (string.IsNullOrEmpty(clean)) return null;

        if (!await _db.HasTableAsync("formulations"))
        {
            return clean;
        }

        var key = clean.ToLowerInvariant();
        var existing = await _db.QuerySingleOrDefaultAsync(
            "SELECT id, name FROM formulations WHERE normalized_name = @key OR LOWER(name) = @key LIMIT 1",
            new { key },
            conn,
            tx);

        if (existing != null && existing.TryGetValue("name", out var existingName) && existingName != null)
        {
            return existingName.ToString();
        }

        try
        {
            await _db.InsertAsync("formulations", new Dictionary<string, object?>
            {
                ["name"] = clean,
                ["normalized_name"] = key
            }, conn, tx);
        }
        catch
        {
        }

        return clean;
    }

    private static double? ToFloat(object? v)
    {
        var s = v?.ToString()?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static int? ToInt(object? v)
    {
        var s = v?.ToString()?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return (int)d;
        return null;
    }

    private static string? NullIfEmpty(string? v)
    {
        if (v == null) return null;
        var s = v.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static bool NormalizeYesNo(object? v)
    {
        var s = (v?.ToString() ?? "").Trim().ToLowerInvariant();
        return s is "1" or "y" or "yes" or "true" or "t";
    }
}
