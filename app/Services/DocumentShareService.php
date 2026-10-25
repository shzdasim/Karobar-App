<?php

namespace App\Services;

use App\Models\Customer;
use App\Models\CustomerLedger;
use App\Models\Payment;
use App\Models\PurchaseOrder;
use App\Models\SaleInvoice;
use App\Models\Setting;
use App\Models\Supplier;
use App\Models\SupplierLedger;
use Barryvdh\DomPDF\Facade\Pdf;
use Carbon\Carbon;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Facades\URL;
use InvalidArgumentException;
use RuntimeException;

class DocumentShareService
{
    public const SUPPORTED_TYPES = [
        'sale_invoice',
        'customer_ledger',
        'supplier_ledger',
        'payment',
        'purchase_order',
    ];

    /**
     * Normalize a local or international phone number into E.164 digits (without '+' or '00')
     * suitable for https://wa.me/{phone}.
     * Examples:
     *   "0300-1234567"   -> "923001234567"
     *   "+92 321 9876543"-> "923219876543"
     *   "00923331112233" -> "923331112233"
     *   "3001234567"     -> "923001234567"
     */
    public function normalizePhone(?string $phone, string $defaultCountryCode = '92'): string
    {
        if (!$phone) {
            return '';
        }

        $digits = preg_replace('/\D+/', '', $phone) ?? '';
        if ($digits === '') {
            return '';
        }

        if (str_starts_with($digits, '00')) {
            $digits = substr($digits, 2);
        }

        // Pakistani 11-digit mobile starting with 03 (e.g. 03001234567)
        if (strlen($digits) === 11 && str_starts_with($digits, '03')) {
            return $defaultCountryCode . substr($digits, 1);
        }

        // Pakistani 10-digit mobile starting with 3 (e.g. 3001234567)
        if (strlen($digits) === 10 && str_starts_with($digits, '3')) {
            return $defaultCountryCode . $digits;
        }

        // Generic leading 0 for local numbers (10-11 digits)
        if (str_starts_with($digits, '0') && strlen($digits) >= 10 && strlen($digits) <= 11) {
            return $defaultCountryCode . substr($digits, 1);
        }

        return $digits;
    }

    /**
     * Build a 7-day cryptographically signed public PDF URL for WhatsApp / external sharing.
     */
    public function buildSignedPdfUrl(string $type, array $params): string
    {
        $query = array_filter([
            'type'        => $type,
            'id'          => $params['id'] ?? null,
            'customer_id' => $params['customer_id'] ?? null,
            'supplier_id' => $params['supplier_id'] ?? null,
            'from'        => $params['from'] ?? null,
            'to'          => $params['to'] ?? null,
        ], fn ($v) => $v !== null && $v !== '');

        return URL::temporarySignedRoute(
            'public.documents.pdf',
            now()->addDays(7),
            $query
        );
    }

    /**
     * Check whether server-side WhatsApp Cloud API credentials are configured.
     */
    public function isWhatsAppApiConfigured(): bool
    {
        return !empty(config('services.whatsapp.access_token'))
            && !empty(config('services.whatsapp.phone_number_id'));
    }

