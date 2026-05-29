<?php

namespace App\Http\Controllers;

use App\Models\Batch;
use App\Models\Product;
use App\Models\PurchaseInvoice;
use App\Models\SupplierLedger;
use App\Models\BankLedger;
use App\Models\Bank;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\DB;

class PurchaseInvoiceController extends Controller
{
    // (Legacy helper) Generate new invoice code — kept for backward compatibility, but
    // the UI no longer calls this; posted_number is now assigned server-side on SAVE.
    public function generateNewCode()
    {
        $this->authorize('create', PurchaseInvoice::class);
        $lastInvoice = PurchaseInvoice::orderBy('id', 'desc')->first();

        if ($lastInvoice && $lastInvoice->posted_number && preg_match('/PRINV-(\d+)/', $lastInvoice->posted_number, $matches)) {
            $lastCodeNum = (int) $matches[1];
            $newCodeNum = $lastCodeNum + 1;
        } else {
            $newCodeNum = 1;
        }

        $formattedCode = 'PRINV-' . str_pad($newCodeNum, 4, '0', STR_PAD_LEFT);
        return response()->json(['posted_number' => $formattedCode]);
    }

    // List all invoices
    public function index(Request $request)
    {
        $this->authorize('viewAny', PurchaseInvoice::class);
        
        $qPosted     = trim((string) $request->query('posted'));
        $qSupplierId = $request->query('supplier_id');
        
        // Pagination parameters
        $page     = (int) $request->query('page', 1);
        $perPage  = (int) $request->query('per_page', 10);

        $query = PurchaseInvoice::with(['supplier']);

        if ($qPosted !== '') {
            $query->where('posted_number', 'like', '%' . $qPosted . '%');
        }

        // Filter by supplier_id when provided (for Purchase Return form)
        if ($qSupplierId) {
            $query->where('supplier_id', $qSupplierId);
        }

        // Also filter by supplier name if provided
        $qSupplier = trim((string) $request->query('supplier'));
        if ($qSupplier !== '') {
            $query->whereHas('supplier', function ($q) use ($qSupplier) {
                $q->where('name', 'like', '%' . $qSupplier . '%');
            });
        }

        return $query->orderByDesc('id')->paginate($perPage, ['*'], 'page', $page);
    }

    // Show single invoice
    public function show(PurchaseInvoice $purchaseInvoice)
    {
        $this->authorize('view', $purchaseInvoice);
        return $purchaseInvoice->load('supplier', 'items.product');
    }

