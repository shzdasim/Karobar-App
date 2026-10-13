<?php

namespace App\Policies;

use App\Models\PurchaseOrder;
use App\Models\User;

class PurchaseOrderPolicy
{
    public function viewAny(User $user): bool
    {
        return $user->can('purchase-order.view');
    }

    public function view(User $user, PurchaseOrder $order): bool
    {
        return $user->can('purchase-order.view');
    }

    public function create(User $user): bool
    {
        return $user->can('purchase-order.create');
    }

    public function update(User $user, PurchaseOrder $order): bool
    {
        return $user->can('purchase-order.update');
    }

    public function cancel(User $user, PurchaseOrder $order): bool
    {
        return $user->can('purchase-order.cancel');
    }

    public function delete(User $user, PurchaseOrder $order): bool
    {
        return $user->can('purchase-order.delete');
    }
}
