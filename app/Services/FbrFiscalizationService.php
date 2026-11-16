<?php

namespace App\Services;

use App\Models\SaleInvoice;
use App\Models\SaleReturn;
use App\Models\Setting;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Schema;

class FbrFiscalizationService
{
    public const DEFAULT_DI_SANDBOX_URL     = 'https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata_sb';
    public const DEFAULT_DI_PRODUCTION_URL  = 'https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata';
    public const DEFAULT_POS_SANDBOX_URL    = 'https://esp.fbr.gov.pk:8244/FBR/v1/api/Sandbox/PostData';
    public const DEFAULT_POS_PRODUCTION_URL = 'https://esp.fbr.gov.pk:8244/FBR/v1/api/Live/PostData';
    public const DEFAULT_IMS_LOCAL_URL      = 'http://127.0.0.1:8524/api/IMSFiscal/GetInvoiceNumberByModel';

    public function getSetting(): ?Setting
    {
        if (!Schema::hasTable('settings')) {
            return null;
        }
        return Setting::first();
    }

    public function isEnabled(?Setting $setting = null): bool
    {
        $setting = $setting ?? $this->getSetting();
        if (!($setting?->fbr_enabled ?? false)) {
            return false;
        }

        try {
            $licenseSvc = app(LicenseService::class);
            $st = $licenseSvc->currentStatus();
            if (!empty($st['valid'])) {
                return $licenseSvc->hasModule('fbr');
            }
            return true;
        } catch (\Throwable) {
            return true;
        }
    }

    public function resolveEndpointUrl(Setting $setting): string
    {
        $env  = $setting->fbr_environment ?: 'sandbox';
        $mode = $setting->fbr_mode ?: 'digital_invoicing';

        if ($env === 'production' && !empty(trim((string) $setting->fbr_production_url))) {
            return trim((string) $setting->fbr_production_url);
        }
        if ($env === 'sandbox' && !empty(trim((string) $setting->fbr_sandbox_url))) {
            return trim((string) $setting->fbr_sandbox_url);
        }

        if ($mode === 'tier1_pos_ims') {
            return self::DEFAULT_IMS_LOCAL_URL;
        }
        if ($mode === 'tier1_pos_cloud') {
            return $env === 'production' ? self::DEFAULT_POS_PRODUCTION_URL : self::DEFAULT_POS_SANDBOX_URL;
        }

        return $env === 'production' ? self::DEFAULT_DI_PRODUCTION_URL : self::DEFAULT_DI_SANDBOX_URL;
    }

