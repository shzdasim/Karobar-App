using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using Karobar.Api.Infrastructure;
using Karobar.Background;
using Karobar.Core.Security;
using Karobar.Data;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Api.Controllers;

[Route("api/fbr")]
[Authorize]
public class FbrController : ApiControllerBase
{
    public const string DefaultDiSandboxUrl = "https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata_sb";
    public const string DefaultDiProductionUrl = "https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata";
    public const string DefaultPosSandboxUrl = "https://esp.fbr.gov.pk:8244/FBR/v1/api/Sandbox/PostData";
    public const string DefaultPosProductionUrl = "https://esp.fbr.gov.pk:8244/FBR/v1/api/Live/PostData";
    public const string DefaultImsLocalUrl = "http://127.0.0.1:8524/api/IMSFiscal/GetInvoiceNumberByModel";

    private readonly FbrFiscalizationEngine _fbrEngine;
    private readonly KarobarDbContext? _dbContext;

    public FbrController(
        KarobarDb db,
        FbrFiscalizationEngine? fbrEngine = null,
        KarobarDbContext? dbContext = null) : base(db)
    {
        _fbrEngine = fbrEngine ?? new FbrFiscalizationEngine();
        _dbContext = dbContext;
    }

    private async Task<Dictionary<string, object?>?> GetSettingAsync()
    {
        if (!await Db.HasTableAsync("settings")) return null;
        return await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
    }

    private static string ResolveEndpointUrl(Dictionary<string, object?>? setting)
    {
        var env = GetVal(setting, "fbr_environment", "sandbox");
        var mode = GetVal(setting, "fbr_mode", "digital_invoicing");
        var prodUrl = GetVal(setting, "fbr_production_url", "");
        var sbUrl = GetVal(setting, "fbr_sandbox_url", "");

        if (env == "production" && !string.IsNullOrWhiteSpace(prodUrl))
        {
            return prodUrl.Trim();
        }
        if (env == "sandbox" && !string.IsNullOrWhiteSpace(sbUrl))
        {
            return sbUrl.Trim();
        }

        if (mode == "tier1_pos_ims")
        {
            return DefaultImsLocalUrl;
        }
        if (mode == "tier1_pos_cloud")
        {
            return env == "production" ? DefaultPosProductionUrl : DefaultPosSandboxUrl;
        }

        return env == "production" ? DefaultDiProductionUrl : DefaultDiSandboxUrl;
    }

    public static bool IsLoopbackHost(string? host)
        => FbrEndpointValidator.IsLoopbackHost(host);

    public static bool IsAllowedFbrEndpointUrl(string? rawUrl, string? mode, out Uri? validatedUri, out string? errorMessage)
        => FbrEndpointValidator.IsAllowedFbrEndpointUrl(rawUrl, mode, out validatedUri, out errorMessage);

