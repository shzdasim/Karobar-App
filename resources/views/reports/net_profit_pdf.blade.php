<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Net Profit &amp; Loss Statement</title>
  <style>
    @page { margin: 24px; }
    body { font-family: DejaVu Sans, sans-serif; font-size: 12px; color: #111; }
    h1 { font-size: 18px; margin: 0 0 6px 0; }
    h2 { font-size: 13px; margin: 16px 0 6px 0; color: #1f2937; text-transform: uppercase; }
    .meta { font-size: 11px; margin-bottom: 14px; color: #4b5563; }
    table { width: 100%; border-collapse: collapse; margin-bottom: 12px; }
    th, td { border: 1px solid #e5e7eb; padding: 7px 10px; font-size: 12px; }
    th { background: #f3f4f6; text-align: left; font-weight: 700; }
    td.num, th.num { text-align: right; font-family: DejaVu Sans Mono, monospace; }
    .highlight { background: #f9fafb; font-weight: 700; }
    .net-row { background: #ecfdf5; font-weight: 700; font-size: 13px; }
  </style>
</head>
<body>
  @php
    $kpis = $data['kpis'] ?? [];
    $grossSales = $kpis['gross_sales'] ?? $data['revenue']['gross_sales'] ?? 0;
    $discounts = $kpis['total_discounts'] ?? $data['revenue']['sales_discounts'] ?? 0;
    $taxes = $kpis['total_taxes'] ?? 0;
    $invoicedSales = $kpis['invoiced_sales'] ?? ($grossSales - $discounts + $taxes);
    $returns = $kpis['returns_amount'] ?? $data['revenue']['sale_returns'] ?? 0;
    $netSales = $kpis['net_sales'] ?? $data['revenue']['net_sales'] ?? 0;
    $cogs = $kpis['cost_of_goods_sold'] ?? $data['cogs']['net_cogs'] ?? 0;
    $grossProfit = $kpis['gross_profit'] ?? $data['gross_profit']['amount'] ?? 0;
    $grossMargin = $kpis['gross_margin_pct'] ?? $data['gross_profit']['margin_percent'] ?? 0;
    $totalExpenses = $kpis['total_expenses'] ?? $data['expenses']['total'] ?? 0;
    $netProfit = $kpis['net_profit'] ?? $data['net_profit']['amount'] ?? 0;
    $netMargin = $kpis['net_margin_pct'] ?? $data['net_profit']['margin_percent'] ?? 0;
    $categories = $data['expenses_by_category'] ?? $data['expenses']['by_category'] ?? [];
  @endphp

  <h1>Executive Net Profit &amp; Loss (P&amp;L) Statement</h1>
  <div class="meta">
    Period: {{ $data['date_range']['from'] ?? $meta['from'] ?? '' }} to {{ $data['date_range']['to'] ?? $meta['to'] ?? '' }} &nbsp;&nbsp;|&nbsp;&nbsp; Generated: {{ $meta['generatedAt'] ?? '' }}
  </div>

  <h2>Revenue &amp; Gross Profit</h2>
  <table>
    <tbody>
      <tr>
        <td>Gross Invoiced Sales</td>
        <td class="num">Rs. {{ number_format($grossSales, 2) }}</td>
      </tr>
      <tr>
        <td>Less: Sales Discounts</td>
        <td class="num">- Rs. {{ number_format($discounts, 2) }}</td>
      </tr>
      @if($taxes > 0)
      <tr>
        <td>Add: Sales Taxes</td>
        <td class="num">+ Rs. {{ number_format($taxes, 2) }}</td>
      </tr>
      @endif
      <tr>
        <td>Invoiced Sales Subtotal</td>
        <td class="num">Rs. {{ number_format($invoicedSales, 2) }}</td>
      </tr>
      <tr>
        <td>Less: Customer Sale Returns</td>
        <td class="num">- Rs. {{ number_format($returns, 2) }}</td>
      </tr>
      <tr class="highlight">
        <td>Net Sales Revenue</td>
        <td class="num">Rs. {{ number_format($netSales, 2) }}</td>
      </tr>
      <tr>
        <td>Less: Cost of Goods Sold (COGS)</td>
        <td class="num">- Rs. {{ number_format($cogs, 2) }}</td>
      </tr>
      <tr class="highlight">
        <td>Gross Profit (Margin: {{ number_format($grossMargin, 2) }}%)</td>
        <td class="num">Rs. {{ number_format($grossProfit, 2) }}</td>
      </tr>
    </tbody>
  </table>

  <h2>Operating Expenses by Category</h2>
  <table>
    <thead>
      <tr>
        <th>Category</th>
        <th class="num">Entries</th>
        <th class="num">Amount</th>
      </tr>
    </thead>
    <tbody>
      @forelse($categories as $cat)
        <tr>
          <td>{{ $cat['category_name'] ?? 'Uncategorized' }}</td>
          <td class="num">{{ number_format($cat['count'] ?? 0) }}</td>
          <td class="num">Rs. {{ number_format($cat['total_amount'] ?? $cat['total'] ?? 0, 2) }}</td>
        </tr>
      @empty
        <tr><td colspan="3" style="text-align:center;">No operating expenses recorded in this period.</td></tr>
      @endforelse
      <tr class="highlight">
        <td colspan="2">Total Operating Expenses</td>
        <td class="num">Rs. {{ number_format($totalExpenses, 2) }}</td>
      </tr>
    </tbody>
  </table>

  <table>
    <tbody>
      <tr class="net-row">
        <td>Net Operating Profit / (Loss) (Net Margin: {{ number_format($netMargin, 2) }}%)</td>
        <td class="num">Rs. {{ number_format($netProfit, 2) }}</td>
      </tr>
    </tbody>
  </table>
</body>
</html>

