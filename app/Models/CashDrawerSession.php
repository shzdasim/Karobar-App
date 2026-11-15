<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class CashDrawerSession extends Model
{
    protected $fillable = [
        'user_id',
        'terminal_id',
        'opened_at',
        'closed_at',
        'status',
        'opening_balance',
        'closing_balance_expected',
        'closing_balance_actual',
        'difference',
        'closing_denominations',
        'notes',
    ];

    protected $casts = [
        'terminal_id'              => 'integer',
        'opened_at'                => 'datetime',
        'closed_at'                => 'datetime',
        'opening_balance'          => 'float',
        'closing_balance_expected' => 'float',
        'closing_balance_actual'   => 'float',
        'difference'               => 'float',
        'closing_denominations'    => 'array',
    ];

    public function user()
    {
        return $this->belongsTo(User::class);
    }

    public function terminal()
    {
        return $this->belongsTo(Terminal::class, 'terminal_id');
    }

    public function transactions()
    {
        return $this->hasMany(CashDrawerTransaction::class, 'session_id')
            ->orderBy('created_at', 'asc')
            ->orderBy('id', 'asc');
    }
}

