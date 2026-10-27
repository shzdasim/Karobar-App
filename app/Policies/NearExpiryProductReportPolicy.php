<?php

namespace App\Policies;

use App\Authorizables\NearExpiryProductReport;
use App\Models\User;
use Illuminate\Auth\Access\HandlesAuthorization;

class NearExpiryProductReportPolicy
{
    use HandlesAuthorization;

    /**
     * Determine whether the user can view the report.
     */
    public function view(User $user, ?NearExpiryProductReport $report = null): bool
    {
        return $user->can('report.near-expiry-product.view');
    }

    /**
     * Determine whether the user can export the report.
     */
    public function export(User $user, ?NearExpiryProductReport $report = null): bool
    {
        return $user->can('report.near-expiry-product.export');
    }
}

