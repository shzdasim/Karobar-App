<?php

namespace App\Services;

use App\Models\Customer;
use App\Models\Product;
use App\Models\TradeScheme;
use Illuminate\Support\Collection;

class TradeSchemeService
{
    /**
     * Get active schemes eligible for a given sale context.
     */
    public function getActiveSchemes(?string $saleType = null, ?int $customerId = null, ?string $date = null): Collection
    {
        if ($customerId) {
            $customer = Customer::find($customerId);
            if ($customer && $customer->eligible_for_schemes === false) {
                return collect();
            }
        }

        return TradeScheme::active($date)
            ->forSaleType($saleType)
            ->forCustomer($customerId)
            ->with([
                'slabs',
                'product:id,name,brand_id,category_id,pack_size',
                'brand:id,name',
                'category:id,name',
            ])
            ->orderByDesc('priority')
            ->orderBy('id')
            ->get();
    }

    /**
     * Evaluate matching trade scheme for an invoice line item.
     */
    public function evaluateBonus(array $item, Collection $activeSchemes, ?string $saleType = null): array
    {
        $productId = (int) ($item['product_id'] ?? 0);
        if (!$productId || $activeSchemes->isEmpty()) {
            return $this->noMatch();
        }

        $packSize = max(1, (int) ($item['pack_size'] ?? 1));
        $packQty = max(0.0, (float) ($item['pack_quantity'] ?? 0));
        $unitQty = max(0, (int) ($item['unit_quantity'] ?? 0));
        $totalUnits = (int) ($item['quantity'] ?? ($packQty * $packSize + $unitQty));

        $isRetail = ($saleType === 'retail')
            || (($item['sale_type'] ?? null) === 'retail')
            || (!array_key_exists('pack_quantity', $item) && !array_key_exists('unit_quantity', $item));

        // Get product details to check brand and category targets
        $brandId = $item['brand_id'] ?? null;
        $categoryId = $item['category_id'] ?? null;

        if ($brandId === null || $categoryId === null) {
            $prod = Product::select('id', 'brand_id', 'category_id', 'pack_size')->find($productId);
            if ($prod) {
                $brandId = $prod->brand_id;
                $categoryId = $prod->category_id;
                if (!isset($item['pack_size']) && $prod->pack_size) {
                    $packSize = max(1, (int) $prod->pack_size);
                }
            }
        }

        // Find best matching scheme:
        // Priority 1: Direct Product match
        // Priority 2: Brand match
        // Priority 3: Category match
        $matchedScheme = $activeSchemes->first(function (TradeScheme $s) use ($productId) {
            return $s->target_type === 'product' && (int) $s->target_id === $productId;
        });

        if (!$matchedScheme && $brandId) {
            $matchedScheme = $activeSchemes->first(function (TradeScheme $s) use ($brandId) {
                return $s->target_type === 'brand' && (int) $s->target_id === (int) $brandId;
            });
        }

        if (!$matchedScheme && $categoryId) {
            $matchedScheme = $activeSchemes->first(function (TradeScheme $s) use ($categoryId) {
                return $s->target_type === 'category' && (int) $s->target_id === (int) $categoryId;
            });
        }

        if (!$matchedScheme || $matchedScheme->slabs->isEmpty()) {
            return $this->noMatch();
        }

        $bonusType = $matchedScheme->bonus_type ?: 'pack'; // 'pack' or 'unit'
        $slabs = $matchedScheme->slabs->sortBy('min_qty');

        $packBonus = 0.0;
        $unitBonus = 0;
        $bonusUnits = 0;
        $matchedSlab = null;

        if ($isRetail) {
            if ($matchedScheme->scheme_type === 'ratio') {
                $firstSlab = $slabs->first();
                $minQ = (float) ($firstSlab->min_qty ?? 0);
                $bQ = (float) ($firstSlab->bonus_qty ?? 0);

                if ($minQ > 0 && $totalUnits >= $minQ) {
                    $multiplier = floor($totalUnits / $minQ);
                    if ($multiplier > 0) {
                        $bonusUnits = (int) ($multiplier * $bQ);
                        $matchedSlab = $firstSlab;
                    }
                }
            } else {
                // Tiered slabs
                foreach ($slabs as $slab) {
                    $sMin = (float) $slab->min_qty;
                    $sMax = $slab->max_qty !== null ? (float) $slab->max_qty : null;

                    $qualifying = $bonusType === 'pack' && $totalUnits >= ($sMin * $packSize)
                        ? floor($totalUnits / $packSize)
                        : $totalUnits;

                    if ($qualifying >= $sMin && ($sMax === null || $qualifying <= $sMax)) {
                        $bonusUnits = $bonusType === 'pack' && $totalUnits >= ($sMin * $packSize)
                            ? (int) round((float) $slab->bonus_qty * $packSize)
                            : (int) round((float) $slab->bonus_qty);
                        $matchedSlab = $slab;
                        break;
                    }
                }
            }

            $unitBonus = $bonusUnits;
            $packBonus = $packSize > 0 ? round($bonusUnits / $packSize, 2) : 0.0;
        } else {
            // Wholesale mode
            $qualifyingPacks = $packQty + ($packSize > 0 ? $unitQty / $packSize : 0);

            if ($matchedScheme->scheme_type === 'ratio') {
                $firstSlab = $slabs->first();
                $minQ = (float) ($firstSlab->min_qty ?? 0);
                $bQ = (float) ($firstSlab->bonus_qty ?? 0);

                if ($minQ > 0) {
                    if ($bonusType === 'pack') {
                        if ($packQty >= $minQ) {
                            $multiplier = floor($packQty / $minQ);
                            $packBonus = round($multiplier * $bQ, 2);
                            $unitBonus = (int) round($packBonus * $packSize);
                            $bonusUnits = $unitBonus;
                            $matchedSlab = $firstSlab;
                        } elseif ($qualifyingPacks >= $minQ) {
                            $multiplier = floor($qualifyingPacks / $minQ);
                            $packBonus = round($multiplier * $bQ, 2);
                            $unitBonus = (int) round($packBonus * $packSize);
                            $bonusUnits = $unitBonus;
                            $matchedSlab = $firstSlab;
                        } elseif ($unitQty >= $minQ) {
                            $multiplier = floor($unitQty / $minQ);
                            $unitBonus = (int) ($multiplier * $bQ);
                            $packBonus = $packSize > 0 ? round($unitBonus / $packSize, 2) : 0.0;
                            $bonusUnits = $unitBonus;
                            $matchedSlab = $firstSlab;
                        }
                    } else {
                        if ($totalUnits >= $minQ) {
                            $multiplier = floor($totalUnits / $minQ);
                            $unitBonus = (int) ($multiplier * $bQ);
                            $packBonus = $packSize > 0 ? round($unitBonus / $packSize, 2) : 0.0;
                            $bonusUnits = $unitBonus;
                            $matchedSlab = $firstSlab;
                        }
                    }
                }
            } else {
                $qualifyingQty = $bonusType === 'pack' ? $qualifyingPacks : $totalUnits;
                foreach ($slabs as $slab) {
                    $minQ = (float) $slab->min_qty;
                    $maxQ = $slab->max_qty !== null ? (float) $slab->max_qty : null;

                    if ($qualifyingQty >= $minQ && ($maxQ === null || $qualifyingQty <= $maxQ)) {
                        $awarded = (float) $slab->bonus_qty;
                        $matchedSlab = $slab;
                        if ($bonusType === 'pack') {
                            $packBonus = round($awarded, 2);
                            $unitBonus = (int) round($awarded * $packSize);
                            $bonusUnits = $unitBonus;
                        } else {
                            $unitBonus = (int) round($awarded);
                            $packBonus = $packSize > 0 ? round($awarded / $packSize, 2) : 0.0;
                            $bonusUnits = $unitBonus;
                        }
                        break;
                    }
                }
            }
        }

        if (($packBonus <= 0 && $unitBonus <= 0 && $bonusUnits <= 0) || !$matchedSlab) {
            return $this->noMatch();
        }

        return [
            'matched'          => true,
            'scheme_id'        => $matchedScheme->id,
            'scheme_name'      => $matchedScheme->name,
            'scheme_type'      => $matchedScheme->scheme_type,
            'bonus_type'       => $matchedScheme->bonus_type,
            'pack_bonus'       => $packBonus,
            'unit_bonus'       => $unitBonus,
            'bonus_units'      => $bonusUnits,
            'bonus_product_id' => $matchedSlab->bonus_product_id ?: $productId,
            'message'          => "Scheme '{$matchedScheme->name}' applied",
        ];
    }

    private function noMatch(): array
    {
        return [
            'matched'          => false,
            'scheme_id'        => null,
            'scheme_name'      => null,
            'scheme_type'      => null,
            'bonus_type'       => 'pack',
            'pack_bonus'       => 0,
            'unit_bonus'       => 0,
            'bonus_units'      => 0,
            'bonus_product_id' => null,
            'message'          => null,
        ];
    }
}
