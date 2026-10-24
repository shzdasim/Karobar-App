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
        Schema::table('backup_schedules', function (Blueprint $table) {
            $table->string('custom_backup_path', 500)->nullable()->after('max_backups_keep');
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('backup_schedules', function (Blueprint $table) {
            $table->dropColumn('custom_backup_path');
        });
    }
};
