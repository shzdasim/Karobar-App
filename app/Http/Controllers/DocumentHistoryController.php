<?php

namespace App\Http\Controllers;

use App\Models\DocumentHistory;
use App\Models\PurchaseInvoice;
use App\Models\SaleInvoice;
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
        $this->authorize('viewHistory', $entity);

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
            'sale_invoice', 'saleinvoice', 'sale' => SaleInvoice::class,
            'purchase_invoice', 'purchaseinvoice', 'purchase' => PurchaseInvoice::class,
            default => class_exists($type) ? $type : abort(400, 'Invalid entity type'),
        };
    }
}

