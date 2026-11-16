<?php

namespace App\Http\Controllers;

use App\Models\CashDrawerSession;
use App\Models\SaleInvoice;
use App\Models\Setting;
use App\Models\Terminal;
use App\Services\LicenseService;
use App\Services\LocalWhatsAppAgentService;
use App\Support\SchemaCache;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Throwable;

class TerminalController extends Controller
{
    /**
     * GET /api/terminals
     * Returns server health, LAN network endpoints, license seat usage, and live attached terminals list.
     */
    public function index(Request $request): JsonResponse
    {
        $terminals = [];
        $today = now()->toDateString();

        if (SchemaCache::hasTable('terminals')) {
            $rows = Terminal::with(['currentUser:id,name,email'])
                ->orderByDesc('is_authorized')
                ->orderByDesc('last_seen_at')
                ->orderBy('id')
                ->get();

            // Preload open cash drawer sessions per terminal
            $openDrawersByTerminal = collect();
            $openDrawersByUser = collect();
            if (SchemaCache::hasTable('cash_drawer_sessions')) {
                $openSessions = CashDrawerSession::with('user:id,name')
                    ->where('status', 'open')
                    ->get();

                if (SchemaCache::hasColumn('cash_drawer_sessions', 'terminal_id')) {
                    $openDrawersByTerminal = $openSessions->whereNotNull('terminal_id')->keyBy('terminal_id');
                }
                $openDrawersByUser = $openSessions->whereNull('terminal_id')->keyBy('user_id');
            }

            // Preload today's invoice count & revenue per terminal
            $todayStatsByTerminal = collect();
            if (SchemaCache::hasTable('sale_invoices') && SchemaCache::hasColumn('sale_invoices', 'terminal_id')) {
                $todayStatsByTerminal = SaleInvoice::query()
                    ->whereDate('date', $today)
                    ->whereNotNull('terminal_id')
                    ->selectRaw('terminal_id, COUNT(*) as invoices_count, COALESCE(SUM(total), 0) as sales_total')
                    ->groupBy('terminal_id')
                    ->get()
                    ->keyBy('terminal_id');
            }

            foreach ($rows as $t) {
                $cachedPresence = Cache::get("karobar:terminal:online:{$t->id}");
                $isOnline = ($cachedPresence !== null) || $t->isOnline();

                $drawer = $openDrawersByTerminal->get($t->id)
                    ?? ($t->current_user_id ? $openDrawersByUser->get($t->current_user_id) : null);

                $todayStat = $todayStatsByTerminal->get($t->id);

                $terminals[] = [
                    'id'                   => $t->id,
                    'terminal_uid'         => $t->terminal_uid,
                    'name'                 => $t->name,
                    'hostname'             => $t->hostname,
                    'ip_address'           => $cachedPresence['ip_address'] ?? $t->ip_address,
                    'os_platform'          => $t->os_platform,
                    'app_version'          => $t->app_version,
                    'mode'                 => $t->mode,
                    'is_authorized'        => (bool) $t->is_authorized,
                    'is_online'            => $isOnline,
                    'last_seen_at'         => $cachedPresence['seen_at'] ?? $t->last_seen_at?->toIso8601String(),
                    'last_seen_human'      => $isOnline ? 'Online now' : ($t->last_seen_at ? $t->last_seen_at->diffForHumans() : 'Never'),
                    'current_user'         => $t->currentUser ? [
                        'id'    => $t->currentUser->id,
                        'name'  => $t->currentUser->name,
                        'email' => $t->currentUser->email,
                    ] : null,
                    'open_drawer'          => $drawer ? [
                        'id'               => $drawer->id,
                        'opened_at'        => $drawer->opened_at?->toIso8601String(),
                        'opening_balance'  => (float) $drawer->opening_balance,
                        'expected_balance' => (float) $drawer->closing_balance_expected,
                        'cashier_name'     => $drawer->user?->name,
                    ] : null,
                    'today_invoices_count' => (int) ($todayStat->invoices_count ?? 0),
                    'today_sales_total'    => round((float) ($todayStat->sales_total ?? 0), 2),
                ];
            }
        }

        return response()->json([
            'server'           => $this->buildServerHealthSummary($request, $terminals),
            'current_terminal' => Terminal::current($request),
            'terminals'        => $terminals,
        ]);
    }

