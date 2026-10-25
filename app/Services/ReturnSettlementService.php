<?php

namespace App\Services;

use App\Models\Account;
use App\Models\AccountTransaction;
use App\Models\CustomerLedger;
use App\Models\PurchaseInvoice;
use App\Models\PurchaseReturn;
use App\Models\SaleInvoice;
use App\Models\SaleReturn;
use App\Models\SupplierLedger;
use App\Models\User;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

class ReturnSettlementService
{
    public const VALID_MODES = ['cash_refund', 'credit_note', 'invoice_offset'];

    protected CashDrawerService $cashDrawerService;
    protected AccountService $accountService;

    public function __construct(CashDrawerService $cashDrawerService, AccountService $accountService)
    {
        $this->cashDrawerService = $cashDrawerService;
        $this->accountService = $accountService;
    }

    /**
     * Resolve the effective settlement mode for a Sale Return.
     * If no mode is explicitly requested, auto-select 'credit_note' when the source SaleInvoice is a credit invoice.
     */
    public function resolveSaleSettlementMode(
        ?string $requestedMode,
        ?int $saleInvoiceId = null,
        ?string $fallbackMode = null
    ): string {
        if ($requestedMode && in_array($requestedMode, self::VALID_MODES, true)) {
            return $requestedMode;
        }

        if ($fallbackMode && in_array($fallbackMode, self::VALID_MODES, true)) {
            return $fallbackMode;
        }

        if ($saleInvoiceId && Schema::hasTable('sale_invoices') && Schema::hasColumn('sale_invoices', 'invoice_type')) {
            $invoiceType = SaleInvoice::where('id', $saleInvoiceId)->value('invoice_type');
            if ($invoiceType === 'credit') {
                return 'credit_note';
            }
        }

        return 'cash_refund';
    }

    /**
     * Resolve the effective settlement mode for a Purchase Return.
     * If no mode is explicitly requested, auto-select 'credit_note' when the source PurchaseInvoice is a credit invoice.
     */
    public function resolvePurchaseSettlementMode(
        ?string $requestedMode,
        ?int $purchaseInvoiceId = null,
        ?string $fallbackMode = null
    ): string {
        if ($requestedMode && in_array($requestedMode, self::VALID_MODES, true)) {
            return $requestedMode;
        }

        if ($fallbackMode && in_array($fallbackMode, self::VALID_MODES, true)) {
            return $fallbackMode;
        }

        if ($purchaseInvoiceId && Schema::hasTable('purchase_invoices') && Schema::hasColumn('purchase_invoices', 'invoice_type')) {
            $invoiceType = PurchaseInvoice::where('id', $purchaseInvoiceId)->value('invoice_type');
            if ($invoiceType === 'credit') {
                return 'credit_note';
            }
        }

        return 'cash_refund';
    }

    /**
     * Safely persist settlement fields on SaleReturn if columns exist in schema.
     */
    protected function persistSaleReturnSettlementFields(SaleReturn $saleReturn, array $fields): void
    {
        $updates = [];
        foreach ($fields as $col => $val) {
            $saleReturn->{$col} = $val;
            if (Schema::hasColumn('sale_returns', $col)) {
                $updates[$col] = $val;
            }
        }
        if (!empty($updates)) {
            SaleReturn::where('id', $saleReturn->id)->update($updates);
        }
    }

    /**
     * Safely persist settlement fields on PurchaseReturn if columns exist in schema.
     */
    protected function persistPurchaseReturnSettlementFields(PurchaseReturn $purchaseReturn, array $fields): void
    {
        $updates = [];
        foreach ($fields as $col => $val) {
            $purchaseReturn->{$col} = $val;
            if (Schema::hasColumn('purchase_returns', $col)) {
                $updates[$col] = $val;
            }
        }
        if (!empty($updates)) {
            PurchaseReturn::where('id', $purchaseReturn->id)->update($updates);
        }
    }

