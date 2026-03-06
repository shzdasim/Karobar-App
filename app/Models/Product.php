<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Support\Facades\DB;

class Product extends Model
{
    use HasFactory;

    protected $fillable = [
        'product_code',
        'name',
        'image',
        'formulation',
        'description',
        'pack_size',
        'quantity',
        'pack_purchase_price',
        'pack_sale_price',
        'unit_purchase_price',
        'unit_sale_price',
        'whole_sale_pack_price',
        'whole_sale_unit_price',
        'whole_sale_margin',
        'avg_price',
        'margin',
        'narcotic',
        'max_discount',
        'category_id',
        'brand_id',
        'supplier_id',
        'rack',
        'barcode',
    ];

    public function brand()
    {
        return $this->belongsTo(Brand::class);
    }

    public function category()
    {
        return $this->belongsTo(Category::class);
    }

    public function supplier()
    {
        return $this->belongsTo(Supplier::class);
    }

    public function batches()
    {
        return $this->hasMany(Batch::class);
    }

    /**
     * Apply a purchase item using incremental weighted-average logic.
     * IMPORTANT: use line-level avg_price (effective cost) so bonuses/discounts are included.
     *
     * $item keys expected:
     * - quantity (units), avg_price
     * - unit_purchase_price/pack_purchase_price/pack_sale_price/unit_sale_price (optional passthroughs)
     */
    public function applyPurchaseFromItem(array $item): void
    {
        $oldQty   = (int) ($this->quantity ?? 0);
        $oldAvg   = (float) ($this->avg_price ?? 0.0);
        $newQty   = (int) ($item['quantity'] ?? 0);
        $effCost  = (float) ($item['avg_price'] ?? $item['unit_purchase_price'] ?? 0.0); // <-- use effective cost

        $totalQty = $oldQty + $newQty;

        $weightedAvg = $totalQty > 0
            ? (($oldQty * $oldAvg) + ($newQty * $effCost)) / $totalQty
            : $effCost;

        // Update live fields (latest prices kept if provided)
        $this->quantity = $totalQty;

        if (array_key_exists('pack_purchase_price', $item)) {
            $this->pack_purchase_price = $item['pack_purchase_price'];
        }
        if (array_key_exists('unit_purchase_price', $item)) {
            $this->unit_purchase_price = $item['unit_purchase_price'];
        }
        if (array_key_exists('pack_sale_price', $item)) {
            $this->pack_sale_price = $item['pack_sale_price'];
        }
        if (array_key_exists('unit_sale_price', $item)) {
            $this->unit_sale_price = $item['unit_sale_price'];
        }
        
        // Update wholesale prices if provided
        if (array_key_exists('whole_sale_pack_price', $item)) {
            $this->whole_sale_pack_price = $item['whole_sale_pack_price'];
        }
        if (array_key_exists('whole_sale_unit_price', $item)) {
            $this->whole_sale_unit_price = $item['whole_sale_unit_price'];
        }

        $this->avg_price = round($weightedAvg, 2);

        // Margin on sale price
        $this->margin = ($this->unit_sale_price > 0)
            ? round((($this->unit_sale_price - $this->avg_price) / $this->unit_sale_price) * 100, 2)
            : 0.0;
        
        // Wholesale margin (if wholesale unit price is set)
        if ($this->whole_sale_unit_price > 0) {
            // Calculate and store wholesale margin in a separate field
            $this->whole_sale_margin = round((($this->whole_sale_unit_price - $this->avg_price) / $this->whole_sale_unit_price) * 100, 2);
        }

        $this->save();
    }

    /**
     * Revert a purchase item using the exact inverse weighted-average math.
     * Also uses line-level avg_price (effective cost).
     *
     * $item may be array or PurchaseInvoiceItem model:
     * - quantity (units), avg_price
     * 
     * @param mixed $item The item to revert (array or model)
     * @param int|null $excludeItemId Optional item ID to exclude from remaining purchases calculation
     */
    public function revertPurchaseFromItem($item, ?int $excludeItemId = null): void
    {
        $qty = (int) (is_array($item) ? ($item['quantity'] ?? 0) : $item->quantity);

        // Adjust quantity — allow negative inventory
        $this->quantity = ((int) $this->quantity) - $qty;

        // If stock becomes zero or negative, reset average price to 0
        // This ensures moving average resets when all stock is removed
        if ($this->quantity <= 0) {
            $this->avg_price = 0;
            $this->margin = 0;
            $this->whole_sale_margin = 0;
        }
        // If there's remaining stock, recalculate average from remaining purchase invoices
        // This handles the case where we're deleting a purchase but other purchases still exist
        elseif ($this->quantity > 0) {
            // Get remaining purchase invoice items for this product, excluding the current item
            $query = DB::table('purchase_invoice_items')
                ->where('product_id', $this->id)
                ->select('quantity', 'avg_price');
            
            // Exclude the current item from the calculation if ID is provided
            if ($excludeItemId !== null) {
                $query->where('id', '!=', $excludeItemId);
            }
            
            $remainingItems = $query->get();
            
            $totalQty = 0;
            $totalCost = 0.0;
            
            foreach ($remainingItems as $ri) {
                $q = (int) $ri->quantity;
                $totalQty += $q;
                $totalCost += $q * (float) $ri->avg_price;
            }
            
            // Recalculate average based on remaining purchase invoices
            if ($totalQty > 0) {
                $this->avg_price = round($totalCost / $totalQty, 2);
            }
            
            // Recalculate margins
            $this->margin = ($this->unit_sale_price > 0 && $this->avg_price > 0)
                ? round((($this->unit_sale_price - $this->avg_price) / $this->unit_sale_price) * 100, 2)
                : 0.0;
                
            if ($this->whole_sale_unit_price > 0) {
                $this->whole_sale_margin = ($this->avg_price > 0)
                    ? round((($this->whole_sale_unit_price - $this->avg_price) / $this->whole_sale_unit_price) * 100, 2)
                    : 0.0;
            }
        }

        $this->save();
    }

}
