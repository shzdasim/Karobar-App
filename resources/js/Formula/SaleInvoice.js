// resources/js/Formula/SaleInvoice.js

// Safe numeric coercion
const n = (v) => (v === "" || v === null || v === undefined ? 0 : Number(v) || 0);

// Round to 2 decimals
const r2 = (v) => (Number.isFinite(v) ? Number(v.toFixed(2)) : 0);

/**
 * Resolve unit cost for a sale line item using moving average cost (avg_price)
 * with fallback to unit_purchase_price or pack_purchase_price / pack_size.
 */
export function resolveItemUnitCost(item, productObj = null) {
  if (!item && !productObj) return 0;
  const packSize = Math.max(1, n(item?.pack_size || productObj?.pack_size || 1));
  const avgPrice = n(item?.avg_price || productObj?.avg_price || item?.product?.avg_price);
  if (avgPrice > 0) return avgPrice;

  const unitPurchase =
    n(item?.unit_purchase_price || productObj?.unit_purchase_price || item?.product?.unit_purchase_price) ||
    (n(item?.pack_purchase_price || productObj?.pack_purchase_price || item?.product?.pack_purchase_price) > 0
      ? n(item?.pack_purchase_price || productObj?.pack_purchase_price || item?.product?.pack_purchase_price) / packSize
      : 0);

  return unitPurchase > 0 ? unitPurchase : 0;
}

/**
 * Compute detailed profit & margin metrics for a sale row, accounting for:
 * - Billed quantity (qty)
 * - Free bonus units (bonus) -> increases COGS without adding revenue
 * - Item-level discount %
 * - Optional bill-level discount %
 */
export function calculateRowProfitMetrics(item, productObj = null, billDiscPct = 0) {
  if (!item) return null;
  const packSize = Math.max(1, n(item.pack_size || productObj?.pack_size || 1));
  const unitPrice = n(item.price);
  const unitCost = resolveItemUnitCost(item, productObj);
  if (unitPrice <= 0 || unitCost <= 0) return null;

  const qty = Math.max(0, n(item.quantity));
  const bonus = Math.max(
    0,
    Math.floor(n(item.bonus || item.unit_bonus || n(item.pack_bonus) * packSize))
  );
  const itemDiscPct = n(item.item_discount_percentage);
  const invoiceDiscPct = Math.max(0, n(billDiscPct));

  const netUnitPriceBeforeBillDisc = unitPrice * (1 - itemDiscPct / 100);
  const netUnitPrice = netUnitPriceBeforeBillDisc * (1 - invoiceDiscPct / 100);

  // Base margin without bonus or discounts
  const baseMarginPct = r2(((unitPrice - unitCost) / unitPrice) * 100);

  if (qty > 0) {
    const grossRevenue = qty * unitPrice;
    const lineSubTotal = item.sub_total !== "" && item.sub_total !== undefined && item.sub_total !== null
      ? n(item.sub_total)
      : qty * netUnitPriceBeforeBillDisc;
    const netRevenue = lineSubTotal * (1 - invoiceDiscPct / 100);
    const totalOutUnits = qty + bonus;
    const totalCost = totalOutUnits * unitCost;
    const netProfit = netRevenue - totalCost;
    const effectiveUnitPrice = totalOutUnits > 0 ? netRevenue / totalOutUnits : 0;
    const marginPct = netRevenue > 0 ? r2(((netRevenue - totalCost) / netRevenue) * 100) : -100;

    return {
      unitPrice: r2(unitPrice),
      unitCost: r2(unitCost),
      effectiveUnitPrice: r2(effectiveUnitPrice),
      billedUnits: qty,
      bonusUnits: bonus,
      totalOutUnits,
      grossRevenue: r2(grossRevenue),
      netRevenue: r2(netRevenue),
      totalCost: r2(totalCost),
      netProfit: r2(netProfit),
      baseMarginPct,
      marginPct,
    };
  }

  const marginPct = netUnitPrice > 0 ? r2(((netUnitPrice - unitCost) / netUnitPrice) * 100) : -100;
  return {
    unitPrice: r2(unitPrice),
    unitCost: r2(unitCost),
    effectiveUnitPrice: r2(netUnitPrice),
    billedUnits: 0,
    bonusUnits: bonus,
    totalOutUnits: bonus,
    grossRevenue: 0,
    netRevenue: 0,
    totalCost: r2(bonus * unitCost),
    netProfit: r2(-bonus * unitCost),
    baseMarginPct,
    marginPct,
  };
}

