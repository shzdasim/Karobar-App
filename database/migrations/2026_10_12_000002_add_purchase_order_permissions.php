<?php

use Illuminate\Database\Migrations\Migration;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\PermissionRegistrar;

return new class extends Migration
{
    public function up(): void
    {
        foreach (['create', 'update', 'cancel'] as $action) {
            Permission::findOrCreate('purchase-order.'.$action, 'sanctum');
        }
        app(PermissionRegistrar::class)->forgetCachedPermissions();
    }

    public function down(): void
    {
        Permission::where('guard_name', 'sanctum')->whereIn('name', ['purchase-order.create', 'purchase-order.update', 'purchase-order.cancel'])->delete();
        app(PermissionRegistrar::class)->forgetCachedPermissions();
    }
};
