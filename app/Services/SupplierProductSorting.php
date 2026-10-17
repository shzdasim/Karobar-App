<?php

namespace App\Services;

use App\Models\SupplierProductPosition;
use Illuminate\Support\Collection;

/**
 * Remembers the product line order used when a Purchase Order is received
 * into a Purchase Invoice, per supplier, and re-applies it to later
 * Purchase Orders and Purchase Invoices for the same supplier.
 */
class SupplierProductSorting
{
    /** @return int[] Product IDs in remembered order. */
    public function productIds(int $supplierId): array
    {
        return SupplierProductPosition::where('supplier_id', $supplierId)
            ->orderBy('position')->pluck('product_id')->map(fn ($id) => (int) $id)->all();
    }

    /**
     * Merge the submitted order into the remembered order. Products that were
     * not part of this invoice keep their slots; submitted products take the
     * submitted relative order; new products are placed after their submitted
     * predecessor (or before the first submitted product).
     *
     * @param int[] $productIds Product IDs in the order they were saved.
     */
    public function remember(int $supplierId, array $productIds): void
    {
        $submitted = array_values(array_unique(array_map('intval', array_filter($productIds))));
        if (!$submitted) return;

        $existing = $this->productIds($supplierId);
        $known = array_flip($existing);
        $submittedSet = array_flip($submitted);

        // 1) Fill the slots already held by submitted products with them, in submitted order.
        $queue = array_values(array_filter($submitted, fn ($id) => isset($known[$id])));
        $merged = [];
        foreach ($existing as $id) {
            $merged[] = isset($submittedSet[$id]) ? array_shift($queue) : $id;
        }

        // 2) Insert newly seen products right after their submitted predecessor.
        foreach ($submitted as $i => $id) {
            if (isset($known[$id])) continue;
            $previous = $i > 0 ? array_search($submitted[$i - 1], $merged, true) : false;
            if ($previous !== false) {
                array_splice($merged, $previous + 1, 0, [$id]);
                continue;
            }
            $firstSubmitted = null;
            foreach ($merged as $pos => $candidate) {
                if (isset($submittedSet[$candidate])) { $firstSubmitted = $pos; break; }
            }
            array_splice($merged, $firstSubmitted ?? count($merged), 0, [$id]);
        }

        $now = now();
        $rows = array_map(fn ($id, $position) => [
            'supplier_id' => $supplierId, 'product_id' => $id, 'position' => $position,
            'created_at' => $now, 'updated_at' => $now,
        ], $merged, array_keys($merged));
        SupplierProductPosition::upsert($rows, ['supplier_id', 'product_id'], ['position', 'updated_at']);
    }

    /**
     * Stable-sort lines by the remembered order; unknown products keep their
     * current relative order after the remembered ones.
     */
    public function sort(int $supplierId, Collection $lines, callable $productIdOf): Collection
    {
        $rank = array_flip($this->productIds($supplierId));
        if (!$rank) return $lines->values();
        $offset = count($rank);

        return $lines->values()
            ->map(fn ($line, $index) => [$line, $rank[(int) $productIdOf($line)] ?? $offset + $index])
            ->sortBy(fn ($pair) => $pair[1])
            ->map(fn ($pair) => $pair[0])
            ->values();
    }
}
