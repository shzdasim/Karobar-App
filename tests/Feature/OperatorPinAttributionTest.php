<?php

namespace Tests\Feature;

use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Employee;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\PurchaseReturn;
use App\Models\SaleInvoice;
use App\Models\SaleReturn;
use App\Models\Setting;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class OperatorPinAttributionTest extends TestCase
{
    use RefreshDatabase;

    protected User $admin;
    protected Brand $brand;
    protected Category $category;
    protected Supplier $supplier;
    protected Customer $customer;
    protected Product $product;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        $this->admin = User::create([
            'name'     => 'Counter Terminal User',
            'email'    => 'counter@store.test',
            'password' => bcrypt('secret123'),
            'status'   => 'active',
        ]);

        app()[\Spatie\Permission\PermissionRegistrar::class]->forgetCachedPermissions();
        $role = \Spatie\Permission\Models\Role::findOrCreate('Admin', 'sanctum');
        $this->admin->assignRole($role);

        $this->actingAs($this->admin, 'sanctum');

        $this->brand = Brand::create(['name' => 'GSK']);
        $this->category = Category::create(['name' => 'Tablets']);
        $this->supplier = Supplier::create(['name' => 'Premier Distributors']);
        $this->customer = Customer::create(['name' => 'Walk-in Customer']);

        $this->product = Product::create([
            'product_code'        => 'PRD-PIN-01',
            'barcode'             => '8964001122334',
            'name'                => 'Panadol 500mg',
            'pack_size'           => 10,
            'quantity'            => 100,
            'pack_purchase_price' => 200,
            'unit_purchase_price' => 20,
            'pack_sale_price'     => 250,
            'unit_sale_price'     => 25,
            'avg_price'           => 20,
            'margin'              => 25,
            'narcotic'            => 'no',
            'category_id'         => $this->category->id,
            'brand_id'            => $this->brand->id,
            'supplier_id'         => $this->supplier->id,
        ]);
    }

    public function test_opt_in_by_default_allows_saving_without_pin(): void
    {
        $this->getJson('/api/settings')
            ->assertStatus(200)
            ->assertJson([
                'require_operator_pin'     => false,
                'operator_pin_auto_submit' => true,
            ]);

        $this->getJson('/api/operator-pins/directory')
            ->assertStatus(200)
            ->assertJsonStructure(['users', 'employees']);

        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'  => $this->customer->id,
            'invoice_type' => 'debit',
            'sale_type'    => 'retail',
            'date'         => now()->toDateString(),
            'items'        => [
                [
                    'product_id' => $this->product->id,
                    'quantity'   => 2,
                    'price'      => 25,
                    'sub_total'  => 50,
                ],
            ],
        ]);
        $saleRes->assertStatus(201);

        $purchaseRes = $this->postJson('/api/purchase-invoices', [
            'supplier_id'    => $this->supplier->id,
            'invoice_type'   => 'debit',
            'posted_date'    => now()->toDateString(),
            'invoice_number' => 'SUP-NO-PIN-001',
            'invoice_amount' => 200,
            'total_amount'   => 200,
            'items'          => [
                [
                    'product_id'          => $this->product->id,
                    'pack_size'           => 10,
                    'pack_quantity'       => 1,
                    'unit_quantity'       => 10,
                    'pack_purchase_price' => 200,
                    'unit_purchase_price' => 20,
                    'pack_sale_price'     => 250,
                    'unit_sale_price'     => 25,
                    'margin'              => 25,
                    'sub_total'           => 200,
                    'avg_price'           => 20,
                    'quantity'            => 10,
                ],
            ],
        ]);
        $purchaseRes->assertStatus(201);
    }

    public function test_unique_4_digit_pin_validation_and_linked_sync(): void
    {
        // Reject non-4-digit PIN
        $badRes = $this->putJson('/api/operator-pins/assign', [
            'type'     => 'user',
            'id'       => $this->admin->id,
            'pin_code' => '123',
        ]);
        $badRes->assertStatus(422);

        // Assign valid PIN to user
        $okRes = $this->putJson('/api/operator-pins/assign', [
            'type'     => 'user',
            'id'       => $this->admin->id,
            'pin_code' => '1024',
        ]);
        $okRes->assertStatus(200)->assertJson(['pin_code' => '1024']);

        // Create second user and try assigning same PIN -> rejected
        $user2 = User::create([
            'name'     => 'Second Cashier',
            'email'    => 'cashier2@store.test',
            'password' => bcrypt('secret123'),
            'status'   => 'active',
        ]);
        $dupRes = $this->putJson('/api/operator-pins/assign', [
            'type'     => 'user',
            'id'       => $user2->id,
            'pin_code' => '1024',
        ]);
        $dupRes->assertStatus(422);

        // Create Employee linked to user2, assign PIN on employee -> syncs to user2
        $emp = Employee::create([
            'employee_code' => 'EMP-0001',
            'user_id'       => $user2->id,
            'name'          => 'Linked Salesman',
            'status'        => 'active',
        ]);
        $empPinRes = $this->putJson('/api/operator-pins/assign', [
            'type'     => 'employee',
            'id'       => $emp->id,
            'pin_code' => '4821',
        ]);
        $empPinRes->assertStatus(200);
        $this->assertSame('4821', $user2->fresh()->pin_code);
        $this->assertSame('4821', $emp->fresh()->pin_code);

        // Generate unique PIN endpoint
        $genRes = $this->getJson('/api/operator-pins/generate');
        $genRes->assertStatus(200);
        $this->assertMatchesRegularExpression('/^\d{4}$/', $genRes->json('pin_code'));
    }

    public function test_enforced_operator_pin_attributes_all_four_document_types_without_relogin(): void
    {
        // Enable enforcement via Settings API
        $this->postJson('/api/settings', [
            'printer_type'             => 'thermal',
            'require_operator_pin'     => true,
            'operator_pin_auto_submit' => true,
            'operator_pin_modules'     => ['sale_invoice', 'purchase_invoice', 'sale_return', 'purchase_return'],
        ])->assertStatus(200);

        $this->getJson('/api/sale-system')
            ->assertStatus(200)
            ->assertJson([
                'require_operator_pin'     => true,
                'operator_pin_auto_submit' => true,
            ]);

        // Create a counter-only employee (no login user) and a separate system user
        $salesmanEmp = Employee::create([
            'employee_code' => 'EMP-0099',
            'name'          => 'Bilal Counter Salesman',
            'pin_code'      => '7788',
            'status'        => 'active',
        ]);

        $otherUser = User::create([
            'name'     => 'Zainab Pharmacist',
            'email'    => 'zainab@store.test',
            'password' => bcrypt('secret123'),
            'pin_code' => '3344',
            'status'   => 'active',
        ]);

        // 1. Sale Invoice without PIN -> 422 requires_operator_pin
        $this->postJson('/api/sale-invoices', [
            'customer_id'  => $this->customer->id,
            'invoice_type' => 'debit',
            'sale_type'    => 'retail',
            'date'         => now()->toDateString(),
            'items'        => [['product_id' => $this->product->id, 'quantity' => 2, 'price' => 25, 'sub_total' => 50]],
        ])->assertStatus(422)->assertJson(['requires_operator_pin' => true]);

        // Wrong PIN -> 422 requires_operator_pin
        $this->postJson('/api/sale-invoices', [
            'customer_id'  => $this->customer->id,
            'invoice_type' => 'debit',
            'sale_type'    => 'retail',
            'date'         => now()->toDateString(),
            'operator_pin' => '9999',
            'items'        => [['product_id' => $this->product->id, 'quantity' => 2, 'price' => 25, 'sub_total' => 50]],
        ])->assertStatus(422)->assertJson(['requires_operator_pin' => true]);

        // Valid Employee PIN -> attributed to Bilal Counter Salesman
        $saleRes = $this->postJson('/api/sale-invoices', [
            'customer_id'  => $this->customer->id,
            'invoice_type' => 'debit',
            'sale_type'    => 'retail',
            'date'         => now()->toDateString(),
            'operator_pin' => '7788',
            'items'        => [['product_id' => $this->product->id, 'quantity' => 2, 'price' => 25, 'sub_total' => 50]],
        ]);
        $saleRes->assertStatus(201)->assertJson(['operator_name' => 'Bilal Counter Salesman']);
        $sale = SaleInvoice::findOrFail($saleRes->json('id'));
        $this->assertSame($salesmanEmp->id, (int) $sale->salesman_id);

        // 2. Purchase Invoice with User PIN ('3344' -> Zainab Pharmacist)
        $this->postJson('/api/purchase-invoices', [
            'supplier_id'    => $this->supplier->id,
            'invoice_type'   => 'debit',
            'posted_date'    => now()->toDateString(),
            'invoice_number' => 'SUP-PIN-001',
            'invoice_amount' => 200,
            'total_amount'   => 200,
            'items'          => [[
                'product_id'          => $this->product->id,
                'pack_size'           => 10,
                'pack_quantity'       => 1,
                'unit_quantity'       => 10,
                'pack_purchase_price' => 200,
                'unit_purchase_price' => 20,
                'pack_sale_price'     => 250,
                'unit_sale_price'     => 25,
                'margin'              => 25,
                'sub_total'           => 200,
                'avg_price'           => 20,
                'quantity'            => 10,
            ]],
        ])->assertStatus(422)->assertJson(['requires_operator_pin' => true]);

        $purchaseRes = $this->postJson('/api/purchase-invoices', [
            'supplier_id'    => $this->supplier->id,
            'invoice_type'   => 'debit',
            'posted_date'    => now()->toDateString(),
            'invoice_number' => 'SUP-PIN-001',
            'invoice_amount' => 200,
            'total_amount'   => 200,
            'operator_pin'   => '3344',
            'items'          => [[
                'product_id'          => $this->product->id,
                'pack_size'           => 10,
                'pack_quantity'       => 1,
                'unit_quantity'       => 10,
                'pack_purchase_price' => 200,
                'unit_purchase_price' => 20,
                'pack_sale_price'     => 250,
                'unit_sale_price'     => 25,
                'margin'              => 25,
                'sub_total'           => 200,
                'avg_price'           => 20,
                'quantity'            => 10,
            ]],
        ]);
        $purchaseRes->assertStatus(201)->assertJson(['operator_name' => 'Zainab Pharmacist']);
        $purchase = PurchaseInvoice::findOrFail($purchaseRes->json('id'));
        $this->assertSame($otherUser->id, (int) $purchase->user_id);

        // 3. Sale Return with Employee PIN ('7788')
        $saleReturnRes = $this->postJson('/api/sale-returns', [
            'customer_id'     => $this->customer->id,
            'sale_invoice_id' => $sale->id,
            'date'            => now()->toDateString(),
            'total'           => 25,
            'operator_pin'    => '7788',
            'items'           => [[
                'product_id'           => $this->product->id,
                'unit_sale_quantity'   => 2,
                'unit_return_quantity' => 1,
                'unit_sale_price'      => 25,
                'sub_total'            => 25,
            ]],
        ]);
        $saleReturnRes->assertStatus(201)->assertJson(['operator_name' => 'Bilal Counter Salesman']);
        $sr = SaleReturn::findOrFail($saleReturnRes->json('id'));
        $this->assertSame($salesmanEmp->id, (int) $sr->employee_id);

        // 4. Purchase Return with User PIN ('3344')
        $purchaseReturnRes = $this->postJson('/api/purchase-returns', [
            'supplier_id'         => $this->supplier->id,
            'purchase_invoice_id' => $purchase->id,
            'date'                => now()->toDateString(),
            'total'               => 20,
            'operator_pin'        => '3344',
            'items'               => [[
                'product_id'           => $this->product->id,
                'pack_size'            => 10,
                'return_pack_quantity' => 0,
                'return_unit_quantity' => 1,
                'pack_purchase_price'  => 200,
                'unit_purchase_price'  => 20,
                'sub_total'            => 20,
            ]],
        ]);
        $purchaseReturnRes->assertStatus(201)->assertJson(['operator_name' => 'Zainab Pharmacist']);
        $pr = PurchaseReturn::findOrFail($purchaseReturnRes->json('id'));
        $this->assertSame($otherUser->id, (int) $pr->user_id);

        // 5. Selective module enforcement: disable purchase_invoice requirement -> saves without PIN
        Setting::first()->update(['operator_pin_modules' => ['sale_invoice']]);
        $purchaseNoPinRes = $this->postJson('/api/purchase-invoices', [
            'supplier_id'    => $this->supplier->id,
            'invoice_type'   => 'debit',
            'posted_date'    => now()->toDateString(),
            'invoice_number' => 'SUP-PIN-002',
            'invoice_amount' => 200,
            'total_amount'   => 200,
            'items'          => [[
                'product_id'          => $this->product->id,
                'pack_size'           => 10,
                'pack_quantity'       => 1,
                'unit_quantity'       => 10,
                'pack_purchase_price' => 200,
                'unit_purchase_price' => 20,
                'pack_sale_price'     => 250,
                'unit_sale_price'     => 25,
                'margin'              => 25,
                'sub_total'           => 200,
                'avg_price'           => 20,
                'quantity'            => 10,
            ]],
        ]);
        $purchaseNoPinRes->assertStatus(201);
    }
}
