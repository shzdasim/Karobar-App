# Feature Guides — Screens & Actions Reference (English)

A plain-language tour of every user-facing area in Karobar-App. For technical specs, formulas, and database tables, see [MODULE_INVENTORY.md](../../MODULE_INVENTORY.md).

---

## 1. Items & Masters

| Screen | Route | What You Can Do |
| :--- | :--- | :--- |
| **Products** | `/products` | Search by name, generic formulation, barcode, brand, or rack; add/edit products; bulk edit/delete; import/export CSV. |
| **Product Batches** | `/product-batches` | View all batches for a product, edit MRP/expiry, split/move stock between counter and godown. |
| **Categories & Brands** | `/categories`, `/brands` | Organize medicines (*Tablets*, *Syrups*, *Surgical*) and manufacturers (*GSK*, *Getz*, *Sami*); CSV import/export. |
| **Suppliers & Customers** | `/suppliers`, `/customers` | Manage distributor & credit customer profiles, opening balances, wholesale pricing, and regular refill schedules. |
| **Doctors** | `/doctors` | Save prescribing doctor names/specialties for prescription tracking on sale invoices. |
| **Barcode Studio** *(License: `barcode`)* | `/barcode-labels` | Design and print thermal/sticker barcode labels for products, batches, or received Purchase Orders. |

![Product Index](../../../Screenshots/Product-Index-Page.webp)

---

## 2. Invoices & Orders

| Screen | Route | What You Can Do |
| :--- | :--- | :--- |
| **Barcode POS** *(License: `barcode`)* | `/sale-invoices/pos` | High-speed fullscreen billing with barcode scanner, FEFO batch selection, hold/draft bills, split cash/bank/credit, and instant thermal print. |
| **Sale Invoices** | `/sale-invoices` | Create Retail (`/sale-invoices/create/retail`) or Wholesale (`/sale-invoices/create/wholesale`) invoices, view/edit/void bills, print A4/Thermal, share via Email/WhatsApp. |
| **Trade Schemes** | `/trade-schemes` | Configure quantity-based bonus/free-unit slabs and promotional discounts. |
| **Purchase Invoices** | `/purchase-invoices` | Record distributor bills (GRN), create/update batches, enter bonus units, and update supplier ledger. |
| **Purchase Orders & Forecast** | `/purchase-orders`, `/purchase-orders/forecast` | Generate smart reorder suggestions from sales history, send POs to suppliers, and convert received items into Purchase Invoices. |

![Sale Invoice Index](../../../Screenshots/Sale-Invoice-Index.webp)

---

## 3. Returns & Inventory Controls

| Screen | Route | What You Can Do |
| :--- | :--- | :--- |
| **Sale & Purchase Returns** | `/sale-returns`, `/purchase-returns` | Return items against original bills (system prevents returning more than sold/purchased) and settle via Cash or Ledger Credit. |
| **Batch & Expiry** | `/batch-expiry` | Filter expired, expiring-soon (30/60/90 days), and healthy batches. |
| **User Demands (Shortbook)** | `/user-demands` | Log out-of-stock requests from patients and convert them into Purchase Orders. |
| **Stock Adjustments** | `/stock-adjustments` | Correct verified stock gains/losses with reason codes. |
| **Stocktake Sessions** | `/stocktake-sessions` | Run full or blind physical shelf counts and post approved variance adjustments. |
| **Quarantine & Disposal** | `/stock-disposals` | Isolate expired/damaged stock from POS, print disposal certificates (with witness fields for narcotics), or restore items if quarantined by mistake. |

![Batch & Expiry](../../../Screenshots/BatchExpiryandTracking.webp)

---

## 4. Finance, Ledgers & Reports

| Screen | Route | What You Can Do |
| :--- | :--- | :--- |
| **Customer & Supplier Ledgers** | `/customer-ledger`, `/supplier-ledger` | View running Khata balances, invoice/return/payment history, print or WhatsApp statements. |
| **Payment Vouchers & Party Credits** | `/payments`, `/party-credits` | Record cash/bank/cheque receipts and payments, allocate across multiple invoices, and manage advance credits. |
| **Cash Drawer** | `/cash-drawer` | Open/close daily shifts, record cash-in/cash-out, and verify physical cash parity. |
| **Financial Accounts & Expenses** | `/accounts`, `/expenses` | Manage Cash, Bank, and Mobile Wallets; record daily and recurring shop expenses. |
| **Double-Entry GL & HR/Payroll** *(License: `accounting_gl`, `hr_payroll`)* | `/accounting`, `/hr-payroll` | Chart of Accounts, Journal Entries, Trial Balance, Balance Sheet; Employee attendance, Kharcha advances, commissions, and monthly payroll. |
| **Reports** | `/reports/*` | Net Profit (P&L), Cost of Sale, Sale/Purchase Detail, Current Stock, Reorder, Near Expiry, Stock Adjustment, Outstanding Aging, Narcotics Register, and Tax Return *(License: `fbr`)*. |

---

## 5. System, Sharing & Multi-PC

- **Settings (`/settings`):** Store info, Printer setup (80mm/58mm/A4), Operator PIN, Pharmacy Clinical rules, Theme, SMTP Email, WhatsApp Sidecar, FBR*, Cube AI*, and Backup settings.
- **Users & Roles (`/users`, `/roles`):** Manage staff accounts and granular permissions.
- **Backups (`/backups`):** Create, download, upload, schedule, and restore SQLite backups.
- **Multi-PC LAN (`/server-dashboard`):** Monitor connected counter terminals on your shop's local network.
