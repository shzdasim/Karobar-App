<?php

namespace App\Http\Controllers;

use App\Models\License;
use App\Services\LicenseService;
use Illuminate\Http\Request;

class LicenseController extends Controller
{
    public function status(LicenseService $svc)
        {
            $st = $svc->currentStatus();
            $st['machine_id'] = $svc->computeMachineId(); // show it (safe)
            return response()->json($st);
        }


    public function activate(Request $req, LicenseService $svc)
    {
        $user = $req->user();
        $hasAnyAdmins = \Spatie\Permission\Models\Role::where('name', 'Admin')->exists()
            && \App\Models\User::role('Admin')->exists();

        if ($hasAnyAdmins && (!$user || (!$user->hasRole(['Admin', 'admin', 'super-admin']) && !$user->can('settings.update')))) {
            return response()->json(['message' => 'Only administrators can activate software licenses.'], 403);
        }

        $req->validate(['license_key' => 'required|string']);
        $key = trim($req->input('license_key'));

        [$ok, $payload, $reason] = $svc->verifyString(
            $key,
            config('license.bind_to_machine') ? $svc->computeMachineId() : null
        );

        $rec = License::create([
            'license_key' => $key,
            'payload'     => $payload ?? [],
            'valid'       => $ok,
            'reason'      => $reason,
            'machine_hash'=> config('license.bind_to_machine') ? $svc->computeMachineId() : null,
            'activated_at'=> now(),
            'expires_at'  => isset($payload['exp']) ? \Carbon\Carbon::createFromTimestamp($payload['exp']) : null,
            'last_verified_at' => now(),
        ]);

        return response()->json([
            'ok' => $ok,
            'reason' => $reason,
            'payload' => $payload,
        ], $ok ? 200 : 422);
    }

    public function deactivate(Request $req)
    {
        $user = $req->user();
        if (!$user || !$user->hasRole(['Admin', 'admin', 'super-admin'])) {
            return response()->json(['message' => 'Only administrators can deactivate the software license.'], 403);
        }

        $req->validate([
            'password' => 'required|string',
        ]);

        if (!\Illuminate\Support\Facades\Hash::check($req->input('password'), $user->password)) {
            return response()->json([
                'message' => 'Invalid password.',
                'errors'  => ['password' => ['The provided password is incorrect.']],
            ], 422);
        }

        // Remove all licenses
        \App\Models\License::query()->delete();
        return response()->json(['ok' => true]);
    }
}
