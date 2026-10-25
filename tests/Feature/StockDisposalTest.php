<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Expense;
use App\Models\Product;
use App\Models\StockAdjustment;
use App\Models\StockDisposal;
use App\Models\Supplier;
use App\Models\User;
use App\Services\ExpenseService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Spatie\Permission\Models\Permission;
use Tests\TestCase;

class StockDisposalTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;
    protected Category $category;
    protected Brand $brand;
    protected Supplier $supplier;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        $this->user = User::create([
            'name' => 'Test Pharmacist',
            'email' => 'pharmacist_' . uniqid() . '@karobar.test',
            'password' => bcrypt('password'),
            'status' => 'active',
        ]);

        $this->category = Category::create(['name' => 'Antibiotics']);
        $this->brand = Brand::create(['name' => 'GSK']);
        $this->supplier = Supplier::create([
            'name' => 'Prime Pharma Distributors',
            'phone' => '03001234567',
        ]);

        foreach ([
            'stock-disposal.view',
            'stock-disposal.create',
            'stock-disposal.dispose',
            'stock-disposal.restore',
            'stock-disposal.delete',
            'stock-adjustment.view',
            'stock-adjustment.create',
            'product.view',
        ] as $perm) {
            Permission::findOrCreate($perm, 'sanctum');
        }

        $this->user->givePermissionTo([
            'stock-disposal.view',
            'stock-disposal.create',
            'stock-disposal.dispose',
            'stock-disposal.restore',
            'stock-disposal.delete',
            'stock-adjustment.view',
            'stock-adjustment.create',
            'product.view',
        ]);
    }

    protected function createProduct(array $overrides = []): Product
    {
        return Product::create(array_merge([
            'product_code' => 'PRD-' . uniqid(),
            'barcode' => 'BC-' . uniqid(),
            'name' => 'Test Medicine',
            'formulation' => 'Tablet',
            'description' => 'Test medicine description',
            'rack' => 'R1',
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'pack_size' => 10,
            'quantity' => 100,
            'pack_purchase_price' => 150.00,
            'pack_sale_price' => 220.00,
            'unit_purchase_price' => 15.00,
            'unit_sale_price' => 22.00,
            'avg_price' => 15.00,
            'margin' => 31.82,
            'max_discount' => 5.00,
            'narcotic' => 'no',
        ], $overrides));
    }

    public function test_quarantine_isolates_stock_without_posting_pnl_writeoff_and_can_be_restored(): void
    {
        $product = $this->createProduct([
            'product_code' => 'PRD-QRN-01',
            'name' => 'Amoxicillin 500mg Cap',
            'pack_size' => 10,
            'quantity' => 40,
            'unit_purchase_price' => 15.00,
            'avg_price' => 15.00,
            'unit_sale_price' => 22.00,
            'narcotic' => 'no',
        ]);

        $batch = Batch::create([
            'product_id' => $product->id,
            'batch_number' => 'B-EXP-99',
            'expiry_date' => now()->subDays(5)->toDateString(),
            'quantity' => 40,
        ]);

        // 1. Move 15 units into Quarantine
        $response = $this->actingAs($this->user, 'sanctum')->postJson('/api/stock-disposals', [
            'disposal_date' => now()->toDateString(),
            'status' => 'quarantined',
            'cause' => 'shelf_expiry',
            'disposal_method' => 'quarantine_holding',
            'reference_no' => 'Locked Bin B-2',
            'notes' => 'Pulled from shelf for supplier inspection',
            'items' => [
                [
                    'product_id' => $product->id,
                    'batch_id' => $batch->id,
                    'dispose_qty' => 15,
                    'unit_purchase_price' => 15.00,
                    'item_note' => 'Expired batch',
                ],
            ],
        ]);

        $response->assertCreated();
        $disposalId = $response->json('disposal.id');

        // Verify sellable stock was immediately isolated (deducted from shelf)
        $this->assertEquals(25, $product->fresh()->quantity);
        $this->assertEquals(25, $batch->fresh()->quantity);

        // Verify linked StockAdjustment exists, but NO P&L Expense has been created yet
        $disposal = StockDisposal::findOrFail($disposalId);
        $this->assertEquals('quarantined', $disposal->status);
        $this->assertNotNull($disposal->stock_adjustment_id);
        $this->assertNull($disposal->expense_id);

        // 2. Restore from Quarantine back to sellable stock
        $restoreRes = $this->actingAs($this->user, 'sanctum')->postJson("/api/stock-disposals/{$disposalId}/restore", [
            'reason' => 'Cleared by QA manager',
        ]);

        $restoreRes->assertOk();
        $this->assertEquals(40, $product->fresh()->quantity);
        $this->assertEquals(40, $batch->fresh()->quantity);

        $disposal->refresh();
        $this->assertEquals('restored', $disposal->status);
        $this->assertNull($disposal->stock_adjustment_id);
        $this->assertNull(StockAdjustment::where('posted_number', $disposal->disposal_number)->first());
    }

    public function test_finalizing_disposal_posts_non_cash_pnl_expense_and_reduces_net_profit(): void
    {
        $product = $this->createProduct([
            'product_code' => 'PRD-DSP-02',
            'name' => 'Cefixime Syrup 100ml',
            'pack_size' => 1,
            'quantity' => 50,
            'pack_purchase_price' => 120.00,
            'pack_sale_price' => 160.00,
            'unit_purchase_price' => 120.00,
            'avg_price' => 120.00,
            'unit_sale_price' => 160.00,
            'narcotic' => 'no',
        ]);

        $batch = Batch::create([
            'product_id' => $product->id,
            'batch_number' => 'B-BRK-10',
            'expiry_date' => now()->addMonths(6)->toDateString(),
            'quantity' => 20,
        ]);

        $expenseService = app(ExpenseService::class);
        $today = now()->toDateString();
        $beforeReport = $expenseService->getNetProfitReport($today, $today);
        $beforeExpenses = (float) ($beforeReport['kpis']['total_expenses'] ?? 0);

        // Create in Quarantine first (5 bottles @ Rs. 120 = Rs. 600)
        $createRes = $this->actingAs($this->user, 'sanctum')->postJson('/api/stock-disposals', [
            'disposal_date' => $today,
            'status' => 'quarantined',
            'cause' => 'breakage_damage',
            'disposal_method' => 'quarantine_holding',
            'items' => [
                [
                    'product_id' => $product->id,
                    'batch_id' => $batch->id,
                    'dispose_qty' => 5,
                    'unit_purchase_price' => 120.00,
                    'item_note' => 'Dropped carton during unpacking',
                ],
            ],
        ]);

        $createRes->assertCreated();
        $disposalId = $createRes->json('disposal.id');

        // Finalize physical destruction
        $disposeRes = $this->actingAs($this->user, 'sanctum')->postJson("/api/stock-disposals/{$disposalId}/dispose", [
            'disposal_method' => 'municipal_waste',
            'witness_name' => 'Asim Shahzad',
            'witness_designation' => 'Store In-Charge',
            'notes' => 'Crushed and disposed',
        ]);

        $disposeRes->assertOk();

        $disposal = StockDisposal::findOrFail($disposalId);
        $this->assertEquals('disposed', $disposal->status);
        $this->assertNotNull($disposal->expense_id);

        // Verify non-cash P&L Expense properties
        $expense = Expense::findOrFail($disposal->expense_id);
        $this->assertEquals(600.00, (float) $expense->amount);
        $this->assertEquals('other', $expense->payment_method);
        $this->assertNull($expense->account_id);
        $this->assertNull($expense->cash_drawer_session_id);

        // Verify Net Profit Report reflects the Rs. 600 write-off
        $afterReport = $expenseService->getNetProfitReport($today, $today);
        $afterExpenses = (float) ($afterReport['kpis']['total_expenses'] ?? 0);
        $this->assertEqualsWithDelta($beforeExpenses + 600.00, $afterExpenses, 0.01);
    }

    public function test_narcotic_disposal_requires_witness_name(): void
    {
        $narcoticProduct = $this->createProduct([
            'product_code' => 'PRD-NRC-03',
            'name' => 'Alprazolam 0.5mg Tab',
            'pack_size' => 30,
            'quantity' => 60,
            'unit_purchase_price' => 8.00,
            'avg_price' => 8.00,
            'unit_sale_price' => 12.00,
            'narcotic' => 'yes',
        ]);

        // Attempt direct disposal WITHOUT witness_name -> should fail with 422
        $failRes = $this->actingAs($this->user, 'sanctum')->postJson('/api/stock-disposals', [
            'disposal_date' => now()->toDateString(),
            'status' => 'disposed',
            'cause' => 'shelf_expiry',
            'disposal_method' => 'incineration',
            'items' => [
                [
                    'product_id' => $narcoticProduct->id,
                    'dispose_qty' => 10,
                    'unit_purchase_price' => 8.00,
                ],
            ],
        ]);

        $failRes->assertStatus(422);
        $failRes->assertJsonValidationErrors(['witness_name']);

        // Provide witness_name -> should succeed
        $okRes = $this->actingAs($this->user, 'sanctum')->postJson('/api/stock-disposals', [
            'disposal_date' => now()->toDateString(),
            'status' => 'disposed',
            'cause' => 'shelf_expiry',
            'disposal_method' => 'incineration',
            'witness_name' => 'Dr. Tariq Mehmood',
            'witness_designation' => 'Chief Pharmacist (Reg #9921)',
            'items' => [
                [
                    'product_id' => $narcoticProduct->id,
                    'dispose_qty' => 10,
                    'unit_purchase_price' => 8.00,
                ],
            ],
        ]);

        $okRes->assertCreated();
        $this->assertTrue((bool) $okRes->json('disposal.has_narcotics'));
        $this->assertEquals(50, $narcoticProduct->fresh()->quantity);
    }
}
