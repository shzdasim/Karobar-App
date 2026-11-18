using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Karobar.Services;

public class CategoryImportService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;
    public const int ChunkSize = 1000;

    private readonly KarobarDb _db;

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ["name", "category", "category_name", "cat", "title", "label"]
    };

    public CategoryImportService(KarobarDb db)
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
        var dir = _db.StoragePath("app/imports/categories");
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
        var primary = _db.StoragePath($"app/imports/categories/{token}.csv");
        if (File.Exists(primary)) return primary;
        return _db.StoragePath($"imports/categories/{token}.csv");
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
                ["message"] = "CSV must contain a header with column: name (any order/alias okay).",
                ["detected_header"] = header,
                ["hint"] = "Accepted: name|category|category_name|title|label"
            };
        }

        var existingRows = await _db.QueryListAsync("SELECT name FROM categories");
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
            var data = new Dictionary<string, object?> { ["name"] = nameVal };

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
            throw new InvalidOperationException("CSV header not recognized (need name).");
        }

        var existingRows = await _db.QueryListAsync("SELECT name FROM categories");
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
        var toUpsert = new List<string>();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var name = (BrandImportService.GetCell(row, colIdx, "name") ?? "").Trim();
            stats["total"]++;

            var (errors, _) = RowIssues(new Dictionary<string, object?> { ["name"] = name }, existingLower, new HashSet<string>(), false);
            if (errors.Count > 0)
            {
                stats["invalid"]++;
                if (insertValidOnly) continue;
                throw new InvalidOperationException("Import aborted due to validation error.");
            }

            stats["valid"]++;
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

            toUpsert.Add(name);
        }

        if (toUpsert.Count > 0)
        {
            await using var conn = await _db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            foreach (var name in toUpsert)
            {
                var exists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM categories WHERE LOWER(name) = @lower",
                    new { lower = name.ToLowerInvariant() },
                    tx);
                if (exists == 0)
                {
                    await _db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = name }, conn, tx);
                }
                stats["committed"]++;
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

        foreach (var alias in Aliases["name"])
        {
            var i = lower.IndexOf(alias);
            if (i >= 0)
            {
                map["name"] = i;
                return map;
            }
        }

        return new Dictionary<string, int>();
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
                errors["duplicate_in_file"] = "Duplicate name within file.";
            }
            if (existingLower.Contains(lower))
            {
                warnings["duplicate_db"] = "Name exists (will upsert).";
            }
        }

        return (errors, warnings);
    }
}
