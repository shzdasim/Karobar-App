import React, { useState, useEffect, useCallback } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useLicense } from "../context/LicenseContext.jsx";
import { usePermissions } from "../api/usePermissions.js";
import axios from "axios";

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
  "/reports/outstanding-aging": ["report.outstanding-aging.view"],
  "/reports/reorder": ["report.reorder.view"],
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
  { pattern: /^\/stock-adjustments\/[^/]+\/edit$/, perms: ["stock-adjustment.update"] },
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

export default function ProtectedRoute({ children, permission, permissionAny }) {
  const token = localStorage.getItem("token");
  const location = useLocation();
  const { loading: licenseLoading, valid: licenseValid } = useLicense();
  const { loading: permsLoading, has } = usePermissions();

  // Local state to track license status independently
  const [isLicenseValid, setIsLicenseValid] = useState(null);
  const [checkingLicense, setCheckingLicense] = useState(true);

  // Check license status explicitly
  const checkLicense = useCallback(async () => {
    if (!token) {
      setCheckingLicense(false);
      return;
    }

    try {
      const response = await axios.get("/api/license/status", {
        headers: { Authorization: `Bearer ${token}` }
      });
      setIsLicenseValid(response.data.valid === true);
    } catch (error) {
      // On API error, be permissive - let the user in
      // Server-side middleware will handle enforcement
      console.warn("License check failed:", error);
      setIsLicenseValid(true); // Allow access, server will enforce
    } finally {
      setCheckingLicense(false);
    }
  }, [token]);

  useEffect(() => {
    // Only check license if LicenseContext is done loading
    if (!licenseLoading) {
      checkLicense();
    }
  }, [licenseLoading, checkLicense]);

  // Check authentication first
  if (!token) {
    return <Navigate to="/" replace />;
  }

  // Enforce route-level or prop-level permissions once loaded
  const requiredPerms =
    permissionAny ||
    (permission ? [permission] : resolveRoutePermissions(location.pathname));

  if (requiredPerms && requiredPerms.length > 0) {
    if (permsLoading) {
      return (
        <div className="flex items-center justify-center min-h-[40vh] text-sm text-slate-500 dark:text-slate-400">
          Checking permissions…
        </div>
      );
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

  // While checking license status, show children
  if (checkingLicense || licenseLoading) {
    return children;
  }

  // If we have an explicit license check result and it's invalid, redirect
  if (isLicenseValid === false && !licenseValid) {
    return <Navigate to="/activate" replace />;
  }

  return children;
}
