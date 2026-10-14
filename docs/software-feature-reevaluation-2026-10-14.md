# Karobar software reevaluation and remaining feature backlog

**Review date:** 14 October 2026 · **Scope:** current repository · **Status:** planning document, no business logic changes.

This document supersedes the remaining-work recommendations in the [13 October audit](missing-features-audit.md). It reflects the implemented purchasing, cost-snapshot, stock-validation and invoice UI changes. Existing pricing, discount, tax, weighted-average and profit formulas must remain unchanged unless explicitly authorized. Historical cost reconstruction is deliberately excluded: legacy costs remain estimated, as agreed.

Implementation update: R01 linked return entitlement and source validation are now implemented, with invoice-style return forms. Unlinked returns retain their existing workflow. See [return entitlement workflow](return-entitlement-workflow.md). The assessment below is retained as the review baseline.

## 1. Assessment method and limits

Reviewed API/frontend routes, models and migrations, sale/purchase/return controllers, stock and cost services, party ledgers, notifications, backup scheduling, invoice forms, arrangement/error utilities and existing tests. Missing-module findings were checked against route/model/service inventories and targeted source searches. A missing implementation in this repository does not prove that an external service or operational procedure is absent.

Verification on this review: **49 backend tests passed, 310 assertions; 9 frontend utility tests passed.** These test results establish a regression baseline, not proof that every business workflow is covered. No production transactions were created. No browser walkthrough, rendered-print review, production-data reconciliation, real parallel MySQL test or jurisdiction-specific legal assessment was performed. Build was not rerun because this review changes only documentation.

Statuses used below:

- **Implemented:** present in current code; remaining verification limits are stated separately.
- **Incomplete:** part of the workflow exists but further behavior is needed.
- **Missing:** no complete module/workflow was found.
- **Verification gap:** source concern that needs a focused reproduction before any fix.
- **Optional:** depends on the business, rather than a universal requirement.

Priority P1 protects daily records/work; P2 completes useful business operations; P3 expands scope. Priorities are recommendations, not permission to implement everything.

## 2. Existing capabilities and completed improvements

| Area | Current capability | Evidence / remaining limit |
| --- | --- | --- |
| Sales | Retail/wholesale entry, customer wholesale pricing, editing, returns and printing | [Sales controller](../app/Http/Controllers/SaleInvoiceController.php), [forms](../resources/js/pages/SaleInvoice). |
| Purchasing | Invoices, paid/bonus quantities, prices/discounts, batches, returns and supplier ledgers | [Purchase controller](../app/Http/Controllers/PurchaseInvoiceController.php). |
| Purchase orders | Multiple receipts, receipt history, remaining quantities, expected receiving dates, cancellation and invoice preparation | [PO controller](../app/Http/Controllers/PurchaseOrderController.php), [workflow](purchase-order-workflow.md). |
| Extra delivery quantities | Linked purchase invoices may receive more than ordered; actual quantities affect stock; original order stays unchanged | [Receiving service](../app/Services/PurchaseOrderReceiving.php). Do not reintroduce an over-receipt block. |
| Sale cost snapshots | New sales keep the selected cost; linked matching returns use it; old records retain the estimated fallback | [Snapshot service](../app/Services/SaleCostSnapshot.php), [cost documentation](sale-cost-snapshots.md). Ambiguous/backdated cases are documented limitations, not promises of reconstruction. |
| Sale stock checks | Server validates aggregate quantities and selected batches; product/batch locks cover sale create/edit/delete | [Stock service](../app/Services/SaleStockAvailability.php), [scope](sale-stock-safeguards.md). Real concurrent production-database testing is outstanding. |
| Invoice errors | Sales and purchase forms highlight row-specific errors and provide messages/jump links | [Shared component](../resources/js/components/InvoiceErrors.jsx). Invoice-wide errors have no exact row; focus/render behavior lacks browser coverage. |
| Product arrangement | Forecast arrangements plus purchase invoice sorting and brand headings | [Invoice arrangements](../resources/js/utils/purchaseInvoiceArrangement.js). Sorting is a display arrangement; it preserves source indices and invoice values. |
| Inventory | Products, categories/brands, pack/unit fields, batches/expiry, adjustments, low-stock and expiry reporting | [Models](../app/Models), [reports](../app/Http/Controllers/ReportsController.php). |
| Party balances | Customer/supplier ledgers, manual/payment entries, invoice settlement and rebuild | [Customer ledger](../app/Http/Controllers/CustomerLedgerController.php), [supplier ledger](../app/Http/Controllers/SupplierLedgerController.php). |
| Data/admin | Master-data imports/exports, bulk product actions, roles/permissions, password reset, license/themes/printers | [API inventory](../routes/api.php), [frontend inventory](../resources/js/routes/index.jsx). |
| Recovery | Manual backup/download/upload/restore and retention cleanup | [Backup service](../app/Services/BackupService.php). Scheduled off-server recovery is not implemented in the repository. |

