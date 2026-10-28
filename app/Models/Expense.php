<?php

namespace App\Models;

use Carbon\Carbon;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\SoftDeletes;

class Expense extends Model
{
    use HasFactory, SoftDeletes;

    protected $fillable = [
        'expense_number',
        'expense_category_id',
        'account_id',
        'user_id',
        'cash_drawer_session_id',
        'amount',
        'date',
        'payment_method',
        'reference_number',
        'payee',
        'description',
        'receipt_path',
        'is_recurring',
        'recurring_frequency',
    ];

    protected $casts = [
        'amount'       => 'float',
        'date'         => 'date:Y-m-d',
        'is_recurring' => 'boolean',
    ];

    protected $appends = [
        'receipt_url',
        'next_recurring_date',
    ];

    public function getReceiptUrlAttribute(): ?string
    {
        if (!$this->receipt_path) {
            return null;
        }
        if (
            str_starts_with($this->receipt_path, 'http://') ||
            str_starts_with($this->receipt_path, 'https://') ||
            str_starts_with($this->receipt_path, '/storage/')
        ) {
            return $this->receipt_path;
        }

        return '/storage/' . ltrim($this->receipt_path, '/');
    }

    public function getNextRecurringDate(): ?Carbon
    {
        if (!$this->is_recurring || !$this->recurring_frequency || !$this->date) {
            return null;
        }

        $baseDate = Carbon::parse($this->date)->startOfDay();

        return match ($this->recurring_frequency) {
            'weekly'  => $baseDate->copy()->addWeek(),
            'monthly' => $baseDate->copy()->addMonthNoOverflow(),
            'yearly'  => $baseDate->copy()->addYearNoOverflow(),
            default   => null,
        };
    }

    public function getNextRecurringDateAttribute(): ?string
    {
        return $this->getNextRecurringDate()?->toDateString();
    }

    public function category(): BelongsTo
    {
        return $this->belongsTo(ExpenseCategory::class, 'expense_category_id');
    }

    public function account(): BelongsTo
    {
        return $this->belongsTo(Account::class);
    }

    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class);
    }

    public function drawerSession(): BelongsTo
    {
        return $this->belongsTo(CashDrawerSession::class, 'cash_drawer_session_id');
    }

    /**
     * Generate sequential unique expense number for the given date.
     */
    public static function generateExpenseNumber(?string $dateStr = null): string
    {
        $d = $dateStr ? Carbon::parse($dateStr) : Carbon::today();
        $datePrefix = 'EXP-' . $d->format('Ymd') . '-';

        $last = self::withTrashed()
            ->where('expense_number', 'like', $datePrefix . '%')
            ->orderByDesc('id')
            ->value('expense_number');

        if ($last) {
            $seq = (int) substr($last, strlen($datePrefix)) + 1;
        } else {
            $seq = 1;
        }

        return $datePrefix . str_pad($seq, 4, '0', STR_PAD_LEFT);
    }
}

