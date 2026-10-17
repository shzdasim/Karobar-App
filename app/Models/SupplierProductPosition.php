<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class SupplierProductPosition extends Model
{
    protected $fillable = ['supplier_id', 'product_id', 'position'];

    protected $casts = ['supplier_id' => 'integer', 'product_id' => 'integer', 'position' => 'integer'];
}
