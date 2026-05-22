<?php

namespace App\Http\Controllers;

use App\Models\Customer;
use App\Models\Quotation;
use App\Models\QuotationItem;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class QuotationController extends Controller
{
    private function i($v): int { return (int)($v ?? 0); }
    private function f($v): float { return (float)($v ?? 0.0); }

    public function index(Request $request)
    {
        $this->authorize('viewAny', Quotation::class);

        $qPosted   = trim((string)$request->query('posted', ''));
        $qCustomer = trim((string)$request->query('customer', ''));

        $page    = (int)$request->query('page', 1);
        $perPage = (int)$request->query('per_page', 10);

        $query = Quotation::with(['customer'])->orderByDesc('id');

        if ($qPosted !== '') {
            $query->where('posted_number', 'like', '%' . $qPosted . '%');
        }

        if ($qCustomer !== '') {
            $query->whereHas('customer', function ($q) use ($qCustomer) {
                $q->where('name', 'like', '%' . $qCustomer . '%');
            });
        }

        return $query->paginate($perPage, ['*'], 'page', $page);
    }

    public function show($id)
    {
        $quotation = Quotation::with(['customer', 'items'])->findOrFail($id);
        $this->authorize('view', $quotation);

        return response()->json($quotation);
    }

    private function nextPostedNumber(): string
    {
        $last = Quotation::lockForUpdate()->orderByDesc('id')->first();
        $next = $last ? ($last->id + 1) : 1;
        return 'QTN-' . str_pad((string)$next, 6, '0', STR_PAD_LEFT);
    }

    private function normalizeItems(array $items): array
    {
        $out = [];
        foreach ($items as $raw) {
            $lineType = ($raw['manual_product'] ?? false) ? 'manual' : 'product';
            $out[] = [
                'line_type' => $lineType,
                'product_id' => $lineType === 'product' ? ($raw['product_id'] ?? null) : null,
                'manual_name' => $lineType === 'manual' ? ($raw['manual_name'] ?? '') : null,
                'quantity' => $this->i($raw['quantity'] ?? 0),
                'price' => $this->f($raw['price'] ?? 0),
                'item_discount_percentage' => $this->f($raw['item_discount_percentage'] ?? 0),
                'sub_total' => $this->f($raw['sub_total'] ?? 0),
            ];
        }
        return $out;
    }

    public function store(Request $request)
    {
        $this->authorize('create', Quotation::class);

        $data = $request->validate([
            'customer_id' => 'required|exists:customers,id',
            'date' => 'required|date',
            'remarks' => 'nullable|string|max:255',
            'discount_percentage' => 'nullable|numeric',
            'discount_amount' => 'nullable|numeric',
            'tax_percentage' => 'nullable|numeric',
            'tax_amount' => 'nullable|numeric',
            'gross_amount' => 'nullable|numeric',
            'total' => 'nullable|numeric',

            'items' => 'required|array|min:1',
            'items.*.manual_product' => 'nullable|boolean',
            'items.*.product_id' => 'nullable|exists:products,id',
            'items.*.manual_name' => 'nullable|string|max:255',
            'items.*.quantity' => 'required|integer|min:1',
            'items.*.price' => 'required|numeric|min:0',
            'items.*.item_discount_percentage' => 'nullable|numeric',
            'items.*.sub_total' => 'nullable|numeric',
        ]);

        return DB::transaction(function () use ($request, $data) {
            $posted = $this->nextPostedNumber();

            $quotation = Quotation::create([
                'user_id' => $request->user()->id,
                'customer_id' => $data['customer_id'],
                'posted_number' => $posted,
                'date' => $data['date'],
                'remarks' => $data['remarks'] ?? null,
                'discount_percentage' => $data['discount_percentage'] ?? 0,
                'discount_amount' => $data['discount_amount'] ?? 0,
                'tax_percentage' => $data['tax_percentage'] ?? 0,
                'tax_amount' => $data['tax_amount'] ?? 0,
                'item_discount' => $data['item_discount'] ?? 0,
                'gross_amount' => $data['gross_amount'] ?? 0,
                'total' => $data['total'] ?? 0,
            ]);

            $items = $this->normalizeItems($data['items']);
            foreach ($items as $it) {
                $quotation->items()->create($it);
            }

            return response()->json(['message' => 'Quotation created', 'id' => $quotation->id], 201);
        });
    }

    public function update(Request $request, $id)
    {
        $quotation = Quotation::with('items')->findOrFail($id);
        $this->authorize('update', $quotation);

        $data = $request->validate([
            'customer_id' => 'required|exists:customers,id',
            'date' => 'required|date',
            'remarks' => 'nullable|string|max:255',
            'discount_percentage' => 'nullable|numeric',
            'discount_amount' => 'nullable|numeric',
            'tax_percentage' => 'nullable|numeric',
            'tax_amount' => 'nullable|numeric',
            'gross_amount' => 'nullable|numeric',
            'total' => 'nullable|numeric',

            'items' => 'required|array|min:1',
            'items.*.manual_product' => 'nullable|boolean',
            'items.*.product_id' => 'nullable|exists:products,id',
            'items.*.manual_name' => 'nullable|string|max:255',
            'items.*.quantity' => 'required|integer|min:1',
            'items.*.price' => 'required|numeric|min:0',
            'items.*.item_discount_percentage' => 'nullable|numeric',
            'items.*.sub_total' => 'nullable|numeric',
        ]);

        return DB::transaction(function () use ($quotation, $data) {
            $quotation->update([
                'customer_id' => $data['customer_id'],
                'date' => $data['date'],
                'remarks' => $data['remarks'] ?? null,
                'discount_percentage' => $data['discount_percentage'] ?? 0,
                'discount_amount' => $data['discount_amount'] ?? 0,
                'tax_percentage' => $data['tax_percentage'] ?? 0,
                'tax_amount' => $data['tax_amount'] ?? 0,
                'item_discount' => $data['item_discount'] ?? 0,
                'gross_amount' => $data['gross_amount'] ?? 0,
                'total' => $data['total'] ?? 0,
            ]);

            $quotation->items()->delete();

            $items = $this->normalizeItems($data['items']);
            foreach ($items as $it) {
                $quotation->items()->create($it);
            }

            return response()->json(['message' => 'Quotation updated', 'id' => $quotation->id]);
        });
    }

    public function destroy($id)
    {
        $quotation = Quotation::with('items')->findOrFail($id);
        $this->authorize('delete', $quotation);

        $quotation->items()->delete();
        $quotation->delete();

        return response()->json(['message' => 'Quotation deleted']);
    }
}

