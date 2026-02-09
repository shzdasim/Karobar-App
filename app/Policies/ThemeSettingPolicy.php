<?php

namespace App\Policies;

use App\Models\User;
use App\Models\ThemeSetting;

class ThemeSettingPolicy
{
    // viewing theme settings page / GET /api/theme-settings
    public function viewAny(User $user): bool
    {
        return $user->can('settings.view');
    }

    // viewing a specific theme / GET /api/theme-settings/{id}
    public function view(User $user, ThemeSetting $themeSetting): bool
    {
        return $user->can('settings.view');
    }

    // creating theme settings / POST /api/theme-settings
    public function create(User $user): bool
    {
        return $user->can('settings.update');
    }

    // updating theme settings / PUT /api/theme-settings/{id}
    public function update(User $user, ThemeSetting $themeSetting): bool
    {
        return $user->can('settings.update');
    }

    // deleting theme settings / DELETE /api/theme-settings/{id}
    public function delete(User $user, ThemeSetting $themeSetting): bool
    {
        return $user->can('settings.update');
    }
}

