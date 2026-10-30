<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Supplier extends Model
{
    protected $fillable = [
        'name',
        'address',
        'phone',
        'tax_number',
        'tax_status',
        'email',
        'credit_limit',
        'credit_days',
        'expiry_return_policy',
        'expiry_return_months',
        'expiry_return_days',
        'expiry_return_notes',
    ];

    protected $casts = [
        'credit_limit'         => 'decimal:2',
        'credit_days'          => 'integer',
        'expiry_return_months' => 'integer',
        'expiry_return_days'   => 'integer',
    ];

    protected $appends = [
        'expiry_policy_label',
    ];

    public function products()
    {
        return $this->hasMany(\App\Models\Product::class);
    }

    public function getExpiryPolicyLabelAttribute(): string
    {
        return Brand::formatExpiryPolicyLabel(
            $this->expiry_return_policy,
            $this->expiry_return_months,
            $this->expiry_return_days
        );
    }
}