## 3. P1 — protect and complete daily workflows

### R01. Return entitlement and source validation — incomplete

**Plain-language problem:** selling five units should not allow the same sale to be returned repeatedly for more than five. A selected source invoice should belong to the selected customer/supplier.

**Current evidence:** [sale returns](../app/Http/Controllers/SaleReturnController.php) accept an optional source invoice, sold quantity and returned quantity. No cumulative entitlement check against original sale lines was found. Snapshot lookup checks customer identity for cost selection, but does not itself reject an invalid source relationship. [Purchase returns](../app/Http/Controllers/PurchaseReturnController.php) also need source-line and cumulative entitlement review.

**Deliverable:** explicit source-line identity, cumulative returnable quantity across multiple returns, adjustments for return edits/deletion, and a deliberate policy for unlinked returns. Keep quantity allowance for extra delivered purchase units based on actual invoice quantities.

**Acceptance:** sell five; return two, then three; a further one is rejected with a row-specific message. Wrong-customer/supplier sources reject without changing stock or money records. Simultaneous returns cannot consume the same entitlement. Monetary calculations remain unchanged.

### R02. Consistent stock transactions across all modules — incomplete

**Problem:** the new sale locks help simultaneous sales, but a purchase return or stock adjustment can also modify the same stock.

**Evidence:** [sale locks](../app/Services/SaleStockAvailability.php) are centralized for sales. Return helpers still read/save stock separately, and purchase returns clamp negative balances to zero. [Adjustments](../app/Http/Controllers/StockAdjustmentController.php) also update product/batch quantities independently. A uniform product/batch locking protocol across all writers was not found.

**Deliverable:** consistent lock order and atomic validation across sales, purchases, both returns, invoice reversals, adjustments and imports that change stock. Reconciliation should respect deliberately unbatched stock; do not assume every product quantity must equal batch totals.

**Acceptance:** concurrent sale/return/adjustment scenarios on a production-like database have no lost updates, unsupported negative deductions or partial writes. Preserve the existing valuation formulas.

### R03. Recoverable drafts and held sales — missing

**Problem:** refresh, browser closure or an interrupted session can lose a long invoice being entered.

**Evidence:** invoice working data lives in React state; no document-draft storage or recovery endpoints were found. Preferences/local storage for theme/auth/forecast sorting do not constitute invoice recovery.

**Deliverable:** autosave/recover drafts, saved draft list, hold/resume carts, discard confirmation and a clear distinction between draft and posted document.

**Acceptance:** reload a draft with quantities, supplier/customer, batches and prices intact; draft changes affect neither stock nor ledgers; posting a recovered draft cannot duplicate an invoice.

### R04. Audit history and controlled document reversal — missing

**Problem:** timestamps and the latest actor do not explain what changed on a posted invoice.

**Evidence:** documents store some actor fields; no general before/after revision history, closed-period control or immutable audit journal was found in [models](../app/Models) and controller mutation paths.