    /**
     * Apply financial / ledger settlement when a Sale Return is created.
     */
    public function applySaleReturnSettlement(SaleReturn $saleReturn, ?User $user = null): void
    {
        DB::transaction(function () use ($saleReturn, $user) {
            $mode = $saleReturn->settlement_mode ?: 'cash_refund';
            $amount = round((float) ($saleReturn->total ?? 0), 2);

            if (in_array($mode, ['credit_note', 'invoice_offset'], true)) {
                $this->persistSaleReturnSettlementFields($saleReturn, [
                    'settlement_mode' => $mode,
                    'credited_amount' => $amount,
                    'account_id'      => null,
                ]);

                $this->syncCustomerLedgerForSaleReturn($saleReturn, $amount, $mode, $user);
                return;
            }

            // cash_refund mode
            $this->deleteCustomerLedgerForSaleReturn($saleReturn->id);

            $payMethod = $saleReturn->payment_method ?: 'cash';
            $resolvedAccountId = $saleReturn->account_id ? (int) $saleReturn->account_id : null;

            if ($amount > 0) {
                $account = $this->accountService->resolveAccount($resolvedAccountId, $payMethod);

                if ($account) {
                    $resolvedAccountId = $account->id;

                    $this->accountService->withdraw(
                        $account,
                        $amount,
                        'sale_return',
                        $saleReturn->id,
                        "Customer refund for sale return #{$saleReturn->posted_number}",
                        $user
                    );
                }
            }

            $this->persistSaleReturnSettlementFields($saleReturn, [
                'settlement_mode' => 'cash_refund',
                'payment_method'  => $payMethod,
                'account_id'      => $resolvedAccountId,
                'credited_amount' => 0,
            ]);

            if ($payMethod === 'cash' && $amount > 0) {
                $this->cashDrawerService->handleSaleReturnCreation($saleReturn, $user);
            }
        });
    }

