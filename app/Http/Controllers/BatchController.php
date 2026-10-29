<?php

namespace App\Http\Controllers;

use App\Models\Batch;
use App\Models\Product;
use App\Models\SaleInvoiceItem;
use App\Models\StorageLocation;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use Illuminate\Validation\Rule;
use Illuminate\Validation\ValidationException;

class BatchController extends Controller
{
    /**
     * List batches for a product, sorted using FEFO (First-Expiry, First-Out).
     * Non-expired batches with stock appear first sorted by earliest expiry date,
     * preferring Shop Counter locations over Godown locations when expiry dates match.
     * Also returns X-Unbatched-Units, X-Batched-Units, and X-Total-Units in headers.
     */
    public function index(Request $request, Product $product)
    {
        $user = $request->user();
        if (
            !$user
            || (
                !$user->hasRole(['Admin', 'admin', 'super-admin'])
                && !$user->can('product.view')
                && !$user->can('barcode.view')
                && !$user->can('barcode.print')
                && !$user->can('barcode-pos.view')
                && !$user->can('barcode-pos.create')
                && !$user->can('batch-expiry.view')
                && !$user->can('sale-invoice.create')
                && !$user->can('sale-invoice.update')
                && !$user->can('purchase-invoice.create')
                && !$user->can('purchase-invoice.update')
                && !$user->can('sale-return.create')
                && !$user->can('sale-return.update')
                && !$user->can('purchase-return.create')
                && !$user->can('purchase-return.update')
                && !$user->can('stock-adjustment.create')
                && !$user->can('stock-adjustment.update')
                && !$user->can('stock-disposal.create')
                && !$user->can('stocktake.create')
                && !$user->can('stocktake.update')
            )
        ) {
            abort(403, 'This action is unauthorized.');
        }

        $today = now()->toDateString();
        $hasLocations = Schema::hasTable('storage_locations') && Schema::hasColumn('batches', 'storage_location_id');

        $query = $product->batches();
        if ($hasLocations) {
            $query->with('storageLocation:id,code,name,type,default_rack');
        }

        if ($request->filled('storage_location_id')) {
            $query->where('storage_location_id', (int) $request->query('storage_location_id'));
        }

        $batches = $query
            ->orderByRaw("CASE WHEN expiry_date IS NOT NULL AND expiry_date >= '{$today}' THEN 0 ELSE 1 END")
            ->orderBy('expiry_date', 'asc')
            ->orderBy('id', 'asc')
            ->get();

        // When expiry dates are equal, prefer 'shop' storage locations before 'godown' locations for FEFO
        if ($hasLocations) {
            $batches = $batches->sort(function ($a, $b) use ($today) {
                $aExp = $a->expiry_date ? substr((string) $a->expiry_date, 0, 10) : null;
                $bExp = $b->expiry_date ? substr((string) $b->expiry_date, 0, 10) : null;

                $aValid = ($aExp && $aExp >= $today) ? 0 : 1;
                $bValid = ($bExp && $bExp >= $today) ? 0 : 1;
                if ($aValid !== $bValid) {
                    return $aValid <=> $bValid;
                }

                $expCmp = ($aExp ?? '9999-12-31') <=> ($bExp ?? '9999-12-31');
                if ($expCmp !== 0) {
                    return $expCmp;
                }

                $aLocRank = ($a->storageLocation?->type === 'shop') ? 0 : (($a->storageLocation?->type === 'cold_storage') ? 1 : 2);
                $bLocRank = ($b->storageLocation?->type === 'shop') ? 0 : (($b->storageLocation?->type === 'cold_storage') ? 1 : 2);
                if ($aLocRank !== $bLocRank) {
                    return $aLocRank <=> $bLocRank;
                }

                return ((int) $a->id) <=> ((int) $b->id);
            })->values();
        }

        // Flag the first eligible FEFO batch (non-expired with positive stock)
        $fefoRecommendedFound = false;
        $batches = $batches->map(function ($batch) use (&$fefoRecommendedFound, $product) {
            $isEligibleFefo = !$batch->is_expired && (int)$batch->quantity > 0;
            if ($isEligibleFefo && !$fefoRecommendedFound) {
                $batch->is_fefo_recommended = true;
                $fefoRecommendedFound = true;
            } else {
                $batch->is_fefo_recommended = false;
            }
            if ($batch->unit_sale_price === null && $product->unit_sale_price !== null) {
                $batch->unit_sale_price = (float) $product->unit_sale_price;
            }
            if ($batch->pack_sale_price === null && $product->pack_sale_price !== null) {
                $batch->pack_sale_price = (float) $product->pack_sale_price;
            }
            if ($batch->unit_purchase_price === null && $product->unit_purchase_price !== null) {
                $batch->unit_purchase_price = (float) $product->unit_purchase_price;
            }
            if ($batch->pack_purchase_price === null && $product->pack_purchase_price !== null) {
                $batch->pack_purchase_price = (float) $product->pack_purchase_price;
            }
            if ($batch->whole_sale_unit_price === null && $product->whole_sale_unit_price !== null) {
                $batch->whole_sale_unit_price = (float) $product->whole_sale_unit_price;
            }
            if ($batch->whole_sale_pack_price === null && $product->whole_sale_pack_price !== null) {
                $batch->whole_sale_pack_price = (float) $product->whole_sale_pack_price;
            }
            return $batch;
        });

        $totalProductQty = (int) ($product->quantity ?? 0);
        $totalBatchedQty = (int) $product->batches()->sum('quantity');
        $unbatchedQty = max(0, $totalProductQty - $totalBatchedQty);

        // Include virtual unbatched item in the list only when caller explicitly requests it (e.g. POS sale forms)
        if ($request->boolean('include_unbatched') && $unbatchedQty > 0) {
            $unbatchedItem = new Batch([
                'product_id'            => $product->id,
                'batch_number'          => '',
                'expiry_date'           => null,
                'quantity'              => $unbatchedQty,
                'storage_location_id'   => $product->storage_location_id,
                'rack'                  => $product->rack,
                'unit_sale_price'       => $product->unit_sale_price,
                'pack_sale_price'       => $product->pack_sale_price,
                'unit_purchase_price'   => $product->unit_purchase_price,
                'pack_purchase_price'   => $product->pack_purchase_price,
                'whole_sale_unit_price' => $product->whole_sale_unit_price,
                'whole_sale_pack_price' => $product->whole_sale_pack_price,
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
            'batch_number'          => ['required', 'string', 'max:100'],
            'expiry_date'           => ['required', 'date'],
            'quantity'              => ['nullable', 'integer', 'min:0'],
            'storage_location_id'   => ['nullable', 'integer', 'exists:storage_locations,id'],
            'rack'                  => ['nullable', 'string', 'max:100'],
            'unit_sale_price'       => ['nullable', 'numeric', 'min:0'],
            'pack_sale_price'       => ['nullable', 'numeric', 'min:0'],
            'unit_purchase_price'   => ['nullable', 'numeric', 'min:0'],
            'pack_purchase_price'   => ['nullable', 'numeric', 'min:0'],
            'whole_sale_unit_price' => ['nullable', 'numeric', 'min:0'],
            'whole_sale_pack_price' => ['nullable', 'numeric', 'min:0'],
        ]);

        $batchNumber = trim($validated['batch_number']);
        $expiryDate = substr(trim($validated['expiry_date']), 0, 10);
        $qty = (int)($validated['quantity'] ?? 0);

        return DB::transaction(function () use ($product, $batchNumber, $expiryDate, $qty, $validated) {
            $product = Product::lockForUpdate()->findOrFail($product->id);
            $currentTotal = max(0, (int) ($product->quantity ?? 0));
            $alreadyBatched = (int) $product->batches()->sum('quantity');

            $defaultLocationId = StorageLocation::where('is_default', true)->value('id')
                ?? StorageLocation::orderBy('id')->value('id');
            $locationId = array_key_exists('storage_location_id', $validated) && $validated['storage_location_id'] !== null
                ? (int) $validated['storage_location_id']
                : ($product->storage_location_id ?? $defaultLocationId);
            $rack = array_key_exists('rack', $validated)
                ? (trim((string) $validated['rack']) ?: null)
                : $product->rack;

            /** @var Batch|null $existing */
            $existingQuery = Batch::lockForUpdate()
                ->where('product_id', $product->id)
                ->where('batch_number', $batchNumber);
            if ($locationId !== null) {
                $existingQuery->where('storage_location_id', $locationId);
            }
            $existing = $existingQuery->first();

            $existingBatchQty = $existing ? (int)$existing->quantity : 0;
            $otherBatched = max(0, $alreadyBatched - $existingBatchQty);
            // Available unassigned units that can be allocated to this batch
            $availableUnassigned = max(0, $currentTotal - $otherBatched);

            if (!$existing) {
                if ($availableUnassigned <= 0) {
                    throw ValidationException::withMessages([
                        'quantity' => ["Cannot add a new batch because this product has 0 unassigned stock available (Total on-hand: {$currentTotal}, already batched: {$otherBatched})."],
                    ]);
                }
                if ($qty <= 0) {
                    throw ValidationException::withMessages([
                        'quantity' => ["Batch quantity must be at least 1 unit from unassigned stock (Available unassigned: {$availableUnassigned})."],
                    ]);
                }
            }

            if ($qty > $availableUnassigned) {
                throw ValidationException::withMessages([
                    'quantity' => ["Cannot assign {$qty} units to batch. Only {$availableUnassigned} unassigned units are available for this batch (Total on-hand: {$currentTotal}, other batches: {$otherBatched})."],
                ]);
            }

            $priceFields = [
                'unit_sale_price',
                'pack_sale_price',
                'unit_purchase_price',
                'pack_purchase_price',
                'whole_sale_unit_price',
                'whole_sale_pack_price',
            ];

            if ($existing) {
                $existing->expiry_date = $expiryDate;
                $existing->quantity = $qty;
                $existing->storage_location_id = $locationId;
                $existing->rack = $rack;
                foreach ($priceFields as $f) {
                    if (array_key_exists($f, $validated) && $validated[$f] !== null) {
                        $existing->{$f} = (float) $validated[$f];
                    }
                }
                $existing->save();
                $batch = $existing;
            } else {
                $createPayload = [
                    'product_id'          => $product->id,
                    'batch_number'        => $batchNumber,
                    'expiry_date'         => $expiryDate,
                    'quantity'            => $qty,
                    'storage_location_id' => $locationId,
                    'rack'                => $rack,
                ];
                foreach ($priceFields as $f) {
                    $createPayload[$f] = array_key_exists($f, $validated) && $validated[$f] !== null
                        ? (float) $validated[$f]
                        : $product->{$f};
                }
                $batch = Batch::create($createPayload);
            }

            // Product overall quantity is strictly preserved; batches only allocate from existing unassigned stock.
            return response()->json($batch->fresh(['storageLocation']), 201);
        });
    }

    /**
     * Update an existing batch.
     */
    public function update(Request $request, Batch $batch)
    {
        $this->authorize('update', $batch->product);

        $targetLocationId = $request->has('storage_location_id')
            ? ($request->input('storage_location_id') !== null && $request->input('storage_location_id') !== ''
                ? (int) $request->input('storage_location_id')
                : null)
            : $batch->storage_location_id;

        $validated = $request->validate([
            'batch_number' => [
                'required',
                'string',
                'max:100',
                Rule::unique('batches')
                    ->where('product_id', $batch->product_id)
                    ->where('storage_location_id', $targetLocationId)
                    ->ignore($batch->id),
            ],
            'expiry_date'           => ['required', 'date'],
            'quantity'              => ['required', 'integer', 'min:0'],
            'storage_location_id'   => ['nullable', 'integer', 'exists:storage_locations,id'],
            'rack'                  => ['nullable', 'string', 'max:100'],
            'unit_sale_price'       => ['nullable', 'numeric', 'min:0'],
            'pack_sale_price'       => ['nullable', 'numeric', 'min:0'],
            'unit_purchase_price'   => ['nullable', 'numeric', 'min:0'],
            'pack_purchase_price'   => ['nullable', 'numeric', 'min:0'],
            'whole_sale_unit_price' => ['nullable', 'numeric', 'min:0'],
            'whole_sale_pack_price' => ['nullable', 'numeric', 'min:0'],
        ]);

        return DB::transaction(function () use ($batch, $validated, $targetLocationId) {
            $product = Product::lockForUpdate()->findOrFail($batch->product_id);
            $currentTotal = max(0, (int) ($product->quantity ?? 0));
            $otherBatched = (int) $product->batches()->where('id', '!=', $batch->id)->sum('quantity');
            $newQty = (int) $validated['quantity'];
            $availableUnassigned = max(0, $currentTotal - $otherBatched);

            if ($newQty > $availableUnassigned) {
                $unbatchedPool = max(0, $currentTotal - ($otherBatched + (int) $batch->quantity));
                throw ValidationException::withMessages([
                    'quantity' => ["Cannot assign {$newQty} units to batch. Maximum assignable quantity is {$availableUnassigned} units (Current batch: {$batch->quantity}, unassigned stock: {$unbatchedPool})."],
                ]);
            }

            $batch->batch_number = trim($validated['batch_number']);
            $batch->expiry_date  = substr(trim($validated['expiry_date']), 0, 10);
            $batch->quantity     = $newQty;
            $batch->storage_location_id = $targetLocationId;
            if (array_key_exists('rack', $validated)) {
                $batch->rack = trim((string) $validated['rack']) ?: null;
            }
            foreach ([
                'unit_sale_price',
                'pack_sale_price',
                'unit_purchase_price',
                'pack_purchase_price',
                'whole_sale_unit_price',
                'whole_sale_pack_price',
            ] as $f) {
                if (array_key_exists($f, $validated) && $validated[$f] !== null) {
                    $batch->{$f} = (float) $validated[$f];
                }
            }
            $batch->save();

            // Product overall quantity is strictly preserved; batches only allocate from existing unassigned stock.
            return response()->json($batch->fresh(['storageLocation']), 200);
        });
    }

    /**
     * Move or split batch units from its current storage location to another storage location
     * (e.g., move 50 units from Main Godown to Shop Counter).
     * Preserves product total stock and copies all batch pricing & expiry fields.
     */
    public function moveLocation(Request $request, Batch $batch)
    {
        $user = $request->user();
        if (
            !$user
            || (
                !$user->hasRole(['Admin', 'admin', 'super-admin'])
                && !$user->can('product.update')
                && !$user->can('batch-expiry.update')
                && !$user->can('stock-adjustment.create')
            )
        ) {
            abort(403, 'This action is unauthorized.');
        }

        $validated = $request->validate([
            'to_storage_location_id' => ['required', 'integer', 'exists:storage_locations,id'],
            'quantity'               => ['required', 'integer', 'min:1'],
            'rack'                   => ['nullable', 'string', 'max:100'],
        ]);

        return DB::transaction(function () use ($batch, $validated) {
            $sourceBatch = Batch::lockForUpdate()->findOrFail($batch->id);
            $sourceQty = (int) ($sourceBatch->quantity ?? 0);
            $moveQty = (int) $validated['quantity'];
            $toLocationId = (int) $validated['to_storage_location_id'];
            $toLocation = StorageLocation::findOrFail($toLocationId);
            $targetRack = array_key_exists('rack', $validated) && trim((string) $validated['rack']) !== ''
                ? trim((string) $validated['rack'])
                : ($toLocation->default_rack ?? $sourceBatch->rack);

            if ($moveQty > $sourceQty) {
                throw ValidationException::withMessages([
                    'quantity' => ["Cannot move {$moveQty} units. Only {$sourceQty} units are available in this batch location."],
                ]);
            }

            // If moving to the same location, just update rack
            if ((int) $sourceBatch->storage_location_id === $toLocationId) {
                $sourceBatch->rack = $targetRack;
                $sourceBatch->save();

                return response()->json([
                    'message'      => 'Batch rack/location updated.',
                    'source_batch' => $sourceBatch->fresh(['storageLocation']),
                    'target_batch' => $sourceBatch->fresh(['storageLocation']),
                ]);
            }

            // Check if a batch row with the same batch_number already exists at the destination location
            $targetBatch = Batch::lockForUpdate()
                ->where('product_id', $sourceBatch->product_id)
                ->where('batch_number', $sourceBatch->batch_number)
                ->where('storage_location_id', $toLocationId)
                ->where('id', '!=', $sourceBatch->id)
                ->first();

            if ($targetBatch) {
                $targetBatch->quantity = (int) $targetBatch->quantity + $moveQty;
                if ($targetRack !== null) {
                    $targetBatch->rack = $targetRack;
                }
                $targetBatch->save();

                $sourceBatch->quantity = $sourceQty - $moveQty;
                if ($sourceBatch->quantity <= 0) {
                    $sourceBatch->delete();
                    $sourceBatchResult = null;
                } else {
                    $sourceBatch->save();
                    $sourceBatchResult = $sourceBatch->fresh(['storageLocation']);
                }

                return response()->json([
                    'message'      => "Moved {$moveQty} units to {$toLocation->name}.",
                    'source_batch' => $sourceBatchResult,
                    'target_batch' => $targetBatch->fresh(['storageLocation']),
                ]);
            }

            // Destination location does not yet have a row for this batch
            if ($moveQty === $sourceQty) {
                // Full move: simply reassign location & rack on existing row
                $sourceBatch->storage_location_id = $toLocationId;
                $sourceBatch->rack = $targetRack;
                $sourceBatch->save();

                return response()->json([
                    'message'      => "Moved all {$moveQty} units to {$toLocation->name}.",
                    'source_batch' => null,
                    'target_batch' => $sourceBatch->fresh(['storageLocation']),
                ]);
            }

            // Partial move: split into source and new destination batch row
            $sourceBatch->quantity = $sourceQty - $moveQty;
            $sourceBatch->save();

            $newTargetBatch = Batch::create([
                'product_id'            => $sourceBatch->product_id,
                'batch_number'          => $sourceBatch->batch_number,
                'expiry_date'           => $sourceBatch->expiry_date,
                'quantity'              => $moveQty,
                'storage_location_id'   => $toLocationId,
                'rack'                  => $targetRack,
                'pack_purchase_price'   => $sourceBatch->pack_purchase_price,
                'unit_purchase_price'   => $sourceBatch->unit_purchase_price,
                'pack_sale_price'       => $sourceBatch->pack_sale_price,
                'unit_sale_price'       => $sourceBatch->unit_sale_price,
                'whole_sale_pack_price' => $sourceBatch->whole_sale_pack_price,
                'whole_sale_unit_price' => $sourceBatch->whole_sale_unit_price,
            ]);

            return response()->json([
                'message'      => "Split and moved {$moveQty} units to {$toLocation->name}.",
                'source_batch' => $sourceBatch->fresh(['storageLocation']),
                'target_batch' => $newTargetBatch->fresh(['storageLocation']),
            ]);
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
