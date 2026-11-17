<?php

namespace Tests\Feature;

use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\UploadedFile;
use Illuminate\Support\Facades\Storage;
use Tests\TestCase;

class PharmacyClinicalAddonsTest extends TestCase
{
    use RefreshDatabase;

    protected User $admin;
    protected Brand $brand;
    protected Category $category;
    protected Supplier $supplier;
    protected Customer $customer;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware([
            \App\Http\Middleware\EnsureLicensed::class,
            \App\Http\Middleware\CheckTokenExpiry::class,
        ]);

        $this->admin = User::create([
            'name'     => 'Pharmacy Admin',
            'email'    => 'admin@pharmacy.test',
            'password' => bcrypt('secret123'),
        ]);

        app()[\Spatie\Permission\PermissionRegistrar::class]->forgetCachedPermissions();
        $role = \Spatie\Permission\Models\Role::findOrCreate('Admin', 'sanctum');
        $this->admin->assignRole($role);

        foreach (['settings.view', 'settings.update', 'product.create', 'product.update', 'sale-invoice.create', 'sale-invoice.update', 'sale-invoice.view'] as $perm) {
            \Spatie\Permission\Models\Permission::findOrCreate($perm, 'sanctum');
            $this->admin->givePermissionTo($perm);
        }

        $this->actingAs($this->admin);

