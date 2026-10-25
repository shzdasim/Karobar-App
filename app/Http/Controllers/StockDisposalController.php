<?php

namespace App\Http\Controllers;

use App\Models\StockDisposal;
use App\Services\StockDisposalService;
use Carbon\Carbon;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;

class StockDisposalController extends Controller
{
    protected StockDisposalService $disposalService;

    public function __construct(StockDisposalService $disposalService)
    {
        $this->disposalService = $disposalService;
    }

    /**
     * GET /api/stock-disposals
     */
    public function index(Request $request): JsonResponse
    {
        $this->authorize('viewAny', StockDisposal::class);

        $query = StockDisposal::with([
            'items.product:id,name,product_code,narcotic,pack_size',
            'creator:id,name',
            'disposer:id,name',
            'restorer:id,name',
            'expense:id,expense_number,amount',
        ])
            ->withCount('items')
            ->orderByDesc('disposal_date')
            ->orderByDesc('id');

        if ($request->filled('status') && $request->query('status') !== 'all') {
            $query->where('status', $request->query('status'));
        }

        if ($request->filled('cause') && $request->query('cause') !== 'all') {
            $query->where('cause', $request->query('cause'));
        }

        if ($request->filled('disposal_method') && $request->query('disposal_method') !== 'all') {
            $query->where('disposal_method', $request->query('disposal_method'));
        }

        if ($request->filled('from')) {
            $query->whereDate('disposal_date', '>=', $request->query('from'));
        }

        if ($request->filled('to')) {
            $query->whereDate('disposal_date', '<=', $request->query('to'));
        }

        if ($request->boolean('has_narcotics')) {
            $query->where('has_narcotics', true);
        }

        if ($request->filled('search')) {
            $s = trim((string) $request->query('search'));
            $query->where(function ($q) use ($s) {
                $q->where('disposal_number', 'like', "%{$s}%")
                  ->orWhere('reference_no', 'like', "%{$s}%")
                  ->orWhere('witness_name', 'like', "%{$s}%")
                  ->orWhere('notes', 'like', "%{$s}%")
                  ->orWhereHas('items', function ($iq) use ($s) {
                      $iq->where('batch_number', 'like', "%{$s}%")
                         ->orWhereHas('product', function ($pq) use ($s) {
                             $pq->where('name', 'like', "%{$s}%")
                                ->orWhere('product_code', 'like', "%{$s}%");
                         });
                  });
            });
        }

        $perPage = max(5, min(100, (int) $request->query('per_page', 20)));
        $paginated = $query->paginate($perPage);

        // Compute KPIs across the system
        $monthStart = Carbon::today()->startOfMonth()->toDateString();
        $monthEnd   = Carbon::today()->endOfMonth()->toDateString();

        $quarantinedStats = StockDisposal::where('status', StockDisposal::STATUS_QUARANTINED)
            ->selectRaw('COUNT(*) as count, COALESCE(SUM(total_qty), 0) as qty, COALESCE(SUM(total_cost_value), 0) as value')
            ->first();

        $disposedMonthStats = StockDisposal::where('status', StockDisposal::STATUS_DISPOSED)
            ->whereBetween('disposal_date', [$monthStart, $monthEnd])
            ->selectRaw('COUNT(*) as count, COALESCE(SUM(total_qty), 0) as qty, COALESCE(SUM(total_cost_value), 0) as value')
            ->first();

        $allDisposedStats = StockDisposal::where('status', StockDisposal::STATUS_DISPOSED)
            ->selectRaw('COUNT(*) as count, COALESCE(SUM(total_cost_value), 0) as value')
            ->first();

        $narcoticsCount = StockDisposal::where('has_narcotics', true)
            ->whereIn('status', [StockDisposal::STATUS_QUARANTINED, StockDisposal::STATUS_DISPOSED])
            ->count();

        return response()->json([
            'disposals' => $paginated,
            'summary'   => [
                'quarantined_count'        => (int) ($quarantinedStats->count ?? 0),
                'quarantined_qty'          => (float) ($quarantinedStats->qty ?? 0),
                'quarantined_value'        => round((float) ($quarantinedStats->value ?? 0), 2),
                'disposed_month_count'     => (int) ($disposedMonthStats->count ?? 0),
                'disposed_month_qty'       => (float) ($disposedMonthStats->qty ?? 0),
                'disposed_month_value'     => round((float) ($disposedMonthStats->value ?? 0), 2),
                'total_disposed_count'     => (int) ($allDisposedStats->count ?? 0),
                'total_writeoff_value'     => round((float) ($allDisposedStats->value ?? 0), 2),
                'narcotic_disposals_count' => $narcoticsCount,
            ],
            'causes'  => StockDisposal::CAUSES,
            'methods' => StockDisposal::METHODS,
        ]);
    }

