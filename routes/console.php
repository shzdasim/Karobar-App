<?php

use Illuminate\Foundation\Inspiring;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\Schedule;

Artisan::command('inspire', function () {
    $this->comment(Inspiring::quote());
})->purpose('Display an inspiring quote');

// Daily reminder for pending user demands (shown in the Notification Center).
// Users see pending demands whenever they open notifications; this command
// marks them as "notified" once per day so reminders are tracked.
Schedule::command('demands:notify')->dailyAt('09:00');

// Automated Scheduled Backups & Off-Server Storage
// Checks hourly if backup is due according to BackupSchedule frequency and time_of_day
Schedule::command('backup:run-scheduled')->hourly();

// Daily cleanup of temporary shared PDF files older than 24 hours
Schedule::call(fn () => app(\App\Services\DocumentShareService::class)->cleanupExpiredSharedPdfs())->daily();
