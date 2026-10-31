<?php

namespace App\Services;

use App\Models\Setting;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Str;
use RuntimeException;
use Throwable;

class LocalWhatsAppAgentService
{
    public const DEFAULT_PORT = 3001;
    public const DEFAULT_HOST = '127.0.0.1';

    public function resolveAgentUrl(?Setting $setting = null): string
    {
        $setting = $setting ?? Setting::first();
        $configured = trim((string) ($setting?->whatsapp_api_url ?: config('services.whatsapp.api_url', '')));

        if ($configured !== '' && (str_contains($configured, '127.0.0.1') || str_contains($configured, 'localhost'))) {
            // Strip any path suffix like /send-document so we have base URL
            $parsed = parse_url($configured);
            $scheme = $parsed['scheme'] ?? 'http';
            $host = $parsed['host'] ?? self::DEFAULT_HOST;
            $port = $parsed['port'] ?? self::DEFAULT_PORT;

            return "{$scheme}://{$host}:{$port}";
        }

        return 'http://' . self::DEFAULT_HOST . ':' . self::DEFAULT_PORT;
    }

    public function ensureApiKey(?Setting $setting = null): string
    {
        $setting = $setting ?? Setting::first();
        $existing = trim((string) ($setting?->whatsapp_access_token ?: config('services.whatsapp.access_token', '')));

        if ($existing !== '') {
            return $existing;
        }

        $generated = 'karobar_wa_' . Str::random(24);
        if ($setting) {
            $setting->whatsapp_access_token = $generated;
            $setting->save();
        }

        return $generated;
    }

    /**
     * Query the local Node.js Baileys WhatsApp Agent status.
     */
    public function status(?Setting $setting = null): array
    {
        $setting = $setting ?? Setting::first();
        $baseUrl = $this->resolveAgentUrl($setting);
        $apiKey = $this->ensureApiKey($setting);

        try {
            $response = Http::timeout(2)
                ->acceptJson()
                ->withHeaders(['X-Api-Key' => $apiKey])
                ->get($baseUrl . '/status');

            if ($response->successful()) {
                $data = $response->json() ?: [];

                return array_merge($data, [
                    'ok'            => true,
                    'agent_running' => true,
                    'agent_url'     => $baseUrl,
                    'api_key'       => $apiKey,
                ]);
            }
        } catch (Throwable) {
            // Agent process is not running on port 3001
        }

        return [
            'ok'            => true,
            'agent_running' => false,
            'status'        => 'stopped',
            'qr_data_url'   => null,
            'pairing_code'  => null,
            'user'          => null,
            'last_error'    => null,
            'agent_url'     => $baseUrl,
            'api_key'       => $apiKey,
        ];
    }

    /**
     * Start the built-in Node.js WhatsApp Agent in the background if not already running,
     * and persist the free local_agent configuration in Settings.
     */
    public function start(?Setting $setting = null): array
    {
        $setting = $setting ?? Setting::firstOrCreate(['id' => 1], ['printer_type' => 'a4']);
        $baseUrl = $this->resolveAgentUrl($setting);
        $apiKey = $this->ensureApiKey($setting);

        // Save local_agent settings automatically so user doesn't even have to fill fields manually
        $setting->whatsapp_enabled = true;
        $setting->whatsapp_provider = 'local_agent';
        $setting->whatsapp_api_url = $baseUrl;
        $setting->whatsapp_access_token = $apiKey;
        $setting->save();

        $current = $this->status($setting);
        if (!empty($current['agent_running'])) {
            if (in_array($current['status'] ?? '', ['disconnected', 'error'], true)) {
                try {
                    Http::timeout(3)
                        ->withHeaders(['X-Api-Key' => $apiKey])
                        ->post($baseUrl . '/restart');
                    usleep(500000);
                } catch (Throwable) {
                    // ignore
                }
            }

            return $this->status($setting);
        }

        $nodeBinary = $this->resolveNodeBinary();
        $scriptPath = base_path('scripts/whatsapp-agent.mjs');

        if (!is_file($scriptPath)) {
            throw new RuntimeException("Built-in WhatsApp Agent script not found at {$scriptPath}");
        }

        $parsed = parse_url($baseUrl);
        $port = (int) ($parsed['port'] ?? self::DEFAULT_PORT);
        $host = (string) ($parsed['host'] ?? self::DEFAULT_HOST);
        $logFile = storage_path('logs/whatsapp-agent.log');
        $storageDir = storage_path('app/whatsapp-agent');

        if (!is_dir($storageDir)) {
            @mkdir($storageDir, 0755, true);
        }

        if (DIRECTORY_SEPARATOR === '\\') {
            $cmd = sprintf(
                'set WHATSAPP_AGENT_PORT=%d&& set WHATSAPP_AGENT_HOST=%s&& set WHATSAPP_AGENT_API_KEY=%s&& start /B "" "%s" "%s" > "%s" 2>&1',
                $port,
                $host,
                $apiKey,
                $nodeBinary,
                $scriptPath,
                $logFile
            );
            pclose(popen($cmd, 'r'));
        } else {
            $nodeDir = dirname($nodeBinary);
            $cmd = sprintf(
                'PATH=%s:$PATH WHATSAPP_AGENT_PORT=%d WHATSAPP_AGENT_HOST=%s WHATSAPP_AGENT_API_KEY=%s nohup %s %s >> %s 2>&1 </dev/null &',
                escapeshellarg($nodeDir),
                $port,
                escapeshellarg($host),
                escapeshellarg($apiKey),
                escapeshellarg($nodeBinary),
                escapeshellarg($scriptPath),
                escapeshellarg($logFile)
            );
            exec($cmd);
        }

        // Wait up to 3.5 seconds for the HTTP server & QR generation to become ready
        for ($i = 0; $i < 14; $i++) {
            usleep(250000);
            $check = $this->status($setting);
            if (!empty($check['agent_running'])) {
                // If it's still 'starting' or 'connecting', wait slightly longer for the QR code to emit
                if (in_array($check['status'] ?? '', ['qr_ready', 'connected'], true) || $i >= 8) {
                    return $check;
                }
            }
        }

        return $this->status($setting);
    }