/**
 * Compute live realized margin % for a sale row.
 */
export function calculateRowMargin(item, productObj = null, billDiscPct = 0) {
  const metrics = calculateRowProfitMetrics(item, productObj, billDiscPct);
  return metrics ? metrics.marginPct : null;
}

// Recalculate a single sale item (retail)
export function recalcItem(item, changedField = null) {
  const packSize = n(item.pack_size);
  const qty = n(item.quantity);        // units
  const bonus = Math.max(0, Math.floor(n(item.bonus))); // free bonus units
  const price = n(item.price);         // unit price
  const discPct = n(item.item_discount_percentage); // may be negative

  // derived
  const gross = qty * price;
  const itemDisc = (gross * discPct) / 100; // negative disc increases subtotal
  const subTotal = gross - itemDisc;

  const unitCost = resolveItemUnitCost(item);
  let margin = item.margin ?? "";
  if (price > 0 && unitCost > 0) {
    const netUnitPrice = price * (1 - discPct / 100);
    if (qty > 0 && bonus > 0) {
      if (subTotal > 0) {
        const totalRowCost = (qty + bonus) * unitCost;
        margin = r2(((subTotal - totalRowCost) / subTotal) * 100);
      } else {
        margin = -100;
      }
    } else if (netUnitPrice > 0) {
      margin = r2(((netUnitPrice - unitCost) / netUnitPrice) * 100);
    } else {
      margin = -100;
    }
  } else if (price <= 0) {
    margin = "";
  }

  return {
    ...item,
    pack_size: packSize,
    quantity: qty,
    price,
    item_discount_percentage: discPct,
    margin,
    sub_total: r2(subTotal),
  };
}

// Recalculate a single wholesale sale item (two-way Pack <-> Unit sync for quantity, bonus, and price, plus live margin from avg_price)
export function recalcWholesaleItem(item, changedField = null) {
  const packSize = Math.max(1, n(item.pack_size));
  let packQty = Math.max(0, n(item.pack_quantity));
  let unitQty = Math.max(0, Math.floor(n(item.unit_quantity)));
  const rawQty = Math.max(0, Math.floor(n(item.quantity)));

  if (changedField === "pack_size") {
    if (packQty > 0) unitQty = Math.round(packQty * packSize);
    else if (unitQty > 0) packQty = r2(unitQty / packSize);
  } else if (changedField === "pack_quantity") {
    unitQty = Math.round(packQty * packSize);
  } else if (changedField === "unit_quantity") {
    packQty = packSize > 0 ? r2(unitQty / packSize) : 0;
  } else {
    if (unitQty > 0) {
      packQty = packSize > 0 ? r2(unitQty / packSize) : 0;
    } else if (packQty > 0) {
      unitQty = Math.round(packQty * packSize);
    } else if (rawQty > 0) {
      unitQty = rawQty;
      packQty = packSize > 0 ? r2(unitQty / packSize) : 0;
    }
  }

  const totalQty = unitQty;

  let packBonus = Math.max(0, n(item.pack_bonus));
  let unitBonus = Math.max(0, Math.floor(n(item.unit_bonus)));
  const rawBonus = Math.max(0, Math.floor(n(item.bonus)));

  if (changedField === "pack_size") {
    if (packBonus > 0) unitBonus = Math.round(packBonus * packSize);
    else if (unitBonus > 0) packBonus = r2(unitBonus / packSize);
  } else if (changedField === "pack_bonus") {
    unitBonus = Math.round(packBonus * packSize);
  } else if (changedField === "unit_bonus") {
    packBonus = packSize > 0 ? r2(unitBonus / packSize) : 0;
  } else {
    if (unitBonus > 0) {
      packBonus = packSize > 0 ? r2(unitBonus / packSize) : 0;
    } else if (packBonus > 0) {
      unitBonus = Math.round(packBonus * packSize);
    } else if (rawBonus > 0) {
      unitBonus = rawBonus;
      packBonus = packSize > 0 ? r2(unitBonus / packSize) : 0;
    }
  }

  const bonusQty = unitBonus;

  let packPrice = n(item.pack_price);
  let unitPrice = n(item.price);

  if (changedField === "pack_price") {
    unitPrice = packSize > 0 ? r2(packPrice / packSize) : r2(packPrice);
  } else if (changedField === "price") {
    packPrice = packSize > 0 ? r2(unitPrice * packSize) : r2(unitPrice);
  } else if (changedField === "pack_size") {
    if (packPrice > 0) unitPrice = r2(packPrice / packSize);
    else if (unitPrice > 0) packPrice = r2(unitPrice * packSize);
  } else {
    if (packPrice > 0 && unitPrice === 0) {
      unitPrice = r2(packPrice / packSize);
    } else if (unitPrice > 0 && packPrice === 0) {
      packPrice = r2(unitPrice * packSize);
    }
  }

  const discPct = n(item.item_discount_percentage);
  const gross = totalQty * unitPrice;
  const itemDisc = (gross * discPct) / 100;
  const subTotal = gross - itemDisc;

  const unitCost = resolveItemUnitCost(item);
  let margin = item.margin ?? "";
  if (unitPrice > 0 && unitCost > 0) {
    const netUnitPrice = unitPrice * (1 - discPct / 100);
    if (totalQty > 0 && bonusQty > 0 && subTotal > 0) {
      const totalRowCost = (totalQty + bonusQty) * unitCost;
      margin = r2(((subTotal - totalRowCost) / subTotal) * 100);
    } else if (netUnitPrice > 0) {
      margin = r2(((netUnitPrice - unitCost) / netUnitPrice) * 100);
    } else {
      margin = -100;
    }
  } else if (unitPrice <= 0) {
    margin = "";
  }

  const preserve = (fieldName, computedValue) =>
    changedField === fieldName ? item[fieldName] : computedValue;

  return {
    ...item,
    pack_size: packSize,
    pack_quantity: preserve("pack_quantity", packQty === 0 ? "" : String(packQty)),
    unit_quantity: preserve("unit_quantity", unitQty === 0 ? "" : String(unitQty)),
    quantity: totalQty,
    pack_bonus: preserve("pack_bonus", packBonus === 0 ? "" : String(packBonus)),
    unit_bonus: preserve("unit_bonus", unitBonus === 0 ? "" : String(unitBonus)),
    bonus: bonusQty === 0 ? "" : String(bonusQty),
    pack_price: preserve("pack_price", packPrice),
    price: preserve("price", unitPrice),
    item_discount_percentage: preserve("item_discount_percentage", discPct === 0 ? "" : discPct),
    margin,
    sub_total: r2(subTotal),
  };
}

