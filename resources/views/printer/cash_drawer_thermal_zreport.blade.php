@php
    $setting = $setting ?? new \App\Models\Setting();
    $logo    = $setting?->logo_url ?? null;
    $store   = $setting?->store_name ?: 'KAROBAR PHARMACY & STORE';
    $phone   = $setting?->phone_number ?? '';
    $addr    = $setting?->address ?? '';
    $cashier = optional($session->user)->name ?? 'Cashier';

    $isClosed   = ($session->status ?? 'open') === 'closed';
    $reportType = $isClosed ? 'DAY-END Z-REPORT SLIP' : 'MID-SHIFT X-REPORT SLIP';
    $statusBadge = $isClosed ? 'CLOSED & RECONCILED' : 'OPEN SHIFT (LIVE READING)';

    $openedAt  = $session->opened_at ? \Carbon\Carbon::parse($session->opened_at)->format('d M Y, h:i A') : '—';
    $closedAt  = $session->closed_at ? \Carbon\Carbon::parse($session->closed_at)->format('d M Y, h:i A') : 'Active (In Progress)';
    $printedAt = now()->format('d M Y, h:i A');
    $duration  = $summary['duration'] ?? '—';

    $openingFloat  = (float) ($summary['opening_balance'] ?? $session->opening_balance ?? 0);
    $totalCashIn   = (float) ($summary['total_cash_in'] ?? 0);
    $totalCashOut  = (float) ($summary['total_cash_out'] ?? 0);
    $expectedCash  = (float) ($summary['expected_balance'] ?? $session->closing_balance_expected ?? ($openingFloat + $totalCashIn - $totalCashOut));
    $actualCash    = $session->closing_balance_actual !== null ? (float) $session->closing_balance_actual : null;
    $difference    = $session->difference !== null ? (float) $session->difference : ($actualCash !== null ? round($actualCash - $expectedCash, 2) : null);

    $counts     = $summary['counts'] ?? [];
    $shiftSales = $summary['shift_sales'] ?? [];

    $totalInvoices    = (int) ($shiftSales['total_invoices_count'] ?? 0);
    $retailInvoices   = (int) ($shiftSales['retail_invoices_count'] ?? 0);
    $wholesaleInvoices= (int) ($shiftSales['wholesale_invoices_count'] ?? 0);
    $grossSales       = (float) ($shiftSales['gross_sales'] ?? 0);
    $discountTotal    = (float) ($shiftSales['discount_total'] ?? 0);
    $taxTotal         = (float) ($shiftSales['tax_total'] ?? 0);
    $netSales         = (float) ($shiftSales['net_sales'] ?? 0);
    $cashTenderSales  = (float) ($shiftSales['cash_tender_sales'] ?? ($summary['cash_sales'] ?? 0));
    $nonCashSales     = (float) ($shiftSales['card_bank_online_sales'] ?? 0);
    $creditUdharSales = (float) ($shiftSales['credit_udhar_sales'] ?? 0);

    $returnsCount     = (int) ($shiftSales['sale_returns_count'] ?? 0);
    $returnsTotal     = (float) ($shiftSales['sale_returns_total'] ?? 0);
    $cashRefundRet    = (float) ($shiftSales['cash_refund_returns'] ?? ($summary['cash_returns'] ?? 0));
    $creditAdjRet     = (float) ($shiftSales['credit_adj_returns'] ?? 0);

    $denominations = is_array($session->closing_denominations ?? null)
        ? $session->closing_denominations
        : (is_array($summary['closing_denominations'] ?? null) ? $summary['closing_denominations'] : []);

    $denomLabels = [
        '5000'  => ['label' => 'Rs. 5,000', 'mult' => 5000],
        '1000'  => ['label' => 'Rs. 1,000', 'mult' => 1000],
        '500'   => ['label' => 'Rs. 500',   'mult' => 500],
        '100'   => ['label' => 'Rs. 100',   'mult' => 100],
        '50'    => ['label' => 'Rs. 50',    'mult' => 50],
        '20'    => ['label' => 'Rs. 20',    'mult' => 20],
        '10'    => ['label' => 'Rs. 10',    'mult' => 10],
        'coins' => ['label' => 'Coins',     'mult' => 1],
    ];
    $activeDenoms = [];
    foreach ($denomLabels as $k => $meta) {
        $qty = isset($denominations[$k]) ? (float) $denominations[$k] : 0;
        if ($qty > 0) {
            $activeDenoms[] = [
                'key'   => $k,
                'label' => $meta['label'],
                'qty'   => $qty,
                'mult'  => $meta['mult'],
                'total' => round($qty * $meta['mult'], 2),
            ];
        }
    }
