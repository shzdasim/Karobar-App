// resources/js/components/settings/PharmacyClinicalSetting.jsx
import { useMemo } from "react";
import Select from "react-select";
import toast from "react-hot-toast";
import SettingSwitch from "@/components/settings/SettingSwitch.jsx";
import { GlassInput } from "@/components/Glass.jsx";
import { BeakerIcon, TagIcon, PrinterIcon } from "@heroicons/react/24/solid";

const STICKER_SIZE_PRESETS = [
  { value: "72x35", label: "72 × 35 mm (72mm Thermal Roll)", width_mm: 72, height_mm: 35 },
  { value: "72x40", label: "72 × 40 mm (72mm Thermal Roll - Tall)", width_mm: 72, height_mm: 40 },
  { value: "50x25", label: "50 × 25 mm (Standard Label Roll)", width_mm: 50, height_mm: 25 },
  { value: "50x30", label: "50 × 30 mm (Medium Thermal)", width_mm: 50, height_mm: 30 },
  { value: "60x40", label: "60 × 40 mm (Large Box Label)", width_mm: 60, height_mm: 40 },
  { value: "70x35", label: "70 × 35 mm (Wide Pharmacy Label)", width_mm: 70, height_mm: 35 },
  { value: "80x50", label: "80 × 50 mm (Jumbo Bottle / Syrup)", width_mm: 80, height_mm: 50 },
  { value: "38x25", label: "38 × 25 mm (Compact Vial / Strip)", width_mm: 38, height_mm: 25 },
  { value: "custom", label: "Custom Size (W × H mm)", width_mm: null, height_mm: null },
];

const STICKER_PRINTER_MODE_OPTIONS = [
  { value: "receipt_roll", label: "Thermal Receipt Roll (72mm / 80mm — Upright)" },
  { value: "label_printer", label: "Die-Cut Label Printer (Exact W × H)" },
];

const STICKER_TEMPLATE_OPTIONS = [
  { value: "classic", label: "Classic Bordered" },
  { value: "minimal", label: "Minimal Clean" },
  { value: "bold", label: "Bold Dark Header" },
  { value: "urdu_focus", label: "Urdu-First (Large Dosage Box)" },
];

const STICKER_FONT_OPTIONS = [
  { value: "compact", label: "Compact" },
  { value: "normal", label: "Normal (Default)" },
  { value: "large", label: "Large" },
];

const STICKER_WEIGHT_OPTIONS = [
  { value: 400, label: "Regular (400)" },
  { value: 500, label: "Medium (500)" },
  { value: 600, label: "Semi-Bold (600)" },
  { value: 700, label: "Bold (700)" },
  { value: 800, label: "Extra Bold (800)" },
  { value: 900, label: "Black (900)" },
];

export const DEFAULT_STICKER_CONFIG = {
  preset_key: "72x35",
  width_mm: 72,
  height_mm: 35,
  printer_mode: "receipt_roll",
  template: "classic",
  font_scale: "normal",
  font_size_header: 9.0,
  font_size_patient: 8.5,
  font_size_product: 11.0,
  font_size_dosage: 10.5,
  font_size_footer: 8.0,
  font_weight_header: 800,
  font_weight_patient: 700,
  font_weight_product: 900,
  font_weight_dosage: 800,
  font_weight_footer: 700,
  show_shop_name: true,
  show_invoice_meta: true,
  show_patient: true,
  show_quantity: true,
  show_batch: true,
  show_expiry: true,
  show_doctor: false,
  footer_note: "Keep out of reach of children",
};

