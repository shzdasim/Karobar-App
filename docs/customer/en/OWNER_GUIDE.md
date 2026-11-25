# Pharmacy Owner & Manager Guide (English)

This guide is written for **Pharmacy Owners** and **Store Managers** responsible for profitability, staff control, expiry prevention, supplier payments, and business safety.

---

## 1. Executive Control from the Dashboard

Your [Dashboard](../../../Screenshots/Dashboard.webp) (`/dashboard`) shows live numbers without running manual reports:
- **Gross Sales vs. Purchases vs. Net Profit** for today, this month, or a custom date range.
- **Customer Receivables (Udhar)** and **Supplier Payables** at a glance.
- **Expiry & Low-Stock Warnings** so you can return near-expiry stock to distributors before it becomes dead loss.

---

## 2. Controlling Staff & Preventing Fraud (Users, Roles & Operator PINs)

1. **Create Individual Logins (`/users`):** Never share the Admin login with counter staff. Create separate accounts for **Cashiers**, **Pharmacists**, and **Storekeepers**.
2. **Assign Least-Privilege Roles (`/roles`):**
   - Give cashiers `barcode-pos.view`, `barcode-pos.create`, `sale-invoice.view`, and `sale-invoice.create`.
   - **Restrict sensitive permissions:** Keep `sale-invoice.delete`, `sale-invoice.discount-override`, `sale-invoice.expired-override`, `stock-adjustment.create`, and `report.net-profit.view` for Managers/Owners only.
3. **Supervisor Operator PIN:** Cashiers cannot give extra discounts or sell expired/near-expired items unless a Manager enters their 4-digit Operator PIN (`Settings → Operator PIN`).

![Roles](../../../Screenshots/Roles-Page.webp)

---

## 3. Protecting Profit & Stopping Expiry Loss

- **FEFO (First-Expired, First-Out):** Karobar-App automatically prioritizes the earliest-expiring batch during sales so older stock moves first.
- **Near-Expiry Monitoring (`/batch-expiry` & `/reports/near-expiry-product`):** Review medicines expiring in the next 30/60/90 days every week and process **Purchase Returns** (`/purchase-returns/create`) to your distributor before their return window closes.
- **Quarantine & Disposal (`/stock-disposals`):** Move damaged or expired medicines to Quarantine immediately so cashiers cannot accidentally sell them. Narcotic/controlled drug disposals require witness details.
- **Smart Reorder Forecast (`/purchase-orders/forecast`):** Let Karobar-App calculate reorder quantities from actual sales velocity instead of guessing.

---

## 4. Managing Cash, Khata & Expenses

- **Mandatory Shift Closing (`/cash-drawer`):** Require every cashier to open a shift at start of duty and count physical cash at closing. Compare **Expected Cash** vs. **Counted Cash** to catch shortages immediately.
- **Customer & Supplier Khata (`/customer-ledger`, `/supplier-ledger`):** Track every rupee owed by regular credit customers or payable to distributors, and print/WhatsApp statements from **Payment Vouchers** (`/payments`).
- **Daily & Recurring Expenses (`/expenses`):** Record rent, electricity, staff tea, and salaries (including recurring monthly bills) so **Net Profit (P&L)** (`/reports/net-profit`) reflects true profit, not just gross margin.

![Profit and Loss](../../../Screenshots/Profit-Loss-Report.webp)

---

## 5. Protecting Your Store Data (Backups)

- Enable **Scheduled Backups** under **Backups & Storage** (`/backups`) or **Settings → Backup & Restore**.
- Keep a periodic backup copy on an external USB drive or cloud folder so a hardware failure never wipes your store records.
