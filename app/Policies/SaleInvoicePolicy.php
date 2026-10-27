<?php

namespace App\Policies;

use App\Models\SaleInvoice;
use App\Models\User;

class SaleInvoicePolicy
{
   public function viewAny(User $user): bool
   {
    return $user->can('sale-invoice.view');
   }

   public function lookup(User $user): bool
   {
    return $user->can('sale-invoice.view')
        || $user->can('sale-return.create')
        || $user->can('sale-return.update')
        || $user->can('report.cost-of-sale-detail.view');
   }

   public function view(User $user, SaleInvoice $invoice):bool
   {
    return $user->can('sale-invoice.view')
        || $user->can('sale-return.create')
        || $user->can('sale-return.update');
   }

   public function create(User $user): bool
   {
    return $user->can('sale-invoice.create') || $user->can('barcode-pos.create');
   }

   public function update(User $user, SaleInvoice $invoice): bool
   {
    return $user->can('sale-invoice.update');
   }

   public function delete(User $user, SaleInvoice $invoice): bool
   {
    return $user->can('sale-invoice.delete');
   }

   public function reverse(User $user, SaleInvoice $invoice): bool
   {
    return $user->can('sale-invoice.delete');
   }

   public function viewHistory(User $user, SaleInvoice $invoice): bool
   {
    return $user->can('sale-invoice.view');
   }
}