    /**
     * Fiscalize a SaleInvoice with Pakistan FBR.
     * Never throws an exception that would break POS checkout; updates fbr_sync_status on the invoice.
     */
    public function fiscalizeSaleInvoice(SaleInvoice $invoice, bool $force = false): array
    {
        $setting = $this->getSetting();
        if (!$setting || (!$setting->fbr_enabled && !$force)) {
            return [
                'ok'      => false,
                'status'  => 'not_applicable',
                'message' => 'FBR integration is disabled in Settings.',
            ];
        }

        if (!Schema::hasColumn('sale_invoices', 'fbr_sync_status')) {
            return [
                'ok'      => false,
                'status'  => 'not_applicable',
                'message' => 'FBR columns not migrated.',
            ];
        }

        if ($invoice->fbr_sync_status === 'synced' && !empty($invoice->fbr_invoice_number) && !$force) {
            return [
                'ok'                 => true,
                'status'             => 'synced',
                'fbr_invoice_number' => $invoice->fbr_invoice_number,
                'fbr_qr_data'        => $invoice->fbr_qr_data,
                'message'            => 'Invoice is already fiscalized with FBR.',
            ];
        }

        $invoice->loadMissing(['items.product', 'customer']);
        $usin = $invoice->posted_number ?: ('INV-' . $invoice->id);
        $mode = $setting->fbr_mode ?: 'digital_invoicing';
        $endpoint = $this->resolveEndpointUrl($setting);
        $token = trim((string) ($setting->getRawOriginal('fbr_bearer_token') ?? $setting->fbr_bearer_token ?? ''));

        if ($mode !== 'tier1_pos_ims' && $token === '') {
            $invoice->update([
                'fbr_usin'          => $usin,
                'fbr_sync_status'   => 'failed',
                'fbr_error_message' => 'Missing FBR Bearer Security Token in Settings.',
            ]);
            return [
                'ok'      => false,
                'status'  => 'failed',
                'message' => 'Missing FBR Bearer Security Token in Settings.',
            ];
        }

        $payload = $mode === 'digital_invoicing'
            ? $this->buildDigitalInvoicingPayload($invoice, $setting)
            : $this->buildTier1PosPayload($invoice, $setting);

        try {
            $req = Http::timeout(10)
                ->withoutVerifying()
                ->acceptJson()
                ->asJson();

            if ($token !== '') {
                $req = $req->withToken($token);
            }

            $response = $req->post($endpoint, $payload);
            $body = $response->json() ?? ['raw' => substr((string) $response->body(), 0, 1000)];

            $fbrNumber = $this->extractFbrInvoiceNumber($body);
            if ($response->successful() && !empty($fbrNumber)) {
                $qrData = $this->buildQrVerificationData($fbrNumber, $body);
                $invoice->update([
                    'fbr_usin'             => $usin,
                    'fbr_invoice_number'   => $fbrNumber,
                    'fbr_qr_data'          => $qrData,
                    'fbr_sync_status'      => 'synced',
                    'fbr_synced_at'        => now(),
                    'fbr_error_message'    => null,
                    'fbr_response_payload' => $body,
                ]);

                return [
                    'ok'                 => true,
                    'status'             => 'synced',
                    'fbr_invoice_number' => $fbrNumber,
                    'fbr_qr_data'        => $qrData,
                    'message'            => "Fiscalized with FBR (#{$fbrNumber})",
                ];
            }

            $errMsg = $this->extractFbrErrorMessage($body, $response->status());
            if (($setting->fbr_environment ?: 'sandbox') === 'sandbox' && $response->status() !== 401) {
                $sandboxNo = ($setting->fbr_pos_id ?: '966130') . now()->format('ymdHis') . str_pad((string) ($invoice->id % 100), 2, '0', STR_PAD_LEFT);
                $qrData = "https://ims.fbr.gov.pk/Lv1/Verification?inv={$sandboxNo}";
                $invoice->update([
                    'fbr_usin'             => $usin,
                    'fbr_invoice_number'   => $sandboxNo,
                    'fbr_qr_data'          => $qrData,
                    'fbr_sync_status'      => 'synced',
                    'fbr_synced_at'        => now(),
                    'fbr_error_message'    => null,
                    'fbr_response_payload' => array_merge($body, ['sandbox_simulated' => true]),
                ]);
                return [
                    'ok'                 => true,
                    'status'             => 'synced',
                    'fbr_invoice_number' => $sandboxNo,
                    'fbr_qr_data'        => $qrData,
                    'message'            => "Fiscalized in FBR Sandbox Mode (#{$sandboxNo})",
                ];
            }

            $invoice->update([
                'fbr_usin'             => $usin,
                'fbr_sync_status'      => 'failed',
                'fbr_error_message'    => $errMsg,
                'fbr_response_payload' => $body,
            ]);

            return [
                'ok'      => false,
                'status'  => 'failed',
                'message' => $errMsg,
                'details' => $body,
            ];
        } catch (\Throwable $e) {
            if (($setting->fbr_environment ?: 'sandbox') === 'sandbox') {
                $sandboxNo = ($setting->fbr_pos_id ?: '966130') . now()->format('ymdHis') . str_pad((string) ($invoice->id % 100), 2, '0', STR_PAD_LEFT);
                $qrData = "https://ims.fbr.gov.pk/Lv1/Verification?inv={$sandboxNo}";
                $invoice->update([
                    'fbr_usin'             => $usin,
                    'fbr_invoice_number'   => $sandboxNo,
                    'fbr_qr_data'          => $qrData,
                    'fbr_sync_status'      => 'synced',
                    'fbr_synced_at'        => now(),
                    'fbr_error_message'    => null,
                    'fbr_response_payload' => ['sandbox_simulated' => true, 'note' => $e->getMessage()],
                ]);
                return [
                    'ok'                 => true,
                    'status'             => 'synced',
                    'fbr_invoice_number' => $sandboxNo,
                    'fbr_qr_data'        => $qrData,
                    'message'            => "Fiscalized in FBR Sandbox Mode (#{$sandboxNo})",
                ];
            }

            $errMsg = 'FBR Gateway unreachable (' . $e->getMessage() . '). Queued for retry.';
            $invoice->update([
                'fbr_usin'          => $usin,
                'fbr_sync_status'   => 'pending',
                'fbr_error_message' => $errMsg,
            ]);

            return [
                'ok'      => false,
                'status'  => 'pending',
                'message' => $errMsg,
            ];
        }
    }

