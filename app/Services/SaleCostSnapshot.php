<?php

namespace App\Services;

use App\Models\Product;
use Illuminate\Support\Collection;

class SaleCostSnapshot
{
    public function productCosts(array $rows): Collection
    {
        return Product::whereIn('id', array_column($rows, 'product_id'))->get()->mapWithKeys(function ($product) {
            // Same cost selection as the existing reports; no costing formula changes.
            $cost = $product->avg_price ?? 0;
            if ($cost == 0) {
                $cost = $product->unit_purchase_price ?? 0;
            }

            return [$product->id => $cost];
        });
    }

    public function matchingRows(Collection $rows, array $row): Collection
    {
        return $rows->filter(fn ($old) => (int) $old->product_id === (int) $row['product_id']
            && trim((string) $old->batch_number) === trim((string) ($row['batch_number'] ?? $row['batch'] ?? ''))
            && substr((string) $old->expiry, 0, 10) === substr((string) ($row['expiry'] ?? $row['expiry_date'] ?? ''), 0, 10));
    }

    public function originalCost(Collection $rows, array $row): mixed
    {
        // Prefer the server-owned line ID; never accept a client-provided cost.
        $matched = $rows->first(fn ($old) => (string) $old->id === (string) ($row['id'] ?? '')
            && (int) $old->product_id === (int) $row['product_id']);
        if ($matched) {
            return $matched->cost_price_snapshot;
        }
        $matches = $this->matchingRows($rows, $row);

        return $this->unambiguousCost($matches);
    }

    public function unambiguousCost(Collection $rows): mixed
    {
        // Ambiguous or legacy costs remain estimated rather than inventing history.
        if ($rows->isEmpty() || $rows->contains(fn ($row) => $row->cost_price_snapshot === null)) {
            return null;
        }
        $costs = $rows->pluck('cost_price_snapshot')->map(fn ($cost) => (string) $cost)->unique();

        return $costs->count() === 1 ? $costs->first() : null;
    }
}
