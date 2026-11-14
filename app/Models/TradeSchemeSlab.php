<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

class TradeSchemeSlab extends Model
{
    use HasFactory;

    protected $fillable = [
        'trade_scheme_id',
        'min_qty',
        'max_qty',
        'bonus_qty',
        'bonus_product_id',
    ];

    protected $casts = [
        'min_qty'          => 'float',
        'max_qty'          => 'float',
        'bonus_qty'        => 'float',
        'trade_scheme_id'  => 'integer',
        'bonus_product_id' => 'integer',
    ];

    public function tradeScheme(): BelongsTo
    {
        return $this->belongsTo(TradeScheme::class);
    }

    public function bonusProduct(): BelongsTo
    {
        return $this->belongsTo(Product::class, 'bonus_product_id');
    }
}
