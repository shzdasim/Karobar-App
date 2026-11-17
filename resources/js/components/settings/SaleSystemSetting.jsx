// resources/js/components/settings/SaleSystemSetting.jsx
import { useMemo } from "react";
import Select from "react-select";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import SettingSwitch from "@/components/settings/SettingSwitch.jsx";
import { GlassInput } from "@/components/Glass.jsx";
import {
  ShoppingCartIcon,
  BanknotesIcon,
  ScaleIcon,
  CheckIcon,
  CheckBadgeIcon,
  ArrowTrendingUpIcon,
  InformationCircleIcon,
  BeakerIcon,
  BuildingStorefrontIcon,
  TagIcon,
} from "@heroicons/react/24/solid";

const STICKER_SIZE_PRESETS = [
  { value: "38x25", label: "38 × 25 mm (Compact Vial / Strip)", width_mm: 38, height_mm: 25 },
  { value: "50x25", label: "50 × 25 mm (Standard Pharmacy Roll)", width_mm: 50, height_mm: 25 },
  { value: "50x30", label: "50 × 30 mm (Medium Thermal)", width_mm: 50, height_mm: 30 },
  { value: "60x40", label: "60 × 40 mm (Large Box Label)", width_mm: 60, height_mm: 40 },
  { value: "70x35", label: "70 × 35 mm (Wide Pharmacy Label)", width_mm: 70, height_mm: 35 },
  { value: "72x35", label: "72 × 35 mm (72mm Thermal Roll)", width_mm: 72, height_mm: 35 },
  { value: "72x40", label: "72 × 40 mm (72mm Thermal Roll - Tall)", width_mm: 72, height_mm: 40 },
  { value: "80x50", label: "80 × 50 mm (Jumbo Bottle / Syrup)", width_mm: 80, height_mm: 50 },
  { value: "custom", label: "Custom Size (W × H mm)", width_mm: null, height_mm: null },
];

const STICKER_PRINTER_MODE_OPTIONS = [
  { value: "receipt_roll", label: "Thermal Receipt Roll (72mm / 80mm — Upright)" },
  { value: "label_printer", label: "Die-Cut Label Printer (Exact W × H)" },
];

