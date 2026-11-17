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
                'slabs.bonusProduct:id,name,product_code,pack_size,unit_sale_price,pack_sale_price,whole_sale_unit_price,whole_sale_pack_price,quantity',
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
        $rawQty = max(0, (int) ($item['quantity'] ?? 0));

        // Get product details to check brand, category, and pack_size targets
        $brandId = $item['brand_id'] ?? null;
        $categoryId = $item['category_id'] ?? null;

        if ($brandId === null || $categoryId === null || !isset($item['pack_size'])) {
            $prod = Product::select('id', 'brand_id', 'category_id', 'pack_size')->find($productId);
            if ($prod) {
                $brandId = $brandId ?? $prod->brand_id;
                $categoryId = $categoryId ?? $prod->category_id;
                if (!isset($item['pack_size']) && $prod->pack_size) {
                    $packSize = max(1, (int) $prod->pack_size);
                }
            }
        }

        // Determine exact totalUnits and purchasedPacks without double-counting
        // (In Wholesale mode, pack_quantity and unit_quantity are two-way synced representations of the same quantity)
        if ($rawQty > 0) {
            $totalUnits = $rawQty;
        } elseif ($unitQty > 0 && ($packQty <= 0 || (int) round($packQty * $packSize) === $unitQty)) {
            $totalUnits = $unitQty;
        } elseif ($packQty > 0 && $unitQty <= 0) {
            $totalUnits = (int) round($packQty * $packSize);
        } else {
            $totalUnits = (int) round($packQty * $packSize) + $unitQty;
        }

        $purchasedPacks = ($packQty > 0 && (int) round($packQty * $packSize) === $totalUnits)
            ? $packQty
            : ($packSize > 0 ? ($totalUnits / $packSize) : 0.0);

        if ($totalUnits <= 0 && $packQty <= 0) {
            return $this->noMatch();
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

        if ($matchedScheme->scheme_type === 'ratio') {
            $firstSlab = $slabs->first();
            $minQ = (float) ($firstSlab->min_qty ?? 0);
            $bQ = (float) ($firstSlab->bonus_qty ?? 0);
            $bonusProdPackSize = max(1, (int) ($firstSlab->bonusProduct->pack_size ?? $packSize));

            if ($minQ > 0 && $bQ > 0) {
                if ($bonusType === 'pack') {
                    // Pack-based ratio scheme (e.g., Buy 2 Packs, Get 1 Pack Free)
                    if ($purchasedPacks + 1e-9 >= $minQ) {
                        $multiplier = floor(($purchasedPacks + 1e-9) / $minQ);
                        if ($multiplier > 0) {
                            $packBonus = round($multiplier * $bQ, 2);
                            $unitBonus = (int) round($packBonus * $bonusProdPackSize);
                            $bonusUnits = $unitBonus;
                            $matchedSlab = $firstSlab;
                        }
                    }
                } else {
                    // Unit-based ratio scheme (e.g., Buy 2 Units, Get 1 Unit Free)
                    if ($totalUnits >= $minQ) {
                        $multiplier = floor($totalUnits / $minQ);
                        if ($multiplier > 0) {
                            $unitBonus = (int) round($multiplier * $bQ);
                            $packBonus = $bonusProdPackSize > 0 ? round($unitBonus / $bonusProdPackSize, 2) : 0.0;
                            $bonusUnits = $unitBonus;
                            $matchedSlab = $firstSlab;
                        }
                    }
                }
            }
        } else {
            // Tiered Slabs (in Packs when bonusType === 'pack', or in Units when bonusType === 'unit')
            $wholePurchasedPacks = floor($purchasedPacks + 1e-9);

            foreach ($slabs as $slab) {
                $minQ = (float) $slab->min_qty;
                $maxQ = $slab->max_qty !== null ? (float) $slab->max_qty : null;
                $awarded = (float) $slab->bonus_qty;
                $bonusProdPackSize = max(1, (int) ($slab->bonusProduct->pack_size ?? $packSize));

                if ($bonusType === 'pack') {
                    if ($purchasedPacks + 1e-9 >= $minQ && ($maxQ === null || $wholePurchasedPacks <= $maxQ)) {
                        $packBonus = round($awarded, 2);
                        $unitBonus = (int) round($packBonus * $bonusProdPackSize);
                        $bonusUnits = $unitBonus;
                        $matchedSlab = $slab;
                        break;
                    }
                } else {
                    if ($totalUnits >= $minQ && ($maxQ === null || $totalUnits <= $maxQ)) {
                        $unitBonus = (int) round($awarded);
                        $packBonus = $bonusProdPackSize > 0 ? round($unitBonus / $bonusProdPackSize, 2) : 0.0;
                        $bonusUnits = $unitBonus;
                        $matchedSlab = $slab;
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
