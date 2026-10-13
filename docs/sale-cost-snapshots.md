# Saved sale costs — 13 October 2026

New sales save the cost selected by your existing rule: product average price, falling back to purchase price when average price is zero/null. Costs are captured before stock changes. No profit, discount, tax, margin, weighted-average or stock formula was changed; report arithmetic and rounding remain unchanged.

Migration `2026_10_13_000006_add_sale_cost_snapshots.php` adds nullable `cost_price_snapshot` fields to sale/return lines and is applied locally. Other deployments must run pending migrations. No old costs were backfilled and no existing invoice amounts were modified. Rollback drops saved cost information.

Daily cost-of-sale, detail and PDF reports prefer a non-null snapshot (including zero); null snapshots retain the original live-cost fallback. APIs expose `cost_is_estimated`; screens and the detail PDF label estimates. Current-stock/potential-profit calculations are untouched.

Invoice edits preserve existing saved costs by server-owned line ID or unambiguous product/batch/expiry matching. New products added to snapshot-bearing invoices capture the cost available at edit time. Entirely legacy invoices remain estimated when edited. Client cost values are ignored.

New linked returns copy the original saved cost when source customer/product/batch matches unambiguously. Legacy, unlinked or ambiguous returns retain the existing estimated calculation. Return edits preserve matching saved costs; unmatched or changed-source lines remain estimated. Backdated new sales save the cost available when entered; historical reconstruction is not attempted.

Verification: 43 backend tests pass (246 assertions), including eight new snapshot tests covering discounts/tax/profit formula values, price changes, depletion resets, zero costs, legacy fallback, edits, linked returns, client cost rejection, migration preservation and JSON/PDF calculation parity. Production build and diff checks pass with existing CSS and bundle-size warnings. Browser UI, rendered PDF layout and concurrent production-database behavior were not manually verified.


## Multiple purchase price verification

Added two integration scenarios through the actual purchase, sale and return controllers in an isolated in-memory database:

1. Buy 10 units at Rs. 100, sell 2; buy 12 at Rs. 120, sell 2; buy 2 at Rs. 80, sell 2. Sale-time cost snapshots are Rs. 100, Rs. 112 and Rs. 108.80 respectively. With the existing test invoice discount/tax amounts, invoice profits are Rs. 65, Rs. 41 and Rs. 47.40. Earlier reports remain identical after each later purchase. Daily cost is Rs. 641.60 across the three sales.
2. Buy 10 at Rs. 100 and sell 2; buy 10 paid units at Rs. 120 with 10% discount and 2 bonus units. Incoming effective cost is Rs. 90 and moving average becomes Rs. 94. After selling 2 and buying 2 at Rs. 80, the current average becomes Rs. 92.60. A return against the first sale still reverses Rs. 100, and daily net cost is Rs. 288.

All 10 snapshot tests pass (116 assertions). No application formulas or live business records were changed for these tests.
