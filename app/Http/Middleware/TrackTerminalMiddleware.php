<?php

namespace App\Http\Middleware;

use App\Models\Setting;
use App\Models\Terminal;
use App\Services\LicenseService;
use App\Support\SchemaCache;
use Closure;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Cache;
use Symfony\Component\HttpFoundation\Response;
use Throwable;

class TrackTerminalMiddleware
{
    public function handle(Request $request, Closure $next): Response
    {
        $terminalUid = trim((string) (
            $request->header('X-Terminal-Id')
            ?: $request->input('terminal_uid')
            ?: ''
        ));

        if ($terminalUid !== '' && SchemaCache::hasTable('terminals')) {
            $terminalName = trim((string) (
                $request->header('X-Terminal-Name')
                ?: $request->input('terminal_name')
                ?: ''
            ));
            $hostname = trim((string) (
                $request->header('X-Terminal-Hostname')
                ?: $request->input('hostname')
                ?: ''
            ));
            $osPlatform = trim((string) (
                $request->header('X-Terminal-OS')
                ?: $request->input('os_platform')
                ?: ''
            ));
            $appVersion = trim((string) (
                $request->header('X-Terminal-Version')
                ?: $request->input('app_version')
                ?: ''
            ));
            $mode = trim((string) (
                $request->header('X-Terminal-Mode')
                ?: $request->input('mode')
                ?: 'terminal'
            ));

            try {
                $terminal = Terminal::where('terminal_uid', $terminalUid)->first();
                $userId = $request->user()?->id;
                $ipAddress = $request->ip();

                if (!$terminal) {
                    $setting = SchemaCache::hasTable('settings') ? Setting::first() : null;
                    $providedNetworkToken = trim((string) (
                        $request->header('X-Network-Token')
                        ?: $request->input('network_token')
                        ?: ''
                    ));
                    $storedNetworkToken = trim((string) ($setting?->network_server_token ?? ''));
                    $hasValidNetworkToken = $storedNetworkToken !== ''
                        && $providedNetworkToken !== ''
                        && hash_equals($storedNetworkToken, $providedNetworkToken);

                    if ($userId === null && !$hasValidNetworkToken) {
                        return $next($request);
                    }

                    $autoAuthorize = true;
                    if ($setting && SchemaCache::hasColumn('settings', 'terminal_auto_authorize') && $setting->terminal_auto_authorize !== null) {
                        $autoAuthorize = (bool) $setting->terminal_auto_authorize;
                    }

                    // Check license seat limit before authorizing a brand-new terminal
                    if ($autoAuthorize && !$this->hasAvailableLicenseSeat($terminalUid)) {
                        $autoAuthorize = false;
                    }

                    $count = Terminal::count() + 1;
                    $defaultName = $terminalName !== ''
                        ? $terminalName
                        : ($hostname !== '' ? "Counter ({$hostname})" : "Counter #{$count}");

                    $terminal = Terminal::create([
                        'terminal_uid'    => $terminalUid,
                        'name'            => $defaultName,
                        'hostname'        => $hostname ?: null,
                        'ip_address'      => $ipAddress,
                        'os_platform'     => $osPlatform ?: null,
                        'app_version'     => $appVersion ?: null,
                        'mode'            => in_array($mode, ['standalone', 'server', 'terminal', 'browser'], true) ? $mode : 'terminal',
                        'current_user_id' => $userId,
                        'is_authorized'   => $autoAuthorize,
                        'last_seen_at'    => now(),
                    ]);
                    SchemaCache::flush();
                } else {
                    // Throttle DB writes for heartbeat timestamps to once every 8 seconds unless user/IP/metadata changed
                    $cacheKey = "karobar:terminal:heartbeat:{$terminal->id}";
                    $needsDbUpdate = !Cache::has($cacheKey)
                        || ($userId !== null && $terminal->current_user_id !== $userId)
                        || ($ipAddress && $terminal->ip_address !== $ipAddress)
                        || ($terminalName !== '' && $terminal->name !== $terminalName && str_starts_with((string) $terminal->name, 'Counter'));

                    if ($needsDbUpdate) {
                        $updates = [
                            'last_seen_at' => now(),
                            'ip_address'   => $ipAddress ?: $terminal->ip_address,
                        ];
                        if ($userId !== null) {
                            $updates['current_user_id'] = $userId;
                        }
                        if ($hostname !== '') {
                            $updates['hostname'] = $hostname;
                        }
                        if ($osPlatform !== '') {
                            $updates['os_platform'] = $osPlatform;
                        }
                        if ($appVersion !== '') {
                            $updates['app_version'] = $appVersion;
                        }
                        if ($mode !== '' && in_array($mode, ['standalone', 'server', 'terminal', 'browser'], true)) {
                            $updates['mode'] = $mode;
                        }
                        $terminal->update($updates);
                        Cache::put($cacheKey, now()->timestamp, 8);
                    } else {
                        $terminal->last_seen_at = now();
                    }
                }

                // Record instant sub-second presence in Cache/Redis for the Server Dashboard
                Cache::put("karobar:terminal:online:{$terminal->id}", [
                    'terminal_id'  => $terminal->id,
                    'terminal_uid' => $terminal->terminal_uid,
                    'ip_address'   => $ipAddress,
                    'user_id'      => $userId ?? $terminal->current_user_id,
                    'seen_at'      => now()->toIso8601String(),
                    'seen_ts'      => now()->timestamp,
                ], 30);

                $request->attributes->set('terminal', $terminal);
                app()->instance('current_terminal', $terminal);

                // Allow ping/handshake and auth endpoints even if blocked so the terminal UI can show a clear message
                $path = $request->path();
                $isHandshakePath = str_contains($path, 'terminals/ping')
                    || str_contains($path, 'terminals/handshake')
                    || str_contains($path, 'login')
                    || str_contains($path, 'logout');

                if (!$terminal->is_authorized && !$isHandshakePath) {
                    return response()->json([
                        'message'       => "Terminal '{$terminal->name}' is not authorized or has been blocked by the Server Administrator.",
                        'code'          => 'TERMINAL_UNAUTHORIZED',
                        'terminal_uid'  => $terminal->terminal_uid,
                        'terminal_name' => $terminal->name,
                    ], 403);
                }
            } catch (Throwable) {
                // Never fail core POS requests if terminal tracking encounters a transient schema/cache issue
            }
        }

        $response = $next($request);

        try {
            if (
                in_array($request->method(), ['POST', 'PUT', 'PATCH', 'DELETE'], true) &&
                $response->getStatusCode() >= 200 &&
                $response->getStatusCode() < 400
            ) {
                $path = $request->path();
                $domainsToBump = [];

                if (preg_match('#(sale-invoices|purchase-invoices|sale-returns|purchase-returns|products|batches|stock-adjustments|stock-disposals|stocktake|user-demands)#', $path)) {
                    $domainsToBump[] = 'stock';
                    $domainsToBump[] = 'drawer';
                    $domainsToBump[] = 'drafts';
                }
                if (str_contains($path, 'invoice-drafts')) {
                    $domainsToBump[] = 'drafts';
                }
                if (preg_match('#(cash-drawer|payments|expenses|accounts)#', $path)) {
                    $domainsToBump[] = 'drawer';
                }
                if (str_contains($path, 'terminals') && !str_contains($path, 'terminals/ping')) {
                    $domainsToBump[] = 'terminals';
                }
                if (str_contains($path, 'settings')) {
                    $domainsToBump[] = 'settings';
                }

                if (!empty($domainsToBump)) {
                    \App\Services\SyncPulseService::bump(array_values(array_unique($domainsToBump)));
                }
            }
        } catch (Throwable) {
        }

        return $response;
    }

    protected function hasAvailableLicenseSeat(string $incomingUid): bool
    {
        try {
            $licenseSvc = app(LicenseService::class);
            $status = $licenseSvc->status();

            // Basic and Gold tiers do not include Server & Multi-PC Terminals (strictly 1 standalone PC)
            if (!empty($status['valid']) && !$licenseSvc->hasModule('server_terminals')) {
                $seats = 1;
            } else {
                $seats = (int) ($status['seats'] ?? 0);
                if ($seats <= 0) {
                    return true; // Unlimited Platinum license
                }
            }

            $activeCount = Terminal::where('is_authorized', true)
                ->where('terminal_uid', '!=', $incomingUid)
                ->where('last_seen_at', '>=', now()->subMinutes(15))
                ->count();

            return $activeCount < $seats;
        } catch (Throwable) {
            return true;
        }
    }
}
