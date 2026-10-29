<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <title>Trial Balance & Balance Sheet</title>
    <style>
        body { font-family: DejaVu Sans, sans-serif; font-size: 10px; color: #1e293b; margin: 18px; }
        .header { text-align: center; border-bottom: 2px solid #0f172a; padding-bottom: 10px; margin-bottom: 14px; }
        .header h1 { font-size: 16px; margin: 0; text-transform: uppercase; }
        .header p { margin: 3px 0 0; color: #475569; font-size: 10px; }
        .section-title { font-size: 11px; font-weight: bold; text-transform: uppercase; background: #0f172a; color: #fff; padding: 5px 8px; margin-top: 12px; margin-bottom: 6px; }
        table.data-table { width: 100%; border-collapse: collapse; margin-bottom: 12px; }
        table.data-table th, table.data-table td { border: 1px solid #cbd5e1; padding: 5px 6px; text-align: left; }
        table.data-table th { background: #f1f5f9; font-weight: bold; font-size: 9px; text-transform: uppercase; }
        .text-right { text-align: right !important; }
        .font-bold { font-weight: bold; }
    </style>
</head>
<body>
    <div class="header">
        <h1>Financial Position (Balance Sheet) & Trial Balance</h1>
        <p>Period: {{ $trialBalance['period']['from'] ?? '' }} to {{ $trialBalance['period']['to'] ?? '' }} | Generated: {{ $meta['generatedAt'] ?? '' }}</p>
    </div>

    <div class="section-title">1. Double-Entry Trial Balance</div>
    <table class="data-table">
        <thead>
            <tr>
                <th>Code</th>
                <th>Account Name</th>
                <th>Class</th>
                <th class="text-right">Debit</th>
                <th class="text-right">Credit</th>
            </tr>
        </thead>
        <tbody>
            @foreach($trialBalance['rows'] ?? [] as $row)
            <tr>
                <td>{{ $row['code'] }}</td>
                <td>{{ $row['name'] }}</td>
                <td>{{ strtoupper($row['account_type']) }}</td>
                <td class="text-right">{{ $row['debit'] > 0 ? number_format($row['debit'], 2) : '—' }}</td>
                <td class="text-right">{{ $row['credit'] > 0 ? number_format($row['credit'], 2) : '—' }}</td>
            </tr>
            @endforeach
            <tr class="font-bold">
                <td colspan="3">TOTAL TRIAL BALANCE ({{ ($trialBalance['is_balanced'] ?? false) ? 'BALANCED' : 'UNBALANCED' }})</td>
                <td class="text-right">{{ number_format($trialBalance['total_debit'] ?? 0, 2) }}</td>
                <td class="text-right">{{ number_format($trialBalance['total_credit'] ?? 0, 2) }}</td>
            </tr>
        </tbody>
    </table>

    <div class="section-title">2. Statement of Financial Position (Balance Sheet as of {{ $balanceSheet['as_of'] ?? '' }})</div>
    <table class="data-table">
        <thead>
            <tr>
                <th>Category</th>
                <th>Code</th>
                <th>Account</th>
                <th class="text-right">Balance</th>
            </tr>
        </thead>
        <tbody>
            @foreach($balanceSheet['assets'] ?? [] as $a)
            <tr>
                <td>ASSET</td>
                <td>{{ $a['code'] }}</td>
                <td>{{ $a['name'] }}</td>
                <td class="text-right">{{ number_format($a['amount'], 2) }}</td>
            </tr>
            @endforeach
            <tr class="font-bold">
                <td colspan="3">TOTAL ASSETS</td>
                <td class="text-right">{{ number_format($balanceSheet['total_assets'] ?? 0, 2) }}</td>
            </tr>
            @foreach($balanceSheet['liabilities'] ?? [] as $l)
            <tr>
                <td>LIABILITY</td>
                <td>{{ $l['code'] }}</td>
                <td>{{ $l['name'] }}</td>
                <td class="text-right">{{ number_format($l['amount'], 2) }}</td>
            </tr>
            @endforeach
            <tr class="font-bold">
                <td colspan="3">TOTAL LIABILITIES</td>
                <td class="text-right">{{ number_format($balanceSheet['total_liabilities'] ?? 0, 2) }}</td>
            </tr>
            @foreach($balanceSheet['equity'] ?? [] as $e)
            <tr>
                <td>EQUITY</td>
                <td>{{ $e['code'] }}</td>
                <td>{{ $e['name'] }}</td>
                <td class="text-right">{{ number_format($e['amount'], 2) }}</td>
            </tr>
            @endforeach
            <tr class="font-bold">
                <td colspan="3">TOTAL LIABILITIES + OWNER'S EQUITY</td>
                <td class="text-right">{{ number_format($balanceSheet['total_liabilities_and_equity'] ?? 0, 2) }}</td>
            </tr>
        </tbody>
    </table>
</body>
</html>

