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
        Schema::create('backup_schedules', function (Blueprint $table) {
            $table->id();
            $table->boolean('is_automated')->default(false);
            $table->string('frequency', 20)->default('daily'); // daily, weekly, monthly
            $table->string('time_of_day', 10)->default('02:00'); // HH:MM
            $table->unsignedTinyInteger('day_of_week')->nullable()->default(0); // 0 (Sun) - 6 (Sat)
            $table->unsignedTinyInteger('day_of_month')->nullable()->default(1); // 1 - 28
            $table->string('backup_type', 20)->default('full'); // full, database, settings
            $table->unsignedInteger('retention_days')->default(30);
            $table->unsignedInteger('max_backups_keep')->default(10);
            $table->string('storage_destination', 30)->default('local'); // local, google_drive, dropbox, onedrive, ftp, sftp
            $table->json('destination_config')->nullable(); // encrypted/structured tokens, folder IDs, API credentials
            $table->boolean('notify_on_failure')->default(true);
            $table->string('notification_email')->nullable();
            $table->timestamp('last_run_at')->nullable();
            $table->string('last_run_status', 20)->default('none'); // none, success, failed
            $table->text('last_error_message')->nullable();
            $table->timestamps();
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('backup_schedules');
    }
};
