<?php

namespace App\Http\Controllers;

use App\Models\Setting;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Mail;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Facades\Storage;
use Illuminate\Validation\Rule;

class SettingController extends Controller
{
    // GET /api/settings
    public function show()
    {
        $this->authorize('view', Setting::class);
        $setting = Setting::firstOrCreate(['id' => 1], [
            'printer_type' => 'thermal',
            'thermal_template' => 'standard',
            'a4_template' => 'standard',
            'navigation_style' => 'sidebar',
            'sale_system' => 'retail_wholesale',
            'shop_type' => 'pharmacy',
            'currency_code' => 'PKR',
            'currency_symbol' => 'Rs.',
            'currency_position' => 'before',
            'currency_decimal_places' => 2,
            'expiry_warning_days' => 90,
        ]);

        return response()->json($setting);
    }

    // GET /api/sale-system
    // Lightweight app-wide sale system mode. Accessible to any authenticated
    // user (no settings.view permission / license gate) because the whole UI
    // — not just the settings page — depends on it (e.g. hiding wholesale
    // actions in retail-only mode). Returns only the two mode fields.
    public function saleSystem()
    {
        $setting = Setting::first();

        return response()->json([
            'sale_system'             => $setting?->sale_system ?? 'retail_wholesale',
            'shop_type'               => $setting?->shop_type ?? 'pharmacy',
            'currency_code'           => $setting?->currency_code ?? 'PKR',
            'currency_symbol'         => $setting?->currency_symbol ?? 'Rs.',
            'currency_position'       => $setting?->currency_position ?? 'before',
            'currency_decimal_places' => (int)($setting?->currency_decimal_places ?? 2),
        ]);
    }

    // GET /api/currency
    public function currency()
    {
        $setting = Setting::first();

        return response()->json([
            'currency_code'           => $setting?->currency_code ?? 'PKR',
            'currency_symbol'         => $setting?->currency_symbol ?? 'Rs.',
            'currency_position'       => $setting?->currency_position ?? 'before',
            'currency_decimal_places' => (int)($setting?->currency_decimal_places ?? 2),
        ]);
    }

    // POST /api/settings  (multipart/form-data supported)
    public function update(Request $request)
    {
        $this->authorize('update', Setting::class);
        $setting = Setting::firstOrCreate(['id' => 1]);

        $validated = $request->validate([
            'store_name'              => ['nullable','string','max:255'],
            'phone_number'            => ['nullable','string','max:30'],
            'address'                 => ['nullable','string','max:500'],
            'license_number'          => ['nullable','string','max:100'],
            'note'                    => ['nullable','string','max:2000'],
            'printer_type'            => ['required', Rule::in(['thermal','a4'])],
            'thermal_template'        => ['nullable', Rule::in(['standard','minimal','detailed','compact','bold','barcode'])],
            'a4_template'             => ['nullable', Rule::in(['standard','minimal','detailed','compact','bold','barcode'])],
            'navigation_style'        => ['nullable', Rule::in(['sidebar','topbar'])],
            'sale_system'             => ['nullable', Rule::in(['retail', 'retail_wholesale'])],
            'shop_type'               => ['nullable', Rule::in(['pharmacy', 'general_store'])],
            'currency_code'           => ['nullable', 'string', 'max:10'],
            'currency_symbol'         => ['nullable', 'string', 'max:15'],
            'currency_position'       => ['nullable', Rule::in(['before', 'after'])],
            'currency_decimal_places' => ['nullable', 'integer', 'min:0', 'max:4'],
            'expiry_warning_days'     => ['nullable', 'integer', 'min:1', 'max:3650'],
            'smtp_enabled'            => ['nullable', 'boolean'],
            'smtp_host'               => ['nullable', 'string', 'max:255'],
            'smtp_port'               => ['nullable', 'integer', 'min:1', 'max:65535'],
            'smtp_username'           => ['nullable', 'string', 'max:255'],
            'smtp_password'           => ['nullable', 'string', 'max:1000'],
            'smtp_encryption'         => ['nullable', Rule::in(['tls', 'ssl', 'none', ''])],
            'smtp_from_address'       => ['nullable', 'email', 'max:255'],
            'smtp_from_name'          => ['nullable', 'string', 'max:255'],
            'logo'                    => ['nullable','image','mimes:jpg,jpeg,png,webp','max:2048'],
        ]);

        // Handle logo upload (optional)
        if ($request->hasFile('logo')) {
            $newPath = $request->file('logo')->store('logos', 'public');
            // delete old file if exists
            if ($setting->logo_path) {
                Storage::disk('public')->delete($setting->logo_path);
            }
            $setting->logo_path = $newPath;
        }

        $smtpFields = [
            'smtp_enabled',
            'smtp_host',
            'smtp_port',
            'smtp_username',
            'smtp_password',
            'smtp_encryption',
            'smtp_from_address',
            'smtp_from_name',
        ];

        // Fill non-SMTP fields first
        $setting->fill(collect($validated)->except(array_merge(['logo'], $smtpFields))->toArray());

        // Fill SMTP fields safely if columns exist on the settings table
        if (Schema::hasColumn('settings', 'smtp_host')) {
            if ($request->has('smtp_enabled')) {
                $setting->smtp_enabled = $request->boolean('smtp_enabled');
            }
            if ($request->has('smtp_host')) {
                $setting->smtp_host = $request->input('smtp_host');
            }
            if ($request->has('smtp_port')) {
                $port = $request->input('smtp_port');
                $setting->smtp_port = ($port !== null && $port !== '') ? (int) $port : 587;
            }
            if ($request->has('smtp_username')) {
                $setting->smtp_username = $request->input('smtp_username');
            }
            if ($request->has('smtp_password')) {
                $setting->smtp_password = $request->input('smtp_password');
            }
            if ($request->has('smtp_encryption')) {
                $setting->smtp_encryption = $request->input('smtp_encryption') ?: 'tls';
            }
            if ($request->has('smtp_from_address')) {
                $setting->smtp_from_address = $request->input('smtp_from_address');
            }
            if ($request->has('smtp_from_name')) {
                $setting->smtp_from_name = $request->input('smtp_from_name');
            }
        }
        
        // Ensure navigation_style is always updated (it might not be in validated if null)
        if ($request->has('navigation_style')) {
            $setting->navigation_style = $request->input('navigation_style');
        }
        
        // Ensure sale_system is always updated (it might not be in validated if null)
        if ($request->has('sale_system')) {
            $setting->sale_system = $request->input('sale_system');
        }
        
        // Ensure shop_type is always updated (it might not be in validated if null)
        if ($request->has('shop_type')) {
            $setting->shop_type = $request->input('shop_type');
        }

        if ($request->has('currency_code')) {
            $setting->currency_code = $request->input('currency_code');
        }
        if ($request->has('currency_symbol')) {
            $setting->currency_symbol = $request->input('currency_symbol');
        }
        if ($request->has('currency_position')) {
            $setting->currency_position = $request->input('currency_position');
        }
        if ($request->has('currency_decimal_places')) {
            $setting->currency_decimal_places = (int)$request->input('currency_decimal_places');
        }
        
        $setting->save();

        return response()->json($setting->fresh());
    }

