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
        'stock-adjustment.', 'stock-disposal.', 'customer-ledger.', 'supplier-ledger.',
        'purchase-order.', 'user-demands.', 'report.', 'settings.', 'backup.',
        'account.', 'cash-drawer.', 'expense.',
    ];

    public function index(Request $request)
    {
        if (!($request->user()?->can('role.view') || $request->user()?->can('permission.view') || $request->user()?->hasRole('Admin'))) {
            abort(403);
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
