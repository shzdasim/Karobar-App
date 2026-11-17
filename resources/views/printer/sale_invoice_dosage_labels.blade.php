<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    @php
        $cfg = $stickerConfig ?? \App\Models\Setting::defaultDosageStickerConfig();
        $w = max(25, min(150, (int) ($cfg['width_mm'] ?? 50)));
        $h = max(15, min(120, (int) ($cfg['height_mm'] ?? 25)));
        $printerMode = ($cfg['printer_mode'] ?? 'receipt_roll') === 'label_printer' ? 'label_printer' : 'receipt_roll';
        $template = $cfg['template'] ?? 'classic';
        $fontScale = $cfg['font_scale'] ?? 'normal';
        $scaleMult = $fontScale === 'compact' ? 0.9 : ($fontScale === 'large' ? 1.15 : 1.0);

        $ptHeader  = round(max(4.0, min(28.0, (float) ($cfg['font_size_header'] ?? 6.5))) * $scaleMult, 2);
        $ptPatient = round(max(4.0, min(28.0, (float) ($cfg['font_size_patient'] ?? 6.0))) * $scaleMult, 2);
        $ptProduct = round(max(4.0, min(28.0, (float) ($cfg['font_size_product'] ?? ($template === 'urdu_focus' ? 7.2 : 8.0)))) * $scaleMult, 2);
        $ptDosage  = round(max(4.0, min(28.0, (float) ($cfg['font_size_dosage'] ?? ($template === 'urdu_focus' ? 8.6 : 7.5)))) * $scaleMult, 2);
        $ptFooter  = round(max(4.0, min(28.0, (float) ($cfg['font_size_footer'] ?? 5.5))) * $scaleMult, 2);

        $fwHeader  = (int) ($cfg['font_weight_header'] ?? 800);
        $fwPatient = (int) ($cfg['font_weight_patient'] ?? 600);
        $fwProduct = (int) ($cfg['font_weight_product'] ?? 900);
        $fwDosage  = (int) ($cfg['font_weight_dosage'] ?? 800);
        $fwFooter  = (int) ($cfg['font_weight_footer'] ?? 700);

        $showShop = (bool) ($cfg['show_shop_name'] ?? true);
        $showInvMeta = (bool) ($cfg['show_invoice_meta'] ?? true);
        $showPatient = (bool) ($cfg['show_patient'] ?? true);
        $showQty = (bool) ($cfg['show_quantity'] ?? true);
        $showBatch = (bool) ($cfg['show_batch'] ?? true);
        $showExpiry = (bool) ($cfg['show_expiry'] ?? true);
        $showDoctor = (bool) ($cfg['show_doctor'] ?? false);
        $footerNote = trim((string) ($cfg['footer_note'] ?? 'Keep out of reach of children'));
    @endphp
    <title>Dosage Stickers ({{ $w }}×{{ $h }}mm) — {{ $invoice->posted_number }}</title>
    <style>
        @if($printerMode === 'label_printer')
        @page {
            size: {{ $w }}mm {{ $h }}mm;
            margin: 0;
        }
        @else
        /* Receipt Roll Mode (72mm / 80mm): Keep portrait orientation so Chrome does not rotate sideways */
        @page {
            size: portrait;
            margin: 0;
        }
        @endif
        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }
        body {
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Noto Nastaliq Urdu", Arial, sans-serif;
            background: #f1f5f9;
            color: #000;
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
        }
        .no-print-bar {
            background: #0f172a;
            color: #fff;
            padding: 10px 16px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 10px;
            flex-wrap: wrap;
            font-size: 12.5px;
            margin-bottom: 12px;
        }
        .no-print-actions {
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .no-print-bar a.mode-btn {
            background: #1e293b;
            color: #cbd5e1;
            border: 1px solid #334155;
            border-radius: 6px;
            padding: 5px 10px;
            font-size: 11.5px;
            font-weight: 600;
            text-decoration: none;
        }
        .no-print-bar a.mode-btn.active {
            background: #0284c7;
            color: #fff;
            border-color: #38bdf8;
        }
        .no-print-bar button {
            background: #2563eb;
            color: #fff;
            border: none;
            border-radius: 6px;
            padding: 6px 14px;
            font-weight: 700;
            cursor: pointer;
        }
        .labels-wrapper {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 8px;
            padding-bottom: 16px;
        }
        .sticker {
            width: {{ $w }}mm;
            height: {{ $h }}mm;
            background: #fff;
            border: 1px dashed #94a3b8;
            padding: {{ $h >= 35 ? '2.2mm 2.8mm' : '1.4mm 2mm' }};
            display: flex;
            flex-direction: column;
            justify-content: space-between;
            @if($printerMode === 'label_printer')
                page-break-after: always;
                break-after: page;
            @else
                page-break-inside: avoid;
                break-inside: avoid;
            @endif
            overflow: hidden;
        }
        .sticker-head {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 1.5mm;
            border-bottom: {{ $template === 'minimal' ? 'none' : '0.3mm solid #000' }};
            padding-bottom: 0.5mm;
            font-size: {{ $ptHeader }}pt;
            font-weight: {{ $fwHeader }};
            text-transform: uppercase;
            @if($template === 'bold')
                background: #000;
                color: #fff;
                padding: 0.7mm 1.2mm;
                margin: -1.4mm -2mm 0.6mm -2mm;
                border-bottom: none;
            @endif
        }
        .sticker-patient {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 1mm;
            font-size: {{ $ptPatient }}pt;
            font-weight: {{ $fwPatient }};
            margin-top: 0.4mm;
        }
        .sticker-product {
            font-size: {{ $ptProduct }}pt;
            font-weight: {{ $fwProduct }};
            line-height: 1.15;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            margin: 0.5mm 0;
        }
        .sticker-dosage-box {
            border: {{ $template === 'minimal' ? '0.25mm solid #cbd5e1' : ($template === 'urdu_focus' ? '0.45mm solid #000' : '0.35mm solid #000') }};
            border-radius: 1mm;
            padding: {{ $template === 'urdu_focus' ? '1.1mm 1.4mm' : '0.7mm 1.2mm' }};
            text-align: center;
            font-size: {{ $ptDosage }}pt;
            font-weight: {{ $fwDosage }};
            line-height: 1.25;
            background: {{ $template === 'minimal' ? '#fff' : '#f8fafc' }};
        }
        .sticker-foot {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 1mm;
            font-size: {{ $ptFooter }}pt;
            font-weight: {{ $fwFooter }};
            margin-top: 0.4mm;
            white-space: nowrap;
            overflow: hidden;
        }
        @media print {
            body {
                background: #fff;
            }
            .no-print-bar {
                display: none !important;
            }
            @if($printerMode === 'receipt_roll')
            .labels-wrapper {
                display: block;
                width: {{ $w }}mm;
                margin: 0 auto;
                padding: 0;
            }
            .sticker {
                border: none;
                border-bottom: 0.35mm dashed #94a3b8;
                margin: 0 0 2mm 0;
            }
            .sticker:last-child {
                border-bottom: none;
                margin-bottom: 0;
            }
            @else
            .labels-wrapper {
                display: block;
                padding: 0;
            }
            .sticker {
                border: none;
                margin: 0;
            }
            @endif
        }
    </style>
</head>
<body>
    <div class="no-print-bar">
        <div>
            <strong>Patient Dosage Stickers ({{ $w }}mm × {{ $h }}mm)</strong>
            <span style="margin-left:8px;opacity:0.8;">Invoice: {{ $invoice->posted_number }}</span>
        </div>
        <div class="no-print-actions">
            <a href="{{ request()->fullUrlWithQuery(['printer_mode' => 'receipt_roll', 'auto_print' => 0]) }}"
               class="mode-btn {{ $printerMode === 'receipt_roll' ? 'active' : '' }}">
                Receipt Roll (Upright)
            </a>
            <a href="{{ request()->fullUrlWithQuery(['printer_mode' => 'label_printer', 'auto_print' => 0]) }}"
               class="mode-btn {{ $printerMode === 'label_printer' ? 'active' : '' }}">
                Die-Cut Label Roll
            </a>
            <button type="button" onclick="window.print()">Print Stickers</button>
        </div>
    </div>

    <div class="labels-wrapper">
        @php
            $storeName = $setting->store_name ?? 'Pharmacy';
            $patientName = $invoice->patient_name ?: ($invoice->customer?->name ?? 'Walk-in Patient');
            $invDate = $invoice->date ? \Carbon\Carbon::parse($invoice->date)->format('d/m/Y') : now()->format('d/m/Y');
        @endphp

        @foreach($invoice->items as $item)
            @php
                $prodName = $item->product?->name ?? 'Medicine';
                $dosage = trim((string) ($item->dosage_instruction ?: ($item->product?->default_dosage ?? '')));
                if ($dosage === '') {
                    $dosage = '1 + 0 + 1 • کھانے کے بعد (After Meal)';
                }
                $qty = (int) ($item->quantity ?? $item->unit_quantity ?? 1);
            @endphp
            <div class="sticker">
                <div>
                    @if($showShop || $showInvMeta)
                        <div class="sticker-head">
                            <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">
                                {{ $showShop ? $storeName : '' }}
                            </span>
                            <span style="white-space:nowrap;">
                                {{ $showInvMeta ? ($invoice->posted_number . ' • ' . $invDate) : '' }}
                            </span>
                        </div>
                    @endif

                    @if($showPatient || $showQty || $showDoctor)
                        <div class="sticker-patient">
                            <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">
                                @if($showPatient) Pt: {{ $patientName }} @endif
                                @if($showDoctor && !empty($invoice->doctor_name))
                                    {{ $showPatient ? '• ' : '' }}Dr: {{ $invoice->doctor_name }}
                                @endif
                            </span>
                            @if($showQty)
                                <span style="white-space:nowrap;">Qty: {{ $qty }}</span>
                            @endif
                        </div>
                    @endif

                    <div class="sticker-product">{{ $prodName }}</div>
                </div>

                <div class="sticker-dosage-box" dir="auto">
                    {{ $dosage }}
                </div>

                <div class="sticker-foot">
                    <span>
                        @if($showBatch && !empty($item->batch_number))
                            B#: {{ $item->batch_number }}
                        @endif
                        @if($showExpiry && !empty($item->expiry))
                            {{ ($showBatch && !empty($item->batch_number)) ? '• ' : '' }}Exp: {{ $item->expiry }}
                        @endif
                    </span>
                    @if($footerNote !== '')
                        <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">{{ $footerNote }}</span>
                    @endif
                </div>
            </div>
        @endforeach
    </div>

    @if(request()->boolean('auto_print', true))
    <script>
        window.addEventListener('load', function () {
            setTimeout(function () {
                window.print();
            }, 250);
        });
    </script>
    @endif
</body>
</html>