    /**
     * Prepare share metadata (recipient details, pre-formatted WhatsApp caption, Email subject & body, and PDF endpoints).
     */
    public function buildMetadata(string $type, array $params): array
    {
        $context = $this->resolveDocumentContext($type, $params);
        $signedPdfUrl = $this->buildSignedPdfUrl($type, $params);
        $normalizedPhone = $this->normalizePhone($context['recipient_phone']);

        $whatsappCaption = trim($context['whatsapp_text']);
        $whatsappUrl = $normalizedPhone !== ''
            ? 'https://wa.me/' . $normalizedPhone . '?text=' . rawurlencode($whatsappCaption)
            : 'https://wa.me/?text=' . rawurlencode($whatsappCaption);

        $apiQuery = http_build_query(array_filter([
            'type'        => $type,
            'id'          => $params['id'] ?? null,
            'customer_id' => $params['customer_id'] ?? null,
            'supplier_id' => $params['supplier_id'] ?? null,
            'from'        => $params['from'] ?? null,
            'to'          => $params['to'] ?? null,
        ], fn ($v) => $v !== null && $v !== ''));

        return [
            'type'                    => $type,
            'document_title'          => $context['title'],
            'filename'                => $context['filename'],
            'party_type'              => $context['party_type'],
            'party_id'                => $context['party_id'],
            'recipient_name'          => $context['recipient_name'],
            'recipient_phone'         => $context['recipient_phone'] ?? '',
            'normalized_phone'        => $normalizedPhone,
            'recipient_email'         => $context['recipient_email'] ?? '',
            'whatsapp_text'           => $whatsappCaption,
            'whatsapp_url'            => $whatsappUrl,
            'whatsapp_api_configured' => $this->isWhatsAppApiConfigured(),
            'email_subject'           => $context['email_subject'],
            'email_body'              => $context['email_body'],
            'signed_pdf_url'          => $signedPdfUrl,
            'pdf_preview_url'         => url('/api/share/pdf?' . $apiQuery),
            'pdf_download_url'        => url('/api/share/pdf?' . $apiQuery . '&download=1'),
        ];
    }

    /**
     * Build the actual PDF document and send/prepare it for WhatsApp (with no URL link in caption).
     */
    public function sendOrBuildWhatsAppPdf(string $type, array $params, ?string $phone, string $caption): array
    {
        $result = $this->generatePdf($type, $params);
        $pdfBytes = $result['pdf']->output();
        $filename = $result['filename'];
        $normalizedPhone = $this->normalizePhone($phone ?: ($result['context']['recipient_phone'] ?? ''));

        // Save built PDF file in public storage so a physical PDF file is persisted
        Storage::disk('public')->put('shared-pdfs/' . $filename, $pdfBytes);

        // If WhatsApp Cloud API credentials are configured and a phone number is provided, upload & send the actual PDF document
        if ($this->isWhatsAppApiConfigured() && $normalizedPhone !== '') {
            $token = config('services.whatsapp.access_token');
            $phoneNumberId = config('services.whatsapp.phone_number_id');
            $version = config('services.whatsapp.api_version', 'v20.0');

            $mediaResponse = Http::withToken($token)
                ->attach('file', $pdfBytes, $filename, ['Content-Type' => 'application/pdf'])
                ->post("https://graph.facebook.com/{$version}/{$phoneNumberId}/media", [
                    'messaging_product' => 'whatsapp',
                    'type'              => 'application/pdf',
                ]);

            if (!$mediaResponse->successful() || empty($mediaResponse->json('id'))) {
                throw new RuntimeException('Failed to upload PDF document to WhatsApp Cloud API: ' . $mediaResponse->body());
            }

            $mediaId = $mediaResponse->json('id');

            $msgResponse = Http::withToken($token)
                ->post("https://graph.facebook.com/{$version}/{$phoneNumberId}/messages", [
                    'messaging_product' => 'whatsapp',
                    'recipient_type'    => 'individual',
                    'to'                => $normalizedPhone,
                    'type'              => 'document',
                    'document'          => [
                        'id'       => $mediaId,
                        'filename' => $filename,
                        'caption'  => $caption,
                    ],
                ]);

            if (!$msgResponse->successful()) {
                throw new RuntimeException('Failed to send PDF document via WhatsApp Cloud API: ' . $msgResponse->body());
            }

            return [
                'status'           => 'sent_via_api',
                'mode'             => 'cloud_api',
                'message'          => "PDF document ({$filename}) sent directly to WhatsApp (+{$normalizedPhone})",
                'filename'         => $filename,
                'normalized_phone' => $normalizedPhone,
                'whatsapp_msg_id'  => $msgResponse->json('messages.0.id'),
            ];
        }

        return [
            'status'           => 'pdf_built',
            'mode'             => 'client_file',
            'message'          => "PDF document ({$filename}) built and ready to send on WhatsApp",
            'filename'         => $filename,
            'mime_type'        => 'application/pdf',
            'size_bytes'       => strlen($pdfBytes),
            'pdf_base64'       => base64_encode($pdfBytes),
            'normalized_phone' => $normalizedPhone,
            'caption'          => $caption,
        ];
    }

