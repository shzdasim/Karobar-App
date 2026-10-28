<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class SaleInvoiceItem extends Model
{
    protected $fillable = [
        'cost_price_snapshot',
        'sale_invoice_id',
        'product_id',
        'pack_size',
        'batch_number',
        'expiry',
        'current_quantity',
        'pack_quantity',
        'unit_quantity',
        'quantity',
        'pack_bonus',
        'unit_bonus',
        'bonus',
        'pack_price',
        'price',
        'item_discount_percentage',
        'sub_total',
        'sale_type',
        'is_custom_price',
    ];
    public function saleInvoice()
    {
        return $this->belongsTo(SaleInvoice::class);
    }
    public function product()
    {
        return $this->belongsTo(Product::class);
    }
}
