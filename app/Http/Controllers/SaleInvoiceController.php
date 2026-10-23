<?php

namespace App\Http\Controllers;

use App\Models\Batch;
use App\Models\CustomerLedger;
use App\Models\CustomerWholeSalePrice;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\Setting;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\DB;

class SaleInvoiceController extends Controller
{
    private function i($v): int   { return (int)($v ?? 0); }
    private function f($v): float { return (float)($v ?? 0.0); }

    private function unitsFromArray(array $item): int
    {
        return $this->i($item['quantity'] ?? 0);
    }
    private function unitsFromModel(SaleInvoiceItem $item): int
    {
        return $this->i($item->quantity ?? 0);
    }

    private function applyStockDeltaSmart(int $productId, ?string $batchNo, ?string $expiry, int $deltaUnits): void
    {
        $batchNo = trim((string)($batchNo ?? ''));
        $expiry  = trim((string)($expiry ?? ''));

        $actualApplied = $deltaUnits;

        $batch = null;
        if ($batchNo !== '') {
            $batch = app(\App\Services\SaleStockAvailability::class)->matchBatch(
                Batch::where('product_id', $productId)->orderBy('id')->lockForUpdate()->get(),
                ['product_id' => $productId, 'batch_number' => $batchNo, 'expiry' => $expiry]
            );
        }

        if ($batch) {
            $before = $this->i($batch->quantity);
            $after  = $before + $deltaUnits;
            if ($after < 0) throw \Illuminate\Validation\ValidationException::withMessages(['items' => 'Selected batch has insufficient stock.']);
            $actualApplied = $after - $before;
            $batch->quantity = $after;
            $batch->save();
        }

        if ($product = Product::lockForUpdate()->find($productId)) {
            $pBefore = $this->i($product->quantity);
            $pAfter  = $pBefore + $actualApplied;
            if ($pAfter < 0) throw \Illuminate\Validation\ValidationException::withMessages(['items' => 'Product has insufficient stock.']);
            $product->quantity = $pAfter;
            
            // If stock becomes zero, reset average price and margins
            // This ensures moving average resets when all stock is sold
            if ($pAfter <= 0) {
                $product->avg_price = 0;
                $product->margin = 0;
                $product->whole_sale_margin = 0;
            }
            
            $product->save();
        }
    }

    private function createItemsAndReduce(SaleInvoice $invoice, array $items, ?\Illuminate\Support\Collection $originalItems = null, ?\Illuminate\Support\Collection $costs = null): void
    {
        $snapshots = app(\App\Services\SaleCostSnapshot::class);
        // Capture every product before any stock depletion can reset its average.
        $costs ??= $snapshots->productCosts($items);
        $legacy = $originalItems !== null && $originalItems->every(fn ($item) => $item->cost_price_snapshot === null);
        foreach ($items as $raw) {
            $matches = $originalItems === null ? collect() : $originalItems->where('product_id', $raw['product_id']);
            $cost = $legacy ? null : ($matches->isNotEmpty() ? $snapshots->originalCost($originalItems, $raw) : $costs->get($raw['product_id'], 0));
            $payload = [
                'cost_price_snapshot' => $cost,
                'product_id'               => $raw['product_id'],
                'pack_size'                => $this->i($raw['pack_size'] ?? 0),
                'batch_number'             => $raw['batch_number'] ?? ($raw['batch'] ?? null),
                'expiry'                   => $raw['expiry'] ?? ($raw['expiry_date'] ?? null),
                'current_quantity'         => $this->i($raw['current_quantity'] ?? 0),
                'quantity'                 => $this->i($raw['quantity'] ?? 0),
                'price'                    => $this->f($raw['price'] ?? 0),
                'item_discount_percentage' => $this->f($raw['item_discount_percentage'] ?? 0),
                'sub_total'                => $this->f($raw['sub_total'] ?? 0),
                'sale_type'                => $raw['sale_type'] ?? 'retail',
                'is_custom_price'          => $raw['is_custom_price'] ?? false,
            ];

            $item = $invoice->items()->create($payload);

            $units = $this->unitsFromArray($payload);
            $this->applyStockDeltaSmart(
                $item->product_id,
                $item->batch_number,
                $item->expiry,
                -$units
            );
        }
    }

    private function revertItems(SaleInvoice $invoice): void
    {
        $invoice->load('items');
        foreach ($invoice->items as $item) {
            $units = $this->unitsFromModel($item);
            $this->applyStockDeltaSmart(
                $item->product_id,
                $item->batch_number ?? null,
                $item->expiry ?? null,
                +$units
            );
        }
    }

