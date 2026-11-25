# Sale Returns — User Manual (English)

> **Who uses this screen?** Cashiers, Return-Counter Staff, Store Managers, and Accountants.  
> **Where to find it:** Sidebar → **Returns → Sale Returns** (`/sale-returns`) or **Create Sale Return** (`/sale-returns/create`).

---

## A. Purpose and Overview

The **Sale Returns** module handles medicines returned by customers—such as unopened strips, wrong strengths, or unused boxes—while keeping your inventory batches, Cash Drawer, and Customer Khata accurate.

- **What can I do here?** Process returns linked to an original Sale Invoice (with automatic over-return protection) or open returns for a customer, and settle via **Instant Refund** (Cash/Bank/Wallet) or **Customer Ledger Credit Note**.
- **How does it help daily business?** Prevents cashiers from refunding more quantity than was actually sold, puts returned items straight back onto the right batch, and keeps your cash drawer balanced.

---

## B. What Users Can Do

- **View & Search Sale Returns** in the index (`/sale-returns`).
- **Create a Return Against a Sale Invoice:** Select the original bill to auto-load sold items, batches, prices, and exact **Returnable** quantities.
- **Create an Open Return:** Select a customer and medicine directly when no invoice is linked.
- **Choose Settlement Outcome:** **Adjust Customer Ledger (Credit Note)** or **Instant Refund (Cash / Bank / Wallet)**.
- **Edit, Print, Share, or Delete** returns (subject to permissions).

---

## C. Screen-by-Screen Explanation

### 1. Sale Returns List (`/sale-returns`)

![Sale Returns Index](../../../Screenshots/Sale-Return-Index.webp)

1. **Top Bar:** **Create Sale Return** button and search/filter controls.
2. **Returns Table:** Return #, Date, Customer, Linked Sale Invoice #, Settlement Mode, Total Refund/Credit, and Print/Edit/Delete actions.

### 2. Create / Edit Sale Return (`/sale-returns/create`)

![Add Sale Return](../../../Screenshots/Add-Sale-Return-Page.webp)

1. **Header Strip:** **Posted #** (*Auto on Save*), **Date**, **Customer \***, and **Sale Invoice (optional)**.
2. **Return Items Table:** Medicine, Batch #, **Returnable** (or **Avail**) quantity, **Return Qty**, Price, Discount, and Line Total.
3. **Return Summary & Settlement Outcome (Bottom):** Gross Total, Discount % / Amount, Tax % / Amount, **Total**, and **Settlement Outcome** radio options (**Adjust Customer Ledger (Credit Note)** vs. **Instant Refund (Cash / Bank / Wallet)** + Refund Method selector).

---

## D. Every Field, Button, Tab and Option

| Element | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Posted #** | Read-Only | Auto-generated return number (e.g., `SR-000001`) when saved. |
| **Date** | Date Picker | Date the customer returned the medicine. |
| **Customer \*** | Searchable Dropdown | Required. Select *Walk-in Customer* or the named customer. |
| **Sale Invoice (optional)** | Searchable Dropdown | Pick the original Sale Invoice to auto-fill items, prices, and **Returnable** limit. |
| **Returnable / Avail** | Read-Only Column | Maximum units still eligible to return from that invoice (blocks over-returning). |
| **Return Qty \*** | Number | Quantity returned by the customer (must be `> 0` and `<= Returnable`). |
| **Discount / Tax (%)** | Numbers | Pro-rated or manual discount/tax adjustments matching the original sale. |
| **Adjust Customer Ledger (Credit Note)** | Radio Option | Auto-selected for Credit invoices; reduces the customer's unpaid balance (or adds advance credit). |
| **Instant Refund (Cash / Bank / Wallet)** | Radio Option | Auto-selected for Cash invoices; refunds money immediately from the chosen **Refund Method** account. |
| **Create Return / Update Return** | Button | Saves the return, restores batch stock, and posts the cash refund or ledger credit. |

---

## E. Step-by-Step Instructions

### Processing a Customer Return Against an Invoice
1. Go to **Sale Returns → Create Sale Return** (`/sale-returns/create`).
2. Select the **Customer** and pick the original **Sale Invoice** (for example, `SI-000142`).
3. The form loads the sold medicines and shows the **Returnable** quantity for each row.
4. Enter **Return Qty** for the returned medicine (e.g., `2` strips) and remove or leave `0` on items not returned.
5. Check **Settlement Outcome**:
   - Leave **Instant Refund** (Cash) if handing cash back to a walk-in patient.
   - Choose **Adjust Customer Ledger (Credit Note)** if deducting from a regular customer's Khata.
6. Click **Create Return**.

---

## F. Practical Business Examples & Simple Calculations

### Example: Returning 2 Strips from a 5-Strip Sale
A customer bought **5 strips** of *Zyrtec* at **Rs. 120/strip** (`Rs. 600` total) with a **10% discount** (`Rs. 60`), paying **Rs. 540** net (`Rs. 108/strip` effective).
Two days later, they return **2 unopened strips**:
- **Gross Return** = `2 × Rs. 120 = Rs. 240`
- **Discount (10%)** = `Rs. 24`
- **Net Refund Total** = `Rs. 240 - Rs. 24 = Rs. 216`
- **Stock Effect:** `2` strips are added back to the original batch; `3` strips remain returnable on `SI-000142`.

---

## G. Expected Results

- Returned units are added back to the batch (and total product stock).
- **Instant Refund (Cash)** reduces your active **Cash Drawer** shift balance; **Credit Note** reduces the customer's **Customer Ledger** balance.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **"Return qty exceeds allowed" error** | Trying to return more units than were sold (or already returned on an earlier return). | Check the **Returnable** column and enter a quantity less than or equal to it. |
| **Cash Drawer balance doesn't match after return** | Return was saved as **Credit Note** instead of **Instant Refund (Cash)** (or vice versa). | Edit the return and select the actual **Settlement Outcome** used at the counter. |

---

## I. Related Features and Workflows

- [Sale Invoices](./sale-invoices.md)
- [Barcode POS](./barcode-pos.md)
- [Daily Workflows](../../customer/en/DAILY_WORKFLOWS.md)
