<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class PurchaseInvoiceBatchTest extends TestCase
{
    use RefreshDatabase;

    private User $user;
    private Supplier $supplier;
    private Product $product;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        // Permissions
        Permission::firstOrCreate(['name' => 'purchase-invoice.create', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'purchase-invoice.view', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'purchase-invoice.update', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'purchase-invoice.delete', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'product.view', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'product.update', 'guard_name' => 'sanctum']);

        $role = Role::firstOrCreate(['name' => 'Purchaser', 'guard_name' => 'sanctum']);
        $role->givePermissionTo(Permission::all());

        $this->user = User::forceCreate([
            'name' => 'Purchaser User',
            'email' => 'purchaser@example.test',
            'password' => bcrypt('secret'),
        ]);
        $this->user->assignRole($role);

        $this->supplier = Supplier::create([
            'name' => 'Alpha Distributors',
            'phone' => '03009998877',
            'address' => 'Industrial Area',
        ]);

        $category = Category::create(['name' => 'Antibiotics']);
        $brand = Brand::create(['name' => 'GSK']);

        $this->product = Product::create([
            'name' => 'Amoxicillin 500mg',
            'product_code' => 'AMX-500',
            'barcode' => '1234567890123',
            'category_id' => $category->id,
            'brand_id' => $brand->id,
            'supplier_id' => $this->supplier->id,
            'pack_size' => 10,
            'quantity' => 0,
            'unit_purchase_price' => 10,
            'pack_purchase_price' => 100,
            'unit_sale_price' => 15,
            'pack_sale_price' => 150,
            'avg_price' => 10,
            'margin' => 50,
        ]);
    }

    /**
     * TEST 1: Purchase invoice creates a new batch.
     */
    public function test_purchase_invoice_creates_new_batch(): void
    {
        $expiry = now()->addYear()->toDateString();

        $response = $this->actingAs($this->user)->postJson('/api/purchase-invoices', [
            'supplier_id' => $this->supplier->id,
            'invoice_number' => 'INV-NEW-BATCH-1',
            'invoice_amount' => 1000,
            'posted_date' => now()->toDateString(),
            'total_amount' => 1000,
            'total_paid' => 1000,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'pack_size' => 10,
                    'batch' => 'BATCH-NEW-01',
                    'expiry' => $expiry,
                    'pack_quantity' => 10,
                    'unit_quantity' => 100,
                    'pack_purchase_price' => 100,
                    'unit_purchase_price' => 10,
                    'pack_sale_price' => 150,
                    'unit_sale_price' => 15,
                    'margin' => 50,
                    'sub_total' => 1000,
                    'avg_price' => 10,
                    'quantity' => 100,
                ],
            ],
        ]);

        $response->assertStatus(201);

        $this->assertEquals(100, $this->product->fresh()->quantity, 'Total product stock must be 100');

        $batch = Batch::where('product_id', $this->product->id)
            ->where('batch_number', 'BATCH-NEW-01')
            ->first();

        $this->assertNotNull($batch, 'New batch must be created in database');
        $this->assertEquals(100, $batch->quantity, 'Batch quantity must be 100');
        $this->assertEquals($expiry, $batch->expiry_date);
    }

    /**
     * TEST 2: Purchase invoice adds to an existing (old) batch.
     */
    public function test_purchase_invoice_adds_to_old_batch(): void
    {
        $expiry = now()->addMonths(8)->toDateString();
        $existingBatch = Batch::create([
            'product_id' => $this->product->id,
            'batch_number' => 'BATCH-EXISTING-99',
            'expiry_date' => $expiry,
            'quantity' => 30,
        ]);
        $this->product->update(['quantity' => 30]);

        $response = $this->actingAs($this->user)->postJson('/api/purchase-invoices', [
            'supplier_id' => $this->supplier->id,
            'invoice_number' => 'INV-ADD-BATCH-2',
            'invoice_amount' => 500,
            'posted_date' => now()->toDateString(),
            'total_amount' => 500,
            'total_paid' => 500,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'pack_size' => 10,
                    'batch' => 'BATCH-EXISTING-99',
                    'expiry' => $expiry,
                    'pack_quantity' => 5,
                    'unit_quantity' => 50,
                    'pack_purchase_price' => 100,
                    'unit_purchase_price' => 10,
                    'pack_sale_price' => 150,
                    'unit_sale_price' => 15,
                    'margin' => 50,
                    'sub_total' => 500,
                    'avg_price' => 10,
                    'quantity' => 50,
                ],
            ],
        ]);

        $response->assertStatus(201);

        $this->assertEquals(80, $this->product->fresh()->quantity, 'Total product stock should be 30 + 50 = 80');
        $this->assertEquals(80, $existingBatch->fresh()->quantity, 'Existing batch should increase from 30 to 80');
    }

    /**
     * TEST 3: Purchase invoice keeps stock un-assigned (batch empty).
     */
    public function test_purchase_invoice_keeps_stock_unassigned(): void
    {
        $response = $this->actingAs($this->user)->postJson('/api/purchase-invoices', [
            'supplier_id' => $this->supplier->id,
            'invoice_number' => 'INV-UNASSIGNED-3',
            'invoice_amount' => 400,
            'posted_date' => now()->toDateString(),
            'total_amount' => 400,
            'total_paid' => 400,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'pack_size' => 10,
                    'batch' => null, // UNASSIGNED!
                    'expiry' => null,
                    'pack_quantity' => 4,
                    'unit_quantity' => 40,
                    'pack_purchase_price' => 100,
                    'unit_purchase_price' => 10,
                    'pack_sale_price' => 150,
                    'unit_sale_price' => 15,
                    'margin' => 50,
                    'sub_total' => 400,
                    'avg_price' => 10,
                    'quantity' => 40,
                ],
            ],
        ]);

        $response->assertStatus(201);

        $this->assertEquals(40, $this->product->fresh()->quantity, 'Total product stock must increase by 40');
        $this->assertEquals(0, Batch::where('product_id', $this->product->id)->count(), 'No batch records should exist');
    }

    /**
     * TEST 4: Purchase invoice with multiple rows for the same product:
     * one new batch, one existing batch, and one unassigned row.
     */
    public function test_purchase_invoice_multiple_rows_with_new_old_and_unassigned_stock(): void
    {
        $existingExpiry = now()->addMonths(6)->toDateString();
        $existingBatch = Batch::create([
            'product_id' => $this->product->id,
            'batch_number' => 'OLD-BATCH',
            'expiry_date' => $existingExpiry,
            'quantity' => 20,
        ]);
        $this->product->update(['quantity' => 20]);

        $newExpiry = now()->addMonths(18)->toDateString();

        $response = $this->actingAs($this->user)->postJson('/api/purchase-invoices', [
            'supplier_id' => $this->supplier->id,
            'invoice_number' => 'INV-MULTI-ROW-4',
            'invoice_amount' => 1000,
            'posted_date' => now()->toDateString(),
            'total_amount' => 1000,
            'total_paid' => 1000,
            'items' => [
                // Row 1: Add to old batch (30 units)
                [
                    'product_id' => $this->product->id,
                    'pack_size' => 10,
                    'batch' => 'OLD-BATCH',
                    'expiry' => $existingExpiry,
                    'pack_quantity' => 3,
                    'unit_quantity' => 30,
                    'pack_purchase_price' => 100,
                    'unit_purchase_price' => 10,
                    'pack_sale_price' => 150,
                    'unit_sale_price' => 15,
                    'margin' => 50,
                    'sub_total' => 300,
                    'avg_price' => 10,
                    'quantity' => 30,
                ],
                // Row 2: Add new batch (50 units)
                [
                    'product_id' => $this->product->id,
                    'pack_size' => 10,
                    'batch' => 'BRAND-NEW-BATCH',
                    'expiry' => $newExpiry,
                    'pack_quantity' => 5,
                    'unit_quantity' => 50,
                    'pack_purchase_price' => 100,
                    'unit_purchase_price' => 10,
                    'pack_sale_price' => 150,
                    'unit_sale_price' => 15,
                    'margin' => 50,
                    'sub_total' => 500,
                    'avg_price' => 10,
                    'quantity' => 50,
                ],
                // Row 3: Keep unassigned (20 units)
                [
                    'product_id' => $this->product->id,
                    'pack_size' => 10,
                    'batch' => null,
                    'expiry' => null,
                    'pack_quantity' => 2,
                    'unit_quantity' => 20,
                    'pack_purchase_price' => 100,
                    'unit_purchase_price' => 10,
                    'pack_sale_price' => 150,
                    'unit_sale_price' => 15,
                    'margin' => 50,
                    'sub_total' => 200,
                    'avg_price' => 10,
                    'quantity' => 20,
                ],
            ],
        ]);

        $response->assertStatus(201);

        // Overall stock: 20 initial + 30 + 50 + 20 = 120
        $this->assertEquals(120, $this->product->fresh()->quantity);

        // OLD-BATCH: 20 initial + 30 = 50
        $this->assertEquals(50, $existingBatch->fresh()->quantity);

        // BRAND-NEW-BATCH: 50
        $newBatch = Batch::where('product_id', $this->product->id)
            ->where('batch_number', 'BRAND-NEW-BATCH')
            ->first();
        $this->assertNotNull($newBatch);
        $this->assertEquals(50, $newBatch->quantity);

        // Unassigned units: total (120) - batched (50 + 50) = 20
        $totalBatched = (int) Batch::where('product_id', $this->product->id)->sum('quantity');
        $this->assertEquals(100, $totalBatched);
        $this->assertEquals(20, $this->product->fresh()->quantity - $totalBatched);
    }

    /**
     * TEST 5: Purchase invoice edit mode preserves unassigned and batched items properly.
     */
    public function test_purchase_invoice_edit_preserves_unassigned_and_batched_items(): void
    {
        $expiry = now()->addMonths(10)->toDateString();

        $createRes = $this->actingAs($this->user)->postJson('/api/purchase-invoices', [
            'supplier_id' => $this->supplier->id,
            'invoice_number' => 'INV-EDIT-TEST-5',
            'invoice_amount' => 500,
            'posted_date' => now()->toDateString(),
            'total_amount' => 500,
            'total_paid' => 500,
            'items' => [
                [
                    'product_id' => $this->product->id,
                    'pack_size' => 10,
                    'batch' => null, // unassigned
                    'expiry' => null,
                    'pack_quantity' => 5,
                    'unit_quantity' => 50,
                    'pack_purchase_price' => 100,
                    'unit_purchase_price' => 10,
                    'pack_sale_price' => 150,
                    'unit_sale_price' => 15,
                    'margin' => 50,
                    'sub_total' => 500,
                    'avg_price' => 10,
                    'quantity' => 50,
                ],
            ],
        ]);
        $createRes->assertStatus(201);
        $invoiceId = $createRes->json('id');

        // Fetch invoice in edit mode
        $showRes = $this->actingAs($this->user)->getJson("/api/purchase-invoices/{$invoiceId}");
        $showRes->assertStatus(200);
        $item = $showRes->json('items.0');
        $this->assertEmpty($item['batch']);
        $this->assertEquals(50, $item['quantity']);

        // Update the invoice (increase unassigned quantity from 50 to 70)
        $updateRes = $this->actingAs($this->user)->putJson("/api/purchase-invoices/{$invoiceId}", [
            'supplier_id' => $this->supplier->id,
            'invoice_number' => 'INV-EDIT-TEST-5',
            'invoice_amount' => 700,
            'posted_date' => now()->toDateString(),
            'total_amount' => 700,
            'total_paid' => 700,
            'items' => [
                [
                    'id' => $item['id'],
                    'product_id' => $this->product->id,
                    'pack_size' => 10,
                    'batch' => null,
                    'expiry' => null,
                    'pack_quantity' => 7,
                    'unit_quantity' => 70,
                    'pack_purchase_price' => 100,
                    'unit_purchase_price' => 10,
                    'pack_sale_price' => 150,
                    'unit_sale_price' => 15,
                    'margin' => 50,
                    'sub_total' => 700,
                    'avg_price' => 10,
                    'quantity' => 70,
                ],
            ],
        ]);
        $updateRes->assertStatus(200);

        $this->assertEquals(70, $this->product->fresh()->quantity);
        $this->assertEquals(0, Batch::where('product_id', $this->product->id)->count());
    }
}
