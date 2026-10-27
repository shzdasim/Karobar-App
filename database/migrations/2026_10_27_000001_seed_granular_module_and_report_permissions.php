<?php

use Illuminate\Database\Migrations\Migration;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Spatie\Permission\PermissionRegistrar;

return new class extends Migration
{
    public function up(): void
    {
        app()[PermissionRegistrar::class]->forgetCachedPermissions();

        $guard = 'sanctum';

        $newPermissions = [
            // Barcode Studio / Label Generator
            'barcode.view',
            'barcode.print',
            'barcode.create',
            'barcode.update',
            'barcode.delete',

            // Barcode POS
            'barcode-pos.view',
            'barcode-pos.create',

            // Batch Expiry & Tracking
            'batch-expiry.view',
            'batch-expiry.update',

            // Dedicated Granular Reports (isolated from Invoice/Ledger modules and other reports)
            'report.cost-of-sale-detail.view',
            'report.cost-of-sale-detail.export',
            'report.net-profit.view',
            'report.net-profit.export',
            'report.purchase-summary.view',
            'report.purchase-summary.export',
            'report.purchase-returns.view',
            'report.purchase-returns.export',
            'report.sale-returns.view',
            'report.sale-returns.export',
            'report.reorder.view',
            'report.reorder.export',
            'report.outstanding-aging.view',
            'report.outstanding-aging.export',
        ];

        foreach ($newPermissions as $permName) {
            Permission::firstOrCreate([
                'name'       => $permName,
                'guard_name' => $guard,
            ]);
        }

        // Sync all sanctum permissions to Admin role if present
        $admin = Role::where('name', 'Admin')->where('guard_name', $guard)->first();
        if ($admin) {
            $admin->syncPermissions(
                Permission::where('guard_name', $guard)->pluck('name')->all()
            );
        }

        app()[PermissionRegistrar::class]->forgetCachedPermissions();
    }

    public function down(): void
    {
        app()[PermissionRegistrar::class]->forgetCachedPermissions();
    }
};

