# Barcode Studio & Label Printing — User Manual (English)

> **Who uses this screen?** Inventory Staff, Storekeepers, and Pharmacy Managers.  
> **Where to find it:** Sidebar → **Items & Masters → Barcode Studio** (`/barcode-labels`). *(Requires the `barcode` license module.)*

---

## A. Purpose and Overview

**Barcode Studio** lets you design and print custom thermal or A4 barcode stickers for loose strips, unbarcoded surgical items, compounded preparations, or newly received Purchase Orders—complete with Store Name, Medicine Name, MRP Price, Batch #, and Expiry Date.

---

## B. What Users Can Do

- **Design & Save Custom Label Templates** (paper size, sticker width/height, columns, font sizes, show/hide Price, Batch, Expiry, Store Name).
- **Set a Default Template** for one-click sticker printing across the app.
- **Select Products, Batches, or a Purchase Order** and specify exact label quantities per item.
- **Preview & Print** stickers directly to your label/thermal printer.

---

## C. Screen-by-Screen Explanation

![Barcode Studio](../../../Screenshots/Barcode-Studio-page.webp)

1. **Template Selector & Designer (Left):** Pick a saved template, adjust label dimensions/fields, **Save New Template**, **Update Template**, **Set Default**, or **Delete**.
2. **Product / Batch / PO Picker (Center):** Add medicines or load items from a Purchase Order and set label print counts.
3. **Live Label Preview & Print (Right):** Visual preview of the sticker exactly as it will print, plus **Print Labels** button.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **Template Selector** | Dropdown | Loads saved sticker layouts (e.g., `38x25mm 2-Up` or `50x25mm`). |
| **Save / Update / Set Default** | Buttons | Saves your custom label dimensions and field toggles for future use. |
| **Add Product / Load from PO** | Controls | Picks items and quantities of stickers to print. |
| **Print Labels** | Button | Opens the sticker print preview and sends to your label printer. |

---

## E. Step-by-Step Instructions

1. Open **Items & Masters → Barcode Studio** (`/barcode-labels`).
2. Select your sticker template (or enter sticker width/height and click **Save Template**).
3. Search and add the medicine (e.g., *Panadol Strip*), select **Batch #**, and enter label count (`20`).
4. Check the live preview on the right and click **Print Labels**.

---

## F. Practical Business Examples

### Example: Labeling Loose Strips
When you open a 20-strip box of a medicine where only the outer box had a barcode, print `20` small stickers with **Medicine Name + Batch + Expiry + Unit MRP** and stick them on each strip so cashiers scan every strip at POS in 1 second.

---

## G. Expected Results

- Printed barcodes scan cleanly in **Barcode POS** (`/sale-invoices/pos`).

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Stickers print misaligned across labels** | Template width/height or column gap does not match physical sticker roll. | Measure your physical sticker roll in mm, update template dimensions, and click **Update Template**. |

---

## I. Related Features and Workflows

- [Products & Pricing](./products.md)
- [Barcode POS](./barcode-pos.md)
- [Purchase Invoices](./purchase-invoices.md)
