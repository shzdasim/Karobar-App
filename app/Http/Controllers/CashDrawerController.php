<?php

namespace App\Http\Controllers;

use App\Models\CashDrawerSession;
use App\Services\CashDrawerService;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;

class CashDrawerController extends Controller
{
    protected CashDrawerService $cashDrawerService;

    public function __construct(CashDrawerService $cashDrawerService)
    {
        $this->cashDrawerService = $cashDrawerService;
    }

    /**
     * Get the active drawer session for the logged-in user / store.
     */
    public function current(Request $request)
    {
        $session = $this->cashDrawerService->getCurrentSession($request->user());

        if (!$session) {
            return response()->json([
                'is_open' => false,
                'session' => null,
                'summary' => null,
            ]);
        }

        $session->load(['transactions' => function ($q) {
            $q->latest()->limit(50);
        }, 'user']);

        return response()->json([
            'is_open' => true,
            'session' => $session,
            'summary' => $this->cashDrawerService->getSessionSummary($session),
        ]);
    }

    /**
     * Open a new cash drawer session.
     */
    public function open(Request $request)
    {
        $data = $request->validate([
            'opening_balance' => 'required|numeric|min:0',
            'notes'           => 'nullable|string|max:500',
        ]);

        $session = $this->cashDrawerService->openSession(
            $request->user(),
            (float) $data['opening_balance'],
            $data['notes'] ?? null
        );

        return response()->json([
            'message' => 'Cash drawer opened successfully.',
            'session' => $session,
            'summary' => $this->cashDrawerService->getSessionSummary($session),
        ], 201);
    }

    /**
     * Record a manual cash in or cash out.
     */
    public function recordTransaction(Request $request)
    {
        $data = $request->validate([
            'type'   => 'required|in:cash_in,cash_out',
            'amount' => 'required|numeric|min:0.01',
            'notes'  => 'required|string|max:255',
        ]);

        $session = $this->cashDrawerService->getCurrentSession($request->user());

        if (!$session) {
            return response()->json([
                'message' => 'No active cash drawer is open. Please open the drawer first.',
            ], 422);
        }

        $txn = $this->cashDrawerService->recordTransaction(
            $session,
            $data['type'],
            (float) $data['amount'],
            'manual',
            null,
            $data['notes'],
            $request->user()
        );

        return response()->json([
            'message'     => ($data['type'] === 'cash_in' ? 'Cash in' : 'Cash out') . ' recorded successfully.',
            'transaction' => $txn,
            'summary'     => $this->cashDrawerService->getSessionSummary($session->fresh()),
        ]);
    }

    /**
     * Close the current drawer session and reconcile.
     */
    public function close(Request $request)
    {
        $data = $request->validate([
            'closing_balance_actual' => 'required|numeric|min:0',
            'notes'                  => 'nullable|string|max:500',
        ]);

        $session = $this->cashDrawerService->getCurrentSession($request->user());

        if (!$session) {
            return response()->json([
                'message' => 'No active cash drawer session is open to close.',
            ], 422);
        }

        $closed = $this->cashDrawerService->closeSession(
            $session,
            (float) $data['closing_balance_actual'],
            $data['notes'] ?? null
        );

        return response()->json([
            'message' => 'Cash drawer closed successfully.',
            'session' => $closed,
            'summary' => $this->cashDrawerService->getSessionSummary($closed),
        ]);
    }

    /**
     * Get historical cash drawer sessions for auditing.
     */
    public function history(Request $request)
    {
        $limit = min(max((int) $request->query('per_page', 15), 1), 100);

        $sessions = CashDrawerSession::with(['user'])
            ->withCount('transactions')
            ->orderBy('id', 'desc')
            ->paginate($limit);

        return response()->json($sessions);
    }
}

