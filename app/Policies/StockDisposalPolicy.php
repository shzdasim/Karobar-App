<?php

namespace App\Policies;

use App\Models\StockDisposal;
use App\Models\User;

class StockDisposalPolicy
{
    public function viewAny(User $user): bool
    {
        return $user->can('stock-disposal.view');
    }

    public function view(User $user, StockDisposal $disposal): bool
    {
        return $user->can('stock-disposal.view');
    }

    public function create(User $user): bool
    {
        return $user->can('stock-disposal.create');
    }

    public function dispose(User $user, StockDisposal $disposal): bool
    {
        return $user->can('stock-disposal.dispose');
    }

    public function restore(User $user, StockDisposal $disposal): bool
    {
        return $user->can('stock-disposal.restore');
    }

    public function delete(User $user, StockDisposal $disposal): bool
    {
        return $user->can('stock-disposal.delete');
    }
}

