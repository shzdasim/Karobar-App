<?php

namespace App\Http\Controllers;

use App\Models\Product;
use App\Models\StockAdjustment;
use App\Models\StockAdjustmentItem;
use App\Models\Batch;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\Rule;

class StockAdjustmentController extends Controller
{
    // ===== Helpers to read/set product stock column consistently =====
    protected function getProductQty(Product $p): float
    {
        // TODO: align with your schema
        return (float)($p->available_units ?? $p->quantity_on_hand ?? $p->quantity ?? 0);
    }

    protected function setProductQty(Product $p, float $qty): void
    {
        // TODO: align with your schema
        if ($p->offsetExists('available_units')) $p->available_units = $qty;
        elseif ($p->offsetExists('quantity_on_hand')) $p->quantity_on_hand = $qty;
        else $p->quantity = $qty;
        
        // If stock becomes zero, reset average price and margins
        // This ensures moving average resets when all stock is removed
        if ($qty <= 0) {
            $p->avg_price = 0;
            $p->margin = 0;
            $p->whole_sale_margin = 0;
        }
        
        $p->save();
    }

    protected function addProductQty(Product $p, float $delta): void
    {
        $current = $this->getProductQty($p);
        $this->setProductQty($p, $current + $delta);
    }

    protected function sumBatches(Product $p): float
    {
        // Sum all batches for product to keep product-level stock in sync
        return (float) Batch::where('product_id', $p->id)->sum(DB::raw('COALESCE(quantity, 0)'));
    }

    protected function addBatchQty(Batch $batch, float $delta): void
    {
        $batch->quantity = (float)($batch->quantity ?? 0) + $delta;
        $batch->save();
    }

    // ===== Endpoints =====

    public function index(Request $request)
    {
        // 🔒 list
        $this->authorize('viewAny', StockAdjustment::class);

        $q = trim((string)$request->get('q'));
        $per = (int)($request->get('per_page', 50));

        $query = StockAdjustment::query()
            ->withCount('items')
            ->when($q !== '', fn($qq) =>
                $qq->where('posted_number','like',"%{$q}%")
                   ->orWhere('note','like',"%{$q}%")
            )
            ->orderByDesc('id');

        return $query->paginate($per);
    }

    public function show($id)
    {
        $adj = StockAdjustment::with(['items.product'])->findOrFail($id);
        // 🔒 view instance
        $this->authorize('view', $adj);
        return $adj;
    }

    public function newCode()
    {
        // 🔒 require create (same pattern as SaleInvoice::generateNewCode)
        $this->authorize('create', StockAdjustment::class);

        $prefix = 'STADJ-';
        $last = StockAdjustment::where('posted_number','like',$prefix.'%')
            ->orderByDesc('id')
            ->value('posted_number');

        $next = 1;
        if ($last && preg_match('/^'.preg_quote($prefix,'/').'([0-9]+)$/',$last,$m)) {
            $next = ((int)$m[1]) + 1;
        }
        return response()->json(['posted_number' => sprintf($prefix.'%05d', $next)]);
    }

