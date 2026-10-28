// resources/js/Formula/SaleInvoice.js

// Safe numeric coercion
const n = (v) => (v === "" || v === null || v === undefined ? 0 : Number(v) || 0);

// Round to 2 decimals
const r2 = (v) => (Number.isFinite(v) ? Number(v.toFixed(2)) : 0);

// Recalculate a single sale item (retail)
export function recalcItem(item, changedField = null) {
  const packSize = n(item.pack_size);
  const qty = n(item.quantity);        // units
  const price = n(item.price);         // unit price
  const discPct = n(item.item_discount_percentage); // may be negative

  // derived
  const gross = qty * price;
  const itemDisc = (gross * discPct) / 100; // negative disc increases subtotal
  const subTotal = gross - itemDisc;

  return {
    ...item,
    pack_size: packSize,
    quantity: qty,
    price,
    item_discount_percentage: discPct,
    sub_total: r2(subTotal),
  };
}

// Recalculate a single wholesale sale item (supports simultaneous Pack Qty + Unit Qty + Pack Bonus + Unit Bonus + Pack Price + Unit Price)
export function recalcWholesaleItem(item, changedField = null) {
  const packSize = Math.max(1, n(item.pack_size));
  const packQty = Math.max(0, Math.floor(n(item.pack_quantity)));
  const unitQty = Math.max(0, Math.floor(n(item.unit_quantity)));
  const packBonus = Math.max(0, Math.floor(n(item.pack_bonus)));
  const unitBonus = Math.max(0, Math.floor(n(item.unit_bonus)));

  const hasSplitBonus =
    changedField === "pack_bonus" ||
    changedField === "unit_bonus" ||
    item.pack_bonus !== undefined ||
    item.unit_bonus !== undefined;

  const bonusQty = hasSplitBonus
    ? packBonus * packSize + unitBonus
    : Math.max(0, Math.floor(n(item.bonus)));

  const hasSplitQty =
    changedField === "pack_quantity" ||
    changedField === "unit_quantity" ||
    item.pack_quantity !== undefined ||
    item.unit_quantity !== undefined;

  const totalQty = hasSplitQty
    ? packQty * packSize + unitQty
    : Math.max(0, Math.floor(n(item.quantity)));

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

  return {
    ...form,
    discount_percentage: discountPct === 0 ? "" : String(r2(discountPct)),
    discount_amount: discountAmt === 0 ? "" : String(r2(discountAmt)),
    tax_percentage: taxPct === 0 ? "" : String(r2(taxPct)),
    tax_amount: taxAmt === 0 ? "" : String(r2(taxAmt)),
    item_discount: r2(itemDiscSum),
    gross_amount: r2(grossSum),
    total: String(totalRounded), // integer string, no decimals
  };
}
