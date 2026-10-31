<?php

namespace App\Http\Controllers;

use App\Services\Cube\CubeAssistantService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class CubeAssistantController extends Controller
{
    public function __construct(
        protected CubeAssistantService $assistantService
    ) {}

    public function ask(Request $request): JsonResponse
    {
        $validated = $request->validate([
            'question' => ['required', 'string', 'min:1', 'max:2000'],
            'language' => ['nullable', 'string', 'in:en,ur,both'],
            'current_route' => ['nullable', 'string', 'max:160', 'regex:/^\/[A-Za-z0-9\/_\-:?=&]*$/'],
            'history' => ['nullable', 'array', 'max:6'],
            'history.*.role' => ['required_with:history', 'string', 'in:user,assistant'],
            'history.*.content' => ['required_with:history', 'string', 'max:2000'],
            'matched_intent_id' => ['nullable', 'string', 'max:120'],
            'screen_guide_id' => ['nullable', 'string', 'max:160'],

            // Security: strictly reject arbitrary provider names, navigation URLs, action IDs, or CSS selectors from frontend
            'provider' => ['prohibited'],
            'model' => ['prohibited'],
            'api_key' => ['prohibited'],
            'navigation_url' => ['prohibited'],
            'navigation_urls' => ['prohibited'],
            'links' => ['prohibited'],
            'action_id' => ['prohibited'],
            'workflow_id' => ['prohibited'],
            'selector' => ['prohibited'],
            'css_selector' => ['prohibited'],
            'target' => ['prohibited'],
            'target_selector' => ['prohibited'],
            'dom_target' => ['prohibited'],
        ]);

        $response = $this->assistantService->ask($validated);

        return response()->json($response);
    }
}

