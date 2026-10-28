<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Purchase Returns Report</title>
  <style>
    @page { margin: 18px; }
    body { font-family: DejaVu Sans, sans-serif; font-size: 11px; color: #111; }
    h1 { font-size: 17px; margin: 0 0 6px 0; }
    .meta { font-size: 11px; margin-bottom: 12px; color: #4b5563; }
    .card { border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 10px; }
    .card-hd { background: #f3f4f6; padding: 6px 10px; border-bottom: 1px solid #e5e7eb; font-size: 11px; }
    .row { display: inline-block; margin-right: 14px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { border: 1px solid #e5e7eb; padding: 5px 8px; line-height: 1.3; font-size: 11px; }
    th { background: #f9fafb; text-align: left; font-weight: 700; color: #111827; }
    td.num, th.num { text-align: right; }
    .summary-box { margin-top: 12px; padding: 8px 10px; background: #f9fafb; border: 1px solid #d1d5db; border-radius: 6px; font-weight: 700; }
  </style>
</head>
<body>
  <h1>Purchase Returns Report</h1>
  <div class="meta">
    From: {{ $meta['from'] ?? '—' }} &nbsp;&nbsp;|&nbsp;&nbsp; To: {{ $meta['to'] ?? '—' }} &nbsp;&nbsp;|&nbsp;&nbsp; Generated: {{ $meta['generatedAt'] ?? '' }}
  </div>

  @forelse($rows as $ret)
    <div class="card">
      <div class="card-hd">
        <span class="row"><strong>Return #:</strong> {{ $ret['posted_number'] ?? '-' }}</span>
        <span class="row"><strong>Date:</strong> {{ $ret['date'] ?? '-' }}</span>
        <span class="row"><strong>Supplier:</strong> {{ $ret['supplier_name'] ?? '-' }}</span>
        <span class="row"><strong>Ref Invoice:</strong> {{ $ret['invoice_number'] ?? '-' }}</span>
        <span class="row"><strong>Total:</strong> Rs. {{ number_format($ret['total'] ?? 0, 2) }}</span>
      </div>
      <table>
        <thead>
          <tr>
            <th>Product</th>
            <th>Code</th>
            <th>Batch</th>
            <th>Expiry</th>
            <th class="num">Return Qty (Units)</th>
            <th class="num">Unit Price</th>
            <th class="num">Disc %</th>
            <th class="num">Sub Total</th>
          </tr>
        </thead>
        <tbody>
          @forelse(($ret['items'] ?? []) as $it)
            <tr>
              <td>{{ $it['product_name'] ?? '-' }}</td>
              <td>{{ $it['product_code'] ?? '-' }}</td>
              <td>{{ $it['batch'] ?? '-' }}</td>
              <td>{{ $it['expiry'] ?? '-' }}</td>
              <td class="num">{{ number_format($it['quantity'] ?? 0) }}</td>
              <td class="num">{{ number_format($it['unit_purchase_price'] ?? 0, 2) }}</td>
              <td class="num">{{ number_format($it['item_discount_percentage'] ?? 0, 2) }}%</td>
              <td class="num">{{ number_format($it['sub_total'] ?? 0, 2) }}</td>
            </tr>
          @empty
            <tr><td colspan="8" style="text-align:center;">No items</td></tr>
          @endforelse
        </tbody>
      </table>
    </div>
  @empty
    <p>No purchase returns found for this period.</p>
  @endforelse

  <div class="summary-box">
    Total Returns: {{ number_format($summary['return_count'] ?? 0) }} &nbsp;&nbsp;|&nbsp;&nbsp;
    Total Items: {{ number_format($summary['item_count'] ?? 0) }} &nbsp;&nbsp;|&nbsp;&nbsp;
    Total Qty: {{ number_format($summary['quantity'] ?? 0) }} &nbsp;&nbsp;|&nbsp;&nbsp;
    Net Returned Value: Rs. {{ number_format($summary['total'] ?? 0, 2) }}
  </div>
</body>
</html>

