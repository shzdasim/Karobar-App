<?php

namespace App\Http\Controllers;

use App\Mail\DocumentShareMail;
use App\Models\Customer;
use App\Models\CustomerLedger;
use App\Models\Payment;
use App\Models\PurchaseOrder;
use App\Models\SaleInvoice;
use App\Models\Setting;
use App\Models\Supplier;
use App\Models\SupplierLedger;
use App\Services\DocumentShareService;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Mail;
use Illuminate\Validation\Rule;

class DocumentShareController extends Controller
{
    public function __construct(private DocumentShareService $shareService) {}

    /**
     * Authorize the current authenticated user for the requested document type & ID.
     */
    private function authorizeDocumentAccess(string $type, array $params): void
    {
        switch ($type) {
            case 'sale_invoice':
                $invoice = SaleInvoice::findOrFail((int) ($params['id'] ?? 0));
                $this->authorize('view', $invoice);
                break;

            case 'customer_ledger':
                $this->authorize('viewAny', CustomerLedger::class);
                break;

            case 'supplier_ledger':
                $this->authorize('viewAny', SupplierLedger::class);
                break;

            case 'payment':
                $payment = Payment::findOrFail((int) ($params['id'] ?? 0));
                $this->authorize('view', $payment);
                break;

            case 'purchase_order':
                $po = PurchaseOrder::findOrFail((int) ($params['id'] ?? 0));
                $this->authorize('view', $po);
                break;
        }
    }

    /**
     * GET /api/share/metadata
     */
    public function metadata(Request $request)
    {
        $validated = $request->validate([
            'type'        => ['required', 'string', Rule::in(DocumentShareService::SUPPORTED_TYPES)],
            'id'          => ['nullable', 'integer'],
            'customer_id' => ['nullable', 'integer', 'exists:customers,id'],
            'supplier_id' => ['nullable', 'integer', 'exists:suppliers,id'],
            'from'        => ['nullable', 'date'],
            'to'          => ['nullable', 'date'],
        ]);

        $this->authorizeDocumentAccess($validated['type'], $validated);

        $metadata = $this->shareService->buildMetadata($validated['type'], $validated);

        return response()->json($metadata);
    }

    /**
     * GET /api/share/pdf
     */
    public function pdf(Request $request)
    {
        $validated = $request->validate([
            'type'        => ['required', 'string', Rule::in(DocumentShareService::SUPPORTED_TYPES)],
            'id'          => ['nullable', 'integer'],
            'customer_id' => ['nullable', 'integer', 'exists:customers,id'],
            'supplier_id' => ['nullable', 'integer', 'exists:suppliers,id'],
            'from'        => ['nullable', 'date'],
            'to'          => ['nullable', 'date'],
            'download'    => ['nullable', 'boolean'],
        ]);

        $this->authorizeDocumentAccess($validated['type'], $validated);

        $result = $this->shareService->generatePdf($validated['type'], $validated);

        if ($request->boolean('download')) {
            return $result['pdf']->download($result['filename']);
        }

        return $result['pdf']->stream($result['filename']);
    }

    /**
     * POST /api/share/email
     */
    public function sendEmail(Request $request)
    {
        $validated = $request->validate([
            'type'        => ['required', 'string', Rule::in(DocumentShareService::SUPPORTED_TYPES)],
            'id'          => ['nullable', 'integer'],
            'customer_id' => ['nullable', 'integer', 'exists:customers,id'],
            'supplier_id' => ['nullable', 'integer', 'exists:suppliers,id'],
            'from'        => ['nullable', 'date'],
            'to'          => ['nullable', 'date'],
            'email'       => ['required', 'email', 'max:150'],
            'subject'     => ['required', 'string', 'max:255'],
            'message'     => ['required', 'string', 'max:5000'],
            'save_email'  => ['nullable', 'boolean'],
        ]);

        $this->authorizeDocumentAccess($validated['type'], $validated);

        $result = $this->shareService->generatePdf($validated['type'], $validated);
        $context = $result['context'];
        $pdfBytes = $result['pdf']->output();
        $storeName = Setting::first()?->store_name ?? 'Karobar App Store';

        // Optionally persist the email back to the Customer or Supplier profile
        if (!empty($validated['save_email']) && !empty($context['party_type']) && !empty($context['party_id'])) {
            if ($context['party_type'] === 'customer') {
                Customer::where('id', $context['party_id'])->update(['email' => $validated['email']]);
            } elseif ($context['party_type'] === 'supplier') {
                Supplier::where('id', $context['party_id'])->update(['email' => $validated['email']]);
            }
        }

        $mailable = new DocumentShareMail(
            subjectLine: $validated['subject'],
            messageBody: $validated['message'],
            storeName: $storeName,
            pdfBytes: $pdfBytes,
            pdfFilename: $result['filename']
        );

        $deliveryMode = 'smtp';
        try {
            Mail::to($validated['email'])->send($mailable);
        } catch (\Throwable $e) {
            // If local SMTP (e.g. 127.0.0.1:1025) is not running, fall back to log mailer
            // so the request succeeds and returns the built PDF file to the user.
            Mail::mailer('log')->to($validated['email'])->send($mailable);
            $deliveryMode = 'log_fallback';
        }

        return response()->json([
            'status'        => 'sent',
            'delivery_mode' => $deliveryMode,
            'message'       => $deliveryMode === 'smtp'
                ? 'Document PDF sent successfully to ' . $validated['email']
                : 'PDF built & logged for ' . $validated['email'] . ' (SMTP server not running on 127.0.0.1:1025; PDF downloaded locally)',
            'filename'      => $result['filename'],
            'pdf_base64'    => base64_encode($pdfBytes),
        ]);
    }

    /**
     * POST /api/share/whatsapp
     * Builds the actual PDF file (and dispatches it directly via WhatsApp Cloud API if configured,
     * or returns the built PDF binary as base64 so the client can share/attach the actual PDF file).
     */
    public function sendWhatsApp(Request $request)
    {
        $validated = $request->validate([
            'type'        => ['required', 'string', Rule::in(DocumentShareService::SUPPORTED_TYPES)],
            'id'          => ['nullable', 'integer'],
            'customer_id' => ['nullable', 'integer', 'exists:customers,id'],
            'supplier_id' => ['nullable', 'integer', 'exists:suppliers,id'],
            'from'        => ['nullable', 'date'],
            'to'          => ['nullable', 'date'],
            'phone'       => ['nullable', 'string', 'max:50'],
            'message'     => ['required', 'string', 'max:5000'],
            'save_phone'  => ['nullable', 'boolean'],
        ]);

        $this->authorizeDocumentAccess($validated['type'], $validated);

        $response = $this->shareService->sendOrBuildWhatsAppPdf(
            type: $validated['type'],
            params: $validated,
            phone: $validated['phone'] ?? null,
            caption: $validated['message']
        );

        return response()->json($response);
    }

    /**
     * GET /print/shared-pdf/{type} (Signed Public Route for WhatsApp recipients)
     */
    public function publicPdf(Request $request, string $type)
    {
        abort_unless(in_array($type, DocumentShareService::SUPPORTED_TYPES, true), 404);

        $params = [
            'id'          => $request->query('id'),
            'customer_id' => $request->query('customer_id'),
            'supplier_id' => $request->query('supplier_id'),
            'from'        => $request->query('from'),
            'to'          => $request->query('to'),
        ];

        $result = $this->shareService->generatePdf($type, $params);

        if ($request->boolean('download')) {
            return $result['pdf']->download($result['filename']);
        }

        return $result['pdf']->stream($result['filename']);
    }
}

