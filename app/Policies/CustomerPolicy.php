<?php

namespace App\Policies;

use App\Models\User;
use App\Models\Customer;

class CustomerPolicy
{
    public function before(User $user, $ability)
    {
        if ($user->hasRole('Admin')) return true;
    }

    public function viewAny(User $user): bool { return $user->can('customer.view'); }

    public function lookup(User $user, ?Customer $customer = null): bool
    {
        return $user->can('customer.view')
            || $user->can('barcode-pos.view')
            || $user->can('barcode-pos.create')
            || $user->can('sale-invoice.view')
            || $user->can('sale-invoice.create')
            || $user->can('sale-invoice.update')
            || $user->can('sale-return.view')
            || $user->can('sale-return.create')
            || $user->can('sale-return.update')
            || $user->can('customer-ledger.view')
            || $user->can('payment.view')
            || $user->can('payment.create')
            || $user->can('report.sale-detail.view')
            || $user->can('report.sale-returns.view')
            || $user->can('report.cost-of-sale-detail.view');
    }

    public function view(User $user, ?Customer $customer = null): bool { return $user->can('customer.view'); }
    public function create(User $user): bool { return $user->can('customer.create'); }
    public function update(User $user, ?Customer $customer = null): bool { return $user->can('customer.update'); }
    public function delete(User $user, ?Customer $customer = null): bool { return $user->can('customer.delete'); }

    // custom abilities
    public function export(User $user): bool { return $user->can('customer.export'); }
    public function import(User $user): bool { return $user->can('customer.import'); }
}
