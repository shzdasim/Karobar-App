import React, { useEffect } from "react";
import { Link, Navigate, Outlet, useLocation } from "react-router-dom";
import {
   useLicense,
  LICENSE_MODULES_CATALOG,
  LICENSE_TIERS_CATALOG,
} from "../context/LicenseContext.jsx";
import { useSaleSystem } from "../context/SaleSystemContext.jsx";
import { usePermissions } from "../api/usePermissions.js";
import PageSkeleton from "./PageSkeleton.jsx";
import { saveLastVisitedPath } from "../utils/lastVisitedPage.js";

const ROUTE_LICENSE_MODULE_MAP = {
  "/sale-invoices/pos": "barcode",
  "/sale-invoices/barcode-pos": "barcode",
  "/barcode-labels": "barcode",
  "/reports/tax-return": "fbr",
  "/server-dashboard": "server_terminals",
  "/terminals": "server_terminals",
  "/accounting": "accounting_gl",
  "/hr-payroll": "hr_payroll",
  "/settings/sqlite-import": "cube_ai",
  "/sqlite-import": "cube_ai",
};

const ROUTE_PERMISSION_MAP = {
  "/products": ["product.view"],
  "/products/create": ["product.create"],
  "/products/import": ["product.import"],
  "/barcode-labels": ["barcode.view", "barcode.print"],
  "/batch-expiry": ["batch-expiry.view"],
  "/categories": ["category.view"],
  "/brands": ["brand.view"],
  "/suppliers": ["supplier.view"],
  "/suppliers/import": ["supplier.import"],
  "/customers": ["customer.view"],
  "/customers/import": ["customer.import"],
  "/purchase-invoices": ["purchase-invoice.view"],
  "/purchase-invoices/create": ["purchase-invoice.create"],
  "/sale-invoices": ["sale-invoice.view"],
  "/sale-invoices/create": ["sale-invoice.create"],
  "/sale-invoices/pos": ["barcode-pos.view", "barcode-pos.create"],
  "/sale-invoices/barcode-pos": ["barcode-pos.view", "barcode-pos.create"],
  "/purchase-returns": ["purchase-return.view"],
  "/purchase-returns/create": ["purchase-return.create"],
  "/sale-returns": ["sale-return.view"],
  "/sale-returns/create": ["sale-return.create"],
  "/purchase-orders": ["purchase-order.view"],
  "/user-demands": ["user-demands.view"],
  "/stock-adjustments": ["stock-adjustment.view"],
  "/stock-adjustments/create": ["stock-adjustment.create"],
  "/stock-disposals": ["stock-disposal.view"],
  "/stock-disposals/create": ["stock-disposal.create"],
  "/stocktake-sessions": ["stocktake.view"],
  "/stocktake-sessions/create": ["stocktake.create"],
  "/supplier-ledger": ["supplier-ledger.view"],
  "/customer-ledger": ["customer-ledger.view"],
  "/accounts": ["account.view"],
  "/payments": ["payment.view"],
  "/expenses": ["expense.view"],
  "/cash-drawer": ["cash-drawer.view"],
  "/reports/net-profit": ["report.net-profit.view"],
  "/reports/current-stock": ["report.current-stock.view"],
  "/reports/cost-of-sale": ["report.cost-of-sale.view"],
  "/reports/cost-of-sale-detail": ["report.cost-of-sale-detail.view"],
  "/reports/purchase-detail": ["report.purchase-detail.view"],
  "/reports/purchase-summary": ["report.purchase-summary.view"],
  "/reports/purchase-returns": ["report.purchase-returns.view"],
  "/reports/sale-detail": ["report.sale-detail.view"],
  "/reports/sale-returns": ["report.sale-returns.view"],
  "/reports/stock-adjustment": ["report.stock-adjustment.view"],
  "/reports/product-comprehensive": ["report.product-comprehensive.view"],
  "/reports/near-expiry-product": ["report.near-expiry-product.view"],
  "/reports/narcotics": [
    "report.sale-detail.view",
    "report.current-stock.view",
    "report.product-comprehensive.view",
    "product.view",
  ],
  "/reports/outstanding-aging": ["report.outstanding-aging.view"],
  "/reports/reorder": ["report.reorder.view"],
  "/reports/tax-return": [
    "report.tax-return.view",
    "report.net-profit.view",
    "report.sale-detail.view",
    "report.purchase-detail.view",
    "account.view",
  ],
  "/settings": ["settings.view"],
  "/backups": ["backup.view"],
  "/users": ["user.view", "user.manage"],
  "/users/create": ["user.create", "user.manage"],
  "/roles": ["role.view"],
  "/roles/create": ["role.create"],
};

const DYNAMIC_ROUTE_PERMISSIONS = [
  { pattern: /^\/products\/[^/]+\/edit$/, perms: ["product.update"] },
  { pattern: /^\/purchase-invoices\/[^/]+\/edit$/, perms: ["purchase-invoice.update"] },
  { pattern: /^\/sale-invoices\/(?!pos$|barcode-pos$)[^/]+\/edit$/, perms: ["sale-invoice.update"] },
  { pattern: /^\/purchase-returns\/[^/]+\/edit$/, perms: ["purchase-return.update"] },
  { pattern: /^\/sale-returns\/[^/]+\/edit$/, perms: ["sale-return.update"] },
  { pattern: /^\/stocktake-sessions\/[^/]+\/edit$/, perms: ["stocktake.view", "stocktake.update"] },
  { pattern: /^\/users\/[^/]+\/edit$/, perms: ["user.update", "user.manage"] },
  { pattern: /^\/roles\/[^/]+\/edit$/, perms: ["role.update"] },
];

