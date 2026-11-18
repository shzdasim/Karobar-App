using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Karobar.Services;

public class CustomerImportService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;
    public const int ChunkSize = 1000;

    private readonly KarobarDb _db;

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ["name", "customer", "customer_name", "full_name"],
        ["email"] = ["email", "e-mail", "mail"],
        ["phone"] = ["phone", "mobile", "contact", "phone_number", "tel", "telephone"],
        ["address"] = ["address", "addr", "location", "street", "addr1"],
        ["opening_balance"] = ["opening_balance", "opening", "balance", "initial_balance"]
    };

    public CustomerImportService(KarobarDb db)
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
        var dir = _db.StoragePath("app/imports/customers");
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
        var primary = _db.StoragePath($"app/imports/customers/{token}.csv");
        if (File.Exists(primary)) return primary;
        return _db.StoragePath($"imports/customers/{token}.csv");
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
                ["message"] = "CSV must contain a header with column: name (email, phone, address optional).",
                ["detected_header"] = header,
                ["hint"] = "Accepted aliases: name|customer|customer_name; email|mail; phone|mobile; address|addr"
            };
        }

        var existingRows = await _db.QueryListAsync("SELECT id, name, email FROM customers");
        var nameToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var emailToOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in existingRows)
        {
            var id = r.TryGetValue("id", out var idv) && idv != null ? Convert.ToInt64(idv) : 0;
            var n = (r.TryGetValue("name", out var nv) ? nv?.ToString() : null)?.Trim() ?? "";
            var e = (r.TryGetValue("email", out var ev) ? ev?.ToString() : null)?.Trim() ?? "";
            if (!string.IsNullOrEmpty(n)) nameToId[n.ToLowerInvariant()] = id;
            if (!string.IsNullOrEmpty(e)) emailToOwner[e.ToLowerInvariant()] = n.ToLowerInvariant();
        }

        var rows = BrandImportService.ReadAllNonEmptyRows(absolutePath, usedDelimiter);
        var seenName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEmail = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int total = 0, valid = 0, invalid = 0;
        var invalidSamples = new List<Dictionary<string, object?>>();
        var validSamples = new List<Dictionary<string, object?>>();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            int rowNo = i + 1;

            var nameVal = BrandImportService.GetCell(row, colIdx, "name");
            var emailVal = colIdx.ContainsKey("email") ? BrandImportService.GetCell(row, colIdx, "email") : null;
            var phoneVal = colIdx.ContainsKey("phone") ? BrandImportService.GetCell(row, colIdx, "phone") : null;
            var addrVal = colIdx.ContainsKey("address") ? BrandImportService.GetCell(row, colIdx, "address") : null;

            var data = new Dictionary<string, object?>
            {
                ["name"] = nameVal,
                ["email"] = emailVal,
                ["phone"] = phoneVal,
                ["address"] = addrVal
            };

            var (errors, warnings) = RowIssues(data, nameToId, emailToOwner, seenName, seenEmail, true);
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
                var ln = (nameVal ?? "").Trim().ToLowerInvariant();
                var le = (emailVal ?? "").Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(ln)) seenName.Add(ln);
                if (!string.IsNullOrEmpty(le)) seenEmail.Add(le);
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
            throw new InvalidOperationException("CSV header not recognized (need name).");
        }

        var existingRows = await _db.QueryListAsync("SELECT id, name, email FROM customers");
        var nameToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var emailToOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in existingRows)
        {
            var id = r.TryGetValue("id", out var idv) && idv != null ? Convert.ToInt64(idv) : 0;
            var n = (r.TryGetValue("name", out var nv) ? nv?.ToString() : null)?.Trim() ?? "";
            var e = (r.TryGetValue("email", out var ev) ? ev?.ToString() : null)?.Trim() ?? "";
            if (!string.IsNullOrEmpty(n)) nameToId[n.ToLowerInvariant()] = id;
            if (!string.IsNullOrEmpty(e)) emailToOwner[e.ToLowerInvariant()] = n.ToLowerInvariant();
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
        var toUpsert = new List<(string Name, string? Email, string? Phone, string? Address)>();
        var openingBalancesByName = new Dictionary<string, (string Name, decimal Amount)>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var name = (BrandImportService.GetCell(row, colIdx, "name") ?? "").Trim();
            var email = colIdx.ContainsKey("email") ? (BrandImportService.GetCell(row, colIdx, "email") ?? "").Trim() : "";
            var phone = colIdx.ContainsKey("phone") ? (BrandImportService.GetCell(row, colIdx, "phone") ?? "").Trim() : "";
            var address = colIdx.ContainsKey("address") ? (BrandImportService.GetCell(row, colIdx, "address") ?? "").Trim() : "";
            var rawOb = colIdx.ContainsKey("opening_balance") ? (BrandImportService.GetCell(row, colIdx, "opening_balance") ?? "").Trim() : "";

            stats["total"]++;

            var data = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["email"] = email,
                ["phone"] = phone,
                ["address"] = address
            };

            var (errors, _) = RowIssues(data, nameToId, emailToOwner, new HashSet<string>(), new HashSet<string>(), false);
            if (errors.Count > 0)
            {
                stats["invalid"]++;
                if (insertValidOnly) continue;
                throw new InvalidOperationException("Import aborted due to validation error.");
            }

            stats["valid"]++;
            var ln = name.ToLowerInvariant();
            if (nameToId.ContainsKey(ln))
            {
                stats["existing"]++;
            }
            else
            {
                stats["new"]++;
                nameToId[ln] = -1;
            }

            if (!string.IsNullOrEmpty(rawOb) && decimal.TryParse(rawOb, NumberStyles.Any, CultureInfo.InvariantCulture, out var obAmt) && obAmt > 0)
            {
                openingBalancesByName[ln] = (name, Math.Round(obAmt, 2));
            }

            toUpsert.Add((
                name,
                !string.IsNullOrEmpty(email) ? email : null,
                !string.IsNullOrEmpty(phone) ? phone : null,
                !string.IsNullOrEmpty(address) ? address : null
            ));
        }

        if (toUpsert.Count > 0)
        {
            var hasLedgerTable = await _db.HasTableAsync("customer_ledgers");
            var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            await using var conn = await _db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            foreach (var item in toUpsert)
            {
                var existingId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM customers WHERE LOWER(name) = @lower LIMIT 1",
                    new { lower = item.Name.ToLowerInvariant() },
                    tx);

                if (existingId.HasValue)
                {
                    await _db.UpdateAsync("customers", existingId.Value, new Dictionary<string, object?>
                    {
                        ["email"] = item.Email,
                        ["phone"] = item.Phone,
                        ["address"] = item.Address
                    }, "id", conn, tx);
                }
                else
                {
                    await _db.InsertAsync("customers", new Dictionary<string, object?>
                    {
                        ["name"] = item.Name,
                        ["email"] = item.Email,
                        ["phone"] = item.Phone,
                        ["address"] = item.Address
                    }, conn, tx);
                }

                stats["committed"]++;
            }

            if (openingBalancesByName.Count > 0 && hasLedgerTable)
            {
                foreach (var obItem in openingBalancesByName.Values)
                {
                    var custId = await conn.ExecuteScalarAsync<long?>(
                        "SELECT id FROM customers WHERE name = @name LIMIT 1",
                        new { name = obItem.Name },
                        tx);
                    if (!custId.HasValue) continue;

                    var postedNo = "OB-" + custId.Value.ToString().PadLeft(5, '0');
                    var alreadyExists = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM customer_ledgers WHERE customer_id = @cid AND posted_number = @pno",
                        new { cid = custId.Value, pno = postedNo },
                        tx);

                    if (alreadyExists == 0)
                    {
                        await _db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
                        {
                            ["customer_id"] = custId.Value,
                            ["entry_type"] = "manual",
                            ["is_manual"] = true,
                            ["entry_date"] = todayStr,
                            ["posted_number"] = postedNo,
                            ["invoice_total"] = obItem.Amount,
                            ["total_received"] = 0m,
                            ["balance_remaining"] = obItem.Amount,
                            ["credited_amount"] = 0m,
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

        foreach (var key in new[] { "name", "email", "phone", "address", "opening_balance" })
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
            if (key == "name" && idx == null) return new Dictionary<string, int>();
            if (idx != null) map[key] = idx.Value;
        }

        return map;
    }

    private static (Dictionary<string, string> Errors, Dictionary<string, string> Warnings) RowIssues(
        Dictionary<string, object?> data,
        Dictionary<string, long> nameToId,
        Dictionary<string, string> emailToOwner,
        HashSet<string> seenName,
        HashSet<string> seenEmail,
        bool strict)
    {
        var errors = new Dictionary<string, string>();
        var warnings = new Dictionary<string, string>();

        var name = (data.TryGetValue("name", out var nv) ? nv?.ToString() : null)?.Trim() ?? "";
        var email = (data.TryGetValue("email", out var ev) ? ev?.ToString() : null)?.Trim() ?? "";
        var phone = (data.TryGetValue("phone", out var pv) ? pv?.ToString() : null)?.Trim() ?? "";
        var address = (data.TryGetValue("address", out var av) ? av?.ToString() : null)?.Trim() ?? "";

        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = "Name is required.";
        }
        else if (name.Length > 255)
        {
            errors["name"] = "Name too long (max 255).";
        }

        if (!string.IsNullOrEmpty(email))
        {
            if (email.Length > 255)
            {
                errors["email"] = "Email too long (max 255).";
            }
            else if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                errors["email"] = "Invalid email format.";
            }
        }

        if (!string.IsNullOrEmpty(phone) && phone.Length > 100)
        {
            warnings["phone"] = "Phone is quite long.";
        }
        if (!string.IsNullOrEmpty(address) && address.Length > 255)
        {
            warnings["address"] = "Address too long (will be truncated on save).";
        }

        var ln = name.ToLowerInvariant();
        var le = email.ToLowerInvariant();

        if (strict)
        {
            if (!string.IsNullOrEmpty(ln) && seenName.Contains(ln))
            {
                errors["duplicate_name_in_file"] = "Duplicate name within file.";
            }
            if (!string.IsNullOrEmpty(le) && seenEmail.Contains(le))
            {
                errors["duplicate_email_in_file"] = "Duplicate email within file.";
            }
        }

        if (!string.IsNullOrEmpty(le) && emailToOwner.TryGetValue(le, out var ownerName))
        {
            if (!string.Equals(ownerName, ln, StringComparison.OrdinalIgnoreCase))
            {
                errors["email_conflict"] = "Email already used by another customer.";
            }
        }

        if (!string.IsNullOrEmpty(ln) && nameToId.ContainsKey(ln))
        {
            warnings["duplicate_name_db"] = "Name exists (will upsert).";
        }

        return (errors, warnings);
    }
}
