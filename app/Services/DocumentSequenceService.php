<?php

namespace App\Services;

use App\Models\DocumentSequence;
use Illuminate\Database\QueryException;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use Throwable;

class DocumentSequenceService
{
    /**
     * Self-healing table check so sequence generation works even if migrations haven't been run yet
     * or when in-memory test databases are recreated between test methods.
     */
    public static function ensureTableExists(): void
    {
        try {
            if (!Schema::hasTable('document_sequences')) {
                Schema::create('document_sequences', function (Blueprint $table) {
                    $table->id();
                    $table->string('sequence_key', 80)->unique();
                    $table->string('prefix', 40);
                    $table->unsignedInteger('padding')->default(4);
                    $table->unsignedBigInteger('last_number')->default(0);
                    $table->timestamps();
                });
            }
        } catch (Throwable) {
            // Ignore race if another concurrent worker created the table simultaneously
        }
    }

    /**
     * Atomically allocate the next formatted document number using row-level locking (`SELECT ... FOR UPDATE`).
     *
     * On first allocation for a given `$sequenceKey` (or if existing rows in `$seedTable` have a higher number
     * due to a database import/restore), automatically seeds `last_number` from `$seedTable.$seedColumn`.
     *
     * @param  string       $sequenceKey  Unique key, e.g. 'sale_invoice', 'payment:IN:2026', 'expense:202611'
     * @param  string       $prefix       Document prefix, e.g. 'SINV-', 'PAY-IN-2026-', 'EXP-202611-'
     * @param  int          $padding      Zero-pad length (e.g. 4 => 0001, 5 => 00001)
     * @param  string|null  $seedTable    Optional table name to inspect for existing maximum number
     * @param  string|null  $seedColumn   Optional column name in `$seedTable` holding formatted numbers
     * @return string
     */
    public static function next(
        string $sequenceKey,
        string $prefix,
        int $padding = 4,
        ?string $seedTable = null,
        ?string $seedColumn = null,
        bool $seedFromMaxId = false
    ): string {
        self::ensureTableExists();

        $runner = function () use ($sequenceKey, $prefix, $padding, $seedTable, $seedColumn, $seedFromMaxId): string {
            $row = DocumentSequence::where('sequence_key', $sequenceKey)
                ->lockForUpdate()
                ->first();

            $existingMax = self::resolveExistingTableMax($prefix, $seedTable, $seedColumn, $seedFromMaxId);

            if (!$row) {
                try {
                    DB::table('document_sequences')->insert([
                        'sequence_key' => $sequenceKey,
                        'prefix' => $prefix,
                        'padding' => $padding,
                        'last_number' => $existingMax,
                        'created_at' => now(),
                        'updated_at' => now(),
                    ]);
                } catch (QueryException) {
                    // Another concurrent transaction inserted the seed row first; lock and read it below.
                }

                $row = DocumentSequence::where('sequence_key', $sequenceKey)
                    ->lockForUpdate()
                    ->first();
            }

            $baseNumber = max((int) ($row->last_number ?? 0), $existingMax);
            $nextNumber = $baseNumber + 1;

            // Ensure the generated candidate does not collide with any manually entered document number
            if ($seedTable && $seedColumn && Schema::hasTable($seedTable)) {
                while (
                    DB::table($seedTable)
                        ->where($seedColumn, $prefix . str_pad((string) $nextNumber, $padding, '0', STR_PAD_LEFT))
                        ->exists()
                ) {
                    $nextNumber++;
                }
            }

            if ($row) {
                $row->last_number = $nextNumber;
                $row->prefix = $prefix;
                $row->padding = $padding;
                $row->save();
            }

            return $prefix . str_pad((string) $nextNumber, $padding, '0', STR_PAD_LEFT);
        };

        if (DB::transactionLevel() > 0) {
            return $runner();
        }

        return DB::transaction($runner);
    }

    /**
     * Preview the next document number for UI display without incrementing the sequence counter.
     */
    public static function preview(
        string $sequenceKey,
        string $prefix,
        int $padding = 4,
        ?string $seedTable = null,
        ?string $seedColumn = null,
        bool $seedFromMaxId = false
    ): string {
        self::ensureTableExists();

        $storedLast = 0;
        try {
            $storedLast = (int) (DocumentSequence::where('sequence_key', $sequenceKey)->value('last_number') ?? 0);
        } catch (Throwable) {
            $storedLast = 0;
        }

        $existingMax = self::resolveExistingTableMax($prefix, $seedTable, $seedColumn, $seedFromMaxId);
        $nextNumber = max($storedLast, $existingMax) + 1;

        return $prefix . str_pad((string) $nextNumber, $padding, '0', STR_PAD_LEFT);
    }

    /**
     * Inspect the latest rows matching `$prefix` in `$seedTable.$seedColumn` to extract the highest numeric suffix.
     */
    protected static function resolveExistingTableMax(
        string $prefix,
        ?string $seedTable,
        ?string $seedColumn,
        bool $seedFromMaxId = false
    ): int {
        if (!$seedTable || !$seedColumn) {
            return 0;
        }

        try {
            if (!Schema::hasTable($seedTable)) {
                return 0;
            }

            $latestValues = DB::table($seedTable)
                ->where($seedColumn, 'like', $prefix . '%')
                ->orderByDesc('id')
                ->limit(25)
                ->pluck($seedColumn);

            $max = 0;
            $escapedPrefix = preg_quote($prefix, '/');

            foreach ($latestValues as $val) {
                $str = (string) $val;
                if (preg_match('/^' . $escapedPrefix . '(\d+)$/', $str, $m)) {
                    $num = (int) $m[1];
                    if ($num > $max) {
                        $max = $num;
                    }
                }
            }

            if ($seedFromMaxId) {
                $maxId = (int) (DB::table($seedTable)->max('id') ?? 0);
                if ($maxId > $max) {
                    $max = $maxId;
                }
            }

            return $max;
        } catch (Throwable) {
            return 0;
        }
    }
}
