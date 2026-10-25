<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Product;
use App\Models\StocktakeSession;
use App\Models\StocktakeItem;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class StocktakeSessionTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;
    protected int $categoryId;
    protected int $brandId;
    protected int $supplierId;
    protected static int $productCounter = 0;

    protected function setUp(): void
    {
        parent::setUp();

        // Bypass all authorization for tests
        Gate::before(fn () => true);

        // Bypass license middleware
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $this->user = User::forceCreate([
            'name'     => 'Stocktake Tester',
            'email'    => 'stocktake@karobar.test',
            'password' => bcrypt('password'),
        ]);
        $this->actingAs($this->user, 'sanctum');

        $this->categoryId = Category::create(['name' => 'Test Category'])->id;
        $this->brandId    = Brand::create(['name' => 'Test Brand'])->id;
        $this->supplierId = Supplier::create(['name' => 'Test Supplier'])->id;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private function makeProduct(array $attrs = []): Product
    {
        static $counter = 0;
        $counter++;

        // Ensure required FK rows exist
        $categoryId = Category::first()?->id ?? Category::create(['name' => 'Cat-' . $counter])->id;
        $brandId    = Brand::first()?->id    ?? Brand::create(['name' => 'Brand-' . $counter])->id;
        $supplierId = Supplier::first()?->id ?? Supplier::create(['name' => 'Sup-' . $counter])->id;

        return Product::create(array_merge([
            'name'                 => 'Test Product ' . $counter,
            'product_code'         => 'TP-' . $counter,
            'barcode'              => 'BC-' . $counter,
            'pack_size'            => 1,
            'quantity'             => 100,
            'unit_purchase_price'  => 10.00,
            'unit_sale_price'      => 15.00,
            'avg_price'            => 10.00,
            'category_id'          => $categoryId,
            'brand_id'             => $brandId,
            'supplier_id'          => $supplierId,
        ], $attrs));
    }

    private function makeProductWithBatch(int $batchQty = 50): array
    {
        $product = $this->makeProduct(['quantity' => $batchQty]);
        $batch = Batch::create([
            'product_id'   => $product->id,
            'batch_number' => 'BATCH-001',
            'expiry_date'  => now()->addYear()->toDateString(),
            'quantity'     => $batchQty,
        ]);
        return [$product, $batch];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. List sessions
    // ──────────────────────────────────────────────────────────────────────────

    public function test_can_list_stocktake_sessions(): void
    {
        StocktakeSession::create([
            'reference_no' => 'ST-202610-001',
            'session_type' => 'full',
            'is_blind'     => true,
            'status'       => 'draft',
            'user_id'      => $this->user->id,
        ]);

        $res = $this->getJson('/api/stocktake-sessions');

        $res->assertOk();
        $this->assertNotEmpty($res->json());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Generate new reference number
    // ──────────────────────────────────────────────────────────────────────────

    public function test_can_generate_new_reference_number(): void
    {
        $res = $this->getJson('/api/stocktake-sessions/new-code');

        $res->assertOk()
            ->assertJsonStructure(['reference_no']);

        $this->assertStringStartsWith('ST-', $res->json('reference_no'));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Create full stocktake session
    // ──────────────────────────────────────────────────────────────────────────

    public function test_can_create_full_stocktake_session(): void
    {
        $res = $this->postJson('/api/stocktake-sessions', [
            'session_type' => 'full',
            'is_blind'     => true,
            'notes'        => 'Monthly full count',
        ]);

        $res->assertCreated()
            ->assertJsonFragment(['session_type' => 'full', 'status' => 'draft', 'is_blind' => true]);

        $this->assertDatabaseHas('stocktake_sessions', [
            'session_type' => 'full',
            'status'       => 'draft',
        ]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Create cycle count session
    // ──────────────────────────────────────────────────────────────────────────

    public function test_can_create_cycle_count_session(): void
    {
        $res = $this->postJson('/api/stocktake-sessions', [
            'session_type' => 'cycle',
            'is_blind'     => true,
            'scope_type'   => 'supplier',
            'scope_id'     => 1,
        ]);

        $res->assertCreated()
            ->assertJsonFragment(['session_type' => 'cycle', 'scope_type' => 'supplier']);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Start session snapshots items
    // ──────────────────────────────────────────────────────────────────────────

    public function test_starting_session_snapshots_product_quantities(): void
    {
        $product = $this->makeProduct(['quantity' => 75]);

        $session = StocktakeSession::create([
            'reference_no' => 'ST-202610-002',
            'session_type' => 'full',
            'is_blind'     => true,
            'status'       => 'draft',
            'user_id'      => $this->user->id,
        ]);

        $res = $this->patchJson("/api/stocktake-sessions/{$session->id}/start");

        $res->assertOk()
            ->assertJsonFragment(['status' => 'in_progress']);

        // Items should be created with frozen system_qty
        $this->assertDatabaseHas('stocktake_items', [
            'stocktake_session_id' => $session->id,
            'product_id'           => $product->id,
            'system_qty'           => 75,
            'status'               => 'pending',
        ]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Items endpoint omits system_qty in blind mode
    // ──────────────────────────────────────────────────────────────────────────

    public function test_items_endpoint_hides_system_qty_in_blind_mode(): void
    {
        $product = $this->makeProduct(['quantity' => 50]);

        $session = StocktakeSession::create([
            'reference_no' => 'ST-202610-003',
            'session_type' => 'full',
            'is_blind'     => true,
            'status'       => 'draft',
            'user_id'      => $this->user->id,
        ]);

        $this->patchJson("/api/stocktake-sessions/{$session->id}/start");

        $res = $this->getJson("/api/stocktake-sessions/{$session->id}/items");
        $res->assertOk();

        $items = collect($res->json('data') ?? $res->json());
        if ($items->count() > 0) {
            // In blind mode, system_qty must be null
            $this->assertNull($items->first()['system_qty']);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Items endpoint reveals system_qty in non-blind mode
    // ──────────────────────────────────────────────────────────────────────────

    public function test_items_endpoint_shows_system_qty_in_non_blind_mode(): void
    {
        $product = $this->makeProduct(['quantity' => 30]);

        $session = StocktakeSession::create([
            'reference_no' => 'ST-202610-004',
            'session_type' => 'full',
            'is_blind'     => false,    // non-blind
            'status'       => 'draft',
            'user_id'      => $this->user->id,
        ]);

        $this->patchJson("/api/stocktake-sessions/{$session->id}/start");

        $res = $this->getJson("/api/stocktake-sessions/{$session->id}/items");
        $res->assertOk();

        $items = collect($res->json('data') ?? $res->json());
        if ($items->count() > 0) {
            // Non-blind mode: system_qty should be visible
            $this->assertNotNull($items->first()['system_qty']);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Record a count for an item
    // ──────────────────────────────────────────────────────────────────────────

    public function test_can_record_count_for_stocktake_item(): void
    {
        $product = $this->makeProduct(['quantity' => 100]);

        $session = StocktakeSession::create([
            'reference_no' => 'ST-202610-005',
            'session_type' => 'full',
            'is_blind'     => true,
            'status'       => 'in_progress',
            'user_id'      => $this->user->id,
        ]);

        $item = StocktakeItem::create([
            'stocktake_session_id' => $session->id,
            'product_id'           => $product->id,
            'system_qty'           => 100,
            'unit_purchase_price'  => 10.00,
            'status'               => 'pending',
        ]);

        $res = $this->patchJson("/api/stocktake-items/{$item->id}", [
            'counted_qty'          => 97,
            'discrepancy_reason'   => null,
        ]);

        $res->assertOk()
            ->assertJsonFragment([
                'status'       => 'counted',
                'counted_qty'  => 97,
                'variance_qty' => -3,
            ]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Full lifecycle: create → start → count → submit → finalize
    // ──────────────────────────────────────────────────────────────────────────

    public function test_full_stocktake_lifecycle_adjusts_stock(): void
    {
        $product = $this->makeProduct(['quantity' => 100]);

        // Create session
        $createRes = $this->postJson('/api/stocktake-sessions', [
            'session_type' => 'full',
            'is_blind'     => true,
        ]);
        $createRes->assertCreated();
        $sessionId = $createRes->json('id');

        // Start → snapshot items
        $this->patchJson("/api/stocktake-sessions/{$sessionId}/start")->assertOk();

        // Get the snapped item
        $itemsRes = $this->getJson("/api/stocktake-sessions/{$sessionId}/items?reveal=1");
        $itemsRes->assertOk();
        $items = $itemsRes->json('data') ?? $itemsRes->json();
        $this->assertNotEmpty($items);

        $item = collect($items)->firstWhere('product_id', $product->id);
        $this->assertNotNull($item);

        // Record count (shortage of 5)
        $this->patchJson("/api/stocktake-items/{$item['id']}", [
            'counted_qty'        => 95,
            'discrepancy_reason' => 'Counting Error',
        ])->assertOk();

        // Submit for review
        $this->patchJson("/api/stocktake-sessions/{$sessionId}/submit-review")->assertOk();

        // Finalize
        $finalRes = $this->postJson("/api/stocktake-sessions/{$sessionId}/finalize");
        $finalRes->assertOk()
            ->assertJsonFragment(['status' => 'finalized']);

        // Check stock was adjusted
        $product->refresh();
        $this->assertEquals(95, $product->quantity);

        // Check a stock adjustment was created
        $this->assertDatabaseHas('stock_adjustments', [
            'note' => "Stocktake finalization for session {$finalRes->json('reference_no')}",
        ]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. Cancel session
    // ──────────────────────────────────────────────────────────────────────────

    public function test_can_cancel_session(): void
    {
        $session = StocktakeSession::create([
            'reference_no' => 'ST-202610-006',
            'session_type' => 'full',
            'is_blind'     => true,
            'status'       => 'in_progress',
            'user_id'      => $this->user->id,
        ]);

        $res = $this->patchJson("/api/stocktake-sessions/{$session->id}/cancel");
        $res->assertOk()
            ->assertJsonFragment(['status' => 'cancelled']);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. Cannot finalize already finalized session
    // ──────────────────────────────────────────────────────────────────────────

    public function test_cannot_finalize_already_finalized_session(): void
    {
        $session = StocktakeSession::create([
            'reference_no' => 'ST-202610-007',
            'session_type' => 'full',
            'is_blind'     => true,
            'status'       => 'finalized',
            'user_id'      => $this->user->id,
        ]);

        $res = $this->postJson("/api/stocktake-sessions/{$session->id}/finalize");
        $res->assertStatus(422);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 12. Delete only draft or cancelled sessions
    // ──────────────────────────────────────────────────────────────────────────

    public function test_can_delete_draft_session(): void
    {
        $session = StocktakeSession::create([
            'reference_no' => 'ST-202610-008',
            'session_type' => 'full',
            'is_blind'     => true,
            'status'       => 'draft',
            'user_id'      => $this->user->id,
        ]);

        $this->deleteJson("/api/stocktake-sessions/{$session->id}")->assertNoContent();
        $this->assertDatabaseMissing('stocktake_sessions', ['id' => $session->id]);
    }

    public function test_cannot_delete_in_progress_session(): void
    {
        $session = StocktakeSession::create([
            'reference_no' => 'ST-202610-009',
            'session_type' => 'full',
            'is_blind'     => true,
            'status'       => 'in_progress',
            'user_id'      => $this->user->id,
        ]);

        $this->deleteJson("/api/stocktake-sessions/{$session->id}")->assertStatus(422);
    }
}
