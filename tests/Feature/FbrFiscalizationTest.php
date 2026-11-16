<?php

namespace Tests\Feature;

use App\Models\Account;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\Setting;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Tests\TestCase;

class FbrFiscalizationTest extends TestCase
{
    use RefreshDatabase;

    protected User $admin;
    protected Product $product;
    protected Customer $customer;
    protected Supplier $supplier;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        $this->admin = User::create([
            'name'     => 'Store Admin',
            'email'    => 'admin@karobar.test',
            'password' => bcrypt('secret123'),
        ]);

        app()[\Spatie\Permission\PermissionRegistrar::class]->forgetCachedPermissions();
        $role = \Spatie\Permission\Models\Role::findOrCreate('Admin', 'sanctum');
        $this->admin->assignRole($role);

        foreach (['settings.view', 'settings.update', 'sale-invoice.create', 'sale-invoice.view', 'payment.create'] as $perm) {
            \Spatie\Permission\Models\Permission::findOrCreate($perm, 'sanctum');
            $this->admin->givePermissionTo($perm);
        }

        $this->actingAs($this->admin);

        $brand = Brand::create(['name' => 'GSK']);
        $category = Category::create(['name' => 'Tablets']);
        $this->supplier = Supplier::create([
            'name'       => 'Premier Distributors',
            'tax_number' => '9988776-5',
            'strn'       => '32-77-8761-009-11',
            'province'   => 'Punjab',
        ]);

        $this->product = Product::create([
            'name'                => 'Augmentin 625mg',
            'product_code'        => 'PRD-0001',
            'barcode'             => '8964001234567',
            'brand_id'            => $brand->id,
            'category_id'         => $category->id,
            'supplier_id'         => $this->supplier->id,
            'pack_size'           => 10,
            'quantity'            => 100,
            'unit_purchase_price' => 30.0,
            'unit_sale_price'     => 40.0,
            'pack_purchase_price' => 300.0,
            'pack_sale_price'     => 400.0,
            'narcotic'            => 'no',
            'hs_code'             => '3004.1000',
            'tax_type'            => 'exempt',
            'sales_tax_rate'      => 0.00,
        ]);

