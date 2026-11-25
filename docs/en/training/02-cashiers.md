# Training Lesson 2: Counter Cashiers

**Objectives:** Open/close Cash Drawer shifts, bill fast at Barcode POS, hold/resume bills, accept split payments, and process simple sale returns.

## Instructions & Examples
1. **Start Shift:** Open **Cash Drawer** (`/cash-drawer`) → **Open Shift** with `Rs. 5,000` float.
2. **Barcode POS (`/sale-invoices/pos`):** Press `F2` to scan or `F3` to search; toggle **Pack/Unit**; press `F8` to **Hold** a waiting customer's bill; press `F9` to **Complete Sale** and print thermal receipt.
3. **Returns:** Open **Sale Returns → Create** (`/sale-returns/create`), link the original Sale Invoice, and process an **Instant Refund (Cash)**.
4. **End Shift:** Count physical cash notes and **Close Shift** in `/cash-drawer`.

## Practice Exercises
1. Open a shift (`Rs. 5,000`), complete 1 cash sale (`Rs. 450`) and 1 split-tender sale (`Rs. 1,000` Cash + `Rs. 500` JazzCash), then close the shift and verify Expected Cash = `Rs. 6,450`.
2. Hold a bill with `F8`, complete a second bill, then resume and finish the held bill.