    /**
     * Synchronize financial / ledger settlement when a Sale Return is updated.
     */
    public function syncSaleReturnSettlementUpdate(
        SaleReturn $saleReturn,
        array $oldSnapshot,
        ?bool $updateDrawerIfPreShift = null,
        ?User $user = null
    ): void {
        DB::transaction(function () use ($saleReturn, $oldSnapshot, $updateDrawerIfPreShift, $user) {
            $oldMode = $oldSnapshot['settlement_mode'] ?? 'cash_refund';
            $oldPayMethod = $oldSnapshot['payment_method'] ?? 'cash';
            $oldTotal = round((float) ($oldSnapshot['total'] ?? 0), 2);

            $newMode = $saleReturn->settlement_mode ?: 'cash_refund';
            $newPayMethod = $saleReturn->payment_method ?: 'cash';
            $newTotal = round((float) ($saleReturn->total ?? 0), 2);

            // 1. Sync Financial Account transaction
            $existingAccTxn = Schema::hasTable('account_transactions')
                ? AccountTransaction::where('reference_type', 'sale_return')
                    ->where('reference_id', $saleReturn->id)
                    ->latest('id')
                    ->first()
                : null;

            if ($newMode === 'cash_refund' && $newTotal > 0) {
                $explicitAccountId = ($newPayMethod !== $oldPayMethod && (int) ($saleReturn->account_id ?? 0) === (int) ($oldSnapshot['account_id'] ?? 0))
                    ? null
                    : ($saleReturn->account_id ? (int) $saleReturn->account_id : null);
                $targetAccount = $this->accountService->resolveAccount(
                    $explicitAccountId,
                    $newPayMethod
                );
                $resolvedAccountId = $explicitAccountId;

                $dateStr = $saleReturn->date instanceof \Carbon\Carbon
                    ? $saleReturn->date->toDateString()
                    : (string) $saleReturn->date;

                if ($existingAccTxn) {
                    $oldAcc = Account::lockForUpdate()->find($existingAccTxn->account_id);
                    if ($oldAcc) {
                        $oldAcc->current_balance = round((float) $oldAcc->current_balance + (float) $existingAccTxn->amount, 2);
                        $oldAcc->save();
                    }

                    if ($targetAccount) {
                        $lockedTarget = Account::lockForUpdate()->find($targetAccount->id);
                        if ($lockedTarget) {
                            $lockedTarget->current_balance = round((float) $lockedTarget->current_balance - $newTotal, 2);
                            $lockedTarget->save();

                            $existingAccTxn->account_id = $lockedTarget->id;
                            $existingAccTxn->amount = $newTotal;
                            $existingAccTxn->balance_after = $lockedTarget->current_balance;
                            $existingAccTxn->transaction_date = $dateStr;
                            $existingAccTxn->description = "Customer refund for sale return #{$saleReturn->posted_number}";
                            $existingAccTxn->save();

                            $resolvedAccountId = $lockedTarget->id;
                        }
                    } else {
                        $existingAccTxn->delete();
                        $resolvedAccountId = null;
                    }
                } elseif ($targetAccount) {
                    $resolvedAccountId = $targetAccount->id;
                    $this->accountService->withdraw(
                        $targetAccount,
                        $newTotal,
                        'sale_return',
                        $saleReturn->id,
                        "Customer refund for sale return #{$saleReturn->posted_number}",
                        $user
                    );
                }

                $this->persistSaleReturnSettlementFields($saleReturn, [
                    'settlement_mode' => 'cash_refund',
                    'payment_method'  => $newPayMethod,
                    'account_id'      => $resolvedAccountId,
                    'credited_amount' => 0,
                ]);

                $this->deleteCustomerLedgerForSaleReturn($saleReturn->id);
            } else {
                // Switched to credit_note / invoice_offset or 0 total: unwind any previous account withdrawal
                if ($existingAccTxn) {
                    $oldAcc = Account::lockForUpdate()->find($existingAccTxn->account_id);
                    if ($oldAcc) {
                        $oldAcc->current_balance = round((float) $oldAcc->current_balance + (float) $existingAccTxn->amount, 2);
                        $oldAcc->save();
                    }
                    $existingAccTxn->delete();
                }

                $credited = in_array($newMode, ['credit_note', 'invoice_offset'], true) ? $newTotal : 0;
                $this->persistSaleReturnSettlementFields($saleReturn, [
                    'settlement_mode' => $newMode,
                    'account_id'      => null,
                    'credited_amount' => $credited,
                ]);

                if (in_array($newMode, ['credit_note', 'invoice_offset'], true)) {
                    $this->syncCustomerLedgerForSaleReturn($saleReturn, $newTotal, $newMode, $user);
                } else {
                    $this->deleteCustomerLedgerForSaleReturn($saleReturn->id);
                }
            }

            // 2. Sync Cash Drawer based on effective physical cash impact before vs after
            $oldCash = ($oldMode === 'cash_refund' && $oldPayMethod === 'cash') ? $oldTotal : 0.0;
            $newCash = ($newMode === 'cash_refund' && $newPayMethod === 'cash') ? $newTotal : 0.0;

            if ($oldCash > 0 || $newCash > 0) {
                $this->cashDrawerService->syncSaleReturnUpdate(
                    $saleReturn,
                    $oldCash,
                    $newCash,
                    $updateDrawerIfPreShift,
                    $user
                );
            }
        });
    }

