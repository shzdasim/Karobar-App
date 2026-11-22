using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Api.Helpers;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Printing;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Api.Controllers;

[AllowAnonymous]
public class WebRoutesController : ApiControllerBase
{
    private readonly PrintTemplateRenderer _renderer;
    private readonly IAntiforgery? _antiforgery;

    public WebRoutesController(
        KarobarDb db,
        PrintTemplateRenderer? renderer = null,
        IAntiforgery? antiforgery = null) : base(db)
    {
        _renderer = renderer ?? new PrintTemplateRenderer();
        _antiforgery = antiforgery;
    }

    [HttpGet("/password/reset/{token}/{email}")]
    public IActionResult PasswordResetRedirect(string token, string email)
    {
        return Redirect($"/reset-password/{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(email)}");
    }

    [HttpGet("/terms-of-service")]
    public IActionResult TermsOfService()
    {
        var legalText = ReadLegalDocumentText(
            "TermsAndConditions.txt",
            "This End-User License Agreement governs your use of Karobar-App ERP.");
        var html = BuildLegalHtml("Terms and Conditions (End-User License Agreement)", legalText);
        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpGet("/privacy-policy")]
    public IActionResult PrivacyPolicy()
    {
        var legalText = ReadLegalDocumentText(
            "PrivacyPolicy.txt",
            "Karobar-App ERP stores your store, inventory, and financial data locally on your machine.");
        var html = BuildLegalHtml("Privacy Policy & Data Handling Agreement", legalText);
        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpGet("/server-launch")]
    public IActionResult ServerLaunchForm()
    {
        var antiforgery = _antiforgery ?? HttpContext?.RequestServices.GetService<IAntiforgery>();
        var hiddenTokenField = "";
        if (antiforgery != null && HttpContext != null)
        {
            var tokens = antiforgery.GetAndStoreTokens(HttpContext);
            if (!string.IsNullOrEmpty(tokens.RequestToken))
            {
                hiddenTokenField =
                    $"\n    <input type=\"hidden\" name=\"{WebUtility.HtmlEncode(tokens.FormFieldName)}\" value=\"{WebUtility.HtmlEncode(tokens.RequestToken)}\" />";
            }
        }

        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <title>Karobar Server Control Center</title>
</head>
<body style=""background:#0f172a;color:#f8fafc;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;"">
  <form method=""POST"" action=""/server-launch"" style=""background:#1e293b;padding:28px;border-radius:12px;width:360px;box-shadow:0 10px 25px rgba(0,0,0,0.4);"">{hiddenTokenField}
    <h2 style=""margin:0 0 8px;font-size:18px;"">Karobar Server Control Center</h2>
    <p style=""color:#94a3b8;font-size:13px;margin:0 0 16px;"">Enter the server launch key from <code>storage/app/server-launch.key</code>.</p>
    <input type=""password"" name=""key"" required autocomplete=""off"" placeholder=""Server launch key"" style=""width:100%;box-sizing:border-box;padding:10px 12px;border-radius:8px;border:1px solid #334155;background:#0f172a;color:#f8fafc;margin-bottom:14px;"" />
    <button type=""submit"" style=""width:100%;padding:10px 12px;border-radius:8px;border:none;background:#2563eb;color:#fff;font-weight:600;cursor:pointer;"">Launch Dashboard</button>
  </form>
</body>
</html>";
        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpPost("/server-launch")]
    [EnableRateLimiting("throttle_10_1")]
    public async Task<IActionResult> ServerLaunchSubmit()
    {
        var antiforgery = _antiforgery ?? HttpContext?.RequestServices.GetService<IAntiforgery>();
        if (antiforgery == null || HttpContext == null)
        {
            return BadRequest("Anti-forgery validation is unavailable.");
        }

        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest("Invalid or missing anti-forgery token.");
        }

        var body = await ReadJsonBodyAsync();
        var providedKey = (GetString(body, "key") ?? "").Trim();
        var keyFile = Db.StoragePath("app/server-launch.key");
        var storedKey = System.IO.File.Exists(keyFile) ? (await System.IO.File.ReadAllTextAsync(keyFile)).Trim() : "";

        bool keyValid = !string.IsNullOrEmpty(storedKey) &&
                        !string.IsNullOrEmpty(providedKey) &&
                        CryptographicOperations.FixedTimeEquals(
                            Encoding.UTF8.GetBytes(storedKey),
                            Encoding.UTF8.GetBytes(providedKey));

        if (!keyValid)
        {
            return Redirect("/server-dashboard");
        }