// Recalculate footer for the whole form
export function recalcFooter(form, changedField = null) {
  const items = form.items || [];
  const grossSum = items.reduce((sum, it) => sum + n(it.quantity) * n(it.price), 0);
  const itemDiscSum = items.reduce((sum, it) => {
    const discPct = n(it.item_discount_percentage); // may be negative
    const gross = n(it.quantity) * n(it.price);
    return sum + (gross * discPct) / 100;
  }, 0);

  let discountPct = n(form.discount_percentage); // may be negative
  let discountAmt = n(form.discount_amount);

  // If % changed, recompute amount; if amount changed, recompute %
  if (changedField === "discount_percentage") {
    discountAmt = (grossSum * discountPct) / 100; // negative % => negative amount
  } else if (changedField === "discount_amount") {
    discountPct = grossSum !== 0 ? (discountAmt / grossSum) * 100 : 0;
  }

  const taxableBase = grossSum - itemDiscSum - discountAmt;
  let taxPct = n(form.tax_percentage);
  let taxAmt = n(form.tax_amount);

  if (changedField === "tax_percentage") {
    taxAmt = (taxableBase * taxPct) / 100;
  } else if (changedField === "tax_amount") {
    taxPct = taxableBase !== 0 ? (taxAmt / taxableBase) * 100 : 0;
  }

  const totalFloat = taxableBase + taxAmt;
  const totalRounded = Math.round(totalFloat); // integer total

  const nextForm = {
    ...form,
    discount_percentage: discountPct === 0 ? "" : String(r2(discountPct)),
    discount_amount: discountAmt === 0 ? "" : String(r2(discountAmt)),
    tax_percentage: taxPct === 0 ? "" : String(r2(taxPct)),
    tax_amount: taxAmt === 0 ? "" : String(r2(taxAmt)),
    item_discount: r2(itemDiscSum),
    gross_amount: r2(grossSum),
    total: String(totalRounded), // integer string, no decimals
  };

  if ((form.invoice_type ?? "debit") !== "credit") {
    nextForm.total_receive = String(totalRounded);
  }

  return nextForm;
}
