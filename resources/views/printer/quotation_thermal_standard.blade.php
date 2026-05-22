@php
    $logo   = $setting->logo_url ?? null;
    $store  = $setting->store_name ?? 'Store Name';
    $phone  = $setting->phone_number ?? '';
    $addr   = $setting->address ?? '';

    $posted = $invoice->posted_number ?? '';
    $date   = $invoice->date ? \Carbon\Carbon::parse($invoice->date)->format('d M Y') : '';
    $user   = optional($invoice->user)->name ?? '';
    $cust   = optional($invoice->customer)->name ?? '';
    $remarks= $invoice->remarks ?? '';

    $gross  = (float)($invoice->items->sum('sub_total') ?? 0);
    $disc   = (float)($invoice->discount_amount ?? 0);
    $tax    = (float)($invoice->tax_amount ?? 0);
    $total  = isset($printTotal)
                ? (float)$printTotal
                : (float)($invoice->total ?? ($gross - $disc + $tax));

    $footerNote = trim(($setting->note ?? '') ?? '');
@endphp
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8"/>
<title>Quotation (Thermal) - {{ $posted }}</title>
<style>
  @page { size: 78mm auto; margin: 0; }
  * { box-sizing: border-box; }
  html, body { margin:0; padding:0; color:#000; background:#fff; }
  body {
    font-family: 'Courier New', monospace;
    font-weight: 700;
    font-size: 12px;
    line-height: 1.3;
    -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
  }
  .print-actions { margin:8px; text-align:right; }
  .print-actions button { padding:6px 10px; cursor:pointer; }
  @media print { .print-actions { display:none; } }
  .receipt { width:78mm; max-width:100%; margin:0 auto; padding:0 6px 6px; position:relative; overflow:hidden; }
  .center { text-align:center; }
  .right  { text-align:right; }
  .logo { max-width:56mm; max-height:30mm; object-fit:contain; display:block; margin:0 auto 4px; }
  .hr { border-top:1px dashed #000; margin:5px 0; }
  .pair { display:flex; justify-content:space-between; }
  .pair + .pair { margin-top:2px; }
  .content { position:relative; z-index:1; }
  table { width:100%; border-collapse:collapse; }
  thead th { text-align:left; padding:2px 2px; border:1px solid #000; }
  tbody td { padding:0px 0; border-bottom:1px solid #000; vertical-align:top; }
  th.right, td.right { text-align:right; white-space:nowrap; }
  td.center { text-align:center; }
  th.col-name { width:55%; }
  th.col-qty { width:15%; }
  th.col-price { width:15%; }
  th.col-disc { width:15%; }
  th.col-sub  { width:20%; }
  .totals .pair.total { font-size:13px; font-weight:bold; margin-top:4px; }
  .note { margin-top:5px; white-space:pre-wrap; font-size:10px; }
  .foot { margin-top:8px; text-align:center; font-size:9px; }
</style>
</head>
<body>
<div class="print-actions"><button onclick="window.print()">Print</button></div>
<div class="receipt">
  @if($logo)
    <img class="logo" src="{{ $logo }}" alt="Logo">
  @endif

  <div class="content">
    <div class="center" style="font-size:24px">{{ $store }}</div>
    @if($addr)<div class="center" style="font-size:10px">{{ $addr }}</div>@endif
    @if($phone)<div class="center" style="font-size:10px">Ph: {{ $phone }}</div>@endif

    <div class="hr"></div>
    <div class="pair"><div>Quotation</div><div># {{ $posted }}</div></div>
    <div class="pair"><div>Date</div><div>{{ $date }}</div></div>
    @if($cust)<div class="pair"><div>Customer</div><div>{{ $cust }}</div></div>@endif
    @if($user)<div class="pair"><div>User</div><div>{{ $user }}</div></div>@endif
    @if($remarks)<div class="pair"><div>Remarks</div><div>{{ $remarks }}</div></div>@endif
    <div class="hr"></div>

    <table>
      <thead>
        <tr>
          <th class="col-name">Name</th>
          <th class="right col-qty">Qty</th>
          <th class="right col-price">Price</th>
          <th class="right col-disc">Disc</th>
          <th class="right col-sub">Subt.</th>
        </tr>
      </thead>
      <tbody>
        @foreach($invoice->items as $it)
          <tr>
            <td style="font-size:10px">{{ $it->line_type === 'manual' ? ($it->manual_name ?? '-') : optional($it->product)->name ?? '-' }}</td>
            <td class="center">{{ number_format((float)$it->quantity) }}</td>
            <td class="right">{{ number_format((float)$it->price, 2) }}</td>
            <td class="center">{{ number_format((float)$it->item_discount_percentage) }}</td>
            <td class="right">{{ number_format((float)$it->sub_total, 2) }}</td>
          </tr>
        @endforeach
      </tbody>
    </table>

    <div class="totals">
      <div class="hr"></div>
      <div class="pair"><div>Gross</div><div>{{ number_format((float)$gross, 2) }}</div></div>
      <div class="pair"><div>Discount</div><div>{{ number_format((float)$disc, 2) }}</div></div>
      <div class="pair"><div>Tax</div><div>{{ number_format((float)$tax, 2) }}</div></div>
      <div class="pair total"><div>TOTAL</div><div>{{ number_format((float)$total, 2) }}</div></div>
    </div>

    @if($footerNote !== '')
      <div class="hr"></div>
      <div class="note">{{ $footerNote }}</div>
    @endif

    <div class="foot">Software by Asim Shahzad<br/>PH:0304-7674787</div>
  </div>
</div>
</body>
</html>

