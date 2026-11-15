<?php

namespace App\Services;

use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Facades\Schema;
use SQLite3;
use Throwable;

class SqliteExportService
{
    /**
     * Ephemeral runtime tables whose session/token rows should be left empty in exported portable SQLite files.
     */
    protected array $ephemeralDataTables = [
        'sessions',
        'cache',
        'cache_locks',
        'jobs',
        'job_batches',
        'personal_access_tokens',
    ];

    /**
     * Export the active database (MariaDB, MySQL, PostgreSQL, or SQLite) into a standalone,
     * portable `.sqlite` database file that can be imported or run directly on any Standalone PC.
     *
     * @param  string|null  $outputPath  Optional destination file path
     * @return string  Absolute path to the generated `.sqlite` file
     */
    public function exportToSqliteFile(?string $outputPath = null): string
    {
        $exportDir = storage_path('app/backups/exports');
        if (!File::exists($exportDir)) {
            File::makeDirectory($exportDir, 0755, true, true);
        }

        $targetPath = $outputPath ?: ($exportDir . '/karobar-portable-' . now()->format('Y-m-d_His') . '.sqlite');
        if (File::exists($targetPath)) {
            File::delete($targetPath);
        }

        $driver = DB::getDriverName();

        // Fast path when active database is already a physical SQLite file on disk
        if ($driver === 'sqlite') {
            $currentDbPath = (string) config('database.connections.sqlite.database');
            if ($currentDbPath !== '' && $currentDbPath !== ':memory:' && is_file($currentDbPath)) {
                try {
                    DB::statement('PRAGMA wal_checkpoint(FULL);');
                } catch (Throwable) {
                }
                File::copy($currentDbPath, $targetPath);
                $this->sanitizeExportedSqliteFile($targetPath);
                return $targetPath;
            }
        }

        // Cross-database portable SQLite builder (MariaDB / MySQL / PostgreSQL / in-memory SQLite -> .sqlite file)
        $sqlite = new SQLite3($targetPath);
        $sqlite->busyTimeout(30000);
        $sqlite->exec('PRAGMA journal_mode = WAL;');
        $sqlite->exec('PRAGMA synchronous = OFF;');
        $sqlite->exec('PRAGMA foreign_keys = OFF;');

        try {
            $tables = $this->resolveTableNames($driver);

            foreach ($tables as $tableName) {
                if (str_starts_with($tableName, 'sqlite_')) {
                    continue;
                }

                $columns = $this->resolveTableColumns($driver, $tableName);
                if (empty($columns)) {
                    continue;
                }

                $createSql = $this->buildSqliteCreateTableSql($tableName, $columns);
                $sqlite->exec($createSql);

                if (in_array($tableName, $this->ephemeralDataTables, true)) {
                    continue;
                }

                $colNames = array_map(fn ($c) => $c['name'], $columns);
                $quotedCols = implode(', ', array_map(fn ($c) => '"' . str_replace('"', '""', $c) . '"', $colNames));

                $sqlite->exec('BEGIN TRANSACTION;');
                try {
                    $orderByCol = in_array('id', $colNames, true) ? 'id' : $colNames[0];
                    DB::table($tableName)->orderBy($orderByCol)->chunk(300, function ($rows) use ($sqlite, $tableName, $colNames, $quotedCols) {
                        foreach ($rows as $row) {
                            $rowArr = (array) $row;
                            $vals = [];
                            foreach ($colNames as $col) {
                                $val = $rowArr[$col] ?? null;
                                if ($val === null) {
                                    $vals[] = 'NULL';
                                } elseif (is_bool($val)) {
                                    $vals[] = $val ? '1' : '0';
                                } elseif (is_int($val) || is_float($val)) {
                                    $vals[] = (string) $val;
                                } else {
                                    $strVal = is_scalar($val) ? (string) $val : (string) json_encode($val);
                                    $vals[] = "'" . $sqlite->escapeString($strVal) . "'";
                                }
                            }
                            $escapedTable = str_replace('"', '""', $tableName);
                            $sqlite->exec("INSERT INTO \"{$escapedTable}\" ({$quotedCols}) VALUES (" . implode(', ', $vals) . ");");
                        }
                    });
                    $sqlite->exec('COMMIT;');
                } catch (Throwable $txErr) {
                    @$sqlite->exec('ROLLBACK;');
                    throw $txErr;
                }
            }

            $sqlite->exec('PRAGMA foreign_keys = ON;');
            $sqlite->exec('PRAGMA wal_checkpoint(TRUNCATE);');
        } finally {
            $sqlite->close();
        }

        return $targetPath;
    }