    public function generateNewCode()
    {
        // kept for compatibility but frontend no longer uses this on load
        $this->authorize('create', SaleInvoice::class);
        $last = SaleInvoice::orderBy('id', 'desc')->first();
        $next = $last ? ($last->id + 1) : 1;
        $code = 'SI-' . str_pad((string)$next, 6, '0', STR_PAD_LEFT);
        return response()->json(['posted_number' => $code]);
    }

    public function index(Request $request)
    {
        $this->authorize('viewAny', SaleInvoice::class);
        
        $qPosted   = trim((string) $request->query('posted'));
        $qCustomer = trim((string) $request->query('customer'));

        // Pagination parameters
        $page     = (int) $request->query('page', 1);
        $perPage  = (int) $request->query('per_page', 10);

        $query = SaleInvoice::with(['customer'])->orderByDesc('id');

        if ($qPosted !== '') {
            $query->where('posted_number', 'like', '%'.$qPosted.'%');
        }
        if ($qCustomer !== '') {
            $query->whereHas('customer', function ($q) use ($qCustomer) {
                $q->where('name', 'like', '%'.$qCustomer.'%');
            });
        }

        return $query->paginate($perPage, ['*'], 'page', $page);
    }

    public function show($id)
    {
        $invoice = SaleInvoice::with(['customer', 'items.product'])->findOrFail($id);
        $this->authorize('view', $invoice);
        app(\App\Services\ReturnEntitlement::class)->annotate('sale', $invoice, request()->integer('exclude_return_id') ?: null);
        return response()->json($invoice);
    }

