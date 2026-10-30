<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Brand extends Model
{
    protected $fillable = [
        'name',
        'image',
        'expiry_return_policy',
        'expiry_return_months',
        'expiry_return_days',
        'expiry_return_notes',
    ];

    protected $casts = [
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
        return self::formatExpiryPolicyLabel(
            $this->expiry_return_policy,
            $this->expiry_return_months,
            $this->expiry_return_days
        );
    }

    public static function formatExpiryPolicyLabel(?string $policy, ?int $months, ?int $days): string
    {
        $policy = $policy ?: 'before_expiry';

        if ($policy === 'accepts_expired') {
            return 'Accepts Expired Products';
        }

        if ($policy === 'non_returnable') {
            return 'Non-Returnable';
        }

        $m = $months !== null ? (int) $months : null;
        $d = $days !== null ? (int) $days : null;

        if ($m !== null && $m > 0) {
            if ($m % 12 === 0) {
                $years = (int) ($m / 12);
                return $years === 1
                    ? '1 Year Before Expiry'
                    : "{$years} Years Before Expiry";
            }
            return $m === 1
                ? '1 Month Before Expiry'
                : "{$m} Months Before Expiry";
        }

        if ($d !== null && $d > 0) {
            if ($d === 365) {
                return '1 Year Before Expiry';
            }
            if ($d === 730) {
                return '2 Years Before Expiry';
            }
            if ($d % 30 === 0) {
                $calcMonths = (int) ($d / 30);
                return $calcMonths === 1
                    ? '1 Month Before Expiry'
                    : "{$calcMonths} Months Before Expiry";
            }
            return "{$d} Days Before Expiry";
        }

        return '3 Months Before Expiry';
    }
}
