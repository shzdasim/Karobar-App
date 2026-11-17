<?php

namespace App\Services;

use App\Models\Employee;
use App\Models\Setting;
use App\Models\User;
use Illuminate\Http\Exceptions\HttpResponseException;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\Schema;
use Illuminate\Validation\ValidationException;

class OperatorPinService
{
    public function normalizePin(?string $pin): ?string
    {
        if ($pin === null) {
            return null;
        }
        $trimmed = trim($pin);
        return $trimmed === '' ? null : $trimmed;
    }

    /**
     * Validate 4-digit format and cross-table uniqueness across Users and Employees.
     * A linked User + Employee pair is allowed to share the exact same PIN.
     */
    public function validateUniquePin(
        ?string $pin,
        ?int $ignoreUserId = null,
        ?int $ignoreEmployeeId = null,
        ?int $linkedUserId = null,
        ?int $linkedEmployeeId = null
    ): ?string {
        $normalized = $this->normalizePin($pin);
        if ($normalized === null) {
            return null;
        }

        if (!preg_match('/^\d{4}$/', $normalized)) {
            throw ValidationException::withMessages([
                'pin_code' => 'Operator code must be exactly 4 numeric digits (0000-9999).',
            ]);
        }

        if (Schema::hasTable('users') && Schema::hasColumn('users', 'pin_code')) {
            $userConflict = User::query()
                ->where('pin_code', $normalized)
                ->when($ignoreUserId, fn ($q) => $q->where('id', '!=', $ignoreUserId))
                ->when($linkedUserId, fn ($q) => $q->where('id', '!=', $linkedUserId))
                ->first(['id', 'name']);

            if ($userConflict) {
                throw ValidationException::withMessages([
                    'pin_code' => "4-digit code {$normalized} is already assigned to user '{$userConflict->name}'. Each user and employee must have a unique code.",
                ]);
            }
        }

        if (Schema::hasTable('employees') && Schema::hasColumn('employees', 'pin_code')) {
            $empConflict = Employee::query()
                ->where('pin_code', $normalized)
                ->when($ignoreEmployeeId, fn ($q) => $q->where('id', '!=', $ignoreEmployeeId))
                ->when($linkedEmployeeId, fn ($q) => $q->where('id', '!=', $linkedEmployeeId))
                ->first(['id', 'name']);

            if ($empConflict) {
                throw ValidationException::withMessages([
                    'pin_code' => "4-digit code {$normalized} is already assigned to employee '{$empConflict->name}'. Each user and employee must have a unique code.",
                ]);
            }
        }

        return $normalized;
    }

    /**
     * Keep linked Employee pin_code in sync when a User's PIN is updated.
     */
    public function syncLinkedPinForUser(User $user, ?string $pin): void
    {
        if (Schema::hasTable('employees') && Schema::hasColumn('employees', 'pin_code')) {
            Employee::where('user_id', $user->id)->update(['pin_code' => $pin]);
        }
    }

    /**
     * Keep linked User pin_code in sync when an Employee's PIN is updated.
     */
    public function syncLinkedPinForEmployee(Employee $employee, ?string $pin): void
    {
        if (!empty($employee->user_id) && Schema::hasTable('users') && Schema::hasColumn('users', 'pin_code')) {
            User::where('id', $employee->user_id)->update(['pin_code' => $pin]);
        }
    }

    /**
     * Generate a random unused 4-digit PIN across both Users and Employees.
     */
    public function generateUniquePin(): string
    {
        $used = [];

        if (Schema::hasTable('users') && Schema::hasColumn('users', 'pin_code')) {
            $used = array_merge($used, User::whereNotNull('pin_code')->pluck('pin_code')->all());
        }
        if (Schema::hasTable('employees') && Schema::hasColumn('employees', 'pin_code')) {
            $used = array_merge($used, Employee::whereNotNull('pin_code')->pluck('pin_code')->all());
        }

        $usedSet = array_flip(array_map('strval', $used));
        $trivial = ['0000', '1111', '2222', '3333', '4444', '5555', '6666', '7777', '8888', '9999', '1234', '4321', '0123'];

        for ($i = 0; $i < 200; $i++) {
            $candidate = str_pad((string) random_int(1000, 9999), 4, '0', STR_PAD_LEFT);
            if (!isset($usedSet[$candidate]) && !in_array($candidate, $trivial, true)) {
                return $candidate;
            }
        }

        for ($n = 1; $n <= 9999; $n++) {
            $candidate = str_pad((string) $n, 4, '0', STR_PAD_LEFT);
            if (!isset($usedSet[$candidate])) {
                return $candidate;
            }
        }

        throw ValidationException::withMessages([
            'pin_code' => 'No available 4-digit PINs remaining.',
        ]);
    }

