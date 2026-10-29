<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class EmployeeAdvance extends Model
{
    protected $fillable = [
        'advance_number',
        'employee_id',
        'type',
        'amount',
        'balance_after',
        'date',
        'payment_method',
        'account_id',
        'payroll_item_id',
        'reference_number',
        'description',
        'created_by',
    ];

    protected $casts = [
        'employee_id'     => 'integer',
        'amount'          => 'float',
        'balance_after'   => 'float',
        'account_id'      => 'integer',
        'payroll_item_id' => 'integer',
    ];

    public function employee()
    {
        return $this->belongsTo(Employee::class);
    }

    public function account()
    {
        return $this->belongsTo(Account::class, 'account_id');
    }

    public static function generateAdvanceNumber(string $dateStr): string
    {
        $prefix = 'ADV-' . str_replace('-', '', substr($dateStr, 0, 10)) . '-';
        $count = static::where('advance_number', 'like', $prefix . '%')->count();
        return $prefix . str_pad((string) ($count + 1), 3, '0', STR_PAD_LEFT);
    }
}