    public function store(Request $request)
    {
        $this->authorize('create', SaleInvoice::class);
        if (!$request->user()) {
            return response()->json(['message' => 'Unauthenticated'], 401);
        }

        // Auto-fallback to default/walk-in customer if customer_id was not provided for debit (cash) sales
        if (empty($request->input('customer_id')) && $request->input('invoice_type', 'debit') !== 'credit') {
            $defaultCustomer = \App\Models\Customer::orderBy('id', 'asc')->first();
            if ($defaultCustomer) {
                $request->merge(['customer_id' => $defaultCustomer->id]);
            }
        }

        // ⬇️ posted_number is NOT provided by client anymore (server assigns)
        $data = $request->validate([
            'draft_id'            => 'nullable|integer|exists:invoice_drafts,id',
            'customer_id'         => 'required|exists:customers,id',
            'invoice_type'        => 'nullable|string|in:credit,debit,default:debit',
            'payment_method'      => 'nullable|string|in:cash,card,bank_transfer,cheque,mobile_wallet,other',
            'sale_type'           => 'nullable|string|in:retail,wholesale,default:retail',
            'wholesale_type'      => 'nullable|string|in:unit,pack,default:unit',
            'date'                => 'required|date',
            'remarks'             => 'nullable|string',
            'doctor_name'         => 'nullable|string',
            'patient_name'        => 'nullable|string',
            'discount_percentage' => 'nullable|numeric',
            'discount_amount'     => 'nullable|numeric',
            'tax_percentage'      => 'nullable|numeric',
            'tax_amount'          => 'nullable|numeric',
            'item_discount'       => 'nullable|numeric',
            'gross_amount'        => 'required|numeric',
            'total'               => 'required|numeric',
            'total_receive'       => 'nullable|numeric',

            'items'                            => 'required|array|min:1',
            'items.*.product_id'               => 'required|exists:products,id',
            'items.*.pack_size'                => 'nullable|integer|min:0',
            'items.*.batch_number'             => 'nullable|string',
            'items.*.batch'                    => 'nullable|string',
            'items.*.expiry'                   => 'nullable|string',
            'items.*.expiry_date'              => 'nullable|string',
            'items.*.current_quantity'         => 'nullable|integer',
            'items.*.quantity'                 => 'required|integer|min:1',
            'items.*.price'                    => 'required|numeric|min:0',
            'items.*.item_discount_percentage' => 'nullable|numeric',
            'items.*.sub_total'                => 'required|numeric|min:0',
            'items.*.sale_type'                => 'nullable|string|in:retail,wholesale',
            'items.*.is_custom_price'          => 'nullable|boolean',
        ]);

        return DB::transaction(function () use ($data, $request) {
            $draft = null;
            if (!empty($data['draft_id'])) {
                $draft = \App\Models\InvoiceDraft::lockForUpdate()->find($data['draft_id']);
                if ($draft && $draft->status === 'posted') {
                    $postedInv = SaleInvoice::find($draft->posted_invoice_id);
                    return response()->json([
                        'message'       => 'Sale Invoice already created',
                        'id'            => $draft->posted_invoice_id,
                        'posted_number' => $postedInv?->posted_number,
                    ], 200);
                }
            }

            app(\App\Services\SaleStockAvailability::class)->validate($data['items']);
            // 🔐 Concurrency-safe numbering: lock last row, compute next
            $last = SaleInvoice::lockForUpdate()->orderByDesc('id')->first();
            $next = $last ? ($last->id + 1) : 1;
            $posted = 'SI-' . str_pad((string)$next, 6, '0', STR_PAD_LEFT);

            $invoiceData = [
                'user_id'            => $request->user()->id,
                'customer_id'        => $data['customer_id'],
                'invoice_type'       => $data['invoice_type'] ?? 'debit',
                'sale_type'          => $data['sale_type'] ?? 'retail',
                'wholesale_type'     => $data['wholesale_type'] ?? 'unit',
                'posted_number'      => $posted, // assigned on save
                'date'               => $data['date'],
                'remarks'            => $data['remarks'] ?? null,
                'doctor_name'        => $data['doctor_name'] ?? null,
                'patient_name'       => $data['patient_name'] ?? null,
                'discount_percentage'=> $data['discount_percentage'] ?? 0,
                'discount_amount'    => $data['discount_amount'] ?? 0,
                'tax_percentage'     => $data['tax_percentage'] ?? 0,
                'tax_amount'         => $data['tax_amount'] ?? 0,
                'item_discount'      => $data['item_discount'] ?? 0,
                'gross_amount'       => $data['gross_amount'],
                'total'              => $data['total'],
                'total_receive'      => $data['total_receive'] ?? 0,
            ];

            if (\Illuminate\Support\Facades\Schema::hasColumn('sale_invoices', 'payment_method')) {
                $invoiceData['payment_method'] = $data['payment_method'] ?? 'cash';
            }

            $invoice = SaleInvoice::create($invoiceData);

            $this->createItemsAndReduce($invoice, $data['items']);

            // Save customer-specific wholesale prices if any custom prices were used
            $saleType = $data['sale_type'] ?? 'retail';
            if ($saleType === 'wholesale' && isset($data['items'])) {
                foreach ($data['items'] as $raw) {
                    if (isset($raw['is_custom_price']) && $raw['is_custom_price'] === true) {
                        CustomerWholeSalePrice::updateOrCreate(
                            [
                                'customer_id' => $data['customer_id'],
                                'product_id' => $raw['product_id'],
                            ],
                            [
                                'pack_price' => $raw['price'],
                            ]
                        );
                    }
                }
            }

            // Create customer ledger entry ONLY for credit sales
            $invoiceType = $data['invoice_type'] ?? 'debit';
            if ($invoiceType === 'credit') {
                $invTotal = (float)$data['total'];
                $totalRecv = (float)($data['total_receive'] ?? 0);
                $recvOnInv = min($totalRecv, $invTotal);
                $excessRecv = max(0.0, $totalRecv - $invTotal);

                $ledger = new CustomerLedger();
                $ledger->customer_id       = $data['customer_id'];
                $ledger->sale_invoice_id   = $invoice->id;
                $ledger->entry_type        = 'invoice';
                $ledger->is_manual         = false;
                $ledger->entry_date        = $data['date'];
                $ledger->posted_number     = $posted;
                $ledger->invoice_total     = $invTotal;
                $ledger->total_received    = $recvOnInv;
                $ledger->balance_remaining = max($invTotal - $recvOnInv, 0);
                $ledger->credited_amount   = 0;
                $ledger->payment_ref       = null;
                $ledger->description       = 'Sale invoice ' . $posted;
                $ledger->created_by        = $request->user()->id;
                $ledger->save();

                // If customer paid more than invoice total, credit excess to customer ledger as payment received
                if ($excessRecv > 0) {
                    $excessEntry = new CustomerLedger();
                    $excessEntry->customer_id       = $data['customer_id'];
                    $excessEntry->sale_invoice_id   = null;
                    $excessEntry->entry_type        = 'payment';
                    $excessEntry->is_manual         = false;
                    $excessEntry->entry_date        = $data['date'];
                    $excessEntry->posted_number     = $posted;
                    $excessEntry->invoice_total     = 0;
                    $excessEntry->total_received    = 0;
                    $excessEntry->balance_remaining = 0;
                    $excessEntry->credited_amount   = $excessRecv;
                    $excessEntry->payment_ref       = 'Inv #' . $posted;
                    $excessEntry->description       = 'Payment received on invoice ' . $posted;
                    $excessEntry->created_by        = $request->user()->id;
                    $excessEntry->save();
                }
            }

            if ($draft) {
                $draft->status = 'posted';
                $draft->posted_invoice_id = $invoice->id;
                $draft->posted_invoice_type = SaleInvoice::class;
                $draft->save();
            }

            // Cash Drawer recording if drawer is open and cash was received
            $payMethod = $invoice->payment_method ?? 'cash';
            $cashReceived = (float)($invoice->total_receive ?? 0);
            if (($invoice->invoice_type ?? 'debit') === 'debit' && $cashReceived <= 0) {
                $cashReceived = (float)$invoice->total;
            }
            if ($payMethod === 'cash' && $cashReceived > 0) {
                app(\App\Services\CashDrawerService::class)->recordTransaction(
                    null,
                    'cash_sale',
                    $cashReceived,
                    'sale_invoice',
                    $invoice->id,
                    "Cash sale {$posted}",
                    $request->user()
                );
            }

            app(\App\Services\DocumentAuditService::class)->recordCreate($invoice->load('items.product'), $request->user());

            return response()->json(['message' => 'Sale Invoice created', 'id' => $invoice->id, 'posted_number' => $posted], 201);
        });
    }