    /**
     * Determine whether 4-digit operator PIN verification is enforced for the given module.
     */
    public function isRequiredForModule(string $module): bool
    {
        if (!Schema::hasTable('settings') || !Schema::hasColumn('settings', 'require_operator_pin')) {
            return false;
        }

        $setting = Setting::first();
        if (!$setting || !$setting->require_operator_pin) {
            return false;
        }

        return in_array($module, $setting->resolvedOperatorPinModules(), true);
    }

    /**
     * Resolve the operator (User + optional Employee) for an invoice or return save.
     * When enforcement is disabled for the module and no PIN is provided, falls back to Auth::user().
     *
     * @return array{user: ?User, employee: ?Employee, operator_name: string, verified_via_pin: bool}
     */
    public function resolveOperator(Request $request, string $module): array
    {
        $isRequired = $this->isRequiredForModule($module);
        $rawPin = trim((string) $request->input('operator_pin', ''));
        $authUser = $request->user() ?? Auth::user();

        if (!$isRequired && $rawPin === '') {
            $linkedEmployee = ($authUser && Schema::hasTable('employees'))
                ? Employee::where('user_id', $authUser->id)->first()
                : null;

            return [
                'user'             => $authUser,
                'employee'         => $linkedEmployee,
                'operator_name'    => $linkedEmployee?->name ?? $authUser?->name ?? 'System',
                'verified_via_pin' => false,
            ];
        }

        if ($rawPin === '') {
            $this->failPinChallenge('Please enter your 4-digit operator code to save.', $module);
        }

        if (!preg_match('/^\d{4}$/', $rawPin)) {
            $this->failPinChallenge('Operator code must be exactly 4 digits.', $module);
        }

        $matchedUser = (Schema::hasTable('users') && Schema::hasColumn('users', 'pin_code'))
            ? User::where('pin_code', $rawPin)->first()
            : null;

        $matchedEmployee = (Schema::hasTable('employees') && Schema::hasColumn('employees', 'pin_code'))
            ? Employee::where('pin_code', $rawPin)->first()
            : null;

        if (!$matchedUser && !$matchedEmployee) {
            $this->failPinChallenge('Invalid 4-digit operator code. Please check your code and try again.', $module);
        }

        if ($matchedUser) {
            if (($matchedUser->status ?? 'active') !== 'active') {
                $this->failPinChallenge("Operator account '{$matchedUser->name}' is inactive.", $module);
            }

            if (!$matchedEmployee && Schema::hasTable('employees')) {
                $matchedEmployee = Employee::where('user_id', $matchedUser->id)->first();
            }

            if ($matchedEmployee && ($matchedEmployee->status ?? 'active') === 'inactive') {
                $this->failPinChallenge("Employee profile '{$matchedEmployee->name}' is inactive.", $module);
            }

            return [
                'user'             => $matchedUser,
                'employee'         => $matchedEmployee,
                'operator_name'    => $matchedEmployee?->name ?? $matchedUser->name,
                'verified_via_pin' => true,
            ];
        }

        // Only Employee matched
        if (($matchedEmployee->status ?? 'active') === 'inactive') {
            $this->failPinChallenge("Employee '{$matchedEmployee->name}' is inactive.", $module);
        }

        $resolvedUser = $matchedEmployee->user_id ? User::find($matchedEmployee->user_id) : null;
        if ($matchedEmployee->user_id && $resolvedUser && ($resolvedUser->status ?? 'active') !== 'active') {
            $this->failPinChallenge("Linked user account for '{$matchedEmployee->name}' is inactive.", $module);
        }

        return [
            'user'             => $resolvedUser ?? $authUser,
            'employee'         => $matchedEmployee,
            'operator_name'    => $matchedEmployee->name,
            'verified_via_pin' => true,
        ];
    }

    protected function failPinChallenge(string $message, string $module): never
    {
        throw new HttpResponseException(response()->json([
            'message'               => $message,
            'requires_operator_pin' => true,
            'module'                => $module,
            'errors'                => [
                'operator_pin' => [$message],
            ],
        ], 422));
    }
}