    // POST /api/settings/test-smtp
    public function testSmtp(Request $request)
    {
        $this->authorize('update', Setting::class);

        $validated = $request->validate([
            'smtp_host'         => ['required', 'string', 'max:255'],
            'smtp_port'         => ['required', 'integer', 'min:1', 'max:65535'],
            'smtp_username'     => ['nullable', 'string', 'max:255'],
            'smtp_password'     => ['nullable', 'string', 'max:1000'],
            'smtp_encryption'   => ['nullable', Rule::in(['tls', 'ssl', 'none', ''])],
            'smtp_from_address' => ['nullable', 'email', 'max:255'],
            'smtp_from_name'    => ['nullable', 'string', 'max:255'],
            'test_email'        => ['required', 'email', 'max:255'],
        ]);

        $setting = Setting::first();
        $storeName = $validated['smtp_from_name'] ?? $setting?->store_name ?? 'Karobar App';

        Setting::applyRuntimeSmtp([
            'smtp_host'         => $validated['smtp_host'],
            'smtp_port'         => $validated['smtp_port'],
            'smtp_username'     => $validated['smtp_username'] ?? null,
            'smtp_password'     => $validated['smtp_password'] ?? null,
            'smtp_encryption'   => $validated['smtp_encryption'] ?? 'tls',
            'smtp_from_address' => $validated['smtp_from_address'] ?? $validated['smtp_username'] ?? null,
            'smtp_from_name'    => $storeName,
        ]);

        try {
            Mail::mailer('smtp')->raw(
                "Hello!\n\nThis is a test email from {$storeName} (Karobar App).\nYour SMTP mail server ({$validated['smtp_host']}:{$validated['smtp_port']}) is configured and ready to send invoices via email.",
                function ($message) use ($validated, $storeName) {
                    $message->to($validated['test_email'])
                        ->subject("SMTP Test Email — {$storeName}");
                }
            );

            return response()->json([
                'status'  => 'sent',
                'message' => 'Test email sent successfully to ' . $validated['test_email'],
            ]);
        } catch (\Throwable $e) {
            return response()->json([
                'status'  => 'failed',
                'message' => 'SMTP connection failed: ' . $e->getMessage(),
            ], 422);
        }
    }
}
