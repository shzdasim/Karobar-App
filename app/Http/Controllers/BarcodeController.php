<?php

namespace App\Http\Controllers;

use App\Models\BarcodeTemplate;
use App\Models\Product;
use App\Models\Setting;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class BarcodeController extends Controller
{
    private function authorizeBarcodeAction(string|array $permissions): void
    {
        $user = request()->user() ?? auth()->user();
        if (!$user) {
            abort(401, 'Unauthenticated.');
        }
        if ($user->hasRole(['Admin', 'admin', 'super-admin'])) {
            return;
        }
        foreach ((array) $permissions as $perm) {
            if ($user->can($perm)) {
                return;
            }
        }
        abort(403, 'This action is unauthorized.');
    }

    /**
     * Get all barcode templates and current store defaults
     * GET /api/barcodes/templates
     */
    public function getTemplates()
    {
        $this->authorizeBarcodeAction(['barcode.view', 'barcode.print']);
        $templates = BarcodeTemplate::orderByDesc('is_default')
            ->orderBy('id')
            ->get();

        $setting = Setting::first();

        return response()->json([
            'templates' => $templates,
            'store_info' => [
                'store_name' => $setting->store_name ?? config('app.name', 'Karobar Pharmacy'),
                'phone_number' => $setting->phone_number ?? '',
                'currency_symbol' => $setting->currency_symbol ?? 'Rs.',
                'currency_position' => $setting->currency_position ?? 'before',
                'currency_decimal_places' => (int)($setting->currency_decimal_places ?? 2),
            ],
        ]);
    }

    /**
     * Store a new custom barcode template
     * POST /api/barcodes/templates
     */
    public function saveTemplate(Request $request)
    {
        $this->authorizeBarcodeAction(['barcode.create', 'barcode.update']);
        $validated = $request->validate([
            'name' => 'required|string|max:100',
            'paper_type' => 'required|in:roll,sheet',
            'width_mm' => 'required|numeric|min:15|max:300',
            'height_mm' => 'required|numeric|min:10|max:300',
            'orientation' => 'required|in:landscape,portrait',
            'columns' => 'nullable|integer|min:1|max:10',
            'rows' => 'nullable|integer|min:1|max:50',
            'gap_x_mm' => 'nullable|numeric|min:0|max:50',
            'gap_y_mm' => 'nullable|numeric|min:0|max:50',
            'margin_top_mm' => 'nullable|numeric|min:0|max:50',
            'margin_left_mm' => 'nullable|numeric|min:0|max:50',
            'margin_right_mm' => 'nullable|numeric|min:0|max:50',
            'margin_bottom_mm' => 'nullable|numeric|min:0|max:50',
            'layout_options' => 'nullable|array',
            'is_default' => 'nullable|boolean',
        ]);

        if (!empty($validated['is_default'])) {
            BarcodeTemplate::where('is_default', true)->update(['is_default' => false]);
        }

        $template = BarcodeTemplate::create($validated);

        return response()->json([
            'message' => 'Template saved successfully',
            'template' => $template,
        ], 201);
    }

    /**
     * Update an existing barcode template
     * PUT /api/barcodes/templates/{id}
     */
    public function updateTemplate(Request $request, $id)
    {
        $this->authorizeBarcodeAction('barcode.update');
        $template = BarcodeTemplate::findOrFail($id);

        $validated = $request->validate([
            'name' => 'sometimes|required|string|max:100',
            'paper_type' => 'sometimes|required|in:roll,sheet',
            'width_mm' => 'sometimes|required|numeric|min:15|max:300',
            'height_mm' => 'sometimes|required|numeric|min:10|max:300',
            'orientation' => 'sometimes|required|in:landscape,portrait',
            'columns' => 'nullable|integer|min:1|max:10',
            'rows' => 'nullable|integer|min:1|max:50',
            'gap_x_mm' => 'nullable|numeric|min:0|max:50',
            'gap_y_mm' => 'nullable|numeric|min:0|max:50',
            'margin_top_mm' => 'nullable|numeric|min:0|max:50',
            'margin_left_mm' => 'nullable|numeric|min:0|max:50',
            'margin_right_mm' => 'nullable|numeric|min:0|max:50',
            'margin_bottom_mm' => 'nullable|numeric|min:0|max:50',
            'layout_options' => 'nullable|array',
            'is_default' => 'nullable|boolean',
        ]);

        if (!empty($validated['is_default'])) {
            BarcodeTemplate::where('id', '!=', $id)->where('is_default', true)->update(['is_default' => false]);
        }

        $template->update($validated);

        return response()->json([
            'message' => 'Template updated successfully',
            'template' => $template,
        ]);
    }

    /**
     * Delete a custom barcode template
     * DELETE /api/barcodes/templates/{id}
     */
    public function deleteTemplate($id)
    {
        $this->authorizeBarcodeAction('barcode.delete');
        $template = BarcodeTemplate::findOrFail($id);

        // Keep at least one template in the database
        if (BarcodeTemplate::count() <= 1) {
            return response()->json([
                'message' => 'Cannot delete the only remaining template',
            ], 422);
        }

        $wasDefault = $template->is_default;
        $template->delete();

        if ($wasDefault) {
            BarcodeTemplate::query()->first()?->update(['is_default' => true]);
        }

        return response()->json([
            'message' => 'Template deleted successfully',
        ]);
    }

    /**
     * Search products with active batches for barcode label printing
     * GET /api/barcodes/products-search
     */
    public function searchProducts(Request $request)
    {
        $this->authorizeBarcodeAction(['barcode.view', 'barcode.print']);
        $search = trim((string) $request->input('search', ''));
        $categoryId = $request->input('category_id');
        $productIds = $request->input('product_ids');

        $query = Product::with(['batches' => function ($q) {
            $q->orderBy('expiry_date', 'asc');
        }])
        ->select([
            'id',
            'product_code',
            'name',
            'formulation',
            'rack',
            'barcode',
            'unit_sale_price',
            'whole_sale_unit_price',
            'quantity',
            'pack_size',
            'category_id',
            'brand_id',
        ]);

        if (!empty($productIds)) {
            $ids = is_array($productIds) ? $productIds : explode(',', (string)$productIds);
            $query->whereIn('id', $ids);
        } elseif ($search !== '') {
            $query->where(function ($q) use ($search) {
                $q->where('name', 'like', "%{$search}%")
                  ->orWhere('barcode', 'like', "%{$search}%")
                  ->orWhere('product_code', 'like', "%{$search}%")
                  ->orWhere('formulation', 'like', "%{$search}%")
                  ->orWhere('rack', 'like', "%{$search}%");
            });
        } elseif (!empty($categoryId)) {
            $query->where('category_id', $categoryId);
        }

        $products = $query->limit(50)->get();

        return response()->json([
            'products' => $products,
        ]);
    }

    /**
     * List purchase orders with items, products, brands, and suppliers for barcode printing
     * GET /api/barcodes/purchase-orders
     */
    public function getPurchaseOrders(Request $request)
    {
        $this->authorizeBarcodeAction(['barcode.view', 'barcode.print']);
        $id = $request->input('id');
        $supplierId = $request->input('supplier_id');
        $brandId = $request->input('brand_id');
        $search = trim((string) $request->input('search', ''));
        $limit = max(1, min((int) $request->input('limit', 50), 100));

        $query = \App\Models\PurchaseOrder::with([
            'supplier:id,name',
            'items.product' => function ($pq) {
                $pq->select([
                    'id',
                    'name',
                    'product_code',
                    'barcode',
                    'unit_sale_price',
                    'whole_sale_unit_price',
                    'quantity',
                    'pack_size',
                    'brand_id',
                    'supplier_id',
                ])
                ->with([
                    'brand:id,name',
                    'supplier:id,name',
                    'batches' => function ($bq) {
                        $bq->orderBy('expiry_date', 'asc');
                    },
                ]);
            },
        ])
        ->withCount('items')
        ->withSum('items', 'ordered_quantity');

        if (!empty($id)) {
            $po = $query->find($id);
            if (!$po) {
                return response()->json(['message' => 'Purchase order not found'], 404);
            }
            return response()->json(['purchase_order' => $po]);
        }

        if (!empty($supplierId)) {
            $query->where('supplier_id', $supplierId);
        }

        if (!empty($brandId)) {
            $query->whereHas('items.product', function ($q) use ($brandId) {
                $q->where('brand_id', $brandId);
            });
        }

        if ($search !== '') {
            $query->where(function ($q) use ($search) {
                $q->where('reference', 'like', "%{$search}%")
                  ->orWhere('id', 'like', "%{$search}%")
                  ->orWhereHas('supplier', function ($sq) use ($search) {
                      $sq->where('name', 'like', "%{$search}%");
                  });
            });
        }

        $orders = $query->orderByDesc('id')->limit($limit)->get();

        return response()->json([
            'purchase_orders' => $orders,
        ]);
    }

    /**
     * High-speed barcode scanner lookup for POS mode
     * GET /api/barcodes/scan
     */
    public function scanProduct(Request $request)
    {
        $this->authorizeBarcodeAction(['barcode-pos.view', 'barcode-pos.create']);
        $code = trim((string) ($request->input('barcode') ?? $request->input('code') ?? ''));
        $productId = $request->input('product_id');

        if (empty($productId) && $code === '') {
            return response()->json([
                'found' => false,
                'message' => 'Please provide a barcode, product code, or product ID',
            ], 422);
        }

        $batchQuery = function ($bq) {
            $today = now()->toDateString();
            $bq->orderByRaw("CASE WHEN expiry_date IS NOT NULL AND expiry_date >= '{$today}' THEN 0 ELSE 1 END")
               ->orderBy('expiry_date', 'asc')
               ->orderBy('id', 'asc');
        };

        if (!empty($productId)) {
            $product = Product::with([
                'brand:id,name',
                'category:id,name',
                'batches' => $batchQuery,
            ])->find($productId);
        } else {
            // Exact match on barcode or product_code
            $product = Product::with([
                'brand:id,name',
                'category:id,name',
                'batches' => $batchQuery,
            ])
            ->where(function ($q) use ($code) {
                $q->where('barcode', $code)
                  ->orWhere('product_code', $code);
            })
            ->first();
        }

        if (!$product) {
            $identifier = !empty($productId) ? "ID '{$productId}'" : "barcode '{$code}'";
            return response()->json([
                'found' => false,
                'barcode' => $code,
                'message' => "Product with {$identifier} not found",
            ], 404);
        }

        // Determine FEFO recommended batch
        $today = now()->toDateString();
        $batches = $product->batches;
        $fefoBatch = $batches->first(function ($b) use ($today) {
            $isNotExpired = empty($b->expiry_date) || $b->expiry_date >= $today;
            return $isNotExpired && (int) $b->quantity > 0;
        });

        // Calculate unassigned stock
        $totalProductQty = (int) ($product->quantity ?? 0);
        $totalBatchedQty = (int) $batches->sum('quantity');
        $unbatchedQty = max(0, $totalProductQty - $totalBatchedQty);

        $selectedBatch = null;
        if ($fefoBatch) {
            $selectedBatch = [
                'id' => $fefoBatch->id,
                'batch_number' => $fefoBatch->batch_number,
                'expiry_date' => $fefoBatch->expiry_date,
                'quantity' => (int) $fefoBatch->quantity,
                'is_expired' => $fefoBatch->is_expired ?? false,
                'is_fefo' => true,
            ];
        } elseif ($unbatchedQty > 0 || $batches->isEmpty()) {
            $selectedBatch = [
                'id' => null,
                'batch_number' => '',
                'expiry_date' => null,
                'quantity' => $unbatchedQty,
                'is_expired' => false,
                'is_unbatched' => true,
            ];
        } elseif ($batches->isNotEmpty()) {
            $first = $batches->first();
            $selectedBatch = [
                'id' => $first->id,
                'batch_number' => $first->batch_number,
                'expiry_date' => $first->expiry_date,
                'quantity' => (int) $first->quantity,
                'is_expired' => $first->is_expired ?? false,
                'is_fefo' => false,
            ];
        }

        return response()->json([
            'found' => true,
            'product' => [
                'id' => $product->id,
                'name' => $product->name,
                'product_code' => $product->product_code,
                'barcode' => $product->barcode,
                'pack_size' => (int) ($product->pack_size ?? 1),
                'unit_sale_price' => (float) ($product->unit_sale_price ?? 0),
                'whole_sale_unit_price' => (float) ($product->whole_sale_unit_price ?? 0),
                'quantity' => $totalProductQty,
                'max_discount' => (float) ($product->max_discount ?? 100),
                'is_narcotic' => (bool) ($product->is_narcotic ?? false),
                'brand' => $product->brand,
                'category' => $product->category,
                'unassigned_quantity' => $unbatchedQty,
                'selected_batch' => $selectedBatch,
                'batches' => $batches->map(fn ($b) => [
                    'id' => $b->id,
                    'batch_number' => $b->batch_number,
                    'expiry_date' => $b->expiry_date,
                    'quantity' => (int) $b->quantity,
                    'is_expired' => $b->is_expired ?? false,
                ]),
            ],
        ]);
    }
}

