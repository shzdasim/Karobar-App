import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import {
  PUBLIC_ROUTES,
  OPEN_AUTHENTICATED_ROUTES,
  ROUTE_LICENSE_MODULE_MAP,
  ROUTE_PERMISSION_MAP,
  DYNAMIC_ROUTE_PERMISSIONS,
  normalizeRoutePath,
  resolveRoutePermissions,
  resolveRouteLicenseModule,
  shouldRenderLicenseSkeleton,
} from "../../resources/js/utils/routePermissions.js";

function materializeRouteSample(routePath) {
  return routePath
    .replace(/:id\b/g, "42")
    .replace(/:token\b/g, "tok123")
    .replace(/:email\b/g, "user@example.com");
}

test("every <Route path> in resources/js/routes/index.jsx is covered by routePermissions", () => {
  const routesSource = fs.readFileSync(
    path.resolve("resources/js/routes/index.jsx"),
    "utf8"
  );
  const matches = [...routesSource.matchAll(/<Route\b[^>]*\bpath="([^"]+)"/g)].map(
    (m) => m[1]
  );
  assert.ok(matches.length >= 90, `Expected >= 90 routes in routes/index.jsx, found ${matches.length}`);

  const publicSet = new Set(PUBLIC_ROUTES.map((r) => (r === "*" ? "*" : normalizeRoutePath(r))));
  const openAuthSet = new Set(OPEN_AUTHENTICATED_ROUTES.map(normalizeRoutePath));

  const uncovered = [];
  for (const rawRoute of matches) {
    if (publicSet.has(rawRoute)) continue;
    const normalized = normalizeRoutePath(rawRoute);
    if (publicSet.has(normalized) || openAuthSet.has(normalized)) continue;

    const sample = materializeRouteSample(rawRoute);
    const perms = resolveRoutePermissions(sample);
    if (!Array.isArray(perms) || perms.length === 0) {
      uncovered.push(rawRoute);
    }
  }

  assert.deepEqual(
    uncovered,
    [],
    `Uncovered routes in resources/js/routes/index.jsx: ${uncovered.join(", ")}`
  );
});

test("normalizeRoutePath, resolveRoutePermissions, and resolveRouteLicenseModule handle trailing slashes and casing", () => {
  assert.equal(normalizeRoutePath("/"), "/");
  assert.equal(normalizeRoutePath("/Products/?tab=1#top"), "/products");
  assert.equal(normalizeRoutePath("/ACCOUNTING///"), "/accounting");

  assert.deepEqual(resolveRoutePermissions("/Products/"), resolveRoutePermissions("/products"));
  assert.deepEqual(
    resolveRoutePermissions("/STOCKTAKE-SESSIONS/42/REVIEW/"),
    ["stocktake.view", "stocktake.finalize"]
  );
  assert.deepEqual(
    resolveRoutePermissions("/stocktake-sessions/42"),
    ["stocktake.view", "stocktake.update"]
  );
  assert.deepEqual(
    resolveRoutePermissions("/customers/42/edit"),
    ["customer.update"]
  );
  assert.deepEqual(resolveRoutePermissions("/customers/42"), ["customer.view"]);

  assert.equal(resolveRouteLicenseModule("/Accounting/"), "accounting_gl");
  assert.equal(resolveRouteLicenseModule("/HR-PAYROLL/"), "hr_payroll");
  assert.equal(resolveRouteLicenseModule("/Settings/Sqlite-Import/"), "cube_ai");
  assert.equal(resolveRouteLicenseModule("/dashboard"), null);
});

test("negative lookaheads prevent static sub-routes from falsely matching /:id rules", () => {
  assert.deepEqual(resolveRoutePermissions("/sale-invoices/pos"), ["barcode-pos.view", "barcode-pos.create"]);
  assert.deepEqual(resolveRoutePermissions("/sale-invoices/barcode-pos"), ["barcode-pos.view", "barcode-pos.create"]);
  assert.deepEqual(resolveRoutePermissions("/sale-invoices/create/retail"), ["sale-invoice.create"]);
  assert.deepEqual(resolveRoutePermissions("/sale-invoices/create-wholesale"), ["sale-invoice.create"]);
  assert.deepEqual(resolveRoutePermissions("/purchase-orders/forecast"), ["purchase-order.view", "purchase-order.generate"]);
  assert.deepEqual(resolveRoutePermissions("/purchase-orders/create"), ["purchase-order.create"]);
  assert.deepEqual(resolveRoutePermissions("/stocktake-sessions/create"), ["stocktake.create"]);
  assert.deepEqual(resolveRoutePermissions("/customers/create"), ["customer.create"]);
  assert.deepEqual(resolveRoutePermissions("/doctors/create"), [
    "doctor.create",
    "customer.create",
    "sale-invoice.create",
    "sale-invoice.update",
    "barcode-pos.create",
  ]);
  assert.ok(DYNAMIC_ROUTE_PERMISSIONS.length > 0 && Object.keys(ROUTE_PERMISSION_MAP).length > 70);
  assert.ok(Object.keys(ROUTE_LICENSE_MODULE_MAP).length >= 10);
});

test("shouldRenderLicenseSkeleton blocks inner and fullscreen pages while keeping DashboardLayout shell mounted", () => {
  // Inner ProtectedRoute inside DashboardLayout (shellOnly=false, fullScreen=false) -> MUST show skeleton
  assert.equal(
    shouldRenderLicenseSkeleton({ licenseLoading: true, shellOnly: false, fullScreen: false }),
    true
  );
  // Fullscreen ProtectedRoute (/setup has shellOnly=true, fullScreen=true) -> MUST show skeleton
  assert.equal(
    shouldRenderLicenseSkeleton({ licenseLoading: true, shellOnly: true, fullScreen: true }),
    true
  );
  // Fullscreen POS (/sale-invoices/pos has shellOnly=false, fullScreen=true) -> MUST show skeleton
  assert.equal(
    shouldRenderLicenseSkeleton({ licenseLoading: true, shellOnly: false, fullScreen: true }),
    true
  );
  // DashboardLayout wrapper (shellOnly=true, fullScreen=false) -> stays mounted so SessionExpiryModal can render on 401
  assert.equal(
    shouldRenderLicenseSkeleton({ licenseLoading: true, shellOnly: true, fullScreen: false }),
    false
  );
  // Once licenseLoading finishes -> never render license skeleton
  assert.equal(
    shouldRenderLicenseSkeleton({ licenseLoading: false, shellOnly: false, fullScreen: false }),
    false
  );
});
