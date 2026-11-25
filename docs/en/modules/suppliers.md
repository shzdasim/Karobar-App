# Suppliers (Distributors) — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Purchase Managers, and Accountants.  
> **Where to find it:** Sidebar → **Items & Masters → Suppliers** (`/suppliers`) or **Add Supplier** (`/suppliers/create`).

---

## A. Purpose and Overview

The **Suppliers** module stores your medicine distributors, wholesalers, and manufacturers—along with their contact details, opening payable balances, and expiry return policies—connecting directly to **Purchase Orders**, **Purchase Invoices**, **Purchase Returns**, and **Supplier Ledger**.

---

## B. What Users Can Do

- **Add, Edit, Search, and Delete** Suppliers (`/suppliers`).
- **Set Opening Balance** when migrating from a paper Khata.
- **Configure Expiry Return Policy** (how many days before expiry a distributor accepts returns).
- **Bulk Import / Export CSV** supplier directories.

---

## C. Screen-by-Screen Explanation

![Suppliers Page](../../../Screenshots/Suppliers-Page.webp)

1. **Top Action Bar:** **Add Supplier**, **Import** (CSV), and **Export** (CSV).
2. **Search Box:** Search by supplier name, phone, or address.
3. **Suppliers Table:** Name, Phone, Email/Address, Payable Balance, and Edit / Ledger / Delete actions.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Name \*** | Text (Required) | Distributor name (e.g., *Muller & Phipps Pakistan*). |
| **Phone / Email / Address** | Text | Contact details used on Purchase Orders and WhatsApp/Email sharing. |
| **Opening Balance** | Number | Any pre-existing balance you owed this supplier before starting Karobar-App. |
| **Expiry Return Policy** | Fields | Optional return window rules for near-expiry reminders. |
| **Import / Export** | Buttons | Bulk upload or download suppliers as CSV. |

---

## E. Step-by-Step Instructions

1. Go to **Suppliers → Add Supplier** (`/suppliers/create`).
2. Enter **Name** (*Ferozsons Distributor*), **Phone** (`923001234567`), and **Address**.
3. If you already owe them `Rs. 25,000` from your old register, enter `25000` in **Opening Balance**.
4. Click **Save**.

---

## F. Practical Business Examples

### Example: Migrating an Existing Distributor Khata
Entering `Rs. 25,000` as **Opening Balance** immediately shows `Rs. 25,000` payable on **Supplier Ledger** (`/supplier-ledger`) before your first new Purchase Invoice.

---

## G. Expected Results

- The supplier becomes selectable in **Purchase Orders**, **Purchase Invoices**, **Purchase Returns**, **Payments**, and **Supplier Ledger**.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Cannot delete a Supplier** | The supplier has linked Purchase Invoices or ledger transactions. | Keep the supplier record for historical audit integrity. |

---

## I. Related Features and Workflows

- [Supplier Ledger (Khata)](./supplier-ledger.md)
- [Purchase Invoices](./purchase-invoices.md)
- [Purchase Orders](./purchase-orders.md)
