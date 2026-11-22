using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karobar.Api.Middleware;

public class SanctumAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Sanctum";
    private readonly KarobarDb _db;

    public SanctumAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        KarobarDb db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ExtractBearerToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        if (!await _db.HasTableAsync("personal_access_tokens") || !await _db.HasTableAsync("users"))
        {
            return AuthenticateResult.NoResult();
        }

        Dictionary<string, object?>? tokenRow = null;
        if (token.Contains('|'))
        {
            var parts = token.Split('|', 2);
            if (long.TryParse(parts[0], out var tokenId))
            {
                var row = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM personal_access_tokens WHERE id = @id",
                    new { id = tokenId });

                if (row != null)
                {
                    var expectedHash = ComputeSha256Hex(parts[1]);
                    var storedHash = row.GetValueOrDefault("token")?.ToString() ?? "";
                    if (CryptographicOperations.FixedTimeEquals(
                            Encoding.UTF8.GetBytes(expectedHash.ToLowerInvariant()),
                            Encoding.UTF8.GetBytes(storedHash.ToLowerInvariant())))
                    {
                        tokenRow = row;
                    }
                }
            }
        }
        else
        {
            var hash = ComputeSha256Hex(token);
            tokenRow = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM personal_access_tokens WHERE token = @hash",
                new { hash });
        }

        if (tokenRow == null)
        {
            return AuthenticateResult.Fail("Invalid access token.");
        }

        if (tokenRow.TryGetValue("expires_at", out var expObj) && expObj != null &&
            DateTimeOffset.TryParse(
                expObj.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var expDt) &&
            expDt < DateTimeOffset.UtcNow)
        {
            return AuthenticateResult.Fail("Token has expired.");
        }

        var userId = Convert.ToInt64(tokenRow["tokenable_id"]);
        var user = await _db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id = userId });
        if (user == null)
        {
            return AuthenticateResult.Fail("User not found.");
        }

        var tokenIdVal = Convert.ToInt64(tokenRow["id"]);
        Context.Items["KarobarUserId"] = userId;
        Context.Items["KarobarUser"] = user;
        Context.Items["KarobarTokenId"] = tokenIdVal;
        Context.Items["KarobarTokenRecord"] = tokenRow;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, user.GetValueOrDefault("name")?.ToString() ?? ""),
            new(ClaimTypes.Email, user.GetValueOrDefault("email")?.ToString() ?? "")
        };

        var roleNames = await _db.GetUserRoleNamesAsync(userId);
        foreach (var r in roleNames)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    public static bool VerifyLaravelBcryptPassword(string plainPassword, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;

        // Normalize PHP $2y$ or $2b$ prefix for BCrypt.Net
        var normalizedHash = storedHash;
        if (normalizedHash.StartsWith("$2y$", StringComparison.Ordinal))
        {
            normalizedHash = "$2a$" + normalizedHash.Substring(4);
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(plainPassword, normalizedHash);
        }
        catch
        {
            return false;
        }
    }

    public static string HashLaravelBcryptPassword(string plainPassword)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 10);
        if (hash.StartsWith("$2a$", StringComparison.Ordinal) || hash.StartsWith("$2b$", StringComparison.Ordinal))
        {
            return "$2y$" + hash.Substring(4);
        }
        return hash;
    }

    public static async Task<string> IssuePersonalAccessTokenAsync(
        KarobarDb db,
        long userId,
        string name = "api_token",
        DateTimeOffset? expiresAt = null)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var plainText = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var hashed = ComputeSha256Hex(plainText);

        var tokenId = await db.InsertAsync("personal_access_tokens", new Dictionary<string, object?>
        {
            ["tokenable_type"] = "App\\Models\\User",
            ["tokenable_id"] = userId,
            ["name"] = name,
            ["token"] = hashed,
            ["abilities"] = "[\"*\"]",
            ["expires_at"] = expiresAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return $"{tokenId}|{plainText}";
    }

    private static readonly Regex QueryTokenAllowedPathRegex = new(
        @"^(/print/.*|/sale-invoices/\d+/dosage-labels|/customer-ledger/print|/supplier-ledger/print|/api/reports/[^/]+/pdf|/api/share/pdf|/api/backups/\d+/download|/api/expenses/\d+/receipt|/api/server/(export-database|terminal-config)|/api/(products|brands|categories|customers|suppliers)/(export|import/template))/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private bool IsQueryTokenAllowedForRequest()
    {
        var path = Request.Path.Value ?? "";
        if (!QueryTokenAllowedPathRegex.IsMatch(path)) return false;
        if (HttpMethods.IsGet(Request.Method) || HttpMethods.IsHead(Request.Method)) return true;
        return HttpMethods.IsPost(Request.Method) &&
               path.TrimEnd('/').Equals("/api/server/export-database", StringComparison.OrdinalIgnoreCase);
    }

    private string? ExtractBearerToken()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader.Substring("Bearer ".Length).Trim();
        }

        if (IsQueryTokenAllowedForRequest() &&
            Request.Query.TryGetValue("api_token", out var qToken) &&
            !string.IsNullOrWhiteSpace(qToken))
        {
            return qToken.ToString().Trim();
        }

        return null;
    }

    public static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
