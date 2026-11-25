# Purchase Orders & Smart Reorder — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Purchase Managers, and Inventory Controllers.  
> **Where to find it:** Sidebar → **Invoices → Purchase Orders** (`/purchase-orders`) or **Smart Reorder Forecast** (`/purchase-orders/forecast`).

---

## A. Purpose and Overview

The **Purchase Orders (PO)** module helps you plan what medicines to order from your distributors before shelves go empty, send clear order lists to suppliers, and receive deliveries straight into a Purchase Invoice without re-typing items.

- **What can I do here?** Generate data-driven reorder suggestions (**Purchase Order Forecast**), create/edit Purchase Orders, email/WhatsApp or print order lists for distributors, and click **Receive** when the delivery van arrives.
- **How does it help daily business?** Stops panic buying and out-of-stock losses by using your real sales speed, current stock, and pending customer demands to suggest exact pack quantities.

---

## B. What Users Can Do

- **Filter & Search Purchase Orders** by status (*Ordered*, *Partially Received*, *Received*, *Cancelled*), supplier name, PO number (`PO-3`), reference, or custom date.
- **Run Smart Reorder Forecast (`Ctrl+N`)** to auto-calculate recommended order packs by supplier/brand.
- **Create / Edit a Purchase Order** with supplier-remembered product ordering (**Apply Saved Order**), Order Packs, Pack Bonus, Unit Bonus, and Discount %.
- **Receive Delivery** directly into a **Purchase Invoice (GRN)**—supporting partial or full deliveries.
- **Print or Share PDF** (WhatsApp / Email) with your distributor, or **Cancel Order** if no longer needed.

---

## C. Screen-by-Screen Explanation

### 1. Purchase Orders List (`/purchase-orders`)

![Purchase Orders Index](../../../Screenshots/Purchase-Order-Index.webp)

1. **Top Action Bar:** **New Purchase Order** and **New Forecast (`Ctrl+N`)**.
2. **Search & Status Tabs:** Filter by *Ordered*, *Partially Received*, *Received*, *Cancelled*, search by supplier/PO #, or pick a custom date.
3. **Orders Table:** PO #, Date, Supplier, Reference, Status badge, Total Amount, and primary **Receive** CTA alongside **View / Edit / Share / Print / Cancel**.

### 2. Create / Edit Purchase Order (`/purchase-orders/create`)

![Add Purchase Order](../../../Screenshots/Add-Purchase-Order.webp)

1. **Order Header:** Order Date, **Reference**, **Supplier \***, and **Notes**.
2. **Order Items Grid:** Product, Brand, Pack Size, **Order Packs**, Received Units, Remaining Units, Pack Purchase Price, Pack Bonus, Unit Bonus, Discount %, and Total.
3. **Footer Bar:** **Add Product**, **Apply Saved Order**, item/pack summary, Grand Total, and **Save Purchase Order (`Ctrl+S`)**.

### 3. Smart Reorder Forecast (`/purchase-orders/forecast`)

![Purchase Order Forecast](../../../Screenshots/Purchase-Order-Forcast.webp)

1. **Forecast Filters:** Choose Supplier, Brands, sales lookback window, and target coverage days.
2. **Forecast Table:** Shows current stock, daily sales velocity, suggested packs to order, and editable order quantities to generate a PO in one click.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **New Forecast (`Ctrl+N`)** | Button | Opens **Purchase Order Forecast** to auto-build an order from sales velocity. |
| **Supplier \*** | Searchable Dropdown | Required. Selects the distributor for the order. |
| **Apply Saved Order** | Button | Arranges rows in the exact sequence remembered for this supplier's bills. |
| **Order Packs & Bonus** | Numbers | Number of boxes/packs to order plus any agreed Pack/Unit Bonus. |
| **Save Purchase Order (`Ctrl+S`)** | Button | Saves the PO in *Ordered* status (does **not** add stock yet). |
| **Receive / Receive Delivery** | Button | Opens a pre-filled **Purchase Invoice** for the remaining undelivered items. |
| **Share / Email PDF** | Button | Sends the PO PDF to your distributor via WhatsApp or Email. |
| **Cancel Order** | Button | Marks an unreceived PO as *Cancelled*. |

---

## E. Step-by-Step Instructions

### Generating & Receiving a Distributor Order
1. Click **Purchase Orders → New Forecast (`Ctrl+N`)** (`/purchase-orders/forecast`).
2. Filter by **Supplier** (e.g., *Muller & Phipps*) and review suggested pack quantities.
3. Adjust quantities if needed and click **Create Purchase Order** → **Save Purchase Order (`Ctrl+S`)**.
4. Click **Share / Email PDF** or **Print Order** to send it to your distributor.
5. When the cartons arrive, open **Purchase Orders**, click **Receive** on that PO, enter **Batch #** and **Expiry Date** for each item, and save the **Purchase Invoice**.

---

## F. Practical Business Examples & Simple Calculations

### Example: Partial Delivery Tracking
You order **20 packs** of *Augmentin 625mg* (`10` strips/pack = `200` units) on `PO-00015`:
- Distributor delivers **12 packs** (`120` units) today.
- **Received Units** = `120` | **Remaining Units** = `200 - 120 = 80` units (`8` packs).
- PO status becomes **Partially Received** until the remaining `8` packs arrive on a second Purchase Invoice.

---

## G. Expected Results

- Creating a PO **does not** change shelf stock or supplier balance—stock and supplier Khata only update when you **Receive** the delivery into a **Purchase Invoice**.
- Linked Purchase Invoices appear in the **Deliveries** table on the PO detail view.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Stock did not increase after saving a Purchase Order** | A Purchase Order is only an order request, not a stock receipt. | Click **Receive** on the PO and save the resulting **Purchase Invoice (GRN)**. |
| **Supplier missing from Forecast** | Forecast filter is restricted to a brand not supplied by that distributor. | Clear brand filters and re-run Forecast. |

---

## I. Related Features and Workflows

- [Purchase Invoices (Stock In)](./purchase-invoices.md)
- [Purchase Returns](./purchase-returns.md)
- [Daily Workflows](../../customer/en/DAILY_WORKFLOWS.md)
