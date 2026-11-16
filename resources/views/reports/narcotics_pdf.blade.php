<!doctype html>
<html>
<head>
    <meta charset="utf-8">
    <title>Pakistani Statutory Narcotics & Controlled Drugs Register</title>
    <style>
        @page { margin: 16px 20px; }
        body {
            font-family: DejaVu Sans, Arial, sans-serif;
            font-size: 9px;
            color: #0f172a;
            margin: 0;
        }
        .header-banner {
            border-bottom: 2px solid #991b1b;
            padding-bottom: 7px;
            margin-bottom: 9px;
        }
        .header-table {
            width: 100%;
            border-collapse: collapse;
        }
        .header-table td {
            border: none;
            padding: 0;
            vertical-align: top;
        }
        .badge-regulatory {
            display: inline-block;
            background: #fef2f2;
            color: #991b1b;
            border: 1px solid #fecaca;
            font-size: 7.5px;
            font-weight: bold;
            padding: 2px 6px;
            border-radius: 3px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-bottom: 3px;
        }
        h1 {
            font-size: 14px;
            margin: 2px 0;
            color: #7f1d1d;
            letter-spacing: 0.2px;
        }
        .sub-meta {
            color: #334155;
            font-size: 8px;
            margin-top: 2px;
        }
        .kpi-grid {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 10px;
        }
        .kpi-grid td {
            border: 1px solid #cbd5e1;
            background: #f8fafc;
            padding: 5px 7px;
            width: 20%;
        }
        .kpi-label {
            font-size: 7.5px;
            text-transform: uppercase;
            color: #64748b;
            font-weight: bold;
        }
        .kpi-val {
            font-size: 11.5px;
            font-weight: bold;
            color: #0f172a;
            margin-top: 2px;
        }
        .kpi-sub {
            font-size: 7.5px;
            color: #475569;
            margin-top: 1px;
        }
        h2 {
            font-size: 9.5px;
            margin: 9px 0 4px 0;
            padding: 4px 7px;
            background: #1e293b;
            color: #ffffff;
            text-transform: uppercase;
            letter-spacing: 0.4px;
        }
        .product-section-card {
            margin-bottom: 12px;
            page-break-inside: avoid;
        }
        .product-section-header {
            background: #fff1f2;
            border: 1px solid #fda4af;
            border-bottom: none;
            padding: 5px 8px;
        }
        .product-section-title {
            font-size: 9.5px;
            font-weight: bold;
            color: #881337;
        }
        .product-section-sub {
            font-size: 7.5px;
            color: #334155;
            margin-top: 2px;
        }
        table.data-table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 9px;
        }
        table.data-table th,
        table.data-table td {
            border: 1px solid #cbd5e1;
            padding: 3.5px 4.5px;
            text-align: left;
            vertical-align: top;
        }
        table.data-table th {
            background: #f1f5f9;
            font-weight: bold;
            font-size: 7.5px;
            text-transform: uppercase;
            color: #1e293b;
        }
        .opening-row td {
            background: #f8fafc;
            font-weight: bold;
            color: #334155;
        }
        .closing-row td {
            background: #f1f5f9;
            font-weight: bold;
            color: #0f172a;
        }
        .right { text-align: right !important; }
        .center { text-align: center !important; }
        .bold { font-weight: bold; }
        .muted { color: #64748b; font-size: 7.5px; }
        .total-row td {
            background: #e2e8f0;
            font-weight: bold;
        }
        .ok-pill {
            color: #166534;
            font-weight: bold;
            font-size: 7.5px;
        }
        .warn-pill {
            color: #b91c1c;
            font-weight: bold;
            font-size: 7.5px;
        }
        .in-val {
            color: #1d4ed8;
            font-weight: bold;
        }
        .out-val {
            color: #be123c;
            font-weight: bold;
        }
        .bal-val {
            color: #0f172a;
            font-weight: bold;
            background: #f8fafc;
        }
        .batch-chip {
            display: block;
            font-size: 7.5px;
            line-height: 1.3;
        }
        .declaration-box {
            border: 1px solid #cbd5e1;
            background: #f8fafc;
            padding: 5px 8px;
            font-size: 7.5px;
            color: #334155;
            margin-top: 8px;
            page-break-inside: avoid;
        }
        .sig-table {
            width: 100%;
            border-collapse: collapse;
            margin-top: 20px;
            page-break-inside: avoid;
        }
        .sig-table td {
            border: none;
            width: 33.33%;
            padding: 16px 10px 0 10px;
            text-align: center;
        }
        .sig-line {
            border-top: 1px solid #334155;
            padding-top: 4px;
            font-weight: bold;
            font-size: 8px;
            color: #1e293b;
        }
        .sig-sub {
            font-size: 7px;
            color: #64748b;
        }
    </style>
</head>
<body>
    @php
        $summary = $data['summary'] ?? [];
        $stockLedger = $data['stock_ledger'] ?? [];
        $productRegisters = $data['product_registers'] ?? [];
    @endphp

    <div class="header-banner">
        <table class="header-table">
            <tr>
                <td>
                    <span class="badge-regulatory">
                        Drugs Act 1976 • DRAP • Provincial Drugs Rules (Form 9 / Schedule B, D &amp; G) • CNSA 1997
                    </span>
                    <h1>{{ $meta['store_name'] ?? 'Karobar Pharmacy & Medical Store' }} — Statutory Narcotics &amp; Controlled Drugs Register</h1>
                    <div class="sub-meta">
                        Drug Sale License No: <strong>{{ !empty($meta['license_number']) ? $meta['license_number'] : '________________________' }}</strong>
                        @if(!empty($meta['address'])) &nbsp;|&nbsp; Premises: {{ $meta['address'] }} @endif
                        @if(!empty($meta['phone'])) &nbsp;|&nbsp; Tel: {{ $meta['phone'] }} @endif
                    </div>
                </td>
                <td class="right">
                    <div style="font-size: 9.5px; font-weight: bold; color: #7f1d1d;">
                        Register Period: {{ $meta['from'] ?? '-' }} to {{ $meta['to'] ?? '-' }}
                    </div>
                    <div class="sub-meta">Generated: {{ $meta['generatedAt'] ?? '-' }}</div>
                    <div class="sub-meta">
                        Mandatory Preservation: <strong>3 Years from Last Entry (Rule 13)</strong>
                    </div>
                </td>
            </tr>
        </table>
    </div>

    <table class="kpi-grid">
        <tr>
            <td>
                <div class="kpi-label">Opening Balance (B/F)</div>
                <div class="kpi-val">{{ number_format((int)($summary['opening_units_total'] ?? 0)) }} Units</div>
                <div class="kpi-sub">Across {{ number_format((int)($summary['total_narcotic_products'] ?? 0)) }} Controlled Drugs</div>
            </td>
            <td>
                <div class="kpi-label">Quantity Received (IN)</div>
                <div class="kpi-val">+{{ number_format((int)($summary['units_purchased'] ?? 0)) }} Units</div>
                <div class="kpi-sub">{{ number_format((int)($summary['purchase_invoices_count'] ?? 0)) }} Authorized Supplier GRNs | +{{ (int)($summary['units_returned_by_customers'] ?? 0) }} Ret.</div>
            </td>
            <td>
                <div class="kpi-label">Quantity Dispensed (OUT)</div>
                <div class="kpi-val">-{{ number_format((int)($summary['units_dispensed'] ?? 0)) }} Units</div>
                <div class="kpi-sub">{{ number_format((int)($summary['dispensing_invoices_count'] ?? 0)) }} Patient Prescriptions Dispensed</div>
            </td>
            <td>
                <div class="kpi-label">RMP &amp; Patient Rx Compliance</div>
                <div class="kpi-val">{{ number_format((float)($summary['compliance_rate'] ?? 100), 1) }}%</div>
                <div class="kpi-sub">Compliant: {{ (int)($summary['compliant_sales_count'] ?? 0) }} | Missing Rx: {{ (int)($summary['missing_rx_sales_count'] ?? 0) }}</div>
            </td>
            <td>
                <div class="kpi-label">Closing Balance in Vault</div>
                <div class="kpi-val">{{ number_format((int)($summary['total_units_on_hand'] ?? 0)) }} Units</div>
                <div class="kpi-sub">{{ number_format((int)($summary['active_batches_count'] ?? 0)) }} Active Batches | Disposed: {{ (int)($summary['units_disposed'] ?? 0) }}</div>
            </td>
        </tr>
    </table>

    {{-- SECTION 1: FIRST MENTION EACH NARCOTIC PRODUCT CURRENTLY HELD WITH ITS BATCHES & EXPIRY DATES --}}
    <h2>1. Master List of Controlled / Narcotic Drugs Currently Held (Stock, Batch &amp; Expiry Summary)</h2>
    <table class="data-table">
        <thead>
            <tr>
                <th style="width: 3%;" class="center">#</th>
                <th style="width: 7%;">Code</th>
                <th style="width: 17%;">Controlled Drug &amp; Manufacturer</th>
                <th style="width: 13%;">Generic Salt / Strength</th>
                <th style="width: 5%;" class="center">Pack</th>
                <th style="width: 16%;">Batch No. &amp; Expiry Date</th>
                <th style="width: 6%;" class="right">Opening B/F</th>
                <th style="width: 6%;" class="right">+ Purchased</th>
                <th style="width: 5%;" class="right">+ Pt. Ret</th>
                <th style="width: 6%;" class="right">- Dispensed</th>
                <th style="width: 5%;" class="right">- Destroyed / Ret</th>
                <th style="width: 11%;" class="right">Closing Balance in Hand</th>
            </tr>
        </thead>
        <tbody>
            @php
                $totOpen = 0; $totPurch = 0; $totCRet = 0; $totDisp = 0;
                $totOtherOut = 0; $totClose = 0;
            @endphp
            @forelse($stockLedger as $idx => $row)
                @php
                    $otherOut = (int)($row['purchase_return_qty'] ?? 0) + (int)($row['disposed_qty'] ?? 0) - (int)($row['adjusted_qty'] ?? 0);
                    $totOpen += (int)($row['opening_qty'] ?? 0);
                    $totPurch += (int)($row['purchased_qty'] ?? 0);
                    $totCRet += (int)($row['sale_return_qty'] ?? 0);
                    $totDisp += (int)($row['dispensed_qty'] ?? 0);
                    $totOtherOut += $otherOut;
                    $totClose += (int)($row['closing_qty'] ?? 0);
                    $bList = $row['batches_list'] ?? [];
                @endphp
                <tr>
                    <td class="center">{{ $idx + 1 }}</td>
                    <td>{{ $row['product_code'] ?? '-' }}</td>
                    <td>
                        <span class="bold">{{ $row['product_name'] ?? '-' }}</span>
                        <div class="muted">{{ $row['brand_name'] ?? '-' }} • {{ $row['rack'] ?? 'Vault' }}</div>
                    </td>
                    <td>{{ $row['formulation'] ?? '-' }}</td>
                    <td class="center">1x{{ $row['pack_size'] ?? 1 }}</td>
                    <td>
                        @if(!empty($bList))
                            @foreach($bList as $bItem)
                                <span class="batch-chip">
                                    <strong>{{ $bItem['batch_number'] }}</strong>
                                    @if(!empty($bItem['expiry_date']))
                                        <span class="muted">(Exp: {{ $bItem['expiry_date'] }})</span>
                                    @endif
                                    @if((int)($bItem['quantity'] ?? 0) > 0)
                                        <span class="muted">— {{ number_format((int)$bItem['quantity']) }} U</span>
                                    @endif
                                </span>
                            @endforeach
                        @else
                            <span class="muted">—</span>
                        @endif
                    </td>
                    <td class="right">{{ number_format((int)($row['opening_qty'] ?? 0)) }}</td>
                    <td class="right in-val">{{ number_format((int)($row['purchased_qty'] ?? 0)) }}</td>
                    <td class="right">{{ number_format((int)($row['sale_return_qty'] ?? 0)) }}</td>
                    <td class="right out-val">{{ number_format((int)($row['dispensed_qty'] ?? 0)) }}</td>
                    <td class="right">{{ number_format($otherOut) }}</td>
                    <td class="right bal-val">
                        {{ number_format((int)($row['closing_qty'] ?? 0)) }} U
                        <div class="muted">{{ $row['closing_pack_breakdown'] ?? '' }}</div>
                    </td>
                </tr>
            @empty
                <tr>
                    <td colspan="12" class="center">No narcotic products marked in catalog.</td>
                </tr>
            @endforelse
            @if(count($stockLedger) > 0)
                <tr class="total-row">
                    <td colspan="6">Total Physical Narcotic Units Across All Controlled Drugs</td>
                    <td class="right">{{ number_format($totOpen) }}</td>
                    <td class="right">{{ number_format($totPurch) }}</td>
                    <td class="right">{{ number_format($totCRet) }}</td>
                    <td class="right">{{ number_format($totDisp) }}</td>
                    <td class="right">{{ number_format($totOtherOut) }}</td>
                    <td class="right">{{ number_format($totClose) }} Units</td>
                </tr>
            @endif
        </tbody>
    </table>

    {{-- SECTION 2: PRODUCT-BY-PRODUCT STATUTORY RUNNING REGISTER (ONE PRODUCT AT A TIME) --}}
    <h2>2. Product-by-Product Official Chronological Narcotics Register (One Controlled Drug at a Time)</h2>

    @forelse($productRegisters as $prodReg)
        @php
            $pBatches = $prodReg['batches_list'] ?? [];
            $pEntries = $prodReg['entries'] ?? [];
        @endphp
        <div class="product-section-card">
            <div class="product-section-header">
                <table class="header-table">
                    <tr>
                        <td>
                            <div class="product-section-title">
                                Drug #{{ $prodReg['product_section_no'] }}: {{ $prodReg['product_name'] }}
                                @if(!empty($prodReg['formulation']) && $prodReg['formulation'] !== '-')
                                    ({{ $prodReg['formulation'] }})
                                @endif
                                — {{ $prodReg['brand_name'] ?? '-' }}
                            </div>
                            <div class="product-section-sub">
                                Code: <strong>{{ $prodReg['product_code'] ?? '-' }}</strong>
                                &nbsp;|&nbsp; Pack Size: <strong>1x{{ $prodReg['pack_size'] ?? 1 }}</strong>
                                &nbsp;|&nbsp; Vault Location: <strong>{{ $prodReg['rack'] ?? 'Double-Lock Vault' }}</strong>
                                &nbsp;|&nbsp; Batches &amp; Expiry:
                                @if(!empty($pBatches))
                                    @foreach($pBatches as $bIdx => $bItem)
                                        <strong>{{ $bItem['batch_number'] }}</strong>@if(!empty($bItem['expiry_date'])) (Exp: {{ $bItem['expiry_date'] }})@endif{{ $bIdx < count($pBatches) - 1 ? ', ' : '' }}
                                    @endforeach
                                @else
                                    —
                                @endif
                            </div>
                        </td>
                        <td class="right">
                            <div class="product-section-title">
                                Closing Balance: {{ number_format((int)($prodReg['closing_qty'] ?? 0)) }} Units ({{ $prodReg['closing_pack_breakdown'] ?? '' }})
                            </div>
                            <div class="product-section-sub">
                                Opening B/F: <strong>{{ number_format((int)($prodReg['opening_qty'] ?? 0)) }} U</strong>
                                &nbsp;|&nbsp; + Total In: <strong>+{{ number_format((int)($prodReg['total_in_qty'] ?? 0)) }} U</strong>
                                &nbsp;|&nbsp; - Total Out: <strong>-{{ number_format((int)($prodReg['total_out_qty'] ?? 0)) }} U</strong>
                            </div>
                        </td>
                    </tr>
                </table>
            </div>

            <table class="data-table" style="margin-bottom: 0;">
                <thead>
                    <tr>
                        <th style="width: 4%;" class="center">Col.1<br>S.#</th>
                        <th style="width: 8%;">Col.2<br>Date</th>
                        <th style="width: 11%;">Col.3<br>Bill / GRN / Rx Ref #</th>
                        <th style="width: 22%;">Col.4<br>Source (Supplier &amp; Address) OR Patient Name, CNIC &amp; Address</th>
                        <th style="width: 18%;">Col.5<br>Prescribing Doctor (RMP Name, PM&amp;DC # &amp; Hospital)</th>
                        <th style="width: 12%;">Col.6<br>Batch # &amp; Expiry Date</th>
                        <th style="width: 5%;" class="right">Col.7<br>Qty In</th>
                        <th style="width: 5%;" class="right">Col.8<br>Qty Out</th>
                        <th style="width: 7%;" class="right">Col.9<br>Balance in Hand</th>
                        <th style="width: 8%;">Col.10<br>Pharmacist / Remarks</th>
                    </tr>
                </thead>
                <tbody>
                    @forelse($pEntries as $row)
                        @php
                            $rowBatches = $row['batches_list'] ?? [];
                        @endphp
                        <tr class="{{ !empty($row['is_opening_row']) ? 'opening-row' : '' }}">
                            <td class="center">{{ $row['product_serial_no'] ?? $row['serial_no'] ?? '-' }}</td>
                            <td>{{ $row['date'] ?? '-' }}</td>
                            <td>
                                <div class="bold">{{ $row['voucher_number'] ?? '-' }}</div>
                                <div class="muted">{{ $row['event_label'] ?? '' }}</div>
                                @if(!empty($row['reference_no']) && $row['reference_no'] !== ('Inv: ' . ($row['voucher_number'] ?? '')))
                                    <div class="muted">{{ $row['reference_no'] }}</div>
                                @endif
                            </td>
                            <td>
                                <div class="bold">{{ $row['party_name'] ?? '-' }}</div>
                                @if(!empty($row['party_cnic_or_phone']))
                                    <div class="muted">CNIC/Tel: {{ $row['party_cnic_or_phone'] }}</div>
                                @endif
                                @if(!empty($row['party_address']))
                                    <div class="muted">Addr: {{ $row['party_address'] }}</div>
                                @endif
                            </td>
                            <td>
                                @if(!empty($row['is_opening_row']))
                                    <span class="muted">—</span>
                                @elseif(($row['event_type'] ?? '') === 'DISPENSED_OUT')
                                    @if(!empty($row['doctor_name']))
                                        <div class="bold">{{ $row['doctor_name'] }}</div>
                                        @if(!empty($row['doctor_pmdc']))
                                            <div class="muted">PM&amp;DC: {{ $row['doctor_pmdc'] }}</div>
                                        @endif
                                        @if(!empty($row['doctor_hospital']))
                                            <div class="muted">{{ $row['doctor_hospital'] }}</div>
                                        @endif
                                    @else
                                        <span class="warn-pill">MISSING RMP / RX</span>
                                    @endif
                                @else
                                    <div>{{ $row['doctor_name'] ?? '-' }}</div>
                                    @if(!empty($row['doctor_pmdc']))
                                        <div class="muted">{{ $row['doctor_pmdc'] }}</div>
                                    @endif
                                @endif
                            </td>
                            <td>
                                @if(!empty($rowBatches) && count($rowBatches) > 1)
                                    @foreach($rowBatches as $rb)
                                        <span class="batch-chip">
                                            <strong>{{ $rb['batch_number'] }}</strong>
                                            @if(!empty($rb['expiry_date']))
                                                <span class="muted">(Exp: {{ $rb['expiry_date'] }})</span>
                                            @endif
                                        </span>
                                    @endforeach
                                @elseif(!empty($rowBatches) && count($rowBatches) === 1)
                                    <div class="bold">{{ $rowBatches[0]['batch_number'] }}</div>
                                    @if(!empty($rowBatches[0]['expiry_date']))
                                        <div class="muted">Exp: {{ $rowBatches[0]['expiry_date'] }}</div>
                                    @endif
                                @else
                                    <div class="bold">{{ $row['batch_number'] ?? '—' }}</div>
                                    @if(!empty($row['expiry']))
                                        <div class="muted">Exp: {{ $row['expiry'] }}</div>
                                    @endif
                                @endif
                            </td>
                            <td class="right in-val">
                                {{ (int)($row['qty_in'] ?? 0) > 0 ? '+' . number_format((int)$row['qty_in']) : '—' }}
                            </td>
                            <td class="right out-val">
                                {{ (int)($row['qty_out'] ?? 0) > 0 ? '-' . number_format((int)$row['qty_out']) : '—' }}
                            </td>
                            <td class="right bal-val">
                                <div>{{ number_format((int)($row['running_balance'] ?? 0)) }}</div>
                                <div class="muted">{{ $row['running_pack_breakdown'] ?? '' }}</div>
                            </td>
                            <td>
                                <div class="bold">{{ $row['pharmacist_name'] ?? 'Pharmacist' }}</div>
                                @if(($row['event_type'] ?? '') === 'DISPENSED_OUT')
                                    @if(!empty($row['is_rx_compliant']))
                                        <span class="ok-pill">RX VERIFIED</span>
                                    @else
                                        <span class="warn-pill">INCOMPLETE RX</span>
                                    @endif
                                @else
                                    <div class="muted">{{ $row['remarks'] ?? '' }}</div>
                                @endif
                            </td>
                        </tr>
                    @empty
                        <tr>
                            <td colspan="10" class="center">No movements recorded for this controlled substance in the selected period.</td>
                        </tr>
                    @endforelse
                    <tr class="closing-row">
                        <td colspan="6">Closing Balance Carried Forward (C/F) for {{ $prodReg['product_name'] }}</td>
                        <td class="right in-val">+{{ number_format((int)($prodReg['total_in_qty'] ?? 0)) }}</td>
                        <td class="right out-val">-{{ number_format((int)($prodReg['total_out_qty'] ?? 0)) }}</td>
                        <td class="right bal-val">
                            {{ number_format((int)($prodReg['closing_qty'] ?? 0)) }} U
                            <div class="muted">{{ $prodReg['closing_pack_breakdown'] ?? '' }}</div>
                        </td>
                        <td class="muted">Verified Physical Balance</td>
                    </tr>
                </tbody>
            </table>
        </div>
    @empty
        <table class="data-table">
            <tr>
                <td class="center">No narcotic or controlled drug movements recorded during this period.</td>
            </tr>
        </table>
    @endforelse

    <div class="declaration-box">
        <strong>Statutory Compliance Certificate (Drugs Act 1976 / Provincial Drugs Rules Schedule B, D &amp; G / CNSA 1997):</strong>
        Certified that all controlled/narcotic substances listed above are stored under double-lock security under the direct supervision of a Registered Qualified Pharmacist, all inward receipts are backed by authorized distributor warranties/invoices, all retail dispensings are backed by prescriptions of Registered Medical Practitioners (PM&amp;DC), and the closing book balances match the physical count in the narcotics cabinet.
    </div>

    <table class="sig-table">
        <tr>
            <td>
                <div class="sig-line">Qualified Pharmacist (Category-A)</div>
                <div class="sig-sub">Name, Pharmacy Council Reg # &amp; Signature</div>
            </td>
            <td>
                <div class="sig-line">Licensee / Pharmacy Proprietor</div>
                <div class="sig-sub">Name, Drug Sale License Stamp &amp; Signature</div>
            </td>
            <td>
                <div class="sig-line">District / Provincial Drug Inspector</div>
                <div class="sig-sub">Inspection Date, Remarks &amp; Official Seal</div>
            </td>
        </tr>
    </table>
</body>
</html>