    /**
     * POST /api/terminals/ping
     * Public/Authenticated handshake endpoint used by Windows Terminal PCs ("Test Connection") and heartbeat checks.
     */
    public function ping(Request $request): JsonResponse
    {
        $terminal = Terminal::current($request);
        $setting = SchemaCache::hasTable('settings') ? Setting::first() : null;

        return response()->json([
            'ok'            => true,
            'app'           => 'Karobar-App ERP Server',
            'store_name'    => $setting?->store_name ?: config('app.name', 'Karobar App'),
            'server_time'   => now()->toIso8601String(),
            'db_driver'     => DB::getDriverName(),
            'os_family'     => PHP_OS_FAMILY,
            'php_version'   => PHP_VERSION,
            'terminal'      => $terminal ? [
                'id'            => $terminal->id,
                'terminal_uid'  => $terminal->terminal_uid,
                'name'          => $terminal->name,
                'mode'          => $terminal->mode,
                'is_authorized' => (bool) $terminal->is_authorized,
            ] : null,
        ]);
    }

    /**
     * PUT /api/terminals/{id}
     * Update terminal name, authorization state, or metadata.
     */
    public function update(Request $request, int $id): JsonResponse
    {
        $user = $request->user();
        if (!$user || (!$user->hasRole(['Admin', 'admin', 'super-admin']) && !$user->can('settings.update'))) {
            abort(403, 'Only administrators can manage terminals.');
        }

        $terminal = Terminal::findOrFail($id);

        $data = $request->validate([
            'name'          => 'sometimes|required|string|max:120',
            'is_authorized' => 'sometimes|boolean',
            'mode'          => 'sometimes|string|in:standalone,server,terminal,browser',
        ]);

        $terminal->update($data);

        return response()->json([
            'message'  => 'Terminal updated successfully.',
            'terminal' => $terminal->fresh(['currentUser:id,name,email']),
        ]);
    }

    /**
     * DELETE /api/terminals/{id}
     * Remove a decommissioned or stale terminal record.
     */
    public function destroy(Request $request, int $id): JsonResponse
    {
        $user = $request->user();
        if (!$user || (!$user->hasRole(['Admin', 'admin', 'super-admin']) && !$user->can('settings.update'))) {
            abort(403, 'Only administrators can remove terminals.');
        }

        $terminal = Terminal::findOrFail($id);
        Cache::forget("karobar:terminal:online:{$terminal->id}");
        Cache::forget("karobar:terminal:heartbeat:{$terminal->id}");
        $terminal->delete();

        return response()->json([
            'message' => 'Terminal removed successfully.',
        ]);
    }

    /**
     * PUT /api/terminals/settings/network
     * Update server network policies (auto-authorize new terminals, rotate pairing token).
     */
    public function updateNetworkSettings(Request $request): JsonResponse
    {
        $user = $request->user();
        if (!$user || (!$user->hasRole(['Admin', 'admin', 'super-admin']) && !$user->can('settings.update'))) {
            abort(403, 'Only administrators can update network settings.');
        }

        $data = $request->validate([
            'terminal_auto_authorize' => 'sometimes|boolean',
            'regenerate_token'        => 'sometimes|boolean',
        ]);

        $setting = Setting::firstOrCreate(['id' => 1]);
        if (array_key_exists('terminal_auto_authorize', $data) && SchemaCache::hasColumn('settings', 'terminal_auto_authorize')) {
            $setting->terminal_auto_authorize = (bool) $data['terminal_auto_authorize'];
        }
        if (!empty($data['regenerate_token']) && SchemaCache::hasColumn('settings', 'network_server_token')) {
            $setting->network_server_token = 'KRB-NET-' . strtoupper(Str::random(12));
        }
        $setting->save();

        return response()->json([
            'message'                 => 'Network settings updated.',
            'terminal_auto_authorize' => (bool) ($setting->terminal_auto_authorize ?? true),
            'network_server_token'    => $setting->network_server_token ?? null,
        ]);
    }

