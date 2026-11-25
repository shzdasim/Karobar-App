# Training Lesson 5: Accountants & Finance Staff

**Objectives:** Reconcile Customer & Supplier Ledgers, record & allocate Payment Vouchers (incl. PDC cheques), manage Financial Accounts, log Expenses, and verify Net Profit (P&L) & Trial Balance.

## Instructions & Examples
1. **Ledgers & Payments:** Check **Customer Ledger** (`/customer-ledger`) and **Supplier Ledger** (`/supplier-ledger`). Record recoveries/payments in **Payment Vouchers** (`/payments`), allocating lump-sum payments across oldest invoices and clearing/bouncing PDC cheques.
2. **Accounts & Expenses:** Record bank deposits via **Transfer Funds** in `/accounts` and enter daily/recurring expenses in `/expenses`.
3. **Profit & GL:** Run **Net Profit (P&L)** (`/reports/net-profit`), **Outstanding Aging** (`/reports/outstanding-aging`), and (if enabled) **Double-Entry & GL** (`/accounting`) to verify `Trial Balance (Dr = Cr)`.

## Practice Exercises
1. Record a `Rs. 15,000` customer payment and allocate it across 2 pending credit invoices; verify the customer's remaining balance in `/customer-ledger`.
2. Record a `Rs. 18,500` electricity bill from your Bank account and verify it reduces **Net Profit** on `/reports/net-profit`.
