import test from 'node:test';
import assert from 'node:assert/strict';
import { arrangePurchaseInvoiceRows, purchaseInvoiceArrangements } from '../../resources/js/utils/purchaseInvoiceArrangement.js';

test('brand grouping preserves source indices and editable item references', () => {
 const items = [{product_id: 1, pack_quantity: 4, sub_total: 40}, {product_id: 2, pack_quantity: 3, sub_total: 30}, {product_id: ''}];
 const products = [{id: 1, name: 'Item 10', brand: {id: 2, name: 'Zeta'}}, {id: 2, name: 'Item 2', brand: {id: 1, name: 'Alpha'}}];
 const rows = arrangePurchaseInvoiceRows(items, products, 'brand_asc');
 assert.deepEqual(rows.map(row => row.index), [1,0,2]);
 assert.deepEqual(rows.map(row => row.brand_name), ['Alpha','Zeta','']);
 assert.equal(rows[0].item, items[1]);
 assert.equal(rows[1].item, items[0]);
 assert.deepEqual(arrangePurchaseInvoiceRows(items, products, 'original').map(row => row.index), [0,1,2]);
 for (const option of purchaseInvoiceArrangements) {
   const sorted = arrangePurchaseInvoiceRows(items, products, option.id);
   assert.equal(sorted.reduce((sum,row)=>sum+Number(row.item.sub_total || 0),0),70);
   assert.equal(sorted.reduce((sum,row)=>sum+Number(row.item.pack_quantity || 0),0),7);
 }
});
test('natural codes, missing brands and row-level product fallback work', () => {
 const items = [{product_id:1,product:{name:'One',product_code:'P10'}},{product_id:2,product:{name:'Two',product_code:'P2',brand:{name:'Alpha'}}}];
 assert.deepEqual(arrangePurchaseInvoiceRows(items, [], 'code_asc').map(row=>row.index),[1,0]);
 assert.deepEqual(arrangePurchaseInvoiceRows(items, [], 'brand_asc').map(row=>row.index),[1,0]);
});
