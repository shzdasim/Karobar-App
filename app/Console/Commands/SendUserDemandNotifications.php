<?php

namespace App\Console\Commands;

use App\Models\UserDemand;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;

class SendUserDemandNotifications extends Command
{
    /**
     * The name and signature of the console command.
     *
     * @var string
     */
    protected $signature = 'demands:notify {--force}';

    /**
     * The console command description.
     *
     * @var string
     */
    protected $description = 'Mark pending user demands as notified. Acts as the daily reminder trigger for user demands shown in the Notification Center.';

    /**
     * Execute the console command.
     */
    public function handle(): int
    {
        $now = now();
        $force = (bool) $this->option('force');

        $query = DB::table('user_demands')
            ->where('status', 'pending');

        if (!$force) {
            $query->where(function ($q) use ($now) {
                $q->whereNull('last_notified_at')
                  ->orWhere('last_notified_at', '<', $now->copy()->subDay());
            });
        }

        $updated = $query->update(['last_notified_at' => $now]);

        $pendingCount = UserDemand::pending()->count();

        $this->info("User demands notified: {$updated}");
        $this->info("Pending user demands still active: {$pendingCount}");

        return self::SUCCESS;
    }
}
