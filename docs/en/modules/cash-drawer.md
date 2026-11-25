# Cash Drawer & Shifts — User Manual (English)

> **Who uses this screen?** Counter Cashiers, Shift Supervisors, and Pharmacy Owners.  
> **Where to find it:** Sidebar → **Finance & Ledgers → Cash Drawer** (`/cash-drawer`).

---

## A. Purpose and Overview

The **Cash Drawer** module tracks every rupee entering and leaving your physical counter drawer during each cashier shift—from morning float, cash sales, and customer recoveries to cash refunds and petty shop expenses.

---

## B. What Users Can Do

- **Open Shift:** Start a shift with an opening cash float (e.g., `Rs. 5,000`) and selected Cash account.
- **Record Cash-In / Cash-Out:** Log mid-shift float additions or cash drops to the owner's safe.
- **Monitor Live Shift Ledger:** See every cash sale, cash purchase, cash return, payment, and expense in real time.
- **Close Shift:** Enter physical **Counted Cash** and compare it against **Expected Cash** to spot shortages or excess.
- **View & Print Shift History** (including blind count / variance reports).

---

## C. Screen-by-Screen Explanation

![Cash Drawer](../../../Screenshots/Cash%20drawer.webp)

1. **Shift Status Banner (Top):** Shows whether your drawer is **Open** or **Closed**, active cashier name, opening float, and expected cash in drawer.
2. **Action Buttons:** **Open Shift** (when closed), **Cash In / Cash Out**, and **Close Shift** (when open).
3. **Current Shift Transactions Table:** Lists every cash movement with Voucher #, Party/Category, Type (In/Out), and Amount.
4. **Past Shifts History:** Review closed shifts, opening/closing totals, and variances.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Open Shift** | Button / Modal | Select Cash account, enter **Opening Cash** float and optional note, and start shift. |
| **Cash In / Cash Out** | Button / Modal | Add mid-shift float (e.g., *"Afternoon float"*) or record cash removed (e.g., *"Dropped to safe"*). |
| **Close Shift** | Button / Modal | Enter physical **Counted Cash**, closing note, and confirm to lock the shift and check parity. |

---

## E. Step-by-Step Instructions

1. **Morning:** Open **Cash Drawer** (`/cash-drawer`) → click **Open Shift** → enter `5000` → click **Open**.
2. **During Day:** All cash sales, cash returns, and cash expenses post automatically. Use **Cash Out** if you hand `Rs. 20,000` cash to the owner mid-day.
3. **Night:** Click **Close Shift**, count all notes/coins in the drawer, type the exact **Counted Cash**, and submit.

---

## F. Practical Business Examples & Simple Calculations

### Example: End-of-Day Cash Reconciliation
- **Opening Float** = `Rs. 5,000`
- **+ Cash Sales** = `+Rs. 42,000`
- **+ Customer Cash Recoveries** = `+Rs. 8,000`
- **- Cash Sale Returns** = `-Rs. 1,500`
- **- Cash Shop Expense (Tea/Courier)** = `-Rs. 500`
- **- Mid-Day Cash Drop to Safe** = `-Rs. 20,000`
- **Expected Closing Cash** = `5,000 + 42,000 + 8,000 - 1,500 - 500 - 20,000 = Rs. 33,000`
- If physical **Counted Cash** = `Rs. 33,000`, **Variance = Rs. 0** (Perfect match!).

---

## G. Expected Results

- Opening and closing shifts keep your counter cash in exact parity with your **Cash Financial Account**.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Cashier cannot open a second shift on same PC** | Another shift is still open on the same Cash account/PC. | Close the existing open shift before opening a new one. |
| **Shortage shown at closing** | A cash expense or supplier cash payment was not entered in the system. | Check today's drawer transactions and record any missed expense or payment. |

---

## I. Related Features and Workflows

- [Financial Accounts](./financial-accounts.md)
- [Operating Expenses](./expenses.md)
- [Barcode POS](./barcode-pos.md)
