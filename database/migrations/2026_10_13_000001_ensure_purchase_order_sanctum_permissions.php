<?php

use Illuminate\Database\Migrations\Migration;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\PermissionRegistrar;

return new class extends Migration
{
    public function up(): void
    {
        // Earlier installations registered these under web; User uses sanctum.
        // Retain existing permissions and role assignments while adding missing ones.
        foreach (['view', 'create', 'update', 'cancel'] as $action) {
            Permission::findOrCreate('purchase-order.'.$action, 'sanctum');
        }

        app(PermissionRegistrar::class)->forgetCachedPermissions();
    }

    public function down(): void
    {
        // These permissions may predate this repair or have role assignments.
        // Preserve authorization records when rolling back the repair.
    }
};