function escapeHtml(str) {
  return String(str ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function openSampleStickerPrint(config, storeName) {
  const win = window.open("", "_blank", "width=520,height=640");
  if (!win) {
    toast.error("Pop-up blocked. Please allow pop-ups to print a sample sticker.");
    return;
  }

  const w = Math.max(25, Math.min(150, Number(config.width_mm || 72)));
  const h = Math.max(15, Math.min(120, Number(config.height_mm || 35)));
  const printerMode = config.printer_mode === "label_printer" ? "label_printer" : "receipt_roll";
  const template = config.template || "classic";
  const scaleMult =
    config.font_scale === "compact" ? 0.9 : config.font_scale === "large" ? 1.15 : 1.0;

  const pt = (val, def) =>
    Number((Math.max(4, Math.min(28, Number(val ?? def))) * scaleMult).toFixed(2));

  const ptHeader = pt(config.font_size_header, 9.0);
  const ptPatient = pt(config.font_size_patient, 8.5);
  const ptProduct = pt(config.font_size_product, 11.0);
  const ptDosage = pt(config.font_size_dosage, 10.5);
  const ptFooter = pt(config.font_size_footer, 8.0);

  const fwHeader = Number(config.font_weight_header || 800);
  const fwPatient = Number(config.font_weight_patient || 700);
  const fwProduct = Number(config.font_weight_product || 900);
  const fwDosage = Number(config.font_weight_dosage || 800);
  const fwFooter = Number(config.font_weight_footer || 700);

  const pageCss =
    printerMode === "label_printer"
      ? `@page { size: ${w}mm ${h}mm; margin: 0; }`
      : `@page { margin: 0; }`;

  const headBlock =
    config.show_shop_name || config.show_invoice_meta
      ? `<div class="sticker-head">
          <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${
            config.show_shop_name ? escapeHtml(storeName || "Karobar Pharmacy") : ""
          }</span>
          <span style="white-space:nowrap;">${
            config.show_invoice_meta ? "SI-00042 • 17/11/2026" : ""
          }</span>
        </div>`
      : "";

  const patientBlock =
    config.show_patient || config.show_quantity || config.show_doctor
      ? `<div class="sticker-patient">
          <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">
            ${config.show_patient ? "Pt: Ahmed Khan" : ""}
            ${config.show_doctor ? `${config.show_patient ? " • " : ""}Dr: Tariq` : ""}
          </span>
          ${config.show_quantity ? `<span style="white-space:nowrap;">Qty: 20</span>` : ""}
        </div>`
      : "";

  const footerLeft = [
    config.show_batch ? "B#: B2409" : "",
    config.show_expiry ? "Exp: 12/2027" : "",
  ]
    .filter(Boolean)
    .join(" • ");

  win.document.open();
  win.document.write(`<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>Sample Dosage Sticker (${w}×${h}mm)</title>
  <style>
    ${pageCss}
    * { box-sizing: border-box; margin: 0; padding: 0; }
    html, body { margin: 0; padding: 0; background: #f1f5f9; color: #000; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Noto Nastaliq Urdu", Arial, sans-serif;
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
      font-size: 12.5px;
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
      padding-bottom: 16px;
    }
    .sticker {
      width: ${w}mm;
      max-width: 100%;
      ${printerMode === "label_printer" ? `height: ${h}mm;` : `min-height: ${h}mm; height: auto;`}
      background: #fff;
      border: 1px dashed #94a3b8;
      padding: ${h >= 35 ? "2.2mm 2.8mm" : "1.6mm 2mm"};
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      overflow: hidden;
    }
    .sticker-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1.5mm;
      border-bottom: ${template === "minimal" ? "none" : "0.3mm solid #000"};
      padding-bottom: 0.5mm;
      font-size: ${ptHeader}pt;
      font-weight: ${fwHeader};
      text-transform: uppercase;
      ${
        template === "bold"
          ? "background: #000; color: #fff; padding: 0.7mm 1.2mm; margin: -1.6mm -2mm 0.6mm -2mm; border-bottom: none;"
          : ""
      }
    }
    .sticker-patient {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1mm;
      font-size: ${ptPatient}pt;
      font-weight: ${fwPatient};
      margin-top: 0.4mm;
    }
    .sticker-product {
      font-size: ${ptProduct}pt;
      font-weight: ${fwProduct};
      line-height: 1.15;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      margin: 0.6mm 0;
    }
    .sticker-dosage-box {
      border: ${template === "minimal" ? "0.25mm solid #cbd5e1" : "0.35mm solid #000"};
      border-radius: 1mm;
      padding: ${template === "urdu_focus" ? "1.1mm 1.4mm" : "0.8mm 1.2mm"};
      text-align: center;
      font-size: ${ptDosage}pt;
      font-weight: ${fwDosage};
      line-height: 1.25;
      background: ${template === "minimal" ? "#fff" : "#f8fafc"};
    }
    .sticker-foot {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1mm;
      font-size: ${ptFooter}pt;
      font-weight: ${fwFooter};
      margin-top: 0.5mm;
      white-space: nowrap;
      overflow: hidden;
    }
    @media print {
      html, body { background: #fff; margin: 0; padding: 0; }
      .no-print-bar { display: none !important; }
      .labels-wrapper { display: block; width: 100%; max-width: ${w}mm; margin: 0; padding: 0; }
      .sticker { width: 100%; max-width: ${w}mm; border: none; margin: 0; }
    }
  </style>
</head>
<body>
  <div class="no-print-bar">
    <div><strong>Sample Sticker (${w}mm × ${h}mm)</strong> — ${
      printerMode === "receipt_roll" ? "Receipt Roll (Upright)" : "Die-Cut Label Roll"
    }</div>
    <button type="button" onclick="window.print()">Print Sample</button>
  </div>
  <div class="labels-wrapper">
    <div class="sticker">
      <div>
        ${headBlock}
        ${patientBlock}
        <div class="sticker-product">Tab. Panadol Extra 500mg</div>
      </div>
      <div class="sticker-dosage-box" dir="auto">1 + 0 + 1 • کھانے کے بعد (After Meal)</div>
      <div class="sticker-foot">
        <span>${escapeHtml(footerLeft)}</span>
        ${
          config.footer_note
            ? `<span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${escapeHtml(
                config.footer_note
              )}</span>`
            : ""
        }
      </div>
    </div>
  </div>
  <script>window.addEventListener('load', () => setTimeout(() => window.print(), 250));</script>
</body>
</html>`);
  win.document.close();
}

export default function PharmacyClinicalSetting({ form, setForm, disableInputs = false }) {
  const config = {
    ...DEFAULT_STICKER_CONFIG,
    ...(form?.dosage_sticker_config || {}),
  };

  const updateConfig = (patch) =>
    setForm((s) => ({
      ...s,
      dosage_sticker_config: {
        ...DEFAULT_STICKER_CONFIG,
        ...(s?.dosage_sticker_config || {}),
        ...patch,
      },
    }));

  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        minHeight: "38px",
        borderRadius: 10,
        backgroundColor: "var(--color-surface, #ffffff)",
        borderColor: state.isFocused
          ? "var(--color-primary, #3b82f6)"
          : "var(--color-border, #cbd5e1)",
        boxShadow: "none",
        fontSize: "12.5px",
      }),
      singleValue: (base) => ({ ...base, color: "var(--color-text-primary, #0f172a)" }),
      menuPortal: (base) => ({ ...base, zIndex: 99999 }),
      menu: (base) => ({
        ...base,
        backgroundColor: "var(--color-surface, #ffffff)",
        border: "1px solid var(--color-border, #cbd5e1)",
        borderRadius: 10,
        fontSize: "12.5px",
      }),
      option: (base, state) => ({
        ...base,
        backgroundColor: state.isSelected
          ? "color-mix(in srgb, var(--color-primary, #3b82f6) 18%, var(--color-surface, #ffffff))"
          : state.isFocused
          ? "color-mix(in srgb, var(--color-primary, #3b82f6) 10%, var(--color-surface, #ffffff))"
          : "var(--color-surface, #ffffff)",
        color: "var(--color-text-primary, #0f172a)",
        cursor: "pointer",
      }),
    }),
    []
  );

  const selectedPreset =
    STICKER_SIZE_PRESETS.find((p) => p.value === config.preset_key) ||
    STICKER_SIZE_PRESETS.find(
      (p) => p.width_mm === Number(config.width_mm) && p.height_mm === Number(config.height_mm)
    ) ||
    STICKER_SIZE_PRESETS[STICKER_SIZE_PRESETS.length - 1];

  const selectedTemplate =
    STICKER_TEMPLATE_OPTIONS.find((t) => t.value === config.template) || STICKER_TEMPLATE_OPTIONS[0];
  const selectedFont =
    STICKER_FONT_OPTIONS.find((f) => f.value === config.font_scale) || STICKER_FONT_OPTIONS[1];
  const selectedPrinterMode =
    STICKER_PRINTER_MODE_OPTIONS.find((m) => m.value === config.printer_mode) ||
    STICKER_PRINTER_MODE_OPTIONS[0];

  const wMm = Math.max(25, Math.min(150, Number(config.width_mm || 72)));
  const hMm = Math.max(15, Math.min(120, Number(config.height_mm || 35)));
  const previewScale = Math.min(4.2, 300 / wMm);
  const previewW = Math.round(wMm * previewScale);
  const previewH = Math.round(hMm * previewScale);
  const scaleMult = config.font_scale === "compact" ? 0.9 : config.font_scale === "large" ? 1.15 : 1;

  const ptToPreviewPx = (pt, fallback) => {
    const num = Number(pt ?? fallback);
    const validPt = Number.isFinite(num) ? Math.max(4, Math.min(28, num)) : fallback;
    return Math.max(8, Math.round(validPt * scaleMult * 0.3528 * previewScale * 1.15));
  };

  const pxHeader = ptToPreviewPx(config.font_size_header, 9.0);
  const pxPatient = ptToPreviewPx(config.font_size_patient, 8.5);
  const pxProduct = ptToPreviewPx(config.font_size_product, 11.0);
  const pxDosage = ptToPreviewPx(config.font_size_dosage, 10.5);
  const pxFooter = ptToPreviewPx(config.font_size_footer, 8.0);

  const fwHeader = Number(config.font_weight_header || 800);
  const fwPatient = Number(config.font_weight_patient || 700);
  const fwProduct = Number(config.font_weight_product || 900);
  const fwDosage = Number(config.font_weight_dosage || 800);
  const fwFooter = Number(config.font_weight_footer || 700);

  const storeName = form?.store_name || "Karobar Pharmacy";

  return (
    <div className="space-y-5">
      {/* Optional Pharmacy Clinical Toggles */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <BeakerIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Optional Pharmacy Clinical Add-Ons</h2>
              <p className="settings-block-note">
                100% optional tools — enable only the clinical add-ons your pharmacy wants to use.
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div className="p-4 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-50/60 dark:bg-slate-800/40 flex items-center justify-between gap-4">
              <div>
                <div className="text-sm font-bold text-slate-800 dark:text-slate-100">
                  Patient Dosage &amp; Thermal Sticker Printer
                </div>
                <div className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                  Adds a "Dosage Stickers" button on the Sale Invoice view page for printing bilingual Urdu/English patient medicine instructions.
                </div>
              </div>
              <SettingSwitch
                checked={form?.enable_dosage_stickers !== false}
                disabled={disableInputs}
                onChange={(val) =>
                  setForm((s) => ({ ...s, enable_dosage_stickers: Boolean(val) }))
                }
              />
            </div>

            <div className="p-4 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-50/60 dark:bg-slate-800/40 flex items-center justify-between gap-4">
              <div>
                <div className="text-sm font-bold text-slate-800 dark:text-slate-100">
                  Prescription Photo / Scan Attachment
                </div>
                <div className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                  Adds a "Prescription Scan" button on the Sale Invoice view page to attach and view doctor prescription images or PDFs.
                </div>
              </div>
              <SettingSwitch
                checked={form?.enable_prescription_attachments !== false}
                disabled={disableInputs}
                onChange={(val) =>
                  setForm((s) => ({ ...s, enable_prescription_attachments: Boolean(val) }))
                }
              />
            </div>
          </div>
        </div>
      </div>

      {/* Dosage Sticker Designer */}
      {form?.enable_dosage_stickers !== false && (
        <div className="settings-block">
          <div className="settings-block-heading">
            <div className="flex items-center justify-between gap-3 flex-wrap w-full">
              <div className="flex items-center gap-3 min-w-0">
                <div className="settings-block-icon">
                  <TagIcon />
                </div>
                <div className="min-w-0">
                  <h2 className="settings-block-title">Dosage Sticker Designer &amp; Thermal Size</h2>
                  <p className="settings-block-note">
                    Customize roll width, printer feed mode, per-field font sizes, boldness, and visible fields.
                  </p>
                </div>
              </div>

              <div className="flex items-center gap-2">
                <span className="px-2.5 py-1 rounded-lg text-xs font-mono font-bold bg-blue-500/10 text-blue-600 dark:text-blue-300 border border-blue-500/20">
                  {wMm} mm × {hMm} mm
                </span>
                <button
                  type="button"
                  onClick={() => openSampleStickerPrint(config, storeName)}
                  className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold bg-blue-600 hover:bg-blue-500 text-white shadow-xs cursor-pointer"
                >
                  <PrinterIcon className="w-4 h-4" />
                  <span>Print Sample Sticker</span>
                </button>
              </div>
            </div>
          </div>

          <div className="settings-body">
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-5 items-start">
              {/* Controls */}
              <div className="lg:col-span-7 space-y-4">
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div className="sm:col-span-2">
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
                      Sticker Size Preset (Width × Height)
                    </label>
                    <Select
                      options={STICKER_SIZE_PRESETS}
                      value={selectedPreset}
                      isDisabled={disableInputs}
                      isSearchable={false}
                      styles={selectStyles}
                      menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                      onChange={(opt) => {
                        if (!opt) return;
                        if (opt.value === "custom") {
                          updateConfig({ preset_key: "custom" });
                        } else {
                          updateConfig({
                            preset_key: opt.value,
                            width_mm: opt.width_mm,
                            height_mm: opt.height_mm,
                          });
                        }
                      }}
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
                      Global Font Scale
                    </label>
                    <Select
                      options={STICKER_FONT_OPTIONS}
                      value={selectedFont}
                      isDisabled={disableInputs}
                      isSearchable={false}
                      styles={selectStyles}
                      menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                      onChange={(opt) => opt && updateConfig({ font_scale: opt.value })}
                    />
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div>
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
                      Width (mm)
                    </label>
                    <GlassInput
                      type="number"
                      min={25}
                      max={150}
                      disabled={disableInputs}
                      value={config.width_mm ?? 72}
                      onChange={(e) =>
                        updateConfig({
                          preset_key: "custom",
                          width_mm: e.target.value === "" ? "" : Number(e.target.value),
                        })
                      }
                      className="w-full text-xs font-mono"
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
                      Min Height (mm)
                    </label>
                    <GlassInput
                      type="number"
                      min={15}
                      max={120}
                      disabled={disableInputs}
                      value={config.height_mm ?? 35}
                      onChange={(e) =>
                        updateConfig({
                          preset_key: "custom",
                          height_mm: e.target.value === "" ? "" : Number(e.target.value),
                        })
                      }
                      className="w-full text-xs font-mono"
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
                      Sticker Template
                    </label>
                    <Select
                      options={STICKER_TEMPLATE_OPTIONS}
                      value={selectedTemplate}
                      isDisabled={disableInputs}
                      isSearchable={false}
                      styles={selectStyles}
                      menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                      onChange={(opt) => opt && updateConfig({ template: opt.value })}
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
                    Printer Feed Mode (Use &ldquo;Receipt Roll&rdquo; for 72mm / 80mm Thermal Printers)
                  </label>
                  <Select
                    options={STICKER_PRINTER_MODE_OPTIONS}
                    value={selectedPrinterMode}
                    isDisabled={disableInputs}
                    isSearchable={false}
                    styles={selectStyles}
                    menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                    onChange={(opt) => opt && updateConfig({ printer_mode: opt.value })}
                  />
                </div>

                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300">
                      Per-Field Font Size (pt) &amp; Boldness
                    </label>
                    <button
                      type="button"
                      disabled={disableInputs}
                      onClick={() =>
                        updateConfig({
                          font_size_header: 9.0,
                          font_size_patient: 8.5,
                          font_size_product: 11.0,
                          font_size_dosage: 10.5,
                          font_size_footer: 8.0,
                          font_weight_header: 800,
                          font_weight_patient: 700,
                          font_weight_product: 900,
                          font_weight_dosage: 800,
                          font_weight_footer: 700,
                        })
                      }
                      className="text-[11px] font-semibold text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
                    >
                      Reset Default Fonts
                    </button>
                  </div>

                  <div className="space-y-2">
                    {[
                      { key: "font_size_header", wKey: "font_weight_header", label: "Header (Store / Inv#)", def: 9.0, defW: 800 },
                      { key: "font_size_patient", wKey: "font_weight_patient", label: "Patient / Qty / Doctor", def: 8.5, defW: 700 },
                      { key: "font_size_product", wKey: "font_weight_product", label: "Medicine Name", def: 11.0, defW: 900 },
                      { key: "font_size_dosage", wKey: "font_weight_dosage", label: "Dosage Text", def: 10.5, defW: 800 },
                      { key: "font_size_footer", wKey: "font_weight_footer", label: "Footer / Batch / Exp", def: 8.0, defW: 700 },
                    ].map((fs) => {
                      const curW = Number(config[fs.wKey] ?? fs.defW);
                      const selectedWeight =
                        STICKER_WEIGHT_OPTIONS.find((w) => w.value === curW) || STICKER_WEIGHT_OPTIONS[3];
                      return (
                        <div
                          key={fs.key}
                          className="grid grid-cols-12 gap-2 items-center p-2 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-50/50 dark:bg-slate-800/30"
                        >
                          <div className="col-span-5 text-xs font-semibold text-slate-700 dark:text-slate-200 truncate">
                            {fs.label}
                          </div>
                          <div className="col-span-3">
                            <GlassInput
                              type="number"
                              step="0.5"
                              min={4}
                              max={28}
                              disabled={disableInputs}
                              value={config[fs.key] ?? fs.def}
                              onChange={(e) =>
                                updateConfig({
                                  [fs.key]: e.target.value === "" ? "" : Number(e.target.value),
                                })
                              }
                              className="w-full text-xs font-mono"
                              title="Font size in pt"
                            />
                          </div>
                          <div className="col-span-4">
                            <Select
                              options={STICKER_WEIGHT_OPTIONS}
                              value={selectedWeight}
                              isDisabled={disableInputs}
                              isSearchable={false}
                              styles={selectStyles}
                              menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                              onChange={(opt) => opt && updateConfig({ [fs.wKey]: opt.value })}
                            />
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>

                <div className="grid grid-cols-2 sm:grid-cols-3 gap-2.5">
                  {[
                    { key: "show_shop_name", label: "Store Name" },
                    { key: "show_invoice_meta", label: "Invoice # & Date" },
                    { key: "show_patient", label: "Patient Name" },
                    { key: "show_quantity", label: "Quantity" },
                    { key: "show_batch", label: "Batch #" },
                    { key: "show_expiry", label: "Expiry Date" },
                    { key: "show_doctor", label: "Doctor Name" },
                  ].map((fld) => (
                    <div
                      key={fld.key}
                      className="px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-50/60 dark:bg-slate-800/40 flex items-center justify-between gap-2"
                    >
                      <span className="text-xs font-medium text-slate-700 dark:text-slate-200">
                        {fld.label}
                      </span>
                      <SettingSwitch
                        checked={Boolean(config[fld.key])}
                        disabled={disableInputs}
                        onChange={(val) => updateConfig({ [fld.key]: Boolean(val) })}
                      />
                    </div>
                  ))}
                </div>

                <div>
                  <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
                    Footer Note (Optional)
                  </label>
                  <GlassInput
                    type="text"
                    maxLength={120}
                    disabled={disableInputs}
                    value={config.footer_note ?? ""}
                    onChange={(e) => updateConfig({ footer_note: e.target.value })}
                    placeholder="Keep out of reach of children"
                    className="w-full text-xs"
                  />
                </div>
              </div>

              {/* Live Scaled Preview */}
              <div className="lg:col-span-5 p-4 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-100/70 dark:bg-slate-900/60 flex flex-col items-center justify-center gap-3 sticky top-4">
                <div className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
                  Live Sticker Preview ({wMm}mm × {hMm}mm)
                </div>

                <div
                  style={{
                    width: `${previewW}px`,
                    minHeight: `${previewH}px`,
                    padding: hMm >= 35 ? "10px 12px" : "6px 8px",
                  }}
                  className="bg-white text-slate-950 border border-dashed border-slate-400 shadow-sm flex flex-col justify-between overflow-hidden rounded-xs"
                >
                  <div>
                    {(config.show_shop_name || config.show_invoice_meta) && (
                      <div
                        style={{ fontSize: `${pxHeader}px`, fontWeight: fwHeader }}
                        className={`flex items-center justify-between gap-2 uppercase leading-tight ${
                          config.template === "bold"
                            ? "bg-slate-950 text-white px-1.5 py-0.5 -mx-1.5 -mt-1 mb-1"
                            : config.template === "minimal"
                            ? "pb-0.5"
                            : "border-b border-slate-900 pb-0.5"
                        }`}
                      >
                        <span className="truncate">{config.show_shop_name ? storeName : ""}</span>
                        <span className="shrink-0">
                          {config.show_invoice_meta ? "SI-00042 • 17/11/26" : ""}
                        </span>
                      </div>
                    )}

                    {(config.show_patient || config.show_quantity || config.show_doctor) && (
                      <div
                        style={{ fontSize: `${pxPatient}px`, fontWeight: fwPatient }}
                        className="flex items-center justify-between gap-2 mt-0.5 leading-tight"
                      >
                        <span className="truncate">
                          {config.show_patient ? "Pt: Ahmed Khan" : ""}
                          {config.show_doctor ? `${config.show_patient ? " • " : ""}Dr: Tariq` : ""}
                        </span>
                        {config.show_quantity && <span className="shrink-0">Qty: 20</span>}
                      </div>
                    )}

                    <div
                      style={{ fontSize: `${pxProduct}px`, fontWeight: fwProduct }}
                      className="truncate mt-0.5 leading-tight"
                    >
                      Tab. Panadol Extra 500mg
                    </div>
                  </div>

                  <div
                    dir="auto"
                    style={{ fontSize: `${pxDosage}px`, fontWeight: fwDosage }}
                    className={`text-center rounded px-1.5 py-1 leading-snug my-1 ${
                      config.template === "minimal"
                        ? "border border-slate-300 bg-white"
                        : "border border-slate-900 bg-slate-50"
                    }`}
                  >
                    1 + 0 + 1 • کھانے کے بعد (After Meal)
                  </div>

                  <div
                    style={{ fontSize: `${pxFooter}px`, fontWeight: fwFooter }}
                    className="flex items-center justify-between gap-2 leading-none"
                  >
                    <span className="truncate">
                      {config.show_batch ? "B#: B2409" : ""}
                      {config.show_expiry ? `${config.show_batch ? " • " : ""}Exp: 12/27` : ""}
                    </span>
                    {config.footer_note && (
                      <span className="truncate">{config.footer_note}</span>
                    )}
                  </div>
                </div>

                <button
                  type="button"
                  onClick={() => openSampleStickerPrint(config, storeName)}
                  className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl text-xs font-bold bg-slate-800 hover:bg-slate-700 text-white cursor-pointer"
                >
                  <PrinterIcon className="w-4 h-4" />
                  <span>Print Sample ({wMm}×{hMm}mm)</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
