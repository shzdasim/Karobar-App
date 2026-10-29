<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Batch extends Model
{
    protected $fillable = [
        'product_id',
        'batch_number',
        'expiry_date',
        'quantity',
        'storage_location_id',
        'rack',
        'pack_purchase_price',
        'unit_purchase_price',
        'pack_sale_price',
        'unit_sale_price',
        'whole_sale_pack_price',
        'whole_sale_unit_price',
    ];

    protected $casts = [
        'quantity'              => 'integer',
        'storage_location_id'   => 'integer',
        'pack_purchase_price'   => 'float',
        'unit_purchase_price'   => 'float',
        'pack_sale_price'       => 'float',
        'unit_sale_price'       => 'float',
        'whole_sale_pack_price' => 'float',
        'whole_sale_unit_price' => 'float',
    ];

    protected $appends = [
        'expiry',
        'available_units',
        'is_expired',
        'days_remaining',
    ];

    public function getIsExpiredAttribute(): bool
    {
        if (!$this->expiry_date) {
            return false;
        }
        return substr((string) $this->expiry_date, 0, 10) < now()->toDateString();
    }

    public function getDaysRemainingAttribute(): ?int
    {
        if (!$this->expiry_date) {
            return null;
        }
        $expiry = \Carbon\Carbon::parse($this->expiry_date)->startOfDay();
        $today = now()->startOfDay();
        return (int) $today->diffInDays($expiry, false);
    }

    // Map DB column names to frontend-friendly names
    public function getExpiryDateAttribute($value)
    {
        return $value;
    }

    public function getQuantityAttribute($value)
    {
        return $value;
    }

    // Accessors for frontend compatibility
    public function getExpiryAttribute()
    {
        return $this->expiry_date;
    }

    public function getAvailableUnitsAttribute()
    {
        return $this->quantity;
    }

    public function setExpiryAttribute($value)
    {
        $this->expiry_date = $value;
    }

    public function setAvailableUnitsAttribute($value)
    {
        $this->quantity = $value;
    }

    public function product()
    {
        return $this->belongsTo(Product::class);
    }

    public function storageLocation()
    {
        return $this->belongsTo(StorageLocation::class, 'storage_location_id');
    }
}
