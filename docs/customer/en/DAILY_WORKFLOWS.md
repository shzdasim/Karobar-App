# Daily Pharmacy Workflows (English)

Step-by-step daily routines for a Pakistani pharmacy or medical store.

---

## 1. Morning Opening (Shift Start)
1. Turn on the server PC (and counter terminals if using LAN).
2. Log in with your personal user account.
3. Go to **Cash Drawer** (`/cash-drawer`) → click **Open Shift**, enter your opening float (e.g., `Rs. 5,000`), and select the Cash account.
4. Check **Dashboard** (`/dashboard`) for **Near-Expiry Alerts** and **Low-Stock Items**.

---

## 2. Counter Retail Sale (Barcode POS)
1. Open **Barcode POS** (`/sale-invoices/pos`).
2. Scan the medicine barcode (or type the medicine/salt name, e.g., *Calpol 60ml* or *Paracetamol*).
3. Karobar-App automatically selects the earliest-expiring batch (**FEFO**).
4. Adjust quantity in **Packs** or **Units** (e.g., `2` strips of `10` tablets).
5. If a patient needs a substitute, click **Substitutes** to view medicines with the same generic formulation.
6. Enter **Cash Received** (e.g., `Rs. 500` for a `Rs. 340` bill) → verify **Change Due** (`Rs. 160`).
7. Click **Save & Print** to print the thermal receipt.

---

## 3. Receiving Distributor Stock (Purchase Invoice)
1. Open **Purchase Invoices → Create** (`/purchase-invoices/create`).
2. Select the **Supplier** (e.g., *Ferozsons Distributor*) and enter the supplier's **Invoice Number** and **Date**.
3. Add each medicine:
   - Enter **Batch Number** and **Expiry Date**.
   - Enter **Pack Quantity** (e.g., `20`), **Bonus/Free Qty** (e.g., `2`), **Purchase Price**, **Trade Discount %**, and **Sale Price / MRP**.
4. Choose **Payment Status**: *Paid* (deducts from Cash/Bank) or *Credit / Unpaid* (posts to Supplier Ledger).
5. Click **Save**. Stock and batch quantities update immediately.

---

## 4. Handling Customer & Supplier Returns
- **Customer Sale Return (`/sale-returns/create`):** Search the original Sale Invoice, select the returned item and quantity (e.g., `1` unopened bottle), choose **Cash Refund** or **Credit to Customer Ledger**, and save. Stock returns to the original batch.
- **Supplier Purchase Return (`/purchase-returns/create`):** Select the supplier and batch (e.g., near-expiry stock), enter return quantity, and save. Stock is deducted and the Supplier Ledger balance is reduced.

---

## 5. Recording Daily Expenses & Recoveries
- **Shop Expense (`/expenses`):** Click **Add Expense** (e.g., *Electricity Bill* `Rs. 12,000` or *Tea/Refreshments* `Rs. 350`), pick the **Cash** or **Bank** account, and save.
- **Customer Recovery / Supplier Payment (`/payments`):** Click **New Payment**, select Customer or Supplier, enter amount received/paid, allocate to pending invoices, and print or WhatsApp the payment voucher.

---

## 6. Nightly Closing (Shift End & Backup)
1. Go to **Cash Drawer** (`/cash-drawer`) → click **Close Shift**.
2. Count physical cash in the drawer and enter the **Counted Cash**. Review any variance.
3. Review **Sale Detail** (`/reports/sale-detail`) for today's total sales.
4. Confirm automatic backup ran in **Backups & Storage** (`/backups`), or click **Create Backup** before shutting down.
