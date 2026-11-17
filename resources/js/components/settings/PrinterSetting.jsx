// resources/js/components/settings/PrinterSetting.jsx
import { useEffect, useRef, useState } from "react";
import toast from "react-hot-toast";
import {
  PrinterIcon,
  DocumentTextIcon,
  EyeIcon,
  DocumentIcon,
  ClipboardDocumentListIcon,
  ScaleIcon,
  BoltIcon,
  QrCodeIcon,
  WrenchScrewdriverIcon,
  AdjustmentsHorizontalIcon,
  CheckCircleIcon,
  ArrowPathIcon,
  TagIcon,
} from "@heroicons/react/24/solid";
import {
  getDesktopPrintBridge,
  getPreferredPrinter,
  setPreferredPrinter,
  printSlipUrl,
  printSlipHtml,
} from "@/utils/printHelper.js";

const CALIBRATION_PRESETS = [
  {
    id: "thermal_80_safe_72",
    label: "Fix Right Cut-Off (80mm Roll → 72mm Safe)",
    tagline: "Fixes clipped totals/prices on standard 80mm POS printers",
    printerType: "thermal",
    values: {
      print_paper_width_mm: 72,
      print_margin_left_mm: 0,
      print_margin_right_mm: 0,
      print_margin_top_mm: 0,
      print_margin_bottom_mm: 6,
      print_font_scale: 100,
      print_scale_mode: "noscale",
    },
  },
  {
    id: "thermal_80_narrow_68",
    label: "Extra-Narrow 80mm Roll (68mm Safe)",
    tagline: "For generic POS-80 drivers with wide hardware side margins",
    printerType: "thermal",
    values: {
      print_paper_width_mm: 68,
      print_margin_left_mm: 0,
      print_margin_right_mm: 2,
      print_margin_top_mm: 0,
      print_margin_bottom_mm: 6,
      print_font_scale: 96,
      print_scale_mode: "noscale",
    },
  },
  {
    id: "thermal_58_safe_48",
    label: "Fix 58mm Roll Cut-Off (48mm Safe)",
    tagline: "Matches the 48mm active printhead on 58mm thermal printers",
    printerType: "thermal",
    values: {
      print_paper_width_mm: 48,
      print_margin_left_mm: 0,
      print_margin_right_mm: 0,
      print_margin_top_mm: 0,
      print_margin_bottom_mm: 6,
      print_font_scale: 92,
      print_scale_mode: "noscale",
    },
  },
  {
    id: "cutter_feed_fix",
    label: "Fix Auto-Cutter Slicing Bottom (+12mm Feed)",
    tagline: "Adds bottom feed margin so the blade cuts below the footer",
    printerType: "any",
    values: {
      print_margin_bottom_mm: 12,
    },
  },
  {
    id: "faint_print_high_contrast",
    label: "Fix Faint / Gray Print (Pure Black + Bold)",
    tagline: "Forces #000000 black ink and bold weights for 203 DPI thermal heads",
    printerType: "any",
    values: {
      print_contrast_mode: "high_contrast",
    },
  },
  {
    id: "extra_dark_bold",
    label: "Extra Dark & Heavy Thermal Print",
    tagline: "Maximum darkness + 105% font scale for aging thermal printheads",
    printerType: "any",
    values: {
      print_contrast_mode: "extra_bold",
      print_font_scale: 105,
    },
  },
  {
    id: "a4_safe_190",
    label: "A4 Safe Printable Margins (190mm)",
    tagline: "Prevents edge clipping on laser/inkjet A4 printers",
    printerType: "a4",
    values: {
      print_paper_width_mm: 190,
      print_margin_top_mm: 8,
      print_margin_bottom_mm: 8,
      print_margin_left_mm: 0,
      print_margin_right_mm: 0,
      print_scale_mode: "fit",
    },
  },
];

const QUICK_WIDTH_PILLS_THERMAL = [
  { label: "Auto", value: 0 },
  { label: "44mm", value: 44 },
  { label: "48mm (58 Roll)", value: 48 },
  { label: "52mm", value: 52 },
  { label: "58mm", value: 58 },
  { label: "64mm", value: 64 },
  { label: "68mm", value: 68 },
  { label: "72mm (80 Roll)", value: 72 },
  { label: "76mm", value: 76 },
  { label: "80mm", value: 80 },
];

const QUICK_WIDTH_PILLS_A4 = [
  { label: "Auto (A4)", value: 0 },
  { label: "180mm", value: 180 },
  { label: "190mm (Safe A4)", value: 190 },
  { label: "200mm", value: 200 },
  { label: "210mm (Full A4)", value: 210 },
];

const STICKER_TEMPLATES = [
  {
    id: "classic",
    name: "Classic Bordered",
    description: "Balanced bordered layout with clear divider for medicine vials & boxes",
    icon: "DocumentTextIcon",
    preview: "Classic bilingual dosage label with separate instruction box",
  },
  {
    id: "minimal",
    name: "Minimal Clean",
    description: "Clean borderless design for modern hospital pharmacy labeling",
    icon: "DocumentIcon",
    preview: "Clean text layout without heavy box borders",
  },
  {
    id: "bold",
    name: "Bold Dark Header",
    description: "Reverse black header with prominent shop name & patient info",
    icon: "BoltIcon",
    preview: "High-emphasis black banner header for fast identification",
  },
  {
    id: "urdu_focus",
    name: "Urdu-First Dosage",
    description: "Enlarged Noto Nastaliq Urdu dosage instructions for patients",
    icon: "ClipboardDocumentListIcon",
    preview: "Maximum readability for Urdu-speaking patients & elders",
  },
];

const STICKER_CALIBRATION_PRESETS = [
  {
    id: "sticker_72_safe",
    label: "Fix 72mm Roll Cut-Off (72×35mm Safe)",
    tagline: "Fits standard 72mm active printhead on 80mm POS receipt rolls",
    values: {
      preset_key: "72x35",
      width_mm: 72,
      height_mm: 35,
      printer_mode: "receipt_roll",
      margin_left_mm: 0,
      margin_right_mm: 0,
      margin_top_mm: 0,
      margin_bottom_mm: 6,
      font_scale: "normal",
      contrast_mode: "high_contrast",
    },
  },
  {
    id: "sticker_50x25_diecut",
    label: "Standard 50×25mm Die-Cut Label",
    tagline: "Calibrates standard retail pharmacy barcode/dosage sticker rolls",
    values: {
      preset_key: "50x25",
      width_mm: 50,
      height_mm: 25,
      printer_mode: "label_printer",
      margin_left_mm: 0,
      margin_right_mm: 0,
      margin_top_mm: 0,
      margin_bottom_mm: 0,
      font_scale: "compact",
      contrast_mode: "high_contrast",
    },
  },
  {
    id: "sticker_38x25_vial",
    label: "Compact 38×25mm Vial / Strip Label",
    tagline: "Compact sizing for small medicine ampoules, vials & eye drops",
    values: {
      preset_key: "38x25",
      width_mm: 38,
      height_mm: 25,
      printer_mode: "label_printer",
      margin_left_mm: 0,
      margin_right_mm: 0,
      margin_top_mm: 0,
      margin_bottom_mm: 0,
      font_scale: "compact",
      contrast_mode: "high_contrast",
    },
  },
  {
    id: "sticker_cutter_feed_gap",
    label: "Fix Auto-Cutter Slicing (+10mm Bottom Feed)",
    tagline: "Adds bottom feed margin so the blade cuts between stickers",
    values: {
      margin_bottom_mm: 10,
    },
  },
  {
    id: "sticker_high_contrast",
    label: "Fix Faint Print on Stickers (Pure Black + Bold)",
    tagline: "Forces #000000 black ink & bold weights for 203 DPI thermal heads",
    values: {
      contrast_mode: "high_contrast",
    },
  },
  {
    id: "sticker_urdu_emphasis",
    label: "Urdu Prescription Focus (Large & Dark)",
    tagline: "Selects Urdu-First template with large font scale and dark contrast",
    values: {
      template: "urdu_focus",
      font_scale: "large",
      contrast_mode: "high_contrast",
    },
  },
];

const STICKER_SIZE_PILLS = [
  { label: "72×35 mm (72mm Roll)", preset_key: "72x35", width_mm: 72, height_mm: 35 },
  { label: "72×40 mm (Tall Roll)", preset_key: "72x40", width_mm: 72, height_mm: 40 },
  { label: "50×25 mm (Std Die-Cut)", preset_key: "50x25", width_mm: 50, height_mm: 25 },
  { label: "50×30 mm (Medium)", preset_key: "50x30", width_mm: 50, height_mm: 30 },
  { label: "60×40 mm (Large Box)", preset_key: "60x40", width_mm: 60, height_mm: 40 },
  { label: "70×35 mm (Wide Label)", preset_key: "70x35", width_mm: 70, height_mm: 35 },
  { label: "80×50 mm (Jumbo Bottle)", preset_key: "80x50", width_mm: 80, height_mm: 50 },
  { label: "38×25 mm (Compact Vial)", preset_key: "38x25", width_mm: 38, height_mm: 25 },
];