    public function update(Request $request, $id)
    {
        $invoice = SaleInvoice::with(['items.product', 'customer'])->findOrFail($id);
        $this->authorize('update', $invoice);

        if ($invoice->status === 'reversed') {
            abort(422, 'Cannot edit an invoice that has been reversed.');
        }

        $auditService = app(\App\Services\DocumentAuditService::class);
        $beforeSnapshot = $auditService->snapshot($invoice);

        // Auto-fallback to default/walk-in customer if customer_id was not provided for debit (cash) sales
        if (empty($request->input('customer_id')) && $request->input('invoice_type', $invoice->invoice_type ?? 'debit') !== 'credit') {
            $defaultCustomer = \App\Models\Customer::orderBy('id', 'asc')->first();
            if ($defaultCustomer) {
                $request->merge(['customer_id' => $defaultCustomer->id]);
            }
        }

        // keep posted_number unique on update (user may not change it)
        $data = $request->validate([
            'reason'              => 'nullable|string|max:500',
            'update_drawer'       => 'nullable|boolean',
            'customer_id'         => 'required|exists:customers,id',
            'invoice_type'        => 'nullable|string|in:credit,debit',
            'payment_method'      => 'nullable|string|in:cash,card,bank_transfer,cheque,mobile_wallet,other',
            'sale_type'           => 'nullable|string|in:retail,wholesale',
            'wholesale_type'      => 'nullable|string|in:unit,pack',
            'posted_number'       => 'required|string|unique:sale_invoices,posted_number,' . $invoice->id,
            'date'                => 'required|date',
            'remarks'             => 'nullable|string',
            'doctor_name'         => 'nullable|string',
            'patient_name'        => 'nullable|string',
            'discount_percentage' => 'nullable|numeric',
            'discount_amount'     => 'nullable|numeric',
            'tax_percentage'      => 'nullable|numeric',
            'tax_amount'          => 'nullable|numeric',
            'item_discount'       => 'nullable|numeric',
            'gross_amount'        => 'required|numeric',
            'total'               => 'required|numeric',
            'total_receive'       => 'nullable|numeric',

            'items'                            => 'required|array|min:1',
            'items.*.id'                       => 'nullable|integer',
            'items.*.product_id'               => 'required|exists:products,id',
            'items.*.pack_size'                => 'nullable|integer|min:0',
            'items.*.batch_number'             => 'nullable|string',
            'items.*.batch'                    => 'nullable|string',
            'items.*.expiry'                   => 'nullable|string',
            'items.*.expiry_date'              => 'nullable|string',
            'items.*.current_quantity'         => 'nullable|integer',
            'items.*.quantity'                 => 'required|integer|min:1',
            'items.*.price'                    => 'required|numeric|min:0',
            'items.*.item_discount_percentage' => 'nullable|numeric',
            'items.*.sub_total'                => 'required|numeric|min:0',
            'items.*.sale_type'                => 'nullable|string|in:retail,wholesale',
            'items.*.is_custom_price'          => 'nullable|boolean',
        ]);

        return DB::transaction(function () use ($invoice, $data, $request, $auditService, $beforeSnapshot) {
            $invoice = SaleInvoice::lockForUpdate()->findOrFail($invoice->id);
            $originalItems = $invoice->items()->get();
            app(\App\Services\SaleStockAvailability::class)->validate($data['items'], $originalItems);
            $costs = app(\App\Services\SaleCostSnapshot::class)->productCosts($data['items']);
            $this->revertItems($invoice);
            $invoice->items()->delete();

            // Capture old cash state before update
            $oldPayMethod = $invoice->payment_method ?? 'cash';
            $oldInvoiceType = $invoice->invoice_type ?? 'debit';
            $oldReceived = (float)($invoice->total_receive ?? 0);
            if ($oldInvoiceType === 'debit' && $oldReceived <= 0) {
                $oldReceived = (float)$invoice->total;
            }
            $oldCash = ($oldPayMethod === 'cash') ? $oldReceived : 0.0;

            // Store old invoice type and sale type before update
            $oldInvoiceType = $invoice->invoice_type ?? 'debit';
            $oldSaleType = $invoice->sale_type ?? 'retail';
            $newInvoiceType = $data['invoice_type'] ?? $oldInvoiceType;
            $newSaleType = $data['sale_type'] ?? $oldSaleType;

            $updateData = [
                'customer_id'        => $data['customer_id'],
                'invoice_type'       => $newInvoiceType,
                'sale_type'          => $newSaleType,
                'wholesale_type'     => $data['wholesale_type'] ?? $invoice->wholesale_type ?? 'unit',
                'posted_number'      => $data['posted_number'], // keep whatever is on the record
                'date'               => $data['date'],
                'remarks'            => $data['remarks'] ?? null,
                'doctor_name'        => $data['doctor_name'] ?? null,
                'patient_name'       => $data['patient_name'] ?? null,
                'discount_percentage'=> $data['discount_percentage'] ?? 0,
                'discount_amount'    => $data['discount_amount'] ?? 0,
                'tax_percentage'     => $data['tax_percentage'] ?? 0,
                'tax_amount'         => $data['tax_amount'] ?? 0,
                'item_discount'      => $data['item_discount'] ?? 0,
                'gross_amount'       => $data['gross_amount'],
                'total'              => $data['total'],
                'total_receive'      => $data['total_receive'] ?? 0,
            ];

            if (\Illuminate\Support\Facades\Schema::hasColumn('sale_invoices', 'payment_method')) {
                $updateData['payment_method'] = $data['payment_method'] ?? $invoice->payment_method ?? 'cash';
            }

            $invoice->update($updateData);

            $this->createItemsAndReduce($invoice, $data['items'], $originalItems, $costs);

            // Save customer-specific wholesale prices if any custom prices were used
            if ($newSaleType === 'wholesale' && isset($data['items'])) {
                foreach ($data['items'] as $raw) {
                    if (isset($raw['is_custom_price']) && $raw['is_custom_price'] === true) {
                        CustomerWholeSalePrice::updateOrCreate(
                            [
                                'customer_id' => $data['customer_id'],
                                'product_id' => $raw['product_id'],
                            ],
                            [
                                'pack_price' => $raw['price'],
                            ]
                        );
                    }
                }
            }

            // Handle customer ledger entries
            // If invoice was previously credit but is now debit, delete the ledger entry
            if ($oldInvoiceType === 'credit' && $newInvoiceType !== 'credit') {
                // Remove existing ledger entry for this invoice
                CustomerLedger::where('sale_invoice_id', $invoice->id)->delete();
                CustomerLedger::where('customer_id', $invoice->customer_id)
                    ->where('payment_ref', 'Inv #' . $invoice->posted_number)
                    ->where('entry_type', 'payment')
                    ->delete();
            }
            // If invoice is now credit, create or update ledger entry
            elseif ($newInvoiceType === 'credit') {
                CustomerLedger::where('sale_invoice_id', $invoice->id)->delete();
                CustomerLedger::where('customer_id', $invoice->customer_id)
                    ->where('payment_ref', 'Inv #' . $invoice->posted_number)
                    ->where('entry_type', 'payment')
                    ->delete();

                $invTotal = (float)$data['total'];
                $totalRecv = (float)($data['total_receive'] ?? 0);
                $recvOnInv = min($totalRecv, $invTotal);
                $excessRecv = max(0.0, $totalRecv - $invTotal);

                $ledger = new CustomerLedger();
                $ledger->customer_id       = $data['customer_id'];
                $ledger->sale_invoice_id   = $invoice->id;
                $ledger->entry_type        = 'invoice';
                $ledger->is_manual         = false;
                $ledger->entry_date        = $data['date'];
                $ledger->posted_number     = $data['posted_number'];
                $ledger->invoice_total     = $invTotal;
                $ledger->total_received    = $recvOnInv;
                $ledger->balance_remaining = max($invTotal - $recvOnInv, 0);
                $ledger->credited_amount   = 0;
                $ledger->payment_ref       = null;
                $ledger->description       = 'Sale invoice ' . $data['posted_number'];
                $ledger->created_by        = optional(Auth::user())->id;
                $ledger->save();

                if ($excessRecv > 0) {
                    $excessEntry = new CustomerLedger();
                    $excessEntry->customer_id       = $data['customer_id'];
                    $excessEntry->sale_invoice_id   = null;
                    $excessEntry->entry_type        = 'payment';
                    $excessEntry->is_manual         = false;
                    $excessEntry->entry_date        = $data['date'];
                    $excessEntry->posted_number     = $data['posted_number'];
                    $excessEntry->invoice_total     = 0;
                    $excessEntry->total_received    = 0;
                    $excessEntry->balance_remaining = 0;
                    $excessEntry->credited_amount   = $excessRecv;
                    $excessEntry->payment_ref       = 'Inv #' . $data['posted_number'];
                    $excessEntry->description       = 'Payment received on invoice ' . $data['posted_number'];
                    $excessEntry->created_by        = optional(Auth::user())->id;
                    $excessEntry->save();
                }
            }

            // Sync with active cash drawer if applicable
            $newPayMethod = $invoice->payment_method ?? 'cash';
            $newReceived = (float)($invoice->total_receive ?? 0);
            if ($newInvoiceType === 'debit' && $newReceived <= 0) {
                $newReceived = (float)$invoice->total;
            }
            $newCash = ($newPayMethod === 'cash') ? $newReceived : 0.0;
            $updateDrawerFlag = array_key_exists('update_drawer', $data) ? (bool)$data['update_drawer'] : null;

            app(\App\Services\CashDrawerService::class)->syncSaleInvoiceUpdate(
                $invoice,
                $oldCash,
                $newCash,
                $updateDrawerFlag,
                $request->user()
            );

            $auditService->recordUpdate($invoice->fresh(['items.product', 'customer']), $beforeSnapshot, $request->user(), $data['reason'] ?? 'Sale invoice updated');

            return response()->json(['message' => 'Sale Invoice updated', 'id' => $invoice->id]);
        });
    }