    protected function sanitizeExportedSqliteFile(string $sqlitePath): void
    {
        try {
            $db = new SQLite3($sqlitePath);
            $db->busyTimeout(15000);
            foreach ($this->ephemeralDataTables as $tbl) {
                @$db->exec('DELETE FROM "' . str_replace('"', '""', $tbl) . '";');
            }
            @$db->exec('PRAGMA wal_checkpoint(TRUNCATE);');
            $db->close();
        } catch (Throwable) {
        }
    }

    protected function resolveTableNames(string $driver): array
    {
        if ($driver === 'sqlite') {
            $rows = DB::select("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
            return array_values(array_filter(array_map(fn ($r) => (string) ($r->name ?? ''), $rows)));
        }

        $tables = Schema::getTables();
        $names = [];
        foreach ($tables as $t) {
            $name = (string) ($t['name'] ?? '');
            if ($name !== '') {
                $names[] = $name;
            }
        }
        sort($names);
        return $names;
    }

    protected function resolveTableColumns(string $driver, string $tableName): array
    {
        if ($driver === 'sqlite') {
            $pragma = DB::select("PRAGMA table_info(`{$tableName}`)");
            $cols = [];
            foreach ($pragma as $c) {
                $cols[] = [
                    'name'           => (string) $c->name,
                    'type'           => strtoupper((string) ($c->type ?: 'TEXT')),
                    'nullable'       => !((bool) $c->notnull),
                    'default'        => $c->dflt_value,
                    'auto_increment' => (bool) $c->pk && strtolower((string) $c->name) === 'id',
                    'primary'        => (bool) $c->pk,
                ];
            }
            return $cols;
        }

        $schemaCols = Schema::getColumns($tableName);
        $cols = [];
        foreach ($schemaCols as $c) {
            $name = (string) ($c['name'] ?? '');
            $isAutoInc = (bool) ($c['auto_increment'] ?? false);
            $cols[] = [
                'name'           => $name,
                'type'           => strtoupper((string) ($c['type_name'] ?? $c['type'] ?? 'TEXT')),
                'nullable'       => (bool) ($c['nullable'] ?? true),
                'default'        => $c['default'] ?? null,
                'auto_increment' => $isAutoInc,
                'primary'        => $isAutoInc || strtolower($name) === 'id',
            ];
        }
        return $cols;
    }

    protected function buildSqliteCreateTableSql(string $tableName, array $columns): string
    {
        $colDefs = [];
        foreach ($columns as $col) {
            $colName = '"' . str_replace('"', '""', $col['name']) . '"';
            if (!empty($col['auto_increment']) || ($col['primary'] && strtolower($col['name']) === 'id')) {
                $colDefs[] = "{$colName} INTEGER PRIMARY KEY AUTOINCREMENT";
                continue;
            }

            $rawType = strtoupper($col['type']);
            $sqliteType = 'TEXT';
            if (str_contains($rawType, 'INT') || str_contains($rawType, 'BOOL')) {
                $sqliteType = 'INTEGER';
            } elseif (str_contains($rawType, 'DEC') || str_contains($rawType, 'NUM') || str_contains($rawType, 'FLOAT') || str_contains($rawType, 'DOUBLE') || str_contains($rawType, 'REAL')) {
                $sqliteType = 'NUMERIC';
            } elseif (str_contains($rawType, 'BLOB') || str_contains($rawType, 'BINARY')) {
                $sqliteType = 'BLOB';
            }

            $def = "{$colName} {$sqliteType}";
            if (empty($col['nullable'])) {
                $def .= ' NOT NULL';
            }
            if ($col['default'] !== null) {
                $dflt = (string) $col['default'];
                if ($dflt !== '' && !str_contains(strtoupper($dflt), 'CURRENT_TIMESTAMP') && !str_contains($dflt, '(')) {
                    $cleanDflt = trim($dflt, "'\"");
                    if (is_numeric($cleanDflt)) {
                        $def .= " DEFAULT {$cleanDflt}";
                    } else {
                        $def .= " DEFAULT '" . str_replace("'", "''", $cleanDflt) . "'";
                    }
                }
            } elseif (!empty($col['nullable'])) {
                $def .= ' DEFAULT NULL';
            }

            $colDefs[] = $def;
        }

        $escapedTable = str_replace('"', '""', $tableName);
        return "CREATE TABLE IF NOT EXISTS \"{$escapedTable}\" (" . implode(', ', $colDefs) . ");";
    }
}
