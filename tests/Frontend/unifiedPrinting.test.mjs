import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const ROOT = process.cwd();
const read = (rel) => fs.readFileSync(path.join(ROOT, rel), "utf8");

test("Unified printing: all 5 transaction pages use printSlipUrl + /print-url instead of printCurrentView", () => {
  const pages = [
    { file: "resources/js/pages/PurchaseInvoice/Show.jsx", endpoint: "/api/purchase-invoices/${id}/print-url" },
    { file: "resources/js/pages/PurchaseInvoice/index.jsx", endpoint: "/api/purchase-invoices/${inv.id}/print-url" },
    { file: "resources/js/pages/SaleReturn/Index.jsx", endpoint: "/api/sale-returns/${returnId}/print-url" },
    { file: "resources/js/pages/PurchaseReturn/index.jsx", endpoint: "/api/purchase-returns/${returnId}/print-url" },
    { file: "resources/js/pages/PurchaseOrders.jsx", endpoint: "/api/purchase-orders/${orderId}/print-url" },
  ];

  for (const { file, endpoint } of pages) {
    const src = read(file);
    assert.equal(src.includes("printCurrentView"), false, `${file} must not reference printCurrentView`);
    assert.ok(src.includes("printSlipUrl"), `${file} must use printSlipUrl`);
    assert.ok(src.includes(endpoint), `${file} must call ${endpoint}`);
  }
});

test("Unified printing: DocumentSharingSetting and PrinterSetting expose all 5 doc_type preview options", () => {
  const docTypes = [
    "sale_invoice",
    "purchase_invoice",
    "sale_return",
    "purchase_return",
    "purchase_order",
  ];

  for (const file of [
    "resources/js/components/settings/DocumentSharingSetting.jsx",
    "resources/js/components/settings/PrinterSetting.jsx",
  ]) {
    const src = read(file);
    assert.ok(src.includes("doc_type"), `${file} must pass doc_type to preview iframe`);
    for (const dt of docTypes) {
      assert.ok(src.includes(dt), `${file} must include document type option ${dt}`);
    }
  }
});
