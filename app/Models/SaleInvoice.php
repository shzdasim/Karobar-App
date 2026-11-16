<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class SaleInvoice extends Model
{
    protected $fillable = [
        'user_id',
        'terminal_id',
        'salesman_id',
        'customer_id',
        'doctor_id',
        'invoice_type',
        'payment_method',
        'sale_type',
        'wholesale_type',
        'posted_number',
        'date',
        'due_date',
        'remarks',
        'doctor_name',
        'doctor_pmdc',
        'doctor_hospital',
        'patient_name',
        'patient_cnic',
        'patient_address',
        'prescription_no',
        'pharmacist_name',
        'discount_percentage',
        'discount_amount',
        'tax_percentage',
        'tax_amount',
        'item_discount',
        'gross_amount',
        'total',
        'total_receive',
        'account_id',
        'status',
        'reversed_at',
        'reversed_by_id',
        'reversal_reason',
        'fbr_invoice_number',
        'fbr_usin',
        'fbr_qr_data',
        'fbr_sync_status',
        'fbr_synced_at',
        'fbr_error_message',
        'fbr_response_payload',
        'fbr_pos_fee',
        'further_tax_amount',
        'advance_wht_percentage',
        'advance_wht_amount',
    ];

    protected $casts = [
        'fbr_synced_at'        => 'datetime',
        'fbr_response_payload' => 'array',
    ];

    public function reversedBy()
    {
        return $this->belongsTo(User::class, 'reversed_by_id');
    }

    public function customer()
    {
        return $this->belongsTo(Customer::class);
    }
    public function doctor()
    {
        return $this->belongsTo(Doctor::class);
    }
    public function account()
    {
        return $this->belongsTo(Account::class);
    }
    public function user()
    {
        return $this->belongsTo(User::class);
    }
    public function salesman()
    {
        return $this->belongsTo(Employee::class, 'salesman_id');
    }
    public function items()
    {
        return $this->hasMany(SaleInvoiceItem::class)->orderBy('id');
    }
    
}
