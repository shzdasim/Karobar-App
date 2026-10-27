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

