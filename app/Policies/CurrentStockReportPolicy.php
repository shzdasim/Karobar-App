<?php

namespace App\Policies;

use App\Authorizables\CurrentStockReport;
use App\Models\User;
use Illuminate\Auth\Access\HandlesAuthorization;

class CurrentStockReportPolicy
{
    use HandlesAuthorization;

    /**
     * Determine whether the user can view the report.
     */
    public function view(User $user, ?CurrentStockReport $report = null): bool
    {
        return $user->can('report.current-stock.view');
    }

    /**
     * Determine whether the user can export the report.
     */
    public function export(User $user, ?CurrentStockReport $report = null): bool
    {
        return $user->can('report.current-stock.export');
    }
}

