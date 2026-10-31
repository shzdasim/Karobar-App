<?php

namespace Tests\Feature;

use App\Models\CashDrawerSession;
use App\Models\CashDrawerTransaction;
use App\Models\Customer;
use App\Models\Product;
use App\Models\PurchaseReturn;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\SaleReturn;
use App\Models\Supplier;
use App\Models\User;
use App\Services\CashDrawerService;
use App\Services\PaymentService;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class CashDrawerTest extends TestCase
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
        ] as $file) {
            (require database_path('migrations/' . $file))->up();
        }

        Gate::before(fn () => true);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);
    }

    public function test_can_open_cash_drawer_and_fetch_current_session(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier User',
            'email'    => 'cashier@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        // Initially no drawer open
        $res = $this->getJson('/api/cash-drawer/current');
        $res->assertOk();
        $this->assertFalse($res->json('is_open'));

        // Open cash drawer with 5000 float
        $openRes = $this->postJson('/api/cash-drawer/open', [
            'opening_balance' => 5000,
            'notes'           => 'Morning shift opening float',
        ]);
        $openRes->assertStatus(201);
        $this->assertEquals(5000, $openRes->json('session.opening_balance'));
        $this->assertEquals('open', $openRes->json('session.status'));

        // Current session returns open
        $currRes = $this->getJson('/api/cash-drawer/current');
        $currRes->assertOk();
        $this->assertTrue($currRes->json('is_open'));
        $this->assertEquals(5000, $currRes->json('session.closing_balance_expected'));
    }

    public function test_cannot_open_duplicate_drawer_session_for_same_user(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier 2',
            'email'    => 'cashier2@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $this->postJson('/api/cash-drawer/open', ['opening_balance' => 1000])->assertStatus(201);
        $this->postJson('/api/cash-drawer/open', ['opening_balance' => 2000])->assertStatus(422);
    }

    public function test_manual_cash_in_and_cash_out_updates_expected_balance(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier 3',
            'email'    => 'cashier3@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $this->postJson('/api/cash-drawer/open', ['opening_balance' => 10000])->assertStatus(201);

        // Add petty cash cash-in +1500
        $inRes = $this->postJson('/api/cash-drawer/transaction', [
            'type'   => 'cash_in',
            'amount' => 1500,
            'notes'  => 'Additional change float',
        ]);
        $inRes->assertOk();
        $this->assertEquals(11500, $inRes->json('summary.expected_balance'));

        // Cash out for office expense -500
        $outRes = $this->postJson('/api/cash-drawer/transaction', [
            'type'   => 'cash_out',
            'amount' => 500,
            'notes'  => 'Staff tea and refreshments',
        ]);
        $outRes->assertOk();
        $this->assertEquals(11000, $outRes->json('summary.expected_balance'));
    }

    public function test_cash_sale_automatically_recorded_in_active_drawer(): void
    {
        $user = User::forceCreate([
            'name'     => 'Sales Cashier',
            'email'    => 'sales@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 2000);

        $customer = Customer::create(['name' => 'Walk-in Customer']);
        DB::table('categories')->insert(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insert(['id' => 1, 'name' => 'General Brand']);
        DB::table('suppliers')->insert(['id' => 1, 'name' => 'General Supplier']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => 1,
            'name'                => 'Test Med',
            'product_code'        => 'TM-01',
            'barcode'             => 'BC-001',
            'pack_size'           => 1,
            'quantity'            => 50,
            'unit_purchase_price' => 100,
            'unit_sale_price'     => 150,
        ]);

        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'gross_amount'   => 300,
            'total'          => 300,
            'total_receive'  => 300,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 2,
                    'price'      => 150,
                    'sub_total'  => 300,
                ],
            ],
        ]);
        $saleRes->assertStatus(201);

        // Drawer expected should now be 2000 + 300 = 2300
        $drawerService->recalculateExpectedBalance($session);
        $this->assertEquals(2300, (float) $session->fresh()->closing_balance_expected);

        // Transaction should be logged
        $txn = CashDrawerTransaction::where('session_id', $session->id)->first();
        $this->assertNotNull($txn);
        $this->assertEquals('cash_sale', $txn->type);
        $this->assertEquals(300, $txn->amount);
    }

    public function test_customer_payment_via_cash_logs_drawer_transaction(): void
    {
        $user = User::forceCreate([
            'name'     => 'Billing User',
            'email'    => 'billing@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 1000);

        $customer = Customer::create(['name' => 'Credit Customer']);

        $paymentService = app(PaymentService::class);
        $payment = $paymentService->recordCustomerPayment($customer, [
            'amount'         => 800,
            'payment_method' => 'cash',
            'notes'          => 'Cash settlement on account',
        ], $user);

        $this->assertEquals(1800, (float) $session->fresh()->closing_balance_expected);
        $txn = CashDrawerTransaction::where('session_id', $session->id)
            ->where('type', 'customer_payment')
            ->first();
        $this->assertNotNull($txn);
        $this->assertEquals(800, $txn->amount);
    }

    public function test_closing_drawer_computes_over_or_short_reconciliation(): void
    {
        $user = User::forceCreate([
            'name'     => 'Closing Cashier',
            'email'    => 'closing@example.com',
            'password' => bcrypt('secret123'),
        ]);

        $this->actingAs($user);

        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 5000);

        // Add 2000 cash in
        $drawerService->recordTransaction($session, 'cash_in', 2000, null, null, 'Float', $user);
        // Expected is 7000

        // Cashier counts 6950 (short by 50)
        $closeRes = $this->postJson('/api/cash-drawer/close', [
            'closing_balance_actual' => 6950,
            'notes'                  => 'Slight shortage of 50 rupees',
        ]);
        $closeRes->assertOk();

        $closedSession = $session->fresh();
        $this->assertEquals('closed', $closedSession->status);
        $this->assertEquals(7000, (float) $closedSession->closing_balance_expected);
        $this->assertEquals(6950, (float) $closedSession->closing_balance_actual);
        $this->assertEquals(-50, (float) $closedSession->difference);
    }

    public function test_editing_cash_sale_invoice_syncs_active_cash_drawer(): void
    {
        $user = User::forceCreate([
            'name'     => 'Edit Cashier',
            'email'    => 'editcashier@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 1000);

        $customer = Customer::create(['name' => 'Cash Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        DB::table('suppliers')->insertOrIgnore(['id' => 1, 'name' => 'General Supplier']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => 1,
            'name'                => 'Syrup A',
            'product_code'        => 'SY-01',
            'barcode'             => 'BC-002',
            'pack_size'           => 1,
            'quantity'            => 100,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        // 1. Create sale invoice for 200 (2 items @ 100)
        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'gross_amount'   => 200,
            'total'          => 200,
            'total_receive'  => 200,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 2,
                    'price'      => 100,
                    'sub_total'  => 200,
                ],
            ],
        ])->assertStatus(201);

        $invId = $saleRes->json('id');
        $postedNo = $saleRes->json('posted_number');

        // Expected drawer balance: 1000 + 200 = 1200
        $this->assertEquals(1200, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(1, CashDrawerTransaction::where('session_id', $session->id)->count());

        // 2. Edit sale invoice from 200 to 500 (5 items @ 100)
        $updateRes = $this->putJson("/api/sale-invoices/{$invId}", [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'posted_number'  => $postedNo,
            'date'           => now()->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 500,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 5,
                    'price'      => 100,
                    'sub_total'  => 500,
                ],
            ],
        ])->assertStatus(200);

        // Expected drawer balance should now reflect 500 sale: 1000 + 500 = 1500
        $this->assertEquals(1500, (float) $session->fresh()->closing_balance_expected);
        $txn = CashDrawerTransaction::where('session_id', $session->id)->where('reference_id', $invId)->first();
        $this->assertEquals(500, (float) $txn->amount);
    }

    public function test_editing_preshift_sale_invoice_with_update_drawer_flag(): void
    {
        $user = User::forceCreate([
            'name'     => 'Shift Manager',
            'email'    => 'manager@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $customer = Customer::create(['name' => 'Previous Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        DB::table('suppliers')->insertOrIgnore(['id' => 1, 'name' => 'General Supplier']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => 1,
            'name'                => 'Capsule B',
            'product_code'        => 'CP-01',
            'barcode'             => 'BC-003',
            'pack_size'           => 1,
            'quantity'            => 100,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        // Invoice created yesterday (pre-shift)
        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'date'           => now()->subDays(2)->toDateString(),
            'gross_amount'   => 300,
            'total'          => 300,
            'total_receive'  => 300,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 3,
                    'price'      => 100,
                    'sub_total'  => 300,
                ],
            ],
        ])->assertStatus(201);
        $invId = $saleRes->json('id');
        $postedNo = $saleRes->json('posted_number');

        // Backdate invoice created_at to 2 days ago
        \App\Models\SaleInvoice::where('id', $invId)->update(['created_at' => now()->subDays(2)]);

        // Now open a brand new drawer session today with 5000 float
        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 5000);

        // Edit pre-shift invoice without updating drawer (user declines)
        $this->putJson("/api/sale-invoices/{$invId}", [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'posted_number'  => $postedNo,
            'date'           => now()->toDateString(),
            'gross_amount'   => 400,
            'total'          => 400,
            'total_receive'  => 400,
            'update_drawer'  => false,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 4,
                    'price'      => 100,
                    'sub_total'  => 400,
                ],
            ],
        ])->assertStatus(200);

        // Drawer session untouched: remains 5000
        $this->assertEquals(5000, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(0, CashDrawerTransaction::where('session_id', $session->id)->count());

        // Edit pre-shift invoice WITH update_drawer: true (user confirms updating drawer by +100 delta from 400 to 500)
        $this->putJson("/api/sale-invoices/{$invId}", [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'posted_number'  => $postedNo,
            'date'           => now()->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 500,
            'update_drawer'  => true,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 5,
                    'price'      => 100,
                    'sub_total'  => 500,
                ],
            ],
        ])->assertStatus(200);

        // Drawer session increases by the +100 delta: 5000 + 100 = 5100
        $this->assertEquals(5100, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(1, CashDrawerTransaction::where('session_id', $session->id)->count());
        $txn = CashDrawerTransaction::where('session_id', $session->id)->first();
        $this->assertEquals(100, (float) $txn->amount);
    }

    public function test_detailed_cash_drawer_session_report_endpoint(): void
    {
        $user = User::forceCreate([
            'name'     => 'Auditor',
            'email'    => 'auditor@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 3000, 'Morning shift audit');
        $drawerService->recordTransaction($session, 'cash_in', 500, 'manual', null, 'Initial float addition', $user);
        $drawerService->recordTransaction($session, 'cash_out', 200, 'manual', null, 'Cleaning supply expense', $user);

        $res = $this->getJson("/api/cash-drawer/session/{$session->id}");
        $res->assertOk();
        $res->assertJsonStructure([
            'session' => ['id', 'user_id', 'status', 'opening_balance', 'closing_balance_expected'],
            'summary' => ['session_id', 'opening_balance', 'manual_cash_in', 'manual_cash_out', 'expected_balance'],
            'transactions',
        ]);

        $this->assertCount(2, $res->json('transactions'));
        $this->assertEquals(3300, (float) $res->json('summary.expected_balance'));
    }

    public function test_deleting_cash_sale_invoice_in_active_session_removes_drawer_transaction_and_recalculates_balance(): void
    {
        $user = User::forceCreate([
            'name'     => 'Delete Cashier',
            'email'    => 'delcashier@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 2000);

        $customer = Customer::create(['name' => 'Delete Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        DB::table('suppliers')->insertOrIgnore(['id' => 1, 'name' => 'General Supplier']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => 1,
            'name'                => 'Item D',
            'product_code'        => 'IT-04',
            'barcode'             => 'BC-004',
            'pack_size'           => 1,
            'quantity'            => 50,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        // Create sale invoice for 400 cash
        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'gross_amount'   => 400,
            'total'          => 400,
            'total_receive'  => 400,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 4,
                    'price'      => 100,
                    'sub_total'  => 400,
                ],
            ],
        ])->assertStatus(201);
        $invId = $saleRes->json('id');

        // Drawer balance: 2000 + 400 = 2400
        $this->assertEquals(2400, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(1, CashDrawerTransaction::where('session_id', $session->id)->count());

        // Now delete the invoice
        $delRes = $this->deleteJson("/api/sale-invoices/{$invId}");
        $delRes->assertOk();

        // Transaction should be removed and expected drawer balance returned to 2000
        $this->assertEquals(2000, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(0, CashDrawerTransaction::where('session_id', $session->id)->count());
    }

    public function test_deleting_preshift_cash_sale_invoice_records_refund_in_active_drawer(): void
    {
        $user = User::forceCreate([
            'name'     => 'Refund Cashier',
            'email'    => 'refundcashier@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $customer = Customer::create(['name' => 'Old Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        DB::table('suppliers')->insertOrIgnore(['id' => 1, 'name' => 'General Supplier']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => 1,
            'name'                => 'Item E',
            'product_code'        => 'IT-05',
            'barcode'             => 'BC-005',
            'pack_size'           => 1,
            'quantity'            => 50,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        // Pre-shift sale invoice created 3 days ago
        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'date'           => now()->subDays(3)->toDateString(),
            'gross_amount'   => 300,
            'total'          => 300,
            'total_receive'  => 300,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 3,
                    'price'      => 100,
                    'sub_total'  => 300,
                ],
            ],
        ])->assertStatus(201);
        $invId = $saleRes->json('id');
        \App\Models\SaleInvoice::where('id', $invId)->update(['created_at' => now()->subDays(3)]);

        // Now open active drawer session today with 5000 float
        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 5000);

        // Delete the pre-shift invoice with refund mode
        $this->deleteJson("/api/sale-invoices/{$invId}?mode=refund")->assertOk();

        // Active drawer pays out refund of 300: 5000 - 300 = 4700
        $this->assertEquals(4700, (float) $session->fresh()->closing_balance_expected);
        $txn = CashDrawerTransaction::where('session_id', $session->id)->first();
        $this->assertEquals('cash_return', $txn->type);
        $this->assertEquals(300, (float) $txn->amount);
    }

    public function test_reversing_cash_sale_invoice_records_refund_in_active_drawer(): void
    {
        $user = User::forceCreate([
            'name'     => 'Void Cashier',
            'email'    => 'voidcashier@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 4000);

        $customer = Customer::create(['name' => 'Void Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        DB::table('suppliers')->insertOrIgnore(['id' => 1, 'name' => 'General Supplier']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => 1,
            'name'                => 'Item F',
            'product_code'        => 'IT-06',
            'barcode'             => 'BC-006',
            'pack_size'           => 1,
            'quantity'            => 50,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 500,
            'items'          => [
                [
                    'product_id' => $product->id,
                    'quantity'   => 5,
                    'price'      => 100,
                    'sub_total'  => 500,
                ],
            ],
        ])->assertStatus(201);
        $invId = $saleRes->json('id');

        // Drawer balance: 4000 + 500 = 4500
        $this->assertEquals(4500, (float) $session->fresh()->closing_balance_expected);

        // Reverse the invoice
        $this->postJson("/api/sale-invoices/{$invId}/reverse", [
            'reason' => 'Customer returned defective merchandise',
        ])->assertStatus(200);

        // Reversal refund recorded: 4500 - 500 = 4000
        $this->assertEquals(4000, (float) $session->fresh()->closing_balance_expected);
        $txn = CashDrawerTransaction::where('session_id', $session->id)->where('type', 'cash_return')->first();
        $this->assertNotNull($txn);
        $this->assertEquals(500, (float) $txn->amount);
    }

    public function test_deleting_cash_purchase_invoice_in_active_session_restores_drawer_balance(): void
    {
        $user = User::forceCreate([
            'name'     => 'Purchase Cashier',
            'email'    => 'purchcashier@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $drawerService = app(CashDrawerService::class);
        $session = $drawerService->openSession($user, 10000);

        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $supplier = Supplier::create(['name' => 'Med Supplier']);

        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => $supplier->id,
            'name'                => 'Item G',
            'product_code'        => 'IT-07',
            'barcode'             => 'BC-007',
            'pack_size'           => 1,
            'quantity'            => 0,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        // Create purchase invoice for 2000 cash
        $purchRes = $this->postJson('/api/purchase-invoices', [
            'supplier_id'         => $supplier->id,
            'invoice_number'      => 'PINV-' . uniqid(),
            'invoice_type'        => 'debit',
            'payment_method'      => 'cash',
            'posted_date'         => now()->toDateString(),
            'invoice_amount'      => 2000,
            'total_amount'        => 2000,
            'total_paid'          => 2000,
            'items'               => [
                [
                    'product_id'          => $product->id,
                    'pack_size'           => 1,
                    'pack_quantity'       => 40,
                    'unit_quantity'       => 40,
                    'pack_purchase_price' => 50,
                    'unit_purchase_price' => 50,
                    'pack_sale_price'     => 100,
                    'unit_sale_price'     => 100,
                    'margin'              => 50,
                    'sub_total'           => 2000,
                    'avg_price'           => 50,
                    'quantity'            => 40,
                ],
            ],
        ])->assertStatus(201);
        $purchId = $purchRes->json('id');

        // Drawer balance: 10000 - 2000 = 8000
        $this->assertEquals(8000, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(1, CashDrawerTransaction::where('session_id', $session->id)->count());

        // Delete the purchase invoice
        $this->deleteJson("/api/purchase-invoices/{$purchId}")->assertOk();

        // Drawer balance is restored to 10000 and transaction removed
        $this->assertEquals(10000, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(0, CashDrawerTransaction::where('session_id', $session->id)->count());
    }

    public function test_sale_return_creation_records_cash_return_in_active_drawer(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier Returns',
            'email'    => 'returns@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $session = app(CashDrawerService::class)->openSession($user, 10000);
        $customer = Customer::create(['name' => 'Cash Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'     => 1,
            'brand_id'        => 1,
            'name'            => 'Item Ret',
            'product_code'    => 'IT-RET-01',
            'barcode'         => 'BC-RET-01',
            'pack_size'       => 1,
            'quantity'        => 50,
            'unit_sale_price' => 100,
        ]);

        $res = $this->postJson('/api/sale-returns', [
            'customer_id' => $customer->id,
            'date'        => now()->toDateString(),
            'total'       => 1500,
            'gross_total' => 1500,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 15,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 1500,
                ],
            ],
        ])->assertStatus(201);

        $retId = $res->json('id');
        $this->assertNotNull($retId);

        // Drawer balance: 10000 - 1500 = 8500
        $this->assertEquals(8500, (float) $session->fresh()->closing_balance_expected);
        $tx = CashDrawerTransaction::where('session_id', $session->id)
            ->where('reference_type', 'sale_return')
            ->where('reference_id', $retId)
            ->first();
        $this->assertNotNull($tx);
        $this->assertEquals('cash_return', $tx->type);
        $this->assertEquals(1500, (float) $tx->amount);
    }

    public function test_editing_sale_return_in_active_session_updates_drawer_transaction(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier Edit Ret',
            'email'    => 'editret@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $session = app(CashDrawerService::class)->openSession($user, 10000);
        $customer = Customer::create(['name' => 'Cash Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'     => 1,
            'brand_id'        => 1,
            'name'            => 'Item Ret 2',
            'product_code'    => 'IT-RET-02',
            'barcode'         => 'BC-RET-02',
            'pack_size'       => 1,
            'quantity'        => 50,
            'unit_sale_price' => 100,
        ]);

        $createRes = $this->postJson('/api/sale-returns', [
            'customer_id' => $customer->id,
            'date'        => now()->toDateString(),
            'total'       => 1000,
            'gross_total' => 1000,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 10,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 1000,
                ],
            ],
        ])->assertStatus(201);
        $retId = $createRes->json('id');
        $postedNumber = $createRes->json('posted_number');

        $this->assertEquals(9000, (float) $session->fresh()->closing_balance_expected);

        // Edit sale return to 2000 total
        $this->putJson("/api/sale-returns/{$retId}", [
            'customer_id'   => $customer->id,
            'posted_number' => $postedNumber,
            'date'          => now()->toDateString(),
            'total'         => 2000,
            'gross_total'   => 2000,
            'items'         => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 20,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 2000,
                ],
            ],
        ])->assertOk();

        // Drawer balance: 10000 - 2000 = 8000
        $this->assertEquals(8000, (float) $session->fresh()->closing_balance_expected);
        $tx = CashDrawerTransaction::where('session_id', $session->id)
            ->where('reference_type', 'sale_return')
            ->where('reference_id', $retId)
            ->first();
        $this->assertEquals(2000, (float) $tx->amount);
    }

    public function test_editing_preshift_sale_return_with_drawer_update_flag(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier PreShift Ret',
            'email'    => 'preshiftret@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $customer = Customer::create(['name' => 'Cash Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'     => 1,
            'brand_id'        => 1,
            'name'            => 'Item Ret 3',
            'product_code'    => 'IT-RET-03',
            'barcode'         => 'BC-RET-03',
            'pack_size'       => 1,
            'quantity'        => 50,
            'unit_sale_price' => 100,
        ]);

        // Create return yesterday (before active shift)
        $createRes = $this->postJson('/api/sale-returns', [
            'customer_id' => $customer->id,
            'date'        => now()->subDay()->toDateString(),
            'total'       => 500,
            'gross_total' => 500,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 5,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 500,
                ],
            ],
        ])->assertStatus(201);
        $retId = $createRes->json('id');
        $postedNumber = $createRes->json('posted_number');
        DB::table('sale_returns')->where('id', $retId)->update(['created_at' => now()->subDay()]);

        // Open active session today
        $session = app(CashDrawerService::class)->openSession($user, 5000);
        $this->assertEquals(5000, (float) $session->closing_balance_expected);

        // Edit with update_drawer_if_pre_shift: true (total goes from 500 to 800: extra 300 refunded)
        $this->putJson("/api/sale-returns/{$retId}", [
            'customer_id'                 => $customer->id,
            'posted_number'               => $postedNumber,
            'date'                        => now()->toDateString(),
            'total'                       => 800,
            'gross_total'                 => 800,
            'update_drawer_if_pre_shift'  => true,
            'items'                       => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 8,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 800,
                ],
            ],
        ])->assertOk();

        // Delta 300 deducted from active drawer: 5000 - 300 = 4700
        $this->assertEquals(4700, (float) $session->fresh()->closing_balance_expected);
        $deltaTx = CashDrawerTransaction::where('session_id', $session->id)->first();
        $this->assertEquals('cash_return', $deltaTx->type);
        $this->assertEquals(300, (float) $deltaTx->amount);
    }

    public function test_deleting_active_session_sale_return_restores_drawer_balance(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier Del Ret',
            'email'    => 'delret@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $session = app(CashDrawerService::class)->openSession($user, 10000);
        $customer = Customer::create(['name' => 'Cash Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'     => 1,
            'brand_id'        => 1,
            'name'            => 'Item Ret 4',
            'product_code'    => 'IT-RET-04',
            'barcode'         => 'BC-RET-04',
            'pack_size'       => 1,
            'quantity'        => 50,
            'unit_sale_price' => 100,
        ]);

        $res = $this->postJson('/api/sale-returns', [
            'customer_id' => $customer->id,
            'date'        => now()->toDateString(),
            'total'       => 1200,
            'gross_total' => 1200,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 12,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 1200,
                ],
            ],
        ])->assertStatus(201);
        $retId = $res->json('id');

        $this->assertEquals(8800, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(1, CashDrawerTransaction::where('session_id', $session->id)->count());

        // Delete active sale return
        $this->deleteJson("/api/sale-returns/{$retId}")->assertOk();

        // Restores cash back to drawer: 10000
        $this->assertEquals(10000, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(0, CashDrawerTransaction::where('session_id', $session->id)->count());
    }

    public function test_deleting_preshift_sale_return_records_cash_in_recovery(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier PreShift Del Ret',
            'email'    => 'preshiftdelret@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $customer = Customer::create(['name' => 'Cash Customer']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'     => 1,
            'brand_id'        => 1,
            'name'            => 'Item Ret 5',
            'product_code'    => 'IT-RET-05',
            'barcode'         => 'BC-RET-05',
            'pack_size'       => 1,
            'quantity'        => 50,
            'unit_sale_price' => 100,
        ]);

        $createRes = $this->postJson('/api/sale-returns', [
            'customer_id' => $customer->id,
            'date'        => now()->subDay()->toDateString(),
            'total'       => 1000,
            'gross_total' => 1000,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 10,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 1000,
                ],
            ],
        ])->assertStatus(201);
        $retId = $createRes->json('id');
        DB::table('sale_returns')->where('id', $retId)->update(['created_at' => now()->subDay()]);

        // Open active session today with 5000
        $session = app(CashDrawerService::class)->openSession($user, 5000);

        // Delete pre-shift sale return
        $this->deleteJson("/api/sale-returns/{$retId}")->assertOk();

        // Active drawer records cash_in for 1000: expected balance becomes 6000
        $this->assertEquals(6000, (float) $session->fresh()->closing_balance_expected);
        $tx = CashDrawerTransaction::where('session_id', $session->id)->first();
        $this->assertEquals('cash_in', $tx->type);
        $this->assertEquals(1000, (float) $tx->amount);
    }

    public function test_purchase_return_creation_records_purchase_return_cash_in_drawer(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier PR',
            'email'    => 'purchaseret@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $session = app(CashDrawerService::class)->openSession($user, 10000);
        $supplier = Supplier::create(['name' => 'Pharma Supplier']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => $supplier->id,
            'name'                => 'Item PR 1',
            'product_code'        => 'IT-PR-01',
            'barcode'             => 'BC-PR-01',
            'pack_size'           => 1,
            'quantity'            => 100,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        $res = $this->postJson('/api/purchase-returns', [
            'supplier_id' => $supplier->id,
            'date'        => now()->toDateString(),
            'total'       => 2500,
            'gross_total' => 2500,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'pack_size'            => 1,
                    'return_pack_quantity' => 50,
                    'return_unit_quantity' => 50,
                    'pack_purchase_price'  => 50,
                    'unit_purchase_price'  => 50,
                    'sub_total'            => 2500,
                ],
            ],
        ])->assertStatus(201);
        $prId = $res->json('id');
        $this->assertNotNull($prId);

        // Drawer balance: 10000 + 2500 = 12500
        $this->assertEquals(12500, (float) $session->fresh()->closing_balance_expected);
        $tx = CashDrawerTransaction::where('session_id', $session->id)
            ->where('reference_type', 'purchase_return')
            ->where('reference_id', $prId)
            ->first();
        $this->assertNotNull($tx);
        $this->assertEquals('purchase_return', $tx->type);
        $this->assertEquals(2500, (float) $tx->amount);
    }

    public function test_editing_purchase_return_in_active_session_updates_drawer_transaction(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier Edit PR',
            'email'    => 'editpr@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $session = app(CashDrawerService::class)->openSession($user, 10000);
        $supplier = Supplier::create(['name' => 'Pharma Supplier 2']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => $supplier->id,
            'name'                => 'Item PR 2',
            'product_code'        => 'IT-PR-02',
            'barcode'             => 'BC-PR-02',
            'pack_size'           => 1,
            'quantity'            => 100,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        $res = $this->postJson('/api/purchase-returns', [
            'supplier_id' => $supplier->id,
            'date'        => now()->toDateString(),
            'total'       => 2000,
            'gross_total' => 2000,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'pack_size'            => 1,
                    'return_pack_quantity' => 40,
                    'return_unit_quantity' => 40,
                    'pack_purchase_price'  => 50,
                    'unit_purchase_price'  => 50,
                    'sub_total'            => 2000,
                ],
            ],
        ])->assertStatus(201);
        $prId = $res->json('id');
        $postedNumber = $res->json('posted_number');

        $this->assertEquals(12000, (float) $session->fresh()->closing_balance_expected);

        // Edit purchase return from 2000 to 3000
        $this->putJson("/api/purchase-returns/{$prId}", [
            'supplier_id'   => $supplier->id,
            'posted_number' => $postedNumber,
            'date'          => now()->toDateString(),
            'total'         => 3000,
            'gross_total'   => 3000,
            'items'         => [
                [
                    'product_id'           => $product->id,
                    'pack_size'            => 1,
                    'return_pack_quantity' => 60,
                    'return_unit_quantity' => 60,
                    'pack_purchase_price'  => 50,
                    'unit_purchase_price'  => 50,
                    'sub_total'            => 3000,
                ],
            ],
        ])->assertOk();

        // Drawer balance: 10000 + 3000 = 13000
        $this->assertEquals(13000, (float) $session->fresh()->closing_balance_expected);
        $tx = CashDrawerTransaction::where('session_id', $session->id)
            ->where('reference_type', 'purchase_return')
            ->where('reference_id', $prId)
            ->first();
        $this->assertEquals(3000, (float) $tx->amount);
    }

    public function test_deleting_active_session_purchase_return_deducts_drawer_balance(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier Del PR',
            'email'    => 'delpr@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $session = app(CashDrawerService::class)->openSession($user, 10000);
        $supplier = Supplier::create(['name' => 'Pharma Supplier 3']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => $supplier->id,
            'name'                => 'Item PR 3',
            'product_code'        => 'IT-PR-03',
            'barcode'             => 'BC-PR-03',
            'pack_size'           => 1,
            'quantity'            => 100,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        $res = $this->postJson('/api/purchase-returns', [
            'supplier_id' => $supplier->id,
            'date'        => now()->toDateString(),
            'total'       => 2000,
            'gross_total' => 2000,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'pack_size'            => 1,
                    'return_pack_quantity' => 40,
                    'return_unit_quantity' => 40,
                    'pack_purchase_price'  => 50,
                    'unit_purchase_price'  => 50,
                    'sub_total'            => 2000,
                ],
            ],
        ])->assertStatus(201);
        $prId = $res->json('id');

        $this->assertEquals(12000, (float) $session->fresh()->closing_balance_expected);

        // Delete the purchase return
        $this->deleteJson("/api/purchase-returns/{$prId}")->assertNoContent();

        // Drawer balance restored to 10000 and transaction removed
        $this->assertEquals(10000, (float) $session->fresh()->closing_balance_expected);
        $this->assertEquals(0, CashDrawerTransaction::where('session_id', $session->id)->count());
    }

    public function test_deleting_preshift_purchase_return_records_cash_out_to_supplier(): void
    {
        $user = User::forceCreate([
            'name'     => 'Cashier PreShift Del PR',
            'email'    => 'preshiftdelpr@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $supplier = Supplier::create(['name' => 'Pharma Supplier 4']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => $supplier->id,
            'name'                => 'Item PR 4',
            'product_code'        => 'IT-PR-04',
            'barcode'             => 'BC-PR-04',
            'pack_size'           => 1,
            'quantity'            => 100,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);

        $res = $this->postJson('/api/purchase-returns', [
            'supplier_id' => $supplier->id,
            'date'        => now()->subDay()->toDateString(),
            'total'       => 1500,
            'gross_total' => 1500,
            'items'       => [
                [
                    'product_id'           => $product->id,
                    'pack_size'            => 1,
                    'return_pack_quantity' => 30,
                    'return_unit_quantity' => 30,
                    'pack_purchase_price'  => 50,
                    'unit_purchase_price'  => 50,
                    'sub_total'            => 1500,
                ],
            ],
        ])->assertStatus(201);
        $prId = $res->json('id');
        DB::table('purchase_returns')->where('id', $prId)->update(['created_at' => now()->subDay()]);

        // Open active drawer today with 8000 float
        $session = app(CashDrawerService::class)->openSession($user, 8000);

        // Delete pre-shift purchase return
        $this->deleteJson("/api/purchase-returns/{$prId}")->assertNoContent();

        // Active drawer records cash_out for 1500: expected balance becomes 6500
        $this->assertEquals(6500, (float) $session->fresh()->closing_balance_expected);
        $tx = CashDrawerTransaction::where('session_id', $session->id)->first();
        $this->assertEquals('cash_out', $tx->type);
        $this->assertEquals(1500, (float) $tx->amount);
    }

    public function test_sale_invoice_search_filters_by_customer_id(): void
    {
        $user = User::forceCreate([
            'name'     => 'Search Cashier',
            'email'    => 'searchcashier@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $custA = Customer::create(['name' => 'Customer Alpha']);
        $custB = Customer::create(['name' => 'Customer Beta']);

        $invA = SaleInvoice::forceCreate([
            'user_id'        => $user->id,
            'customer_id'    => $custA->id,
            'posted_number'  => 'SI-ALPHA-01',
            'date'           => now()->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 500,
            'status'         => 'posted',
            'invoice_type'   => 'credit',
            'payment_method' => 'cash',
        ]);

        $invB = SaleInvoice::forceCreate([
            'user_id'        => $user->id,
            'customer_id'    => $custB->id,
            'posted_number'  => 'SI-BETA-01',
            'date'           => now()->toDateString(),
            'gross_amount'   => 800,
            'total'          => 800,
            'total_receive'  => 800,
            'status'         => 'posted',
            'invoice_type'   => 'credit',
            'payment_method' => 'cash',
        ]);

        $resA = $this->getJson("/api/sale-invoices/search?customer_id={$custA->id}");
        $resA->assertOk();
        $idsA = collect($resA->json('data'))->pluck('id')->all();
        $this->assertContains($invA->id, $idsA);
        $this->assertNotContains($invB->id, $idsA);

        $resB = $this->getJson("/api/sale-invoices/search?customer_id={$custB->id}");
        $resB->assertOk();
        $idsB = collect($resB->json('data'))->pluck('id')->all();
        $this->assertContains($invB->id, $idsB);
        $this->assertNotContains($invA->id, $idsB);
    }

    public function test_linked_sale_return_can_be_edited_and_updated_successfully(): void
    {
        $user = User::forceCreate([
            'name'     => 'Return Edit Cashier',
            'email'    => 'reteditcashier@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $customer = Customer::create(['name' => 'Pharma Client']);
        DB::table('categories')->insertOrIgnore(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insertOrIgnore(['id' => 1, 'name' => 'General Brand']);
        $product = Product::create([
            'category_id'     => 1,
            'brand_id'        => 1,
            'name'            => 'Item Linked Ret',
            'product_code'    => 'IT-LNK-01',
            'barcode'         => 'BC-LNK-01',
            'pack_size'       => 1,
            'quantity'        => 50,
            'unit_sale_price' => 100,
        ]);

        $invoice = SaleInvoice::forceCreate([
            'user_id'        => $user->id,
            'customer_id'    => $customer->id,
            'posted_number'  => 'SI-PHARMA-01',
            'date'           => now()->toDateString(),
            'gross_amount'   => 1000,
            'total'          => 1000,
            'total_receive'  => 1000,
            'status'         => 'posted',
            'invoice_type'   => 'credit',
            'payment_method' => 'cash',
        ]);

        SaleInvoiceItem::forceCreate([
            'sale_invoice_id'    => $invoice->id,
            'product_id'         => $product->id,
            'pack_size'          => 1,
            'quantity'           => 10,
            'price'              => 100,
            'sub_total'          => 1000,
        ]);

        // Create return linked to invoice
        $createRes = $this->postJson('/api/sale-returns', [
            'customer_id'     => $customer->id,
            'sale_invoice_id' => $invoice->id,
            'date'            => now()->toDateString(),
            'total'           => 300,
            'gross_total'     => 300,
            'items'           => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 3,
                    'unit_sale_quantity'   => 10,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 300,
                ],
            ],
        ])->assertStatus(201);

        $retId = $createRes->json('id');
        $postedNumber = $createRes->json('posted_number');

        // Edit the linked return to 4 units (still within allowed 10)
        $updateRes = $this->putJson("/api/sale-returns/{$retId}", [
            'customer_id'     => $customer->id,
            'sale_invoice_id' => $invoice->id,
            'posted_number'   => $postedNumber,
            'date'            => now()->toDateString(),
            'total'           => 400,
            'gross_total'     => 400,
            'items'           => [
                [
                    'product_id'           => $product->id,
                    'unit_return_quantity' => 4,
                    'unit_sale_quantity'   => 10,
                    'unit_sale_price'      => 100,
                    'sub_total'            => 400,
                ],
            ],
        ]);

        $updateRes->assertOk();
        $this->assertEquals(400, (float) SaleReturn::find($retId)->total);
    }

    public function test_current_endpoint_returns_enriched_transactions_and_last_closed_session_when_closed(): void
    {
        $user = User::forceCreate([
            'name'     => 'Enrich Cashier',
            'email'    => 'enrichcashier@example.com',
            'password' => bcrypt('secret123'),
        ]);
        $this->actingAs($user);

        $session = app(CashDrawerService::class)->openSession($user, 5000);
        $customer = Customer::create(['name' => 'Zain Pharmacy']);

        $invoice = SaleInvoice::forceCreate([
            'user_id'        => $user->id,
            'customer_id'    => $customer->id,
            'posted_number'  => 'SI-00999',
            'date'           => now()->toDateString(),
            'gross_amount'   => 1200,
            'total'          => 1200,
            'total_receive'  => 1200,
            'status'         => 'posted',
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
        ]);

        app(CashDrawerService::class)->recordTransaction(
            $session,
            'cash_sale',
            1200,
            SaleInvoice::class,
            $invoice->id,
            'POS Sale SI-00999'
        );

        // 1. While open, current() returns enriched transactions with reference_label & party_name
        $openRes = $this->getJson('/api/cash-drawer/current')->assertOk();
        $this->assertTrue($openRes->json('is_open'));
        $txns = $openRes->json('session.transactions');
        $this->assertNotEmpty($txns);
        $this->assertEquals('SI-00999', $txns[0]['reference_label']);
        $this->assertEquals('Zain Pharmacy', $txns[0]['party_name']);
        $this->assertEquals('Cash Sale Invoice', $txns[0]['category_label']);

        // 2. Close the shift and verify current() returns last_closed_session & last_closed_summary
        $this->postJson('/api/cash-drawer/close', [
            'closing_balance_actual' => 6200,
            'notes'                  => 'Balanced shift',
        ])->assertOk();

        $closedRes = $this->getJson('/api/cash-drawer/current')->assertOk();
        $this->assertFalse($closedRes->json('is_open'));
        $this->assertNotNull($closedRes->json('last_closed_session'));
        $this->assertEquals(6200, (float) $closedRes->json('last_closed_session.closing_balance_actual'));
        $this->assertEquals(1200, (float) $closedRes->json('last_closed_summary.cash_sales'));
    }
}
