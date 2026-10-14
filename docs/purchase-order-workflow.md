# Purchase Order implementation report

Current update: multiple receiving invoices per PO are implemented. See [Multiple receipt update](#multiple-receipt-update-13-october-2026). Earlier sections below describe the original release and are superseded where they refer to a single invoice or PO invoice uniqueness.

## Result

Create PO → Select Supplier on Form → Save PO → Receive Order → Review/edit Purchase Invoice → Save Invoice → Existing invoice stock update.

Orders support multiple brands and one finalized header supplier. Order creation, editing, cancellation, and invoice preparation do not modify stock. Saving the invoice uses the existing invoice transaction, product pricing/stock methods, batch handling, and supplier accounting.

This work preserved and completed the purchasing implementation already present as uncommitted changes in the workspace.

## Feature files

Created feature files (including files already present as untracked work at the start):

- `app/Models/PurchaseOrder.php`
- `app/Models/PurchaseOrderItem.php`
- `app/Policies/PurchaseOrderPolicy.php`
- `app/Services/PurchaseOrderReceiving.php`
- `database/migrations/2026_10_12_000001_create_purchase_orders.php`
- `database/migrations/2026_10_12_000002_add_purchase_order_permissions.php`
- `database/migrations/2026_10_13_000001_ensure_purchase_order_sanctum_permissions.php`
- `database/migrations/2026_10_13_000002_add_bonus_to_purchase_order_items.php`
- `resources/js/pages/PurchaseOrders.jsx`
- `tests/Feature/PurchaseOrderWorkflowTest.php`
- `docs/purchase-order-workflow.md`

Modified feature files (including existing uncommitted modifications retained):

- `app/Http/Controllers/PurchaseOrderController.php`
- `app/Http/Controllers/PurchaseInvoiceController.php`
- `app/Models/PurchaseInvoice.php`
- `app/Models/PurchaseInvoiceItem.php`
- `database/seeders/RolesAndPermissionsSeeder.php`
- `resources/js/components/SupplierSearch.jsx`
- `resources/css/app.css`
- `resources/js/pages/PurchaseInvoice/Create.jsx`
- `resources/js/pages/PurchaseInvoice/PurchaseInvoiceForm.jsx`
- `resources/js/pages/PurchaseInvoice/Show.jsx`
- `resources/js/pages/PurchaseOrder.jsx`
- `resources/js/routes/index.jsx`
- `routes/api.php`

Production frontend assets were regenerated with Vite. The pre-existing untracked `database.sqlite` was not part of the implementation.

## Database

The PO schema migration creates `purchase_orders` with a unique PO number, date, supplier foreign key, indexed status, reference, notes, creator/updater/receiver foreign keys, receipt timestamp, and standard timestamps. Supplier deletion is restricted; deleted users leave nullable audit links.

`purchase_order_items` retains ordered unit quantity, pack size, purchase unit price, discount percentage, and product/order foreign keys. A unique order/product index prevents duplicate PO lines. Order deletion cascades to its lines; product deletion is restricted.

`purchase_invoices.purchase_order_id` is nullable, unique, and references the order with restricted deletion. Existing standalone invoices remain compatible. `purchase_invoice_items.purchase_order_item_id` is a nullable foreign key with restricted deletion, connecting received paid units to original ordered units.

PO numbering uses the database-generated ID (`PO-00001`). Existing invoice numbering (`PRINV-…`) is preserved.

Permission migrations register PO abilities under the application's `sanctum` guard. The follow-up migration repairs installations that already ran the earlier version under `web`; it adds missing permissions without deleting existing permission records or assignments. Its rollback intentionally retains shared authorization records.

The two October 12 migrations were already applied in the local database when inspected. Both October 13 migrations were tested and applied locally. Apply pending migrations with `php artisan migrate` during deployment. Assign PO create/update/cancel permissions to appropriate non-admin roles through the existing permission management UI; no new role access is automatically granted.

## Backend

Existing authenticated/licensed API routes are reused:

| Operation | Endpoint |
| --- | --- |
| List/create | GET/POST `/api/purchase-orders` |
| Show/update | GET/PUT/PATCH `/api/purchase-orders/{id}` |
| Prepare editable invoice | POST `/api/purchase-orders/{id}/receive` |
| Cancel | POST `/api/purchase-orders/{id}/cancel` |
| Forecast | GET `/api/purchase-orders/forecast` |
| Save invoice | Existing POST `/api/purchase-invoices` |

Preparation returns supplier, source PO, notes, product data, quantities, prices and discounts. It creates no invoice or stock movement. Users retain the existing invoice fields, including tax, discounts, batch, expiry, bonus, retail and wholesale pricing.

Backend validation requires supplier, date, at least one existing product, distinct PO products, positive whole order packs and product-derived pack sizes, nonnegative prices, and discounts between 0 and 100%. Submitted suppliers cannot change. Only unreceived ordered documents can be edited or cancelled.

Receipt validation checks the source status, finalized supplier, source-line identity, nonnegative quantities/prices, valid pack size, discount range, and stock quantity equal to paid plus bonus units. Paid units summed across batches cannot exceed ordered units. At least one ordered unit must be received. Extra delivered products can be added without pretending to be PO lines.

Preparation requires PO view and purchase-invoice create authorization. Invoice saving independently enforces invoice creation and source PO view. PO CRUD/cancellation use the new policy and existing permission system.

Invoice creation locks the PO row, verifies no invoice exists, and saves invoice, lines, stock, accounting and PO status in the existing transaction. The unique invoice-to-PO constraint provides database-level duplicate protection. Invoice editing recomputes receipts through existing stock deltas; invoice deletion reverses stock through existing methods and reopens the PO. Audit sync preserves the original receipt actor/time when an invoice is edited and records the latest updater.

## Frontend

`/purchase-orders` lists number, date, supplier, item count, ordered units, status and permitted actions. Create/edit pages reuse product search. The form displays the invoice-style supplier search control. Supplier selection does not save the order; Save and Alt+S show a validation error when no supplier is selected. Submitted order suppliers remain finalized. Product brands are independent of the final supplier.

Details show ordered/received/remaining paid units, rates, discounts, line totals, notes, supplier, status and summary. Actions include Edit, Receive Order, Cancel, Print, and View Purchase Invoice as appropriate. Preparation has a loading state; request errors are displayed and submissions guarded.

Receive Order passes prepared data directly to the existing editable invoice form. Direct invoice URLs can also prepare an order. The form displays a clickable source PO and locks the finalized supplier. The invoice details show the stored source relationship. Successful save navigates to invoice details and names the originating PO in the success notification.

Existing forecast functionality and its print formats are preserved at `/purchase-orders/forecast`, with navigation to saved orders and creation from forecast rows. Saved PO details have browser print styling with supplier, date, number, status and items.

## Lifecycle and partial receiving

The editor is an unsaved working draft. Persisted states are Ordered, Partially Received, Received, and Cancelled; a separate persisted Draft state is not added.

An invoice changed from 100 to 80 paid units increases stock by 80, retains ordered quantity 100 and displays remaining quantity 20. Bonus units increase stock but do not count toward ordered paid-unit fulfillment. Purchase returns remain separate stock/accounting documents and do not reopen an order's receipt history.

This release permits one invoice per PO, including an invoice with a partial quantity. Subsequent invoices for the same PO are deliberately blocked. Multiple-receipt support will require changing the invoice uniqueness constraint and relationship to a collection and validating against cumulative receipts. Ordered quantities and source-line links are retained for that extension.

## Verification

`php artisan test`: **28 tests passed, 126 assertions**, including 17 PO workflow tests:

1. Mixed-brand PO save/edit/preparation preserves stock and supplier.
2. Edited 80-unit receipt updates actual product/batch stock, retains 100 ordered and 20 remaining, and blocks duplication.
3. Full receipt reaches Received; standalone invoices still work without PO links.
4. Missing supplier and empty items reject saving, including the requested supplier message.
5. Supplier mismatch and over-receipt preserve order and stock.
6. Receipt preparation requires invoice creation permission.
7. Failed batch writes roll back invoice, product stock and PO status.
8. Invoice edits recalculate received quantity; deletion reverses stock and reopens the order.
9. Database uniqueness rejects a second invoice link.
10. Real Sanctum permissions deny both preparation and invoice saving without invoice create access.
11. Finalized supplier changes and cancelled-order receiving are rejected.
12. Inconsistent stock quantity, negative pack quantity, invalid discount and mismatched source lines are rejected.
13. Split batches and bonus units count paid fulfillment correctly; invoice edits preserve initial receipt time.
14. HTTP routes expose order/invoice source links, save receipts and reject duplicate conversion.
15. Permission repair runs idempotently and retains existing permission records.
16. Pack quantities, pack purchase prices and bonus units persist and pre-populate invoice fields without stock changes.
17. Fractional packs, modified product pack sizes and unit quantities that are not full packs are rejected.

The remaining tests cover existing login, product listing and application smoke behavior.

`npm run build`: passed. Vite reports three existing shared CSS selector warnings and a large JavaScript bundle warning.

`git diff --check`: passed. Newly added PHP files and workflow tests were formatted with Laravel Pint. PO route registration was checked with `php artisan route:list --path=purchase-orders`.

Tests use an isolated in-memory SQLite database with real purchasing migrations. The HTTP workflow test bypasses only licensing middleware. No browser surface was available, so interactive UI, print rendering, and parallel MySQL request execution were not manually verified. Database uniqueness and receipt locking are covered by schema/controller tests, not a concurrent production-database test.

## Pack-based form refinement

The form now displays read-only product Pack Size, Order Packs (whole packs only), Pack Purchase Price, Pack Bonus, Unit Bonus, Discount %, and line total. Pack and unit bonus inputs synchronize like the purchase invoice. `purchase_order_items.unit_bonus` stores the canonical free-unit quantity with default zero for existing documents. Paid quantities/prices retain the existing unit storage, with backend conversion from pack inputs. Both bonus representations carry into the editable invoice.

The PO pages reuse purchase-entry header, table and footer styling, workspace theme colors and existing glass button utilities. Undefined `g-btn-secondary` classes were replaced with the defined `g-btn-ghost` style; links/actions use proper inline-flex layout, disabled states and product action icons. Scoped table/field styling is in `resources/css/app.css`.

## Expected receiving date and dashboard KPI

PO create/update requires `expected_received_date`, on or after the order date. The form leaves it empty until selected and blocks saving without it. Details display the scheduled date. Migration `2026_10_13_000004_add_expected_received_date_to_purchase_orders.php` adds a nullable indexed date for historical compatibility and has been applied locally. Historical records remain unscheduled until edited.

`PurchaseOrder::expectedAmountBetween` totals ordered paid-unit quantities multiplied by purchase price after item discounts. It includes only Ordered POs without a receiving invoice in the expected-date range. Cancelled, fully received and partially invoiced orders are excluded. Bonuses do not add payable value.

The dashboard KPI metrics response includes `expected_orders_amount` only for users allowed to view POs. The Expected Orders money card uses existing KPI styling and links to the PO list. It defaults to today's selected dashboard range and follows the dashboard date filters. Backend fallback dates use Asia/Karachi. The card has no fabricated historical trend or sparkline. Changes are in `app/Http/Controllers/DashboardController.php` and `resources/js/pages/Dashboard.jsx`.

Validation: 20 PO tests passed (106 assertions), including date requirement, discounted expected value, date exclusion and cancelled/received exclusion. Production build passed.


## Multiple receipt update — 13 October 2026

Purchase orders now have an `invoices` collection. Ordered and Partially Received orders can prepare another invoice; fully received and cancelled orders cannot. Preparation suggests remaining paid units, omits completed lines and suggests only bonuses not already received. Bonus quantities remain editable and do not count toward paid-unit fulfillment.

Saving a receipt locks the PO and validates quantities against cumulative receipts, including split batches. Editing an invoice excludes its previous lines from the cumulative check, then replaces them; deletion reverses that invoice's stock and recomputes the PO from surviving invoices. Deleting the last receipt reopens the order and clears the receipt actor/time. While any receipts remain, the existing first-receipt audit fields are retained. Purchase returns remain separate and do not reopen PO fulfillment.

The PO details show Receipt History with invoice numbers, dates, supplier references, amounts and permitted invoice links. Details and the listing offer receiving for partially received orders. Orders with invoices cannot be edited, cancelled or deleted.

Migration `2026_10_13_000005_allow_multiple_purchase_order_receipts.php` replaces the unique PO link with a normal index and adds a nullable unique `receipt_key`. Legacy invoice links are preserved. New PO invoice creation requires the UUID returned by receipt preparation; the frontend carries it through the editable form. Reusing that key is rejected, including by a database constraint. Preparing a genuinely new delivery generates a new key; cumulative quantity validation still rejects stale over-receipts. Standalone invoices do not require a receipt key. The PO API now returns `invoices` instead of `invoice`; external API clients need to adopt that collection and preparation key.

Rollback refuses to restore one-invoice uniqueness if any PO already has multiple invoices. The migration has been applied to the local development database. Apply it to other deployments with `php artisan migrate` before using the new code.

The Expected Orders dashboard KPI keeps its original scope: only uninvoiced Ordered POs scheduled in the selected range. Remaining value for partially received POs is a separate reporting enhancement.

Verification: all 35 backend tests pass (175 assertions), including 24 purchasing tests. New coverage includes successive deliveries, stale over-receipts, duplicate keys, cumulative edit/delete behavior, split-batch receipts, remaining bonus suggestions and migration preservation/rollback safety. Frontend production build passes with existing CSS selector and bundle-size warnings. No browser was connected; interactive UI/printing and concurrent production MySQL execution were not verified.


## Extra delivered quantities — 14 October 2026

Purchase invoices linked to a PO now allow paid quantities above the ordered amount, including invoice edits and cumulative deliveries. The original ordered quantity stays unchanged, actual received quantity includes all paid units, and remaining quantity is clamped to zero. An order is Received when every line meets or exceeds its ordered quantity; otherwise it is Partially Received. Stock/pricing/accounting use the actual invoice quantities and existing formulas. Bonus units still do not count toward paid-unit fulfillment. Duplicate receipt protection and supplier/source validation remain enforced. This supersedes earlier references to blocking over-receipts.

Validation: all 49 backend tests pass (310 assertions), including extra quantities in a subsequent receipt, extra quantities on invoice edit, subsequent reduction to partial receipt, deletion and stock reversal.