        var adminRow = await Db.QuerySingleOrDefaultAsync(@"
            SELECT u.*
            FROM users u
            INNER JOIN model_has_roles mhr ON mhr.model_id = u.id
            INNER JOIN roles r ON r.id = mhr.role_id
            WHERE u.status = 'active' AND LOWER(r.name) IN ('admin', 'super-admin', 'super admin')
            ORDER BY u.id ASC
            LIMIT 1");

        if (adminRow == null)
        {
            return Redirect("/");
        }

        var adminId = Convert.ToInt64(adminRow["id"]);
        var token = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(Db, adminId, "server-control-center", DateTimeOffset.UtcNow.AddHours(24));
        var roles = await Db.GetUserRoleNamesAsync(adminId);

        var userJson = JsonSerializer.Serialize(new
        {
            id = adminId,
            name = adminRow.GetValueOrDefault("name"),
            email = adminRow.GetValueOrDefault("email"),
            roles = roles.Count > 0 ? roles : new List<string> { "Admin" }
        });
        var tokenJson = JsonSerializer.Serialize(token);

        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <title>Launching Karobar Server Control Center...</title>
</head>
<body style=""background:#0f172a;color:#f8fafc;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;"">
  <div style=""text-align:center;"">
    <h2 style=""margin-bottom:8px;"">Opening Karobar Server &amp; Terminals Dashboard…</h2>
    <p style=""color:#94a3b8;font-size:13px;"">Initializing LAN Sync Hub &amp; Troubleshooter…</p>
  </div>
  <script>
    try {{
      localStorage.setItem('token', {tokenJson});
      localStorage.setItem('user', JSON.stringify({userJson}));
      localStorage.setItem('karobar_terminal_mode', 'server');
      localStorage.setItem('karobar_terminal_name', 'Store Server');
      sessionStorage.setItem('karobar_just_logged_in', '1');
    }} catch (e) {{}}
    window.location.replace('/server-dashboard');
  </script>
</body>
</html>";

        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpGet("/print/thermal-preview/{template}")]
    public async Task<IActionResult> ThermalPreview(string template)
    {
        if (!PrintTemplateRenderer.ValidInvoiceTemplates.Contains(template, StringComparer.OrdinalIgnoreCase))
        {
            return NotFound("Template not found");
        }

        var setting = await PrintViewModelMapper.LoadPrintSettingAsync(Db);
        var html = _renderer.RenderThermalPreview(template, setting, PrintViewModelMapper.QueryToDict(Request.Query));
        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpGet("/print/a4-preview/{template}")]
    public async Task<IActionResult> A4Preview(string template)
    {
        if (!PrintTemplateRenderer.ValidInvoiceTemplates.Contains(template, StringComparer.OrdinalIgnoreCase))
        {
            return NotFound("Template not found");
        }

        var setting = await PrintViewModelMapper.LoadPrintSettingAsync(Db);
        var html = _renderer.RenderA4Preview(template, setting, PrintViewModelMapper.QueryToDict(Request.Query));
        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpGet("/print/sticker-preview/{template?}")]
    public async Task<IActionResult> StickerPreview(string? template = "classic")
    {
        var setting = await PrintViewModelMapper.LoadPrintSettingAsync(Db);
        var html = _renderer.RenderStickerPreview(template, setting, PrintViewModelMapper.QueryToDict(Request.Query));
        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpGet("/print/share-preview/{size}/{template}")]
    public async Task<IActionResult> SharePreview(string size, string template)
    {
        var validSizes = new[] { "a4", "thermal" };
        if (!validSizes.Contains(size, StringComparer.OrdinalIgnoreCase) ||
            !PrintTemplateRenderer.ValidInvoiceTemplates.Contains(template, StringComparer.OrdinalIgnoreCase))
        {
            return NotFound("Preview template not found");
        }

        var setting = await PrintViewModelMapper.LoadPrintSettingAsync(Db);
        var html = _renderer.RenderSharePreview(size, template, setting, PrintViewModelMapper.QueryToDict(Request.Query));
        return Content(html, "text/html; charset=UTF-8");
    }

