<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class PayrollRun extends Model
{
    protected $fillable = [
        'run_number',
        'period_month',
        'start_date',
        'end_date',
        'status',
        'total_basic',
        'total_allowances',
        'total_overtime',
        'total_commissions',
        'total_bonuses',
        'total_deductions',
        'total_advance_deductions',
        'total_net_payable',
        'total_paid',
        'notes',
        'generated_by',
        'paid_at',
    ];

    protected $casts = [
        'total_basic'              => 'float',
        'total_allowances'         => 'float',
        'total_overtime'           => 'float',
        'total_commissions'        => 'float',
        'total_bonuses'            => 'float',
        'total_deductions'         => 'float',
        'total_advance_deductions' => 'float',
        'total_net_payable'        => 'float',
        'total_paid'               => 'float',
        'paid_at'                  => 'datetime',
    ];

    public function items()
    {
        return $this->hasMany(PayrollItem::class);
    }

    public function generatedByUser()
    {
        return $this->belongsTo(User::class, 'generated_by');
    }
}

