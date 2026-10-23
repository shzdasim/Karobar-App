<?php

namespace Tests\Feature;

use App\Models\Customer;
use App\Models\CustomerLedger;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class ServerAuthoritativeTotalsAndDiscountControlTest extends TestCase
{
    use RefreshDatabase;

    private User $adminUser;
    private User $cashierUser;
    private Customer $customer;
    private Supplier $supplier;
    private Product $discountCappedProduct;
    private Product $standardProduct;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        // Setup permissions and roles
        Permission::firstOrCreate(['name' => 'sale-invoice.create', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-invoice.view', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'sale-invoice.discount-override', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'purchase-invoice.create', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'purchase-invoice.view', 'guard_name' => 'sanctum']);

        $adminRole = Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        $adminRole->givePermissionTo(Permission::all());

        $cashierRole = Role::firstOrCreate(['name' => 'Cashier', 'guard_name' => 'sanctum']);
        $cashierRole->givePermissionTo(['sale-invoice.create', 'sale-invoice.view', 'purchase-invoice.create', 'purchase-invoice.view']);

        $this->adminUser = User::forceCreate([
            'name' => 'Admin User',
            'email' => 'admin_override@example.test',
            'password' => bcrypt('secret'),
        ]);
        $this->adminUser->assignRole($adminRole);

        $this->cashierUser = User::forceCreate([
            'name' => 'Cashier User',
            'email' => 'cashier_override@example.test',
            'password' => bcrypt('secret'),
        ]);
        $this->cashierUser->assignRole($cashierRole);

        $this->customer = Customer::create([
            'name' => 'Test Customer',
            'phone' => '03001234567',
            'address' => 'Test Address',
            'credit_limit' => 50000,
        ]);

        $this->supplier = Supplier::create([
            'name' => 'Pharma Supplier Ltd',
            'phone' => '03009876543',
            'address' => 'Wholesale Depot',
        ]);

        $category = DB::table('categories')->insertGetId([
            'name' => 'General Medicine',
            'created_at' => now(),
            'updated_at' => now(),
        ]);

        $brand = DB::table('brands')->insertGetId([
            'name' => 'GSK',
            'created_at' => now(),
            'updated_at' => now(),
        ]);

        // Product with max_discount cap of 10%
        $this->discountCappedProduct = Product::create([
            'category_id' => $category,
            'brand_id' => $brand,
            'supplier_id' => $this->supplier->id,
            'product_code' => 'CAP-10',
            'name' => 'Panadol Capped Extra',
            'pack_size' => 10,
            'quantity' => 100,
            'unit_purchase_price' => 80,
            'unit_sale_price' => 100,
            'max_discount' => 10, // 10% max allowed discount
            'barcode' => '896400010001',
        ]);

        // Standard Product without discount cap
        $this->standardProduct = Product::create([
            'category_id' => $category,
            'brand_id' => $brand,
            'supplier_id' => $this->supplier->id,
            'product_code' => 'STD-01',
            'name' => 'Standard Vitamin C',
            'pack_size' => 1,
            'quantity' => 100,
            'unit_purchase_price' => 40,
            'unit_sale_price' => 50,
            'max_discount' => null,
            'barcode' => '896400010002',
        ]);
    }

    /**
     * Test 1: Server independently recalculates line subtotals and invoice total,
     * ignoring any malicious or manipulated totals sent by the client.
     */
    public function test_server_recalculates_sale_totals_ignoring_manipulated_client_totals(): void
    {
        // 5 units at Rs. 100 = Rs. 500
        // Item discount 10% = Rs. 50
        // Subtotal should be Rs. 450
        // Bill discount amount Rs. 50 -> taxable base Rs. 400
        // Tax 5% on 400 = Rs. 20
        // True Server Total = Rs. 420
        //
        // Client attempts to tamper with the payload: gross_amount: 50, total: 25, item subtotal: 10
        $tamperedPayload = [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'credit',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'discount_amount'=> 50,
            'tax_percentage' => 5,
            'gross_amount'   => 50,  // Fake client gross
            'total'          => 25,  // Fake client total
            'items'          => [
                [
                    'product_id'               => $this->discountCappedProduct->id,
                    'pack_size'                => 10,
                    'quantity'                 => 5,
                    'price'                    => 100,
                    'item_discount_percentage' => 10,
                    'sub_total'                => 10, // Fake client subtotal
                ],
            ],
        ];

        $response = $this->actingAs($this->adminUser)->postJson('/api/sale-invoices', $tamperedPayload);
        $response->assertStatus(201);

        $invoice = SaleInvoice::with('items')->latest('id')->first();
        $this->assertNotNull($invoice);

        // Verify Server-Authoritative Totals were stored
        $this->assertEquals(500.0, (float)$invoice->gross_amount, 'Server must calculate true gross of 500');
        $this->assertEquals(50.0, (float)$invoice->item_discount, 'Server must calculate true item discount of 50');
        $this->assertEquals(50.0, (float)$invoice->discount_amount, 'Server must record bill discount of 50');
        $this->assertEquals(20.0, (float)$invoice->tax_amount, 'Server must calculate 5% tax of 20');
        $this->assertEquals(420.0, (float)$invoice->total, 'Server must save authoritative total of 420');

        // Verify Line item subtotal was computed authoritatively
        $item = $invoice->items->first();
        $this->assertEquals(450.0, (float)$item->sub_total, 'Line subtotal must be 450 (500 - 50)');

        // Customer Ledger must record the authoritative total of 420, NOT the fake 25
        $ledger = CustomerLedger::where('sale_invoice_id', $invoice->id)->first();
        $this->assertNotNull($ledger);
        $this->assertEquals(420.0, (float)$ledger->invoice_total);
        $this->assertEquals(420.0, (float)$ledger->balance_remaining);
    }

    /**
     * Test 2: Product max discount limit blocks cashier when exceeded without override.
     */
    public function test_server_enforces_product_max_discount_limit(): void
    {
        // Panadol has max_discount = 10%
        // Cashier attempts to grant 25% discount
        $payload = [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'items'          => [
                [
                    'product_id'               => $this->discountCappedProduct->id,
                    'quantity'                 => 2,
                    'price'                    => 100,
                    'item_discount_percentage' => 25, // Exceeds 10% cap!
                ],
            ],
        ];

        $response = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', $payload);

        $response->assertStatus(422);
        $response->assertJson([
            'requires_discount_override' => true,
            'max_discount'               => 10,
            'requested_discount'         => 25,
            'product_id'                 => $this->discountCappedProduct->id,
        ]);

        $this->assertStringContainsString('exceeds maximum allowed limit of 10%', $response->json('message'));
        $this->assertEquals(0, SaleInvoice::count());
    }

    /**
     * Test 3: Authorized supervisor/admin can override max discount limit.
     */
    public function test_authorized_supervisor_can_override_max_discount(): void
    {
        $payload = [
            'customer_id'       => $this->customer->id,
            'invoice_type'      => 'debit',
            'payment_method'    => 'cash',
            'date'              => now()->toDateString(),
            'discount_override' => true, // Supervisor approval granted
            'items'             => [
                [
                    'product_id'               => $this->discountCappedProduct->id,
                    'quantity'                 => 2,
                    'price'                    => 100,
                    'item_discount_percentage' => 25, // Overridden to 25%
                ],
            ],
        ];

        $response = $this->actingAs($this->adminUser)->postJson('/api/sale-invoices', $payload);
        $response->assertStatus(201);

        $invoice = SaleInvoice::with('items')->latest('id')->first();
        $this->assertNotNull($invoice);
        // 2 * 100 = 200 - 25% (50) = 150
        $this->assertEquals(150.0, (float)$invoice->total);
        $this->assertEquals(25.0, (float)$invoice->items->first()->item_discount_percentage);
    }

    /**
     * Test 4: Cashier without discount-override permission cannot pass discount_override flag.
     */
    public function test_unauthorized_user_cannot_override_max_discount(): void
    {
        $payload = [
            'customer_id'       => $this->customer->id,
            'invoice_type'      => 'debit',
            'payment_method'    => 'cash',
            'date'              => now()->toDateString(),
            'discount_override' => true, // Cashier tries to override on their own
            'items'             => [
                [
                    'product_id'               => $this->discountCappedProduct->id,
                    'quantity'                 => 2,
                    'price'                    => 100,
                    'item_discount_percentage' => 25,
                ],
            ],
        ];

        $response = $this->actingAs($this->cashierUser)->postJson('/api/sale-invoices', $payload);
        $response->assertStatus(403);
        $this->assertStringContainsString('do not have permission to override maximum discount limits', $response->json('message'));
        $this->assertEquals(0, SaleInvoice::count());
    }

    /**
     * Test 5: Server independently recalculates purchase invoice totals.
     */
    public function test_server_recalculates_purchase_invoice_totals(): void
    {
        // 10 packs with pack_size 10 = 100 units
        // pack_purchase_price = 200 -> unit_purchase_price = 20
        // Gross subtotal = 100 * 20 = 2000
        // Item discount 10% = 200
        // Row Net Subtotal = 1800
        // Bill discount amount = 100 -> 1700
        // Tax 10% on 1700 = 170
        // True Total Amount = 1870
        $payload = [
            'supplier_id'         => $this->supplier->id,
            'invoice_number'      => 'PINV-CALC-TEST',
            'posted_date'         => now()->toDateString(),
            'invoice_type'        => 'credit',
            'discount_amount'     => 100,
            'tax_percentage'      => 10,
            'invoice_amount'      => 999, // Fake client rows total
            'total_amount'        => 500, // Fake client total
            'items'               => [
                [
                    'product_id'               => $this->standardProduct->id,
                    'pack_size'                => 10,
                    'pack_quantity'            => 10,
                    'unit_quantity'            => 0, // Auto-computed to 100
                    'pack_purchase_price'      => 200,
                    'unit_purchase_price'      => 20,
                    'pack_sale_price'          => 300,
                    'unit_sale_price'          => 30,
                    'margin'                   => 50,
                    'avg_price'                => 0, // Auto-computed
                    'quantity'                 => 0, // Auto-computed to 100
                    'sub_total'                => 50, // Fake client line subtotal
                    'item_discount_percentage' => 10,
                ],
            ],
        ];

        $response = $this->actingAs($this->adminUser)->postJson('/api/purchase-invoices', $payload);
        $response->assertStatus(201);

        $invoice = PurchaseInvoice::with('items')->where('invoice_number', 'PINV-CALC-TEST')->first();
        $this->assertNotNull($invoice);

        // Server-recalculated values
        $this->assertEquals(1800.0, (float)$invoice->invoice_amount, 'Rows total should be 1800');
        $this->assertEquals(100.0, (float)$invoice->discount_amount);
        $this->assertEquals(170.0, (float)$invoice->tax_amount);
        $this->assertEquals(1870.0, (float)$invoice->total_amount, 'Authoritative total amount must be 1870');

        $item = $invoice->items->first();
        $this->assertEquals(100, (int)$item->quantity);
        $this->assertEquals(1800.0, (float)$item->sub_total);
        $this->assertEquals(18.0, (float)$item->avg_price); // 1800 / 100 = 18
    }
}
