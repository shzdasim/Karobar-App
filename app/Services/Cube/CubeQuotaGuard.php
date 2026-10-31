<?php

namespace App\Services\Cube;

use App\Models\CubeProviderUsage;
use Carbon\Carbon;
use Illuminate\Support\Facades\Schema;
use Throwable;

class CubeQuotaGuard
{
    /**
     * Evaluates whether a provider is eligible and within its configured free daily quota.
     *
     * @return array{can_attempt: bool, status: string, reason: ?string, model: string, daily_limit: int, usage: ?CubeProviderUsage}
     */
    public function inspectProvider(string $provider): array
    {
        $aiEnabled = (bool) config('cube.ai_enabled', true);
        $freeOnly = (bool) config('cube.free_only', true);
        $providerConfig = (array) config("cube.providers.{$provider}", []);

        $enabled = (bool) ($providerConfig['enabled'] ?? false);
        $apiKey = trim((string) ($providerConfig['api_key'] ?? ''));
        $model = trim((string) ($providerConfig['model'] ?? ''));
        $dailyLimit = (int) ($providerConfig['daily_limit'] ?? 0);
        $maxCap = (int) ($providerConfig['max_daily_free_cap'] ?? 1000);
        $allowedModels = (array) ($providerConfig['allowed_free_models'] ?? []);

        if (! $aiEnabled || ! $enabled) {
            return [
                'can_attempt' => false,
                'status' => 'disabled',
                'reason' => 'provider_disabled',
                'model' => $model,
                'daily_limit' => $dailyLimit,
                'usage' => null,
            ];
        }

        // Rule: If a daily limit is missing or zero, disable that provider (never allow unlimited external requests).
        if ($dailyLimit <= 0) {
            return [
                'can_attempt' => false,
                'status' => 'disabled',
                'reason' => 'daily_limit_missing_or_zero',
                'model' => $model,
                'daily_limit' => 0,
                'usage' => null,
            ];
        }

        if ($apiKey === '') {
            return [
                'can_attempt' => false,
                'status' => 'disabled',
                'reason' => 'missing_api_key',
                'model' => $model,
                'daily_limit' => $dailyLimit,
                'usage' => null,
            ];
        }

        // Rule: Free-only model eligibility enforcement
        if ($freeOnly && ! $this->isEligibleFreeModel($provider, $model, $allowedModels)) {
            $usage = $this->getOrCreateTodayUsage($provider, $this->fingerprint($providerConfig));
            if ($usage) {
                $usage->update([
                    'status' => 'disabled_until_reconfigured',
                    'disabled_reason' => 'model_not_in_free_tier_allowlist',
                    'last_error_code' => 'ineligible_model',
                ]);
            }

            return [
                'can_attempt' => false,
                'status' => 'disabled_until_reconfigured',
                'reason' => 'model_not_in_free_tier_allowlist',
                'model' => $model,
                'daily_limit' => $dailyLimit,
                'usage' => $usage,
            ];
        }

        $fingerprint = $this->fingerprint($providerConfig);
        $usage = $this->getOrCreateTodayUsage($provider, $fingerprint);

        if ($usage) {
            if ($usage->status === 'disabled_until_reconfigured' && $usage->config_fingerprint === $fingerprint) {
                return [
                    'can_attempt' => false,
                    'status' => 'disabled_until_reconfigured',
                    'reason' => $usage->disabled_reason ?: 'disabled_until_reconfigured',
                    'model' => $model,
                    'daily_limit' => $dailyLimit,
                    'usage' => $usage,
                ];
            }

            if ($usage->cooldown_until && Carbon::now()->lt($usage->cooldown_until)) {
                return [
                    'can_attempt' => false,
                    'status' => 'rate_limited',
                    'reason' => 'cooldown_active',
                    'model' => $model,
                    'daily_limit' => $dailyLimit,
                    'usage' => $usage,
                ];
            }

            $effectiveLimit = $freeOnly ? min($dailyLimit, $maxCap) : $dailyLimit;
            if ($usage->request_count >= $effectiveLimit) {
                if ($usage->status !== 'quota_exhausted') {
                    $usage->update([
                        'status' => 'quota_exhausted',
                        'last_error_code' => 'daily_limit_reached',
                    ]);
                }

                return [
                    'can_attempt' => false,
                    'status' => 'quota_exhausted',
                    'reason' => 'daily_quota_exhausted',
                    'model' => $model,
                    'daily_limit' => $effectiveLimit,
                    'usage' => $usage,
                ];
            }
        }

        return [
            'can_attempt' => true,
            'status' => 'ready',
            'reason' => null,
            'model' => $model,
            'daily_limit' => $freeOnly ? min($dailyLimit, $maxCap) : $dailyLimit,
            'usage' => $usage,
        ];
    }

    public function recordAttempt(string $provider): ?CubeProviderUsage
    {
        $providerConfig = (array) config("cube.providers.{$provider}", []);
        $usage = $this->getOrCreateTodayUsage($provider, $this->fingerprint($providerConfig));
        if (! $usage) {
            return null;
        }

        $usage->increment('request_count');

        return $usage->fresh();
    }

    public function recordSuccess(string $provider): ?CubeProviderUsage
    {
        $providerConfig = (array) config("cube.providers.{$provider}", []);
        $usage = $this->getOrCreateTodayUsage($provider, $this->fingerprint($providerConfig));
        if (! $usage) {
            return null;
        }

        $usage->update([
            'success_count' => $usage->success_count + 1,
            'status' => 'ready',
            'cooldown_until' => null,
            'last_http_status' => 200,
            'last_error_code' => null,
        ]);

        return $usage->fresh();
    }