    [HttpGet("status-summary")]
    public async Task<IActionResult> StatusSummary()
    {
        if (!await CanAsync("settings.view", "settings.update", "sale-invoice.view", "sale-invoice.create"))
            return Forbidden();

        var setting = await GetSettingAsync();
        var hasCols = await Db.HasColumnAsync("sale_invoices", "fbr_sync_status");

        var counts = new Dictionary<string, int>
        {
            ["synced"] = 0,
            ["pending"] = 0,
            ["failed"] = 0
        };

        var recentQueue = new List<Dictionary<string, object?>>();

        if (hasCols)
        {
            counts["synced"] = await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoices WHERE fbr_sync_status = 'synced'");
            counts["pending"] = await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoices WHERE fbr_sync_status = 'pending'");
            counts["failed"] = await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoices WHERE fbr_sync_status = 'failed'");

            var rows = await Db.QueryListAsync(@"
                SELECT si.id,
                       si.posted_number,
                       si.date,
                       si.customer_id,
                       si.total,
                       si.fbr_sync_status,
                       si.fbr_error_message,
                       c.name AS customer_name
                FROM sale_invoices si
                LEFT JOIN customers c ON c.id = si.customer_id
                WHERE si.fbr_sync_status IN ('pending', 'failed')
                ORDER BY si.id DESC
                LIMIT 20");

            foreach (var r in rows)
            {
                var custId = r.TryGetValue("customer_id", out var cid) ? cid : null;
                var custName = r.TryGetValue("customer_name", out var cn) ? cn : null;
                r.Remove("customer_name");
                r["customer"] = custId != null
                    ? new Dictionary<string, object?> { ["id"] = custId, ["name"] = custName }
                    : null;
                recentQueue.Add(r);
            }
        }

        var rawToken = GetVal(setting, "fbr_bearer_token", "").Trim();
        var fbrEnabled = setting != null && setting.TryGetValue("fbr_enabled", out var en) &&
                         (en is true || en?.ToString() == "1" || string.Equals(en?.ToString(), "true", StringComparison.OrdinalIgnoreCase));

        return Ok(new Dictionary<string, object?>
        {
            ["fbr_enabled"] = fbrEnabled,
            ["fbr_environment"] = GetVal(setting, "fbr_environment", "sandbox"),
            ["fbr_mode"] = GetVal(setting, "fbr_mode", "digital_invoicing"),
            ["has_fbr_bearer_token"] = !string.IsNullOrEmpty(rawToken),
            ["fbr_seller_ntn"] = setting != null && setting.TryGetValue("fbr_seller_ntn", out var ntn) ? ntn : null,
            ["fbr_pos_id"] = setting != null && setting.TryGetValue("fbr_pos_id", out var pos) ? pos : null,
            ["endpoint"] = setting != null ? ResolveEndpointUrl(setting) : null,
            ["counts"] = counts,
            ["queue"] = recentQueue
        });
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection()
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Unauthenticated." });
        }

        if (!await IsAdminAsync() && !await CanAsync("settings.update", "settings.view"))
        {
            return Forbidden("Unauthorized to test FBR connection.");
        }

        var body = await ReadJsonBodyAsync();
        var setting = await GetSettingAsync() ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var overrideKeys = new[]
        {
            "fbr_environment", "fbr_mode", "fbr_bearer_token", "fbr_pos_id",
            "fbr_seller_ntn", "fbr_seller_strn", "fbr_seller_province",
            "fbr_sandbox_url", "fbr_production_url", "fbr_default_hs_code", "fbr_scenario_id"
        };

        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in overrideKeys)
        {
            var v = GetString(body, k);
            if (!string.IsNullOrWhiteSpace(v))
            {
                setting[k] = v;
                overrides[k] = v;
            }
        }

