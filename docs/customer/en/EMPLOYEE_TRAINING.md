# Pharmacy Employee Training Curriculum (English)

A structured 3-level training program for new medical store cashiers, dispensers, storekeepers, and shift managers.

---

## Level 1 — Beginner (Counter Cashier & Dispenser)
**Goal:** Handle walk-in patients, scan medicines, print receipts, and manage a cash drawer shift safely.

| Module | Skill to Master | Practice Exercise |
| :--- | :--- | :--- |
| **1.1 Login & Shift Open** | Sign in with personal credentials; open Cash Drawer (`/cash-drawer`) with morning float (`Rs. 5,000`). | Open a shift and verify your name appears on the Topbar drawer badge. |
| **1.2 Barcode POS & Search** | Open `/sale-invoices/pos`. Search *Augmentin 625mg* by name, generic salt, or barcode. | Sell `1` strip (`6` tablets) vs `1` full pack (`10` strips). Check unit conversion. |
| **1.3 Hold / Draft Bills** | Put an incomplete customer bill on hold (`Hold / Draft`) when a patient goes to bring cash. | Hold Bill #1, serve Customer #2, then resume and complete Bill #1. |
| **1.4 Shortbook (User Demands)** | Record out-of-stock requests in `/user-demands/create` when a patient asks for an unavailable medicine. | Log a demand for *Insulatard Penfill* with customer phone number. |
| **1.5 Closing Shift** | Count physical cash notes/coins and close shift in `/cash-drawer`. | Match counted cash against Expected Cash (`0` difference). |

![Barcode POS](../../../Screenshots/High-Speed-Barcode-POS.webp)

---

## Level 2 — Intermediate (Inventory, Purchases & Returns)
**Goal:** Receive distributor stock accurately, manage batch/expiry, and process customer/supplier returns.

| Module | Skill to Master | Practice Exercise |
| :--- | :--- | :--- |
| **2.1 Purchase Invoice (GRN)** | Enter distributor bill in `/purchase-invoices/create` with **Batch No**, **Expiry Date**, **Bonus/Free Qty**, and **Trade Discount %**. | Enter *GSK Distributor* bill: `10` packs + `1` free bonus pack of *Panadol CF*, Batch `B-902`, Expiry `2028-06-30`. |
| **2.2 Sale Returns** | Process returned unopened strips in `/sale-returns/create` against the original Sale Invoice. | Return `2` strips from a `5`-strip sale and choose Cash Refund or Customer Credit. |
| **2.3 Purchase Returns** | Return near-expiry/damaged items to distributor in `/purchase-returns/create`. | Return `3` packs of near-expiry syrup to *Muller & Phipps* and verify supplier ledger updates. |
| **2.4 Batch & Expiry Monitoring** | Check `/batch-expiry` and `/reports/near-expiry-product` weekly. | Identify all batches expiring within `60` days. |
| **2.5 Barcode Label Studio** | Print shelf/pack barcode stickers in `/barcode-labels` for unbarcoded items. | Generate a barcode label with product name, MRP, and batch number. |

![Add Purchase Invoice](../../../Screenshots/Add-Purchase-Invoice.webp)

---

## Level 3 — Advanced (Manager, Finance & Controls)
**Goal:** Control credit ledgers, physical stocktakes, disposals, expenses, and profit/compliance reports.

| Module | Skill to Master | Practice Exercise |
| :--- | :--- | :--- |
| **3.1 Credit & Payment Vouchers** | Record partial customer recoveries and distributor payments in `/payments` and verify `/customer-ledger` & `/supplier-ledger`. | Receive `Rs. 15,000` against two pending credit invoices and print/WhatsApp the receipt. |
| **3.2 Stocktake & Adjustments** | Run a blind/guided physical count in `/stocktake-sessions` and handle variance write-offs; use `/stock-adjustments` for verified corrections. | Run a cycle count on the *Antibiotics* rack and finalize variance. |
| **3.3 Quarantine & Disposal** | Quarantine expired/damaged items in `/stock-disposals/create`; record witness details for narcotic/controlled items. | Quarantine an expired batch so it cannot be sold at POS. |
| **3.4 Reports & Compliance** | Review `/reports/net-profit`, `/reports/cost-of-sale`, `/reports/current-stock`, and `/reports/narcotics`. | Export monthly Net Profit (P&L) PDF and verify expenses are deducted. |
| **3.5 Operator PIN & Overrides** | Authorize valid discount or near-expiry overrides using Supervisor Operator PIN without sharing Admin login. | Approve a `5%` senior-citizen discount at POS using your 4-digit PIN. |
