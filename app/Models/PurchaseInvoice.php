<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class PurchaseInvoice extends Model
{
    protected $fillable = [
        'supplier_id',
        'purchase_order_id',
        'receipt_key',
        'invoice_type',
        'payment_method',
        'posted_number',
        'posted_date',
        'due_date',
        'remarks',
        'invoice_number',
        'invoice_amount',
        'tax_percentage',
        'tax_amount',
        'discount_percentage',
        'discount_amount',
        'total_amount',
        'total_paid',
        'account_id',
        'status',
        'reversed_at',
        'reversed_by_id',
        'reversal_reason',
    ];

    public function reversedBy() { return $this->belongsTo(User::class, 'reversed_by_id'); }
    public function purchaseOrder() { return $this->belongsTo(PurchaseOrder::class); }

    public function supplier()
    {
        return $this->belongsTo(Supplier::class);
    }

    public function account()
    {
        return $this->belongsTo(Account::class);
    }

    public function items()
    {
        return $this->hasMany(PurchaseInvoiceItem::class)->orderBy('id');
    }
}
