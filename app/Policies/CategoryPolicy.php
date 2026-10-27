<?php

namespace App\Policies;

use App\Models\User;
use App\Models\Category;

class CategoryPolicy
{
    /**
     * Let Admin do anything.
     */
    public function before(User $user, string $ability): ?bool
    {
        if ($user->hasRole('Admin')) {
            return true;
        }
        return null;
    }

    public function viewAny(User $user): bool
    {
        return $user->can('category.view');
    }

    public function lookup(User $user): bool
    {
        return $user->can('category.view')
            || $user->can('product.view')
            || $user->can('product.create')
            || $user->can('product.update')
            || $user->can('barcode.view')
            || $user->can('barcode.print')
            || $user->can('batch-expiry.view')
            || $user->can('stocktake.view')
            || $user->can('stocktake.create')
            || $user->can('stocktake.update')
            || $user->can('report.current-stock.view');
    }

    public function view(User $user, Category $category): bool
    {
        return $user->can('category.view');
    }

    public function create(User $user): bool
    {
        return $user->can('category.create');
    }

    public function update(User $user, Category $category): bool
    {
        return $user->can('category.update');
    }

    public function delete(User $user, Category $category): bool
    {
        return $user->can('category.delete');
    }

    public function export(User $user): bool
    {
        return $user->can('category.export');
    }
}