    public function reverse(Request $request, $id)
    {
        $invoice = SaleInvoice::with(['items.product', 'customer'])->findOrFail($id);
        $this->authorize('reverse', $invoice);

        $validated = $request->validate([
            'reason' => 'required|string|min:3|max:500',
        ]);

        if ($invoice->status === 'reversed') {
            return response()->json(['message' => 'Invoice has already been reversed.'], 422);
        }

        // Check if any linked sale return exists
        if (\App\Models\SaleReturn::where('sale_invoice_id', $invoice->id)->exists()) {
            return response()->json([
                'message' => 'Cannot reverse this invoice because linked sale returns exist. Please reverse or remove the returns first.',
            ], 422);
        }

        return DB::transaction(function () use ($invoice, $validated, $request) {
            $invoice = SaleInvoice::lockForUpdate()->findOrFail($invoice->id);
            app(\App\Services\SaleStockAvailability::class)->validate([], $invoice->items()->get());
            
            // Revert inventory stock safely
            $this->revertItems($invoice);

            // If credit sale, remove or balance customer ledger entry
            if ($invoice->invoice_type === 'credit') {
                CustomerLedger::where('sale_invoice_id', $invoice->id)->delete();
                CustomerLedger::where('customer_id', $invoice->customer_id)
                    ->where('payment_ref', 'Inv #' . $invoice->posted_number)
                    ->where('entry_type', 'payment')
                    ->delete();
            }

            $invoice->status = 'reversed';
            $invoice->reversed_at = now();
            $invoice->reversed_by_id = $request->user()?->id;
            $invoice->reversal_reason = $validated['reason'];
            $invoice->save();

            // Reversal refund recorded in cash drawer if cash was received
            app(\App\Services\CashDrawerService::class)->handleSaleInvoiceReversal(
                $invoice,
                $validated['reason'],
                $request->user()
            );

            app(\App\Services\DocumentAuditService::class)->recordReversal($invoice, $request->user(), $validated['reason']);

            return response()->json([
                'message' => 'Sale invoice reversed successfully.',
                'invoice' => $invoice->fresh(['items.product', 'customer']),
            ]);
        });
    }

