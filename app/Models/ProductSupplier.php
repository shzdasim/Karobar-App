<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class ProductSupplier extends Model
{
    protected $fillable = [
        'product_id',
        'supplier_id',
        'supplier_sku',
        'last_purchase_price',
        'lead_time_days',
        'min_order_qty',
        'is_preferred',
        'notes',
    ];

    protected $casts = [
        'last_purchase_price' => 'float',
        'lead_time_days'      => 'integer',
        'min_order_qty'       => 'integer',
        'is_preferred'        => 'boolean',
    ];

    public function product()
    {
        return $this->belongsTo(Product::class);
    }

    public function supplier()
    {
        return $this->belongsTo(Supplier::class);
    }
}

