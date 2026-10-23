<?php

namespace Tests\Feature;

use App\Models\Account;
use App\Models\Brand;
use App\Models\CashDrawerSession;
use App\Models\CashDrawerTransaction;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Expense;
use App\Models\ExpenseCategory;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\Supplier;
use App\Models\User;
use Carbon\Carbon;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class ExpenseAndNetProfitTest extends TestCase
{
    protected User $user;
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
            '2025_08_30_171505_create_sale_invoices_table.php',
            '2025_08_30_171513_create_sale_invoice_items_table.php',
            '2025_09_01_074800_create_sale_returns_table.php',
            '2025_09_01_074814_create_sale_return_items_table.php',
            '2025_09_08_162536_add_total_receive_to_sale_invoices_table.php',
            '2025_09_25_000000_add_invoice_type_to_sale_invoices_table.php',
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
            '2026_10_23_000010_create_expenses_and_categories_tables.php',
        ] as $file) {
            (require database_path('migrations/' . $file))->up();
        }

        Gate::before(fn () => true);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $this->user = User::forceCreate([
            'name'     => 'Store Manager',
            'email'    => 'manager@karobar.test',
            'password' => bcrypt('password'),
        ]);

        $this->bankAccount = Account::create([
            'name'            => 'Meezan Bank Operations',
            'type'            => 'bank',
            'code'            => 'meezan',
            'opening_balance' => 100000,
            'current_balance' => 100000,
            'is_active'       => true,
            'is_default'      => true,
        ]);
    }

    public function test_can_list_and_create_expense_categories()
    {
        $response = $this->actingAs($this->user)->getJson('/api/expense-categories');
        $response->assertStatus(200);
        $this->assertNotEmpty($response->json());

        $createRes = $this->actingAs($this->user)->postJson('/api/expense-categories', [
            'name'        => 'Software Subscriptions',
            'code'        => 'software',
            'description' => 'Cloud hosting, POS license, domain fees',
        ]);

        $createRes->assertStatus(201)
            ->assertJson([
                'name' => 'Software Subscriptions',
                'code' => 'software',
            ]);

        $this->assertDatabaseHas('expense_categories', ['code' => 'software']);
    }

    public function test_cash_expense_automatically_logs_in_active_cash_drawer()
    {
        // 1. Open drawer session with 10,000 float
        $session = CashDrawerSession::create([
            'user_id'                  => $this->user->id,
            'opened_at'                => now(),
            'status'                   => 'open',
            'opening_balance'          => 10000,
            'closing_balance_expected' => 10000,
        ]);

        $category = ExpenseCategory::where('code', 'utilities')->first();

        // 2. Post electricity bill expense of 2,500 paid via cash
        $res = $this->actingAs($this->user)->postJson('/api/expenses', [
            'expense_category_id' => $category->id,
            'amount'              => 2500,
            'date'                => now()->toDateString(),
            'payment_method'      => 'cash',
            'payee'               => 'K-Electric Bill',
            'description'         => 'Store electricity bill October',
        ]);

        $res->assertStatus(201);
        $expenseId = $res->json('expense.id');

        // Verify expense is linked to the drawer session
        $this->assertDatabaseHas('expenses', [
            'id'                     => $expenseId,
            'amount'                 => 2500,
            'cash_drawer_session_id' => $session->id,
            'payment_method'         => 'cash',
        ]);

        // Verify cash drawer transaction was created as cash_out
        $this->assertDatabaseHas('cash_drawer_transactions', [
            'session_id'     => $session->id,
            'type'           => 'cash_out',
            'amount'         => 2500,
            'reference_type' => Expense::class,
            'reference_id'   => $expenseId,
        ]);

        // Verify session expected closing balance is reduced from 10,000 to 7,500
        $session->refresh();
        $this->assertEquals(7500.0, (float) $session->closing_balance_expected);
    }

    public function test_bank_expense_withdraws_from_bank_account()
    {
        $category = ExpenseCategory::where('code', 'rent')->first();

        $res = $this->actingAs($this->user)->postJson('/api/expenses', [
            'expense_category_id' => $category->id,
            'account_id'          => $this->bankAccount->id,
            'amount'              => 45000,
            'date'                => now()->toDateString(),
            'payment_method'      => 'bank_transfer',
            'reference_number'    => 'CHQ-987654',
            'payee'               => 'Shop Landlord Haji Muhammad',
            'description'         => 'Monthly shop rent',
        ]);

        $res->assertStatus(201);

        // Bank balance should be 100,000 - 45,000 = 55,000
        $this->bankAccount->refresh();
        $this->assertEquals(55000.0, (float) $this->bankAccount->current_balance);

        $this->assertDatabaseHas('account_transactions', [
            'account_id'   => $this->bankAccount->id,
            'type'         => 'withdrawal',
            'amount'       => 45000,
            'balance_after'=> 55000,
        ]);
    }

    public function test_deleting_expense_reverses_financial_effects()
    {
        $category = ExpenseCategory::where('code', 'maintenance')->first();

        $res = $this->actingAs($this->user)->postJson('/api/expenses', [
            'expense_category_id' => $category->id,
            'account_id'          => $this->bankAccount->id,
            'amount'              => 10000,
            'date'                => now()->toDateString(),
            'payment_method'      => 'bank_transfer',
            'payee'               => 'AC Repair Service',
        ]);

        $res->assertStatus(201);
        $expenseId = $res->json('expense.id');
        $this->assertEquals(90000.0, (float) $this->bankAccount->fresh()->current_balance);

        // Delete the expense
        $del = $this->actingAs($this->user)->deleteJson("/api/expenses/{$expenseId}");
        $del->assertStatus(200);

        // Bank balance should be restored to 100,000
        $this->assertEquals(100000.0, (float) $this->bankAccount->fresh()->current_balance);
        $this->assertSoftDeleted('expenses', ['id' => $expenseId]);
    }

    public function test_net_profit_statement_computes_gross_and_net_income()
    {
        $category = Category::create(['name' => 'Pharma']);
        $brand = Brand::create(['name' => 'Abbott']);
        $supplier = Supplier::create(['name' => 'Abbott Distributor', 'phone' => '03001112233']);
        $customer = Customer::create(['name' => 'Walk-in Customer']);

        $product = Product::create([
            'name'                => 'Brufen 400mg',
            'product_code'        => 'BRU-400',
            'barcode'             => '1234567890123',
            'category_id'         => $category->id,
            'brand_id'            => $brand->id,
            'supplier_id'         => $supplier->id,
            'quantity'            => 100,
            'pack_size'           => 1,
            'unit_purchase_price' => 50, // Cost = 50
            'avg_price'           => 50,
            'unit_sale_price'     => 80, // Sale = 80
            'pack_purchase_price' => 50,
            'pack_sale_price'     => 80,
            'narcotic'            => 'no',
        ]);

        $today = now()->toDateString();

        // 1. Record Sale Invoice: 10 units @ 80 = Total 800 (COGS = 10 * 50 = 500, Gross Profit = 300)
        $sale = SaleInvoice::create([
            'customer_id'   => $customer->id,
            'user_id'       => $this->user->id,
            'invoice_number'=> 'INV-TEST-001',
            'posted_number' => 'SI-TEST-001',
            'date'          => $today,
            'gross_amount'  => 800,
            'total'         => 800,
            'total_receive' => 800,
            'payment_method'=> 'cash',
            'invoice_type'  => 'retail',
        ]);

        SaleInvoiceItem::create([
            'sale_invoice_id'     => $sale->id,
            'product_id'          => $product->id,
            'quantity'            => 10,
            'pack_size'           => 1,
            'price'               => 80,
            'sub_total'           => 800,
            'cost_price_snapshot' => 50,
        ]);

        // 2. Record Operating Expense: 100 (Tea & Refreshments)
        $expCat = ExpenseCategory::where('code', 'refreshments')->first();
        Expense::create([
            'expense_number'      => 'EXP-TEST-001',
            'expense_category_id' => $expCat->id,
            'user_id'             => $this->user->id,
            'amount'              => 100,
            'date'                => $today,
            'payment_method'      => 'cash',
            'payee'               => 'Chai Hotel',
            'description'         => 'Staff morning tea',
        ]);

        // 3. Request Net Profit Report
        $reportRes = $this->actingAs($this->user)->getJson("/api/reports/net-profit?from={$today}&to={$today}");

        $reportRes->assertStatus(200);
        $kpis = $reportRes->json('kpis');

        // Net Sales = 800
        $this->assertEquals(800.0, (float) $kpis['net_sales']);
        // COGS = 500
        $this->assertEquals(500.0, (float) $kpis['cost_of_goods_sold']);
        // Gross Profit = 300
        $this->assertEquals(300.0, (float) $kpis['gross_profit']);
        // Gross Margin = (300 / 800) * 100 = 37.5%
        $this->assertEquals(37.5, (float) $kpis['gross_margin_pct']);
        // Total Expenses = 100
        $this->assertEquals(100.0, (float) $kpis['total_expenses']);
        // Net Profit = 300 - 100 = 200
        $this->assertEquals(200.0, (float) $kpis['net_profit']);
        // Net Margin = (200 / 800) * 100 = 25.0%
        $this->assertEquals(25.0, (float) $kpis['net_margin_pct']);
        $this->assertTrue($kpis['is_profitable']);

        // Check expense category breakdown
        $categories = $reportRes->json('expenses_by_category');
        $this->assertCount(1, $categories);
        $this->assertEquals('Tea & Refreshments (Kharcha)', $categories[0]['category_name']);
        $this->assertEquals(100.0, (float) $categories[0]['total_amount']);
    }
}
