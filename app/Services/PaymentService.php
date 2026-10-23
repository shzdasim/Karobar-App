<?php

namespace App\Services;

use App\Models\Customer;
use App\Models\CustomerLedger;
use App\Models\Payment;
use App\Models\PaymentAllocation;
use App\Models\PurchaseInvoice;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use App\Models\SupplierLedger;
use App\Models\User;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use Illuminate\Validation\ValidationException;

class PaymentService
{
    protected DocumentAuditService $auditService;

    public function __construct(DocumentAuditService $auditService)
    {
        $this->auditService = $auditService;
    }

    /**
     * Generate sequential payment number.
     */
    public function generatePaymentNumber(string $partyType): string
    {
        $prefix = $partyType === 'customer' ? 'CR' : 'SD';
        $year = Carbon::now()->format('Y');

        $last = Payment::where('party_type', $partyType)
            ->where('payment_number', 'LIKE', "{$prefix}-{$year}-%")
            ->orderBy('id', 'desc')
            ->first();

        if ($last) {
            $parts = explode('-', $last->payment_number);
            $seq = intval(end($parts)) + 1;
        } else {
            $seq = 1;
        }

        return sprintf('%s-%s-%05d', $prefix, $year, $seq);
    }

    /**
     * Record a customer payment / receipt.
     */
    public function recordCustomerPayment(Customer $customer, array $data, ?User $user = null): Payment
    {
        return DB::transaction(function () use ($customer, $data, $user) {
            // Idempotency check
            if (!empty($data['idempotency_key'])) {
                $existing = Payment::where('idempotency_key', $data['idempotency_key'])->first();
                if ($existing) {
                    return $existing->load('allocations');
                }
            }

            $amount = round((float)$data['amount'], 2);
            if ($amount <= 0) {
                throw ValidationException::withMessages(['amount' => 'Payment amount must be greater than zero.']);
            }

            $allocationsInput = $data['allocations'] ?? [];
            $totalAllocated = 0.0;
            $allocationsToCreate = [];

            foreach ($allocationsInput as $alloc) {
                $allocAmount = round((float)($alloc['amount'] ?? 0), 2);
                if ($allocAmount <= 0) continue;

                $invoiceId = (int)$alloc['invoice_id'];
                $invoice = SaleInvoice::where('id', $invoiceId)
                    ->where('customer_id', $customer->id)
                    ->lockForUpdate()
                    ->first();

                if (!$invoice) {
                    throw ValidationException::withMessages([
                        'allocations' => "Sale invoice #{$invoiceId} not found for this customer.",
                    ]);
                }

                if ($invoice->status === 'reversed') {
                    throw ValidationException::withMessages([
                        'allocations' => "Invoice {$invoice->posted_number} is reversed and cannot receive payments.",
                    ]);
                }

                $invTotal = (float)($invoice->total ?? 0);
                $invReceived = (float)($invoice->total_receive ?? $invoice->total_recieve ?? 0);
                $remainingDue = max($invTotal - $invReceived, 0);

                if ($allocAmount > ($remainingDue + 0.001)) {
                    throw ValidationException::withMessages([
                        'allocations' => "Allocated amount ({$allocAmount}) exceeds invoice {$invoice->posted_number} due balance ({$remainingDue}).",
                    ]);
                }

                $totalAllocated += $allocAmount;
                $allocationsToCreate[] = [
                    'invoice' => $invoice,
                    'amount'  => $allocAmount,
                ];
            }

            if ($totalAllocated > ($amount + 0.001)) {
                throw ValidationException::withMessages([
                    'allocations' => "Total allocated ({$totalAllocated}) exceeds payment amount ({$amount}).",
                ]);
            }

            $unapplied = round(max($amount - $totalAllocated, 0), 2);
            $paymentNumber = $this->generatePaymentNumber('customer');

            $payment = Payment::create([
                'payment_number'   => $paymentNumber,
                'party_type'       => 'customer',
                'party_id'         => $customer->id,
                'payment_date'     => $data['payment_date'] ?? Carbon::now()->toDateString(),
                'amount'           => $amount,
                'allocated_amount' => $totalAllocated,
                'unapplied_amount' => $unapplied,
                'payment_method'   => $data['payment_method'] ?? 'cash',
                'reference'        => $data['reference'] ?? null,
                'status'           => 'posted',
                'notes'            => $data['notes'] ?? null,
                'idempotency_key'  => $data['idempotency_key'] ?? null,
                'created_by_id'    => $user?->id,
            ]);

            // Save allocations and update invoice totals
            foreach ($allocationsToCreate as $item) {
                PaymentAllocation::create([
                    'payment_id'   => $payment->id,
                    'invoice_type' => 'sale_invoice',
                    'invoice_id'   => $item['invoice']->id,
                    'amount'       => $item['amount'],
                ]);

                // Update invoice projection
                $curReceived = (float)($item['invoice']->total_receive ?? 0);
                $item['invoice']->total_receive = round($curReceived + $item['amount'], 2);
                $item['invoice']->save();
            }

            // Sync with CustomerLedger
            if (Schema::hasTable('customer_ledgers')) {
                $methodLabel = ucfirst(str_replace('_', ' ', $payment->payment_method));
                $notesText = $payment->notes ? ": {$payment->notes}" : '';

                CustomerLedger::create([
                    'customer_id'     => $customer->id,
                    'entry_date'      => $payment->payment_date,
                    'entry_type'      => 'payment',
                    'is_manual'       => false,
                    'credited_amount' => $payment->amount,
                    'payment_ref'     => $payment->payment_number . ($payment->reference ? " ({$payment->reference})" : ''),
                    'payment_id'      => $payment->id,
                    'description'     => "Payment via {$methodLabel}{$notesText}",
                    'created_by'      => $user?->id,
                ]);
            }

            // Audit Log
            $this->auditService->recordCreate(
                $payment,
                $user,
                "Customer payment {$payment->payment_number} recorded"
            );

            // Record financial account deposit
            $account = app(AccountService::class)->resolveAccount($payment->account_id ?? $data['account_id'] ?? null, $payment->payment_method);
            if ($account) {
                $payment->account_id = $account->id;
                $payment->saveQuietly();

                app(AccountService::class)->deposit(
                    $account,
                    $payment->amount,
                    'customer_payment',
                    $payment->id,
                    "Customer payment {$payment->payment_number} ({$customer->name})",
                    $user
                );
            }

            // Record cash drawer transaction if payment method is cash
            if (($account && $account->type === 'cash') || $payment->payment_method === 'cash') {
                app(CashDrawerService::class)->recordTransaction(
                    null,
                    'customer_payment',
                    $payment->amount,
                    'payment',
                    $payment->id,
                    "Customer payment {$payment->payment_number}",
                    $user
                );
            }

            return $payment->load('allocations');
        });
    }

