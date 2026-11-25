using Karobar.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Api.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class EnsureLicensedAttribute : Attribute, IAsyncActionFilter
{
    public string? RequiredModuleOrTier { get; }

    public EnsureLicensedAttribute(string? requiredModuleOrTier = null)
    {
        RequiredModuleOrTier = requiredModuleOrTier;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var licenseSvc = context.HttpContext.RequestServices.GetRequiredService<LicenseService>();
        var check = await EnsureLicensedMiddleware.CheckLicenseRequirementAsync(licenseSvc, RequiredModuleOrTier);
        if (!check.Allowed)
        {
            context.Result = new ObjectResult(check.ErrorBody)
            {
                StatusCode = check.StatusCode
            };
            return;
        }

        await next();
    }
}

public class EnsureLicensedMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] BypassLicensePrefixes =
    {
        "/up",
        "/api/system/version",
        "/api/system/check-update",
        "/api/terminals/ping",
        "/api/login",
        "/api/session/extend",
        "/api/user",
        "/api/profile",
        "/api/logout",
        "/api/forgot-password",
        "/api/reset-password",
        "/api/verify-password",
        "/api/auth/confirm-password",
        "/api/categories/import/template",
        "/api/brands/import/template",
        "/api/products/import/template",
        "/api/suppliers/import/template",
        "/api/customers/import/template",
        "/api/license/status",
        "/api/license/activate",
        "/api/license/deactivate",
        "/api/me",
        "/api/theme-settings/active",
        "/api/sale-system",
        "/api/currency",
        "/terms-of-service",
        "/privacy-policy",
        "/server-launch",
        "/print/thermal-preview",
        "/print/a4-preview",
        "/print/sticker-preview",
        "/print/share-preview",
        "/sale-invoices/"
    };

    public EnsureLicensedMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, LicenseService licenseService)
    {
        var path = context.Request.Path.Value ?? "/";

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/sale-invoices", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/purchase-invoices", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/sale-returns", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/purchase-returns", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/purchase-orders", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/purchase-invoices", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/sale-returns", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/purchase-returns", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/purchase-orders", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/cash-drawer", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/print/shared-pdf", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/supplier-ledger/print", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/customer-ledger/print", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (BypassLicensePrefixes.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                                           (p.EndsWith("/") && path.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
        {
            await _next(context);
            return;
        }

        // Determine required module based on route prefix matching routes/api.php
        string? requiredModule = null;
        if (path.StartsWith("/api/accounting/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/reports/financial-statements/pdf", StringComparison.OrdinalIgnoreCase))
        {
            requiredModule = "accounting_gl";
        }
        else if (path.StartsWith("/api/hr/", StringComparison.OrdinalIgnoreCase))
        {
            requiredModule = "hr_payroll";
        }
        else if (path.StartsWith("/api/fbr/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("/api/reports/tax-return", StringComparison.OrdinalIgnoreCase))
        {
            requiredModule = "fbr";
        }
        else if (path.StartsWith("/api/cube/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("/api/settings/sqlite-import", StringComparison.OrdinalIgnoreCase) ||
                 path.Equals("/api/settings/test-cube-ai", StringComparison.OrdinalIgnoreCase))
        {
            requiredModule = "cube_ai";
        }
        else if (path.StartsWith("/api/barcodes/", StringComparison.OrdinalIgnoreCase))
        {
            requiredModule = "barcode";
        }
        else if (path.StartsWith("/api/terminals", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("/api/server/", StringComparison.OrdinalIgnoreCase))
        {
            requiredModule = "server_terminals";
        }

        var check = await CheckLicenseRequirementAsync(licenseService, requiredModule);
        if (!check.Allowed)
        {
            context.Response.StatusCode = check.StatusCode;
            await context.Response.WriteAsJsonAsync(check.ErrorBody);
            return;
        }

        await _next(context);
    }

    public static async Task<(bool Allowed, int StatusCode, Dictionary<string, object?>? ErrorBody)> CheckLicenseRequirementAsync(
        LicenseService licenseSvc,
        string? requiredModuleOrTier = null)
    {
        var status = await licenseSvc.CurrentStatusAsync();
        if (status.GetValueOrDefault("valid") is not true)
        {
            return (false, 402, new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["code"] = "license_required",
                ["reason"] = status.GetValueOrDefault("reason")?.ToString() ?? "Not licensed"
            });
        }

        if (!string.IsNullOrWhiteSpace(requiredModuleOrTier))
        {
            var requirement = requiredModuleOrTier.Trim().ToLowerInvariant();
            var currentTier = (status.GetValueOrDefault("tier")?.ToString() ?? "platinum").ToLowerInvariant();
            var modules = status.GetValueOrDefault("modules") as IEnumerable<string>
                          ?? (string[])LicenseService.TIERS["platinum"]["modules"];

            bool isAllowed;
            if (LicenseService.TIERS.TryGetValue(requirement, out var reqTierMeta))
            {
                var currentRank = LicenseService.TIERS.TryGetValue(currentTier, out var curTierMeta)
                    ? Convert.ToInt32(curTierMeta["rank"])
                    : 3;
                var reqRank = Convert.ToInt32(reqTierMeta["rank"]);
                isAllowed = currentRank >= reqRank;
            }
            else
            {
                isAllowed = modules.Contains(requirement, StringComparer.OrdinalIgnoreCase);
            }

            if (!isAllowed)
            {
                var currentLabel = LicenseService.TIERS.TryGetValue(currentTier, out var cm)
                    ? cm["label"].ToString()!
                    : currentTier;
                var requiredTierKey = LicenseService.TIERS.ContainsKey(requirement)
                    ? requirement
                    : (LicenseService.MODULES.TryGetValue(requirement, out var modMeta)
                        ? modMeta["required_tier"].ToString()!
                        : "platinum");
                var requiredTierLabel = LicenseService.TIERS.TryGetValue(requiredTierKey, out var rtm)
                    ? rtm["label"].ToString()!
                    : requiredTierKey;
                var moduleLabel = LicenseService.MODULES.TryGetValue(requirement, out var mm)
                    ? mm["label"].ToString()!
                    : requirement.Replace('_', ' ');

                return (false, 403, new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["code"] = "license_module_locked",
                    ["module"] = requirement,
                    ["module_label"] = moduleLabel,
                    ["current_tier"] = currentTier,
                    ["current_tier_label"] = currentLabel,
                    ["required_tier"] = requiredTierKey,
                    ["required_tier_label"] = requiredTierLabel,
                    ["message"] = $"Your current {currentLabel} license does not include {moduleLabel}. Please upgrade to the {requiredTierLabel} license tier."
                });
            }
        }

        return (true, 200, null);
    }
}
