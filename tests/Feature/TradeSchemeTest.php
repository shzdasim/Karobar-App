<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\TradeScheme;
use App\Models\User;
use App\Services\TradeSchemeService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class TradeSchemeTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;
    protected Product $product;
    protected Brand $brand;
    protected Category $category;
    protected Customer $customer;
    protected TradeSchemeService $schemeService;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        $this->user = User::create([
            'name'     => 'Test Manager',
            'email'    => 'manager@test.com',
            'password' => bcrypt('secret123'),
        ]);

        app()[\Spatie\Permission\PermissionRegistrar::class]->forgetCachedPermissions();
        $permissions = [
            'customer.view',
            'customer.create',
            'customer.update',
            'sale-invoice.view',
            'sale-invoice.create',
            'sale-invoice.update',
        ];
        foreach ($permissions as $perm) {
            \Spatie\Permission\Models\Permission::findOrCreate($perm, 'sanctum');
        }
        $this->user->givePermissionTo($permissions);

        $this->actingAs($this->user);

        $this->brand = Brand::create(['name' => 'GlaxoSmithKline']);
        $this->category = Category::create(['name' => 'Analgesics']);

        $this->product = Product::create([
            'name'                  => 'Panadol Extra 500mg',
            'product_code'          => 'PAN-500',
            'barcode'               => '8964000111222',
            'brand_id'              => $this->brand->id,
            'category_id'           => $this->category->id,
            'pack_size'             => 10,
            'unit_sale_price'       => 5.0,
            'pack_sale_price'       => 50.0,
            'whole_sale_pack_price' => 45.0,
            'whole_sale_unit_price' => 4.5,
        ]);

        $this->customer = Customer::create([
            'name'                 => 'Al-Shifa Pharmacy',
            'phone'                => '03001234567',
            'eligible_for_schemes' => true,
        ]);

        $this->schemeService = app(TradeSchemeService::class);
    }

    public function test_can_create_and_evaluate_ratio_trade_scheme(): void
    {
        // Scheme: Buy 10 packs, get 1 pack free (10+1)
        $response = $this->postJson('/api/trade-schemes', [
            'name'        => 'Panadol 10+1 Trade Scheme',
            'scheme_type' => 'ratio',
            'target_type' => 'product',
            'target_id'   => $this->product->id,
            'bonus_type'  => 'pack',
            'sale_type'   => 'all',
            'is_active'   => true,
            'priority'    => 10,
            'slabs'       => [
                [
                    'min_qty'   => 10,
                    'bonus_qty' => 1,
                ],
            ],
        ]);

        $response->assertStatus(201);
        $this->assertDatabaseHas('trade_schemes', ['name' => 'Panadol 10+1 Trade Scheme']);
        $this->assertDatabaseHas('trade_scheme_slabs', ['min_qty' => 10, 'bonus_qty' => 1]);

        $activeSchemes = $this->schemeService->getActiveSchemes('wholesale', $this->customer->id);
        $this->assertCount(1, $activeSchemes);

        // Case 1: Buying 9 packs -> 0 bonus
        $eval1 = $this->schemeService->evaluateBonus([
            'product_id'    => $this->product->id,
            'pack_size'     => 10,
            'pack_quantity' => 9,
            'quantity'      => 90,
        ], $activeSchemes);

        $this->assertFalse($eval1['matched']);
        $this->assertEquals(0, $eval1['pack_bonus']);

        // Case 2: Buying 10 packs -> 1 pack free (10 units bonus)
        $eval2 = $this->schemeService->evaluateBonus([
            'product_id'    => $this->product->id,
            'pack_size'     => 10,
            'pack_quantity' => 10,
            'quantity'      => 100,
        ], $activeSchemes);

        $this->assertTrue($eval2['matched']);
        $this->assertEquals(1, $eval2['pack_bonus']);
        $this->assertEquals(10, $eval2['unit_bonus']);

        // Case 3: Buying 25 packs -> 2 packs free (multiple of 10)
        $eval3 = $this->schemeService->evaluateBonus([
            'product_id'    => $this->product->id,
            'pack_size'     => 10,
            'pack_quantity' => 25,
            'quantity'      => 250,
        ], $activeSchemes);

        $this->assertTrue($eval3['matched']);
        $this->assertEquals(2, $eval3['pack_bonus']);
        $this->assertEquals(20, $eval3['unit_bonus']);
    }

    public function test_can_evaluate_tiered_slab_trade_scheme(): void
    {
        // Slabs: 10-24 => 1 bonus pack, 25-49 => 3 bonus packs, 50+ => 8 bonus packs
        $scheme = TradeScheme::create([
            'name'        => 'Tiered Slabs Scheme',
            'scheme_type' => 'slab',
            'target_type' => 'product',
            'target_id'   => $this->product->id,
            'bonus_type'  => 'pack',
            'sale_type'   => 'all',
            'is_active'   => true,
        ]);

        $scheme->slabs()->createMany([
            ['min_qty' => 10, 'max_qty' => 24, 'bonus_qty' => 1],
            ['min_qty' => 25, 'max_qty' => 49, 'bonus_qty' => 3],
            ['min_qty' => 50, 'max_qty' => null, 'bonus_qty' => 8],
        ]);

        $activeSchemes = $this->schemeService->getActiveSchemes('wholesale');

        // Test 15 packs -> tier 1 -> 1 pack bonus
        $eval15 = $this->schemeService->evaluateBonus([
            'product_id'    => $this->product->id,
            'pack_size'     => 10,
            'pack_quantity' => 15,
            'quantity'      => 150,
        ], $activeSchemes);
        $this->assertTrue($eval15['matched']);
        $this->assertEquals(1, $eval15['pack_bonus']);

        // Test 30 packs -> tier 2 -> 3 packs bonus
        $eval30 = $this->schemeService->evaluateBonus([
            'product_id'    => $this->product->id,
            'pack_size'     => 10,
            'pack_quantity' => 30,
            'quantity'      => 300,
        ], $activeSchemes);
        $this->assertTrue($eval30['matched']);
        $this->assertEquals(3, $eval30['pack_bonus']);

        // Test 60 packs -> tier 3 -> 8 packs bonus
        $eval60 = $this->schemeService->evaluateBonus([
            'product_id'    => $this->product->id,
            'pack_size'     => 10,
            'pack_quantity' => 60,
            'quantity'      => 600,
        ], $activeSchemes);
        $this->assertTrue($eval60['matched']);
        $this->assertEquals(8, $eval60['pack_bonus']);
    }

    public function test_customer_exclusion_from_trade_schemes(): void
    {
        TradeScheme::create([
            'name'        => 'Global Panadol Promo',
            'scheme_type' => 'ratio',
            'target_type' => 'product',
            'target_id'   => $this->product->id,
            'bonus_type'  => 'pack',
            'is_active'   => true,
        ])->slabs()->create(['min_qty' => 10, 'bonus_qty' => 1]);

        // Customer with eligible_for_schemes = false
        $excludedCustomer = Customer::create([
            'name'                 => 'Government Hospital (Net Rate)',
            'phone'                => '03009999999',
            'eligible_for_schemes' => false,
        ]);

        $activeSchemes = $this->schemeService->getActiveSchemes('wholesale', $excludedCustomer->id);
        $this->assertCount(0, $activeSchemes);

        // Standard eligible customer gets scheme
        $eligibleSchemes = $this->schemeService->getActiveSchemes('wholesale', $this->customer->id);
        $this->assertCount(1, $eligibleSchemes);
    }

    public function test_manual_override_respected_on_sale_invoice_creation(): void
    {
        $this->product->update(['quantity' => 500, 'current_quantity' => 500]);

        // Batch with 500 units stock
        $batch = Batch::create([
            'product_id'      => $this->product->id,
            'batch_number'    => 'BATCH-SCHEME-01',
            'expiry_date'     => now()->addYear()->toDateString(),
            'quantity'        => 500,
            'purchase_price'  => 3.0,
            'sale_price'      => 5.0,
        ]);

        $scheme = TradeScheme::create([
            'name'        => 'Panadol 10+1',
            'scheme_type' => 'ratio',
            'target_type' => 'product',
            'target_id'   => $this->product->id,
            'bonus_type'  => 'pack',
            'is_active'   => true,
        ]);
        $scheme->slabs()->create(['min_qty' => 10, 'bonus_qty' => 1]);

        // Scenario 1: Invoice with automated scheme bonus (10 packs + 1 pack bonus = 100 billed + 10 free units)
        $resAuto = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $this->customer->id,
            'sale_type'      => 'wholesale',
            'date'           => now()->toDateString(),
            'invoice_type'   => 'debit',
            'gross_amount'   => 450,
            'total'          => 450,
            'total_receive'  => 450,
            'items'          => [
                [
                    'product_id'          => $this->product->id,
                    'batch_number'        => $batch->batch_number,
                    'pack_size'           => 10,
                    'pack_quantity'       => 10,
                    'unit_quantity'       => 0,
                    'quantity'            => 100,
                    'pack_bonus'          => 1,
                    'unit_bonus'          => 10,
                    'bonus'               => 10,
                    'pack_price'          => 45.0,
                    'price'               => 4.5,
                    'sub_total'           => 450.0,
                    'applied_scheme_id'   => $scheme->id,
                    'applied_scheme_name' => $scheme->name,
                ],
            ],
        ]);

        $resAuto->assertStatus(201);
        $invoiceAutoId = $resAuto->json('id');

        $this->assertDatabaseHas('sale_invoice_items', [
            'sale_invoice_id'     => $invoiceAutoId,
            'quantity'            => 100,
            'bonus'               => 10,
            'applied_scheme_id'   => $scheme->id,
            'applied_scheme_name' => 'Panadol 10+1',
        ]);

        // Stock delta: 500 - (100 + 10) = 390 units remaining
        $this->assertEquals(390, $batch->fresh()->quantity);

        // Scenario 2: Manual Override (Operator explicitly waives/zeros bonus: 10 packs, 0 bonus)
        $resOverridden = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $this->customer->id,
            'sale_type'      => 'wholesale',
            'date'           => now()->toDateString(),
            'invoice_type'   => 'debit',
            'gross_amount'   => 450,
            'total'          => 450,
            'total_receive'  => 450,
            'items'          => [
                [
                    'product_id'          => $this->product->id,
                    'batch_number'        => $batch->batch_number,
                    'pack_size'           => 10,
                    'pack_quantity'       => 10,
                    'unit_quantity'       => 0,
                    'quantity'            => 100,
                    'pack_bonus'          => 0,
                    'unit_bonus'          => 0,
                    'bonus'               => 0,
                    'pack_price'          => 45.0,
                    'price'               => 4.5,
                    'sub_total'           => 450.0,
                    'applied_scheme_id'   => null,
                    'applied_scheme_name' => null,
                ],
            ],
        ]);

        $resOverridden->assertStatus(201);
        $invoiceOverriddenId = $resOverridden->json('id');

        $this->assertDatabaseHas('sale_invoice_items', [
            'sale_invoice_id' => $invoiceOverriddenId,
            'quantity'        => 100,
            'bonus'           => 0,
        ]);

        // Stock delta: 390 - 100 = 290 units remaining
        $this->assertEquals(290, $batch->fresh()->quantity);
    }

    public function test_retail_sale_evaluates_ratio_scheme_proportionally(): void
    {
        // 2+1 ratio scheme on product with pack_size = 10
        $scheme = TradeScheme::create([
            'name'        => 'Panadol 2+1 Scheme',
            'scheme_type' => 'ratio',
            'target_type' => 'product',
            'target_id'   => $this->product->id,
            'bonus_type'  => 'pack',
            'sale_type'   => 'all',
            'is_active'   => true,
        ]);
        $scheme->slabs()->create(['min_qty' => 2, 'bonus_qty' => 1]);

        $activeSchemes = $this->schemeService->getActiveSchemes('retail');

        // Case 1: Retail customer buys 2 units -> 1 unit bonus
        $eval2 = $this->schemeService->evaluateBonus([
            'product_id' => $this->product->id,
            'pack_size'  => 10,
            'quantity'   => 2,
        ], $activeSchemes, 'retail');

        $this->assertTrue($eval2['matched']);
        $this->assertEquals(1, $eval2['bonus_units']);

        // Case 2: Retail customer buys 4 units -> 2 units bonus
        $eval4 = $this->schemeService->evaluateBonus([
            'product_id' => $this->product->id,
            'pack_size'  => 10,
            'quantity'   => 4,
        ], $activeSchemes, 'retail');

        $this->assertTrue($eval4['matched']);
        $this->assertEquals(2, $eval4['bonus_units']);

        // Case 3: Retail customer buys 20 units (2 packs) -> 10 units bonus (1 full pack)
        $eval20 = $this->schemeService->evaluateBonus([
            'product_id' => $this->product->id,
            'pack_size'  => 10,
            'quantity'   => 20,
        ], $activeSchemes, 'retail');

        $this->assertTrue($eval20['matched']);
        $this->assertEquals(10, $eval20['bonus_units']);
        $this->assertEquals(1.0, $eval20['pack_bonus']);
    }

    public function test_purchase_10_plus_1_and_sale_20_plus_1_profit_and_margin_calculation(): void
    {
        $adminRole = \Spatie\Permission\Models\Role::findOrCreate('Admin', 'sanctum');
        $this->user->assignRole($adminRole);

        $supplier = \App\Models\Supplier::create([
            'name'  => 'GSK Distribution',
            'phone' => '03001112222',
        ]);

        // Product: Pack Size = 1 (or 10), Base Unit Sale Price = 100.00, Base Unit Purchase Price (before bonus) = 85.00 (15% margin before bonus)
        $product = Product::create([
            'name'                  => 'Test Product 15% Base Margin',
            'product_code'          => 'TP-15',
            'barcode'               => '8964000999888',
            'brand_id'              => $this->brand->id,
            'category_id'           => $this->category->id,
            'supplier_id'           => $supplier->id,
            'pack_size'             => 1,
            'quantity'              => 0,
            'pack_purchase_price'   => 85.00,
            'unit_purchase_price'   => 85.00,
            'pack_sale_price'       => 100.00,
            'unit_sale_price'       => 100.00,
            'whole_sale_pack_price' => 100.00,
            'whole_sale_unit_price' => 100.00,
            'avg_price'             => 85.00,
            'margin'                => 15.00,
            'whole_sale_margin'     => 15.00,
        ]);

        $today = now()->toDateString();

        // STEP 1: Buy at 10 + 1 ratio from Supplier (20 paid + 2 bonus = 22 total units so we have enough stock to sell 20+1 = 21 units)
        // Paid Cost = 20 * 85.00 = 1,700.00
        // Total Units Received = 20 + 2 = 22 units
        // Effective Average Unit Cost = 1700 / 22 = 77.2727... -> 77.27
        // New Product Margin after 10+1 Purchase Bonus = (100 - 77.27) / 100 = 22.73%
        $purchaseRes = $this->postJson('/api/purchase-invoices', [
            'supplier_id'         => $supplier->id,
            'posted_date'         => $today,
            'invoice_number'      => 'SUP-10PLUS1-001',
            'invoice_type'        => 'debit',
            'invoice_amount'      => 1700.00,
            'discount_percentage' => 0,
            'discount_amount'     => 0,
            'tax_percentage'      => 0,
            'tax_amount'          => 0,
            'total_amount'        => 1700.00,
            'total_paid'          => 1700.00,
            'items'               => [
                [
                    'product_id'               => $product->id,
                    'pack_size'                => 1,
                    'batch'                    => 'B-10PLUS1',
                    'expiry'                   => now()->addYear()->toDateString(),
                    'pack_quantity'            => 20,
                    'unit_quantity'            => 20,
                    'pack_purchase_price'      => 85.00,
                    'unit_purchase_price'      => 85.00,
                    'pack_sale_price'          => 100.00,
                    'unit_sale_price'          => 100.00,
                    'whole_sale_pack_price'    => 100.00,
                    'whole_sale_unit_price'    => 100.00,
                    'whole_sale_margin'        => 22.73,
                    'item_discount_percentage' => 0,
                    'pack_bonus'               => 2,
                    'unit_bonus'               => 2,
                    'margin'                   => 22.73,
                    'sub_total'                => 1700.00,
                    'avg_price'                => 77.27,
                    'quantity'                 => 22,
                ],
            ],
        ]);

        $purchaseRes->assertStatus(201);
        $product->refresh();

        $this->assertEquals(22, $product->quantity);
        $this->assertEquals(77.27, (float) $product->avg_price);
        $this->assertEquals(22.73, (float) $product->margin);

        // STEP 2: Create a 20 + 1 Trade Scheme for customers
        $scheme = TradeScheme::create([
            'name'        => 'Customer 20+1 Promo',
            'scheme_type' => 'ratio',
            'target_type' => 'product',
            'target_id'   => $product->id,
            'bonus_type'  => 'unit',
            'sale_type'   => 'all',
            'is_active'   => true,
        ]);
        $scheme->slabs()->create(['min_qty' => 20, 'bonus_qty' => 1]);

        $activeSchemes = $this->schemeService->getActiveSchemes('retail', $this->customer->id);
        $eval = $this->schemeService->evaluateBonus([
            'product_id' => $product->id,
            'pack_size'  => 1,
            'quantity'   => 20,
        ], $activeSchemes, 'retail');

        $this->assertTrue($eval['matched']);
        $this->assertEquals(1, $eval['bonus_units']);

        // STEP 3: Sell 20 + 1 to the customer on a Sale Invoice
        // Billed Qty = 20 units @ 100.00 = Rs. 2,000.00 Net Revenue
        // Bonus Qty = 1 unit free -> Total Units Deducted from Stock = 21 units
        // Total COGS = 21 units * 77.27 avg_price = Rs. 1,622.67
        // Net Profit (GP Amount) = 2,000.00 - 1,622.67 = Rs. 377.33
        // Realized Profit Margin % = (377.33 / 2000.00) * 100 = 18.8665% -> 18.87%
        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'   => $this->customer->id,
            'sale_type'     => 'retail',
            'date'          => $today,
            'invoice_type'  => 'debit',
            'gross_amount'  => 2000.00,
            'total'         => 2000.00,
            'total_receive' => 2000.00,
            'items'         => [
                [
                    'product_id'          => $product->id,
                    'batch_number'        => 'B-10PLUS1',
                    'expiry'              => now()->addYear()->toDateString(),
                    'pack_size'           => 1,
                    'quantity'            => 20,
                    'bonus'               => 1,
                    'price'               => 100.00,
                    'sub_total'           => 2000.00,
                    'applied_scheme_id'   => $scheme->id,
                    'applied_scheme_name' => $scheme->name,
                ],
            ],
        ]);

        $saleRes->assertStatus(201);
        $product->refresh();

        // Remaining stock: 22 - (20 + 1) = 1 unit
        $this->assertEquals(1, $product->quantity);

        // STEP 4: Verify Cost of Sale & Gross Profit Report
        $reportRes = $this->getJson("/api/reports/cost-of-sale?from={$today}&to={$today}&invoice_type=all");
        $reportRes->assertStatus(200);
        $reportRows = $reportRes->json();
        $this->assertCount(1, $reportRows);

        $row = $reportRows[0];
        $netSale = $row['total_sales'] - $row['sale_return'];
        $cogs = $row['cost_of_sales'];
        $gpAmount = round($netSale - $cogs, 2);
        $gpPct = round(($gpAmount / $netSale) * 100, 2);

        $this->assertEquals(2000.00, $netSale);
        $this->assertEquals(1622.67, $cogs);
        $this->assertEquals(377.33, $gpAmount);
        $this->assertEquals(18.87, $gpPct);
    }
}
