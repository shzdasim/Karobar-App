<?php

namespace App\Services;

use App\Models\Batch;
use App\Models\Product;
use App\Models\User;
use Illuminate\Support\Collection;
use Illuminate\Validation\ValidationException;

class SaleStockAvailability
{
    /**
     * Check if a user has permission to override expired stock locks.
     */
    public function canOverrideExpired(?User $user): bool
    {
        if (!$user) {
            return false;
        }

        if (method_exists($user, 'hasRole')) {
            if ($user->hasRole(['Admin', 'admin', 'super-admin'])) {
                return true;
            }
        }

        if (method_exists($user, 'hasPermissionTo')) {
            try {
                if ($user->hasPermissionTo('sale-invoice.expired-override', 'sanctum') ||
                    $user->hasPermissionTo('sale-invoice.expired-override')) {
                    return true;
                }
            } catch (\Throwable $e) {
                // fall through
            }
        }

        if (method_exists($user, 'can') && $user->can('sale-invoice.expired-override')) {
            return true;
        }

        return false;
    }

    // Called inside the sale transaction. Lock products and batches in ID order.
    public function validate(
        array $items,
        ?Collection $originalItems = null,
        ?User $user = null,
        bool $expiredOverride = false,
        array $overrideCredentials = []
    ): void {
        if ($expiredOverride && !empty($overrideCredentials)) {
            $user = app(InvoiceCalculationService::class)->resolveAuthorizingUser($user, $overrideCredentials, 'sale-invoice.expired-override');
        }
        $originalItems ??= collect();
        $ids = collect($items)->pluck('product_id')->merge($originalItems->pluck('product_id'))->unique()->sort()->values();
        $products = Product::whereIn('id', $ids)->orderBy('id')->lockForUpdate()->get()->keyBy('id');
        $batches = Batch::whereIn('product_id', $ids)->orderBy('id')->lockForUpdate()->get();
        $requested = [];
        $batchRequested = [];
        $unbatchedRequested = [];
        $restored = $originalItems->groupBy('product_id')->map(fn ($rows) => $rows->sum('quantity'));
        $batchRestored = [];
        $unbatchedRestored = [];

        foreach ($originalItems as $old) {
            $batch = $this->matchBatch($batches, $old->toArray());
            if ($batch) {
                $batchRestored[$batch->id] = ($batchRestored[$batch->id] ?? 0) + (int) $old->quantity;
            } else {
                $unbatchedRestored[$old->product_id] = ($unbatchedRestored[$old->product_id] ?? 0) + (int) $old->quantity;
            }
        }

        $seenAllocations = [];

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

            $batchNo = trim((string) ($row['batch_number'] ?? $row['batch'] ?? ''));
            $allocationKey = ($batchNo !== '' && $batchNo !== '__unbatched__')
                ? "batch:{$batchNo}"
                : "unbatched";

            if (isset($seenAllocations[$product->id][$allocationKey])) {
                if ($allocationKey === 'unbatched') {
                    throw ValidationException::withMessages([
                        "items.$index.quantity" => "Unassigned stock for {$product->name} can only be added once per invoice.",
                    ]);
                } else {
                    throw ValidationException::withMessages([
                        "items.$index.batch_number" => "Duplicate batch '{$batchNo}' for {$product->name}. Each batch can only be selected once per invoice.",
                    ]);
                }
            }
            $seenAllocations[$product->id][$allocationKey] = true;

            if ($batchNo !== '' && $batchNo !== '__unbatched__') {
                $batch = $this->matchBatch($batches, $row);
                if (! $batch) {
                    throw ValidationException::withMessages(["items.$index.batch_number" => "The selected batch for {$product->name} is unavailable or ambiguous. Select a valid batch and expiry."]);
                }

                // 🛑 FEFO Expired Stock Selling Lock
                if ($batch->is_expired) {
                    if (!$expiredOverride) {
                        throw new \Illuminate\Http\Exceptions\HttpResponseException(response()->json([
                            'message' => "Batch '{$batch->batch_number}' for '{$product->name}' expired on {$batch->expiry_date} and cannot be sold without supervisor override.",
                            'requires_expired_override' => true,
                            'product_id' => $product->id,
                            'product_name' => $product->name,
                            'batch_number' => $batch->batch_number,
                            'expiry_date' => (string) $batch->expiry_date,
                            'errors' => [
                                "items.{$index}.batch_number" => ["Batch '{$batch->batch_number}' for '{$product->name}' expired on {$batch->expiry_date} and cannot be sold without supervisor override."],
                            ],
                        ], 422));
                    }

                    if (!$this->canOverrideExpired($user)) {
                        throw new \Illuminate\Http\Exceptions\HttpResponseException(response()->json([
                            'message' => 'You do not have permission to sell expired stock.',
                            'code' => 'EXPIRED_STOCK_UNAUTHORIZED',
                        ], 403));
                    }
                }

                $batchRequested[$batch->id] = ($batchRequested[$batch->id] ?? 0) + (int) $row['quantity'];
                $availableBatchQty = (int) $batch->quantity + ($batchRestored[$batch->id] ?? 0);
                if ($batchRequested[$batch->id] > $availableBatchQty) {
                    throw ValidationException::withMessages(["items.$index.quantity" => "Insufficient stock in batch {$batch->batch_number} for {$product->name}. Available: $availableBatchQty units; requested: {$batchRequested[$batch->id]} units."]);
                }
            } else {
                // Selling unbatched stock
                $productBatches = $batches->where('product_id', $product->id);
                if ($productBatches->isNotEmpty()) {
                    $totalBatched = (int) $productBatches->sum('quantity');
                    $unbatchedAvailable = max(0, ((int) $product->quantity + (int) ($unbatchedRestored[$product->id] ?? 0)) - $totalBatched);

                    $unbatchedRequested[$product->id] = ($unbatchedRequested[$product->id] ?? 0) + (int) $row['quantity'];

                    if ($unbatchedRequested[$product->id] > $unbatchedAvailable) {
                        // If unbatched stock is 0 and all batches are expired, trigger expired stock lock
                        $allBatchesExpired = $productBatches->every(fn ($b) => $b->is_expired);
                        if ($unbatchedAvailable === 0 && $allBatchesExpired) {
                            $earliestExpired = $productBatches->sortBy('expiry_date')->first();
                            $expiryStr = $earliestExpired ? $earliestExpired->expiry_date : 'already';

                            if (!$expiredOverride) {
                                throw new \Illuminate\Http\Exceptions\HttpResponseException(response()->json([
                                    'message' => "Stock for '{$product->name}' has expired ({$expiryStr}) and cannot be sold without supervisor override.",
                                    'requires_expired_override' => true,
                                    'product_id' => $product->id,
                                    'product_name' => $product->name,
                                    'expiry_date' => (string) $expiryStr,
                                    'errors' => [
                                        "items.{$index}.quantity" => ["Stock for '{$product->name}' has expired ({$expiryStr}) and cannot be sold without supervisor override."],
                                    ],
                                ], 422));
                            }

                            if (!$this->canOverrideExpired($user)) {
                                throw new \Illuminate\Http\Exceptions\HttpResponseException(response()->json([
                                    'message' => 'You do not have permission to sell expired stock.',
                                    'code' => 'EXPIRED_STOCK_UNAUTHORIZED',
                                ], 403));
                            }
                        }

                        throw ValidationException::withMessages([
                            "items.$index.quantity" => "Insufficient unbatched stock for {$product->name}. Available unbatched: {$unbatchedAvailable} units ({$totalBatched} units are assigned to batches). Please select a batch.",
                        ]);
                    }
                }
            }
        }
    }

    public function matchBatch(Collection $batches, array $row): ?Batch
    {
        $number = trim((string) ($row['batch_number'] ?? $row['batch'] ?? ''));
        if ($number === '' || $number === '__unbatched__') {
            return null;
        }
        $expiry = substr(trim((string) ($row['expiry'] ?? $row['expiry_date'] ?? '')), 0, 10);
        $matches = $batches->filter(fn ($batch) => (int) $batch->product_id === (int) $row['product_id']
            && trim((string) $batch->batch_number) === $number
            && ($expiry === '' || substr((string) $batch->expiry_date, 0, 10) === $expiry));

        return $matches->count() === 1 ? $matches->first() : null;
    }
}
