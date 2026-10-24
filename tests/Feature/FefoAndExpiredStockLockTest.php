<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Customer;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class FefoAndExpiredStockLockTest extends TestCase
{
    use RefreshDatabase;

    private User $adminUser;
    private User $cashierUser;
    private Customer $customer;
    private Product $pharmaProduct;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        // Setup permissions and roles
        Permission::firstOrCreate(['name' => 'sale-invoice.create', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-invoice.update', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-invoice.view', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-invoice.expired-override', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'product.view', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'product.update', 'guard_name' => 'sanctum']);

        $adminRole = Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        $adminRole->givePermissionTo(Permission::all());

        $cashierRole = Role::firstOrCreate(['name' => 'Cashier', 'guard_name' => 'sanctum']);
        $cashierRole->givePermissionTo(['sale-invoice.create', 'sale-invoice.update', 'sale-invoice.view', 'product.view', 'product.update']);

        $this->adminUser = User::forceCreate([
            'name' => 'Admin Supervisor',
            'email' => 'admin_fefo@example.test',
            'password' => bcrypt('secret'),
        ]);
        $this->adminUser->assignRole($adminRole);

        $this->cashierUser = User::forceCreate([
            'name' => 'Cashier Regular',
            'email' => 'cashier_fefo@example.test',
            'password' => bcrypt('secret'),
        ]);
        $this->cashierUser->assignRole($cashierRole);

        $this->customer = Customer::create([
            'name' => 'Walk-in Patient',
            'phone' => '03001112233',
            'address' => 'City Hospital Road',
            'credit_limit' => 50000,
        ]);

        $supplier = Supplier::create([
            'name' => 'Glaxo Pharma',
            'phone' => '03009998877',
            'address' => 'Industrial Zone',
        ]);

        $categoryId = DB::table('categories')->insertGetId([
            'name' => 'Antibiotics',
            'created_at' => now(),
            'updated_at' => now(),
        ]);

        $brandId = DB::table('brands')->insertGetId([
            'name' => 'GSK Global',
            'created_at' => now(),
            'updated_at' => now(),
        ]);

        $this->pharmaProduct = Product::create([
            'category_id' => $categoryId,
            'brand_id' => $brandId,
            'supplier_id' => $supplier->id,
            'product_code' => 'AUG-625',
            'name' => 'Augmentin 625mg',
            'pack_size' => 10,
            'quantity' => 100,
            'unit_purchase_price' => 150,
            'unit_sale_price' => 200,
            'barcode' => '896400099901',
        ]);
    }

    /**
     * TEST 1: Batches endpoint returns batches in strict FEFO order.
     * Earliest non-expired batch with positive stock is marked as is_fefo_recommended = true.
     */
    public function test_batches_returned_in_fefo_order_with_recommendation(): void
    {
        // 1. Expired batch (in the past)
        $expiredBatch = Batch::create([
            'product_id' => $this->pharmaProduct->id,
            'batch_number' => 'LOT-EXPIRED',
            'expiry_date' => now()->subMonths(2)->toDateString(),
            'quantity' => 20,
        ]);

        // 2. Further future batch
        $farFutureBatch = Batch::create([
            'product_id' => $this->pharmaProduct->id,
            'batch_number' => 'LOT-FAR',
            'expiry_date' => now()->addMonths(12)->toDateString(),
            'quantity' => 30,
        ]);

        // 3. Near future batch (earliest unexpired)
        $fefoBatch = Batch::create([
            'product_id' => $this->pharmaProduct->id,
            'batch_number' => 'LOT-FEFO-EARLY',
            'expiry_date' => now()->addMonths(2)->toDateString(),
            'quantity' => 50,
        ]);

        $response = $this->actingAs($this->cashierUser)->getJson("/api/products/{$this->pharmaProduct->id}/batches");
        $response->assertStatus(200);

        $batches = $response->json();
        $this->assertCount(3, $batches);

        // FEFO order: earliest non-expired batch first
        $this->assertEquals('LOT-FEFO-EARLY', $batches[0]['batch_number']);
        $this->assertTrue($batches[0]['is_fefo_recommended']);
        $this->assertFalse($batches[0]['is_expired']);

        // Second: far future batch
        $this->assertEquals('LOT-FAR', $batches[1]['batch_number']);
        $this->assertFalse($batches[1]['is_fefo_recommended']);
        $this->assertFalse($batches[1]['is_expired']);

        // Last: expired batch
        $this->assertEquals('LOT-EXPIRED', $batches[2]['batch_number']);
        $this->assertTrue($batches[2]['is_expired']);
    }

    /**
     * TEST 2: Two different batches CAN have the same expiry date.
     */
    public function test_two_different_batches_can_have_same_expiry_date(): void
    {
        $sharedExpiry = now()->addMonths(6)->toDateString();

        // Add Batch 1
        $res1 = $this->actingAs($this->adminUser)->postJson("/api/products/{$this->pharmaProduct->id}/batches", [
            'batch_number' => 'BATCH-ALPHA',
            'expiry_date' => $sharedExpiry,
            'quantity' => 40,
        ]);
        $res1->assertStatus(201);

        // Add Batch 2 with the EXACT same expiry date
        $res2 = $this->actingAs($this->adminUser)->postJson("/api/products/{$this->pharmaProduct->id}/batches", [
            'batch_number' => 'BATCH-BETA',
            'expiry_date' => $sharedExpiry,
            'quantity' => 60,
        ]);
        $res2->assertStatus(201);

        // Verify both batches exist independently with the same expiry
        $this->assertEquals(2, Batch::where('product_id', $this->pharmaProduct->id)->where('expiry_date', $sharedExpiry)->count());

        // Product total quantity must equal the sum of both batches (40 + 60 = 100)
        $this->assertEquals(100, $this->pharmaProduct->fresh()->quantity);
    }

    /**
     * TEST 3: Product edit allows adding, updating, and removing batches.
     */
    public function test_product_edit_allows_adding_and_updating_batches(): void
    {
        $expiry = now()->addMonths(8)->toDateString();

        // 1. Add batch
        $createRes = $this->actingAs($this->adminUser)->postJson("/api/products/{$this->pharmaProduct->id}/batches", [
            'batch_number' => 'EDIT-BATCH-1',
            'expiry_date' => $expiry,
            'quantity' => 50,
        ]);
        $createRes->assertStatus(201);
        $batchId = $createRes->json('id');

        // 2. Update batch quantity and expiry
        $newExpiry = now()->addMonths(9)->toDateString();
        $updateRes = $this->actingAs($this->adminUser)->putJson("/api/batches/{$batchId}", [
            'batch_number' => 'EDIT-BATCH-1-RENAMED',
            'expiry_date' => $newExpiry,
            'quantity' => 75,
        ]);
        $updateRes->assertStatus(200);

        $freshBatch = Batch::find($batchId);
        $this->assertEquals('EDIT-BATCH-1-RENAMED', $freshBatch->batch_number);
        $this->assertEquals($newExpiry, $freshBatch->expiry_date);
        $this->assertEquals(75, $freshBatch->quantity);
        // Product total on-hand remains 100 (75 batched + 25 unassigned)
        $this->assertEquals(100, $this->pharmaProduct->fresh()->quantity);

        // 3. Delete batch - returns units to unassigned pool, preserving total product stock
        $deleteRes = $this->actingAs($this->adminUser)->deleteJson("/api/batches/{$batchId}");
        $deleteRes->assertStatus(200);
        $this->assertNull(Batch::find($batchId));
        $this->assertEquals(100, $this->pharmaProduct->fresh()->quantity);
    }

    /**
     * TEST 4: Expired stock selling is locked and rejected with HTTP 422 requires_expired_override.
     */
    public function test_expired_stock_sale_is_locked_and_rejected(): void
    {
        $expiredDate = now()->subDays(10)->toDateString();
        $expiredBatch = Batch::create([
            'product_id' => $this->pharmaProduct->id,
            'batch_number' => 'DEAD-BATCH-99',
            'expiry_date' => $expiredDate,
            'quantity' => 50,
        ]);

        $payload = [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 2,
                    'price' => 200,
                    'batch_number' => 'DEAD-BATCH-99',
                    'expiry' => $expiredDate,
                ],
            ],
        ];

        $response = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', $payload);

        $response->assertStatus(422);
        $response->assertJson([
            'requires_expired_override' => true,
            'batch_number' => 'DEAD-BATCH-99',
        ]);
        $this->assertEquals(0, SaleInvoice::count());
    }

    /**
     * TEST 5: Authorized supervisor can override expired stock lock.
     */
    public function test_authorized_supervisor_can_override_expired_stock_sale(): void
    {
        $expiredDate = now()->subDays(5)->toDateString();
        $expiredBatch = Batch::create([
            'product_id' => $this->pharmaProduct->id,
            'batch_number' => 'AUTHORIZED-EXPIRED',
            'expiry_date' => $expiredDate,
            'quantity' => 50,
        ]);

        $payload = [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'expired_override' => true, // Supervisor override requested
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 5,
                    'price' => 200,
                    'batch_number' => 'AUTHORIZED-EXPIRED',
                    'expiry' => $expiredDate,
                ],
            ],
        ];

        // Admin has permission to override expired stock
        $response = $this->actingAs($this->adminUser)->postJson('/api/sale-invoices', $payload);
        $response->assertStatus(201);

        $this->assertEquals(1, SaleInvoice::count());
        $this->assertEquals(45, $expiredBatch->fresh()->quantity);
    }

    /**
     * TEST 6: Unauthorized cashier cannot override expired stock lock (HTTP 403 Forbidden).
     */
    public function test_unauthorized_user_cannot_override_expired_stock_sale(): void
    {
        $expiredDate = now()->subDays(5)->toDateString();
        Batch::create([
            'product_id' => $this->pharmaProduct->id,
            'batch_number' => 'BLOCKED-EXPIRED',
            'expiry_date' => $expiredDate,
            'quantity' => 50,
        ]);

        $payload = [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'expired_override' => true, // Unauthorized override attempt
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 5,
                    'price' => 200,
                    'batch_number' => 'BLOCKED-EXPIRED',
                    'expiry' => $expiredDate,
                ],
            ],
        ];

        // Cashier does not have 'sale-invoice.expired-override' permission
        $response = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', $payload);
        $response->assertStatus(403);
        $this->assertStringContainsString('do not have permission to sell expired stock', $response->json('message'));

        $this->assertEquals(0, SaleInvoice::count());
    }

    /**
     * TEST 7: Batch assignment takes quantity from available unassigned stock in product edit.
     * Example: Product A has 50 units. We add batch 43564 with quantity 30.
     * It takes 30 from available unassigned stock, keeping product total at 50 (30 batched, 20 unassigned).
     */
    public function test_batch_assignment_takes_quantity_from_available_unassigned_stock(): void
    {
        $this->pharmaProduct->update(['quantity' => 50]);

        $expiry = now()->addMonths(10)->toDateString();

        // 1. Add batch 43564 with quantity 30 from the 50 on-hand units
        $res = $this->actingAs($this->adminUser)->postJson("/api/products/{$this->pharmaProduct->id}/batches", [
            'batch_number' => '43564',
            'expiry_date' => $expiry,
            'quantity' => 30,
        ]);
        $res->assertStatus(201);

        $product = $this->pharmaProduct->fresh();
        $this->assertEquals(50, $product->quantity, 'Overall product quantity must remain 50');

        // Check batches listing with include_unbatched=1
        $listRes = $this->actingAs($this->cashierUser)->getJson("/api/products/{$this->pharmaProduct->id}/batches?include_unbatched=1");
        $listRes->assertStatus(200);
        $listRes->assertHeader('X-Total-Units', '50');
        $listRes->assertHeader('X-Batched-Units', '30');
        $listRes->assertHeader('X-Unbatched-Units', '20');

        $data = $listRes->json();
        $this->assertCount(2, $data);
        $this->assertEquals('43564', $data[0]['batch_number']);
        $this->assertEquals(30, $data[0]['quantity']);
        $this->assertTrue($data[1]['is_unbatched']);
        $this->assertEquals(20, $data[1]['quantity']);

        // 2. Reject adding more than available unassigned units (trying to add 25 when only 20 unassigned remain)
        $excessRes = $this->actingAs($this->adminUser)->postJson("/api/products/{$this->pharmaProduct->id}/batches", [
            'batch_number' => 'OVERFLOW-BATCH',
            'expiry_date' => $expiry,
            'quantity' => 25,
        ]);
        $excessRes->assertStatus(422);
        $excessRes->assertJsonValidationErrors(['quantity']);

        // 3. Deleting batch 43564 releases 30 units back to unassigned pool; product total remains 50
        $batchId = $res->json('id');
        $delRes = $this->actingAs($this->adminUser)->deleteJson("/api/batches/{$batchId}");
        $delRes->assertStatus(200);

        $productAfterDel = $this->pharmaProduct->fresh();
        $this->assertEquals(50, $productAfterDel->quantity, 'Product quantity remains 50 after batch deletion');

        $listAfterDel = $this->actingAs($this->cashierUser)->getJson("/api/products/{$this->pharmaProduct->id}/batches?include_unbatched=1");
        $listAfterDel->assertHeader('X-Batched-Units', '0');
        $listAfterDel->assertHeader('X-Unbatched-Units', '50');
    }

    /**
     * TEST 8: Sale invoice allows user to selectively sell from a specific batch OR unbatched stock.
     * Example: Product A has 30 batched and 20 unbatched units (total 50).
     */
    public function test_sale_invoice_user_can_select_between_batched_and_unbatched_stock(): void
    {
        $this->pharmaProduct->update(['quantity' => 50]);

        $expiry = now()->addMonths(6)->toDateString();
        $batch = Batch::create([
            'product_id' => $this->pharmaProduct->id,
            'batch_number' => 'LOT-30',
            'expiry_date' => $expiry,
            'quantity' => 30,
        ]);

        // Verify availableQuantity endpoint for batched vs unbatched
        $availBatchRes = $this->actingAs($this->cashierUser)->getJson("/api/products/available-quantity?product_id={$this->pharmaProduct->id}&batch=LOT-30");
        $availBatchRes->assertStatus(200);
        $this->assertEquals(30, $availBatchRes->json('available_units'));

        $availUnbatchedRes = $this->actingAs($this->cashierUser)->getJson("/api/products/available-quantity?product_id={$this->pharmaProduct->id}&batch=");
        $availUnbatchedRes->assertStatus(200);
        $this->assertEquals(20, $availUnbatchedRes->json('available_units'));
        $this->assertEquals(20, $availUnbatchedRes->json('unbatched_units'));

        // 1. Sell 10 units specifically from batch LOT-30
        $sale1 = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 10,
                    'price' => 200,
                    'batch_number' => 'LOT-30',
                    'expiry' => $expiry,
                ],
            ],
        ]);
        $sale1->assertStatus(201);

        $this->assertEquals(20, $batch->fresh()->quantity, 'Batch LOT-30 should now have 20 units');
        $this->assertEquals(40, $this->pharmaProduct->fresh()->quantity, 'Product total stock should now be 40 units');

        // 2. Sell 15 units from UNBATCHED stock (batch_number empty)
        $sale2 = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 15,
                    'price' => 200,
                    'batch_number' => '', // Selling unbatched stock
                    'expiry' => '',
                ],
            ],
        ]);
        $sale2->assertStatus(201);

        $this->assertEquals(20, $batch->fresh()->quantity, 'Batch LOT-30 should remain at 20 units');
        $this->assertEquals(25, $this->pharmaProduct->fresh()->quantity, 'Product total stock should now be 25 units (20 batched, 5 unbatched)');

        // 3. Attempt to sell 10 unbatched units when only 5 unbatched remain -> must fail with validation error
        $sale3 = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 10,
                    'price' => 200,
                    'batch_number' => '', // Only 5 unbatched available!
                    'expiry' => '',
                ],
            ],
        ]);
        $sale3->assertStatus(422);
        $this->assertStringContainsString('Insufficient unbatched stock', json_encode($sale3->json()));

        // 4. Selling from the remaining 20 units in batch LOT-30 succeeds
        $sale4 = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 20,
                    'price' => 200,
                    'batch_number' => 'LOT-30',
                    'expiry' => $expiry,
                ],
            ],
        ]);
        $sale4->assertStatus(201);

        $this->assertEquals(0, $batch->fresh()->quantity, 'Batch LOT-30 is now 0 units');
        $this->assertEquals(5, $this->pharmaProduct->fresh()->quantity, 'Product total stock is 5 units (5 unbatched remaining)');
    }

    /**
     * TEST 9: Sale invoice allows adding the same product on multiple rows ONLY IF
     * batches are different and unassigned stock is used only one time.
     * Example: Product A has 5 batches and unassigned stock. User sells from 2 batches and unassigned stock.
     */
    public function test_same_product_multiple_rows_allowed_with_distinct_batches_and_single_unassigned(): void
    {
        $this->pharmaProduct->update(['quantity' => 100]);

        $b1 = Batch::create(['product_id' => $this->pharmaProduct->id, 'batch_number' => 'B-1', 'expiry_date' => now()->addMonths(2)->toDateString(), 'quantity' => 20]);
        $b2 = Batch::create(['product_id' => $this->pharmaProduct->id, 'batch_number' => 'B-2', 'expiry_date' => now()->addMonths(4)->toDateString(), 'quantity' => 20]);
        $b3 = Batch::create(['product_id' => $this->pharmaProduct->id, 'batch_number' => 'B-3', 'expiry_date' => now()->addMonths(6)->toDateString(), 'quantity' => 20]);
        $b4 = Batch::create(['product_id' => $this->pharmaProduct->id, 'batch_number' => 'B-4', 'expiry_date' => now()->addMonths(8)->toDateString(), 'quantity' => 10]);
        $b5 = Batch::create(['product_id' => $this->pharmaProduct->id, 'batch_number' => 'B-5', 'expiry_date' => now()->addMonths(10)->toDateString(), 'quantity' => 10]);
        // Total batched = 80, unassigned = 20 (Total = 100)

        // 1. Success: 2 different batches + unassigned stock on 3 separate rows
        $sale = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 10,
                    'price' => 200,
                    'batch_number' => 'B-1',
                    'expiry' => $b1->expiry_date,
                ],
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 15,
                    'price' => 200,
                    'batch_number' => 'B-2',
                    'expiry' => $b2->expiry_date,
                ],
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 10,
                    'price' => 200,
                    'batch_number' => '', // Unassigned stock
                    'expiry' => '',
                ],
            ],
        ]);
        $sale->assertStatus(201);

        $this->assertEquals(10, $b1->fresh()->quantity, 'B-1 drops from 20 to 10');
        $this->assertEquals(5, $b2->fresh()->quantity, 'B-2 drops from 20 to 5');
        // Total product quantity should be 100 - (10 + 15 + 10) = 65
        $this->assertEquals(65, $this->pharmaProduct->fresh()->quantity);

        // 2. Reject duplicate batch for the same product
        $dupBatchSale = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 2,
                    'price' => 200,
                    'batch_number' => 'B-3',
                    'expiry' => $b3->expiry_date,
                ],
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 3,
                    'price' => 200,
                    'batch_number' => 'B-3', // DUPLICATE BATCH B-3!
                    'expiry' => $b3->expiry_date,
                ],
            ],
        ]);
        $dupBatchSale->assertStatus(422);
        $dupBatchSale->assertJsonValidationErrors(['items.1.batch_number']);

        // 3. Reject duplicate unassigned stock for the same product
        $dupUnassignedSale = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 2,
                    'price' => 200,
                    'batch_number' => '', // First unassigned row
                    'expiry' => '',
                ],
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 3,
                    'price' => 200,
                    'batch_number' => '', // Second unassigned row!
                    'expiry' => '',
                ],
            ],
        ]);
        $dupUnassignedSale->assertStatus(422);
        $dupUnassignedSale->assertJsonValidationErrors(['items.1.quantity']);
    }

    /**
     * TEST 10: Sale invoice in edit mode remembers unassigned stock and allows clean update.
     */
    public function test_sale_invoice_edit_mode_remembers_unassigned_stock_and_allows_update(): void
    {
        $this->pharmaProduct->update(['quantity' => 100]);

        $batch = Batch::create([
            'product_id' => $this->pharmaProduct->id,
            'batch_number' => 'BATCH-50',
            'expiry_date' => now()->addYear()->toDateString(),
            'quantity' => 50,
        ]);

        // Sell 20 units from unassigned stock
        $createRes = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', [
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 20,
                    'price' => 150,
                    'batch_number' => '', // unassigned stock
                ],
            ],
        ]);
        $createRes->assertStatus(201);
        $invoiceId = $createRes->json('id');
        $postedNumber = $createRes->json('posted_number');

        // Fetch invoice in edit mode
        $showRes = $this->actingAs($this->cashierUser)->getJson("/api/sale-invoices/{$invoiceId}");
        $showRes->assertStatus(200);
        $item = $showRes->json('items.0');
        $this->assertEmpty($item['batch_number']);
        $this->assertEquals(20, $item['quantity']);

        // Update the invoice (e.g. adjust quantity to 25 or save as is)
        $updateRes = $this->actingAs($this->cashierUser)->putJson("/api/sale-invoices/{$invoiceId}", [
            'posted_number' => $postedNumber,
            'customer_id' => $this->customer->id,
            'date' => now()->toDateString(),
            'items' => [
                [
                    'id' => $item['id'],
                    'product_id' => $this->pharmaProduct->id,
                    'quantity' => 25,
                    'price' => 150,
                    'batch_number' => null, // preserves unassigned
                ],
            ],
        ]);
        $updateRes->assertStatus(200);

        // Batch should still be 50, total product stock should now be 75 (100 - 25)
        $this->assertEquals(50, $batch->fresh()->quantity);
        $this->assertEquals(75, $this->pharmaProduct->fresh()->quantity);
    }
}

