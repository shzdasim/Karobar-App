<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Factories\HasFactory;

class SupplierLedger extends Model
{
    use HasFactory;

    protected $fillable = [
        'supplier_id',
        'purchase_invoice_id',
        'purchase_return_id',
        'payment_id',
        'entry_type',          // 'invoice' | 'payment' | 'manual'
        'entry_date',
        'due_date',
        'posted_number',
        'invoice_number',
        'invoice_total',
        'total_paid',
        'debited_amount',      // payments
        'payment_ref',
        'credit_remaining',
        'description',
        'is_manual',
        'created_by',
    ];

    protected $casts = [
        'entry_date'        => 'date',
        'due_date'          => 'date',
        'invoice_total'     => 'decimal:2',
        'total_paid'        => 'decimal:2',
        'debited_amount'    => 'decimal:2',
        'credit_remaining'  => 'decimal:2',
        'is_manual'         => 'boolean',
    ];

    public function supplier()
    {
        return $this->belongsTo(Supplier::class);
    }

    public function purchaseInvoice()
    {
        return $this->belongsTo(PurchaseInvoice::class);
    }

    public function purchaseReturn()
    {
        return $this->belongsTo(PurchaseReturn::class, 'purchase_return_id');
    }

    /**
     * Calculate the supplier's current net payable balance from the ledger
     * (including manual/opening balance rows, credit invoices, and payments).
     */
    public static function getSupplierNetBalance(int $supplierId): float
    {
        $rows = self::with(['purchaseInvoice:id,invoice_number,posted_number,total_amount,total_paid,status'])
            ->where('supplier_id', $supplierId)
            ->get();

        $allocatedByInvoice = collect();
        $invIds = $rows->pluck('purchase_invoice_id')->filter()->unique()->values()->all();
        if (!empty($invIds) && \Illuminate\Support\Facades\Schema::hasTable('payment_allocations') && \Illuminate\Support\Facades\Schema::hasTable('payments')) {
            $allocatedByInvoice = PaymentAllocation::query()
                ->where('invoice_type', 'purchase_invoice')
                ->whereIn('invoice_id', $invIds)
                ->whereHas('payment', fn ($q) => $q->where('status', 'posted'))
                ->selectRaw('invoice_id, SUM(amount) as total_allocated')
                ->groupBy('invoice_id')
                ->pluck('total_allocated', 'invoice_id');
        }

        $totalInvoiced = 0.0;
        $paidOnInvoice = 0.0;
        $paymentsDebited = 0.0;

        foreach ($rows as $row) {
            if ($row->entry_type === 'invoice' && $row->purchaseInvoice) {
                $inv = $row->purchaseInvoice;
                if (($inv->status ?? null) === 'reversed') {
                    continue;
                }
                $invTotal = (float) ($inv->total_amount ?? $row->invoice_total ?? 0);
                $rawPaid = (float) ($inv->total_paid ?? $row->total_paid ?? 0);
                $allocated = (float) ($allocatedByInvoice[$inv->id] ?? 0);
                $paidOnInv = max(0.0, min($rawPaid - $allocated, $invTotal));

                $totalInvoiced += $invTotal;
                $paidOnInvoice += $paidOnInv;
            } elseif (in_array($row->entry_type, ['invoice', 'manual'], true)) {
                $invTotal = (float) ($row->invoice_total ?? 0);
                $paidOnInv = min((float) ($row->total_paid ?? 0), $invTotal);

                $totalInvoiced += $invTotal;
                $paidOnInvoice += $paidOnInv;
            } elseif ($row->entry_type === 'payment') {
                $paymentsDebited += (float) ($row->debited_amount ?? 0);
            }
        }

        // Include any credit purchase invoices that do not yet have a ledger row
        $unlinkedInvoices = PurchaseInvoice::query()
            ->where('supplier_id', $supplierId)
            ->where('invoice_type', 'credit')
            ->where(function ($q) {
                $q->whereNull('status')->orWhere('status', '!=', 'reversed');
            })
            ->when(!empty($invIds), fn ($q) => $q->whereNotIn('id', $invIds))
            ->get(['id', 'total_amount', 'total_paid']);

        if ($unlinkedInvoices->isNotEmpty()) {
            $unlinkedIds = $unlinkedInvoices->pluck('id')->all();
            $unlinkedAlloc = collect();
            if (\Illuminate\Support\Facades\Schema::hasTable('payment_allocations') && \Illuminate\Support\Facades\Schema::hasTable('payments')) {
                $unlinkedAlloc = PaymentAllocation::query()
                    ->where('invoice_type', 'purchase_invoice')
                    ->whereIn('invoice_id', $unlinkedIds)
                    ->whereHas('payment', fn ($q) => $q->where('status', 'posted'))
                    ->selectRaw('invoice_id, SUM(amount) as total_allocated')
                    ->groupBy('invoice_id')
                    ->pluck('total_allocated', 'invoice_id');
            }

            foreach ($unlinkedInvoices as $uInv) {
                $uTotal = (float) ($uInv->total_amount ?? 0);
                $uRawPaid = (float) ($uInv->total_paid ?? 0);
                $uAlloc = (float) ($unlinkedAlloc[$uInv->id] ?? 0);
                $uPaidOnInv = max(0.0, min($uRawPaid - $uAlloc, $uTotal));

                $totalInvoiced += $uTotal;
                $paidOnInvoice += $uPaidOnInv;
            }
        }

        $netBalance = ($totalInvoiced - $paidOnInvoice) - $paymentsDebited;

        return $netBalance > 0 ? round($netBalance, 2) : 0.0;
    }
}
