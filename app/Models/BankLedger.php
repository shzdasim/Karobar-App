<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class BankLedger extends Model
{
    protected $fillable = [
        'bank_id',
        'entry_date',
        'entry_type',
        'ref_type',
        'ref_id',
        'direction',
        'amount',
        'description',
    ];

    protected $casts = [
        'entry_date' => 'date',
        'amount'     => 'decimal:2',
    ];

    public function bank()
    {
        return $this->belongsTo(Bank::class);
    }
}

