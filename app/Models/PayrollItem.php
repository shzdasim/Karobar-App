<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class PayrollItem extends Model
{
    protected $fillable = [
        'payroll_run_id',
        'employee_id',
        'working_days',
        'present_days',
        'absent_days',
        'late_days',
        'half_days',
        'leave_days',
        'overtime_hours',
        'basic_salary',
        'allowances',
        'overtime_pay',
        'sales_invoices_count',
        'sales_volume',
        'sales_gross_profit',
        'sales_commission',
        'target_bonus',
        'other_bonus',
        'gross_earnings',
        'absence_deduction',
        'late_or_fine_deduction',
        'advance_deduction',
        'other_deduction',
        'total_deductions',
        'net_payable',
        'payment_status',
        'payment_method',
        'account_id',
        'expense_id',
        'paid_at',
        'notes',
    ];

    protected $casts = [
        'payroll_run_id'         => 'integer',
        'employee_id'            => 'integer',
        'working_days'           => 'integer',
        'present_days'           => 'integer',
        'absent_days'            => 'integer',
        'late_days'              => 'integer',
        'half_days'              => 'integer',
        'leave_days'             => 'integer',
        'overtime_hours'         => 'float',
        'basic_salary'           => 'float',
        'allowances'             => 'float',
        'overtime_pay'           => 'float',
        'sales_invoices_count'   => 'integer',
        'sales_volume'           => 'float',
        'sales_gross_profit'     => 'float',
        'sales_commission'       => 'float',
        'target_bonus'           => 'float',
        'other_bonus'            => 'float',
        'gross_earnings'         => 'float',
        'absence_deduction'      => 'float',
        'late_or_fine_deduction' => 'float',
        'advance_deduction'      => 'float',
        'other_deduction'        => 'float',
        'total_deductions'       => 'float',
        'net_payable'            => 'float',
        'account_id'             => 'integer',
        'expense_id'             => 'integer',
        'paid_at'                => 'datetime',
    ];

    public function payrollRun()
    {
        return $this->belongsTo(PayrollRun::class);
    }

    public function employee()
    {
        return $this->belongsTo(Employee::class);
    }

    public function account()
    {
        return $this->belongsTo(Account::class, 'account_id');
    }

    public function expense()
    {
        return $this->belongsTo(Expense::class, 'expense_id');
    }
}

