<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (Schema::hasTable('brands')) {
            Schema::table('brands', function (Blueprint $table) {
                if (!Schema::hasColumn('brands', 'expiry_return_policy')) {
                    $table->string('expiry_return_policy', 40)->default('before_expiry')->after('image');
                }
                if (!Schema::hasColumn('brands', 'expiry_return_months')) {
                    $table->integer('expiry_return_months')->nullable()->default(3)->after('expiry_return_policy');
                }
                if (!Schema::hasColumn('brands', 'expiry_return_days')) {
                    $table->integer('expiry_return_days')->nullable()->default(90)->after('expiry_return_months');
                }
                if (!Schema::hasColumn('brands', 'expiry_return_notes')) {
                    $table->string('expiry_return_notes', 500)->nullable()->after('expiry_return_days');
                }
            });
        }

        if (Schema::hasTable('suppliers')) {
            Schema::table('suppliers', function (Blueprint $table) {
                if (!Schema::hasColumn('suppliers', 'expiry_return_policy')) {
                    $table->string('expiry_return_policy', 40)->default('before_expiry')->after('credit_days');
                }
                if (!Schema::hasColumn('suppliers', 'expiry_return_months')) {
                    $table->integer('expiry_return_months')->nullable()->default(3)->after('expiry_return_policy');
                }
                if (!Schema::hasColumn('suppliers', 'expiry_return_days')) {
                    $table->integer('expiry_return_days')->nullable()->default(90)->after('expiry_return_months');
                }
                if (!Schema::hasColumn('suppliers', 'expiry_return_notes')) {
                    $table->string('expiry_return_notes', 500)->nullable()->after('expiry_return_days');
                }
            });
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('brands')) {
            Schema::table('brands', function (Blueprint $table) {
                $cols = array_filter(
                    ['expiry_return_policy', 'expiry_return_months', 'expiry_return_days', 'expiry_return_notes'],
                    fn ($col) => Schema::hasColumn('brands', $col)
                );
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }

        if (Schema::hasTable('suppliers')) {
            Schema::table('suppliers', function (Blueprint $table) {
                $cols = array_filter(
                    ['expiry_return_policy', 'expiry_return_months', 'expiry_return_days', 'expiry_return_notes'],
                    fn ($col) => Schema::hasColumn('suppliers', $col)
                );
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }
    }
};

