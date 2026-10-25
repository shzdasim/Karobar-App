<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

class StockDisposalItem extends Model
{
    use HasFactory;

    protected $fillable = [
        'stock_disposal_id',
        'product_id',
        'batch_id',
        'batch_number',
        'expiry_date',
        'pack_size',
        'previous_qty',
        'dispose_qty',
        'remaining_qty',
        'unit_purchase_price',
        'unit_sale_price',
        'cost_value',
        'retail_value',
        'item_note',
    ];

    protected $casts = [
        'expiry_date'         => 'date:Y-m-d',
        'pack_size'           => 'integer',
        'previous_qty'        => 'float',
        'dispose_qty'         => 'float',
        'remaining_qty'        => 'float',
        'unit_purchase_price' => 'float',
        'unit_sale_price'     => 'float',
        'cost_value'          => 'float',
        'retail_value'        => 'float',
    ];

    public function disposal(): BelongsTo
    {
        return $this->belongsTo(StockDisposal::class, 'stock_disposal_id');
    }

    public function product(): BelongsTo
    {
        return $this->belongsTo(Product::class, 'product_id');
    }

    public function batch(): BelongsTo
    {
        return $this->belongsTo(Batch::class, 'batch_id');
    }
}

