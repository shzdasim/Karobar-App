<?php

namespace App\Policies;

use App\Models\Payment;
use App\Models\User;

class PaymentPolicy
{
    public function viewAny(User $user): bool
    {
        return $user->can('payment.view')
            || $user->can('customer-ledger.view')
            || $user->can('supplier-ledger.view')
            || $user->hasRole('admin');
    }

    public function view(User $user, Payment $payment): bool
    {
        return $this->viewAny($user);
    }

    public function create(User $user): bool
    {
        return $user->can('payment.create')
            || $user->can('customer-ledger.create')
            || $user->can('supplier-ledger.create')
            || $user->hasRole('admin');
    }

    public function reverse(User $user, Payment $payment): bool
    {
        return $user->can('payment.delete')
            || $user->can('customer-ledger.delete')
            || $user->can('supplier-ledger.delete')
            || $user->hasRole('admin');
    }
}