**Deliverable:** actor/time/reason and before/after changes; revision view; permission-controlled void/reversal rather than unexplained deletion; optional posting-period locks and manager approvals.

**Acceptance:** an invoice edit, delete/reversal, payment change, stock adjustment and permission change can be traced. Audit entries cannot be casually edited through normal business screens.

### R05. Server-side amount verification using the same formulas — incomplete

**Problem:** browser totals are submitted to the API. Validation currently checks many values' types, but does not establish that all submitted totals agree with the approved calculations.

**Evidence:** [sales](../app/Http/Controllers/SaleInvoiceController.php) and [purchases](../app/Http/Controllers/PurchaseInvoiceController.php) accept totals/subtotals/tax/discount values; several discount fields lack explicit business limits. Product maximum discount and some frontend wholesale price checks already exist.

**Deliverable:** first document the current formulas with representative fixtures, then validate/recompute server values with exactly those formulas, rounding and permitted exceptions. Add price/discount override permissions where needed.

**Acceptance:** ordinary valid invoices produce identical amounts; inconsistent submitted amounts reject clearly; approved exceptions remain possible. **Do not replace the user's formulas with generic accounting assumptions.**

### R06. Payment events, methods and allocation history — incomplete

**Problem:** knowing “received Rs. 10,000” does not show whether it was cash, bank or two payments on different dates.

**Evidence:** party ledgers already support payments and references, and invoices store aggregate paid/received values. Settlement updates aggregate balances. No complete tender-event/allocation module was found.

**Deliverable:** dated receipts/payments, cash/card/bank/wallet methods, split tenders, references, invoice allocations, unapplied advances and reversible payment records. Integrate existing ledger payments rather than count them again.

**Acceptance:** one payment can settle multiple invoices, multiple payments can settle one invoice, and a reversed payment produces consistent invoice/party/account balances with history.

### R07. Customer/supplier credit terms and aging — missing

**Problem:** balance reports show who owes money, but not necessarily which invoices are overdue or whether another credit sale should be allowed.

**Evidence:** [customer fields](../app/Models/Customer.php) contain contact information; no credit-limit/term fields or invoice due dates were found.

**Deliverable:** agreed due dates, credit limits, overdue aging, reminders, overrides and receivables/payables work queues.

**Acceptance:** partial payments reduce the correct aging bucket; due-date changes are traceable; authorized exceptions are possible without changing invoice profit.

### R08. Scheduled off-server backups and recovery visibility — incomplete

**Problem:** a manual backup on the same server may be unavailable when that server fails.

**Evidence:** backups use local storage with hardcoded count/age retention; [scheduler](../routes/console.php) schedules demand reminders, not backup creation. External deployment automation was not inspected.

**Deliverable:** schedule/retention settings, off-server copies, failure alerts, last-success/recovery-point status, integrity checks and restore rehearsal.

**Acceptance:** a failed scheduled backup is visible, retained copies follow policy, and a tested copy restores on a separate environment. Avoid exposing backup contents to ordinary users.

## 4. P2 — complete money, inventory and purchasing operations

