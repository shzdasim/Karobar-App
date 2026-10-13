const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
const text = (field, direction = 1) => (a, b) => {
  const left = String(a[field] ?? '').trim();
  const right = String(b[field] ?? '').trim();
  if (!left || !right) return left ? -1 : right ? 1 : 0;
  return direction * collator.compare(left, right);
};
const numeric = (field, direction = 1) => (a, b) => direction * (Number(a[field] || 0) - Number(b[field] || 0));
const date = (direction) => (a, b) => {
  const left = Date.parse(a.last_sold_date);
  const right = Date.parse(b.last_sold_date);
  if (!Number.isFinite(left) || !Number.isFinite(right)) return Number.isFinite(left) ? -1 : Number.isFinite(right) ? 1 : 0;
  return direction * (left - right);
};
const priority = (field) => (a, b) => Number(Boolean(Number(b[field]))) - Number(Boolean(Number(a[field])));

export const purchaseOrderArrangements = [
  { id: 'original', group: 'Document order', label: 'Original forecast order', compare: [] },
  { id: 'name_asc', group: 'Alphabetical', label: 'Product A–Z', compare: [text('product_name')] },
  { id: 'name_desc', group: 'Alphabetical', label: 'Product Z–A', compare: [text('product_name', -1)] },
  { id: 'brand_asc', group: 'Grouped', label: 'Brand A–Z → Product A–Z', compare: [text('brand_name'), text('product_name')] },
  { id: 'brand_desc', group: 'Grouped', label: 'Brand Z–A → Product A–Z', compare: [text('brand_name', -1), text('product_name')] },
  { id: 'supplier_asc', group: 'Grouped', label: 'Supplier A–Z → Product A–Z', compare: [text('supplier_name'), text('product_name')] },
  { id: 'supplier_desc', group: 'Grouped', label: 'Supplier Z–A → Product A–Z', compare: [text('supplier_name', -1), text('product_name')] },
  { id: 'supplier_brand', group: 'Grouped', label: 'Supplier → Brand → Product A–Z', compare: [text('supplier_name'), text('brand_name'), text('product_name')] },
  { id: 'brand_code', group: 'Grouped', label: 'Brand A–Z → Product Code', compare: [text('brand_name'), text('product_code')] },
  { id: 'code_asc', group: 'Product code', label: 'Product Code: Ascending', compare: [text('product_code')] },
  { id: 'code_desc', group: 'Product code', label: 'Product Code: Descending', compare: [text('product_code', -1)] },
  ...[
    ['packs', 'order_packs', 'Order Packs', 'Quantity and value', 'Lowest', 'Highest'],
    ['amount', 'order_amount', 'Order Amount', 'Quantity and value', 'Lowest', 'Highest'],
    ['stock', 'current_stock_units', 'Current Stock', 'Stock and demand', 'Lowest', 'Highest'],
    ['sales', 'units_sold', 'Sales / Demand Units', 'Stock and demand', 'Lowest', 'Highest'],
    ['price', 'pack_price', 'Pack Purchase Price', 'Pricing and packs', 'Lowest', 'Highest'],
    ['size', 'pack_size', 'Pack Size', 'Pricing and packs', 'Smallest', 'Largest'],
  ].flatMap(([id, field, label, group, low, high]) => [
    { id: `${id}_asc`, group, label: `${label}: ${low} First`, compare: [numeric(field)] },
    { id: `${id}_desc`, group, label: `${label}: ${high} First`, compare: [numeric(field, -1)] },
  ]),
  { id: 'sold_desc', group: 'Dates and priority', label: 'Last Sold: Newest First', compare: [date(-1)] },
  { id: 'sold_asc', group: 'Dates and priority', label: 'Last Sold: Oldest First', compare: [date(1)] },
  { id: 'demand_first', group: 'Dates and priority', label: 'User Demands First → Brand → Product', compare: [priority('is_user_demand'), text('brand_name')] },
  { id: 'returns_first', group: 'Dates and priority', label: 'Purchase Returns First → Brand → Product', compare: [priority('has_purchase_return'), text('brand_name')] },
];

export function arrangePurchaseOrderRows(rows, arrangement) {
  const option = purchaseOrderArrangements.find(item => item.id === arrangement);
  if (!option || option.id === 'original') return [...rows];
  const comparisons = [...option.compare, text('product_name'), text('product_code'), numeric('product_id')];
  return [...rows].sort((a, b) => {
    for (const compare of comparisons) {
      const result = compare(a, b);
      if (result) return result;
    }
    return 0;
  });
}
