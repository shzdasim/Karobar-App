<?php

namespace App\Policies;

use App\Models\InvoiceDraft;
use App\Models\User;

class InvoiceDraftPolicy
{
    public function viewAny(User $user): bool
    {
        return $user->can('sale-invoice.view') || $user->can('purchase-invoice.view') || $user->hasRole('Admin');
    }

    public function view(User $user, InvoiceDraft $draft): bool
    {
        return $draft->user_id === $user->id || $user->hasRole('Admin');
    }

    public function create(User $user): bool
    {
        return $user->can('sale-invoice.create') || $user->can('purchase-invoice.create') || $user->hasRole('Admin');
    }

    public function update(User $user, InvoiceDraft $draft): bool
    {
        return $draft->user_id === $user->id || $user->hasRole('Admin');
    }

    public function delete(User $user, InvoiceDraft $draft): bool
    {
        return $draft->user_id === $user->id || $user->hasRole('Admin');
    }
}

