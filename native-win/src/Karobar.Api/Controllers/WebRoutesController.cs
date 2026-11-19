using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Karobar.Api.Helpers;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Printing;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[AllowAnonymous]
public class WebRoutesController : ApiControllerBase
{
    private readonly PrintTemplateRenderer _renderer;

    public WebRoutesController(KarobarDb db, PrintTemplateRenderer? renderer = null) : base(db)
    {
        _renderer = renderer ?? new PrintTemplateRenderer();
    }

    [HttpGet("/password/reset/{token}/{email}")]
    public IActionResult PasswordResetRedirect(string token, string email)
    {
        return Redirect($"/reset-password/{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(email)}");
    }

    [HttpGet("/terms-of-service")]
    public IActionResult TermsOfService()
    {
        var html = BuildLegalHtml("Terms and Conditions (End-User License Agreement)",
            "This End-User License Agreement governs your use of Karobar-App ERP.");
        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpGet("/privacy-policy")]
    public IActionResult PrivacyPolicy()
    {
        var html = BuildLegalHtml("Privacy Policy & Data Handling Agreement",
            "Karobar-App ERP stores your store, inventory, and financial data locally on your machine.");
        return Content(html, "text/html; charset=UTF-8");
    }

    [HttpGet("/server-launch")]
    public IActionResult ServerLaunchForm()
    {
        const string html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <title>Karobar Server Control Center</title>
</head>
<body style=""background:#0f172a;color:#f8fafc;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;"">
  <form method=""POST"" action=""/server-launch"" style=""background:#1e293b;padding:28px;border-radius:12px;width:360px;box-shadow:0 10px 25px rgba(0,0,0,0.4);"">
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
    public async Task<IActionResult> ServerLaunchSubmit()
    {
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
            WHERE u.status = 'active' AND LOWER(r.name) IN ('admin', 'super-admin')
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

    private static string BuildLegalHtml(string title, string bodyText)
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedBody = WebUtility.HtmlEncode(bodyText);
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""UTF-8""><title>{encodedTitle}</title></head>
<body style=""font-family:system-ui,sans-serif;max-width:800px;margin:40px auto;padding:0 20px;line-height:1.6;"">
  <h1>{encodedTitle}</h1>
  <p>{encodedBody}</p>
</body>
</html>";
    }
}
