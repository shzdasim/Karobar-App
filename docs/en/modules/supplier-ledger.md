# Supplier Ledger (Khata) — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Accountants, and Purchase Managers.  
> **Where to find it:** Sidebar → **Finance & Ledgers → Supplier Ledger** (`/supplier-ledger`).

---

## A. Purpose and Overview

The **Supplier Ledger (Khata)** tracks every rupee your pharmacy owes to medicine distributors and manufacturers. Every credit Purchase Invoice, Purchase Return (Debit Note), and Supplier Payment appears in one reconciled statement.

---

## B. What Users Can Do

- **Select any Supplier / Distributor** and filter by date range.
- **View Running Payable Balance** (what you owe vs. what you paid/returned).
- **Record Supplier Payments** directly or review allocated bills.
- **Print (`Ctrl+P`)** or **Share PDF** statements with your distributor for reconciliation.

---

## C. Screen-by-Screen Explanation

![Supplier Ledger](../../../Screenshots/Supplier-Ledger.webp)

1. **Top Bar:** **Print (`Ctrl+P`)** and **Share (WhatsApp / Email PDF)**.
2. **Supplier Selector & Actions:** Pick distributor and record payments.
3. **Date Filter & Ledger Table:** Date, Reference/Bill #, Description, Purchases (Payable), Payments/Returns, and Running Balance.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **Supplier Selector** | Searchable Dropdown | Loads the distributor's complete payable Khata. |
| **From / To Date** | Date Pickers | Filters entries to a selected period. |
| **Record Payment** | Button | Opens payment entry to pay the distributor via Cash, Bank, Wallet, or Cheque. |
| **Print / Share** | Buttons | Prints or sends the distributor statement PDF. |

---

## E. Step-by-Step Instructions

1. Open **Finance & Ledgers → Supplier Ledger** (`/supplier-ledger`).
2. Select the **Supplier** (e.g., *GSK Distributor*).
3. Compare the running balance against the distributor representative's monthly statement.
4. Click **Record Payment** (or go to `/payments`) when paying their due bills.

---

## F. Practical Business Examples & Simple Calculations

### Example: Reconciling a Distributor Account
- Credit Purchase `PI-000045` = `+Rs. 40,000` (Payable)
- Near-Expiry Purchase Return `PR-000008` = `-Rs. 3,500` → Balance = `Rs. 36,500`
- Bank Transfer Payment `PAY-000021` = `-Rs. 25,000` → **Remaining Payable = Rs. 11,500**

---

## G. Expected Results

- Every credit purchase, purchase return, and supplier payment updates the payable balance automatically.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Paid bill still shows as unpaid on Supplier Ledger** | Payment was saved without allocating to the bill or under another supplier. | Open **Payment Vouchers** (`/payments`) and check the party and invoice allocations. |

---

## I. Related Features and Workflows

- [Payment Vouchers & Party Credits](./payments.md)
- [Purchase Invoices](./purchase-invoices.md)
- [Purchase Returns](./purchase-returns.md)