| ID | Status / feature | Existing behavior and missing work | Acceptance / business example | Evidence |
| --- | --- | --- | --- | --- |
| R09 | Missing: cashier shifts and cash drawer | No till/session entities were found. Add opening cash, paid-in/out, cashier identity, counted close and variance reasons. | A cashier can close a shift and explain the difference between expected and counted cash. | [Sales model](../app/Models/SaleInvoice.php), [routes](../routes/api.php). |
| R10 | Missing: expenses and net-profit view | Current cost reports measure sales/returns/gross profit. Add expenses, categories, recurring entries and attachments; show net profit separately. | Rent/utilities reduce a new net-profit report while existing gross-profit formulas stay intact. | [Reports](../app/Http/Controllers/ReportsController.php), [models](../app/Models). |
| R11 | Missing: cash/bank accounts and reconciliation | Party ledgers exist; account movements/transfers and statement matching are absent. | A bank deposit/payment can be matched to its statement and accounted for once. | [Ledger controllers](../app/Http/Controllers), [models](../app/Models). |
| R12 | Incomplete: stocktake sessions | Adjustments exist. Add count sessions, blind counts, batches, variance approval and handling of trading during a count. | A physical count produces reviewed adjustments with the original count retained. | [Adjustment controller](../app/Http/Controllers/StockAdjustmentController.php). |
| R13 | Incomplete: replenishment policies | Low-stock uses pack size; forecast has safety/MOQ/projected-day inputs. Add per-product rules, lead time and outstanding-order awareness. | A supplier delay or an already-open PO changes the suggested replenishment appropriately. | [Notifications](../app/Http/Controllers/NotificationController.php), [forecast](../app/Http/Controllers/PurchaseOrderController.php). |
| R14 | Incomplete: alert lifecycle | Dismissed low-stock product IDs are excluded via preferences; automatic reset after stock recovery was not found. | Restock a dismissed product; a later new shortage creates a fresh alert. | [Notification controller](../app/Http/Controllers/NotificationController.php). |
| R15 | Incomplete: expiry allocation and sale restriction | Expiry reports/fields and batch availability exist; expired-stock rejection and earliest-expiry allocation are absent. | Recommend the earliest valid batch, reject expired stock, allow only explicit authorized exceptions. | [Stock availability](../app/Services/SaleStockAvailability.php), [batch endpoint](../app/Http/Controllers/BatchController.php). |
| R16 | Missing: quarantine/damage/recall workflow | Returns generally restore inventory; batches lack saleable/quarantined/disposed status. | A damaged return is counted physically but excluded from saleable stock; a recalled batch can be traced. | [Batch model](../app/Models/Batch.php), [return controller](../app/Http/Controllers/SaleReturnController.php). |
| R17 | Incomplete: movement journal and reconciliation | Comprehensive reports exist, but no dedicated stock movement entity was found. Add source-linked movements and discrepancy reports. | Explain an item's opening, purchases, sales, returns and adjustments, including legitimate unbatched quantities. | [Product comprehensive report](../app/Http/Controllers/ReportsController.php), [models](../app/Models). |
| R18 | Incomplete: demand-to-fulfillment linkage | Demand has status/requested quantity; purchases can mark demand ordered. No reserved/fulfilled units or exact PO/sale links. | Receive half a customer's request, reserve it, notify them and track what remains. | [Demand model](../app/Models/UserDemand.php), [purchase controller](../app/Http/Controllers/PurchaseInvoiceController.php). |
| R19 | Missing: supplier product offers/performance | Products have one supplier; no alternative-offer catalog was found. | Compare supplier-specific pack prices/MOQs/lead times and actual delivery performance. | [Product](../app/Models/Product.php), [POs](../app/Models/PurchaseOrder.php). |
| R20 | Incomplete: outstanding PO management | Dates/status/history exist. Expected Amount excludes partially invoiced orders. Add overdue/remaining-value views, close-short remainder and optional approvals. | After receiving 2 of 3, deliberately close the missing 1 with a reason rather than leave it pending indefinitely. Extra delivery must remain allowed. | [Expected amount query](../app/Models/PurchaseOrder.php), [PO controller](../app/Http/Controllers/PurchaseOrderController.php). |
| R21 | Incomplete: saved supplier layouts | Forecast/invoice sorts exist. Manual supplier ordering, templates and persisted invoice sort preferences remain absent. | Reuse a supplier's preferred line sequence without editing quantities or values. | [Arrangement utility](../resources/js/utils/purchaseInvoiceArrangement.js), [todo](purchase-order-arrangement-todo.md). |
| R22 | Incomplete: barcode assignment and labels | Barcode import/search exists; normal product form disables barcode editing. Add manufacturer assignment and dedicated label workflow. | Scan a supplier's barcode and print product/shelf labels; invoice barcode templates remain separate. | [Product form](../resources/js/pages/Products/ProductForm.jsx). |
| R23 | Missing: quotations/orders/dispatch | Sales start with invoices. No quote/sales-order/reservation/delivery entities were found. | Confirm an order, dispatch part, retain a backorder and invoice according to agreed policy. | [Routes](../routes/api.php), [models](../app/Models). |
| R24 | Missing: transaction attachments and sharing | Product images/printing exist. Add supplier bill/receipt attachments and deliberate outbound document sharing. | Open the scanned original supplier bill beside the invoice; send a statement with a recorded action. | [Models](../app/Models), [routes](../routes/api.php). |
| R25 | Incomplete: management analytics | Current transaction/stock/expiry reports exist. Add slow/dead stock, cover/turnover, return reasons, purchase price variance and party profitability. | Identify stock consuming cash and distinguish gross profit from collections. Keep legacy cost estimates visible. | [Reports](../app/Http/Controllers/ReportsController.php). |
| R26 | Incomplete: report presets/transaction exports | PDFs/master CSV exports exist. Add saved filters, transaction CSV/XLSX and scheduled delivery if needed. | Reopen a saved supplier report; export values matching the screen under the same filters. | [Report routes](../routes/api.php), [report pages](../resources/js/pages/Reports). |