    public function recordRateLimit(
        string $provider,
        ?string $retryAfterHeader = null,
        int $httpStatus = 429,
        string $errorCode = 'rate_limited'
    ): ?CubeProviderUsage {
        $providerConfig = (array) config("cube.providers.{$provider}", []);
        $usage = $this->getOrCreateTodayUsage($provider, $this->fingerprint($providerConfig));
        if (! $usage) {
            return null;
        }

        $cooldownSeconds = $this->parseRetryAfterSeconds($retryAfterHeader);

        $usage->update([
            'failure_count' => $usage->failure_count + 1,
            'status' => 'rate_limited',
            'cooldown_until' => Carbon::now()->addSeconds($cooldownSeconds),
            'last_http_status' => $httpStatus,
            'last_error_code' => $errorCode,
        ]);

        return $usage->fresh();
    }

    public function recordPermanentDisable(
        string $provider,
        string $reason,
        int $httpStatus = 403,
        string $errorCode = 'auth_or_billing_error'
    ): ?CubeProviderUsage {
        $providerConfig = (array) config("cube.providers.{$provider}", []);
        $fingerprint = $this->fingerprint($providerConfig);
        $usage = $this->getOrCreateTodayUsage($provider, $fingerprint);
        if (! $usage) {
            return null;
        }

        $usage->update([
            'failure_count' => $usage->failure_count + 1,
            'status' => 'disabled_until_reconfigured',
            'disabled_reason' => substr($reason, 0, 190),
            'config_fingerprint' => $fingerprint,
            'last_http_status' => $httpStatus,
            'last_error_code' => substr($errorCode, 0, 90),
        ]);

        return $usage->fresh();
    }

    public function recordTransientFailure(
        string $provider,
        int $httpStatus = 500,
        string $errorCode = 'transient_error'
    ): ?CubeProviderUsage {
        $providerConfig = (array) config("cube.providers.{$provider}", []);
        $usage = $this->getOrCreateTodayUsage($provider, $this->fingerprint($providerConfig));
        if (! $usage) {
            return null;
        }

        $usage->update([
            'failure_count' => $usage->failure_count + 1,
            'status' => 'transient_error',
            'last_http_status' => $httpStatus,
            'last_error_code' => substr($errorCode, 0, 90),
        ]);

        return $usage->fresh();
    }

    public function parseRetryAfterSeconds(?string $retryAfterHeader): int
    {
        $defaultCooldown = max(10, (int) config('cube.default_rate_limit_cooldown_seconds', 120));
        if ($retryAfterHeader === null || trim($retryAfterHeader) === '') {
            return $defaultCooldown;
        }

        $trimmed = trim($retryAfterHeader);
        if (ctype_digit($trimmed)) {
            return max(1, min(86400, (int) $trimmed));
        }

        try {
            $retryDate = Carbon::parse($trimmed);
            $diff = Carbon::now()->diffInSeconds($retryDate, false);
            if ($diff > 0) {
                return max(1, min(86400, (int) $diff));
            }
        } catch (Throwable) {
            // Fall through to default cooldown
        }

        return $defaultCooldown;
    }

    public function isEligibleFreeModel(string $provider, string $model, array $allowedModels = []): bool
    {
        $trimmed = trim($model);
        if ($trimmed === '') {
            return false;
        }

        if (in_array($trimmed, $allowedModels, true)) {
            return true;
        }

        if ($provider === 'groq') {
            return app(GroqProvider::class)->isChatCapableModelId($trimmed);
        }

        if ($provider === 'gemini') {
            return app(GeminiProvider::class)->isFreeTierGeminiModelId($trimmed);
        }

        return false;
    }

    protected function fingerprint(array $providerConfig): string
    {
        $enabled = (bool) ($providerConfig['enabled'] ?? false);
        $model = trim((string) ($providerConfig['model'] ?? ''));
        $dailyLimit = (int) ($providerConfig['daily_limit'] ?? 0);
        $keyHash = hash('sha256', trim((string) ($providerConfig['api_key'] ?? '')));

        return substr(hash('sha256', json_encode([$enabled, $model, $dailyLimit, $keyHash])), 0, 64);
    }

    protected function getOrCreateTodayUsage(string $provider, string $fingerprint): ?CubeProviderUsage
    {
        try {
            if (! Schema::hasTable('cube_provider_usages')) {
                return null;
            }

            $today = Carbon::now()->toDateString();
            $usage = CubeProviderUsage::query()
                ->where('provider', $provider)
                ->whereDate('usage_date', $today)
                ->first();

            if (! $usage) {
                // Inherit disabled_until_reconfigured if an earlier row had the identical fingerprint
                $priorDisabled = CubeProviderUsage::query()
                    ->where('provider', $provider)
                    ->where('status', 'disabled_until_reconfigured')
                    ->where('config_fingerprint', $fingerprint)
                    ->latest('id')
                    ->first();

                $usage = CubeProviderUsage::query()->create([
                    'provider' => $provider,
                    'usage_date' => $today,
                    'request_count' => 0,
                    'success_count' => 0,
                    'failure_count' => 0,
                    'status' => $priorDisabled ? 'disabled_until_reconfigured' : 'ready',
                    'disabled_reason' => $priorDisabled?->disabled_reason,
                    'config_fingerprint' => $fingerprint,
                ]);
            } elseif ($usage->config_fingerprint !== $fingerprint) {
                // Admin updated provider configuration (.env / config) -> reset disabled_until_reconfigured state
                $usage->update([
                    'config_fingerprint' => $fingerprint,
                    'status' => $usage->status === 'disabled_until_reconfigured' ? 'ready' : $usage->status,
                    'disabled_reason' => $usage->status === 'disabled_until_reconfigured' ? null : $usage->disabled_reason,
                    'cooldown_until' => null,
                ]);
            }

            return $usage;
        } catch (Throwable) {
            return null;
        }
    }
}

