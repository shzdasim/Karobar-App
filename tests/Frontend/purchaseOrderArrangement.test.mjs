import test from 'node:test';
import assert from 'node:assert/strict';
import { arrangePurchaseOrderRows, purchaseOrderArrangements } from '../../resources/js/utils/purchaseOrderArrangement.js';
const rows = [
  { product_id: 3, product_name: 'Zinc 10', product_code: 'P10', brand_name: 'Abbott', supplier_name: 'Beta', order_packs: 5, order_amount: 50, current_stock_units: 0, units_sold: 10, pack_price: 10, pack_size: 10, last_sold_date: '2026-10-12', is_user_demand: 0, has_purchase_return: 1 },
  { product_id: 1, product_name: 'Alpha', product_code: 'P2', brand_name: 'GSK', supplier_name: 'Acme', order_packs: 2, order_amount: 40, current_stock_units: 20, units_sold: 4, pack_price: 20, pack_size: 20, last_sold_date: '2026-10-01', is_user_demand: 1, has_purchase_return: 0 },
  { product_id: 2, product_name: 'Zinc 2', product_code: 'P3', brand_name: 'Abbott', supplier_name: 'Acme', order_packs: 3, order_amount: 45, current_stock_units: 10, units_sold: 6, pack_price: 15, pack_size: 15, last_sold_date: null, is_user_demand: 0, has_purchase_return: 0 },
];
const ids = key => arrangePurchaseOrderRows(rows, key).map(row => row.product_id);
test('alphabetical, natural codes and brand/supplier hierarchies', () => {
  assert.deepEqual(ids('original'), [3,1,2]);
  assert.deepEqual(ids('name_asc'), [1,2,3]);
  assert.deepEqual(ids('name_desc'), [3,2,1]);
  assert.deepEqual(ids('brand_asc'), [2,3,1]);
  assert.deepEqual(ids('brand_desc'), [1,2,3]);
  assert.deepEqual(ids('supplier_asc'), [1,2,3]);
  assert.deepEqual(ids('supplier_desc'), [3,1,2]);
  assert.deepEqual(ids('supplier_brand'), [2,1,3]);
  assert.deepEqual(ids('brand_code'), [2,3,1]);
  assert.deepEqual(ids('code_asc'), [1,2,3]);
  assert.deepEqual(ids('code_desc'), [3,2,1]);
});
test('every numeric arrangement uses its actual forecast field and direction', () => {
  for (const [key, field] of [['packs','order_packs'], ['amount','order_amount'], ['stock','current_stock_units'], ['sales','units_sold'], ['price','pack_price'], ['size','pack_size']]) {
    for (const direction of ['asc','desc']) {
      const expected = [...rows].sort((a,b) => (a[field] - b[field]) * (direction === 'asc' ? 1 : -1)).map(row => row.product_id);
      assert.deepEqual(ids(`${key}_${direction}`), expected);
    }
  }
});
test('dates put unknown dates last; demand and return priorities differ', () => {
  assert.deepEqual(ids('sold_desc'), [3,1,2]);
  assert.deepEqual(ids('sold_asc'), [1,3,2]);
  assert.deepEqual(ids('demand_first'), [1,2,3]);
  assert.deepEqual(ids('returns_first'), [3,2,1]);
});
test('missing group names sort last and equal names use deterministic IDs', () => {
  const input = [{ product_id: 3, product_name: 'same', brand_name: '' }, { product_id: 2, product_name: 'Same', brand_name: 'GSK' }, { product_id: 1, product_name: 'same', brand_name: 'GSK' }];
  for (const key of ['brand_asc','brand_desc']) assert.deepEqual(arrangePurchaseOrderRows(input,key).map(row => row.product_id), [1,2,3]);
});
test('all options preserve rows, quantities and input order; unknown option restores original', () => {
  const before = JSON.stringify(rows);
  for (const option of purchaseOrderArrangements) {
    const sorted = arrangePurchaseOrderRows(rows,option.id);
    assert.equal(sorted.length, rows.length);
    assert.deepEqual([...sorted].sort((a,b) => a.product_id-b.product_id), [...rows].sort((a,b) => a.product_id-b.product_id));
  }
  assert.equal(JSON.stringify(rows), before);
  assert.deepEqual(ids('unknown'), [3,1,2]);
});
