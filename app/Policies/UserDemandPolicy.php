<?php

namespace App\Policies;

use App\Models\User;
use App\Models\UserDemand;

class UserDemandPolicy
{
    /** List / index / count */
    public function viewAny(User $user): bool
    {
        return $user->can('user-demands.view');
    }

    /** Create a demand */
    public function create(User $user): bool
    {
        return $user->can('user-demands.create');
    }

    /** Update a demand (status change) */
    public function update(User $user, UserDemand $userDemand): bool
    {
        return $user->can('user-demands.update');
    }

    /** Delete a demand */
    public function delete(User $user, UserDemand $userDemand): bool
    {
        return $user->can('user-demands.delete');
    }
}
