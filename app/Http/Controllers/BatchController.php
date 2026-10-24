<?php

namespace App\Http\Controllers;

use App\Models\Batch;
use App\Models\Product;
use App\Models\SaleInvoiceItem;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\Rule;
use Illuminate\Validation\ValidationException;

class BatchController extends Controller
{
    /**
     * List batches for a product, sorted using FEFO (First-Expiry, First-Out).
     * Non-expired batches with stock appear first sorted by earliest expiry date.
     * Also returns X-Unbatched-Units, X-Batched-Units, and X-Total-Units in headers.
     */
    public function index(Request $request, Product $product)
    {
        $today = now()->toDateString();

        $batches = $product->batches()
            ->orderByRaw("CASE WHEN expiry_date IS NOT NULL AND expiry_date >= '{$today}' THEN 0 ELSE 1 END")
            ->orderBy('expiry_date', 'asc')
            ->orderBy('id', 'asc')
            ->get();

        // Flag the first eligible FEFO batch (non-expired with positive stock)
        $fefoRecommendedFound = false;
        $batches = $batches->map(function ($batch) use (&$fefoRecommendedFound) {
            $isEligibleFefo = !$batch->is_expired && (int)$batch->quantity > 0;
            if ($isEligibleFefo && !$fefoRecommendedFound) {
                $batch->is_fefo_recommended = true;
                $fefoRecommendedFound = true;
            } else {
                $batch->is_fefo_recommended = false;
            }
            return $batch;
        });

        $totalProductQty = (int) ($product->quantity ?? 0);
        $totalBatchedQty = (int) $batches->sum('quantity');
        $unbatchedQty = max(0, $totalProductQty - $totalBatchedQty);

        // Include virtual unbatched item in the list only when caller explicitly requests it (e.g. POS sale forms)
        if ($request->boolean('include_unbatched') && $unbatchedQty > 0) {
            $unbatchedItem = new Batch([
                'product_id'   => $product->id,
                'batch_number' => '',
                'expiry_date'  => null,
                'quantity'     => $unbatchedQty,
            ]);
            $unbatchedItem->id = null;
            $unbatchedItem->is_unbatched = true;
            $unbatchedItem->display_label = 'Unbatched Stock';
            $unbatchedItem->is_fefo_recommended = false;
            $batches->push($unbatchedItem);
        }

        return response()->json($batches)
            ->header('X-Total-Units', (string) $totalProductQty)
            ->header('X-Batched-Units', (string) $totalBatchedQty)
            ->header('X-Unbatched-Units', (string) $unbatchedQty);
    }

    /**
     * Store or add a batch for a product.
     * Takes the specified quantity from available unassigned quantity of the product.
     */
    public function store(Request $request, Product $product)
    {
        $this->authorize('update', $product);

        $validated = $request->validate([
            'batch_number' => ['required', 'string', 'max:100'],
            'expiry_date'  => ['required', 'date'],
            'quantity'     => ['nullable', 'integer', 'min:0'],
        ]);

        $batchNumber = trim($validated['batch_number']);
        $expiryDate = substr(trim($validated['expiry_date']), 0, 10);
        $qty = (int)($validated['quantity'] ?? 0);

        return DB::transaction(function () use ($product, $batchNumber, $expiryDate, $qty) {
            $product = Product::lockForUpdate()->findOrFail($product->id);
            $currentTotal = (int) ($product->quantity ?? 0);
            $alreadyBatched = (int) $product->batches()->sum('quantity');

            /** @var Batch|null $existing */
            $existing = Batch::lockForUpdate()
                ->where('product_id', $product->id)
                ->where('batch_number', $batchNumber)
                ->first();

            $existingBatchQty = $existing ? (int)$existing->quantity : 0;
            // Available unassigned units that have no batch assigned
            $availableUnassigned = max(0, $currentTotal - ($alreadyBatched - $existingBatchQty));

            // If product has on-hand stock, the batch quantity takes from unassigned stock
            if ($currentTotal > 0 && $qty > $availableUnassigned) {
                throw ValidationException::withMessages([
                    'quantity' => ["Cannot assign {$qty} units to batch. Only {$availableUnassigned} unassigned units are available for this product (Total on-hand: {$currentTotal}, already batched: " . ($alreadyBatched - $existingBatchQty) . ")."],
                ]);
            }

            if ($existing) {
                $existing->expiry_date = $expiryDate;
                $existing->quantity = $qty;
                $existing->save();
                $batch = $existing;
            } else {
                $batch = Batch::create([
                    'product_id'   => $product->id,
                    'batch_number' => $batchNumber,
                    'expiry_date'  => $expiryDate,
                    'quantity'     => $qty,
                ]);
            }

            // The product overall quantity is kept intact, taking batch quantity from unassigned stock.
            // If product had 0 stock, product quantity expands to cover the new batch.
            $newBatchedSum = (int) $product->batches()->sum('quantity');
            if ($currentTotal < $newBatchedSum) {
                $product->quantity = $newBatchedSum;
                $product->save();
            }

            return response()->json($batch->fresh(), 201);
        });
    }

    /**
     * Update an existing batch.
     */
    public function update(Request $request, Batch $batch)
    {
        $this->authorize('update', $batch->product);

        $validated = $request->validate([
            'batch_number' => [
                'required',
                'string',
                'max:100',
                Rule::unique('batches')->where('product_id', $batch->product_id)->ignore($batch->id),
            ],
            'expiry_date'  => ['required', 'date'],
            'quantity'     => ['required', 'integer', 'min:0'],
        ]);

        return DB::transaction(function () use ($batch, $validated) {
            $product = Product::lockForUpdate()->findOrFail($batch->product_id);
            $currentTotal = (int) ($product->quantity ?? 0);
            $otherBatched = (int) $product->batches()->where('id', '!=', $batch->id)->sum('quantity');
            $newQty = (int) $validated['quantity'];
            $availableUnassigned = max(0, $currentTotal - $otherBatched);

            if ($currentTotal > 0 && $newQty > $availableUnassigned) {
                throw ValidationException::withMessages([
                    'quantity' => ["Cannot assign {$newQty} units to batch. Only {$availableUnassigned} unassigned units are available for this product."],
                ]);
            }

            $batch->batch_number = trim($validated['batch_number']);
            $batch->expiry_date  = substr(trim($validated['expiry_date']), 0, 10);
            $batch->quantity     = $newQty;
            $batch->save();

            $newBatchedSum = (int) $product->batches()->sum('quantity');
            if ($currentTotal < $newBatchedSum) {
                $product->quantity = $newBatchedSum;
                $product->save();
            }

            return response()->json($batch->fresh(), 200);
        });
    }

    /**
     * Delete a batch.
     * Deleting releases the units back into the product's unassigned stock pool.
     */
    public function destroy(Batch $batch)
    {
        $this->authorize('update', $batch->product);

        $hasSales = SaleInvoiceItem::where('product_id', $batch->product_id)
            ->where('batch_number', $batch->batch_number)
            ->exists();

        if ($hasSales) {
            throw ValidationException::withMessages([
                'batch' => ["Cannot delete batch '{$batch->batch_number}' because sale transactions are recorded for it. Adjust its quantity to 0 instead."],
            ]);
        }

        return DB::transaction(function () use ($batch) {
            $batch->delete();

            // Deleting a batch releases its units back into the unassigned stock pool.
            // Product total quantity is preserved!
            return response()->json(['message' => 'Batch deleted successfully']);
        });
    }
}