    /**
     * Revert financial / ledger settlement when a Sale Return is deleted or reversed.
     */
    public function revertSaleReturnSettlement(SaleReturn $saleReturn, ?User $user = null): void
    {
        DB::transaction(function () use ($saleReturn, $user) {
            $this->deleteCustomerLedgerForSaleReturn($saleReturn->id);

            $mode = $saleReturn->settlement_mode ?: 'cash_refund';
            $payMethod = $saleReturn->payment_method ?: 'cash';

            if ($mode === 'cash_refund') {
                if (Schema::hasTable('account_transactions')) {
                    $existingAccTxn = AccountTransaction::where('reference_type', 'sale_return')
                        ->where('reference_id', $saleReturn->id)
                        ->latest('id')
                        ->first();

                    if ($existingAccTxn) {
                        $acc = Account::lockForUpdate()->find($existingAccTxn->account_id);
                        if ($acc) {
                            $this->accountService->deposit(
                                $acc,
                                (float) $existingAccTxn->amount,
                                'sale_return_deletion',
                                $saleReturn->id,
                                "Recovery for deleted sale return #{$saleReturn->posted_number}",
                                $user
                            );
                        }
                    } elseif ((float) $saleReturn->total > 0) {
                        $acc = $this->accountService->resolveAccount(
                            $saleReturn->account_id ? (int) $saleReturn->account_id : null,
                            $payMethod
                        );
                        if ($acc) {
                            $this->accountService->deposit(
                                $acc,
                                (float) $saleReturn->total,
                                'sale_return_deletion',
                                $saleReturn->id,
                                "Recovery for deleted sale return #{$saleReturn->posted_number}",
                                $user
                            );
                        }
                    }
                }

                if ($payMethod === 'cash') {
                    $this->cashDrawerService->handleSaleReturnDeletion($saleReturn, $user);
                }
            }
        });
    }

    /**
     * Apply financial / ledger settlement when a Purchase Return is created.
     */
    public function applyPurchaseReturnSettlement(PurchaseReturn $purchaseReturn, ?User $user = null): void
    {
        DB::transaction(function () use ($purchaseReturn, $user) {
            $mode = $purchaseReturn->settlement_mode ?: 'cash_refund';
            $amount = round((float) ($purchaseReturn->total ?? 0), 2);

            if (in_array($mode, ['credit_note', 'invoice_offset'], true)) {
                $this->persistPurchaseReturnSettlementFields($purchaseReturn, [
                    'settlement_mode' => $mode,
                    'credited_amount' => $amount,
                    'account_id'      => null,
                ]);

                $this->syncSupplierLedgerForPurchaseReturn($purchaseReturn, $amount, $mode, $user);
                return;
            }

            // cash_refund mode
            $this->deleteSupplierLedgerForPurchaseReturn($purchaseReturn->id);

            $payMethod = $purchaseReturn->payment_method ?: 'cash';
            $resolvedAccountId = $purchaseReturn->account_id ? (int) $purchaseReturn->account_id : null;

            if ($amount > 0) {
                $account = $this->accountService->resolveAccount($resolvedAccountId, $payMethod);

                if ($account) {
                    $resolvedAccountId = $account->id;

                    $this->accountService->deposit(
                        $account,
                        $amount,
                        'purchase_return',
                        $purchaseReturn->id,
                        "Supplier refund for purchase return #{$purchaseReturn->posted_number}",
                        $user
                    );
                }
            }

            $this->persistPurchaseReturnSettlementFields($purchaseReturn, [
                'settlement_mode' => 'cash_refund',
                'payment_method'  => $payMethod,
                'account_id'      => $resolvedAccountId,
                'credited_amount' => 0,
            ]);

            if ($payMethod === 'cash' && $amount > 0) {
                $this->cashDrawerService->handlePurchaseReturnCreation($purchaseReturn, $user);
            }
        });
    }