    // Store new invoice — posted_number is assigned HERE (atomic, race-safe)
    public function store(Request $request)
    {
        $this->authorize('create', PurchaseInvoice::class);

        $data = $request->validate([
            'supplier_id'          => 'required|exists:suppliers,id',
            'invoice_type'         => 'nullable|string|in:credit,debit,default:debit',
            'posted_date'          => 'required|date',
            'remarks'              => 'nullable|string',
            'invoice_number'       => 'required|string|unique:purchase_invoices,invoice_number',
            'invoice_amount'       => 'required|numeric',
            'tax_percentage'       => 'nullable|numeric',
            'tax_amount'           => 'nullable|numeric',
            'discount_percentage'  => 'nullable|numeric',
            'discount_amount'      => 'nullable|numeric',
            'total_amount'         => 'required|numeric|min:0',
            'total_paid'           => 'nullable|numeric|min:0',

            // bank used only for debit payments
            'bank_id'              => 'nullable|integer|exists:banks,id',

            'items'                                => 'required|array',
            'items.*.product_id'                   => 'required|exists:products,id',
            'items.*.pack_size'                    => 'nullable|integer',
            'items.*.batch'                        => 'nullable|string',
            'items.*.expiry'                       => 'nullable|date',
            'items.*.pack_quantity'                => 'required|numeric',
            'items.*.unit_quantity'                => 'required|integer',
            'items.*.pack_purchase_price'          => 'required|numeric',
            'items.*.unit_purchase_price'          => 'required|numeric',
            'items.*.pack_sale_price'              => 'required|numeric',
            'items.*.unit_sale_price'              => 'required|numeric',
            'items.*.whole_sale_pack_price'       => 'nullable|numeric',
            'items.*.whole_sale_unit_price'       => 'nullable|numeric',
            'items.*.whole_sale_margin'           => 'nullable|numeric',
            'items.*.item_discount_percentage'     => 'nullable|numeric|min:0',
            'items.*.pack_bonus'                   => 'nullable|numeric|min:0',
            'items.*.unit_bonus'                   => 'nullable|integer|min:0',
            'items.*.margin'                       => 'required|numeric',
            'items.*.sub_total'                    => 'required|numeric',
            'items.*.avg_price'                    => 'required|numeric',
            'items.*.quantity'                     => 'required|integer',
        ]);

        if (!array_key_exists('total_paid', $data) || $data['total_paid'] === null || $data['total_paid'] === '') {
            $data['total_paid'] = $data['total_amount'] ?? 0;
        }

        $invoiceType = $data['invoice_type'] ?? 'debit';
        $totalPaid = (float) ($data['total_paid'] ?? 0);

        if ($invoiceType === 'debit' && $totalPaid > 0 && empty($data['bank_id'])) {
            return response()->json(['message' => 'Bank is required to pay supplier'], 422);
        }

        try {
            return DB::transaction(function () use ($data, $invoiceType, $totalPaid) {
                $prefix = 'PRINV-';
                $lastPosted = DB::table('purchase_invoices')
                    ->whereNotNull('posted_number')
                    ->where('posted_number', 'like', $prefix . '%')
                    ->lockForUpdate()
                    ->orderByDesc('id')
                    ->value('posted_number');

                $nextNum = 1;
                if ($lastPosted && preg_match('/^' . preg_quote($prefix, '/') . '(\d+)$/', $lastPosted, $m)) {
                    $nextNum = (int) $m[1] + 1;
                }
                $nextCode = $prefix . str_pad($nextNum, 4, '0', STR_PAD_LEFT);

                $invoice = PurchaseInvoice::create(array_merge($data, [
                    'posted_number' => $nextCode,
                    'invoice_type'  => $invoiceType,
                ]));

                $this->processInvoiceItems($invoice, $data['items']);

                $this->recalcProductsByIds(
                    collect($data['items'])->pluck('product_id')->unique()->all()
                );

                // credit invoice: supplier ledger invoice row only
                if ($invoiceType === 'credit') {
                    $ledger = new SupplierLedger();
                    $ledger->supplier_id             = $data['supplier_id'];
                    $ledger->purchase_invoice_id   = $invoice->id;
                    $ledger->entry_type            = 'invoice';
                    $ledger->is_manual             = false;
                    $ledger->entry_date            = $data['posted_date'];
                    $ledger->posted_number         = $nextCode;
                    $ledger->invoice_number        = $data['invoice_number'];
                    $ledger->invoice_total         = $data['total_amount'];
                    $ledger->total_paid            = $data['total_paid'] ?? 0;
                    $ledger->debited_amount        = 0;
                    $ledger->credit_remaining      = max((float) $data['total_amount'] - (float) ($data['total_paid'] ?? 0), 0);
                    $ledger->payment_ref           = null;
                    $ledger->description           = 'Purchase invoice ' . $nextCode;
                    $ledger->created_by            = optional(Auth::user())->id;
                    $ledger->save();
                }

                // debit invoice: supplier ledger invoice row + supplier payment row + bank movement
                if ($invoiceType === 'debit') {
                    // create invoice row (optional for debit; but ledger rebuild expects invoice rows for credit only)
                    // Your current system only uses supplier ledger invoice rows for credit. For debit we create payment rows only.
                    if ($totalPaid > 0) {
                        $paymentRow = SupplierLedger::create([
                            'supplier_id'           => $data['supplier_id'],
                            'purchase_invoice_id'  => $invoice->id,
                            'entry_type'            => 'payment',
                            'entry_date'            => $data['posted_date'],
                            'description'           => $data['remarks'] ? $data['remarks'] : ('Purchase payment ' . $nextCode),
                            'posted_number'         => $nextCode,
                            'invoice_number'        => $data['invoice_number'],
                            'invoice_total'         => (float) $data['total_amount'],
                            'total_paid'            => (float) $data['total_paid'] ?? 0,
                            'debited_amount'        => $totalPaid,
                            'payment_ref'           => $data['invoice_number'],
                            'bank_id'                => $data['bank_id'],
                            'credit_remaining'     => 0,
                            'is_manual'             => false,
                            'created_by'            => optional(Auth::user())->id,
                        ]);

                        BankLedger::create([
                            'bank_id'     => (int) $data['bank_id'],
                            'entry_date'  => $paymentRow->entry_date,
                            'entry_type'  => 'supplier_payment',
                            'ref_type'    => 'supplier_ledger',
                            'ref_id'      => $paymentRow->id,
                            'direction'   => 'debit',
                            'amount'      => $totalPaid,
                            'description' => $paymentRow->description,
                        ]);

                        $bank = Bank::find($data['bank_id']);
                        if ($bank) {
                            $bank->balance = (float) $bank->balance - $totalPaid;
                            $bank->save();
                        }
                    }
                }

                return response()->json($invoice->load('items.product', 'supplier'), 201);
            });
        } catch (\Illuminate\Database\QueryException $e) {
            return $this->respondDuplicateError($e);
        } catch (\Exception $e) {
            return response()->json([
                'message' => 'Error creating purchase invoice',
                'error'   => $e->getMessage(),
            ], 500);
        }
    }

