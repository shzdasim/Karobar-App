<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>Sale Invoice {{ $invoice->posted_number ?? $invoice->id }}</title>
    <style>
        @page { margin: 14mm 12mm; }
        body {
            font-family: DejaVu Sans, sans-serif;
            font-size: 11px;
            color: #1e293b;
            line-height: 1.45;
            margin: 0;
            padding: 0;
        }
        table { width: 100%; border-collapse: collapse; }
        .header-table { border-bottom: 2px solid #0f172a; padding-bottom: 10px; margin-bottom: 14px; }
        .store-name { font-size: 18px; font-weight: bold; color: #0f172a; margin: 0 0 3px 0; }
        .store-meta { font-size: 10px; color: #475569; margin: 1px 0; }
        .doc-badge {
            display: inline-block;
            background: #0f172a;
            color: #ffffff;
            font-size: 13px;
            font-weight: bold;
            padding: 4px 12px;
            border-radius: 3px;
            letter-spacing: 0.5px;
            text-transform: uppercase;
        }
        .meta-box {
            background: #f8fafc;
            border: 1px solid #cbd5e1;
            border-radius: 4px;
            padding: 8px 10px;
            margin-bottom: 14px;
        }
        .meta-label { font-size: 9px; font-weight: bold; color: #64748b; text-transform: uppercase; }
        .meta-value { font-size: 11px; font-weight: bold; color: #0f172a; }
        .items-table th {
            background: #0f172a;
            color: #ffffff;
            font-size: 9.5px;
            font-weight: bold;
            text-transform: uppercase;
            padding: 6px 7px;
            border: 1px solid #0f172a;
            text-align: left;
        }
        .items-table td {
            padding: 6px 7px;
            border: 1px solid #cbd5e1;
            font-size: 10.5px;
        }
        .items-table tr:nth-child(even) td { background: #f8fafc; }
        .text-right { text-align: right !important; }
        .text-center { text-align: center !important; }
        .totals-table { width: 270px; margin-left: auto; margin-top: 12px; }
        .totals-table td { padding: 4px 8px; font-size: 11px; border-bottom: 1px solid #e2e8f0; }
        .totals-table .grand-row td {
            background: #0f172a;
            color: #ffffff;
            font-weight: bold;
            font-size: 12px;
            border: none;
            padding: 6px 8px;
        }
        .footer-note {
            margin-top: 24px;
            padding-top: 8px;
            border-top: 1px solid #cbd5e1;
            font-size: 9.5px;
            color: #64748b;
            text-align: center;
        }
    </style>
</head>
<body>
    @php
        $storeName = $setting->store_name ?? 'Business Store';
        $phone = $setting->phone_number ?? '';
        $address = $setting->address ?? '';
        $license = $setting->license_number ?? '';
        $note = $setting->note ?? 'Thank you for your business!';

        $gross = (float) ($invoice->gross_amount ?? 0);
        if ($gross <= 0 && $invoice->relationLoaded('items')) {
            $gross = (float) $invoice->items->sum(fn($it) => (float)$it->price * (float)$it->quantity);
        }
        $disc = (float) ($invoice->discount_amount ?? 0);
        if ($disc <= 0 && (float)($invoice->discount_percentage ?? 0) > 0) {
            $disc = ($gross * (float)$invoice->discount_percentage) / 100;
        }
        $tax = (float) ($invoice->tax_amount ?? 0);
        if ($tax <= 0 && (float)($invoice->tax_percentage ?? 0) > 0) {
            $tax = (($gross - $disc) * (float)$invoice->tax_percentage) / 100;
        }
        $total = (float) ($invoice->total ?? ($gross - $disc + $tax));
        $received = (float) ($invoice->total_receive ?? 0);
        $remainThis = max($total - $received, 0);
    @endphp

    <table class="header-table">
        <tr>
            <td style="width: 60%; vertical-align: top;">
                <div class="store-name">{{ $storeName }}</div>
                @if($address)<div class="store-meta">{{ $address }}</div>@endif
                @if($phone)<div class="store-meta">Phone: {{ $phone }}</div>@endif
                @if($license)<div class="store-meta">License #: {{ $license }}</div>@endif
            </td>
            <td style="width: 40%; vertical-align: top; text-align: right;">
                <div class="doc-badge">Sale Invoice</div>
                <div style="margin-top: 6px; font-size: 12px; font-weight: bold;">
                    #{{ $invoice->posted_number ?? $invoice->id }}
                </div>
                <div class="store-meta">
                    Date: {{ $invoice->date ? \Carbon\Carbon::parse($invoice->date)->format('d M Y') : optional($invoice->created_at)->format('d M Y') }}
                </div>
            </td>
        </tr>
    </table>

    <div class="meta-box">
        <table>
            <tr>
                <td style="width: 55%; vertical-align: top;">
                    <div class="meta-label">Billed To Customer</div>
                    <div class="meta-value">{{ $invoice->customer->name ?? 'Walk-in Customer' }}</div>
                    @if(!empty($invoice->customer?->phone))
                        <div class="store-meta">Phone: {{ $invoice->customer->phone }}</div>
                    @endif
                    @if(!empty($invoice->customer?->address))
                        <div class="store-meta">Address: {{ $invoice->customer->address }}</div>
                    @endif
                </td>
                <td style="width: 45%; vertical-align: top; text-align: right;">
                    <div class="meta-label">Invoice Details</div>
                    <div class="store-meta">Type: <strong>{{ ucfirst($invoice->invoice_type ?? 'Cash') }}</strong></div>
                    @if(!empty($invoice->doctor_name))
                        <div class="store-meta">Ref / Doctor: {{ $invoice->doctor_name }}</div>
                    @endif
                    @if(!empty($invoice->patient_name))
                        <div class="store-meta">Patient: {{ $invoice->patient_name }}</div>
                    @endif
                    <div class="store-meta">Prepared By: {{ $invoice->user->name ?? 'Staff' }}</div>
                </td>
            </tr>
        </table>
    </div>

    <table class="items-table">
        <thead>
            <tr>
                <th style="width: 30px;" class="text-center">#</th>
                <th>Product Description</th>
                <th style="width: 65px;" class="text-center">Batch</th>
                <th style="width: 65px;" class="text-center">Expiry</th>
                <th style="width: 55px;" class="text-right">Qty</th>
                <th style="width: 75px;" class="text-right">Unit Price</th>
                <th style="width: 65px;" class="text-right">Disc</th>
                <th style="width: 85px;" class="text-right">Sub Total</th>
            </tr>
        </thead>
        <tbody>
            @forelse($invoice->items as $idx => $item)
                <tr>
                    <td class="text-center">{{ $idx + 1 }}</td>
                    <td><strong>{{ $item->product->name ?? 'Item' }}</strong></td>
                    <td class="text-center">{{ $item->batch_number ?? '-' }}</td>
                    <td class="text-center">{{ $item->expiry ? \Carbon\Carbon::parse($item->expiry)->format('M Y') : '-' }}</td>
                    <td class="text-right">{{ number_format((float)($item->unit_quantity ?? $item->quantity ?? 0)) }}</td>
                    <td class="text-right">{{ number_format((float)($item->unit_price ?? $item->price ?? 0), 2) }}</td>
                    <td class="text-right">{{ number_format((float)($item->item_discount_amount ?? 0), 2) }}</td>
                    <td class="text-right"><strong>{{ number_format((float)($item->sub_total ?? 0), 2) }}</strong></td>
                </tr>
            @empty
                <tr>
                    <td colspan="8" class="text-center">No line items recorded.</td>
                </tr>
            @endforelse
        </tbody>
    </table>

    <table class="totals-table">
        <tr>
            <td>Gross Amount</td>
            <td class="text-right">Rs. {{ number_format($gross, 2) }}</td>
        </tr>
        @if($disc > 0)
            <tr>
                <td>Discount</td>
                <td class="text-right">- Rs. {{ number_format($disc, 2) }}</td>
            </tr>
        @endif
        @if($tax > 0)
            <tr>
                <td>Tax</td>
                <td class="text-right">+ Rs. {{ number_format($tax, 2) }}</td>
            </tr>
        @endif
        <tr class="grand-row">
            <td>Invoice Total</td>
            <td class="text-right">Rs. {{ number_format($total, 2) }}</td>
        </tr>
        <tr>
            <td>Received on Invoice</td>
            <td class="text-right">Rs. {{ number_format($received, 2) }}</td>
        </tr>
        <tr>
            <td><strong>Remaining (This Invoice)</strong></td>
            <td class="text-right"><strong>Rs. {{ number_format($remainThis, 2) }}</strong></td>
        </tr>
        @if(isset($customerTotalDue) && $invoice->customer_id)
            <tr>
                <td><strong>Total Ledger Balance Due</strong></td>
                <td class="text-right"><strong>Rs. {{ number_format((float)$customerTotalDue, 2) }}</strong></td>
            </tr>
        @endif
    </table>

    @if(!empty($invoice->remarks))
        <div style="margin-top: 12px; font-size: 10px; color: #334155;">
            <strong>Remarks:</strong> {{ $invoice->remarks }}
        </div>
    @endif

    <div class="footer-note">
        {{ $note }}<br>
        Generated on {{ now()->format('d M Y, h:i A') }}
    </div>
</body>
</html>