        $this->brand = Brand::create(['name' => 'Abbott']);
        $this->category = Category::create(['name' => 'Syrups']);
        $this->supplier = Supplier::create(['name' => 'Muller & Phipps']);
        $this->customer = Customer::create(['name' => 'Walk-in Customer']);
    }

    public function test_optional_pharmacy_drug_schedule_default_dosage_and_sticker_printer(): void
    {
        $prodRes = $this->postJson('/api/products', [
            'product_code'        => 'PRD-CLIN-01',
            'barcode'             => '8964009988771',
            'name'                => 'Brufen Syrup 120ml',
            'formulation'         => 'Ibuprofen',
            'pack_size'           => 1,
            'quantity'            => 50,
            'pack_purchase_price' => 100,
            'unit_purchase_price' => 100,
            'pack_sale_price'     => 140,
            'unit_sale_price'     => 140,
            'avg_price'           => 100,
            'narcotic'            => 'no',
            'drug_schedule'       => 'schedule_h',
            'requires_cold_chain' => false,
            'default_dosage'      => '1 + 0 + 1 • کھانے کے بعد (After Meal)',
            'category_id'         => $this->category->id,
            'brand_id'            => $this->brand->id,
            'supplier_id'         => $this->supplier->id,
        ]);

        $prodRes->assertStatus(201)
            ->assertJsonPath('drug_schedule', 'schedule_h')
            ->assertJsonPath('default_dosage', '1 + 0 + 1 • کھانے کے بعد (After Meal)');

        $productId = $prodRes->json('id');

        // Create a Sale Invoice without specifying dosage_instruction — it inherits default_dosage automatically
        $invRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'sale_type'      => 'retail',
            'date'           => '2026-11-17',
            'patient_name'   => 'Zainab Bibi',
            'total_receive'  => 140,
            'items'          => [
                [
                    'product_id'    => $productId,
                    'pack_size'     => 1,
                    'unit_quantity' => 1,
                    'quantity'      => 1,
                    'price'         => 140,
                    'sub_total'     => 140,
                ],
            ],
        ]);

        $invRes->assertStatus(201);
        $invoiceId = $invRes->json('id');
        $invoice = SaleInvoice::with('items')->findOrFail($invoiceId);
        $item = $invoice->items->first();

        $this->assertSame('1 + 0 + 1 • کھانے کے بعد (After Meal)', $item->dosage_instruction);

        // Update dosage instruction via PUT /api/sale-invoices/{id}/dosages
        $dosageRes = $this->putJson("/api/sale-invoices/{$invoiceId}/dosages", [
            'items' => [
                [
                    'id'                      => $item->id,
                    'dosage_instruction'      => '1 + 1 + 1 • کھانے کے بعد (3 Times Daily)',
                    'save_as_product_default' => true,
                ],
            ],
        ]);

        $dosageRes->assertOk()->assertJsonPath('ok', true);
        $this->assertSame('1 + 1 + 1 • کھانے کے بعد (3 Times Daily)', $item->fresh()->dosage_instruction);
        $this->assertSame('1 + 1 + 1 • کھانے کے بعد (3 Times Daily)', Product::find($productId)->default_dosage);

        // Render thermal 50x25mm dosage stickers view
        $stickerHtml = $this->get("/sale-invoices/{$invoiceId}/dosage-labels")->assertOk()->getContent();
        $this->assertStringContainsString('Brufen Syrup 120ml', $stickerHtml);
        $this->assertStringContainsString('Zainab Bibi', $stickerHtml);
        $this->assertStringContainsString('1 + 1 + 1 • کھانے کے بعد (3 Times Daily)', $stickerHtml);

        // Save custom dosage sticker dimensions & design in Settings
        \App\Models\Setting::firstOrCreate(['id' => 1], ['printer_type' => 'thermal']);
        $this->postJson('/api/settings', [
            'printer_type'          => 'thermal',
            'dosage_sticker_config' => json_encode([
                'preset_key'        => '60x40',
                'width_mm'          => 60,
                'height_mm'         => 40,
                'template'          => 'bold',
                'font_scale'        => 'large',
                'show_shop_name'    => true,
                'show_invoice_meta' => true,
                'show_patient'      => true,
                'show_quantity'     => true,
                'show_batch'        => true,
                'show_expiry'       => true,
                'show_doctor'       => true,
                'footer_note'       => 'Store below 25C',
            ]),
        ])->assertOk();

        $this->getJson('/api/sale-system')
            ->assertOk()
            ->assertJsonPath('dosage_sticker_config.width_mm', 60)
            ->assertJsonPath('dosage_sticker_config.height_mm', 40)
            ->assertJsonPath('dosage_sticker_config.template', 'bold');

        $customStickerHtml = $this->get("/sale-invoices/{$invoiceId}/dosage-labels")->assertOk()->getContent();
        $this->assertStringContainsString('width: 60mm;', $customStickerHtml);
        $this->assertStringContainsString('min-height: 40mm;', $customStickerHtml);
        $this->assertStringContainsString('Store below 25C', $customStickerHtml);

        $labelPrinterHtml = $this->get("/sale-invoices/{$invoiceId}/dosage-labels?printer_mode=label_printer")->assertOk()->getContent();
        $this->assertStringContainsString('size: 60mm 40mm;', $labelPrinterHtml);
    }

    public function test_optional_prescription_attachment_upload_and_delete(): void
    {
        Storage::fake('public');

        $product = Product::create([
            'product_code'        => 'PRD-CLIN-02',
            'barcode'             => '8964009988772',
            'name'                => 'Lexotanil 3mg',
            'pack_size'           => 30,
            'quantity'            => 90,
            'unit_purchase_price' => 10,
            'unit_sale_price'     => 15,
            'avg_price'           => 10,
            'narcotic'            => 'yes',
            'drug_schedule'       => 'narcotic',
            'category_id'         => $this->category->id,
            'brand_id'            => $this->brand->id,
            'supplier_id'         => $this->supplier->id,
        ]);

        $invRes = $this->postJson('/api/sale-invoices', [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'debit',
            'payment_method' => 'cash',
            'sale_type'      => 'retail',
            'date'           => '2026-11-17',
            'total_receive'  => 150,
            'items'          => [
                [
                    'product_id'    => $product->id,
                    'pack_size'     => 30,
                    'unit_quantity' => 10,
                    'quantity'      => 10,
                    'price'         => 15,
                    'sub_total'     => 150,
                ],
            ],
        ])->assertStatus(201);

        $invoiceId = $invRes->json('id');

        $file = UploadedFile::fake()->create('prescription.jpg', 120, 'image/jpeg');
        $uploadRes = $this->postJson("/api/sale-invoices/{$invoiceId}/prescription", [
            'prescription'    => $file,
            'prescription_no' => 'RX-2026-889',
        ]);

        $uploadRes->assertOk()
            ->assertJsonPath('ok', true)
            ->assertJsonPath('prescription_no', 'RX-2026-889');

        $invoice = SaleInvoice::findOrFail($invoiceId);
        $this->assertNotEmpty($invoice->prescription_image_path);
        Storage::disk('public')->assertExists($invoice->prescription_image_path);

        // Delete attachment
        $this->deleteJson("/api/sale-invoices/{$invoiceId}/prescription")
            ->assertOk()
            ->assertJsonPath('ok', true);

        $this->assertNull($invoice->fresh()->prescription_image_path);
    }
}