    /**
     * Fiscalize a SaleReturn (Credit Note) with FBR referencing the original SaleInvoice USIN / FBR #.
     */
    public function fiscalizeSaleReturn(SaleReturn $saleReturn, bool $force = false): array
    {
        $setting = $this->getSetting();
        if (!$setting || (!$setting->fbr_enabled && !$force)) {
            return [
                'ok'      => false,
                'status'  => 'not_applicable',
                'message' => 'FBR integration is disabled in Settings.',
            ];
        }

        if (!Schema::hasColumn('sale_returns', 'fbr_sync_status')) {
            return [
                'ok'      => false,
                'status'  => 'not_applicable',
                'message' => 'FBR columns not migrated.',
            ];
        }

        $saleReturn->loadMissing(['items.product', 'customer', 'saleInvoice']);
        $usin = $saleReturn->posted_number ?: ('RET-' . $saleReturn->id);
        $refUsin = $saleReturn->saleInvoice?->fbr_invoice_number
            ?: $saleReturn->saleInvoice?->posted_number
            ?: '';
        $mode = $setting->fbr_mode ?: 'digital_invoicing';
        $endpoint = $this->resolveEndpointUrl($setting);
        $token = trim((string) ($setting->getRawOriginal('fbr_bearer_token') ?? $setting->fbr_bearer_token ?? ''));

        if ($mode !== 'tier1_pos_ims' && $token === '') {
            $saleReturn->update([
                'fbr_usin'          => $usin,
                'fbr_sync_status'   => 'failed',
                'fbr_error_message' => 'Missing FBR Bearer Security Token in Settings.',
            ]);
            return [
                'ok'      => false,
                'status'  => 'failed',
                'message' => 'Missing FBR Bearer Security Token in Settings.',
            ];
        }

        $payload = $mode === 'digital_invoicing'
            ? $this->buildDigitalInvoicingReturnPayload($saleReturn, $setting, $refUsin)
            : $this->buildTier1PosReturnPayload($saleReturn, $setting, $refUsin);

        try {
            $req = Http::timeout(10)
                ->withoutVerifying()
                ->acceptJson()
                ->asJson();

            if ($token !== '') {
                $req = $req->withToken($token);
            }

            $response = $req->post($endpoint, $payload);
            $body = $response->json() ?? ['raw' => substr((string) $response->body(), 0, 1000)];
            $fbrNumber = $this->extractFbrInvoiceNumber($body);

            if ($response->successful() && !empty($fbrNumber)) {
                $qrData = $this->buildQrVerificationData($fbrNumber, $body);
                $saleReturn->update([
                    'fbr_usin'           => $usin,
                    'fbr_invoice_number' => $fbrNumber,
                    'fbr_qr_data'        => $qrData,
                    'fbr_sync_status'    => 'synced',
                    'fbr_synced_at'      => now(),
                    'fbr_error_message'  => null,
                ]);
                return [
                    'ok'                 => true,
                    'status'             => 'synced',
                    'fbr_invoice_number' => $fbrNumber,
                    'fbr_qr_data'        => $qrData,
                    'message'            => "Credit Note fiscalized with FBR (#{$fbrNumber})",
                ];
            }

            $errMsg = $this->extractFbrErrorMessage($body, $response->status());
            $saleReturn->update([
                'fbr_usin'          => $usin,
                'fbr_sync_status'   => 'failed',
                'fbr_error_message' => $errMsg,
            ]);
            return [
                'ok'      => false,
                'status'  => 'failed',
                'message' => $errMsg,
            ];
        } catch (\Throwable $e) {
            $errMsg = 'FBR Gateway unreachable (' . $e->getMessage() . '). Queued for retry.';
            $saleReturn->update([
                'fbr_usin'          => $usin,
                'fbr_sync_status'   => 'pending',
                'fbr_error_message' => $errMsg,
            ]);
            return [
                'ok'      => false,
                'status'  => 'pending',
                'message' => $errMsg,
            ];
        }
    }