export default function PrinterSetting({
  form,
  setForm,
  handleChange,
  disableInputs,
  saving,
  handleSave,
}) {
  const [selectedThermalTemplate, setSelectedThermalTemplate] = useState(
    form.thermal_template || "standard"
  );
  const [selectedA4Template, setSelectedA4Template] = useState(
    form.a4_template || "standard"
  );
  const [layoutCategory, setLayoutCategory] = useState(
    form.printer_type === "a4" ? "a4" : "thermal"
  );
  const [showPreviewModal, setShowPreviewModal] = useState(false);
  const [previewingTemplate, setPreviewingTemplate] = useState(null);
  const [previewingType, setPreviewingType] = useState("thermal"); // 'thermal', 'a4', 'sticker'
  const [installedPrinters, setInstalledPrinters] = useState([]);
  const [preferredDeviceName, setPreferredDeviceName] = useState(() =>
    getPreferredPrinter()
  );
  const [resolvedPrinterName, setResolvedPrinterName] = useState("");
  const [loadingPrinters, setLoadingPrinters] = useState(false);

  const calibrationSectionRef = useRef(null);

  const desktopBridge = getDesktopPrintBridge();
  const isDesktopApp = Boolean(desktopBridge?.isDesktop);

  useEffect(() => {
    if (form.thermal_template && form.thermal_template !== selectedThermalTemplate) {
      setSelectedThermalTemplate(form.thermal_template);
    }
    if (form.a4_template && form.a4_template !== selectedA4Template) {
      setSelectedA4Template(form.a4_template);
    }
  }, [form.thermal_template, form.a4_template]);

  const loadDesktopPrinters = async () => {
    const bridge = getDesktopPrintBridge();
    if (!bridge || typeof bridge.getPrinters !== "function") return;
    setLoadingPrinters(true);
    try {
      const list = await bridge.getPrinters();
      setInstalledPrinters(Array.isArray(list) ? list : []);
      if (typeof bridge.getPreferredPrinter === "function") {
        const info = await bridge.getPreferredPrinter();
        if (info?.preferredPrinter && !preferredDeviceName) {
          setPreferredDeviceName(info.preferredPrinter);
        }
        setResolvedPrinterName(String(info?.resolvedPrinter || ""));
      }
    } catch {
      setInstalledPrinters([]);
    } finally {
      setLoadingPrinters(false);
    }
  };

  useEffect(() => {
    if (isDesktopApp) {
      loadDesktopPrinters();
    }
  }, [isDesktopApp]);

  const updateStickerConfig = (patch) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    setForm((prev) => ({
      ...prev,
      dosage_sticker_config: {
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
        margin_top_mm: 0,
        margin_bottom_mm: 4,
        margin_left_mm: 0,
        margin_right_mm: 0,
        contrast_mode: "high_contrast",
        scale_mode: "auto",
        preferred_printer: "",
        ...(prev?.dosage_sticker_config || {}),
        ...patch,
      },
    }));
  };

  const handleResetStickerCalibration = () => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    updateStickerConfig({
      preset_key: "72x35",
      width_mm: 72,
      height_mm: 35,
      printer_mode: "receipt_roll",
      margin_top_mm: 0,
      margin_bottom_mm: 4,
      margin_left_mm: 0,
      margin_right_mm: 0,
      contrast_mode: "high_contrast",
      scale_mode: "auto",
      font_scale: "normal",
    });
    toast.success("Sticker layout calibration reset to default");
  };

  const applyStickerCalibrationPreset = (preset) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    updateStickerConfig(preset.values);
    toast.success(`Applied preset: ${preset.label}`);
  };

  const updateCalibrationFields = (patch) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    if (typeof setForm === "function") {
      setForm((prev) => ({
        ...prev,
        ...patch,
      }));
    } else {
      Object.entries(patch).forEach(([name, value]) => {
        handleChange({ target: { name, value } });
      });
    }
  };

  const applyCalibrationPreset = (preset) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    updateCalibrationFields(preset.values);
    toast.success(`Applied preset: ${preset.label}`);
  };

  const handleResetCalibration = () => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    updateCalibrationFields({
      print_paper_width_mm: 0,
      print_margin_top_mm: 0,
      print_margin_bottom_mm: 4,
      print_margin_left_mm: 0,
      print_margin_right_mm: 0,
      print_font_scale: 100,
      print_contrast_mode: "standard",
      print_scale_mode: "auto",
      print_show_logo: true,
    });
    toast.success("Receipt layout calibration reset to default");
  };

  const buildCalibratedPreviewUrl = (type, templateId, customForm = form) => {
    if (type === "sticker") {
      const stickerCfg = customForm?.dosage_sticker_config || {};
      const safeTpl = templateId || stickerCfg.template || "classic";
      const params = new URLSearchParams();
      params.set("width_mm", String(stickerCfg.width_mm || 72));
      params.set("height_mm", String(stickerCfg.height_mm || 35));
      params.set("printer_mode", stickerCfg.printer_mode || "receipt_roll");
      params.set("preset_key", stickerCfg.preset_key || "72x35");
      params.set("font_scale", stickerCfg.font_scale || "normal");
      params.set("contrast_mode", stickerCfg.contrast_mode || "high_contrast");
      params.set("scale_mode", stickerCfg.scale_mode || "auto");
      params.set("margin_top_mm", String(stickerCfg.margin_top_mm ?? 0));
      params.set("margin_bottom_mm", String(stickerCfg.margin_bottom_mm ?? 4));
      params.set("margin_left_mm", String(stickerCfg.margin_left_mm ?? 0));
      params.set("margin_right_mm", String(stickerCfg.margin_right_mm ?? 0));
      params.set("show_shop_name", stickerCfg.show_shop_name !== false ? "1" : "0");
      params.set("show_invoice_meta", stickerCfg.show_invoice_meta !== false ? "1" : "0");
      params.set("show_patient", stickerCfg.show_patient !== false ? "1" : "0");
      params.set("show_quantity", stickerCfg.show_quantity !== false ? "1" : "0");
      params.set("show_batch", stickerCfg.show_batch !== false ? "1" : "0");
      params.set("show_expiry", stickerCfg.show_expiry !== false ? "1" : "0");
      params.set("show_doctor", stickerCfg.show_doctor ? "1" : "0");
      return `/print/sticker-preview/${safeTpl}?${params.toString()}`;
    }

    const safeType = type === "a4" ? "a4" : "thermal";
    const safeTpl = templateId || "standard";
    const params = new URLSearchParams();

    const widthMm = Number(customForm?.print_paper_width_mm || 0);
    const topMm = Number(customForm?.print_margin_top_mm || 0);
    const bottomMm = Number(
      customForm?.print_margin_bottom_mm !== undefined
        ? customForm.print_margin_bottom_mm
        : 4
    );
    const leftMm = Number(customForm?.print_margin_left_mm || 0);
    const rightMm = Number(customForm?.print_margin_right_mm || 0);
    const fontScale = Number(customForm?.print_font_scale || 100);
    const contrastMode = String(customForm?.print_contrast_mode || "standard");
    const scaleMode = String(customForm?.print_scale_mode || "auto");
    const showLogo = customForm?.print_show_logo !== false ? "1" : "0";

    params.set("paper_width_mm", String(widthMm));
    params.set("margin_top_mm", String(topMm));
    params.set("margin_bottom_mm", String(bottomMm));
    params.set("margin_left_mm", String(leftMm));
    params.set("margin_right_mm", String(rightMm));
    params.set("font_scale", String(fontScale));
    params.set("contrast_mode", contrastMode);
    params.set("scale_mode", scaleMode);
    params.set("print_show_logo", showLogo);

    if (customForm?.note) {
      params.set("note", String(customForm.note));
    }

    return `/print/${safeType}-preview/${safeTpl}?${params.toString()}`;
  };

  const handlePreferredPrinterChange = async (e) => {
    const nextName = e.target.value;
    setPreferredDeviceName(nextName);
    setPreferredPrinter(nextName);
    try {
      const bridge = getDesktopPrintBridge();
      if (bridge && typeof bridge.setPreferredPrinter === "function") {
        const res = await bridge.setPreferredPrinter(nextName);
        if (res?.resolvedPrinter !== undefined) {
          setResolvedPrinterName(String(res.resolvedPrinter || ""));
        }
      }
    } catch {
      // ignore
    }
    toast.success(
      nextName
        ? `Direct print target set to: ${nextName}`
        : "Direct print target set to Automatic Hardware Detection"
    );
  };

  const handleTestTemplatePrint = async (type, templateId, templateName = "") => {
    if (type === "sticker") {
      const activeTpl = templateId || form.dosage_sticker_config?.template || "classic";
      const url = buildCalibratedPreviewUrl("sticker", activeTpl, form);
      const label = templateName ? `${templateName} (Sticker)` : `${activeTpl} sticker`;
      const targetPrinter = form.dosage_sticker_config?.preferred_printer || "";
      await printSlipUrl(url, {
        deviceName: targetPrinter,
        loadingMessage: `Sending ${label} test print to printer...`,
        successMessage: `Test sticker (${label}) sent to printer`,
      });
      return;
    }
    const activeType = type || (form.printer_type === "thermal" ? "thermal" : "a4");
    const activeTpl =
      templateId ||
      (activeType === "thermal"
        ? form.thermal_template || selectedThermalTemplate || "standard"
        : form.a4_template || selectedA4Template || "standard");
    const url = buildCalibratedPreviewUrl(activeType, activeTpl, form);
    const label = templateName ? `${templateName} (${activeType.toUpperCase()})` : `${activeTpl} receipt`;
    await printSlipUrl(url, {
      loadingMessage: `Sending ${label} test print to printer...`,
      successMessage: `Test receipt (${label}) sent to printer`,
    });
  };

  const handleTestDirectPrint = async () => {
    if (layoutCategory === "sticker") {
      await handleTestTemplatePrint("sticker", form.dosage_sticker_config?.template || "classic");
      return;
    }
    const activeType = form.printer_type === "thermal" ? "thermal" : "a4";
    const activeTpl =
      activeType === "thermal"
        ? form.thermal_template || selectedThermalTemplate || "standard"
        : form.a4_template || selectedA4Template || "standard";
    await handleTestTemplatePrint(activeType, activeTpl);
  };

  const handlePrintAlignmentRulerSlip = async () => {
    const configuredWidth = Number(form.print_paper_width_mm || 0);
    const isThermalMode = form.printer_type === "thermal";
    const targetWidthMm =
      configuredWidth >= 40
        ? configuredWidth
        : isThermalMode
        ? 72
        : 190;
    const leftMm = Number(form.print_margin_left_mm || 0);
    const rightMm = Number(form.print_margin_right_mm || 0);
    const topMm = Number(form.print_margin_top_mm || 0);
    const bottomMm = Number(form.print_margin_bottom_mm ?? 4);
    const fontScale = Number(form.print_font_scale || 100);
    const contrastMode = String(form.print_contrast_mode || "standard");
    const scaleMode = String(form.print_scale_mode || "auto");
    const nowStr = new Date().toLocaleString();

    const html = `<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8" />
  <meta name="karobar-print-config"
    content="paperWidthMm=${targetWidthMm};marginTopMm=${topMm};marginBottomMm=${bottomMm};marginLeftMm=${leftMm};marginRightMm=${rightMm};fontScale=${fontScale};contrastMode=${contrastMode};scaleMode=${scaleMode}"
  />
  <title>Printer Alignment & Width Calibration Test</title>
  <style>
    @page { size: ${targetWidthMm}mm auto; margin: 0; }
    * { box-sizing: border-box; margin: 0; padding: 0; color: #000 !important; }
    html, body {
      width: ${targetWidthMm}mm;
      max-width: ${targetWidthMm}mm;
      font-family: 'Courier New', Consolas, monospace;
      font-size: 11px;
      font-weight: ${contrastMode === "standard" ? "600" : "800"};
      background: #fff;
      padding-top: ${topMm}mm;
      padding-bottom: ${bottomMm}mm;
      padding-left: ${Math.max(0, leftMm)}mm;
      padding-right: ${Math.max(0, rightMm)}mm;
    }
    .frame {
      border: 2px solid #000;
      padding: 2mm;
      width: 100%;
    }
    .title {
      text-align: center;
      font-size: 13px;
      font-weight: 900;
      border-bottom: 2px solid #000;
      padding-bottom: 4px;
      margin-bottom: 6px;
    }
    .edge-markers {
      display: flex;
      justify-content: space-between;
      font-weight: 900;
      font-size: 11px;
      border: 1px dashed #000;
      padding: 3px 2px;
      margin-bottom: 6px;
    }
    .ruler-box {
      margin: 6px 0;
      border-top: 2px solid #000;
      border-bottom: 2px solid #000;
      padding: 4px 0;
    }
    .width-bar {
      height: 16px;
      border: 1.5px solid #000;
      margin: 3px 0;
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 3px;
      font-size: 9px;
      font-weight: 800;
      background: #fff;
    }
    .row {
      display: flex;
      justify-content: space-between;
      margin: 2px 0;
      font-size: 10px;
    }
    .check-guide {
      margin-top: 6px;
      border-top: 1px solid #000;
      padding-top: 4px;
      font-size: 9.5px;
      line-height: 1.35;
    }
    .footer-cut {
      margin-top: 8px;
      text-align: center;
      border-top: 2px dashed #000;
      padding-top: 4px;
      font-size: 10px;
      font-weight: 900;
    }
  </style>
</head>
<body>
  <div class="frame">
    <div class="title">KAROBAR ERP CALIBRATION SLIP</div>
    <div class="edge-markers">
      <span>|&lt;-- LEFT EDGE</span>
      <span>RIGHT EDGE --&gt;|</span>
    </div>
    <div class="row"><span>Configured Width:</span><span>${configuredWidth ? configuredWidth + "mm" : "Auto (" + targetWidthMm + "mm)"}</span></div>
    <div class="row"><span>Margins (T/B/L/R):</span><span>${topMm}/${bottomMm}/${leftMm}/${rightMm} mm</span></div>
    <div class="row"><span>Font / Contrast:</span><span>${fontScale}% / ${contrastMode}</span></div>
    <div class="row"><span>Spool Scale Mode:</span><span>${scaleMode}</span></div>

    <div class="ruler-box">
      <div style="text-align:center;font-weight:900;margin-bottom:3px;">PHYSICAL WIDTH TEST BARS</div>
      <div class="width-bar" style="width:44mm;"><span>|44mm</span><span>44mm|</span></div>
      <div class="width-bar" style="width:48mm;"><span>|48mm (58 Roll)</span><span>48mm|</span></div>
      ${
        targetWidthMm >= 58
          ? `<div class="width-bar" style="width:58mm;"><span>|58mm</span><span>58mm|</span></div>`
          : ""
      }
      ${
        targetWidthMm >= 68
          ? `<div class="width-bar" style="width:68mm;"><span>|68mm (Narrow 80)</span><span>68mm|</span></div>`
          : ""
      }
      ${
        targetWidthMm >= 72
          ? `<div class="width-bar" style="width:72mm;"><span>|72mm (Std 80 Roll)</span><span>72mm|</span></div>`
          : ""
      }
    </div>

    <div class="row" style="border-bottom:1px dotted #000;padding:2px 0;">
      <span>1x Paracetamol 500mg Box</span>
      <span>Rs. 1,250.00</span>
    </div>
    <div class="row" style="border-bottom:1px dotted #000;padding:2px 0;">
      <span>2x Augmentin 625mg Tab</span>
      <span>Rs. 980.00</span>
    </div>
    <div class="row" style="font-size:12px;font-weight:900;padding-top:3px;">
      <span>NET PAYABLE TOTAL:</span>
      <span>Rs. 2,230.00</span>
    </div>

    <div class="check-guide">
      1. If RIGHT border or "Rs. 2,230.00" is clipped, pick the widest bar above that printed completely (e.g. 72mm, 68mm, or 48mm) and set Printable Width to that value.<br/>
      2. If auto-cutter cuts this text, increase Bottom Feed Margin to 12mm.
    </div>

    <div class="footer-cut">
      --- AUTO-CUTTER SAFE LINE (${nowStr}) ---
    </div>
  </div>
</body>
</html>`;

    await printSlipHtml(html, {
      loadingMessage: "Printing hardware alignment & millimeter ruler slip...",
      successMessage: "Calibration ruler slip sent to printer",
    });
  };

  // Thermal templates data
  const thermalTemplates = [
    {
      id: "standard",
      name: "Standard",
      description: "Classic layout with logo support",
      icon: "DocumentTextIcon",
      preview: "Standard thermal layout with store branding",
    },
    {
      id: "minimal",
      name: "Minimal",
      description: "Clean 58mm receipt with essential invoice details",
      icon: "DocumentIcon",
      preview: "Customer, invoice type, remarks and item count",
    },
    {
      id: "detailed",
      name: "Detailed",
      description: "Roomy 80mm receipt with a complete customer header",
      icon: "ClipboardDocumentListIcon",
      preview: "Full invoice details, remarks and payment balance",
    },
    {
      id: "compact",
      name: "Compact",
      description: "Space-saving 58mm receipt with a compact detail grid",
      icon: "ScaleIcon",
      preview: "All invoice header fields in a tight layout",
    },
    {
      id: "bold",
      name: "Bold",
      description: "High-contrast 80mm receipt with bold invoice details",
      icon: "BoltIcon",
      preview: "Prominent invoice type, customer and total",
    },
    {
      id: "barcode",
      name: "Barcode",
      description: "Verification-focused layout with product barcodes",
      icon: "QrCodeIcon",
      preview: "Invoice details, remarks, product barcodes and QR",
    },
  ];

  // A4 templates data
  const a4Templates = [
    {
      id: "standard",
      name: "Standard",
      description: "Classic layout with logo support",
      icon: "DocumentTextIcon",
      preview: "Standard A4 layout with store branding",
    },
    {
      id: "minimal",
      name: "Minimal",
      description: "Clean and simple design",
      icon: "DocumentIcon",
      preview: "Minimal A4 receipt",
    },
    {
      id: "detailed",
      name: "Detailed",
      description: "Extended customer & payment info",
      icon: "ClipboardDocumentListIcon",
      preview: "Complete with customer balance details",
    },
    {
      id: "compact",
      name: "Compact",
      description: "Space-efficient layout",
      icon: "ScaleIcon",
      preview: "Compact A4 format",
    },
    {
      id: "bold",
      name: "Bold",
      description: "Large fonts, high emphasis",
      icon: "BoltIcon",
      preview: "Bold fonts with high contrast",
    },
    {
      id: "barcode",
      name: "Barcode",
      description: "With barcodes & QR code",
      icon: "QrCodeIcon",
      preview: "Includes barcodes and verification QR",
    },
  ];

  const handleTemplateSelect = (templateId) => {
    if (!disableInputs) {
      setSelectedThermalTemplate(templateId);
      handleChange({ target: { name: "thermal_template", value: templateId } });
    }
  };

  const handleA4TemplateSelect = (templateId) => {
    if (!disableInputs) {
      setSelectedA4Template(templateId);
      handleChange({ target: { name: "a4_template", value: templateId } });
    }
  };

  const getIconComponent = (iconName) => {
    const icons = {
      DocumentTextIcon,
      DocumentIcon,
      ClipboardDocumentListIcon,
      ScaleIcon,
      BoltIcon,
      QrCodeIcon,
      TagIcon,
    };
    return icons[iconName] || TagIcon;
  };

  const isSticker = layoutCategory === "sticker";
  const isThermal = layoutCategory === "thermal" || (!isSticker && form.printer_type === "thermal");
  const activeType = isSticker ? "sticker" : isThermal ? "thermal" : "a4";
  const activeTemplateList = isSticker
    ? STICKER_TEMPLATES
    : isThermal
    ? thermalTemplates
    : a4Templates;
  const activeTemplateId = isSticker
    ? form.dosage_sticker_config?.template || "classic"
    : isThermal
    ? form.thermal_template || selectedThermalTemplate || "standard"
    : form.a4_template || selectedA4Template || "standard";
  const activeTemplateObj =
    activeTemplateList.find((t) => t.id === activeTemplateId) || activeTemplateList[0];

  const handleStickerTemplateSelect = (id) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    updateStickerConfig({ template: id });
  };

  const openFixModalForTemplate = (template, type) => {
    setPreviewingTemplate(template);
    setPreviewingType(type);
    setShowPreviewModal(true);
  };

  const scrollToCalibrationStudio = () => {
    if (calibrationSectionRef.current) {
      calibrationSectionRef.current.scrollIntoView({
        behavior: "smooth",
        block: "start",
      });
    }
  };

  const renderCalibrationControls = ({ compact = false } = {}) => {
    const quickWidthPills = isThermal ? QUICK_WIDTH_PILLS_THERMAL : QUICK_WIDTH_PILLS_A4;
    const currentWidth = Number(form.print_paper_width_mm || 0);
    const currentFontScale = Number(form.print_font_scale || 100);
    const currentContrast = form.print_contrast_mode || "standard";
    const currentScaleMode = form.print_scale_mode || "auto";
    const currentTop = Number(form.print_margin_top_mm || 0);
    const currentBottom = Number(
      form.print_margin_bottom_mm !== undefined ? form.print_margin_bottom_mm : 4
    );
    const currentLeft = Number(form.print_margin_left_mm || 0);
    const currentRight = Number(form.print_margin_right_mm || 0);
    const currentShowLogo = form.print_show_logo !== false;

    return (
      <div className="space-y-4">
        {/* 1-Click Problem Fixer Presets */}
        <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-50/70 dark:bg-slate-800/40 p-3.5">
          <div className="flex items-center justify-between gap-2 mb-2.5">
            <div className="flex items-center gap-2">
              <WrenchScrewdriverIcon className="w-4 h-4 text-blue-600 dark:text-blue-400 shrink-0" />
              <span className="text-xs font-bold uppercase tracking-wider text-slate-700 dark:text-slate-200">
                1-Click Printer Layout Problem Fixes
              </span>
            </div>
            <button
              type="button"
              onClick={handleResetCalibration}
              disabled={disableInputs}
              className="inline-flex items-center gap-1 text-xs font-medium text-slate-600 dark:text-slate-300 hover:text-blue-600 dark:hover:text-blue-400"
              title="Reset all layout calibration settings to factory default"
            >
              <ArrowPathIcon className="w-3.5 h-3.5" />
              <span>Reset Defaults</span>
            </button>
          </div>

          <div
            className={`grid gap-2 ${
              compact ? "grid-cols-1 sm:grid-cols-2" : "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3"
            }`}
          >
            {CALIBRATION_PRESETS.filter(
              (p) => p.printerType === "any" || p.printerType === activeType
            ).map((preset) => (
              <button
                key={preset.id}
                type="button"
                disabled={disableInputs}
                onClick={() => applyCalibrationPreset(preset)}
                className="text-left rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900/70 hover:border-blue-500 dark:hover:border-blue-400 px-3 py-2 transition shadow-2xs"
              >
                <div className="text-xs font-semibold text-slate-800 dark:text-slate-100">
                  {preset.label}
                </div>
                <div className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 line-clamp-2">
                  {preset.tagline}
                </div>
              </button>
            ))}
          </div>
        </div>

        {/* Printable Paper Width (mm) */}
        <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3.5 space-y-2.5">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <label
                htmlFor="calibration-paper-width-mm"
                className="text-xs font-bold text-slate-800 dark:text-slate-100 block"
              >
                Printable Paper Width (mm) — Fixes Right-Edge Cut-Off
              </label>
              <span className="text-[11px] text-slate-500 dark:text-slate-400">
                {isThermal
                  ? "80mm paper rolls have a 72mm (or 68mm) active thermal head; 58mm rolls have a 48mm head."
                  : "Standard A4 is 210mm; set 190mm if your printer clips left/right borders."}
              </span>
            </div>
            <div className="flex items-center gap-1.5">
              <input
                id="calibration-paper-width-mm"
                type="number"
                min={0}
                max={215}
                step={1}
                disabled={disableInputs}
                value={currentWidth}
                onChange={(e) =>
                  updateCalibrationFields({
                    print_paper_width_mm: Math.max(0, Math.min(215, Number(e.target.value) || 0)),
                  })
                }
                className="w-20 text-center text-xs font-bold rounded-lg border border-slate-300 dark:border-slate-600 px-2 py-1"
              />
              <span className="text-xs font-semibold text-slate-500">
                {currentWidth === 0 ? "mm (Auto)" : "mm"}
              </span>
            </div>
          </div>

          <div className="flex flex-wrap gap-1.5">
            {quickWidthPills.map((pill) => {
              const active = currentWidth === pill.value;
              return (
                <button
                  key={pill.label}
                  type="button"
                  disabled={disableInputs}
                  onClick={() =>
                    updateCalibrationFields({ print_paper_width_mm: pill.value })
                  }
                  className={`px-2.5 py-1 rounded-md text-xs font-semibold border transition ${
                    active
                      ? "bg-blue-600 text-white border-blue-600"
                      : "bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:border-blue-400"
                  }`}
                >
                  {pill.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Font Scale + Contrast + Spooler Scale Mode */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3">
            <div className="flex items-center justify-between mb-1">
              <label
                htmlFor="calibration-font-scale"
                className="text-xs font-bold text-slate-800 dark:text-slate-100"
              >
                Text & Font Size Scale
              </label>
              <span className="text-xs font-bold text-blue-600 dark:text-blue-400">
                {currentFontScale}%
              </span>
            </div>
            <input
              id="calibration-font-scale"
              type="range"
              min={70}
              max={140}
              step={2}
              disabled={disableInputs}
              value={currentFontScale}
              onChange={(e) =>
                updateCalibrationFields({
                  print_font_scale: Number(e.target.value) || 100,
                })
              }
              className="w-full accent-blue-600"
            />
            <div className="flex justify-between text-[10px] text-slate-400 mt-1">
              <span>70% (Compact)</span>
              <span>100%</span>
              <span>140% (Large)</span>
            </div>
          </div>

          <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3">
            <label
              htmlFor="calibration-contrast-mode"
              className="text-xs font-bold text-slate-800 dark:text-slate-100 block mb-1"
            >
              Print Darkness & Contrast
            </label>
            <select
              id="calibration-contrast-mode"
              name="print_contrast_mode"
              value={currentContrast}
              disabled={disableInputs}
              onChange={handleChange}
              className="w-full text-xs rounded-lg"
            >
              <option value="standard">Standard Template Weight</option>
              <option value="high_contrast">
                High Contrast — Pure Black & Bold (Recommended Thermal)
              </option>
              <option value="extra_bold">
                Extra Bold — Maximum Darkness for Faint Printheads
              </option>
            </select>
            <span className="text-[10px] text-slate-500 dark:text-slate-400 block mt-1">
              Eliminates washed-out gray text on 203 DPI thermal heads.
            </span>
          </div>

          <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3">
            <label
              htmlFor="calibration-scale-mode"
              className="text-xs font-bold text-slate-800 dark:text-slate-100 block mb-1"
            >
              Hardware Spooler Scaling
            </label>
            <select
              id="calibration-scale-mode"
              name="print_scale_mode"
              value={currentScaleMode}
              disabled={disableInputs}
              onChange={handleChange}
              className="w-full text-xs rounded-lg"
            >
              <option value="auto">Automatic (1:1 Thermal / Fit A4)</option>
              <option value="noscale">Exact 1:1 Millimeter Size (No Driver Scaling)</option>
              <option value="fit">Fit to Printer Printable Area</option>
              <option value="shrink">Shrink Oversized Pages Only</option>
            </select>
            <span className="text-[10px] text-slate-500 dark:text-slate-400 block mt-1">
              Controls Windows PDF/GDI spooler page scaling.
            </span>
          </div>
        </div>

        {/* Margins & Alignment Offsets (mm) */}
        <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3.5">
          <div className="text-xs font-bold text-slate-800 dark:text-slate-100 mb-2">
            Hardware Margins, Horizontal Shift & Auto-Cutter Feed (mm)
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <div>
              <label
                htmlFor="calibration-margin-left"
                className="text-[11px] font-semibold text-slate-600 dark:text-slate-300 block mb-1"
              >
                Left Shift ({currentLeft}mm)
              </label>
              <input
                id="calibration-margin-left"
                type="number"
                min={-15}
                max={25}
                disabled={disableInputs}
                value={currentLeft}
                onChange={(e) =>
                  updateCalibrationFields({
                    print_margin_left_mm: Math.max(
                      -15,
                      Math.min(25, Number(e.target.value) || 0)
                    ),
                  })
                }
                className="w-full text-xs rounded-lg"
              />
              <span className="text-[10px] text-slate-400 block mt-0.5">
                -15 to +25mm
              </span>
            </div>

            <div>
              <label
                htmlFor="calibration-margin-right"
                className="text-[11px] font-semibold text-slate-600 dark:text-slate-300 block mb-1"
              >
                Right SafePad ({currentRight}mm)
              </label>
              <input
                id="calibration-margin-right"
                type="number"
                min={0}
                max={30}
                disabled={disableInputs}
                value={currentRight}
                onChange={(e) =>
                  updateCalibrationFields({
                    print_margin_right_mm: Math.max(
                      0,
                      Math.min(30, Number(e.target.value) || 0)
                    ),
                  })
                }
                className="w-full text-xs rounded-lg"
              />
              <span className="text-[10px] text-slate-400 block mt-0.5">
                0 to 30mm right buffer
              </span>
            </div>

            <div>
              <label
                htmlFor="calibration-margin-top"
                className="text-[11px] font-semibold text-slate-600 dark:text-slate-300 block mb-1"
              >
                Top Feed ({currentTop}mm)
              </label>
              <input
                id="calibration-margin-top"
                type="number"
                min={0}
                max={30}
                disabled={disableInputs}
                value={currentTop}
                onChange={(e) =>
                  updateCalibrationFields({
                    print_margin_top_mm: Math.max(
                      0,
                      Math.min(30, Number(e.target.value) || 0)
                    ),
                  })
                }
                className="w-full text-xs rounded-lg"
              />
              <span className="text-[10px] text-slate-400 block mt-0.5">
                Top header spacing
              </span>
            </div>

            <div>
              <label
                htmlFor="calibration-margin-bottom"
                className="text-[11px] font-semibold text-slate-600 dark:text-slate-300 block mb-1"
              >
                Bottom Cutter Feed ({currentBottom}mm)
              </label>
              <input
                id="calibration-margin-bottom"
                type="number"
                min={0}
                max={40}
                disabled={disableInputs}
                value={currentBottom}
                onChange={(e) =>
                  updateCalibrationFields({
                    print_margin_bottom_mm: Math.max(
                      0,
                      Math.min(40, Number(e.target.value) || 0)
                    ),
                  })
                }
                className="w-full text-xs rounded-lg"
              />
              <span className="text-[10px] text-slate-400 block mt-0.5">
                Prevents cutter slicing footer
              </span>
            </div>
          </div>

          {/* Show Logo Toggle */}
          <div className="mt-3 pt-2.5 border-t border-slate-200 dark:border-slate-700/70 flex items-center justify-between">
            <div>
              <span className="text-xs font-semibold text-slate-800 dark:text-slate-100 block">
                Print Store Logo / Watermark on Receipts
              </span>
              <span className="text-[11px] text-slate-500 dark:text-slate-400">
                Disable if your thermal printer prints graphics slowly or too darkly
              </span>
            </div>
            <label className="inline-flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={currentShowLogo}
                disabled={disableInputs}
                onChange={(e) =>
                  updateCalibrationFields({ print_show_logo: e.target.checked })
                }
                className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
              />
              <span className="text-xs font-semibold text-slate-700 dark:text-slate-200">
                {currentShowLogo ? "Enabled" : "Hidden"}
              </span>
            </label>
          </div>
        </div>
      </div>
    );
  };

  const renderStickerCalibrationControls = ({ compact = false } = {}) => {
    const stickerCfg = form.dosage_sticker_config || {};
    const currentWidth = Number(stickerCfg.width_mm || 72);
    const currentHeight = Number(stickerCfg.height_mm || 35);
    const currentMode = stickerCfg.printer_mode || "receipt_roll";
    const currentPresetKey = stickerCfg.preset_key || "72x35";
    const currentFontScale = stickerCfg.font_scale || "normal";
    const currentContrast = stickerCfg.contrast_mode || "high_contrast";
    const currentScaleMode = stickerCfg.scale_mode || "auto";
    const currentTop = Number(stickerCfg.margin_top_mm || 0);
    const currentBottom = Number(
      stickerCfg.margin_bottom_mm !== undefined ? stickerCfg.margin_bottom_mm : 4
    );
    const currentLeft = Number(stickerCfg.margin_left_mm || 0);
    const currentRight = Number(stickerCfg.margin_right_mm || 0);
    const currentPrinter = stickerCfg.preferred_printer || "";

    return (
      <div className="space-y-4">
        {/* 1-Click Problem Fixer Presets */}
        <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-50/70 dark:bg-slate-800/40 p-3.5">
          <div className="flex items-center justify-between gap-2 mb-2.5">
            <div className="flex items-center gap-2">
              <WrenchScrewdriverIcon className="w-4 h-4 text-blue-600 dark:text-blue-400 shrink-0" />
              <span className="text-xs font-bold uppercase tracking-wider text-slate-700 dark:text-slate-200">
                1-Click Sticker & Label Problem Fixes
              </span>
            </div>
            <button
              type="button"
              onClick={handleResetStickerCalibration}
              disabled={disableInputs}
              className="inline-flex items-center gap-1 text-xs font-medium text-slate-600 dark:text-slate-300 hover:text-blue-600 dark:hover:text-blue-400"
              title="Reset sticker layout calibration settings to factory default"
            >
              <ArrowPathIcon className="w-3.5 h-3.5" />
              <span>Reset Defaults</span>
            </button>
          </div>

          <div
            className={`grid gap-2 ${
              compact ? "grid-cols-1 sm:grid-cols-2" : "grid-cols-1 sm:grid-cols-2 lg:grid-cols-2"
            }`}
          >
            {STICKER_CALIBRATION_PRESETS.map((preset) => (
              <button
                key={preset.id}
                type="button"
                disabled={disableInputs}
                onClick={() => applyStickerCalibrationPreset(preset)}
                className="text-left rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900/70 hover:border-blue-500 dark:hover:border-blue-400 px-3 py-2 transition shadow-2xs"
              >
                <div className="text-xs font-semibold text-slate-800 dark:text-slate-100">
                  {preset.label}
                </div>
                <div className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 line-clamp-2">
                  {preset.tagline}
                </div>
              </button>
            ))}
          </div>
        </div>

        {/* Sticker Dimensions & Media Mode */}
        <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3.5 space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <span className="text-xs font-bold text-slate-800 dark:text-slate-100 block">
                Label Size Presets & Printable Dimensions
              </span>
              <span className="text-[11px] text-slate-500 dark:text-slate-400">
                Choose a standard pharmacy size or enter custom width & height in millimeters
              </span>
            </div>
            <div className="flex items-center gap-2">
              <div className="flex items-center gap-1">
                <span className="text-[11px] font-semibold text-slate-500">W:</span>
                <input
                  type="number"
                  min={25}
                  max={120}
                  step={1}
                  disabled={disableInputs}
                  value={currentWidth}
                  onChange={(e) =>
                    updateStickerConfig({
                      width_mm: Math.max(25, Math.min(120, Number(e.target.value) || 72)),
                    })
                  }
                  className="w-16 text-center text-xs font-bold rounded-lg border border-slate-300 dark:border-slate-600 px-1.5 py-1"
                />
                <span className="text-xs text-slate-500">mm</span>
              </div>
              <span className="text-slate-400 font-bold">×</span>
              <div className="flex items-center gap-1">
                <span className="text-[11px] font-semibold text-slate-500">H:</span>
                <input
                  type="number"
                  min={15}
                  max={200}
                  step={1}
                  disabled={disableInputs}
                  value={currentHeight}
                  onChange={(e) =>
                    updateStickerConfig({
                      height_mm: Math.max(15, Math.min(200, Number(e.target.value) || 35)),
                    })
                  }
                  className="w-16 text-center text-xs font-bold rounded-lg border border-slate-300 dark:border-slate-600 px-1.5 py-1"
                />
                <span className="text-xs text-slate-500">mm</span>
              </div>
            </div>
          </div>

          <div className="flex flex-wrap gap-1.5">
            {STICKER_SIZE_PILLS.map((pill) => {
              const active =
                currentPresetKey === pill.preset_key ||
                (currentWidth === pill.width_mm && currentHeight === pill.height_mm);
              return (
                <button
                  key={pill.preset_key}
                  type="button"
                  disabled={disableInputs}
                  onClick={() =>
                    updateStickerConfig({
                      preset_key: pill.preset_key,
                      width_mm: pill.width_mm,
                      height_mm: pill.height_mm,
                    })
                  }
                  className={`px-2.5 py-1 rounded-md text-xs font-semibold border transition ${
                    active
                      ? "bg-blue-600 text-white border-blue-600"
                      : "bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:border-blue-400"
                  }`}
                >
                  {pill.label}
                </button>
              );
            })}
          </div>

          {/* Media / Roll Mode */}
          <div className="pt-2 border-t border-slate-200 dark:border-slate-700/60">
            <span className="text-xs font-semibold text-slate-800 dark:text-slate-100 block mb-1.5">
              Printer Media Mode
            </span>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
              <label
                className={`p-2.5 rounded-lg border cursor-pointer flex items-start gap-2.5 transition ${
                  currentMode === "receipt_roll"
                    ? "bg-blue-50/70 border-blue-500 dark:bg-blue-950/40 dark:border-blue-500"
                    : "bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-700"
                }`}
              >
                <input
                  type="radio"
                  name="sticker_printer_mode"
                  value="receipt_roll"
                  checked={currentMode === "receipt_roll"}
                  onChange={() => updateStickerConfig({ printer_mode: "receipt_roll" })}
                  disabled={disableInputs}
                  className="mt-0.5 text-blue-600"
                />
                <div>
                  <span className="text-xs font-bold text-slate-800 dark:text-slate-100 block">
                    Continuous Receipt Roll (72mm/80mm)
                  </span>
                  <span className="text-[11px] text-slate-500 dark:text-slate-400 block mt-0.5">
                    Separated with dash lines & auto-cutter feed guard. Standard POS thermal printers.
                  </span>
                </div>
              </label>

              <label
                className={`p-2.5 rounded-lg border cursor-pointer flex items-start gap-2.5 transition ${
                  currentMode === "label_sheet"
                    ? "bg-blue-50/70 border-blue-500 dark:bg-blue-950/40 dark:border-blue-500"
                    : "bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-700"
                }`}
              >
                <input
                  type="radio"
                  name="sticker_printer_mode"
                  value="label_sheet"
                  checked={currentMode === "label_sheet"}
                  onChange={() => updateStickerConfig({ printer_mode: "label_sheet" })}
                  disabled={disableInputs}
                  className="mt-0.5 text-blue-600"
                />
                <div>
                  <span className="text-xs font-bold text-slate-800 dark:text-slate-100 block">
                    Die-Cut Label Printer (Gap / Black Mark)
                  </span>
                  <span className="text-[11px] text-slate-500 dark:text-slate-400 block mt-0.5">
                    1 label per page break. Zebra, Xprinter, TSC, Gprinter sticker printers.
                  </span>
                </div>
              </label>
            </div>
          </div>
        </div>

        {/* Font Scale + Darkness & Contrast + Spooler Scaling */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3">
            <label
              htmlFor="sticker-font-scale"
              className="text-xs font-bold text-slate-800 dark:text-slate-100 block mb-1"
            >
              Text Scale & Size
            </label>
            <select
              id="sticker-font-scale"
              value={currentFontScale}
              disabled={disableInputs}
              onChange={(e) => updateStickerConfig({ font_scale: e.target.value })}
              className="w-full text-xs rounded-lg"
            >
              <option value="compact">Compact (80% - Small vials & 50×25mm)</option>
              <option value="normal">Normal (100% - Standard 72×35mm)</option>
              <option value="large">Large (115% - Bold elderly readable)</option>
            </select>
            <span className="text-[10px] text-slate-500 dark:text-slate-400 block mt-1">
              Scales medicine name, patient and Urdu instructions.
            </span>
          </div>

          <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3">
            <label
              htmlFor="sticker-contrast-mode"
              className="text-xs font-bold text-slate-800 dark:text-slate-100 block mb-1"
            >
              Print Darkness & Contrast
            </label>
            <select
              id="sticker-contrast-mode"
              value={currentContrast}
              disabled={disableInputs}
              onChange={(e) => updateStickerConfig({ contrast_mode: e.target.value })}
              className="w-full text-xs rounded-lg"
            >
              <option value="standard">Standard Weight</option>
              <option value="high_contrast">
                High Contrast — Pure Black & Bold (Recommended)
              </option>
              <option value="extra_bold">
                Extra Bold — Maximum Darkness for Faint Printheads
              </option>
            </select>
            <span className="text-[10px] text-slate-500 dark:text-slate-400 block mt-1">
              Guarantees deep black Urdu Nastaliq glyphs.
            </span>
          </div>

          <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3">
            <label
              htmlFor="sticker-scale-mode"
              className="text-xs font-bold text-slate-800 dark:text-slate-100 block mb-1"
            >
              Hardware Spooler Scaling
            </label>
            <select
              id="sticker-scale-mode"
              value={currentScaleMode}
              disabled={disableInputs}
              onChange={(e) => updateStickerConfig({ scale_mode: e.target.value })}
              className="w-full text-xs rounded-lg"
            >
              <option value="auto">Automatic (1:1 Millimeter)</option>
              <option value="noscale">Exact 1:1 Millimeter Size (No Driver Scaling)</option>
              <option value="fit">Fit to Printable Area</option>
              <option value="shrink">Shrink Oversized Only</option>
            </select>
            <span className="text-[10px] text-slate-500 dark:text-slate-400 block mt-1">
              Matches driver print margins without distorting stickers.
            </span>
          </div>
        </div>

        {/* Margins & Cutter Feed Guard */}
        <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3.5">
          <div className="text-xs font-bold text-slate-800 dark:text-slate-100 mb-2">
            Hardware Margins, Horizontal Alignment & Cutter Guard (mm)
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <div>
              <label
                htmlFor="sticker-margin-left"
                className="text-[11px] font-semibold text-slate-600 dark:text-slate-300 block mb-1"
              >
                Left Shift ({currentLeft}mm)
              </label>
              <input
                id="sticker-margin-left"
                type="number"
                min={-10}
                max={20}
                disabled={disableInputs}
                value={currentLeft}
                onChange={(e) =>
                  updateStickerConfig({
                    margin_left_mm: Math.max(-10, Math.min(20, Number(e.target.value) || 0)),
                  })
                }
                className="w-full text-xs rounded-lg"
              />
              <span className="text-[10px] text-slate-400 block mt-0.5">
                -10 to +20mm
              </span>
            </div>

            <div>
              <label
                htmlFor="sticker-margin-right"
                className="text-[11px] font-semibold text-slate-600 dark:text-slate-300 block mb-1"
              >
                Right SafePad ({currentRight}mm)
              </label>
              <input
                id="sticker-margin-right"
                type="number"
                min={0}
                max={25}
                disabled={disableInputs}
                value={currentRight}
                onChange={(e) =>
                  updateStickerConfig({
                    margin_right_mm: Math.max(0, Math.min(25, Number(e.target.value) || 0)),
                  })
                }
                className="w-full text-xs rounded-lg"
              />
              <span className="text-[10px] text-slate-400 block mt-0.5">
                Prevents right edge clip
              </span>
            </div>

            <div>
              <label
                htmlFor="sticker-margin-top"
                className="text-[11px] font-semibold text-slate-600 dark:text-slate-300 block mb-1"
              >
                Top Feed ({currentTop}mm)
              </label>
              <input
                id="sticker-margin-top"
                type="number"
                min={0}
                max={25}
                disabled={disableInputs}
                value={currentTop}
                onChange={(e) =>
                  updateStickerConfig({
                    margin_top_mm: Math.max(0, Math.min(25, Number(e.target.value) || 0)),
                  })
                }
                className="w-full text-xs rounded-lg"
              />
              <span className="text-[10px] text-slate-400 block mt-0.5">
                Top spacing
              </span>
            </div>

            <div>
              <label
                htmlFor="sticker-margin-bottom"
                className="text-[11px] font-semibold text-slate-600 dark:text-slate-300 block mb-1"
              >
                Cutter Guard / Bottom Feed ({currentBottom}mm)
              </label>
              <input
                id="sticker-margin-bottom"
                type="number"
                min={0}
                max={35}
                disabled={disableInputs}
                value={currentBottom}
                onChange={(e) =>
                  updateStickerConfig({
                    margin_bottom_mm: Math.max(0, Math.min(35, Number(e.target.value) || 0)),
                  })
                }
                className="w-full text-xs rounded-lg"
              />
              <span className="text-[10px] text-slate-400 block mt-0.5">
                Prevents cutter slicing text
              </span>
            </div>
          </div>
        </div>

        {/* Content & Field Toggles */}
        <div className="rounded-xl border border-slate-200 dark:border-slate-700/80 p-3.5 space-y-2">
          <div className="text-xs font-bold text-slate-800 dark:text-slate-100 mb-1">
            Sticker Label Content & Field Toggles
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-2.5">
            {[
              { key: "show_shop_name", label: "Store / Pharmacy Name" },
              { key: "show_invoice_meta", label: "Invoice # & Date" },
              { key: "show_patient", label: "Patient Name" },
              { key: "show_quantity", label: "Quantity & Unit" },
              { key: "show_batch", label: "Batch Number" },
              { key: "show_expiry", label: "Expiry Date" },
              { key: "show_doctor", label: "Doctor / Prescriber" },
            ].map(({ key, label }) => {
              const enabled = stickerCfg[key] !== false;
              return (
                <label
                  key={key}
                  className="inline-flex items-center gap-2 cursor-pointer text-xs text-slate-700 dark:text-slate-200"
                >
                  <input
                    type="checkbox"
                    checked={enabled}
                    disabled={disableInputs}
                    onChange={(e) => updateStickerConfig({ [key]: e.target.checked })}
                    className="rounded border-slate-300 text-blue-600 focus:ring-blue-500"
                  />
                  <span>{label}</span>
                </label>
              );
            })}
          </div>

          {/* Preferred Printer in Desktop App */}
          {isDesktopApp && installedPrinters.length > 0 && (
            <div className="pt-2.5 border-t border-slate-200 dark:border-slate-700/60 flex flex-wrap items-center justify-between gap-2">
              <div>
                <span className="text-xs font-semibold text-slate-800 dark:text-slate-100 block">
                  Dedicated Sticker Printer Device
                </span>
                <span className="text-[11px] text-slate-500 dark:text-slate-400">
                  Route sticker jobs directly to this label printer without affecting your receipt printer
                </span>
              </div>
              <select
                value={currentPrinter}
                disabled={disableInputs}
                onChange={(e) => updateStickerConfig({ preferred_printer: e.target.value })}
                className="text-xs rounded-lg max-w-xs"
              >
                <option value="">Default POS / Receipt Printer</option>
                {installedPrinters.map((p) => {
                  const pName = typeof p === "string" ? p : p?.name || "";
                  return (
                    <option key={pName} value={pName}>
                      {pName}
                    </option>
                  );
                })}
              </select>
            </div>
          )}
        </div>
      </div>
    );
  };

  const renderTemplateGrid = ({ templates, selectedId, onSelect, type }) => (
    <div className="settings-template-grid">
      {templates.map((template) => {
        const IconComponent = getIconComponent(template.icon);
        const isSelected = selectedId === template.id;
        const livePreviewUrl = buildCalibratedPreviewUrl(type, template.id, form);

        return (
          <div
            key={template.id}
            role="button"
            tabIndex={0}
            aria-pressed={isSelected}
            className={`settings-template ${isSelected ? "is-active" : ""}`}
            onClick={() => onSelect(template.id)}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                onSelect(template.id);
              }
            }}
          >
            {/* Selection indicator */}
            {isSelected && (
              <span className="settings-template-badge" aria-hidden="true">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth="3"
                    d="M5 13l4 4L19 7"
                  />
                </svg>
              </span>
            )}

            {/* Preview & Fix trigger */}
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                openFixModalForTemplate(template, type);
              }}
              className="settings-template-preview-btn"
              title="Preview, Test Print & Fix Layout"
              aria-label={`Preview and fix ${template.name} template`}
            >
              <EyeIcon aria-hidden="true" />
            </button>

            {/* Thumbnail */}
            <span className="settings-template-preview">
              <iframe
                src={livePreviewUrl}
                className="border-0"
                title={`${template.name} Thumbnail`}
              />
            </span>

            {/* Name + description */}
            <span className="settings-template-head">
              <span className="settings-template-icon">
                <IconComponent aria-hidden="true" />
              </span>
              <span className="min-w-0">
                <span className="settings-option-title block">{template.name}</span>
                <span className="settings-option-tagline block">
                  {template.description}
                </span>
              </span>
            </span>

            <span className="settings-template-note">{template.preview}</span>

            {/* Action bar: Select + Test Print + Fix Layout */}
            <div className="flex flex-col gap-1.5 w-full mt-auto">
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  if (!disableInputs) {
                    onSelect(template.id);
                  } else {
                    toast.error("You don't have permission to update settings.");
                  }
                }}
                disabled={disableInputs}
                className={`products-action w-full ${
                  isSelected
                    ? "products-action-primary theme-btn-primary"
                    : "theme-btn-secondary"
                }`}
              >
                {isSelected ? "Selected Template" : "Select Template"}
              </button>

              <div className="grid grid-cols-2 gap-1.5 w-full">
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    handleTestTemplatePrint(type, template.id, template.name);
                  }}
                  className="products-action theme-btn-secondary text-xs justify-center py-1.5"
                  title={`Send a test print of ${template.name} with current layout calibration`}
                >
                  <PrinterIcon className="w-3.5 h-3.5" aria-hidden="true" />
                  <span>Test Print</span>
                </button>

                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    if (!disableInputs) {
                      onSelect(template.id);
                    }
                    openFixModalForTemplate(template, type);
                  }}
                  className="products-action theme-btn-secondary text-xs justify-center py-1.5"
                  title={`Calibrate & fix layout for ${template.name}`}
                >
                  <WrenchScrewdriverIcon className="w-3.5 h-3.5" aria-hidden="true" />
                  <span>Fix Layout</span>
                </button>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );

  return (
    <>
      {/* ===== Invoice footer note ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <DocumentTextIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Invoice Footer Note</h2>
              <p className="settings-block-note">
                Custom message at the bottom of invoices
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-field">
            <label className="settings-label" htmlFor="setting-invoice-note">
              Footer note
            </label>
            <textarea
              id="setting-invoice-note"
              name="note"
              value={form.note}
              onChange={handleChange}
              disabled={disableInputs}
              rows={3}
              onKeyDown={(e) => {
                if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
                  e.preventDefault();
                  if (!disableInputs) handleSave();
                }
              }}
              placeholder="This note will be printed at the bottom of the invoice..."
            />
            <p className="settings-hint">
              This note will appear at the bottom of all printed invoices and receipts. Ctrl/⌘ + Enter saves.
            </p>
          </div>
        </div>
      </div>

      {/* ===== Windows Hardware Printer ===== */}
      {isDesktopApp && (
        <div className="settings-block">
          <div className="settings-block-heading">
            <div className="flex items-center gap-3 min-w-0">
              <div className="settings-block-icon">
                <PrinterIcon />
              </div>
              <div className="min-w-0">
                <h2 className="settings-block-title">
                  Windows Hardware Printer (Direct Silent Print)
                </h2>
                <p className="settings-block-note">
                  Ctrl+P and Print buttons send slips directly to this printer without any popup window
                </p>
              </div>
            </div>
          </div>

          <div className="settings-body">
            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-windows-printer">
                Target Windows Printer
              </label>
              <div className="flex flex-wrap items-center gap-2">
                <select
                  id="setting-windows-printer"
                  value={preferredDeviceName}
                  onChange={handlePreferredPrinterChange}
                  disabled={disableInputs || loadingPrinters}
                  className="flex-1 min-w-[240px]"
                >
                  <option value="">
                    Automatic Hardware Printer Detection
                    {resolvedPrinterName ? ` (Active: ${resolvedPrinterName})` : ""}
                  </option>
                  {installedPrinters
                    .filter((p) => !p.isVirtual)
                    .map((p) => (
                      <option key={p.name} value={p.name}>
                        {p.displayName || p.name}
                        {p.isOnline
                          ? " — [Online / Ready]"
                          : p.workOffline
                          ? " — [Offline / Unplugged]"
                          : ""}
                        {p.isDefault ? " (Windows Default)" : ""}
                      </option>
                    ))}
                </select>
                <button
                  type="button"
                  onClick={loadDesktopPrinters}
                  disabled={loadingPrinters}
                  className="products-action theme-btn-secondary"
                >
                  {loadingPrinters ? "Scanning..." : "Refresh Printers"}
                </button>
                <button
                  type="button"
                  onClick={handleTestDirectPrint}
                  className="products-action products-action-primary theme-btn-primary"
                >
                  <PrinterIcon className="w-4 h-4" />
                  <span>Test Direct Print</span>
                </button>
              </div>
              <p className="settings-hint">
                {resolvedPrinterName
                  ? `Active Hardware Printer: ${resolvedPrinterName} — Ctrl+P and Print buttons spool directly to this printer.`
                  : "Connect and turn on your thermal or A4 printer, then click Refresh Printers or Test Direct Print."}
              </p>
            </div>
          </div>
        </div>
      )}

      {/* ===== Printer Type ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <PrinterIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Printer Type</h2>
              <p className="settings-block-note">Select your printer type</p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-options settings-options-2">
            <label className={`settings-option ${isThermal ? "is-active" : ""}`}>
              <input
                type="radio"
                name="printer_type"
                value="thermal"
                checked={isThermal}
                onChange={handleChange}
                disabled={disableInputs}
                className="sr-only"
              />
              <span className="settings-option-head">
                <PrinterIcon aria-hidden="true" />
                <span className="settings-option-title">Thermal Printer</span>
              </span>
              <span className="settings-option-tagline">
                58mm–80mm receipt paper with a compact thermal template.
              </span>
            </label>

            <label className={`settings-option ${!isThermal ? "is-active" : ""}`}>
              <input
                type="radio"
                name="printer_type"
                value="a4"
                checked={!isThermal}
                onChange={handleChange}
                disabled={disableInputs}
                className="sr-only"
              />
              <span className="settings-option-head">
                <DocumentTextIcon aria-hidden="true" />
                <span className="settings-option-title">A4 Printer</span>
              </span>
              <span className="settings-option-tagline">
                Standard letter-size paper with a full invoice template.
              </span>
            </label>
          </div>

          <p className="settings-hint">
            {isThermal
              ? "A thermal receipt template is used for every thermal invoice."
              : "An A4 invoice template is used for every A4 invoice."}
          </p>
        </div>
      </div>

      {/* ===== Layout Category Switcher (Thermal Receipts | A4 Full Invoices | Dosage & Sticker Printer) ===== */}
      <div className="flex flex-wrap items-center gap-2 p-1.5 bg-slate-100 dark:bg-slate-800/80 rounded-2xl border border-slate-200 dark:border-slate-700/80 mb-5">
        <button
          type="button"
          onClick={() => {
            setLayoutCategory("thermal");
            if (form.printer_type !== "thermal") {
              handleChange({ target: { name: "printer_type", value: "thermal" } });
            }
          }}
          className={`flex-1 min-w-[170px] flex items-center justify-center gap-2.5 py-2.5 px-4 rounded-xl text-xs font-bold transition shadow-xs ${
            layoutCategory === "thermal"
              ? "bg-white dark:bg-slate-900 text-blue-600 dark:text-blue-400 border border-slate-200/80 dark:border-slate-700 shadow-sm"
              : "text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-white/50 dark:hover:bg-slate-900/40"
          }`}
        >
          <BoltIcon className="w-4 h-4 shrink-0" />
          <div className="text-left">
            <span className="block leading-tight">Thermal Receipts</span>
            <span className="block text-[10px] font-normal opacity-75">80mm & 58mm Roll Slips</span>
          </div>
        </button>

        <button
          type="button"
          onClick={() => {
            setLayoutCategory("a4");
            if (form.printer_type !== "a4") {
              handleChange({ target: { name: "printer_type", value: "a4" } });
            }
          }}
          className={`flex-1 min-w-[170px] flex items-center justify-center gap-2.5 py-2.5 px-4 rounded-xl text-xs font-bold transition shadow-xs ${
            layoutCategory === "a4"
              ? "bg-white dark:bg-slate-900 text-blue-600 dark:text-blue-400 border border-slate-200/80 dark:border-slate-700 shadow-sm"
              : "text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-white/50 dark:hover:bg-slate-900/40"
          }`}
        >
          <DocumentTextIcon className="w-4 h-4 shrink-0" />
          <div className="text-left">
            <span className="block leading-tight">A4 Invoices</span>
            <span className="block text-[10px] font-normal opacity-75">Standard Full-Page Sheets</span>
          </div>
        </button>

        <button
          type="button"
          onClick={() => {
            setLayoutCategory("sticker");
          }}
          className={`flex-1 min-w-[170px] flex items-center justify-center gap-2.5 py-2.5 px-4 rounded-xl text-xs font-bold transition shadow-xs ${
            layoutCategory === "sticker"
              ? "bg-white dark:bg-slate-900 text-blue-600 dark:text-blue-400 border border-slate-200/80 dark:border-slate-700 shadow-sm"
              : "text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-white/50 dark:hover:bg-slate-900/40"
          }`}
        >
          <TagIcon className="w-4 h-4 shrink-0" />
          <div className="text-left">
            <span className="block leading-tight">Dosage Stickers & Labels</span>
            <span className="block text-[10px] font-normal opacity-75">Pharmacy labels, rolls & die-cut</span>
          </div>
        </button>
      </div>

      {/* ===== Template selection ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex flex-wrap items-center justify-between gap-3 w-full">
            <div className="flex items-center gap-3 min-w-0">
              <div className="settings-block-icon">
                {isSticker ? <TagIcon /> : <DocumentTextIcon />}
              </div>
              <div className="min-w-0">
                <h2 className="settings-block-title">
                  {isSticker
                    ? "Dosage Sticker & Label Template"
                    : isThermal
                    ? "Thermal Receipt Template"
                    : "A4 Invoice Template"}
                </h2>
                <p className="settings-block-note">
                  {isSticker
                    ? "Select a pharmacy sticker template, preview bilingual Urdu dosage layouts, and test print directly"
                    : "Select a layout template, test print it immediately, or click Fix Layout to calibrate width & margins"}
                </p>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={handleTestDirectPrint}
                className="products-action products-action-primary theme-btn-primary"
              >
                <PrinterIcon className="w-4 h-4" />
                <span>Test Print Selected ({activeTemplateObj?.name})</span>
              </button>
              <button
                type="button"
                onClick={() => openFixModalForTemplate(activeTemplateObj, activeType)}
                className="products-action theme-btn-secondary"
              >
                <WrenchScrewdriverIcon className="w-4 h-4" />
                <span>Fix Layout & Calibrate</span>
              </button>
            </div>
          </div>
        </div>

        <div className="settings-body">
          {isSticker
            ? renderTemplateGrid({
                templates: STICKER_TEMPLATES,
                selectedId: activeTemplateId,
                onSelect: handleStickerTemplateSelect,
                type: "sticker",
              })
            : isThermal
            ? renderTemplateGrid({
                templates: thermalTemplates,
                selectedId: selectedThermalTemplate,
                onSelect: handleTemplateSelect,
                type: "thermal",
              })
            : renderTemplateGrid({
                templates: a4Templates,
                selectedId: selectedA4Template,
                onSelect: handleA4TemplateSelect,
                type: "a4",
              })}

          {/* Selected Template + Quick Test & Fix Bar */}
          <div className="settings-note flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-start gap-2.5 min-w-0">
              {isSticker ? (
                <TagIcon aria-hidden="true" className="shrink-0 mt-0.5" />
              ) : (
                <DocumentTextIcon aria-hidden="true" className="shrink-0 mt-0.5" />
              )}
              <div>
                <span className="settings-note-title">
                  Selected: {activeTemplateObj?.name} Template (
                  {isSticker
                    ? "Dosage Sticker & Label"
                    : isThermal
                    ? "Thermal Receipt"
                    : "A4 Invoice"}
                  )
                </span>
                <div className="text-xs opacity-90">
                  {isSticker ? (
                    <>
                      Active Calibration:{" "}
                      <strong>
                        Size {form.dosage_sticker_config?.width_mm || 72}×
                        {form.dosage_sticker_config?.height_mm || 35}mm (
                        {form.dosage_sticker_config?.printer_mode === "receipt_roll"
                          ? "Receipt Roll"
                          : "Die-Cut Label"}
                        )
                      </strong>{" "}
                      •{" "}
                      <strong>
                        Font{" "}
                        {form.dosage_sticker_config?.font_scale === "compact"
                          ? "Compact 80%"
                          : form.dosage_sticker_config?.font_scale === "large"
                          ? "Large 115%"
                          : "Normal 100%"}
                      </strong>{" "}
                      •{" "}
                      <strong>
                        Contrast{" "}
                        {form.dosage_sticker_config?.contrast_mode === "high_contrast"
                          ? "Pure Black + Bold"
                          : form.dosage_sticker_config?.contrast_mode === "extra_bold"
                          ? "Extra Bold"
                          : "Standard"}
                      </strong>{" "}
                      •{" "}
                      <strong>
                        Cutter Guard {form.dosage_sticker_config?.margin_bottom_mm ?? 4}mm
                      </strong>
                    </>
                  ) : (
                    <>
                      Active Calibration:{" "}
                      <strong>
                        Width{" "}
                        {Number(form.print_paper_width_mm || 0) > 0
                          ? `${form.print_paper_width_mm}mm`
                          : "Template Default"}
                      </strong>{" "}
                      • <strong>Font {form.print_font_scale || 100}%</strong> •{" "}
                      <strong>
                        Contrast{" "}
                        {form.print_contrast_mode === "high_contrast"
                          ? "Pure Black + Bold"
                          : form.print_contrast_mode === "extra_bold"
                          ? "Extra Bold"
                          : "Standard"}
                      </strong>{" "}
                      • <strong>Bottom Feed {form.print_margin_bottom_mm ?? 4}mm</strong>
                    </>
                  )}
                </div>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={handleTestDirectPrint}
                className="products-action products-action-primary theme-btn-primary text-xs"
              >
                <PrinterIcon className="w-4 h-4" />
                <span>{isSticker ? "Test Sticker Print" : "Test Receipt Print"}</span>
              </button>
              <button
                type="button"
                onClick={scrollToCalibrationStudio}
                className="products-action theme-btn-secondary text-xs"
              >
                <AdjustmentsHorizontalIcon className="w-4 h-4" />
                <span>Calibrate & Fix Layout Below</span>
              </button>
              {!isSticker && (
                <button
                  type="button"
                  onClick={handlePrintAlignmentRulerSlip}
                  className="products-action theme-btn-secondary text-xs"
                >
                  <ScaleIcon className="w-4 h-4" />
                  <span>Print Width Ruler Slip</span>
                </button>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* ===== Receipt / Sticker Layout Fix & Printer Calibration Studio ===== */}
      <div className="settings-block" ref={calibrationSectionRef}>
        <div className="settings-block-heading">
          <div className="flex flex-wrap items-center justify-between gap-3 w-full">
            <div className="flex items-center gap-3 min-w-0">
              <div className="settings-block-icon">
                {isSticker ? <TagIcon /> : <WrenchScrewdriverIcon />}
              </div>
              <div className="min-w-0">
                <h2 className="settings-block-title">
                  {isSticker
                    ? "Sticker & Label Layout Fix Studio"
                    : "Receipt Layout Fix & Printer Calibration Studio"}
                </h2>
                <p className="settings-block-note">
                  {isSticker
                    ? "Calibrate label roll width (72mm/80mm), die-cut dimensions (50×25mm), cutter guard feed, and pure-black Urdu contrast"
                    : "Fix right-edge price cut-off, auto-cutter slicing, faint thermal text, or paper width mismatch across all ERP receipts & ledgers"}
                </p>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              {!isSticker && (
                <button
                  type="button"
                  onClick={handlePrintAlignmentRulerSlip}
                  className="products-action theme-btn-secondary"
                  title="Prints a physical millimeter ruler (44mm to 72mm) to measure your exact thermal printhead width"
                >
                  <ScaleIcon className="w-4 h-4" />
                  <span>Print Hardware Ruler Slip</span>
                </button>
              )}
              <button
                type="button"
                onClick={handleTestDirectPrint}
                className="products-action products-action-primary theme-btn-primary"
              >
                <PrinterIcon className="w-4 h-4" />
                <span>
                  {isSticker
                    ? "Test Print Calibrated Sticker"
                    : "Test Print Calibrated Receipt"}
                </span>
              </button>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="grid grid-cols-1 xl:grid-cols-12 gap-6 items-start">
            {/* Left 7 Columns: Calibration Controls */}
            <div className="xl:col-span-7">
              {isSticker
                ? renderStickerCalibrationControls({ compact: false })
                : renderCalibrationControls({ compact: false })}
            </div>

            {/* Right 5 Columns: Live Calibrated Preview + Physical Millimeter Ruler */}
            <div className="xl:col-span-5 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-slate-50/70 dark:bg-slate-900/50 p-3.5">
              <div className="flex flex-wrap items-center justify-between gap-2 mb-2.5">
                <div>
                  <span className="text-xs font-bold uppercase tracking-wider text-slate-700 dark:text-slate-200 block">
                    Live Calibrated Preview ({activeTemplateObj?.name})
                  </span>
                  <span className="text-[11px] text-slate-500 dark:text-slate-400">
                    Updates instantly as you adjust dimensions, margins, font & contrast
                  </span>
                </div>
                <span className="px-2 py-0.5 rounded-full text-[11px] font-bold bg-blue-100 text-blue-700 dark:bg-blue-900/50 dark:text-blue-300">
                  {isSticker
                    ? `${form.dosage_sticker_config?.width_mm || 72}×${
                        form.dosage_sticker_config?.height_mm || 35
                      }mm (${
                        form.dosage_sticker_config?.printer_mode === "receipt_roll"
                          ? "Roll Cutter"
                          : "Die-Cut"
                      })`
                    : Number(form.print_paper_width_mm || 0) > 0
                    ? `${form.print_paper_width_mm}mm Printable`
                    : isThermal
                    ? "Auto Thermal Width"
                    : "Auto A4 Width"}
                </span>
              </div>

              {/* Visual Millimeter Ruler Header */}
              {isSticker ? (
                <div className="mb-2 rounded-lg bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800/60 px-2.5 py-1.5 text-[10px] font-mono text-emerald-900 dark:text-emerald-200">
                  <div className="flex justify-between font-bold">
                    <span>0mm</span>
                    <span>38mm</span>
                    <span>50mm (Label)</span>
                    <span>72mm (Roll)</span>
                    <span>80mm</span>
                  </div>
                  <div className="w-full h-1.5 bg-emerald-200 dark:bg-emerald-800 rounded-full mt-1 overflow-hidden">
                    <div
                      className="h-full bg-emerald-600 transition-all"
                      style={{
                        width: `${Math.min(
                          100,
                          Math.max(
                            25,
                            ((Number(form.dosage_sticker_config?.width_mm || 72) || 72) / 80) *
                              100
                          )
                        )}%`,
                      }}
                    />
                  </div>
                </div>
              ) : (
                isThermal && (
                  <div className="mb-2 rounded-lg bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-800/60 px-2.5 py-1.5 text-[10px] font-mono text-amber-900 dark:text-amber-200">
                    <div className="flex justify-between font-bold">
                      <span>0mm</span>
                      <span>48mm (58 Roll)</span>
                      <span>68mm</span>
                      <span>72mm (80 Roll)</span>
                      <span>80mm</span>
                    </div>
                    <div className="w-full h-1.5 bg-amber-200 dark:bg-amber-800 rounded-full mt-1 overflow-hidden">
                      <div
                        className="h-full bg-blue-600 transition-all"
                        style={{
                          width: `${Math.min(
                            100,
                            Math.max(
                              35,
                              ((Number(form.print_paper_width_mm || 72) || 72) / 80) * 100
                            )
                          )}%`,
                        }}
                      />
                    </div>
                  </div>
                )
              )}

              <div className="rounded-lg border border-slate-200 dark:border-slate-700 bg-white overflow-hidden shadow-inner flex justify-center p-2">
                <iframe
                  key={buildCalibratedPreviewUrl(activeType, activeTemplateId, form)}
                  src={buildCalibratedPreviewUrl(activeType, activeTemplateId, form)}
                  className="border-0 w-full"
                  style={{ minHeight: "420px" }}
                  title="Live Calibrated Preview"
                />
              </div>

              <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                <button
                  type="button"
                  onClick={handleTestDirectPrint}
                  className="products-action theme-btn-secondary flex-1 justify-center"
                >
                  <PrinterIcon className="w-4 h-4" />
                  <span>Test Print Now</span>
                </button>
                <button
                  type="button"
                  onClick={handleSave}
                  disabled={disableInputs || saving}
                  className="products-action products-action-primary theme-btn-primary flex-1 justify-center"
                >
                  <CheckCircleIcon className="w-4 h-4" />
                  <span>{saving ? "Saving..." : "Save Layout Fix"}</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* ===== Template Preview, Test Print & Fix Layout Modal ===== */}
      {showPreviewModal && previewingTemplate && (
        <div
          className="settings-modal"
          role="dialog"
          aria-modal="true"
          aria-labelledby="template-preview-title"
          onClick={(e) => {
            if (e.target === e.currentTarget) {
              setShowPreviewModal(false);
              setPreviewingTemplate(null);
            }
          }}
        >
          <div
            className="settings-modal-card settings-modal-card-lg"
            style={{ maxWidth: "1040px", width: "95vw" }}
          >
            {/* Header */}
            <div className="settings-modal-heading">
              <div className="settings-block-icon">
                <WrenchScrewdriverIcon />
              </div>
              <div className="min-w-0">
                <h3 id="template-preview-title" className="settings-block-title">
                  {previewingTemplate.name} Template — Test Print & Fix Layout
                </h3>
                <p className="settings-block-note">
                  {previewingTemplate.description} • Adjust dimensions, margins, font size & contrast live and test print immediately
                </p>
              </div>
              <button
                type="button"
                onClick={() => {
                  setShowPreviewModal(false);
                  setPreviewingTemplate(null);
                }}
                className="products-action"
                style={{ marginLeft: "auto" }}
                aria-label="Close preview"
              >
                <svg
                  className="w-4 h-4"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth="2"
                    d="M6 18L18 6M6 6l12 12"
                  />
                </svg>
              </button>
            </div>

            {/* Body: 2-Column Live Preview + Layout Fix Controls */}
            <div className="settings-body max-h-[75vh] overflow-y-auto">
              <div className="grid grid-cols-1 lg:grid-cols-12 gap-5 items-start">
                {/* Left Column: Live Preview */}
                <div className="lg:col-span-5 flex flex-col items-center bg-slate-100 dark:bg-slate-900/70 rounded-xl p-3 border border-slate-200 dark:border-slate-700">
                  <div className="w-full flex items-center justify-between text-xs font-semibold text-slate-600 dark:text-slate-300 mb-2">
                    <span>
                      {previewingType === "sticker"
                        ? "Live Calibrated Sticker Preview"
                        : "Live Calibrated Receipt Preview"}
                    </span>
                    <span>
                      {previewingType === "sticker"
                        ? `${form.dosage_sticker_config?.width_mm || 72}×${
                            form.dosage_sticker_config?.height_mm || 35
                          }mm`
                        : Number(form.print_paper_width_mm || 0) > 0
                        ? `${form.print_paper_width_mm}mm`
                        : "Default Width"}
                    </span>
                  </div>
                  <div
                    className="settings-template-frame bg-white rounded-lg shadow-sm w-full"
                    style={{
                      maxWidth:
                        previewingType === "a4"
                          ? "100%"
                          : previewingType === "sticker"
                          ? "360px"
                          : previewingTemplate.id === "minimal" ||
                            previewingTemplate.id === "compact"
                          ? "320px"
                          : "400px",
                    }}
                  >
                    <iframe
                      key={buildCalibratedPreviewUrl(
                        previewingType,
                        previewingTemplate.id,
                        form
                      )}
                      src={buildCalibratedPreviewUrl(
                        previewingType,
                        previewingTemplate.id,
                        form
                      )}
                      className="border-0"
                      style={{ minHeight: "430px", width: "100%" }}
                      title={`${previewingTemplate.name} Template Preview`}
                    />
                  </div>
                </div>

                {/* Right Column: Interactive Layout Fix Controls */}
                <div className="lg:col-span-7">
                  {previewingType === "sticker"
                    ? renderStickerCalibrationControls({ compact: true })
                    : renderCalibrationControls({ compact: true })}
                </div>
              </div>
            </div>

            {/* Footer */}
            <div className="settings-modal-footer">
              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  onClick={() =>
                    handleTestTemplatePrint(
                      previewingType,
                      previewingTemplate.id,
                      previewingTemplate.name
                    )
                  }
                  className="products-action theme-btn-secondary"
                >
                  <PrinterIcon className="w-4 h-4" aria-hidden="true" />
                  <span>Test Print {previewingTemplate.name}</span>
                </button>

                {previewingType !== "sticker" && (
                  <button
                    type="button"
                    onClick={handlePrintAlignmentRulerSlip}
                    className="products-action theme-btn-secondary"
                  >
                    <ScaleIcon className="w-4 h-4" aria-hidden="true" />
                    <span>Print Width Ruler</span>
                  </button>
                )}
              </div>

              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={() => {
                    setShowPreviewModal(false);
                    setPreviewingTemplate(null);
                  }}
                  className="products-action theme-btn-tertiary"
                >
                  Close
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    if (disableInputs) {
                      toast.error("You don't have permission to update settings.");
                      return;
                    }
                    if (previewingType === "sticker") {
                      updateStickerConfig({ template: previewingTemplate.id });
                    } else if (previewingType === "a4") {
                      handleA4TemplateSelect(previewingTemplate.id);
                    } else {
                      handleTemplateSelect(previewingTemplate.id);
                    }
                    await handleSave();
                    setShowPreviewModal(false);
                    setPreviewingTemplate(null);
                  }}
                  disabled={disableInputs || saving}
                  className="products-action products-action-primary theme-btn-primary"
                >
                  <CheckCircleIcon className="w-4 h-4" />
                  <span>
                    {saving
                      ? "Saving..."
                      : `Use ${previewingTemplate.name} & Save Layout`}
                  </span>
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
