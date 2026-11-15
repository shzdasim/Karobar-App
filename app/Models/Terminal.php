<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Http\Request;

class Terminal extends Model
{
    protected $fillable = [
        'terminal_uid',
        'name',
        'hostname',
        'ip_address',
        'os_platform',
        'app_version',
        'mode',
        'current_user_id',
        'is_authorized',
        'last_seen_at',
        'meta',
    ];

    protected $casts = [
        'current_user_id' => 'integer',
        'is_authorized'   => 'boolean',
        'last_seen_at'    => 'datetime',
        'meta'            => 'array',
    ];

    public function currentUser()
    {
        return $this->belongsTo(User::class, 'current_user_id');
    }

    public function cashDrawerSessions()
    {
        return $this->hasMany(CashDrawerSession::class, 'terminal_id');
    }

    public function openCashDrawerSession()
    {
        return $this->hasOne(CashDrawerSession::class, 'terminal_id')
            ->where('status', 'open')
            ->latestOfMany('opened_at');
    }

    public function saleInvoices()
    {
        return $this->hasMany(SaleInvoice::class, 'terminal_id');
    }

    /**
     * Retrieve the currently resolved Terminal for the active HTTP request (if any).
     */
    public static function current(?Request $request = null): ?self
    {
        if (app()->bound('current_terminal')) {
            $bound = app('current_terminal');
            if ($bound instanceof self) {
                return $bound;
            }
        }

        $req = $request ?? request();
        if ($req && $req->attributes->has('terminal')) {
            $attr = $req->attributes->get('terminal');
            if ($attr instanceof self) {
                return $attr;
            }
        }

        return null;
    }

    /**
     * Retrieve the currently resolved Terminal primary key ID (if any).
     */
    public static function currentId(?Request $request = null): ?int
    {
        return self::current($request)?->id;
    }

    /**
     * Check if the terminal has been active within the last 25 seconds.
     */
    public function isOnline(): bool
    {
        if (!$this->last_seen_at) {
            return false;
        }

        return $this->last_seen_at->greaterThanOrEqualTo(now()->subSeconds(25));
    }
}
