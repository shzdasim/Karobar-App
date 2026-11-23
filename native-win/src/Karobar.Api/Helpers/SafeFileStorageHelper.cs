using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Karobar.Api.Helpers;

public static class SafeFileStorageHelper
{
    public const long DefaultMaxUploadBytes = 5L * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> AllowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/pjpeg", "image/png", "image/webp"
    };

    public static async Task<(bool Ok, string? RelativePath, string? Error)> SaveValidatedUploadAsync(
        KarobarDb db,
        IFormFile? file,
        string subfolder,
        bool allowPdf,
        long maxBytes = DefaultMaxUploadBytes)
    {
        if (file == null || file.Length <= 0)
        {
            return (false, null, "File must be between 1 byte and 5 MB.");
        }

        if (file.Length > maxBytes)
        {
            return (false, null, $"File must not exceed {Math.Max(1, maxBytes / (1024 * 1024))} MB.");
        }

        var cleanSubfolder = (subfolder ?? "").Trim().Trim('/', '\\');
        if (string.IsNullOrWhiteSpace(cleanSubfolder) ||
            cleanSubfolder.Contains("..", StringComparison.Ordinal) ||
            cleanSubfolder.Contains('\\') ||
            cleanSubfolder.Contains(':'))
        {
            return (false, null, "Invalid storage directory.");
        }

        var ext = (Path.GetExtension(file.FileName) ?? "").Trim().ToLowerInvariant();
        var extAllowed = AllowedImageExtensions.Contains(ext) || (allowPdf && ext == ".pdf");
        if (!extAllowed)
        {
            return (false, null, allowPdf
                ? "Only JPG, JPEG, PNG, WebP, and PDF files are allowed."
                : "Only JPG, JPEG, PNG, and WebP image files are allowed.");
        }

        var rawContentType = (file.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(rawContentType) && rawContentType != "application/octet-stream")
        {
            var mimeMatchesExt = ext switch
            {
                ".jpg" or ".jpeg" => rawContentType is "image/jpeg" or "image/jpg" or "image/pjpeg",
                ".png" => rawContentType == "image/png",
                ".webp" => rawContentType == "image/webp",
                ".pdf" => allowPdf && rawContentType == "application/pdf",
                _ => false
            };

            if (!mimeMatchesExt)
            {
                var mimeAllowed = AllowedImageMimeTypes.Contains(rawContentType) || (allowPdf && rawContentType == "application/pdf");
                return (false, null, mimeAllowed
                    ? "File content type does not match its extension."
                    : "File content type is not an allowed format.");
            }
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var ms = new MemoryStream((int)Math.Min(file.Length, maxBytes));
            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                totalRead += read;
                if (totalRead > maxBytes)
                {
                    return (false, null, $"File must not exceed {Math.Max(1, maxBytes / (1024 * 1024))} MB.");
                }
                ms.Write(buffer, 0, read);
            }

            if (totalRead <= 0)
            {
                return (false, null, "File must be between 1 byte and 5 MB.");
            }

            bytes = ms.ToArray();
        }

        bool isPng = bytes.Length >= 8 &&
                     bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                     bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

        bool isJpeg = bytes.Length >= 3 &&
                      bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

        bool isWebp = bytes.Length >= 12 &&
                      bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
                      bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';

        bool isPdf = allowPdf &&
                     bytes.Length >= 5 &&
                     bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F' && bytes[4] == (byte)'-';

        string? safeExt = ext switch
        {
            ".png" when isPng => ".png",
            ".jpg" or ".jpeg" when isJpeg => ".jpg",
            ".webp" when isWebp => ".webp",
            ".pdf" when isPdf => ".pdf",
            _ => null
        };

        if (safeExt == null)
        {
            return (false, null, allowPdf
                ? "Uploaded file content does not match a valid PNG, JPEG, WebP, or PDF signature."
                : "Uploaded file content does not match a valid PNG, JPEG, or WebP image signature.");
        }

        var publicRoot = Path.GetFullPath(db.StoragePath("app/public"));
        var publicRootPrefix = publicRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var targetDir = Path.GetFullPath(Path.Combine(publicRoot, cleanSubfolder.Replace('/', Path.DirectorySeparatorChar)));

        if (!targetDir.StartsWith(publicRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "Invalid storage directory.");
        }

        Directory.CreateDirectory(targetDir);
        if (HasReparsePointInChain(targetDir, publicRoot))
        {
            return (false, null, "Unsafe storage directory symlink detected.");
        }

        var fileName = $"{Guid.NewGuid():N}{safeExt}";
        var fullPath = Path.GetFullPath(Path.Combine(targetDir, fileName));
        var targetDirPrefix = targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(targetDirPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "Invalid storage target path.");
        }

        await File.WriteAllBytesAsync(fullPath, bytes);
        return (true, $"{cleanSubfolder}/{fileName}", null);
    }

    public static bool TryResolveContainedStoragePath(
        KarobarDb db,
        string? relativePath,
        string allowedPrefix,
        out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(allowedPrefix))
        {
            return false;
        }

        if (relativePath.IndexOf('\0') >= 0)
        {
            return false;
        }

        var raw = relativePath.Trim();
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains('\\') ||
            raw.Contains(':') ||
            raw.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(raw))
        {
            return false;
        }

        var cleanPrefix = allowedPrefix.Trim().Trim('/');
        if (string.IsNullOrEmpty(cleanPrefix) || cleanPrefix.Contains("..", StringComparison.Ordinal) || cleanPrefix.Contains('\\'))
        {
            return false;
        }

        var cleanRel = raw.TrimStart('/');
        if (!cleanRel.StartsWith(cleanPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = cleanRel.Split('/', StringSplitOptions.None);
        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg) || seg == "." || seg == "..")
            {
                return false;
            }
        }

        var publicRoot = Path.GetFullPath(db.StoragePath("app/public"));
        var prefixDir = Path.GetFullPath(Path.Combine(publicRoot, cleanPrefix.Replace('/', Path.DirectorySeparatorChar)));
        var prefixWithSep = prefixDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(publicRoot, cleanRel.Replace('/', Path.DirectorySeparatorChar)));

        if (!candidate.StartsWith(prefixWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Directory.Exists(candidate))
        {
            return false;
        }

        if (File.Exists(candidate))
        {
            try
            {
                if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        if (HasReparsePointInChain(Path.GetDirectoryName(candidate), publicRoot))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public static bool TryDeleteContainedStorageFile(KarobarDb db, string? relativePath, string allowedPrefix)
    {
        if (!TryResolveContainedStoragePath(db, relativePath, allowedPrefix, out var fullPath))
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            File.Delete(fullPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetSafeReceiptMimeType(string fullPath, out string contentType)
    {
        contentType = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        var ext = (Path.GetExtension(fullPath) ?? "").Trim().ToLowerInvariant();
        switch (ext)
        {
            case ".jpg":
            case ".jpeg":
                contentType = "image/jpeg";
                return true;
            case ".png":
                contentType = "image/png";
                return true;
            case ".webp":
                contentType = "image/webp";
                return true;
            case ".pdf":
                contentType = "application/pdf";
                return true;
            default:
                return false;
        }
    }

    private static bool HasReparsePointInChain(string? directoryPath, string stopAtRoot)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return false;

        try
        {
            var rootFull = Path.GetFullPath(stopAtRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            while (!string.IsNullOrEmpty(current) && current.Length >= rootFull.Length)
            {
                if (Directory.Exists(current))
                {
                    var attrs = File.GetAttributes(current);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }

                if (string.Equals(current, rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                {
                    break;
                }

                current = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch
        {
            return true;
        }

        return false;
    }
}
