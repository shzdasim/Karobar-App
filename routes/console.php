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
