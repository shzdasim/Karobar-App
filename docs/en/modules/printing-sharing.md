# Invoice Printing & Document Sharing — User Manual (English)

> **Who uses this screen?** Cashiers, Managers, Accountants, and Pharmacy Owners.  
> **Where to find it:** **Print / Share** buttons on any Invoice, Order, Return, or Ledger, plus **Settings → Print & Receipt Layouts** and **Document Sharing** (`/settings`).

---

## A. Purpose and Overview

Karobar-App supports both **80mm / 58mm Thermal Receipt Printers** at the counter and **A4 Invoices & Statements**, plus one-click **WhatsApp PDF** and **Email PDF** sharing so you can send bills and Khata statements directly to customers and distributors.

---

## B. What Users Can Do

- **Print Thermal Receipts (80mm / 58mm)** directly from Barcode POS, Sale Invoices, and Payment Vouchers.
- **Print A4 Invoices, Purchase Orders, and Ledgers.**
- **Customize Receipt & A4 Layouts** in **Settings → Print & Receipt Layouts** (store header, footer, logo, Urdu/English notes, paper width, auto-print toggle).
- **Share PDF via WhatsApp or Email:** Click **Share** on any document to send via the built-in local WhatsApp Agent (QR-paired once) or configured SMTP Email.

---

## C. Screen-by-Screen Explanation

![Printer Settings](../../../Screenshots/Printer-Settings.webp)

1. **Print & Receipt Layouts (`/settings` → Print & Receipt Layouts):** Select Windows thermal/A4 printer, paper size (`80mm`, `58mm`, `A4`), receipt template, and click **Test Print**.
2. **Document Sharing (`/settings` → Document Sharing):** Sub-tabs for **WhatsApp** (QR pairing + Meta Cloud), **Email (SMTP)**, and **PDF Design**.
3. **Share Modal (on any Invoice/Ledger):** Enter recipient phone/email, preview message, and click **Send**.

---

## D. Every Field, Button, Tab and Option

| Element | Location | What It Does |
| :--- | :--- | :--- |
| **Printer & Paper Size** | Printer Settings | Chooses your counter thermal printer and `80mm` vs `58mm` width. |
| **Test Print** | Printer Settings | Prints a sample receipt to verify alignment before serving customers. |
| **WhatsApp QR Pair** | Document Sharing | Connects your store WhatsApp number by scanning the QR code once. |
| **Share (`WhatsApp / Email`)** | Invoice / Ledger | Sends the document as a PDF attachment directly to the party. |

---

## E. Step-by-Step Instructions

### Setting Up Your Counter Thermal Printer
1. Plug in your 80mm/58mm thermal printer and install its Windows driver.
2. Open **Settings → Print & Receipt Layouts** (`/settings`).
3. Select the printer name, set paper width (`80mm` or `58mm`), and click **Test Print**.
4. Click **Save**.

---

## F. Practical Business Examples

### Example: Sending a Monthly Clinic Khata on WhatsApp
Open **Customer Ledger** (`/customer-ledger`), select *Al-Shifa Clinic*, click **Share**, verify their phone (`923001234567`), and click **Send via WhatsApp**—the clinic receives a clean PDF statement in seconds.

---

## G. Expected Results

- Invoices print automatically or on click in the configured layout; shared PDFs are logged in document history.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Thermal printer prints blank or cut-off lines** | Paper width is set to `80mm` on a `58mm` printer (or wrong printer chosen). | Switch paper width to `58mm` in **Settings → Print & Receipt Layouts** and run **Test Print**. |
| **WhatsApp PDF fails to send** | Session disconnected or phone lacks country code `92`. | Re-scan QR in **Settings → Document Sharing → WhatsApp** and format numbers as `923XXXXXXXXX`. |

---

## I. Related Features and Workflows

- [Barcode POS](./barcode-pos.md)
- [Sale Invoices](./sale-invoices.md)
- [Customer Ledger](./customer-ledger.md)
