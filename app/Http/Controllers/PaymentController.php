<?php

namespace App\Http\Controllers;

use App\Models\Customer;
use App\Models\Payment;
use App\Models\Supplier;
use App\Services\PaymentService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\Validation\Rule;

class PaymentController extends Controller
{
    protected PaymentService $paymentService;

    public function __construct(PaymentService $paymentService)
    {
        $this->paymentService = $paymentService;
    }

    /**
     * List payments with optional filters.
     */
    public function index(Request $request): JsonResponse
    {
        $this->authorize('viewAny', Payment::class);

        $request->validate([
            'party_type' => ['nullable', Rule::in(['customer', 'supplier'])],
            'party_id'   => ['nullable', 'integer'],
            'status'     => ['nullable', Rule::in(['posted', 'reversed'])],
            'from'       => ['nullable', 'date'],
            'to'         => ['nullable', 'date'],
            'search'     => ['nullable', 'string', 'max:100'],
            'per_page'   => ['nullable', 'integer', 'min:1', 'max:200'],
        ]);

        $query = Payment::with([
            'customer:id,name',
            'supplier:id,name',
            'creator:id,name',
            'allocations',
        ]);

        if ($request->filled('party_type')) {
            $query->where('party_type', $request->party_type);
        }

        if ($request->filled('party_id')) {
            $query->where('party_id', $request->party_id);
        }

        if ($request->filled('status')) {
            $query->where('status', $request->status);
        }

        if ($request->filled('from')) {
            $query->whereDate('payment_date', '>=', $request->from);
        }

        if ($request->filled('to')) {
            $query->whereDate('payment_date', '<=', $request->to);
        }

        if ($request->filled('search')) {
            $search = trim($request->search);
            $query->where(function ($q) use ($search) {
                $q->where('payment_number', 'LIKE', "%{$search}%")
                  ->orWhere('reference', 'LIKE', "%{$search}%")
                  ->orWhere('notes', 'LIKE', "%{$search}%");
            });
        }

        $query->orderBy('payment_date', 'desc')->orderBy('id', 'desc');

        $perPage = (int)($request->per_page ?? 15);
        $paginated = $query->paginate($perPage);

        return response()->json($paginated);
    }

    /**
     * Show single payment.
     */
    public function show(int $id): JsonResponse
    {
        $payment = Payment::with([
            'customer:id,name,phone,address',
            'supplier:id,name,phone,address',
            'creator:id,name',
            'reversedBy:id,name',
            'allocations.saleInvoice:id,posted_number,date,total',
            'allocations.purchaseInvoice:id,posted_number,invoice_number,posted_date,total_amount',
        ])->findOrFail($id);

        $this->authorize('view', $payment);

        return response()->json($payment);
    }

    /**
     * Create and post payment.
     */
    public function store(Request $request): JsonResponse
    {
        $this->authorize('create', Payment::class);

        $data = $request->validate([
            'party_type'                => ['required', Rule::in(['customer', 'supplier'])],
            'party_id'                  => ['required', 'integer'],
            'amount'                    => ['required', 'numeric', 'min:0.01'],
            'payment_date'              => ['required', 'date'],
            'payment_method'            => ['required', 'string', 'max:50'],
            'reference'                 => ['nullable', 'string', 'max:100'],
            'notes'                     => ['nullable', 'string', 'max:500'],
            'idempotency_key'           => ['nullable', 'string', 'max:100'],
            'allocations'               => ['nullable', 'array'],
            'allocations.*.invoice_id'  => ['required', 'integer'],
            'allocations.*.amount'      => ['required', 'numeric', 'min:0.01'],
        ]);

        $user = Auth::user();

        if ($data['party_type'] === 'customer') {
            $customer = Customer::findOrFail($data['party_id']);
            $payment = $this->paymentService->recordCustomerPayment($customer, $data, $user);
        } else {
            $supplier = Supplier::findOrFail($data['party_id']);
            $payment = $this->paymentService->recordSupplierPayment($supplier, $data, $user);
        }

        return response()->json([
            'message' => 'Payment posted successfully',
            'payment' => $payment->load([
                'allocations',
                'customer:id,name',
                'supplier:id,name',
            ]),
        ], 201);
    }

    /**
     * Reverse a payment.
     */
    public function reverse(Request $request, int $id): JsonResponse
    {
        $payment = Payment::findOrFail($id);
        $this->authorize('reverse', $payment);

        $data = $request->validate([
            'reason' => ['required', 'string', 'min:3', 'max:500'],
        ]);

        $reversed = $this->paymentService->reversePayment($payment, $data['reason'], Auth::user());

        return response()->json([
            'message' => 'Payment reversed successfully',
            'payment' => $reversed->fresh(['allocations']),
        ]);
    }

    /**
     * Fetch open invoices eligible for payment allocation.
     */
    public function openInvoices(Request $request): JsonResponse
    {
        $this->authorize('viewAny', Payment::class);

        $request->validate([
            'party_type' => ['required', Rule::in(['customer', 'supplier'])],
            'party_id'   => ['required', 'integer'],
        ]);

        if ($request->party_type === 'customer') {
            $invoices = $this->paymentService->getOpenInvoicesForCustomer((int)$request->party_id);
        } else {
            $invoices = $this->paymentService->getOpenInvoicesForSupplier((int)$request->party_id);
        }

        return response()->json($invoices);
    }
}

