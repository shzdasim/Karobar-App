<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Outstanding Aging Report</title>
  <style>
    @page { margin: 18px; }
    body { font-family: DejaVu Sans, sans-serif; font-size: 11px; color: #111; }
    h1 { font-size: 17px; margin: 0 0 6px 0; }
    h2 { font-size: 13px; margin: 14px 0 6px 0; color: #1f2937; }
    .meta { font-size: 11px; margin-bottom: 10px; color: #4b5563; }
    table { width: 100%; border-collapse: collapse; margin-bottom: 12px; }
    th, td { border: 1px solid #e5e7eb; padding: 5px 7px; line-height: 1.3; font-size: 10.5px; }
    th { background: #f3f4f6; text-align: left; font-weight: 700; color: #111827; }
    td.num, th.num { text-align: right; }
    tfoot td { background: #f9fafb; font-weight: 700; }
  </style>
</head>
<body>
  <h1>Outstanding Receivables &amp; Payables Aging Report</h1>
  <div class="meta">
    As Of: {{ $meta['as_of'] ?? '' }} &nbsp;&nbsp;|&nbsp;&nbsp; Aging Basis: {{ strtoupper(str_replace('_', ' ', $meta['aging_by'] ?? 'due_date')) }} &nbsp;&nbsp;|&nbsp;&nbsp; Generated: {{ $meta['generatedAt'] ?? '' }}
  </div>

  <h2>Customer Receivables Aging</h2>
  <table>
    <thead>
      <tr>
        <th>Customer</th>
        <th>Phone</th>
        <th class="num">Current</th>
        <th class="num">1–30 Days</th>
        <th class="num">31–60 Days</th>
        <th class="num">61–90 Days</th>
        <th class="num">&gt; 90 Days</th>
        <th class="num">Total Overdue</th>
        <th class="num">Total Balance</th>
      </tr>
    </thead>
    <tbody>
      @forelse($customers as $c)
        <tr>
          <td>{{ $c['party_name'] ?? '-' }}</td>
          <td>{{ $c['party_phone'] ?? '-' }}</td>
          <td class="num">{{ number_format($c['current'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($c['days_1_30'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($c['days_31_60'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($c['days_61_90'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($c['over_90'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($c['total_overdue'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($c['total'] ?? 0, 2) }}</td>
        </tr>
      @empty
        <tr><td colspan="9" style="text-align:center;">No outstanding customer receivables.</td></tr>
      @endforelse
    </tbody>
    <tfoot>
      <tr>
        <td colspan="2">Total Customers ({{ $summary['customers']['party_count'] ?? 0 }})</td>
        <td class="num">{{ number_format($summary['customers']['current'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['customers']['days_1_30'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['customers']['days_31_60'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['customers']['days_61_90'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['customers']['over_90'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['customers']['total_overdue'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['customers']['total'] ?? 0, 2) }}</td>
      </tr>
    </tfoot>
  </table>

  <h2>Supplier Payables Aging</h2>
  <table>
    <thead>
      <tr>
        <th>Supplier</th>
        <th>Phone</th>
        <th class="num">Current</th>
        <th class="num">1–30 Days</th>
        <th class="num">31–60 Days</th>
        <th class="num">61–90 Days</th>
        <th class="num">&gt; 90 Days</th>
        <th class="num">Total Overdue</th>
        <th class="num">Total Payable</th>
      </tr>
    </thead>
    <tbody>
      @forelse($suppliers as $s)
        <tr>
          <td>{{ $s['party_name'] ?? '-' }}</td>
          <td>{{ $s['party_phone'] ?? '-' }}</td>
          <td class="num">{{ number_format($s['current'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($s['days_1_30'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($s['days_31_60'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($s['days_61_90'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($s['over_90'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($s['total_overdue'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($s['total'] ?? 0, 2) }}</td>
        </tr>
      @empty
        <tr><td colspan="9" style="text-align:center;">No outstanding supplier payables.</td></tr>
      @endforelse
    </tbody>
    <tfoot>
      <tr>
        <td colspan="2">Total Suppliers ({{ $summary['suppliers']['party_count'] ?? 0 }})</td>
        <td class="num">{{ number_format($summary['suppliers']['current'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['suppliers']['days_1_30'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['suppliers']['days_31_60'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['suppliers']['days_61_90'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['suppliers']['over_90'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['suppliers']['total_overdue'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['suppliers']['total'] ?? 0, 2) }}</td>
      </tr>
    </tfoot>
  </table>
</body>
</html>

