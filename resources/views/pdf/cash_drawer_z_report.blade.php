<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>Cash Drawer Shift Z-Report - #{{ $session->id }}</title>
  <style>
    @page { margin: 14mm 12mm; }
    body { font-family: DejaVu Sans, sans-serif; font-size: 10.5px; color: #1e293b; line-height: 1.4; }
    table { width: 100%; border-collapse: collapse; }
    .header-table { border-bottom: 2px solid #0f172a; padding-bottom: 10px; margin-bottom: 12px; }
    .store-name { font-size: 18px; font-weight: bold; color: #0f172a; }
    .store-meta { font-size: 10px; color: #475569; }
    .doc-badge { display: inline-block; background: #0f172a; color: #fff; font-size: 12px; font-weight: bold; padding: 4px 10px; border-radius: 3px; text-transform: uppercase; }
    .summary-table td, .summary-table th { border: 1px solid #cbd5e1; padding: 6px 8px; font-size: 10.5px; }
    .summary-table th { background: #f1f5f9; text-align: left; }
    .tx-table th { background: #0f172a; color: #fff; font-size: 9px; font-weight: bold; text-transform: uppercase; padding: 6px; border: 1px solid #0f172a; text-align: left; }
    .tx-table td { padding: 5px 6px; border: 1px solid #cbd5e1; font-size: 9.5px; }
    .text-right { text-align: right !important; }
  </style>
</head>
<body>
  @php
    $storeName = $setting->store_name ?? 'Business Store';
  @endphp

  <table class="header-table">
    <tr>
      <td style="width: 60%;">
        <div class="store-name">{{ $storeName }}</div>
        <div class="store-meta">Cashier: <strong>{{ $session->user->name ?? 'N/A' }}</strong></div>
      </td>
      <td style="width: 40%; text-align: right;">
        <div class="doc-badge">Shift Z-Report #{{ $session->id }}</div>
        <div class="store-meta" style="margin-top:4px;">
          Status: <strong>{{ strtoupper($session->status ?? 'OPEN') }}</strong><br>
          Opened: {{ $session->opened_at ? \Carbon\Carbon::parse($session->opened_at)->format('d M Y h:i A') : '-' }}<br>
          Closed: {{ $session->closed_at ? \Carbon\Carbon::parse($session->closed_at)->format('d M Y h:i A') : 'Active Shift' }}
        </div>
      </td>
    </tr>
  </table>

  <table class="summary-table" style="margin-bottom: 14px;">
    <tr>
      <th>Opening Float</th>
      <td class="text-right">Rs. {{ number_format((float)($session->opening_balance ?? 0), 2) }}</td>
      <th>Expected Drawer Balance</th>
      <td class="text-right"><strong>Rs. {{ number_format((float)($session->closing_balance_expected ?? $session->expected_balance ?? 0), 2) }}</strong></td>
    </tr>
    <tr>
      <th>Actual Counted Closing Cash</th>
      <td class="text-right">
        {{ ($session->closing_balance_actual ?? $session->actual_closing_balance) !== null ? 'Rs. ' . number_format((float)($session->closing_balance_actual ?? $session->actual_closing_balance), 2) : 'Pending Closure' }}
      </td>
      <th>Variance (Over / Short)</th>
      <td class="text-right">
        <strong>{{ $session->difference !== null ? 'Rs. ' . number_format((float)$session->difference, 2) : '—' }}</strong>
      </td>
    </tr>
  </table>

  @php
    $cashInTypes = ['cash_sale', 'customer_payment', 'purchase_return', 'cash_in'];
    $runningBalance = (float)($session->opening_balance ?? 0);
  @endphp

  <h3 style="font-size:12px; margin: 10px 0 6px 0;">Shift Cash Movements Log (Chronological Order)</h3>
  <table class="tx-table">
    <thead>
      <tr>
        <th style="width: 30px;">#</th>
        <th style="width: 115px;">Time</th>
        <th style="width: 110px;">Type</th>
        <th style="width: 55px;">Flow</th>
        <th>Notes / Reference</th>
        <th class="text-right" style="width: 95px;">Amount</th>
        <th class="text-right" style="width: 100px;">Balance</th>
      </tr>
    </thead>
    <tbody>
      <tr style="background: #f8fafc; font-weight: bold;">
        <td>—</td>
        <td>{{ $session->opened_at ? \Carbon\Carbon::parse($session->opened_at)->format('d M Y h:i A') : '-' }}</td>
        <td>DRAWER OPENED</td>
        <td>OPEN</td>
        <td>Opening Float{{ $session->notes ? ' — ' . $session->notes : '' }}</td>
        <td class="text-right">—</td>
        <td class="text-right">Rs. {{ number_format($runningBalance, 2) }}</td>
      </tr>
      @forelse($session->transactions as $idx => $tx)
        @php
          $isCashIn = in_array($tx->type, $cashInTypes, true);
          $amt = (float)($tx->amount ?? 0);
          $runningBalance += $isCashIn ? $amt : -$amt;
        @endphp
        <tr>
          <td>{{ $idx + 1 }}</td>
          <td>{{ $tx->created_at ? \Carbon\Carbon::parse($tx->created_at)->format('d M Y h:i A') : '-' }}</td>
          <td>{{ strtoupper(str_replace('_', ' ', $tx->type ?? '')) }}</td>
          <td>{{ $isCashIn ? 'IN' : 'OUT' }}</td>
          <td>{{ $tx->notes ?: ($tx->reference_type ? $tx->reference_type . ' #' . $tx->reference_id : '—') }}</td>
          <td class="text-right">{{ $isCashIn ? '+' : '-' }} Rs. {{ number_format($amt, 2) }}</td>
          <td class="text-right"><strong>Rs. {{ number_format($runningBalance, 2) }}</strong></td>
        </tr>
      @empty
        <tr><td colspan="7" style="text-align:center;">No cash transactions recorded during this shift.</td></tr>
      @endforelse
    </tbody>
  </table>
</body>
</html>

