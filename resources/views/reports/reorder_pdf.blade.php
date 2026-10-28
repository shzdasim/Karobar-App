<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Low Stock &amp; Reorder Report</title>
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
  <h1>Low Stock &amp; Smart Reorder Report</h1>
  <div class="meta">
    Threshold: {{ $meta['threshold'] ?? '' }} &nbsp;&nbsp;|&nbsp;&nbsp; Generated: {{ $meta['generatedAt'] ?? '' }}
  </div>

  <table>
    <thead>
      <tr>
        <th>Code</th>
        <th>Product Name</th>
        <th>Category</th>
        <th>Brand</th>
        <th>Supplier</th>
        <th class="num">Pack Size</th>
        <th class="num">On-Hand Qty</th>
        <th class="num">Suggested Reorder</th>
        <th class="num">Unit Purchase Price</th>
        <th class="num">Est. Reorder Value</th>
      </tr>
    </thead>
    <tbody>
      @forelse($rows as $r)
        <tr>
          <td>{{ $r['product_code'] ?? '-' }}</td>
          <td>{{ $r['name'] ?? '-' }}</td>
          <td>{{ $r['category_name'] ?? '-' }}</td>
          <td>{{ $r['brand_name'] ?? '-' }}</td>
          <td>{{ $r['supplier_name'] ?? '-' }}</td>
          <td class="num">{{ number_format($r['pack_size'] ?? 0) }}</td>
          <td class="num">{{ number_format($r['quantity'] ?? 0) }}</td>
          <td class="num">{{ number_format($r['suggested_reorder_quantity'] ?? 0) }}</td>
          <td class="num">{{ number_format($r['unit_purchase_price'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($r['estimated_reorder_value'] ?? 0, 2) }}</td>
        </tr>
      @empty
        <tr><td colspan="10" style="text-align:center;">No low-stock products found.</td></tr>
      @endforelse
    </tbody>
    <tfoot>
      <tr>
        <td colspan="6">Total ({{ number_format($summary['product_count'] ?? 0) }} Products)</td>
        <td class="num">{{ number_format($summary['total_quantity'] ?? 0) }}</td>
        <td class="num">{{ number_format($summary['suggested_quantity'] ?? 0) }}</td>
        <td></td>
        <td class="num">Rs. {{ number_format($summary['estimated_reorder_value'] ?? 0, 2) }}</td>
      </tr>
    </tfoot>
  </table>
</body>
</html>

