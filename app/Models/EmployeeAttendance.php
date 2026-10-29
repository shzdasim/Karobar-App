<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class EmployeeAttendance extends Model
{
    protected $fillable = [
        'employee_id',
        'date',
        'status',
        'check_in',
        'check_out',
        'late_minutes',
        'overtime_hours',
        'fine_amount',
        'notes',
        'recorded_by',
    ];

    protected $casts = [
        'employee_id'    => 'integer',
        'late_minutes'   => 'integer',
        'overtime_hours' => 'float',
        'fine_amount'    => 'float',
    ];

    public function employee()
    {
        return $this->belongsTo(Employee::class);
    }
}

