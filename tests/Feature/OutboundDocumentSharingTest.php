<?php

namespace Tests\Feature;

use App\Mail\DocumentShareMail;
use App\Models\Account;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\CustomerLedger;
use App\Models\Payment;
use App\Models\Product;
use App\Models\PurchaseOrder;
use App\Models\PurchaseOrderItem;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\Setting;
use App\Models\Supplier;
use App\Models\SupplierLedger;
use App\Models\User;
use App\Services\DocumentShareService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Mail;
use Illuminate\Support\Facades\Storage;
use Laravel\Sanctum\Sanctum;
use Spatie\Permission\Models\Permission;
use Tests\TestCase;

class OutboundDocumentSharingTest extends TestCase
{
    use RefreshDatabase;

    private User $authorizedUser;
    private User $unauthorizedUser;
    private Category $category;
    private Brand $brand;
    private Supplier $defaultSupplier;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        $this->category = Category::create(['name' => 'Tablets']);
        $this->brand = Brand::create(['name' => 'GSK']);
        $this->defaultSupplier = Supplier::create(['name' => 'Default Supplier']);

        Setting::create([
            'store_name'   => 'Al-Shifa Pharmacy & Mart',
            'phone_number' => '0300-1112223',
            'address'      => 'Main Boulevard, Lahore',
        ]);

        $permissions = [
            'sale-invoice.view',
            'customer-ledger.view',
            'supplier-ledger.view',
            'payment.view',
            'purchase-order.view',
        ];

        foreach ($permissions as $perm) {
            Permission::findOrCreate($perm, 'sanctum');
        }

        $this->authorizedUser = User::create([
            'name'     => 'Authorized Manager',
            'email'    => 'manager@karobar.test',
            'password' => bcrypt('password'),
        ]);
        $this->authorizedUser->givePermissionTo($permissions);

