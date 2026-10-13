# Forecast product arrangement todo

- [x] Original forecast order (restore the API order).
- [x] Product name A–Z / Z–A, using natural, case-insensitive comparison.
- [x] Brand A–Z / Z–A → product A–Z.
- [x] Supplier A–Z / Z–A → product A–Z.
- [x] Supplier A–Z → brand A–Z → product A–Z.
- [x] Brand A–Z → product code.
- [x] Product code ascending / descending (natural numeric order).
- [x] Order packs highest / lowest first.
- [x] Order amount highest / lowest first.
- [x] Current stock lowest / highest first.
- [x] Sales/demand units highest / lowest first.
- [x] Pack purchase price lowest / highest first.
- [x] Pack size smallest / largest first.
- [x] Last sold newest / oldest first (missing dates last).
- [x] User demands first → brand → product.
- [x] Purchase returns first → brand → product.
- [x] Put missing brands/suppliers/codes last; break ties deterministically by product name/code/ID.
- [x] Add a compact grouped dropdown consistent with existing forecast filters.
- [x] Rearrange immediately without changing quantities, totals or API data; reset row selection on user sort changes.
- [x] Remember arrangement on this browser; use it for filtering, A4/thermal printing and order creation.
- [x] Verify every arrangement and run the production build.

Manual supplier-specific line sequencing and saved per-supplier templates are future enhancements; this dropdown uses the fields currently available in the forecast.

Verification: all 27 arrangements are covered by five passing Node tests in `tests/Frontend/purchaseOrderArrangement.test.mjs`. Production build passed with existing shared CSS and bundle-size warnings. Browser verification was unavailable.