    /**
     * Render the DomPDF instance and return ['pdf' => \Barryvdh\DomPDF\PDF, 'filename' => string, 'context' => array].
     */
    public function generatePdf(string $type, array $params): array
    {
        $context = $this->resolveDocumentContext($type, $params);

        $pdf = Pdf::loadView($context['view'], $context['view_data'])
            ->setPaper('a4', 'portrait');

        return [
            'pdf'      => $pdf,
            'filename' => $context['filename'],
            'context'  => $context,
        ];
    }

    /**
     * Resolve document data, view template, recipient info, and message copy.
     */
    public function resolveDocumentContext(string $type, array $params): array
    {
        $setting = Setting::first();
        $storeName = $setting->store_name ?? 'Karobar App Store';

        return match ($type) {
            'sale_invoice'    => $this->resolveSaleInvoice((int) ($params['id'] ?? 0), $setting, $storeName),
            'customer_ledger' => $this->resolveCustomerLedger(
                (int) ($params['customer_id'] ?? $params['id'] ?? 0),
                $params['from'] ?? null,
                $params['to'] ?? null,
                $setting,
                $storeName
            ),
            'supplier_ledger' => $this->resolveSupplierLedger(
                (int) ($params['supplier_id'] ?? $params['id'] ?? 0),
                $params['from'] ?? null,
                $params['to'] ?? null,
                $setting,
                $storeName
            ),
            'payment'         => $this->resolvePayment((int) ($params['id'] ?? 0), $setting, $storeName),
            'purchase_order'  => $this->resolvePurchaseOrder((int) ($params['id'] ?? 0), $setting, $storeName),
            default           => throw new InvalidArgumentException("Unsupported document type: {$type}"),
        };
    }

    private function resolveSaleInvoice(int $id, ?Setting $setting, string $storeName): array
    {
        $invoice = SaleInvoice::with(['customer', 'user', 'items.product'])->findOrFail($id);
        $customer = $invoice->customer;

        $customerTotalDue = null;
        if ($invoice->customer_id) {
            $customerTotalDue = $this->computeCustomerNetBalance((int) $invoice->customer_id);
        }

        $docNo = $invoice->posted_number ?: ('INV-' . $invoice->id);
        $dateStr = $invoice->date ? Carbon::parse($invoice->date)->format('d M Y') : optional($invoice->created_at)->format('d M Y');
        $total = (float) ($invoice->total ?? 0);
        $received = (float) ($invoice->total_receive ?? 0);
        $remaining = max(0, $total - $received);
        $custName = $customer->name ?? 'Valued Customer';

        $waLines = [
            "*{$storeName}*",
            "🧾 *Sale Invoice: {$docNo}*",
            "📅 Date: {$dateStr}",
            "👤 Customer: {$custName}",
            "────────────────────",
            "💰 *Invoice Total:* Rs. " . number_format($total, 2),
            "✅ *Received:* Rs. " . number_format($received, 2),
            "📌 *Balance (This Invoice):* Rs. " . number_format($remaining, 2),
        ];
        if ($customerTotalDue !== null) {
            $waLines[] = "📊 *Total Account Balance Due:* Rs. " . number_format($customerTotalDue, 2);
        }
        $waLines[] = "\nThank you for your business!";

        $emailSubject = "Sale Invoice {$docNo} from {$storeName}";
        $emailBody = "Dear {$custName},\n\n"
            . "Please find attached your Sale Invoice ({$docNo}) dated {$dateStr} from {$storeName}.\n\n"
            . "Invoice Total: Rs. " . number_format($total, 2) . "\n"
            . "Received Amount: Rs. " . number_format($received, 2) . "\n"
            . "Remaining Invoice Balance: Rs. " . number_format($remaining, 2) . "\n"
            . ($customerTotalDue !== null ? "Total Account Balance Due: Rs. " . number_format($customerTotalDue, 2) . "\n" : '')
            . "\nThank you for your business!\n\nBest regards,\n{$storeName}";

        return [
            'title'           => "Sale Invoice {$docNo}",
            'filename'        => 'Sale_Invoice_' . preg_replace('/[^A-Za-z0-9_-]/', '_', $docNo) . '.pdf',
            'view'            => 'pdf.sale_invoice',
            'view_data'       => [
                'invoice'          => $invoice,
                'setting'          => $setting,
                'customerTotalDue' => $customerTotalDue,
            ],
            'party_type'      => $customer ? 'customer' : null,
            'party_id'        => $customer?->id,
            'recipient_name'  => $custName,
            'recipient_phone' => $customer?->phone ?? '',
            'recipient_email' => $customer?->email ?? '',
            'whatsapp_text'   => implode("\n", $waLines),
            'email_subject'   => $emailSubject,
            'email_body'      => $emailBody,
        ];
    }

