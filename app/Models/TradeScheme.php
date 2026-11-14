<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

class TradeScheme extends Model
{
    use HasFactory;

    protected $fillable = [
        'name',
        'scheme_type',   // 'ratio', 'slab'
        'target_type',   // 'product', 'brand', 'category'
        'target_id',
        'bonus_type',    // 'pack', 'unit'
        'customer_id',
        'sale_type',     // 'all', 'wholesale', 'retail'
        'start_date',
        'end_date',
        'is_active',
        'priority',
        'notes',
    ];

    protected $casts = [
        'is_active'   => 'boolean',
        'priority'    => 'integer',
        'target_id'   => 'integer',
        'customer_id' => 'integer',
        'start_date'  => 'date',
        'end_date'    => 'date',
    ];

    public function slabs(): HasMany
    {
        return $this->hasMany(TradeSchemeSlab::class)->orderBy('min_qty', 'asc');
    }

    public function customer(): BelongsTo
    {
        return $this->belongsTo(Customer::class);
    }

    public function product(): BelongsTo
    {
        return $this->belongsTo(Product::class, 'target_id');
    }

    public function brand(): BelongsTo
    {
        return $this->belongsTo(Brand::class, 'target_id');
    }

    public function category(): BelongsTo
    {
        return $this->belongsTo(Category::class, 'target_id');
    }

    /**
     * Scope for active schemes valid on given date.
     */
    public function scopeActive($query, ?string $date = null)
    {
        $d = $date ?: now()->toDateString();
        return $query->where('is_active', true)
            ->where(function ($q) use ($d) {
                $q->whereNull('start_date')->orWhereDate('start_date', '<=', $d);
            })
            ->where(function ($q) use ($d) {
                $q->whereNull('end_date')->orWhereDate('end_date', '>=', $d);
            });
    }

    /**
     * Scope matching sale type ('wholesale', 'retail').
     */
    public function scopeForSaleType($query, ?string $saleType)
    {
        if (!$saleType) return $query;
        return $query->where(function ($q) use ($saleType) {
            $q->where('sale_type', 'all')->orWhere('sale_type', $saleType);
        });
    }

    /**
     * Scope matching customer eligibility (all customers OR specific customer).
     */
    public function scopeForCustomer($query, ?int $customerId)
    {
        return $query->where(function ($q) use ($customerId) {
            $q->whereNull('customer_id');
            if ($customerId) {
                $q->orWhere('customer_id', $customerId);
            }
        });
    }
}
