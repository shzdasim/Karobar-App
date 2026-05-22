// resources/js/Formula/Quotation.js

// Quotation line items (no stock batches in this module)
// We reuse the same calculation model as SaleInvoice:
// - quantity is units
// - price is unit price
// - item_discount_percentage can be negative/positive

const n = (v) => (v === "" || v === null || v === undefined ? 0 : Number(v) || 0);
const r2 = (v) => (Number.isFinite(v) ? Number(v.toFixed(2)) : 0);

export function recalcQuotationItem(item) {
  const qty = n(item.quantity);
  const price = n(item.price);
  const discPct = n(item.item_discount_percentage);

  const gross = qty * price;
  const itemDisc = (gross * discPct) / 100;
  const subTotal = gross - itemDisc;

  return {
    ...item,
    quantity: qty,
    price,
    item_discount_percentage: discPct,
    sub_total: r2(subTotal),
  };
}

export function recalcQuotationFooter(form, changedField = null) {
  const items = form.items || [];

  const grossSum = items.reduce((sum, it) => sum + n(it.quantity) * n(it.price), 0);
  const itemDiscSum = items.reduce((sum, it) => {
    const discPct = n(it.item_discount_percentage);
    const gross = n(it.quantity) * n(it.price);
    return sum + (gross * discPct) / 100;
  }, 0);

  let discountPct = n(form.discount_percentage);
  let discountAmt = n(form.discount_amount);

  if (changedField === "discount_percentage") {
    discountAmt = (grossSum * discountPct) / 100;
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
  const totalRounded = Math.round(totalFloat);

  return {
    ...form,
    discount_percentage: discountPct === 0 ? "" : String(r2(discountPct)),
    discount_amount: discountAmt === 0 ? "" : String(r2(discountAmt)),
    tax_percentage: taxPct === 0 ? "" : String(r2(taxPct)),
    tax_amount: taxAmt === 0 ? "" : String(r2(taxAmt)),
    item_discount: r2(itemDiscSum),
    gross_amount: r2(grossSum),
    total: String(totalRounded),
  };
}

