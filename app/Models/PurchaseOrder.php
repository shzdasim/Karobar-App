<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class PurchaseOrder extends Model
{
    protected $fillable = ['date', 'supplier_id', 'status', 'reference', 'notes', 'created_by', 'updated_by', 'received_by', 'received_at'];

    protected $casts = ['received_at' => 'datetime'];

    public function supplier()
    {
        return $this->belongsTo(Supplier::class);
    }

    public function items()
    {
        return $this->hasMany(PurchaseOrderItem::class);
    }

    public function invoice()
    {
        return $this->hasOne(PurchaseInvoice::class);
    }
}
