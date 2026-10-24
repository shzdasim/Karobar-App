<?php

namespace App\Http\Controllers;

use App\Models\Batch;
use App\Models\Customer;
use App\Models\InvoiceDraft;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Illuminate\Validation\ValidationException;

class InvoiceDraftController extends Controller
{
    private function resolveDraft($id): InvoiceDraft
    {
        if (is_numeric($id)) {
            return InvoiceDraft::findOrFail((int)$id);
        }
        return InvoiceDraft::where('uuid', $id)->firstOrFail();
    }

    public function index(Request $request)
    {
        $this->authorize('viewAny', InvoiceDraft::class);

        $user = $request->user();
        $query = InvoiceDraft::query();

        // Unless Admin requests another user's drafts, scope to current user
        if ($request->has('user_id') && $user->hasRole('Admin')) {
            $query->where('user_id', $request->query('user_id'));
        } else {
            $query->where('user_id', $user->id);
        }

        if ($type = $request->query('type')) {
            $query->where('type', $type);
        }

        $status = $request->query('status', 'draft');
        if ($status !== 'all') {
            $query->where('status', $status);
        }

        $drafts = $query->orderByDesc('updated_at')->get([
            'id', 'uuid', 'user_id', 'type', 'title', 'party_name',
            'total_amount', 'items_count', 'version', 'status',
            'payload', 'posted_invoice_id', 'posted_invoice_type', 'created_at', 'updated_at'
        ]);

        $allProductIds = $drafts->flatMap(function ($d) {
            return collect($d->payload['items'] ?? [])->pluck('product_id')->filter();
        })->unique()->values()->all();

        $productMap = empty($allProductIds) ? [] : Product::whereIn('id', $allProductIds)->pluck('name', 'id')->all();

        $result = $drafts->map(function ($d) use ($productMap) {
            $items = $d->payload['items'] ?? [];
            $itemsSummary = [];
            foreach ($items as $it) {
                $pid = $it['product_id'] ?? null;
                if ($pid) {
                    $qty = $it['quantity'] ?? ($it['unit_quantity'] ?? 0);
                    $name = $productMap[$pid] ?? "Product #{$pid}";
                    $itemsSummary[] = [
                        'product_id' => $pid,
                        'name'       => $name,
                        'quantity'   => (int)$qty,
                    ];
                }
            }

            $arr = $d->only([
                'id', 'uuid', 'user_id', 'type', 'title', 'party_name',
                'total_amount', 'items_count', 'version', 'status',
                'posted_invoice_id', 'posted_invoice_type', 'created_at', 'updated_at'
            ]);
            $arr['items_summary'] = $itemsSummary;
            return $arr;
        });

        return response()->json($result);
    }

