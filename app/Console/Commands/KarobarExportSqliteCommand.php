<?php

namespace App\Console\Commands;

use App\Services\SqliteExportService;
use Illuminate\Console\Command;
use Throwable;

class KarobarExportSqliteCommand extends Command
{
    protected $signature = 'karobar:export-sqlite
                            {output? : Optional output file path for the generated .sqlite database}';

    protected $description = 'Export the active Karobar-App database (MariaDB, MySQL, PostgreSQL, or SQLite) into a standalone portable .sqlite file';

    public function handle(SqliteExportService $exportService): int
    {
        $output = $this->argument('output') ? (string) $this->argument('output') : null;
        $this->info('Exporting active database to portable SQLite file...');

        try {
            $path = $exportService->exportToSqliteFile($output);
            $sizeMb = round(filesize($path) / (1024 * 1024), 2);
            $this->info("Portable SQLite database exported successfully ({$sizeMb} MB): {$path}");
            return self::SUCCESS;
        } catch (Throwable $e) {
            $this->error('Failed to export SQLite database: ' . $e->getMessage());
            return self::FAILURE;
        }
    }
}