## 5. P3 — optional expansion

These are separate scope choices; do not delay basic workflow reliability to add every module.

| ID | Extension | What it would require |
| --- | --- | --- |
| R27 | Branches/warehouses/transfers | Location-specific balances, transfers/in-transit state, branch permissions and location reporting. A rack field is not warehouse accounting. |
| R28 | Full double-entry accounting | Chart of accounts, journals, trial balance, balance sheet and posting periods; define integration with existing party ledgers. |
| R29 | Variants/flexible units/serials/bundles | Decide whether the business needs weights, fractional stock, sizes/colors, conversions, serial tracking or kits; current model is pack/integer-unit oriented. |
| R30 | Loyalty/promotions/gift credit | Promotion rules, eligibility, points and dedicated redemption history. Existing discounts/wholesale prices are already present. |
| R31 | Offline POS | Offline queue, reconciliation, duplicate prevention and conflict policy; local auth/theme storage does not provide offline trading. |
| R32 | Portals/commerce connectors | Customer/supplier access, order/payment interfaces, explicit synchronization and supported external systems. |
| R33 | Localization/currency/tax integrations | Urdu/RTL and configurable currency where useful; multi-currency/tax filing require separately agreed requirements. No legal obligations assessed here. |
| R34 | Two-factor/session management | Enrollment/recovery, active devices, remote logout and relevant login visibility. Password reset/token expiry already exist. |
| R35 | Pharmacy-specific dispensing | Prescription verification, restricted-item records and recall handling only if pharmacy deployment is intended. Existing narcotic/doctor/patient fields do not form a complete dispensing workflow. |

Inventory evidence for these absent extensions: [models](../app/Models), [API routes](../routes/api.php), [frontend routes](../resources/js/routes/index.jsx), [services](../app/Services).

## 6. Follow-up verification and implementation gaps

These are not all new product modules. Reproduce the concern, agree expected behavior, then fix it without altering approved formulas.

### V01. Real database concurrency

Current automated tests use isolated SQLite; row-lock behavior needs parallel MySQL/PostgreSQL testing. Include sales sharing stock, invoice edits/deletion, returns, adjustments and PO receipts. Stock locks for sales do not establish a system-wide concurrency guarantee.

### V02. Batch schema consistency outside sales

Sale batch lookup now uses actual `batch_number`/`expiry_date` columns. Return helpers still query aliases such as `batch` and `expiry`, which are model accessors rather than columns in the base [batch migration](../database/migrations/2025_08_10_152438_create_batches_table.php). On that schema, batch-specific return queries can fail. Verify against the deployed schema and cover both create/edit/delete with real batches. This was identified from source, not reproduced in this audit.

