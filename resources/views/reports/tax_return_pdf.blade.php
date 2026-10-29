<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <title>Statutory Tax & GST Return Report</title>
    <style>
        body { font-family: DejaVu Sans, sans-serif; font-size: 10px; color: #1e293b; margin: 18px; }
        .header { text-align: center; border-bottom: 2px solid #0f172a; padding-bottom: 10px; margin-bottom: 14px; }
        .header h1 { font-size: 16px; margin: 0; text-transform: uppercase; letter-spacing: 0.5px; }
        .header p { margin: 3px 0 0; color: #475569; font-size: 10px; }
        .kpi-table { width: 100%; border-collapse: collapse; margin-bottom: 14px; }
        .kpi-table td { width: 25%; border: 1px solid #cbd5e1; padding: 8px 10px; background: #f8fafc; }
        .kpi-label { font-size: 9px; text-transform: uppercase; color: #64748b; font-weight: bold; }
        .kpi-value { font-size: 13px; font-weight: bold; margin-top: 3px; color: #0f172a; }
        .section-title { font-size: 11px; font-weight: bold; text-transform: uppercase; background: #0f172a; color: #fff; padding: 5px 8px; margin-top: 12px; margin-bottom: 6px; }
        table.data-table { width: 100%; border-collapse: collapse; margin-bottom: 12px; }
        table.data-table th, table.data-table td { border: 1px solid #cbd5e1; padding: 5px 6px; text-align: left; }
        table.data-table th { background: #f1f5f9; font-weight: bold; font-size: 9px; text-transform: uppercase; }
        .text-right { text-align: right !important; }
        .font-bold { font-weight: bold; }
    </style>
</head>
<body>
    <div class="header">
        <h1>Statutory Tax & GST / VAT Return Statement</h1>
        <p>Period: {{ $data['period']['from'] ?? '' }} to {{ $data['period']['to'] ?? '' }} | Generated: {{ $meta['generatedAt'] ?? '' }}</p>
    </div>

    @php $s = $data['summary'] ?? []; @endphp

    <table class="kpi-table">
        <tr>
            <td>
                <div class="kpi-label">Net Output Tax (Sales)</div>
                <div class="kpi-value">{{ number_format($s['net_output_tax'] ?? 0, 2) }}</div>
                <div style="font-size:8px;color:#64748b;">Taxable Sales: {{ number_format($s['taxable_sales_base'] ?? 0, 2) }}</div>
            </td>
            <td>
                <div class="kpi-label">Net Input Tax Credit (Purchases)</div>
                <div class="kpi-value">{{ number_format($s['net_input_tax'] ?? 0, 2) }}</div>
                <div style="font-size:8px;color:#64748b;">Taxable Purchases: {{ number_format($s['taxable_purchases_base'] ?? 0, 2) }}</div>
            </td>
            <td>
                <div class="kpi-label">Tax Challans Paid</div>
                <div class="kpi-value">{{ number_format($s['tax_challans_paid'] ?? 0, 2) }}</div>
                <div style="font-size:8px;color:#64748b;">Via Journal Vouchers</div>
            </td>
            <td>
                <div class="kpi-label">Net GST Payable / (Refundable)</div>
                <div class="kpi-value">{{ number_format($s['net_tax_payable'] ?? 0, 2) }}</div>
                <div style="font-size:8px;color:#64748b;">Status: {{ strtoupper(str_replace('_', ' ', $s['status'] ?? 'settled')) }}</div>
            </td>
        </tr>
    </table>

    <div class="section-title">1. Statutory Return Summary (Output Tax vs. Input Tax Credit)</div>
    <table class="data-table">
        <thead>
            <tr>
                <th>Return Box / Description</th>
                <th class="text-right">Documents</th>
                <th class="text-right">Taxable / Exempt Value</th>
                <th class="text-right">Tax Amount</th>
            </tr>
        </thead>
        <tbody>
            <tr>
                <td>Box 1 — Taxable Supplies (Standard & Reduced Rate Sales)</td>
                <td class="text-right">{{ $s['taxable_sales_count'] ?? 0 }}</td>
                <td class="text-right">{{ number_format($s['taxable_sales_base'] ?? 0, 2) }}</td>
                <td class="text-right">{{ number_format($s['output_tax_collected'] ?? 0, 2) }}</td>
            </tr>
            <tr>
                <td>Box 2 — Zero-Rated / Exempt Supplies (Essential Pharma / Retail)</td>
                <td class="text-right">{{ $s['exempt_sales_count'] ?? 0 }}</td>
                <td class="text-right">{{ number_format($s['exempt_sales_base'] ?? 0, 2) }}</td>
                <td class="text-right">0.00</td>
            </tr>
            <tr>
                <td>Box 3 — Less: Sales Returns / Credit Notes Issued</td>
                <td class="text-right">—</td>
                <td class="text-right">-{{ number_format($s['sale_returns_base'] ?? 0, 2) }}</td>
                <td class="text-right">-{{ number_format($s['sale_returns_tax_reversed'] ?? 0, 2) }}</td>
            </tr>
            <tr class="font-bold">
                <td colspan="3">Net Output Tax Payable on Sales (A)</td>
                <td class="text-right">{{ number_format($s['net_output_tax'] ?? 0, 2) }}</td>
            </tr>
            <tr>
                <td>Box 4 — Taxable Purchases from Suppliers (Input Tax Credit)</td>
                <td class="text-right">{{ $s['taxable_purchases_count'] ?? 0 }}</td>
                <td class="text-right">{{ number_format($s['taxable_purchases_base'] ?? 0, 2) }}</td>
                <td class="text-right">{{ number_format($s['input_tax_paid'] ?? 0, 2) }}</td>
            </tr>
            <tr>
                <td>Box 5 — Less: Purchase Returns / Debit Notes</td>
                <td class="text-right">—</td>
                <td class="text-right">-{{ number_format($s['purchase_returns_base'] ?? 0, 2) }}</td>
                <td class="text-right">-{{ number_format($s['purchase_returns_tax_reversed'] ?? 0, 2) }}</td>
            </tr>
            <tr class="font-bold">
                <td colspan="3">Net Input Tax Claimable on Purchases (B)</td>
                <td class="text-right">{{ number_format($s['net_input_tax'] ?? 0, 2) }}</td>
            </tr>
            <tr class="font-bold">
                <td colspan="3">Net Tax Payable / (Carry-Forward Credit) (A - B - Challans Paid)</td>
                <td class="text-right">{{ number_format($s['net_tax_payable'] ?? 0, 2) }}</td>
            </tr>
        </tbody>
    </table>

    @if(!empty($data['output_register']))
    <div class="section-title">2. Output Tax Register (Sales & Credit Notes)</div>
    <table class="data-table">
        <thead>
            <tr>
                <th>Date</th>
                <th>Document #</th>
                <th>Type</th>
                <th>Customer</th>
                <th>NTN / Tax ID</th>
                <th class="text-right">Taxable Value</th>
                <th class="text-right">Tax %</th>
                <th class="text-right">Output Tax</th>
                <th class="text-right">Total</th>
            </tr>
        </thead>
        <tbody>
            @foreach(array_slice($data['output_register'], 0, 250) as $row)
            <tr>
                <td>{{ $row['date'] }}</td>
                <td>{{ $row['posted_number'] }}</td>
                <td>{{ $row['doc_type_label'] }}</td>
                <td>{{ $row['party_name'] }}</td>
                <td>{{ $row['party_tax_number'] }}</td>
                <td class="text-right">{{ number_format($row['taxable_amount'], 2) }}</td>
                <td class="text-right">{{ number_format($row['tax_percentage'], 2) }}%</td>
                <td class="text-right">{{ number_format($row['tax_amount'], 2) }}</td>
                <td class="text-right">{{ number_format($row['invoice_total'], 2) }}</td>
            </tr>
            @endforeach
        </tbody>
    </table>
    @endif

    @if(!empty($data['input_register']))
    <div class="section-title">3. Input Tax Register (Purchases & Debit Notes)</div>
    <table class="data-table">
        <thead>
            <tr>
                <th>Date</th>
                <th>Posted #</th>
                <th>Supplier Inv #</th>
                <th>Supplier</th>
                <th>NTN / Tax ID</th>
                <th class="text-right">Taxable Value</th>
                <th class="text-right">Tax %</th>
                <th class="text-right">Input Tax</th>
                <th class="text-right">Total</th>
            </tr>
        </thead>
        <tbody>
            @foreach(array_slice($data['input_register'], 0, 250) as $row)
            <tr>
                <td>{{ $row['date'] }}</td>
                <td>{{ $row['posted_number'] }}</td>
                <td>{{ $row['supplier_inv_no'] }}</td>
                <td>{{ $row['party_name'] }}</td>
                <td>{{ $row['party_tax_number'] }}</td>
                <td class="text-right">{{ number_format($row['taxable_amount'], 2) }}</td>
                <td class="text-right">{{ number_format($row['tax_percentage'], 2) }}%</td>
                <td class="text-right">{{ number_format($row['tax_amount'], 2) }}</td>
                <td class="text-right">{{ number_format($row['invoice_total'], 2) }}</td>
            </tr>
            @endforeach
        </tbody>
    </table>
    @endif
</body>
</html>

