# Products & Pricing — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Inventory Managers, and Data-Entry Staff.  
> **Where to find it:** Sidebar → **Items & Masters → Products** (`/products`) or **Add Product** (`/products/create`).

---

## A. Purpose and Overview

The **Products** module is your pharmacy's master medicine and item catalog. Every medicine, syrup, injection, surgical item, or cosmetic must exist here before you can buy it from a distributor or sell it at the counter.

- **What can I do here?** Add new medicines, set pack sizes (for example, `10` strips per box), record generic salt formulations, assign storage racks, set retail/wholesale prices, bulk-edit items, and import/export CSV lists.
- **How does it help daily business?** Accurate product records prevent pricing mistakes, help cashiers find substitutes when a brand is out of stock, and ensure your shelf units match your box purchases.

---

## B. What Users Can Do

- **Search & Filter** products by name, generic/salt formulation, barcode, product code, rack/shelf, brand, or supplier.
- **Add or Edit** individual medicines, including pharmacy-specific fields (Drug Schedule, Default Dosage in Urdu/English, Cold-Chain Fridge `2°C–8°C`, Narcotic/Controlled flag).
- **Manage Pack vs. Unit Pricing** with automatic retail margin calculation.
- **Bulk Edit or Bulk Delete** multiple selected products at once.
- **Import or Export** your entire product catalog via CSV.

---

## C. Screen-by-Screen Explanation

### 1. Products List (`/products`)

![Products List](../../../Screenshots/Product-Index-Page.webp)

1. **Top Action Bar:** **Add Product**, **Import** (CSV), **Export** (CSV), **Bulk edit**, and **Bulk delete**.
2. **Search Box:** Type any medicine name, generic salt, barcode, code, rack, brand, or supplier.
3. **Products Table:** Shows Code/Barcode, Medicine Name, Category/Brand, Pack Size, On-Hand Stock, Purchase/Sale Prices, and **Edit / Delete** actions.

### 2. Add / Edit Product (`/products/create`)

![Add Product](../../../Screenshots/Add-Product-Page.webp)

1. **Identification & Storage (Top):** Image, **Product Code**, **Barcode** (with auto-generate/scan), **Storage Location**, **Rack / Shelf Code**, and **Narcotic / Controlled** selector.
2. **Medicine & Master Links:** **Name \***, **Formulation (Generic / Salt)**, **Pack Size**, **Category**, **Brand**, **Supplier**, and **Description**.
3. **Pharmacy Clinical Settings:** **Drug Schedule** (*OTC*, *Schedule G*, *Schedule H*, *Controlled / Narcotic*), **Default Dosage** (bilingual Urdu/English presets like `1+0+1 • کھانے کے بعد`), and **Storage Temperature** (*Room Temp 15°C–25°C* or *Cold-Chain Fridge 2°C–8°C*).
4. **Stock & Pricing Grid:** On-Hand Stock (read-only on edit), **Pack Purchase Price**, **Pack Sale Price**, **Unit Purchase Cost**, **Unit Sale MRP**, **Retail Margin (%)**, **Wholesale Pack Price**, and **Wholesale Unit Price**.

---

## D. Every Field, Button, Tab and Option

| Field / Button | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Name \*** | Text (Required) | Full medicine name and strength (e.g., *Augmentin 625mg Tablet*). |
| **Product Code / Barcode** | Text | Manufacturer barcode or store SKU. Scan directly or leave/generate as needed. |
| **Formulation (Generic / Salt)** | Searchable Dropdown | Active chemical salt (e.g., *Co-Amoxiclav*). Enables **Substitutes** lookup at POS. |
| **Pack Size** | Number | Number of sellable units in 1 pack/box (e.g., `10` strips or `6` tablets). |
| **Storage Location & Rack** | Dropdown / Text | Physical shelf or fridge (e.g., `R-12 / Shelf 2`) so new staff find medicines fast. |
| **Drug Schedule & Narcotic** | Dropdowns | Marks prescription-only (`Schedule H`) or controlled/narcotic items for compliance registers. |
| **Default Dosage** | Dropdown / Text | Pre-fills common dosage instructions (e.g., `1+0+1 • کھانے کے بعد (After Meal)`). |
| **Cold-Chain Storage** | Dropdown | Marks insulin/vaccines requiring `2°C – 8°C` fridge storage. |
| **Pack / Unit Purchase & Sale Prices** | Numbers | Entering Pack Price and Pack Size automatically updates Unit Cost/MRP and **Retail Margin (%)**. |
| **On-Hand Stock (Qty)** | Read-Only (on Edit) | Shows current total stock. *Cannot be overwritten directly on Edit—use Purchase Invoice, Stock Adjustment, or Stocktake.* |
| **Import / Export** | Buttons | Opens CSV bulk import modal or downloads your product list as CSV. |
| **Bulk edit / Bulk delete** | Buttons | Updates or removes multiple checked rows at once (permission-controlled). |

---

## E. Step-by-Step Instructions

### Adding a New Medicine
1. Go to **Products → Add Product** (`/products/create`).
2. Enter **Name** (e.g., *Panadol 500mg*), scan or enter **Barcode**, and select **Category** (*Tablets*), **Brand** (*GSK*), and **Formulation** (*Paracetamol*).
3. Set **Pack Size** (e.g., `20` strips per box).
4. Enter **Pack Purchase Price** (`Rs. 540`) and **Pack Sale Price** (`Rs. 640`). Verify **Unit Purchase Cost** (`Rs. 27`) and **Unit Sale MRP** (`Rs. 32`) calculate automatically.
5. Set **Storage Location / Rack** (e.g., `R-01`) and **Drug Schedule** (*OTC / General Medicine*).
6. Click **Save**.

---

## F. Practical Business Examples & Simple Calculations

### Example: Pack-to-Unit Conversion & Retail Margin
Suppose you add **Panadol 500mg Box**:
- **Pack Size** = `20` strips
- **Pack Purchase Price** = `Rs. 540` → **Unit Purchase Cost** = `540 ÷ 20 = Rs. 27.00` per strip
- **Pack Sale Price** = `Rs. 640` → **Unit Sale MRP** = `640 ÷ 20 = Rs. 32.00` per strip
- **Profit per Strip** = `Rs. 32.00 - Rs. 27.00 = Rs. 5.00`
- **Retail Margin (%)** = `(5.00 ÷ 27.00) × 100 ≈ 18.52%` markup (or `15.63%` on sale price, depending on margin setting).

---

## G. Expected Results

- The new medicine appears immediately in **Products**, **Purchase Invoices**, **Purchase Orders**, and **Barcode POS** search.
- Changing **Pack Size** or prices updates future invoices without altering already-posted historical bills.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Cannot change On-Hand Stock on Edit Product** | Direct stock edits are locked to protect against inventory fraud and accounting mismatches. | Use **Purchase Invoice** for new stock, or **Stock Adjustment** / **Stocktake** for verified count corrections. |
| **Unit price looks wrong at POS** | **Pack Size** was left as `1` instead of the number of strips/tablets per box. | Open the product, set the correct **Pack Size**, re-enter Pack Price, and save. |
| **Medicine does not show under Substitutes at POS** | **Formulation (Generic / Salt)** was left blank. | Edit the product and link its **Formulation** (e.g., *Paracetamol*). |

---

## I. Related Features and Workflows

- [Purchase Invoices (Stock In)](../../customer/en/DAILY_WORKFLOWS.md)
- [Barcode POS (Fast Billing)](./barcode-pos.md)
- [Dashboard](./dashboard.md)
