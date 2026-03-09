<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Near Expiry Product Report</title>
  <style>
    @page { margin: 18px; }
    body { font-family: DejaVu Sans, sans-serif; font-size: 10px; color: #111; }
    h1 { font-size: 16px; margin: 0 0 6px 0; }
    .meta { font-size: 10px; margin-bottom: 10px; }
    .card { border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 10px; }
    .card-hd { background: #f3f4f6; padding: 6px 8px; border-bottom: 1px solid #e5e7eb; }
    .row { display: inline-block; margin-right: 12px; }
    table { width: 100%; border-collapse: collapse; table-layout: auto; }
    th, td { border: 1px solid #e5e7eb; padding: 4px 6px; line-height: 1.3; font-size: 10px; }
    th { background: #f9fafb; text-align: left; font-weight: 700; color: #111827; }
    td.num, th.num { text-align: right; }
    .nowrap { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .footer td { font-weight: 600; }
    .summary-box { background: #fef3c7; border: 1px solid #fcd34d; border-radius: 6px; padding: 8px; margin-top: 10px; }
    .summary-box h3 { margin: 0 0 6px 0; font-size: 12px; color: #92400e; }
    .summary-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; }
    .summary-item { background: #fff; border-radius: 4px; padding: 6px; text-align: center; }
    .summary-item .label { font-size: 9px; color: #64748b; margin-bottom: 3px; }
    .summary-item .value { font-size: 13px; font-weight: 700; color: #0f172a; }
    .expiry-soon { color: #dc2626; font-weight: 600; }
    .expiry-warning { color: #ea580c; }
    .page-break { page-break-after: always; }
  </style>
</head>
<body>
  <h1>Near Expiry Product Report</h1>
  <div class="meta">
    <div>Expiry Date Range: {{ $meta['from'] ?? 'N/A' }} to {{ $meta['to'] ?? 'N/A' }}</div>
    <div>Generated: {{ $meta['generatedAt'] }}</div>
  </div>

  <table>
    <thead>
      <tr>
        <th class="nowrap" style="width: 4%;">#</th>
        <th class="nowrap" style="width: 25%;">Product Name</th>
        <th class="nowrap" style="width: 15%;">Supplier</th>
        <th class="nowrap" style="width: 12%;">Brand</th>
        <th class="nowrap" style="width: 12%;">Batch Number</th>
        <th class="nowrap" style="width: 12%;">Expiry Date</th>
        <th class="num nowrap" style="width: 10%;">Quantity</th>
      </tr>
    </thead>
    <tbody>
      @forelse($rows as $index => $row)
        @php
          $expiryDate = isset($row['expiry_date']) ? (\Carbon\Carbon::parse($row['expiry_date']) ?? \Carbon\Carbon::today()) : \Carbon\Carbon::today();
          $daysUntilExpiry = \Carbon\Carbon::today()->diffInDays($expiryDate, false);
          $expiryClass = $daysUntilExpiry <= 30 ? 'expiry-soon' : ($daysUntilExpiry <= 60 ? 'expiry-warning' : '');
        @endphp
        <tr>
          <td class="nowrap">{{ $index + 1 }}</td>
          <td class="nowrap">
            {{ $row['product_name'] ?? '-' }}
            @if(isset($row['product_code']) && $row['product_code'])
              <div style="font-size: 9px; color: #666;">{{ $row['product_code'] }}</div>
            @endif
          </td>
          <td class="nowrap">{{ $row['supplier_name'] ?? '-' }}</td>
          <td class="nowrap">{{ $row['brand_name'] ?? '-' }}</td>
          <td class="nowrap">{{ $row['batch_number'] ?? '-' }}</td>
          <td class="nowrap {{ $expiryClass }}">{{ $row['expiry_date'] ?? '-' }}</td>
          <td class="num">{{ number_format($row['quantity'] ?? 0) }}</td>
        </tr>
      @empty
        <tr>
          <td colspan="7" style="text-align:center;color:#666;">No near expiry products found.</td>
        </tr>
      @endforelse
    </tbody>
    @if(count($rows) > 0)
    <tfoot class="footer">
      <tr style="background: #fef3c7;">
        <td colspan="6" class="num"><strong>TOTAL</strong></td>
        <td class="num"><strong>{{ number_format($summary['total_quantity'] ?? 0) }}</strong></td>
      </tr>
    </tfoot>
    @endif
  </table>

  <!-- Summary Box -->
  @if(count($rows) > 0)
  <div class="summary-box">
    <h3>Near Expiry Summary</h3>
    <div class="summary-grid">
      <div class="summary-item">
        <div class="label">Total Batches</div>
        <div class="value">{{ number_format($summary['total_batches'] ?? 0) }}</div>
      </div>
      <div class="summary-item">
        <div class="label">Total Quantity</div>
        <div class="value">{{ number_format($summary['total_quantity'] ?? 0) }}</div>
      </div>
    </div>
  </div>
  @endif

  @if(count($rows) > 30)
    <div class="page-break"></div>
  @endif
</body>
</html>