    /**
     * GET /api/stock-disposals/new-code
     */
    public function newCode(Request $request): JsonResponse
    {
        $this->authorize('create', StockDisposal::class);

        $date = $request->query('date');
        return response()->json([
            'disposal_number' => $this->disposalService->generateDisposalNumber($date),
            'causes'          => StockDisposal::CAUSES,
            'methods'         => StockDisposal::METHODS,
        ]);
    }

    /**
     * GET /api/stock-disposals/{id}
     */
    public function show($id): JsonResponse
    {
        $disposal = StockDisposal::with([
            'items.product:id,name,product_code,barcode,narcotic,pack_size,unit_purchase_price,unit_sale_price',
            'creator:id,name',
            'disposer:id,name',
            'restorer:id,name',
            'expense:id,expense_number,amount,date',
            'stockAdjustment:id,posted_number,posted_date,total_worth',
        ])->findOrFail($id);

        $this->authorize('view', $disposal);

        return response()->json($disposal);
    }

    /**
     * POST /api/stock-disposals
     */
    public function store(Request $request): JsonResponse
    {
        $this->authorize('create', StockDisposal::class);

        $validated = $request->validate([
            'disposal_number'             => ['nullable', 'string', 'max:50'],
            'disposal_date'               => ['required', 'date'],
            'status'                      => ['required', 'string', Rule::in(['quarantined', 'disposed'])],
            'cause'                       => ['required', 'string', Rule::in(array_keys(StockDisposal::CAUSES))],
            'disposal_method'             => ['required', 'string', Rule::in(array_keys(StockDisposal::METHODS))],
            'reference_no'                => ['nullable', 'string', 'max:100'],
            'witness_name'                => ['nullable', 'string', 'max:191'],
            'witness_designation'         => ['nullable', 'string', 'max:191'],
            'notes'                       => ['nullable', 'string', 'max:2000'],
            'record_expense'              => ['nullable', 'boolean'],
            'items'                       => ['required', 'array', 'min:1'],
            'items.*.product_id'          => ['required', 'integer', 'exists:products,id'],
            'items.*.batch_id'            => ['nullable', 'integer', 'exists:batches,id'],
            'items.*.batch_number'        => ['nullable', 'string', 'max:191'],
            'items.*.expiry_date'         => ['nullable', 'date'],
            'items.*.pack_size'           => ['nullable', 'integer', 'min:1'],
            'items.*.dispose_qty'         => ['required', 'numeric', 'min:0.01'],
            'items.*.unit_purchase_price' => ['nullable', 'numeric', 'min:0'],
            'items.*.item_note'           => ['nullable', 'string', 'max:500'],
        ]);

        $disposal = $this->disposalService->createDisposal($validated, $request->user());

        return response()->json([
            'message'  => $disposal->status === StockDisposal::STATUS_DISPOSED
                ? "Stock disposal {$disposal->disposal_number} finalized and written off successfully."
                : "Stock moved to Quarantine ({$disposal->disposal_number}) and isolated from sellable inventory.",
            'disposal' => $disposal,
        ], 201);
    }

    /**
     * POST /api/stock-disposals/{id}/dispose
     */
    public function dispose(Request $request, $id): JsonResponse
    {
        $disposal = StockDisposal::findOrFail($id);
        $this->authorize('dispose', $disposal);

        $validated = $request->validate([
            'disposal_method'     => ['required', 'string', Rule::in(array_keys(StockDisposal::METHODS))],
            'witness_name'        => ['nullable', 'string', 'max:191'],
            'witness_designation' => ['nullable', 'string', 'max:191'],
            'notes'               => ['nullable', 'string', 'max:2000'],
            'record_expense'      => ['nullable', 'boolean'],
        ]);

        $updated = $this->disposalService->finalizeDisposal($disposal, $validated, $request->user());

        return response()->json([
            'message'  => "Quarantine voucher {$updated->disposal_number} has been permanently disposed and written off.",
            'disposal' => $updated,
        ]);
    }

    /**
     * POST /api/stock-disposals/{id}/restore
     */
    public function restore(Request $request, $id): JsonResponse
    {
        $disposal = StockDisposal::findOrFail($id);
        $this->authorize('restore', $disposal);

        $validated = $request->validate([
            'reason' => ['nullable', 'string', 'max:500'],
        ]);

        $updated = $this->disposalService->restoreFromQuarantine(
            $disposal,
            $validated['reason'] ?? null,
            $request->user()
        );

        return response()->json([
            'message'  => "Quarantine voucher {$updated->disposal_number} restored to sellable stock.",
            'disposal' => $updated,
        ]);
    }
}

