<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class PurchaseOrderItem extends Model
{
    protected $fillable = ['product_id', 'ordered_quantity', 'pack_size', 'unit_purchase_price', 'item_discount_percentage', 'unit_bonus'];

    public function product()
    {
        return $this->belongsTo(Product::class);
    }

    public function purchaseOrder()
    {
        return $this->belongsTo(PurchaseOrder::class);
    }

    public function invoiceItems()
    {
        return $this->hasMany(PurchaseInvoiceItem::class);
    }
}
