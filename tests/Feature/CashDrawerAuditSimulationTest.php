<?php

namespace Tests\Feature;

use App\Models\Account;
use App\Models\CashDrawerSession;
use App\Models\CashDrawerTransaction;
use App\Models\Customer;
use App\Models\Employee;
use App\Models\ExpenseCategory;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\Supplier;
use App\Models\User;
use App\Services\AccountService;
use App\Services\CashDrawerService;
use App\Services\ExpenseService;
use App\Services\HrPayrollService;
use App\Services\PaymentService;
use App\Services\ReturnSettlementService;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class CashDrawerAuditSimulationTest extends TestCase
{
    protected User $user;
    protected Customer $customer;
    protected Supplier $supplier;
    protected Product $product;
    protected Account $cashAccount;
    protected Account $bankAccount;

    protected function setUp(): void
    {
        parent::setUp();

        foreach ([
            '0001_01_01_000000_create_users_table.php',
            '2025_08_10_080111_create_permission_tables.php',
            '2025_08_10_100605_create_suppliers_table.php',
            '2025_08_10_103922_create_customers_table.php',
            '2025_08_10_113445_create_categories_table.php',
            '2025_08_10_113550_create_brands_table.php',
            '2025_08_10_151532_create_products_table.php',
            '2025_08_10_152438_create_batches_table.php',
            '2025_08_20_154726_create_purchase_invoices_table.php',
            '2025_08_20_154744_create_purchase_invoice_items_table.php',
            '2025_08_24_015207_create_purchase_returns_table.php',
            '2025_08_24_015215_create_purchase_return_items_table.php',
            '2025_09_08_012037_add_total_paid_to_purchase_invoices_table.php',
            '2025_09_26_000000_add_invoice_type_to_purchase_invoices_table.php',
            '2026_04_01_000000_add_whole_sale_prices_to_products_table.php',
            '2026_04_01_000001_add_whole_sale_prices_to_purchase_invoice_items_table.php',
            '2026_04_01_000002_add_whole_sale_margin_to_purchase_invoice_items_table.php',
            '2026_04_01_000003_add_whole_sale_margin_to_products_table.php',
            '2025_08_30_171505_create_sale_invoices_table.php',
            '2025_08_30_171513_create_sale_invoice_items_table.php',
            '2025_09_01_074800_create_sale_returns_table.php',
            '2025_09_01_074814_create_sale_return_items_table.php',
            '2025_09_08_162536_add_total_receive_to_sale_invoices_table.php',
            '2025_09_25_000000_add_invoice_type_to_sale_invoices_table.php',
            '2026_04_02_000000_add_sale_type_to_sale_invoices_table.php',
            '2026_04_02_000001_add_sale_type_to_sale_invoice_items_table.php',
            '2026_04_02_000003_add_wholesale_type_to_sale_invoices_table.php',
            '2026_10_13_000006_add_sale_cost_snapshots.php',
            '2025_09_08_171931_create_customer_ledgers_table.php',
            '2025_09_08_013727_create_supplier_ledgers_table.php',
            '2025_09_21_102824_create_licenses_table.php',
            '2026_10_23_000003_create_document_histories_and_invoice_reversals.php',
            '2026_10_23_000004_create_payments_and_allocations_tables.php',
            '2026_10_23_000005_add_payment_method_to_invoices_table.php',
            '2026_10_23_000006_create_cash_drawer_tables.php',
            '2026_10_23_000008_create_financial_accounts_tables.php',
            '2026_10_23_000009_add_code_and_icon_to_accounts_table.php',
            '2026_10_23_000010_create_expenses_and_categories_tables.php',
            '2026_10_29_000002_create_hr_payroll_and_commissions_tables.php',
        ] as $file) {
            $path = database_path('migrations/' . $file);
            if (file_exists($path)) {
                (require $path)->up();
            }
        }

        Gate::before(fn () => true);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $this->user = User::forceCreate([
            'name'     => 'Main Cashier',
            'email'    => 'cashier@karobar.test',
            'password' => bcrypt('password'),
        ]);

        $this->customer = Customer::create([
            'name'  => 'Test Customer',
            'phone' => '03001234567',
        ]);

        $this->supplier = Supplier::create([
            'name'  => 'Test Supplier',
            'phone' => '03111234567',
        ]);

        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);

        $this->product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => $this->supplier->id,
            'name'                => 'Panadol 500mg',
            'product_code'        => 'PANA-01',
            'barcode'             => 'BC-001',
            'pack_size'           => 1,
            'quantity'            => 1000,
            'unit_purchase_price' => 70,
            'unit_sale_price'     => 100,
        ]);

        // Setup accounts
        $accountService = app(AccountService::class);
        $this->cashAccount = $accountService->syncCashDrawerBalance();
        $this->bankAccount = Account::create([
            'name'            => 'Meezan Bank Ltd',
            'type'            => 'bank',
            'code'            => 'meezan_bank',
            'opening_balance' => 50000,
            'current_balance' => 50000,
            'is_active'       => true,
            'is_default'      => false,
        ]);

        $this->actingAs($this->user);
    }

    public function test_simulation_split_payment_cash_drawer_sync(): void
    {
        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($this->user, 1000, 'Initial Float');

        // Total bill 1000: 400 cash, 600 bank card
        $res = $this->postJson('/api/sale-invoices', [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'total'         => 1000,
            'gross_amount'  => 1000,
            'total_receive' => 1000,
            'split_payments'=> [
                ['payment_method' => 'cash', 'amount' => 400, 'account_id' => $this->cashAccount->id],
                ['payment_method' => 'bank_transfer', 'amount' => 600, 'account_id' => $this->bankAccount->id],
            ],
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ]);
        $res->assertCreated();
        $invoiceId = $res->json('id');

        // Expected drawer balance: opening 1000 + 400 cash = 1400
        $summary = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(400, $summary['cash_sales'], 'Only cash split leg should count towards cash_sales');
        $this->assertEquals(1400, $summary['expected_balance'], 'Expected balance should be float 1000 + cash split 400');

        // Now edit invoice (e.g. change remarks)
        $updateRes = $this->putJson("/api/sale-invoices/{$invoiceId}", [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'total'         => 1000,
            'gross_amount'  => 1000,
            'total_receive' => 1000,
            'posted_number' => $res->json('posted_number'),
            'remarks'       => 'Updated remarks',
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ]);
        $updateRes->assertOk();

        $summaryAfterEdit = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(400, $summaryAfterEdit['cash_sales'], 'Editing invoice should NOT turn bank split payment into cash!');
        $this->assertEquals(1400, $summaryAfterEdit['expected_balance'], 'Expected balance should remain 1400 after editing split invoice');
    }

    public function test_simulation_credit_note_applied_on_sale_invoice(): void
    {
        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($this->user, 1000, 'Initial Float');

        // Step 1: Create a sale return with credit_note mode of 300 Rs
        $returnRes = $this->postJson('/api/sale-returns', [
            'customer_id'    => $this->customer->id,
            'date'           => now()->toDateString(),
            'posted_number'  => 'SR-00001',
            'settlement_mode'=> 'credit_note',
            'total'          => 300,
            'gross_total'    => 300,
            'items'          => [
                [
                    'product_id'           => $this->product->id,
                    'unit_return_quantity' => 3,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 300,
                ],
            ],
        ]);
        $returnRes->assertCreated();

        // Drawer balance should still be 1000 (credit note does not give cash back)
        $summary1 = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(1000, $summary1['expected_balance']);

        // Step 2: Customer buys 1000 Rs bill, uses 300 credit note, pays 700 cash
        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'         => $this->customer->id,
            'date'                => now()->toDateString(),
            'invoice_type'        => 'debit',
            'total'               => 1000,
            'gross_amount'        => 1000,
            'total_receive'       => 700,
            'cash_given'          => 700,
            'apply_credit_amount' => 300,
            'payment_method'      => 'cash',
            'items'               => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ]);
        $saleRes->assertCreated();
        $invoiceId = $saleRes->json('id');

        // Expected physical cash in drawer: 1000 (float) + 700 (cash paid) = 1700
        $summary2 = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(700, $summary2['cash_sales'], 'Only physical cash (700) should be recorded in drawer, NOT the 300 credit note!');
        $this->assertEquals(1700, $summary2['expected_balance'], 'Expected drawer balance must be 1700');

        // Step 3: Now edit the invoice
        $updateRes = $this->putJson("/api/sale-invoices/{$invoiceId}", [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'credit',
            'total'         => 1000,
            'gross_amount'  => 1000,
            'total_receive' => 700,
            'posted_number' => $saleRes->json('posted_number'),
            'remarks'       => 'Edit test',
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ]);
        $updateRes->assertOk();

        $summary3 = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(700, $summary3['cash_sales'], 'Cash sales must remain 700 after editing');
        $this->assertEquals(1700, $summary3['expected_balance'], 'Expected balance must remain 1700 after editing');
    }

    public function test_simulation_pos_overtender_and_change_return(): void
    {
        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($this->user, 1000, 'Initial Float');

        $this->product->unit_sale_price = 50;
        $this->product->save();

        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'total'         => 750,
            'gross_amount'  => 750,
            'total_receive' => 750,
            'cash_given'    => 1000,
            'payment_method'=> 'cash',
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 15, 'price' => 50, 'sub_total' => 750],
            ],
        ]);
        $saleRes->assertCreated();
        $invoiceId = $saleRes->json('id');

        $summary = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(750, $summary['cash_sales'], 'Net cash kept in drawer should be 750, not 1000');
        $this->assertEquals(1750, $summary['expected_balance'], 'Opening 1000 + 750 net cash = 1750');

        // Now edit the invoice
        $updateRes = $this->putJson("/api/sale-invoices/{$invoiceId}", [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'total'         => 750,
            'gross_amount'  => 750,
            'total_receive' => 750,
            'posted_number' => $saleRes->json('posted_number'),
            'remarks'       => 'Edit note',
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 15, 'price' => 50, 'sub_total' => 750],
            ],
        ]);
        $updateRes->assertOk();

        $summaryAfterEdit = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(750, $summaryAfterEdit['cash_sales']);
        $this->assertEquals(1750, $summaryAfterEdit['expected_balance']);
    }

    public function test_simulation_expense_cash_and_non_cash(): void
    {
        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($this->user, 1000, 'Initial Float');

        $category = ExpenseCategory::create(['name' => 'Tea & Refreshments']);

        // 1. Cash expense of 150
        $expRes = $this->postJson('/api/expenses', [
            'expense_category_id' => $category->id,
            'amount'              => 150,
            'date'                => now()->toDateString(),
            'payment_method'      => 'cash',
            'account_id'          => $this->cashAccount->id,
            'description'         => 'Tea for staff',
        ]);
        $expRes->assertCreated();
        $expenseId = $expRes->json('expense.id');

        $summary1 = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(150, $summary1['expenses']);
        $this->assertEquals(850, $summary1['expected_balance']);

        // 2. Bank expense of 500 (Paid from Meezan Bank)
        $bankExpRes = $this->postJson('/api/expenses', [
            'expense_category_id' => $category->id,
            'amount'              => 500,
            'date'                => now()->toDateString(),
            'payment_method'      => 'bank_transfer',
            'account_id'          => $this->bankAccount->id,
            'description'         => 'Online software subscription',
        ]);
        $bankExpRes->assertCreated();

        $summary2 = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(150, $summary2['expenses'], 'Bank expense should NOT affect cash drawer!');
        $this->assertEquals(850, $summary2['expected_balance'], 'Expected drawer balance must remain 850');

        // 3. Update cash expense from 150 to 200
        $updateExpRes = $this->putJson("/api/expenses/{$expenseId}", [
            'expense_category_id' => $category->id,
            'amount'              => 200,
            'date'                => now()->toDateString(),
            'payment_method'      => 'cash',
            'account_id'          => $this->cashAccount->id,
            'description'         => 'Tea & snacks for staff',
        ]);
        $updateExpRes->assertOk();

        $summary3 = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(200, $summary3['expenses']);
        $this->assertEquals(800, $summary3['expected_balance']);
    }

    public function test_simulation_bank_transfer_from_cash_drawer(): void
    {
        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($this->user, 5000, 'Morning Float');

        // Transfer 2000 cash from Cash in Hand (Drawer) -> Bank Account
        $transferRes = $this->postJson('/api/accounts/transfer', [
            'from_account_id' => $this->cashAccount->id,
            'to_account_id'   => $this->bankAccount->id,
            'amount'          => 2000,
            'notes'           => 'Midday cash deposit to bank',
        ]);
        $transferRes->assertOk();

        $summary = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(2000, $summary['manual_cash_out']);
        $this->assertEquals(3000, $summary['expected_balance'], '5000 float - 2000 bank deposit = 3000 expected in drawer');
    }

    public function test_simulation_100_percent_credit_note_paid_sale(): void
    {
        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($this->user, 1000, 'Initial Float');

        // Step 1: Sale return generating 1000 credit note
        $returnRes = $this->postJson('/api/sale-returns', [
            'customer_id'    => $this->customer->id,
            'date'           => now()->toDateString(),
            'posted_number'  => 'SR-00099',
            'settlement_mode'=> 'credit_note',
            'total'          => 1000,
            'gross_total'    => 1000,
            'items'          => [
                [
                    'product_id'           => $this->product->id,
                    'unit_return_quantity' => 10,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 1000,
                ],
            ],
        ]);
        $returnRes->assertCreated();

        // Step 2: Customer buys 1000 Rs of items and pays 100% via 1000 credit note (0 cash)
        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'         => $this->customer->id,
            'date'                => now()->toDateString(),
            'invoice_type'        => 'credit',
            'total'               => 1000,
            'gross_amount'        => 1000,
            'total_receive'       => 0,
            'apply_credit_amount' => 1000,
            'items'               => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ]);
        $saleRes->assertCreated();

        $summary = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals(0, $summary['cash_sales'], 'Zero cash was paid, so cash_sales MUST be 0');
        $this->assertEquals(1000, $summary['expected_balance'], 'Expected balance must remain exactly 1000 (opening float)');
    }

    public function test_simulation_full_e2e_daily_shift_exact_physical_cash_balance(): void
    {
        $drawerService = app(CashDrawerService::class);
        $expenseService = app(ExpenseService::class);
        $hrService = app(HrPayrollService::class);

        // 1. Open drawer with 5,000 opening float
        $physicalCash = 5000.0;
        $session = $drawerService->openSession($this->user, $physicalCash, 'Morning Shift');

        // 2. Pure Cash Sale of 1,200
        $this->product->unit_sale_price = 120;
        $this->product->save();
        $sale1 = $this->postJson('/api/sale-invoices', [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'total'         => 1200,
            'gross_amount'  => 1200,
            'total_receive' => 1200,
            'payment_method'=> 'cash',
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 120, 'sub_total' => 1200],
            ],
        ])->assertCreated();
        $physicalCash += 1200.0; // 6,200

        // 3. Split Payment Sale: 500 cash, 500 bank transfer
        $sale2 = $this->postJson('/api/sale-invoices', [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'total'         => 1000,
            'gross_amount'  => 1000,
            'total_receive' => 1000,
            'split_payments'=> [
                ['payment_method' => 'cash', 'amount' => 500, 'account_id' => $this->cashAccount->id],
                ['payment_method' => 'bank_transfer', 'amount' => 500, 'account_id' => $this->bankAccount->id],
            ],
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ])->assertCreated();
        $physicalCash += 500.0; // 6,700

        // 4. Credit Sale with partial cash: 300 cash now, 700 remaining on credit
        $sale3 = $this->postJson('/api/sale-invoices', [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'credit',
            'total'         => 1000,
            'gross_amount'  => 1000,
            'total_receive' => 300,
            'payment_method'=> 'cash',
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ])->assertCreated();
        $physicalCash += 300.0; // 7,000

        // 5. Customer sale return with store credit note: 400 credit note (0 cash leaves drawer)
        $return1 = $this->postJson('/api/sale-returns', [
            'customer_id'    => $this->customer->id,
            'date'           => now()->toDateString(),
            'posted_number'  => 'SR-E2E-01',
            'settlement_mode'=> 'credit_note',
            'total'          => 400,
            'gross_total'    => 400,
            'items'          => [
                ['product_id' => $this->product->id, 'unit_return_quantity' => 4, 'unit_sale_price' => 100, 'sub_total' => 400],
            ],
        ])->assertCreated();
        $physicalCash -= 0.0; // 7,000

        // 6. Sale using the 400 credit note + 600 cash = 1000 bill
        $sale4 = $this->postJson('/api/sale-invoices', [
            'customer_id'         => $this->customer->id,
            'date'                => now()->toDateString(),
            'invoice_type'        => 'credit',
            'total'               => 1000,
            'gross_amount'        => 1000,
            'total_receive'       => 600,
            'apply_credit_amount' => 400,
            'payment_method'      => 'cash',
            'items'               => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ])->assertCreated();
        $physicalCash += 600.0; // 7,600

        // 7. Sale return with cash refund: 300 cash given to customer
        $return2 = $this->postJson('/api/sale-returns', [
            'customer_id'    => $this->customer->id,
            'date'           => now()->toDateString(),
            'posted_number'  => 'SR-E2E-02',
            'settlement_mode'=> 'cash_refund',
            'payment_method' => 'cash',
            'account_id'     => $this->cashAccount->id,
            'total'          => 300,
            'gross_total'    => 300,
            'items'          => [
                ['product_id' => $this->product->id, 'unit_return_quantity' => 3, 'unit_sale_price' => 100, 'sub_total' => 300],
            ],
        ])->assertCreated();
        $physicalCash -= 300.0; // 7,300

        // 8. Expense in cash: 250
        $category = ExpenseCategory::firstOrCreate(['name' => 'General Operations']);
        $exp1 = $this->postJson('/api/expenses', [
            'expense_category_id' => $category->id,
            'amount'              => 250,
            'date'                => now()->toDateString(),
            'payment_method'      => 'cash',
            'account_id'          => $this->cashAccount->id,
            'description'         => 'Stationery and packaging',
        ])->assertCreated();
        $physicalCash -= 250.0; // 7,000

        // 9. Expense via bank: 1,500 (Does NOT affect drawer)
        $this->postJson('/api/expenses', [
            'expense_category_id' => $category->id,
            'amount'              => 1500,
            'date'                => now()->toDateString(),
            'payment_method'      => 'bank_transfer',
            'account_id'          => $this->bankAccount->id,
            'description'         => 'Internet and electricity',
        ])->assertCreated();
        $physicalCash -= 0.0; // 7,000

        // 10. Staff Cash Advance of 800
        $emp = Employee::create([
            'employee_code'  => 'EMP-001',
            'name'           => 'Ali Cashier',
            'monthly_salary' => 30000,
            'advance_balance'=> 0,
            'is_active'      => true,
        ]);
        $hrService->recordAdvanceTransaction([
            'employee_id'    => $emp->id,
            'type'           => 'advance',
            'amount'         => 800,
            'payment_method' => 'cash',
            'account_id'     => $this->cashAccount->id,
        ], $this->user);
        $physicalCash -= 800.0; // 6,200

        // 11. Staff Advance Repayment of 800 in cash
        $hrService->recordAdvanceTransaction([
            'employee_id'    => $emp->id,
            'type'           => 'repayment',
            'amount'         => 800,
            'payment_method' => 'cash',
            'account_id'     => $this->cashAccount->id,
        ], $this->user);
        $physicalCash += 800.0; // 7,000

        // 12. Supplier Purchase in cash: 1,500
        $purch = $this->postJson('/api/purchase-invoices', [
            'supplier_id'         => $this->supplier->id,
            'invoice_number'      => 'PINV-E2E-01',
            'invoice_type'        => 'debit',
            'payment_method'      => 'cash',
            'account_id'          => $this->cashAccount->id,
            'posted_date'         => now()->toDateString(),
            'invoice_amount'      => 1500,
            'total_amount'        => 1500,
            'total_paid'          => 1500,
            'items'               => [
                [
                    'product_id'          => $this->product->id,
                    'pack_size'           => 1,
                    'pack_quantity'       => 20,
                    'unit_quantity'       => 20,
                    'pack_purchase_price' => 75,
                    'unit_purchase_price' => 75,
                    'pack_sale_price'     => 100,
                    'unit_sale_price'     => 100,
                    'margin'              => 25,
                    'sub_total'           => 1500,
                    'avg_price'           => 75,
                    'quantity'            => 20,
                ],
            ],
        ])->assertCreated();
        $physicalCash -= 1500.0; // 5,500

        // 13. Purchase return with cash refund from supplier: 600
        $this->postJson('/api/purchase-returns', [
            'supplier_id'     => $this->supplier->id,
            'date'            => now()->toDateString(),
            'total'           => 600,
            'gross_total'     => 600,
            'settlement_mode' => 'cash_refund',
            'payment_method'  => 'cash',
            'account_id'      => $this->cashAccount->id,
            'items'           => [
                [
                    'product_id'           => $this->product->id,
                    'pack_size'            => 1,
                    'return_pack_quantity' => 8,
                    'return_unit_quantity' => 8,
                    'pack_purchase_price'  => 75,
                    'unit_purchase_price'  => 75,
                    'sub_total'            => 600,
                ],
            ],
        ])->assertCreated();
        $physicalCash += 600.0; // 6,100

        // 14. Midday cash transfer from drawer to Bank: 2,000
        $this->postJson('/api/accounts/transfer', [
            'from_account_id' => $this->cashAccount->id,
            'to_account_id'   => $this->bankAccount->id,
            'amount'          => 2000,
            'notes'           => 'Bank deposit',
        ])->assertOk();
        $physicalCash -= 2000.0; // 4,100

        // 15. Edit Split Sale (Sale 2): change remarks
        $this->putJson("/api/sale-invoices/{$sale2->json('id')}", [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'total'         => 1000,
            'gross_amount'  => 1000,
            'total_receive' => 1000,
            'posted_number' => $sale2->json('posted_number'),
            'remarks'       => 'Updated Split Sale remarks',
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ])->assertOk();
        // Physical cash unchanged: 4,100

        // 16. Edit Credit Note Sale (Sale 4): change remarks
        $this->putJson("/api/sale-invoices/{$sale4->json('id')}", [
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'credit',
            'total'         => 1000,
            'gross_amount'  => 1000,
            'total_receive' => 600,
            'posted_number' => $sale4->json('posted_number'),
            'remarks'       => 'Updated Credit Note sale remarks',
            'items'         => [
                ['product_id' => $this->product->id, 'quantity' => 10, 'price' => 100, 'sub_total' => 1000],
            ],
        ])->assertOk();
        // Physical cash unchanged: 4,100

        // NOW CHECK SHIFT SUMMARY & CLOSE DRAWER
        $summary = $drawerService->getSessionSummary($session->fresh());
        $this->assertEquals($physicalCash, $summary['expected_balance'], "Expected drawer balance must exactly equal physical cash counted ({$physicalCash})");

        // Close drawer with actual count equal to physical cash
        $closed = $drawerService->closeSession($session, $physicalCash, 'End of shift reconciliation');
        $this->assertEquals('closed', $closed->status);
        $this->assertEquals(0.0, (float) $closed->difference, 'Drawer close difference must be zero (perfect match)');
    }
}
