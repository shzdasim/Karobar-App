<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\SoftDeletes;

class InvoiceDraft extends Model
{
    use HasFactory, SoftDeletes;

    protected $fillable = [
        'uuid',
        'user_id',
        'type',
        'title',
        'party_name',
        'total_amount',
        'items_count',
        'version',
        'payload',
        'status',
        'posted_invoice_id',
        'posted_invoice_type',
    ];

    protected $casts = [
        'payload'      => 'array',
        'total_amount' => 'decimal:2',
        'items_count'  => 'integer',
        'version'      => 'integer',
    ];

    public function user()
    {
        return $this->belongsTo(User::class);
    }

    public function postedInvoice()
    {
        return $this->morphTo();
    }
}