    public function destroy(Request $request, $id)
    {
        $invoice = SaleInvoice::with(['items', 'customer'])->findOrFail($id);
        $this->authorize('delete', $invoice);

        $mode = strtolower(trim((string)($request->query('mode', $request->input('mode', 'none')))));
        if (!in_array($mode, ['none', 'credit', 'refund'], true)) {
            $mode = 'none';
        }

        $invTotal = (float)($invoice->total ?? $invoice->grand_total ?? $invoice->gross_amount ?? 0);
        $received = (float)($invoice->total_receive ?? $invoice->total_recieve ?? $invoice->received ?? 0);
        $remaining = max($invTotal - $received, 0);

        return DB::transaction(function () use ($invoice, $mode, $invTotal, $received, $remaining, $request) {
            $invoice = SaleInvoice::lockForUpdate()->findOrFail($invoice->id);
            app(\App\Services\SaleStockAvailability::class)->validate([], $invoice->items()->get());
            $this->revertItems($invoice);

            // Only handle customer ledger entries for credit invoices
            // Debit invoices (cash sales) are not tracked in customer ledger
            $isCreditInvoice = ($invoice->invoice_type ?? 'debit') === 'credit';

            if ($isCreditInvoice) {
                // Delete the original ledger entry and any excess payment for this invoice
                CustomerLedger::where('sale_invoice_id', $invoice->id)->delete();
                CustomerLedger::where('customer_id', $invoice->customer_id)
                    ->where('payment_ref', 'Inv #' . $invoice->posted_number)
                    ->where('entry_type', 'payment')
                    ->delete();

                $customerId   = (int)$invoice->customer_id;
                $postedNumber = $invoice->posted_number;
                $entryDate    = $invoice->date ?? now()->toDateString();
                $userId       = optional(Auth::user())->id;

                // mode 'credit' = Store Credit (create negative entry to credit the customer)
                if ($mode === 'credit') {
                    $creditEntry = new CustomerLedger();
                    $creditEntry->customer_id       = $customerId;
                    $creditEntry->entry_type        = 'manual';
                    $creditEntry->is_manual         = true;
                    $creditEntry->entry_date        = $entryDate;
                    $creditEntry->posted_number     = $postedNumber;
                    $creditEntry->invoice_total     = -$invTotal;
                    $creditEntry->total_received    = 0;
                    $creditEntry->balance_remaining = 0;
                    $creditEntry->credited_amount   = 0;
                    $creditEntry->payment_ref       = null;
                    $creditEntry->sale_invoice_id   = null;
                    $creditEntry->description       = 'Store credit for deleted invoice ' . $postedNumber;
                    $creditEntry->created_by        = $userId;
                    $creditEntry->save();
                }

                // mode 'refund' = Refund (delete everything, no ledger entries)
                // No additional ledger entries created - just delete the original entry
            }

            // Adjust cash drawer for deleted invoice
            app(\App\Services\CashDrawerService::class)->handleSaleInvoiceDeletion(
                $invoice,
                $mode,
                $request->user()
            );

            $invoice->items()->delete();
            $invoice->delete();

            return response()->json(['message' => 'Sale Invoice deleted']);
        });
    }

