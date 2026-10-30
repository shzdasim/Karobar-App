<?php

namespace App\Http\Controllers;

use App\Models\Customer;
use App\Models\CustomerLedger;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Symfony\Component\HttpFoundation\StreamedResponse;

class CustomerController extends Controller
{
    public function search(Request $request)
{
    $this->authorize('lookup', Customer::class);
    $q     = trim((string) $request->query('q', ''));
    $page  = max(1, (int) $request->query('page', 1));
    $limit = min(50, max(5, (int) $request->query('limit', 20)));

    $builder = \App\Models\Customer::query();

    if ($q !== '') {
        $builder->where(function($w) use ($q) {
            $w->where('name', 'like', "%{$q}%")
              ->orWhere('phone', 'like', "%{$q}%")
              ->orWhere('email', 'like', "%{$q}%");
        });
    } else {
        $builder->orderByRaw("CASE WHEN id = 1 OR UPPER(name) LIKE '%WALK-IN%' THEN 0 ELSE 1 END ASC");
    }

    $builder->orderBy('name');

    $paginator = $builder->paginate($limit, ['*'], 'page', $page);

    // Shape: { data: [{id,name,phone,email}], next_page: <int|null> }
    return response()->json([
        'data' => $paginator->items(),
        'next_page' => $paginator->hasMorePages() ? ($paginator->currentPage() + 1) : null,
    ]);
}

    public function index()
    {
        $this->authorize('viewAny', Customer::class);
        // Count via subqueries (no dependency on relation names)
        $customers = Customer::query()
            ->select('customers.*')
            ->selectSub(
                'SELECT COUNT(*) FROM sale_invoices si WHERE si.customer_id = customers.id',
                'sale_invoices_count'
            )
            ->selectSub(
                'SELECT COUNT(*) FROM sale_returns sr WHERE sr.customer_id = customers.id',
                'sale_returns_count'
            )
            ->orderBy('name')
            ->get();

        // Expose a combined count used by the UI
        $customers->transform(function ($c) {
            $c->transactions_count = (int) ($c->sale_invoices_count ?? 0) + (int) ($c->sale_returns_count ?? 0);
            return $c;
        });

        return response()->json($customers);
    }

    public function store(Request $request)
    {
        $this->authorize('create', Customer::class);
        $validated = $request->validate([
            'name'            => 'required|unique:customers,name',
            'email'           => 'nullable|email|unique:customers,email',
            'phone'           => 'nullable|string',
            'tax_number'      => 'nullable|string|max:60',
            'tax_status'      => 'nullable|string|in:registered,unregistered,exempt',
            'address'         => 'nullable|string',
            'credit_limit'    => 'nullable|numeric|min:0',
            'credit_days'     => 'nullable|integer|min:0|max:365',
            'credit_status'   => 'nullable|string|in:active,on_hold,blocked',
            'opening_balance' => 'nullable|numeric|min:0',
        ]);

        $openingBalance = round((float) ($validated['opening_balance'] ?? 0), 2);
        unset($validated['opening_balance']);

        $customer = DB::transaction(function () use ($validated, $openingBalance, $request) {
            $customer = Customer::create($validated);

            if ($openingBalance > 0 && \Illuminate\Support\Facades\Schema::hasTable('customer_ledgers')) {
                CustomerLedger::create([
                    'customer_id'       => $customer->id,
                    'entry_type'        => 'manual',
                    'is_manual'         => true,
                    'entry_date'        => now()->toDateString(),
                    'posted_number'     => 'OB-' . str_pad((string) $customer->id, 5, '0', STR_PAD_LEFT),
                    'invoice_total'     => $openingBalance,
                    'total_received'    => 0,
                    'balance_remaining' => $openingBalance,
                    'credited_amount'   => 0,
                    'description'       => 'Opening Balance',
                    'created_by'        => $request->user()?->id,
                ]);
            }

            return $customer;
        });

        return response()->json($customer, 201);
    }

    public function show(Customer $customer)
    {
        $this->authorize('view', $customer);
        return response()->json($customer);
    }