const STICKER_WEIGHT_OPTIONS = [
  { value: 400, label: "Regular (400)" },
  { value: 500, label: "Medium (500)" },
  { value: 600, label: "Semi-Bold (600)" },
  { value: 700, label: "Bold (700)" },
  { value: 800, label: "Extra Bold (800)" },
  { value: 900, label: "Black (900)" },
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

const DEFAULT_STICKER_CONFIG = {
  preset_key: "50x25",
  width_mm: 50,
  height_mm: 25,
  printer_mode: "receipt_roll",
  template: "classic",
  font_scale: "normal",
  font_size_header: 6.5,
  font_size_patient: 6.0,
  font_size_product: 8.0,
  font_size_dosage: 7.5,
  font_size_footer: 5.5,
  font_weight_header: 800,
  font_weight_patient: 600,
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

// Feature sets shown for each sale system mode
const FEATURES = {
  retail: {
    icon: BanknotesIcon,
    title: 'Retail Only',
    tagline: 'Simple counter sales — one price per product',
    description:
      'Best for walk-in / counter businesses. One selling price per product and a clean, fast sale invoice workflow with no wholesale clutter.',
    bullets: [
      'Single selling price for every product',
      'Clean, fast sale invoice entry',
      'No wholesale price columns (pack / unit / margin %)',
      'No wholesale actions on the sale invoice list',
      'Hides wholesale-only pages and routes',
    ],
  },
  retail_wholesale: {
    icon: ScaleIcon,
    title: 'Retail + Wholesale',
    tagline: 'Full functionality — retail and bulk sales together',
    description:
      'Best for businesses that also sell in bulk. Adds wholesale pricing, wholesale invoices and full wholesale customer support while keeping all retail features.',
    bullets: [
      'Everything in Retail Only',
      'Wholesale price per product — pack, unit & wholesale margin %',
      'Wholesale / bulk sale invoices',
      'Wholesale columns in purchase invoice entry for accurate stock & margin',
      'Full wholesale customer support with ledgers',
    ],
  },
};

// Shop type choices shown for each kind of store
const SHOP_TYPES = {
  pharmacy: {
    icon: BeakerIcon,
    title: 'Pharmacy',
    tagline: 'Medicine, controlled items & prescription sales',
    description:
      'Best for medical / pharmacy stores. Unlocks pharmacy-specific fields like product formulation, the narcotic / controlled-product flag, and Doctor & Patient name fields on sale invoices.',
    bullets: [
      'Formulation field on product entry',
      'Narcotic / controlled-product flag',
      'Doctor & Patient name fields on sale invoices',
      'Doctor / Patient shown in search & reports',
    ],
  },
  general_store: {
    icon: BuildingStorefrontIcon,
    title: 'General Store',
    tagline: 'Everyday retail goods — no pharmacy-specific fields',
    description:
      'Best for general / grocery / non-pharmacy stores. Hides pharmacy-only fields so product entry and sale invoices stay clean and focused on standard pricing and stock.',
    bullets: [
      'No formulation field on product entry',
      'No narcotic / controlled-product flag',
      'No Doctor / Patient fields on sale invoices',
      'Clean, generic sale invoice workflow',
    ],
  },
};

function ChoiceGroup({ name, legend, options, selectedKey, onSelect, disableInputs }) {
  return (
    <div className="settings-field">
      <span className="settings-label">{legend}</span>
      <div className="settings-options settings-options-2">
        {Object.entries(options).map(([key, option]) => {
          const Icon = option.icon;
          const isActive = selectedKey === key;
          return (
            <label key={key} className={`settings-option ${isActive ? "is-active" : ""}`}>
              <input
                type="radio"
                name={name}
                value={key}
                checked={isActive}
                onChange={onSelect}
                disabled={disableInputs}
                className="sr-only"
              />

              <span className="settings-option-head">
                <Icon aria-hidden="true" />
                <span className="settings-option-title">{option.title}</span>
                {isActive && (
                  <CheckIcon
                    aria-hidden="true"
                    style={{ color: "var(--workspace-accent)", marginLeft: "auto" }}
                  />
                )}
              </span>

              <span className="settings-option-tagline">{option.tagline}</span>
              <span className="settings-option-note">{option.description}</span>

              <ul className="settings-bullets">
                {option.bullets.map((bullet, i) => (
                  <li key={i}>
                    <CheckIcon aria-hidden="true" />
                    <span>{bullet}</span>
                  </li>
                ))}
              </ul>
            </label>
          );
        })}
      </div>
    </div>
  );
}

function DosageStickerDesigner({ config, storeName, disableInputs, onChange }) {
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

  const wMm = Math.max(25, Math.min(150, Number(config.width_mm || 50)));
  const hMm = Math.max(15, Math.min(120, Number(config.height_mm || 25)));
  const previewScale = Math.min(4.6, 290 / wMm);
  const previewW = Math.round(wMm * previewScale);
  const previewH = Math.round(hMm * previewScale);
  const scaleMult = config.font_scale === "compact" ? 0.9 : config.font_scale === "large" ? 1.15 : 1;
  // 1pt = 0.3528mm; convert exact pt sizes to scaled preview px
  const ptToPreviewPx = (pt, fallback) => {
    const num = Number(pt ?? fallback);
    const validPt = Number.isFinite(num) ? Math.max(4, Math.min(28, num)) : fallback;
    return Math.max(7, Math.round(validPt * scaleMult * 0.3528 * previewScale * 1.15));
  };
  const pxHeader = ptToPreviewPx(config.font_size_header, 6.5);
  const pxPatient = ptToPreviewPx(config.font_size_patient, 6.0);
  const pxProduct = ptToPreviewPx(config.font_size_product, 8.0);
  const pxDosage = ptToPreviewPx(config.font_size_dosage, 7.5);
  const pxFooter = ptToPreviewPx(config.font_size_footer, 5.5);

  const fwHeader = Number(config.font_weight_header || 800);
  const fwPatient = Number(config.font_weight_patient || 600);
  const fwProduct = Number(config.font_weight_product || 900);
  const fwDosage = Number(config.font_weight_dosage || 800);
  const fwFooter = Number(config.font_weight_footer || 700);

  return (
    <div className="mt-5 pt-5 border-t border-slate-200 dark:border-slate-700/80 space-y-4">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2">
          <TagIcon className="w-4 h-4 text-blue-500" />
          <div>
            <h3 className="text-sm font-bold text-slate-800 dark:text-slate-100">
              Dosage Sticker Designer &amp; Label Size
            </h3>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Choose your thermal roll dimensions (width × height in mm), template style, and visible fields.
            </p>
          </div>
        </div>
        <span className="px-2.5 py-1 rounded-lg text-xs font-mono font-bold bg-blue-500/10 text-blue-600 dark:text-blue-300 border border-blue-500/20">
          {wMm} mm × {hMm} mm
        </span>
      </div>

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
                    onChange({ preset_key: "custom" });
                  } else {
                    onChange({
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
                Font Size
              </label>
              <Select
                options={STICKER_FONT_OPTIONS}
                value={selectedFont}
                isDisabled={disableInputs}
                isSearchable={false}
                styles={selectStyles}
                menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                onChange={(opt) => opt && onChange({ font_scale: opt.value })}
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
                value={config.width_mm ?? 50}
                onChange={(e) =>
                  onChange({
                    preset_key: "custom",
                    width_mm: e.target.value === "" ? "" : Number(e.target.value),
                  })
                }
                className="w-full text-xs font-mono"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
                Height (mm)
              </label>
              <GlassInput
                type="number"
                min={15}
                max={120}
                disabled={disableInputs}
                value={config.height_mm ?? 25}
                onChange={(e) =>
                  onChange({
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
                onChange={(opt) => opt && onChange({ template: opt.value })}
              />
            </div>
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 mb-1">
              Printer Feed Mode (Fixes Sideways Printing on 72mm / 80mm Receipt Rolls)
            </label>
            <Select
              options={STICKER_PRINTER_MODE_OPTIONS}
              value={selectedPrinterMode}
              isDisabled={disableInputs}
              isSearchable={false}
              styles={selectStyles}
              menuPortalTarget={typeof document !== "undefined" ? document.body : null}
              onChange={(opt) => opt && onChange({ printer_mode: opt.value })}
            />
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
                  onChange={(val) => onChange({ [fld.key]: Boolean(val) })}
                />
              </div>
            ))}
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
                  onChange({
                    font_size_header: 6.5,
                    font_size_patient: 6.0,
                    font_size_product: 8.0,
                    font_size_dosage: 7.5,
                    font_size_footer: 5.5,
                    font_weight_header: 800,
                    font_weight_patient: 600,
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
                { key: "font_size_header", wKey: "font_weight_header", label: "Header (Store / Inv#)", def: 6.5, defW: 800 },
                { key: "font_size_patient", wKey: "font_weight_patient", label: "Patient / Qty / Doctor", def: 6.0, defW: 600 },
                { key: "font_size_product", wKey: "font_weight_product", label: "Medicine Name", def: 8.0, defW: 900 },
                { key: "font_size_dosage", wKey: "font_weight_dosage", label: "Dosage Text", def: 7.5, defW: 800 },
                { key: "font_size_footer", wKey: "font_weight_footer", label: "Footer / Batch / Exp", def: 5.5, defW: 700 },
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
                          onChange({
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
                        onChange={(opt) => opt && onChange({ [fs.wKey]: opt.value })}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
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
              onChange={(e) => onChange({ footer_note: e.target.value })}
              placeholder="Keep out of reach of children"
              className="w-full text-xs"
            />
          </div>
        </div>

        {/* Live Scaled Preview */}
        <div className="lg:col-span-5 p-4 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-100/70 dark:bg-slate-900/60 flex flex-col items-center justify-center gap-3">
          <div className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
            Live Sticker Preview ({wMm}mm × {hMm}mm)
          </div>

          <div
            style={{
              width: `${previewW}px`,
              height: `${previewH}px`,
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
              className={`text-center rounded px-1.5 py-1 leading-snug ${
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
        </div>
      </div>
    </div>
  );
}

export default function SaleSystemSetting({
  form,
  setForm,
  handleSaleSystemChange,
  handleShopTypeChange,
  disableInputs,
}) {
  const {
    saleSystem: activeSaleSystem,
    hasWholesale,
    shopType: activeShopType,
    isPharmacy,
    loading: saleSystemLoading,
  } = useSaleSystem();

  // Pending selection (from the settings form) falls back to the saved system
  const selected = form?.sale_system || activeSaleSystem || 'retail_wholesale';

  // Pending shop type falls back to the saved shop type
  const selectedShopType = form?.shop_type || activeShopType || 'pharmacy';

  const activeLabel = hasWholesale ? 'Retail + Wholesale' : 'Retail Only';
  const activeShopTypeLabel = isPharmacy ? 'Pharmacy' : 'General Store';
  const activeNote = hasWholesale
    ? 'Wholesale is enabled — you can sell both retail and in bulk.'
    : 'Retail only — wholesale columns and bulk sale actions are hidden.';

  return (
    <>
      {/* ===== Header + currently active ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <ShoppingCartIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Sale System Configuration</h2>
              <p className="settings-block-note">
                Choose your sale mode (retail / wholesale) and shop type (pharmacy / general store).
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-field">
            <span className="settings-label">Currently active</span>
            <div className="flex flex-wrap items-center gap-3">
              {saleSystemLoading ? (
                <span className="settings-hint">Loading…</span>
              ) : (
                <>
                  <span
                    className="settings-chip"
                    style={{ "--settings-chip": "var(--color-primary)" }}
                  >
                    <CheckBadgeIcon aria-hidden="true" />
                    {activeLabel}
                  </span>
                  <span
                    className="settings-chip"
                    style={{ "--settings-chip": "var(--color-secondary)" }}
                  >
                    <CheckBadgeIcon aria-hidden="true" />
                    {activeShopTypeLabel}
                  </span>
                  <span className="settings-hint">{activeNote}</span>
                </>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* ===== Mode selection ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <ScaleIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Sale System Mode</h2>
              <p className="settings-block-note">
                Changes below apply instantly across the app once saved.
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <ChoiceGroup
            name="sale_system"
            legend="Choose a sale system mode"
            options={FEATURES}
            selectedKey={selected}
            onSelect={handleSaleSystemChange}
            disableInputs={disableInputs}
          />
        </div>
      </div>

      {/* ===== Shop type selection ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <BeakerIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Shop Type</h2>
              <p className="settings-block-note">
                Pharmacy fields stay available only for medical stores.
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <ChoiceGroup
            name="shop_type"
            legend="Choose a shop type"
            options={SHOP_TYPES}
            selectedKey={selectedShopType}
            onSelect={handleShopTypeChange}
            disableInputs={disableInputs}
          />
        </div>
      </div>

      {/* ===== Optional Pharmacy Clinical Add-Ons (100% Optional) ===== */}
      {selectedShopType === "pharmacy" && setForm && (
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
                    Adds an optional "Dosage Stickers (50×25mm)" button on Sale Invoices for printing bilingual Urdu/English patient medicine instructions.
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
                    Adds an optional "Prescription Scan" button on Sale Invoices to attach and view doctor prescription images or PDFs.
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

            {form?.enable_dosage_stickers !== false && (
              <DosageStickerDesigner
                config={{ ...DEFAULT_STICKER_CONFIG, ...(form?.dosage_sticker_config || {}) }}
                storeName={form?.store_name || "Karobar Pharmacy"}
                disableInputs={disableInputs}
                onChange={(patch) =>
                  setForm((s) => ({
                    ...s,
                    dosage_sticker_config: {
                      ...DEFAULT_STICKER_CONFIG,
                      ...(s?.dosage_sticker_config || {}),
                      ...patch,
                    },
                  }))
                }
              />
            )}
          </div>
        </div>
      )}

      {/* ===== What changes when you switch ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <ArrowTrendingUpIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">What changes when you switch</h2>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-tiles">
            <div className="settings-tile">
              <div className="settings-tile-title">
                <BanknotesIcon aria-hidden="true" />
                Retail Only mode shows you
              </div>
              <ul className="settings-bullets">
                {[
                  'Single selling price per product',
                  'Clean sale invoice — no wholesale unit / pack / margin columns',
                  'Only retail actions on the sale invoice list',
                  'Wholesale-only routes stay hidden / blocked',
                ].map((item, i) => (
                  <li key={i}>
                    <CheckIcon aria-hidden="true" />
                    <span>{item}</span>
                  </li>
                ))}
              </ul>
            </div>

            <div className="settings-tile">
              <div className="settings-tile-title">
                <ScaleIcon aria-hidden="true" />
                Retail + Wholesale mode adds
              </div>
              <ul className="settings-bullets">
                {[
                  'Wholesale price per product — pack, unit & margin %',
                  'Wholesale columns inside purchase invoice entry',
                  'Wholesale / bulk sale invoices and extra list actions',
                  'Full wholesale customer workflow with ledgers',
                ].map((item, i) => (
                  <li key={i}>
                    <CheckIcon aria-hidden="true" />
                    <span>{item}</span>
                  </li>
                ))}
              </ul>
            </div>
          </div>

          <div className="settings-note">
            <InformationCircleIcon aria-hidden="true" />
            <div>
              <span className="settings-note-title">How this works</span>
              Your selection is saved together with the rest of your settings. It takes effect immediately across
              the whole app — forms, invoice lists and pages adapt to the mode you choose. Use Retail + Wholesale
              if you sell in bulk; switch to Retail Only for a simpler, counter-only workflow.
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
