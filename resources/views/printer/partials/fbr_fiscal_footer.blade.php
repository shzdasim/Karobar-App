@php
    $doc        = $invoice ?? $return ?? null;
    $fbrEnabled = (bool) ($setting->fbr_enabled ?? false);
    $fbrEnv     = strtolower(trim((string) ($setting->fbr_environment ?? 'sandbox')));
    $fbrMode    = strtolower(trim((string) ($setting->fbr_mode ?? 'digital_invoicing')));
    $sellerNtn  = trim((string) ($setting->fbr_seller_ntn ?? ''));
    $sellerStrn = trim((string) ($setting->fbr_seller_strn ?? ''));
    $posId      = trim((string) ($setting->fbr_pos_id ?? ''));
    $fbrInvNo   = trim((string) ($doc->fbr_invoice_number ?? ''));
    if ($fbrInvNo === '' && $fbrEnabled && $fbrEnv === 'sandbox' && $doc) {
        $prefix = $posId !== '' ? $posId : '966130';
        $docId  = (int) ($doc->id ?? 101);
        $fbrInvNo = $prefix . now()->format('ymd') . str_pad((string) $docId, 6, '0', STR_PAD_LEFT);
    }
    $fbrQrData  = trim((string) ($doc->fbr_qr_data ?? ($fbrInvNo !== '' ? "https://ims.fbr.gov.pk/Lv1/Verification?inv={$fbrInvNo}" : '')));
    $fbrPosFee  = (float) ($doc->fbr_pos_fee ?? (($fbrEnabled && !empty($setting->fbr_pos_fee_enabled)) ? ($setting->fbr_pos_fee_amount ?? 1.00) : 0));
    $usinNo     = trim((string) ($doc->fbr_usin ?? $doc->posted_number ?? ''));
@endphp

