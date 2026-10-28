<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Purchase Summary Report</title>
  <style>
    @page { margin: 18px; }
    body { font-family: DejaVu Sans, sans-serif; font-size: 11px; color: #111; }
    h1 { font-size: 17px; margin: 0 0 6px 0; }
    .meta { font-size: 11px; margin-bottom: 12px; color: #4b5563; }
    table { width: 100%; border-collapse: collapse; }
    th, td { border: 1px solid #e5e7eb; padding: 5px 8px; line-height: 1.3; font-size: 11px; }
    th { background: #f3f4f6; text-align: left; font-weight: 700; color: #111827; }
    td.num, th.num { text-align: right; }
    tfoot td { background: #f9fafb; font-weight: 700; }
  </style>
</head>
<body>
  <h1>Purchase Summary Report</h1>
  <div class="meta">
    From: {{ $meta['from'] ?? '—' }} &nbsp;&nbsp;|&nbsp;&nbsp; To: {{ $meta['to'] ?? '—' }} &nbsp;&nbsp;|&nbsp;&nbsp; Generated: {{ $meta['generatedAt'] ?? '' }}
  </div>

  <table>
    <thead>
      <tr>
        <th>Date</th>
        <th>Posted #</th>
        <th>Supplier Invoice #</th>
        <th>Supplier</th>
        <th class="num">Items</th>
        <th class="num">Total Qty</th>
        <th class="num">Items Value</th>
        <th class="num">Invoice Total</th>
        <th class="num">Total Paid</th>
      </tr>
    </thead>
    <tbody>
      @forelse($rows as $r)
        <tr>
          <td>{{ $r['date'] ?? '-' }}</td>
          <td>{{ $r['posted_number'] ?? '-' }}</td>
          <td>{{ $r['invoice_number'] ?? '-' }}</td>
          <td>{{ $r['supplier_name'] ?? '-' }}</td>
          <td class="num">{{ number_format($r['item_count'] ?? 0) }}</td>
          <td class="num">{{ number_format($r['quantity'] ?? 0) }}</td>
          <td class="num">{{ number_format($r['items_value'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($r['invoice_total'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($r['total_paid'] ?? 0, 2) }}</td>
        </tr>
      @empty
        <tr><td colspan="9" style="text-align:center;">No purchase invoices found.</td></tr>
      @endforelse
    </tbody>
    <tfoot>
      <tr>
        <td colspan="4">Total ({{ number_format($summary['invoice_count'] ?? 0) }} Invoices)</td>
        <td class="num">{{ number_format($summary['item_count'] ?? 0) }}</td>
        <td class="num">{{ number_format($summary['quantity'] ?? 0) }}</td>
        <td class="num">{{ number_format($summary['items_value'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['invoice_total'] ?? 0, 2) }}</td>
        <td class="num">{{ number_format($summary['total_paid'] ?? 0, 2) }}</td>
      </tr>
    </tfoot>
  </table>
</body>
</html>