    public function store(Request $request)
    {
        // 🔒 create
        $this->authorize('create', StockAdjustment::class);

        $data = $this->validatePayload($request);

        return DB::transaction(function () use ($data, $request) {
            $adj = StockAdjustment::create([
                'posted_number' => $data['posted_number'],
                'posted_date'   => $data['posted_date'],
                'note'          => $data['note'],
                'total_worth'   => 0,
                'user_id'       => optional($request->user())->id,
            ]);

            $totalWorth = 0.0;
            $touchedProducts = [];

            foreach ($data['items'] as $row) {
                /** @var Product $product */
                $product = Product::query()->lockForUpdate()->findOrFail($row['product_id']);

                $useBatch = isset($row['batch_number']) && $row['batch_number'] !== null && $row['batch_number'] !== '';

                if ($useBatch) {
                    /** @var Batch $batch */
                    $batch = Batch::query()->lockForUpdate()
                        ->where('product_id', $product->id)
                        ->where('batch_number', $row['batch_number'])
                        ->first();

                    if (!$batch) {
                        // create batch if adjusting a new batch code
                        $batch = new Batch();
                        $batch->product_id = $product->id;
                        $batch->batch_number = $row['batch_number'];
                        $batch->expiry_date = $row['expiry'] ?? null;
                        $batch->quantity = 0;
                    }

                    $previous = (float)($batch->quantity ?? 0);
                    $actual   = (float)$row['actual_qty'];
                    $diff     = $actual - $previous;

                    $unitCost = (float)($row['unit_purchase_price'] ?? $product->unit_purchase_price ?? 0);
                    $worth    = abs($diff) * $unitCost;

                    StockAdjustmentItem::create([
                        'stock_adjustment_id' => $adj->id,
                        'product_id'          => $product->id,
                        'batch_number'        => $row['batch_number'],
                        'expiry'              => $row['expiry'] ?? null,
                        'pack_size'           => $row['pack_size'] ?? null,
                        'previous_qty'        => $previous,
                        'actual_qty'          => $actual,
                        'diff_qty'            => $diff,
                        'unit_purchase_price' => $unitCost,
                        'worth_adjusted'      => $worth,
                    ]);

                    $totalWorth += $worth;

                    // Apply to batch
                    $batch->quantity = $actual;
                    $batch->save();

                } else {
                    // Product-level adjustment
                    $previous = $this->getProductQty($product);
                    $actual   = (float)$row['actual_qty'];
                    $diff     = $actual - $previous;
                    $unitCost = (float)($row['unit_purchase_price'] ?? $product->unit_purchase_price ?? 0);
                    $worth    = abs($diff) * $unitCost;

                    StockAdjustmentItem::create([
                        'stock_adjustment_id' => $adj->id,
                        'product_id'          => $product->id,
                        'previous_qty'        => $previous,
                        'actual_qty'          => $actual,
                        'diff_qty'            => $diff,
                        'unit_purchase_price' => $unitCost,
                        'worth_adjusted'      => $worth,
                    ]);

                    $totalWorth += $worth;

                    // Apply to product-level qty
                    $this->setProductQty($product, $actual);
                }

                $touchedProducts[$product->id] = true;
            }

            // Sync product-level totals with batch sums (for products with any batches)
            foreach (array_keys($touchedProducts) as $pid) {
                /** @var Product $p */
                $p = Product::query()->lockForUpdate()->findOrFail($pid);
                $batchCount = Batch::where('product_id', $pid)->count();
                if ($batchCount > 0) {
                    $this->setProductQty($p, $this->sumBatches($p));
                }
            }

            $adj->update(['total_worth' => $totalWorth]);
            // return created (and authorized view)
            $this->authorize('view', $adj);
            return $this->show($adj->id);
        });
    }

    public function update(Request $request, $id)
    {
        $adj = StockAdjustment::with('items')->findOrFail($id);
        // 🔒 update instance
        $this->authorize('update', $adj);

        $data = $this->validatePayload($request, $id);

        return DB::transaction(function () use ($adj, $data) {
            $baselineQtyByKey = [];
            $oldItems = $adj->items->keyBy(function ($item) {
                return $this->stockAdjustmentItemKey([
                    'product_id' => $item->product_id,
                    'batch_number' => $item->batch_number,
                    'expiry' => $item->expiry,
                    'pack_size' => $item->pack_size,
                ]);
            });

            $newItems = collect($data['items'])->keyBy(function ($row) {
                return $this->stockAdjustmentItemKey($row);
            });

            $allKeys = array_values(array_unique(array_merge(
                $oldItems->keys()->all(),
                $newItems->keys()->all()
            )));

            foreach ($allKeys as $key) {
                $old = $oldItems->get($key);
                $new = $newItems->get($key);

                $productId = (int)($new['product_id'] ?? $old->product_id ?? 0);
                if ($productId <= 0) {
                    continue;
                }

                /** @var Product $product */
                $product = Product::query()->lockForUpdate()->findOrFail($productId);
                $baselineQtyByKey[$key] = $old ? (float)($old->previous_qty ?? 0) : 0.0;

                $oldActual = (float)($old->actual_qty ?? 0);
                $newActual = (float)($new['actual_qty'] ?? 0);
                $delta = $newActual - $oldActual;

                if ($delta !== 0.0) {
                    if (!empty($new['batch_number']) || !empty($old->batch_number)) {
                        $batchNumber = $new['batch_number'] ?? $old->batch_number;
                        $batch = Batch::query()->lockForUpdate()
                            ->where('product_id', $product->id)
                            ->where('batch_number', $batchNumber)
                            ->first();
                        if ($batch) {
                            $this->addBatchQty($batch, $delta);
                        }
                    } else {
                        $this->addProductQty($product, $delta);
                    }
                }
            }

            // Delete old items after live stock has been adjusted by net deltas only
            $adj->items()->delete();

            $totalWorth = 0.0;
            $touchedProducts = [];
            foreach ($data['items'] as $row) {
                /** @var Product $product */
                $product = Product::query()->lockForUpdate()->findOrFail($row['product_id']);
                $useBatch = isset($row['batch_number']) && $row['batch_number'] !== null && $row['batch_number'] !== '';
                $key = $this->stockAdjustmentItemKey($row);
                $previous = (float)($baselineQtyByKey[$key] ?? $this->getProductQty($product));
                $actual   = (float)$row['actual_qty'];
                $diff     = $actual - $previous;
                $unitCost = (float)($row['unit_purchase_price'] ?? $product->unit_purchase_price ?? 0);
                $worth    = abs($diff) * $unitCost;

                StockAdjustmentItem::create([
                    'stock_adjustment_id' => $adj->id,
                    'product_id'          => $product->id,
                    'batch_number'        => $row['batch_number'] ?? null,
                    'expiry'              => $row['expiry'] ?? null,
                    'pack_size'           => $row['pack_size'] ?? null,
                    'previous_qty'        => $previous,
                    'actual_qty'          => $actual,
                    'diff_qty'            => $diff,
                    'unit_purchase_price' => $unitCost,
                    'worth_adjusted'      => $worth,
                ]);

                $totalWorth += $worth;
                $touchedProducts[$product->id] = true;
            }

            foreach (array_keys($touchedProducts) as $pid) {
                /** @var Product $p */
                $p = Product::query()->lockForUpdate()->findOrFail($pid);
                $batchCount = Batch::where('product_id', $pid)->count();
                if ($batchCount > 0) {
                    $this->setProductQty($p, $this->sumBatches($p));
                }
            }

            $adj->update([
                'posted_number' => $data['posted_number'],
                'posted_date'   => $data['posted_date'],
                'note'          => $data['note'],
                'total_worth'   => $totalWorth,
            ]);

            // return updated (and authorized view)
            $this->authorize('view', $adj);
            return $this->show($adj->id);
        });
    }