        $this->unauthorizedUser = User::create([
            'name'     => 'Restricted User',
            'email'    => 'restricted@karobar.test',
            'password' => bcrypt('password'),
        ]);
    }

    public function test_phone_normalization_converts_local_and_international_formats(): void
    {
        $service = app(DocumentShareService::class);

        $this->assertSame('923001234567', $service->normalizePhone('0300-1234567'));
        $this->assertSame('923219876543', $service->normalizePhone('+92 321 9876543'));
        $this->assertSame('923331112233', $service->normalizePhone('00923331112233'));
        $this->assertSame('923001234567', $service->normalizePhone('3001234567'));
        $this->assertSame('', $service->normalizePhone(null));
    }

    public function test_sale_invoice_metadata_pdf_and_signed_public_link(): void
    {
        Sanctum::actingAs($this->authorizedUser);

        $customer = Customer::create([
            'name'    => 'Tariq Mehmood',
            'phone'   => '0300-7654321',
            'email'   => 'tariq@example.com',
            'address' => 'Model Town',
        ]);

        $product = Product::create([
            'product_code'        => 'PRD-001',
            'barcode'             => '8964001112221',
            'name'                => 'Panadol 500mg',
            'category_id'         => $this->category->id,
            'brand_id'            => $this->brand->id,
            'supplier_id'         => $this->defaultSupplier->id,
            'pack_size'           => 10,
            'unit_purchase_price' => 30,
            'unit_sale_price'     => 45,
            'quantity'            => 100,
        ]);

        $invoice = SaleInvoice::create([
            'user_id'        => $this->authorizedUser->id,
            'customer_id'    => $customer->id,
            'posted_number'  => 'SI-000105',
            'date'           => '2026-10-25',
            'invoice_type'   => 'credit',
            'gross_amount'   => 450,
            'total'          => 450,
            'total_receive'  => 150,
        ]);

        SaleInvoiceItem::create([
            'sale_invoice_id' => $invoice->id,
            'product_id'      => $product->id,
            'pack_size'       => 10,
            'quantity'        => 10,
            'price'           => 45,
            'sub_total'       => 450,
        ]);

        // 1. Metadata check (no URL link inside whatsapp_text)
        $metaRes = $this->getJson("/api/share/metadata?type=sale_invoice&id={$invoice->id}");
        $metaRes->assertOk()
            ->assertJsonPath('type', 'sale_invoice')
            ->assertJsonPath('normalized_phone', '923007654321')
            ->assertJsonPath('recipient_email', 'tariq@example.com');

        $this->assertStringContainsString('https://wa.me/923007654321?text=', $metaRes->json('whatsapp_url'));
        $this->assertStringContainsString('SI-000105', $metaRes->json('whatsapp_text'));
        $this->assertStringNotContainsString('http://', $metaRes->json('whatsapp_text'));
        $this->assertStringNotContainsString('https://', $metaRes->json('whatsapp_text'));

        // 2. Build & Send actual PDF for WhatsApp (client_file mode + saved PDF in storage)
        Storage::fake('public');
        $waRes = $this->postJson('/api/share/whatsapp', [
            'type'    => 'sale_invoice',
            'id'      => $invoice->id,
            'phone'   => '0300-7654321',
            'message' => $metaRes->json('whatsapp_text'),
        ]);
        $waRes->assertOk()
            ->assertJsonPath('status', 'pdf_built')
            ->assertJsonPath('filename', 'Sale_Invoice_SI-000105.pdf')
            ->assertJsonPath('normalized_phone', '923007654321');
        $this->assertNotEmpty($waRes->json('pdf_base64'));
        $this->assertStringStartsWith('%PDF-', base64_decode($waRes->json('pdf_base64')));
        Storage::disk('public')->assertExists('shared-pdfs/Sale_Invoice_SI-000105.pdf');

        // 3. Direct WhatsApp Cloud API PDF document dispatch when credentials are configured
        config([
            'services.whatsapp.access_token'    => 'test-wa-token',
            'services.whatsapp.phone_number_id' => '1234567890',
            'services.whatsapp.api_version'     => 'v20.0',
        ]);
        Http::fake([
            'https://graph.facebook.com/v20.0/1234567890/media'    => Http::response(['id' => 'media_pdf_999'], 200),
            'https://graph.facebook.com/v20.0/1234567890/messages' => Http::response(['messages' => [['id' => 'wamid.test123']]], 200),
        ]);

        $waCloudRes = $this->postJson('/api/share/whatsapp', [
            'type'    => 'sale_invoice',
            'id'      => $invoice->id,
            'phone'   => '0300-7654321',
            'message' => 'Attached Sale Invoice PDF',
        ]);
        $waCloudRes->assertOk()
            ->assertJsonPath('status', 'sent_via_api')
            ->assertJsonPath('mode', 'cloud_api')
            ->assertJsonPath('whatsapp_msg_id', 'wamid.test123');

        // 4. Authenticated PDF download
        $pdfRes = $this->get("/api/share/pdf?type=sale_invoice&id={$invoice->id}&download=1");
        $pdfRes->assertOk();
        $this->assertStringContainsString('application/pdf', $pdfRes->headers->get('content-type'));

        // 3. Signed public PDF link works without auth
        $signedUrl = $metaRes->json('signed_pdf_url');
        $this->assertNotEmpty($signedUrl);

        $publicRes = $this->get($signedUrl);
        $publicRes->assertOk();
        $this->assertStringContainsString('application/pdf', $publicRes->headers->get('content-type'));

        // Tampered signature is rejected with 403
        $tamperedRes = $this->get($signedUrl . 'tampered');
        $tamperedRes->assertStatus(403);
    }

    public function test_customer_and_supplier_ledger_statement_share_and_email_dispatch(): void
    {
        Mail::fake();
        Sanctum::actingAs($this->authorizedUser);

        $customer = Customer::create([
            'name'  => 'Zainab Enterprises',
            'phone' => '0321-5556677',
            'email' => null,
        ]);

        CustomerLedger::create([
            'customer_id'     => $customer->id,
            'entry_type'      => 'manual',
            'is_manual'       => true,
            'entry_date'      => '2026-10-20',
            'posted_number'   => 'OB-01',
            'invoice_total'   => 5000,
            'total_received'  => 1000,
            'credited_amount' => 0,
            'description'     => 'Opening balance',
        ]);

        // Send Email with PDF & save_email=true
        $emailRes = $this->postJson('/api/share/email', [
            'type'        => 'customer_ledger',
            'customer_id' => $customer->id,
            'email'       => 'accounts@zainab.pk',
            'subject'     => 'Customer Statement - Zainab Enterprises',
            'message'     => 'Please find attached your statement.',
            'save_email'  => true,
        ]);

        $emailRes->assertOk()
            ->assertJsonPath('status', 'sent');

        Mail::assertSent(DocumentShareMail::class, function (DocumentShareMail $mail) {
            return $mail->hasTo('accounts@zainab.pk')
                && $mail->subjectLine === 'Customer Statement - Zainab Enterprises'
                && !empty($mail->pdfBytes);
        });

        // Verify email was saved back to customer profile
        $this->assertDatabaseHas('customers', [
            'id'    => $customer->id,
            'email' => 'accounts@zainab.pk',
        ]);

        // Supplier Ledger Statement + supplier email persistence
        $supplier = Supplier::create([
            'name'  => 'Meditech Distributors',
            'phone' => '0333-4445566',
        ]);

        SupplierLedger::create([
            'supplier_id'      => $supplier->id,
            'entry_type'       => 'manual',
            'is_manual'        => true,
            'entry_date'       => '2026-10-22',
            'posted_number'    => 'SUP-OB-1',
            'invoice_total'    => 12000,
            'total_paid'       => 2000,
            'debited_amount'   => 0,
            'credit_remaining' => 10000,
            'description'      => 'Opening supplier balance',
        ]);

        $supEmailRes = $this->postJson('/api/share/email', [
            'type'        => 'supplier_ledger',
            'supplier_id' => $supplier->id,
            'email'       => 'billing@meditech.pk',
            'subject'     => 'Supplier Statement',
            'message'     => 'Attached is our supplier statement.',
            'save_email'  => true,
        ]);

        $supEmailRes->assertOk();
        $this->assertDatabaseHas('suppliers', [
            'id'    => $supplier->id,
            'email' => 'billing@meditech.pk',
        ]);
    }

    public function test_payment_voucher_and_purchase_order_pdf_sharing(): void
    {
        Sanctum::actingAs($this->authorizedUser);

        $supplier = Supplier::create([
            'name'  => 'Prime Pharma',
            'phone' => '0345-1239876',
            'email' => 'orders@primepharma.pk',
        ]);

        $account = Account::create([
            'code'            => 'ACC-CASH',
            'name'            => 'Main Cash',
            'type'            => 'cash',
            'opening_balance' => 50000,
            'current_balance' => 50000,
            'is_active'       => true,
            'is_default_cash' => true,
        ]);

        $payment = Payment::create([
            'payment_number'   => 'PAY-2026-0001',
            'party_type'       => 'supplier',
            'party_id'         => $supplier->id,
            'payment_date'     => '2026-10-25',
            'amount'           => 7500,
            'allocated_amount' => 7500,
            'unapplied_amount' => 0,
            'payment_method'   => 'cash',
            'account_id'       => $account->id,
            'status'           => 'posted',
            'created_by_id'    => $this->authorizedUser->id,
        ]);

        $payMeta = $this->getJson("/api/share/metadata?type=payment&id={$payment->id}");
        $payMeta->assertOk()
            ->assertJsonPath('normalized_phone', '923451239876')
            ->assertJsonPath('recipient_email', 'orders@primepharma.pk');

        $payPdf = $this->get("/api/share/pdf?type=payment&id={$payment->id}");
        $payPdf->assertOk();

        // Purchase Order test
        $product = Product::create([
            'product_code'        => 'PRD-002',
            'barcode'             => '8964001112222',
            'name'                => 'Augmentin 625mg',
            'category_id'         => $this->category->id,
            'brand_id'            => $this->brand->id,
            'supplier_id'         => $supplier->id,
            'pack_size'           => 6,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 65,
            'quantity'            => 30,
        ]);

        $po = PurchaseOrder::create([
            'date'                    => '2026-10-25',
            'expected_received_date'  => '2026-10-30',
            'supplier_id'             => $supplier->id,
            'status'                  => 'ordered',
            'reference'               => 'REF-PO-99',
            'created_by'              => $this->authorizedUser->id,
        ]);

        PurchaseOrderItem::create([
            'purchase_order_id'        => $po->id,
            'product_id'               => $product->id,
            'ordered_quantity'         => 60,
            'pack_size'                => 6,
            'unit_purchase_price'      => 50,
            'item_discount_percentage' => 0,
            'unit_bonus'               => 0,
        ]);

        $poMeta = $this->getJson("/api/share/metadata?type=purchase_order&id={$po->id}");
        $poMeta->assertOk()
            ->assertJsonPath('recipient_name', 'Prime Pharma');
        $this->assertStringContainsString('3,000.00', $poMeta->json('whatsapp_text'));

        $poPdf = $this->get("/api/share/pdf?type=purchase_order&id={$po->id}");
        $poPdf->assertOk();
    }

    public function test_unauthorized_user_cannot_access_document_share_endpoints(): void
    {
        Sanctum::actingAs($this->unauthorizedUser);

        $customer = Customer::create(['name' => 'Test Customer']);

        $res = $this->getJson("/api/share/metadata?type=customer_ledger&customer_id={$customer->id}");
        $res->assertStatus(403);
    }
}
