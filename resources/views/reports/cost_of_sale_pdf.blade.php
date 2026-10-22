<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Cost of Sale Report</title>
  <style>
    @page { margin: 24px; }
    body { font-family: DejaVu Sans, sans-serif; color: #1f2937; font-size: 9px; }
    h1 { margin: 0 0 6px; color: #1e3a8a; font-size: 20px; text-align: center; }
    .meta { margin: 0 0 18px; color: #475569; text-align: center; font-size: 10px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 7px 5px; border: 1px solid #cbd5e1; }
    th { background: #1e40af; color: #fff; text-align: left; font-size: 8px; }
    td.num, th.num { text-align: right; white-space: nowrap; }
    tbody tr:nth-child(even) { background: #f1f5f9; }
    tfoot td { background: #dbeafe; font-weight: bold; }
    .estimated { margin: 0 0 10px; color: #92400e; font-size: 8px; }
  </style>
</head>
<body>
  <h1>Cost of Sale Report</h1>
  <p class="meta">Period: {{ $from }} to {{ $to }} &nbsp; | &nbsp; Sale type: {{ ucfirst($invoiceType) }}</p>
  @if($rows->contains(fn ($row) => $row['cost_is_estimated']))
    <p class="estimated">Estimated historical profit: items without saved costs use the existing current-cost calculation.</p>
  @endif
  <table>
    <thead>
      <tr>
        <th>Date</th>
        <th class="num">Gross Sale</th>
        <th class="num">Item Discount</th>
        <th class="num">Flat Discount</th>
        <th class="num">Tax</th>
        <th class="num">Total Sales</th>
        <th class="num">Sale Return</th>
        <th class="num">Net Sale</th>
        <th class="num">Cost of Sales</th>
        <th class="num">Gross Profit</th>
        <th class="num">GP %</th>
      </tr>
    </thead>
    <tbody>
      @forelse($rows as $row)
        <tr>
          <td>{{ $row['sale_date'] }}{{ $row['cost_is_estimated'] ? ' *' : '' }}</td>
          <td class="num">{{ number_format($row['gross_sale'], 2) }}</td>
          <td class="num">{{ number_format($row['item_discount'], 2) }}</td>
          <td class="num">{{ number_format($row['discount_amount'], 2) }}</td>
          <td class="num">{{ number_format($row['tax_amount'], 2) }}</td>
          <td class="num">{{ number_format($row['total_sales'], 2) }}</td>
          <td class="num">{{ number_format($row['sale_return'], 2) }}</td>
          <td class="num">{{ number_format($row['net_sale'], 2) }}</td>
          <td class="num">{{ number_format($row['cost_of_sales'], 2) }}</td>
          <td class="num">{{ number_format($row['gp_amount'], 2) }}</td>
          <td class="num">{{ number_format($row['gp_pct'], 2) }}%</td>
        </tr>
      @empty
        <tr><td colspan="11" style="text-align:center;">No data for the selected date range.</td></tr>
      @endforelse
    </tbody>
    @if($rows->isNotEmpty())
      <tfoot>
        <tr>
          <td>Totals</td>
          <td class="num">{{ number_format($totals['gross_sale'], 2) }}</td>
          <td class="num">{{ number_format($totals['item_discount'], 2) }}</td>
          <td class="num">{{ number_format($totals['discount_amount'], 2) }}</td>
          <td class="num">{{ number_format($totals['tax_amount'], 2) }}</td>
          <td class="num">{{ number_format($totals['total_sales'], 2) }}</td>
          <td class="num">{{ number_format($totals['sale_return'], 2) }}</td>
          <td class="num">{{ number_format($totals['net_sale'], 2) }}</td>
          <td class="num">{{ number_format($totals['cost_of_sales'], 2) }}</td>
          <td class="num">{{ number_format($totals['gp_amount'], 2) }}</td>
          <td class="num">{{ number_format($totals['gp_pct'], 2) }}%</td>
        </tr>
      </tfoot>
    @endif
  </table>
</body>
</html>
