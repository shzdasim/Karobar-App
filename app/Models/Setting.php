<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Facades\Storage;

class Setting extends Model
{
    protected $fillable = [
        'store_name',
        'phone_number',
        'address',
        'license_number',
        'note',
        'printer_type',
        'thermal_template',
        'a4_template',
        'logo_path',
        'navigation_style',
        'sale_system',
        'shop_type',
    ];
    protected $appends = ['logo_url'];

    public function getLogoUrlAttribute(): ?string
    {
        return $this->logo_path ? Storage::url($this->logo_path) : null;
    }
}
