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
}
