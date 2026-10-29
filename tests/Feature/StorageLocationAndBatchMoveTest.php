<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Product;
use App\Models\StorageLocation;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class StorageLocationAndBatchMoveTest extends TestCase
{
    use RefreshDatabase;

    private User $admin;
    private Product $product;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $role = Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        $this->admin = User::create([
            'name'     => 'Test Admin',
            'email'    => 'admin@test.com',
            'password' => bcrypt('password'),
            'status'   => 'active',
        ]);
        $this->admin->assignRole($role);

        $category = Category::create(['name' => 'Tablets']);
        $brand = Brand::create(['name' => 'GSK']);
        $supplier = Supplier::create(['name' => 'Premier Distributors']);

        $shopLocation = StorageLocation::where('code', 'SHOP')->first();

        $this->product = Product::create([
            'product_code'        => 'PRD-0001',
            'barcode'             => '1234567890123',
            'name'                => 'Panadol Advance 500mg',
            'pack_size'           => 20,
            'quantity'            => 200,
            'unit_purchase_price' => 8.00,
            'pack_purchase_price' => 160.00,
            'unit_sale_price'     => 10.00,
            'pack_sale_price'     => 200.00,
            'narcotic'            => 'no',
            'category_id'         => $category->id,
            'brand_id'            => $brand->id,
            'supplier_id'         => $supplier->id,
            'storage_location_id' => $shopLocation?->id,
            'rack'                => 'R-01',
        ]);
    }

    public function test_default_storage_locations_are_seeded_and_can_be_managed(): void
    {
        $res = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->getJson('/api/storage-locations');

        $res->assertOk();
        $codes = collect($res->json())->pluck('code')->all();
        $this->assertContains('SHOP', $codes);
        $this->assertContains('GDN', $codes);
        $this->assertContains('FRIDGE', $codes);

        // Create a custom location
        $createRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson('/api/storage-locations', [
                'name'         => 'Upper Floor Godown',
                'code'         => 'GDN-2',
                'type'         => 'godown',
                'default_rack' => 'Pallet-A',
            ]);

        $createRes->assertStatus(201)
            ->assertJsonPath('code', 'GDN-2')
            ->assertJsonPath('type', 'godown');
    }

    public function test_batch_can_be_split_and_merged_across_godown_and_shop_counter(): void
    {
        $godown = StorageLocation::where('code', 'GDN')->firstOrFail();
        $shop = StorageLocation::where('code', 'SHOP')->firstOrFail();

        // 1. Create a batch of 200 units in Main Godown
        $createRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$this->product->id}/batches", [
                'batch_number'        => 'BATCH-900',
                'expiry_date'         => '2028-12-31',
                'quantity'            => 200,
                'storage_location_id' => $godown->id,
                'rack'                => 'Pallet-1',
                'unit_sale_price'     => 12.50,
                'unit_purchase_price' => 9.50,
            ]);

        $createRes->assertStatus(201);
        $godownBatchId = $createRes->json('id');

        // 2. Move 60 units from Main Godown to Shop Counter (Partial Split)
        $moveRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/batches/{$godownBatchId}/move-location", [
                'to_storage_location_id' => $shop->id,
                'quantity'               => 60,
                'rack'                   => 'Counter-Shelf-A',
            ]);

        $moveRes->assertOk();
        $this->assertEquals(140, $moveRes->json('source_batch.quantity'));
        $this->assertEquals(60, $moveRes->json('target_batch.quantity'));
        $this->assertEquals($shop->id, $moveRes->json('target_batch.storage_location_id'));
        $this->assertEquals('Counter-Shelf-A', $moveRes->json('target_batch.rack'));
        $this->assertEquals(12.50, (float) $moveRes->json('target_batch.unit_sale_price'));

        // Total product stock remains 200
        $this->assertEquals(200, (int) $this->product->fresh()->quantity);
        $this->assertEquals(200, (int) Batch::where('product_id', $this->product->id)->sum('quantity'));

        // Verify SaleStockAvailability validates selling 100 units across split locations (60 Shop + 140 Godown)
        app(\App\Services\SaleStockAvailability::class)->validate([
            [
                'product_id'   => $this->product->id,
                'batch_number' => 'BATCH-900',
                'expiry'       => '2028-12-31',
                'quantity'     => 100,
            ],
        ]);

        // 3. Move remaining 140 units from Godown to Shop Counter (Merges into existing Shop batch!)
        $mergeRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/batches/{$godownBatchId}/move-location", [
                'to_storage_location_id' => $shop->id,
                'quantity'               => 140,
                'rack'                   => 'Counter-Shelf-A',
            ]);

        $mergeRes->assertOk();
        $this->assertNull($mergeRes->json('source_batch'));
        $this->assertEquals(200, $mergeRes->json('target_batch.quantity'));
        $this->assertEquals(1, Batch::where('product_id', $this->product->id)->count());
    }

    public function test_cannot_create_batch_when_product_has_zero_stock_or_zero_unassigned_stock(): void
    {
        $zeroStockProduct = Product::create([
            'product_code'        => 'PRD-ZERO',
            'barcode'             => '9999999999999',
            'name'                => 'Zero Stock Syrup',
            'pack_size'           => 1,
            'quantity'            => 0,
            'unit_purchase_price' => 50.00,
            'pack_purchase_price' => 50.00,
            'unit_sale_price'     => 70.00,
            'pack_sale_price'     => 70.00,
            'narcotic'            => 'no',
            'category_id'         => $this->product->category_id,
            'brand_id'            => $this->product->brand_id,
            'supplier_id'         => $this->product->supplier_id,
        ]);

        // 1. Attempting to add 50 units to a batch when product has 0 stock must be rejected (422)
        // even if caller passes allow_stock_increase = true
        $res1 = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$zeroStockProduct->id}/batches", [
                'batch_number'         => 'B-GHOST-1',
                'expiry_date'          => '2028-06-30',
                'quantity'             => 50,
                'allow_stock_increase' => true,
            ]);

        $res1->assertStatus(422)
            ->assertJsonValidationErrors(['quantity']);

        // 2. Attempting to add a 0-quantity batch when product has 0 unassigned stock must also be rejected (422)
        $res2 = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$zeroStockProduct->id}/batches", [
                'batch_number' => 'B-GHOST-2',
                'expiry_date'  => '2028-06-30',
                'quantity'     => 0,
            ]);

        $res2->assertStatus(422)
            ->assertJsonValidationErrors(['quantity']);

        // Verify product stock was NEVER inflated and no phantom batch was created
        $this->assertEquals(0, (int) $zeroStockProduct->fresh()->quantity);
        $this->assertEquals(0, Batch::where('product_id', $zeroStockProduct->id)->count());
    }

    public function test_batch_creation_and_update_strictly_enforces_unassigned_stock_and_never_inflates_product_quantity(): void
    {
        // Product starts with 200 total stock, 0 batched, 200 unassigned
        $this->assertEquals(200, (int) $this->product->quantity);

        // 1. Attempting to create BATCH-A with 201 units (> 200 unassigned) fails with 422
        $overRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$this->product->id}/batches", [
                'batch_number'         => 'BATCH-A',
                'expiry_date'          => '2027-12-31',
                'quantity'             => 201,
                'allow_stock_increase' => true,
            ]);

        $overRes->assertStatus(422)
            ->assertJsonValidationErrors(['quantity']);
        $this->assertEquals(200, (int) $this->product->fresh()->quantity);
        $this->assertEquals(0, Batch::where('product_id', $this->product->id)->count());

        // 2. Creating BATCH-A with 120 units succeeds (leaves 80 unassigned)
        $batchARes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$this->product->id}/batches", [
                'batch_number' => 'BATCH-A',
                'expiry_date'  => '2027-12-31',
                'quantity'     => 120,
            ]);

        $batchARes->assertStatus(201);
        $batchAId = $batchARes->json('id');
        $this->assertEquals(200, (int) $this->product->fresh()->quantity);

        // Check headers on GET /api/products/{id}/batches
        $listAfterA = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->getJson("/api/products/{$this->product->id}/batches");
        $listAfterA->assertOk()
            ->assertHeader('X-Total-Units', '200')
            ->assertHeader('X-Batched-Units', '120')
            ->assertHeader('X-Unbatched-Units', '80');

        // 3. Attempting to create BATCH-B with 90 units (> 80 remaining unassigned) fails with 422
        $overBatchBRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$this->product->id}/batches", [
                'batch_number' => 'BATCH-B',
                'expiry_date'  => '2028-03-31',
                'quantity'     => 90,
            ]);

        $overBatchBRes->assertStatus(422)
            ->assertJsonValidationErrors(['quantity']);
        $this->assertEquals(200, (int) $this->product->fresh()->quantity);

        // 4. Creating BATCH-B with exactly 80 units succeeds (leaves 0 unassigned)
        $batchBRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$this->product->id}/batches", [
                'batch_number' => 'BATCH-B',
                'expiry_date'  => '2028-03-31',
                'quantity'     => 80,
            ]);

        $batchBRes->assertStatus(201);
        $batchBId = $batchBRes->json('id');
        $this->assertEquals(200, (int) $this->product->fresh()->quantity);

        // 5. Now unassigned is 0: attempting to create BATCH-C with 10 units or 0 units fails with 422
        $batchCRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$this->product->id}/batches", [
                'batch_number' => 'BATCH-C',
                'expiry_date'  => '2028-09-30',
                'quantity'     => 10,
            ]);
        $batchCRes->assertStatus(422)
            ->assertJsonValidationErrors(['quantity']);

        $batchCZeroRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->postJson("/api/products/{$this->product->id}/batches", [
                'batch_number' => 'BATCH-C',
                'expiry_date'  => '2028-09-30',
                'quantity'     => 0,
            ]);
        $batchCZeroRes->assertStatus(422)
            ->assertJsonValidationErrors(['quantity']);

        // 6. Attempting to increase BATCH-B from 80 to 81 via PUT when unassigned is 0 fails with 422
        $updateOverRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->putJson("/api/batches/{$batchBId}", [
                'batch_number' => 'BATCH-B',
                'expiry_date'  => '2028-03-31',
                'quantity'     => 81,
            ]);
        $updateOverRes->assertStatus(422)
            ->assertJsonValidationErrors(['quantity']);
        $this->assertEquals(200, (int) $this->product->fresh()->quantity);

        // 7. Updating BATCH-B metadata (expiry, rack, price) while keeping quantity = 80 succeeds
        $updateMetaRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->putJson("/api/batches/{$batchBId}", [
                'batch_number'    => 'BATCH-B',
                'expiry_date'     => '2028-05-31',
                'quantity'        => 80,
                'rack'            => 'R-99',
                'unit_sale_price' => 14.00,
            ]);
        $updateMetaRes->assertOk()
            ->assertJsonPath('rack', 'R-99')
            ->assertJsonPath('quantity', 80);

        // 8. Reducing BATCH-B from 80 to 50 releases 30 units back into unassigned stock
        $reduceRes = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->putJson("/api/batches/{$batchBId}", [
                'batch_number' => 'BATCH-B',
                'expiry_date'  => '2028-05-31',
                'quantity'     => 50,
            ]);
        $reduceRes->assertOk()
            ->assertJsonPath('quantity', 50);
        $this->assertEquals(200, (int) $this->product->fresh()->quantity);

        // 9. Now BATCH-A (currently 120) can be increased up to 150 (120 + 30 unassigned), but 151 fails
        $increaseAOver = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->putJson("/api/batches/{$batchAId}", [
                'batch_number' => 'BATCH-A',
                'expiry_date'  => '2027-12-31',
                'quantity'     => 151,
            ]);
        $increaseAOver->assertStatus(422)
            ->assertJsonValidationErrors(['quantity']);

        $increaseAOk = $this->actingAs($this->admin, 'sanctum')
            ->withoutMiddleware(['licensed'])
            ->putJson("/api/batches/{$batchAId}", [
                'batch_number' => 'BATCH-A',
                'expiry_date'  => '2027-12-31',
                'quantity'     => 150,
            ]);
        $increaseAOk->assertOk()
            ->assertJsonPath('quantity', 150);

        // Total stock is still strictly 200 (150 in BATCH-A + 50 in BATCH-B = 200)
        $this->assertEquals(200, (int) $this->product->fresh()->quantity);
        $this->assertEquals(200, (int) Batch::where('product_id', $this->product->id)->sum('quantity'));
    }
}
