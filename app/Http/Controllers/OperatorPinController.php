<?php

namespace App\Http\Controllers;

use App\Models\Employee;
use App\Models\User;
use App\Services\OperatorPinService;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Schema;

class OperatorPinController extends Controller
{
    public function __construct(protected OperatorPinService $pinService)
    {
    }

    protected function authorizeManage(Request $request): void
    {
        $user = $request->user();
        if (
            !$user
            || (
                !$user->hasRole(['Admin', 'admin', 'super-admin'])
                && !$user->can('settings.update')
                && !$user->can('user.manage')
                && !$user->can('user.update')
                && !$user->can('hr.manage')
            )
        ) {
            abort(403, 'Unauthorized to manage operator PINs.');
        }
    }

    protected function authorizeView(Request $request): void
    {
        $user = $request->user();
        if (
            !$user
            || (
                !$user->hasRole(['Admin', 'admin', 'super-admin'])
                && !$user->can('settings.view')
                && !$user->can('settings.update')
                && !$user->can('user.view')
                && !$user->can('user.manage')
                && !$user->can('hr.view')
                && !$user->can('hr.manage')
            )
        ) {
            abort(403, 'Unauthorized to view operator PIN directory.');
        }
    }

    /**
     * GET /api/operator-pins/directory
     */
    public function directory(Request $request)
    {
        $this->authorizeView($request);

        $hasUserPin = Schema::hasTable('users') && Schema::hasColumn('users', 'pin_code');
        $users = User::with('roles:id,name')
            ->orderBy('name')
            ->get()
            ->map(function (User $u) use ($hasUserPin) {
                return [
                    'id'       => $u->id,
                    'name'     => $u->name,
                    'email'    => $u->email,
                    'status'   => $u->status ?? 'active',
                    'roles'    => $u->roles->pluck('name')->values(),
                    'pin_code' => $hasUserPin ? $u->pin_code : null,
                ];
            });

        $employees = collect();
        if (Schema::hasTable('employees')) {
            $hasEmpPin = Schema::hasColumn('employees', 'pin_code');
            $employees = Employee::with('user:id,name,email')
                ->orderBy('name')
                ->get()
                ->map(function (Employee $e) use ($hasEmpPin) {
                    return [
                        'id'            => $e->id,
                        'employee_code' => $e->employee_code,
                        'user_id'       => $e->user_id,
                        'user_name'     => $e->user?->name,
                        'name'          => $e->name,
                        'department'    => $e->department,
                        'designation'   => $e->designation,
                        'status'        => $e->status ?? 'active',
                        'pin_code'      => $hasEmpPin ? $e->pin_code : null,
                    ];
                });
        }

        return response()->json([
            'users'     => $users,
            'employees' => $employees,
        ]);
    }

    /**
     * GET /api/operator-pins/generate
     */
    public function generate(Request $request)
    {
        $this->authorizeManage($request);

        return response()->json([
            'pin_code' => $this->pinService->generateUniquePin(),
        ]);
    }

    /**
     * PUT /api/operator-pins/assign
     */
    public function assign(Request $request)
    {
        $this->authorizeManage($request);

        $data = $request->validate([
            'type'     => ['required', 'string', 'in:user,employee'],
            'id'       => ['required', 'integer'],
            'pin_code' => ['nullable', 'string', 'max:10'],
        ]);

        if ($data['type'] === 'user') {
            $user = User::findOrFail($data['id']);
            $linkedEmpId = Schema::hasTable('employees')
                ? Employee::where('user_id', $user->id)->value('id')
                : null;

            $pin = $this->pinService->validateUniquePin(
                $data['pin_code'] ?? null,
                $user->id,
                null,
                null,
                $linkedEmpId ? (int) $linkedEmpId : null
            );

            $user->pin_code = $pin;
            $user->save();
            $this->pinService->syncLinkedPinForUser($user, $pin);

            return response()->json([
                'message'  => $pin ? "4-digit code assigned to {$user->name}." : "4-digit code cleared for {$user->name}.",
                'pin_code' => $user->pin_code,
            ]);
        }

        $employee = Employee::findOrFail($data['id']);
        $pin = $this->pinService->validateUniquePin(
            $data['pin_code'] ?? null,
            null,
            $employee->id,
            $employee->user_id ? (int) $employee->user_id : null,
            null
        );

        $employee->pin_code = $pin;
        $employee->save();
        $this->pinService->syncLinkedPinForEmployee($employee, $pin);

        return response()->json([
            'message'  => $pin ? "4-digit code assigned to {$employee->name}." : "4-digit code cleared for {$employee->name}.",
            'pin_code' => $employee->pin_code,
        ]);
    }
}
