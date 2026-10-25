<?php

namespace Tests\Feature;

use App\Models\Category;
use App\Models\Brand;
use App\Models\Supplier;
use App\Models\Product;
use App\Models\Batch;
use App\Models\Customer;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class BarcodePosModeTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;
    protected Customer $customer;
    protected Category $category;
    protected Brand $brand;
    protected Supplier $supplier;

    protected function setUp(): void
    {
        parent::setUp();

        Gate::before(fn () => true);

        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $this->user = User::forceCreate([
            'name' => 'POS Cashier',
            'email' => 'pos@karobar.test',
            'password' => bcrypt('password123'),
        ]);
        $this->actingAs($this->user, 'sanctum');

        $this->customer = Customer::create([
            'name' => 'Walk-in Customer',
            'phone' => '03001234567',
            'customer_type' => 'retail',
        ]);

        $this->category = Category::create(['name' => 'Analgesics']);
        $this->brand = Brand::create(['name' => 'GSK']);
        $this->supplier = Supplier::create(['name' => 'Premier Distributor']);
    }

    public function test_can_scan_product_by_barcode(): void
    {
        $product = Product::create([
            'name' => 'Panadol Extra 500mg',
            'product_code' => 'PND-500',
            'barcode' => '8901234567890',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 25.0,
            'quantity' => 100,
            'pack_size' => 10,
        ]);

        $response = $this->getJson('/api/barcodes/scan?barcode=8901234567890');

        $response->assertStatus(200)
            ->assertJson([
                'found' => true,
                'product' => [
                    'id' => $product->id,
                    'name' => 'Panadol Extra 500mg',
                    'barcode' => '8901234567890',
                    'unit_sale_price' => 25.0,
                ],
            ]);
    }

    public function test_can_scan_product_by_product_code(): void
    {
        $product = Product::create([
            'name' => 'Brufen 400mg',
            'product_code' => 'BRU-400-TAB',
            'barcode' => '998877665544',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 15.0,
            'quantity' => 80,
            'pack_size' => 10,
        ]);

        $response = $this->getJson('/api/barcodes/scan?barcode=BRU-400-TAB');

        $response->assertStatus(200)
            ->assertJson([
                'found' => true,
                'product' => [
                    'id' => $product->id,
                    'name' => 'Brufen 400mg',
                    'product_code' => 'BRU-400-TAB',
                ],
            ]);
    }

    public function test_can_scan_product_by_product_id_without_barcode(): void
    {
        $product = Product::create([
            'name' => 'Custom Compounded Ointment',
            'product_code' => 'CCO-999',
            'barcode' => '',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 150.0,
            'quantity' => 15,
            'pack_size' => 1,
        ]);

        $response = $this->getJson("/api/barcodes/scan?product_id={$product->id}");

        $response->assertStatus(200)
            ->assertJson([
                'found' => true,
                'product' => [
                    'id' => $product->id,
                    'name' => 'Custom Compounded Ointment',
                    'unit_sale_price' => 150.0,
                ],
            ]);
    }

    public function test_scan_product_returns_fefo_recommended_batch(): void
    {
        $product = Product::create([
            'name' => 'Augmentin 625mg',
            'product_code' => 'AUG-625',
            'barcode' => '1122334455',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 450.0,
            'quantity' => 60,
            'pack_size' => 6,
        ]);

        // Batch 1: Expiring later (2028)
        Batch::create([
            'product_id' => $product->id,
            'batch_number' => 'BATCH-LATER',
            'expiry_date' => '2028-12-31',
            'quantity' => 30,
        ]);

        // Batch 2: Expiring earlier (2027) - Should be chosen by FEFO
        Batch::create([
            'product_id' => $product->id,
            'batch_number' => 'BATCH-FEFO-FIRST',
            'expiry_date' => '2027-06-30',
            'quantity' => 30,
        ]);

        $response = $this->getJson('/api/barcodes/scan?barcode=1122334455');

        $response->assertStatus(200);
        $this->assertTrue($response->json('found'));
        $this->assertEquals('BATCH-FEFO-FIRST', $response->json('product.selected_batch.batch_number'));
        $this->assertTrue($response->json('product.selected_batch.is_fefo'));
    }

    public function test_scan_non_existent_product_returns_404(): void
    {
        $response = $this->getJson('/api/barcodes/scan?barcode=DOES_NOT_EXIST_999');

        $response->assertStatus(404)
            ->assertJson([
                'found' => false,
                'barcode' => 'DOES_NOT_EXIST_999',
            ]);
    }

    public function test_can_complete_retail_sale_from_pos_mode(): void
    {
        $product = Product::create([
            'name' => 'Disprin 300mg Tablets',
            'product_code' => 'DSP-300',
            'barcode' => '5000167049824',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'unit_sale_price' => 10.0,
            'quantity' => 100,
            'pack_size' => 10,
        ]);

        $batch = Batch::create([
            'product_id' => $product->id,
            'batch_number' => 'DSP-B101',
            'expiry_date' => '2027-10-31',
            'quantity' => 100,
        ]);

        $payload = [
            'sale_type' => 'retail',
            'invoice_type' => 'debit',
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'payment_method' => 'cash',
            'gross_amount' => 30.0,
            'discount_amount' => 0,
            'total' => 30.0,
            'total_receive' => 30.0,
            'remarks' => 'Fast POS checkout',
            'items' => [
                [
                    'product_id' => $product->id,
                    'quantity' => 3,
                    'price' => 10.0,
                    'pack_size' => 10,
                    'batch_number' => $batch->batch_number,
                    'expiry' => $batch->expiry_date,
                    'item_discount_percentage' => 0,
                    'sub_total' => 30.0,
                ],
            ],
        ];

        $response = $this->postJson('/api/sale-invoices', $payload);

        $response->assertStatus(201);
        $this->assertDatabaseHas('sale_invoices', [
            'customer_id' => $this->customer->id,
            'total' => 30.0,
        ]);

        // Assert stock decremented
        $product->refresh();
        $this->assertEquals(97, $product->quantity);

        $batch->refresh();
        $this->assertEquals(97, $batch->quantity);
    }
}