    private function resolveCustomerLedger(int $customerId, ?string $from, ?string $to, ?Setting $setting, string $storeName): array
    {
        $customer = Customer::findOrFail($customerId);

        $hasInvoiceDate = Schema::hasColumn('sale_invoices', 'invoice_date');
        $dateColumn     = $hasInvoiceDate ? 'invoice_date' : 'date';
        $hasInvoiceNo   = Schema::hasColumn('sale_invoices', 'invoice_no');

        $selectCols = [
            'id', 'posted_number', 'date',
            'invoice_total', 'total', 'grand_total', 'net_total', 'gross_amount', 'sub_total',
            'total_receive', 'total_recieve', 'received', 'amount_received',
        ];
        if ($hasInvoiceDate) $selectCols[] = 'invoice_date';
        if ($hasInvoiceNo)   $selectCols[] = 'invoice_no';

        $invoices = SaleInvoice::query()
            ->where('customer_id', $customer->id)
            ->where('invoice_type', 'credit')
            ->when($from, fn ($q) => $q->whereDate($dateColumn, '>=', $from))
            ->when($to, fn ($q) => $q->whereDate($dateColumn, '<=', $to))
            ->orderBy($dateColumn)
            ->orderBy('id')
            ->get($selectCols)
            ->map(function ($inv) use ($hasInvoiceDate, $hasInvoiceNo) {
                $t = (float) ($inv->invoice_total ?? $inv->total ?? $inv->grand_total ?? $inv->net_total ?? $inv->gross_amount ?? $inv->sub_total ?? 0);
                $rawR = (float) ($inv->total_receive ?? $inv->total_recieve ?? $inv->received ?? $inv->amount_received ?? 0);
                $r = min($rawR, $t);
                $rawDate = $hasInvoiceDate ? ($inv->invoice_date ?? $inv->date) : $inv->date;
                $entryDate = $rawDate ? Carbon::parse($rawDate)->format('Y-m-d') : null;
                $invoiceNumber = $hasInvoiceNo ? ($inv->invoice_no ?? null) : null;

                return (object) [
                    'id'              => $inv->id,
                    'entry_type'      => 'invoice',
                    'is_manual'       => false,
                    'entry_date'      => $entryDate,
                    'posted_number'   => $inv->posted_number,
                    'invoice_number'  => $invoiceNumber,
                    'invoice_total'   => $t,
                    'total_received'  => $r,
                    'credited_amount' => 0.0,
                    'payment_amount'  => 0.0,
                    'payment_ref'     => null,
                    'description'     => 'Sale Invoice ' . ($inv->posted_number ?? ('#' . $inv->id)),
                ];
            });

        $manualAndPayments = CustomerLedger::query()
            ->where('customer_id', $customer->id)
            ->whereIn('entry_type', ['payment', 'manual'])
            ->when($from, fn ($q) => $q->whereDate('entry_date', '>=', $from))
            ->when($to, fn ($q) => $q->whereDate('entry_date', '<=', $to))
            ->orderBy('entry_date')
            ->orderBy('id')
            ->get()
            ->map(function (CustomerLedger $r) {
                $isInvoiceLike = strtolower($r->entry_type) === 'manual';
                $credited = (float) ($r->credited_amount ?? 0);
                return (object) [
                    'id'              => $r->id,
                    'entry_type'      => strtolower($r->entry_type),
                    'is_manual'       => (bool) $r->is_manual,
                    'entry_date'      => optional($r->entry_date)->format('Y-m-d'),
                    'posted_number'   => $r->posted_number,
                    'invoice_number'  => null,
                    'invoice_total'   => $isInvoiceLike ? (float) ($r->invoice_total ?? 0) : 0.0,
                    'total_received'  => $isInvoiceLike ? (float) ($r->total_received ?? 0) : 0.0,
                    'credited_amount' => $credited,
                    'payment_amount'  => $credited,
                    'payment_ref'     => $r->payment_ref,
                    'description'     => $r->description,
                ];
            });

        $all = $invoices->toBase()->concat($manualAndPayments->toBase())
            ->sortBy(fn ($r) => [$r->entry_date, $r->id ?? 0])
            ->values();

        $balance = 0.0;
        foreach ($all as $r) {
            $t = strtolower($r->entry_type);
            if (in_array($t, ['invoice', 'manual'], true)) {
                $balance += ((float) $r->invoice_total - (float) $r->total_received);
            } elseif ($t === 'payment') {
                $balance -= (float) $r->credited_amount;
            }
            $r->running_balance = round($balance, 2);
        }

        $sumInv  = (float) $all->whereIn('entry_type', ['invoice', 'manual'])->sum('invoice_total');
        $sumRecv = (float) $all->whereIn('entry_type', ['invoice', 'manual'])->sum('total_received');
        $sumPay  = (float) $all->where('entry_type', 'payment')->sum('credited_amount');
        $netBal  = round(($sumInv - $sumRecv) - $sumPay, 2);

        $summary = [
            'total_invoiced'      => $sumInv,
            'received_on_invoice' => $sumRecv,
            'payments_credited'   => $sumPay,
            'net_balance'         => $netBal,
        ];

        $periodLabel = ($from ?: 'Beginning') . ' to ' . ($to ?: now()->toDateString());

        $waLines = [
            "*{$storeName}*",
            "📒 *Customer Account Statement*",
            "👤 Customer: {$customer->name}",
            "📅 Period: {$periodLabel}",
            "────────────────────",
            "🧾 Total Invoiced: Rs. " . number_format($sumInv, 2),
            "✅ Total Received: Rs. " . number_format($sumRecv + $sumPay, 2),
            "📌 *Net Balance Due: Rs. " . number_format($netBal, 2) . "*",
            "\nPlease review your attached statement.",
        ];

        $emailSubject = "Account Statement ({$customer->name}) - {$storeName}";
        $emailBody = "Dear {$customer->name},\n\n"
            . "Please find attached your Customer Ledger Statement from {$storeName} for the period {$periodLabel}.\n\n"
            . "Total Invoiced: Rs. " . number_format($sumInv, 2) . "\n"
            . "Total Payments Received: Rs. " . number_format($sumRecv + $sumPay, 2) . "\n"
            . "Current Net Balance Due: Rs. " . number_format($netBal, 2) . "\n\n"
            . "Best regards,\n{$storeName}";

        return [
            'title'           => "Customer Statement - {$customer->name}",
            'filename'        => 'Customer_Statement_' . preg_replace('/[^A-Za-z0-9_-]/', '_', $customer->name) . '.pdf',
            'view'            => 'pdf.customer_ledger',
            'view_data'       => [
                'customer' => $customer,
                'setting'  => $setting,
                'rows'     => $all,
                'summary'  => $summary,
                'from'     => $from,
                'to'       => $to,
            ],
            'party_type'      => 'customer',
            'party_id'        => $customer->id,
            'recipient_name'  => $customer->name,
            'recipient_phone' => $customer->phone ?? '',
            'recipient_email' => $customer->email ?? '',
            'whatsapp_text'   => implode("\n", $waLines),
            'email_subject'   => $emailSubject,
            'email_body'      => $emailBody,
        ];
    }

