<?php

namespace App\Services;

use App\Models\InvoiceDraft;
use App\Models\Terminal;
use App\Support\SchemaCache;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Cache;
use Throwable;

class SyncPulseService
{
    public const CACHE_KEY = 'karobar:sync_pulse:versions';

    /**
     * Increment one or more domain version counters so all connected POS terminals
     * immediately invalidate their local lookup caches on the next 3-second pulse.
     *
     * @param  string|array<int, string>  $domains  e.g. 'stock', 'drafts', 'drawer', 'terminals', 'settings'
     */
    public static function bump(string|array $domains = ['stock']): array
    {
        $versions = self::getVersions();
        $nowMs = (int) round(microtime(true) * 1000);

        foreach ((array) $domains as $domain) {
            $key = str_ends_with($domain, '_version') ? $domain : "{$domain}_version";
            $versions[$key] = max(((int) ($versions[$key] ?? 1)) + 1, $nowMs);
        }

        $versions['updated_at_ms'] = $nowMs;
        Cache::forever(self::CACHE_KEY, $versions);

        return $versions;
    }

    /**
     * Read current domain version counters from Cache/Redis.
     */
    public static function getVersions(): array
    {
        $defaults = [
            'stock_version'     => 1,
            'drafts_version'    => 1,
            'drawer_version'    => 1,
            'terminals_version' => 1,
            'settings_version'  => 1,
            'updated_at_ms'     => 1,
        ];

        try {
            $cached = Cache::get(self::CACHE_KEY);
            if (is_array($cached)) {
                return array_merge($defaults, $cached);
            }
        } catch (Throwable) {
        }

        return $defaults;
    }

    /**
     * Build the lightweight (< 2ms) real-time sync pulse payload for connected terminals.
     */
    public static function getPulsePayload(?Request $request = null): array
    {
        $versions = self::getVersions();
        $currentTerminal = Terminal::current($request);

        // Cache aggregate counts for 3 seconds so 20 terminals polling every 3s execute at most 1 count query per 3s
        $counts = Cache::remember('karobar:sync_pulse:counts', 3, function () {
            $onlineTerminals = 0;
            $openDrafts = 0;

            try {
                if (SchemaCache::hasTable('terminals')) {
                    $onlineTerminals = Terminal::where('is_authorized', true)
                        ->where('last_seen_at', '>=', now()->subSeconds(25))
                        ->count();
                }
            } catch (Throwable) {
            }

            try {
                if (SchemaCache::hasTable('invoice_drafts')) {
                    $openDrafts = InvoiceDraft::where('status', 'draft')->count();
                }
            } catch (Throwable) {
            }

            return [
                'online_terminals_count' => max(1, $onlineTerminals),
                'open_held_bills_count'  => $openDrafts,
            ];
        });

        return [
            'ok'                     => true,
            'server_time'            => now()->toIso8601String(),
            'versions'               => $versions,
            'online_terminals_count' => $counts['online_terminals_count'],
            'open_held_bills_count'  => $counts['open_held_bills_count'],
            'terminal'               => $currentTerminal ? [
                'id'            => $currentTerminal->id,
                'terminal_uid'  => $currentTerminal->terminal_uid,
                'name'          => $currentTerminal->name,
                'mode'          => $currentTerminal->mode,
                'is_authorized' => (bool) $currentTerminal->is_authorized,
            ] : null,
        ];
    }
}