    // Update invoice (posted_number stays unchanged; do not regenerate)
    public function update(Request $request, PurchaseInvoice $purchaseInvoice)
    {
        $this->authorize('update', $purchaseInvoice);

        $data = $request->validate([
            'supplier_id'          => 'required|exists:suppliers,id',
            'invoice_type'         => 'nullable|string|in:credit,debit',
            'posted_date'          => 'required|date',
            'remarks'              => 'nullable|string',
            'invoice_number'       => 'required|string|unique:purchase_invoices,invoice_number,' . $purchaseInvoice->id,
            'invoice_amount'       => 'required|numeric',
            'tax_percentage'       => 'nullable|numeric',
            'tax_amount'           => 'nullable|numeric',
            'discount_percentage'  => 'nullable|numeric',
            'discount_amount'      => 'nullable|numeric',
            'total_amount'         => 'required|numeric|min:0',
            'total_paid'           => 'nullable|numeric|min:0',

            // bank used only when we create supplier payment + bank ledger movements
            'bank_id'              => 'nullable|integer|exists:banks,id',

            'items'                                => 'required|array',
            'items.*.product_id'                   => 'required|exists:products,id',
            'items.*.pack_size'                    => 'nullable|integer',
            'items.*.batch'                        => 'nullable|string',
            'items.*.expiry'                       => 'nullable|date',
            'items.*.pack_quantity'                => 'required|numeric',
            'items.*.unit_quantity'                => 'required|integer',
            'items.*.pack_purchase_price'          => 'required|numeric',
            'items.*.unit_purchase_price'          => 'required|numeric',
            'items.*.pack_sale_price'              => 'required|numeric',
            'items.*.unit_sale_price'              => 'required|numeric',
            'items.*.whole_sale_pack_price'       => 'nullable|numeric',
            'items.*.whole_sale_unit_price'       => 'nullable|numeric',
            'items.*.whole_sale_margin'           => 'nullable|numeric',
            'items.*.item_discount_percentage'     => 'nullable|numeric|min:0',
            'items.*.pack_bonus'                   => 'nullable|numeric|min:0',
            'items.*.unit_bonus'                   => 'nullable|integer|min:0',
            'items.*.margin'                       => 'required|numeric',
            'items.*.sub_total'                    => 'required|numeric',
            'items.*.avg_price'                    => 'required|numeric',
            'items.*.quantity'                     => 'required|integer',
        ]);

        $invoiceType = $data['invoice_type'] ?? ($purchaseInvoice->invoice_type ?? 'debit');
        $totalPaid = (float) ($data['total_paid'] ?? 0);

        if ($totalPaid > 0 && empty($data['bank_id'])) {
            return response()->json(['message' => 'Bank is required to pay supplier'], 422);
        }

        try {
            return DB::transaction(function () use ($purchaseInvoice, $data, $invoiceType, $totalPaid) {
                $data['posted_number'] = $purchaseInvoice->posted_number;

                $oldInvoiceType = $purchaseInvoice->invoice_type ?? 'debit';

                $purchaseInvoice->update([
                    'supplier_id'         => $data['supplier_id'],
                    'invoice_type'        => $invoiceType,
                    'posted_number'       => $data['posted_number'],
                    'posted_date'         => $data['posted_date'],
                    'remarks'             => $data['remarks'] ?? null,
                    'invoice_number'      => $data['invoice_number'],
                    'invoice_amount'      => $data['invoice_amount'],
                    'tax_percentage'      => $data['tax_percentage'] ?? 0,
                    'tax_amount'          => $data['tax_amount'] ?? 0,
                    'discount_percentage' => $data['discount_percentage'] ?? 0,
                    'discount_amount'     => $data['discount_amount'] ?? 0,
                    'total_amount'        => $data['total_amount'],
                    'total_paid'          => $data['total_paid'] ?? 0,
                ]);

                $purchaseInvoice->load('items');

                $affectedIds = $purchaseInvoice->items->pluck('product_id')->merge(
                    collect($data['items'])->pluck('product_id')
                )->unique()->all();

                foreach ($purchaseInvoice->items as $oldItem) {
                    $this->revertItem($oldItem, false);
                }

                $purchaseInvoice->items()->delete();
                $this->processInvoiceItems($purchaseInvoice, $data['items']);
                $this->recalcProductsByIds($affectedIds);

                // Always keep supplier ledger invoice row consistent for credit.
                // For debit, this app previously created only payment rows; we keep that behavior.
                if ($invoiceType === 'credit') {
                    SupplierLedger::updateOrCreate(
                        [
                            'purchase_invoice_id' => $purchaseInvoice->id,
                            'entry_type'          => 'invoice',
                        ],
                        [
                            'supplier_id'        => $data['supplier_id'],
                            'entry_date'         => $data['posted_date'],
                            'posted_number'      => $data['posted_number'],
                            'invoice_number'     => $data['invoice_number'],
                            'invoice_total'      => $data['total_amount'],
                            'total_paid'         => $data['total_paid'] ?? 0,
                            'debited_amount'     => 0,
                            'credit_remaining'   => max((float)$data['total_amount'] - (float)($data['total_paid'] ?? 0), 0),
                            'payment_ref'        => null,
                            'description'        => 'Purchase invoice ' . $data['posted_number'],
                            'is_manual'          => false,
                            'created_by'         => optional(Auth::user())->id,
                        ]
                    );
                } elseif ($oldInvoiceType === 'credit' && $invoiceType !== 'credit') {
                    // if switching away from credit, remove invoice ledger row(s)
                    SupplierLedger::where('purchase_invoice_id', $purchaseInvoice->id)
                        ->where('entry_type', 'invoice')
                        ->delete();
                }

                // Reconcile supplier payment row + bank movement (for both credit & debit)
                $oldPaymentRows = SupplierLedger::query()
                    ->where('purchase_invoice_id', $purchaseInvoice->id)
                    ->where('entry_type', 'payment')
                    ->get();

                foreach ($oldPaymentRows as $oldPay) {
                    // delete linked bank ledger movement(s) and reverse balance
                    $linkedBankLedger = BankLedger::query()
                        ->where('ref_type', 'supplier_ledger')
                        ->where('ref_id', $oldPay->id)
                        ->get();

                    foreach ($linkedBankLedger as $bl) {
                        if ((float)$bl->amount > 0 && $bl->bank_id) {
                            // previous was debit => add back
                            $bank = Bank::find($bl->bank_id);
                            if ($bank) {
                                $bank->balance = (float)$bank->balance + (float)$bl->amount;
                                $bank->save();
                            }
                        }
                        $bl->delete();
                    }

                    $oldPay->delete();
                }

                if ($totalPaid > 0) {
                    $paymentRow = SupplierLedger::create([
                        'supplier_id'           => $data['supplier_id'],
                        'purchase_invoice_id'  => $purchaseInvoice->id,
                        'entry_type'            => 'payment',
                        'entry_date'            => $data['posted_date'],
                        'description'           => $data['remarks'] ? $data['remarks'] : ('Purchase payment ' . $data['posted_number']),
                        'posted_number'         => $data['posted_number'],
                        'invoice_number'        => $data['invoice_number'],
                        'invoice_total'         => (float)$data['total_amount'],
                        'total_paid'            => (float)($data['total_paid'] ?? 0),
                        'debited_amount'        => $totalPaid,
                        'payment_ref'           => $data['invoice_number'],
                        'bank_id'               => $data['bank_id'],
                        'credit_remaining'     => 0,
                        'is_manual'            => false,
                        'created_by'           => optional(Auth::user())->id,
                    ]);

                    BankLedger::create([
                        'bank_id'     => (int)$data['bank_id'],
                        'entry_date'  => $paymentRow->entry_date,
                        'entry_type'  => 'supplier_payment',
                        'ref_type'    => 'supplier_ledger',
                        'ref_id'      => $paymentRow->id,
                        'direction'   => 'debit',
                        'amount'      => $totalPaid,
                        'description' => $paymentRow->description,
                    ]);

                    $bank = Bank::find($data['bank_id']);
                    if ($bank) {
                        $bank->balance = (float)$bank->balance - $totalPaid;
                        $bank->save();
                    }
                }

                return response()->json($purchaseInvoice->load('items.product', 'supplier'));
            });
        } catch (\Illuminate\Database\QueryException $e) {
            return $this->respondDuplicateError($e);
        } catch (\Exception $e) {
            return response()->json([
                'message' => 'Failed to update purchase invoice',
                'error'   => $e->getMessage(),
            ], 500);
        }
    }


