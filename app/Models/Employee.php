<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Employee extends Model
{
    protected $fillable = [
        'employee_code',
        'user_id',
        'name',
        'phone',
        'email',
        'cnic',
        'address',
        'department',
        'designation',
        'employment_type',
        'joining_date',
        'shift_name',
        'shift_start',
        'shift_end',
        'working_days_per_month',
        'basic_salary',
        'default_allowances',
        'hourly_overtime_rate',
        'commission_type',
        'commission_rate',
        'monthly_sales_target',
        'target_bonus_amount',
        'payment_method',
        'bank_name',
        'bank_account_number',
        'advance_balance',
        'status',
        'notes',
    ];

    protected $casts = [
        'user_id'                => 'integer',
        'working_days_per_month' => 'integer',
        'basic_salary'           => 'float',
        'default_allowances'     => 'float',
        'hourly_overtime_rate'   => 'float',
        'commission_rate'        => 'float',
        'monthly_sales_target'   => 'float',
        'target_bonus_amount'    => 'float',
        'advance_balance'        => 'float',
    ];

    public function user()
    {
        return $this->belongsTo(User::class, 'user_id');
    }

    public function attendances()
    {
        return $this->hasMany(EmployeeAttendance::class);
    }

    public function advances()
    {
        return $this->hasMany(EmployeeAdvance::class);
    }

    public function payrollItems()
    {
        return $this->hasMany(PayrollItem::class);
    }

    public static function generateEmployeeCode(): string
    {
        $last = static::orderByDesc('id')->first();
        if ($last && preg_match('/EMP-(\d+)/', (string) $last->employee_code, $m)) {
            $next = ((int) $m[1]) + 1;
        } else {
            $next = static::count() + 1;
        }
        return 'EMP-' . str_pad((string) $next, 4, '0', STR_PAD_LEFT);
    }
}

