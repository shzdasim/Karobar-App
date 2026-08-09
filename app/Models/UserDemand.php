<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Factories\HasFactory;

class UserDemand extends Model
{
    use HasFactory;

protected $fillable = [
        'product_id',
        'customer_id',
        'requested_by',
        'requested_name',
        'requested_quantity',
        'notes',
        'status',
        'last_notified_at',
    ];

    protected $casts = [
        'requested_quantity' => 'integer',
        'last_notified_at'   => 'datetime',
    ];

public function product()
    {
        return $this->belongsTo(Product::class);
    }

    public function customer()
    {
        return $this->belongsTo(Customer::class);
    }

    public function requester()
    {
        return $this->belongsTo(User::class, 'requested_by');
    }

    public function scopePending($query)
    {
        return $query->where('status', 'pending');
    }

    public function getProductBrandNameAttribute()
    {
        return optional($this->product?->brand)->name ?? null;
    }

    public function getProductSupplierNameAttribute()
    {
        return optional($this->product?->supplier)->name ?? null;
    }
}
