- [x] 1) Backend DB: create `banks` migration (bank_name, account_number, image, balance)

- [x] 2) Backend DB: create `bank_ledgers` migration (bank_id, entry_date, entry_type/ref_id, direction, amount, description)

- [x] 3) Backend DB: add `bank_id` nullable to `customer_ledgers` and `supplier_ledgers`

- [x] 7) Backend Ledger Integration (bank_id linkage + BankLedger feed)

- [x] 7.1) Update `CustomerLedgerController@store/bulkUpdate` to accept `bank_id` for payment/manual rows and create `BankLedger` entries + update bank balance

- [ ] 7.2) Update `SupplierLedgerController@store/bulkUpdate` to accept `bank_id` for payment rows and create `BankLedger` entries + update bank balance

- [x] 8) Backend Rebuild logic: ensure rebuild/upsert does not break existing payment rows; keep `bank_id` on manual/payment rows

- [x] 9) Bank Ledger Endpoint: add `GET /api/bank-ledger` with filters (bank_id/from/to) returning rows + summary

- [x] 10) Invoice Payment UI/Backend integration:
- [x] 10.1) Update `SaleInvoiceController` payment processing to store selected `bank_id` in CustomerLedger payment rows

- [x] 10.2) Update `PurchaseInvoiceController` payment processing to store selected `bank_id` in SupplierLedger payment rows

- [x] 11) Frontend Banking pages:
- [x] 11.1) Add sidebar entry + routes for Banking

- [x] 11.2) Create pages: Banks list/create/edit + Bank Ledger page

- [ ] 13) Frontend Invoice payment forms:
- [ ] 13.1) Add bank dropdown for sale invoice payments (credit/debit handling)

- [ ] 13.2) Add bank dropdown for purchase invoice payments

- [ ] 14) Printing updates (optional but recommended): include bank info in printed customer/supplier ledger receipts

- [ ] 15) Migrations + smoke tests:
- [ ] 15.1) Run migrations

- [ ] 15.2) Create bank accounts, enter sale payment, validate customer ledger + bank ledger + balances

- [ ] 15.3) Enter purchase payment, validate supplier ledger + bank ledger + balances

