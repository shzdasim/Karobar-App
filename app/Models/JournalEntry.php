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
        $ymd = str_replace('-', '', $date ?: now()->toDateString());
        $prefix = 'JV-' . $ymd . '-';

        return \App\Services\DocumentSequenceService::next(
            "journal_entry:{$ymd}",
            $prefix,
            4,
            'journal_entries',
            'entry_number'
        );
    }
}

