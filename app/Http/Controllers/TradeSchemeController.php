<?php

namespace App\Http\Controllers;

use App\Models\Brand;
use App\Models\Category;
use App\Models\Product;
use App\Models\TradeScheme;
use App\Models\TradeSchemeSlab;
use App\Services\TradeSchemeService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class TradeSchemeController extends Controller
{
    public function __construct(protected TradeSchemeService $schemeService)
    {
    }

    /**
     * List all trade schemes.
     */
    public function index(Request $request): JsonResponse
    {
        $query = TradeScheme::with([
            'slabs.bonusProduct:id,name,product_code,pack_size',
            'product:id,name,product_code',
            'brand:id,name',
            'category:id,name',
            'customer:id,name,phone',
        ]);

        if ($request->filled('search')) {
            $s = trim($request->search);
            $query->where(function ($q) use ($s) {
                $q->where('name', 'like', "%{$s}%")
                    ->orWhere('notes', 'like', "%{$s}%")
                    ->orWhereHas('product', fn ($pq) => $pq->where('name', 'like', "%{$s}%"))
                    ->orWhereHas('brand', fn ($bq) => $bq->where('name', 'like', "%{$s}%"))
                    ->orWhereHas('category', fn ($cq) => $cq->where('name', 'like', "%{$s}%"));
            });
        }

        if ($request->filled('status')) {
            $status = $request->status;
            if ($status === 'active') {
                $query->active();
            } elseif ($status === 'inactive') {
                $query->where('is_active', false);
            }
        }

        if ($request->filled('target_type')) {
            $query->where('target_type', $request->target_type);
        }

        if ($request->filled('sale_type') && $request->sale_type !== 'all') {
            $query->forSaleType($request->sale_type);
        }

        $perPage = (int) $request->get('per_page', 25);
        $schemes = $perPage === -1
            ? $query->orderByDesc('priority')->orderByDesc('id')->get()
            : $query->orderByDesc('priority')->orderByDesc('id')->paginate($perPage);

        return response()->json($schemes);
    }

    /**
     * Active lookup for live sale invoicing.
     */
    public function activeLookup(Request $request): JsonResponse
    {
        $saleType = $request->get('sale_type');
        $customerId = $request->filled('customer_id') ? (int) $request->customer_id : null;
        $date = $request->get('date', now()->toDateString());

        $schemes = $this->schemeService->getActiveSchemes($saleType, $customerId, $date);

        return response()->json([
            'schemes' => $schemes,
        ]);
    }

    /**
     * Create a new trade scheme.
     */
    public function store(Request $request): JsonResponse
    {
        $validated = $request->validate([
            'name'                     => 'required|string|max:255',
            'scheme_type'              => 'required|string|in:ratio,slab',
            'target_type'              => 'required|string|in:product,brand,category',
            'target_id'                => 'required|integer',
            'bonus_type'               => 'required|string|in:pack,unit',
            'customer_id'              => 'nullable|integer|exists:customers,id',
            'sale_type'                => 'required|string|in:all,wholesale,retail',
            'start_date'               => 'nullable|date',
            'end_date'                 => 'nullable|date|after_or_equal:start_date',
            'is_active'                => 'boolean',
            'priority'                 => 'integer',
            'notes'                    => 'nullable|string',
            'slabs'                    => 'required|array|min:1',
            'slabs.*.min_qty'          => 'required|numeric|min:0.01',
            'slabs.*.max_qty'          => 'nullable|numeric|gt:slabs.*.min_qty',
            'slabs.*.bonus_qty'        => 'required|numeric|min:0.01',
            'slabs.*.bonus_product_id' => 'nullable|integer|exists:products,id',
        ]);

        $this->validateTargetExists($validated['target_type'], $validated['target_id']);

        $scheme = DB::transaction(function () use ($validated) {
            $scheme = TradeScheme::create([
                'name'        => $validated['name'],
                'scheme_type' => $validated['scheme_type'],
                'target_type' => $validated['target_type'],
                'target_id'   => $validated['target_id'],
                'bonus_type'  => $validated['bonus_type'],
                'customer_id' => $validated['customer_id'] ?? null,
                'sale_type'   => $validated['sale_type'],
                'start_date'  => $validated['start_date'] ?? null,
                'end_date'    => $validated['end_date'] ?? null,
                'is_active'   => $validated['is_active'] ?? true,
                'priority'    => $validated['priority'] ?? 0,
                'notes'       => $validated['notes'] ?? null,
            ]);

            foreach ($validated['slabs'] as $slabData) {
                $scheme->slabs()->create([
                    'min_qty'          => $slabData['min_qty'],
                    'max_qty'          => $slabData['max_qty'] ?? null,
                    'bonus_qty'        => $slabData['bonus_qty'],
                    'bonus_product_id' => $slabData['bonus_product_id'] ?? null,
                ]);
            }

            return $scheme->load(['slabs', 'product', 'brand', 'category', 'customer']);
        });

        return response()->json([
            'message' => 'Trade scheme created successfully',
            'scheme'  => $scheme,
        ], 201);
    }

    /**
     * Show a trade scheme.
     */
    public function show($id): JsonResponse
    {
        $scheme = TradeScheme::with([
            'slabs.bonusProduct:id,name,product_code,pack_size',
            'product:id,name,product_code,pack_size',
            'brand:id,name',
            'category:id,name',
            'customer:id,name,phone',
        ])->findOrFail($id);

        return response()->json($scheme);
    }

    /**
     * Update a trade scheme.
     */
    public function update(Request $request, $id): JsonResponse
    {
        $scheme = TradeScheme::findOrFail($id);

        $validated = $request->validate([
            'name'                     => 'required|string|max:255',
            'scheme_type'              => 'required|string|in:ratio,slab',
            'target_type'              => 'required|string|in:product,brand,category',
            'target_id'                => 'required|integer',
            'bonus_type'               => 'required|string|in:pack,unit',
            'customer_id'              => 'nullable|integer|exists:customers,id',
            'sale_type'                => 'required|string|in:all,wholesale,retail',
            'start_date'               => 'nullable|date',
            'end_date'                 => 'nullable|date|after_or_equal:start_date',
            'is_active'                => 'boolean',
            'priority'                 => 'integer',
            'notes'                    => 'nullable|string',
            'slabs'                    => 'required|array|min:1',
            'slabs.*.min_qty'          => 'required|numeric|min:0.01',
            'slabs.*.max_qty'          => 'nullable|numeric',
            'slabs.*.bonus_qty'        => 'required|numeric|min:0.01',
            'slabs.*.bonus_product_id' => 'nullable|integer|exists:products,id',
        ]);

        $this->validateTargetExists($validated['target_type'], $validated['target_id']);

        $updated = DB::transaction(function () use ($scheme, $validated) {
            $scheme->update([
                'name'        => $validated['name'],
                'scheme_type' => $validated['scheme_type'],
                'target_type' => $validated['target_type'],
                'target_id'   => $validated['target_id'],
                'bonus_type'  => $validated['bonus_type'],
                'customer_id' => $validated['customer_id'] ?? null,
                'sale_type'   => $validated['sale_type'],
                'start_date'  => $validated['start_date'] ?? null,
                'end_date'    => $validated['end_date'] ?? null,
                'is_active'   => $validated['is_active'] ?? $scheme->is_active,
                'priority'    => $validated['priority'] ?? 0,
                'notes'       => $validated['notes'] ?? null,
            ]);

            // Replace slabs
            $scheme->slabs()->delete();
            foreach ($validated['slabs'] as $slabData) {
                $scheme->slabs()->create([
                    'min_qty'          => $slabData['min_qty'],
                    'max_qty'          => $slabData['max_qty'] ?? null,
                    'bonus_qty'        => $slabData['bonus_qty'],
                    'bonus_product_id' => $slabData['bonus_product_id'] ?? null,
                ]);
            }

            return $scheme->load(['slabs', 'product', 'brand', 'category', 'customer']);
        });

        return response()->json([
            'message' => 'Trade scheme updated successfully',
            'scheme'  => $updated,
        ]);
    }

    /**
     * Delete a trade scheme.
     */
    public function destroy($id): JsonResponse
    {
        $scheme = TradeScheme::findOrFail($id);
        $scheme->delete();

        return response()->json([
            'message' => 'Trade scheme deleted successfully',
        ]);
    }

    /**
     * Toggle scheme active status.
     */
    public function toggleActive($id): JsonResponse
    {
        $scheme = TradeScheme::findOrFail($id);
        $scheme->is_active = !$scheme->is_active;
        $scheme->save();

        return response()->json([
            'message'   => $scheme->is_active ? 'Scheme activated' : 'Scheme deactivated',
            'is_active' => $scheme->is_active,
        ]);
    }

    private function validateTargetExists(string $type, int $id): void
    {
        if ($type === 'product' && !Product::where('id', $id)->exists()) {
            abort(422, 'Selected product does not exist.');
        } elseif ($type === 'brand' && !Brand::where('id', $id)->exists()) {
            abort(422, 'Selected brand does not exist.');
        } elseif ($type === 'category' && !Category::where('id', $id)->exists()) {
            abort(422, 'Selected category does not exist.');
        }
    }
}
