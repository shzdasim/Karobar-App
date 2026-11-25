# Stocktake Sessions (Physical Count) — User Manual (English)

> **Who uses this screen?** Store Managers, Inventory Auditors, and Pharmacy Owners.  
> **Where to find it:** Sidebar → **Stock & Inventory → Stocktake Sessions** (`/stocktake-sessions`) or **New Stocktake Session** (`/stocktake-sessions/create`).

---

## A. Purpose and Overview

The **Stocktake Sessions** module lets you physically count your pharmacy shelves—either the entire store (**Full Count**) or a specific category/brand/rack (**Cycle Count**)—and automatically reconcile computer stock against real shelf stock.

- **Why use Blind Mode?** When **Blind Count Mode** is active, staff counting the shelves **cannot see** the expected system quantity—preventing lazy guessing and forcing an honest physical count.

---

## B. What Users Can Do

- **Start a Full or Cycle Count** (`/stocktake-sessions/create`) filtered by Category, Brand, Supplier, or Storage Location.
- **Enable Blind Count Mode** so counters only enter physical counts without seeing system quantities.
- **Count by Product / Batch** (`/stocktake-sessions/:id`), search items, and save counts incrementally.
- **Review Variance & Finalize** (`/stocktake-sessions/:id/review`)—once finalized by an authorized manager, Karobar-App posts the exact variance adjustment automatically.

---

## C. Screen-by-Screen Explanation

### 1. Start Stocktake Session (`/stocktake-sessions/create`)

![New Stocktake Session](../../../Screenshots/StockTakeSessionStart.webp)

1. **Reference Number & Session Type:** Choose **Full Count** or **Cycle Count** (select scope: Category, Brand, Supplier, or Storage Location).
2. **Blind Count Mode Toggle:** Active by default to hide system stock from counters.
3. **Notes:** Optional note (e.g., *"End of month count, Shelf A–C"*).

### 2. Counting & Review Session (`/stocktake-sessions/:id`)

![Stocktake Session](../../../Screenshots/StockTake-Session.webp)

1. **Header & Blind Mode Badge:** Session reference, scope, and **Blind Mode** indicator.
2. **Search & Counting Grid:** Search by medicine/batch and enter physical counted quantity per row.
3. **Review & Finalize:** Managers with `stocktake.finalize` permission review variance quantities/values and click **Finalize** to update stock.

*(Related view: [New Stocktake View](../../../Screenshots/New%20StockIntake-Session.webp))*

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **Session Type** | Selector | **Full Count** (entire store) or **Cycle Count** (specific Category/Brand/Supplier/Location). |
| **Blind Count Mode** | Toggle | Hides system stock, variance, and cost values from counters without `stocktake.finalize`. |
| **Counted Qty** | Number | Physical units counted on the shelf for each batch. |
| **Finalize** | Button | Locks the session and auto-creates a **Stock Adjustment** for all variances. |

---

## E. Step-by-Step Instructions

1. Go to **Stocktake Sessions → New Stocktake Session** (`/stocktake-sessions/create`).
2. Select **Cycle Count**, pick **Category** → *Antibiotics*, keep **Blind Count Mode** ON, and click **Start Session**.
3. Walk the shelf and enter **Counted Qty** for each medicine batch.
4. Click **Review** (`/stocktake-sessions/:id/review`) to inspect shortages/excesses (Manager view).
5. Click **Finalize** to post the verified adjustment.

---

## F. Practical Business Examples & Simple Calculations

### Example: Shelf Variance on Finalization
- **System Expected Stock** of *Augmentin 625mg* = `50` strips
- **Physical Counted Stock** = `48` strips
- **Variance** = `48 - 50 = -2` strips (missing `2` strips)
- **Result on Finalize:** Karobar-App automatically reduces stock by `2` strips via a locked Stock Adjustment.

---

## G. Expected Results

- During counting, shelf stock is untouched until **Finalize** is clicked.
- After finalization, the session is locked and cannot be edited; variances are posted cleanly.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Counter cannot see expected stock or variance value** | **Blind Count Mode** is active and user lacks `stocktake.finalize`. | This is intentional! Only the Manager reviewing at the end should see variances. |
| **Cannot delete a finalized Stocktake adjustment** | Finalized stocktake adjustments are locked for audit safety. | Verify counts carefully on the **Review** screen before clicking **Finalize**. |

---

## I. Related Features and Workflows

- [Stock Adjustments](./stock-adjustments.md)
- [Batch & Expiry Tracking](./batch-expiry.md)
- [Daily Workflows](../../customer/en/DAILY_WORKFLOWS.md)