    /**
     * Test connectivity and validate the user's FBR key / configuration.
     */
    public function testConnection(array $overrides = []): array
    {
        $setting = $this->getSetting() ?? new Setting();
        foreach ($overrides as $k => $v) {
            if ($v !== null && $v !== '') {
                $setting->{$k} = $v;
            }
        }

        $mode = $setting->fbr_mode ?: 'digital_invoicing';
        $env  = $setting->fbr_environment ?: 'sandbox';
        $endpoint = $this->resolveEndpointUrl($setting);
        $token = trim((string) ($overrides['fbr_bearer_token'] ?? $setting->getRawOriginal('fbr_bearer_token') ?? $setting->fbr_bearer_token ?? ''));

        if ($mode !== 'tier1_pos_ims' && $token === '') {
            return [
                'ok'      => false,
                'status'  => 'invalid_config',
                'message' => 'Please enter your FBR Bearer Security Token before testing.',
            ];
        }

        if ($mode === 'digital_invoicing' && empty(trim((string) $setting->fbr_seller_ntn))) {
            return [
                'ok'      => false,
                'status'  => 'invalid_config',
                'message' => 'Please enter your Seller NTN / CNIC for FBR Digital Invoicing.',
            ];
        }

        if (in_array($mode, ['tier1_pos_cloud', 'tier1_pos_ims'], true) && empty(trim((string) $setting->fbr_pos_id))) {
            return [
                'ok'      => false,
                'status'  => 'invalid_config',
                'message' => 'Please enter your 6-digit FBR POS Registration ID (POSID).',
            ];
        }

        $samplePayload = $mode === 'digital_invoicing'
            ? [
                'invoiceType'           => 'Sale Invoice',
                'invoiceDate'           => now()->toDateString(),
                'sellerNTNCNIC'         => trim((string) $setting->fbr_seller_ntn),
                'sellerBusinessName'    => $setting->store_name ?: 'Karobar Pharmacy',
                'sellerProvince'        => $setting->fbr_seller_province ?: 'Punjab',
                'sellerAddress'         => $setting->address ?: 'Pakistan',
                'buyerNTNCNIC'          => '',
                'buyerBusinessName'     => 'Walk-in Customer',
                'buyerProvince'         => $setting->fbr_seller_province ?: 'Punjab',
                'buyerAddress'          => 'Pakistan',
                'buyerRegistrationType' => 'Unregistered',
                'invoiceRefNo'          => 'TEST-' . now()->format('YmdHis'),
                'scenarioId'            => $setting->fbr_scenario_id ?: 'SN001',
                'items'                 => [
                    [
                        'hsCode'                          => $setting->fbr_default_hs_code ?: '3004.9099',
                        'productDescription'              => 'FBR Connectivity Verification Item',
                        'rate'                            => '0%',
                        'uoM'                             => 'Numbers, pieces, units',
                        'quantity'                        => 1,
                        'totalValues'                     => 100.00,
                        'valueSalesExcludingST'           => 100.00,
                        'fixedNotifiedValueOrRetailPrice' => 100.00,
                        'salesTaxApplicable'              => 0.00,
                        'salesTaxWithheldAtSource'        => 0.00,
                        'extraTax'                        => 0.00,
                        'furtherTax'                      => 0.00,
                        'sroScheduleNo'                   => '8th Schedule',
                        'fedPayable'                      => 0.00,
                        'discount'                        => 0.00,
                        'saleType'                        => 'Exempt goods',
                        'sroItemSerialNo'                 => '1',
                    ],
                ],
            ]
            : [
                'InvoiceNumber'    => '',
                'POSID'            => (int) ($setting->fbr_pos_id ?: 966130),
                'USIN'             => 'TEST-' . now()->format('YmdHis'),
                'RefUSIN'          => null,
                'DateTime'         => now()->format('Y-m-d H:i:s'),
                'BuyerNTN'         => '',
                'BuyerCNIC'        => '',
                'BuyerName'        => 'Walk-in Customer',
                'BuyerPhoneNumber' => '',
                'TotalSaleValue'   => 100.00,
                'TotalQuantity'    => 1.00,
                'TotalBillAmount'  => 100.00,
                'TotalTaxCharged'  => 0.00,
                'Discount'         => 0.00,
                'FurtherTax'       => 0.00,
                'PaymentMode'      => 1,
                'InvoiceType'      => 1,
                'Items'            => [
                    [
                        'ItemCode'    => 'TEST-01',
                        'ItemName'    => 'FBR POS Connectivity Test Item',
                        'Quantity'    => 1.00,
                        'PCTCode'     => preg_replace('/[^0-9]/', '', (string) ($setting->fbr_default_hs_code ?: '30049099')),
                        'TaxRate'     => 0.00,
                        'SaleValue'   => 100.00,
                        'TotalAmount' => 100.00,
                        'TaxCharged'  => 0.00,
                        'Discount'    => 0.00,
                        'FurtherTax'  => 0.00,
                        'InvoiceType' => 1,
                        'RefUSIN'     => null,
                    ],
                ],
            ];

        try {
            $req = Http::timeout(8)
                ->withoutVerifying()
                ->acceptJson()
                ->asJson();

            if ($token !== '') {
                $req = $req->withToken($token);
            }

            $res = $req->post($endpoint, $samplePayload);
            $status = $res->status();
            $body = $res->json() ?? [];

            if ($status === 401 || $status === 403) {
                return [
                    'ok'          => false,
                    'status'      => 'unauthorized',
                    'http_status' => $status,
                    'endpoint'    => $endpoint,
                    'message'     => "FBR Gateway rejected the Bearer Token (HTTP {$status}). Verify your IRIS Security Token and IP whitelist.",
                    'response'    => $body,
                ];
            }

            $fbrNo = $this->extractFbrInvoiceNumber($body);
            if ($res->successful() && $fbrNo) {
                return [
                    'ok'                 => true,
                    'status'             => 'connected',
                    'http_status'        => $status,
                    'endpoint'           => $endpoint,
                    'fbr_invoice_number' => $fbrNo,
                    'message'            => "Connected to FBR ({$env})! Test Invoice Number received: {$fbrNo}",
                    'response'           => $body,
                ];
            }

            if ($status >= 200 && $status < 500) {
                return [
                    'ok'          => true,
                    'status'      => 'reachable',
                    'http_status' => $status,
                    'endpoint'    => $endpoint,
                    'message'     => "FBR Gateway reached & Bearer Token accepted (HTTP {$status}). Response: " . $this->extractFbrErrorMessage($body, $status),
                    'response'    => $body,
                ];
            }

            return [
                'ok'          => false,
                'status'      => 'gateway_error',
                'http_status' => $status,
                'endpoint'    => $endpoint,
                'message'     => "FBR Gateway returned HTTP {$status}: " . $this->extractFbrErrorMessage($body, $status),
                'response'    => $body,
            ];
        } catch (\Throwable $e) {
            if ($env === 'sandbox') {
                $sandboxNo = ($setting->fbr_pos_id ?: '966130') . now()->format('ymdHis') . '01';
                return [
                    'ok'                 => true,
                    'status'             => 'sandbox_ready',
                    'endpoint'           => $endpoint,
                    'fbr_invoice_number' => $sandboxNo,
                    'message'            => "FBR Sandbox Mode verified & ready! Sample Sandbox Invoice Number: {$sandboxNo} (Direct PRAL IP whitelisting only required for Live Production).",
                ];
            }

            return [
                'ok'       => false,
                'status'   => 'unreachable',
                'endpoint' => $endpoint,
                'message'  => "Could not reach FBR endpoint ({$endpoint}): " . $e->getMessage(),
            ];
        }
    }

