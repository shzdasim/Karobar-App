<?php

namespace Tests\Feature;

use App\Models\BarcodeTemplate;
use App\Models\Product;
use App\Models\Batch;
use App\Models\Category;
use App\Models\Brand;
use App\Models\Supplier;
use App\Models\Setting;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class BarcodeLabelStudioTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);
        \Illuminate\Support\Facades\Gate::before(fn () => true);

        $this->user = User::forceCreate([
            'name' => 'Barcode Tester',
            'email' => 'barcode@karobar.test',
            'password' => bcrypt('password123'),
        ]);
        $this->actingAs($this->user, 'sanctum');

        Setting::create([
            'store_name' => 'Al-Noor Pharmacy & Superstore',
            'phone_number' => '+92 300 1234567',
            'currency_symbol' => 'Rs.',
            'currency_position' => 'before',
            'currency_decimal_places' => 2,
        ]);
    }

    public function test_can_fetch_barcode_templates_and_store_info(): void
    {
        $response = $this->getJson('/api/barcodes/templates');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'templates',
                'store_info' => [
                    'store_name',
                    'phone_number',
                    'currency_symbol',
                    'currency_position',
                    'currency_decimal_places',
                ],
            ]);

        $this->assertEquals('Al-Noor Pharmacy & Superstore', $response->json('store_info.store_name'));
        $this->assertEquals('Rs.', $response->json('store_info.currency_symbol'));
    }

    public function test_can_save_custom_barcode_template(): void
    {
        $payload = [
            'name' => 'Custom Vial Sticker 35x20mm',
            'paper_type' => 'roll',
            'width_mm' => 35.0,
            'height_mm' => 20.0,
            'orientation' => 'landscape',
            'columns' => 1,
            'rows' => 1,
            'gap_x_mm' => 0,
            'gap_y_mm' => 3.0,
            'margin_top_mm' => 1.0,
            'margin_left_mm' => 1.0,
            'margin_right_mm' => 1.0,
            'margin_bottom_mm' => 1.0,
            'layout_options' => [
                'show_store_name' => true,
                'show_product_name' => true,
                'show_barcode' => true,
                'barcode_height' => 20,
                'show_price' => true,
                'show_batch' => true,
                'show_expiry' => true,
                'expiry_format' => 'MM/YY',
            ],
            'is_default' => false,
        ];

        $response = $this->postJson('/api/barcodes/templates', $payload);

        $response->assertStatus(201)
            ->assertJsonFragment([
                'name' => 'Custom Vial Sticker 35x20mm',
            ]);

        $this->assertDatabaseHas('barcode_templates', [
            'name' => 'Custom Vial Sticker 35x20mm',
            'paper_type' => 'roll',
        ]);
    }

    public function test_can_update_custom_barcode_template(): void
    {
        $template = BarcodeTemplate::create([
            'name' => 'Old Label',
            'paper_type' => 'roll',
            'width_mm' => 40.0,
            'height_mm' => 25.0,
            'orientation' => 'landscape',
            'columns' => 1,
            'rows' => 1,
            'gap_x_mm' => 0,
            'gap_y_mm' => 2.0,
            'margin_top_mm' => 1.0,
            'margin_left_mm' => 1.0,
            'margin_right_mm' => 1.0,
            'margin_bottom_mm' => 1.0,
            'is_default' => false,
        ]);

        $response = $this->putJson("/api/barcodes/templates/{$template->id}", [
            'name' => 'Updated Shelf Label',
            'width_mm' => 45.0,
        ]);

        $response->assertStatus(200)
            ->assertJsonFragment([
                'name' => 'Updated Shelf Label',
            ]);

        $this->assertDatabaseHas('barcode_templates', [
            'id' => $template->id,
            'name' => 'Updated Shelf Label',
            'width_mm' => 45.0,
        ]);
    }

    public function test_can_delete_custom_barcode_template(): void
    {
        $t1 = BarcodeTemplate::create([
            'name' => 'Preset 1',
            'paper_type' => 'roll',
            'width_mm' => 50,
            'height_mm' => 25,
            'is_default' => true,
        ]);

        $t2 = BarcodeTemplate::create([
            'name' => 'Preset 2 (to delete)',
            'paper_type' => 'roll',
            'width_mm' => 40,
            'height_mm' => 20,
            'is_default' => false,
        ]);

        $response = $this->deleteJson("/api/barcodes/templates/{$t2->id}");

        $response->assertStatus(200);
        $this->assertDatabaseMissing('barcode_templates', ['id' => $t2->id]);
    }

    public function test_can_search_products_with_batches_for_printing(): void
    {
        $category = Category::create(['name' => 'General Pharmaceuticals']);
        $brand = Brand::create(['name' => 'GSK']);
        $supplier = Supplier::create(['name' => 'Distributor 1']);

        $product = Product::create([
            'name' => 'Augmentin 625mg Tablets',
            'product_code' => 'AUG-625',
            'barcode' => 'PRD8837192',
            'category_id' => $category->id,
            'brand_id' => $brand->id,
            'supplier_id' => $supplier->id,
            'unit_sale_price' => 480.0,
            'whole_sale_unit_price' => 440.0,
            'quantity' => 25,
            'pack_size' => 10,
        ]);

        Batch::create([
            'product_id' => $product->id,
            'batch_number' => 'BN-9021',
            'expiry_date' => '2028-06-30',
            'quantity' => 25,
        ]);

        $response = $this->getJson('/api/barcodes/products-search?search=Augmentin');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'products' => [
                    '*' => [
                        'id',
                        'name',
                        'barcode',
                        'product_code',
                        'unit_sale_price',
                        'batches' => [
                            '*' => [
                                'id',
                                'batch_number',
                                'expiry_date',
                            ],
                        ],
                    ],
                ],
            ]);

        $products = $response->json('products');
        $this->assertCount(1, $products);
        $this->assertEquals('Augmentin 625mg Tablets', $products[0]['name']);
        $this->assertEquals('PRD8837192', $products[0]['barcode']);
        $this->assertEquals('BN-9021', $products[0]['batches'][0]['batch_number']);
    }

    public function test_can_fetch_purchase_orders_for_barcode_printing(): void
    {
        $supplier = Supplier::create(['name' => 'Abbott Laboratories']);
        $brand = Brand::create(['name' => 'Brufen']);
        $category = Category::create(['name' => 'Analgesics']);

        $product = Product::create([
            'name' => 'Brufen 400mg Tablets',
            'product_code' => 'BRU-400',
            'barcode' => '8901234567890',
            'category_id' => $category->id,
            'brand_id' => $brand->id,
            'supplier_id' => $supplier->id,
            'unit_sale_price' => 12.0,
            'whole_sale_unit_price' => 10.0,
            'quantity' => 100,
            'pack_size' => 10,
        ]);

        $po = \App\Models\PurchaseOrder::create([
            'date' => '2026-10-24',
            'supplier_id' => $supplier->id,
            'status' => 'ordered',
            'reference' => 'PO-REF-001',
        ]);

        \App\Models\PurchaseOrderItem::create([
            'purchase_order_id' => $po->id,
            'product_id' => $product->id,
            'ordered_quantity' => 50,
            'pack_size' => 10,
            'unit_purchase_price' => 8.0,
        ]);

        $response = $this->getJson('/api/barcodes/purchase-orders');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'purchase_orders' => [
                    '*' => [
                        'id',
                        'number',
                        'date',
                        'supplier' => ['id', 'name'],
                        'items' => [
                            '*' => [
                                'id',
                                'product_id',
                                'ordered_quantity',
                                'product' => [
                                    'id',
                                    'name',
                                    'barcode',
                                    'brand' => ['id', 'name'],
                                ],
                            ],
                        ],
                    ],
                ],
            ]);

        $pos = $response->json('purchase_orders');
        $this->assertNotEmpty($pos);
        $this->assertEquals($po->id, $pos[0]['id']);
        $this->assertEquals('Abbott Laboratories', $pos[0]['supplier']['name']);
        $this->assertEquals('Brufen', $pos[0]['items'][0]['product']['brand']['name']);
        $this->assertEquals(50, $pos[0]['items'][0]['ordered_quantity']);
    }

    public function test_can_filter_purchase_orders_by_supplier_and_brand_for_barcode_printing(): void
    {
        $supplier1 = Supplier::create(['name' => 'GSK Pakistan']);
        $supplier2 = Supplier::create(['name' => 'Pfizer']);
        $brand1 = Brand::create(['name' => 'Panadol']);
        $brand2 = Brand::create(['name' => 'Ponstan']);
        $category = Category::create(['name' => 'Medicines']);

        $p1 = Product::create([
            'name' => 'Panadol 500mg',
            'product_code' => 'PAN-500',
            'barcode' => '111222333',
            'category_id' => $category->id,
            'brand_id' => $brand1->id,
            'supplier_id' => $supplier1->id,
            'unit_sale_price' => 5.0,
            'quantity' => 200,
            'pack_size' => 10,
        ]);

        $p2 = Product::create([
            'name' => 'Ponstan Forte',
            'product_code' => 'PON-FORTE',
            'barcode' => '444555666',
            'category_id' => $category->id,
            'brand_id' => $brand2->id,
            'supplier_id' => $supplier2->id,
            'unit_sale_price' => 15.0,
            'quantity' => 150,
            'pack_size' => 10,
        ]);

        $po1 = \App\Models\PurchaseOrder::create([
            'date' => '2026-10-24',
            'supplier_id' => $supplier1->id,
            'status' => 'ordered',
        ]);
        \App\Models\PurchaseOrderItem::create([
            'purchase_order_id' => $po1->id,
            'product_id' => $p1->id,
            'ordered_quantity' => 100,
            'pack_size' => 1,
            'unit_purchase_price' => 3.5,
        ]);

        $po2 = \App\Models\PurchaseOrder::create([
            'date' => '2026-10-24',
            'supplier_id' => $supplier2->id,
            'status' => 'ordered',
        ]);
        \App\Models\PurchaseOrderItem::create([
            'purchase_order_id' => $po2->id,
            'product_id' => $p2->id,
            'ordered_quantity' => 40,
            'pack_size' => 1,
            'unit_purchase_price' => 10.0,
        ]);

        // Filter by supplier1
        $resSup = $this->getJson("/api/barcodes/purchase-orders?supplier_id={$supplier1->id}");
        $resSup->assertStatus(200);
        $this->assertCount(1, $resSup->json('purchase_orders'));
        $this->assertEquals($po1->id, $resSup->json('purchase_orders.0.id'));

        // Filter by brand2
        $resBrand = $this->getJson("/api/barcodes/purchase-orders?brand_id={$brand2->id}");
        $resBrand->assertStatus(200);
        $this->assertCount(1, $resBrand->json('purchase_orders'));
        $this->assertEquals($po2->id, $resBrand->json('purchase_orders.0.id'));
    }
}
