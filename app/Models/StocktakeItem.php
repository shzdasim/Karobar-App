<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;

class StocktakeItem extends Model
{
    use HasFactory;

    protected $fillable = [
        'stocktake_session_id',
        'product_id',
        'batch_number',
        'expiry_date',
        'system_qty',
        'counted_qty',
        'variance_qty',
        'variance_value',
        'unit_purchase_price',
        'status',
        'discrepancy_reason',
        'counted_by',
        'counted_at',
    ];

    protected $casts = [
        'expiry_date'         => 'date',
        'counted_at'          => 'datetime',
        'system_qty'          => 'float',
        'counted_qty'         => 'float',
        'variance_qty'        => 'float',
        'variance_value'      => 'float',
        'unit_purchase_price' => 'float',
    ];

    // ===== Relationships =====

    public function session()
    {
        return $this->belongsTo(StocktakeSession::class, 'stocktake_session_id');
    }

    public function product()
    {
        return $this->belongsTo(Product::class);
    }

    /**
     * The user who performed the count.
     */
    public function counter()
    {
        return $this->belongsTo(User::class, 'counted_by');
    }
}