    public function show($id)
    {
        $draft = $this->resolveDraft($id);
        $this->authorize('view', $draft);

        // Annotate with live business data check
        $payload = $draft->payload ?? [];
        $items = $payload['items'] ?? [];
        $productIds = collect($items)->pluck('product_id')->filter()->unique()->values();
        $products = Product::whereIn('id', $productIds)->with(['batches', 'category', 'brand', 'supplier'])->get();
        $productsKeyed = $products->keyBy('id');

        // Enrich items with product details and product_name if missing
        if (!empty($items)) {
            foreach ($items as &$item) {
                $pid = $item['product_id'] ?? null;
                if ($pid && isset($productsKeyed[$pid])) {
                    $prod = $productsKeyed[$pid];
                    if (empty($item['product_name'])) {
                        $item['product_name'] = $prod->name;
                    }
                    if (empty($item['product'])) {
                        $item['product'] = [
                            'id' => $prod->id,
                            'name' => $prod->name,
                            'unit_sale_price' => $prod->unit_sale_price,
                            'pack_purchase_price' => $prod->pack_purchase_price,
                            'pack_size' => $prod->pack_size,
                            'quantity' => $prod->quantity,
                            'narcotic' => $prod->narcotic,
                        ];
                    }
                }
            }
            unset($item);
            $payload['items'] = $items;
            $draft->payload = $payload;
        }

        $annotations = [
            'party_valid'   => true,
            'party_warning' => null,
            'item_issues'   => [],
        ];

        if (in_array($draft->type, ['sale_retail', 'sale_wholesale'])) {
            $customerId = $payload['customer_id'] ?? null;
            if ($customerId) {
                $customer = Customer::find($customerId);
                if (!$customer) {
                    $annotations['party_valid'] = false;
                    $annotations['party_warning'] = 'The selected customer no longer exists.';
                }
            }

            foreach ($items as $idx => $item) {
                $productId = $item['product_id'] ?? null;
                $product = $productId ? $productsKeyed->get($productId) : null;
                $batchNo = $item['batch_number'] ?? ($item['batch'] ?? null);
                $batch = ($product && $batchNo) ? $product->batches->firstWhere('batch_number', $batchNo) : null;

                $lineIssues = [];
                if (!$product) {
                    $lineIssues[] = 'Product no longer exists in catalogue.';
                } else {
                    $reqQty = (int)($item['quantity'] ?? 0);
                    $availQty = (int)($product->quantity ?? 0);
                    if ($batch) {
                        $batchQty = (int)($batch->quantity ?? 0);
                        if ($reqQty > $batchQty) {
                            $lineIssues[] = "Batch only has {$batchQty} units available (requested {$reqQty}).";
                        }
                    } elseif ($reqQty > $availQty) {
                        $lineIssues[] = "Product only has {$availQty} units in stock (requested {$reqQty}).";
                    }
                }

                if (!empty($lineIssues)) {
                    $annotations['item_issues'][$idx] = $lineIssues;
                }
            }
        } elseif ($draft->type === 'purchase') {
            $supplierId = $payload['supplier_id'] ?? null;
            if ($supplierId) {
                $supplier = Supplier::find($supplierId);
                if (!$supplier) {
                    $annotations['party_valid'] = false;
                    $annotations['party_warning'] = 'The selected supplier no longer exists.';
                }
            }
            foreach ($items as $idx => $item) {
                $productId = $item['product_id'] ?? null;
                if ($productId && !$productsKeyed->has($productId)) {
                    $annotations['item_issues'][$idx] = ['Product no longer exists in catalogue.'];
                }
            }
        }

        return response()->json([
            'draft'       => $draft,
            'products'    => $products,
            'annotations' => $annotations,
        ]);
    }

    public function store(Request $request)
    {
        $this->authorize('create', InvoiceDraft::class);

        $validated = $request->validate([
            'uuid'    => 'nullable|uuid',
            'type'    => 'required|string|in:sale_retail,sale_wholesale,purchase',
            'title'   => 'nullable|string|max:255',
            'payload' => 'required|array',
        ]);

        $user = $request->user();
        $uuid = $validated['uuid'] ?? (string)Str::uuid();
        $payload = $validated['payload'];

        $validItems = collect($payload['items'] ?? [])->filter(fn($it) => !empty($it['product_id']));
        if ($validItems->isEmpty()) {
            throw ValidationException::withMessages([
                'items' => ['Cannot hold invoice with zero products selected. Please add at least one product.'],
            ]);
        }

        // If draft with this UUID already exists for this user in draft status, redirect to update
        $existing = InvoiceDraft::where('uuid', $uuid)->first();
        if ($existing) {
            if ($existing->user_id !== $user->id && !$user->hasRole('Admin')) {
                abort(403, 'Unauthorized draft access');
            }
            return $this->update($request, $existing->id);
        }

        $partyName = $this->extractPartyName($validated['type'], $payload);
        $totalAmount = $this->extractTotalAmount($validated['type'], $payload);
        $itemsCount = $validItems->count();

        $title = $validated['title'] ?? null;
        if (empty($title)) {
            $title = $this->generateDefaultTitle($validated['type'], $partyName, $itemsCount);
        }

        $draft = InvoiceDraft::create([
            'uuid'         => $uuid,
            'user_id'      => $user->id,
            'type'         => $validated['type'],
            'title'        => $title,
            'party_name'   => $partyName,
            'total_amount' => $totalAmount,
            'items_count'  => $itemsCount,
            'version'      => 1,
            'payload'      => $payload,
            'status'       => 'draft',
        ]);

        return response()->json($draft, 201);
    }

