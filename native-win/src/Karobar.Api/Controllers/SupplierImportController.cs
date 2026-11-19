using System.Text;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/suppliers/import")]
[Authorize]
public class SupplierImportController : ApiControllerBase
{
    private readonly SupplierImportService _service;

    public SupplierImportController(KarobarDb db) : base(db)
    {
        _service = new SupplierImportService(db);
    }

    [HttpGet("template")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult Template()
    {
        const string csv = "name,address,phone,opening_balance\nAcme Supplies,21 Main Street,+92 300 1234567,15000\nNexus Traders,Plot #45, +92 333 7654321,0\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "suppliers_template.csv");
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateUpload()
    {
        if (!await CanAsync("supplier.import")) return Forbidden();

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

        Dictionary<string, object?> res;
        try
        {
            res = await _service.ValidateCsvAsync(stored["path"], delimiter);
        }
        catch
        {
            try { System.IO.File.Delete(stored["path"]); } catch { }
            return StatusCode(500, new { message = "Could not read uploaded file." });
        }

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
        if (!await CanAsync("supplier.import")) return Forbidden();

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
            return NotFound(new { message = "Upload token expired or file not found." });
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
