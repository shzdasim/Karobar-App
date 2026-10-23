<?php

namespace Tests\Feature;

use App\Models\Account;
use App\Models\AccountTransaction;
use App\Models\Customer;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use App\Models\User;
use App\Services\AccountService;
use App\Services\CashDrawerService;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class FinancialAccountsTest extends TestCase
{
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
            '2026_10_23_000007_add_credit_terms_and_due_dates.php',
            '2026_10_23_000008_create_financial_accounts_tables.php',
            '2026_10_23_000009_add_code_and_icon_to_accounts_table.php',
        ] as $file) {
            (require database_path('migrations/' . $file))->up();
        }

        Gate::before(fn () => true);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);
    }

    public function test_default_accounts_exist_and_can_be_retrieved(): void
    {
        $user = User::forceCreate([
            'name'     => 'Accountant User',
            'email'    => 'accountant@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $res = $this->getJson('/api/accounts');
        $res->assertOk();
        $this->assertArrayHasKey('accounts', $res->json());
        $this->assertArrayHasKey('total_balance', $res->json());

        $names = collect($res->json('accounts'))->pluck('name')->all();
        $this->assertContains('Cash in Hand (Drawer)', $names);
        $this->assertContains('Main Bank Account', $names);
    }

    public function test_can_create_new_bank_account_with_opening_balance(): void
    {
        $user = User::forceCreate([
            'name'     => 'Accountant User',
            'email'    => 'accountant@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $res = $this->postJson('/api/accounts', [
            'name'            => 'Meezan Bank - Gulberg',
            'type'            => 'bank',
            'bank_name'       => 'Meezan Bank',
            'account_number'  => '010203040506',
            'opening_balance' => 75000,
            'notes'           => 'Primary business operations account',
        ]);

        $res->assertStatus(201);
        $this->assertEquals(75000, $res->json('account.current_balance'));
        $this->assertEquals('Meezan Bank', $res->json('account.bank_name'));

        // Verify transaction was logged
        $accountId = $res->json('account.id');
        $txRes = $this->getJson("/api/accounts/{$accountId}/transactions");
        $txRes->assertOk();
        $this->assertEquals(1, count($txRes->json('transactions.data')));
        $this->assertEquals('opening_balance', $txRes->json('transactions.data.0.type'));
        $this->assertEquals(75000, $txRes->json('transactions.data.0.amount'));
    }

    public function test_transfer_between_cash_drawer_and_bank_account(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier Manager',
            'email'    => 'manager@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        // Open drawer session
        app(CashDrawerService::class)->openSession($user, 10000, 'Shift opening float');

        $cashAcc = Account::where('type', 'cash')->first();
        $cashAcc->current_balance = 10000;
        $cashAcc->save();

        $bankAcc = Account::create([
            'name'            => 'HBL Business',
            'type'            => 'bank',
            'bank_name'       => 'HBL',
            'account_number'  => '9988776655',
            'opening_balance' => 5000,
            'current_balance' => 5000,
            'is_active'       => true,
        ]);

        // Transfer 4,000 from Cash Drawer to Bank
        $res = $this->postJson('/api/accounts/transfer', [
            'from_account_id' => $cashAcc->id,
            'to_account_id'   => $bankAcc->id,
            'amount'          => 4000,
            'notes'           => 'Evening cash deposit to bank',
        ]);

        $res->assertOk();
        $this->assertEquals(6000, $cashAcc->fresh()->current_balance);
        $this->assertEquals(9000, $bankAcc->fresh()->current_balance);

        // Verify active cash drawer session also updated (cash_out 4,000)
        $drawerSession = app(CashDrawerService::class)->getCurrentSession($user);
        $this->assertEquals(6000, $drawerSession->closing_balance_expected);
    }

    public function test_sale_invoice_with_bank_transfer_updates_bank_account_not_drawer(): void
    {
        $user = User::forceCreate([
            'name'     => 'POS Cashier',
            'email'    => 'pos@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        // Open cash drawer with 2,000
        app(CashDrawerService::class)->openSession($user, 2000);

        $customer = Customer::create(['name' => 'Walk-in Customer']);
        \Illuminate\Support\Facades\DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        \Illuminate\Support\Facades\DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'name'                => 'Test Medicine',
            'product_code'        => 'TM-01',
            'barcode'             => 'BC-TM-01',
            'pack_size'           => 1,
            'quantity'            => 50,
            'unit_purchase_price' => 300,
            'unit_sale_price'     => 500,
        ]);

        $bankAcc = Account::where('type', 'bank')->first();
        $initialBankBalance = (float)$bankAcc->current_balance;

        // Post sale invoice paying via Bank Transfer
        $res = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'bank_transfer',
            'account_id'     => $bankAcc->id,
            'date'           => now()->format('Y-m-d'),
            'gross_amount'   => 1500,
            'total'          => 1500,
            'total_receive'  => 1500,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 3,
                    'price'      => 500,
                    'sub_total'  => 1500,
                ],
            ],
        ]);

        $res->assertStatus(201);

        // Bank account credited by 1500
        $this->assertEquals($initialBankBalance + 1500, (float)$bankAcc->fresh()->current_balance);

        // Cash drawer unaffected (stays at 2000)
        $drawerSession = app(CashDrawerService::class)->getCurrentSession($user);
        $this->assertEquals(2000, (float)$drawerSession->closing_balance_expected);
    }

    public function test_purchase_invoice_paying_from_specific_account_withdraws_correctly(): void
    {
        $user = User::forceCreate([
            'name'     => 'Procurement User',
            'email'    => 'procure@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $supplier = Supplier::create(['name' => 'ABC Pharma']);
        \Illuminate\Support\Facades\DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        \Illuminate\Support\Facades\DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => $supplier->id,
            'name'                => 'Syrup Panadol',
            'product_code'        => 'SP-01',
            'barcode'             => 'BC-SP-01',
            'price'               => 150,
            'whole_sale_price'    => 140,
            'unit_purchase_price' => 100,
            'pack_purchase_price' => 1000,
            'pack_size'           => 10,
            'current_quantity'    => 20,
        ]);

        $walletAcc = Account::where('type', 'mobile_wallet')->first();
        $walletAcc->current_balance = 20000;
        $walletAcc->save();

        // Create purchase invoice paid via mobile wallet
        $res = $this->postJson('/api/purchase-invoices', [
            'supplier_id'    => $supplier->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'mobile_wallet',
            'account_id'     => $walletAcc->id,
            'invoice_number' => 'INV-PUR-9001',
            'invoice_amount' => 5000,
            'posted_date'    => now()->format('Y-m-d'),
            'total_amount'   => 5000,
            'total_paid'     => 5000,
            'items'          => [
                [
                    'product_id'          => $product->id,
                    'pack_quantity'       => 5,
                    'unit_quantity'       => 50,
                    'pack_purchase_price' => 1000,
                    'unit_purchase_price' => 100,
                    'pack_sale_price'     => 1500,
                    'unit_sale_price'     => 150,
                    'margin'              => 50,
                    'sub_total'           => 5000,
                    'avg_price'           => 100,
                    'quantity'            => 50,
                ],
            ],
        ]);

        $res->assertStatus(201);

        // Wallet balance decreased by 5,000 (from 20,000 to 15,000)
        $this->assertEquals(15000, (float)$walletAcc->fresh()->current_balance);
    }

    public function test_customer_payment_via_card_allocates_and_updates_card_account(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier',
            'email'    => 'cashier@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $customer = Customer::create(['name' => 'Corporate Client']);
        \Illuminate\Support\Facades\DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        \Illuminate\Support\Facades\DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'name'                => 'Bandage Pack',
            'product_code'        => 'BP-01',
            'barcode'             => 'BC-BP-01',
            'pack_size'           => 1,
            'quantity'            => 50,
            'unit_purchase_price' => 700,
            'unit_sale_price'     => 1000,
        ]);

        // Create credit sale invoice
        $invRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'credit',
            'payment_method' => 'cash',
            'date'           => now()->format('Y-m-d'),
            'gross_amount'   => 3000,
            'total'          => 3000,
            'total_receive'  => 0,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 3,
                    'price'      => 1000,
                    'sub_total'  => 3000,
                ],
            ],
        ]);
        $invRes->assertStatus(201);
        $invoiceId = $invRes->json('id');

        $cardAcc = Account::where('type', 'card')->first();
        $initialCardBalance = (float)$cardAcc->current_balance;

        // Record payment of 3000 via card
        $payRes = $this->postJson('/api/payments', [
            'party_type'     => 'customer',
            'party_id'       => $customer->id,
            'payment_date'   => now()->format('Y-m-d'),
            'amount'         => 3000,
            'payment_method' => 'card',
            'account_id'     => $cardAcc->id,
            'allocations'    => [
                ['invoice_id' => $invoiceId, 'amount' => 3000],
            ],
        ]);

        $payRes->assertStatus(201);

        // Card account increased by 3000
        $this->assertEquals($initialCardBalance + 3000, (float)$cardAcc->fresh()->current_balance);
    }

    public function test_manual_adjustment_increases_and_decreases_account_balance(): void
    {
        $user = User::forceCreate([
            'name'     => 'Owner',
            'email'    => 'owner@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $bank = Account::where('type', 'bank')->first();
        $startBal = (float)$bank->current_balance;

        // Direct deposit (e.g. Bank profit or owner deposit)
        $depRes = $this->postJson("/api/accounts/{$bank->id}/adjust", [
            'type'   => 'deposit',
            'amount' => 12500,
            'reason' => 'Owner capital injection',
        ]);
        $depRes->assertOk();
        $this->assertEquals($startBal + 12500, (float)$bank->fresh()->current_balance);

        // Direct withdrawal (e.g. Bank charges or utility payment)
        $withRes = $this->postJson("/api/accounts/{$bank->id}/adjust", [
            'type'   => 'withdrawal',
            'amount' => 500,
            'reason' => 'Monthly account maintenance fee',
        ]);
        $withRes->assertOk();
        $this->assertEquals($startBal + 12000, (float)$bank->fresh()->current_balance);
    }

    public function test_sale_invoice_reversal_deducts_money_back_from_account(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier',
            'email'    => 'cashier@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $customer = Customer::create(['name' => 'John Doe']);
        \Illuminate\Support\Facades\DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        \Illuminate\Support\Facades\DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'name'                => 'Syrup Fever',
            'product_code'        => 'SF-01',
            'barcode'             => 'BC-SF-01',
            'pack_size'           => 1,
            'quantity'            => 50,
            'unit_purchase_price' => 100,
            'unit_sale_price'     => 200,
        ]);

        $bankAcc = Account::where('type', 'bank')->first();
        $startBal = (float)$bankAcc->current_balance;

        // Create debit sale via bank transfer
        $invRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'bank_transfer',
            'account_id'     => $bankAcc->id,
            'date'           => now()->format('Y-m-d'),
            'gross_amount'   => 1000,
            'total'          => 1000,
            'total_receive'  => 1000,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 5,
                    'price'      => 200,
                    'sub_total'  => 1000,
                ],
            ],
        ]);
        $invRes->assertStatus(201);
        $invoiceId = $invRes->json('id');
        $this->assertEquals($startBal + 1000, (float)$bankAcc->fresh()->current_balance);

        // Reverse the invoice
        $revRes = $this->postJson("/api/sale-invoices/{$invoiceId}/reverse", [
            'reason' => 'Customer returned goods and requested refund',
        ]);
        $revRes->assertOk();

        // Account balance reverted back
        $this->assertEquals($startBal, (float)$bankAcc->fresh()->current_balance);
    }

    public function test_cash_in_hand_account_automatically_pulls_balance_from_active_cash_drawer(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier Drawer User',
            'email'    => 'cashierdrawer@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        // Open a cash drawer session with 7,500 float
        $openDrawerRes = $this->postJson('/api/cash-drawer/open', [
            'opening_balance' => 7500,
            'notes'           => 'Morning shift opening float',
        ]);
        $openDrawerRes->assertStatus(201);

        // Add a manual cash in of 1,200 (e.g. change float)
        $this->postJson('/api/cash-drawer/transaction', [
            'type'   => 'cash_in',
            'amount' => 1200,
            'notes'  => 'Additional change float',
        ])->assertOk();

        // Expected drawer balance is 7,500 + 1,200 = 8,700
        $drawerSummary = $this->getJson('/api/cash-drawer/current')->json('summary');
        $this->assertEquals(8700, (float)$drawerSummary['expected_balance']);

        // Check that GET /api/accounts automatically pulls this balance into Cash in Hand (Drawer)
        $accountsRes = $this->getJson('/api/accounts');
        $accountsRes->assertOk();

        $accounts = collect($accountsRes->json('accounts'));
        $cashAcc = $accounts->firstWhere('type', 'cash');
        $this->assertNotNull($cashAcc);
        $this->assertEquals(8700, (float)$cashAcc['current_balance']);

        // And total_balance reflects this automatically
        $this->assertGreaterThanOrEqual(8700, (float)$accountsRes->json('total_balance'));
    }

    public function test_pakistan_banks_and_wallets_presets_endpoint(): void
    {
        $user = User::forceCreate([
            'name'     => 'Preset User',
            'email'    => 'presets@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $res = $this->getJson('/api/accounts/presets');
        $res->assertOk();

        $banks = collect($res->json('banks'));
        $wallets = collect($res->json('wallets'));

        $this->assertTrue($banks->contains('code', 'meezan'));
        $this->assertTrue($banks->contains('code', 'hbl'));
        $this->assertTrue($banks->contains('code', 'ubl'));
        $this->assertTrue($banks->contains('code', 'alfalah'));
        $this->assertTrue($wallets->contains('code', 'jazzcash'));
        $this->assertTrue($wallets->contains('code', 'easypaisa'));
        $this->assertTrue($wallets->contains('code', 'sadapay'));
        $this->assertTrue($wallets->contains('code', 'nayapay'));
    }

    public function test_seed_default_pakistani_accounts(): void
    {
        $user = User::forceCreate([
            'name'     => 'Seed User',
            'email'    => 'seed@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $res = $this->postJson('/api/accounts/seed-defaults');
        $res->assertOk();

        $accounts = collect($res->json('summary.accounts'));
        $this->assertTrue($accounts->contains('name', 'Meezan Bank'));
        $this->assertTrue($accounts->contains('name', 'JazzCash Business'));
        $this->assertTrue($accounts->contains('name', 'Easypaisa Business'));
    }

    public function test_can_create_account_with_code_icon_and_color(): void
    {
        $user = User::forceCreate([
            'name'     => 'Create User',
            'email'    => 'createacc@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $res = $this->postJson('/api/accounts', [
            'name'            => 'Meezan Corporate Islamic',
            'type'            => 'bank',
            'code'            => 'meezan',
            'icon'            => 'meezan',
            'color'           => '#005b41',
            'bank_name'       => 'Meezan Bank Limited',
            'account_number'  => 'PK88MEZN0011223344',
            'opening_balance' => 50000,
            'notes'           => 'Corporate operational account',
        ]);

        $res->assertStatus(201);
        $acc = $res->json('account');
        $this->assertEquals('meezan', $acc['code']);
        $this->assertEquals('meezan', $acc['icon']);
        $this->assertEquals('#005b41', $acc['color']);
        $this->assertEquals(50000, (float)$acc['current_balance']);
    }
}
