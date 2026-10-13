<?php

namespace Tests\Feature;

use App\Http\Controllers\PurchaseInvoiceController;
use App\Http\Controllers\PurchaseOrderController;
use App\Http\Middleware\EnsureLicensed;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\PurchaseOrder;
use App\Models\PurchaseOrderItem;
use App\Models\User;
use Illuminate\Auth\Access\AuthorizationException;
use Illuminate\Database\QueryException;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Gate;
use Illuminate\Validation\ValidationException;
use Spatie\Permission\Models\Permission;
use Tests\TestCase;

class PurchaseOrderWorkflowTest extends TestCase
{
    private bool $denyInvoiceCreate = false;

    private bool $useRealPermissions = false;

    protected function setUp(): void
    {
        parent::setUp();
        // Use real purchasing schemas, isolated from unrelated historical migrations.
        foreach ([
            '0001_01_01_000000_create_users_table.php',
            '2025_08_10_080111_create_permission_tables.php',
            '2025_08_10_100605_create_suppliers_table.php',
            '2025_08_10_113445_create_categories_table.php',
            '2025_08_10_113550_create_brands_table.php',
            '2025_08_10_151532_create_products_table.php',
            '2025_08_10_152438_create_batches_table.php',
            '2025_08_20_154726_create_purchase_invoices_table.php',
            '2025_08_20_154744_create_purchase_invoice_items_table.php',
            '2025_09_08_012037_add_total_paid_to_purchase_invoices_table.php',
            '2025_09_26_000000_add_invoice_type_to_purchase_invoices_table.php',
            '2026_04_01_000000_add_whole_sale_prices_to_products_table.php',
            '2026_04_01_000001_add_whole_sale_prices_to_purchase_invoice_items_table.php',
            '2026_04_01_000002_add_whole_sale_margin_to_purchase_invoice_items_table.php',
            '2026_04_01_000003_add_whole_sale_margin_to_products_table.php',
            '2026_05_01_000000_create_user_demands_table.php',
            '2026_10_12_000001_create_purchase_orders.php',
            '2026_10_12_000002_add_purchase_order_permissions.php',
            '2026_10_13_000002_add_bonus_to_purchase_order_items.php',
            '2026_10_13_000004_add_expected_received_date_to_purchase_orders.php',
        ] as $file) {
            (require database_path('migrations/'.$file))->up();
        }
        $user = User::forceCreate(['name' => 'Tester', 'email' => 'po@example.test', 'password' => bcrypt('test')]);
        $this->actingAs($user);
        Gate::before(fn ($user, $ability) => $this->useRealPermissions ? null : ($this->denyInvoiceCreate && $ability === 'create' ? false : true));
        DB::table('suppliers')->insert([['id' => 1, 'name' => 'Final distributor'], ['id' => 2, 'name' => 'Other distributor']]);
        DB::table('categories')->insert(['id' => 1, 'name' => 'Medicine']);
        foreach (['GSK', 'Abbott', 'Getz'] as $i => $brand) {
            DB::table('brands')->insert(['id' => $i + 1, 'name' => $brand]);
            Product::create(['name' => $brand.' item', 'product_code' => 'P'.$i, 'barcode' => 'B'.$i, 'brand_id' => $i + 1, 'category_id' => 1, 'supplier_id' => 2, 'pack_size' => 10, 'quantity' => 5, 'unit_purchase_price' => 2, 'unit_sale_price' => 4, 'pack_sale_price' => 40, 'avg_price' => 2]);
        }
    }

    private function request(array $data): Request
    {
        $request = Request::create('/api/test', 'POST', $data);
        $this->app->instance('request', $request);

        return $request;
    }

    private function order(): PurchaseOrder
    {
        $data = ['supplier_id' => 1, 'date' => '2026-10-12', 'expected_received_date' => '2026-10-13', 'notes' => 'Mixed brands', 'items' => []];
        foreach ([1, 2, 3] as $id) {
            $data['items'][] = ['product_id' => $id, 'ordered_quantity' => 100, 'pack_size' => 10, 'unit_purchase_price' => 2, 'item_discount_percentage' => 0];
        }
        $response = app(PurchaseOrderController::class)->store($this->request($data))->getData(true);

        return PurchaseOrder::findOrFail($response['id']);
    }

