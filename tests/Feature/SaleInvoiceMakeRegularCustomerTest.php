<?php

namespace Tests\Feature;

use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\CustomerRegularItem;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\Setting;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Laravel\Sanctum\Sanctum;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\PermissionRegistrar;
use Tests\TestCase;

class SaleInvoiceMakeRegularCustomerTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;
    protected Customer $registeredCustomer;
    protected Customer $walkInCustomer;
    protected Product $productA;
    protected Product $productB;
    protected SaleInvoice $invoice;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        app()[PermissionRegistrar::class]->forgetCachedPermissions();

        $permissions = [
            'customer.view',
            'customer.create',
            'customer.update',
            'sale-invoice.view',
            'sale-invoice.create',
            'sale-invoice.update',
        ];

        foreach ($permissions as $perm) {
            Permission::findOrCreate($perm, 'sanctum');
        }

        $this->user = User::create([
            'name'     => 'Counter Pharmacist',
            'email'    => 'counter@pharmacy.test',
            'password' => bcrypt('password'),
        ]);
        $this->user->givePermissionTo($permissions);

        Setting::create([
            'store_name'   => 'Al-Madina Medical Store',
            'phone_number' => '0300-1112233',
        ]);

        $category = Category::create(['name' => 'Cardiology']);
        $brand = Brand::create(['name' => 'Searle']);
        $supplier = Supplier::create(['name' => 'Muller & Phipps']);

        $this->productA = Product::create([
            'product_code'       => 'CAP-EZIDAY-50',
            'barcode'            => '770001',
            'name'               => 'Eziday 50mg Tablets',
            'category_id'        => $category->id,
            'brand_id'           => $brand->id,
            'supplier_id'        => $supplier->id,
            'pack_size'          => 20,
            'quantity'           => 100,
            'pack_purchase_price'=> 300,
            'unit_purchase_price'=> 15,
            'pack_sale_price'    => 400,
            'unit_sale_price'    => 20,
        ]);

        $this->productB = Product::create([
            'product_code'       => 'TAB-NORVASC-5',
            'barcode'            => '770002',
            'name'               => 'Norvasc 5mg Tablets',
            'category_id'        => $category->id,
            'brand_id'           => $brand->id,
            'supplier_id'        => $supplier->id,
            'pack_size'          => 30,
            'quantity'           => 120,
            'pack_purchase_price'=> 450,
            'unit_purchase_price'=> 15,
            'pack_sale_price'    => 600,
            'unit_sale_price'    => 20,
        ]);

        $this->registeredCustomer = Customer::create([
            'name'        => 'Muhammad Rashid',
            'phone'       => '0321-7654321',
            'is_regular'  => false,
        ]);

        $this->walkInCustomer = Customer::create([
            'name'        => 'Walk-in Customer',
            'phone'       => null,
            'is_regular'  => false,
        ]);

        // Invoice for registered customer
        $this->invoice = SaleInvoice::create([
            'user_id'       => $this->user->id,
            'posted_number' => 'SI-000099',
            'customer_id'   => $this->registeredCustomer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'sale_type'     => 'retail',
            'gross_amount'  => 1000,
            'total'         => 1000,
            'total_receive' => 1000,
            'status'        => 'completed',
        ]);

        SaleInvoiceItem::create([
            'sale_invoice_id' => $this->invoice->id,
            'product_id'      => $this->productA->id,
            'pack_size'       => 20,
            'pack_quantity'   => 1,
            'unit_quantity'   => 20,
            'quantity'        => 20,
            'price'           => 20,
            'sub_total'       => 400,
        ]);

        SaleInvoiceItem::create([
            'sale_invoice_id' => $this->invoice->id,
            'product_id'      => $this->productB->id,
            'pack_size'       => 30,
            'pack_quantity'   => 1,
            'unit_quantity'   => 30,
            'quantity'        => 30,
            'price'           => 20,
            'sub_total'       => 600,
        ]);
    }

    public function test_can_make_registered_customer_regular_from_sale_invoice(): void
    {
        Sanctum::actingAs($this->user);

        $nextDate = now()->addDays(30)->toDateString();

        $res = $this->postJson("/api/sale-invoices/{$this->invoice->id}/make-regular-customer", [
            'refill_cycle'       => 'monthly',
            'reminder_lead_days' => 2,
            'next_refill_date'   => $nextDate,
            'reminder_language'  => 'urdu',
            'regular_notes'      => 'Blood pressure patient - daily dosage',
            'merge_mode'         => 'replace',
            'items'              => [
                [
                    'product_id'    => $this->productA->id,
                    'pack_quantity' => 1,
                    'unit_quantity' => 20,
                    'dosage_notes'  => '1 Tab Morning',
                ],
                [
                    'product_id'    => $this->productB->id,
                    'pack_quantity' => 1,
                    'unit_quantity' => 30,
                    'dosage_notes'  => '1 Tab Night after dinner',
                ],
            ],
        ]);

        $res->assertOk()
            ->assertJsonPath('customer.is_regular', true)
            ->assertJsonPath('customer.refill_cycle', 'monthly')
            ->assertJsonPath('customer.next_refill_date', $nextDate)
            ->assertJsonPath('customer.reminder_language', 'urdu')
            ->assertJsonPath('customer.regular_notes', 'Blood pressure patient - daily dosage');

        $this->registeredCustomer->refresh();
        $this->assertTrue((bool) $this->registeredCustomer->is_regular);
        $this->assertEquals('monthly', $this->registeredCustomer->refill_cycle);
        $this->assertEquals($nextDate, $this->registeredCustomer->next_refill_date->toDateString());

        $regularItems = CustomerRegularItem::where('customer_id', $this->registeredCustomer->id)->get();
        $this->assertCount(2, $regularItems);

        $itemA = $regularItems->firstWhere('product_id', $this->productA->id);
        $this->assertNotNull($itemA);
        $this->assertEquals('1 Tab Morning', $itemA->dosage_notes);
        $this->assertEquals(20, $itemA->unit_quantity);
    }

    public function test_can_convert_walk_in_invoice_to_new_regular_customer(): void
    {
        Sanctum::actingAs($this->user);

        $walkInInv = SaleInvoice::create([
            'user_id'       => $this->user->id,
            'posted_number' => 'SI-000100',
            'customer_id'   => $this->walkInCustomer->id,
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'sale_type'     => 'retail',
            'gross_amount'  => 400,
            'total'         => 400,
        ]);

        SaleInvoiceItem::create([
            'sale_invoice_id' => $walkInInv->id,
            'product_id'      => $this->productA->id,
            'pack_size'       => 20,
            'pack_quantity'   => 1,
            'unit_quantity'   => 20,
            'quantity'        => 20,
            'price'           => 20,
            'sub_total'       => 400,
        ]);

        $res = $this->postJson("/api/sale-invoices/{$walkInInv->id}/make-regular-customer", [
            'new_customer' => [
                'name'    => 'Chaudhry Akram',
                'phone'   => '0300-8877665',
                'address' => 'Gulberg III, Lahore',
            ],
            'refill_cycle'       => 'weekly',
            'reminder_lead_days' => 1,
            'items'              => [
                [
                    'product_id'    => $this->productA->id,
                    'pack_quantity' => 1,
                    'unit_quantity' => 20,
                    'dosage_notes'  => '2 Tabs Daily',
                ],
            ],
        ]);

        $res->assertOk();

        // Customer was created and is regular
        $newCustomer = Customer::where('name', 'Chaudhry Akram')->first();
        $this->assertNotNull($newCustomer);
        $this->assertTrue((bool) $newCustomer->is_regular);
        $this->assertEquals('0300-8877665', $newCustomer->phone);
        $this->assertEquals('weekly', $newCustomer->refill_cycle);

        // Invoice customer was updated from walk-in to new customer
        $walkInInv->refresh();
        $this->assertEquals($newCustomer->id, $walkInInv->customer_id);

        $regularItems = CustomerRegularItem::where('customer_id', $newCustomer->id)->get();
        $this->assertCount(1, $regularItems);
        $this->assertEquals('2 Tabs Daily', $regularItems->first()->dosage_notes);
    }

    public function test_can_merge_items_with_existing_regular_customer(): void
    {
        Sanctum::actingAs($this->user);

        // First set product A as regular item
        $this->registeredCustomer->update(['is_regular' => true]);
        CustomerRegularItem::create([
            'customer_id'   => $this->registeredCustomer->id,
            'product_id'    => $this->productA->id,
            'pack_quantity' => 1,
            'unit_quantity' => 20,
            'dosage_notes'  => 'Old note',
        ]);

        // Now from invoice, merge product B and update product A
        $res = $this->postJson("/api/sale-invoices/{$this->invoice->id}/make-regular-customer", [
            'merge_mode' => 'merge',
            'items'      => [
                [
                    'product_id'    => $this->productA->id,
                    'pack_quantity' => 2,
                    'unit_quantity' => 40,
                    'dosage_notes'  => 'Updated 2 Tabs Morning',
                ],
                [
                    'product_id'    => $this->productB->id,
                    'pack_quantity' => 1,
                    'unit_quantity' => 30,
                    'dosage_notes'  => '1 Tab Night',
                ],
            ],
        ]);

        $res->assertOk();

        $items = CustomerRegularItem::where('customer_id', $this->registeredCustomer->id)->get();
        $this->assertCount(2, $items);

        $itemA = $items->firstWhere('product_id', $this->productA->id);
        $this->assertEquals('Updated 2 Tabs Morning', $itemA->dosage_notes);
        $this->assertEquals(40, $itemA->unit_quantity);

        $itemB = $items->firstWhere('product_id', $this->productB->id);
        $this->assertEquals('1 Tab Night', $itemB->dosage_notes);
    }
}
