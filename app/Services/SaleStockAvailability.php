<?php

namespace App\Services;

use App\Models\Batch;
use App\Models\Product;
use Illuminate\Support\Collection;
use Illuminate\Validation\ValidationException;

class SaleStockAvailability
{
    // Called inside the sale transaction. Lock products and batches in ID order.
    public function validate(array $items, ?Collection $originalItems = null): void
    {
        $originalItems ??= collect();
        $ids = collect($items)->pluck('product_id')->merge($originalItems->pluck('product_id'))->unique()->sort()->values();
        $products = Product::whereIn('id', $ids)->orderBy('id')->lockForUpdate()->get()->keyBy('id');
        $batches = Batch::whereIn('product_id', $ids)->orderBy('id')->lockForUpdate()->get();
        $requested = [];
        $batchRequested = [];
        $restored = $originalItems->groupBy('product_id')->map(fn ($rows) => $rows->sum('quantity'));
        $batchRestored = [];
        foreach ($originalItems as $old) {
            $batch = $this->matchBatch($batches, $old->toArray());
            if ($batch) {
                $batchRestored[$batch->id] = ($batchRestored[$batch->id] ?? 0) + (int) $old->quantity;
            }
        }
        foreach ($items as $index => $row) {
            $product = $products->get($row['product_id']);
            if (! $product) {
                throw ValidationException::withMessages(["items.$index.product_id" => 'This product is no longer available.']);
            }
            $requested[$product->id] = ($requested[$product->id] ?? 0) + (int) $row['quantity'];
            $available = (int) $product->quantity + (int) ($restored[$product->id] ?? 0);
            if ($requested[$product->id] > $available) {
                throw ValidationException::withMessages(["items.$index.quantity" => "Insufficient stock for {$product->name}. Available: $available units; requested: {$requested[$product->id]} units."]);
            }
            if (trim((string) ($row['batch_number'] ?? $row['batch'] ?? '')) !== '') {
                $batch = $this->matchBatch($batches, $row);
                if (! $batch) {
                    throw ValidationException::withMessages(["items.$index.batch_number" => "The selected batch for {$product->name} is unavailable or ambiguous. Select a valid batch and expiry."]);
                }
                $batchRequested[$batch->id] = ($batchRequested[$batch->id] ?? 0) + (int) $row['quantity'];
                $available = (int) $batch->quantity + ($batchRestored[$batch->id] ?? 0);
                if ($batchRequested[$batch->id] > $available) {
                    throw ValidationException::withMessages(["items.$index.quantity" => "Insufficient stock in batch {$batch->batch_number} for {$product->name}. Available: $available units; requested: {$batchRequested[$batch->id]} units."]);
                }
            }
        }
    }

    public function matchBatch(Collection $batches, array $row): ?Batch
    {
        $number = trim((string) ($row['batch_number'] ?? $row['batch'] ?? ''));
        if ($number === '') {
            return null;
        }
        $expiry = substr(trim((string) ($row['expiry'] ?? $row['expiry_date'] ?? '')), 0, 10);
        $matches = $batches->filter(fn ($batch) => (int) $batch->product_id === (int) $row['product_id']
            && trim((string) $batch->batch_number) === $number
            && ($expiry === '' || substr((string) $batch->expiry_date, 0, 10) === $expiry));

        return $matches->count() === 1 ? $matches->first() : null;
    }
}
