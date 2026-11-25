# Dashboard — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Store Managers, Accountants, and Shift Supervisors.  
> **Where to find it:** Automatically opens after login, or click **Dashboard** in the sidebar.

---

## A. Purpose and Overview

The **Dashboard** is your pharmacy's daily command center. Instead of opening separate reports to check today's sales, distributor bills, expired medicines, or customer credit, the Dashboard puts all key numbers on one screen.

- **What can I do here?** Monitor live sales, purchases, gross margin, top-selling medicines, near-expiry batches, low-stock warnings, customer receivables, and supplier dues.
- **How does it help daily business?** In 30 seconds each morning and evening, you can spot fast-moving items, catch medicines approaching expiry before they become dead loss, and jump straight into creating a sale or purchase order.

---

## B. What Users Can Do

- Filter store performance by quick presets (**Today**, **Yesterday**, **Last 3/7/30 Days**, **This Month**, **Last Month**, **This Year**, or **Custom Range**).
- Switch between three dedicated tabs: **Overview**, **Operations & Stock**, and **Financials & Credit**.
- Click **New Sale**, **New PO**, or **Refresh** (`Ctrl+R`) directly from the header.
- View interactive trend charts (**Sales vs Purchases**, **Gross Margin**, **Returns Trend**).
- Filter the **Near Expiry Batch Radar** by Expiry Horizon (`1 Month` to `3 Years`), **Supplier**, and **Brand**.
- Click any KPI card or shortcut to jump straight to Invoices, Products, Demands, Purchase Orders, Ledgers, Cash Drawer, or Reports.

---

## C. Screen-by-Screen Explanation

![Dashboard Overview](../../../Screenshots/Dashboard.webp)

1. **Header Strip (Top):** Shows **Live Store** indicator, active date range, last updated time, and quick buttons (**New Sale**, **New PO**, **Refresh**).
2. **Control Bar (Below Header):** Contains the 3 view tabs (**Overview**, **Operations & Stock**, **Financials & Credit**) on the left and the **Date Period Preset** + **Start/End Date Pickers** on the right.
3. **4 Hero KPI Cards (Overview Tab):**
   - **Net Sales** (Cash vs. Credit split + trend pill)
   - **Purchases (Stock Intake)** (Paid vs. Dues split + trend pill)
   - **Gross Margin & Spread** (Margin % badge, Net Sales vs. Returns)
   - **Action Center** (Clickable badges for Expiring Soon, Low Stock, Demands, and Inbound POs)
4. **Analytics & Trends Chart:** Interactive area chart with 3 metric switches (**Sales vs Purchases**, **Gross Margin**, **Returns Trend**).
5. **Bottom Dual Hub (Overview Tab):**
   - Left: **Top Performers** (switch between **Products** and **Brands**).
   - Right: **Store Operational Vitals** (Total Invoices, Stock Units on Hand, Inventory Valuation, Active Customers/Suppliers, Pending Demands).

---

## D. Every Field, Button, Tab and Option

### Header & Control Bar

| Element | Type | What It Does / What You Enter |
| :--- | :--- | :--- |
| **New Sale** | Button | Opens the **Create Sale Invoice** screen immediately. |
| **New PO** | Button | Opens **Create Purchase Order** to reorder stock from a supplier. |
| **Refresh (`Ctrl+R`)** | Button | Reloads all live KPI cards, charts, and alerts from the store database. |
| **Overview** | Tab | Shows the 4 Hero KPI cards, Trends Chart, Top Performers, and Store Operational Vitals. |
| **Operations & Stock** | Tab | Shows Stock Valuation, Units in Stock, Near Expiry Batches, Low Stock Warnings, and the **Near Expiry Batch Radar** table. Badge shows total expiry + low-stock alerts. |
| **Financials & Credit** | Tab | Shows Customer Receivables (Credit), Supplier Payables (Dues), Sale Returns, Purchase Returns, and 6 quick ledger shortcuts. |
| **Period Preset Dropdown** | Dropdown | Quick-selects *Today*, *Yesterday*, *Last 3 Days*, *Last 7 Days*, *Last 30 Days*, *This Month*, *Last Month*, *This Year*, or *Custom Range…*. |
| **Start — End Date Pickers** | Date Fields | Pick any custom start and end date (up to today). |