    private function resolveSupplierLedger(int $supplierId, ?string $from, ?string $to, ?Setting $setting, string $storeName): array
    {
        $supplier = Supplier::findOrFail($supplierId);

        $rows = SupplierLedger::query()
            ->where('supplier_id', $supplier->id)
            ->when($from, fn ($q) => $q->whereDate('entry_date', '>=', $from))
            ->when($to, fn ($q) => $q->whereDate('entry_date', '<=', $to))
            ->orderBy('entry_date')
            ->orderBy('id')
            ->get();

        $balance = 0.0;
        $mapped = $rows->map(function ($r) use (&$balance) {
            $invoiceTotal = (float) ($r->invoice_total ?? 0);
            $paidOnInv    = (float) ($r->total_paid ?? 0);
            $payment      = (float) ($r->debited_amount ?? 0);
            $type         = $r->entry_type;

            if (in_array($type, ['invoice', 'manual'], true)) {
                $balance += ($invoiceTotal - $paidOnInv);
            }
            if ($type === 'payment') {
                $balance -= $payment;
            }

            $r->payment_amount  = $payment;
            $r->running_balance = round($balance, 2);
            return $r;
        });

        $sumInv  = (float) $mapped->sum('invoice_total');
        $sumPaid = (float) $mapped->sum('total_paid');
        $sumDeb  = (float) $mapped->sum('debited_amount');
        $netBal  = round(($sumInv - $sumPaid) - $sumDeb, 2);

        $summary = [
            'total_invoiced'   => $sumInv,
            'paid_on_invoice'  => $sumPaid,
            'payments_debited' => $sumDeb,
            'net_balance'      => $netBal,
        ];

        $periodLabel = ($from ?: 'Beginning') . ' to ' . ($to ?: now()->toDateString());

        $waLines = [
            "*{$storeName}*",
            "📒 *Supplier Account Statement*",
            "🏢 Supplier: {$supplier->name}",
            "📅 Period: {$periodLabel}",
            "────────────────────",
            "🧾 Total Invoiced: Rs. " . number_format($sumInv, 2),
            "✅ Total Paid: Rs. " . number_format($sumPaid + $sumDeb, 2),
            "📌 *Net Payable Balance: Rs. " . number_format($netBal, 2) . "*",
        ];

        $emailSubject = "Supplier Ledger Statement ({$supplier->name}) - {$storeName}";
        $emailBody = "Dear {$supplier->name},\n\n"
            . "Please find attached our Supplier Account Statement from {$storeName} for the period {$periodLabel}.\n\n"
            . "Total Invoiced: Rs. " . number_format($sumInv, 2) . "\n"
            . "Total Paid: Rs. " . number_format($sumPaid + $sumDeb, 2) . "\n"
            . "Net Payable Balance: Rs. " . number_format($netBal, 2) . "\n\n"
            . "Best regards,\n{$storeName}";

        return [
            'title'           => "Supplier Statement - {$supplier->name}",
            'filename'        => 'Supplier_Statement_' . preg_replace('/[^A-Za-z0-9_-]/', '_', $supplier->name) . '.pdf',
            'view'            => 'pdf.supplier_ledger',
            'view_data'       => [
                'supplier' => $supplier,
                'setting'  => $setting,
                'rows'     => $mapped,
                'summary'  => $summary,
                'from'     => $from,
                'to'       => $to,
            ],
            'party_type'      => 'supplier',
            'party_id'        => $supplier->id,
            'recipient_name'  => $supplier->name,
            'recipient_phone' => $supplier->phone ?? '',
            'recipient_email' => $supplier->email ?? '',
            'whatsapp_text'   => implode("\n", $waLines),
            'email_subject'   => $emailSubject,
            'email_body'      => $emailBody,
        ];
    }

