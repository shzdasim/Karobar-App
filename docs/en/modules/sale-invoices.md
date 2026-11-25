# Sale Invoices (Retail & Wholesale) — User Manual (English)

> **Who uses this screen?** Cashiers, Salesmen, Wholesale Billing Staff, Store Managers, and Pharmacy Owners.  
> **Where to find it:** Sidebar → **Invoices → Sale Invoices** (`/sale-invoices`) or **Wholesale Invoices** (`/wholesale-invoices`). *(For fast scanner checkout, see [Barcode POS](./barcode-pos.md).)*

---

## A. Purpose and Overview

The **Sale Invoices** module is where you create, review, print, share, and manage full-page **Retail** and **Wholesale** customer bills.

- **What can I do here?** Create retail bills (unit/strip level) or wholesale B2B bills (pack/box level with customer-specific rates and free bonus units), hold drafts, print A4 or thermal receipts, share PDFs on WhatsApp/Email, or void an erroneous bill.
- **How does it help daily business?** Every posted invoice automatically deducts the earliest-expiring batches (**FEFO**), calculates your profit margin, updates your **Cash Drawer** or **Customer Khata**, and keeps an audit trail.

---

## B. What Users Can Do

- **Search & Filter Invoices** by Posted Number (e.g., `SI-000001`), Customer name, Sale Type (Retail/Wholesale), and date.
- **Create Retail Sale Invoices** (`/sale-invoices/create/retail`) with item/invoice discounts, tax rates, cash/bank/credit payment, and automatic Trade Scheme bonus application.
- **Create Wholesale Invoices** (`/wholesale-invoices/create`, when Wholesale mode is enabled in **Sale System Settings**) with Pack Quantity, Pack Bonus, and automatic customer-specific wholesale pricing.
- **Resume or Discard Held Drafts (`Ctrl+H` / `Ctrl+D`)** from the **Held Drafts** modal.
- **Print** Thermal or A4 invoices, **Share** via WhatsApp or Email PDF, or **Void / Reverse** an invoice with a reason.

---

## C. Screen-by-Screen Explanation

### 1. Sale Invoices List (`/sale-invoices`)

![Sale Invoices List](../../../Screenshots/Sale-Invoice-Index.webp)

1. **Top Action Bar:** **New Retail Sale**, **New Wholesale Sale** (when enabled), and **Held Drafts (`Ctrl+H` / `Ctrl+D`)**.
2. **Search & Filter Strip:** Search by Posted No (`SI-000001`), Search by Customer, and **Show** page size selector.
3. **Invoices Table:** Invoice #, Date, Customer, Type (Retail/Wholesale), Payment Status (Paid / Partial / Unpaid / Voided), Grand Total, and row actions (**View**, **Print**, **Share**, **Edit**, **Void / Reverse**).

### 2. Create / Edit Sale Invoice (`/sale-invoices/create/retail`)

![Add Sale Invoice](../../../Screenshots/Add-Sale-Invoice-page.webp)

1. **Invoice Header:** Invoice #, Date, **Invoice Type** (*Cash* or *Credit*), **Customer** (Walk-in or named customer with live credit summary), and **Doctor** (optional prescription link).
2. **Items Grid:** Product search, **Batch #** (auto-selected via FEFO), Expiry, **Available Qty**, **Qty**, **Bonus** (auto-filled by Trade Schemes if applicable), **Price**, **Discount % / Amount**, **Tax**, and **Sub Total**.
3. **Totals & Payment Footer:** Gross Total, Invoice Discount, Tax, **Grand Total**, **Payment Account** selector, **Amount Received**, and **Save / Save & Print** buttons.

### 3. Sale System Mode (`Retail` vs. `Retail + Wholesale`)

![Sale System Settings](../../../Screenshots/Sale-System.webp)

