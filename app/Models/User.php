<?php

namespace App\Models;

use Illuminate\Foundation\Auth\User as Authenticatable;
use Illuminate\Contracts\Auth\CanResetPassword;
use Laravel\Sanctum\HasApiTokens;
use Spatie\Permission\Traits\HasRoles;
use Illuminate\Notifications\Notifiable;
use App\Notifications\ResetPasswordNotification;

class User extends Authenticatable implements CanResetPassword
{
    use HasApiTokens, Notifiable, HasRoles;

    protected $guard_name = 'sanctum';
    protected $fillable = [
        'name',
        'email',
        'password',
        'status',
        'remember_token_expires_at',
        'preferences',
    ];

    protected $hidden = [
        'password',
        'remember_token',
    ];

    protected $casts = [
        'preferences' => 'array',
        'remember_token_expires_at' => 'datetime',
    ];

    /**
     * Get a specific preference value
     */
    public function getPreference(string $key, $default = null)
    {
        return $this->preferences[$key] ?? $default;
    }

    /**
     * Set a specific preference value
     */
    public function setPreference(string $key, $value): void
    {
        $preferences = $this->preferences ?? [];
        $preferences[$key] = $value;
        $this->preferences = $preferences;
    }

    /**
     * Save preferences (batch update)
     */
    public function savePreferences(array $preferences): void
    {
        $current = $this->preferences ?? [];
        $this->preferences = array_merge($current, $preferences);
    }

    /**
     * Send the password reset notification.
     *
     * @param  string  $token
     * @return void
     */
    public function sendPasswordResetNotification($token)
    {
        $this->notify(new ResetPasswordNotification($token));
    }
}
