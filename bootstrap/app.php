<?php

use App\Http\Middleware\EnsureLicensed;
use App\Http\Middleware\CheckTokenExpiry;
use App\Http\Middleware\TrackTerminalMiddleware;
use Illuminate\Foundation\Application;
use Illuminate\Foundation\Configuration\Exceptions;
use Illuminate\Foundation\Configuration\Middleware;
use Spatie\Permission\Middleware\PermissionMiddleware;
use Spatie\Permission\Middleware\RoleMiddleware;
use Spatie\Permission\Middleware\RoleOrPermissionMiddleware;

return Application::configure(basePath: dirname(__DIR__))
    ->withRouting(
        web: __DIR__ . '/../routes/web.php',
        api: __DIR__ . '/../routes/api.php',
        commands: __DIR__ . '/../routes/console.php',
        health: '/up',
    )
    ->withMiddleware(function (Middleware $middleware): void {
        // Route middleware aliases (Laravel 11 replaces Http\Kernel)
        $middleware->alias([
            'role'                 => RoleMiddleware::class,
            'permission'           => PermissionMiddleware::class,
            'roles_or_permissions' => RoleOrPermissionMiddleware::class,
            'licensed'             => EnsureLicensed::class,
            'token.expiry'        => CheckTokenExpiry::class,
            'track.terminal'       => TrackTerminalMiddleware::class,
        ]);

        // Automatically track multi-PC terminal heartbeats, IP, active user, and authorization across API requests
        $middleware->appendToGroup('api', [TrackTerminalMiddleware::class]);
    })
    ->withExceptions(function (Exceptions $exceptions): void {
        //
    })
    ->create();
