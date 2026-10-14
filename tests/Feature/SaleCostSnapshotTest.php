<?php

namespace Tests\Feature;

use App\Http\Controllers\PurchaseInvoiceController;
use App\Http\Controllers\ReportsController;
use App\Http\Controllers\SaleInvoiceController;
use App\Http\Controllers\SaleReturnController;
use App\Models\Batch;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\SaleReturn;
use App\Models\User;
use Barryvdh\DomPDF\Facade\Pdf;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Gate;
use Illuminate\Validation\ValidationException;
use Tests\TestCase;

class SaleCostSnapshotTest extends TestCase
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
            '2026_04_01_000000_add_whole_sale_prices_to_products_table.php',
            '2026_04_01_000003_add_whole_sale_margin_to_products_table.php',
            '2026_04_01_000001_add_whole_sale_prices_to_purchase_invoice_items_table.php',
            '2026_04_01_000002_add_whole_sale_margin_to_purchase_invoice_items_table.php',
            '2026_04_02_000000_add_sale_type_to_sale_invoices_table.php',
            '2026_04_02_000001_add_sale_type_to_sale_invoice_items_table.php',
            '2026_04_02_000003_add_wholesale_type_to_sale_invoices_table.php',
            '2025_09_08_171931_create_customer_ledgers_table.php',
            '2026_05_01_000000_create_user_demands_table.php',
            '2026_10_12_000001_create_purchase_orders.php',
            '2026_10_13_000006_add_sale_cost_snapshots.php',
        ] as $file) {
            (require database_path('migrations/'.$file))->up();
        }
        $user = User::forceCreate(['name' => 'Tester', 'email' => 'cost@example.test', 'password' => bcrypt('test')]);
        $this->actingAs($user);
        Gate::before(fn () => true);
        DB::table('customers')->insert(['id' => 1, 'name' => 'Customer']);
        DB::table('categories')->insert(['id' => 1, 'name' => 'Category']);
        DB::table('brands')->insert(['id' => 1, 'name' => 'Brand']);
        DB::table('suppliers')->insert(['id' => 1, 'name' => 'Supplier']);
        Product::create(['category_id' => 1, 'brand_id' => 1, 'supplier_id' => 1, 'name' => 'Item', 'product_code' => 'P1', 'barcode' => 'B1', 'pack_size' => 10,
            'quantity' => 100, 'unit_purchase_price' => 120, 'avg_price' => 100, 'unit_sale_price' => 150]);
    }

    private function request(array $data, string $method = 'POST'): Request
    {
        $request = Request::create('/api/test', $method, $data);
        $request->setUserResolver(fn () => auth()->user());
        $this->app->instance('request', $request);

        return $request;
    }

    private function sale(int $quantity = 2): SaleInvoice
    {
        $data = ['customer_id' => 1, 'date' => '2026-10-13', 'invoice_type' => 'debit',
            'gross_amount' => $quantity * 150, 'item_discount' => 30, 'discount_amount' => 10,
            'tax_amount' => 5, 'total' => $quantity * 150 - 35, 'items' => [[
                'product_id' => 1, 'pack_size' => 10, 'quantity' => $quantity, 'price' => 150,
                'item_discount_percentage' => 10, 'sub_total' => $quantity * 135,
                'cost_price_snapshot' => 9999,
            ]]];
        app(SaleInvoiceController::class)->store($this->request($data));

        return SaleInvoice::latest('id')->first();
    }

    private function detail(SaleInvoice $sale): array
    {
        $response = app(ReportsController::class)->costOfSaleDetail($this->request(['invoice_id' => $sale->id], 'GET'));
        $this->assertSame(200, $response->status(), $response->getContent());

        return $response->getData(true);
    }

    private function daily(): array
    {
        $response = app(ReportsController::class)->costOfSale($this->request(['from' => '2026-10-13', 'to' => '2026-10-13'], 'GET'));
        $this->assertSame(200, $response->status(), $response->getContent());

        return $response->getData(true)[0];
    }

    private function purchase(int $paid, float $price, int $bonus = 0, float $discount = 0): void
    {
        $quantity = $paid + $bonus;
        $subtotal = round($paid * $price * (1 - $discount / 100), 2);
        $effective = round($subtotal / $quantity, 2);
        $response = app(PurchaseInvoiceController::class)->store($this->request([
            'supplier_id' => 1, 'posted_date' => '2026-10-13',
            'invoice_number' => 'MULTI-'.(DB::table('purchase_invoices')->count() + 1),
            'invoice_type' => 'debit', 'invoice_amount' => $subtotal, 'total_amount' => $subtotal,
            'items' => [[
                'product_id' => 1, 'pack_size' => 10, 'batch' => '', 'expiry' => null,
                'pack_quantity' => $paid / 10, 'unit_quantity' => $paid,
                'pack_purchase_price' => $price * 10, 'unit_purchase_price' => $price,
                'pack_sale_price' => 1500, 'unit_sale_price' => 150,
                'item_discount_percentage' => $discount,
                'pack_bonus' => $bonus / 10, 'unit_bonus' => $bonus,
                'margin' => 0, 'sub_total' => $subtotal, 'avg_price' => $effective, 'quantity' => $quantity,
            ]],
        ]));
        $this->assertSame(201, $response->status(), $response->getContent());
    }

    public function test_real_purchases_at_increasing_and_decreasing_prices_keep_each_sale_profit_fixed(): void
    {
        Product::find(1)->update(['quantity' => 0, 'avg_price' => 0]);
        $this->purchase(10, 100);
        $first = $this->sale();
        $firstReport = $this->detail($first);
        $this->assertEquals(100, $first->items()->first()->cost_price_snapshot);
        $this->assertEquals(65, $firstReport['summary']['total_profit']);

        // 8 remaining at 100 + 12 incoming at 120 = 20 at 112.
        $this->purchase(12, 120);
        $this->assertEquals(20, Product::find(1)->quantity);
        $this->assertEquals(112, Product::find(1)->avg_price);
        $second = $this->sale();
        $secondReport = $this->detail($second);
        $this->assertEquals(112, $second->items()->first()->cost_price_snapshot);
        $this->assertEquals(41, $secondReport['summary']['total_profit']);
        $this->assertSame($firstReport, $this->detail($first));

        // 18 remaining at 112 + 2 incoming at 80 = 20 at 108.80.
        $this->purchase(2, 80);
        $this->assertEquals(108.8, Product::find(1)->avg_price);
        $third = $this->sale();
        $this->assertEquals(108.8, $third->items()->first()->cost_price_snapshot);
        $this->assertEquals(47.4, $this->detail($third)['summary']['total_profit']);
        $this->assertSame($firstReport, $this->detail($first));
        $this->assertSame($secondReport, $this->detail($second));
        $this->assertEquals(641.6, $this->daily()['cost_of_sales']);
        $this->assertEquals(795, $this->daily()['total_sales']);
        $this->assertFalse($this->daily()['cost_is_estimated']);
    }

    public function test_real_discounted_bonus_purchase_uses_existing_effective_cost_and_return_snapshot(): void
    {
        Product::find(1)->update(['quantity' => 0, 'avg_price' => 0]);
        $this->purchase(10, 100);
        $first = $this->sale();
        $firstReport = $this->detail($first);
        // 10 paid at 120, 10% discount, 2 bonus units: 1080 / 12 = 90 effective cost.
        // 8 remaining at 100 + 12 incoming at 90 = 20 at 94.
        $this->purchase(10, 120, 2, 10);
        $this->assertEquals(94, Product::find(1)->avg_price);
        $second = $this->sale();
        $this->assertEquals(94, $second->items()->first()->cost_price_snapshot);
        $this->assertEquals(77, $this->detail($second)['summary']['total_profit']);
        $this->purchase(2, 80);
        $this->assertEquals(92.6, Product::find(1)->avg_price);
        $this->assertSame($firstReport, $this->detail($first));
        app(SaleReturnController::class)->store($this->request([
            'customer_id' => 1, 'sale_invoice_id' => $first->id, 'date' => '2026-10-13', 'total' => 135,
            'items' => [['product_id' => 1, 'unit_return_quantity' => 1, 'unit_sale_quantity' => 2,
                'unit_sale_price' => 150, 'item_discount_percentage' => 10, 'sub_total' => 135]],
        ]));
        $this->assertEquals(100, SaleReturn::first()->items()->first()->cost_price_snapshot);
        // 2 * 100 + 2 * 94 - 1 * 100 = 288; same return deduction formula.
        $this->assertEquals(288, $this->daily()['cost_of_sales']);
        $this->assertEquals(135, $this->daily()['sale_return']);
        $this->assertFalse($this->daily()['cost_is_estimated']);
    }

    public function test_second_cashier_stale_quantity_is_rejected_without_changing_stock_or_invoices(): void
    {
        Product::find(1)->update(['quantity' => 5]);
        $first = $this->sale(4);
        try {
            $this->sale(4);
            $this->fail('Stale sale was accepted');
        } catch (ValidationException $e) {
            $this->assertStringContainsString('Available: 1 units', $e->errors()['items.0.quantity'][0]);
        }
        $this->assertSame(1, SaleInvoice::count());
        $this->assertEquals(1, Product::find(1)->quantity);
        $this->assertEquals(100, $first->items()->first()->cost_price_snapshot);
    }

    public function test_duplicate_product_rows_are_checked_together_and_fail_atomically(): void
    {
        Product::find(1)->update(['quantity' => 5]);
        $row = ['product_id' => 1, 'quantity' => 3, 'price' => 150, 'sub_total' => 450];
        try {
            app(SaleInvoiceController::class)->store($this->request(['customer_id' => 1,
                'date' => '2026-10-13', 'gross_amount' => 900, 'total' => 900, 'items' => [$row, $row]]));
            $this->fail('Combined over-sale accepted');
        } catch (ValidationException $e) {
            $this->assertArrayHasKey('items.1.quantity', $e->errors());
        }
        $this->assertEquals(5, Product::find(1)->quantity);
        $this->assertSame(0, SaleInvoice::count());
        $this->assertSame(0, DB::table('sale_invoice_items')->count());
    }

    public function test_batch_availability_is_enforced_even_with_sufficient_product_stock(): void
    {
        $batch = Batch::create(['product_id' => 1, 'batch_number' => 'LOT-A',
            'expiry_date' => '2028-01-01', 'quantity' => 3]);
        foreach (['LOT-A', 'MISSING'] as $number) {
            try {
                app(SaleInvoiceController::class)->store($this->request(['customer_id' => 1,
                    'date' => '2026-10-13', 'gross_amount' => 600, 'total' => 600,
                    'items' => [['product_id' => 1, 'quantity' => 4, 'price' => 150, 'sub_total' => 600,
                        'batch_number' => $number, 'expiry' => '2028-01-01']]]));
                $this->fail('Invalid batch sale accepted');
            } catch (ValidationException $e) {
                $this->assertNotEmpty($e->errors());
            }
        }
        $this->assertSame(0, SaleInvoice::count());
        $this->assertEquals(100, Product::find(1)->quantity);
        $this->assertEquals(3, $batch->fresh()->quantity);
    }

    public function test_batch_sale_edit_credits_original_units_and_rejected_edit_preserves_everything(): void
    {
        Product::find(1)->update(['quantity' => 5]);
        $batch = Batch::create(['product_id' => 1, 'batch_number' => 'LOT-A',
            'expiry_date' => '2028-01-01', 'quantity' => 5]);
        app(SaleInvoiceController::class)->store($this->request(['customer_id' => 1,
            'date' => '2026-10-13', 'gross_amount' => 600, 'total' => 600,
            'items' => [['product_id' => 1, 'quantity' => 4, 'price' => 150, 'sub_total' => 600,
                'batch_number' => 'LOT-A', 'expiry' => '2028-01-01']]]));
        $sale = SaleInvoice::first();
        $this->assertEquals(1, Product::find(1)->quantity);
        $this->assertEquals(1, $batch->fresh()->quantity);
        $edit = $sale->load('items')->toArray();
        $edit['items'][0]['quantity'] = 5;
        $edit['items'][0]['sub_total'] = $edit['total'] = 750;
        app(SaleInvoiceController::class)->update($this->request($edit), $sale->id);
        $this->assertEquals(0, Product::find(1)->quantity);
        $this->assertEquals(0, $batch->fresh()->quantity);
        $this->assertEquals(100, $sale->items()->first()->cost_price_snapshot);
        $edit = $sale->fresh()->load('items')->toArray();
        $edit['items'][0]['quantity'] = 6;
        try {
            app(SaleInvoiceController::class)->update($this->request($edit), $sale->id);
            $this->fail('Oversized edit accepted');
        } catch (ValidationException $e) {
            $this->assertArrayHasKey('items.0.quantity', $e->errors());
        }
        $this->assertEquals(5, $sale->items()->first()->quantity);
        $this->assertEquals(750, $sale->fresh()->total);
        $this->assertEquals(0, Product::find(1)->quantity);
        $this->assertEquals(0, $batch->fresh()->quantity);
    }

    public function test_saved_cost_freezes_reports_without_changing_profit_formulas(): void
    {
        $sale = $this->sale();
        $this->assertEquals(100, $sale->items()->first()->cost_price_snapshot);
        $before = $this->detail($sale);
        $this->assertEquals(200, $before['summary']['total_cost']);
        $this->assertEquals(65, $before['summary']['total_profit']);
        $this->assertEquals(24.53, $before['summary']['profit_margin']);
        $this->assertEquals(70, $before['invoices'][0]['items'][0]['profit']);
        Product::find(1)->update(['avg_price' => 125, 'unit_purchase_price' => 140]);
        $this->assertSame($before, $this->detail($sale));
        $daily = $this->daily();
        $this->assertEquals(200, $daily['cost_of_sales']);
        $this->assertEquals(30, $daily['item_discount']);
        $this->assertEquals(10, $daily['discount_amount']);
        $this->assertEquals(5, $daily['tax_amount']);
        $this->assertFalse($daily['cost_is_estimated']);
    }

    public function test_cost_is_captured_before_depletion_and_zero_is_a_real_snapshot(): void
    {
        Product::find(1)->update(['quantity' => 2]);
        $sale = $this->sale();
        $this->assertEquals(0, Product::find(1)->avg_price);
        $this->assertEquals(100, $sale->items()->first()->cost_price_snapshot);
        $this->assertEquals(200, $this->detail($sale)['summary']['total_cost']);
        Product::find(1)->update(['quantity' => 2, 'avg_price' => 0, 'unit_purchase_price' => 0]);
        $free = $this->sale();
        Product::find(1)->update(['unit_purchase_price' => 500]);
        $this->assertEquals(0, $this->detail($free)['summary']['total_cost']);
        $this->assertFalse($this->detail($free)['invoices'][0]['cost_is_estimated']);
    }

    public function test_legacy_costs_keep_the_existing_fallback_and_estimated_label_on_edit(): void
    {
        Product::find(1)->update(['avg_price' => 0]);
        $sale = $this->sale();
        $this->assertEquals(120, $sale->items()->first()->cost_price_snapshot);
        $sale->items()->update(['cost_price_snapshot' => null]);
        Product::find(1)->update(['unit_purchase_price' => 130]);
        $this->assertEquals(260, $this->detail($sale)['summary']['total_cost']);
        $this->assertTrue($this->detail($sale)['invoices'][0]['cost_is_estimated']);
        $this->assertTrue($this->daily()['cost_is_estimated']);
        $data = $sale->load('items')->toArray();
        app(SaleInvoiceController::class)->update($this->request($data), $sale->id);
        $this->assertNull($sale->items()->first()->cost_price_snapshot);
    }

    public function test_sale_edit_preserves_saved_cost_and_ignores_client_cost(): void
    {
        $sale = $this->sale();
        Product::find(1)->update(['avg_price' => 125]);
        $data = $sale->load('items')->toArray();
        $data['items'][0]['quantity'] = 3;
        $data['items'][0]['sub_total'] = 405;
        $data['items'][0]['cost_price_snapshot'] = 9999;
        $data['total'] = 400;
        app(SaleInvoiceController::class)->update($this->request($data), $sale->id);
        $this->assertEquals(100, $sale->items()->first()->cost_price_snapshot);
        $this->assertEquals(300, $this->detail($sale)['summary']['total_cost']);
        $this->assertEquals(100, $this->detail($sale)['summary']['total_profit']);
    }

    public function test_linked_return_uses_original_sale_cost_and_keeps_it_on_edit(): void
    {
        $sale = $this->sale();
        Product::find(1)->update(['avg_price' => 125]);
        $data = ['customer_id' => 1, 'sale_invoice_id' => $sale->id, 'date' => '2026-10-13', 'total' => 135,
            'items' => [['product_id' => 1, 'unit_return_quantity' => 1, 'unit_sale_quantity' => 2,
                'unit_sale_price' => 150, 'sub_total' => 135, 'cost_price_snapshot' => 9999]]];
        app(SaleReturnController::class)->store($this->request($data));
        $return = SaleReturn::first();
        $this->assertEquals(100, $return->items()->first()->cost_price_snapshot);
        $this->assertEquals(100, $this->daily()['cost_of_sales']);
        $this->assertEquals(135, $this->daily()['sale_return']);
        $this->assertFalse($this->daily()['cost_is_estimated']);
        Product::find(1)->update(['avg_price' => 200]);
        $edit = $return->load('items')->toArray();
        app(SaleReturnController::class)->update($this->request($edit), $return);
        $this->assertEquals(100, $return->items()->first()->cost_price_snapshot);
        $this->assertEquals(100, $this->daily()['cost_of_sales']);
    }

    public function test_unlinked_and_legacy_linked_returns_remain_estimated(): void
    {
        $sale = $this->sale();
        $sale->items()->update(['cost_price_snapshot' => null]);
        foreach ([null, $sale->id] as $source) {
            app(SaleReturnController::class)->store($this->request(['customer_id' => 1,
                'sale_invoice_id' => $source, 'date' => '2026-10-13', 'total' => 150,
                'items' => [['product_id' => 1, 'unit_return_quantity' => 1, 'unit_sale_price' => 150, 'sub_total' => 150]]]));
            $this->assertNull(SaleReturn::latest('id')->first()->items()->first()->cost_price_snapshot);
        }
        $this->assertTrue($this->daily()['cost_is_estimated']);
    }

    public function test_migration_adds_nullable_costs_without_rewriting_old_amounts(): void
    {
        $migration = require database_path('migrations/2026_10_13_000006_add_sale_cost_snapshots.php');
        $sale = $this->sale();
        $amounts = $sale->only(['gross_amount', 'item_discount', 'discount_amount', 'tax_amount', 'total']);
        $migration->down();
        $migration->up();
        $this->assertNull($sale->items()->first()->cost_price_snapshot);
        $this->assertSame($amounts, $sale->fresh()->only(array_keys($amounts)));
    }

    public function test_pdf_uses_the_same_saved_cost_and_legacy_labels_as_json(): void
    {
        $sale = $this->sale();
        Product::find(1)->update(['avg_price' => 175]);
        foreach ([false, true] as $legacy) {
            if ($legacy) {
                $sale->items()->update(['cost_price_snapshot' => null]);
            }
            $json = $this->detail($sale);
            $pdf = \Mockery::mock(\Barryvdh\DomPDF\PDF::class);
            $pdf->shouldReceive('setPaper')->with('a4', 'landscape')->once()->andReturnSelf();
            $pdf->shouldReceive('stream')->once()->andReturn(response('PDF verified'));
            Pdf::shouldReceive('loadView')->once()
                ->with('reports.cost_of_sale_detail_pdf', \Mockery::on(function ($data) use ($json, $legacy) {
                    $this->assertEquals($json['summary']['total_cost'], $data['summary']['total_cost']);
                    $this->assertEquals($json['summary']['total_profit'], $data['summary']['total_profit']);
                    $this->assertEquals($json['summary']['profit_margin'], $data['summary']['profit_margin']);
                    $this->assertSame($legacy, $data['rows'][0]['cost_is_estimated']);
                    $this->assertSame($legacy, $data['rows'][0]['items'][0]['cost_is_estimated']);

                    return true;
                }))->andReturn($pdf);
            $response = app(ReportsController::class)->costOfSaleDetailPdf($this->request(['invoice_id' => $sale->id], 'GET'));
            $this->assertSame(200, $response->status(), $response->getContent());
        }
    }
}