    // Delete invoice
    public function destroy(PurchaseInvoice $purchaseInvoice)
    {
        $this->authorize('delete', $purchaseInvoice);
        try {
            return DB::transaction(function () use ($purchaseInvoice) {
                $purchaseInvoice->load('items');

                $affectedIds = $purchaseInvoice->items->pluck('product_id')->unique()->all();

                foreach ($purchaseInvoice->items as $item) {
                    $this->revertItem($item, true);
                }

                // === Ledger reconciliation for invoice payments (supplier_ledgers + bank_ledgers + bank balances) ===
                $paymentRows = SupplierLedger::query()
                    ->where('purchase_invoice_id', $purchaseInvoice->id)
                    ->where('entry_type', 'payment')
                    ->get();

                foreach ($paymentRows as $pay) {
                    $bankMovements = BankLedger::query()
                        ->where('ref_type', 'supplier_ledger')
                        ->where('ref_id', $pay->id)
                        ->get();

                    foreach ($bankMovements as $bl) {
                        if ((float)$bl->amount > 0 && $bl->bank_id) {
                            // bank movements for supplier payments are debit => reverse by adding back
                            $bank = Bank::find($bl->bank_id);
                            if ($bank) {
                                $bank->balance = (float)$bank->balance + (float)$bl->amount;
                                $bank->save();
                            }
                        }
                        $bl->delete();
                    }

                    $pay->delete();
                }

                // delete supplier invoice ledger row(s)
                SupplierLedger::where('purchase_invoice_id', $purchaseInvoice->id)
                    ->where('entry_type', 'invoice')
                    ->delete();

                $purchaseInvoice->items()->delete();
                $purchaseInvoice->delete();

                $this->recalcProductsByIds($affectedIds);

                return response()->json(['message' => 'Invoice and related data deleted successfully']);
            });
        } catch (\Illuminate\Database\QueryException $e) {
            // Likely unique-constraint violation (invoice_number / posted_number)
            return $this->respondDuplicateError($e);
        } catch (\Exception $e) {
            return response()->json([
                'message' => 'Failed to delete invoice',
                'error'   => $e->getMessage()
            ], 500);
        }
    }


    
    private function respondDuplicateError(\Throwable $e)
    {
        // Try to infer which field caused the duplicate via constraint name/text
        $errorInfo = method_exists($e, 'errorInfo') ? ($e->errorInfo ?? []) : [];
        $message   = $errorInfo[2] ?? ($e->getMessage() ?? '');
        $mLower    = strtolower($message);

        $errors = [];

        if (str_contains($mLower, 'invoice_number')) {
            $errors['invoice_number'][] = 'Invoice number has already been taken for this supplier or globally.';
        }
        if (str_contains($mLower, 'posted_number')) {
            $errors['posted_number'][] = 'Posted number has already been assigned. Please try again.';
        }

        if (!$errors) {
            $errors['general'][] = 'Duplicate value detected.';
        }

        return response()->json([
            'message' => 'Validation failed. Please correct the highlighted fields.',
            'errors'  => $errors,
        ], 422);
    }