    /**
     * Record a supplier payment / disbursement.
     */
    public function recordSupplierPayment(Supplier $supplier, array $data, ?User $user = null): Payment
    {
        return DB::transaction(function () use ($supplier, $data, $user) {
            // Idempotency check
            if (!empty($data['idempotency_key'])) {
                $existing = Payment::where('idempotency_key', $data['idempotency_key'])->first();
                if ($existing) {
                    return $existing->load('allocations');
                }
            }

            $amount = round((float)$data['amount'], 2);
            if ($amount <= 0) {
                throw ValidationException::withMessages(['amount' => 'Payment amount must be greater than zero.']);
            }

            $allocationsInput = $data['allocations'] ?? [];
            $totalAllocated = 0.0;
            $allocationsToCreate = [];

            foreach ($allocationsInput as $alloc) {
                $allocAmount = round((float)($alloc['amount'] ?? 0), 2);
                if ($allocAmount <= 0) continue;

                $invoiceId = (int)$alloc['invoice_id'];
                $invoice = PurchaseInvoice::where('id', $invoiceId)
                    ->where('supplier_id', $supplier->id)
                    ->lockForUpdate()
                    ->first();

                if (!$invoice) {
                    throw ValidationException::withMessages([
                        'allocations' => "Purchase invoice #{$invoiceId} not found for this supplier.",
                    ]);
                }

                if ($invoice->status === 'reversed') {
                    throw ValidationException::withMessages([
                        'allocations' => "Invoice {$invoice->posted_number} is reversed and cannot receive payments.",
                    ]);
                }

                $invTotal = (float)($invoice->total_amount ?? 0);
                $invPaid = (float)($invoice->total_paid ?? 0);
                $remainingDue = max($invTotal - $invPaid, 0);

                if ($allocAmount > ($remainingDue + 0.001)) {
                    throw ValidationException::withMessages([
                        'allocations' => "Allocated amount ({$allocAmount}) exceeds purchase invoice {$invoice->posted_number} payable ({$remainingDue}).",
                    ]);
                }

                $totalAllocated += $allocAmount;
                $allocationsToCreate[] = [
                    'invoice' => $invoice,
                    'amount'  => $allocAmount,
                ];
            }

            if ($totalAllocated > ($amount + 0.001)) {
                throw ValidationException::withMessages([
                    'allocations' => "Total allocated ({$totalAllocated}) exceeds payment amount ({$amount}).",
                ]);
            }

            $unapplied = round(max($amount - $totalAllocated, 0), 2);
            $paymentNumber = $this->generatePaymentNumber('supplier');

            $payment = Payment::create([
                'payment_number'   => $paymentNumber,
                'party_type'       => 'supplier',
                'party_id'         => $supplier->id,
                'payment_date'     => $data['payment_date'] ?? Carbon::now()->toDateString(),
                'amount'           => $amount,
                'allocated_amount' => $totalAllocated,
                'unapplied_amount' => $unapplied,
                'payment_method'   => $data['payment_method'] ?? 'cash',
                'reference'        => $data['reference'] ?? null,
                'status'           => 'posted',
                'notes'            => $data['notes'] ?? null,
                'idempotency_key'  => $data['idempotency_key'] ?? null,
                'created_by_id'    => $user?->id,
            ]);

            // Save allocations and update purchase invoice totals
            foreach ($allocationsToCreate as $item) {
                PaymentAllocation::create([
                    'payment_id'   => $payment->id,
                    'invoice_type' => 'purchase_invoice',
                    'invoice_id'   => $item['invoice']->id,
                    'amount'       => $item['amount'],
                ]);

                // Update invoice projection
                $curPaid = (float)($item['invoice']->total_paid ?? 0);
                $item['invoice']->total_paid = round($curPaid + $item['amount'], 2);
                $item['invoice']->save();
            }

            // Sync with SupplierLedger
            if (Schema::hasTable('supplier_ledgers')) {
                $methodLabel = ucfirst(str_replace('_', ' ', $payment->payment_method));
                $notesText = $payment->notes ? ": {$payment->notes}" : '';

                SupplierLedger::create([
                    'supplier_id'    => $supplier->id,
                    'entry_date'     => $payment->payment_date,
                    'entry_type'     => 'payment',
                    'is_manual'      => false,
                    'debited_amount' => $payment->amount,
                    'payment_ref'    => $payment->payment_number . ($payment->reference ? " ({$payment->reference})" : ''),
                    'payment_id'     => $payment->id,
                    'description'    => "Payment via {$methodLabel}{$notesText}",
                    'created_by'     => $user?->id,
                ]);
            }

            // Audit Log
            $this->auditService->recordCreate(
                $payment,
                $user,
                "Supplier payment {$payment->payment_number} recorded"
            );

            // Record financial account withdrawal
            $account = app(AccountService::class)->resolveAccount($payment->account_id ?? $data['account_id'] ?? null, $payment->payment_method);
            if ($account) {
                $payment->account_id = $account->id;
                $payment->saveQuietly();

                app(AccountService::class)->withdraw(
                    $account,
                    $payment->amount,
                    'supplier_payment',
                    $payment->id,
                    "Supplier payment {$payment->payment_number} ({$supplier->name})",
                    $user
                );
            }

            // Record cash drawer transaction if payment method is cash
            if (($account && $account->type === 'cash') || $payment->payment_method === 'cash') {
                app(CashDrawerService::class)->recordTransaction(
                    null,
                    'supplier_payment',
                    $payment->amount,
                    'payment',
                    $payment->id,
                    "Supplier payment {$payment->payment_number}",
                    $user
                );
            }

            return $payment->load('allocations');
        });
    }

