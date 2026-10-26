<?php

namespace App\Http\Controllers;

use App\Models\User;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;
use Illuminate\Support\Facades\Hash;
use Illuminate\Support\Facades\Gate;

class UserController extends Controller
{
    private function isSuperOrAdmin(?User $user): bool
    {
        return (bool) ($user && $user->hasRole(['Admin', 'admin', 'super-admin']));
    }

    private function assertCanTouchAdminRoleOrUser(Request $request, ?User $targetUser = null, array $requestedRoles = []): void
    {
        if ($this->isSuperOrAdmin($request->user())) {
            return;
        }

        if ($targetUser && $this->isSuperOrAdmin($targetUser)) {
            abort(403, 'Non-admin users cannot modify or delete an Administrator account.');
        }

        foreach ($requestedRoles as $roleName) {
            if (in_array(strtolower((string) $roleName), ['admin', 'super-admin'], true)) {
                abort(403, 'Non-admin users cannot assign the Admin role.');
            }
        }
    }

    public function index(Request $request)
    {
        Gate::authorize('manage-users');

        $search  = trim($request->query('search', ''));
        $perPage = (int) $request->query('per_page', 15);
        $perPage = $perPage > 0 && $perPage <= 200 ? $perPage : 15;

        $q = User::query()
            ->with(['roles:id,name', 'permissions:id,name'])
            ->when($search, function ($qq) use ($search) {
                $like = '%'.$search.'%';
                $qq->where(function ($w) use ($like) {
                    $w->where('name', 'like', $like)
                      ->orWhere('email', 'like', $like);
                });
            })
            ->orderBy('id', 'desc');

        $users = $q->paginate($perPage);

        return response()->json([
            'data' => $users->items(),
            'meta' => [
                'current_page' => $users->currentPage(),
                'per_page'     => $users->perPage(),
                'total'        => $users->total(),
                'last_page'    => $users->lastPage(),
            ],
        ]);
    }

    public function store(Request $request)
    {
        Gate::authorize('manage-users');

        $data = $request->validate([
            'name'        => ['required','string','max:255'],
            'email'       => ['required','email','max:255','unique:users,email'],
            'password'    => ['required','string','min:6'],
            'status'      => ['nullable', Rule::in(['active','inactive'])],
            // NEW:
            'roles'       => ['array'],
            'roles.*'     => ['string','exists:roles,name'],
            'permissions' => ['array'],
            'permissions.*' => ['string','exists:permissions,name'],
        ]);

        $this->assertCanTouchAdminRoleOrUser($request, null, $data['roles'] ?? []);

        $user = new User();
        $user->name   = $data['name'];
        $user->email  = $data['email'];
        $user->status = $data['status'] ?? 'active';
        $user->password = Hash::make($data['password']);
        $user->save();

        if (!empty($data['roles']))        $user->syncRoles($data['roles']);
        if (!empty($data['permissions']))  $user->syncPermissions($data['permissions']);

        return response()->json($user->load('roles:id,name','permissions:id,name'), 201);
    }

    public function show(User $user)
    {
        Gate::authorize('manage-users');
        return response()->json($user->load('roles:id,name','permissions:id,name'));
    }

    public function update(Request $request, User $user)
    {
        Gate::authorize('manage-users');

        $data = $request->validate([
            'name'     => ['required','string','max:255'],
            'email'    => ['required','email','max:255', Rule::unique('users','email')->ignore($user->id)],
            'password' => ['nullable','string','min:6'],
            'status'   => ['nullable', Rule::in(['active','inactive'])],
            // optional role/permission sync on update:
            'roles'       => ['sometimes','array'],
            'roles.*'     => ['string','exists:roles,name'],
            'permissions' => ['sometimes','array'],
            'permissions.*' => ['string','exists:permissions,name'],
        ]);

        $this->assertCanTouchAdminRoleOrUser($request, $user, $data['roles'] ?? []);

        $user->name  = $data['name'];
        $user->email = $data['email'];
        if (isset($data['status'])) $user->status = $data['status'];
        if (!empty($data['password'])) $user->password = Hash::make($data['password']);
        $user->save();

        if (($user->status ?? 'active') !== 'active') {
            $user->tokens()->delete();
        }

        if (array_key_exists('roles', $data))        $user->syncRoles($data['roles'] ?? []);
        if (array_key_exists('permissions', $data))  $user->syncPermissions($data['permissions'] ?? []);

        return response()->json($user->load('roles:id,name','permissions:id,name'));
    }

    public function destroy(User $user, Request $request)
    {
        Gate::authorize('manage-users');
        $this->assertCanTouchAdminRoleOrUser($request, $user);

        if ($request->user()->id === $user->id) {
            return response()->json(['message' => 'You cannot delete your own account.'], 422);
        }
        $user->tokens()->delete();
        $user->delete();
        return response()->json(['message' => 'Deleted']);
    }

    // Optional: explicit endpoints if you prefer separate sync calls:
    public function syncRoles(Request $request, User $user)
    {
        Gate::authorize('manage-users');

        $data = $request->validate([
            'roles' => ['array'],
            'roles.*' => ['string','exists:roles,name'],
        ]);
        $this->assertCanTouchAdminRoleOrUser($request, $user, $data['roles'] ?? []);
        $user->syncRoles($data['roles'] ?? []);
        return $user->load('roles:id,name','permissions:id,name');
    }

    public function syncPermissions(Request $request, User $user)
    {
        Gate::authorize('manage-users');
        $this->assertCanTouchAdminRoleOrUser($request, $user);

        $data = $request->validate([
            'permissions' => ['array'],
            'permissions.*' => ['string','exists:permissions,name'],
        ]);
        $user->syncPermissions($data['permissions'] ?? []);
        return $user->load('roles:id,name','permissions:id,name');
    }
}
