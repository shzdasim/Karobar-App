<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\PurchaseInvoiceItem;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\SaleReturn;
use App\Models\SaleReturnItem;
use App\Models\Setting;
use App\Models\StockDisposal;
use App\Models\StockDisposalItem;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class NarcoticsReportTest extends TestCase
{
    protected User $user;

    protected function setUp(): void
    {
        parent::setUp();

        \Illuminate\Support\Facades\Schema::disableForeignKeyConstraints();

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
            '2025_08_30_171505_create_sale_invoices_table.php',
            '2025_08_30_171513_create_sale_invoice_items_table.php',
            '2025_09_01_074800_create_sale_returns_table.php',
            '2025_09_01_074814_create_sale_return_items_table.php',
            '2025_09_02_133214_create_settings_table.php',
            '2025_09_06_170858_create_stock_adjustments_table.php',
            '2025_09_06_171041_create_stock_adjustment_items_table.php',
            '2025_09_08_162536_add_total_receive_to_sale_invoices_table.php',
            '2025_09_25_000000_add_invoice_type_to_sale_invoices_table.php',
            '2025_09_26_000000_add_invoice_type_to_purchase_invoices_table.php',
            '2026_10_23_000003_create_document_histories_and_invoice_reversals.php',
            '2026_10_25_000004_create_stock_disposals_tables.php',
            '2026_11_15_220001_add_narcotics_regulatory_fields_to_sale_invoices_table.php',
        ] as $file) {
            (require database_path('migrations/' . $file))->up();
        }

        $this->user = User::forceCreate([
            'name'     => 'RPh. Ahmad Raza (Cat-A)',
            'email'    => 'pharmacist@karobar.test',
            'password' => bcrypt('password'),
        ]);

        $this->actingAs($this->user);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);
        Gate::before(fn () => true);
    }

    public function test_pakistani_statutory_narcotics_register_tracks_physical_running_balance_and_excludes_monetary_values(): void
    {
        Setting::create([
            'store_name'     => 'Al-Shifa Model Pharmacy',
            'license_number' => 'Form-9/LHR/2026/8841',
            'address'        => 'Mall Road, Lahore',
            'phone_number'   => '042-31112223',
        ]);

        $brand = Brand::create(['name' => 'Getz Pharma']);
        $category = Category::create(['name' => 'Schedule G Narcotics']);
        $supplier = Supplier::create([
            'name'    => 'Muller & Phipps Pakistan (Pvt) Ltd',
            'phone'   => '042-35712345',
            'address' => 'Industrial Area, Kot Lakhpat, Lahore',
        ]);

        $patientCustomer = Customer::create([
            'name'    => 'Muhammad Bilal',
            'phone'   => '0300-8889991',
            'address' => 'House 14, Model Town, Lahore',
        ]);

        // Narcotic Product: Nalbuphine HCl 10mg/ml Injection (Pack of 10 Ampoules)
        // Suppose Opening B/F on 2026-11-01 was 20 units:
        // + 100 purchased (2026-11-05)
        // - 15 dispensed (2026-11-10)
        // + 5 returned by patient (2026-11-12)
        // - 10 destroyed under Drug Inspector witness (2026-11-14)
        // = 100 closing units on hand
        $narcotic = Product::create([
            'product_code'        => 'NARC-001',
            'name'                => 'Nubiget 10mg Injection',
            'formulation'         => 'Nalbuphine HCl 10mg/ml',
            'pack_size'           => 10,
            'quantity'            => 100, // current on hand after movements
            'pack_purchase_price' => 1200,
            'pack_sale_price'     => 1500,
            'unit_purchase_price' => 120,
            'unit_sale_price'     => 150,
            'avg_price'           => 120,
            'narcotic'            => 'yes',
            'brand_id'            => $brand->id,
            'category_id'         => $category->id,
            'supplier_id'         => $supplier->id,
            'rack'                => 'Vault-A1 (Double-Lock)',
            'barcode'             => '8964001234567',
        ]);

        Batch::create([
            'product_id'   => $narcotic->id,
            'batch_number' => 'NBG-2609',
            'expiry_date'  => '2028-06-30',
            'quantity'     => 100,
        ]);

        // 1. Inward Purchase (GRN) on 2026-11-05: +100 units (10 packs)
        $pi = PurchaseInvoice::create([
            'supplier_id'    => $supplier->id,
            'posted_number'  => 'PRINV-101',
            'posted_date'    => '2026-11-05',
            'invoice_number' => 'MP-BILL-9088',
            'invoice_amount' => 12000,
            'total_amount'   => 12000,
            'status'         => 'posted',
        ]);
        PurchaseInvoiceItem::create([
            'purchase_invoice_id' => $pi->id,
            'product_id'          => $narcotic->id,
            'batch'               => 'NBG-2609',
            'expiry'              => '2028-06-30',
            'pack_size'           => 10,
            'pack_quantity'       => 10,
            'unit_quantity'       => 0,
            'quantity'            => 100,
            'unit_bonus'          => 0,
            'pack_purchase_price' => 1200,
            'unit_purchase_price' => 120,
            'pack_sale_price'     => 1500,
            'unit_sale_price'     => 150,
            'margin'              => 20,
            'avg_price'           => 120,
            'sub_total'           => 12000,
        ]);

        // 2. Outward Dispensing on 2026-11-10: -15 units (initially missing Doctor/PMDC/CNIC)
        $si = SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $patientCustomer->id,
            'invoice_type'  => 'debit',
            'posted_number' => 'SINV-501',
            'date'          => '2026-11-10',
            'doctor_name'   => null,
            'patient_name'  => 'Muhammad Bilal',
            'gross_amount'  => 2250,
            'total'         => 2250,
            'total_receive' => 2250,
            'status'        => 'posted',
        ]);
        SaleInvoiceItem::create([
            'sale_invoice_id'          => $si->id,
            'product_id'               => $narcotic->id,
            'pack_size'                => 10,
            'batch_number'             => 'NBG-2609',
            'expiry'                   => '2028-06-30',
            'current_quantity'         => 120,
            'quantity'                 => 15,
            'price'                    => 150,
            'item_discount_percentage' => 0,
            'sub_total'                => 2250,
        ]);

        // 3. Patient Return on 2026-11-12: +5 units
        $sr = SaleReturn::create([
            'user_id'         => $this->user->id,
            'customer_id'     => $patientCustomer->id,
            'sale_invoice_id' => $si->id,
            'posted_number'   => 'SRET-201',
            'date'            => '2026-11-12',
            'gross_total'     => 750,
            'total'           => 750,
        ]);
        SaleReturnItem::create([
            'sale_return_id'       => $sr->id,
            'product_id'           => $narcotic->id,
            'batch_number'         => 'NBG-2609',
            'expiry'               => '2028-06-30',
            'unit_return_quantity' => 5,
            'unit_sale_price'      => 150,
            'sub_total'            => 750,
        ]);

        // 4. Witnessed Destruction on 2026-11-14: -10 units
        $disp = StockDisposal::create([
            'disposal_number'     => 'DSP-0001',
            'disposal_date'       => '2026-11-14',
            'status'              => 'disposed',
            'cause'               => 'broken_in_transit',
            'disposal_method'     => 'drug_inspector_destruction',
            'reference_no'        => 'FORM-DI-772',
            'witness_name'        => 'Mr. Kamran Cheema',
            'witness_designation' => 'District Drug Inspector, Lahore',
            'has_narcotics'       => true,
            'created_by'          => $this->user->id,
            'disposed_by'         => $this->user->id,
        ]);
        StockDisposalItem::create([
            'stock_disposal_id'   => $disp->id,
            'product_id'          => $narcotic->id,
            'batch_number'        => 'NBG-2609',
            'expiry_date'         => '2028-06-30',
            'dispose_qty'         => 10,
            'unit_purchase_price' => 120,
            'unit_sale_price'     => 150,
            'cost_value'          => 1200,
            'retail_value'        => 1500,
        ]);

        // Fetch Narcotics Register
        $res = $this->getJson('/api/reports/narcotics?from=2026-11-01&to=2026-11-30');
        $res->assertOk();
        $payload = $res->json();

        // Verify store license header
        $this->assertSame('Form-9/LHR/2026/8841', $payload['store_license']['license_number']);

        // Verify ZERO monetary fields in summary, stock_ledger, dispensing_register, procurement_register
        $this->assertArrayNotHasKey('stock_cost_valuation', $payload['summary']);
        $this->assertArrayNotHasKey('stock_retail_valuation', $payload['summary']);
        $this->assertArrayNotHasKey('purchase_total_value', $payload['summary']);
        $this->assertArrayNotHasKey('dispensing_total_value', $payload['summary']);

        $ledgerRow = $payload['stock_ledger'][0];
        $this->assertArrayNotHasKey('cost_value', $ledgerRow);
        $this->assertArrayNotHasKey('retail_value', $ledgerRow);
        $this->assertSame(20, $ledgerRow['opening_qty']);
        $this->assertSame(100, $ledgerRow['purchased_qty']);
        $this->assertSame(5, $ledgerRow['sale_return_qty']);
        $this->assertSame(15, $ledgerRow['dispensed_qty']);
        $this->assertSame(10, $ledgerRow['disposed_qty']);
        $this->assertSame(100, $ledgerRow['closing_qty']);
        $this->assertSame('10 Packs (1×10)', $ledgerRow['closing_pack_breakdown']);

        // Verify Chronological Running Balance sequence:
        // Row 0: OPENING_BF -> 20
        // Row 1: PURCHASE_IN (+100) -> 120
        // Row 2: DISPENSED_OUT (-15) -> 105
        // Row 3: PATIENT_RETURN_IN (+5) -> 110
        // Row 4: DESTRUCTION_OUT (-10) -> 100
        $chrono = $payload['chronological_register'];
        $this->assertCount(5, $chrono);
        $this->assertSame('OPENING_BF', $chrono[0]['event_type']);
        $this->assertSame(20, $chrono[0]['running_balance']);

        $this->assertSame('PURCHASE_IN', $chrono[1]['event_type']);
        $this->assertSame(100, $chrono[1]['qty_in']);
        $this->assertSame(120, $chrono[1]['running_balance']);

        $this->assertSame('DISPENSED_OUT', $chrono[2]['event_type']);
        $this->assertSame(15, $chrono[2]['qty_out']);
        $this->assertSame(105, $chrono[2]['running_balance']);
        $this->assertFalse($chrono[2]['is_rx_compliant']); // Doctor name is missing initially

        $this->assertSame('PATIENT_RETURN_IN', $chrono[3]['event_type']);
        $this->assertSame(5, $chrono[3]['qty_in']);
        $this->assertSame(110, $chrono[3]['running_balance']);

        $this->assertSame('DESTRUCTION_OUT', $chrono[4]['event_type']);
        $this->assertSame(10, $chrono[4]['qty_out']);
        $this->assertSame(100, $chrono[4]['running_balance']);

        // Now test updating Pakistani Statutory Rx, PM&DC & Patient CNIC via PUT /api/sale-invoices/{id}/meta
        $metaRes = $this->putJson("/api/sale-invoices/{$si->id}/meta", [
            'doctor_name'     => 'Dr. Tariq Mahmood',
            'doctor_pmdc'     => 'PMDC-45821-P',
            'doctor_hospital' => 'Mayo Hospital, Lahore',
            'patient_name'    => 'Muhammad Bilal',
            'patient_cnic'    => '35202-1234567-1',
            'patient_address' => 'House 14, Model Town, Lahore',
            'prescription_no' => 'RX-2026-991',
            'pharmacist_name' => 'RPh. Ahmad Raza (Reg # A-9421)',
        ]);
        $metaRes->assertOk();

        // Re-fetch Narcotics Register and verify 100% Rx & CNSA compliance
        $resAfter = $this->getJson('/api/reports/narcotics?from=2026-11-01&to=2026-11-30');
        $resAfter->assertOk();
        $afterData = $resAfter->json();

        $this->assertSame(100.0, (float) $afterData['summary']['compliance_rate']);
        $this->assertSame(0, $afterData['summary']['missing_rx_sales_count']);
        $this->assertSame(1, $afterData['summary']['full_cnsa_compliant_count']);

        $dispRow = $afterData['dispensing_register'][0];
        $this->assertTrue($dispRow['is_rx_compliant']);
        $this->assertTrue($dispRow['has_full_cnsa_particulars']);
        $this->assertSame('Dr. Tariq Mahmood', $dispRow['doctor_name']);
        $this->assertSame('PMDC-45821-P', $dispRow['doctor_pmdc']);
        $this->assertSame('Mayo Hospital, Lahore', $dispRow['doctor_hospital']);
        $this->assertSame('35202-1234567-1', $dispRow['patient_cnic']);
        $this->assertSame('RX-2026-991', $dispRow['prescription_no']);
        $this->assertSame('RPh. Ahmad Raza (Reg # A-9421)', $dispRow['dispensed_by']);
        $this->assertArrayNotHasKey('price', $dispRow);
        $this->assertArrayNotHasKey('sub_total', $dispRow);

        // Verify PDF endpoint generates valid PDF
        $pdfRes = $this->get('/api/reports/narcotics/pdf?from=2026-11-01&to=2026-11-30');
        $pdfRes->assertOk();
    }
}
