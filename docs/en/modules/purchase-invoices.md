# Purchase Invoices (Stock In / GRN) — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Inventory Managers, and Purchase Data-Entry Staff.  
> **Where to find it:** Sidebar → **Invoices → Purchase Invoices** (`/purchase-invoices`) or **Create Purchase Invoice** (`/purchase-invoices/create`).

---

## A. Purpose and Overview

The **Purchase Invoices** module is where every distributor bill enters your pharmacy. Posting a Purchase Invoice adds the received medicines to your shelf stock, creates or updates **Batch Numbers** and **Expiry Dates**, updates purchase cost/margins, and records your payment or supplier credit in the **Supplier Ledger**.

---

## B. What Users Can Do

- **View & Search Purchase Bills** (`/purchase-invoices`) by Posted #, Supplier Invoice #, Supplier Name, and date.
- **Enter Distributor Bills** with **Batch #**, **Expiry Date**, **Pack Quantity**, **Pack Bonus (`PBonus`)**, **Unit Bonus (`UBonus`)**, **Discount %**, and **Tax**.
- **Arrange Rows (`Apply Sort`)** or drag-and-drop rows/brand groups to match the physical paper bill order.
- **Record Paid, Partial, or Credit Bills** (with **Due Date** tracking for credit bills) using Cash, Bank, or Wallet accounts.
- **Edit, Print, Share, or Void/Reverse** Purchase Invoices (subject to stock rules).

---

## C. Screen-by-Screen Explanation

### 1. Purchase Invoices List (`/purchase-invoices`)

![Purchase Invoices List](../../../Screenshots/Purchase-Invoice-Page.webp)

1. **Top Action Bar:** **Create Purchase Invoice** button and filters.
2. **Purchase Table:** Posted # (`PI-000001`), Date, Supplier, Supplier Invoice #, Payment Status, Total Amount, and actions (**View**, **Edit**, **Print**, **Share**, **Void / Delete**).

### 2. Create / Edit Purchase Invoice (`/purchase-invoices/create`)

![Add Purchase Invoice](../../../Screenshots/Add-Purchase-Invoice.webp)

1. **Bill Header:** Posted # (*Auto*), **Date**, **Supplier \***, **Invoice #** (Supplier's Bill #), **Bill Amt** (cross-check against paper bill), **Due Date** (for credit), and **Remarks**.
2. **Arrangement & Drag-and-Drop Bar:** Sort or drag rows/brands to match your supplier's invoice printout.
3. **Items Grid:** Product, **Pack Size**, **Batch**, **Expiry**, **Pack Qty**, **Unit Qty**, **Pack Purchase Price**, **Discount %**, **PBonus**, **UBonus**, and **Line Total**.
4. **Totals & Payment Footer:** Gross Total, Discount, Tax, **Grand Total**, **Payment Account** selector, **Amount Paid**, and **Save** buttons.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Supplier \*** | Searchable Dropdown | Required. Selects the distributor. |
| **Invoice #** | Text | Enter the distributor's paper bill number (must be unique per supplier). |
| **Bill Amt** | Number | Optional cross-check field—enter the paper bill's printed total to verify your entry matches. |
| **Batch & Expiry** | Text / Date | Required when a batch number is entered. Creates/updates that batch's stock. |
| **Pack Qty & Unit Qty** | Numbers | Enter packs (boxes) or units. Fractional packs round cleanly to integer units. |
| **PBonus / UBonus** | Numbers | Free bonus packs or units from the distributor (billed at Rs. 0, added to stock). |
| **Discount %** | Number | Trade discount percentage given on that line or whole invoice. |
| **Apply Sort** | Button | Sorts rows by selected arrangement (e.g., brand or supplier's remembered order). |
| **Payment Account & Paid** | Selector / Number | Enter amount paid now (if Cash/Bank) or leave `0` for a Credit bill. |

---

## E. Step-by-Step Instructions

### Entering a Distributor Purchase Bill
1. Go to **Purchase Invoices → Create** (`/purchase-invoices/create`).
2. Select **Supplier** (e.g., *Ferozsons Distributor*), enter **Invoice #** (e.g., `INV-88412`), and optionally enter **Bill Amt**.
3. Search and add each medicine from the paper bill:
   - Enter **Batch** (e.g., `F-204`) and **Expiry** (e.g., `2028-05-31`).
   - Enter **Pack Qty** (e.g., `10`), **PBonus** (e.g., `1`), **Pack Purchase Price**, and **Discount %**.
4. Verify your **Grand Total** matches the distributor's paper **Bill Amt**.
5. If paying immediately, select **Payment Account** and enter **Amount Paid**; otherwise set **Due Date** for credit.
6. Click **Save**.

---

## F. Practical Business Examples & Simple Calculations

### Example: Purchase Total with Trade Discount & Free Bonus
You buy **10 packs** of *Panadol CF* at **Rs. 250/pack** with **10% Trade Discount** and **1 Free Bonus Pack (`PBonus = 1`)**:
- **Gross Cost** = `10 × Rs. 250 = Rs. 2,500`
- **Trade Discount (10%)** = `Rs. 2,500 × 0.10 = Rs. 250`
- **Net Payable to Supplier** = `Rs. 2,500 - Rs. 250 = Rs. 2,250`
- **Total Stock Added** = `10 paid + 1 bonus = 11 packs` added to the batch.
- **Effective Cost per Pack** = `Rs. 2,250 ÷ 11 ≈ Rs. 204.55` per pack.

---

## G. Expected Results

- Paid + bonus quantities immediately increase both **Batch Stock** and **Total Product Stock**.
- Cash/Bank payments update your **Cash Drawer** or **Financial Account**; unpaid credit balances post to **Supplier Ledger**.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **"Expiry date is required for batch" error** | A **Batch** number was typed without an **Expiry** date. | Enter the expiry date printed on the medicine box before saving. |
| **Duplicate Product + Batch error** | The same medicine + batch number appears on two separate rows of the same bill. | Combine them into one row (add quantities together) or check if one row is a different batch. |
| **Cannot reverse/delete a Purchase Invoice** | Some stock from that bill has already been sold. | You cannot reverse a purchase after its stock is sold; process a **Purchase Return** for remaining stock instead. |

---

## I. Related Features and Workflows

- [Purchase Orders & Smart Reorder](./purchase-orders.md)
- [Purchase Returns](./purchase-returns.md)
- [Products & Pricing](./products.md)