    /**
     * GET|POST /api/server/export-database?format=sqlite|sql|zip
     * 1-Click Database Export from the Linux Server & Terminals Dashboard.
     * Supports portable .sqlite export (even when server runs MariaDB/MySQL/PostgreSQL),
     * compressed .sql.gz dump, or full .zip archive.
     */
    public function exportDatabase(Request $request)
    {
        $user = $request->user();
        if (
            !$user ||
            (!$user->hasRole(['Admin', 'admin', 'super-admin']) &&
                !$user->can('backup.create') &&
                !$user->can('backup.download') &&
                !$user->can('settings.update'))
        ) {
            abort(403, 'Only administrators can export the database.');
        }

        $format = strtolower((string) $request->input('format', 'sqlite'));

        if ($format === 'sqlite') {
            $exportService = app(\App\Services\SqliteExportService::class);
            $sqlitePath = $exportService->exportToSqliteFile();
            $downloadName = 'karobar-database-' . now()->format('Y-m-d_His') . '.sqlite';

            return response()->download($sqlitePath, $downloadName, [
                'Content-Type' => 'application/vnd.sqlite3',
            ])->deleteFileAfterSend(true);
        }

        $backupService = app(\App\Services\BackupService::class);
        $backupType = $format === 'zip' ? 'full' : 'database';
        $result = $backupService->createBackup($backupType, 'Server Dashboard 1-Click Export');

        $filePath = $result['path'] ?? null;
        if (!$filePath || !is_file($filePath)) {
            return response()->json([
                'message' => 'Failed to generate database export file.',
            ], 500);
        }

        return response()->download($filePath, basename($filePath));
    }

    /**
     * POST /api/server/quick-import-database
     * 1-Click Database Import from the Linux Server & Terminals Dashboard.
     * Accepts .sqlite / .db (runs topological SqliteImportService into MariaDB/MySQL/SQLite)
     * or .sql / .sql.gz / .zip (runs BackupService restore).
     */
    public function quickImportDatabase(Request $request): JsonResponse
    {
        $user = $request->user();
        if (
            !$user ||
            (!$user->hasRole(['Admin', 'admin', 'super-admin']) &&
                !$user->can('backup.restore') &&
                !$user->can('settings.update'))
        ) {
            abort(403, 'Only administrators can import a database.');
        }

        $request->validate([
            'file'             => 'required|file|max:512000',
            'mode'             => 'nullable|string|in:replace,merge',
            'conflict_policy'  => 'nullable|string|in:update,skip',
            'include_settings' => 'nullable|boolean',
        ]);

        $file = $request->file('file');
        $origName = strtolower((string) $file->getClientOriginalName());
        $mode = (string) $request->input('mode', 'replace');
        $conflict = (string) $request->input('conflict_policy', 'update');
        $includeSettings = $request->boolean('include_settings', false);

        $backupService = app(\App\Services\BackupService::class);
        try {
            $backupService->createBackup('database', 'Pre-Import Automatic Safety Snapshot');
        } catch (Throwable) {
        }

        // If .sql, .sql.gz, or .zip archive -> restore via BackupService
        if (
            str_ends_with($origName, '.sql') ||
            str_ends_with($origName, '.sql.gz') ||
            str_ends_with($origName, '.zip')
        ) {
            $restoreRes = $backupService->uploadAndRestore($file);
            \App\Services\SyncPulseService::bump(['stock', 'drafts', 'drawer', 'settings']);

            return response()->json([
                'ok'      => true,
                'type'    => 'backup_restore',
                'message' => $restoreRes['message'] ?? 'Database backup restored successfully.',
                'details' => $restoreRes,
            ]);
        }

        // Otherwise (.sqlite, .sqlite3, .db) -> run topological SqliteImportService into active DB (MariaDB/MySQL/SQLite)
        $importService = app(\App\Services\SqliteImportService::class);
        $staged = $importService->storeUploadedSqlite($file);
        $token = $staged['token'];
        $inspection = $importService->inspectAndProposeMapping($token);
        $sourceTables = $inspection['tables'] ?? [];

        $tablesConfig = [];
        foreach ($sourceTables as $srcTbl) {
            $srcName = $srcTbl['source_table'] ?? null;
            $targetTable = $srcTbl['target_table'] ?? null;
            if (!$srcName || !$targetTable) {
                continue;
            }
            $enabled = (bool) ($srcTbl['enabled_by_default'] ?? true);
            if ($targetTable === 'settings' && !$includeSettings) {
                $enabled = false;
            }
            $tablesConfig[$srcName] = [
                'enabled'      => $enabled,
                'target_table' => $targetTable,
                'column_map'   => [],
            ];
        }

        $result = $importService->executeImport($token, [
            'mode'              => $mode,
            'conflict_policy'   => $conflict === 'update' ? 'upsert' : 'skip',
            'create_backup'     => false,
            'reconcile_batches' => true,
            'tables'            => $tablesConfig,
        ], $user->id);

        \App\Services\SyncPulseService::bump(['stock', 'drafts', 'drawer', 'settings']);

        $summary = $result['summary'] ?? [];
        $inserted = (int) ($summary['rows_inserted'] ?? ($result['total_inserted'] ?? 0));
        $updated = (int) ($summary['rows_updated'] ?? ($result['total_updated'] ?? 0));
        $skipped = (int) ($summary['rows_skipped'] ?? ($result['total_skipped'] ?? 0));

        return response()->json([
            'ok'             => true,
            'type'           => 'sqlite_topological_import',
            'message'        => sprintf(
                'Database imported successfully! Inserted %d rows, Updated %d rows.',
                $inserted,
                $updated
            ),
            'total_inserted' => $inserted,
            'total_updated'  => $updated,
            'total_skipped'  => $skipped,
            'details'        => $result,
        ]);
    }

