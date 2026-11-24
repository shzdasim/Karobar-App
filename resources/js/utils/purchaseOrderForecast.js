// Pure helpers for Purchase Order Forecast input handling, query serialization, and PO handoff.

const MAX_ORDER_PACKS = 999999;

/**
 * Sanitize raw numeric input for Order Packs while allowing empty string during typing
 * and multi-digit entry without stripping digits or forcing "0" prematurely.
 */
export function sanitizeOrderPacksInput(rawValue) {
  if (rawValue === null || rawValue === undefined) return "";
  const digitsOnly = String(rawValue).replace(/[^\d]/g, "");
  if (digitsOnly === "") return "";
  const normalized = digitsOnly.replace(/^0+(?=\d)/, "");
  const numeric = Math.min(MAX_ORDER_PACKS, parseInt(normalized, 10) || 0);
  return String(numeric);
}

/**
 * Parse sanitized or raw Order Packs input into a safe non-negative integer.
 */
export function parseOrderPacksValue(rawValue) {
  const sanitized = sanitizeOrderPacksInput(rawValue);
  if (!sanitized) return 0;
  return Math.max(0, Math.min(MAX_ORDER_PACKS, parseInt(sanitized, 10) || 0));
}

/**
 * Update a single forecast row's Order Packs from live typing.
 * Preserves the editable string (including "") in `order_packs_input`
 * while keeping numeric `order_packs`, `order_units`, and `order_amount` in sync.
 */
export function updateForecastRowPacks(rows, rowId, rawValue) {
  const inputStr = sanitizeOrderPacksInput(rawValue);
  const packs = parseOrderPacksValue(inputStr);

  return rows.map((row) => {
    if (row._rowId !== rowId) return row;
    const packSize = Math.max(1, Number(row.pack_size || 1));
    const packPrice = Number(row.pack_price || 0);
    return {
      ...row,
      order_packs_input: inputStr,
      order_packs: packs,
      order_units: packs * packSize,
      order_amount: packs * packPrice,
    };
  });
}

/**
 * Normalize a row's editable input string on blur (e.g. "" -> "0").
 */
export function finalizeForecastRowPacks(rows, rowId) {
  return rows.map((row) => {
    if (row._rowId !== rowId) return row;
    const packs = Math.max(0, parseOrderPacksValue(row.order_packs_input ?? row.order_packs));
    const packSize = Math.max(1, Number(row.pack_size || 1));
    const packPrice = Number(row.pack_price || 0);
    return {
      ...row,
      order_packs_input: String(packs),
      order_packs: packs,
      order_units: packs * packSize,
      order_amount: packs * packPrice,
    };
  });
}

/**
 * Build URLSearchParams for GET /api/purchase-orders/forecast.
 * Uses repeated `brand_ids` entries (e.g. brand_ids=1&brand_ids=2) so ASP.NET Core
 * binds multiple brands cleanly without bracket-encoding ambiguity.
 */
export function buildForecastQueryParams({
  dateFrom,
  dateTo,
  projectedDays,
  safetyPacks = 0,
  moqPacks = 0,
  supplier = null,
  brands = [],
  includeSecondarySuppliers = false,
  deductOnOrder = true,
}) {
  const params = new URLSearchParams();
  params.set("date_from", String(dateFrom || ""));
  params.set("date_to", String(dateTo || ""));
  params.set("projected_days", String(Math.max(1, parseInt(projectedDays, 10) || 1)));
  params.set("safety_packs", String(Math.max(0, parseInt(safetyPacks, 10) || 0)));
  params.set("moq_packs", String(Math.max(0, parseInt(moqPacks, 10) || 0)));
  params.set("include_secondary_suppliers", includeSecondarySuppliers ? "true" : "false");
  params.set("deduct_on_order", deductOnOrder ? "true" : "false");

  const supplierId = Number(supplier?.value ?? supplier?.id ?? 0);
  if (supplierId > 0) {
    params.set("supplier_id", String(supplierId));
  }

  const seenBrandIds = new Set();
  for (const b of brands || []) {
    const bid = Number(b?.value ?? b?.id ?? b);
    if (bid > 0 && !seenBrandIds.has(bid)) {
      seenBrandIds.add(bid);
      params.append("brand_ids", String(bid));
    }
  }

  return params;
}

/**
 * Produce a stable signature string of query parameters to detect when user edits
 * filter controls after generating a forecast.
 */
