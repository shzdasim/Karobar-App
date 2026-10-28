<?php

namespace App\Http\Controllers;

use App\Models\DocumentHistory;
use App\Models\Payment;
use App\Models\PurchaseInvoice;
use App\Models\PurchaseReturn;
use App\Models\SaleInvoice;
use App\Models\SaleReturn;
use App\Models\StockAdjustment;
use App\Models\StockDisposal;
use App\Models\StocktakeSession;
use App\Services\DocumentAuditService;
use Illuminate\Http\Request;

class DocumentHistoryController extends Controller
{
    public function __construct(private DocumentAuditService $auditService)
    {
    }

    public function index(Request $request)
    {
        if (!$request->filled('entity_type') || !$request->filled('entity_id')) {
            $user = $request->user();
            if (!$user || (!$user->hasRole(['Admin', 'admin', 'super-admin']) && !$user->can('settings.view') && !$user->can('user.view'))) {
                abort(403, 'This action is unauthorized.');
            }

            $query = DocumentHistory::with('user:id,name,email')->orderByDesc('created_at')->orderByDesc('id');

            if ($request->filled('entity_type')) {
                $type = $this->resolveEntityType((string) $request->input('entity_type'));
                $query->where('entity_type', $type);
            }
            if ($request->filled('action')) {
                $query->where('action', (string) $request->input('action'));
            }
            if ($request->filled('user_id')) {
                $query->where('user_id', (int) $request->input('user_id'));
            }
            if ($request->filled('search')) {
                $s = trim((string) $request->input('search'));
                $query->where(function ($q) use ($s) {
                    $q->where('reason', 'like', "%{$s}%")
                      ->orWhere('entity_id', 'like', "%{$s}%");
                });
            }

            $perPage = max(1, min((int) $request->input('per_page', 25), 100));
            $paginated = $query->paginate($perPage);
            return response()->json([
                'histories'    => $paginated->items(),
                'data'         => $paginated->items(),
                'total'        => $paginated->total(),
                'current_page' => $paginated->currentPage(),
                'last_page'    => $paginated->lastPage(),
            ]);
        }

        $validated = $request->validate([
            'entity_type' => 'required|string',
            'entity_id'   => 'required|integer',
        ]);

        $type = $this->resolveEntityType($validated['entity_type']);
        $id = $validated['entity_id'];

        $entity = $type::findOrFail($id);
        if (method_exists(app(\Illuminate\Contracts\Auth\Access\Gate::class)->getPolicyFor($entity) ?: new \stdClass(), 'viewHistory')) {
            $this->authorize('viewHistory', $entity);
        } else {
            $this->authorize('view', $entity);
        }

        $histories = $this->auditService->getHistory($type, $id);

        return response()->json([
            'entity_type' => $type,
            'entity_id'   => $id,
            'histories'   => $histories,
        ]);
    }

    private function resolveEntityType(string $type): string
    {
        return match (strtolower(trim($type))) {
            'sale_invoice', 'saleinvoice', 'sale', strtolower(SaleInvoice::class) => SaleInvoice::class,
            'purchase_invoice', 'purchaseinvoice', 'purchase', strtolower(PurchaseInvoice::class) => PurchaseInvoice::class,
            'stock_adjustment', 'stockadjustment', 'stock-adjustment', strtolower(StockAdjustment::class) => StockAdjustment::class,
            'payment', strtolower(Payment::class) => Payment::class,
            'sale_return', 'salereturn', 'sale-return', strtolower(SaleReturn::class) => SaleReturn::class,
            'purchase_return', 'purchasereturn', 'purchase-return', strtolower(PurchaseReturn::class) => PurchaseReturn::class,
            'stock_disposal', 'stockdisposal', 'stock-disposal', strtolower(StockDisposal::class) => StockDisposal::class,
            'stocktake_session', 'stocktakesession', 'stocktake', strtolower(StocktakeSession::class) => StocktakeSession::class,
            default => abort(400, 'Invalid entity type'),
        };
    }
}

