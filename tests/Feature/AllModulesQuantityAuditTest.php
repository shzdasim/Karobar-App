<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\PurchaseOrder;
use App\Models\PurchaseReturn;
use App\Models\SaleInvoice;
use App\Models\SaleReturn;
use App\Models\StockAdjustment;
use App\Models\StockDisposal;
use App\Models\StocktakeSession;
use App\Models\StorageLocation;
use App\Models\Supplier;
use App\Models\User;
use App\Services\ProductImportService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class AllModulesQuantityAuditTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;
    protected Category $category;
    protected Brand $brand;
    protected Supplier $supplier;
    protected Customer $customer;
    protected StorageLocation $locA;
    protected StorageLocation $locB;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        Gate::before(fn () => true);

        $this->user = User::create([
            'name'     => 'Audit User',
            'email'    => 'audit_' . uniqid() . '@karobar.test',
            'password' => bcrypt('password'),
            'status'   => 'active',
        ]);
        $this->actingAs($this->user);

        $this->category = Category::create(['name' => 'General']);
        $this->brand = Brand::create(['name' => 'Acme']);
        $this->supplier = Supplier::create(['name' => 'Main Supplier', 'phone' => '03001112223']);
        $this->customer = Customer::create(['name' => 'Walk-in Test', 'phone' => '03009998887']);
        $this->locA = StorageLocation::create(['code' => 'LOC-A', 'name' => 'Main Store', 'type' => 'shop', 'is_default' => true]);
        $this->locB = StorageLocation::create(['code' => 'LOC-B', 'name' => 'Godown', 'type' => 'godown', 'is_default' => false]);
    }

    private function makeProduct(array $overrides = []): Product
    {
        static $seq = 1;
        $n = $seq++;
        return Product::create(array_merge([
            'product_code'        => "PRD-{$n}",
            'name'                => "Product {$n}",
            'barcode'             => "BAR-{$n}",
            'pack_size'           => 10,
            'quantity'            => 0,
            'pack_purchase_price' => 100,
            'unit_purchase_price' => 10,
            'pack_sale_price'     => 150,
            'unit_sale_price'     => 15,
            'whole_sale_pack_price' => 130,
            'whole_sale_unit_price' => 13,
            'avg_price'           => 10,
            'narcotic'            => 'no',
            'category_id'         => $this->category->id,
            'brand_id'            => $this->brand->id,
            'supplier_id'         => $this->supplier->id,
            'storage_location_id' => $this->locA->id,
        ], $overrides));
    }

    public function test_product_crud_and_import_quantity_behavior(): void
    {
        // 1. Create product via API with initial quantity = 20
        $res = $this->postJson('/api/products', [
            'product_code'        => 'P-100',
            'name'                => 'Amoxil 500mg',
            'barcode'             => '890123456001',
            'pack_size'           => 10,
            'quantity'            => 20,
            'pack_purchase_price' => 100,
            'unit_purchase_price' => 10,
            'pack_sale_price'     => 150,
            'unit_sale_price'     => 15,
            'narcotic'            => 'no',
            'category_id'         => $this->category->id,
            'brand_id'            => $this->brand->id,
            'supplier_id'         => $this->supplier->id,
        ]);
        $res->assertCreated();
        $product = Product::findOrFail($res->json('id'));
        $this->assertSame(20, (int) $product->quantity);

        // 2. Update product via API without changing quantity -> quantity must remain 20
        $updRes = $this->putJson("/api/products/{$product->id}", [
            'product_code'        => 'P-100',
            'name'                => 'Amoxil 500mg Updated',
            'barcode'             => '890123456001',
            'pack_size'           => 10,
            'quantity'            => 20,
            'pack_purchase_price' => 100,
            'unit_purchase_price' => 10,
            'pack_sale_price'     => 150,
            'unit_sale_price'     => 15,
            'narcotic'            => 'no',
            'category_id'         => $this->category->id,
            'brand_id'            => $this->brand->id,
            'supplier_id'         => $this->supplier->id,
        ]);
        $updRes->assertOk();
        $this->assertSame(20, (int) $product->fresh()->quantity);
    }

    public function test_purchase_order_and_purchase_invoice_lifecycle(): void
    {
        $p = $this->makeProduct(['pack_size' => 10, 'quantity' => 0]);

        // 1. Create PO for 30 units -> must NOT change product.quantity
        $poRes = $this->postJson('/api/purchase-orders', [
            'supplier_id'            => $this->supplier->id,
            'date'                   => now()->toDateString(),
            'expected_received_date' => now()->addDays(3)->toDateString(),
            'items'                  => [[
                'product_id'          => $p->id,
                'ordered_quantity'    => 30,
                'pack_size'           => 10,
                'unit_purchase_price' => 10,
            ]],
        ]);
        $poRes->assertCreated();
        $this->assertSame(0, (int) $p->fresh()->quantity);

        // 2. Receive PO via Purchase Invoice: 2 packs (20 units) + 5 unit_bonus = 25 units
        $piRes = $this->postJson('/api/purchase-invoices', [
            'supplier_id'        => $this->supplier->id,
            'invoice_type'       => 'debit',
            'posted_date'        => now()->toDateString(),
            'invoice_number'     => 'SUP-INV-001',
            'invoice_amount'     => 200,
            'total_amount'       => 200,
            'total_paid'         => 200,
            'items'              => [[
                'product_id'               => $p->id,
                'pack_size'                => 10,
                'batch'                    => 'B-PI-1',
                'expiry'                   => now()->addYear()->toDateString(),
                'pack_quantity'            => 2,
                'unit_quantity'            => 20,
                'pack_bonus'               => 0,
                'unit_bonus'               => 5,
                'quantity'                 => 25,
                'pack_purchase_price'      => 100,
                'unit_purchase_price'      => 10,
                'pack_sale_price'          => 150,
                'unit_sale_price'          => 15,
                'item_discount_percentage' => 0,
                'margin'                   => 46.67,
                'sub_total'                => 200,
                'avg_price'                => 8,
            ]],
        ]);
        $piRes->assertCreated();
        $piId = $piRes->json('id');

        $this->assertSame(25, (int) $p->fresh()->quantity);
        $batch = Batch::where('product_id', $p->id)->where('batch_number', 'B-PI-1')->firstOrFail();
        $this->assertSame(25, (int) $batch->quantity);

        // 3. Update Purchase Invoice: increase to 3 packs (30) + 5 bonus = 35 units
        $piUpd = $this->putJson("/api/purchase-invoices/{$piId}", [
            'supplier_id'        => $this->supplier->id,
            'invoice_type'       => 'debit',
            'posted_number'      => $piRes->json('posted_number'),
            'posted_date'        => now()->toDateString(),
            'invoice_number'     => 'SUP-INV-001',
            'invoice_amount'     => 300,
            'total_amount'       => 300,
            'total_paid'         => 300,
            'items'              => [[
                'product_id'               => $p->id,
                'pack_size'                => 10,
                'batch'                    => 'B-PI-1',
                'expiry'                   => now()->addYear()->toDateString(),
                'pack_quantity'            => 3,
                'unit_quantity'            => 30,
                'pack_bonus'               => 0,
                'unit_bonus'               => 5,
                'quantity'                 => 35,
                'pack_purchase_price'      => 100,
                'unit_purchase_price'      => 10,
                'pack_sale_price'          => 150,
                'unit_sale_price'          => 15,
                'item_discount_percentage' => 0,
                'margin'                   => 42.86,
                'sub_total'                => 300,
                'avg_price'                => 8.57,
            ]],
        ]);
        $piUpd->assertOk();
        $this->assertSame(35, (int) $p->fresh()->quantity);
        $this->assertSame(35, (int) $batch->fresh()->quantity);

        // 4. Delete Purchase Invoice -> reverts to 0
        $this->deleteJson("/api/purchase-invoices/{$piId}")->assertSuccessful();
        $this->assertSame(0, (int) $p->fresh()->quantity);
        $this->assertSame(0, (int) $batch->fresh()->quantity);
    }

    public function test_batch_create_update_and_move_location_preserves_product_quantity(): void
    {
        // Product has 50 unbatched units
        $p = $this->makeProduct(['quantity' => 50]);

        // 1. Allocate 30 units to Batch B1 at LocA (leaving 20 unbatched)
        $bRes = $this->postJson("/api/products/{$p->id}/batches", [
            'batch_number'        => 'B1',
            'expiry_date'         => now()->addYear()->toDateString(),
            'quantity'            => 30,
            'storage_location_id' => $this->locA->id,
        ]);
        $bRes->assertCreated();
        $batchId = $bRes->json('id');
        $this->assertSame(50, (int) $p->fresh()->quantity);
        $this->assertSame(30, (int) Batch::find($batchId)->quantity);

        // 2. Move 12 units from LocA to LocB
        $moveRes = $this->postJson("/api/batches/{$batchId}/move-location", [
            'to_storage_location_id' => $this->locB->id,
            'quantity'               => 12,
        ]);
        $moveRes->assertOk();
        $this->assertSame(50, (int) $p->fresh()->quantity);
        $this->assertSame(30, (int) Batch::where('product_id', $p->id)->sum('quantity'));
        $this->assertSame(18, (int) Batch::find($batchId)->quantity);
    }

    public function test_sale_invoice_retail_wholesale_bonus_and_sale_return_and_purchase_return(): void
    {
        // Product with 60 units: 40 in Batch B-S1, 20 unbatched
        $p = $this->makeProduct(['pack_size' => 10, 'quantity' => 60]);
        $batch = Batch::create([
            'product_id'          => $p->id,
            'batch_number'        => 'B-S1',
            'expiry_date'         => now()->addYear()->toDateString(),
            'quantity'            => 40,
            'storage_location_id' => $this->locA->id,
        ]);

        // 1. Wholesale Sale Invoice: 1 pack (10 units) + 2 unit_bonus = 12 units from Batch B-S1
        $siRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'debit',
            'sale_type'      => 'wholesale',
            'wholesale_type' => 'pack',
            'date'           => now()->toDateString(),
            'gross_amount'   => 130,
            'total'          => 130,
            'total_receive'  => 130,
            'items'          => [[
                'product_id'    => $p->id,
                'pack_size'     => 10,
                'batch_number'  => 'B-S1',
                'expiry'        => $batch->expiry_date,
                'pack_quantity' => 1,
                'unit_quantity' => 10,
                'quantity'      => 10,
                'pack_bonus'    => 0,
                'unit_bonus'    => 2,
                'bonus'         => 2,
                'pack_price'    => 130,
                'price'         => 13,
                'sub_total'     => 130,
            ]],
        ]);
        $siRes->assertCreated();
        $siId = $siRes->json('id');

        $this->assertSame(48, (int) $p->fresh()->quantity); // 60 - 12
        $this->assertSame(28, (int) $batch->fresh()->quantity); // 40 - 12

        // 2. Sale Return: return 3 units to Batch B-S1
        $srRes = $this->postJson('/api/sale-returns', [
            'customer_id'     => $this->customer->id,
            'sale_invoice_id' => $siId,
            'date'            => now()->toDateString(),
            'gross_total'     => 39,
            'total'           => 39,
            'total_refund'    => 39,
            'items'           => [[
                'product_id'           => $p->id,
                'batch_number'         => 'B-S1',
                'expiry'               => $batch->expiry_date,
                'unit_sale_quantity'   => 12,
                'unit_return_quantity' => 3,
                'unit_sale_price'      => 13,
                'sub_total'            => 39,
            ]],
        ]);
        $srRes->assertCreated();
        $this->assertSame(51, (int) $p->fresh()->quantity); // 48 + 3
        $this->assertSame(31, (int) $batch->fresh()->quantity); // 28 + 3

        // 3. Purchase Return: return 5 units from Batch B-S1
        $prRes = $this->postJson('/api/purchase-returns', [
            'supplier_id'    => $this->supplier->id,
            'date'           => now()->toDateString(),
            'gross_total'    => 50,
            'total'          => 50,
            'items'          => [[
                'product_id'           => $p->id,
                'batch'                => 'B-S1',
                'expiry'               => $batch->expiry_date,
                'pack_size'            => 10,
                'return_pack_quantity' => 0.5,
                'return_unit_quantity' => 5,
                'pack_purchase_price'  => 100,
                'unit_purchase_price'  => 10,
                'sub_total'            => 50,
            ]],
        ]);
        $prRes->assertCreated();
        $this->assertSame(46, (int) $p->fresh()->quantity); // 51 - 5
        $this->assertSame(26, (int) $batch->fresh()->quantity); // 31 - 5

        // 4. Unbatched Sale of 20 units (all unassigned stock) -> leaves 26 in B-S1
        $si2Res = $this->postJson('/api/sale-invoices', [
            'customer_id'   => $this->customer->id,
            'invoice_type'  => 'debit',
            'sale_type'     => 'retail',
            'date'          => now()->toDateString(),
            'gross_amount'  => 300,
            'total'         => 300,
            'total_receive' => 300,
            'items'         => [[
                'product_id' => $p->id,
                'quantity'   => 20,
                'price'      => 15,
                'sub_total'  => 300,
            ]],
        ]);
        $si2Res->assertCreated();
        $this->assertSame(26, (int) $p->fresh()->quantity); // 46 - 20
        $this->assertSame(26, (int) $batch->fresh()->quantity); // untouched
    }

    public function test_stock_adjustment_on_mixed_batched_and_unbatched_product_preserves_unbatched_stock(): void
    {
        // Product has 50 total: 30 in Batch B-ADJ, 20 unassigned
        $p = $this->makeProduct(['quantity' => 50]);
        $batch = Batch::create([
            'product_id'          => $p->id,
            'batch_number'        => 'B-ADJ',
            'expiry_date'         => now()->addYear()->toDateString(),
            'quantity'            => 30,
            'storage_location_id' => $this->locA->id,
        ]);

        // Adjust Batch B-ADJ from 30 -> 25 (diff = -5)
        $res = $this->postJson('/api/stock-adjustments', [
            'posted_number' => 'STADJ-90001',
            'posted_date'   => now()->toDateString(),
            'note'          => 'Damaged 5 units in batch',
            'items'         => [[
                'product_id'   => $p->id,
                'batch_number' => 'B-ADJ',
                'expiry'       => $batch->expiry_date,
                'actual_qty'   => 25,
            ]],
        ]);
        $res->assertOk();

        $this->assertSame(25, (int) $batch->fresh()->quantity);
        // Expected total = 25 (batch) + 20 (unassigned) = 45!
        $this->assertSame(45, (int) $p->fresh()->quantity, 'Batch stock adjustment wiped out unbatched stock!');

        // Delete the batch adjustment -> reverts batch to 30 and product to 50 (preserving 20 unassigned)
        $adjId = $res->json('id');
        $this->deleteJson("/api/stock-adjustments/{$adjId}")->assertNoContent();
        $this->assertSame(30, (int) $batch->fresh()->quantity);
        $this->assertSame(50, (int) $p->fresh()->quantity);
    }

    public function test_stock_disposal_on_mixed_batched_and_unbatched_product_preserves_unbatched_stock(): void
    {
        // Product has 50 total: 30 in Batch B-DSP, 20 unassigned
        $p = $this->makeProduct(['quantity' => 50]);
        $batch = Batch::create([
            'product_id'          => $p->id,
            'batch_number'        => 'B-DSP',
            'expiry_date'         => now()->subDay()->toDateString(),
            'quantity'            => 30,
            'storage_location_id' => $this->locA->id,
        ]);

        // Quarantine 5 units from Batch B-DSP
        $res = $this->postJson('/api/stock-disposals', [
            'disposal_date'   => now()->toDateString(),
            'cause'           => 'shelf_expiry',
            'disposal_method' => 'incineration',
            'status'          => 'quarantined',
            'record_expense'  => false,
            'items'           => [[
                'product_id'  => $p->id,
                'batch_id'    => $batch->id,
                'dispose_qty' => 5,
            ]],
        ]);
        $res->assertCreated();

        $this->assertSame(25, (int) $batch->fresh()->quantity);
        $this->assertSame(45, (int) $p->fresh()->quantity, 'Batch stock disposal wiped out unbatched stock!');

        // Restore from Quarantine
        $disposalId = $res->json('disposal.id') ?? $res->json('id') ?? StockDisposal::latest('id')->value('id');
        $this->postJson("/api/stock-disposals/{$disposalId}/restore", ['reason' => 'False alarm'])->assertOk();
        $this->assertSame(30, (int) $batch->fresh()->quantity);
        $this->assertSame(50, (int) $p->fresh()->quantity, 'Restore from quarantine wiped out unbatched stock!');
    }

    public function test_stocktake_on_mixed_batched_and_unbatched_product_preserves_unbatched_stock(): void
    {
        // Product has 50 total: 30 in Batch B-STK, 20 unassigned
        $p = $this->makeProduct(['quantity' => 50]);
        $batch = Batch::create([
            'product_id'          => $p->id,
            'batch_number'        => 'B-STK',
            'expiry_date'         => now()->addYear()->toDateString(),
            'quantity'            => 30,
            'storage_location_id' => $this->locA->id,
        ]);

        // Create stocktake session
        $sessRes = $this->postJson('/api/stocktake-sessions', [
            'session_type' => 'cycle',
            'scope_type'   => 'category',
            'scope_id'     => $this->category->id,
            'notes'        => 'Mixed stock test',
        ]);
        $sessRes->assertCreated();
        $sessionId = $sessRes->json('id');
        $this->patchJson("/api/stocktake-sessions/{$sessionId}/start")->assertOk();
        $session = StocktakeSession::with('items')->findOrFail($sessionId);
        $batchItem = $session->items->firstWhere('batch_number', 'B-STK');
        $this->assertNotNull($batchItem);

        // Count Batch B-STK as 28 (diff = -2) and any other items at their system_qty
        foreach ($session->items as $it) {
            $qty = $it->id === $batchItem->id ? 28 : (float) $it->system_qty;
            $this->patchJson("/api/stocktake-items/{$it->id}", [
                'counted_qty' => $qty,
            ])->assertOk();
        }

        // Submit for review and finalize stocktake
        $this->patchJson("/api/stocktake-sessions/{$sessionId}/submit-review")->assertOk();
        $this->postJson("/api/stocktake-sessions/{$sessionId}/finalize")->assertOk();

        $this->assertSame(28, (int) $batch->fresh()->quantity);
        $this->assertSame(48, (int) $p->fresh()->quantity, 'Stocktake wiped out unbatched stock!');
    }

    public function test_invoice_draft_does_not_mutate_stock(): void
    {
        $p = $this->makeProduct(['quantity' => 40]);

        $res = $this->postJson('/api/invoice-drafts', [
            'type'    => 'sale_retail',
            'title'   => 'Test Draft',
            'payload' => [
                'customer_id' => $this->customer->id,
                'sale_type'   => 'retail',
                'items'       => [[
                    'product_id' => $p->id,
                    'quantity'   => 10,
                    'price'      => 15,
                    'sub_total'  => 150,
                ]],
            ],
        ]);
        $res->assertSuccessful();
        $this->assertSame(40, (int) $p->fresh()->quantity);
    }
}
