<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

class JournalEntry extends Model
{
    protected $fillable = [
        'entry_number',
        'entry_date',
        'reference_type',
        'reference_number',
        'account_id',
        'description',
        'total_debit',
        'total_credit',
        'status',
        'created_by',
        'reversed_by',
        'reversed_at',
        'reversal_reason',
    ];

    protected $casts = [
        'entry_date'   => 'date:Y-m-d',
        'total_debit'  => 'decimal:2',
        'total_credit' => 'decimal:2',
        'reversed_at'  => 'datetime',
    ];

    public function lines(): HasMany
    {
        return $this->hasMany(JournalEntryLine::class, 'journal_entry_id');
    }

    public function creator(): BelongsTo
    {
        return $this->belongsTo(User::class, 'created_by');
    }

    public function reversedByUser(): BelongsTo
    {
        return $this->belongsTo(User::class, 'reversed_by');
    }

    public function treasuryAccount(): BelongsTo
    {
        return $this->belongsTo(Account::class, 'account_id');
    }

    public static function generateEntryNumber(?string $date = null): string
    {
        $prefix = 'JV-' . str_replace('-', '', $date ?: now()->toDateString()) . '-';
        $last = static::where('entry_number', 'like', $prefix . '%')
            ->orderByDesc('id')
            ->value('entry_number');

        if ($last && preg_match('/-(\d+)$/', $last, $m)) {
            $next = ((int) $m[1]) + 1;
        } else {
            $next = (static::count() + 1);
        }

        return $prefix . str_pad((string) $next, 4, '0', STR_PAD_LEFT);
    }
}

