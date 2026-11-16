<?php

namespace App\Http\Controllers;

use App\Models\SaleInvoice;
use App\Models\SaleReturn;
use App\Models\Setting;
use App\Services\FbrFiscalizationService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Schema;

class FbrController extends Controller
{
    public function __construct(protected FbrFiscalizationService $fbrService)
    {
    }

    /**
     * GET /api/fbr/status-summary
     */
    public function statusSummary(Request $request): JsonResponse
    {
        $setting = $this->fbrService->getSetting();
        $hasCols = Schema::hasColumn('sale_invoices', 'fbr_sync_status');

        $counts = [
            'synced'  => 0,
            'pending' => 0,
            'failed'  => 0,
        ];

        $recentQueue = [];

        if ($hasCols) {
            $counts['synced'] = SaleInvoice::where('fbr_sync_status', 'synced')->count();
            $counts['pending'] = SaleInvoice::where('fbr_sync_status', 'pending')->count();
            $counts['failed'] = SaleInvoice::where('fbr_sync_status', 'failed')->count();

            $recentQueue = SaleInvoice::with('customer:id,name')
                ->whereIn('fbr_sync_status', ['pending', 'failed'])
                ->orderByDesc('id')
                ->limit(20)
                ->get(['id', 'posted_number', 'date', 'customer_id', 'total', 'fbr_sync_status', 'fbr_error_message']);
        }

        $rawToken = trim((string) ($setting?->getRawOriginal('fbr_bearer_token') ?? $setting?->fbr_bearer_token ?? ''));

        return response()->json([
            'fbr_enabled'          => (bool) ($setting?->fbr_enabled ?? false),
            'fbr_environment'      => $setting?->fbr_environment ?? 'sandbox',
            'fbr_mode'             => $setting?->fbr_mode ?? 'digital_invoicing',
            'has_fbr_bearer_token' => $rawToken !== '',
            'fbr_seller_ntn'       => $setting?->fbr_seller_ntn,
            'fbr_pos_id'           => $setting?->fbr_pos_id,
            'endpoint'             => $setting ? $this->fbrService->resolveEndpointUrl($setting) : null,
            'counts'               => $counts,
            'queue'                => $recentQueue,
        ]);
    }

    /**
     * POST /api/fbr/test-connection
     */
    public function testConnection(Request $request): JsonResponse
    {
        $user = $request->user();
        if (!$user || (!$user->hasRole(['Admin', 'admin', 'super-admin']) && !$user->can('settings.update') && !$user->can('settings.view'))) {
            abort(403, 'Unauthorized to test FBR connection.');
        }

        $overrides = $request->only([
            'fbr_environment',
            'fbr_mode',
            'fbr_bearer_token',
            'fbr_pos_id',
            'fbr_seller_ntn',
            'fbr_seller_strn',
            'fbr_seller_province',
            'fbr_sandbox_url',
            'fbr_production_url',
            'fbr_default_hs_code',
            'fbr_scenario_id',
        ]);

        $res = $this->fbrService->testConnection($overrides);
        return response()->json($res, ($res['ok'] ?? false) ? 200 : 422);
    }

    /**
     * POST /api/fbr/sale-invoices/{id}/sync
     */
    public function syncSaleInvoice(Request $request, int $id): JsonResponse
    {
        $invoice = SaleInvoice::findOrFail($id);
        $res = $this->fbrService->fiscalizeSaleInvoice($invoice, true);

        return response()->json([
            ...$res,
            'invoice' => $invoice->fresh(),
        ], ($res['ok'] ?? false) ? 200 : 422);
    }

    /**
     * POST /api/fbr/sale-returns/{id}/sync
     */
    public function syncSaleReturn(Request $request, int $id): JsonResponse
    {
        $saleReturn = SaleReturn::findOrFail($id);
        $res = $this->fbrService->fiscalizeSaleReturn($saleReturn, true);

        return response()->json([
            ...$res,
            'sale_return' => $saleReturn->fresh(),
        ], ($res['ok'] ?? false) ? 200 : 422);
    }

    /**
     * POST /api/fbr/retry-queue
     */
    public function retryQueue(Request $request): JsonResponse
    {
        $summary = $this->fbrService->retryPendingInvoices(50);
        return response()->json([
            'ok'      => true,
            'summary' => $summary,
            'message' => "Processed {$summary['processed']} queued invoice(s): {$summary['synced']} synced, {$summary['failed']} failed.",
        ]);
    }
}
