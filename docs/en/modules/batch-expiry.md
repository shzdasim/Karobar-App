# Batch & Expiry Tracking — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Inventory Managers, and Purchase Staff.  
> **Where to find it:** Sidebar → **Stock & Inventory → Batch & Expiry** (`/batch-expiry`) or **Items & Masters → Product Batches** (`/product-batches`).

---

## A. Purpose and Overview

The **Batch & Expiry Tracking** module monitors every medicine batch on your shelves so you never lose money to silent expiration or sell an expired medicine by mistake.

- **What can I do here?** Filter batches by expiry status (**Expired**, **Critical**, **Warning**, **Safe**) or remaining months, filter by location/brand/supplier, and jump to the **Batch Workbench** to adjust or split batches.
- **How does it help daily business?** Lets you spot batches expiring in 60–90 days early enough to return them to your distributor or run a clearance push.

---

## B. What Users Can Do

- **Click KPI Status Cards** (*All*, *Expired*, *Critical*, *Warning*) to filter batches instantly.
- **Filter by Remaining Months** (e.g., within `3` or `6` months) and **More Filters** (Storage Location, Supplier, Brand).
- **Open Batch Workbench** (`/product-batches`) to view/edit batch MRP, expiry dates, or move stock between counter and godown.

---

## C. Screen-by-Screen Explanation

![Batch & Expiry Tracking](../../../Screenshots/BatchExpiryandTracking.webp)

1. **Header & Action Bar:** **Batch Workbench** button, **Refresh**, and **Near Expiry Report** shortcut.
2. **4 Interactive KPI Filter Cards:** Click **All Batches**, **Expired**, **Critical**, or **Warning** to filter the table immediately.
3. **Filter Strip:** Remaining Months filter and **More Filters** (Storage Location, Supplier, Brand).
4. **Batches Table:** Medicine name, Batch #, Expiry Date, Remaining Days/Status badge, Storage Location, On-Hand Stock, and action links.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **KPI Cards (`All` / `Expired` / `Critical` / `Warning`)** | Clickable Cards | Filters the batch list to that expiry bucket. |
| **Remaining Months** | Number / Mode | Shows batches expiring within (or exact) N months. |
| **More Filters** | Collapsible Panel | Filter by **Storage Location**, **Supplier**, or **Brand**. |
| **Batch Workbench** | Button | Opens `/product-batches` for direct batch editing and stock moves. |

---

## E. Step-by-Step Instructions

### Weekly Expiry Check for Distributor Returns
1. Open **Stock & Inventory → Batch & Expiry** (`/batch-expiry`).
2. Click the **Critical** or **Warning** KPI card (or set Remaining Months to `3`).
3. Open **More Filters** and select a **Supplier** (e.g., *Muller & Phipps*).
4. Print or note the listed Batch Numbers and process a **Purchase Return** (`/purchase-returns/create`) before the distributor's return window closes.
5. For any **Expired** batches that cannot be returned, move them to **Quarantine & Disposal** (`/stock-disposals/create`) immediately.

---

## F. Practical Business Examples & Simple Calculations

### Example: Preventing Rs. 18,000 Expiry Loss
You spot `15` packs of *Cefixime 400mg* (Purchase Cost `Rs. 1,200/pack`, Total Value `Rs. 18,000`) expiring in **75 days** under the **Warning** bucket. Returning them 2 months early recovers `Rs. 18,000` on your **Supplier Ledger** instead of writing off dead stock.

---

## G. Expected Results

- Filtering updates the table immediately without altering any stock.
- Expired batches remain blocked at POS unless explicitly overridden by an authorized supervisor.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Expired item still appears in stock** | Batch was never moved to Quarantine or returned to supplier. | Go to **Quarantine & Disposal** (`/stock-disposals/create`) to isolate it from POS. |
| **Batch expiry date is wrong** | Mistyped during Purchase Invoice entry. | Open **Batch Workbench** (`/product-batches`) and correct the expiry date (if permitted). |

---

## I. Related Features and Workflows

- [Quarantine & Disposal](./quarantine-disposal.md)
- [Purchase Returns](./purchase-returns.md)
- [Dashboard](./dashboard.md)
