<?php

namespace App\Http\Controllers;

use App\Models\User;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\Hash;
use Illuminate\Support\Facades\Password;
use App\Services\LicenseService;

class AuthController extends Controller
{
    protected LicenseService $licenseService;

    // Token expiry times in minutes
    protected const REMEMBER_ME_EXPIRY = 1440; // 24 hours
    protected const DEFAULT_EXPIRY = 20; // 20 minutes

    public function __construct(LicenseService $licenseService)
    {
        $this->licenseService = $licenseService;
    }

    /**
     * Handle user login.
     *
     * @param  \Illuminate\Http\Request  $request
     * @return \Illuminate\Http\JsonResponse
     */
    public function login(Request $request): JsonResponse
    {
        $credentials = $request->validate([
            'email'    => 'required|email',
            'password' => 'required',
        ]);

        if (!Auth::attempt($credentials)) {
            return response()->json(['message' => 'Invalid credentials'], 401);
        }

        /** @var User $user */
        $user = Auth::user();

        // Check license and auto-revoke if machine changed (software copied to new device)
        $licenseCleared = $this->licenseService->clearLicenseIfMachineChanged();

        // Determine token expiry based on remember me flag
        $rememberMe = $request->boolean('remember');
        $expiryMinutes = $rememberMe ? self::REMEMBER_ME_EXPIRY : self::DEFAULT_EXPIRY;
        $expiresAt = now()->addMinutes($expiryMinutes);

        // Delete any existing personal access tokens first
        $user->tokens()->delete();

        // Generate API token (Sanctum) with custom expiry via TTL
        $token = $user->createToken('api_token', ['expires_at' => $expiresAt])->plainTextToken;

        // Store remember token expiry in database
        $user->update([
            'remember_token_expires_at' => $expiresAt,
        ]);

        $response = [
            'token' => $token,
            'user'  => $user->load('roles'),
            'expires_at' => $expiresAt->toIso8601String(),
            'remember_me' => $rememberMe,
        ];

        // If license was cleared due to machine change, inform the client
        if ($licenseCleared) {
            $response['license_revoked'] = true;
            $response['message'] = 'License was revoked because software was moved to a new device. Please activate a new license.';
        }

        return response()->json($response);
    }

    /**
     * Get the currently authenticated user.
     *
     * @param  \Illuminate\Http\Request  $request
     * @return \App\Models\User
     */
    public function user(Request $request): User
    {
        /** @var User $user */
        $user = $request->user();

        return $user->load('roles');
    }
    public function updateProfile(Request $request)
{
    $user = $request->user();

    $data = $request->validate([
        'name' => 'required|string|max:255',
        'email' => 'required|email|unique:users,email,' . $user->id,
        'password' => 'nullable|confirmed|min:6',
    ]);

    if (!empty($data['password'])) {
        $data['password'] = bcrypt($data['password']);
    } else {
        unset($data['password']);
    }

    $user->update($data);

    return response()->json($user);
}
// AuthController.php
public function logout(Request $request): \Illuminate\Http\JsonResponse
{
    $request->user()->currentAccessToken()?->delete();
    return response()->json(['message' => 'Logged out']);
}

public function confirmPassword(Request $request)
    {
        $request->validate([
            'password' => 'required|string',
        ]);

        $user = $request->user(); // via sanctum

        if (!$user || !Hash::check($request->password, $user->password)) {
            return response()->json(['message' => 'Invalid password'], 422);
        }

        // You might add throttle/rate-limit here if you wish

        return response()->json(['ok' => true], 200);
    }

    /**
     * Handle a forgot password request.
     *
     * @param  \Illuminate\Http\Request  $request
     * @return \Illuminate\Http\JsonResponse
     */
    public function forgotPassword(Request $request): JsonResponse
    {
        $request->validate([
            'email' => 'required|email|exists:users,email',
        ]);

        $status = Password::sendResetLink(
            $request->only('email')
        );

        if ($status === Password::RESET_LINK_SENT) {
            return response()->json([
                'message' => 'Password reset link sent to your email.',
                'status' => __($status)
            ], 200);
        }

        return response()->json([
            'message' => 'Unable to send password reset link.',
            'errors' => ['email' => [__($status)]]
        ], 422);
    }

    /**
     * Handle a password reset request.
     *
     * @param  \Illuminate\Http\Request  $request
     * @return \Illuminate\Http\JsonResponse
     */
    public function resetPassword(Request $request): JsonResponse
    {
        $request->validate([
            'token' => 'required|string',
            'email' => 'required|email|exists:users,email',
            'password' => 'required|confirmed|min:8',
        ]);

        $status = Password::reset(
            $request->only('email', 'password', 'password_confirmation', 'token'),
            function (User $user, string $password) {
                $user->forceFill([
                    'password' => Hash::make($password)
                ])->save();
            }
        );

        if ($status === Password::PASSWORD_RESET) {
            return response()->json([
                'message' => 'Password has been reset successfully.',
                'status' => __($status)
            ], 200);
        }

        return response()->json([
            'message' => 'Unable to reset password.',
            'errors' => ['email' => [__($status)]]
        ], 422);
    }

}
