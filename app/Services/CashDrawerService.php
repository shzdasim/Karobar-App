<?php

namespace App\Services;

use App\Models\CashDrawerSession;
use App\Models\CashDrawerTransaction;
use App\Models\User;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use Illuminate\Validation\ValidationException;

class CashDrawerService
{
    /**
     * Cash-in transaction types that increase drawer balance.
     */
    public const CASH_IN_TYPES = [
        'cash_sale',
        'customer_payment',
        'cash_in',
    ];

    /**
     * Cash-out transaction types that decrease drawer balance.
     */
    public const CASH_OUT_TYPES = [
        'cash_purchase',
        'cash_return',
        'supplier_payment',
        'cash_out',
    ];

    /**
     * Get currently active open session for a user or system.
     */
    public function getCurrentSession(?User $user = null): ?CashDrawerSession
    {
        if (!Schema::hasTable('cash_drawer_sessions')) {
            return null;
        }

        $query = CashDrawerSession::with(['transactions.user', 'user'])
            ->where('status', 'open');

        if ($user) {
            $userSession = (clone $query)->where('user_id', $user->id)->latest('opened_at')->first();
            if ($userSession) {
                return $userSession;
            }
        }

        // Fallback to the latest active open session
        return $query->latest('opened_at')->first();
    }

    /**
     * Open a new cash drawer session.
     */
    public function openSession(User $user, float $openingBalance, ?string $notes = null): CashDrawerSession
    {
        return DB::transaction(function () use ($user, $openingBalance, $notes) {
            $existing = CashDrawerSession::where('user_id', $user->id)
                ->where('status', 'open')
                ->lockForUpdate()
                ->first();

            if ($existing) {
                throw ValidationException::withMessages([
                    'session' => 'A cash drawer session is already open for this user. Please close it first.',
                ]);
            }

            $opening = max(round($openingBalance, 2), 0);

            $session = CashDrawerSession::create([
                'user_id'                  => $user->id,
                'opened_at'                => Carbon::now(),
                'status'                   => 'open',
                'opening_balance'          => $opening,
                'closing_balance_expected' => $opening,
                'closing_balance_actual'   => null,
                'difference'               => 0,
                'notes'                    => $notes,
            ]);

            return $session->load('user');
        });
    }

    /**
     * Record a transaction in an active drawer session.
     */
    public function recordTransaction(
        ?CashDrawerSession $session,
        string $type,
        float $amount,
        ?string $refType = null,
        ?int $refId = null,
        ?string $notes = null,
        ?User $user = null
    ): ?CashDrawerTransaction {
        if (!Schema::hasTable('cash_drawer_sessions') || !Schema::hasTable('cash_drawer_transactions')) {
            return null;
        }

        if (!$session) {
            $session = $this->getCurrentSession($user);
        }

        if (!$session || $session->status !== 'open') {
            return null;
        }

        return DB::transaction(function () use ($session, $type, $amount, $refType, $refId, $notes, $user) {
            $lockedSession = CashDrawerSession::lockForUpdate()->find($session->id);
            if (!$lockedSession || $lockedSession->status !== 'open') {
                return null;
            }

            $amount = round(abs($amount), 2);
            if ($amount <= 0) {
                return null;
            }

            $txn = CashDrawerTransaction::create([
                'session_id'     => $lockedSession->id,
                'user_id'        => $user?->id ?? $lockedSession->user_id,
                'type'           => $type,
                'amount'         => $amount,
                'reference_type' => $refType,
                'reference_id'   => $refId,
                'notes'          => $notes,
            ]);

            $this->recalculateExpectedBalance($lockedSession);

            return $txn;
        });
    }

    /**
     * Recalculate and update the expected cash balance in drawer.
     */
    public function recalculateExpectedBalance(CashDrawerSession $session): float
    {
        $cashIn = (float) CashDrawerTransaction::where('session_id', $session->id)
            ->whereIn('type', self::CASH_IN_TYPES)
            ->sum('amount');

        $cashOut = (float) CashDrawerTransaction::where('session_id', $session->id)
            ->whereIn('type', self::CASH_OUT_TYPES)
            ->sum('amount');

        $expected = round((float) $session->opening_balance + $cashIn - $cashOut, 2);

        $session->closing_balance_expected = $expected;
        $session->save();

        return $expected;
    }

    /**
     * Close a drawer session with cash count reconciliation.
     */
    public function closeSession(CashDrawerSession $session, float $actualCount, ?string $notes = null): CashDrawerSession
    {
        return DB::transaction(function () use ($session, $actualCount, $notes) {
            $lockedSession = CashDrawerSession::lockForUpdate()->findOrFail($session->id);

            if ($lockedSession->status !== 'open') {
                throw ValidationException::withMessages([
                    'session' => 'This cash drawer session has already been closed.',
                ]);
            }

            $expected = $this->recalculateExpectedBalance($lockedSession);
            $actual = round($actualCount, 2);
            $difference = round($actual - $expected, 2);

            $combinedNotes = trim(($lockedSession->notes ? $lockedSession->notes . "\n" : '') . ($notes ?? ''));

            $lockedSession->update([
                'status'                   => 'closed',
                'closed_at'                => Carbon::now(),
                'closing_balance_expected' => $expected,
                'closing_balance_actual'   => $actual,
                'difference'               => $difference,
                'notes'                    => $combinedNotes ?: null,
            ]);

            return $lockedSession->fresh(['transactions.user', 'user']);
        });
    }

    /**
     * Get computed breakdown statistics for a drawer session.
     */
    public function getSessionSummary(CashDrawerSession $session): array
    {
        $transactions = CashDrawerTransaction::where('session_id', $session->id)->get();

        $cashSales = (float) $transactions->where('type', 'cash_sale')->sum('amount');
        $cashReturns = (float) $transactions->where('type', 'cash_return')->sum('amount');
        $cashPurchases = (float) $transactions->where('type', 'cash_purchase')->sum('amount');
        $customerPayments = (float) $transactions->where('type', 'customer_payment')->sum('amount');
        $supplierPayments = (float) $transactions->where('type', 'supplier_payment')->sum('amount');
        $cashIn = (float) $transactions->where('type', 'cash_in')->sum('amount');
        $cashOut = (float) $transactions->where('type', 'cash_out')->sum('amount');

        $totalCashIn = $cashSales + $customerPayments + $cashIn;
        $totalCashOut = $cashPurchases + $cashReturns + $supplierPayments + $cashOut;
        $expected = round((float) $session->opening_balance + $totalCashIn - $totalCashOut, 2);

        return [
            'session_id'        => $session->id,
            'status'            => $session->status,
            'opened_at'         => $session->opened_at,
            'closed_at'         => $session->closed_at,
            'user'              => $session->user ? ['id' => $session->user->id, 'name' => $session->user->name] : null,
            'opening_balance'   => (float) $session->opening_balance,
            'cash_sales'        => $cashSales,
            'cash_returns'      => $cashReturns,
            'cash_purchases'    => $cashPurchases,
            'customer_payments' => $customerPayments,
            'supplier_payments' => $supplierPayments,
            'manual_cash_in'    => $cashIn,
            'manual_cash_out'   => $cashOut,
            'total_cash_in'     => $totalCashIn,
            'total_cash_out'    => $totalCashOut,
            'expected_balance'  => $expected,
            'actual_balance'    => $session->closing_balance_actual,
            'difference'        => $session->difference,
            'notes'             => $session->notes,
            'transactions_count'=> $transactions->count(),
        ];
    }
}
