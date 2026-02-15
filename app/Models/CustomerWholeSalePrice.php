<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class CustomerWholeSalePrice extends Model
{
    protected $fillable = [
        'customer_id',
        'product_id',
        'pack_price',
    ];

    public function customer()
    {
        return $this->belongsTo(Customer::class);
    }

    public function product()
    {
        return $this->belongsTo(Product::class);
    }
}

