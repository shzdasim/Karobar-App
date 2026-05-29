<?php

namespace App\Http\Controllers;

use App\Models\BankLedger;
use Illuminate\Http\Request;


class BankLedgerController extends Controller
{
    // GET /api/bank-ledger?bank_id=&from=&to=
    // Returns: { data: [...], summary: { total_in, total_out, net, from_to } }
    public function index(Request $request)
    {
        $this->authorize('viewAny', BankLedger::class);

        $validated = $request->validate([
            'bank_id' => ['nullable','integer','exists:banks,id'],
            'from'    => ['nullable','date'],
            'to'      => ['nullable','date'],
        ]);

        $q = BankLedger::query();

        if (!empty($validated['bank_id'])) {
            $q->where('bank_id', (int)$validated['bank_id']);
        }

        if (!empty($validated['from'])) {
            $q->whereDate('entry_date', '>=', $validated['from']);
        }

        if (!empty($validated['to'])) {
            $q->whereDate('entry_date', '<=', $validated['to']);
        }

        $rows = $q->with(['bank'])
            ->orderBy('entry_date','asc')
            ->orderBy('id','asc')
            ->get();

        $totalIn = (float) $rows->where('direction', 'credit')->sum('amount');
        $totalOut = (float) $rows->where('direction', 'debit')->sum('amount');
        $net = $totalIn - $totalOut;

        return response()->json([
            'data' => $rows->map(function (BankLedger $r) {
                return [
                    'id' => $r->id,
                    'bank_id' => $r->bank_id,
                    'bank' => $r->bank ? [
                        'bank_name' => $r->bank->bank_name,
                        'account_number' => $r->bank->account_number,
                    ] : null,
                    'entry_date' => optional($r->entry_date)->format('Y-m-d'),
                    'entry_type' => $r->entry_type,
                    'ref_type' => $r->ref_type,
                    'ref_id' => $r->ref_id,
                    'direction' => $r->direction,
                    'amount' => (float) $r->amount,
                    'description' => $r->description,
                ];
            }),
            'summary' => [
                'total_in' => round($totalIn, 2),
                'total_out' => round($totalOut, 2),
                'net' => round($net, 2),
                'from' => $validated['from'] ?? null,
                'to' => $validated['to'] ?? null,
                'bank_id' => $validated['bank_id'] ?? null,
            ],
        ]);
    }
}