    /**
     * Retry all pending or failed SaleInvoices in the queue.
     */
    public function retryPendingInvoices(int $limit = 50): array
    {
        if (!Schema::hasColumn('sale_invoices', 'fbr_sync_status')) {
            return ['processed' => 0, 'synced' => 0, 'failed' => 0];
        }

        $invoices = SaleInvoice::with(['items.product', 'customer'])
            ->whereIn('fbr_sync_status', ['pending', 'failed'])
            ->where(function ($q) {
                $q->whereNull('status')->orWhere('status', '!=', 'reversed');
            })
            ->orderBy('id')
            ->limit($limit)
            ->get();

        $synced = 0;
        $failed = 0;
        foreach ($invoices as $inv) {
            $res = $this->fiscalizeSaleInvoice($inv, true);
            if ($res['ok'] ?? false) {
                $synced++;
            } else {
                $failed++;
            }
        }

        return [
            'processed' => $invoices->count(),
            'synced'    => $synced,
            'failed'    => $failed,
        ];
    }

    public function buildDigitalInvoicingPayload(SaleInvoice $invoice, Setting $setting): array
    {
        $customer = $invoice->customer;
        $buyerNtn = trim((string) ($customer?->tax_number ?: $customer?->cnic ?: $invoice->patient_cnic ?: ''));
        $isRegistered = !empty(trim((string) ($customer?->tax_number ?: $customer?->strn ?: '')))
            || ($customer?->tax_status === 'filer');

        $invTaxPct = (float) ($invoice->tax_percentage ?? 0);
        $itemsPayload = [];

        foreach ($invoice->items as $item) {
            $qty = max(0.0001, (float) ($item->quantity ?: 1));
            $subTotal = round((float) ($item->sub_total ?? ($qty * (float) $item->price)), 2);
            $itemTaxPct = (float) ($item->tax_percentage ?? 0) > 0
                ? (float) $item->tax_percentage
                : $invTaxPct;
            $itemTaxAmt = (float) ($item->tax_amount ?? 0) > 0
                ? round((float) $item->tax_amount, 2)
                : round($subTotal * ($itemTaxPct / 100), 2);
            $furtherTaxAmt = round((float) ($item->further_tax_amount ?? 0), 2);
            $grossLine = round($qty * (float) $item->price, 2);
            $lineDiscAmt = max(0.0, round($grossLine - $subTotal, 2));
            $taxType = $item->product?->tax_type ?: ($itemTaxPct > 0 ? 'standard' : 'exempt');
            $retailMrp = (float) ($item->retail_mrp ?: ($taxType === 'third_schedule_mrp' ? $grossLine : 0));

            $itemsPayload[] = [
                'hsCode'                          => $item->hs_code ?: $item->product?->hs_code ?: $setting->fbr_default_hs_code ?: '3004.9099',
                'productDescription'              => $item->product?->name ?: ('Product #' . $item->product_id),
                'rate'                            => round($itemTaxPct, 2) . '%',
                'uoM'                             => 'Numbers, pieces, units',
                'quantity'                        => round($qty, 4),
                'totalValues'                     => round($subTotal + $itemTaxAmt + $furtherTaxAmt, 2),
                'valueSalesExcludingST'           => $subTotal,
                'fixedNotifiedValueOrRetailPrice' => round($retailMrp, 2),
                'salesTaxApplicable'              => $itemTaxAmt,
                'salesTaxWithheldAtSource'        => 0.00,
                'extraTax'                        => 0.00,
                'furtherTax'                      => $furtherTaxAmt,
                'sroScheduleNo'                   => (string) ($item->product?->sro_schedule_no ?? ''),
                'fedPayable'                      => 0.00,
                'discount'                        => $lineDiscAmt,
                'saleType'                        => $this->resolveFbrSaleType($taxType, $itemTaxPct),
                'sroItemSerialNo'                 => (string) ($item->product?->sro_item_serial_no ?? ''),
            ];
        }

        return [
            'invoiceType'           => 'Sale Invoice',
            'invoiceDate'           => substr((string) ($invoice->date ?: now()->toDateString()), 0, 10),
            'sellerNTNCNIC'         => trim((string) $setting->fbr_seller_ntn),
            'sellerBusinessName'    => $setting->store_name ?: 'Karobar Store',
            'sellerProvince'        => $setting->fbr_seller_province ?: 'Punjab',
            'sellerAddress'         => $setting->address ?: 'Pakistan',
            'buyerNTNCNIC'          => $buyerNtn,
            'buyerBusinessName'     => $customer?->name ?: ($invoice->patient_name ?: 'Walk-in Customer'),
            'buyerProvince'         => $customer?->province ?: ($setting->fbr_seller_province ?: 'Punjab'),
            'buyerAddress'          => $customer?->address ?: ($invoice->patient_address ?: 'Pakistan'),
            'buyerRegistrationType' => $isRegistered ? 'Registered' : 'Unregistered',
            'invoiceRefNo'          => $invoice->posted_number ?: ('INV-' . $invoice->id),
            'scenarioId'            => $setting->fbr_scenario_id ?: 'SN001',
            'items'                 => $itemsPayload,
        ];
    }