### Operations & Stock Tab — Near Expiry Batch Radar

| Element | Type | What It Does / What You Enter |
| :--- | :--- | :--- |
| **Expiry Horizon** | Dropdown | Choose how far ahead to check: *1 Month*, *2 Months*, *3 Months (Quarter)*, *6 Months (Half Year)*, *9 Months*, *1 Year*, *1.5 Years*, *2 Years*, *2.5 Years*, or *3 Years*. |
| **Supplier** | Searchable Dropdown | Filter near-expiry batches by a specific distributor (or *All Suppliers*). |
| **Brand** | Searchable Dropdown | Filter near-expiry batches by manufacturer/brand (or *All Brands*). |
| **Clear Filters** | Button | Resets Supplier and Brand filters back to all. |
| **Filter Batches** | Button | Refreshes the Near Expiry table using the selected Horizon, Supplier, and Brand. |
| **Batch Workbench** | Link | Opens the full **Product Batch Workbench** (`/product-batches`) to edit or manage batches. |

---

## E. Step-by-Step Instructions

### Checking Today's Store Performance
1. Open the **Dashboard**.
2. In the **Period Preset** dropdown, select **Today** (or **This Month**).
3. Review **Net Sales** (check Cash vs. Credit), **Purchases**, and **Gross Margin**.
4. Click **Net Sales** if you want to inspect the individual bills in **Sale Invoices**.

### Checking Medicines Expiring Soon
1. Click the **Operations & Stock** tab (or click the **Expiring Soon** badge inside the **Action Center** card).
2. In **Near Expiry Batch Radar**, set **Expiry Horizon** to **3 Months (Quarter)** (or **6 Months**).
3. Optionally select a **Supplier** (for example, *GSK Distributor*) and click **Filter Batches**.
4. Review the **Product**, **Batch #**, **Expiry Date**, **Status**, and **In Stock** columns to prepare a **Purchase Return**.

---

## F. Practical Business Examples & Simple Calculations

### Example 1: Understanding Gross Margin & Spread on the Dashboard
Suppose for **Today**, your pharmacy records:
- **Gross Sales** = `Rs. 52,000` (`Rs. 45,000` Cash + `Rs. 7,000` Credit)
- **Sale Returns** = `Rs. 2,000`
- **Purchases** = `Rs. 35,000`

What the Dashboard shows:
- **Net Sales** = `Rs. 52,000 - Rs. 2,000 = Rs. 50,000`
- **Gross Margin & Spread** compares your Net Sales against the cost/purchases spread for the selected period and displays the **Margin %** badge.  
*(Tip: For exact item-by-item profit after shop expenses like rent and electricity, open the **Net Profit (P&L)** report from the Reports menu.)*

---

## G. Expected Results

- Selecting a new date preset or custom date range automatically recalculates all KPI cards, sparklines, charts, and Top Performers for that exact period.
- Clicking any KPI card or shortcut opens the corresponding module without losing your place.

---

## H. Common Mistakes and Solutions

| Mistake / Confusion | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Dashboard numbers look lower than expected** | Date filter is set to **Today** instead of **This Month** (or vice versa). | Check the **Period Preset** dropdown and date range shown under the Dashboard title. |
| **Gross Margin on Dashboard differs from Net Profit Report** | Dashboard **Gross Margin & Spread** tracks sales vs. cost/spread; it does not subtract monthly shop expenses (rent, salaries, bills). | Open **Reports → Net Profit (P&L)** to see true profit after all operating expenses. |
| **Near Expiry table looks empty** | A Supplier or Brand filter is still active, or Expiry Horizon is set to `1 Month`. | Click **Clear Filters**, set **Expiry Horizon** to `3 Months (Quarter)` or `6 Months`, and click **Filter Batches**. |

---

## I. Related Features and Workflows

- [Getting Started](../../customer/en/GETTING_STARTED.md)
- [Owner & Manager Guide](../../customer/en/OWNER_GUIDE.md)
- [Daily Workflows](../../customer/en/DAILY_WORKFLOWS.md)
- [Troubleshooting](../../customer/en/TROUBLESHOOTING.md)