    /**
     * Synchronize financial / ledger settlement when a Purchase Return is updated.
     */
    public function syncPurchaseReturnSettlementUpdate(
        PurchaseReturn $purchaseReturn,
        array $oldSnapshot,
        ?bool $updateDrawerIfPreShift = null,
        ?User $user = null
    ): void {
        DB::transaction(function () use ($purchaseReturn, $oldSnapshot, $updateDrawerIfPreShift, $user) {
            $oldMode = $oldSnapshot['settlement_mode'] ?? 'cash_refund';
            $oldPayMethod = $oldSnapshot['payment_method'] ?? 'cash';
            $oldTotal = round((float) ($oldSnapshot['total'] ?? 0), 2);

            $newMode = $purchaseReturn->settlement_mode ?: 'cash_refund';
            $newPayMethod = $purchaseReturn->payment_method ?: 'cash';
            $newTotal = round((float) ($purchaseReturn->total ?? 0), 2);

            // 1. Sync Financial Account transaction
            $existingAccTxn = Schema::hasTable('account_transactions')
                ? AccountTransaction::where('reference_type', 'purchase_return')
                    ->where('reference_id', $purchaseReturn->id)
                    ->latest('id')
                    ->first()
                : null;

            if ($newMode === 'cash_refund' && $newTotal > 0) {
                $explicitAccountId = ($newPayMethod !== $oldPayMethod && (int) ($purchaseReturn->account_id ?? 0) === (int) ($oldSnapshot['account_id'] ?? 0))
                    ? null
                    : ($purchaseReturn->account_id ? (int) $purchaseReturn->account_id : null);
                $targetAccount = $this->accountService->resolveAccount(
                    $explicitAccountId,
                    $newPayMethod
                );
                $resolvedAccountId = $explicitAccountId;

                $dateStr = $purchaseReturn->date instanceof \Carbon\Carbon
                    ? $purchaseReturn->date->toDateString()
                    : (string) $purchaseReturn->date;

                if ($existingAccTxn) {
                    $oldAcc = Account::lockForUpdate()->find($existingAccTxn->account_id);
                    if ($oldAcc) {
                        $oldAcc->current_balance = round((float) $oldAcc->current_balance - (float) $existingAccTxn->amount, 2);
                        $oldAcc->save();
                    }

                    if ($targetAccount) {
                        $lockedTarget = Account::lockForUpdate()->find($targetAccount->id);
                        if ($lockedTarget) {
                            $lockedTarget->current_balance = round((float) $lockedTarget->current_balance + $newTotal, 2);
                            $lockedTarget->save();

                            $existingAccTxn->account_id = $lockedTarget->id;
                            $existingAccTxn->amount = $newTotal;
                            $existingAccTxn->balance_after = $lockedTarget->current_balance;
                            $existingAccTxn->transaction_date = $dateStr;
                            $existingAccTxn->description = "Supplier refund for purchase return #{$purchaseReturn->posted_number}";
                            $existingAccTxn->save();

                            $resolvedAccountId = $lockedTarget->id;
                        }
                    } else {
                        $existingAccTxn->delete();
                        $resolvedAccountId = null;
                    }
                } elseif ($targetAccount) {
                    $resolvedAccountId = $targetAccount->id;
                    $this->accountService->deposit(
                        $targetAccount,
                        $newTotal,
                        'purchase_return',
                        $purchaseReturn->id,
                        "Supplier refund for purchase return #{$purchaseReturn->posted_number}",
                        $user
                    );
                }

                $this->persistPurchaseReturnSettlementFields($purchaseReturn, [
                    'settlement_mode' => 'cash_refund',
                    'payment_method'  => $newPayMethod,
                    'account_id'      => $resolvedAccountId,
                    'credited_amount' => 0,
                ]);

                $this->deleteSupplierLedgerForPurchaseReturn($purchaseReturn->id);
            } else {
                // Switched to credit_note / invoice_offset or 0 total: unwind any previous account deposit
                if ($existingAccTxn) {
                    $oldAcc = Account::lockForUpdate()->find($existingAccTxn->account_id);
                    if ($oldAcc) {
                        $oldAcc->current_balance = round((float) $oldAcc->current_balance - (float) $existingAccTxn->amount, 2);
                        $oldAcc->save();
                    }
                    $existingAccTxn->delete();
                }

                $credited = in_array($newMode, ['credit_note', 'invoice_offset'], true) ? $newTotal : 0;
                $this->persistPurchaseReturnSettlementFields($purchaseReturn, [
                    'settlement_mode' => $newMode,
                    'account_id'      => null,
                    'credited_amount' => $credited,
                ]);

                if (in_array($newMode, ['credit_note', 'invoice_offset'], true)) {
                    $this->syncSupplierLedgerForPurchaseReturn($purchaseReturn, $newTotal, $newMode, $user);
                } else {
                    $this->deleteSupplierLedgerForPurchaseReturn($purchaseReturn->id);
                }
            }

            // 2. Sync Cash Drawer based on effective physical cash impact before vs after
            $oldCash = ($oldMode === 'cash_refund' && $oldPayMethod === 'cash') ? $oldTotal : 0.0;
            $newCash = ($newMode === 'cash_refund' && $newPayMethod === 'cash') ? $newTotal : 0.0;

            if ($oldCash > 0 || $newCash > 0) {
                $this->cashDrawerService->syncPurchaseReturnUpdate(
                    $purchaseReturn,
                    $oldCash,
                    $newCash,
                    $updateDrawerIfPreShift,
                    $user
                );
            }
        });
    }

