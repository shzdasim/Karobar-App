<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Services\SyncPulseService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class SyncPulseController extends Controller
{
    /**
     * GET /api/sync/pulse
     * Ultra-fast LAN heartbeat & cache-version endpoint polled by multi-PC terminals.
     */
    public function pulse(Request $request): JsonResponse
    {
        return response()->json(SyncPulseService::getPulsePayload($request));
    }

    /**
     * POST /api/sync/bump
     * Manually bump one or more sync domains across all connected terminals.
     */
    public function bump(Request $request): JsonResponse
    {
        $domains = $request->input('domains', ['stock', 'drafts', 'drawer']);
        $versions = SyncPulseService::bump($domains);

        return response()->json([
            'ok'       => true,
            'versions' => $versions,
        ]);
    }
}