    private function resolvePayment(int $id, ?Setting $setting, string $storeName): array
    {
        $payment = Payment::with([
            'customer',
            'supplier',
            'account',
            'creator',
            'allocations.saleInvoice:id,posted_number,total,date',
            'allocations.purchaseInvoice:id,posted_number,invoice_number,total_amount,posted_date',
        ])->findOrFail($id);

        $isCustomer = $payment->party_type === 'customer';
        $party = $isCustomer ? $payment->customer : $payment->supplier;
        $partyName = $party->name ?? 'Valued Partner';
        $voucherNo = $payment->payment_number ?: ('PAY-' . $payment->id);
        $dateStr = $payment->payment_date ? Carbon::parse($payment->payment_date)->format('d M Y') : '-';
        $totalAmount = (float) ($payment->amount ?? 0);
        $docLabel = $isCustomer ? 'Official Payment Receipt' : 'Payment Voucher';

        $waLines = [
            "*{$storeName}*",
            "💳 *{$docLabel}: {$voucherNo}*",
            "📅 Date: {$dateStr}",
            "👤 " . ($isCustomer ? 'Customer' : 'Supplier') . ": {$partyName}",
            "────────────────────",
            "💰 *Amount " . ($isCustomer ? 'Received' : 'Paid') . ":* Rs. " . number_format($totalAmount, 2),
            "🏦 Method: " . strtoupper(str_replace('_', ' ', $payment->payment_method ?? 'CASH')),
        ];
        if ($payment->reference) {
            $waLines[] = "🔖 Ref #: {$payment->reference}";
        }

        $emailSubject = "{$docLabel} {$voucherNo} from {$storeName}";
        $emailBody = "Dear {$partyName},\n\n"
            . "Please find attached the {$docLabel} ({$voucherNo}) dated {$dateStr} from {$storeName}.\n\n"
            . "Total Amount " . ($isCustomer ? 'Received' : 'Paid') . ": Rs. " . number_format($totalAmount, 2) . "\n"
            . "Payment Method: " . strtoupper(str_replace('_', ' ', $payment->payment_method ?? 'CASH')) . "\n\n"
            . "Best regards,\n{$storeName}";

        return [
            'title'           => "{$docLabel} {$voucherNo}",
            'filename'        => 'Payment_Voucher_' . preg_replace('/[^A-Za-z0-9_-]/', '_', $voucherNo) . '.pdf',
            'view'            => 'pdf.payment_voucher',
            'view_data'       => [
                'payment' => $payment,
                'setting' => $setting,
            ],
            'party_type'      => $isCustomer ? 'customer' : 'supplier',
            'party_id'        => $party?->id,
            'recipient_name'  => $partyName,
            'recipient_phone' => $party?->phone ?? '',
            'recipient_email' => $party?->email ?? '',
            'whatsapp_text'   => implode("\n", $waLines),
            'email_subject'   => $emailSubject,
            'email_body'      => $emailBody,
        ];
    }