    /**
     * Revert financial / ledger settlement when a Purchase Return is deleted or reversed.
     */
    public function revertPurchaseReturnSettlement(PurchaseReturn $purchaseReturn, ?User $user = null): void
    {
        DB::transaction(function () use ($purchaseReturn, $user) {
            $this->deleteSupplierLedgerForPurchaseReturn($purchaseReturn->id);

            $mode = $purchaseReturn->settlement_mode ?: 'cash_refund';
            $payMethod = $purchaseReturn->payment_method ?: 'cash';

            if ($mode === 'cash_refund') {
                if (Schema::hasTable('account_transactions')) {
                    $existingAccTxn = AccountTransaction::where('reference_type', 'purchase_return')
                        ->where('reference_id', $purchaseReturn->id)
                        ->latest('id')
                        ->first();

                    if ($existingAccTxn) {
                        $acc = Account::lockForUpdate()->find($existingAccTxn->account_id);
                        if ($acc) {
                            $this->accountService->withdraw(
                                $acc,
                                (float) $existingAccTxn->amount,
                                'purchase_return_deletion',
                                $purchaseReturn->id,
                                "Reversal for deleted purchase return #{$purchaseReturn->posted_number}",
                                $user
                            );
                        }
                    } elseif ((float) $purchaseReturn->total > 0) {
                        $acc = $this->accountService->resolveAccount(
                            $purchaseReturn->account_id ? (int) $purchaseReturn->account_id : null,
                            $payMethod
                        );
                        if ($acc) {
                            $this->accountService->withdraw(
                                $acc,
                                (float) $purchaseReturn->total,
                                'purchase_return_deletion',
                                $purchaseReturn->id,
                                "Reversal for deleted purchase return #{$purchaseReturn->posted_number}",
                                $user
                            );
                        }
                    }
                }

                if ($payMethod === 'cash') {
                    $this->cashDrawerService->handlePurchaseReturnDeletion($purchaseReturn, $user);
                }
            }
        });
    }

    protected function deleteCustomerLedgerForSaleReturn(int $saleReturnId): void
    {
        if (Schema::hasTable('customer_ledgers') && Schema::hasColumn('customer_ledgers', 'sale_return_id')) {
            CustomerLedger::where('sale_return_id', $saleReturnId)->delete();
        }
    }

    protected function deleteSupplierLedgerForPurchaseReturn(int $purchaseReturnId): void
    {
        if (Schema::hasTable('supplier_ledgers') && Schema::hasColumn('supplier_ledgers', 'purchase_return_id')) {
            SupplierLedger::where('purchase_return_id', $purchaseReturnId)->delete();
        }
    }

