<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

class Account extends Model
{
    use HasFactory;

    protected $fillable = [
        'name',
        'type',
        'code',
        'icon',
        'color',
        'bank_name',
        'account_number',
        'opening_balance',
        'current_balance',
        'is_active',
        'is_default',
        'notes',
    ];

    protected $casts = [
        'opening_balance' => 'decimal:2',
        'current_balance' => 'decimal:2',
        'is_active'       => 'boolean',
        'is_default'      => 'boolean',
    ];

    public function transactions(): HasMany
    {
        return $this->hasMany(AccountTransaction::class, 'account_id')->latest('transaction_date');
    }
}