    protected function buildServerHealthSummary(Request $request, array $terminals): array
    {
        $driver = DB::getDriverName();
        $dbVersion = 'Unknown';
        try {
            if ($driver === 'sqlite') {
                $dbVersion = 'SQLite ' . (DB::selectOne('SELECT sqlite_version() as v')->v ?? '');
            } elseif (in_array($driver, ['mysql', 'mariadb'], true)) {
                $dbVersion = (string) (DB::selectOne('SELECT VERSION() as v')->v ?? 'MariaDB/MySQL');
            } elseif ($driver === 'pgsql') {
                $dbVersion = (string) (DB::selectOne('SHOW server_version')->server_version ?? 'PostgreSQL');
            }
        } catch (Throwable) {
        }

        $lanIps = $this->detectLanIpv4Addresses();
        $port = $request->getPort() ?: 8000;
        $scheme = $request->getScheme() ?: 'http';
        $primaryIp = $lanIps[0] ?? ($request->server('SERVER_ADDR') ?: '127.0.0.1');
        $recommendedUrl = "{$scheme}://{$primaryIp}" . (in_array((int) $port, [80, 443], true) ? '' : ":{$port}");

        $licenseStatus = [];
        try {
            $licenseStatus = app(LicenseService::class)->status();
        } catch (Throwable) {
        }

        $whatsappStatus = ['reachable' => false];
        try {
            $whatsappStatus = app(LocalWhatsAppAgentService::class)->status();
        } catch (Throwable) {
        }

        $setting = SchemaCache::hasTable('settings') ? Setting::first() : null;
        if ($setting && SchemaCache::hasColumn('settings', 'network_server_token') && empty($setting->network_server_token)) {
            try {
                $setting->network_server_token = 'KRB-NET-' . strtoupper(Str::random(12));
                $setting->save();
            } catch (Throwable) {
            }
        }

        $onlineCount = collect($terminals)->where('is_online', true)->count();
        $authorizedCount = collect($terminals)->where('is_authorized', true)->count();
        $seatsLimit = (int) ($licenseStatus['seats'] ?? 0);

        $user = $request->user();
        $isServerAdmin = $user && (
            $user->hasRole(['Admin', 'admin', 'super-admin']) ||
            $user->can('settings.update')
        );

        return [
            'hostname'                => gethostname() ?: php_uname('n'),
            'os_family'               => PHP_OS_FAMILY,
            'os_release'              => php_uname('s') . ' ' . php_uname('r'),
            'php_version'             => PHP_VERSION,
            'db_driver'               => $driver,
            'db_version'              => $dbVersion,
            'cache_driver'            => config('cache.default'),
            'session_driver'          => config('session.driver'),
            'lan_ips'                 => $lanIps,
            'primary_ip'              => $primaryIp,
            'port'                    => (int) $port,
            'recommended_url'         => $recommendedUrl,
            'online_terminals_count'  => $onlineCount,
            'total_terminals_count'   => count($terminals),
            'authorized_count'        => $authorizedCount,
            'seats_limit'             => $seatsLimit > 0 ? $seatsLimit : null,
            'terminal_auto_authorize' => (bool) ($setting?->terminal_auto_authorize ?? true),
            'network_server_token'    => $isServerAdmin ? ($setting?->network_server_token ?? null) : null,
            'services'                => [
                'database'       => true,
                'cache'          => true,
                'whatsapp_agent' => (bool) ($whatsappStatus['reachable'] ?? false),
            ],
        ];
    }

