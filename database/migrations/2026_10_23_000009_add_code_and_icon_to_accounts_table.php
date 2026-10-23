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
        if (Schema::hasTable('accounts')) {
            Schema::table('accounts', function (Blueprint $table) {
                if (!Schema::hasColumn('accounts', 'code')) {
                    $table->string('code', 50)->nullable()->after('type')->index();
                }
                if (!Schema::hasColumn('accounts', 'icon')) {
                    $table->string('icon', 50)->nullable()->after('code');
                }
                if (!Schema::hasColumn('accounts', 'color')) {
                    $table->string('color', 50)->nullable()->after('icon');
                }
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        if (Schema::hasTable('accounts')) {
            Schema::table('accounts', function (Blueprint $table) {
                $columns = [];
                if (Schema::hasColumn('accounts', 'code')) $columns[] = 'code';
                if (Schema::hasColumn('accounts', 'icon')) $columns[] = 'icon';
                if (Schema::hasColumn('accounts', 'color')) $columns[] = 'color';
                if (!empty($columns)) {
                    $table->dropColumn($columns);
                }
            });
        }
    }
};