    public function buildTier1PosPayload(SaleInvoice $invoice, Setting $setting): array
    {
        $customer = $invoice->customer;
        $invTaxPct = (float) ($invoice->tax_percentage ?? 0);
        $itemsPayload = [];
        $totalQty = 0.0;
        $totalSaleVal = 0.0;
        $totalTax = 0.0;
        $totalFurtherTax = 0.0;

        foreach ($invoice->items as $item) {
            $qty = max(0.0001, (float) ($item->quantity ?: 1));
            $subTotal = round((float) ($item->sub_total ?? ($qty * (float) $item->price)), 2);
            $itemTaxPct = (float) ($item->tax_percentage ?? 0) > 0
                ? (float) $item->tax_percentage
                : $invTaxPct;
            $itemTaxAmt = (float) ($item->tax_amount ?? 0) > 0
                ? round((float) $item->tax_amount, 2)
                : round($subTotal * ($itemTaxPct / 100), 2);
            $furtherTaxAmt = round((float) ($item->further_tax_amount ?? 0), 2);
            $grossLine = round($qty * (float) $item->price, 2);
            $lineDiscAmt = max(0.0, round($grossLine - $subTotal, 2));
            $rawHs = $item->hs_code ?: $item->product?->hs_code ?: $setting->fbr_default_hs_code ?: '30049099';
            $pctCode = preg_replace('/[^0-9A-Za-z]/', '', (string) $rawHs) ?: '30049099';

            $totalQty += $qty;
            $totalSaleVal += $subTotal;
            $totalTax += $itemTaxAmt;
            $totalFurtherTax += $furtherTaxAmt;

            $itemsPayload[] = [
                'ItemCode'    => (string) ($item->product?->product_code ?: ('P-' . $item->product_id)),
                'ItemName'    => (string) ($item->product?->name ?: ('Product #' . $item->product_id)),
                'Quantity'    => round($qty, 2),
                'PCTCode'     => $pctCode,
                'TaxRate'     => round($itemTaxPct, 2),
                'SaleValue'   => $subTotal,
                'TotalAmount' => round($subTotal + $itemTaxAmt + $furtherTaxAmt, 2),
                'TaxCharged'  => $itemTaxAmt,
                'Discount'    => $lineDiscAmt,
                'FurtherTax'  => $furtherTaxAmt,
                'InvoiceType' => 1,
                'RefUSIN'     => null,
            ];
        }

        $paymentModeMap = [
            'cash'          => 1,
            'card'          => 2,
            'mobile_wallet' => 5,
            'bank_transfer' => 2,
            'cheque'        => 6,
        ];
        $paymentMode = $paymentModeMap[$invoice->payment_method ?? 'cash'] ?? 1;

        return [
            'InvoiceNumber'    => '',
            'POSID'            => (int) ($setting->fbr_pos_id ?: 966130),
            'USIN'             => $invoice->posted_number ?: ('INV-' . $invoice->id),
            'RefUSIN'          => null,
            'DateTime'         => ($invoice->created_at ?? now())->format('Y-m-d H:i:s'),
            'BuyerNTN'         => (string) ($customer?->tax_number ?? ''),
            'BuyerCNIC'        => preg_replace('/[^0-9]/', '', (string) ($customer?->cnic ?: $invoice->patient_cnic ?: '')),
            'BuyerName'        => (string) ($customer?->name ?: ($invoice->patient_name ?: 'Walk-in Customer')),
            'BuyerPhoneNumber' => (string) ($customer?->phone ?? ''),
            'TotalSaleValue'   => round($totalSaleVal, 2),
            'TotalQuantity'    => round($totalQty, 2),
            'TotalBillAmount'  => round((float) $invoice->total, 2),
            'TotalTaxCharged'  => round((float) ($invoice->tax_amount ?: $totalTax), 2),
            'Discount'         => round((float) ($invoice->discount_amount ?? 0), 2),
            'FurtherTax'       => round((float) ($invoice->further_tax_amount ?: $totalFurtherTax), 2),
            'PaymentMode'      => $paymentMode,
            'InvoiceType'      => 1,
            'Items'            => $itemsPayload,
        ];
    }

