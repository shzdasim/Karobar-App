<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Supplier extends Model
{
    protected $fillable = [
        'name',
        'address',
        'phone',
        'email',
        'credit_limit',
        'credit_days',
    ];

    protected $casts = [
        'credit_limit' => 'decimal:2',
        'credit_days'  => 'integer',
    ];

    public function products() { return $this->hasMany(\App\Models\Product::class); }

}
