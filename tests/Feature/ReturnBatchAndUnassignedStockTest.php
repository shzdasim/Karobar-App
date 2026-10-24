<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\PurchaseReturn;
use App\Models\SaleReturn;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class ReturnBatchAndUnassignedStockTest extends TestCase
{
    use RefreshDatabase;

    private User $user;
    private Supplier $supplier;
    private Customer $customer;
    private Product $product;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        // Permissions for returns
        Permission::firstOrCreate(['name' => 'purchase-return.create', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'purchase-return.view', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'purchase-return.update', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'purchase-return.delete', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-return.create', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-return.view', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-return.update', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-return.delete', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'product.view', 'guard_name' => 'sanctum']);

        $role = Role::firstOrCreate(['name' => 'Manager', 'guard_name' => 'sanctum']);
        $role->givePermissionTo(Permission::all());

        $this->user = User::forceCreate([
            'name' => 'Manager User',
            'email' => 'manager@example.test',
            'password' => bcrypt('secret'),
        ]);
        $this->user->assignRole($role);

        $this->supplier = Supplier::create([
            'name' => 'Beta Distributors',
            'phone' => '03001112233',
        ]);

        $this->customer = Customer::create([
            'name' => 'Walkin Customer',
            'phone' => '03003334455',
        ]);

        $category = Category::create(['name' => 'General']);
        $brand = Brand::create(['name' => 'PharmaCorp']);

        // Product with 150 total stock: 100 in BATCH-A, 50 unassigned
        $this->product = Product::create([
            'name' => 'Paracetamol 500mg',
            'product_code' => 'PCM-500',
            'barcode' => '1234567890123',
            'category_id' => $category->id,
            'brand_id' => $brand->id,
            'supplier_id' => $this->supplier->id,
            'pack_size' => 10,
            'quantity' => 150,
            'unit_purchase_price' => 2,
            'pack_purchase_price' => 20,
            'unit_sale_price' => 3,
            'pack_sale_price' => 30,
            'avg_price' => 2,
            'margin' => 50,
        ]);

        Batch::create([
            'product_id' => $this->product->id,
            'batch_number' => 'BATCH-A',
            'expiry_date' => now()->addYear()->toDateString(),
            'quantity' => 100,
        ]);
    }

    public function test_batches_endpoint_returns_unassigned_stock(): void
    {
        $response = $this->actingAs($this->user)->getJson("/api/products/{$this->product->id}/batches?include_unbatched=1");
        $response->assertOk();

        $data = $response->json();
        // Should contain BATCH-A (100) and Unassigned Stock (50)
        $this->assertCount(2, $data);

        $unbatched = collect($data)->firstWhere('is_unbatched', true);
        $this->assertNotNull($unbatched);
        $this->assertEquals(50, $unbatched['quantity']);
    }

    public function test_purchase_return_unassigned_stock_decreases_total_without_affecting_batches(): void
    {
        // Return 30 units of unassigned stock (out of 50 available)
        $response = $this->actingAs($this->user)->postJson('/api/purchase-returns', [
            'supplier_id' => $this->supplier->id,
            'date' => now()->toDateString(),
            'total' => 60,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'batch' => '',
                    'expiry' => null,
                    'is_unbatched' => true,
                    'pack_size' => 10,
                    'return_pack_quantity' => 3,
                    'return_unit_quantity' => 30,
                    'pack_purchase_price' => 20,
                    'sub_total' => 60,
                ],
            ],
        ]);

        $response->assertSuccessful();

        $this->product->refresh();
        // Total was 150, returned 30 -> 120
        $this->assertEquals(120, $this->product->quantity);

        // BATCH-A was 100 -> must still be 100
        $batch = Batch::where('product_id', $this->product->id)->where('batch_number', 'BATCH-A')->first();
        $this->assertEquals(100, $batch->quantity);
    }

    public function test_purchase_return_unassigned_stock_cannot_exceed_available_unassigned(): void
    {
        // Total stock is 150, BATCH-A has 100, so unassigned available is 50.
        // Attempting to return 60 units of unassigned stock should be rejected.
        $response = $this->actingAs($this->user)->postJson('/api/purchase-returns', [
            'supplier_id' => $this->supplier->id,
            'date' => now()->toDateString(),
            'total' => 120,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'batch' => '',
                    'expiry' => null,
                    'is_unbatched' => true,
                    'pack_size' => 10,
                    'return_pack_quantity' => 6,
                    'return_unit_quantity' => 60,
                    'pack_purchase_price' => 20,
                    'sub_total' => 120,
                ],
            ],
        ]);

        $response->assertStatus(422);
        $response->assertJsonValidationErrors(['items.0.return_unit_quantity']);
    }

    public function test_purchase_return_can_return_both_batch_and_unassigned_in_different_rows(): void
    {
        // Return 20 of BATCH-A and 30 of unassigned stock
        $response = $this->actingAs($this->user)->postJson('/api/purchase-returns', [
            'supplier_id' => $this->supplier->id,
            'date' => now()->toDateString(),
            'total' => 100,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'batch' => 'BATCH-A',
                    'expiry' => now()->addYear()->toDateString(),
                    'is_unbatched' => false,
                    'pack_size' => 10,
                    'return_pack_quantity' => 2,
                    'return_unit_quantity' => 20,
                    'pack_purchase_price' => 20,
                    'sub_total' => 40,
                ],
                [
                    'product_id' => $this->product->id,
                    'batch' => '',
                    'expiry' => null,
                    'is_unbatched' => true,
                    'pack_size' => 10,
                    'return_pack_quantity' => 3,
                    'return_unit_quantity' => 30,
                    'pack_purchase_price' => 20,
                    'sub_total' => 60,
                ],
            ],
        ]);

        $response->assertSuccessful();

        $this->product->refresh();
        // Total was 150 - 50 = 100
        $this->assertEquals(100, $this->product->quantity);

        // BATCH-A was 100 - 20 = 80
        $batch = Batch::where('product_id', $this->product->id)->where('batch_number', 'BATCH-A')->first();
        $this->assertEquals(80, $batch->quantity);
    }

    public function test_sale_return_unassigned_stock_increases_total_without_affecting_batches(): void
    {
        // Customer returns 20 unassigned units
        $response = $this->actingAs($this->user)->postJson('/api/sale-returns', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'total' => 60,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'batch_number' => null,
                    'expiry' => null,
                    'is_unbatched' => true,
                    'unit_sale_quantity' => 20,
                    'unit_return_quantity' => 20,
                    'unit_sale_price' => 3,
                    'sub_total' => 60,
                ],
            ],
        ]);

        $response->assertSuccessful();

        $this->product->refresh();
        // Total was 150 + 20 = 170
        $this->assertEquals(170, $this->product->quantity);

        // BATCH-A quantity should still be 100
        $batch = Batch::where('product_id', $this->product->id)->where('batch_number', 'BATCH-A')->first();
        $this->assertEquals(100, $batch->quantity);
    }

    public function test_sale_return_with_batch_increases_batch_and_total_stock(): void
    {
        // Customer returns 15 units of BATCH-A
        $response = $this->actingAs($this->user)->postJson('/api/sale-returns', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'total' => 45,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'batch_number' => 'BATCH-A',
                    'expiry' => now()->addYear()->toDateString(),
                    'is_unbatched' => false,
                    'unit_sale_quantity' => 15,
                    'unit_return_quantity' => 15,
                    'unit_sale_price' => 3,
                    'sub_total' => 45,
                ],
            ],
        ]);

        $response->assertSuccessful();

        $this->product->refresh();
        // Total was 150 + 15 = 165
        $this->assertEquals(165, $this->product->quantity);

        // BATCH-A was 100 + 15 = 115
        $batch = Batch::where('product_id', $this->product->id)->where('batch_number', 'BATCH-A')->first();
        $this->assertEquals(115, $batch->quantity);
    }
}
