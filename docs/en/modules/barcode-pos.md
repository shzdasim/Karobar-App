# Barcode POS (Fast Counter Billing) — User Manual (English)

> **Who uses this screen?** Counter Cashiers, Salesmen, and Shift Supervisors.  
> **Where to find it:** Sidebar → **Invoices → Barcode POS** (`/sale-invoices/pos`). *(Requires the `barcode` license module.)*

---

## A. Purpose and Overview

**Barcode POS** is Karobar-App's fullscreen, high-speed checkout terminal built for busy pharmacy counters. It lets cashiers scan medicines, serve walk-in or credit patients, hold bills for waiting customers, and print thermal receipts in seconds without leaving the keyboard.

---

## B. What Users Can Do

- **Scan or Search Medicines** via barcode scanner (`F2`), product name/salt search (`F3`), or SKU.
- **Sell in Packs or Units** with automatic **FEFO (First-Expired, First-Out)** batch selection.
- **Park / Hold Bills (`F8` / `Ctrl+H`)** so another customer can be billed immediately.
- **Accept Multiple Payment Modes:** Cash, Card, Mobile Wallet, **Split / Credit** (part cash + part card/wallet), or Customer Credit (Udhar).
- **Apply Discounts or Overrides** (with Supervisor Operator PIN protection when required).
- **Complete & Print (`F4` / `F9` / `Ctrl+S` / `Ctrl+Enter`)** thermal receipts automatically.

---

## C. Screen-by-Screen Explanation

![Barcode POS](../../../Screenshots/High-Speed-Barcode-POS.webp)

1. **Top Bar & Scanner Strip:** Barcode input (`F2`), Product Search (`F3`), Customer & Doctor selector, and Held/Parked Bills indicator.
2. **Live Cart Table (Center):** Lists scanned medicines, Batch # & Expiry, Pack/Unit toggle, Quantity, Unit Price, Item Discount, and Line Total.
3. **Checkout & Tender Panel (Right):** Shows Subtotal, Invoice Discount, Grand Total, Payment Mode tabs (**Cash**, **Card**, **Mobile Wallet**, **Split / Credit**), Cash Received, Change Due, and **Complete Sale** button.
4. **Bottom Keyboard Shortcut Strip:** Quick reminders (`F2` Barcode Input, `F3` Search, `F4/F9` Checkout, `F8` Hold Cart).

---

## D. Every Field, Button, Tab and Option

| Element | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Barcode / SKU (`F2`)** | Input | Scan barcode or type SKU and press `Enter` to add 1 unit/pack to cart. |
| **Product Search (`F3`)** | Search Input | Type medicine name or generic salt; shows stock, price, and **Substitutes** button. |
| **Customer / Doctor** | Selectors | Leave as *Walk-in Customer* for cash sales, or pick a regular customer for credit/refill tracking and doctor prescription logging. |
| **Park / Hold Cart (`F8` / `Ctrl+H`)** | Button | Saves current cart as a draft so you can serve the next patient in line. |
| **Pack / Unit Toggle (Row)** | Toggle | Switch between selling full packs (boxes) or single units (strips/tablets). |
| **Item / Invoice Discount** | Input | Enter flat amount or `%` discount. Extra discounts beyond limit trigger **Supervisor Operator PIN** prompt. |
| **Payment Tabs** | Tabs | Choose **Cash**, **Card** (select Card Account + Slip/Last 4 digits), **Mobile Wallet** (select Wallet Account + TID/Phone), or **Split / Credit** (enter Cash + Card/Wallet amounts). |
| **Cash Received** | Input | Enter cash handed by customer (`Ctrl+C` fills exact total). **Change Due** calculates automatically. |
| **Complete Sale (`F4` / `F9` / `Ctrl+S`)** | Button | Saves the invoice, deducts batch stock, updates Cash Drawer/Account/Ledger, and prints the thermal receipt. |

---

## E. Step-by-Step Instructions

### Processing a Walk-In Cash Sale
1. Ensure your **Cash Drawer** shift is open (`/cash-drawer`).
2. Open **Barcode POS** (`/sale-invoices/pos`).
3. Scan the medicine barcode (or press `F3` and type *Augmentin 625mg*).
4. Set quantity (e.g., `2` strips). Verify the auto-selected **Batch #** and **Expiry** are valid.
5. Enter **Cash Received** (e.g., `Rs. 1,000`). Confirm **Change Due**.
6. Press `F9` (or click **Complete Sale**) to save and print the thermal receipt.

### Holding a Bill for a Waiting Customer
1. While Customer A's items are in the cart, press `F8` (or `Ctrl+H` / **Hold Cart**).
2. The cart clears immediately so you can scan and complete Customer B's bill.
3. Click the **Held / Drafts** indicator to restore Customer A's cart and finish checkout.

---

## F. Practical Business Examples & Simple Calculations

### Example: Split-Tender Checkout
A patient's bill totals **Rs. 1,450** (after a `Rs. 50` discount on `Rs. 1,500`):
- Customer pays **Rs. 1,000** in Cash and **Rs. 450** via JazzCash/EasyPaisa (Mobile Wallet).
- Select the **Split / Credit** tab → enter `1000` in **Cash** → select the **Mobile Wallet** account and enter `450` + Transaction ID.
- **Result:** `Rs. 1,000` is recorded in your **Cash Drawer**, `Rs. 450` is deposited into your **Mobile Wallet** financial account, and the invoice status is **Paid**.

---

## G. Expected Results

- Batch stock decreases immediately using FEFO order.
- Cash sales add to your active **Cash Drawer** shift; Card/Wallet payments credit the chosen **Financial Account**; unpaid balances on a named customer post to **Customer Ledger**.
- A thermal receipt prints automatically if auto-print is enabled in **Printer Settings**.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **"Please open a Cash Drawer shift" warning** | Shift was not opened before billing. | Go to **Cash Drawer** (`/cash-drawer`), click **Open Shift**, and return to POS. |
| **Scanner types into wrong box** | Focus moved away from the barcode input. | Press **`F2`** to snap the cursor straight back to the Barcode/SKU box. |
| **Discount or expired batch blocked** | Cashier role does not have override permission. | Ask a Manager to enter their 4-digit **Supervisor Operator PIN** (for valid discounts only; never sell expired medicine). |

---

## I. Related Features and Workflows

- [Products & Pricing](./products.md)
- [Daily Workflows](../../customer/en/DAILY_WORKFLOWS.md)
- [Employee Training — Level 1](../../customer/en/EMPLOYEE_TRAINING.md)
