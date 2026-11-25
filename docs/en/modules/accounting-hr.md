# Double-Entry Accounting (GL) & HR Payroll — User Manual (English)

> **Who uses this screen?** Accountants, HR/Payroll Managers, and Pharmacy Owners.  
> **Where to find it:** Sidebar → **Finance & Ledgers → Double-Entry & GL** (`/accounting`) and **HR, Payroll & Commissions** (`/hr-payroll`). *(Requires the `accounting_gl` and `hr_payroll` license modules respectively.)*

---

## A. Purpose and Overview

- **Double-Entry & GL (`/accounting`):** Provides full accounting statements (**Balance Sheet**, **Trial Balance**, **General Ledger**, **Cash Flow**, **Chart of Accounts & Tax**, and balanced **Journal Vouchers (JV)**) for pharmacies needing formal accounting.
- **HR, Payroll & Commissions (`/hr-payroll`):** Manages your staff directory, daily attendance, salary advances (**Kharcha**), salesman commissions, and monthly payroll runs with payslips.

---

## B. What Users Can Do

### Double-Entry & GL (`/accounting`)
- Switch between **Balance Sheet**, **Trial Balance (`Dr = Cr`)**, **General Ledger**, **Cash Flow**, and **Chart of Accounts / Tax** tabs.
- Create balanced **New Journal Vouchers (JV)** where `Total Debit = Total Credit`, or reverse a manual JV with a reason.

### HR, Payroll & Commissions (`/hr-payroll`)
- Add **Employees** (with base salary, commission %, and optional login link).
- Mark **Daily Attendance** (Present, Absent, Late, Leave).
- Issue **Salary Advances (Kharcha)** and deduct repayments automatically during payroll.
- Generate **Monthly Payroll Runs**, review **Net Payable** (`Base Salary + Commissions - Advances`), and disburse salaries from Cash/Bank.

---

## C. Screen-by-Screen Explanation

*(No standalone screenshot is currently stored for `/accounting` or `/hr-payroll` in `Screenshots/`.)*

- **`/accounting`:** Top action bar with **New Journal Voucher (JV)** and period date pickers; 5 tabs (**Balance Sheet**, **Trial Balance**, **General Ledger**, **Cash Flow**, **COA & Tax**).
- **`/hr-payroll`:** Sub-tabs for **Employees**, **Attendance**, **Advances (Kharcha)**, **Commissions**, and **Payroll Runs**.

---

## D. Every Field, Button, Tab and Option

| Element | Module | What It Does |
| :--- | :--- | :--- |
| **Balance Sheet / Trial Balance** | Accounting | Displays Assets, Liabilities, Equity, and verifies `Total Debit = Total Credit`. |
| **New Journal Voucher (JV)** | Accounting | Posts manual debit/credit adjustments (blocks saving if `Debit ≠ Credit`). |
| **Attendance & Advances** | HR & Payroll | Tracks daily presence and staff Kharcha advances paid from Cash/Bank. |
| **Run Payroll / Disburse** | HR & Payroll | Calculates monthly salary + commission minus advance deductions and records salary payout. |

---

## E. Step-by-Step Instructions

### Running Monthly Staff Payroll
1. Open **Finance & Ledgers → HR, Payroll & Commissions** (`/hr-payroll`).
2. Confirm **Attendance** and **Advances (Kharcha)** for the month are up to date.
3. Open **Payroll Runs** → click **Generate Payroll** for the target month.
4. Review each employee's **Net Payable**, select the paying **Financial Account** (*Cash* or *Bank*), and click **Disburse**.

---

## F. Practical Business Examples & Simple Calculations

### Example: Pharmacist Monthly Payslip
- **Base Salary** = `Rs. 45,000`
- **+ Sales Commission (1% on Rs. 300,000 sales)** = `+Rs. 3,000`
- **- Mid-Month Kharcha Advance** = `-Rs. 5,000`
- **Net Salary Paid** = `45,000 + 3,000 - 5,000 = Rs. 43,000`

---

## G. Expected Results

- Journal Vouchers keep `Debit = Credit` balanced across the General Ledger.
- Payroll disbursement deducts net salaries from the chosen Cash/Bank account and settles deducted advances.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Cannot post Journal Voucher** | Total Debit does not equal Total Credit. | Adjust line amounts until **Difference = 0.00**. |
| **Accounting or HR menu is hidden** | Your active license tier does not include `accounting_gl` or `hr_payroll`. | Check **Settings → License** to verify enabled modules. |

---

## I. Related Features and Workflows

- [Financial Accounts](./financial-accounts.md)
- [Operating Expenses](./expenses.md)
- [Profit & Loss Reports](./reports-financial.md)
