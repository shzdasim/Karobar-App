<?php

namespace App\Policies;

use App\Authorizables\ProductComprehensiveReport;
use App\Models\User;
use Illuminate\Auth\Access\HandlesAuthorization;

class ProductComprehensiveReportPolicy
{
    use HandlesAuthorization;

    /**
     * Determine whether the user can view the report.
     */
    public function view(User $user, ?ProductComprehensiveReport $report = null): bool
    {
        return $user->can('report.product-comprehensive.view');
    }

    /**
     * Determine whether the user can export the report.
     */
    public function export(User $user, ?ProductComprehensiveReport $report = null): bool
    {
        return $user->can('report.product-comprehensive.export');
    }
}
