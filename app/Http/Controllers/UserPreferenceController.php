<?php

namespace App\Http\Controllers;

use App\Models\User;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class UserPreferenceController extends Controller
{
    /**
     * Get all user preferences
     */
    public function index(Request $request): JsonResponse
    {
        /** @var User $user */
        $user = $request->user();
        
        return response()->json([
            'preferences' => $user->preferences ?? [],
        ]);
    }

    /**
     * Update user preferences (partial update)
     */
    public function update(Request $request): JsonResponse
    {
        /** @var User $user */
        $user = $request->user();

        $request->validate([
            'preferences' => 'required|array',
        ]);

        $currentPreferences = $user->preferences ?? [];
        $newPreferences = array_merge($currentPreferences, $request->input('preferences'));
        
        $user->update(['preferences' => $newPreferences]);

        return response()->json([
            'preferences' => $user->preferences,
            'message' => 'Preferences updated successfully',
        ]);
    }

    /**
     * Get a specific preference
     */
    public function show(Request $request, string $key): JsonResponse
    {
        /** @var User $user */
        $user = $request->user();

        return response()->json([
            'key' => $key,
            'value' => $user->getPreference($key),
        ]);
    }

    /**
     * Update a single preference
     */
    public function store(Request $request, string $key): JsonResponse
    {
        /** @var User $user */
        $user = $request->user();

        $request->validate([
            'value' => 'required',
        ]);

        $user->setPreference($key, $request->input('value'));
        $user->save();

        return response()->json([
            'key' => $key,
            'value' => $user->getPreference($key),
            'message' => 'Preference updated successfully',
        ]);
    }
}

