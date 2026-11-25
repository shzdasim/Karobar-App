# Purchase Returns (Debit Notes) — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Inventory Managers, and Return/Purchase Staff.  
> **Where to find it:** Sidebar → **Returns → Purchase Returns** (`/purchase-returns`) or **Create Purchase Return** (`/purchase-returns/create`).

---

## A. Purpose and Overview

The **Purchase Returns** module lets you send near-expiry, expired, damaged, or excess medicines back to your distributor and automatically deduct that amount from your **Supplier Ledger** (or record a cash/bank refund).

- **What can I do here?** Return medicines against a specific **Purchase Invoice** or create an **Open Return** for a supplier, select exact **Batch Numbers**, and settle via **Credit Note** (reducing supplier dues) or **Instant Cash/Bank Refund**.
- **How does it help daily business?** Protects your pharmacy from bearing expiry losses by tracking every returned pack and ensuring your distributor credits your account accurately.

---

## B. What Users Can Do

- **View & Search Purchase Returns** (`/purchase-returns`).
- **Return Against a Purchase Invoice:** Select the original supplier bill to auto-load batches, purchase prices, and returnable quantities.
- **Open Purchase Return:** Select a supplier and any medicine/batch currently in stock when no single bill is referenced.
- **Settle via Supplier Ledger (Credit Note) or Cash/Bank Refund.**
- **Edit, Print, Share, or Delete** Purchase Returns (subject to permissions).

---

## C. Screen-by-Screen Explanation

### 1. Purchase Returns List (`/purchase-returns`)

![Purchase Returns Index](../../../Screenshots/Purchase-return-index.webp)

1. **Top Bar:** **Create Purchase Return** button and search filters.
2. **Returns Table:** Return #, Date, Supplier, Linked Purchase Invoice #, Settlement Mode, Total Amount, and Print/Edit/Delete actions.

### 2. Create / Edit Purchase Return (`/purchase-returns/create`)

![Add Purchase Return](../../../Screenshots/Add-Purchase-return-page.webp)

1. **Header Strip:** Posted # (*Auto on Save*), **Date**, **Supplier \***, and **Purchase Invoice (optional)**.
2. **Return Items Table:** Medicine, **Batch #**, Available / Returnable Qty, **Return Pack Qty / Unit Qty**, Purchase Price, Discount %, and Sub Total.
3. **Summary & Settlement Footer:** Gross Total, Discount, Tax, **Total**, and **Settlement Outcome** (**Adjust Supplier Ledger (Credit Note)** vs. **Instant Refund (Cash / Bank / Wallet)**).

---

## D. Every Field, Button, Tab and Option

| Element | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Supplier \*** | Searchable Dropdown | Required. Selects the distributor receiving the returned stock. |
| **Purchase Invoice (optional)** | Searchable Dropdown | Optional. Links the return to a specific distributor bill to cap returnable quantities. |
| **Batch #** | Dropdown | Selects the exact batch being sent back (deducts from that batch's stock). |
| **Return Pack / Unit Qty** | Numbers | Enter packs or units returned (cannot exceed available batch stock or invoice limit). |
| **Adjust Supplier Ledger (Credit Note)** | Radio Option | Auto-selected for credit bills; reduces the balance you owe the supplier. |
| **Instant Refund (Cash / Bank / Wallet)** | Radio Option | Auto-selected for cash bills; records money received back into your Cash Drawer or Bank account. |
| **Save / Update Return** | Button | Deducts batch stock and posts the credit note or refund. |

---

## E. Step-by-Step Instructions

### Returning Near-Expiry Stock to a Distributor
1. Go to **Purchase Returns → Create** (`/purchase-returns/create`).
2. Select the **Supplier** (e.g., *Muller & Phipps*).
3. Optionally select the original **Purchase Invoice** (or leave blank for Open Return mode).
4. Add the medicine and pick the near-expiry **Batch #** (e.g., `MP-109`).
5. Enter **Return Pack Qty** (e.g., `3` packs). Verify price and total.
6. Choose **Adjust Supplier Ledger (Credit Note)** (to deduct from your next bill) and click **Save**.

---

## F. Practical Business Examples & Simple Calculations

### Example: Returning 3 Packs of Near-Expiry Syrup
You return **3 packs** of *Brufen Syrup* bought at **Rs. 480/pack** with **5% Trade Discount**:
- **Gross Return** = `3 × Rs. 480 = Rs. 1,440`
- **Discount (5%)** = `Rs. 72`
- **Net Credit Note** = `Rs. 1,440 - Rs. 72 = Rs. 1,368`
- **Result:** `3` packs are deducted from batch `MP-109`, and your **Supplier Ledger** payable balance drops by **Rs. 1,368**.

---

## G. Expected Results

- Returned quantities are immediately deducted from the selected **Batch** and **Total Product Stock**.
- **Credit Note** reduces your payable balance in **Supplier Ledger**; **Cash/Bank Refund** adds money to your **Cash Drawer** or **Financial Account**.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Batch is not listed in Open Return mode** | That batch has `0` available stock or belongs to another product. | Check `/product-batches` to verify remaining stock on the batch. |
| **Cannot return more than available quantity** | Return quantity exceeds what is left in the batch or on the linked invoice. | Reduce Return Qty to match actual remaining stock. |

---

## I. Related Features and Workflows

- [Purchase Invoices (Stock In)](./purchase-invoices.md)
- [Purchase Orders & Smart Reorder](./purchase-orders.md)
- [Daily Workflows](../../customer/en/DAILY_WORKFLOWS.md)