    public function creditSummary(Customer $customer)
    {
        $this->authorize('lookup', $customer);

        $netBalance = CustomerLedger::getCustomerNetBalance($customer->id);
        $creditLimit = $customer->credit_limit !== null ? (float)$customer->credit_limit : null;
        $availableCredit = $creditLimit !== null ? max(0.0, round($creditLimit - $netBalance, 2)) : null;
        $isLimitExceeded = $creditLimit !== null && $creditLimit > 0 && $netBalance > $creditLimit;

        // Calculate overdue invoices
        $today = now()->toDateString();
        $openInvoices = app(\App\Services\PaymentService::class)->getOpenInvoicesForCustomer($customer->id);
        $totalOverdue = 0.0;
        $overdueCount = 0;
        $oldestDue = null;

        foreach ($openInvoices as $inv) {
            $dueDate = $inv['due_date'] ?? $inv['date'];
            if ($dueDate < $today) {
                $totalOverdue += (float)$inv['due'];
                $overdueCount++;
                if ($oldestDue === null || $dueDate < $oldestDue) {
                    $oldestDue = $dueDate;
                }
            }
        }

        return response()->json([
            'customer_id'            => $customer->id,
            'name'                   => $customer->name,
            'phone'                  => $customer->phone,
            'credit_status'          => $customer->credit_status ?? 'active',
            'credit_limit'           => $creditLimit,
            'credit_days'            => $customer->credit_days ?? 30,
            'net_balance'            => round($netBalance, 2),
            'current_balance'        => round($netBalance, 2),
            'available_credit'       => $availableCredit,
            'is_limit_exceeded'      => $isLimitExceeded,
            'total_overdue'          => round($totalOverdue, 2),
            'overdue_invoices_count' => $overdueCount,
            'oldest_due_date'        => $oldestDue,
        ]);
    }

    public function update(Request $request, Customer $customer)
    {
        $this->authorize('update', $customer);
        $validated = $request->validate([
            'name'          => 'required|unique:customers,name,' . $customer->id,
            'email'         => 'nullable|email|unique:customers,email,' . $customer->id,
            'phone'         => 'nullable|string',
            'tax_number'    => 'nullable|string|max:60',
            'tax_status'    => 'nullable|string|in:registered,unregistered,exempt',
            'address'       => 'nullable|string',
            'credit_limit'  => 'nullable|numeric|min:0',
            'credit_days'   => 'nullable|integer|min:0|max:365',
            'credit_status' => 'nullable|string|in:active,on_hold,blocked',
        ]);
        $customer->update($validated);
        return response()->json($customer);
    }

    public function destroy(Customer $customer)
    {
        $this->authorize('delete', $customer);
        // Hard guard: block delete if referenced in invoices/returns
        $hasInvoices = DB::table('sale_invoices')->where('customer_id', $customer->id)->exists();
        $hasReturns  = DB::table('sale_returns')->where('customer_id', $customer->id)->exists();

        if ($hasInvoices || $hasReturns) {
            return response()->json([
                'message' => 'Cannot delete: customer has related sale invoices/returns.'
            ], 422);
        }

        $customer->delete();
        return response()->json(null, 204);
    }
    public function export(): StreamedResponse
{
    $this->authorize('export', Customer::class);
    $file = 'customers_'.now()->format('Y-m-d_H-i-s').'.csv';

    return response()->streamDownload(function () {
        $out = fopen('php://output', 'w');
        // UTF-8 BOM for Excel
        fprintf($out, chr(0xEF).chr(0xBB).chr(0xBF));
        // Header
        fputcsv($out, ['name','email','phone','address']);

        \App\Models\Customer::select('name','email','phone','address')
            ->orderBy('name')
            ->chunk(1000, function ($chunk) use ($out) {
                foreach ($chunk as $c) {
                    fputcsv($out, [
                        (string)($c->name ?? ''),
                        (string)($c->email ?? ''),
                        (string)($c->phone ?? ''),
                        (string)($c->address ?? ''),
                    ]);
                }
            });

        fclose($out);
    }, $file, ['Content-Type' => 'text/csv; charset=UTF-8']);
}
}