    private function invoiceData(PurchaseOrder $order, int $quantity = 100): array
    {
        $data = app(PurchaseOrderController::class)->receive($order)->getData(true);
        $data += ['invoice_number' => 'SUP-1', 'invoice_amount' => 600, 'total_amount' => 600, 'invoice_type' => 'debit'];
        foreach ($data['items'] as &$row) {
            $row['unit_quantity'] = $quantity;
            $row['pack_quantity'] = $quantity / 10;
            $row['quantity'] = $quantity;
            $row['sub_total'] = $quantity * 2;
            $row['avg_price'] = 2;
            $row['margin'] = 50;
            $row['batch'] = 'BATCH';
            $row['expiry'] = '2028-01-01';
        }

        return $data;
    }

    public function test_mixed_brand_order_save_edit_and_preparation_do_not_change_stock(): void
    {
        $order = $this->order();
        $this->assertSame('PO-00001', $order->number);
        $data = $this->invoiceData($order);
        $this->assertSame(1, $data['supplier_id']);
        $this->assertCount(3, $data['items']);
        $this->assertSame([100, 100, 100], array_column($data['items'], 'unit_quantity'));
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
        $payload = $order->load('items')->toArray();
        $payload['items'][0]['ordered_quantity'] = 110;
        app(PurchaseOrderController::class)->update($this->request($payload), $order);
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_edited_receipt_updates_actual_stock_preserves_order_and_prevents_duplicate(): void
    {
        $order = $this->order();
        $data = $this->invoiceData($order, 80);
        $result = app(PurchaseInvoiceController::class)->store($this->request($data));
        $this->assertSame(201, $result->status());
        $this->assertSame([85, 85, 85], Product::orderBy('id')->pluck('quantity')->all());
        $this->assertSame([80, 80, 80], DB::table('batches')->pluck('quantity')->all());
        $details = app(PurchaseOrderController::class)->show($order->fresh())->toArray();
        $this->assertSame('partially_received', $details['status']);
        $this->assertSame([100, 100, 100], array_column($details['items'], 'ordered_quantity'));
        $this->assertSame([20, 20, 20], array_column($details['items'], 'remaining_quantity'));
        $data['invoice_number'] = 'SUP-2';
        try {
            app(PurchaseInvoiceController::class)->store($this->request($data));
            $this->fail('Duplicate accepted');
        } catch (ValidationException $e) {
            $this->assertArrayHasKey('purchase_order_id', $e->errors());
        }
        $this->assertSame(1, PurchaseInvoice::count());
        $this->assertSame([85, 85, 85], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_full_receipt_and_standalone_invoice_both_work(): void
    {
        $order = $this->order();
        $data = $this->invoiceData($order);
        app(PurchaseInvoiceController::class)->store($this->request($data));
        $this->assertSame('received', $order->fresh()->status);
        unset($data['purchase_order_id']);
        $data['invoice_number'] = 'NORMAL';
        $result = app(PurchaseInvoiceController::class)->store($this->request($data));
        $this->assertSame(201, $result->status());
        $this->assertNull(PurchaseInvoice::latest('id')->first()->purchase_order_id);
        $this->assertSame([205, 205, 205], Product::orderBy('id')->pluck('quantity')->all());
        $this->assertSame(3, DB::table('purchase_invoice_items')->whereNull('purchase_order_item_id')->count());
    }

    public function test_invalid_order_headers_and_items_are_rejected(): void
    {
        foreach ([['date' => '2026-10-12', 'expected_received_date' => '2026-10-13', 'items' => []], ['date' => '2026-10-12', 'expected_received_date' => '2026-10-13', 'supplier_id' => 1, 'items' => []]] as $payload) {
            try {
                app(PurchaseOrderController::class)->store($this->request($payload));
                $this->fail('Invalid order accepted');
            } catch (ValidationException $e) {
                $this->assertArrayHasKey('items', $e->errors());
                if (! isset($payload['supplier_id'])) {
                    $this->assertSame(['Please select a supplier before saving the Purchase Order.'], $e->errors()['supplier_id']);
                }
            }
        }
        $this->assertSame(0, PurchaseOrder::count());
    }

    public function test_invalid_supplier_or_over_receipt_leaves_order_and_stock_unchanged(): void
    {
        $order = $this->order();
        foreach (['supplier', 'quantity'] as $case) {
            $data = $this->invoiceData($order, $case === 'quantity' ? 101 : 100);
            if ($case === 'supplier') {
                $data['supplier_id'] = 2;
            }
            try {
                app(PurchaseInvoiceController::class)->store($this->request($data));
                $this->fail('Invalid receipt accepted');
            } catch (ValidationException $e) {
                $this->assertNotEmpty($e->errors());
            }
        }
        $this->assertSame('ordered', $order->fresh()->status);
        $this->assertSame(0, PurchaseInvoice::count());
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_invoice_permission_is_required_to_prepare_receipt(): void
    {
        $order = $this->order();
        $this->denyInvoiceCreate = true;
        $this->expectException(AuthorizationException::class);
        app(PurchaseOrderController::class)->receive($order);
    }

    public function test_failed_stock_batch_write_rolls_back_invoice_stock_and_order(): void
    {
        $order = $this->order();
        DB::table('batches')->insert(['product_id' => 1, 'batch_number' => 'BATCH', 'expiry_date' => '2027-01-01', 'quantity' => 7]);
        $result = app(PurchaseInvoiceController::class)->store($this->request($this->invoiceData($order)));
        $this->assertSame(422, $result->status());
        $this->assertSame(0, PurchaseInvoice::count());
        $this->assertSame('ordered', $order->fresh()->status);
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_edit_receipt_recomputes_received_quantities_and_delete_reopens_order(): void
    {
        $order = $this->order();
        app(PurchaseInvoiceController::class)->store($this->request($this->invoiceData($order, 80)));
        $invoice = PurchaseInvoice::first();
        $data = $invoice->load('items')->toArray();
        foreach ($data['items'] as &$row) {
            $row['unit_quantity'] = 100;
            $row['quantity'] = 100;
            $row['pack_quantity'] = 10;
        }
        unset($row);
        $response = app(PurchaseInvoiceController::class)->update($this->request($data), $invoice);
        $this->assertSame(200, $response->status());
        $this->assertSame('received', $order->fresh()->status);
        $this->assertSame([105, 105, 105], Product::orderBy('id')->pluck('quantity')->all());
        $response = app(PurchaseInvoiceController::class)->destroy($invoice->fresh());
        $this->assertSame(200, $response->status());
        $this->assertSame('ordered', $order->fresh()->status);
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_database_rejects_a_second_invoice_link(): void
    {
        $order = $this->order();
        $data = $this->invoiceData($order);
        app(PurchaseInvoiceController::class)->store($this->request($data));
        $this->expectException(QueryException::class);
        PurchaseInvoice::first()->replicate()->save();
    }

    public function test_real_policy_prevents_receipt_preparation_and_invoice_creation(): void
    {
        $order = $this->order();
        $data = $this->invoiceData($order);
        foreach (['purchase-order.view', 'purchase-invoice.create'] as $name) {
            Permission::findOrCreate($name, 'sanctum');
        }
        $this->useRealPermissions = true;
        auth()->user()->givePermissionTo('purchase-order.view');
        $this->assertTrue(Gate::allows('view', $order));
        $this->assertFalse(Gate::allows('create', PurchaseInvoice::class));
        foreach (['prepare', 'save'] as $action) {
            try {
                if ($action === 'prepare') {
                    app(PurchaseOrderController::class)->receive($order);
                } else {
                    app(PurchaseInvoiceController::class)->store($this->request($data));
                }
                $this->fail('Unauthorized action accepted');
            } catch (AuthorizationException $e) {
                $this->assertSame(0, PurchaseInvoice::count());
            }
        }
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_finalized_supplier_and_cancelled_order_are_enforced(): void
    {
        $order = $this->order();
        $payload = $order->load('items')->toArray();
        $payload['supplier_id'] = 2;
        try {
            app(PurchaseOrderController::class)->update($this->request($payload), $order);
            $this->fail('Finalized supplier changed');
        } catch (ValidationException $e) {
            $this->assertArrayHasKey('supplier_id', $e->errors());
        }
        $data = $this->invoiceData($order);
        app(PurchaseOrderController::class)->cancel($order);
        try {
            app(PurchaseInvoiceController::class)->store($this->request($data));
            $this->fail('Cancelled order received');
        } catch (ValidationException $e) {
            $this->assertArrayHasKey('purchase_order_id', $e->errors());
        }
        $this->assertSame('cancelled', $order->fresh()->status);
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_invalid_stock_quantities_discounts_and_source_lines_are_rejected(): void
    {
        $order = $this->order();
        foreach (['quantity', 'pack_quantity', 'item_discount_percentage', 'purchase_order_item_id'] as $field) {
            $data = $this->invoiceData($order);
            $data['items'][0][$field] = match ($field) {
                'quantity' => 99,
                'pack_quantity' => -1,
                'item_discount_percentage' => 101,
                'purchase_order_item_id' => $order->items->last()->id,
            };
            try {
                app(PurchaseInvoiceController::class)->store($this->request($data));
                $this->fail('Invalid receipt accepted: '.$field);
            } catch (ValidationException $e) {
                $this->assertNotEmpty($e->errors());
            }
        }
        $this->assertSame(0, PurchaseInvoice::count());
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_split_batches_and_bonus_track_paid_units_and_preserve_receipt_audit(): void
    {
        $order = $this->order();
        $data = $this->invoiceData($order);
        $data['items'][0]['unit_quantity'] = 60;
        $data['items'][0]['pack_quantity'] = 6;
        $data['items'][0]['unit_bonus'] = 5;
        $data['items'][0]['quantity'] = 65;
        $second = $data['items'][0];
        $second['unit_quantity'] = 40;
        $second['pack_quantity'] = 4;
        $second['unit_bonus'] = 0;
        $second['quantity'] = 40;
        $second['batch'] = 'SECOND';
        $data['items'][] = $second;
        app(PurchaseInvoiceController::class)->store($this->request($data));
        $this->assertSame('received', $order->fresh()->status);
        $this->assertSame([110, 105, 105], Product::orderBy('id')->pluck('quantity')->all());
        $receivedAt = $order->fresh()->received_at->toDateTimeString();
        $this->travel(1)->days();
        $invoice = PurchaseInvoice::first();
        $payload = $invoice->load('items')->toArray();
        app(PurchaseInvoiceController::class)->update($this->request($payload), $invoice);
        $this->assertSame($receivedAt, $order->fresh()->received_at->toDateTimeString());
        $this->assertSame([110, 105, 105], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_http_routes_prepare_save_and_block_second_receipt(): void
    {
        $this->withoutMiddleware(EnsureLicensed::class);
        $order = $this->order();
        $this->getJson('/api/purchase-orders/'.$order->id)->assertOk()->assertJsonPath('number', $order->number);
        $this->getJson('/api/purchase-orders')->assertOk()->assertJsonPath('total', 1);
        $this->postJson('/api/purchase-orders/'.$order->id.'/receive')->assertOk()->assertJsonPath('supplier_id', 1);
        $data = $this->invoiceData($order);
        $response = $this->postJson('/api/purchase-invoices', $data)->assertCreated()->assertJsonPath('purchase_order.id', $order->id);
        $this->getJson('/api/purchase-invoices/'.$response->json('id'))->assertOk()->assertJsonPath('purchase_order.number', $order->number);
        $this->postJson('/api/purchase-orders/'.$order->id.'/receive')->assertStatus(409);
        $data['invoice_number'] = 'SECOND-ATTEMPT';
        $this->postJson('/api/purchase-invoices', $data)->assertUnprocessable()->assertJsonValidationErrors('purchase_order_id');
        $this->assertSame([105, 105, 105], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_permission_repair_is_idempotent_and_preserves_existing_grants(): void
    {
        $permission = Permission::findOrCreate('purchase-order.create', 'web');
        $migration = require database_path('migrations/2026_10_13_000001_ensure_purchase_order_sanctum_permissions.php');
        $migration->up();
        $migration->up();
        $this->assertSame(4, Permission::where('guard_name', 'sanctum')->whereIn('name', ['purchase-order.view', 'purchase-order.create', 'purchase-order.update', 'purchase-order.cancel'])->count());
        $this->assertNotNull($permission->fresh());
    }

    public function test_pack_order_pricing_and_bonus_are_saved_and_prepared_without_stock_changes(): void
    {
        $payload = ['supplier_id' => 1, 'date' => '2026-10-13', 'expected_received_date' => '2026-10-13', 'items' => [[
            'product_id' => 1, 'pack_size' => 10, 'ordered_packs' => 3,
            'pack_purchase_price' => 25, 'unit_bonus' => 15, 'item_discount_percentage' => 5,
        ]]];
        $response = app(PurchaseOrderController::class)->store($this->request($payload))->getData(true);
        $order = PurchaseOrder::findOrFail($response['id']);
        $line = $order->items()->first();
        $this->assertSame(30, $line->ordered_quantity);
        $this->assertEquals(2.5, $line->unit_purchase_price);
        $this->assertSame(15, $line->unit_bonus);
        $prepared = app(PurchaseOrderController::class)->receive($order)->getData(true);
        $this->assertEquals(3, $prepared['items'][0]['pack_quantity']);
        $this->assertEquals(25, $prepared['items'][0]['pack_purchase_price']);
        $this->assertEquals(1.5, $prepared['items'][0]['pack_bonus']);
        $this->assertSame(15, $prepared['items'][0]['unit_bonus']);
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
    }

    public function test_orders_reject_fractional_packs_and_modified_product_pack_size(): void
    {
        foreach (['fractional', 'size', 'units'] as $case) {
            $item = ['product_id' => 1, 'pack_size' => 10, 'ordered_packs' => 3, 'pack_purchase_price' => 25];
            if ($case === 'fractional') {
                $item['ordered_packs'] = 1.5;
            }
            if ($case === 'size') {
                $item['pack_size'] = 5;
            }
            if ($case === 'units') {
                unset($item['ordered_packs']);
                $item['ordered_quantity'] = 15;
            }
            try {
                app(PurchaseOrderController::class)->store($this->request(['supplier_id' => 1, 'date' => '2026-10-13', 'expected_received_date' => '2026-10-13', 'items' => [$item]]));
                $this->fail('Invalid pack order accepted');
            } catch (ValidationException $e) {
                $this->assertNotEmpty($e->errors());
            }
        }
        $this->assertSame(0, PurchaseOrder::count());
    }

    public function test_delete_route_removes_only_unreceived_orders_without_stock_changes(): void
    {
        $this->withoutMiddleware(EnsureLicensed::class);
        $order = $this->order();
        $this->deleteJson('/api/purchase-orders/'.$order->id)->assertNoContent();
        $this->assertSame(0, PurchaseOrder::count());
        $this->assertSame(0, PurchaseOrderItem::count());
        $this->assertSame([5, 5, 5], Product::orderBy('id')->pluck('quantity')->all());
        foreach ([80, 100] as $quantity) {
            $order = $this->order();
            $data = $this->invoiceData($order, $quantity);
            $data['invoice_number'] = 'RECEIPT-'.$quantity;
            app(PurchaseInvoiceController::class)->store($this->request($data));
            $this->deleteJson('/api/purchase-orders/'.$order->id)->assertStatus(409);
            $this->assertNotNull($order->fresh());
        }
    }

    public function test_delete_requires_its_own_permission(): void
    {
        $this->withoutMiddleware(EnsureLicensed::class);
        $order = $this->order();
        Permission::findOrCreate('purchase-order.view', 'sanctum');
        Permission::findOrCreate('purchase-order.delete', 'sanctum');
        $this->useRealPermissions = true;
        auth()->user()->givePermissionTo('purchase-order.view');
        $this->deleteJson('/api/purchase-orders/'.$order->id)->assertForbidden();
        $this->assertNotNull($order->fresh());
    }

    public function test_expected_date_is_required_and_expected_amount_excludes_received_cancelled_and_other_dates(): void
    {
        $order = $this->order();
        $this->assertEquals(600, PurchaseOrder::expectedAmountBetween('2026-10-13', '2026-10-13'));
        $this->assertEquals(0, PurchaseOrder::expectedAmountBetween('2026-10-14', '2026-10-14'));
        $order->items()->update(['item_discount_percentage' => 10]);
        $this->assertEquals(540, PurchaseOrder::expectedAmountBetween('2026-10-13', '2026-10-13'));
        $payload = $order->load('items')->toArray();
        unset($payload['expected_received_date']);
        try {
            app(PurchaseOrderController::class)->store($this->request($payload));
            $this->fail('Missing expected date accepted');
        } catch (ValidationException $e) {
            $this->assertArrayHasKey('expected_received_date', $e->errors());
        }
        $order->update(['status' => 'cancelled']);
        $this->assertEquals(0, PurchaseOrder::expectedAmountBetween('2026-10-13', '2026-10-13'));
        $order->update(['status' => 'ordered']);
        app(PurchaseInvoiceController::class)->store($this->request($this->invoiceData($order)));
        $this->assertEquals(0, PurchaseOrder::expectedAmountBetween('2026-10-13', '2026-10-13'));
    }
}