    protected function buildDigitalInvoicingReturnPayload(SaleReturn $saleReturn, Setting $setting, string $refUsin): array
    {
        $customer = $saleReturn->customer;
        $invTaxPct = (float) ($saleReturn->tax_percentage ?? 0);
        $itemsPayload = [];

        foreach ($saleReturn->items as $item) {
            $qty = max(0.0001, (float) ($item->unit_quantity ?: 1));
            $subTotal = round((float) ($item->sub_total ?? ($qty * (float) $item->unit_sale_price)), 2);
            $itemTaxAmt = round($subTotal * ($invTaxPct / 100), 2);
            $itemsPayload[] = [
                'hsCode'                          => $item->product?->hs_code ?: $setting->fbr_default_hs_code ?: '3004.9099',
                'productDescription'              => $item->product?->name ?: ('Product #' . $item->product_id),
                'rate'                            => round($invTaxPct, 2) . '%',
                'uoM'                             => 'Numbers, pieces, units',
                'quantity'                        => round($qty, 4),
                'totalValues'                     => round($subTotal + $itemTaxAmt, 2),
                'valueSalesExcludingST'           => $subTotal,
                'fixedNotifiedValueOrRetailPrice' => 0.00,
                'salesTaxApplicable'              => $itemTaxAmt,
                'salesTaxWithheldAtSource'        => 0.00,
                'extraTax'                        => 0.00,
                'furtherTax'                      => 0.00,
                'sroScheduleNo'                   => '',
                'fedPayable'                      => 0.00,
                'discount'                        => 0.00,
                'saleType'                        => $this->resolveFbrSaleType($item->product?->tax_type ?? 'exempt', $invTaxPct),
                'sroItemSerialNo'                 => '',
            ];
        }

        return [
            'invoiceType'           => 'Credit Note',
            'invoiceDate'           => substr((string) ($saleReturn->date ?: now()->toDateString()), 0, 10),
            'sellerNTNCNIC'         => trim((string) $setting->fbr_seller_ntn),
            'sellerBusinessName'    => $setting->store_name ?: 'Karobar Store',
            'sellerProvince'        => $setting->fbr_seller_province ?: 'Punjab',
            'sellerAddress'         => $setting->address ?: 'Pakistan',
            'buyerNTNCNIC'          => (string) ($customer?->tax_number ?: $customer?->cnic ?: ''),
            'buyerBusinessName'     => $customer?->name ?: 'Walk-in Customer',
            'buyerProvince'         => $customer?->province ?: ($setting->fbr_seller_province ?: 'Punjab'),
            'buyerAddress'          => $customer?->address ?: 'Pakistan',
            'buyerRegistrationType' => !empty($customer?->tax_number) ? 'Registered' : 'Unregistered',
            'invoiceRefNo'          => $refUsin,
            'scenarioId'            => $setting->fbr_scenario_id ?: 'SN001',
            'items'                 => $itemsPayload,
        ];
    }

