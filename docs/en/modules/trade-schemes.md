# Trade Schemes (Bonus & Slab Rules) — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Wholesale Managers, and Purchase Managers.  
> **Where to find it:** Sidebar → **Invoices → Trade Schemes** (`/trade-schemes`) or **Create Trade Scheme** (`/trade-schemes/create`).

---

## A. Purpose and Overview

**Trade Schemes** automate promotional quantity-based bonus rules (such as *"Buy 10 packs, get 1 free"* or *"Buy 50 packs, get 6 free"*). Once active, Karobar-App automatically calculates and fills in bonus quantities when billing eligible products.

---

## B. What Users Can Do

- **Create & Edit Trade Schemes** (`/trade-schemes/create`) with start/end dates, active toggle, and target product/scope.
- **Define Quantity Slabs:** Set minimum purchase/sale quantity thresholds and exact **Free / Bonus** quantities (same product or cross-product bonus).
- **Enable / Disable Schemes** anytime without deleting historical records.

---

## C. Screen-by-Screen Explanation

*(No standalone screenshot is currently stored for `/trade-schemes` in `Screenshots/`.)*

1. **Trade Schemes List (`/trade-schemes`):** Scheme Name, Product/Scope, Date Window, Active Status, and Edit/Delete actions.
2. **Create / Edit Form (`/trade-schemes/create`):** Scheme details (Name, Product, Active dates) and **Slabs Table** (Min Qty → Bonus Qty / Cross-Product Bonus).

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **Scheme Name & Active Toggle** | Input / Switch | Names the promotion and turns automatic application ON/OFF. |
| **Start / End Date** | Date Pickers | Limits when the scheme applies automatically. |
| **Slab Rows (Min Qty → Bonus Qty)** | Table | Defines tiered free quantities (e.g., `10 → 1`, `25 → 3`, `50 → 7`). |

---

## E. Step-by-Step Instructions

1. Go to **Invoices → Trade Schemes → Create** (`/trade-schemes/create`).
2. Enter **Scheme Name** (*"Panadol Winter 10+1"*), select the **Product**, and set valid dates.
3. Add a slab: **Min Qty = `10`**, **Bonus Qty = `1`**.
4. Click **Save**. Now any eligible Sale Invoice for `10+` units auto-fills `1` bonus unit.

---

## F. Practical Business Examples & Simple Calculations

### Example: Tiered Slab Calculation
With slabs `10 → 1 free` and `25 → 3 free`:
- Customer orders `12` packs → qualifies for `10` slab → **1 Free Pack** (`13` packs leave shelf, billed for `12`).
- Customer orders `25` packs → qualifies for `25` slab → **3 Free Packs** (`28` packs leave shelf, billed for `25`).

---

## G. Expected Results

- Eligible Sale Invoices automatically apply the bonus quantity; cashiers can waive a bonus row if needed.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Bonus did not auto-apply on invoice** | Scheme is inactive, outside its Start/End Date, or quantity is below the minimum slab. | Check scheme active status, date range, and slab thresholds in `/trade-schemes`. |

---

## I. Related Features and Workflows

- [Sale Invoices (Retail & Wholesale)](./sale-invoices.md)
- [Purchase Orders](./purchase-orders.md)
