<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        if (Schema::hasTable('settings')) {
            Schema::table('settings', function (Blueprint $table) {
                if (!Schema::hasColumn('settings', 'expiry_warning_days')) {
                    $table->unsignedInteger('expiry_warning_days')->default(90)->after('currency_decimal_places');
                }
            });
        }

        if (Schema::hasTable('products')) {
            Schema::table('products', function (Blueprint $table) {
                if (!Schema::hasColumn('products', 'expiry_warning_days')) {
                    $table->unsignedInteger('expiry_warning_days')->nullable()->default(null)->after('max_discount');
                }
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        if (Schema::hasTable('settings')) {
            Schema::table('settings', function (Blueprint $table) {
                if (Schema::hasColumn('settings', 'expiry_warning_days')) {
                    $table->dropColumn('expiry_warning_days');
                }
            });
        }

        if (Schema::hasTable('products')) {
            Schema::table('products', function (Blueprint $table) {
                if (Schema::hasColumn('products', 'expiry_warning_days')) {
                    $table->dropColumn('expiry_warning_days');
                }
            });
        }
    }
};

