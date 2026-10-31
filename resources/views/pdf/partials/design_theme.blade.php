@php
    $activeDesign = in_array($pdfDesign ?? null, ['standard', 'minimal', 'detailed', 'compact', 'bold', 'barcode'], true)
        ? $pdfDesign
        : ($setting->a4_template ?? 'standard');
    $urduFonts = \App\Support\UrduPdfTextShaper::resolveUrduFontUrls();
@endphp
<style>
    @if(!empty($urduFonts['regular']))
        @font-face {
            font-family: 'UrduFont';
            font-style: normal;
            font-weight: normal;
            src: url('{!! $urduFonts['regular'] !!}') format('truetype');
        }
        @font-face {
            font-family: 'UrduFont';
            font-style: normal;
            font-weight: bold;
            src: url('{!! $urduFonts['bold'] ?? $urduFonts['regular'] !!}') format('truetype');
        }
        body, table, td, th, div, span, p {
            font-family: 'UrduFont', DejaVu Sans, sans-serif !important;
        }
    @endif
    @if($activeDesign === 'minimal')
        @page { margin: 16mm 14mm; }
        body {
            font-family: DejaVu Sans, sans-serif;
            font-size: 10.5px;
            color: #1e293b;
            line-height: 1.5;
        }
        .header-table {
            border-bottom: 1px solid #cbd5e1 !important;
            padding-bottom: 12px !important;
            margin-bottom: 16px !important;
        }
        .store-name {
            font-size: 19px !important;
            font-weight: bold !important;
            color: #0f172a !important;
            letter-spacing: -0.3px;
        }
        .doc-badge {
            background: #ffffff !important;
            color: #0f172a !important;
            border: 1.5px solid #0f172a !important;
            font-size: 11px !important;
            padding: 3px 10px !important;
            border-radius: 2px !important;
            letter-spacing: 0.8px !important;
        }
        .meta-box, .info-box, .summary-grid {
            background: #ffffff !important;
            border: none !important;
            border-top: 1px solid #e2e8f0 !important;
            border-bottom: 1px solid #e2e8f0 !important;
            border-radius: 0 !important;
            padding: 10px 2px !important;
            margin-bottom: 16px !important;
        }
        .items-table th, .alloc-table th, .ledger-table th, .tx-table th {
            background: #f8fafc !important;
            color: #334155 !important;
            border-top: 1.5px solid #0f172a !important;
            border-bottom: 1.5px solid #0f172a !important;
            border-left: none !important;
            border-right: none !important;
            padding: 7px 6px !important;
        }
        .items-table td, .alloc-table td, .ledger-table td, .tx-table td {
            border-left: none !important;
            border-right: none !important;
            border-top: none !important;
            border-bottom: 1px solid #e2e8f0 !important;
            padding: 7px 6px !important;
            background: #ffffff !important;
        }
        .totals-table .grand-row td, .totals-box .grand-row td {
            background: #f8fafc !important;
            color: #0f172a !important;
            border-top: 1.5px solid #0f172a !important;
            border-bottom: 2px solid #0f172a !important;
        }
    @elseif($activeDesign === 'detailed')
        @page { margin: 13mm 12mm; }
        body {
            font-family: DejaVu Sans, sans-serif;
            font-size: 10.5px;
            color: #0f172a;
        }
        .header-table {
            border-bottom: 3px solid #1e40af !important;
            padding-bottom: 10px !important;
            margin-bottom: 14px !important;
        }
        .store-name {
            font-size: 19px !important;
            color: #1e3a8a !important;
        }
        .doc-badge {
            background: #1e40af !important;
            color: #ffffff !important;
            padding: 5px 14px !important;
            border-radius: 4px !important;
        }
        .meta-box, .info-box, .summary-grid {
            background: #eff6ff !important;
            border: 1px solid #bfdbfe !important;
            border-left: 4px solid #1e40af !important;
            border-radius: 4px !important;
            padding: 10px 12px !important;
        }
        .meta-label, .kpi-label {
            color: #1e40af !important;
        }
        .items-table th, .alloc-table th, .ledger-table th, .tx-table th {
            background: #1e3a8a !important;
            color: #ffffff !important;
            border: 1px solid #1e3a8a !important;
            padding: 6.5px 7px !important;
        }
        .items-table tr:nth-child(even) td, .ledger-table tr:nth-child(even) td {
            background: #f8fafc !important;
        }
        .totals-table .grand-row td, .totals-box .grand-row td {
            background: #1e40af !important;
            color: #ffffff !important;
        }
    @elseif($activeDesign === 'compact')
        @page { margin: 9mm 9mm; }
        body {
            font-family: DejaVu Sans, sans-serif;
            font-size: 9.2px !important;
            line-height: 1.25 !important;
            color: #0f172a;
        }
        .header-table {
            border-bottom: 1.5px solid #334155 !important;
            padding-bottom: 6px !important;
            margin-bottom: 8px !important;
        }
        .store-name {
            font-size: 14px !important;
            margin: 0 !important;
        }
        .store-meta {
            font-size: 8.5px !important;
        }
        .doc-badge {
            background: #334155 !important;
            color: #ffffff !important;
            font-size: 10px !important;
            padding: 2px 8px !important;
        }
        .meta-box, .info-box, .summary-grid {
            background: #f8fafc !important;
            border: 1px solid #cbd5e1 !important;
            padding: 5px 8px !important;
            margin-bottom: 8px !important;
        }
        .items-table th, .alloc-table th, .ledger-table th, .tx-table th {
            background: #334155 !important;
            color: #ffffff !important;
            font-size: 8.2px !important;
            padding: 3.5px 5px !important;
            border: 1px solid #334155 !important;
        }
        .items-table td, .alloc-table td, .ledger-table td, .tx-table td {
            font-size: 8.8px !important;
            padding: 3px 5px !important;
        }
        .totals-table, .totals-box {
            width: 230px !important;
            margin-top: 8px !important;
        }
        .totals-table td, .totals-box td {
            padding: 2.5px 6px !important;
            font-size: 9px !important;
        }
        .totals-table .grand-row td, .totals-box .grand-row td {
            background: #334155 !important;
            font-size: 10px !important;
            padding: 4px 6px !important;
        }
        .footer-note {
            margin-top: 12px !important;
            font-size: 8px !important;
        }
    @elseif($activeDesign === 'bold')
        @page { margin: 13mm 12mm; }
        body {
            font-family: DejaVu Sans, sans-serif;
            font-size: 10.8px;
            color: #0f172a;
        }
        .header-table {
            background: #064e3b !important;
            color: #ffffff !important;
            border-bottom: 3px solid #10b981 !important;
            padding: 12px 14px !important;
            margin-bottom: 14px !important;
        }
        .header-table td {
            padding: 8px 10px !important;
        }
        .header-table .store-name {
            color: #ffffff !important;
            font-size: 20px !important;
        }
        .header-table .store-meta {
            color: #d1fae5 !important;
        }
        .doc-badge {
            background: #10b981 !important;
            color: #064e3b !important;
            font-size: 12px !important;
            font-weight: bold !important;
            padding: 4px 12px !important;
        }
        .meta-box, .info-box, .summary-grid {
            background: #ecfdf5 !important;
            border: 1px solid #a7f3d0 !important;
            border-radius: 4px !important;
            padding: 9px 12px !important;
        }
        .meta-label, .kpi-label {
            color: #047857 !important;
        }
        .items-table th, .alloc-table th, .ledger-table th, .tx-table th {
            background: #065f46 !important;
            color: #ffffff !important;
            border: 1px solid #065f46 !important;
        }
        .items-table tr:nth-child(even) td {
            background: #f0fdf4 !important;
        }
        .totals-table .grand-row td, .totals-box .grand-row td {
            background: #047857 !important;
            color: #ffffff !important;
            font-size: 12.5px !important;
        }
    @elseif($activeDesign === 'barcode')
        @page { margin: 13mm 12mm; }
        body {
            font-family: DejaVu Sans, sans-serif;
            font-size: 10.5px;
            color: #18181b;
        }
        .header-table {
            border-bottom: 2px dashed #3f3f46 !important;
            padding-bottom: 10px !important;
            margin-bottom: 14px !important;
        }
        .doc-badge {
            background: #27272a !important;
            color: #ffffff !important;
            border: 1px solid #18181b !important;
            letter-spacing: 1px !important;
        }
        .meta-box, .info-box, .summary-grid {
            background: #fafafa !important;
            border: 1px solid #d4d4d8 !important;
            border-left: 4px solid #27272a !important;
        }
        .items-table th, .alloc-table th, .ledger-table th, .tx-table th {
            background: #27272a !important;
            color: #ffffff !important;
            border: 1px solid #27272a !important;
        }
        .totals-table .grand-row td, .totals-box .grand-row td {
            background: #27272a !important;
            color: #ffffff !important;
        }
        .barcode-strip {
            font-family: DejaVu Sans Mono, monospace;
            font-size: 10px;
            letter-spacing: 2px;
            color: #27272a;
            background: #f4f4f5;
            border: 1px solid #d4d4d8;
            padding: 3px 8px;
            display: inline-block;
            margin-top: 4px;
        }
    @endif
</style>

