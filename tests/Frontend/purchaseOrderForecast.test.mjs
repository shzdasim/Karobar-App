import test from "node:test";
import assert from "node:assert/strict";
import {
  sanitizeOrderPacksInput,
  parseOrderPacksValue,
  updateForecastRowPacks,
  finalizeForecastRowPacks,
  buildForecastQueryParams,
  getForecastFilterSignature,
  buildCreateOrderPayload,
} from "../../resources/js/utils/purchaseOrderForecast.js";

test("multi-digit quantity entry works cleanly without losing digits", () => {
  let rows = [
    {
      _rowId: "10-0",
      product_id: 10,
      product_name: "Panadol 500mg",
      pack_size: 10,
      pack_price: 250,
      suggested_packs: 5,
      order_packs: 5,
      order_packs_input: "5",
      order_units: 50,
      order_amount: 1250,
    },
  ];

  // Clear the field while editing -> keeps "" without forcing "0"
  rows = updateForecastRowPacks(rows, "10-0", "");
  assert.equal(rows[0].order_packs_input, "");
  assert.equal(rows[0].order_packs, 0);
  assert.equal(rows[0].order_units, 0);
  assert.equal(rows[0].order_amount, 0);

  // Type "5", then "50", then "500"
  rows = updateForecastRowPacks(rows, "10-0", "5");
  assert.equal(rows[0].order_packs_input, "5");
  assert.equal(rows[0].order_packs, 5);

  rows = updateForecastRowPacks(rows, "10-0", "50");
  assert.equal(rows[0].order_packs_input, "50");
  assert.equal(rows[0].order_packs, 50);
  assert.equal(rows[0].order_units, 500);
  assert.equal(rows[0].order_amount, 12500);

  rows = updateForecastRowPacks(rows, "10-0", "500");
  assert.equal(rows[0].order_packs_input, "500");
  assert.equal(rows[0].order_packs, 500);
  assert.equal(rows[0].order_units, 5000);

  // Strips non-digits and leading zeros cleanly
  assert.equal(sanitizeOrderPacksInput("050"), "50");
  assert.equal(sanitizeOrderPacksInput("00"), "0");
  assert.equal(sanitizeOrderPacksInput("12abc3"), "123");
  assert.equal(parseOrderPacksValue("50"), 50);

  // Finalize empty input on blur -> "0"
  rows = updateForecastRowPacks(rows, "10-0", "");
  rows = finalizeForecastRowPacks(rows, "10-0");
  assert.equal(rows[0].order_packs_input, "0");
  assert.equal(rows[0].order_packs, 0);
});

test("buildForecastQueryParams serializes repeated brand_ids and strict supplier options", () => {
  const params = buildForecastQueryParams({
    dateFrom: "2026-10-01",
    dateTo: "2026-10-30",
    projectedDays: 14,
    safetyPacks: 2,
    moqPacks: 1,
    supplier: { value: 7, label: "Acme Pharma" },
    brands: [
      { value: 3, label: "GSK" },
      { value: 8, label: "Abbott" },
      { value: 3, label: "GSK Duplicate" },
    ],
    includeSecondarySuppliers: false,
    deductOnOrder: true,
  });

  assert.equal(params.get("date_from"), "2026-10-01");
  assert.equal(params.get("date_to"), "2026-10-30");
  assert.equal(params.get("projected_days"), "14");
  assert.equal(params.get("safety_packs"), "2");
  assert.equal(params.get("moq_packs"), "1");
  assert.equal(params.get("supplier_id"), "7");
  assert.equal(params.get("include_secondary_suppliers"), "false");
  assert.equal(params.get("deduct_on_order"), "true");
  assert.deepEqual(params.getAll("brand_ids"), ["3", "8"]);
});

test("filter signature detects supplier and brand changes for stale warning", () => {
  const sig1 = getForecastFilterSignature({
    dateFrom: "2026-10-01",
    dateTo: "2026-10-30",
    projectedDays: 7,
    supplier: { value: 1, label: "Sup A" },
    brands: [{ value: 2, label: "Brand X" }],
  });
  const sig2 = getForecastFilterSignature({
    dateFrom: "2026-10-01",
    dateTo: "2026-10-30",
    projectedDays: 7,
    supplier: { value: 2, label: "Sup B" },
    brands: [{ value: 2, label: "Brand X" }],
  });
  const sig3 = getForecastFilterSignature({
    dateFrom: "2026-10-01",
    dateTo: "2026-10-30",
    projectedDays: 7,
    supplier: { value: 1, label: "Sup A" },
    brands: [{ value: 2, label: "Brand X" }],
  });
  assert.notEqual(sig1, sig2);
  assert.equal(sig1, sig3);
});

test("buildCreateOrderPayload passes supplier and flags mixed-supplier lists", () => {
  const single = buildCreateOrderPayload({
    orderRows: [
      {
        product_id: 101,
        product_name: "Item A",
        product_code: "P-101",
        brand_id: 1,
        brand_name: "GSK",
        supplier_id: 5,
        supplier_name: "Alpha Dist",
        pack_size: 10,
        pack_price: 500,
        order_packs: 4,
      },
    ],
    appliedSupplier: { value: 5, label: "Alpha Dist" },
  });
  assert.equal(single.hasMultipleSuppliers, false);
  assert.deepEqual(single.supplier, { id: 5, name: "Alpha Dist" });
  assert.equal(single.items[0].ordered_quantity, 40);
  assert.equal(single.items[0].unit_purchase_price, 50);

  const mixed = buildCreateOrderPayload({
    orderRows: [
      {
        product_id: 101,
        product_name: "Item A",
        supplier_id: 5,
        supplier_name: "Alpha Dist",
        pack_size: 10,
        pack_price: 500,
        order_packs: 2,
      },
      {
        product_id: 102,
        product_name: "Item B",
        supplier_id: 9,
        supplier_name: "Beta Dist",
        pack_size: 10,
        pack_price: 300,
        order_packs: 3,
      },
    ],
    appliedSupplier: null,
  });
  assert.equal(mixed.hasMultipleSuppliers, true);
  assert.equal(mixed.distinctSuppliers.length, 2);
  assert.equal(mixed.supplier, null);
});
