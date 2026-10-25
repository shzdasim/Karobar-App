<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

class StockDisposal extends Model
{
    use HasFactory;

    public const STATUS_QUARANTINED = 'quarantined';
    public const STATUS_DISPOSED    = 'disposed';
    public const STATUS_RESTORED    = 'restored';

    public const CAUSES = [
        'shelf_expiry'           => 'Shelf Expiry (Expired Batch)',
        'breakage_damage'        => 'Physical Breakage / Packaging Damage',
        'temperature_spoilage'   => 'Cold-Chain / Temperature Spoilage',
        'customer_return_defect' => 'Defective Customer Return',
        'manufacturer_recall'    => 'Manufacturer / Regulatory Recall',
        'water_pest_damage'      => 'Moisture / Pest Contamination',
        'theft_shrinkage'        => 'Unrecoverable Shrinkage / Pilferage',
        'other'                  => 'Other Cause',
    ];

    public const METHODS = [
        'incineration'            => 'High-Temperature Incineration',
        'chemical_neutralization' => 'Chemical Neutralization / Encapsulation',
        'municipal_waste'         => 'Authorized Bio-Hazard / Municipal Waste',
        'supplier_destruction'    => 'Handed to Supplier (No Credit)',
        'drain_flush'             => 'Authorized Dilution & Drain Flush',
        'quarantine_holding'      => 'In Quarantine Bay (Pending Destruction)',
        'other'                   => 'Other Method',
    ];

    protected $fillable = [
        'disposal_number',
        'disposal_date',
        'status',
        'cause',
        'disposal_method',
        'reference_no',
        'witness_name',
        'witness_designation',
        'notes',
        'total_qty',
        'total_cost_value',
        'total_retail_value',
        'has_narcotics',
        'record_expense',
        'stock_adjustment_id',
        'expense_id',
        'created_by',
        'disposed_by',
        'disposed_at',
        'restored_by',
        'restored_at',
    ];

    protected $casts = [
        'disposal_date'      => 'date:Y-m-d',
        'total_qty'          => 'float',
        'total_cost_value'   => 'float',
        'total_retail_value' => 'float',
        'has_narcotics'      => 'boolean',
        'record_expense'     => 'boolean',
        'disposed_at'        => 'datetime',
        'restored_at'        => 'datetime',
    ];

    protected $appends = [
        'cause_label',
        'disposal_method_label',
    ];

    public function getCauseLabelAttribute(): string
    {
        return self::CAUSES[$this->cause] ?? ucfirst(str_replace('_', ' ', (string) $this->cause));
    }

    public function getDisposalMethodLabelAttribute(): string
    {
        return self::METHODS[$this->disposal_method] ?? ucfirst(str_replace('_', ' ', (string) $this->disposal_method));
    }

    public function items(): HasMany
    {
        return $this->hasMany(StockDisposalItem::class, 'stock_disposal_id');
    }

    public function stockAdjustment(): BelongsTo
    {
        return $this->belongsTo(StockAdjustment::class, 'stock_adjustment_id');
    }

    public function expense(): BelongsTo
    {
        return $this->belongsTo(Expense::class, 'expense_id');
    }

    public function creator(): BelongsTo
    {
        return $this->belongsTo(User::class, 'created_by');
    }

    public function disposer(): BelongsTo
    {
        return $this->belongsTo(User::class, 'disposed_by');
    }

    public function restorer(): BelongsTo
    {
        return $this->belongsTo(User::class, 'restored_by');
    }
}