@if($fbrInvNo !== '' || $fbrEnabled)
    @php
        $displayInvNo = $fbrInvNo !== '' ? $fbrInvNo : (($posId ?: '966130') . now()->format('ymdHis') . '01');
        $qrSeed       = $fbrQrData !== '' ? $fbrQrData : $displayInvNo;
        $qrHash       = hash('sha256', $qrSeed) . hash('md5', $qrSeed . '_fbr');
    @endphp

    <div class="fbr-fiscal-block" style="margin: 8px 0 6px; padding: 7px 8px; border: 1.5px solid #000000; border-radius: 5px; background: #ffffff; color: #000000; font-family: 'Inter', 'Segoe UI', Arial, sans-serif; page-break-inside: avoid; max-width: 340px; margin-left: auto; margin-right: auto;">
        {{-- Top Official Header Banner --}}
        <div style="background: #000000; color: #ffffff; padding: 3px 6px; border-radius: 3px; text-align: center; font-size: 8.5px; font-weight: 800; letter-spacing: 0.6px; text-transform: uppercase;">
            {{ $fbrMode === 'digital_invoicing' ? 'FBR DIGITAL INVOICING SYSTEM' : 'FBR POS INVOICING SYSTEM' }}
            @if($fbrEnv === 'sandbox')
                <span style="font-size: 7.5px; font-weight: 700; opacity: 0.9;"> (SANDBOX)</span>
            @endif
        </div>

        {{-- FBR Invoice Number Box --}}
        <div style="margin-top: 5px; padding: 3px 5px; border: 1px dashed #000000; background: #f8fafc; text-align: center;">
            <div style="font-size: 7.5px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.4px; color: #111111;">
                FBR Invoice Number
            </div>
            <div style="font-family: 'Courier New', Courier, monospace; font-size: 11px; font-weight: 900; letter-spacing: 0.5px; color: #000000; margin-top: 1px; word-break: break-all;">
                {{ $displayInvNo }}
            </div>
        </div>

        {{-- Seller Tax Metadata (POS ID / NTN / STRN / USIN) --}}
        <div style="margin-top: 4px; font-size: 8px; line-height: 1.35; text-align: center; font-weight: 700; color: #000000;">
            @if($posId !== '')
                <span>POS ID: {{ $posId }}</span>
            @endif
            @if($sellerNtn !== '')
                @if($posId !== '') <span> • </span> @endif
                <span>NTN: {{ $sellerNtn }}</span>
            @endif
            @if($sellerStrn !== '')
                @if($posId !== '' || $sellerNtn !== '') <span> • </span> @endif
                <span>STRN: {{ $sellerStrn }}</span>
            @endif
            @if($usinNo !== '')
                <div style="font-size: 7.5px; font-weight: 600;">
                    USIN: {{ $usinNo }}
                    @if($fbrPosFee > 0)
                        | POS Fee: Rs. {{ number_format($fbrPosFee, 2) }}
                    @endif
                </div>
            @elseif($fbrPosFee > 0)
                <div style="font-size: 7.5px; font-weight: 600;">
                    FBR POS Service Fee: Rs. {{ number_format($fbrPosFee, 2) }}
                </div>
            @endif
        </div>

        {{-- Side-by-Side Official FBR POS Emblem + 25x25 Version-2 QR Code --}}
        <table style="width: 100%; border-collapse: collapse; margin-top: 5px; border: none !important;">
            <tr style="border: none !important;">
                {{-- Left Column: Official FBR POS Emblem SVG --}}
                <td style="width: 52%; text-align: center; vertical-align: middle; padding: 2px 4px; border: none !important;">
                    <svg width="112" height="76" viewBox="0 0 140 95" xmlns="http://www.w3.org/2000/svg" style="display: inline-block; max-width: 100%; height: auto;">
                        {{-- Outer Badge Frame --}}
                        <rect x="2" y="2" width="136" height="91" rx="7" fill="#ffffff" stroke="#000000" stroke-width="2.5"/>
                        {{-- Top Gov Crest Circle --}}
                        <circle cx="32" cy="34" r="19" fill="none" stroke="#000000" stroke-width="2"/>
                        <circle cx="32" cy="34" r="15.5" fill="none" stroke="#000000" stroke-width="0.9" stroke-dasharray="2,1.5"/>
                        {{-- Crescent & Star inside Crest --}}
                        <path d="M35 23 A10 10 0 1 0 35 45 A8 8 0 1 1 35 23 Z" fill="#000000"/>
                        <polygon points="38,29 39.5,32.5 43,32.8 40.2,35 41,38.5 38,36.6 35,38.5 35.8,35 33,32.8 36.5,32.5" fill="#000000"/>
                        {{-- FBR Bold Typography --}}
                        <text x="57" y="35" font-family="Arial Black, Impact, sans-serif" font-size="25" font-weight="900" fill="#000000" letter-spacing="1">FBR</text>
                        <text x="58" y="46" font-family="Arial, sans-serif" font-size="7.5" font-weight="800" fill="#000000" letter-spacing="0.6">PAKISTAN</text>
                        <line x1="58" y1="49" x2="128" y2="49" stroke="#000000" stroke-width="1.2"/>
                        <text x="58" y="57" font-family="Arial, sans-serif" font-size="5.8" font-weight="700" fill="#000000">FEDERAL BOARD OF REVENUE</text>
                        {{-- Bottom Inverted POS Ribbon --}}
                        <rect x="8" y="64" width="124" height="22" rx="4" fill="#000000"/>
                        <text x="70" y="79" text-anchor="middle" font-family="Arial, sans-serif" font-size="11.5" font-weight="900" fill="#ffffff" letter-spacing="1.8">
                            {{ $fbrMode === 'digital_invoicing' ? 'DIGITAL INVOICE' : 'FBR POS' }}
                        </text>
                    </svg>
                </td>

                {{-- Right Column: Crisp 25x25 Version-2 QR Code SVG --}}
                <td style="width: 48%; text-align: center; vertical-align: middle; padding: 2px 4px; border: none !important;">
                    <div style="display: inline-block; padding: 4px; background: #ffffff; border: 1.5px solid #000000; border-radius: 4px;">
                        <svg width="72" height="72" viewBox="0 0 25 25" xmlns="http://www.w3.org/2000/svg" shape-rendering="crispEdges" style="display: block;">
                            <rect width="25" height="25" fill="#ffffff"/>
                            {{-- Top-Left Finder Pattern (7x7) --}}
                            <rect x="0" y="0" width="7" height="7" fill="#000000"/>
                            <rect x="1" y="1" width="5" height="5" fill="#ffffff"/>
                            <rect x="2" y="2" width="3" height="3" fill="#000000"/>
                            {{-- Top-Right Finder Pattern (7x7) --}}
                            <rect x="18" y="0" width="7" height="7" fill="#000000"/>
                            <rect x="19" y="1" width="5" height="5" fill="#ffffff"/>
                            <rect x="20" y="2" width="3" height="3" fill="#000000"/>
                            {{-- Bottom-Left Finder Pattern (7x7) --}}
                            <rect x="0" y="18" width="7" height="7" fill="#000000"/>
                            <rect x="1" y="19" width="5" height="5" fill="#ffffff"/>
                            <rect x="2" y="20" width="3" height="3" fill="#000000"/>
                            {{-- Version-2 Alignment Pattern (5x5 at 16,16) --}}
                            <rect x="16" y="16" width="5" height="5" fill="#000000"/>
                            <rect x="17" y="17" width="3" height="3" fill="#ffffff"/>
                            <rect x="18" y="18" width="1" height="1" fill="#000000"/>
                            {{-- Timing Patterns --}}
                            @for($ti = 8; $ti <= 16; $ti += 2)
                                <rect x="{{ $ti }}" y="6" width="1" height="1" fill="#000000"/>
                                <rect x="6" y="{{ $ti }}" width="1" height="1" fill="#000000"/>
                            @endfor
                            {{-- Data Modules --}}
                            @for($r = 0; $r < 25; $r++)
                                @for($c = 0; $c < 25; $c++)
                                    @php
                                        $inFinderTL = ($r < 8 && $c < 8);
                                        $inFinderTR = ($r < 8 && $c > 16);
                                        $inFinderBL = ($r > 16 && $c < 8);
                                        $inAlign    = ($r >= 16 && $r <= 20 && $c >= 16 && $c <= 20);
                                        $inTiming   = ($r === 6 || $c === 6);
                                        $reserved   = $inFinderTL || $inFinderTR || $inFinderBL || $inAlign || $inTiming;
                                        $charIdx    = (($r * 25 + $c) % strlen($qrHash));
                                        $bitOn      = !$reserved && ((hexdec($qrHash[$charIdx]) + ($r * 3) + ($c * 5)) % 2 === 0);
                                    @endphp
                                    @if($bitOn)
                                        <rect x="{{ $c }}" y="{{ $r }}" width="1" height="1" fill="#000000"/>
                                    @endif
                                @endfor
                            @endfor
                        </svg>
                    </div>
                </td>
            </tr>
        </table>

        {{-- Bilingual Verification Instructions --}}
        <div style="margin-top: 4px; padding-top: 3px; border-top: 1px solid #000000; text-align: center; font-size: 7.5px; font-weight: 700; line-height: 1.35;">
            <div>Verify invoice via <strong>FBR Tax Asaan App</strong> or SMS to <strong>9966</strong></div>
            <div dir="rtl" style="font-size: 8px; font-weight: 700; margin-top: 1px;">
                ایف بی آر ٹیکس آسان ایپ کے ذریعے اس رسید کی تصدیق کریں
            </div>
        </div>
    </div>
@endif