    private function processInvoiceItems(PurchaseInvoice $invoice, array $items): void
    {
        foreach ($items as $item) {
            $invoice->items()->create($item);

            $product = Product::find($item['product_id']);
            if ($product) {
                $product->applyPurchaseFromItem($item); 
            }

            if (!empty($item['batch']) && !empty($item['expiry'])) {
                $batch = \App\Models\Batch::firstOrNew([
                    'product_id'   => $item['product_id'],
                    'batch_number' => $item['batch'],
                    'expiry_date'  => $item['expiry'],
                ]);

                $batch->quantity = ($batch->exists ? (int) $batch->quantity : 0) + (int) $item['quantity'];
                $batch->save();
            }
        }
    }

    private function revertItem($item, bool $deleteBatch = false): void
    {
        $product = Product::find($item->product_id);
        if ($product) {
            // Pass the item ID to exclude it from the remaining purchase invoices calculation
            $itemId = isset($item->id) ? $item->id : null;
            $product->revertPurchaseFromItem($item, $itemId); // uses avg_price
        }

        if (!empty($item->batch) && !empty($item->expiry)) {
            $batch = Batch::where('product_id', $item->product_id)
                ->where('batch_number', $item->batch)
                ->where('expiry_date', $item->expiry)
                ->first();

            if ($batch) {
                if ($deleteBatch) {
                    $batch->delete();
                } else {
                    $batch->quantity = max(0, (int) $batch->quantity - (int) $item->quantity);
                    $batch->save();
                }
            }
        }
    }

