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
        Schema::table('backup_logs', function (Blueprint $table) {
            $table->boolean('is_automated')->default(false)->after('created_by');
            $table->string('storage_destination', 30)->default('local')->after('is_automated');
            $table->string('remote_path')->nullable()->after('storage_destination');
            $table->string('remote_file_id')->nullable()->after('remote_path');
            $table->enum('remote_status', ['pending', 'success', 'failed', 'skipped'])->default('skipped')->after('remote_file_id');
            $table->text('remote_error')->nullable()->after('remote_status');
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('backup_logs', function (Blueprint $table) {
            $table->dropColumn([
                'is_automated',
                'storage_destination',
                'remote_path',
                'remote_file_id',
                'remote_status',
                'remote_error',
            ]);
        });
    }
};
