# Karobar-App — Cube product guide

## What the software does
Karobar-App is a retail and wholesale business-management ERP for shops, pharmacies, distributors, and general merchants. It combines billing/POS, inventory, batch and expiry tracking, customer and supplier ledgers, purchasing, cash control, expenses, reports, staff permissions, and backups.

## Navigation and modules
- Dashboard (/dashboard): daily and monthly sales, purchases, returns, profit indicators, receivables, payables, stock alerts, expiry alerts, top-selling products, and recent activity.
- Products (/products): create and manage products, product codes, barcodes, generic/formula names, categories, brands, supplier, rack location, pack size, pack/unit purchase and sale prices, reorder level, and prescription/narcotic controls.
- Barcode Studio (/barcode-labels): queue products or batches, design labels, choose thermal roll or A4 sheet layouts, and print barcodes with product, price, batch, and expiry details.
- Categories (/categories) and Brands (/brands): organize products and manufacturers.
- Suppliers (/suppliers) and Customers (/customers): maintain party records, contact information, opening balances, customer credit limits, discounts, and wholesale prices.
- Batch & Expiry (/batch-expiry): review expired, expiring-soon, and safe batches; filter by supplier or brand; initiate purchase returns or quarantine/disposal.
- Purchase Invoices (/purchase-invoices): receive stock, create batches, record batch/expiry, pack and unit quantities, bonus units, costs, discounts, tax, sale price, and cash or credit settlement.
- Sale Invoices (/sale-invoices): retail and wholesale billing, batch selection, stock checks, discounts, returns history, and cash or credit settlement.
- Barcode POS (/sale-invoices/pos): fast full-screen checkout. Scan barcodes, search products with F2, enter cash received, and complete/print with Enter or Alt+S.
- Purchase Orders (/purchase-orders): create supplier orders and track draft, sent, partially received, and completed orders. Forecasting can suggest replenishment from sales velocity and current stock.
- User Demands (/user-demands): record items customers requested that are currently unavailable; use the list when planning purchases.
- Stock Adjustments (/stock-adjustments): authorized stock corrections with reason notes and audit records.
- Stocktake Sessions (/stocktake-sessions): start a count, enter physical quantities, submit for manager review, inspect variances, then finalize to synchronize approved counts.
- Quarantine & Disposal (/stock-disposals): isolate expired, damaged, recalled, or unsaleable batches and record cause and disposal method.
- Customer Ledger (/customer-ledger): customer credit invoices, partial payments, advances, returns, running balances, and printable statements.
- Supplier Ledger (/supplier-ledger): supplier purchase invoices, purchase returns, payments, running payable balance, and statements.
- Payment Vouchers (/payments): incoming customer receipts and outgoing supplier payments, with payment method and invoice allocation.
- Financial Accounts (/accounts): cash, bank, card/POS, and mobile-wallet balances, deposits, withdrawals, and transfers.
- Cash Drawer (/cash-drawer): open shifts with a starting float, review cash-in/cash-out activity, and close shifts by comparing expected and counted cash.
- Operating Expenses (/expenses): record rent, salaries, electricity, fuel, packaging, delivery, maintenance, and other costs, paid from cash drawer or a bank/wallet account.
- Reports: Net Profit & Loss (/reports/net-profit), current stock, cost of sale, sale detail, purchase detail, returns, outstanding aging, near expiry, and low-stock/reorder reports.
- Settings (/settings): store identity, retail/wholesale mode, currency, themes, printer templates, and license settings.
- Users and Roles (/users, /roles): staff accounts and role-based screen/action permissions.
- Backups (/backups): manual and scheduled database/settings/files backups, download, and restore.

## Common walkthroughs

### Add a product
1. Open Products.
2. Choose the create/add product action.
3. Enter the product name and useful identifiers such as product code or barcode.
4. Set category/brand/supplier, pack size, and pack/unit purchase and sale prices.
5. Set reorder level and any prescription controls that apply.
6. Save the product. Add batch stock through a purchase invoice when stock arrives.

### Make a retail sale
1. Open Sale Invoices and start a retail invoice.
2. Find or scan products and enter quantities.
3. Confirm the selected batch and available stock; the system supports FEFO batch selection.
4. Select a customer if the sale is on credit or needs customer history.
5. Review discounts, amount received, and balance.
6. Save the invoice and print or share the receipt.

### Use the barcode POS
1. Open Barcode POS.
2. Scan each product barcode; scans add or increment items.
3. Use F2 to search for products that do not have a barcode.
4. Enter the cash received and confirm the change due.
5. Press Enter or Alt+S to complete the bill and trigger receipt printing.

### Receive a supplier purchase
1. Open Purchase Invoices and create a new invoice.
2. Select the supplier and enter the supplier bill details.
3. Add products with batch number, expiry, pack/unit quantities, bonus units, purchase cost, discount, tax, and sale price.
4. Choose cash/bank settlement or post the amount to the supplier ledger as credit.
5. Save the purchase invoice to add the stock.

### Record a customer payment
1. Open Customer Ledger.
2. Select the customer and review outstanding invoices and balance.
3. Record the received amount and choose the relevant payment/account details.
4. Allocate the payment to the intended invoices and save the receipt.

### Record an operating expense
1. Open Operating Expenses.
2. Select or create the expense category.
3. Enter the amount, date, and description.
4. Select whether it was paid from the active cash drawer or a bank/wallet account.
5. Save and review the updated expense record.

### Complete a stocktake
1. Open Stocktake Sessions and create a session for the store or a selected category/brand.
2. Start the session and enter physical quantities.
3. Submit the session for review.
4. Have an authorized manager review system quantities against counted quantities.
5. Finalize only after the variances are checked and approved.

### Review profit
1. Open the Net Profit (P&L) report.
2. Select the required date range.
3. Review sales, returns, cost of goods sold, gross profit, operating expenses, net profit, and margin.
4. Use the detail reports to investigate individual transactions.

## Safety and accuracy boundaries
- Cube explains workflows; it does not create, edit, delete, settle, or finalize ERP records.
- Cube cannot see live tenant data. Never invent a stock quantity, current balance, invoice status, sales total, or profit figure.
- For live figures, direct the user to the relevant report, ledger, dashboard, or module.
- Do not advise bypassing role permissions, audit trails, batch expiry controls, or stocktake approval.
- If a menu destination is not included in the allowed navigation list supplied by the server, do not offer a navigation action for it.
