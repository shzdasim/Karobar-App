<?php

namespace Tests\Feature;

use App\Models\Customer;
use App\Models\CustomerLedger;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class CreditInvoiceOverpaymentTest extends TestCase
{
    protected User $user;
    protected Customer $customer;
    protected Product $product;

    protected function setUp(): void
    {
        parent::setUp();

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
            '2025_09_08_012037_add_total_paid_to_purchase_invoices_table.php',
            '2025_09_26_000000_add_invoice_type_to_purchase_invoices_table.php',
            '2025_08_30_171505_create_sale_invoices_table.php',
            '2025_08_30_171513_create_sale_invoice_items_table.php',
            '2025_09_01_074800_create_sale_returns_table.php',
            '2025_09_01_074814_create_sale_return_items_table.php',
            '2025_09_08_162536_add_total_receive_to_sale_invoices_table.php',
            '2025_09_25_000000_add_invoice_type_to_sale_invoices_table.php',
            '2026_04_02_000000_add_sale_type_to_sale_invoices_table.php',
            '2026_04_02_000001_add_sale_type_to_sale_invoice_items_table.php',
            '2026_04_02_000003_add_wholesale_type_to_sale_invoices_table.php',
            '2026_10_13_000006_add_sale_cost_snapshots.php',
            '2025_09_08_171931_create_customer_ledgers_table.php',
            '2025_09_08_013727_create_supplier_ledgers_table.php',
            '2025_09_21_102824_create_licenses_table.php',
            '2026_10_23_000003_create_document_histories_and_invoice_reversals.php',
            '2026_10_23_000004_create_payments_and_allocations_tables.php',
            '2026_10_23_000005_add_payment_method_to_invoices_table.php',
            '2026_10_23_000006_create_cash_drawer_tables.php',
        ] as $file) {
            (require database_path('migrations/' . $file))->up();
        }

        Gate::before(fn () => true);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $this->user = User::forceCreate([
            'name'     => 'Cashier Charlie',
            'email'    => 'charlie@test.local',
            'password' => bcrypt('password'),
        ]);

        $this->actingAs($this->user);

        DB::table('categories')->insert(['id' => 1, 'name' => 'General Cat']);
        DB::table('brands')->insert(['id' => 1, 'name' => 'General Brand']);
        DB::table('suppliers')->insert(['id' => 1, 'name' => 'General Supplier']);

        $this->customer = Customer::create([
            'name' => 'Regular Buyer',
            'phone' => '03001234567',
            'customer_type' => 'retail',
        ]);

        $this->product = Product::create([
            'category_id'         => 1,
            'brand_id'            => 1,
            'supplier_id'         => 1,
            'name'                => 'Panadol 500mg',
            'product_code'        => 'PAN-01',
            'barcode'             => 'BC-PAN-001',
            'pack_size'           => 1,
            'quantity'            => 100,
            'unit_purchase_price' => 50,
            'unit_sale_price'     => 100,
        ]);
    }

    public function test_credit_sale_overpayment_records_invoice_and_payment_in_customer_ledger(): void
    {
        // Bill total: 500 (5 items * 100)
        // Cash received: 1000
        $payload = [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'credit',
            'sale_type'      => 'retail',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 1000,
            'items'          => [
                [
                    'product_id'       => $this->product->id,
                    'quantity'         => 5,
                    'price'            => 100,
                    'sub_total'        => 500,
                    'current_quantity' => 100,
                ],
            ],
        ];

        $response = $this->postJson('/api/sale-invoices', $payload);
        $response->assertStatus(201);
        $invoiceId = $response->json('id');

        $invoice = SaleInvoice::find($invoiceId);
        $this->assertNotNull($invoice);
        $this->assertEquals(500, (float)$invoice->total);
        $this->assertEquals(1000, (float)$invoice->total_receive);

        // Check CustomerLedger records
        $ledgerEntries = CustomerLedger::where('customer_id', $this->customer->id)->get();
        $this->assertCount(2, $ledgerEntries);

        $invoiceEntry = $ledgerEntries->firstWhere('entry_type', 'invoice');
        $this->assertNotNull($invoiceEntry);
        $this->assertEquals(500, (float)$invoiceEntry->invoice_total);
        $this->assertEquals(500, (float)$invoiceEntry->total_received);
        $this->assertEquals(0, (float)$invoiceEntry->balance_remaining);

        $paymentEntry = $ledgerEntries->firstWhere('entry_type', 'payment');
        $this->assertNotNull($paymentEntry);
        $this->assertEquals(500, (float)$paymentEntry->credited_amount);
        $this->assertEquals('Inv #' . $invoice->posted_number, $paymentEntry->payment_ref);

        // Test customer ledger index view endpoint
        $ledgerRes = $this->getJson('/api/customer-ledger?customer_id=' . $this->customer->id);
        $ledgerRes->assertStatus(200);

        $summary = $ledgerRes->json('summary');
        $this->assertEquals(500, (float)$summary['total_invoiced']);
        $this->assertEquals(500, (float)$summary['received_on_invoice']);
        $this->assertEquals(500, (float)$summary['payments_credited']);
        $this->assertEquals(-500, (float)$summary['net_balance']);
    }

    public function test_updating_credit_sale_overpayment_syncs_ledger_entries(): void
    {
        $payload = [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'credit',
            'sale_type'      => 'retail',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 1000,
            'items'          => [
                [
                    'product_id'       => $this->product->id,
                    'quantity'         => 5,
                    'price'            => 100,
                    'sub_total'        => 500,
                    'current_quantity' => 100,
                ],
            ],
        ];

        $createRes = $this->postJson('/api/sale-invoices', $payload);
        $invoiceId = $createRes->json('id');
        $postedNumber = $createRes->json('posted_number');

        // Update to received = 600 (excess: 100)
        $updatePayload = array_merge($payload, [
            'posted_number' => $postedNumber,
            'total_receive' => 600,
            'reason'        => 'Correcting excess cash',
        ]);

        $updateRes = $this->putJson('/api/sale-invoices/' . $invoiceId, $updatePayload);
        $updateRes->assertStatus(200);

        $ledgerEntries = CustomerLedger::where('customer_id', $this->customer->id)->get();
        $this->assertCount(2, $ledgerEntries);

        $paymentEntry = $ledgerEntries->firstWhere('entry_type', 'payment');
        $this->assertEquals(100, (float)$paymentEntry->credited_amount);
    }

    public function test_overpayment_offsets_previous_debt_in_customer_ledger(): void
    {
        // 1. Previous invoice: Bill 500, received 0 -> Customer owes 500
        $firstInvoice = [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'credit',
            'sale_type'      => 'retail',
            'payment_method' => 'cash',
            'date'           => now()->subDays(2)->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 0,
            'items'          => [
                [
                    'product_id'       => $this->product->id,
                    'quantity'         => 5,
                    'price'            => 100,
                    'sub_total'        => 500,
                    'current_quantity' => 100,
                ],
            ],
        ];
        $this->postJson('/api/sale-invoices', $firstInvoice)->assertStatus(201);

        $ledger1 = $this->getJson('/api/customer-ledger?customer_id=' . $this->customer->id)->json('summary');
        $this->assertEquals(500, (float)$ledger1['net_balance']);

        // 2. Second invoice: Bill 500, customer gives 1000 rupees
        // 500 pays second invoice, 500 excess payment offsets previous debt
        $secondInvoice = [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'credit',
            'sale_type'      => 'retail',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 1000,
            'items'          => [
                [
                    'product_id'       => $this->product->id,
                    'quantity'         => 5,
                    'price'            => 100,
                    'sub_total'        => 500,
                    'current_quantity' => 95,
                ],
            ],
        ];
        $this->postJson('/api/sale-invoices', $secondInvoice)->assertStatus(201);

        // Verify ledger summary: Total Invoiced = 1000, Received on Invoices = 500, Payments Credited = 500
        // Net Balance = (1000 - 500) - 500 = 0.00! Debt is completely cleared!
        $ledger2 = $this->getJson('/api/customer-ledger?customer_id=' . $this->customer->id)->json('summary');
        $this->assertEquals(1000, (float)$ledger2['total_invoiced']);
        $this->assertEquals(500, (float)$ledger2['received_on_invoice']);
        $this->assertEquals(500, (float)$ledger2['payments_credited']);
        $this->assertEquals(0, (float)$ledger2['net_balance']);

        // Test rebuild endpoint recreates same balanced state
        $this->postJson('/api/customer-ledger/rebuild', ['customer_id' => $this->customer->id])->assertStatus(200);

        $ledger3 = $this->getJson('/api/customer-ledger?customer_id=' . $this->customer->id)->json('summary');
        $this->assertEquals(1000, (float)$ledger3['total_invoiced']);
        $this->assertEquals(500, (float)$ledger3['received_on_invoice']);
        $this->assertEquals(500, (float)$ledger3['payments_credited']);
        $this->assertEquals(0, (float)$ledger3['net_balance']);
    }

    public function test_reversing_overpaid_invoice_cleans_up_ledger_entries(): void
    {
        $payload = [
            'customer_id'    => $this->customer->id,
            'invoice_type'   => 'credit',
            'sale_type'      => 'retail',
            'payment_method' => 'cash',
            'date'           => now()->toDateString(),
            'gross_amount'   => 500,
            'total'          => 500,
            'total_receive'  => 1000,
            'items'          => [
                [
                    'product_id'       => $this->product->id,
                    'quantity'         => 5,
                    'price'            => 100,
                    'sub_total'        => 500,
                    'current_quantity' => 100,
                ],
            ],
        ];

        $createRes = $this->postJson('/api/sale-invoices', $payload);
        $invoiceId = $createRes->json('id');

        $this->assertCount(2, CustomerLedger::where('customer_id', $this->customer->id)->get());

        // Reverse invoice
        $this->postJson('/api/sale-invoices/' . $invoiceId . '/reverse', [
            'reason' => 'Customer cancelled and refunded',
        ])->assertStatus(200);

        // Both invoice entry and excess payment entry should be removed
        $this->assertCount(0, CustomerLedger::where('customer_id', $this->customer->id)->get());
    }
}
