<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Doctor extends Model
{
    protected $fillable = [
        'name',
        'pmdc_number',
        'hospital_clinic',
        'specialization',
        'phone',
        'address',
        'is_active',
    ];

    protected $casts = [
        'is_active' => 'boolean',
    ];

    public function saleInvoices()
    {
        return $this->hasMany(SaleInvoice::class);
    }
}