    /**
     * Safely reverse a payment with full audit history and balance unwinding.
     */
    public function reversePayment(Payment $payment, string $reason, ?User $user = null): Payment
    {
        return DB::transaction(function () use ($payment, $reason, $user) {
            if ($payment->status === 'reversed') {
                throw ValidationException::withMessages(['payment' => 'This payment has already been reversed.']);
            }

            $beforeSnapshot = $payment->toArray();

            // Unwind allocations
            $payment->load('allocations');
            foreach ($payment->allocations as $alloc) {
                if ($alloc->invoice_type === 'sale_invoice') {
                    $invoice = SaleInvoice::find($alloc->invoice_id);
                    if ($invoice) {
                        $curReceived = (float)($invoice->total_receive ?? 0);
                        $invoice->total_receive = round(max($curReceived - $alloc->amount, 0), 2);
                        $invoice->save();
                    }
                } elseif ($alloc->invoice_type === 'purchase_invoice') {
                    $invoice = PurchaseInvoice::find($alloc->invoice_id);
                    if ($invoice) {
                        $curPaid = (float)($invoice->total_paid ?? 0);
                        $invoice->total_paid = round(max($curPaid - $alloc->amount, 0), 2);
                        $invoice->save();
                    }
                }
            }

            // Remove or unwind party ledger entry
            if ($payment->party_type === 'customer' && Schema::hasTable('customer_ledgers')) {
                CustomerLedger::where('payment_id', $payment->id)->delete();
            } elseif ($payment->party_type === 'supplier' && Schema::hasTable('supplier_ledgers')) {
                SupplierLedger::where('payment_id', $payment->id)->delete();
            }

            // Mark reversed
            $payment->status = 'reversed';
            $payment->reversed_at = Carbon::now();
            $payment->reversed_by_id = $user?->id;
            $payment->reversal_reason = $reason;
            $payment->save();

            // Reverse financial account effect
            $account = app(AccountService::class)->resolveAccount($payment->account_id, $payment->payment_method);
            if ($account) {
                if ($payment->party_type === 'customer') {
                    app(AccountService::class)->withdraw(
                        $account,
                        $payment->amount,
                        'payment_reversal',
                        $payment->id,
                        "Reversal of Customer Payment {$payment->payment_number}: {$reason}",
                        $user
                    );
                } else {
                    app(AccountService::class)->deposit(
                        $account,
                        $payment->amount,
                        'payment_reversal',
                        $payment->id,
                        "Reversal of Supplier Payment {$payment->payment_number}: {$reason}",
                        $user
                    );
                }
            }

            // Record audit
            $this->auditService->recordReversal(
                $payment,
                $user,
                $reason
            );

            return $payment;
        });
    }