    public function print(Request $request, SaleInvoice $invoice)
    {
        $invoice->load(['items.product', 'customer', 'user']);
        $setting = Setting::first();

        $type = strtolower($request->query('type', $setting->printer_type ?? 'a4'));
        if (!in_array($type, ['a4', 'thermal'])) {
            $type = 'a4';
        }

        $gross  = (float) ($invoice->items?->sum('sub_total') ?? 0);
        $disc   = (float) ($invoice->discount_amount ?? 0);
        $tax    = (float) ($invoice->tax_amount ?? 0);
        $total  = (float) ($invoice->total ?? ($gross - $disc + $tax));

        $receivedOnInvoice = (float) ($invoice->total_receive ?? 0);
        $remainThis        = max($total - $receivedOnInvoice, 0);

        $customerId = (int) $invoice->customer_id;

        // Get customer's Total Due from customer ledger (net_balance)
        // This is the correct approach - get the value from the ledger which already calculates it
        // net_balance = (total_invoiced - received_on_invoice) - payments_credited
        $customerTotalDue = \App\Models\CustomerLedger::getCustomerNetBalance($customerId);

        $gross  = (float) ($invoice->items?->sum('sub_total') ?? 0);
        $disc   = (float) ($invoice->discount_amount ?? 0);
        $tax    = (float) ($invoice->tax_amount ?? 0);
        $total  = (float) ($invoice->total ?? ($gross - $disc + $tax));

        $receivedOnInvoice = (float) ($invoice->total_receive ?? 0);
        $remainThis        = max($total - $receivedOnInvoice, 0);

        // Determine which template to use
        if ($type === 'thermal') {
            // For thermal, use the selected thermal template from settings
            $thermalTemplate = $setting->thermal_template ?? 'standard';
            $templateName = "sale_invoice_thermal_{$thermalTemplate}";
        } else {
            // For A4, use the selected A4 template from settings
            $a4Template = $setting->a4_template ?? 'standard';
            $templateName = "sale_invoice_a4_{$a4Template}";
        }

        return view("printer.{$templateName}", [
            'invoice'            => $invoice,
            'setting'            => $setting,
            'printTotal'         => $total,
            'printReceive'       => $receivedOnInvoice,
            'printRemainThis'    => $remainThis,
            'printCustomerTotalDue' => $customerTotalDue,
        ]);
    }


