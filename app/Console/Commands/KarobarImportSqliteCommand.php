<?php

namespace App\Console\Commands;

use App\Services\BackupService;
use App\Services\SqliteImportService;
use Illuminate\Console\Command;
use Illuminate\Http\UploadedFile;
use Throwable;

class KarobarImportSqliteCommand extends Command
{
    protected $signature = 'karobar:import-sqlite
                            {file : Path to the source .sqlite / .db file to import}
                            {--mode=replace : Import mode: replace (clean business tables first) or merge}
                            {--conflict=update : Conflict policy: update or skip}
                            {--include-settings : Also import store settings preferences}
                            {--no-backup : Skip creating a pre-import safety backup}';

    protected $description = 'Import an existing Standalone/Herd SQLite database into the active Karobar-App database (MariaDB, MySQL, PostgreSQL, or SQLite)';

    public function handle(SqliteImportService $importService, BackupService $backupService): int
    {
        $filePath = (string) $this->argument('file');
        if (!is_file($filePath) || !is_readable($filePath)) {
            $this->error("Source SQLite file not found or not readable: {$filePath}");
            return self::FAILURE;
        }

        $mode = in_array($this->option('mode'), ['replace', 'merge'], true)
            ? (string) $this->option('mode')
            : 'replace';
        $conflict = in_array($this->option('conflict'), ['update', 'skip'], true)
            ? (string) $this->option('conflict')
            : 'update';

        if (!$this->option('no-backup')) {
            $this->info('Creating pre-import safety backup...');
            try {
                $backupService->createBackup('database', null, true);
            } catch (Throwable $e) {
                $this->warn('Pre-import backup skipped: ' . $e->getMessage());
            }
        }

        $this->info("Analyzing SQLite file: {$filePath} ...");
        $uploadedFile = new UploadedFile(
            $filePath,
            basename($filePath),
            'application/vnd.sqlite3',
            null,
            true
        );

        try {
            $staged = $importService->stageSqliteFromFile($filePath, basename($filePath));
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
                if ($targetTable === 'settings' && !$this->option('include-settings')) {
                    $enabled = false;
                }
                $tablesConfig[$srcName] = [
                    'enabled'      => $enabled,
                    'target_table' => $targetTable,
                    'column_map'   => [],
                ];
            }

            $this->info('Executing topological import across ' . count($tablesConfig) . ' tables...');
            $result = $importService->executeImport($token, [
                'mode'              => $mode,
                'conflict_policy'   => $conflict === 'update' ? 'upsert' : 'skip',
                'create_backup'     => false,
                'reconcile_batches' => true,
                'tables'            => $tablesConfig,
            ]);

            $totals = $result['totals'] ?? ($result['summary'] ?? []);
            $this->info(sprintf(
                'Import completed! Inserted: %d | Updated: %d | Skipped: %d',
                $totals['inserted'] ?? ($totals['rows_inserted'] ?? 0),
                $totals['updated'] ?? ($totals['rows_updated'] ?? 0),
                $totals['skipped'] ?? ($totals['rows_skipped'] ?? 0)
            ));

            return self::SUCCESS;
        } catch (Throwable $e) {
            $this->error('SQLite Import Failed: ' . $e->getMessage());
            return self::FAILURE;
        }
    }
}
