<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class CashDrawerTransaction extends Model
{
    protected $fillable = [
        'session_id',
        'user_id',
        'type',
        'amount',
        'reference_type',
        'reference_id',
        'notes',
    ];

    protected $casts = [
        'amount' => 'float',
    ];

    public function session()
    {
        return $this->belongsTo(CashDrawerSession::class, 'session_id');
    }

    public function user()
    {
        return $this->belongsTo(User::class);
    }
}

