<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class ErpPhase1FeaturesTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;
    protected Category $category;
    protected Brand $brand;
    protected Supplier $supplier;

    protected function setUp(): void
    {
        parent::setUp();

        Gate::before(fn () => true);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $this->user = User::forceCreate([
            'name' => 'ERP Admin',
            'email' => 'erp@karobar.test',
            'password' => bcrypt('password123'),
        ]);
        $this->actingAs($this->user, 'sanctum');

        $this->category = Category::create(['name' => 'Antibiotics']);
        $this->brand = Brand::create(['name' => 'Getz Pharma']);
        $this->supplier = Supplier::create(['name' => 'Muller & Phipps']);
    }

    public function test_batch_specific_mrp_is_preserved_and_returned_by_fefo_scan(): void
    {
        $product = Product::create([
            'name' => 'Augmentin 625mg',
            'product_code' => 'PRD-0010',
            'barcode' => '8900000000101',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 45.00,
            'unit_purchase_price' => 36.00,
            'quantity' => 50,
            'pack_size' => 10,
        ]);

        // Older batch with printed MRP 40.00
        Batch::create([
            'product_id' => $product->id,
            'batch_number' => 'B-OLD',
            'expiry_date' => now()->addMonths(4)->toDateString(),
            'quantity' => 20,
            'unit_sale_price' => 40.00,
            'unit_purchase_price' => 31.00,
        ]);

        // Newer batch with higher printed MRP 45.00
        Batch::create([
            'product_id' => $product->id,
            'batch_number' => 'B-NEW',
            'expiry_date' => now()->addMonths(12)->toDateString(),
            'quantity' => 30,
            'unit_sale_price' => 45.00,
            'unit_purchase_price' => 36.00,
        ]);

        $response = $this->getJson('/api/barcodes/scan?barcode=8900000000101');
        $response->assertStatus(200)
            ->assertJsonPath('found', true)
            ->assertJsonPath('product.selected_batch.batch_number', 'B-OLD')
            ->assertJsonPath('product.selected_batch.unit_sale_price', 40)
            ->assertJsonPath('product.unit_sale_price', 40);
    }

    public function test_weight_embedded_barcode_20xxxx_parses_product_and_fractional_weight(): void
    {
        $product = Product::create([
            'name' => 'Organic Almonds (Loose)',
            'product_code' => 'PRD-0105',
            'barcode' => '00105',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 2800.00,
            'quantity' => 25,
            'pack_size' => 1,
        ]);

        // Prefix 20 + PLU 00105 + Weight 00750g (0.750 kg) + Checksum 8
        $response = $this->getJson('/api/barcodes/scan?barcode=2000105007508');

        $response->assertStatus(200)
            ->assertJsonPath('found', true)
            ->assertJsonPath('is_weight_barcode', true)
            ->assertJsonPath('parsed_quantity', 0.75)
            ->assertJsonPath('weight_grams', 750)
            ->assertJsonPath('product.id', $product->id);
    }

    public function test_generic_formulation_substitutes_endpoint_returns_in_stock_matches(): void
    {
        $outOfStockBrand = Product::create([
            'name' => 'Panadol 500mg',
            'product_code' => 'PRD-0201',
            'barcode' => '8900201',
            'formulation' => 'Paracetamol 500mg',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 5.00,
            'quantity' => 0,
            'pack_size' => 10,
        ]);

        $inStockGeneric = Product::create([
            'name' => 'Calpol 500mg Tabs',
            'product_code' => 'PRD-0202',
            'barcode' => '8900202',
            'formulation' => 'Paracetamol 500mg',
            'rack' => 'R-14',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 4.20,
            'quantity' => 120,
            'pack_size' => 10,
        ]);

        $response = $this->getJson("/api/products/{$outOfStockBrand->id}/substitutes");

        $response->assertStatus(200)
            ->assertJsonPath('match_type', 'formulation')
            ->assertJsonCount(1, 'substitutes')
            ->assertJsonPath('substitutes.0.id', $inStockGeneric->id)
            ->assertJsonPath('substitutes.0.is_exact_formulation', true)
            ->assertJsonPath('substitutes.0.rack', 'R-14');
    }

    public function test_multi_supplier_mapping_for_product(): void
    {
        $product = Product::create([
            'name' => 'Risek 20mg Cap',
            'product_code' => 'PRD-0301',
            'barcode' => '8900301',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_purchase_price' => 18.00,
            'unit_sale_price' => 22.00,
            'quantity' => 40,
            'pack_size' => 14,
        ]);

        $secondarySupplier = Supplier::create(['name' => 'UDL Distribution']);

        $postRes = $this->postJson("/api/products/{$product->id}/suppliers", [
            'supplier_id' => $secondarySupplier->id,
            'supplier_sku' => 'UDL-RSK20',
            'last_purchase_price' => 17.50,
            'lead_time_days' => 2,
            'min_order_qty' => 5,
            'is_preferred' => true,
        ]);

        $postRes->assertStatus(201)
            ->assertJsonPath('supplier_id', $secondarySupplier->id)
            ->assertJsonPath('supplier_sku', 'UDL-RSK20')
            ->assertJsonPath('is_preferred', true);

        $listRes = $this->getJson("/api/products/{$product->id}/suppliers");
        $listRes->assertStatus(200)
            ->assertJsonCount(1);
    }
}

