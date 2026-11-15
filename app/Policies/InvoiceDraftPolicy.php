<?php

namespace App\Policies;

use App\Models\InvoiceDraft;
use App\Models\User;

class InvoiceDraftPolicy
{
    protected function isSharedTerminalDraft(InvoiceDraft $draft): bool
    {
        return !empty($draft->terminal_id)
            || !empty($draft->terminal_name)
            || \App\Models\Terminal::currentId() !== null
            || (function_exists('request') && request() && request()->hasHeader('X-Terminal-Id'));
    }

    public function viewAny(User $user): bool
    {
        return $user->can('draft.view')
            || $user->can('sale-invoice.view')
            || $user->can('purchase-invoice.view')
            || $user->hasRole('Admin');
    }

    public function view(User $user, InvoiceDraft $draft): bool
    {
        if ($draft->user_id === $user->id || $user->hasRole('Admin')) {
            return true;
        }
        if (!$this->isSharedTerminalDraft($draft)) {
            return false;
        }
        if (in_array($draft->type, ['sale_retail', 'sale_wholesale', 'quotation'], true)) {
            return $user->can('draft.view') || $user->can('sale-invoice.view') || $user->can('sale-invoice.create');
        }
        if ($draft->type === 'purchase') {
            return $user->can('draft.view') || $user->can('purchase-invoice.view') || $user->can('purchase-invoice.create');
        }
        return false;
    }

    public function create(User $user): bool
    {
        return $user->can('draft.create')
            || $user->can('sale-invoice.create')
            || $user->can('purchase-invoice.create')
            || $user->hasRole('Admin');
    }

    public function update(User $user, InvoiceDraft $draft): bool
    {
        if ($draft->user_id === $user->id || $user->hasRole('Admin')) {
            return true;
        }
        if (!$this->isSharedTerminalDraft($draft)) {
            return false;
        }
        if (in_array($draft->type, ['sale_retail', 'sale_wholesale', 'quotation'], true)) {
            return $user->can('draft.update') || $user->can('sale-invoice.create') || $user->can('sale-invoice.edit');
        }
        if ($draft->type === 'purchase') {
            return $user->can('draft.update') || $user->can('purchase-invoice.create') || $user->can('purchase-invoice.edit');
        }
        return false;
    }

    public function delete(User $user, InvoiceDraft $draft): bool
    {
        if ($draft->user_id === $user->id || $user->hasRole('Admin')) {
            return true;
        }
        if (!$this->isSharedTerminalDraft($draft)) {
            return false;
        }
        if (in_array($draft->type, ['sale_retail', 'sale_wholesale', 'quotation'], true)) {
            return $user->can('draft.delete') || $user->can('sale-invoice.create') || $user->can('sale-invoice.delete');
        }
        if ($draft->type === 'purchase') {
            return $user->can('draft.delete') || $user->can('purchase-invoice.create') || $user->can('purchase-invoice.delete');
        }
        return false;
    }
}

