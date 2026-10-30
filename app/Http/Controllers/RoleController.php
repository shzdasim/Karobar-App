<?php

namespace App\Http\Controllers;

use Illuminate\Http\Request;
use Illuminate\Validation\Rule;
use Spatie\Permission\Models\Role;
use Spatie\Permission\Models\Permission;

class RoleController extends Controller
{
    private function isReservedAdminRoleName(string $name): bool
    {
        return in_array(strtolower(trim($name)), ['admin', 'super-admin'], true);
    }

    private function assertCanGrantPermissions(Request $request, array $requestedPermissions): void
    {
        $actor = $request->user();
        if ($actor && $actor->hasRole('Admin')) {
            return;
        }

        foreach ($requestedPermissions as $permName) {
            if (!$actor || !$actor->can((string) $permName)) {
                abort(403, "You cannot grant the '{$permName}' permission because you do not possess it.");
            }
        }
    }

    // GET /api/roles?search=&page=&per_page=
    public function index(Request $request)
    {
        // Allow role viewers as well as user managers who need to populate role dropdowns
        $this->authorizeViaMiddleware($request, [
            'role.view', 'user.view', 'user.create', 'user.update', 'user.assign.roles', 'user.manage'
        ]);

        $search  = trim($request->query('search', ''));
        $perPage = (int) $request->query('per_page', 15);
        $perPage = $perPage > 0 && $perPage <= 200 ? $perPage : 15;

        $q = Role::query()->where('guard_name', 'sanctum')
            ->withCount('permissions')
            ->when($search, fn($qq) => $qq->where('name', 'like', "%{$search}%"))
            ->orderBy('id', 'desc');

        $roles = $q->paginate($perPage);

        // include permissions count (optional)
        $data = $roles->getCollection()
            ->map(fn($r) => [
                'id' => $r->id,
                'name' => $r->name,
                'guard_name' => $r->guard_name,
                'permissions_count' => (int) ($r->permissions_count ?? 0),
            ])->all();

        return response()->json([
            'data' => $data,
            'meta' => [
                'current_page' => $roles->currentPage(),
                'per_page'     => $roles->perPage(),
                'total'        => $roles->total(),
                'last_page'    => $roles->lastPage(),
            ],
        ]);
    }

    // POST /api/roles
    public function store(Request $request)
    {
        $this->authorizeViaMiddleware($request, 'role.create');

        $validated = $request->validate([
            'name' => [
                'required','string','max:255',
                Rule::unique('roles','name')->where('guard_name','sanctum'),
            ],
            'permissions' => ['array'],
            'permissions.*' => ['string'],
        ]);

        if ($this->isReservedAdminRoleName($validated['name'])) {
            return response()->json(['message' => 'Cannot create a role with a reserved Admin role name.'], 422);
        }

        if (!empty($validated['permissions'])) {
            $this->assertCanGrantPermissions($request, $validated['permissions']);
        }

        $role = Role::create([
            'name' => $validated['name'],
            'guard_name' => 'sanctum',
        ]);

        if (!empty($validated['permissions'])) {
            $perms = Permission::whereIn('name', $validated['permissions'])
                ->where('guard_name','sanctum')->pluck('name')->all();
            $role->syncPermissions($perms);
        }

        return response()->json($role->load('permissions'), 201);
    }

    // GET /api/roles/{role}
    public function show(Role $role, Request $request)
    {
        $this->authorizeViaMiddleware($request, ['role.view', 'role.update', 'role.sync.permissions']);
        abort_unless($role->guard_name === 'sanctum', 404);

        return response()->json([
            'id' => $role->id,
            'name' => $role->name,
            'guard_name' => $role->guard_name,
            'permissions' => $role->permissions()->pluck('name')->values()->all(),
        ]);
    }

    // PUT /api/roles/{role}
    public function update(Request $request, Role $role)
    {
        $this->authorizeViaMiddleware($request, ['role.update', 'role.sync.permissions']);
        abort_unless($role->guard_name === 'sanctum', 404);

        $validated = $request->validate([
            'name' => [
                'required','string','max:255',
                Rule::unique('roles','name')
                    ->where('guard_name','sanctum')
                    ->ignore($role->id),
            ],
            'permissions' => ['sometimes', 'array'],
            'permissions.*' => ['string'],
        ]);

        if ($this->isReservedAdminRoleName($role->name)) {
            if ($validated['name'] !== $role->name) {
                return response()->json(['message' => 'Cannot rename Admin role'], 422);
            }
            if (!$request->user()?->hasRole('Admin')) {
                return response()->json(['message' => 'Only an Admin can modify the Admin role'], 403);
            }
        } elseif ($this->isReservedAdminRoleName($validated['name'])) {
            return response()->json(['message' => 'Cannot rename a role to a reserved Admin role name.'], 422);
        }

        if (array_key_exists('permissions', $validated)) {
            $this->assertCanGrantPermissions($request, $validated['permissions'] ?? []);
        }

        $role->name = $validated['name'];
        $role->save();

        if ($role->name === 'Admin') {
            $allPerms = Permission::where('guard_name', 'sanctum')->pluck('name')->all();
            $role->syncPermissions($allPerms);
        } elseif (array_key_exists('permissions', $validated)) {
            $perms = $validated['permissions'] ?? [];
            $perms = Permission::whereIn('name', $perms)->where('guard_name','sanctum')->pluck('name')->all();
            $role->syncPermissions($perms);
        }

        return response()->json($role->load('permissions'));
    }

    // DELETE /api/roles/{role}
    public function destroy(Request $request, Role $role)
    {
        $this->authorizeViaMiddleware($request, 'role.delete');
        abort_unless($role->guard_name === 'sanctum', 404);

        if ($this->isReservedAdminRoleName($role->name)) {
            return response()->json(['message' => 'Cannot delete Admin role'], 422);
        }

        $role->delete();
        return response()->json(['message' => 'Deleted']);
    }

    private function authorizeViaMiddleware(Request $request, array|string $permissions): void
    {
        $user = $request->user();
        if ($user && $user->hasRole('Admin')) {
            return;
        }

        if ($user && method_exists($user, 'can')) {
            foreach ((array) $permissions as $perm) {
                if ($user->can($perm)) {
                    return;
                }
            }
        }

        abort(403);
    }
}