function resolveRoutePermissions(pathname) {
  if (ROUTE_PERMISSION_MAP[pathname]) {
    return ROUTE_PERMISSION_MAP[pathname];
  }
  for (const entry of DYNAMIC_ROUTE_PERMISSIONS) {
    if (entry.pattern.test(pathname)) {
      return entry.perms;
    }
  }
  return null;
}

export default function ProtectedRoute({
  children,
  permission,
  permissionAny,
  shellOnly = false,
  fullScreen = false,
}) {
  const token = localStorage.getItem("token");
  const location = useLocation();
  const {
    loading: licenseLoading,
    valid: licenseValid,
    tierLabel,
    hasModule,
  } = useLicense();
  const { isSetupCompleted, loading: saleSystemLoading } = useSaleSystem();
  const { loading: permsLoading, has } = usePermissions();

  useEffect(() => {
    if (location.pathname !== "/setup") {
      saveLastVisitedPath(location);
    }
  }, [location.pathname, location.search, location.hash]);

  // Check authentication first
  if (!token) {
    if (location.pathname !== "/setup") {
      saveLastVisitedPath(location);
    }
    const fullPath = `${location.pathname}${location.search || ""}${location.hash || ""}`;
    return <Navigate to="/" state={{ from: fullPath }} replace />;
  }

  // Enforce route-level or prop-level permissions once loaded (unless shellOnly)
  if (!shellOnly) {
    const requiredPerms =
      permissionAny ||
      (permission ? [permission] : resolveRoutePermissions(location.pathname));

    if (requiredPerms && requiredPerms.length > 0) {
      if (permsLoading) {
        return <PageSkeleton fullScreen={fullScreen} />;
      }
      const isAllowed = requiredPerms.some((perm) => has(perm));
      if (!isAllowed) {
        return (
          <div className="p-6">
            <div className="rounded-xl border border-rose-200 bg-rose-50/80 dark:border-rose-900/50 dark:bg-rose-950/30 p-4 text-sm text-rose-700 dark:text-rose-300">
              You do not have permission to access this page ({requiredPerms.join(" or ")}).
            </div>
          </div>
        );
      }
    }
  }

  const content = children ?? <Outlet />;

  // While checking license status, show children
  if (licenseLoading) {
    return content;
  }

  // If we have an explicit license check result and it's invalid, redirect
  if (!licenseValid) {
    return <Navigate to="/activate" replace />;
  }

  // Enforce license tier module access on route navigation (when !shellOnly so DashboardLayout shell stays mounted)
  if (!shellOnly) {
    const requiredLicenseModule = ROUTE_LICENSE_MODULE_MAP[location.pathname];
    if (requiredLicenseModule && !hasModule(requiredLicenseModule)) {
      const modMeta = LICENSE_MODULES_CATALOG[requiredLicenseModule];
      const reqTierKey = modMeta?.required_tier || "platinum";
      const reqTierLabel = LICENSE_TIERS_CATALOG[reqTierKey]?.label || "Platinum";

      return (
        <div className="p-6 max-w-2xl mx-auto mt-8">
          <div className="rounded-2xl border border-amber-300/80 dark:border-amber-700/60 bg-amber-50/90 dark:bg-amber-950/30 p-6 shadow-lg">
            <div className="flex items-center gap-3 mb-2">
              <span className="px-2.5 py-1 rounded-full text-xs font-bold uppercase tracking-wider bg-amber-500 text-white">
                {reqTierLabel} Tier Required
              </span>
              <span className="text-xs font-semibold text-amber-800 dark:text-amber-300">
                Current License: {tierLabel || "Basic"}
              </span>
            </div>
            <h2 className="text-lg font-bold text-slate-900 dark:text-white">
              {modMeta?.label || "Module Locked"} is not included in your {tierLabel || "Basic"} license
            </h2>
            <p className="text-sm text-slate-600 dark:text-slate-300 mt-1.5">
              {modMeta?.description ||
                `Upgrade your license to the ${reqTierLabel} tier to unlock this module.`}
            </p>
            <div className="mt-4 flex flex-wrap items-center gap-3">
              <Link
                to="/settings#license"
                className="px-4 py-2 rounded-xl text-xs font-bold bg-amber-600 hover:bg-amber-500 text-white transition-colors"
              >
                Upgrade License in Settings
              </Link>
              <Link
                to="/dashboard"
                className="px-4 py-2 rounded-xl text-xs font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200"
              >
                Back to Dashboard
              </Link>
            </div>
          </div>
        </div>
      );
    }
  }

  // Redirect to the First-Login Genie Setup Wizard (/setup) if store onboarding has not been completed yet
  if (!saleSystemLoading && isSetupCompleted === false && location.pathname !== "/setup") {
    return <Navigate to="/setup" replace />;
  }

  return content;
}
