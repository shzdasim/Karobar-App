<?php

namespace App\Policies;

use App\Models\StockDisposal;
use App\Models\User;

class StockDisposalPolicy
{
    public function viewAny(User $user): bool
    {
        return $user->can('stock-disposal.view') || $user->can('stock-adjustment.view');
    }

    public function view(User $user, StockDisposal $disposal): bool
    {
        return $user->can('stock-disposal.view') || $user->can('stock-adjustment.view');
    }

    public function create(User $user): bool
    {
        return $user->can('stock-disposal.create') || $user->can('stock-adjustment.create');
    }

    public function dispose(User $user, StockDisposal $disposal): bool
    {
        return $user->can('stock-disposal.dispose') || $user->can('stock-adjustment.create');
    }

    public function restore(User $user, StockDisposal $disposal): bool
    {
        return $user->can('stock-disposal.restore') || $user->can('stock-adjustment.delete');
    }

    public function delete(User $user, StockDisposal $disposal): bool
    {
        return $user->can('stock-disposal.delete') || $user->can('stock-adjustment.delete');
    }
}

