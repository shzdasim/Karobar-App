<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Add SQLite COLLATE NOCASE indexes and partial low-stock indexes
     * to turn 14,000+ row full-table scans into sub-millisecond B-tree lookups.
     */
    public function up(): void
    {
        if (DB::getDriverName() === 'sqlite') {
            $sqliteStatements = [
                'products' => [
                    'CREATE INDEX IF NOT EXISTS "idx_products_name_nocase" ON "products" ("name" COLLATE NOCASE)',
                    'CREATE INDEX IF NOT EXISTS "idx_products_code_nocase" ON "products" ("product_code" COLLATE NOCASE)',
                    'CREATE INDEX IF NOT EXISTS "idx_products_barcode_nocase" ON "products" ("barcode" COLLATE NOCASE)',
                    'CREATE INDEX IF NOT EXISTS "idx_products_rack_nocase" ON "products" ("rack" COLLATE NOCASE)',
                    'CREATE INDEX IF NOT EXISTS "idx_products_low_stock_partial" ON "products" ("id", "quantity", "pack_size") WHERE "quantity" IS NOT NULL AND "quantity" < "pack_size"',
                ],
                'customers' => [
                    'CREATE INDEX IF NOT EXISTS "idx_customers_name_nocase" ON "customers" ("name" COLLATE NOCASE)',
                ],
                'suppliers' => [
                    'CREATE INDEX IF NOT EXISTS "idx_suppliers_name_nocase" ON "suppliers" ("name" COLLATE NOCASE)',
                ],
            ];

            foreach ($sqliteStatements as $table => $statements) {
                if (!Schema::hasTable($table)) {
                    continue;
                }
                foreach ($statements as $sql) {
                    try {
                        DB::statement($sql);
                    } catch (\Throwable) {
                        // Ignore if column or index cannot be created
                    }
                }
            }
        }
    }

    public function down(): void
    {
        if (DB::getDriverName() === 'sqlite') {
            $indexes = [
                'idx_products_name_nocase',
                'idx_products_code_nocase',
                'idx_products_barcode_nocase',
                'idx_products_rack_nocase',
                'idx_products_low_stock_partial',
                'idx_customers_name_nocase',
                'idx_suppliers_name_nocase',
            ];

            foreach ($indexes as $idx) {
                try {
                    DB::statement("DROP INDEX IF EXISTS \"{$idx}\"");
                } catch (\Throwable) {
                    // Ignore
                }
            }
        }
    }
};
