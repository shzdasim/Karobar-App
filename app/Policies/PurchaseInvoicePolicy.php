<?php

namespace App\Policies;

use App\Models\User;
use App\Models\PurchaseInvoice;

class PurchaseInvoicePolicy
{
    public function viewAny(User $user): bool
    {
        return $user->can('purchase-invoice.view');
    }

    public function lookup(User $user): bool
    {
        return $user->can('purchase-invoice.view')
            || $user->can('purchase-return.create')
            || $user->can('purchase-return.update');
    }

    public function view(User $user, PurchaseInvoice $invoice): bool
    {
        return $user->can('purchase-invoice.view')
            || $user->can('purchase-return.create')
            || $user->can('purchase-return.update');
    }

    public function create(User $user): bool
    {
        return $user->can('purchase-invoice.create');
    }

    public function update(User $user, PurchaseInvoice $invoice): bool
    {
        return $user->can('purchase-invoice.update');
    }

    public function delete(User $user, PurchaseInvoice $invoice): bool
    {
        return $user->can('purchase-invoice.delete');
    }

    public function reverse(User $user, PurchaseInvoice $invoice): bool
    {
        return $user->can('purchase-invoice.delete');
    }

    public function viewHistory(User $user, PurchaseInvoice $invoice): bool
    {
        return $user->can('purchase-invoice.view');
    }
}
