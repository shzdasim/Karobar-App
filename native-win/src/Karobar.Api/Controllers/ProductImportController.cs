using System.Text;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/products/import")]
public class ProductImportController : ApiControllerBase
{
    private readonly ProductImportService _service;

    public ProductImportController(KarobarDb db) : base(db)
    {
        _service = new ProductImportService(db);
    }

    [HttpGet("template")]
    public async Task<IActionResult> Template()
    {
        if (!await CanAsync("product.import")) return Forbidden();

        const string csv = "product_code,name,pack_size,unit_purchase_price,unit_sale_price,category,brand,supplier,quantity,barcode,narcotic,max_discount,formulation,description,rack\nPRD-0001,Paracetamol 500mg,10,3.5,5,Analgesics,Acme,ABC Pharma,0,1234567890123,no,10,Tablet,Pain reliever,R1\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "products_template.csv");
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateUpload()
    {
        if (!await CanAsync("product.import")) return Forbidden();

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

        var createRefs = true;
        if (Request.Form.TryGetValue("create_missing_refs", out var cmrVal) && !string.IsNullOrEmpty(cmrVal))
        {
            var s = cmrVal.ToString().Trim().ToLowerInvariant();
            createRefs = s is "1" or "true" or "on" or "yes";
        }

        Dictionary<string, string> stored;
        try
        {
            stored = await _service.StoreUploadedAsync(file);
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }

        var res = await _service.ValidateCsvAsync(stored["path"], delimiter, createRefs);
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
        if (!await CanAsync("product.import")) return Forbidden();

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
        var createRefs = body.ContainsKey("create_missing_refs") ? GetBool(body, "create_missing_refs", true) : true;

        var stats = await _service.CommitAsync(path, insertValidOnly, delimiter, createRefs);
        try { System.IO.File.Delete(path); } catch { }

        return Ok(new
        {
            message = "Import complete.",
            stats
        });
    }
}