    private function recalcProductAverages(Product $product): void
    {
        // Correct moving average logic: use current stock value, NOT full purchase history.
        // This ensures average resets when stock becomes 0.
        
        $currentQty = (int) ($product->quantity ?? 0);
        $currentAvg = (float) ($product->avg_price ?? 0.0);
        
        // If stock is zero or negative, the average is already reset 
        // (handled by applyPurchaseFromItem when stock was sold to zero)
        // We don't need to recalculate from history - just ensure the margins are correct
        
        // Ensure avg_price is rounded properly
        $product->avg_price = round($currentAvg, 2);

        // Retail Margin calculation
        $product->margin = ($product->unit_sale_price > 0 && $product->avg_price > 0)
            ? round((($product->unit_sale_price - $product->avg_price) / $product->unit_sale_price) * 100, 2)
            : 0.0;

        // Wholesale Margin calculation
        if ($product->whole_sale_unit_price > 0) {
            $product->whole_sale_margin = ($product->avg_price > 0)
                ? round((($product->whole_sale_unit_price - $product->avg_price) / $product->whole_sale_unit_price) * 100, 2)
                : 0.0;
        }

        $product->save();
    }

    private function recalcProductsByIds(array $productIds): void
    {
        foreach (array_unique($productIds) as $pid) {
            $product = Product::find($pid);
            if ($product) {
                $this->recalcProductAverages($product);
            }
        }
    }