    protected function detectLanIpv4Addresses(): array
    {
        $ips = [];

        try {
            if (PHP_OS_FAMILY === 'Linux') {
                $out = @shell_exec('hostname -I 2>/dev/null');
                if (is_string($out) && trim($out) !== '') {
                    foreach (preg_split('/\s+/', trim($out)) as $candidate) {
                        if (filter_var($candidate, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4) && !str_starts_with($candidate, '127.') && !str_starts_with($candidate, '172.17.')) {
                            $ips[] = $candidate;
                        }
                    }
                }
            } elseif (PHP_OS_FAMILY === 'Darwin') {
                $out = @shell_exec('ifconfig 2>/dev/null');
                if (is_string($out) && preg_match_all('/inet\s+(\d+\.\d+\.\d+\.\d+)/', $out, $matches)) {
                    foreach ($matches[1] as $candidate) {
                        if (!str_starts_with($candidate, '127.')) {
                            $ips[] = $candidate;
                        }
                    }
                }
            } elseif (PHP_OS_FAMILY === 'Windows') {
                $out = @shell_exec('ipconfig 2>nul');
                if (is_string($out) && preg_match_all('/IPv4[^:]*:\s*(\d+\.\d+\.\d+\.\d+)/i', $out, $matches)) {
                    foreach ($matches[1] as $candidate) {
                        if (!str_starts_with($candidate, '127.')) {
                            $ips[] = $candidate;
                        }
                    }
                }
            }
        } catch (Throwable) {
        }

        if (empty($ips)) {
            $hostIp = @gethostbyname(gethostname() ?: 'localhost');
            if (is_string($hostIp) && filter_var($hostIp, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4) && !str_starts_with($hostIp, '127.')) {
                $ips[] = $hostIp;
            }
        }

        return array_values(array_unique($ips));
    }

    /**
     * Ensure the authenticated user is an Admin or holds settings.update permission.
     */
    private function authorizeServerAdmin(Request $request, string $message): void
    {
        $user = $request->user();
        if (
            !$user ||
            (!$user->hasRole(['Admin', 'admin', 'super-admin']) &&
                !$user->can('settings.update'))
        ) {
            abort(403, $message);
        }
    }

