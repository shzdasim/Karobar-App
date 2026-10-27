<?php

namespace App\Http\Controllers;

use Illuminate\Http\Request;
use Illuminate\Validation\Rule;
use Spatie\Permission\Models\Permission;

class PermissionController extends Controller
{
    private const CORE_PERMISSION_PREFIXES = [
        'user.', 'role.', 'permission.', 'product.', 'invoice.',
        'category.', 'brand.', 'supplier.', 'customer.',
        'sale-invoice.', 'purchase-invoice.', 'sale-return.', 'purchase-return.',
        'stock-adjustment.', 'stock-disposal.', 'stocktake.', 'payment.',
        'customer-ledger.', 'supplier-ledger.',
        'purchase-order.', 'user-demands.', 'report.', 'settings.', 'backup.',
        'account.', 'cash-drawer.', 'expense.',
        'barcode.', 'barcode-pos.', 'batch-expiry.',
    ];

    public function index(Request $request)
    {
        $user = $request->user();
        $allowed = $user && (
            $user->hasRole('Admin')
            || $user->can('permission.view')
            || $user->can('role.view')
            || $user->can('role.create')
            || $user->can('role.update')
            || $user->can('role.sync.permissions')
            || $user->can('user.view')
            || $user->can('user.create')
            || $user->can('user.update')
            || $user->can('user.assign.roles')
            || $user->can('user.assign.permissions')
            || $user->can('user.manage')
        );

        if (!$allowed) {
            abort(403);
        }

        if (Permission::where('guard_name', 'sanctum')->count() === 0) {
            app(\Database\Seeders\RolesAndPermissionsSeeder::class)->run();
        }

        $search = trim($request->query('search', ''));
        $q = Permission::query()
            ->where('guard_name', 'sanctum')
            ->when($search !== '', fn($qq) => $qq->where('name', 'like', "%{$search}%"))
            ->orderBy('name');

        return response()->json(
            $q->pluck('name')->values()->all(),
            200
        );
    }

    public function store(Request $request)
    {
        $data = $request->validate([
            'name' => ['required','string','max:150', Rule::unique('permissions','name')],
        ]);
        $p = Permission::create(['name' => $data['name'], 'guard_name' => 'sanctum']);
        return response()->json($p, 201);
    }

    public function destroy(Permission $permission)
    {
        foreach (self::CORE_PERMISSION_PREFIXES as $prefix) {
            if (str_starts_with($permission->name, $prefix)) {
                return response()->json([
                    'message' => "Cannot delete core system permission '{$permission->name}'.",
                ], 422);
            }
        }

        $permission->delete();
        return response()->noContent();
    }
}