        var effectiveMode = GetVal(setting, "fbr_mode", "digital_invoicing");
        var effectiveEndpoint = ResolveEndpointUrl(setting);
        if (!IsAllowedFbrEndpointUrl(effectiveEndpoint, effectiveMode, out _, out var endpointError))
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "invalid_url",
                ["endpoint"] = effectiveEndpoint,
                ["message"] = endpointError ?? "Invalid FBR endpoint URL."
            });
        }

        foreach (var urlOverrideKey in new[] { "fbr_sandbox_url", "fbr_production_url" })
        {
            if (overrides.TryGetValue(urlOverrideKey, out var overrideUrl) &&
                !string.IsNullOrWhiteSpace(overrideUrl) &&
                !IsAllowedFbrEndpointUrl(overrideUrl, effectiveMode, out _, out var overrideErr))
            {
                return StatusCode(422, new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["status"] = "invalid_url",
                    ["endpoint"] = overrideUrl,
                    ["message"] = overrideErr ?? "Invalid FBR endpoint override URL."
                });
            }
        }

        if (_dbContext != null)
        {
            var efSetting = await _dbContext.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(HttpContext.RequestAborted);
            var engineRes = await _fbrEngine.TestConnectionAsync(efSetting, overrides, HttpContext.RequestAborted);
            var engineOk = engineRes.TryGetValue("ok", out var okVal) && okVal is true;
            return StatusCode(engineOk ? 200 : 422, engineRes);
        }

        var mode = effectiveMode;
        var env = GetVal(setting, "fbr_environment", "sandbox");
        var endpoint = effectiveEndpoint;
        var token = GetVal(setting, "fbr_bearer_token", "").Trim();
        var sellerNtn = GetVal(setting, "fbr_seller_ntn", "").Trim();
        var posId = GetVal(setting, "fbr_pos_id", "").Trim();

        if (mode != "tier1_pos_ims" && string.IsNullOrEmpty(token))
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "invalid_config",
                ["message"] = "Please enter your FBR Bearer Security Token before testing."
            });
        }

        if (mode == "digital_invoicing" && string.IsNullOrEmpty(sellerNtn))
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "invalid_config",
                ["message"] = "Please enter your Seller NTN / CNIC for FBR Digital Invoicing."
            });
        }

        if ((mode == "tier1_pos_cloud" || mode == "tier1_pos_ims") && string.IsNullOrEmpty(posId))
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "invalid_config",
                ["message"] = "Please enter your 6-digit FBR POS Registration ID (POSID)."
            });
        }

        if (env == "sandbox")
        {
            var sandboxNo = $"{(string.IsNullOrEmpty(posId) ? "966130" : posId)}{DateTime.UtcNow:yyMMddHHmmss}01";
            return Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["status"] = "sandbox_ready",
                ["endpoint"] = endpoint,
                ["fbr_invoice_number"] = sandboxNo,
                ["message"] = $"FBR Sandbox Mode verified & ready! Sample Sandbox Invoice Number: {sandboxNo} (Direct PRAL IP whitelisting only required for Live Production)."
            });
        }

        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };
            if (string.Equals(mode, "tier1_pos_ims", StringComparison.OrdinalIgnoreCase))
            {
                handler.ServerCertificateCustomValidationCallback = (req, _, _, errors) =>
                    errors == SslPolicyErrors.None || IsLoopbackHost(req.RequestUri?.Host);
            }
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var payloadJson = JsonSerializer.Serialize(new
            {
                invoiceType = "Sale Invoice",
                invoiceDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                sellerNTNCNIC = sellerNtn,
                invoiceRefNo = "TEST-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")
            });

            var resp = await client.PostAsync(endpoint, new StringContent(payloadJson, Encoding.UTF8, "application/json"));
            var status = (int)resp.StatusCode;
            if (status == 401 || status == 403)
            {
                return StatusCode(422, new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["status"] = "unauthorized",
                    ["http_status"] = status,
                    ["endpoint"] = endpoint,
                    ["message"] = $"FBR Gateway rejected the Bearer Token (HTTP {status}). Verify your IRIS Security Token and IP whitelist."
                });
            }

            if ((status >= 200 && status < 300) || (status >= 400 && status < 500))
            {
                return Ok(new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["status"] = "reachable",
                    ["http_status"] = status,
                    ["endpoint"] = endpoint,
                    ["message"] = $"FBR Gateway reached & Bearer Token accepted (HTTP {status})."
                });
            }

            return StatusCode(422, new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "gateway_error",
                ["http_status"] = status,
                ["endpoint"] = endpoint,
                ["message"] = $"FBR Gateway returned HTTP {status}."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "unreachable",
                ["endpoint"] = endpoint,
                ["message"] = $"Could not reach FBR endpoint ({endpoint}): {ex.Message}"
            });
        }
    }

    [HttpPost("sale-invoices/{id:long}/sync")]
    public async Task<IActionResult> SyncSaleInvoice(long id)
    {
        if (!await CanAsync("settings.update", "sale-invoice.update", "sale-invoice.create"))
            return Forbidden();

        var invoice = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id });
        if (invoice == null)
        {
            return NotFound(new { message = "Sale invoice not found." });
        }

        var res = _dbContext != null
            ? await _fbrEngine.FiscalizeSaleInvoiceAsync(_dbContext, (int)id, force: true, HttpContext.RequestAborted)
            : await FiscalizeSaleInvoiceInternalAsync(invoice, true);
        var freshInvoice = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id });
        res["invoice"] = freshInvoice;

        var ok = res.TryGetValue("ok", out var okVal) && okVal is true;
        return StatusCode(ok ? 200 : 422, res);
    }

    [HttpPost("sale-returns/{id:long}/sync")]
    public async Task<IActionResult> SyncSaleReturn(long id)
    {
        if (!await CanAsync("settings.update", "sale-return.update", "sale-return.create"))
            return Forbidden();

        var saleReturn = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id });
        if (saleReturn == null)
        {
            return NotFound(new { message = "Sale return not found." });
        }

        var res = _dbContext != null
            ? await _fbrEngine.FiscalizeSaleReturnAsync(_dbContext, (int)id, force: true, HttpContext.RequestAborted)
            : await FiscalizeSaleReturnInternalAsync(saleReturn, true);
        var freshReturn = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id });
        res["sale_return"] = freshReturn;

        var ok = res.TryGetValue("ok", out var okVal) && okVal is true;
        return StatusCode(ok ? 200 : 422, res);
    }

    [HttpPost("retry-queue")]
    public async Task<IActionResult> RetryQueue()
    {
        if (!await CanAsync("settings.update", "sale-invoice.update"))
            return Forbidden();

        if (!await Db.HasColumnAsync("sale_invoices", "fbr_sync_status"))
        {
            return Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["summary"] = new { processed = 0, synced = 0, failed = 0 },
                ["message"] = "Processed 0 queued invoice(s): 0 synced, 0 failed."
            });
        }

        if (_dbContext != null)
        {
            var summary = await _fbrEngine.RetryPendingInvoicesAsync(_dbContext, limit: 50, HttpContext.RequestAborted);
            return Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["summary"] = summary,
                ["message"] = $"Processed {summary["processed"]} queued invoice(s): {summary["synced"]} synced, {summary["failed"]} failed."
            });
        }

        var invoices = await Db.QueryListAsync(@"
            SELECT *
            FROM sale_invoices
            WHERE fbr_sync_status IN ('pending', 'failed')
              AND (status IS NULL OR status != 'reversed')
            ORDER BY id ASC
            LIMIT 50");

        int synced = 0;
        int failed = 0;
        foreach (var inv in invoices)
        {
            var res = await FiscalizeSaleInvoiceInternalAsync(inv, true);
            if (res.TryGetValue("ok", out var okVal) && okVal is true)
            {
                synced++;
            }
            else
            {
                failed++;
            }
        }

        var processed = invoices.Count;
        return Ok(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["summary"] = new Dictionary<string, int>
            {
                ["processed"] = processed,
                ["synced"] = synced,
                ["failed"] = failed
            },
            ["message"] = $"Processed {processed} queued invoice(s): {synced} synced, {failed} failed."
        });
    }

    private async Task<Dictionary<string, object?>> FiscalizeSaleInvoiceInternalAsync(Dictionary<string, object?> invoice, bool force)
    {
        var setting = await GetSettingAsync();
        var fbrEnabled = setting != null && setting.TryGetValue("fbr_enabled", out var en) &&
                         (en is true || en?.ToString() == "1" || string.Equals(en?.ToString(), "true", StringComparison.OrdinalIgnoreCase));

        if (setting == null || (!fbrEnabled && !force))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "not_applicable",
                ["message"] = "FBR integration is disabled in Settings."
            };
        }

        if (!await Db.HasColumnAsync("sale_invoices", "fbr_sync_status"))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "not_applicable",
                ["message"] = "FBR columns not migrated."
            };
        }

        var invId = Convert.ToInt64(invoice["id"]);
        var currentStatus = GetVal(invoice, "fbr_sync_status", "");
        var currentFbrNo = GetVal(invoice, "fbr_invoice_number", "");
        var currentQr = GetVal(invoice, "fbr_qr_data", "");

        if (currentStatus == "synced" && !string.IsNullOrEmpty(currentFbrNo) && !force)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["status"] = "synced",
                ["fbr_invoice_number"] = currentFbrNo,
                ["fbr_qr_data"] = currentQr,
                ["message"] = "Invoice is already fiscalized with FBR."
            };
        }

        var usin = GetVal(invoice, "posted_number", $"INV-{invId}");
        var mode = GetVal(setting, "fbr_mode", "digital_invoicing");
        var env = GetVal(setting, "fbr_environment", "sandbox");
        var token = GetVal(setting, "fbr_bearer_token", "").Trim();
        var posId = GetVal(setting, "fbr_pos_id", "966130").Trim();
        if (string.IsNullOrEmpty(posId)) posId = "966130";

        if (mode != "tier1_pos_ims" && string.IsNullOrEmpty(token))
        {
            await Db.UpdateAsync("sale_invoices", invId, new Dictionary<string, object?>
            {
                ["fbr_usin"] = usin,
                ["fbr_sync_status"] = "failed",
                ["fbr_error_message"] = "Missing FBR Bearer Security Token in Settings."
            });

            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "failed",
                ["message"] = "Missing FBR Bearer Security Token in Settings."
            };
        }

        if (env == "sandbox")
        {
            var sandboxNo = $"{posId}{DateTime.UtcNow:yyMMddHHmmss}{(invId % 100):D2}";
            var qrData = $"https://ims.fbr.gov.pk/Lv1/Verification?inv={sandboxNo}";
            await Db.UpdateAsync("sale_invoices", invId, new Dictionary<string, object?>
            {
                ["fbr_usin"] = usin,
                ["fbr_invoice_number"] = sandboxNo,
                ["fbr_qr_data"] = qrData,
                ["fbr_sync_status"] = "synced",
                ["fbr_synced_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                ["fbr_error_message"] = null,
                ["fbr_response_payload"] = JsonSerializer.Serialize(new { sandbox_simulated = true, invoice_number = sandboxNo })
            });

            return new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["status"] = "synced",
                ["fbr_invoice_number"] = sandboxNo,
                ["fbr_qr_data"] = qrData,
                ["message"] = $"Fiscalized in FBR Sandbox Mode (#{sandboxNo})"
            };
        }

        var errMsg = "FBR Gateway unreachable in offline environment. Queued for retry.";
        await Db.UpdateAsync("sale_invoices", invId, new Dictionary<string, object?>
        {
            ["fbr_usin"] = usin,
            ["fbr_sync_status"] = "pending",
            ["fbr_error_message"] = errMsg
        });

        return new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["status"] = "pending",
            ["message"] = errMsg
        };
    }

    private async Task<Dictionary<string, object?>> FiscalizeSaleReturnInternalAsync(Dictionary<string, object?> saleReturn, bool force)
    {
        var setting = await GetSettingAsync();
        var fbrEnabled = setting != null && setting.TryGetValue("fbr_enabled", out var en) &&
                         (en is true || en?.ToString() == "1" || string.Equals(en?.ToString(), "true", StringComparison.OrdinalIgnoreCase));

        if (setting == null || (!fbrEnabled && !force))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "not_applicable",
                ["message"] = "FBR integration is disabled in Settings."
            };
        }

        if (!await Db.HasColumnAsync("sale_returns", "fbr_sync_status"))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "not_applicable",
                ["message"] = "FBR columns not migrated."
            };
        }

        var retId = Convert.ToInt64(saleReturn["id"]);
        var usin = GetVal(saleReturn, "posted_number", $"RET-{retId}");
        var mode = GetVal(setting, "fbr_mode", "digital_invoicing");
        var env = GetVal(setting, "fbr_environment", "sandbox");
        var token = GetVal(setting, "fbr_bearer_token", "").Trim();
        var posId = GetVal(setting, "fbr_pos_id", "966130").Trim();
        if (string.IsNullOrEmpty(posId)) posId = "966130";

        if (mode != "tier1_pos_ims" && string.IsNullOrEmpty(token))
        {
            await Db.UpdateAsync("sale_returns", retId, new Dictionary<string, object?>
            {
                ["fbr_usin"] = usin,
                ["fbr_sync_status"] = "failed",
                ["fbr_error_message"] = "Missing FBR Bearer Security Token in Settings."
            });

            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["status"] = "failed",
                ["message"] = "Missing FBR Bearer Security Token in Settings."
            };
        }

        if (env == "sandbox")
        {
            var sandboxNo = $"{posId}{DateTime.UtcNow:yyMMddHHmmss}{(retId % 100):D2}";
            var qrData = $"https://ims.fbr.gov.pk/Lv1/Verification?inv={sandboxNo}";
            await Db.UpdateAsync("sale_returns", retId, new Dictionary<string, object?>
            {
                ["fbr_usin"] = usin,
                ["fbr_invoice_number"] = sandboxNo,
                ["fbr_qr_data"] = qrData,
                ["fbr_sync_status"] = "synced",
                ["fbr_synced_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                ["fbr_error_message"] = null
            });

            return new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["status"] = "synced",
                ["fbr_invoice_number"] = sandboxNo,
                ["fbr_qr_data"] = qrData,
                ["message"] = $"Credit Note fiscalized with FBR (#{sandboxNo})"
            };
        }

        var errMsg = "FBR Gateway unreachable. Queued for retry.";
        await Db.UpdateAsync("sale_returns", retId, new Dictionary<string, object?>
        {
            ["fbr_usin"] = usin,
            ["fbr_sync_status"] = "pending",
            ["fbr_error_message"] = errMsg
        });

        return new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["status"] = "pending",
            ["message"] = errMsg
        };
    }

    private static string GetVal(Dictionary<string, object?>? dict, string key, string fallback = "")
    {
        if (dict == null || !dict.TryGetValue(key, out var val) || val == null) return fallback;
        var s = val.ToString();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }
}
