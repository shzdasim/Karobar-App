# Customer Ledger (Khata) — User Manual (English)

> **Who uses this screen?** Cashiers, Accountants, Store Managers, and Pharmacy Owners.  
> **Where to find it:** Sidebar → **Finance & Ledgers → Customer Ledger** (`/customer-ledger`).

---

## A. Purpose and Overview

The **Customer Ledger (Khata)** replaces your paper credit register for regular patients, clinics, and wholesale buyers. Every credit sale, sale return, and payment recovery appears in chronological order with a live running balance.

---

## B. What Users Can Do

- **Select any Customer** and filter their statement by **From Date / To Date**.
- **View Running Balance** (Total Debit, Total Credit, and Net Receivable).
- **Record a Payment Receipt** directly from the ledger or add manual ledger entries.
- **Print (`Ctrl+P`)** A4 or Thermal customer statements, or **Share PDF** via WhatsApp / Email.

---

## C. Screen-by-Screen Explanation

![Customer Ledger](../../../Screenshots/Customer-Ledger.webp)

1. **Top Action Bar:** **Print (`Ctrl+P`)** and **Share (WhatsApp / Email PDF)** buttons.
2. **Customer & Entry Bar:** Searchable **Customer** selector, **Record Payment** button, and direct sheet entry controls.
3. **Date Filter Strip:** **From Date** and **To Date** filters.
4. **Ledger Table:** Date, Reference/Voucher #, Description, Debit (Udhar added), Credit (Paid/Returned), and Running Balance.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **Customer Selector** | Searchable Dropdown | Loads that customer's complete Khata statement. |
| **From / To Date** | Date Pickers | Filters ledger entries to a specific date range. |
| **Record Payment** | Button | Opens payment entry to record cash/bank/cheque recovery against the customer's balance. |
| **Print (`Ctrl+P`)** | Button | Prints the statement in A4 or Thermal format. |
| **Share** | Button | Sends the PDF statement to the customer on WhatsApp or Email. |

---

## E. Step-by-Step Instructions

1. Open **Finance & Ledgers → Customer Ledger** (`/customer-ledger`).
2. Select the **Customer** (e.g., *Al-Shifa Clinic*).
3. Optionally set **From Date** and **To Date** to check this month's activity.
4. Click **Print (`Ctrl+P`)** or **Share** to send the balance statement on WhatsApp.

---

## F. Practical Business Examples & Simple Calculations

### Example: Running Customer Balance
- Opening Balance = `Rs. 5,000`
- Credit Sale Invoice `SI-000120` = `+Rs. 3,500` (Debit) → Balance = `Rs. 8,500`
- Customer Pays `Rs. 4,000` Cash = `-Rs. 4,000` (Credit) → Balance = `Rs. 4,500`
- Sale Return Credit Note `SR-000014` = `-Rs. 500` (Credit) → **Final Receivable = Rs. 4,000**

---

## G. Expected Results

- Every credit invoice, sale return, and payment voucher updates the customer's running balance automatically.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Customer payment did not reduce balance** | Payment was recorded against Walk-in Customer or wrong customer. | Verify the customer name on the Payment Voucher in `/payments`. |

---

## I. Related Features and Workflows

- [Payment Vouchers & Party Credits](./payments.md)
- [Sale Invoices](./sale-invoices.md)
- [Supplier Ledger](./supplier-ledger.md)
