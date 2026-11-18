using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Karobar.Services;

public class SupplierImportService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;
    public const int ChunkSize = 1000;

    private readonly KarobarDb _db;

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ["name", "supplier", "supplier_name", "company", "company_name", "suppliername"],
        ["address"] = ["address", "addr", "supplier_address", "location", "address1", "address_line1"],
        ["phone"] = ["phone", "mobile", "contact", "contact_no", "contact_number", "phone_number", "tel", "telephone"],
        ["opening_balance"] = ["opening_balance", "opening", "balance", "initial_balance"]
    };

    public SupplierImportService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, string>> StoreUploadedAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName)?.TrimStart('.').ToLowerInvariant() ?? "csv";
        if (string.IsNullOrEmpty(ext)) ext = "csv";
        if (ext != "csv" && ext != "txt")
        {
            throw new InvalidOperationException("Only CSV files are supported right now.");
        }

        var token = Guid.NewGuid().ToString();
        var dir = _db.StoragePath("app/imports/suppliers");
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
        var primary = _db.StoragePath($"app/imports/suppliers/{token}.csv");
        if (File.Exists(primary)) return primary;
        return _db.StoragePath($"imports/suppliers/{token}.csv");
    }

    public async Task<Dictionary<string, object?>> ValidateCsvAsync(string absolutePath, string delimiter = ",")
    {
        delimiter = BrandImportService.NormalizeDelimiter(delimiter);
        var (header, colIdx, usedDelimiter) = ReadAndMapHeader(absolutePath, delimiter);

        if (header == null || header.Count == 0 || colIdx.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["message"] = "CSV must contain a header with columns: name,address,phone (any order).",
                ["detected_header"] = header,
                ["hint"] = "Accepted synonyms: name|supplier|company, address|addr|location, phone|mobile|contact."
            };
        }

        var existingRows = await _db.QueryListAsync("SELECT name FROM suppliers");
        var existingLower = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in existingRows)
        {
            var n = (r.TryGetValue("name", out var v) ? v?.ToString() : null)?.Trim();
            if (!string.IsNullOrEmpty(n)) existingLower.Add(n.ToLowerInvariant());
        }

        var rows = BrandImportService.ReadAllNonEmptyRows(absolutePath, usedDelimiter);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int total = 0, valid = 0, invalid = 0;
        var invalidSamples = new List<Dictionary<string, object?>>();
        var validSamples = new List<Dictionary<string, object?>>();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            int rowNo = i + 1;

            var nameVal = BrandImportService.GetCell(row, colIdx, "name");
            var addrVal = BrandImportService.GetCell(row, colIdx, "address");
            var phoneVal = BrandImportService.GetCell(row, colIdx, "phone");

            var data = new Dictionary<string, object?>
            {
                ["name"] = nameVal,
                ["address"] = addrVal,
                ["phone"] = phoneVal
            };

            var (errors, warnings) = RowIssues(data, existingLower, seenInFile, true);
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
                var lower = (nameVal ?? "").Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(lower)) seenInFile.Add(lower);
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

    public async Task<Dictionary<string, int>> CommitAsync(string absolutePath, bool insertValidOnly = true, string delimiter = ",")
    {
        delimiter = BrandImportService.NormalizeDelimiter(delimiter);
        var (_, colIdx, usedDelimiter) = ReadAndMapHeader(absolutePath, delimiter);
        if (colIdx.Count == 0)
        {
            throw new InvalidOperationException("CSV header not recognized. Expected: name,address,phone (any order).");
        }

        var existingRows = await _db.QueryListAsync("SELECT name FROM suppliers");
        var existingLower = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in existingRows)
        {
            var n = (r.TryGetValue("name", out var v) ? v?.ToString() : null)?.Trim();
            if (!string.IsNullOrEmpty(n)) existingLower.Add(n.ToLowerInvariant());
        }

        var stats = new Dictionary<string, int>
        {
            ["total"] = 0,
            ["valid"] = 0,
            ["invalid"] = 0,
            ["committed"] = 0,
            ["new"] = 0,
            ["existing"] = 0
        };

        var rows = BrandImportService.ReadAllNonEmptyRows(absolutePath, usedDelimiter);
        var toUpsert = new List<(string Name, string? Address, string? Phone)>();
        var openingBalancesByName = new Dictionary<string, (string Name, decimal Amount)>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var nameVal = BrandImportService.GetCell(row, colIdx, "name");
            var addrVal = BrandImportService.GetCell(row, colIdx, "address");
            var phoneVal = BrandImportService.GetCell(row, colIdx, "phone");
            var rawOb = colIdx.ContainsKey("opening_balance") ? (BrandImportService.GetCell(row, colIdx, "opening_balance") ?? "").Trim() : "";

            var data = new Dictionary<string, object?>
            {
                ["name"] = nameVal,
                ["address"] = addrVal,
                ["phone"] = phoneVal,
                ["opening_balance"] = rawOb
            };

            stats["total"]++;

            var (errors, _) = RowIssues(data, existingLower, new HashSet<string>(), false);
            if (errors.Count > 0)
            {
                stats["invalid"]++;
                if (insertValidOnly) continue;
                throw new InvalidOperationException("Import aborted due to validation error.");
            }

            stats["valid"]++;
            var name = (nameVal ?? "").Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var lower = name.ToLowerInvariant();
            if (existingLower.Contains(lower))
            {
                stats["existing"]++;
            }
            else
            {
                stats["new"]++;
                existingLower.Add(lower);
            }

            if (!string.IsNullOrEmpty(rawOb) && decimal.TryParse(rawOb, NumberStyles.Any, CultureInfo.InvariantCulture, out var obAmt) && obAmt > 0)
            {
                openingBalancesByName[lower] = (name, Math.Round(obAmt, 2));
            }

            toUpsert.Add((name, SafeStr(addrVal), SafeStr(phoneVal)));
        }

        if (toUpsert.Count > 0)
        {
            var hasLedgerTable = await _db.HasTableAsync("supplier_ledgers");
            var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            await using var conn = await _db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            foreach (var item in toUpsert)
            {
                var existingId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM suppliers WHERE LOWER(name) = @lower LIMIT 1",
                    new { lower = item.Name.ToLowerInvariant() },
                    tx);

                if (existingId.HasValue)
                {
                    await _db.UpdateAsync("suppliers", existingId.Value, new Dictionary<string, object?>
                    {
                        ["address"] = item.Address,
                        ["phone"] = item.Phone
                    }, "id", conn, tx);
                }
                else
                {
                    await _db.InsertAsync("suppliers", new Dictionary<string, object?>
                    {
                        ["name"] = item.Name,
                        ["address"] = item.Address,
                        ["phone"] = item.Phone,
                        ["expiry_return_policy"] = "before_expiry",
                        ["expiry_return_months"] = 3,
                        ["expiry_return_days"] = 90
                    }, conn, tx);
                }

                stats["committed"]++;
            }

            if (openingBalancesByName.Count > 0 && hasLedgerTable)
            {
                foreach (var obItem in openingBalancesByName.Values)
                {
                    var supId = await conn.ExecuteScalarAsync<long?>(
                        "SELECT id FROM suppliers WHERE name = @name LIMIT 1",
                        new { name = obItem.Name },
                        tx);
                    if (!supId.HasValue) continue;

                    var postedNo = "OB-" + supId.Value.ToString().PadLeft(5, '0');
                    var alreadyExists = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM supplier_ledgers WHERE supplier_id = @sid AND posted_number = @pno",
                        new { sid = supId.Value, pno = postedNo },
                        tx);

                    if (alreadyExists == 0)
                    {
                        await _db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
                        {
                            ["supplier_id"] = supId.Value,
                            ["entry_type"] = "manual",
                            ["is_manual"] = true,
                            ["entry_date"] = todayStr,
                            ["posted_number"] = postedNo,
                            ["invoice_total"] = obItem.Amount,
                            ["total_paid"] = 0m,
                            ["credit_remaining"] = obItem.Amount,
                            ["debited_amount"] = 0m,
                            ["description"] = "Opening Balance (CSV Import)"
                        }, conn, tx);
                    }
                }
            }

            await tx.CommitAsync();
        }

        return stats;
    }

    private (List<string?>? Header, Dictionary<string, int> ColIdx, string UsedDelimiter) ReadAndMapHeader(string path, string delimiter)
    {
        var rows = BrandImportService.ReadAllNonEmptyRows(path, delimiter, maxRows: 1);
        var header = rows.FirstOrDefault();
        var colIdx = header != null ? MapHeaderToCanonical(header) : new Dictionary<string, int>();
        if (colIdx.Count > 0) return (header, colIdx, delimiter);

        var auto = BrandImportService.GuessDelimiterFromSample(path);
        if (auto != delimiter)
        {
            var autoRows = BrandImportService.ReadAllNonEmptyRows(path, auto, maxRows: 1);
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

        foreach (var key in new[] { "name", "address", "phone", "opening_balance" })
        {
            int? idx = null;
            foreach (var alias in Aliases[key])
            {
                var i = lower.IndexOf(alias);
                if (i >= 0)
                {
                    idx = i;
                    break;
                }
            }
            if ((key == "name" || key == "address" || key == "phone") && idx == null)
            {
                return new Dictionary<string, int>();
            }
            if (idx != null) map[key] = idx.Value;
        }

        return map;
    }

    private static (Dictionary<string, string> Errors, Dictionary<string, string> Warnings) RowIssues(
        Dictionary<string, object?> data,
        HashSet<string> existingLower,
        HashSet<string> seenInFile,
        bool strict)
    {
        var errors = new Dictionary<string, string>();
        var warnings = new Dictionary<string, string>();

        var name = (data.TryGetValue("name", out var nv) ? nv?.ToString() : null)?.Trim() ?? "";
        var addr = (data.TryGetValue("address", out var av) ? av?.ToString() : null) ?? "";
        var phone = (data.TryGetValue("phone", out var pv) ? pv?.ToString() : null) ?? "";

        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = "Name is required.";
        }
        else if (name.Length > 255)
        {
            errors["name"] = "Name too long (max 255).";
        }

        var lower = name.ToLowerInvariant();
        if (!string.IsNullOrEmpty(name))
        {
            if (strict && seenInFile.Contains(lower))
            {
                errors["duplicate_in_file"] = "Duplicate supplier name within the file.";
            }
            if (existingLower.Contains(lower))
            {
                warnings["duplicate_db"] = "Name already exists in DB (will update via upsert).";
            }
        }

        if (!string.IsNullOrEmpty(addr) && addr.Length > 255)
        {
            errors["address"] = "Address too long (max 255).";
        }

        if (!string.IsNullOrEmpty(phone) && phone.Length > 50)
        {
            errors["phone"] = "Phone too long (max 50).";
        }
        else if (!string.IsNullOrEmpty(phone) && !Regex.IsMatch(phone, @"^[0-9+\-\s()]{3,}$"))
        {
            warnings["phone_hint"] = "Phone looks unusual; check formatting.";
        }

        return (errors, warnings);
    }

    private static string? SafeStr(string? s)
    {
        if (s == null) return null;
        var trimmed = s.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
