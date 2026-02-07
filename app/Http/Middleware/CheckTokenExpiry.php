<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;
use App\Models\User;

class CheckTokenExpiry
{
    /**
     * Handle an incoming request.
     *
     * @param  \Closure(\Illuminate\Http\Request): (\Symfony\Component\HttpFoundation\Response)  $next
     */
    public function handle(Request $request, Closure $next): Response
    {
        $user = $request->user();

        // If no user, proceed (other middleware will handle auth)
        if (!$user) {
            return $next($request);
        }

        // Check if remember_token_expires_at is set and has passed
        if ($user->remember_token_expires_at && now()->isAfter($user->remember_token_expires_at)) {
            // Token has expired, delete it and return 401
            $user->currentAccessToken()->delete();
            $user->update(['remember_token_expires_at' => null]);

            return response()->json([
                'message' => 'Session expired. Please login again.',
                'expired' => true,
            ], 401);
        }

        return $next($request);
    }
}

