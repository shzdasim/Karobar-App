<?php

namespace Tests\Feature;

use App\Models\Customer;
use App\Models\CustomerLedger;
use App\Models\Payment;
use App\Models\PurchaseInvoice;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use App\Models\SupplierLedger;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class PaymentAndAllocationTest extends TestCase
{
    protected User $user;

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
            '2025_08_24_015207_create_purchase_returns_table.php',
            '2025_08_24_015215_create_purchase_return_items_table.php',
            '2025_08_30_171505_create_sale_invoices_table.php',
            '2025_08_30_171513_create_sale_invoice_items_table.php',
            '2025_09_08_012037_add_total_paid_to_purchase_invoices_table.php',
            '2025_09_08_013727_create_supplier_ledgers_table.php',
            '2025_09_08_162536_add_total_receive_to_sale_invoices_table.php',
            '2025_09_08_171931_create_customer_ledgers_table.php',
            '2025_09_25_000000_add_invoice_type_to_sale_invoices_table.php',
            '2025_09_26_000000_add_invoice_type_to_purchase_invoices_table.php',
            '2026_10_23_000003_create_document_histories_and_invoice_reversals.php',
            '2026_10_23_000004_create_payments_and_allocations_tables.php',
        ] as $file) {
            (require database_path('migrations/'.$file))->up();
        }

        $this->user = User::forceCreate([
            'name'     => 'Cashier Charlie',
            'email'    => 'charlie@test.test',
            'password' => bcrypt('password'),
        ]);

        $this->actingAs($this->user);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);
        Gate::before(fn () => true);
    }

    public function test_customer_payment_with_multi_invoice_allocation_updates_invoices_and_ledger(): void
    {
        $customer = Customer::create([
            'name'  => 'Test Customer',
            'phone' => '03001234567',
        ]);

        $invoice1 = SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $customer->id,
            'invoice_type'  => 'credit',
            'posted_number' => 'INV-001',
            'date'          => '2026-10-20',
            'gross_amount'  => 1000.00,
            'total'         => 1000.00,
            'total_receive' => 0.00,
            'status'        => 'posted',
        ]);

        $invoice2 = SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $customer->id,
            'invoice_type'  => 'credit',
            'posted_number' => 'INV-002',
            'date'          => '2026-10-21',
            'gross_amount'  => 500.00,
            'total'         => 500.00,
            'total_receive' => 0.00,
            'status'        => 'posted',
        ]);

        $response = $this->postJson('/api/payments', [
            'party_type'     => 'customer',
            'party_id'       => $customer->id,
            'payment_date'   => '2026-10-23',
            'amount'         => 1200.00,
            'payment_method' => 'bank_transfer',
            'reference'      => 'TXN-9988',
            'notes'          => 'Settlement for two invoices',
            'allocations'    => [
                ['invoice_id' => $invoice1->id, 'amount' => 1000.00],
                ['invoice_id' => $invoice2->id, 'amount' => 200.00],
            ],
        ]);

        $response->assertStatus(201);
        $data = $response->json('payment');

        $this->assertEquals(1200.00, $data['amount']);
        $this->assertEquals(1200.00, $data['allocated_amount']);
        $this->assertEquals(0.00, $data['unapplied_amount']);
        $this->assertEquals('posted', $data['status']);
        $this->assertCount(2, $data['allocations']);

        // Check invoices
        $invoice1->refresh();
        $this->assertEquals(1000.00, (float)$invoice1->total_receive);

        $invoice2->refresh();
        $this->assertEquals(200.00, (float)$invoice2->total_receive);

        // Check ledger sync
        $ledger = CustomerLedger::where('payment_id', $data['id'])->first();
        $this->assertNotNull($ledger);
        $this->assertEquals(1200.00, (float)$ledger->credited_amount);
        $this->assertStringContainsString('Bank transfer', $ledger->description);
    }

    public function test_customer_payment_with_unapplied_advance_retains_credit(): void
    {
        $customer = Customer::create([
            'name'  => 'Advance Customer',
            'phone' => '03009998877',
        ]);

        $invoice = SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $customer->id,
            'invoice_type'  => 'credit',
            'posted_number' => 'INV-ADV-1',
            'date'          => '2026-10-20',
            'gross_amount'  => 600.00,
            'total'         => 600.00,
            'total_receive' => 0.00,
            'status'        => 'posted',
        ]);

        $response = $this->postJson('/api/payments', [
            'party_type'     => 'customer',
            'party_id'       => $customer->id,
            'payment_date'   => '2026-10-23',
            'amount'         => 1000.00,
            'payment_method' => 'cash',
            'allocations'    => [
                ['invoice_id' => $invoice->id, 'amount' => 600.00],
            ],
        ]);

        $response->assertStatus(201);
        $payment = $response->json('payment');

        $this->assertEquals(1000.00, $payment['amount']);
        $this->assertEquals(600.00, $payment['allocated_amount']);
        $this->assertEquals(400.00, $payment['unapplied_amount']);
    }

    public function test_allocation_exceeding_invoice_due_balance_is_rejected(): void
    {
        $customer = Customer::create(['name' => 'Overpay Customer']);

        $invoice = SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $customer->id,
            'invoice_type'  => 'credit',
            'posted_number' => 'INV-OVER',
            'date'          => '2026-10-20',
            'gross_amount'  => 500.00,
            'total'         => 500.00,
            'total_receive' => 200.00, // Due is only 300
            'status'        => 'posted',
        ]);

        $response = $this->postJson('/api/payments', [
            'party_type'     => 'customer',
            'party_id'       => $customer->id,
            'payment_date'   => '2026-10-23',
            'amount'         => 500.00,
            'payment_method' => 'cash',
            'allocations'    => [
                ['invoice_id' => $invoice->id, 'amount' => 400.00], // 400 > 300 remaining
            ],
        ]);

        $response->assertStatus(422);
        $response->assertJsonValidationErrors(['allocations']);
    }

    public function test_idempotent_payment_submission_prevents_duplicate_charge(): void
    {
        $customer = Customer::create(['name' => 'Idempotent Customer']);

        $payload = [
            'party_type'      => 'customer',
            'party_id'        => $customer->id,
            'payment_date'    => '2026-10-23',
            'amount'          => 350.00,
            'payment_method'  => 'card',
            'idempotency_key' => 'unique-token-abc-123',
        ];

        $res1 = $this->postJson('/api/payments', $payload);
        $res1->assertStatus(201);
        $p1 = $res1->json('payment');

        // Retry same request
        $res2 = $this->postJson('/api/payments', $payload);
        $res2->assertStatus(201);
        $p2 = $res2->json('payment');

        $this->assertEquals($p1['id'], $p2['id']);
        $this->assertEquals(1, Payment::where('idempotency_key', 'unique-token-abc-123')->count());
    }

    public function test_supplier_payment_updates_purchase_invoice_and_supplier_ledger(): void
    {
        $supplier = Supplier::create([
            'name'  => 'Pharma Dist Wholesale',
            'phone' => '04211122233',
        ]);

        $pInvoice = PurchaseInvoice::create([
            'supplier_id'    => $supplier->id,
            'invoice_number' => 'SUP-BILL-100',
            'posted_number'  => 'PI-2026-0001',
            'posted_date'    => '2026-10-18',
            'invoice_amount' => 5000.00,
            'total_amount'   => 5000.00,
            'total_paid'     => 1000.00,
            'invoice_type'   => 'credit',
            'status'         => 'posted',
        ]);

        $response = $this->postJson('/api/payments', [
            'party_type'     => 'supplier',
            'party_id'       => $supplier->id,
            'payment_date'   => '2026-10-23',
            'amount'         => 4000.00,
            'payment_method' => 'cheque',
            'reference'      => 'CHQ-882211',
            'allocations'    => [
                ['invoice_id' => $pInvoice->id, 'amount' => 4000.00],
            ],
        ]);

        $response->assertStatus(201);
        $data = $response->json('payment');

        $this->assertEquals('supplier', $data['party_type']);
        $this->assertEquals(4000.00, $data['amount']);
        $this->assertEquals(4000.00, $data['allocated_amount']);

        $pInvoice->refresh();
        $this->assertEquals(5000.00, (float)$pInvoice->total_paid);

        $ledger = SupplierLedger::where('payment_id', $data['id'])->first();
        $this->assertNotNull($ledger);
        $this->assertEquals(4000.00, (float)$ledger->debited_amount);
    }

    public function test_payment_reversal_safely_unwinds_invoice_and_ledger_balances(): void
    {
        $customer = Customer::create(['name' => 'Reversal Customer']);

        $invoice = SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $customer->id,
            'invoice_type'  => 'credit',
            'posted_number' => 'INV-REV-01',
            'date'          => '2026-10-20',
            'gross_amount'  => 800.00,
            'total'         => 800.00,
            'total_receive' => 0.00,
            'status'        => 'posted',
        ]);

        $postRes = $this->postJson('/api/payments', [
            'party_type'     => 'customer',
            'party_id'       => $customer->id,
            'payment_date'   => '2026-10-23',
            'amount'         => 800.00,
            'payment_method' => 'cash',
            'allocations'    => [
                ['invoice_id' => $invoice->id, 'amount' => 800.00],
            ],
        ]);

        $paymentId = $postRes->json('payment.id');
        $invoice->refresh();
        $this->assertEquals(800.00, (float)$invoice->total_receive);
        $this->assertEquals(1, CustomerLedger::where('payment_id', $paymentId)->count());

        // Reverse the payment
        $revRes = $this->postJson("/api/payments/{$paymentId}/reverse", [
            'reason' => 'Cheque bounced by bank',
        ]);

        $revRes->assertStatus(200);
        $revData = $revRes->json('payment');
        $this->assertEquals('reversed', $revData['status']);
        $this->assertEquals('Cheque bounced by bank', $revData['reversal_reason']);

        // Check invoice balances are restored
        $invoice->refresh();
        $this->assertEquals(0.00, (float)$invoice->total_receive);

        // Check ledger entry removed/unwound
        $this->assertEquals(0, CustomerLedger::where('payment_id', $paymentId)->count());

        // Double reversal rejected
        $doubleRes = $this->postJson("/api/payments/{$paymentId}/reverse", [
            'reason' => 'Trying to reverse again',
        ]);
        $doubleRes->assertStatus(422);
    }

    public function test_open_invoices_endpoint_returns_only_collectible_or_payable_invoices(): void
    {
        $customer = Customer::create(['name' => 'Open Inv Customer']);

        SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $customer->id,
            'invoice_type'  => 'credit',
            'posted_number' => 'INV-OPEN-1',
            'date'          => '2026-10-15',
            'gross_amount'  => 1000.00,
            'total'         => 1000.00,
            'total_receive' => 400.00, // Due 600
            'status'        => 'posted',
        ]);

        SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $customer->id,
            'invoice_type'  => 'credit',
            'posted_number' => 'INV-PAID-2',
            'date'          => '2026-10-16',
            'gross_amount'  => 500.00,
            'total'         => 500.00,
            'total_receive' => 500.00, // Due 0 (should not be listed)
            'status'        => 'posted',
        ]);

        SaleInvoice::create([
            'user_id'       => $this->user->id,
            'customer_id'   => $customer->id,
            'invoice_type'  => 'credit',
            'posted_number' => 'INV-REV-3',
            'date'          => '2026-10-17',
            'gross_amount'  => 700.00,
            'total'         => 700.00,
            'total_receive' => 0.00,
            'status'        => 'reversed', // Reversed (should not be listed)
        ]);

        $res = $this->getJson("/api/payments/open-invoices?party_type=customer&party_id={$customer->id}");
        $res->assertStatus(200);

        $items = $res->json();
        $this->assertCount(1, $items);
        $this->assertEquals('INV-OPEN-1', $items[0]['posted_number']);
        $this->assertEquals(600.00, $items[0]['due']);
    }
}
