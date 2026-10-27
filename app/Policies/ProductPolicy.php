<?php

namespace App\Policies;

use App\Models\User;
use App\Models\Product;

class ProductPolicy
{
    // If you have an Admin role, you can short-circuit in AuthServiceProvider (see below).
    // Otherwise, keep policies strict here.

    /** List / index */
    public function viewAny(User $user): bool
    {
        return $user->can('product.view');
    }

    /** Lookup / search / stock check during POS & invoice transactions */
    public function lookup(User $user): bool
    {
        return $user->can('product.view')
            || $user->can('barcode.view')
            || $user->can('barcode.print')
            || $user->can('barcode-pos.view')
            || $user->can('barcode-pos.create')
            || $user->can('batch-expiry.view')
            || $user->can('sale-invoice.view')
            || $user->can('sale-invoice.create')
            || $user->can('sale-invoice.update')
            || $user->can('sale-return.view')
            || $user->can('sale-return.create')
            || $user->can('sale-return.update')
            || $user->can('purchase-invoice.view')
            || $user->can('purchase-invoice.create')
            || $user->can('purchase-invoice.update')
            || $user->can('purchase-return.view')
            || $user->can('purchase-return.create')
            || $user->can('purchase-return.update')
            || $user->can('purchase-order.view')
            || $user->can('purchase-order.create')
            || $user->can('purchase-order.update')
            || $user->can('stock-adjustment.view')
            || $user->can('stock-adjustment.create')
            || $user->can('stock-adjustment.update')
            || $user->can('stock-disposal.view')
            || $user->can('stock-disposal.create')
            || $user->can('stocktake.view')
            || $user->can('stocktake.create')
            || $user->can('stocktake.update')
            || $user->can('report.purchase-detail.view')
            || $user->can('report.purchase-summary.view')
            || $user->can('report.purchase-returns.view')
            || $user->can('report.sale-detail.view')
            || $user->can('report.sale-returns.view')
            || $user->can('report.product-comprehensive.view')
            || $user->can('report.near-expiry-product.view');
    }

    /** View a single product */
    public function view(User $user, ?Product $product = null): bool
    {
        return $user->can('product.view');
    }

    /** Create */
    public function create(User $user): bool
    {
        return $user->can('product.create');
    }

    /** Update */
    public function update(User $user, ?Product $product = null): bool
    {
        return $user->can('product.update');
    }

    /** Delete */
    public function delete(User $user, ?Product $product = null): bool
    {
        return $user->can('product.delete');
    }

    /** Optional: CSV export */
    public function export(User $user): bool
    {
        return $user->can('product.export') || false; // keep false if you don't have this perm
    }

    /** Optional: CSV import */
    public function import(User $user): bool
    {
        return $user->can('product.import') || false;
    }

    /** Optional: bulk meta update */
    public function bulkUpdate(User $user): bool
    {
        return $user->can('product.update'); // piggyback on update
    }
}
