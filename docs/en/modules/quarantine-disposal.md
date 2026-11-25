# Quarantine & Disposal — User Manual (English)

> **Who uses this screen?** Pharmacists, Store Managers, and Pharmacy Owners.  
> **Where to find it:** Sidebar → **Stock & Inventory → Quarantine & Disposal** (`/stock-disposals`) or **New Disposal** (`/stock-disposals/create`).

---

## A. Purpose and Overview

The **Quarantine & Disposal** module isolates expired, recalled, or damaged medicines away from active counter stock so cashiers **cannot accidentally sell them**, and maintains witness-verified destruction records (mandatory for narcotics/controlled drugs).

---

## B. What Users Can Do

- **Move Batches to Quarantine** (`/stock-disposals/create`) immediately removing them from sellable shelf stock.
- **Filter by Tab:** *In Quarantine*, *Disposed & Written Off*, and *Restored to Shelf*.
- **Finalize Disposal** (writes off value to P&L and prints a **Disposal Certificate**; requires **Witness Name** for controlled/narcotic medicines).
- **Restore to Shelf** (with a reason) if a batch was quarantined by mistake.

---

## C. Screen-by-Screen Explanation

![Quarantine & Disposal](../../../Screenshots/Quarantine-and-disposal.webp)

1. **Header & Action Bar:** **New Quarantine / Disposal** button.
2. **Summary Cards:** Active in Quarantine, Disposed This Month, and Witness-verified regulatory records.
3. **Status Tabs & Search:** Switch between *In Quarantine*, *Disposed & Written Off*, and *Restored to Shelf*; search by voucher #, product, batch, or witness; filter by From/To date.
4. **Register Table:** Voucher #, Product, Batch, Qty, Value, Witness / P&L Link, Status badge, and **Dispose / Restore / Certificate** actions.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **New Quarantine / Disposal** | Button | Opens `/stock-disposals/create` to isolate expired/damaged batches. |
| **In Quarantine / Disposed / Restored Tabs** | Tabs | Filters vouchers by lifecycle stage. |
| **Witness Name** | Input | **Mandatory** when disposing narcotic/controlled medicines. |
| **Dispose** | Action Button | Permanently writes off quarantined stock and generates a Disposal Certificate. |
| **Restore** | Action Button | Returns quarantined stock back to active sellable batch stock (requires reason). |

---

## E. Step-by-Step Instructions

1. Go to **Quarantine & Disposal → New Quarantine / Disposal** (`/stock-disposals/create`).
2. Select the expired or damaged medicine and **Batch #**, enter quantity, and save to **Quarantine**.
3. When ready to destroy the items, open the **In Quarantine** tab and click **Dispose** (enter **Witness Name** for controlled/narcotic drugs).
4. Print the **Disposal Certificate** for your pharmacy's regulatory file.

---

## F. Practical Business Examples & Simple Calculations

### Example: Narcotic Expiry Disposal
`5` ampoules of a controlled injection (`Purchase Cost = Rs. 400/ampoule`) expire:
- Moving to **Quarantine** immediately blocks all `5` ampoules from POS.
- Clicking **Dispose** with Witness *"Dr. Tariq"* records a `Rs. 2,000` write-off in P&L and prints the signed **Disposal Certificate**.

---

## G. Expected Results

- Quarantined batches cannot be sold at POS or Sale Invoices.
- Finalized disposals write off the stock cost cleanly; Restored items return to sellable stock.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **"Witness name is mandatory" error** | Trying to dispose a controlled/narcotic product without a witness. | Enter the full name of the witness overseeing the destruction. |

---

## I. Related Features and Workflows

- [Batch & Expiry Tracking](./batch-expiry.md)
- [Stock Adjustments](./stock-adjustments.md)
- [Daily Workflows](../../customer/en/DAILY_WORKFLOWS.md)
