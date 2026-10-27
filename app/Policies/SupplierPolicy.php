<?php

namespace App\Policies;

use App\Models\User;
use App\Models\Supplier;

class SupplierPolicy
{
    // Optional: let Admin do everything
    public function before(User $user, $ability)
    {
        if ($user->hasRole('Admin')) return true;
    }

    public function viewAny(User $user): bool { return $user->can('supplier.view'); }

    public function lookup(User $user, ?Supplier $supplier = null): bool
    {
        return $user->can('supplier.view')
            || $user->can('barcode.view')
            || $user->can('barcode.print')
            || $user->can('batch-expiry.view')
            || $user->can('purchase-invoice.view')
            || $user->can('purchase-invoice.create')
            || $user->can('purchase-invoice.update')
            || $user->can('purchase-return.view')
            || $user->can('purchase-return.create')
            || $user->can('purchase-return.update')
            || $user->can('purchase-order.view')
            || $user->can('purchase-order.create')
            || $user->can('purchase-order.update')
            || $user->can('supplier-ledger.view')
            || $user->can('payment.view')
            || $user->can('payment.create')
            || $user->can('report.purchase-detail.view')
            || $user->can('report.purchase-summary.view')
            || $user->can('report.purchase-returns.view')
            || $user->can('report.current-stock.view')
            || $user->can('report.reorder.view')
            || $user->can('report.near-expiry-product.view');
    }

    public function view(User $user, ?Supplier $supplier = null): bool { return $user->can('supplier.view'); }
    public function create(User $user): bool { return $user->can('supplier.create'); }
    public function update(User $user, ?Supplier $supplier = null): bool { return $user->can('supplier.update'); }
    public function delete(User $user, ?Supplier $supplier = null): bool { return $user->can('supplier.delete'); }

    // Custom abilities
    public function export(User $user): bool { return $user->can('supplier.export'); }
    public function import(User $user): bool { return $user->can('supplier.import'); }
}