    /**
     * GET /api/server/diagnostics
     * Full-featured Server & LAN Troubleshooter diagnostic suite.
     */
    public function diagnostics(Request $request): JsonResponse
    {
        $this->authorizeServerAdmin($request, 'Only administrators can access server diagnostics.');

        $checks = [];

        // 1. Database Engine & Latency Check
        $dbStart = microtime(true);
        $driver = DB::getDriverName();
        $dbOk = true;
        $dbDetail = '';
        try {
            DB::select('SELECT 1');
            $dbLatencyMs = round((microtime(true) - $dbStart) * 1000, 2);
            $tablesCount = count(\Illuminate\Support\Facades\Schema::getTables());
            $dbDetail = strtoupper($driver) . " connected ({$dbLatencyMs} ms, {$tablesCount} tables)";
        } catch (Throwable $e) {
            $dbOk = false;
            $dbLatencyMs = 0;
            $dbDetail = 'Database Error: ' . $e->getMessage();
        }
        $checks[] = [
            'id'         => 'database',
            'title'      => 'Database Engine & Row Locking',
            'status'     => $dbOk ? ($driver === 'sqlite' ? 'warn' : 'ok') : 'error',
            'latency_ms' => $dbLatencyMs,
            'summary'    => $dbDetail,
            'hint'       => $driver === 'sqlite'
                ? 'Running on SQLite (ideal for 1-PC Standalone). For Multi-PC Linux Server, MariaDB 10.11 (InnoDB) is recommended.'
                : 'MariaDB / MySQL InnoDB row-level locking active for multi-counter concurrency.',
        ];

        // 2. Cache & Real-Time SyncPulse Check
        $cacheStart = microtime(true);
        $cacheDriver = (string) config('cache.default');
        $cacheOk = true;
        try {
            $probeKey = 'karobar:diag:probe';
            Cache::put($probeKey, 'ok', 10);
            $cacheOk = Cache::get($probeKey) === 'ok';
            $cacheLatencyMs = round((microtime(true) - $cacheStart) * 1000, 2);
        } catch (Throwable $e) {
            $cacheOk = false;
            $cacheLatencyMs = 0;
        }
        $pulseVersions = \App\Services\SyncPulseService::getVersions();
        $checks[] = [
            'id'         => 'cache_sync',
            'title'      => 'Real-Time SyncPulse & Cache Engine',
            'status'     => $cacheOk ? 'ok' : 'error',
            'latency_ms' => $cacheLatencyMs,
            'summary'    => "Driver: {$cacheDriver} ({$cacheLatencyMs} ms) • Stock v{$pulseVersions['stock_version']} • Drafts v{$pulseVersions['drafts_version']}",
            'hint'       => 'Broadcasts 3-second stock, held-bill, and cash-drawer updates to all attached POS terminals.',
        ];

        // 3. Atomic Document Sequence Engine Check
        $seqCount = SchemaCache::hasTable('document_sequences')
            ? \App\Models\DocumentSequence::count()
            : 0;
        $seqTableExists = SchemaCache::hasTable('document_sequences');
        $checks[] = [
            'id'      => 'sequences',
            'title'   => 'Atomic Document Sequence Allocator',
            'status'  => $seqTableExists ? 'ok' : 'error',
            'summary' => $seqTableExists
                ? "Active ({$seqCount} sequence counters initialized with SELECT ... FOR UPDATE locking)"
                : 'Missing document_sequences table! Run "Run Pending Migrations" below.',
            'hint'    => 'Prevents duplicate SI / SLRET / PRINV / PRRET /CR / EXP / JV numbers when multiple counters bill simultaneously.',
        ];

        // 4. Storage Permissions & Free Disk Space Check
        $pathsToCheck = [
            'storage/logs'            => storage_path('logs'),
            'storage/app/backups'     => storage_path('app/backups'),
            'storage/framework/cache' => storage_path('framework/cache'),
            'bootstrap/cache'         => base_path('bootstrap/cache'),
        ];
        $unwritable = [];
        foreach ($pathsToCheck as $label => $dirPath) {
            if (!is_dir($dirPath)) {
                @mkdir($dirPath, 0775, true);
            }
            if (!is_writable($dirPath)) {
                $unwritable[] = $label;
            }
        }
        $freeBytes = @disk_free_space(base_path()) ?: 0;
        $totalBytes = @disk_total_space(base_path()) ?: 1;
        $freeGb = round($freeBytes / (1024 * 1024 * 1024), 2);
        $totalGb = round($totalBytes / (1024 * 1024 * 1024), 2);
        $usedPct = round((1 - ($freeBytes / max($totalBytes, 1))) * 100, 1);

        $checks[] = [
            'id'      => 'storage',
            'title'   => 'Disk Space & Storage Permissions',
            'status'  => empty($unwritable) && $freeGb > 1.0 ? 'ok' : (empty($unwritable) ? 'warn' : 'error'),
            'summary' => empty($unwritable)
                ? "All directories writable • {$freeGb} GB free of {$totalGb} GB ({$usedPct}% used)"
                : 'Unwritable directories: ' . implode(', ', $unwritable),
            'hint'    => 'Required for database backups, portable SQLite exports, invoice PDFs, and session storage.',
        ];

        // 5. PHP Extensions & Runtime Check
        $requiredExts = ['pdo', 'sqlite3', 'pdo_sqlite', 'mbstring', 'openssl', 'json', 'curl', 'zip'];
        $missingExts = array_values(array_filter($requiredExts, fn ($ext) => !extension_loaded($ext)));
        $hasMysqlExt = extension_loaded('pdo_mysql');
        $hasRedisExt = extension_loaded('redis');
        $checks[] = [
            'id'      => 'php_runtime',
            'title'   => 'PHP Runtime & Extensions',
            'status'  => empty($missingExts) ? 'ok' : 'error',
            'summary' => sprintf(
                'PHP %s • Memory: %s • Upload Max: %s • PDO_MySQL: %s • Redis Ext: %s',
                PHP_VERSION,
                ini_get('memory_limit'),
                ini_get('upload_max_filesize'),
                $hasMysqlExt ? 'Yes' : 'No',
                $hasRedisExt ? 'Yes' : 'No'
            ),
            'hint'    => empty($missingExts)
                ? 'All core PHP extensions for ERP, SQLite Import/Export, and LAN sync are loaded.'
                : 'Missing extensions: ' . implode(', ', $missingExts),
        ];

        // 6. LAN Network & Firewall Endpoint Check
        $lanIps = $this->detectLanIpv4Addresses();
        $port = $request->getPort() ?: 8000;
        $primaryIp = $lanIps[0] ?? '127.0.0.1';
        $hostname = gethostname() ?: 'karobar-server';
        $checks[] = [
            'id'      => 'network',
            'title'   => 'LAN Network & mDNS Discovery',
            'status'  => !empty($lanIps) ? 'ok' : 'warn',
            'summary' => !empty($lanIps)
                ? 'LAN IPs: ' . implode(', ', $lanIps) . " (Port {$port}) • mDNS: http://{$hostname}.local:{$port}"
                : "No external LAN IPv4 detected (bound to 127.0.0.1:{$port}). Check Ethernet/Wi-Fi cable.",
            'hint'    => "Ensure TCP port {$port} is allowed in firewall (sudo ufw allow {$port}/tcp) so Windows Terminal PCs can connect.",
        ];

        // Read recent server error logs (last 60 lines)
        $logPath = storage_path('logs/laravel.log');
        $recentLogs = [];
        $logSizeKb = 0;
        if (is_file($logPath) && is_readable($logPath)) {
            $logSizeKb = round((@filesize($logPath) ?: 0) / 1024, 1);
            $lines = @file($logPath, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES);
            if (is_array($lines)) {
                $recentLogs = array_slice($lines, -60);
            }
        }

        // Sequence table snapshot
        $sequences = SchemaCache::hasTable('document_sequences')
            ? \App\Models\DocumentSequence::orderBy('sequence_key')->get(['sequence_key', 'prefix', 'last_number', 'padding', 'updated_at'])
            : [];

        return response()->json([
            'ok'           => true,
            'checked_at'   => now()->toIso8601String(),
            'checks'       => $checks,
            'sequences'    => $sequences,
            'log_size_kb'  => $logSizeKb,
            'recent_logs'  => $recentLogs,
            'cli_commands' => [
                'status'        => 'sudo karobar-ctl status',
                'terminals'     => 'sudo karobar-ctl terminals',
                'import_sqlite' => 'sudo karobar-ctl import-sqlite /path/to/database.sqlite',
                'export_sqlite' => 'sudo karobar-ctl export-sqlite /tmp/karobar-portable.sqlite',
                'restart'       => 'sudo karobar-ctl restart',
                'logs'          => 'sudo karobar-ctl logs',
            ],
        ]);
    }

