<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Support\Facades\DB;

class CustomerLedger extends Model
{
    protected $fillable = [
        'customer_id',
        'sale_invoice_id',
        'entry_date',
        'posted_number',
        'invoice_number',

        'invoice_total',
        'total_received',
        'balance_remaining',

        'entry_type',
        'description',
        'is_manual',

        'credited_amount',
        'payment_ref',

        'created_by',
    ];

    protected $casts = [
        'entry_date' => 'date',
        'is_manual'  => 'boolean',
    ];

    /**
     * Get the customer's net balance from the ledger.
     * This is the total amount the customer owes.
     * 
     * Formula: net_balance = (total_invoiced - received_on_invoice) - payments_credited
     */
    public static function getCustomerNetBalance(int $customerId): float
    {
        // Get invoice totals (invoices where customer owes money)
        $invoiceRows = self::where('customer_id', $customerId)
            ->whereIn('entry_type', ['invoice', 'manual'])
            ->get();

        $totalInvoiced = $invoiceRows->sum('invoice_total');
        $receivedOnInv = $invoiceRows->sum('total_received');

        // Get payments (money received from customer)
        $paymentsCred = self::where('customer_id', $customerId)
            ->where('entry_type', 'payment')
            ->sum(DB::raw('COALESCE(credited_amount, 0)'));

        // Calculate net balance
        $netBalance = ($totalInvoiced - $receivedOnInv) - $paymentsCred;

        // Return 0 if negative (customer has credit)
        return $netBalance > 0 ? round($netBalance, 2) : 0.0;
    }

    public function customer(): BelongsTo
    {
        return $this->belongsTo(Customer::class);
    }

    public function saleInvoice(): BelongsTo
    {
        return $this->belongsTo(SaleInvoice::class);
    }
}
