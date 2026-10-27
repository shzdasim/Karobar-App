<?php

namespace App\Policies;

use App\Models\StocktakeSession;
use App\Models\User;

class StocktakePolicy
{
    public function viewAny(User $user): bool
    {
        return $user->can('stocktake.view');
    }

    public function view(User $user, ?StocktakeSession $session = null): bool
    {
        return $user->can('stocktake.view');
    }

    public function create(User $user): bool
    {
        return $user->can('stocktake.create');
    }

    public function update(User $user, ?StocktakeSession $session = null): bool
    {
        return $user->can('stocktake.update');
    }

    public function delete(User $user, ?StocktakeSession $session = null): bool
    {
        return $user->can('stocktake.delete');
    }

    public function finalize(User $user, ?StocktakeSession $session = null): bool
    {
        return $user->can('stocktake.finalize');
    }
}
