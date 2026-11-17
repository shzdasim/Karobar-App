<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    @php
        $cfg = $stickerConfig ?? \App\Models\Setting::defaultDosageStickerConfig();
        $w = max(25, min(150, (int) ($cfg['width_mm'] ?? 50)));
        $h = max(15, min(120, (int) ($cfg['height_mm'] ?? 25)));
        $template = $cfg['template'] ?? 'classic';
        $fontScale = $cfg['font_scale'] ?? 'normal';
        $scaleMult = $fontScale === 'compact' ? 0.9 : ($fontScale === 'large' ? 1.15 : 1.0);

        $ptHeader  = round(max(4.0, min(28.0, (float) ($cfg['font_size_header'] ?? 6.5))) * $scaleMult, 2);
        $ptPatient = round(max(4.0, min(28.0, (float) ($cfg['font_size_patient'] ?? 6.0))) * $scaleMult, 2);
        $ptProduct = round(max(4.0, min(28.0, (float) ($cfg['font_size_product'] ?? ($template === 'urdu_focus' ? 7.2 : 8.0)))) * $scaleMult, 2);
        $ptDosage  = round(max(4.0, min(28.0, (float) ($cfg['font_size_dosage'] ?? ($template === 'urdu_focus' ? 8.6 : 7.5)))) * $scaleMult, 2);
        $ptFooter  = round(max(4.0, min(28.0, (float) ($cfg['font_size_footer'] ?? 5.5))) * $scaleMult, 2);

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
        @page {
            size: {{ $w }}mm {{ $h }}mm;
            margin: 0;
        }
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
            font-size: 13px;
            margin-bottom: 12px;
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
            page-break-after: always;
            break-after: page;
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
            font-weight: 800;
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
            font-weight: 600;
            margin-top: 0.4mm;
        }
        .sticker-product {
            font-size: {{ $ptProduct }}pt;
            font-weight: 900;
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
            font-weight: 800;
            line-height: 1.25;
            background: {{ $template === 'minimal' ? '#fff' : '#f8fafc' }};
        }
        .sticker-foot {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 1mm;
            font-size: {{ $ptFooter }}pt;
            font-weight: 700;
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
            .labels-wrapper {
                display: block;
                padding: 0;
            }
            .sticker {
                border: none;
                margin: 0;
            }
        }
    </style>
</head>
<body>
    <div class="no-print-bar">
        <div>
            <strong>Patient Dosage Thermal Stickers ({{ $w }}mm × {{ $h }}mm)</strong>
            <span style="margin-left:8px;opacity:0.8;">Invoice: {{ $invoice->posted_number }}</span>
        </div>
        <button type="button" onclick="window.print()">Print Stickers</button>
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
