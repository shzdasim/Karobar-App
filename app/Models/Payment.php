<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

class Payment extends Model
{
    use HasFactory;

    protected $fillable = [
        'payment_number',
        'party_type',
        'party_id',
        'payment_date',
        'amount',
        'allocated_amount',
        'unapplied_amount',
        'payment_method',
        'reference',
        'status',
        'notes',
        'idempotency_key',
        'created_by_id',
        'reversed_at',
        'reversed_by_id',
        'reversal_reason',
    ];

    protected $casts = [
        'payment_date'     => 'date',
        'amount'           => 'float',
        'allocated_amount' => 'float',
        'unapplied_amount' => 'float',
        'reversed_at'      => 'datetime',
    ];

    public function allocations(): HasMany
    {
        return $this->hasMany(PaymentAllocation::class, 'payment_id');
    }

    public function creator(): BelongsTo
    {
        return $this->belongsTo(User::class, 'created_by_id');
    }

    public function reversedBy(): BelongsTo
    {
        return $this->belongsTo(User::class, 'reversed_by_id');
    }

    public function customer(): BelongsTo
    {
        return $this->belongsTo(Customer::class, 'party_id');
    }

    public function supplier(): BelongsTo
    {
        return $this->belongsTo(Supplier::class, 'party_id');
    }

    public function isReversed(): bool
    {
        return $this->status === 'reversed';
    }
}