    /**
     * Create or update the CustomerLedger credit entry for a non-cash Sale Return.
     */
    protected function syncCustomerLedgerForSaleReturn(
        SaleReturn $saleReturn,
        float $amount,
        string $mode,
        ?User $user = null
    ): void {
        if (!Schema::hasTable('customer_ledgers') || !Schema::hasColumn('customer_ledgers', 'sale_return_id')) {
            return;
        }

        if ($amount <= 0) {
            CustomerLedger::where('sale_return_id', $saleReturn->id)->delete();
            return;
        }

        $invPosted = null;
        if ($saleReturn->sale_invoice_id) {
            $invPosted = $saleReturn->relationLoaded('saleInvoice')
                ? $saleReturn->saleInvoice?->posted_number
                : SaleInvoice::where('id', $saleReturn->sale_invoice_id)->value('posted_number');
        }

        $label = $mode === 'invoice_offset' ? 'Invoice Offset' : 'Credit Note';
        $description = "{$label} - Sale Return #{$saleReturn->posted_number}" . ($invPosted ? " (Inv #{$invPosted})" : '');
        $entryDate = $saleReturn->date instanceof \Carbon\Carbon
            ? $saleReturn->date->toDateString()
            : (string) $saleReturn->date;

        CustomerLedger::updateOrCreate(
            ['sale_return_id' => $saleReturn->id],
            [
                'customer_id'       => $saleReturn->customer_id,
                'sale_invoice_id'   => null,
                'entry_type'        => 'payment',
                'is_manual'         => false,
                'entry_date'        => $entryDate,
                'posted_number'     => $saleReturn->posted_number,
                'invoice_total'     => 0,
                'total_received'    => 0,
                'balance_remaining' => 0,
                'credited_amount'   => $amount,
                'payment_ref'       => $saleReturn->posted_number,
                'description'       => $description,
                'created_by'        => $user?->id ?? $saleReturn->user_id,
            ]
        );
    }

    /**
     * Create or update the SupplierLedger debit entry for a non-cash Purchase Return.
     */
    protected function syncSupplierLedgerForPurchaseReturn(
        PurchaseReturn $purchaseReturn,
        float $amount,
        string $mode,
        ?User $user = null
    ): void {
        if (!Schema::hasTable('supplier_ledgers') || !Schema::hasColumn('supplier_ledgers', 'purchase_return_id')) {
            return;
        }

        if ($amount <= 0) {
            SupplierLedger::where('purchase_return_id', $purchaseReturn->id)->delete();
            return;
        }

        $invPosted = null;
        $invNumber = null;
        if ($purchaseReturn->purchase_invoice_id) {
            $inv = $purchaseReturn->relationLoaded('purchaseInvoice')
                ? $purchaseReturn->purchaseInvoice
                : PurchaseInvoice::select('id', 'posted_number', 'invoice_number')->find($purchaseReturn->purchase_invoice_id);
            $invPosted = $inv?->posted_number;
            $invNumber = $inv?->invoice_number;
        }

        $label = $mode === 'invoice_offset' ? 'Invoice Offset' : 'Debit Note';
        $description = "{$label} - Purchase Return #{$purchaseReturn->posted_number}" . ($invPosted ? " (Inv #{$invPosted})" : '');
        $entryDate = $purchaseReturn->date instanceof \Carbon\Carbon
            ? $purchaseReturn->date->toDateString()
            : (string) $purchaseReturn->date;

        SupplierLedger::updateOrCreate(
            ['purchase_return_id' => $purchaseReturn->id],
            [
                'supplier_id'         => $purchaseReturn->supplier_id,
                'purchase_invoice_id' => null,
                'entry_type'          => 'payment',
                'is_manual'           => false,
                'entry_date'          => $entryDate,
                'posted_number'       => $purchaseReturn->posted_number,
                'invoice_number'      => $invNumber,
                'invoice_total'       => 0,
                'total_paid'          => 0,
                'debited_amount'      => $amount,
                'credit_remaining'    => 0,
                'payment_ref'         => $purchaseReturn->posted_number,
                'description'         => $description,
                'created_by'          => $user?->id,
            ]
        );
    }
}