        $this->customer = Customer::create([
            'name'       => 'Walk-in Customer',
            'phone'      => '03000000000',
            'tax_number' => '1122334-5',
            'province'   => 'Punjab',
        ]);
    }

    public function test_user_can_save_fbr_bearer_key_and_test_connection(): void
    {
        Http::fake([
            'https://gw.fbr.gov.pk/*' => Http::response([
                'invoiceNumber' => '7000112233445566778899',
                'validationResponse' => [
                    'statusCode' => '00',
                    'status'     => 'Valid',
                    'invoiceNo'  => '7000112233445566778899',
                ],
            ], 200),
        ]);

        $saveRes = $this->postJson('/api/settings', [
            'printer_type'        => 'thermal',
            'store_name'          => 'Al-Shifa Pharmacy',
            'fbr_enabled'         => true,
            'fbr_environment'     => 'sandbox',
            'fbr_mode'            => 'digital_invoicing',
            'fbr_bearer_token'    => '07eabd29-fb34-3a2a-ab73-1ff4eb282aef',
            'fbr_seller_ntn'      => '1234567-8',
            'fbr_seller_strn'     => '32-77-8761-001-45',
            'fbr_seller_province' => 'Punjab',
            'fbr_default_hs_code' => '3004.9099',
            'fbr_scenario_id'     => 'SN001',
            'fbr_auto_sync'       => true,
        ]);

        $saveRes->assertStatus(200)
            ->assertJsonPath('fbr_enabled', true)
            ->assertJsonPath('has_fbr_bearer_token', true)
            ->assertJsonPath('fbr_bearer_token_preview', '07ea••••••••2aef');

        $testRes = $this->postJson('/api/fbr/test-connection', [
            'fbr_environment' => 'sandbox',
            'fbr_mode'        => 'digital_invoicing',
            'fbr_seller_ntn'  => '1234567-8',
        ]);

        $testRes->assertStatus(200)
            ->assertJsonPath('ok', true)
            ->assertJsonPath('fbr_invoice_number', '7000112233445566778899');
    }

    public function test_sale_invoice_automatically_fiscalizes_with_fbr_and_renders_qr_on_receipt(): void
    {
        Setting::firstOrCreate(['id' => 1], [
            'store_name'          => 'Al-Shifa Pharmacy',
            'printer_type'        => 'thermal',
            'thermal_template'    => 'standard',
            'fbr_enabled'         => true,
            'fbr_environment'     => 'sandbox',
            'fbr_mode'            => 'tier1_pos_cloud',
            'fbr_pos_id'          => '110014',
            'fbr_bearer_token'    => '1298b5eb-b252-3d97-8622-a4a69d5bf818',
            'fbr_seller_ntn'      => '1234567-8',
            'fbr_seller_strn'     => '32-77-8761-001-45',
            'fbr_auto_sync'       => true,
        ]);

        Http::fake([
            '*esp.fbr.gov.pk*' => Http::response([
                'InvoiceNumber' => '1100142611161630001',
                'Response'      => 'Invoice received successfully',
                'Code'          => '100',
            ], 200),
        ]);

        $res = $this->postJson('/api/sale-invoices', [
            'sale_type'     => 'retail',
            'invoice_type'  => 'debit',
            'customer_id'   => $this->customer->id,
            'date'          => now()->toDateString(),
            'gross_amount'  => 80,
            'total'         => 80,
            'total_receive' => 80,
            'items'         => [
                [
                    'product_id' => $this->product->id,
                    'pack_size'  => 10,
                    'quantity'   => 2,
                    'price'      => 40,
                    'sub_total'  => 80,
                ],
            ],
        ]);

        $res->assertStatus(201)
            ->assertJsonPath('fbr_invoice_number', '1100142611161630001')
            ->assertJsonPath('fbr_sync_status', 'synced');

        $invoiceId = $res->json('id');
        $invoice = SaleInvoice::with('items')->findOrFail($invoiceId);
        $this->assertEquals('1100142611161630001', $invoice->fbr_invoice_number);
        $this->assertEquals('3004.1000', $invoice->items->first()->hs_code);

        // Verify thermal print output includes the FBR Invoice # and QR code block
        $printRes = $this->getJson("/api/sale-invoices/{$invoiceId}/print-url");
        $printRes->assertStatus(200);
        $this->assertStringContainsString('1100142611161630001', (string) $printRes->json('html'));
        $this->assertStringContainsString('FBR POS INVOICING SYSTEM', (string) $printRes->json('html'));
        $this->assertStringContainsString('FEDERAL BOARD OF REVENUE', (string) $printRes->json('html'));

        // Verify ALL 6 Thermal and ALL 6 A4 templates render the FBR block cleanly
        $setting = Setting::first();
        foreach (['standard', 'minimal', 'detailed', 'compact', 'bold', 'barcode'] as $tpl) {
            $thermalHtml = view("printer.sale_invoice_thermal_{$tpl}", [
                'invoice' => $invoice,
                'setting' => $setting,
            ])->render();
            $this->assertStringContainsString('1100142611161630001', $thermalHtml, "Thermal template {$tpl} missing FBR Invoice #");

            $a4Html = view("printer.sale_invoice_a4_{$tpl}", [
                'invoice' => $invoice,
                'setting' => $setting,
            ])->render();
            $this->assertStringContainsString('1100142611161630001', $a4Html, "A4 template {$tpl} missing FBR Invoice #");
        }
    }

    public function test_supplier_payment_deducts_withholding_tax_from_cash_outflow(): void
    {
        $bankAccount = Account::create([
            'code'            => 'BANK-01',
            'name'            => 'Meezan Bank',
            'type'            => 'bank',
            'opening_balance' => 100000,
            'current_balance' => 100000,
            'is_active'       => true,
            'is_default'      => true,
        ]);

        $res = $this->postJson('/api/payments', [
            'party_type'     => 'supplier',
            'party_id'       => $this->supplier->id,
            'payment_date'   => now()->toDateString(),
            'amount'         => 10000,
            'wht_percentage' => 4.5,
            'wht_amount'     => 450,
            'payment_method' => 'bank_transfer',
            'account_id'     => $bankAccount->id,
            'reference'      => 'CHQ-WHT-01',
        ]);

        $res->assertStatus(201)
            ->assertJsonPath('payment.amount', 10000)
            ->assertJsonPath('payment.wht_percentage', 4.5)
            ->assertJsonPath('payment.wht_amount', 450);

        // Bank account should only be debited by Net Outflow (10,000 - 450 = 9,550), leaving 90,450
        $this->assertEquals(90450.0, (float) $bankAccount->fresh()->current_balance);
    }
}
