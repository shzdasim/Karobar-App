# Return entitlement and invoice-style forms

Implemented 14 October 2026.

Linked sale and purchase returns validate the invoice's customer/supplier and the product/batch/expiry identity. Return quantities are checked against actual invoice stock units minus all earlier linked returns. Repeated rows consume one shared allowance. Purchase limits use actual received quantities, including invoice bonus units, rather than PO ordered quantities.

Edits exclude their own previous quantities. Source invoices are locked in ID order, including both sources when relinking. Existing returns are reloaded under a return-row lock; a conflicting source change asks the user to reload. Deletion locks the source before reversing the return and frees its entitlement. These locks serialize linked returns on databases supporting row locks.

Invoice details expose `returnable_quantity`; return forms request allowances excluding the edited return and use the remaining quantities in their quantity helpers. Row errors identify the affected quantity, and customer/supplier errors appear in the persistent summary. Unlinked returns retain the existing workflow because no original entitlement can be inferred; no new permission policy for unlinked returns was introduced.

Sale return uses the sale invoice's entry/header/banner/details/items/footer and action styling. Purchase return uses the corresponding purchase invoice styling. Existing return-specific fields, source selection, shortcuts, formulas and source-cost behavior are preserved. Batch stock queries in return controllers now reference the actual `batch_number` and `expiry_date` database columns.

No migration or historical data rewrite is required. Existing pricing, discount, tax, profit and stock valuation formulas are unchanged. Physical saleable-stock handling, disposal/quarantine and refund-event accounting are separate enhancements.

Validation: 52 backend tests pass, 325 assertions. New tests cover cumulative sale returns, wrong customer and unmatched batch rejection, edit/delete allowance restoration, purchase returns based on actual units, batch stock effects and rollback on rejected excess quantities. Production build and diff checks pass, with existing CSS selector and bundle-size warnings. Interactive browser layout and real parallel MySQL requests were not verified. Further dedicated tests should cover wrong suppliers, duplicate source lines and source relinking under contention.
