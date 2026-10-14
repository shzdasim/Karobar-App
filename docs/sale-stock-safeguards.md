# Sale stock safeguards — 13 October 2026

Sale creation validates current server-side stock inside its transaction before posting an invoice, ledger entry or stock change. Requested quantities are summed across repeated product and batch rows. Product and batch rows are locked in ascending ID order and held until commit/rollback, serializing sales that share products on databases supporting row locks.

Selected batches must match the product and, when supplied, expiry. Missing or ambiguous batches are rejected. Batch lookup uses the actual schema columns `batch_number` and `expiry_date`. Sales without a selected batch retain their existing product-level behavior; automatic batch allocation is not introduced.

Sale edits lock and reload the invoice and credit its original quantities when checking availability. Rejected edits leave invoice, lines, ledger and stock unchanged. Sale deletion takes the same invoice/product/batch locks before restoring quantities. Stock deduction rejects negative outcomes rather than silently clamping them to zero.

Retail and wholesale forms display readable server validation messages showing product/batch availability and requested units. Pricing, discounts, taxes, weighted-average, margin, saved-cost selection and profit formulas remain unchanged. No schema migration is required.

Verification: 49 backend tests pass (314 assertions), including stale second-cashier submissions, combined duplicate rows, insufficient/missing batch rejection and edits reusing original product/batch units. Existing cost snapshot and multi-price purchase tests remain passing. Production build and diff checks pass with existing CSS and bundle-size warnings.

Tests use isolated in-memory SQLite. Real parallel MySQL/PostgreSQL requests and interactive browser behavior were not manually verified. These locks cover sale creation/edit/deletion; concurrency across other stock-writing modules is a separate consistency audit.
