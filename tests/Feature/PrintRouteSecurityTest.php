<?php

namespace Tests\Feature;

use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\Setting;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Laravel\Sanctum\Sanctum;
use Spatie\Permission\Models\Permission;
use Tests\TestCase;

class PrintRouteSecurityTest extends TestCase
{
    use RefreshDatabase;

    private User $authorizedUser;
    private User $unauthorizedUser;
    private Customer $customer;
    private Supplier $supplier;
    private SaleInvoice $invoice;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        Setting::create([
            'store_name'   => 'Karobar Test Pharmacy',
            'phone_number' => '0300-0000000',
            'address'      => 'Test Street',
            'printer_type' => 'a4',
            'a4_template'  => 'standard',
        ]);

        $category = Category::create(['name' => 'General']);
        $brand    = Brand::create(['name' => 'BrandA']);
        $this->supplier = Supplier::create(['name' => 'Alpha Pharma Supplier']);
        $this->customer = Customer::create(['name' => 'Bilal Ahmad', 'phone' => '03001234567']);

        $product = Product::create([
            'product_code'        => 'PRD-100',
            'barcode'             => '1234567890123',
            'name'                => 'Amoxicillin 500mg',
            'category_id'         => $category->id,
            'brand_id'            => $brand->id,
            'supplier_id'         => $this->supplier->id,
            'pack_size'           => 10,
            'unit_purchase_price' => 10,
            'unit_sale_price'     => 15,
            'quantity'            => 50,
        ]);

        $permissions = [
            'sale-invoice.view',
            'customer-ledger.view',
            'supplier-ledger.view',
        ];

        foreach ($permissions as $perm) {
            Permission::findOrCreate($perm, 'sanctum');
        }

        $this->authorizedUser = User::create([
            'name'     => 'Authorized Staff',
            'email'    => 'auth@karobar.test',
            'password' => bcrypt('password'),
            'status'   => 'active',
        ]);
        $this->authorizedUser->givePermissionTo($permissions);

        $this->unauthorizedUser = User::create([
            'name'     => 'No Perm Staff',
            'email'    => 'noperm@karobar.test',
            'password' => bcrypt('password'),
            'status'   => 'active',
        ]);

        $this->invoice = SaleInvoice::create([
            'user_id'       => $this->authorizedUser->id,
            'customer_id'   => $this->customer->id,
            'posted_number' => 'SI-0001',
            'date'          => now()->toDateString(),
            'invoice_type'  => 'debit',
            'gross_amount'  => 150,
            'total'         => 150,
            'total_receive' => 150,
        ]);

        $this->invoice->items()->create([
            'product_id'               => $product->id,
            'pack_size'                => 10,
            'quantity'                 => 10,
            'price'                    => 15,
            'item_discount_percentage' => 0,
            'sub_total'                => 150,
        ]);
    }

    public function test_direct_unsigned_print_routes_are_forbidden(): void
    {
        $this->get("/print/sale-invoices/{$this->invoice->id}")
            ->assertStatus(403);

        $this->get("/customer-ledger/print?customer_id={$this->customer->id}")
            ->assertStatus(403);

        $this->get("/supplier-ledger/print?supplier_id={$this->supplier->id}")
            ->assertStatus(403);
    }

    public function test_unauthenticated_users_cannot_request_signed_print_urls(): void
    {
        $this->getJson("/api/sale-invoices/{$this->invoice->id}/print-url")
            ->assertStatus(401);

        $this->getJson("/api/customer-ledger/print-url?customer_id={$this->customer->id}")
            ->assertStatus(401);

        $this->getJson("/api/supplier-ledger/print-url?supplier_id={$this->supplier->id}")
            ->assertStatus(401);
    }

    public function test_users_without_permission_cannot_request_signed_print_urls(): void
    {
        Sanctum::actingAs($this->unauthorizedUser);

        $this->getJson("/api/sale-invoices/{$this->invoice->id}/print-url")
            ->assertStatus(403);

        $this->getJson("/api/customer-ledger/print-url?customer_id={$this->customer->id}")
            ->assertStatus(403);

        $this->getJson("/api/supplier-ledger/print-url?supplier_id={$this->supplier->id}")
            ->assertStatus(403);
    }

    public function test_authorized_users_receive_working_signed_print_urls_and_tampering_is_rejected(): void
    {
        Sanctum::actingAs($this->authorizedUser);

        // 1. Sale Invoice Print URL
        $saleRes = $this->getJson("/api/sale-invoices/{$this->invoice->id}/print-url")
            ->assertOk()
            ->assertJsonStructure(['url']);

        $salePrintUrl = $saleRes->json('url');
        $this->get($salePrintUrl)
            ->assertOk()
            ->assertSee('SI-0001');

        // 2. Customer Ledger Print URL
        $custRes = $this->getJson("/api/customer-ledger/print-url?customer_id={$this->customer->id}")
            ->assertOk()
            ->assertJsonStructure(['url']);

        $custPrintUrl = $custRes->json('url');
        $this->get($custPrintUrl)
            ->assertOk()
            ->assertSee('Bilal Ahmad');

        // Tampering with customer_id in signed URL must fail with 403
        $otherCustomer = Customer::create(['name' => 'Secret Customer']);
        $tamperedUrl = str_replace(
            "customer_id={$this->customer->id}",
            "customer_id={$otherCustomer->id}",
            $custPrintUrl
        );
        $this->get($tamperedUrl)->assertStatus(403);

        // 3. Supplier Ledger Print URL
        $supRes = $this->getJson("/api/supplier-ledger/print-url?supplier_id={$this->supplier->id}")
            ->assertOk()
            ->assertJsonStructure(['url']);

        $supPrintUrl = $supRes->json('url');
        $this->get($supPrintUrl)
            ->assertOk()
            ->assertSee('Alpha Pharma Supplier');
    }
}

