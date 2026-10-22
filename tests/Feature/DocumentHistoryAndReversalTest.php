<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Customer;
use App\Models\CustomerLedger;
use App\Models\DocumentHistory;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\SaleReturn;
use App\Models\Supplier;
use App\Models\SupplierLedger;
use App\Models\User;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class DocumentHistoryAndReversalTest extends TestCase
{
    private User $user;

    protected function setUp(): void
    {
        parent::setUp();

        foreach ([
            '0001_01_01_000000_create_users_table.php',
            '2025_08_10_080111_create_permission_tables.php',
            '2025_08_10_100605_create_suppliers_table.php',
            '2025_08_10_103922_create_customers_table.php',
            '2025_08_10_113445_create_categories_table.php',
            '2025_08_10_113550_create_brands_table.php',
            '2025_08_10_151532_create_products_table.php',
            '2025_08_10_152438_create_batches_table.php',
            '2025_08_20_154726_create_purchase_invoices_table.php',
            '2025_08_20_154744_create_purchase_invoice_items_table.php',
            '2025_08_24_015207_create_purchase_returns_table.php',
            '2025_08_24_015215_create_purchase_return_items_table.php',
            '2025_09_08_012037_add_total_paid_to_purchase_invoices_table.php',
            '2025_09_26_000000_add_invoice_type_to_purchase_invoices_table.php',
            '2025_08_30_171505_create_sale_invoices_table.php',
            '2025_08_30_171513_create_sale_invoice_items_table.php',
            '2025_09_01_074800_create_sale_returns_table.php',
            '2025_09_01_074814_create_sale_return_items_table.php',
            '2025_09_08_162536_add_total_receive_to_sale_invoices_table.php',
            '2025_09_25_000000_add_invoice_type_to_sale_invoices_table.php',
            '2026_04_01_000000_add_whole_sale_prices_to_products_table.php',
            '2026_04_01_000003_add_whole_sale_margin_to_products_table.php',
            '2026_04_01_000001_add_whole_sale_prices_to_purchase_invoice_items_table.php',
            '2026_04_01_000002_add_whole_sale_margin_to_purchase_invoice_items_table.php',
            '2026_04_02_000000_add_sale_type_to_sale_invoices_table.php',
            '2026_04_02_000001_add_sale_type_to_sale_invoice_items_table.php',
            '2026_04_02_000003_add_wholesale_type_to_sale_invoices_table.php',
            '2025_09_08_171931_create_customer_ledgers_table.php',
            '2025_09_08_013727_create_supplier_ledgers_table.php',
            '2026_05_01_000000_create_user_demands_table.php',
            '2026_10_12_000001_create_purchase_orders.php',
            '2026_10_13_000006_add_sale_cost_snapshots.php',
            '2026_10_23_000001_create_invoice_drafts_table.php',
            '2026_10_23_000003_create_document_histories_and_invoice_reversals.php',
        ] as $file) {
            (require database_path('migrations/'.$file))->up();
        }

        $this->user = User::forceCreate([
            'name'     => 'Cashier Charlie',
            'email'    => 'charlie@test.test',
            'password' => bcrypt('password'),
        ]);

        $this->actingAs($this->user);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);
        Gate::before(fn () => true);

        DB::table('customers')->insert(['id' => 1, 'name' => 'Regular Customer']);
        DB::table('suppliers')->insert(['id' => 1, 'name' => 'Distributor A']);
        DB::table('categories')->insert(['id' => 1, 'name' => 'Pharma']);
        DB::table('brands')->insert(['id' => 1, 'name' => 'Brand A']);

        Product::forceCreate([
            'id'                  => 1,
            'name'                => 'Panadol 500mg',
            'product_code'        => 'P500',
            'barcode'             => 'BAR500',
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => 1,
            'quantity'            => 100,
            'pack_size'           => 10,
            'unit_sale_price'     => 10,
            'pack_purchase_price' => 70,
        ]);

        Batch::forceCreate([
            'id'           => 1,
            'product_id'   => 1,
            'batch_number' => 'B2026',
            'expiry_date'  => '2028-01-01',
            'quantity'     => 100,
        ]);
    }

    public function test_sale_invoice_creation_records_audit_history(): void
    {
        $payload = [
            'customer_id'  => 1,
            'invoice_type' => 'credit',
            'sale_type'    => 'retail',
            'date'         => '2026-10-23',
            'gross_amount' => 100,
            'total'        => 100,
            'items'        => [
                [
                    'product_id'   => 1,
                    'batch_number' => 'B2026',
                    'quantity'     => 10,
                    'price'        => 10,
                    'sub_total'    => 100,
                ],
            ],
        ];

        $response = $this->postJson('/api/sale-invoices', $payload);
        $response->assertStatus(201);
        $saleId = $response->json('id');

        // DocumentHistory was created
        $history = DocumentHistory::where('entity_type', SaleInvoice::class)
            ->where('entity_id', $saleId)
            ->first();

        $this->assertNotNull($history);
        $this->assertSame('created', $history->action);
        $this->assertSame($this->user->id, $history->user_id);
        $this->assertSame(100, (int)$history->after_payload['total']);

        // Stock was deducted properly
        $this->assertSame(90, (int)Product::find(1)->quantity);
        $this->assertSame(90, (int)Batch::find(1)->quantity);
    }

    public function test_sale_invoice_update_records_audit_history_with_reason_and_diff(): void
    {
        $sale = SaleInvoice::forceCreate([
            'user_id'       => $this->user->id,
            'customer_id'   => 1,
            'posted_number' => 'SI-000001',
            'date'          => '2026-10-23',
            'gross_amount'  => 50,
            'total'         => 50,
            'status'        => 'posted',
        ]);

        SaleInvoiceItem::forceCreate([
            'sale_invoice_id' => $sale->id,
            'product_id'      => 1,
            'batch_number'    => 'B2026',
            'pack_size'       => 10,
            'quantity'        => 5,
            'price'           => 10,
            'sub_total'       => 50,
        ]);

        Product::find(1)->decrement('quantity', 5);
        Batch::find(1)->decrement('quantity', 5);

        $payload = [
            'reason'              => 'Customer requested 5 more units',
            'customer_id'         => 1,
            'posted_number'       => 'SI-000001',
            'date'                => '2026-10-23',
            'gross_amount'        => 100,
            'total'               => 100,
            'items'               => [
                [
                    'product_id'   => 1,
                    'batch_number' => 'B2026',
                    'quantity'     => 10,
                    'price'        => 10,
                    'sub_total'    => 100,
                ],
            ],
        ];

        $response = $this->putJson("/api/sale-invoices/{$sale->id}", $payload);
        $response->assertStatus(200);

        $updateHistory = DocumentHistory::where('entity_type', SaleInvoice::class)
            ->where('entity_id', $sale->id)
            ->where('action', 'updated')
            ->first();

        $this->assertNotNull($updateHistory);
        $this->assertSame('Customer requested 5 more units', $updateHistory->reason);
        $this->assertEquals(50, $updateHistory->diff['total']['from']);
        $this->assertEquals(100, $updateHistory->diff['total']['to']);
    }

    public function test_sale_invoice_reversal_safely_restores_stock_and_marks_invoice_reversed(): void
    {
        $sale = SaleInvoice::forceCreate([
            'user_id'       => $this->user->id,
            'customer_id'   => 1,
            'invoice_type'  => 'credit',
            'posted_number' => 'SI-000002',
            'date'          => '2026-10-23',
            'gross_amount'  => 100,
            'total'         => 100,
            'status'        => 'posted',
        ]);

        SaleInvoiceItem::forceCreate([
            'sale_invoice_id' => $sale->id,
            'product_id'      => 1,
            'batch_number'    => 'B2026',
            'pack_size'       => 10,
            'quantity'        => 10,
            'price'           => 10,
            'sub_total'       => 100,
        ]);

        CustomerLedger::forceCreate([
            'customer_id'       => 1,
            'sale_invoice_id'   => $sale->id,
            'entry_type'        => 'invoice',
            'entry_date'        => '2026-10-23',
            'posted_number'     => 'SI-000002',
            'invoice_total'     => 100,
            'total_received'    => 0,
            'balance_remaining' => 100,
        ]);

        Product::find(1)->decrement('quantity', 10);
        Batch::find(1)->decrement('quantity', 10);
        $this->assertSame(90, (int)Product::find(1)->quantity);

        // Reverse the invoice
        $response = $this->postJson("/api/sale-invoices/{$sale->id}/reverse", [
            'reason' => 'Customer order cancelled by manager',
        ]);
        $response->assertStatus(200);

        $sale->refresh();
        $this->assertSame('reversed', $sale->status);
        $this->assertNotNull($sale->reversed_at);
        $this->assertSame($this->user->id, $sale->reversed_by_id);
        $this->assertSame('Customer order cancelled by manager', $sale->reversal_reason);

        // Inventory stock was safely restored!
        $this->assertSame(100, (int)Product::find(1)->quantity);
        $this->assertSame(100, (int)Batch::find(1)->quantity);

        // Customer ledger obligation was cleared
        $this->assertSame(0, CustomerLedger::where('sale_invoice_id', $sale->id)->count());

        // DocumentHistory has 'reversed' entry
        $reversalHistory = DocumentHistory::where('entity_type', SaleInvoice::class)
            ->where('entity_id', $sale->id)
            ->where('action', 'reversed')
            ->first();
        $this->assertNotNull($reversalHistory);
        $this->assertSame('Customer order cancelled by manager', $reversalHistory->reason);

        // Attempting to reverse again returns 422
        $secondAttempt = $this->postJson("/api/sale-invoices/{$sale->id}/reverse", [
            'reason' => 'Try again',
        ]);
        $secondAttempt->assertStatus(422);

        // Attempting to edit a reversed invoice returns 422
        $editAttempt = $this->putJson("/api/sale-invoices/{$sale->id}", [
            'customer_id'   => 1,
            'posted_number' => 'SI-000002',
            'date'          => '2026-10-23',
            'gross_amount'  => 100,
            'total'         => 100,
            'items'         => [],
        ]);
        $editAttempt->assertStatus(422);
    }

    public function test_sale_invoice_with_active_return_cannot_be_reversed(): void
    {
        $sale = SaleInvoice::forceCreate([
            'user_id'       => $this->user->id,
            'customer_id'   => 1,
            'posted_number' => 'SI-000003',
            'date'          => '2026-10-23',
            'gross_amount'  => 100,
            'total'         => 100,
            'status'        => 'posted',
        ]);

        SaleReturn::forceCreate([
            'user_id'         => $this->user->id,
            'customer_id'     => 1,
            'sale_invoice_id' => $sale->id,
            'posted_number'   => 'SR-000001',
            'date'            => '2026-10-23',
            'gross_total'     => 20,
            'total'           => 20,
        ]);

        $response = $this->postJson("/api/sale-invoices/{$sale->id}/reverse", [
            'reason' => 'Attempting reversal with return',
        ]);

        $response->assertStatus(422);
        $response->assertJsonFragment([
            'message' => 'Cannot reverse this invoice because linked sale returns exist. Please reverse or remove the returns first.',
        ]);
    }

    public function test_document_history_endpoint_returns_chronological_trail(): void
    {
        $sale = SaleInvoice::forceCreate([
            'user_id'       => $this->user->id,
            'customer_id'   => 1,
            'posted_number' => 'SI-000004',
            'date'          => '2026-10-23',
            'gross_amount'  => 100,
            'total'         => 100,
            'status'        => 'posted',
        ]);

        DocumentHistory::forceCreate([
            'user_id'     => $this->user->id,
            'entity_type' => SaleInvoice::class,
            'entity_id'   => $sale->id,
            'action'      => 'created',
            'diff'        => ['event' => 'Initial document creation'],
        ]);

        DocumentHistory::forceCreate([
            'user_id'     => $this->user->id,
            'entity_type' => SaleInvoice::class,
            'entity_id'   => $sale->id,
            'action'      => 'updated',
            'reason'      => 'Price corrected',
            'diff'        => ['total' => ['from' => 100, 'to' => 90]],
        ]);

        $response = $this->getJson("/api/document-histories?entity_type=sale_invoice&entity_id={$sale->id}");
        $response->assertStatus(200);
        $data = $response->json('histories');

        $this->assertCount(2, $data);
        $this->assertSame('updated', $data[0]['action']);
        $this->assertSame('Price corrected', $data[0]['reason']);
        $this->assertSame('created', $data[1]['action']);
    }

    public function test_purchase_invoice_reversal_safely_deducts_stock_and_records_history(): void
    {
        $payload = [
            'supplier_id'         => 1,
            'invoice_type'        => 'credit',
            'posted_date'         => '2026-10-23',
            'invoice_number'      => 'PINV-999',
            'invoice_amount'      => 700,
            'total_amount'        => 700,
            'items'               => [
                [
                    'product_id'               => 1,
                    'batch_number'             => 'PB-001',
                    'expiry_date'              => '2028-10-23',
                    'pack_quantity'            => 10,
                    'unit_quantity'            => 100,
                    'pack_purchase_price'      => 70,
                    'unit_purchase_price'      => 7,
                    'pack_sale_price'          => 100,
                    'unit_sale_price'          => 10,
                    'margin'                   => 42.85,
                    'sub_total'                => 700,
                    'avg_price'                => 7,
                    'quantity'                 => 100,
                ],
            ],
        ];

        $response = $this->postJson('/api/purchase-invoices', $payload);
        $response->assertStatus(201);
        $purchaseId = $response->json('id');

        // Product stock increased from 100 to 200
        $this->assertSame(200, (int)Product::find(1)->quantity);

        // DocumentHistory created
        $history = DocumentHistory::where('entity_type', PurchaseInvoice::class)
            ->where('entity_id', $purchaseId)
            ->where('action', 'created')
            ->first();
        $this->assertNotNull($history);

        // Reverse the purchase invoice
        $revResp = $this->postJson("/api/purchase-invoices/{$purchaseId}/reverse", [
            'reason' => 'Vendor delivered wrong items',
        ]);
        $revResp->assertStatus(200);

        // Product stock safely returned from 200 to 100
        $this->assertSame(100, (int)Product::find(1)->quantity);

        $purchase = PurchaseInvoice::find($purchaseId);
        $this->assertSame('reversed', $purchase->status);
        $this->assertSame('Vendor delivered wrong items', $purchase->reversal_reason);
        $this->assertSame($this->user->id, $purchase->reversed_by_id);

        $revHistory = DocumentHistory::where('entity_type', PurchaseInvoice::class)
            ->where('entity_id', $purchaseId)
            ->where('action', 'reversed')
            ->first();
        $this->assertNotNull($revHistory);
        $this->assertSame('Vendor delivered wrong items', $revHistory->reason);
    }
}
