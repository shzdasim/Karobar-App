<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Customer extends Model
{
    protected $fillable = [
        'name',
        'email',
        'phone',
        'tax_number',
        'tax_status',
        'address',
        'credit_limit',
        'credit_days',
        'credit_status',
    ];

    protected $casts = [
        'credit_limit' => 'decimal:2',
        'credit_days'  => 'integer',
    ];

    public function getNetBalance(): float
    {
        return CustomerLedger::getCustomerNetBalance($this->id);
    }

    public function getAvailableCredit(): ?float
    {
        if ($this->credit_limit === null || (float)$this->credit_limit <= 0) {
            return null; // Unlimited
        }
        $net = $this->getNetBalance();
        return round(max(0.0, (float)$this->credit_limit - $net), 2);
    }

    public function isCreditLimitExceeded(): bool
    {
        if ($this->credit_limit === null || (float)$this->credit_limit <= 0) {
            return false;
        }
        return $this->getNetBalance() > (float)$this->credit_limit;
    }
    public function saleInvoices()
    {
        return $this->hasMany(\App\Models\SaleInvoice::class);
    }

    public function saleReturns()
    {
        return $this->hasMany(\App\Models\SaleReturn::class);
    }

    public function wholeSalePrices()
    {
        return $this->hasMany(\App\Models\CustomerWholeSalePrice::class);
    }
}