    /**
     * Retrieve open collectible invoices for a customer.
     */
    public function getOpenInvoicesForCustomer(int $customerId): array
    {
        $invoices = SaleInvoice::where('customer_id', $customerId)
            ->where('invoice_type', 'credit')
            ->where('status', '!=', 'reversed')
            ->orderBy('date', 'asc')
            ->orderBy('id', 'asc')
            ->get();

        $open = [];
        foreach ($invoices as $inv) {
            $total = (float)($inv->total ?? 0);
            $received = (float)($inv->total_receive ?? $inv->total_recieve ?? 0);
            $remaining = round(max($total - $received, 0), 2);

            if ($remaining > 0.01) {
                $open[] = [
                    'id'            => $inv->id,
                    'posted_number' => $inv->posted_number ?: "#{$inv->id}",
                    'date'          => $inv->date,
                    'total'         => $total,
                    'received'      => $received,
                    'due'           => $remaining,
                ];
            }
        }

        return $open;
    }

    /**
     * Retrieve open payable invoices for a supplier.
     */
    public function getOpenInvoicesForSupplier(int $supplierId): array
    {
        $invoices = PurchaseInvoice::where('supplier_id', $supplierId)
            ->where('invoice_type', 'credit')
            ->where('status', '!=', 'reversed')
            ->orderBy('posted_date', 'asc')
            ->orderBy('id', 'asc')
            ->get();

        $open = [];
        foreach ($invoices as $inv) {
            $total = (float)($inv->total_amount ?? 0);
            $paid = (float)($inv->total_paid ?? 0);
            $remaining = round(max($total - $paid, 0), 2);

            if ($remaining > 0.01) {
                $open[] = [
                    'id'            => $inv->id,
                    'posted_number' => $inv->posted_number ?: ($inv->invoice_number ?: "#{$inv->id}"),
                    'date'          => $inv->posted_date,
                    'total'         => $total,
                    'paid'          => $paid,
                    'due'           => $remaining,
                ];
            }
        }

        return $open;
    }
}