### V03. Report filter consistency

The daily cost-of-sale report filters sales/costs by invoice type, while its return queries intentionally remain unfiltered. A credit-only report may therefore include returns from debit sales. Agree whether filters should follow the source invoice; verify with mixed invoices/returns before changing queries. The arithmetic itself must stay unchanged. Evidence: [report controller](../app/Http/Controllers/ReportsController.php).

### V04. Numbering and duplicate-save coverage

PO receipts have a unique receipt key; ordinary sales/purchases do not have a universal equivalent. Some numbering paths lock the last invoice row, which deserves a concurrent first-invoice and different-product test. Add idempotent replay policy for ordinary posting/payment actions where necessary. Do not treat a new genuine delivery as a duplicate just because it exceeds ordered quantities.

### V05. Row errors, sorting and focus

Utility tests cover parsing/sorting, not real React rendering, focus or row correction. Verify create/edit/PO-origin invoices, both sales modes, deleting an invalid row, duplicate-product input, sorting after server errors and arrow navigation. Current purchase display order preserves source indices, so error labels may describe the original index rather than the visible position. Current focus selects the first enabled control, potentially the remove button rather than the invalid field. Both need usability verification.

### V06. Snapshot matching and edit policy

Snapshots deliberately remain estimated when a linked return or edited line cannot identify a unique original cost. Strong persistent source-line identity would reduce ambiguity. Test changing a line's product/batch, source-invoice deletion and repeated edits. Do not backfill historical costs or silently replace estimates with guessed values. Evidence: [snapshot service](../app/Services/SaleCostSnapshot.php).

### V07. Ledger consistency around returns and reversals

Return controllers primarily create stock documents; a complete settlement/refund-event workflow is absent. Test invoice payments, customer/supplier returns, invoice deletion and ledger rebuild together for consistent balances. Existing negative/manual entries and deletion credit modes need to be respected; do not assume they constitute a complete refund allocation module.

### V08. Documentation and interface promises

The old audit contains implemented labels alongside original now-stale descriptions. README claims about profit-and-loss, warehouse readiness and cash/bank accounts should be aligned with actual scope. Keep this document as the current planning reference. Printed/PDF/browser layouts, compact screens and accessibility remain a verification area, including the newly added brand headers and error panels.

## 7. Recommended implementation sequence

| Phase | Work | Exit condition |
| --- | --- | --- |
| 1 | Reproduce V02/V03; implement R01 return entitlement; define shared stock locking for R02 | Returns are bounded/source-validated and important stock writers have consistent transactions; approved formulas unchanged. |
| 2 | R03 drafts; R04 audit trail; R08 scheduled recovery | Interrupted work can be recovered and posted changes can be traced/restored operationally. |
| 3 | R05 server verification using approved formula fixtures; R06 payment events; R07 aging | Server values agree with the existing calculations, and payments/due balances have traceable history. |
| 4 | R09 shifts; R10 expenses; R11 cash/bank | Daily cash and net profit can be reconciled without changing existing gross-profit calculations. |
| 5 | R12–R20 inventory/replenishment/demand/PO follow-up | Physical stock, pending demand and outstanding deliveries have complete operational workflows. |
| 6 | R21–R26 efficiency/reporting, then selected P3 modules | Add only features justified by actual usage and business needs. |

**Recommended next concrete task:** return entitlement and source validation, starting with a reproduction of batch-specific return behavior. An alternative quick usability task is recoverable invoice drafts if interrupted entry is the more frequent operational problem. No implementation is authorized by this document alone.

## 8. Definition of done for future work

For each selected feature: agree workflow examples and exclusions; use the existing permissions/theme/layout; add row-specific errors where applicable; preserve approved formulas; test transaction rollback and relevant stock/ledger effects; verify migrations on existing data; perform browser/print checks when a browser is available; document behavior and remaining limitations. Avoid broad changes to unrelated modules in one feature delivery.