    /**
     * Request an 8-digit phone number pairing code from the built-in agent.
     */
    public function requestPairingCode(string $rawPhone, ?Setting $setting = null): array
    {
        $setting = $setting ?? Setting::first();
        $status = $this->status($setting);

        if (empty($status['agent_running'])) {
            $status = $this->start($setting);
        }

        $baseUrl = $this->resolveAgentUrl($setting);
        $apiKey = $this->ensureApiKey($setting);
        $defaultCountryCode = (string) ($setting?->whatsapp_default_country_code ?: '92');

        $normalizedPhone = app(DocumentShareService::class)->normalizePhone($rawPhone, $defaultCountryCode);
        if (!$normalizedPhone) {
            throw new RuntimeException('Please enter a valid WhatsApp phone number (e.g. 0300-1234567).');
        }

        $response = Http::timeout(15)
            ->acceptJson()
            ->asJson()
            ->withHeaders(['X-Api-Key' => $apiKey])
            ->post($baseUrl . '/pair-code', [
                'phone' => $normalizedPhone,
            ]);

        $json = $response->json() ?: [];
        if ($response->failed() || empty($json['ok'])) {
            throw new RuntimeException($json['message'] ?? 'Failed to request 8-digit pairing code from WhatsApp.');
        }

        return $json;
    }

    /**
     * Unlink the currently connected WhatsApp account and generate a fresh QR code.
     */
    public function logout(?Setting $setting = null): array
    {
        $setting = $setting ?? Setting::first();
        $baseUrl = $this->resolveAgentUrl($setting);
        $apiKey = $this->ensureApiKey($setting);

        try {
            $response = Http::timeout(10)
                ->acceptJson()
                ->withHeaders(['X-Api-Key' => $apiKey])
                ->post($baseUrl . '/logout');

            if ($response->successful()) {
                usleep(800000);

                return $this->status($setting);
            }
        } catch (Throwable) {
            // If agent wasn't running, clear auth folder directly
        }

        $authDir = storage_path('app/whatsapp-agent/auth');
        if (is_dir($authDir)) {
            $files = glob($authDir . '/*') ?: [];
            foreach ($files as $file) {
                if (is_file($file)) {
                    @unlink($file);
                }
            }
        }

        return $this->status($setting);
    }

    /**
     * Stop the background Node.js WhatsApp Agent process.
     */
    public function stop(?Setting $setting = null): array
    {
        $setting = $setting ?? Setting::first();
        $baseUrl = $this->resolveAgentUrl($setting);
        $apiKey = $this->ensureApiKey($setting);

        try {
            Http::timeout(3)
                ->acceptJson()
                ->withHeaders(['X-Api-Key' => $apiKey])
                ->post($baseUrl . '/shutdown');
            usleep(300000);
        } catch (Throwable) {
            // Already stopped
        }

        return $this->status($setting);
    }

    /**
     * Locate the Node.js executable across macOS, Linux, Windows, and NVM installations.
     */
    public function resolveNodeBinary(): string
    {
        $candidates = [
            '/usr/local/bin/node',
            '/opt/homebrew/bin/node',
            '/usr/bin/node',
        ];

        $home = getenv('HOME') ?: ($_SERVER['HOME'] ?? '');
        if ($home) {
            $nvmNodes = glob($home . '/.nvm/versions/node/*/bin/node') ?: [];
            if (!empty($nvmNodes)) {
                rsort($nvmNodes, SORT_NATURAL);
                array_unshift($candidates, ...$nvmNodes);
            }
        }

        // Also check common macOS /Users/*/.nvm paths when PHP runs under another user env
        $userNvmNodes = glob('/Users/*/.nvm/versions/node/*/bin/node') ?: [];
        if (!empty($userNvmNodes)) {
            rsort($userNvmNodes, SORT_NATURAL);
            $candidates = array_merge($candidates, $userNvmNodes);
        }

        foreach ($candidates as $candidate) {
            if (is_file($candidate) && is_executable($candidate)) {
                return $candidate;
            }
        }

        return 'node';
    }
}