    private function resolvePurchaseOrder(int $id, ?Setting $setting, string $storeName): array
    {
        $order = PurchaseOrder::with(['supplier', 'items.product'])->findOrFail($id);
        $supplier = $order->supplier;
        $supplierName = $supplier->name ?? 'Supplier';
        $poNo = $order->number ?: ('PO-' . str_pad((string) $order->id, 5, '0', STR_PAD_LEFT));
        $dateStr = $order->date ? Carbon::parse($order->date)->format('d M Y') : '-';
        $deliveryStr = $order->expected_received_date ? Carbon::parse($order->expected_received_date)->format('d M Y') : 'As soon as possible';
        $totalAmount = (float) $order->items->sum(function ($item) {
            $gross = (float) ($item->ordered_quantity ?? 0) * (float) ($item->unit_purchase_price ?? 0);
            $disc = (float) ($item->item_discount_percentage ?? 0);
            return $gross * (1 - ($disc / 100.0));
        });
        $itemsCount = $order->items->count();

        $waLines = [
            "*{$storeName}*",
            "📦 *Purchase Order: {$poNo}*",
            "📅 Order Date: {$dateStr}",
            "🏢 Supplier: {$supplierName}",
            "🚚 Expected Delivery: {$deliveryStr}",
            "────────────────────",
            "📋 Total Line Items: {$itemsCount}",
            "💰 *Estimated Order Total:* Rs. " . number_format($totalAmount, 2),
            "\nPlease confirm receipt of this Purchase Order.",
        ];

        $emailSubject = "Purchase Order {$poNo} from {$storeName}";
        $emailBody = "Dear {$supplierName},\n\n"
            . "Please find attached our official Purchase Order ({$poNo}) dated {$dateStr} from {$storeName}.\n\n"
            . "Total Line Items: {$itemsCount}\n"
            . "Estimated Order Value: Rs. " . number_format($totalAmount, 2) . "\n"
            . "Expected Delivery Date: {$deliveryStr}\n\n"
            . "Kindly confirm receipt and expected dispatch schedule.\n\n"
            . "Best regards,\n{$storeName}";

        return [
            'title'           => "Purchase Order {$poNo}",
            'filename'        => 'Purchase_Order_' . preg_replace('/[^A-Za-z0-9_-]/', '_', $poNo) . '.pdf',
            'view'            => 'pdf.purchase_order',
            'view_data'       => [
                'order'       => $order,
                'setting'     => $setting,
                'totalAmount' => $totalAmount,
            ],
            'party_type'      => $supplier ? 'supplier' : null,
            'party_id'        => $supplier?->id,
            'recipient_name'  => $supplierName,
            'recipient_phone' => $supplier?->phone ?? '',
            'recipient_email' => $supplier?->email ?? '',
            'whatsapp_text'   => implode("\n", $waLines),
            'email_subject'   => $emailSubject,
            'email_body'      => $emailBody,
        ];
    }

    private function computeCustomerNetBalance(int $customerId): float
    {
        $invoices = SaleInvoice::query()
            ->where('customer_id', $customerId)
            ->where('invoice_type', 'credit')
            ->get(['total', 'total_receive']);

        $invDelta = $invoices->sum(function ($inv) {
            $t = (float) ($inv->total ?? 0);
            $r = min((float) ($inv->total_receive ?? 0), $t);
            return max(0, $t - $r);
        });

        $manualRows = CustomerLedger::query()
            ->where('customer_id', $customerId)
            ->where('entry_type', 'manual')
            ->get(['invoice_total', 'total_received']);

        $manualDelta = $manualRows->sum(fn ($r) => (float) ($r->invoice_total ?? 0) - (float) ($r->total_received ?? 0));

        $paymentsCredited = (float) CustomerLedger::query()
            ->where('customer_id', $customerId)
            ->where('entry_type', 'payment')
            ->sum('credited_amount');

        return round(($invDelta + $manualDelta) - $paymentsCredited, 2);
    }
}
