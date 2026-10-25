<?php

use Illuminate\Database\Migrations\Migration;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\PermissionRegistrar;

return new class extends Migration
{
    public function up(): void
    {
        $permissions = [
            'stocktake.view',
            'stocktake.create',
            'stocktake.update',
            'stocktake.delete',
            'stocktake.finalize',
        ];

        foreach ($permissions as $perm) {
            Permission::findOrCreate($perm, 'sanctum');
        }

        app(PermissionRegistrar::class)->forgetCachedPermissions();
    }

    public function down(): void
    {
        $permissions = [
            'stocktake.view',
            'stocktake.create',
            'stocktake.update',
            'stocktake.delete',
            'stocktake.finalize',
        ];

        foreach ($permissions as $perm) {
            Permission::where('name', $perm)->where('guard_name', 'sanctum')->delete();
        }

        app(PermissionRegistrar::class)->forgetCachedPermissions();
    }
};
