# Payment Vouchers & Party Credits — User Manual (English)

> **Who uses this screen?** Accountants, Cashiers, Store Managers, and Pharmacy Owners.  
> **Where to find it:** Sidebar → **Finance & Ledgers → Payment Vouchers** (`/payments`) and **Party Credits** (`/party-credits`).

---

## A. Purpose and Overview

The **Payment Vouchers** module records all money received from customers (**Receipts**) and all money paid to suppliers (**Payments**)—whether in Cash, Bank Transfer, Mobile Wallet, or Post-Dated Cheques (PDC)—and links them to specific unpaid invoices.

---

## B. What Users Can Do

- **Record Customer Receipts & Supplier Payments** with automatic or manual multi-invoice allocation.
- **Track Post-Dated Cheques (PDC):** Mark cheques as *Deposited*, *Cleared*, or *Bounced* (with bounce reason).
- **Manage Unapplied Credits (`/party-credits`):** View advance payments or credit notes and click **Allocate** later to settle new invoices.
- **Filter** by Payment #, Party, Status, **Cheque Status**, **Unapplied Credits Only**, and Date Range.
- **Print or Share** payment receipts on WhatsApp / Email.

---

## C. Screen-by-Screen Explanation

![Payment Vouchers](../../../Screenshots/Payment%20Voucers.webp)

1. **Top Action Bar:** **Record Payment** button and **Party Credits** link.
2. **Filter Strip:** Search payment#/reference, Party filter, Status, **Cheque Status**, **Unapplied Credits Only** toggle, and From/To dates.
3. **Vouchers Table:** Voucher #, Date, Party (Customer/Supplier), Method (Cash/Bank/Cheque/Credit Note), Amount, **Allocated**, Unapplied Credit, Status, and **Allocate / Deposit / Clear / Bounce / Print / Reverse** actions.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **Record Payment** | Button | Opens the modal to receive money from a customer or pay a supplier. |
| **Cheque Status Filter & Actions** | Dropdown / Buttons | Tracks cheques from *Pending* → *Deposited* → *Cleared* (deposits to account) or *Bounced* (reverses ledger credit). |
| **Unapplied Credits Only** | Toggle | Shows only advance payments/credit notes waiting to be applied to invoices. |
| **Allocate** | Row Button | Applies an unapplied credit or advance to one or more unpaid invoices. |

---

## E. Step-by-Step Instructions

### Receiving a Customer Payment Across Multiple Invoices
1. Go to **Payment Vouchers** (`/payments`) and click **Record Payment**.
2. Choose **Customer Receipt**, select the Customer (e.g., *Al-Shifa Clinic*), and enter **Amount** (`Rs. 15,000`).
3. Select **Payment Method** (*Cash* or *Bank*) and the receiving **Financial Account**.
4. Allocate the `Rs. 15,000` across their oldest pending invoices (or click **Auto Allocate**) and save.

---

## F. Practical Business Examples & Simple Calculations

### Example: Allocating a Lump-Sum Recovery
A customer owes `Rs. 8,000` on `SI-101` and `Rs. 10,000` on `SI-105` (`Rs. 18,000` total) and pays **Rs. 12,000**:
- `SI-101` receives `Rs. 8,000` → status becomes **Paid**.
- `SI-105` receives the remaining `Rs. 4,000` → status becomes **Partial** (`Rs. 6,000` still due).

---

## G. Expected Results

- Paid/cleared amounts update your **Cash Drawer** (if Cash) or **Financial Account**, reduce the party's ledger balance, and mark allocated invoices Paid/Partial.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Cheque payment didn't increase Bank balance yet** | Post-dated cheques only affect Bank balance once marked **Cleared**. | Click **Deposited** then **Clear** on the cheque row when it clears the bank. |

---

## I. Related Features and Workflows

- [Customer Ledger](./customer-ledger.md)
- [Supplier Ledger](./supplier-ledger.md)
- [Financial Accounts](./financial-accounts.md)