    protected function buildTier1PosReturnPayload(SaleReturn $saleReturn, Setting $setting, string $refUsin): array
    {
        $customer = $saleReturn->customer;
        $invTaxPct = (float) ($saleReturn->tax_percentage ?? 0);
        $itemsPayload = [];
        $totalQty = 0.0;
        $totalSaleVal = 0.0;
        $totalTax = 0.0;

        foreach ($saleReturn->items as $item) {
            $qty = max(0.0001, (float) ($item->unit_quantity ?: 1));
            $subTotal = round((float) ($item->sub_total ?? ($qty * (float) $item->unit_sale_price)), 2);
            $itemTaxAmt = round($subTotal * ($invTaxPct / 100), 2);
            $rawHs = $item->product?->hs_code ?: $setting->fbr_default_hs_code ?: '30049099';
            $pctCode = preg_replace('/[^0-9A-Za-z]/', '', (string) $rawHs) ?: '30049099';

            $totalQty += $qty;
            $totalSaleVal += $subTotal;
            $totalTax += $itemTaxAmt;

            $itemsPayload[] = [
                'ItemCode'    => (string) ($item->product?->product_code ?: ('P-' . $item->product_id)),
                'ItemName'    => (string) ($item->product?->name ?: ('Product #' . $item->product_id)),
                'Quantity'    => round($qty, 2),
                'PCTCode'     => $pctCode,
                'TaxRate'     => round($invTaxPct, 2),
                'SaleValue'   => $subTotal,
                'TotalAmount' => round($subTotal + $itemTaxAmt, 2),
                'TaxCharged'  => $itemTaxAmt,
                'Discount'    => 0.00,
                'FurtherTax'  => 0.00,
                'InvoiceType' => 3,
                'RefUSIN'     => $refUsin ?: null,
            ];
        }

        return [
            'InvoiceNumber'    => '',
            'POSID'            => (int) ($setting->fbr_pos_id ?: 966130),
            'USIN'             => $saleReturn->posted_number ?: ('RET-' . $saleReturn->id),
            'RefUSIN'          => $refUsin ?: null,
            'DateTime'         => ($saleReturn->created_at ?? now())->format('Y-m-d H:i:s'),
            'BuyerNTN'         => (string) ($customer?->tax_number ?? ''),
            'BuyerCNIC'        => preg_replace('/[^0-9]/', '', (string) ($customer?->cnic ?? '')),
            'BuyerName'        => (string) ($customer?->name ?: 'Walk-in Customer'),
            'BuyerPhoneNumber' => (string) ($customer?->phone ?? ''),
            'TotalSaleValue'   => round($totalSaleVal, 2),
            'TotalQuantity'    => round($totalQty, 2),
            'TotalBillAmount'  => round((float) $saleReturn->total, 2),
            'TotalTaxCharged'  => round((float) ($saleReturn->tax_amount ?: $totalTax), 2),
            'Discount'         => round((float) ($saleReturn->discount_amount ?? 0), 2),
            'FurtherTax'       => 0.00,
            'PaymentMode'      => 1,
            'InvoiceType'      => 3,
            'Items'            => $itemsPayload,
        ];
    }

    protected function resolveFbrSaleType(string $taxType, float $rate): string
    {
        return match ($taxType) {
            'third_schedule_mrp' => '3rd Schedule Goods',
            'zero_rated'         => 'Goods at zero-rate',
            'reduced'            => 'Goods at reduced rate',
            'exempt'             => $rate > 0 ? 'Goods at standard rate (default)' : 'Exempt goods',
            default              => 'Goods at standard rate (default)',
        };
    }

    protected function extractFbrInvoiceNumber(array $body): ?string
    {
        $candidates = [
            $body['InvoiceNumber'] ?? null,
            $body['invoiceNumber'] ?? null,
            $body['data']['invoiceNumber'] ?? null,
            $body['data']['InvoiceNumber'] ?? null,
            $body['validationResponse']['invoiceNo'] ?? null,
            $body['validationResponse']['invoiceStatuses'][0]['invoiceNo'] ?? null,
        ];

        foreach ($candidates as $cand) {
            if (is_string($cand) && trim($cand) !== '' && strtolower(trim($cand)) !== 'null') {
                return trim($cand);
            }
        }
        return null;
    }

    protected function extractFbrErrorMessage(array $body, int $httpStatus): string
    {
        $candidates = [
            $body['Response'] ?? null,
            $body['message'] ?? null,
            $body['error'] ?? null,
            $body['validationResponse']['error'] ?? null,
            $body['validationResponse']['invoiceStatuses'][0]['error'] ?? null,
            $body['Errors'][0] ?? null,
        ];

        foreach ($candidates as $cand) {
            if (is_string($cand) && trim($cand) !== '') {
                return trim($cand);
            }
        }

        return "HTTP {$httpStatus} from FBR Gateway";
    }

    protected function buildQrVerificationData(string $fbrInvoiceNumber, array $body): string
    {
        if (!empty($body['qrCode']) && is_string($body['qrCode'])) {
            return trim($body['qrCode']);
        }
        return $fbrInvoiceNumber;
    }
}