    // AJAX: unique check per supplier
    public function checkUnique(Request $request)
    {
        $request->validate([
            'supplier_id'    => 'required|integer',
            'invoice_number' => 'required|string',
            'exclude_id'     => 'nullable|integer',
        ]);

        if ($request->filled('exclude_id')) {
            $invoice = PurchaseInvoice::findOrFail($request->input('exclude_id'));
            $this->authorize('update', $invoice);
        } else {
            $this->authorize('create', PurchaseInvoice::class);
        }

        $query = \App\Models\PurchaseInvoice::where('supplier_id', $request->supplier_id)
            ->where('invoice_number', $request->invoice_number);

        if ($request->filled('exclude_id')) {
            $query->where('id', '!=', $request->exclude_id);
        }

        $exists = $query->exists();

        return response()->json([
            'unique' => !$exists,
        ]);
    }

    // Search purchase invoices
    public function search(Request $request)
    {
        $this->authorize('viewAny', PurchaseInvoice::class);

        $q = trim((string) $request->query('q', ''));
        
        // Pagination parameters
        $page = (int) $request->query('page', 1);
        $perPage = (int) $request->query('per_page', 20);

        // If no search query, return recent invoices
        if (strlen($q) < 1) {
            $results = PurchaseInvoice::with(['supplier'])
                ->orderByDesc('id')
                ->paginate($perPage, ['*'], 'page', $page);

            // Add computed fields
            $results->getCollection()->transform(function ($invoice) {
                $invTotal = (float) ($invoice->total_amount ?? $invoice->invoice_amount ?? 0);
                $paid = (float) ($invoice->total_paid ?? 0);
                $invoice->remaining = max($invTotal - $paid, 0);
                $invoice->supplier_name = $invoice->supplier?->name ?? ($invoice->supplier_id ?? 'N/A');
                return $invoice;
            });

            return response()->json($results);
        }

        $query = PurchaseInvoice::with(['supplier'])
            ->orderByDesc('id');

        // Search by posted_number, invoice_number, or supplier name
        $query->where(function ($sub) use ($q) {
            $sub->where('posted_number', 'like', '%' . $q . '%')
                ->orWhere('invoice_number', 'like', '%' . $q . '%')
                ->orWhereHas('supplier', function ($supplierQuery) use ($q) {
                    $supplierQuery->where('name', 'like', '%' . $q . '%');
                });
        });

        $results = $query->paginate($perPage, ['*'], 'page', $page);

        // Add computed fields
        $results->getCollection()->transform(function ($invoice) {
            $invTotal = (float) ($invoice->total_amount ?? $invoice->invoice_amount ?? 0);
            $paid = (float) ($invoice->total_paid ?? 0);
            $invoice->remaining = max($invTotal - $paid, 0);
            $invoice->supplier_name = $invoice->supplier?->name ?? ($invoice->supplier_id ?? 'N/A');
            return $invoice;
        });

        return response()->json($results);
    }
}
