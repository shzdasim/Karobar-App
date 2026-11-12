<?php

namespace App\Providers;

use App\Models\PersonalAccessToken;
use Illuminate\Database\Events\ConnectionEstablished;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Event;
use Illuminate\Support\ServiceProvider;
use Laravel\Sanctum\Sanctum;

class AppServiceProvider extends ServiceProvider
{
    /**
     * Register any application services.
     */
    public function register(): void
    {
        foreach (['framework/views', 'framework/cache/data', 'framework/sessions', 'framework/testing', 'fonts'] as $dir) {
            $path = storage_path($dir);
            if (!is_dir($path)) {
                @mkdir($path, 0775, true);
            }
        }

        if (!config('view.compiled')) {
            config(['view.compiled' => realpath(storage_path('framework/views')) ?: storage_path('framework/views')]);
        }
    }

    /**
     * Bootstrap any application services.
     */
    public function boot(): void
    {
        Sanctum::usePersonalAccessTokenModel(PersonalAccessToken::class);

        $applySqlitePragmas = static function ($connection): void {
            try {
                if ($connection && $connection->getDriverName() === 'sqlite') {
                    $connection->unprepared(
                        "PRAGMA busy_timeout = 30000;\n"
                        . "PRAGMA journal_mode = WAL;\n"
                        . "PRAGMA synchronous = NORMAL;\n"
                        . "PRAGMA wal_autocheckpoint = 1000;\n"
                        . "PRAGMA temp_store = MEMORY;\n"
                        . "PRAGMA cache_size = -65536;\n"
                        . "PRAGMA mmap_size = 268435456;"
                    );
                }
            } catch (\Throwable) {
                // Ignore if connection is not ready
            }
        };

        Event::listen(ConnectionEstablished::class, static function (ConnectionEstablished $event) use ($applySqlitePragmas): void {
            $applySqlitePragmas($event->connection);
        });

        try {
            $connections = DB::getConnections();
            foreach ($connections as $conn) {
                $applySqlitePragmas($conn);
            }
        } catch (\Throwable) {
            // Ignore when DB connection is not yet initialized
        }
    }
}
