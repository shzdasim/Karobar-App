<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\MorphTo;

class DocumentHistory extends Model
{
    protected $fillable = [
        'user_id',
        'entity_type',
        'entity_id',
        'action',
        'reason',
        'before_payload',
        'after_payload',
        'diff',
    ];

    protected $casts = [
        'before_payload' => 'array',
        'after_payload'  => 'array',
        'diff'            => 'array',
        'created_at'      => 'datetime',
        'updated_at'      => 'datetime',
    ];

    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class);
    }

    public function entity(): MorphTo
    {
        return $this->morphTo();
    }

    public function scopeForEntity($query, string $type, int|string $id)
    {
        return $query->where('entity_type', $type)->where('entity_id', $id);
    }
}