    public function updateMeta(Request $request, SaleInvoice $saleInvoice)
{
    // Permission
    if (! $request->user()->can('report.sale-detail.edit')) {
        abort(403, 'Unauthorized');
    }

    // Safety: allow only doctor/patient
    $data = $request->validate([
        'doctor_name'  => 'nullable|string|max:255',
        'patient_name' => 'nullable|string|max:255',
    ]);

    // Optional: prevent editing cancelled invoices
    if ($saleInvoice->status === 'cancelled') {
        abort(403, 'Cancelled invoice cannot be edited');
    }

    $saleInvoice->update($data);

    return response()->json([
        'success' => true,
        'doctor_name'  => $saleInvoice->doctor_name,
        'patient_name' => $saleInvoice->patient_name,
    ]);
}

    public function search(Request $request)
    {
        $this->authorize('viewAny', SaleInvoice::class);

        $q = trim((string) $request->query('q', ''));
        $customerId = $request->query('customer_id');
        
        // Pagination parameters
        $page = (int) $request->query('page', 1);
        $perPage = (int) $request->query('per_page', 20);

        $query = SaleInvoice::with(['customer'])->orderByDesc('id');
        if ($customerId) {
            $query->where('customer_id', $customerId);
        }

        // If no search query, return recent invoices
        if (strlen($q) < 1) {
            $results = $query->paginate($perPage, ['*'], 'page', $page);

            // Add computed fields
            $results->getCollection()->transform(function ($invoice) {
                $invTotal = (float) ($invoice->total ?? 0);
                $received = (float) ($invoice->total_receive ?? 0);
                $invoice->remaining = max($invTotal - $received, 0);
                $invoice->customer_name = $invoice->customer?->name ?? ($invoice->customer_id ?? 'N/A');
                return $invoice;
            });

            return response()->json($results);
        }

        // Search by posted_number or customer name
        $query->where(function ($sub) use ($q) {
            $sub->where('posted_number', 'like', '%' . $q . '%')
                ->orWhereHas('customer', function ($customerQuery) use ($q) {
                    $customerQuery->where('name', 'like', '%' . $q . '%');
                });
        });

        $results = $query->paginate($perPage, ['*'], 'page', $page);

        // Add computed fields
        $results->getCollection()->transform(function ($invoice) {
            $invTotal = (float) ($invoice->total ?? 0);
            $received = (float) ($invoice->total_receive ?? 0);
            $invoice->remaining = max($invTotal - $received, 0);
            $invoice->customer_name = $invoice->customer?->name ?? ($invoice->customer_id ?? 'N/A');
            return $invoice;
        });

        return response()->json($results);
    }
}
