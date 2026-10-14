import { arrangePurchaseOrderRows, purchaseOrderArrangements } from './purchaseOrderArrangement.js';

const supported = new Set(['original', 'name_asc', 'name_desc', 'brand_asc', 'brand_desc', 'brand_code', 'code_asc', 'code_desc', 'packs_asc', 'packs_desc', 'amount_asc', 'amount_desc', 'price_asc', 'price_desc', 'size_asc', 'size_desc']);
export const purchaseInvoiceArrangements = purchaseOrderArrangements.filter(option => supported.has(option.id)).map(option => ({ ...option, label: option.label.replace('Original forecast order', 'Original invoice order').replace('Order Packs', 'Purchase Packs').replace('Order Amount', 'Line Amount') }));

export function arrangePurchaseInvoiceRows(items, products, arrangement) {
  const lookup = new Map(products.filter(Boolean).map(product => [String(product.id), product]));
  const rows = items.map((item, index) => {
    const product = lookup.get(String(item.product_id)) || item.product || {};
    return { item, index, product_id: item.product_id, product_name: product.name || '',
      brand_name: product.brand?.name || product.brand_name || item.brand_name || '',
      brand_key: product.brand_id ?? product.brand?.id ?? product.brand_name ?? '',
      product_code: product.product_code || '', order_packs: item.pack_quantity,
      order_amount: item.sub_total, pack_price: item.pack_purchase_price, pack_size: item.pack_size };
  });
  const selected = rows.filter(row => row.item.product_id);
  const empty = rows.filter(row => !row.item.product_id);
  return [...arrangePurchaseOrderRows(selected, arrangement), ...empty];
}
