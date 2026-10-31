<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class CubeProviderUsage extends Model
{
    protected $table = 'cube_provider_usages';

    protected $fillable = [
        'provider',
        'usage_date',
        'request_count',
        'success_count',
        'failure_count',
        'status',
        'cooldown_until',
        'disabled_reason',
        'config_fingerprint',
        'last_http_status',
        'last_error_code',
    ];

    protected function casts(): array
    {
        return [
            'request_count' => 'integer',
            'success_count' => 'integer',
            'failure_count' => 'integer',
            'last_http_status' => 'integer',
            'cooldown_until' => 'datetime',
        ];
    }
}