    /**
     * POST /api/server/troubleshoot
     * Execute 1-click self-healing & troubleshooting repairs from the Server Dashboard.
     */
    public function troubleshoot(Request $request): JsonResponse
    {
        $user = $request->user();
        if (!$user || (!$user->hasRole(['Admin', 'admin', 'super-admin']) && !$user->can('settings.update'))) {
            abort(403, 'Only administrators can run server troubleshooting actions.');
        }

        $data = $request->validate([
            'action' => 'required|string|in:resync_sequences,force_sync_pulse,clear_caches,run_migrations,prune_offline_terminals,clear_logs',
        ]);

        $action = $data['action'];

        switch ($action) {
            case 'resync_sequences':
                $seqService = app(\App\Services\DocumentSequenceService::class);
                $targets = [
                    ['sale_invoice', 'SI-', 6, 'sale_invoices', 'posted_number'],
                    ['sale_return', 'SLRET-', 6, 'sale_returns', 'posted_number'],
                    ['purchase_invoice', 'PRINV-', 6, 'purchase_invoices', 'posted_number'],
                    ['purchase_return', 'PRRET-', 6, 'purchase_returns', 'posted_number'],
                    ['stock_adjustment', 'STADJ-', 6, 'stock_adjustments', 'posted_number'],
                ];
                $synced = [];
                foreach ($targets as [$key, $prefix, $pad, $table, $col]) {
                    if (!SchemaCache::hasTable($table)) {
                        continue;
                    }
                    // Preview seeds or checks against table max
                    $nextPreview = $seqService->preview($key, $prefix, $pad, $table, $col);
                    $num = (int) preg_replace('/\D+/', '', $nextPreview);
                    $currentVal = max(0, $num - 1);
                    \App\Models\DocumentSequence::updateOrCreate(
                        ['sequence_key' => $key],
                        ['prefix' => $prefix, 'padding' => $pad, 'last_number' => $currentVal]
                    );
                    $synced[$key] = $nextPreview;
                }
                return response()->json([
                    'ok'      => true,
                    'action'  => $action,
                    'message' => 'All document sequences verified and synchronized with live invoice tables.',
                    'details' => $synced,
                ]);

            case 'force_sync_pulse':
                $versions = \App\Services\SyncPulseService::bump(['stock', 'drafts', 'drawer', 'terminals', 'settings']);
                return response()->json([
                    'ok'       => true,
                    'action'   => $action,
                    'message'  => 'Broadcasted Force-Sync Pulse to all connected LAN terminals.',
                    'versions' => $versions,
                ]);

            case 'clear_caches':
                SchemaCache::flush();
                try {
                    \Illuminate\Support\Facades\Artisan::call('optimize:clear');
                } catch (Throwable) {
                }
                \App\Services\SyncPulseService::bump(['stock', 'drafts', 'drawer', 'terminals', 'settings']);
                return response()->json([
                    'ok'      => true,
                    'action'  => $action,
                    'message' => 'Cleared application, route, view, config, and schema caches.',
                ]);

            case 'run_migrations':
                \Illuminate\Support\Facades\Artisan::call('migrate', ['--force' => true]);
                $output = trim(\Illuminate\Support\Facades\Artisan::output());
                SchemaCache::flush();
                return response()->json([
                    'ok'      => true,
                    'action'  => $action,
                    'message' => 'Database migrations executed successfully.',
                    'output'  => $output ?: 'Nothing to migrate (all tables up to date).',
                ]);

            case 'prune_offline_terminals':
                $currentTerm = Terminal::current($request);
                $deleted = 0;
                if (SchemaCache::hasTable('terminals')) {
                    $allTerminals = Terminal::all();
                    foreach ($allTerminals as $t) {
                        if ($currentTerm && $t->id === $currentTerm->id) {
                            continue;
                        }
                        $isOnline = Cache::has("karobar:terminal:online:{$t->id}") || $t->isOnline();
                        if (!$isOnline) {
                            Cache::forget("karobar:terminal:online:{$t->id}");
                            Cache::forget("karobar:terminal:heartbeat:{$t->id}");
                            $t->delete();
                            $deleted++;
                        }
                    }
                }
                \App\Services\SyncPulseService::bump('terminals');
                return response()->json([
                    'ok'      => true,
                    'action'  => $action,
                    'message' => "Removed {$deleted} offline terminal record(s) from registry.",
                ]);

            case 'clear_logs':
                $logPath = storage_path('logs/laravel.log');
                if (is_file($logPath) && is_writable($logPath)) {
                    @file_put_contents($logPath, '');
                }
                return response()->json([
                    'ok'      => true,
                    'action'  => $action,
                    'message' => 'Server error log cleared.',
                ]);
        }

        return response()->json(['ok' => false, 'message' => 'Unknown action.'], 422);
    }

