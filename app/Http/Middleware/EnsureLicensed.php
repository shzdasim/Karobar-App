<?php

namespace App\Http\Middleware;

use App\Services\LicenseService;
use Closure;
use Illuminate\Http\Request;

class EnsureLicensed
{
    public function __construct(private LicenseService $svc) {}

    public function handle(Request $request, Closure $next, ?string $requiredModuleOrTier = null)
    {
        $status = $this->svc->currentStatus();

        if (!$status['valid']) {
            return response()->json([
                'ok' => false,
                'code' => 'license_required',
                'reason' => $status['reason'] ?? 'Not licensed',
            ], 402); // Payment Required (signals the frontend to redirect)
        }

        if ($requiredModuleOrTier !== null && $requiredModuleOrTier !== '') {
            $requirement = strtolower(trim($requiredModuleOrTier));

            $payload = is_array($status['payload'] ?? null) ? $status['payload'] : [];
            $rawTier = strtolower(trim((string) (
                $status['tier']
                ?? $payload['tier']
                ?? $payload['license_type']
                ?? $payload['Edition']
                ?? $payload['edition']
                ?? $payload['plan']
                ?? ''
            )));

            $currentTier = match (true) {
                in_array($rawTier, ['basic', 'basic_pos', 'basic pos', 'starter', 'standard', 'silver'], true) || str_contains($rawTier, 'basic') => 'basic',
                in_array($rawTier, ['gold', 'gold_edition', 'gold edition', 'fbr', 'fbr_edition'], true) || str_contains($rawTier, 'gold') => 'gold',
                default => 'platinum',
            };

            $modules = is_array($status['modules'] ?? null) && !empty($status['modules'])
                ? $status['modules']
                : (LicenseService::TIERS[$currentTier]['modules'] ?? LicenseService::TIERS['platinum']['modules']);

            // Check if requirement is a tier ('basic', 'gold', 'platinum') or a module key ('fbr', 'barcode', 'server_terminals', etc.)
            if (isset(LicenseService::TIERS[$requirement])) {
                $currentRank = LicenseService::TIERS[$currentTier]['rank'] ?? 3;
                $reqRank = LicenseService::TIERS[$requirement]['rank'] ?? 3;
                $isAllowed = $currentRank >= $reqRank;
            } else {
                $isAllowed = in_array($requirement, $modules, true);
            }

            if (!$isAllowed) {
                $currentLabel = LicenseService::TIERS[$currentTier]['label'] ?? ucfirst($currentTier);
                $requiredTierKey = isset(LicenseService::TIERS[$requirement])
                    ? $requirement
                    : (LicenseService::MODULES[$requirement]['required_tier'] ?? 'platinum');
                $requiredTierLabel = LicenseService::TIERS[$requiredTierKey]['label'] ?? ucfirst($requiredTierKey);
                $moduleLabel = LicenseService::MODULES[$requirement]['label'] ?? ucfirst(str_replace('_', ' ', $requirement));

                return response()->json([
                    'ok' => false,
                    'code' => 'license_module_locked',
                    'module' => $requirement,
                    'module_label' => $moduleLabel,
                    'current_tier' => $currentTier,
                    'current_tier_label' => $currentLabel,
                    'required_tier' => $requiredTierKey,
                    'required_tier_label' => $requiredTierLabel,
                    'message' => "Your current {$currentLabel} license does not include {$moduleLabel}. Please upgrade to the {$requiredTierLabel} license tier.",
                ], 403);
            }
        }

        return $next($request);
    }
}
