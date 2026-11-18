using System.Data;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Karobar.Services;

public class BrandImportService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;
    public const int ChunkSize = 1000;

    private readonly KarobarDb _db;
    private static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ["name", "brand", "brand_name", "label", "company"],
        ["image"] = ["image", "image_path", "image_url", "logo", "logo_url", "picture", "photo"]
    };

    public BrandImportService(KarobarDb db)
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
        var dir = _db.StoragePath("app/imports/brands");
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
        var primary = _db.StoragePath($"app/imports/brands/{token}.csv");
        if (File.Exists(primary)) return primary;
        var fallback = _db.StoragePath($"imports/brands/{token}.csv");
        return fallback;
    }

    public async Task<Dictionary<string, object?>> ValidateCsvAsync(string absolutePath, string delimiter = ",")
    {
        delimiter = NormalizeDelimiter(delimiter);
        var (header, colIdx, usedDelimiter) = ReadAndMapHeader(absolutePath, delimiter);

        if (header == null || header.Count == 0 || colIdx.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["message"] = "CSV must contain header columns: name[, image] (any order).",
                ["detected_header"] = header,
                ["hint"] = "Accepted: name|brand|brand_name and image|image_url|logo."
            };
        }

        var existingRows = await _db.QueryListAsync("SELECT name FROM brands");
        var existingLower = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in existingRows)
        {
            var n = (r.TryGetValue("name", out var v) ? v?.ToString() : null)?.Trim();
            if (!string.IsNullOrEmpty(n)) existingLower.Add(n.ToLowerInvariant());
        }

        var rows = ReadAllNonEmptyRows(absolutePath, usedDelimiter);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int total = 0, valid = 0, invalid = 0;
        var invalidSamples = new List<Dictionary<string, object?>>();
        var validSamples = new List<Dictionary<string, object?>>();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            int rowNo = i + 1;

            var nameVal = GetCell(row, colIdx, "name");
            var imageVal = colIdx.ContainsKey("image") ? GetCell(row, colIdx, "image") : null;

            var data = new Dictionary<string, object?>
            {
                ["name"] = nameVal,
                ["image"] = imageVal
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
        delimiter = NormalizeDelimiter(delimiter);
        var (_, colIdx, usedDelimiter) = ReadAndMapHeader(absolutePath, delimiter);
        if (colIdx.Count == 0)
        {
            throw new InvalidOperationException("CSV header not recognized. Expected: name[, image].");
        }

        var existingRows = await _db.QueryListAsync("SELECT id, name, image FROM brands");
        var existingLower = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var existingIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in existingRows)
        {
            var n = (r.TryGetValue("name", out var nv) ? nv?.ToString() : null)?.Trim() ?? "";
            if (!string.IsNullOrEmpty(n))
            {
                var lower = n.ToLowerInvariant();
                existingLower.Add(lower);
                existingImages[lower] = (r.TryGetValue("image", out var iv) ? iv?.ToString() : null) ?? "";
                if (r.TryGetValue("id", out var idv) && idv != null)
                {
                    existingIds[lower] = Convert.ToInt64(idv);
                }
            }
        }

        var stats = new Dictionary<string, int>
        {
            ["total"] = 0,
            ["valid"] = 0,
            ["invalid"] = 0,
            ["committed"] = 0,
            ["new"] = 0,
            ["existing"] = 0,
            ["images_downloaded"] = 0,
            ["image_failures"] = 0
        };

        var rows = ReadAllNonEmptyRows(absolutePath, usedDelimiter);
        var toUpsert = new List<(string Name, string? Image)>();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var name = (GetCell(row, colIdx, "name") ?? "").Trim();
            var imageRaw = colIdx.ContainsKey("image") ? (GetCell(row, colIdx, "image") ?? "").Trim() : "";

            stats["total"]++;

            var data = new Dictionary<string, object?> { ["name"] = name, ["image"] = imageRaw };
            var (errors, _) = RowIssues(data, existingLower, new HashSet<string>(), false);
            if (errors.Count > 0)
            {
                stats["invalid"]++;
                if (insertValidOnly) continue;
                throw new InvalidOperationException("Import aborted due to validation error.");
            }

            stats["valid"]++;
            var lower = name.ToLowerInvariant();

            var imageFinal = existingImages.TryGetValue(lower, out var prevImg) ? prevImg : "";
            if (!string.IsNullOrEmpty(imageRaw))
            {
                if (IsHttpUrl(imageRaw))
                {
                    var saved = await DownloadImageFromUrlAsync(imageRaw);
                    if (!string.IsNullOrEmpty(saved))
                    {
                        imageFinal = saved;
                        stats["images_downloaded"]++;
                    }
                    else
                    {
                        stats["image_failures"]++;
                    }
                }
                else
                {
                    imageFinal = imageRaw;
                }
            }

            if (existingLower.Contains(lower))
            {
                stats["existing"]++;
            }
            else
            {
                stats["new"]++;
                existingLower.Add(lower);
            }

            toUpsert.Add((name, !string.IsNullOrEmpty(imageFinal) ? imageFinal : null));
        }

        if (toUpsert.Count > 0)
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await using var conn = await _db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            foreach (var item in toUpsert)
            {
                var lower = item.Name.ToLowerInvariant();
                if (existingIds.TryGetValue(lower, out var id))
                {
                    await conn.ExecuteAsync(
                        "UPDATE brands SET image = @image, updated_at = @now WHERE id = @id",
                        new { image = item.Image, now = nowStr, id },
                        tx);
                }
                else
                {
                    var existingId = await conn.ExecuteScalarAsync<long?>(
                        "SELECT id FROM brands WHERE LOWER(name) = @lower LIMIT 1",
                        new { lower },
                        tx);
                    if (existingId.HasValue)
                    {
                        await conn.ExecuteAsync(
                            "UPDATE brands SET image = @image, updated_at = @now WHERE id = @id",
                            new { image = item.Image, now = nowStr, id = existingId.Value },
                            tx);
                        existingIds[lower] = existingId.Value;
                    }
                    else
                    {
                        var newId = await _db.InsertAsync("brands", new Dictionary<string, object?>
                        {
                            ["name"] = item.Name,
                            ["image"] = item.Image,
                            ["expiry_return_policy"] = "before_expiry",
                            ["expiry_return_months"] = 3,
                            ["expiry_return_days"] = 90
                        }, conn, tx);
                        existingIds[lower] = newId;
                    }
                }
                stats["committed"]++;
            }

            await tx.CommitAsync();
        }

        return stats;
    }

    private (Dictionary<string, string> Errors, Dictionary<string, string> Warnings) RowIssues(
        Dictionary<string, object?> data,
        HashSet<string> existingLower,
        HashSet<string> seenInFile,
        bool strict)
    {
        var errors = new Dictionary<string, string>();
        var warnings = new Dictionary<string, string>();

        var name = (data.TryGetValue("name", out var nv) ? nv?.ToString() : null)?.Trim() ?? "";
        var image = (data.TryGetValue("image", out var iv) ? iv?.ToString() : null) ?? "";

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

        if (!string.IsNullOrEmpty(image) && image.Length > 255)
        {
            errors["image"] = "Image path/url too long.";
        }
        if (!string.IsNullOrEmpty(image) && !IsHttpUrl(image) && !image.StartsWith("brands/", StringComparison.Ordinal))
        {
            warnings["image_hint"] = "Image will be treated as a storage path or ignored.";
        }

        return (errors, warnings);
    }

    private static bool IsHttpUrl(string s)
    {
        return Regex.IsMatch(s, @"^https?://", RegexOptions.IgnoreCase);
    }

    private static bool IsSafePublicUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host.Trim('[', ']').ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".local") || host.EndsWith(".internal")) return false;

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0) return false;
            foreach (var ip in addresses)
            {
                if (IPAddress.IsLoopback(ip)) return false;
                var bytes = ip.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    if (bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0) return false;
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                    if (bytes[0] == 192 && bytes[1] == 168) return false;
                    if (bytes[0] == 169 && bytes[1] == 254) return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> DownloadImageFromUrlAsync(string url)
    {
        try
        {
            if (!IsSafePublicUrl(url)) return null;
            using var resp = await SharedHttp.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 5 * 1024 * 1024) return null;

            var mime = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (!mime.StartsWith("image/")) return null;

            var ext = mime.Contains("jpeg") ? "jpg"
                : mime.Contains("png") ? "png"
                : mime.Contains("webp") ? "webp"
                : mime.Contains("gif") ? "gif"
                : "jpg";

            var dir = _db.StoragePath("app/public/brands/imports");
            Directory.CreateDirectory(dir);
            var filename = $"{Guid.NewGuid()}.{ext}";
            var fullPath = Path.Combine(dir, filename);
            await File.WriteAllBytesAsync(fullPath, bytes);

            return $"brands/imports/{filename}";
        }
        catch
        {
            return null;
        }
    }

    internal static string NormalizeDelimiter(string delimiter)
    {
        return delimiter == "\\t" ? "\t" : (string.IsNullOrEmpty(delimiter) ? "," : delimiter);
    }

    private (List<string?>? Header, Dictionary<string, int> ColIdx, string UsedDelimiter) ReadAndMapHeader(string path, string delimiter)
    {
        var rows = ReadAllNonEmptyRows(path, delimiter, maxRows: 1);
        var header = rows.FirstOrDefault();
        var colIdx = header != null ? MapHeaderToCanonical(header) : new Dictionary<string, int>();
        if (colIdx.Count > 0) return (header, colIdx, delimiter);

        var auto = GuessDelimiterFromSample(path);
        if (auto != delimiter)
        {
            var autoRows = ReadAllNonEmptyRows(path, auto, maxRows: 1);
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

        foreach (var key in new[] { "name", "image" })
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

    internal static string GuessDelimiterFromSample(string path)
    {
        try
        {
            using var sr = new StreamReader(path, Encoding.UTF8, true);
            var line = sr.ReadLine() ?? "";
            var candidates = new[] { ",", ";", "\t", "|" };
            var best = ",";
            var bestCount = -1;
            foreach (var c in candidates)
            {
                var count = line.Split(c).Length - 1;
                if (count > bestCount)
                {
                    best = c;
                    bestCount = count;
                }
            }
            return best;
        }
        catch
        {
            return ",";
        }
    }

    internal static List<List<string?>> ReadAllNonEmptyRows(string path, string delimiter, int maxRows = int.MaxValue, bool sanitizeFormulas = true)
    {
        var result = new List<List<string?>>();
        if (!File.Exists(path)) return result;

        var delimChar = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        var content = File.ReadAllText(path, Encoding.UTF8);
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        var rawRows = ParseCsvContent(content, delimChar);
        foreach (var rawRow in rawRows)
        {
            var normalized = NormalizeCsvRow(rawRow, sanitizeFormulas);
            if (normalized.Count == 0 || (normalized.Count == 1 && normalized[0] == null))
            {
                continue;
            }
            result.Add(normalized);
            if (result.Count >= maxRows) break;
        }

        return result;
    }

    private static List<string?> NormalizeCsvRow(List<string> row, bool sanitizeFormulas)
    {
        var outList = new List<string?>(row.Count);
        for (int i = 0; i < row.Count; i++)
        {
            var s = (row[i] ?? "").Trim();
            if (i == 0) s = s.TrimStart('\uFEFF');
            if (sanitizeFormulas && !string.IsNullOrEmpty(s) && Regex.IsMatch(s, @"^[=+\-@]") && !decimal.TryParse(s, out _))
            {
                s = "'" + s;
            }
            outList.Add(string.IsNullOrEmpty(s) ? null : s);
        }

        while (outList.Count > 0 && outList[^1] == null)
        {
            outList.RemoveAt(outList.Count - 1);
        }

        return outList;
    }

    private static List<List<string>> ParseCsvContent(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        currentCell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentCell.Append(c);
                }
            }
            else
            {
                if (c == '"' && currentCell.Length == 0)
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                }
                else
                {
                    currentCell.Append(c);
                }
            }
        }

        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    internal static string? GetCell(List<string?> row, Dictionary<string, int> colIdx, string key)
    {
        if (!colIdx.TryGetValue(key, out var idx)) return null;
        if (idx < 0 || idx >= row.Count) return null;
        return row[idx];
    }
}
