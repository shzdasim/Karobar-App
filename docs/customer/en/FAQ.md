# Frequently Asked Questions (FAQ) — English

### 1. Can I sell single tablets or strips when I buy in full boxes?
Yes. Set **Pack Size** on the product (e.g., `10` strips per box, or `100` tablets per box). During a Purchase Invoice you can enter quantities in **Packs**, and at Barcode POS or Retail Sale you can sell in either **Packs** or **Units**—Karobar-App converts and deducts stock automatically.

### 2. How does Karobar-App choose which batch to sell?
Karobar-App uses **FEFO (First-Expired, First-Out)**. It automatically selects the active, non-expired batch with the earliest expiry date so older stock leaves your shelf first.

### 3. Can a cashier sell an expired medicine by mistake?
No. Expired batches are blocked by default at POS and Sale Invoices. Only a user with `sale-invoice.expired-override` permission (or a Manager entering their **Supervisor Operator PIN**) can override this—and expired stock moved to **Quarantine (`/stock-disposals`)** cannot be sold at all.

### 4. Why can't I edit a product's stock number directly on the Edit Product page?
To protect your store from inventory fraud and accounting mismatches, direct stock overwriting on Product Edit is intentionally blocked. All stock changes must go through **Purchase Invoices**, **Sale Invoices**, **Returns**, **Stock Adjustments**, **Stocktake Sessions**, or **Disposals** so every unit has an audit trail.

### 5. What happens if I accidentally delete or void a Sale Invoice?
Voiding/reversing a Sale Invoice restores the sold quantities back to the batches, updates the Customer Ledger (if it was a credit sale), and adjusts the Cash Drawer / Financial Account parity cleanly. Note: You cannot delete a Sale Invoice if a **Sale Return** has already been created against it.

### 6. Can I use Karobar-App without internet?
Yes. Karobar-App runs locally on your store PC using a local SQLite database. Internet is only needed for optional cloud features such as **Email sharing**, **WhatsApp**, **FBR sync**, or **Cloud AI** (Groq/Gemini).

### 7. Can multiple billing counters connect to one database?
Yes. In a Multi-PC LAN setup, one PC acts as the **Server** and counter PCs connect as **Terminals** over your shop's local Wi-Fi or Ethernet router (`/server-dashboard`).

### 8. How do I send an invoice PDF on WhatsApp?
Open any saved Sale Invoice, Purchase Order, or Ledger statement, click **Share → WhatsApp**, and dispatch via the built-in local WhatsApp Agent (paired once via QR code in **Settings → WhatsApp**) or Meta Cloud API.
