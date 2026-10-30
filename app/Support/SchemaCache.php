<?php

namespace App\Support;

use Illuminate\Support\Facades\Schema;

/**
 * In-memory static schema metadata cache to eliminate repeated
 * `sqlite_master` and `PRAGMA table_info(...)` queries on hot paths.
 */
final class SchemaCache
{
    /** @var array<string, bool> */
    private static array $tables = [];

    /** @var array<string, bool> */
    private static array $columns = [];

    /** @var array<string, array<int, string>> */
    private static array $columnListings = [];

    public static function hasTable(string $table): bool
    {
        if (app()->runningUnitTests()) {
            return Schema::hasTable($table);
        }

        if (!array_key_exists($table, self::$tables)) {
            self::$tables[$table] = Schema::hasTable($table);
        }

        return self::$tables[$table];
    }

    public static function hasColumn(string $table, string $column): bool
    {
        if (app()->runningUnitTests()) {
            return Schema::hasColumn($table, $column);
        }

        $key = $table . '.' . $column;
        if (!array_key_exists($key, self::$columns)) {
            self::$columns[$key] = Schema::hasColumn($table, $column);
        }

        return self::$columns[$key];
    }

    /**
     * @return array<int, string>
     */
    public static function getColumnListing(string $table): array
    {
        if (app()->runningUnitTests()) {
            return Schema::getColumnListing($table);
        }

        if (!array_key_exists($table, self::$columnListings)) {
            self::$columnListings[$table] = Schema::getColumnListing($table);
        }

        return self::$columnListings[$table];
    }

    public static function flush(): void
    {
        self::$tables = [];
        self::$columns = [];
        self::$columnListings = [];
    }
}
