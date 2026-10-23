<?php

namespace Tests\Feature;

use App\Models\CashDrawerSession;
use App\Models\CashDrawerTransaction;
use App\Models\Customer;
use App\Models\Product;
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
            '2025_09_08_012037_add_total_paid_to_purchase_invoices_table.php',
            '2025_09_26_000000_add_invoice_type_to_purchase_invoices_table.php',
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
}
