<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Bank extends Model
{
    protected $fillable = [
        'bank_name',
        'account_number',
        'image_path',
        'balance',
    ];

    protected $casts = [
        'balance' => 'decimal:2',
    ];

    public function ledgers()
    {
        return $this->hasMany(BankLedger::class);
    }
}

