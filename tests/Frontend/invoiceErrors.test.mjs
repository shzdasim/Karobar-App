import test from 'node:test';
import assert from 'node:assert/strict';
import { invoiceErrorEntries } from '../../resources/js/utils/invoiceErrors.js';

test('all server field errors map to the exact zero-based invoice row', () => {
  const entries = invoiceErrorEntries({ response: { data: { message: 'Validation failed', errors: {
    'items.0.quantity': ['Only 2 units available'], 'items.12.batch': ['Batch missing', 'Expiry required'],
    'items[3].price': ['Invalid price'], invoice_number: ['Invoice number already used'],
  } } } });
  assert.deepEqual(entries.map(e => e.row), [0, 12, 12, 3, null]);
  assert.equal(entries[0].message, 'Only 2 units available');
  assert.equal(entries[4].message, 'Invoice number already used');
});
test('invoice-wide errors do not invent an affected row', () => {
  assert.equal(invoiceErrorEntries({ response: { data: { errors: { items: ['Receive at least one ordered unit'] } } } })[0].row, null);
  assert.equal(invoiceErrorEntries({ response: { data: { message: 'Supplier mismatch' } } })[0].message, 'Supplier mismatch');
  assert.match(invoiceErrorEntries({})[0].message, /Connection failed/);
});

test('sorted save errors map back to the unchanged entry-order rows', () => {
 const entries = invoiceErrorEntries({ response: { data: { errors: {
  'items.0.unit_quantity': ['Invalid quantity'], 'items.2.batch': ['Batch required'], supplier_id: ['Supplier required'],
 } } } }, [2, 0, 1]);
 assert.deepEqual(entries.map(entry => entry.row), [2, 1, null]);
});
