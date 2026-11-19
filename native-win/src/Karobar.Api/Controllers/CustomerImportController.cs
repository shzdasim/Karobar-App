using System.Text;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/customers/import")]
[Authorize]
public class CustomerImportController : ApiControllerBase
{
    private readonly CustomerImportService _service;

    public CustomerImportController(KarobarDb db) : base(db)
    {
        _service = new CustomerImportService(db);
    }

    [HttpGet("template")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult Template()
    {
        const string csv = "name,email,phone,address,opening_balance\nAlice Smith,alice@example.com,0300-1234567,123 Main St,5000\nBob Khan,,021-1112223,Defense Phase 6,0\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "customers_template.csv");
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateUpload()
    {
        if (!await CanAsync("customer.import")) return Forbidden();

        if (!Request.HasFormContentType || Request.Form.Files.GetFile("file") is not { Length: > 0 } file)
        {
            return Unprocessable("The file field is required.", new Dictionary<string, string[]>
            {
                ["file"] = ["The file field is required."]
            });
        }

        var delimiter = Request.Form.TryGetValue("delimiter", out var dVal) && !string.IsNullOrEmpty(dVal)
            ? dVal.ToString()
            : ",";

        Dictionary<string, string> stored;
        try
        {
            stored = await _service.StoreUploadedAsync(file);
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }

        var res = await _service.ValidateCsvAsync(stored["path"], delimiter);
        if (res.TryGetValue("ok", out var okObj) && okObj is false)
        {
            try { System.IO.File.Delete(stored["path"]); } catch { }
            return StatusCode(422, res);
        }

        res["token"] = stored["token"];
        return Ok(res);
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit()
    {
        if (!await CanAsync("customer.import")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var token = GetString(body, "token")?.Trim() ?? "";
        if (string.IsNullOrEmpty(token) || !Guid.TryParse(token, out _))
        {
            return Unprocessable("The token field is required.", new Dictionary<string, string[]>
            {
                ["token"] = ["The token field must be a valid UUID."]
            });
        }

        var path = _service.ResolveStoredPath(token);
        if (!System.IO.File.Exists(path))
        {
            return NotFound(new { message = "Upload token expired or file missing." });
        }

        var insertValidOnly = GetBool(body, "insert_valid_only", true);
        var delimiter = GetString(body, "delimiter", ",") ?? ",";

        Dictionary<string, int> stats;
        try
        {
            stats = await _service.CommitAsync(path, insertValidOnly, delimiter);
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
        finally
        {
            try { System.IO.File.Delete(path); } catch { }
        }

        return Ok(new
        {
            message = "Import complete.",
            stats
        });
    }
}
