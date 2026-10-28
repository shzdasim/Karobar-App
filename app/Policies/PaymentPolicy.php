<?php

namespace App\Policies;

use App\Models\Payment;
use App\Models\User;

class PaymentPolicy
{
    public function viewAny(User $user): bool
    {
        return $user->can('payment.view')
            || $user->hasRole('Admin');
    }

    public function view(User $user, ?Payment $payment = null): bool
    {
        return $this->viewAny($user);
    }

    public function create(User $user): bool
    {
        return $user->can('payment.create')
            || $user->hasRole('Admin');
    }

    public function allocate(User $user, ?Payment $payment = null): bool
    {
        return $user->can('payment.create')
            || $user->can('payment.update')
            || $user->hasRole('Admin');
    }

    public function reverse(User $user, ?Payment $payment = null): bool
    {
        return $user->can('payment.delete')
            || $user->hasRole('Admin');
    }
}