    public function update(Request $request, $id)
    {
        $draft = $this->resolveDraft($id);
        $this->authorize('update', $draft);

        $validated = $request->validate([
            'expected_version' => 'required|integer',
            'title'            => 'nullable|string|max:255',
            'payload'          => 'required|array',
        ]);

        if ($draft->status !== 'draft') {
            return response()->json([
                'message' => "Draft cannot be updated because its status is {$draft->status}.",
            ], 422);
        }

        if ((int)$draft->version !== (int)$validated['expected_version']) {
            return response()->json([
                'message'         => 'Draft conflict: this draft has been modified in another tab or session.',
                'current_version' => $draft->version,
                'draft'           => $draft,
            ], 409);
        }

        $payload = $validated['payload'];
        $validItems = collect($payload['items'] ?? [])->filter(fn($it) => !empty($it['product_id']));
        if ($validItems->isEmpty()) {
            throw ValidationException::withMessages([
                'items' => ['Cannot hold invoice with zero products selected. Please add at least one product.'],
            ]);
        }

        $partyName = $this->extractPartyName($draft->type, $payload);
        $totalAmount = $this->extractTotalAmount($draft->type, $payload);
        $itemsCount = $validItems->count();

        $draft->payload      = $payload;
        $draft->party_name   = $partyName;
        $draft->total_amount = $totalAmount;
        $draft->items_count  = $itemsCount;
        $draft->version     += 1;

        if (array_key_exists('title', $validated) && $validated['title'] !== null) {
            $draft->title = $validated['title'];
        } elseif (empty($draft->title)) {
            $draft->title = $this->generateDefaultTitle($draft->type, $partyName, $itemsCount);
        }

        $draft->save();

        return response()->json($draft);
    }

    public function destroy(Request $request, $id)
    {
        $draft = $this->resolveDraft($id);
        $this->authorize('delete', $draft);

        $draft->status = 'discarded';
        $draft->save();
        $draft->delete();

        return response()->json(['message' => 'Draft discarded successfully']);
    }

    public function post(Request $request, $id)
    {
        $draft = $this->resolveDraft($id);
        $this->authorize('update', $draft);

        // Idempotency: If already posted, return the posted invoice immediately
        if ($draft->status === 'posted') {
            if ($draft->posted_invoice_type === SaleInvoice::class) {
                $invoice = SaleInvoice::find($draft->posted_invoice_id);
                return response()->json([
                    'message'        => 'Invoice already posted',
                    'id'             => $draft->posted_invoice_id,
                    'posted_number'  => $invoice?->posted_number,
                    'already_posted' => true,
                ], 200);
            } elseif ($draft->posted_invoice_type === PurchaseInvoice::class) {
                $invoice = PurchaseInvoice::find($draft->posted_invoice_id);
                return response()->json([
                    'message'        => 'Invoice already posted',
                    'id'             => $draft->posted_invoice_id,
                    'posted_number'  => $invoice?->posted_number,
                    'already_posted' => true,
                ], 200);
            }
        }

        if ($draft->status !== 'draft') {
            return response()->json([
                'message' => "Draft cannot be posted because its status is {$draft->status}.",
            ], 422);
        }

        $payload = $draft->payload ?? [];
        $payload['draft_id'] = $draft->id;

        $fakeRequest = Request::create(
            in_array($draft->type, ['sale_retail', 'sale_wholesale']) ? '/api/sale-invoices' : '/api/purchase-invoices',
            'POST',
            $payload
        );
        $fakeRequest->setUserResolver(fn () => $request->user());

        if (in_array($draft->type, ['sale_retail', 'sale_wholesale'])) {
            $response = app(SaleInvoiceController::class)->store($fakeRequest);
        } else {
            $response = app(PurchaseInvoiceController::class)->store($fakeRequest);
        }

        return $response;
    }

    private function extractPartyName(string $type, array $payload): ?string
    {
        if (in_array($type, ['sale_retail', 'sale_wholesale'])) {
            $cid = $payload['customer_id'] ?? null;
            if ($cid) {
                return Customer::find($cid)?->name;
            }
            return 'Walk-in Customer';
        }

        $sid = $payload['supplier_id'] ?? null;
        if ($sid) {
            return Supplier::find($sid)?->name;
        }
        return null;
    }

    private function extractTotalAmount(string $type, array $payload): float
    {
        if (in_array($type, ['sale_retail', 'sale_wholesale'])) {
            return (float)($payload['total'] ?? 0);
        }
        return (float)($payload['total_amount'] ?? 0);
    }

    private function generateDefaultTitle(string $type, ?string $party, int $itemsCount): string
    {
        $prefix = match ($type) {
            'sale_retail'    => 'Retail Sale',
            'sale_wholesale' => 'Wholesale Sale',
            'purchase'       => 'Purchase Invoice',
            default          => 'Draft',
        };
        $partyStr = $party ? " - {$party}" : "";
        return "{$prefix}{$partyStr} ({$itemsCount} items)";
    }
}

