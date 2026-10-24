<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Factories\HasFactory;

class BarcodeTemplate extends Model
{
    use HasFactory;

    protected $fillable = [
        'name',
        'paper_type',
        'width_mm',
        'height_mm',
        'orientation',
        'columns',
        'rows',
        'gap_x_mm',
        'gap_y_mm',
        'margin_top_mm',
        'margin_left_mm',
        'margin_right_mm',
        'margin_bottom_mm',
        'layout_options',
        'is_default',
    ];

    protected $casts = [
        'width_mm' => 'float',
        'height_mm' => 'float',
        'gap_x_mm' => 'float',
        'gap_y_mm' => 'float',
        'margin_top_mm' => 'float',
        'margin_left_mm' => 'float',
        'margin_right_mm' => 'float',
        'margin_bottom_mm' => 'float',
        'columns' => 'integer',
        'rows' => 'integer',
        'layout_options' => 'array',
        'is_default' => 'boolean',
    ];
}

