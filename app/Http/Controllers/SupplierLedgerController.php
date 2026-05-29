<?php

namespace App\Http\Controllers;

use App\Models\SupplierLedger;
use App\Models\PurchaseInvoice;
use App\Models\Setting;
use App\Models\Supplier;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class SupplierLedgerController extends Controller
{
    // GET /api/supplier-ledger?supplier_id=..&from=..&to=..
    public function index(Request $request)
    {
        $this->authorize('viewAny', SupplierLedger::class);

        $validated = $request->validate([
            'supplier_id' => ['required','integer','exists:suppliers,id'],
            'from'        => ['nullable','date'],
            'to'          => ['nullable','date'],
        ]);

        $q = SupplierLedger::with(['purchaseInvoice:id,invoice_number,posted_number'])
            ->where('supplier_id', $validated['supplier_id']);

        if ($request->filled('from')) $q->whereDate('entry_date', '>=', $request->get('from'));
        if ($request->filled('to'))   $q->whereDate('entry_date', '<=', $request->get('to'));

        $rows = $q->orderBy('entry_date')->orderBy('id')->get();

        $invoiceRows = $rows->where('entry_type', 'invoice');
        $paymentRows = $rows->where('entry_type', 'payment');

        $summary = [
            'total_invoiced'   => $invoiceRows->sum('invoice_total'),
            'paid_on_invoice'  => $invoiceRows->sum('total_paid'),
            'payments_debited' => $paymentRows->sum('debited_amount'),
            'net_balance'      => ($invoiceRows->sum('invoice_total'))
                                - ($invoiceRows->sum('total_paid') + $paymentRows->sum('debited_amount')),
        ];

        return response()->json([
            'data'    => $rows->values(),
            'summary' => $summary,
        ]);
    }

    // POST /api/supplier-ledger   (create manual row OR a payment row)
    // Recognize "payment" if debited_amount > 0 and no purchase_invoice_id.
    public function store(Request $request)
    {
        $this->authorize('create', SupplierLedger::class);

        $validated = $request->validate([
            'supplier_id'        => ['required','integer','exists:suppliers,id'],
            'entry_date'         => ['required','date'],
            'description'        => ['nullable','string','max:255'],
            'posted_number'      => ['nullable','string','max:191'],
            'invoice_number'     => ['nullable','string','max:191'],

            // invoice snapshot (for manual invoice-like rows if you ever want)
            'invoice_total'      => ['nullable','numeric','min:0'],
            'total_paid'         => ['nullable','numeric','min:0'],

            // payment
            'debited_amount'     => ['nullable','numeric','min:0'],
            'payment_ref'        => ['nullable','string','max:191'],
            'bank_id'            => ['nullable','integer','exists:banks,id'],

            // tie to invoice if needed
            'purchase_invoice_id'=> ['nullable','integer','exists:purchase_invoices,id'],
        ]);

        $isPayment = ($validated['debited_amount'] ?? 0) > 0 && empty($validated['purchase_invoice_id']);
        $entryType = $isPayment ? 'payment' : ($validated['purchase_invoice_id'] ? 'invoice' : 'manual');

        $invoiceTotal = (float)($validated['invoice_total'] ?? 0);
        $totalPaid    = (float)($validated['total_paid'] ?? 0);
        $credit       = round($invoiceTotal - $totalPaid, 2);
        if ($credit < 0) $credit = 0;

        $row = SupplierLedger::create([
            'supplier_id'         => $validated['supplier_id'],
            'purchase_invoice_id' => $validated['purchase_invoice_id'] ?? null,
            'entry_type'          => $entryType,
            'entry_date'          => $validated['entry_date'],
            'description'         => $validated['description'] ?? null,
            'posted_number'       => $validated['posted_number'] ?? null,
            'invoice_number'      => $validated['invoice_number'] ?? null,
            'invoice_total'       => $invoiceTotal,
            'total_paid'          => $totalPaid,
            'debited_amount'      => (float)($validated['debited_amount'] ?? 0),
            'payment_ref'         => $validated['payment_ref'] ?? null,
            'bank_id'             => $validated['bank_id'] ?? null,
            'credit_remaining'    => $credit,
            'is_manual'           => $entryType !== 'invoice',
            'created_by'          => optional($request->user())->id,
        ]);

        // Create corresponding bank movement for supplier payments
        if ($row->entry_type === 'payment' && $row->bank_id) {
            $amount = (float)($row->debited_amount ?? 0);
            if ($amount > 0) {
                $direction = 'debit'; // money leaving bank

                \App\Models\BankLedger::create([
                    'bank_id'     => (int)$row->bank_id,
                    'entry_date'  => $row->entry_date,
                    'entry_type'  => 'supplier_payment',
                    // Tie bank movement to the supplier_ledger row so bulk edits/rebuild can reconcile safely
                    'ref_type'    => 'supplier_ledger',
                    'ref_id'      => $row->id,

                    'direction'   => $direction,
                    'amount'      => $amount,
                    'description' => $row->description,
                ]);

                $bank = \App\Models\Bank::find($row->bank_id);
                if ($bank) {
                    $bank->balance = (float)$bank->balance - $amount;
                    $bank->save();
                }
            }
        }

        return response()->json(['data' => $row], 201);
    }

    // PUT /api/supplier-ledger/bulk
    // rows: [{id, entry_date, description, posted_number, invoice_number, invoice_total, total_paid, debited_amount, payment_ref}]
    public function bulkUpdate(Request $request)
    {
        $this->authorize('updateAny', SupplierLedger::class);

        $validated = $request->validate([
            'rows'                        => ['required','array','min:1'],
            'rows.*.id'                   => ['required','integer','exists:supplier_ledgers,id'],
            'rows.*.entry_date'           => ['required','date'],
            'rows.*.description'          => ['nullable','string','max:255'],
            'rows.*.posted_number'        => ['nullable','string','max:191'],
            'rows.*.invoice_number'       => ['nullable','string','max:191'],
            'rows.*.invoice_total'        => ['required','numeric','min:0'],
            'rows.*.total_paid'           => ['required','numeric','min:0'],
            'rows.*.debited_amount'       => ['nullable','numeric','min:0'],
            'rows.*.payment_ref'          => ['nullable','string','max:191'],
            'rows.*.bank_id'              => ['nullable','integer','exists:banks,id'],
        ]);

        DB::transaction(function () use ($validated) {
            foreach ($validated['rows'] as $r) {
                $row = SupplierLedger::findOrFail($r['id']);

                $credit = round(($r['invoice_total'] ?? 0) - ($r['total_paid'] ?? 0), 2);
                if ($credit < 0) $credit = 0;

                // Capture old payment state for bank reconciliation
                $oldBankId = $row->bank_id;
                $oldAmount = (float)($row->debited_amount ?? 0);

                // Update ledger row
                $row->entry_date       = $r['entry_date'];
                $row->description      = $r['description'] ?? null;
                $row->posted_number    = $r['posted_number'] ?? null;
                $row->invoice_number   = $r['invoice_number'] ?? null;
                $row->invoice_total    = $r['invoice_total'] ?? 0;
                $row->total_paid       = $r['total_paid'] ?? 0;
                $row->debited_amount   = (float)($r['debited_amount'] ?? 0);
                $row->payment_ref      = $r['payment_ref'] ?? null;
                $row->bank_id           = $r['bank_id'] ?? null;
                $row->credit_remaining = $credit;

                $row->save();

                // Reconcile BankLedger for edited supplier payment rows
                if ($row->entry_type === 'payment') {
                    $newBankId = $row->bank_id;
                    $newAmount = (float)($row->debited_amount ?? 0);
                    $newEntryDate = $row->entry_date;
                    $newDescription = $row->description;

                    // Remove old bank movement + reverse old balance
                    if ($oldBankId && $oldAmount > 0) {
                        \App\Models\BankLedger::query()
                            ->where('ref_type', 'supplier_ledger')
                            ->where('ref_id', $row->id)
                            ->where('bank_id', (int)$oldBankId)
                            ->delete();

                        $oldBank = \App\Models\Bank::find($oldBankId);
                        if ($oldBank) {
                            $oldBank->balance = (float)$oldBank->balance + $oldAmount; // undo debit
                            $oldBank->save();
                        }
                    }

                    // Create new bank movement + apply balance
                    if ($newBankId && $newAmount > 0) {
                        \App\Models\BankLedger::create([
                            'bank_id'     => (int)$newBankId,
                            'entry_date'  => $newEntryDate,
                            'entry_type'  => 'supplier_payment',
                            'ref_type'    => 'supplier_ledger',
                            'ref_id'      => $row->id,
                            'direction'   => 'debit',
                            'amount'      => $newAmount,
                            'description' => $newDescription,
                        ]);

                        $newBank = \App\Models\Bank::find($newBankId);
                        if ($newBank) {
                            $newBank->balance = (float)$newBank->balance - $newAmount; // debit
                            $newBank->save();
                        }
                    }
                }
            }
        });

        return response()->json(['status' => 'ok']);
    }

    // DELETE /api/supplier-ledger/{id}
    public function destroy($id)
    {
        $row = SupplierLedger::findOrFail($id);
        $this->authorize('delete', $row);

        if (!$row->is_manual && $row->entry_type === 'invoice') {
            return response()->json(['message' => 'Cannot delete auto-synced invoice row.'], 422);
        }
        $row->delete();
        return response()->json(['status' => 'ok']);
    }

    // POST /api/supplier-ledger/rebuild {supplier_id}
    public function rebuild(Request $request)
    {
        $this->authorize('updateAny', SupplierLedger::class);

        $validated = $request->validate([
            'supplier_id' => ['required','integer','exists:suppliers,id'],
        ]);

        $invoices = PurchaseInvoice::query()
            ->select('id','supplier_id','invoice_number','posted_number','invoice_date','total_amount','total_paid')
            ->where('supplier_id', $validated['supplier_id'])
            ->get();

        DB::transaction(function () use ($invoices) {
            foreach ($invoices as $inv) {
                $credit = round(($inv->total_amount ?? 0) - ($inv->total_paid ?? 0), 2);
                if ($credit < 0) $credit = 0;

                // Rebuild only affects invoice rows.
                // Payment/manual rows (which carry bank_id + bank_ledgers refs) must remain untouched.
                SupplierLedger::updateOrCreate(
                    [
                        'supplier_id'         => $inv->supplier_id,
                        'purchase_invoice_id' => $inv->id,
                        'entry_type'          => 'invoice',
                    ],
                    [
                        'entry_date'       => $inv->invoice_date ?? now()->toDateString(),
                        'description'      => 'Invoice #'.$inv->invoice_number,
                        'posted_number'    => $inv->posted_number,
                        'invoice_number'   => $inv->invoice_number,
                        'invoice_total'    => $inv->total_amount ?? 0,
                        'total_paid'       => $inv->total_paid ?? 0,
                        'debited_amount'   => 0, // not a payment row
                        'credit_remaining' => $credit,
                        'is_manual'        => false,
                        'bank_id'          => null,
                        'payment_ref'      => null,
                    ]
                );
            }
        });

        return response()->json(['status' => 'ok', 'count' => $invoices->count()]);
    }

    public function print(Request $request)
    {

        $supplierId = (int) $request->query('supplier_id');
        abort_if(!$supplierId, 404, 'Supplier is required');

        $supplier = Supplier::findOrFail($supplierId);
        $setting  = Setting::first();

        $type = strtolower($request->query('type', $setting->printer_type ?? 'a4'));
        if (!in_array($type, ['a4', 'thermal'])) {
            $type = 'a4';
        }

        $from = $request->query('from');
        $to   = $request->query('to');

        $rows = SupplierLedger::query()
            ->where('supplier_id', $supplier->id)
            ->when($from, fn($q) => $q->whereDate('entry_date', '>=', $from))
            ->when($to,   fn($q) => $q->whereDate('entry_date', '<=', $to))
            ->orderBy('entry_date')
            ->orderBy('id')
            ->get();

        $balance = 0;
        $mapped = $rows->map(function ($r) use (&$balance) {
            $invoiceTotal = (float) ($r->invoice_total ?? 0);
            $paidOnInv    = (float) ($r->total_paid ?? 0);
            $payment      = (float) ($r->debited_amount ?? 0);
            $type         = $r->entry_type;

            $creditRemaining = 0.0;
            if (in_array($type, ['invoice', 'manual'])) {
                $creditRemaining = max(0, round($invoiceTotal - $paidOnInv, 2));
                $balance += ($invoiceTotal - $paidOnInv);
            }
            if ($type === 'payment') {
                $balance -= $payment;
            }

            $r->credit_remaining_calc = $creditRemaining;
            $r->running_balance = round($balance, 2);
            return $r;
        });

        $summary = [
            'total_invoiced'    => (float) $mapped->sum('invoice_total'),
            'paid_on_invoice'   => (float) $mapped->sum('total_paid'),
            'payments_debited'  => (float) $mapped->sum('debited_amount'),
            'net_balance'       => round(
                ($mapped->sum('invoice_total') - $mapped->sum('total_paid')) - $mapped->sum('debited_amount'),
                2
            ),
        ];

        return view("printer.supplier_ledger_{$type}", [
            'supplier' => $supplier,
            'setting'  => $setting,
            'rows'     => $mapped,
            'summary'  => $summary,
            'from'     => $from,
            'to'       => $to,
        ]);
    }
}