    /**
     * GET /api/server/terminal-config?name=Counter+2
     * Download a pre-configured network-config.json for rapid Windows Terminal PC setup.
     */
    public function downloadTerminalConfig(Request $request)
    {
        $this->authorizeServerAdmin($request, 'Only administrators can download terminal configuration.');

        $lanIps = $this->detectLanIpv4Addresses();
        $port = $request->getPort() ?: 8000;
        $scheme = $request->getScheme() ?: 'http';
        $primaryIp = $lanIps[0] ?? ($request->server('SERVER_ADDR') ?: '127.0.0.1');
        $serverUrl = "{$scheme}://{$primaryIp}" . (in_array((int) $port, [80, 443], true) ? '' : ":{$port}");

        $terminalName = trim((string) $request->query('name', 'Counter 2'));
        if ($terminalName === '') {
            $terminalName = 'Counter 2';
        }

        $setting = SchemaCache::hasTable('settings') ? Setting::first() : null;
        $payload = [
            'mode'         => 'terminal',
            'serverUrl'    => $serverUrl,
            'terminalId'   => 'TERM-' . strtoupper(Str::random(8)),
            'terminalName' => $terminalName,
            'pairingToken' => $setting?->network_server_token ?? null,
            'generatedAt'  => now()->toIso8601String(),
            'instructions' => 'Place this file at %APPDATA%\\Karobar App\\network-config.json on the Windows Terminal PC, or enter serverUrl during the Karobar App Windows Installer.',
        ];

        $slug = Str::slug($terminalName) ?: 'terminal';
        $filename = "network-config-{$slug}.json";

        return response()->streamDownload(function () use ($payload) {
            echo json_encode($payload, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES);
        }, $filename, [
            'Content-Type' => 'application/json',
        ]);
    }
}
