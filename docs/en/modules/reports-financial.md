# Financial & Profit Reports — User Manual (English)

> **Who uses this screen?** Pharmacy Owners, Accountants, and Store Managers.  
> **Where to find it:** Sidebar → **Reports → Financial & Tax** (`/reports/net-profit`, `/reports/cost-of-sale`, `/reports/cost-of-sale-detail`, `/reports/outstanding-aging`, `/reports/tax-return`).

---

## A. Purpose and Overview

The **Financial & Profit Reports** show whether your pharmacy is truly making money after subtracting medicine purchase costs, returns, discounts, and shop operating expenses—plus who owes you overdue money (**Outstanding Aging**) and tax summaries (**Statutory Tax Return**).

---

## B. What Users Can Do

- **Net Profit (P&L) (`/reports/net-profit`):** View Net Sales, Cost of Goods Sold (COGS), Gross Profit, Operating Expenses, and **Net Profit**, then **Export CSV**, **Export PDF**, or **Print P&L**.
- **Cost of Sale & Cost of Sale Detail (`/reports/cost-of-sale`, `/reports/cost-of-sale-detail`):** Inspect exact medicine-level purchase cost vs. sale price and margin per invoice.
- **Outstanding Aging (`/reports/outstanding-aging`):** See overdue customer/supplier balances grouped by age (`0–30`, `31–60`, `61–90`, `90+` days).
- **Statutory Tax Return (`/reports/tax-return`):** Review GST/FBR tax collected vs. paid *(when `fbr` license module is active)*.

---

## C. Screen-by-Screen Explanation

### 1. Net Profit (P&L) (`/reports/net-profit`)

![Profit and Loss Report](../../../Screenshots/Profit-Loss-Report.webp)

### 2. Cost of Sale Summary & Detail

![Cost of Sale Report](../../../Screenshots/Cost%20of%20Sale%20Report.webp)
![Cost of Sale Detail Report](../../../Screenshots/Cost-Of-Sale%20Report%20Detail.webp)

1. **Top Bar:** Date range pickers, quick period buttons, **Export CSV**, **Export PDF**, and **Print**.
2. **Printable Statement:** Clean summary cards, itemized breakdown tables, and signature lines for printing.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **From / To Date & Quick Presets** | Filters | Selects the reporting period (Today, This Month, Last Month, Custom). |
| **Export CSV** | Button | Downloads report data for Excel/spreadsheets. |
| **Export PDF** | Button | Generates a formatted PDF report. |
| **Print** | Button | Prints the report directly on A4 paper. |

---

## E. Step-by-Step Instructions

1. Go to **Reports → Net Profit (P&L)** (`/reports/net-profit`).
2. Select **From Date** and **To Date** (e.g., 1st to 30th of the month).
3. Review **Gross Profit**, **Operating Expenses**, and **Net Profit**.
4. Click **Export PDF** or **Print P&L** for your monthly owner review.

---

## F. Practical Business Examples & Simple Calculations

### Example: Monthly Net Profit (P&L)
- **Net Sales** (Sales minus Sale Returns & Discounts) = `Rs. 850,000`
- **Cost of Goods Sold (COGS)** = `Rs. 640,000`
- **Gross Profit** = `850,000 - 640,000 = Rs. 210,000` (`24.7%` Gross Margin)
- **Operating Expenses** (Rent + Electricity + Salaries) = `Rs. 110,000`
- **Net Profit** = `210,000 - 110,000 = Rs. 100,000`

---

## G. Expected Results

- Reports read live posted invoices, returns, and expenses without altering any balances.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Export PDF / CSV button is missing** | User role does not have `report.*.export` permission. | Ask the Store Admin to grant export permission in **Roles** (`/roles`). |

---

## I. Related Features and Workflows

- [Operating Expenses](./expenses.md)
- [Sales & Purchase Reports](./reports-sales-purchases.md)
- [Dashboard](./dashboard.md)
