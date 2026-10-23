<?php

use Illuminate\Database\Migrations\Migration;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Spatie\Permission\PermissionRegistrar;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        $permissions = [
            'sale-invoice.discount-override',
        ];

        foreach ($permissions as $permName) {
            Permission::firstOrCreate(['name' => $permName, 'guard_name' => 'sanctum']);
        }

        app(PermissionRegistrar::class)->forgetCachedPermissions();

        // Assign to Admin role if exists
        $adminRole = Role::where('name', 'Admin')->where('guard_name', 'sanctum')->first();
        if ($adminRole) {
            $adminRole->givePermissionTo($permissions);
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Permission::where('name', 'sale-invoice.discount-override')->delete();
        app(PermissionRegistrar::class)->forgetCachedPermissions();
    }
};