- Controlled under **Settings → Sale System** (`/settings`). Enable **Wholesale** to unlock the **Wholesale Invoices** menu and pack-level B2B billing form.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Customer** | Searchable Dropdown | Choose *Walk-in Customer* for cash counter bills, or a named customer to enable Credit (Udhar) and customer-specific wholesale rates. |
| **Invoice Type** | Selector | **Cash** (full payment expected) or **Credit** (unpaid balance posts to Customer Ledger). |
| **Batch #** | Dropdown | Auto-picks earliest non-expired batch (**FEFO**). Expired batches are blocked unless authorized by Supervisor PIN. |
| **Quantity & Bonus** | Numbers | Enter billed quantity. Active **Trade Schemes** automatically populate **Bonus** units (or cross-product bonus rows). |
| **Discount (%) / Amount** | Numbers | Line-level or invoice-level discount. Exceeding your role's max discount triggers the **Discount Override** prompt (Supervisor PIN). |
| **Tax Rate** | Dropdown / % | Select configured sales tax presets (if applicable) or leave at `0%`. |
| **Total Receive & Account** | Input / Dropdown | Enter amount paid now and choose Cash, Bank, or Mobile Wallet account. |
| **Held Drafts (`Ctrl+H`)** | Modal Button | Opens saved incomplete drafts so you can **Resume** or **Discard** them. |
| **Print / Share** | Row Buttons | Opens Thermal/A4 print preview or the **Share Document** modal (WhatsApp PDF / Email). |
| **Void / Reverse** | Row Button | Reverses a posted invoice (requires reason); restores batch stock and reverses ledger/cash entries. |

---

## E. Step-by-Step Instructions

### Creating a Credit Sale Invoice for a Regular Customer
1. Go to **Sale Invoices → Create Retail Sale** (`/sale-invoices/create/retail`).
2. Select the **Customer** (e.g., *Al-Shifa Clinic*) and set **Invoice Type** to **Credit**.
3. Add medicines (e.g., *Augmentin 625mg*, Quantity `10` packs). Confirm the auto-selected Batch and Expiry.
4. If the customer pays part of the bill today (e.g., `Rs. 2,000` out of `Rs. 5,500`), enter `2000` in **Total Receive** and select **Cash** or **Bank**.
5. Click **Save & Print**.

### Voiding (Reversing) an Incorrect Invoice
1. Find the invoice in **Sale Invoices** (`/sale-invoices`).
2. Click the **Void / Reverse** button on that row.
3. Enter the reason (e.g., *"Wrong customer selected"*) and confirm.
4. *(Note: If a Sale Return already exists against this invoice, voiding is blocked.)*

---

## F. Practical Business Examples & Simple Calculations

### Example: Wholesale Bill with Bonus & Partial Credit
A clinic buys **10 packs** of *Panadol CF* at **Rs. 300/pack** with a **10 + 1 Free** Trade Scheme and **5% Discount**:
- **Gross Total** = `10 × Rs. 300 = Rs. 3,000` *(the `1` bonus pack is billed at Rs. 0)*
- **Discount (5%)** = `Rs. 3,000 × 0.05 = Rs. 150`
- **Net Bill Total** = `Rs. 3,000 - Rs. 150 = Rs. 2,850`
- **Customer Pays Today** = `Rs. 1,000` Cash
- **Remaining Credit Balance** = `Rs. 2,850 - Rs. 1,000 = Rs. 1,850` (posted to Customer Ledger)
- **Stock Deducted** = `10 + 1 = 11` packs deducted from the batch.

---

## G. Expected Results

- **Stock:** Billed + bonus quantities are deducted from the selected batch(es).
- **Cash & Accounts:** Amount received is added to your active **Cash Drawer** shift (if Cash) or selected Bank/Wallet account.
- **Customer Khata:** Any unpaid remainder increases the customer's receivable balance.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Credit limit warning blocks saving** | Customer's unpaid balance + new bill exceeds their credit limit (or account is On Hold). | Collect a partial payment, or have a Manager approve via **Credit Override**. |
| **Cannot void/delete a Sale Invoice** | A **Sale Return** has already been recorded against this invoice. | Delete/reverse the linked Sale Return first, or issue an adjustment if appropriate. |
| **Wholesale Invoices menu is missing** | **Wholesale** mode is turned off in Settings. | Go to **Settings → Sale System** and enable Wholesale mode. |

---

## I. Related Features and Workflows

- [Barcode POS (Fast Billing)](./barcode-pos.md)
- [Sale Returns](./sale-returns.md)
- [Products & Pricing](./products.md)
- [Daily Workflows](../../customer/en/DAILY_WORKFLOWS.md)
