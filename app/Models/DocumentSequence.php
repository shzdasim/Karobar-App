<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class DocumentSequence extends Model
{
    protected $fillable = [
        'sequence_key',
        'prefix',
        'padding',
        'last_number',
    ];

    protected $casts = [
        'padding' => 'integer',
        'last_number' => 'integer',
    ];
}
