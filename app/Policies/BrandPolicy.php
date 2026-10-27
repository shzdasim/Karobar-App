<?php

namespace App\Policies;

use App\Models\User;
use App\Models\Brand;

class BrandPolicy
{
    public function before(User $user, $ability)
    {
        if ($user->hasRole('Admin')) return true;
    }

    public function viewAny(User $user): bool { return $user->can('brand.view'); }

    public function lookup(User $user): bool
    {
        return $user->can('brand.view')
            || $user->can('product.view')
            || $user->can('product.create')
            || $user->can('product.update')
            || $user->can('barcode.view')
            || $user->can('barcode.print')
            || $user->can('batch-expiry.view')
            || $user->can('purchase-order.view')
            || $user->can('purchase-order.create')
            || $user->can('purchase-order.update')
            || $user->can('stocktake.view')
            || $user->can('stocktake.create')
            || $user->can('stocktake.update')
            || $user->can('report.current-stock.view')
            || $user->can('report.reorder.view')
            || $user->can('report.near-expiry-product.view');
    }

    public function view(User $user, Brand $brand): bool { return $user->can('brand.view'); }
    public function create(User $user): bool { return $user->can('brand.create'); }
    public function update(User $user, Brand $brand): bool { return $user->can('brand.update'); }
    public function delete(User $user, Brand $brand): bool { return $user->can('brand.delete'); }

    // custom
    public function export(User $user): bool { return $user->can('brand.export'); }
    public function import(User $user): bool { return $user->can('brand.import'); }
}
