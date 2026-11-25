import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const ROOT = process.cwd();

test("SaleInvoiceWholesaleForm imports Select and defines paymentSelectStyles", () => {
  const src = fs.readFileSync(
    path.join(ROOT, "resources/js/pages/SaleInvoice/SaleInvoiceWholesaleForm.jsx"),
    "utf8"
  );
  assert.ok(src.includes('import Select from "react-select"'), "Must import Select from react-select");
  assert.ok(src.includes("const paymentSelectStyles = useMemo"), "Must define paymentSelectStyles");
});
