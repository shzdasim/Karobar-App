<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;

class StocktakeSession extends Model
{
    use HasFactory;

    protected $fillable = [
        'reference_no',
        'session_type',
        'is_blind',
        'status',
        'scope_type',
        'scope_id',
        'notes',
        'user_id',
        'stock_adjustment_id',
        'started_at',
        'completed_at',
    ];

    protected $casts = [
        'is_blind'     => 'boolean',
        'started_at'   => 'datetime',
        'completed_at' => 'datetime',
    ];

    protected $appends = ['progress'];

    // ===== Relationships =====

    public function items()
    {
        return $this->hasMany(StocktakeItem::class);
    }

    public function user()
    {
        return $this->belongsTo(User::class);
    }

    public function stockAdjustment()
    {
        return $this->belongsTo(StockAdjustment::class);
    }

    // ===== Computed Attributes =====

    public function getProgressAttribute(): array
    {
        // Avoid N+1 if items already loaded
        if ($this->relationLoaded('items')) {
            $items   = $this->items;
            $total   = $items->count();
            $counted = $items->where('status', 'counted')->count();
        } else {
            $total   = $this->items()->count();
            $counted = $this->items()->where('status', 'counted')->count();
        }

        return [
            'counted' => $counted,
            'total'   => $total,
        ];
    }

    // ===== Scopes =====

    public function scopePending($query)
    {
        return $query->whereNotIn('status', ['finalized', 'cancelled']);
    }
}
