<?php

namespace App\Http\Controllers;

use App\Models\Product;
use App\Models\Supplier;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\StreamedResponse;

class SupplierController extends Controller
{
    public function search(Request $req)
    {
        $this->authorize('viewAny', Supplier::class);

        $q     = trim($req->input('q', ''));
        $limit = max(1, min((int)$req->input('limit', 20), 100));
        $page  = (int)$req->input('page', 0);

        $query = Supplier::select('id','name','address','phone','email')->orderBy('name');

        if ($q !== '') {
            $query->where('name','like',"%{$q}%");
        }

        // Paginated mode: used by the SupplierSearch modal (e.g. on the
        // Purchase Invoice form). Backward compatible: when no `page` is
        // provided we keep returning a plain array as before.
        if ($page >= 1) {
            $perPage = max(1, min((int)$req->input('per_page', 20), 100));
            return $query->paginate($perPage, ['*'], 'page', $page);
        }

        return $query->limit($limit)->get();
    }

    /**
     * GET /api/suppliers
     */
    public function index()
    {
        $this->authorize('viewAny', Supplier::class);

        return response()->json(
            Supplier::withCount('products')->orderBy('name')->get()
        );
    }

    /**
     * POST /api/suppliers
     */
    public function store(Request $request)
    {
        $this->authorize('create', Supplier::class);

        $data = $request->validate([
            'name'         => 'required|string|max:150|unique:suppliers,name',
            'address'      => 'nullable|string|max:255',
            'phone'        => 'nullable|string|max:50',
            'email'        => 'nullable|email|max:150',
            'credit_limit' => 'nullable|numeric|min:0',
            'credit_days'  => 'nullable|integer|min:0|max:365',
        ]);

        $supplier = Supplier::create($data);

        return response()->json($supplier, 201);
    }

    /**
     * GET /api/suppliers/{supplier}
     */
    public function show(Supplier $supplier)
    {
        $this->authorize('view', $supplier);

        return response()->json($supplier);
    }

    public function creditSummary(Supplier $supplier)
    {
        $this->authorize('view', $supplier);

        $openInvoices = app(\App\Services\PaymentService::class)->getOpenInvoicesForSupplier($supplier->id);
        $totalOutstanding = collect($openInvoices)->sum('due');
        $creditLimit = $supplier->credit_limit !== null ? (float)$supplier->credit_limit : null;
        $availableCredit = $creditLimit !== null ? max(0.0, round($creditLimit - $totalOutstanding, 2)) : null;

        $today = now()->toDateString();
        $totalOverdue = 0.0;
        $overdueCount = 0;
        $oldestDue = null;

        foreach ($openInvoices as $inv) {
            $invObj = \App\Models\PurchaseInvoice::find($inv['id']);
            $dueDate = $invObj?->due_date ? $invObj->due_date->toDateString() : $inv['date'];
            if ($dueDate < $today) {
                $totalOverdue += (float)$inv['due'];
                $overdueCount++;
                if ($oldestDue === null || $dueDate < $oldestDue) {
                    $oldestDue = $dueDate;
                }
            }
        }

        return response()->json([
            'supplier_id'            => $supplier->id,
            'name'                   => $supplier->name,
            'phone'                  => $supplier->phone,
            'credit_limit'           => $creditLimit,
            'credit_days'            => $supplier->credit_days ?? 30,
            'net_balance'            => round($totalOutstanding, 2),
            'current_balance'        => round($totalOutstanding, 2),
            'available_credit'       => $availableCredit,
            'is_limit_exceeded'      => $creditLimit !== null && $creditLimit > 0 && $totalOutstanding > $creditLimit,
            'total_overdue'          => round($totalOverdue, 2),
            'overdue_invoices_count' => $overdueCount,
            'oldest_due_date'        => $oldestDue,
        ]);
    }

    /**
     * PUT/PATCH /api/suppliers/{supplier}
     */
    public function update(Request $request, Supplier $supplier)
    {
        $this->authorize('update', $supplier);

        $data = $request->validate([
            'name'         => 'required|string|max:150|unique:suppliers,name,' . $supplier->id,
            'address'      => 'nullable|string|max:255',
            'phone'        => 'nullable|string|max:50',
            'email'        => 'nullable|email|max:150',
            'credit_limit' => 'nullable|numeric|min:0',
            'credit_days'  => 'nullable|integer|min:0|max:365',
        ]);

        $supplier->update($data);

        return response()->json($supplier);
    }

    /**
     * DELETE /api/suppliers/{supplier}
     */
    public function destroy(Supplier $supplier)
    {
        $this->authorize('delete', $supplier);

        // HARD GUARD: block delete if any product references this supplier
        $inUse = Product::where('supplier_id', $supplier->id)->exists();
        if ($inUse) {
            return response()->json([
                'message' => 'Cannot delete: supplier is used by one or more products.'
            ], 422);
        }

        $supplier->delete();

        return response()->json(null, 204);
    }

    /**
     * GET /api/suppliers/export
     */
    public function export(): StreamedResponse
    {
        $this->authorize('export', Supplier::class);

        $file = 'suppliers_'.now()->format('Y-m-d_H-i-s').'.csv';

        return response()->streamDownload(function () {
            $out = fopen('php://output', 'w');

            // UTF-8 BOM for Excel
            fprintf($out, chr(0xEF).chr(0xBB).chr(0xBF));

            // Header
            fputcsv($out, ['name','address','phone']);

            // Rows (streamed)
            Supplier::select('name','address','phone')
                ->orderBy('name')
                ->chunk(1000, function ($chunk) use ($out) {
                    foreach ($chunk as $s) {
                        fputcsv($out, [
                            (string) ($s->name ?? ''),
                            (string) ($s->address ?? ''),
                            (string) ($s->phone ?? ''),
                        ]);
                    }
                });

            fclose($out);
        }, $file, ['Content-Type' => 'text/csv; charset=UTF-8']);
    }
}