export function getForecastFilterSignature({
  dateFrom,
  dateTo,
  projectedDays,
  safetyPacks = 0,
  moqPacks = 0,
  supplier = null,
  brands = [],
  includeSecondarySuppliers = false,
  deductOnOrder = true,
}) {
  const supplierId = Number(supplier?.value ?? supplier?.id ?? 0);
  const brandIds = Array.from(
    new Set(
      (brands || [])
        .map((b) => Number(b?.value ?? b?.id ?? b))
        .filter((id) => id > 0)
    )
  ).sort((a, b) => a - b);

  return JSON.stringify({
    dateFrom: String(dateFrom || ""),
    dateTo: String(dateTo || ""),
    projectedDays: Math.max(1, parseInt(projectedDays, 10) || 1),
    safetyPacks: Math.max(0, parseInt(safetyPacks, 10) || 0),
    moqPacks: Math.max(0, parseInt(moqPacks, 10) || 0),
    supplierId,
    brandIds,
    includeSecondarySuppliers: Boolean(includeSecondarySuppliers),
    deductOnOrder: Boolean(deductOnOrder),
  });
}

/**
 * Inspect the rows selected for PO creation and build navigation state for /purchase-orders/create.
 * Resolves the supplier automatically when a supplier filter is active or all order rows
 * share a single supplier, and reports distinct suppliers so mixed-supplier POs are surfaced.
 */
export function buildCreateOrderPayload({
  orderRows = [],
  appliedSupplier = null,
  resultSupplierKey = "all",
  availableSuppliers = [],
}) {
  const supplierMap = new Map();
  for (const row of orderRows) {
    const sid = Number(row.supplier_id || 0);
    if (sid > 0 && !supplierMap.has(sid)) {
      supplierMap.set(sid, {
        id: sid,
        name: row.supplier_name || `Supplier #${sid}`,
      });
    }
  }

  const distinctSuppliers = Array.from(supplierMap.values());

  let resolvedSupplier = null;
  if (resultSupplierKey !== "all" && resultSupplierKey !== "none") {
    const match = availableSuppliers.find((s) => String(s.key) === String(resultSupplierKey));
    if (match && Number(match.id) > 0) {
      resolvedSupplier = { id: Number(match.id), name: match.name };
    }
  }
  if (!resolvedSupplier && appliedSupplier && Number(appliedSupplier.value || appliedSupplier.id) > 0) {
    resolvedSupplier = {
      id: Number(appliedSupplier.value || appliedSupplier.id),
      name: appliedSupplier.label || appliedSupplier.name,
    };
  }
  if (!resolvedSupplier && distinctSuppliers.length === 1) {
    resolvedSupplier = distinctSuppliers[0];
  }

  const items = orderRows.map((row) => {
    const packSize = Math.max(1, Number(row.pack_size || 1));
    const packPrice = Number(row.pack_price || 0);
    const orderedPacks = Math.max(1, parseOrderPacksValue(row.order_packs));
    const orderedUnits = orderedPacks * packSize;
    return {
      product_id: row.product_id,
      product: {
        id: row.product_id,
        name: row.product_name,
        product_code: row.product_code || "",
        pack_size: packSize,
        pack_purchase_price: packPrice,
        unit_purchase_price: packPrice / packSize,
        brand_id: row.brand_id ?? null,
        brand_name: row.brand_name || "",
        brand: row.brand_id ? { id: row.brand_id, name: row.brand_name || "" } : null,
      },
      ordered_quantity: orderedUnits,
      pack_size: packSize,
      unit_purchase_price: packPrice / packSize,
      item_discount_percentage: 0,
    };
  });

  return {
    items,
    supplier: resolvedSupplier,
    distinctSuppliers,
    hasMultipleSuppliers: distinctSuppliers.length > 1,
  };
}

/**
 * Calculates any necessary scrollTop delta to keep a table row fully visible
 * between a sticky header (thead) and sticky footer (tfoot).
 *
 * @param {object} params
 * @param {number} params.trTop - Element getBoundingClientRect().top
 * @param {number} params.trBottom - Element getBoundingClientRect().bottom
 * @param {number} params.wrapTop - Container getBoundingClientRect().top
 * @param {number} params.wrapBottom - Container getBoundingClientRect().bottom
 * @param {number} [params.headerHeight=44] - Height of sticky table header
 * @param {number} [params.footerHeight=54] - Height of sticky table footer (totals row)
 * @param {number} [params.padding=12] - Additional safety breathing room in pixels
 * @returns {number} delta in pixels to add to container.scrollTop (positive scrolls down, negative scrolls up, 0 if already visible)
 */
export function calculateRowScrollOffset({
  trTop,
  trBottom,
  wrapTop,
  wrapBottom,
  headerHeight = 44,
  footerHeight = 54,
  padding = 12,
}) {
  const minVisibleTop = wrapTop + headerHeight;
  const maxVisibleBottom = wrapBottom - footerHeight;

  // If row is hidden or partially obstructed by sticky bottom footer (tfoot)
  if (trBottom > maxVisibleBottom - padding) {
    return trBottom - maxVisibleBottom + padding;
  }

  // If row is hidden or partially obstructed by sticky top header (thead)
  if (trTop < minVisibleTop + padding) {
    return trTop - (minVisibleTop + padding);
  }

  return 0;
}

