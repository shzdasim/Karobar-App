<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Overdue Work Queue Report</title>
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
  <h1>Overdue Collections &amp; Payables Work Queue ({{ ucfirst($meta['party_type'] ?? 'Customers') }})</h1>
  <div class="meta">
    As Of: {{ $meta['as_of'] ?? '' }} &nbsp;&nbsp;|&nbsp;&nbsp; Total Overdue Parties: {{ $total_parties ?? 0 }} &nbsp;&nbsp;|&nbsp;&nbsp; Generated: {{ $meta['generatedAt'] ?? '' }}
  </div>

  <table>
    <thead>
      <tr>
        <th>Party Name</th>
        <th>Phone</th>
        <th>Credit Status</th>
        <th class="num">Credit Limit</th>
        <th class="num">Open Bills</th>
        <th class="num">Max Days Overdue</th>
        <th class="num">Overdue Amount</th>
        <th class="num">Total Balance</th>
      </tr>
    </thead>
    <tbody>
      @forelse($parties as $p)
        <tr>
          <td>{{ $p['party_name'] ?? '-' }}</td>
          <td>{{ $p['party_phone'] ?? '-' }}</td>
          <td>{{ strtoupper($p['credit_status'] ?? 'ACTIVE') }}</td>
          <td class="num">{{ ($p['credit_limit'] ?? 0) > 0 ? number_format($p['credit_limit'], 2) : 'Unlimited' }}</td>
          <td class="num">{{ number_format($p['open_entry_count'] ?? 0) }}</td>
          <td class="num">{{ number_format($p['max_days_overdue'] ?? 0) }} days</td>
          <td class="num">{{ number_format($p['total_overdue'] ?? 0, 2) }}</td>
          <td class="num">{{ number_format($p['total'] ?? 0, 2) }}</td>
        </tr>
      @empty
        <tr><td colspan="8" style="text-align:center;">No overdue accounts found.</td></tr>
      @endforelse
    </tbody>
    <tfoot>
      <tr>
        <td colspan="6">Total Overdue Exposure ({{ $total_parties ?? 0 }} parties)</td>
        <td class="num">Rs. {{ number_format($total_overdue_sum ?? 0, 2) }}</td>
        <td></td>
      </tr>
    </tfoot>
  </table>
</body>
</html>

