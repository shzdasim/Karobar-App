# Customers & Doctors — User Manual (English)

> **Who uses this screen?** Cashiers, Pharmacy Owners, Managers, and Wholesale Staff.  
> **Where to find it:** Sidebar → **Items & Masters → Customers** (`/customers`) and **Doctors** (`/doctors`).

---

## A. Purpose and Overview

The **Customers & Doctors** module manages regular pharmacy patients, wholesale clinics/hospitals, credit limits, monthly/weekly **Regular Medicine Refill Schedules** (with WhatsApp reminders), custom wholesale price lists, and prescribing **Doctors** for prescription tracking.

---

## B. What Users Can Do

- **Add, Edit, Search, and Import/Export Customers** (`/customers`).
- **Set Credit Limits & Account Status** (Active / On Hold) and inspect **Credit Summary**.
- **Manage Regular Refill Customers** (Weekly / Monthly cycles): see **Due Customers** banner, send WhatsApp refill reminders, or shift all regular medicines straight into a **Retail Sale Invoice** in one click.
- **Set Customer-Specific Wholesale Prices** for B2B buyers.
- **Manage Prescribing Doctors** (`/doctors`) to link prescriptions on Sale Invoices.

---

## C. Screen-by-Screen Explanation

![Customers Page](../../../Screenshots/Customers-Page.webp)

1. **Top Action Bar:** **Add Customer**, **Import** (CSV), **Export** (CSV), and **Send Bulk Refill Reminders**.
2. **Due Customers Banner:** Highlights regular patients whose refill date is due, with **Filter Due Customers**.
3. **Search & Filter Strip:** Search by name, phone, email, or address; filter by *Regular Customers (All / Weekly / Monthly)* or *Standard*.
4. **Customers Table:** Name, Phone, Regular Refill Schedule & Stock Readiness, **Credit Limit**, and actions (**Credit Summary**, **Wholesale Prices**, **Shift Regular Products to Sale Invoice**, **Edit**, **Delete**).

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **Credit Limit** | Number | Maximum unpaid balance allowed before POS/Sale Invoice requires a Supervisor Credit Override. |
| **Regular Customer & Cycle** | Toggle / Selector | Marks patient as *Weekly* or *Monthly* regular refill customer with their list of chronic medicines. |
| **Shift All Regular Products** | Row Button | Opens a new **Retail Sale Invoice** pre-filled with that patient's monthly medicines. |
| **Wholesale Price List** | Row Modal | Sets custom negotiated prices for a specific wholesale clinic or pharmacy buyer. |
| **Doctors (`/doctors`)** | Directory | Stores doctor name, clinic, phone, and specialty for prescription logging. |

---

## E. Step-by-Step Instructions

### Setting Up a Monthly Chronic-Care Patient
1. Go to **Customers → Add Customer** (`/customers/create`).
2. Enter **Name** (*Haji Abdul Rehman*), **Phone** (`923001234567`), and optional **Credit Limit**.
3. Save, then mark as **Regular Customer** (*Monthly*) and add their regular medicines (e.g., *Glucophage 500mg* × `2` packs, *Lipitor 20mg* × `1` pack).
4. Next month, click **Shift All Regular Products** to generate their refill bill in one click.

---

## F. Practical Business Examples

### Example: Preventing Credit Over-Exposure
If *City Clinic* has a **Credit Limit** of `Rs. 50,000` and already owes `Rs. 46,000`, trying to save a new `Rs. 8,000` credit bill (`Rs. 54,000` total) triggers a **Credit Override** prompt requiring Manager approval.

---

## G. Expected Results

- Regular customers due for refills appear on the **Due Customers** banner; wholesale prices apply automatically on Wholesale Sale Invoices.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **WhatsApp refill reminder fails** | Phone number does not include country code `92`. | Edit the customer phone to `923XXXXXXXXX` format. |

---

## I. Related Features and Workflows

- [Customer Ledger (Khata)](./customer-ledger.md)
- [Sale Invoices](./sale-invoices.md)
- [Payment Vouchers](./payments.md)
