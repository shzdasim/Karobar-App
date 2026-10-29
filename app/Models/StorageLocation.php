<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class StorageLocation extends Model
{
    protected $fillable = [
        'code',
        'name',
        'type',
        'default_rack',
        'is_default',
        'is_active',
        'notes',
    ];

    protected $casts = [
        'is_default' => 'boolean',
        'is_active'  => 'boolean',
    ];

    public function products()
    {
        return $this->hasMany(Product::class, 'storage_location_id');
    }

    public function batches()
    {
        return $this->hasMany(Batch::class, 'storage_location_id');
    }
}

