<?php

namespace App\Http\Controllers;

use App\Models\ThemeSetting;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Storage;
use Illuminate\Validation\Rule;

class ThemeSettingController extends Controller
{
    // GET /api/theme-settings
    public function index()
    {
        $this->authorize('viewAny', ThemeSetting::class);
        return response()->json(ThemeSetting::orderBy('created_at', 'desc')->get());
    }

    // GET /api/theme-settings/active
    public function getActive()
    {
        $activeTheme = ThemeSetting::active()->first();
        
        if (!$activeTheme) {
            // Create default theme if none exists
            $activeTheme = ThemeSetting::create([
                'name' => 'Default Theme',
                'is_active' => true,
            ]);
        }
        
        return response()->json($activeTheme);
    }

    // GET /api/theme-settings/{id}
    public function show($id)
    {
        $theme = ThemeSetting::findOrFail($id);
        $this->authorize('view', $theme);
        return response()->json($theme);
    }

    // POST /api/theme-settings
    public function store(Request $request)
    {
        $this->authorize('create', ThemeSetting::class);
        
        $validated = $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'primary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'primary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'primary_light' => ['nullable', 'string'],
            'secondary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'secondary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'secondary_light' => ['nullable', 'string'],
            'tertiary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'tertiary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'tertiary_light' => ['nullable', 'string'],
            'background_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'surface_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'text_primary' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'text_secondary' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'success_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'warning_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'danger_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'border_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'shadow_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
        ]);

        $theme = ThemeSetting::create($validated);
        
        return response()->json($theme, 201);
    }

    // PUT /api/theme-settings/active
    public function updateActive(Request $request)
    {
        $this->authorize('update', ThemeSetting::class);
        
        $activeTheme = ThemeSetting::active()->first();
        
        if (!$activeTheme) {
            // Create default theme if none exists
            $activeTheme = ThemeSetting::create([
                'name' => 'Default Theme',
                'is_active' => true,
            ]);
        }
        
        $validated = $request->validate([
            'name' => ['sometimes', 'required', 'string', 'max:255'],
            'primary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'primary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'primary_light' => ['nullable', 'string'],
            'secondary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'secondary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'secondary_light' => ['nullable', 'string'],
            'tertiary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'tertiary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'tertiary_light' => ['nullable', 'string'],
            'background_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'surface_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'text_primary' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'text_secondary' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'success_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'warning_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'danger_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'border_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'shadow_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'button_style' => ['nullable', 'string', 'in:rounded,outlined,pill,soft'],
        ]);

        $activeTheme->update($validated);
        
        return response()->json($activeTheme->fresh());
    }

    // PUT /api/theme-settings/{id}
    public function update(Request $request, $id)
    {
        $theme = ThemeSetting::findOrFail($id);
        $this->authorize('update', $theme);
        
        $validated = $request->validate([
            'name' => ['sometimes', 'required', 'string', 'max:255'],
            'primary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'primary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'primary_light' => ['nullable', 'string'],
            'secondary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'secondary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'secondary_light' => ['nullable', 'string'],
            'tertiary_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'tertiary_hover' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'tertiary_light' => ['nullable', 'string'],
            'background_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'surface_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'text_primary' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'text_secondary' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'success_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'warning_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'danger_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'border_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'shadow_color' => ['nullable', 'string', 'regex:/^#[0-9A-Fa-f]{6}$/'],
            'is_active' => ['nullable', 'boolean'],
        ]);

        $theme->update($validated);
        
        return response()->json($theme->fresh());
    }

    // PUT /api/theme-settings/{id}/activate
    public function activate($id)
    {
        $theme = ThemeSetting::findOrFail($id);
        $this->authorize('update', $theme);
        
        $theme->activate();
        
        return response()->json($theme->fresh());
    }

    // DELETE /api/theme-settings/{id}
    public function destroy($id)
    {
        $theme = ThemeSetting::findOrFail($id);
        $this->authorize('delete', $theme);
        
        $theme->delete();
        
        return response()->json(null, 204);
    }
}