    public function destroy($id)
    {
        $adj = StockAdjustment::with('items')->findOrFail($id);
        // 🔒 delete instance
        $this->authorize('delete', $adj);

        return DB::transaction(function () use ($adj) {
            $touchedProducts = [];

            foreach ($adj->items as $item) {
                /** @var Product $product */
                $product = Product::query()->lockForUpdate()->findOrFail($item->product_id);
                $inverseDiff = (float)($item->diff_qty ?? ((float)$item->actual_qty - (float)$item->previous_qty));
                if ($item->batch_number) {
                    /** @var Batch $batch */
                    $batch = Batch::query()->lockForUpdate()
                        ->where('product_id', $product->id)
                        ->where('batch_number', $item->batch_number)
                        ->first();
                    if ($batch) {
                        $this->addBatchQty($batch, -$inverseDiff);
                    }
                } else {
                    $this->addProductQty($product, -$inverseDiff);
                }
                $touchedProducts[$product->id] = true;
            }

            $adj->items()->delete();
            $adj->delete();

            foreach (array_keys($touchedProducts) as $pid) {
                /** @var Product $p */
                $p = Product::query()->lockForUpdate()->findOrFail($pid);
                $batchCount = Batch::where('product_id', $pid)->count();
                if ($batchCount > 0) {
                    $this->setProductQty($p, $this->sumBatches($p));
                }
            }

            return response()->noContent();
        });
    }

    // ===== Validation =====

    protected function validatePayload(Request $request, $id = null): array
    {
        return $request->validate([
            'posted_number' => ['required', 'string', Rule::unique('stock_adjustments','posted_number')->ignore($id)],
            'posted_date'   => ['required', 'date'],
            'note'          => ['required', 'string', 'max:2000'],

            'items'                          => ['required', 'array', 'min:1'],
            'items.*.product_id'            => ['required','integer','exists:products,id'],
            'items.*.actual_qty'            => ['required','numeric','min:0'],
            'items.*.unit_purchase_price'   => ['nullable','numeric','min:0'],
            'items.*.batch_number'          => ['nullable','string','max:191'],
            'items.*.expiry'                => ['nullable','date'],
            'items.*.pack_size'             => ['nullable','numeric','min:0'],
        ]);
    }

    protected function stockAdjustmentItemKey(array|object $row): string
    {
        $data = is_object($row) ? (array) $row : $row;

        return implode('|', [
            (int)($data['product_id'] ?? 0),
            trim((string)($data['batch_number'] ?? '')),
            trim((string)($data['expiry'] ?? '')),
            trim((string)($data['pack_size'] ?? '')),
        ]);
    }
}