    private string ReadLegalDocumentText(string fileName, string fallbackText)
    {
        var path = ResolveLegalDocumentPath(fileName, Db.BasePath());
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            try
            {
                var raw = System.IO.File.ReadAllText(path, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return raw;
                }
            }
            catch
            {
                // Fall back to summary text if file read fails
            }
        }
        return fallbackText;
    }

    public static string? ResolveLegalDocumentPath(string fileName, string? baseRoot = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseRoot))
        {
            candidates.Add(Path.Combine(baseRoot, "assets", fileName));
            candidates.Add(Path.Combine(baseRoot, "native-win", "assets", fileName));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "assets", fileName));

        var cwd = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(cwd, "assets", fileName));
        candidates.Add(Path.Combine(cwd, "native-win", "assets", fileName));

        foreach (var startDir in new[] { AppContext.BaseDirectory, cwd, baseRoot })
        {
            if (string.IsNullOrWhiteSpace(startDir)) continue;
            var dir = startDir;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                candidates.Add(Path.Combine(dir, "native-win", "assets", fileName));
                candidates.Add(Path.Combine(dir, "assets", fileName));
                dir = Path.GetDirectoryName(dir) ?? "";
            }
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && System.IO.File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string FormatLegalBodyHtml(string bodyText)
    {
        var sb = new StringBuilder();
        var lines = (bodyText ?? "").Replace("\r\n", "\n").Split('\n');
        var inList = false;
        var seenBannerSeparators = 0;

        void CloseList()
        {
            if (inList)
            {
                sb.AppendLine("        </ul>");
                inList = false;
            }
        }

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                CloseList();
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^={4,}$"))
            {
                seenBannerSeparators++;
                continue;
            }

            // Skip top banner title line between the opening ==== lines
            if (seenBannerSeparators == 1 && trimmed.StartsWith("KAROBAR APP ERP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^\d+\.\s+\S"))
            {
                CloseList();
                sb.AppendLine($"        <h2>{WebUtility.HtmlEncode(trimmed)}</h2>");
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (!inList)
                {
                    sb.AppendLine("        <ul>");
                    inList = true;
                }
                sb.AppendLine($"            <li>{WebUtility.HtmlEncode(trimmed[2..].Trim())}</li>");
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^\([a-z0-9]+\)\s+\S", RegexOptions.IgnoreCase))
            {
                if (!inList)
                {
                    sb.AppendLine("        <ul>");
                    inList = true;
                }
                sb.AppendLine($"            <li>{WebUtility.HtmlEncode(trimmed)}</li>");
                continue;
            }

            CloseList();
            sb.AppendLine($"        <p>{WebUtility.HtmlEncode(trimmed)}</p>");
        }

        CloseList();
        return sb.ToString();
    }

    private static string BuildLegalHtml(string title, string bodyText)
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var formattedBody = FormatLegalBodyHtml(bodyText);
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{encodedTitle} — Karobar App ERP</title>
    <style>
        :root {{
            --bg: #f8fafc;
            --card: #ffffff;
            --text: #0f172a;
            --muted: #475569;
            --border: #e2e8f0;
            --accent: #2563eb;
        }}
        * {{ box-sizing: border-box; }}
        body {{
            margin: 0;
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
            background: var(--bg);
            color: var(--text);
            line-height: 1.65;
            padding: 32px 16px;
        }}
        .container {{
            max-width: 840px;
            margin: 0 auto;
            background: var(--card);
            border: 1px solid var(--border);
            border-radius: 16px;
            padding: 36px 40px;
            box-shadow: 0 10px 30px rgba(15, 23, 42, 0.05);
        }}
        .badge {{
            display: inline-block;
            font-size: 12px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            color: var(--accent);
            background: #eff6ff;
            padding: 4px 10px;
            border-radius: 999px;
            margin-bottom: 12px;
        }}
        h1 {{ margin: 0 0 8px; font-size: 28px; letter-spacing: -0.02em; }}
        .meta {{ font-size: 13px; color: var(--muted); margin-bottom: 28px; padding-bottom: 20px; border-bottom: 1px solid var(--border); }}
        h2 {{ font-size: 18px; margin-top: 26px; margin-bottom: 8px; color: #1e293b; }}
        p, li {{ font-size: 14.5px; color: #334155; }}
        ul {{ padding-left: 20px; }}
        .nav-links {{ margin-top: 32px; padding-top: 20px; border-top: 1px solid var(--border); display: flex; gap: 16px; font-size: 13.5px; }}
        .nav-links a {{ color: var(--accent); text-decoration: none; font-weight: 500; }}
        .nav-links a:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <div class=""container"">
        <span class=""badge"">Karobar App ERP v2.0 — Legal Agreement</span>
        <h1>{encodedTitle}</h1>
        <div class=""meta"">Effective Version: 2.0 &bull; Applies to Windows Desktop (.exe) &amp; On-Premise Installations</div>
{formattedBody}        <div class=""nav-links"">
            <a href=""/terms-of-service"">Terms &amp; Conditions</a>
            <a href=""/privacy-policy"">Privacy Policy</a>
            <a href=""/"">Back to Karobar App</a>
        </div>
    </div>
</body>
</html>";
    }
}