@endphp
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8"/>
<title>{{ $reportType }} - Shift #{{ $session->id }}</title>
<style>
  @page { margin: 0; }
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; color: #000; background: #fff; }
  body {
    font-family: 'Courier New', Courier, monospace;
    font-weight: 700;
    font-size: 11.5px;
    line-height: 1.32;
    -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
  }
  .print-actions { margin: 8px; text-align: right; }
  .print-actions button { padding: 6px 12px; cursor: pointer; font-weight: bold; }
  @media print { .print-actions { display: none; } .receipt { margin: 0; } }
  .receipt {
    width: 78mm;
    max-width: 100%;
    margin: 0 auto;
    padding: 2mm 4px 6mm;
    position: relative;
    overflow: hidden;
  }
  .center { text-align: center; }
  .right  { text-align: right; }
  .logo { max-width: 48mm; max-height: 22mm; object-fit: contain; display: block; margin: 0 auto 4px; }
  .hr { border-top: 1px dashed #000; margin: 5px 0; }
  .double-hr { border-top: 2px double #000; margin: 5px 0; }
  .solid-hr { border-top: 1px solid #000; margin: 4px 0; }
  .banner-box {
    border: 2px solid #000;
    padding: 4px 4px;
    margin: 5px 0;
    text-align: center;
  }
  .banner-title {
    font-size: 14px;
    font-weight: 900;
    letter-spacing: 0.5px;
  }
  .banner-sub {
    font-size: 11px;
    margin-top: 1px;
  }
  .sec-head {
    background: #000;
    color: #fff;
    padding: 2px 4px;
    font-size: 11px;
    font-weight: 900;
    text-transform: uppercase;
    margin: 6px 0 4px;
    letter-spacing: 0.4px;
  }
  .pair {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    gap: 6px;
  }
  .pair + .pair { margin-top: 2px; }
  .pair .lbl { flex: 1; min-width: 0; }
  .pair .val { text-align: right; white-space: nowrap; }
  .sub-pair {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    padding-left: 8px;
    font-size: 10.5px;
  }
  .sub-pair + .sub-pair { margin-top: 1.5px; }
  .total-line {
    font-size: 12.5px;
    font-weight: 900;
    padding: 2px 0;
  }
  .grand-box {
    border: 1.5px solid #000;
    padding: 4px 5px;
    margin: 4px 0;
  }
  .notes-box {
    border: 1px dashed #000;
    padding: 4px 5px;
    margin-top: 5px;
    font-size: 10.5px;
    white-space: pre-wrap;
    word-break: break-word;
  }
  .sig-block {
    margin-top: 16px;
    font-size: 10.5px;
  }
  .sig-line {
    border-top: 1px solid #000;
    margin-top: 22px;
    padding-top: 2px;
    text-align: center;
  }
  .foot {
    margin-top: 8px;
    text-align: center;
    font-size: 9.5px;
  }
</style>
@include('printer.partials.calibration_styles')
</head>
<body onload="if (!window.karobarDesktop && !window.__karobarSilentPrintWorker && !/Electron/i.test(navigator.userAgent) && window.self === window.top && !new URLSearchParams(window.location.search).has('silent_print')) window.print()">
<div class="print-actions">
  <button onclick="window.print()">Print Thermal Slip</button>
</div>
<div class="receipt">
  <div class="content">
    @if($logo)
      <img src="{{ $logo }}" alt="Logo" class="logo">
    @endif
    <div class="center" style="font-size: 18px; font-weight: 900;">{{ $store }}</div>
    @if($addr)<div class="center" style="font-size: 10px;">{{ $addr }}</div>@endif
    @if($phone)<div class="center" style="font-size: 10px;">Ph: {{ $phone }}</div>@endif

    <div class="banner-box">
      <div class="banner-title">{{ $reportType }}</div>
      <div class="banner-sub">SHIFT #{{ $session->id }} • {{ $statusBadge }}</div>
    </div>

    <div class="pair"><div class="lbl">Cashier:</div><div class="val">{{ $cashier }}</div></div>
    <div class="pair"><div class="lbl">Opened:</div><div class="val">{{ $openedAt }}</div></div>
    <div class="pair"><div class="lbl">Closed:</div><div class="val">{{ $closedAt }}</div></div>
    <div class="pair"><div class="lbl">Duration:</div><div class="val">{{ $duration }}</div></div>
    <div class="pair"><div class="lbl">Printed:</div><div class="val">{{ $printedAt }}</div></div>

    {{-- 1. SHIFT SALES & TENDER SUMMARY --}}
    <div class="sec-head">1. Shift Sales &amp; Tender Summary</div>
    <div class="pair">
      <div class="lbl">Total Invoices:</div>
      <div class="val">{{ $totalInvoices }} (R:{{ $retailInvoices }} / W:{{ $wholesaleInvoices }})</div>
    </div>
    <div class="pair">
      <div class="lbl">Gross Shift Sales:</div>
      <div class="val">{{ number_format($grossSales, 2) }}</div>
    </div>
    @if($discountTotal > 0)
    <div class="pair">
      <div class="lbl">Invoice Discounts:</div>
      <div class="val">-{{ number_format($discountTotal, 2) }}</div>
    </div>
    @endif
    @if($taxTotal > 0)
    <div class="pair">
      <div class="lbl">Tax Amount:</div>
      <div class="val">+{{ number_format($taxTotal, 2) }}</div>
    </div>
    @endif
    <div class="solid-hr"></div>
    <div class="pair total-line">
      <div class="lbl">NET SHIFT SALES:</div>
      <div class="val">Rs. {{ number_format($netSales, 2) }}</div>
    </div>
    <div class="sub-pair">
      <div>• Cash Sales (Paid):</div>
      <div>{{ number_format($cashTenderSales, 2) }}</div>
    </div>
    <div class="sub-pair">
      <div>• Bank / Card / Online:</div>
      <div>{{ number_format($nonCashSales, 2) }}</div>
    </div>
    <div class="sub-pair">
      <div>• Credit (Udhar) Sales:</div>
      <div>{{ number_format($creditUdharSales, 2) }}</div>
    </div>

    @if($returnsCount > 0 || $returnsTotal > 0 || $cashRefundRet > 0)
    <div class="hr"></div>
    <div class="pair">
      <div class="lbl">Sale Returns ({{ $returnsCount }}):</div>
      <div class="val">-{{ number_format(max($returnsTotal, $cashRefundRet), 2) }}</div>
    </div>
    <div class="sub-pair">
      <div>• Cash Refunded:</div>
      <div>-{{ number_format($cashRefundRet, 2) }}</div>
    </div>
    @if($creditAdjRet > 0)
    <div class="sub-pair">
      <div>• Credit / Ledger Adj:</div>
      <div>-{{ number_format($creditAdjRet, 2) }}</div>
    </div>
    @endif
    @endif

    {{-- 2. CASH DRAWER INFLOWS (+) --}}
    <div class="sec-head">2. Cash Drawer Inflows (+)</div>
    <div class="pair">
      <div class="lbl">Opening Cash Float:</div>
      <div class="val">{{ number_format($openingFloat, 2) }}</div>
    </div>
    <div class="pair">
      <div class="lbl">(+) Cash Sales ({{ (int)($counts['cash_sales'] ?? 0) }}):</div>
      <div class="val">+{{ number_format((float)($summary['cash_sales'] ?? 0), 2) }}</div>
    </div>
    <div class="pair">
      <div class="lbl">(+) Customer Receipts ({{ (int)($counts['customer_payments'] ?? 0) }}):</div>
      <div class="val">+{{ number_format((float)($summary['customer_payments'] ?? 0), 2) }}</div>
    </div>
    @if((float)($summary['purchase_returns'] ?? 0) > 0 || (int)($counts['purchase_returns'] ?? 0) > 0)
    <div class="pair">
      <div class="lbl">(+) Supplier Return Cash ({{ (int)($counts['purchase_returns'] ?? 0) }}):</div>
      <div class="val">+{{ number_format((float)($summary['purchase_returns'] ?? 0), 2) }}</div>
    </div>
    @endif
    <div class="pair">
      <div class="lbl">(+) Manual Cash In ({{ (int)($counts['manual_cash_in'] ?? 0) }}):</div>
      <div class="val">+{{ number_format((float)($summary['manual_cash_in'] ?? 0), 2) }}</div>
    </div>
    <div class="solid-hr"></div>
    <div class="pair total-line">
      <div class="lbl">TOTAL CASH INFLOW:</div>
      <div class="val">+Rs. {{ number_format($totalCashIn, 2) }}</div>
    </div>

    {{-- 3. CASH DRAWER OUTFLOWS (-) --}}
    <div class="sec-head">3. Cash Drawer Outflows (-)</div>
    <div class="pair">
      <div class="lbl">(-) Cash Purchases ({{ (int)($counts['cash_purchases'] ?? 0) }}):</div>
      <div class="val">-{{ number_format((float)($summary['cash_purchases'] ?? 0), 2) }}</div>
    </div>
    <div class="pair">
      <div class="lbl">(-) Cash Sale Refunds ({{ (int)($counts['cash_returns'] ?? 0) }}):</div>
      <div class="val">-{{ number_format((float)($summary['cash_returns'] ?? 0), 2) }}</div>
    </div>
    <div class="pair">
      <div class="lbl">(-) Supplier Payments ({{ (int)($counts['supplier_payments'] ?? 0) }}):</div>
      <div class="val">-{{ number_format((float)($summary['supplier_payments'] ?? 0), 2) }}</div>
    </div>
    <div class="pair">
      <div class="lbl">(-) Shop Expenses ({{ (int)($counts['expenses'] ?? 0) }}):</div>
      <div class="val">-{{ number_format((float)($summary['expenses'] ?? 0), 2) }}</div>
    </div>
    @if((float)($summary['staff_advances'] ?? 0) > 0 || (int)($counts['staff_advances'] ?? 0) > 0)
    <div class="pair">
      <div class="lbl">(-) Staff Advances ({{ (int)($counts['staff_advances'] ?? 0) }}):</div>
      <div class="val">-{{ number_format((float)($summary['staff_advances'] ?? 0), 2) }}</div>
    </div>
    @endif
    <div class="pair">
      <div class="lbl">(-) Manual Cash Out ({{ (int)($counts['manual_cash_out'] ?? 0) }}):</div>
      <div class="val">-{{ number_format((float)($summary['manual_cash_out'] ?? 0), 2) }}</div>
    </div>
    <div class="solid-hr"></div>
    <div class="pair total-line">
      <div class="lbl">TOTAL CASH OUTFLOW:</div>
      <div class="val">-Rs. {{ number_format($totalCashOut, 2) }}</div>
    </div>

    {{-- 4. CASH RECONCILIATION --}}
    <div class="sec-head">4. Cash Drawer Reconciliation</div>
    <div class="pair">
      <div class="lbl">Opening Float:</div>
      <div class="val">{{ number_format($openingFloat, 2) }}</div>
    </div>
    <div class="pair">
      <div class="lbl">(+) Total Cash In:</div>
      <div class="val">+{{ number_format($totalCashIn, 2) }}</div>
    </div>
    <div class="pair">
      <div class="lbl">(-) Total Cash Out:</div>
      <div class="val">-{{ number_format($totalCashOut, 2) }}</div>
    </div>
    <div class="grand-box">
      <div class="pair total-line">
        <div class="lbl">EXPECTED CASH:</div>
        <div class="val">Rs. {{ number_format($expectedCash, 2) }}</div>
      </div>
      @if($isClosed && $actualCash !== null)
      <div class="solid-hr"></div>
      <div class="pair total-line">
        <div class="lbl">ACTUAL COUNTED:</div>
        <div class="val">Rs. {{ number_format($actualCash, 2) }}</div>
      </div>
      <div class="solid-hr"></div>
      <div class="pair total-line">
        <div class="lbl">
          @if(abs($difference ?? 0) < 0.005)
            VARIANCE (BALANCED):
          @elseif(($difference ?? 0) > 0)
            VARIANCE (SURPLUS +):
          @else
            VARIANCE (SHORTAGE -):
          @endif
        </div>
        <div class="val">
          @if(abs($difference ?? 0) < 0.005)
            Rs. 0.00
          @elseif(($difference ?? 0) > 0)
            +Rs. {{ number_format($difference, 2) }}
          @else
            -Rs. {{ number_format(abs($difference), 2) }}
          @endif
        </div>
      </div>
      @else
      <div class="hr"></div>
      <div class="center" style="font-size: 10.5px;">
        MID-SHIFT X-REPORT (DRAWER STILL OPEN)
      </div>
      @endif
    </div>

    {{-- 5. DENOMINATION BREAKDOWN (IF RECORDED) --}}
    @if(!empty($activeDenoms))
    <div class="sec-head">5. Cash Note Denominations</div>
    @foreach($activeDenoms as $d)
      <div class="pair">
        <div class="lbl">
          {{ $d['key'] === 'coins' ? 'Coins (Rs. 1/2/5)' : ($d['label'] . ' × ' . (int)$d['qty']) }}
        </div>
        <div class="val">{{ number_format($d['total'], 2) }}</div>
      </div>
    @endforeach
    @endif

    @if(!empty($session->notes))
    <div class="notes-box">
      <strong>Shift Notes:</strong> {{ $session->notes }}
    </div>
    @endif

    <div class="sig-block">
      <div class="sig-line">Cashier Signature ({{ $cashier }})</div>
      <div class="sig-line">Manager / Supervisor Signature</div>
    </div>

    <div class="hr"></div>
    <div class="foot">
      System Generated {{ $isClosed ? 'Z-Report' : 'X-Report' }} • Shift #{{ $session->id }}
    </div>
  </div>
</div>
</body>
</html>
