/**
 * Pure-ESM zero-dependency route permission and license-module resolution helpers.
 * Intentionally free of React, JSX, DOM globals, or side effects at module top-level
 * so Node `node:test` suites can import and verify 100% route coverage directly.
 */

export const PUBLIC_ROUTES = [
  "/",
  "/forgot-password",
  "/reset-password/:token",
  "/reset-password/:token/:email",
  "/windows-preview",
  "/theme-windows-preview",
  "/setup",
  "/activate",
  "*",
];

export const OPEN_AUTHENTICATED_ROUTES = [
  "/dashboard",
  "/profile",
];

export const ROUTE_LICENSE_MODULE_MAP = {
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

export const ROUTE_PERMISSION_MAP = {
  // Suppliers & Customers
  "/suppliers": ["supplier.view"],
  "/suppliers/create": ["supplier.create"],
  "/suppliers/import": ["supplier.import"],
  "/customers": ["customer.view"],
  "/customers/create": ["customer.create"],
  "/customers/import": ["customer.import"],
  "/doctors": [
    "doctor.view",
    "customer.view",
    "sale-invoice.view",
    "sale-invoice.create",
    "sale-invoice.update",
    "barcode-pos.view",
    "barcode-pos.create",
  ],
  "/doctors/create": [
    "doctor.create",
    "customer.create",
    "sale-invoice.create",
    "sale-invoice.update",
    "barcode-pos.create",
  ],

  // Catalog Masters & Background Tasks
  "/categories": ["category.view"],
  "/brands": ["brand.view"],
  "/brands/create": ["brand.create"],
  "/products": ["product.view"],
  "/products/create": ["product.create"],
  "/products/import": ["product.import"],
  "/background-tasks": ["product.update", "product.create"],
  "/barcode-labels": ["barcode.view", "barcode.print"],
  "/batch-expiry": ["batch-expiry.view"],
  "/product-batches": ["product.view", "product.update", "batch-expiry.view"],

  // Purchase Invoices & Returns
  "/purchase-invoices": ["purchase-invoice.view"],
  "/purchase-invoices/create": ["purchase-invoice.create"],
  "/purchase-returns": ["purchase-return.view"],
  "/purchase-returns/create": ["purchase-return.create"],

  // Sale Invoices, POS & Trade Schemes
  "/sale-invoices": ["sale-invoice.view"],
  "/wholesale-invoices": ["sale-invoice.view"],
  "/wholesale-invoices/create": ["sale-invoice.create"],
  "/sale-invoices/create": ["sale-invoice.create"],
  "/sale-invoices/create/retail": ["sale-invoice.create"],
  "/sale-invoices/create-retail": ["sale-invoice.create"],
  "/sale-invoices/create/wholesale": ["sale-invoice.create"],
  "/sale-invoices/create-wholesale": ["sale-invoice.create"],
  "/sale-invoices/pos": ["barcode-pos.view", "barcode-pos.create"],
  "/sale-invoices/barcode-pos": ["barcode-pos.view", "barcode-pos.create"],
  "/trade-schemes": ["trade-scheme.view", "sale-invoice.view"],
  "/trade-schemes/create": ["trade-scheme.create", "trade-scheme.manage", "sale-invoice.create"],

  // Sale Returns
  "/sale-returns": ["sale-return.view"],
  "/sale-returns/create": ["sale-return.create"],

  // Purchase Orders & Reorder
  "/purchase-orders": ["purchase-order.view"],
  "/purchase-orders/create": ["purchase-order.create"],
  "/purchase-orders/forecast": ["purchase-order.view", "purchase-order.generate"],
  "/smart-reorder": [
    "purchase-order.view",
    "purchase-order.generate",
    "purchase-order.create",
    "report.reorder.view",
  ],

  // User Demands
  "/user-demands": ["user-demands.view"],
  "/user-demands/create": ["user-demands.create"],

  // Stock Adjustments, Stocktake & Disposals
  "/stock-adjustments": ["stock-adjustment.view"],
  "/stock-adjustments/create": ["stock-adjustment.create"],
  "/stocktake-sessions": ["stocktake.view"],
  "/stocktake-sessions/create": ["stocktake.create"],
  "/stock-disposals": ["stock-disposal.view"],
  "/stock-disposals/create": ["stock-disposal.create"],

  // Finance, Accounting, HR & Ledgers
  "/supplier-ledger": ["supplier-ledger.view"],
  "/customer-ledger": ["customer-ledger.view"],
  "/accounts": ["account.view"],
  "/accounting": [
    "accounting.view",
    "accounting.manage",
    "report.financial-statements.view",
    "account.view",
    "report.net-profit.view",
  ],
  "/hr-payroll": [
    "hr.view",
    "hr.manage",
    "payroll.manage",
    "user.manage",
    "expense.view",
    "account.view",
    "user.view",
  ],
  "/payments": ["payment.view"],
  "/party-credits": ["payment.view", "customer-ledger.view", "supplier-ledger.view"],
  "/expenses": ["expense.view"],
  "/cash-drawer": ["cash-drawer.view"],

  // Reports
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

  // Settings, Terminals, SQLite Import, Backups, Users & Roles
  "/settings": ["settings.view"],
  "/server-dashboard": ["settings.view", "settings.update"],
  "/terminals": ["settings.view", "settings.update"],
  "/settings/sqlite-import": ["settings.update"],
  "/sqlite-import": ["settings.update"],
  "/backups": ["backup.view"],
  "/users": ["user.view", "user.manage"],
  "/users/create": ["user.create", "user.manage"],
  "/roles": ["role.view"],
  "/roles/create": ["role.create"],
};

// Order `/edit` and `/review` patterns BEFORE `/:id` patterns, and guard `/:id` with
// negative lookaheads so static sub-routes never falsely match `/:id`.
export const DYNAMIC_ROUTE_PERMISSIONS = [
  // Edit & review sub-routes first
  { pattern: /^\/suppliers\/(?!create$|import$)[^/]+\/edit$/, perms: ["supplier.update"] },
  { pattern: /^\/customers\/(?!create$|import$)[^/]+\/edit$/, perms: ["customer.update"] },
  {
    pattern: /^\/doctors\/(?!create$)[^/]+\/edit$/,
    perms: ["doctor.update", "customer.update", "sale-invoice.create", "sale-invoice.update"],
  },
  { pattern: /^\/brands\/(?!create$)[^/]+\/edit$/, perms: ["brand.update"] },
  { pattern: /^\/products\/(?!create$|import$)[^/]+\/edit$/, perms: ["product.update"] },
  { pattern: /^\/purchase-invoices\/(?!create$)[^/]+\/edit$/, perms: ["purchase-invoice.update"] },
  { pattern: /^\/purchase-returns\/(?!create$)[^/]+\/edit$/, perms: ["purchase-return.update"] },
  {
    pattern: /^\/sale-invoices\/(?!pos$|barcode-pos$|create$|create-retail$|create-wholesale$)[^/]+\/edit$/,
    perms: ["sale-invoice.update"],
  },
  {
    pattern: /^\/trade-schemes\/(?!create$)[^/]+\/edit$/,
    perms: ["trade-scheme.update", "trade-scheme.manage", "sale-invoice.update"],
  },
  { pattern: /^\/sale-returns\/(?!create$)[^/]+\/edit$/, perms: ["sale-return.update"] },
  { pattern: /^\/purchase-orders\/(?!create$|forecast$)[^/]+\/edit$/, perms: ["purchase-order.update"] },
  { pattern: /^\/stocktake-sessions\/(?!create$)[^/]+\/review$/, perms: ["stocktake.view", "stocktake.finalize"] },
  { pattern: /^\/stocktake-sessions\/(?!create$)[^/]+\/edit$/, perms: ["stocktake.view", "stocktake.update"] },
  { pattern: /^\/users\/(?!create$)[^/]+\/edit$/, perms: ["user.update", "user.manage"] },
  { pattern: /^\/roles\/(?!create$)[^/]+\/edit$/, perms: ["role.update"] },

  // Detail (`/:id`) routes second with static-segment negative lookaheads
  { pattern: /^\/customers\/(?!create$|import$)[^/]+$/, perms: ["customer.view"] },
  { pattern: /^\/purchase-invoices\/(?!create$)[^/]+$/, perms: ["purchase-invoice.view"] },
  { pattern: /^\/purchase-returns\/(?!create$)[^/]+$/, perms: ["purchase-return.view"] },
  {
    pattern: /^\/sale-invoices\/(?!pos$|barcode-pos$|create$|create-retail$|create-wholesale$)[^/]+$/,
    perms: ["sale-invoice.view"],
  },
  { pattern: /^\/wholesale-invoices\/(?!create$)[^/]+$/, perms: ["sale-invoice.view"] },
  { pattern: /^\/sale-returns\/(?!create$)[^/]+$/, perms: ["sale-return.view"] },
  { pattern: /^\/purchase-orders\/(?!create$|forecast$)[^/]+$/, perms: ["purchase-order.view"] },
  { pattern: /^\/stock-adjustments\/(?!create$)[^/]+$/, perms: ["stock-adjustment.view"] },
  { pattern: /^\/stocktake-sessions\/(?!create$)[^/]+$/, perms: ["stocktake.view", "stocktake.update"] },
  { pattern: /^\/stock-disposals\/(?!create$)[^/]+$/, perms: ["stock-disposal.view"] },
];

/**
 * Normalizes a route pathname: strips `?query` and `#hash`, collapses repeated slashes,
 * trims trailing slashes (preserving `"/"`), and lowercases.
 */
export function normalizeRoutePath(pathname) {
  if (pathname === null || pathname === undefined) return "/";
  let clean = String(pathname).trim();
  clean = clean.split("?")[0].split("#")[0];
  clean = clean.replace(/\/+/g, "/");
  if (clean.length > 1) {
    clean = clean.replace(/\/+$/, "");
  }
  if (!clean.startsWith("/")) {
    clean = `/${clean}`;
  }
  return clean.toLowerCase();
}

export function resolveRoutePermissions(pathname) {
  const clean = normalizeRoutePath(pathname);
  if (Object.prototype.hasOwnProperty.call(ROUTE_PERMISSION_MAP, clean)) {
    return ROUTE_PERMISSION_MAP[clean];
  }
  for (const entry of DYNAMIC_ROUTE_PERMISSIONS) {
    if (entry.pattern.test(clean)) {
      return entry.perms;
    }
  }
  return null;
}

export function resolveRouteLicenseModule(pathname) {
  const clean = normalizeRoutePath(pathname);
  return Object.prototype.hasOwnProperty.call(ROUTE_LICENSE_MODULE_MAP, clean)
    ? ROUTE_LICENSE_MODULE_MAP[clean]
    : null;
}

/**
 * Determines whether ProtectedRoute should render a loading skeleton instead of children
 * while license validation is still pending.
 *
 * Preserves `DashboardLayout` (`shellOnly && !fullScreen`) so `<SessionExpiryModal />`
 * remains mounted when `/api/license/status` returns 401, while gating all inner pages
 * (`!shellOnly`) and fullscreen routes (`fullScreen`, e.g. `/setup`, `/sale-invoices/pos`).
 */
export function shouldRenderLicenseSkeleton({ licenseLoading, shellOnly, fullScreen } = {}) {
  if (!licenseLoading) return false;
  return !shellOnly || Boolean(fullScreen);
}
