// src/pages/purchase-orders/forecast.jsx
import { useEffect, useMemo, useRef, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import {
  ArrowPathIcon,
  PlayCircleIcon,
  PrinterIcon,
  CalculatorIcon,
  CalendarIcon,
  ShieldCheckIcon,
  CubeIcon,
  TagIcon,
  BuildingStorefrontIcon,
} from "@heroicons/react/24/solid";
import { usePermissions } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";
import BrandSearch from "../components/BrandSearch.jsx";
import SupplierSearch from "../components/SupplierSearch.jsx";

// Reusable components
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
  TextSearch,
} from "@/components";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  return getContrastText(primaryHoverColor || primaryColor);
};

// Section configuration with color schemes - will use dynamic theme colors
const SECTION_CONFIG = {
  core: {
    key: 'primary',
  },
  management: {
    key: 'secondary',
  },
};

// Helper to get color value from theme
const getThemeColor = (theme, colorKey, variant = 'color') => {
  if (!theme) return '#3b82f6';
  const key = `${colorKey}_${variant}`;
  return theme[key] || '#3b82f6';
};

// Helper to generate section styles from theme
const getSectionStyles = (theme, colorKey) => {
  const baseColor = getThemeColor(theme, colorKey, 'color');
  const hoverColor = getThemeColor(theme, colorKey, 'hover');
  const lightColor = getThemeColor(theme, colorKey, 'light');
  
  return {
    gradient: `from-[${baseColor}] to-[${hoverColor}]`,
    bgLight: `bg-[${lightColor}]`,
    bgDark: `dark:bg-[${lightColor}]`,
    borderColor: `border-[${baseColor}]/30 dark:border-[${baseColor}]/30`,
    iconColor: `text-[${baseColor}] dark:text-[${baseColor}]`,
    ringColor: `ring-[${baseColor}]/30`,
  };
};

export default function PurchaseOrder() {
  const today = new Date().toISOString().split("T")[0];

  // 🔒 permissions (support both has() and canFor())
  const { loading: permsLoading, has, canFor } = usePermissions?.() || {};
  const canView =
    typeof has === "function"
      ? has("purchase-order.view")
      : typeof canFor === "function"
      ? !!canFor("purchase-order")?.view
      : true;
  const canGenerate =
    typeof has === "function"
      ? has("purchase-order.generate")
      : typeof canFor === "function"
      ? !!canFor("purchase-order")?.create
      : true;

  // Get dark mode state and theme colors
  const { theme, isDark } = useTheme();

  // Memoize theme colors for performance
  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#3b82f6',
        primaryHover: '#2563eb',
        primaryLight: '#dbeafe',
        secondary: '#8b5cf6',
        secondaryHover: '#7c3aed',
        secondaryLight: '#ede9fe',
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
        tertiaryLight: '#cffafe',
        emerald: '#10b981',
        emeraldHover: '#059669',
        emeraldLight: '#d1fae5',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
      tertiary: theme.tertiary_color || '#06b6d4',
      tertiaryHover: theme.tertiary_hover || '#0891b2',
      tertiaryLight: theme.tertiary_light || '#cffafe',
      emerald: theme.success_color || '#10b981',
      emeraldHover: '#059669',
      emeraldLight: '#d1fae5',
    };
  }, [theme]);

  // Calculate text colors based on background brightness
  const primaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.primary, themeColors.primaryHover), 
    [themeColors.primary, themeColors.primaryHover]
  );
  
  const secondaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.secondary, themeColors.secondaryHover), 
    [themeColors.secondary, themeColors.secondaryHover]
  );
  
  const emeraldTextColor = useMemo(() => 
    getButtonTextColor(themeColors.emerald, themeColors.emeraldHover), 
    [themeColors.emerald, themeColors.emeraldHover]
  );

  // Get section styles for core and management
  const coreStyles = useMemo(() => getSectionStyles(themeColors, 'primary'), [themeColors]);
  const managementStyles = useMemo(() => getSectionStyles(themeColors, 'secondary'), [themeColors]);

  // Get button style from theme
  const buttonStyle = theme?.button_style || 'rounded';
  
  // Get button style classes and styles based on theme button_style
  const getButtonClasses = useMemo(() => {
    const radiusMap = {
      'rounded': 'rounded-lg',
      'outlined': 'rounded-lg',
      'soft': 'rounded-xl',
    };
    const radiusClass = radiusMap[buttonStyle] || 'rounded-lg';
    
    if (buttonStyle === 'outlined') {
      return {
        primary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.primary,
            color: themeColors.primary,
            backgroundColor: 'transparent',
          }
        },
        secondary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.secondary,
            color: themeColors.secondary,
            backgroundColor: 'transparent',
          }
        },
        tertiary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.tertiary,
            color: themeColors.tertiary,
            backgroundColor: 'transparent',
          }
        },
        glass: {
          className: radiusClass,
          style: {
            background: isDark ? "rgba(71,85,105,0.6)" : "rgba(255,255,255,0.8)",
            backdropFilter: "blur(8px)",
            border: isDark ? "1px solid rgba(255,255,255,0.1)" : "1px solid rgba(0,0,0,0.1)",
            color: isDark ? "#f1f5f9" : "#1f2937",
          }
        },
      };
    }
    
    // Filled styles for rounded and soft
    return {
      primary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
          color: primaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
        }
      },
      secondary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
          color: secondaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`,
        }
      },
      tertiary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})`,
          color: secondaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40`,
        }
      },
      glass: {
        className: radiusClass,
        style: {
          background: isDark ? "rgba(71,85,105,0.6)" : "rgba(255,255,255,0.8)",
          backdropFilter: "blur(8px)",
          border: isDark ? "1px solid rgba(255,255,255,0.1)" : "1px solid rgba(0,0,0,0.1)",
          color: isDark ? "#f1f5f9" : "#1f2937",
        }
      },
    };
  }, [buttonStyle, themeColors, primaryTextColor, secondaryTextColor, isDark]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnTertiary = getButtonClasses.tertiary;
  const btnGlass = getButtonClasses.glass;

  const [dateFrom, setDateFrom] = useState(today);
  const [dateTo, setDateTo] = useState(today);
  const [projectedDays, setProjectedDays] = useState(7);

  const [safetyPacks, setSafetyPacks] = useState(1);
  const [moqPacks, setMoqPacks] = useState(0);

const [supplier, setSupplier] = useState(null);
  const [brand, setBrand] = useState(null);
  const [supplierSearchOpen, setSupplierSearchOpen] = useState(false);
  const [brandSearchOpen, setBrandSearchOpen] = useState(false);

const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);

  // === printer type (a4 | thermal) — respects saved setting ===
  const [printerType, setPrinterType] = useState("a4");

  // === keyboard navigation state/refs ===
  const [selectedIndex, setSelectedIndex] = useState(-1);
  const tableWrapRef = useRef(null);
  const inputRefs = useRef({});
  const rowRefs = useRef({});

  const printBtnRef = useRef(null);

  // Load saved printer preference
  useEffect(() => {
    axios
      .get("/api/settings")
      .then(({ data }) => {
        if (data?.printer_type) setPrinterType(String(data.printer_type).toLowerCase());
      })
      .catch(() => {});
  }, []);

  // Alt+P: print
  useEffect(() => {
    const onKey = (e) => {
      if (!e.altKey) return;
      const key = (e.key || "").toLowerCase();
      if (key !== "p") return;
      e.preventDefault();
      doPrint();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [rows, canView, printerType]);

  const fmt2 = (v) => Number(v ?? 0).toFixed(2);

  // Rows to print: only products with order_packs > 0
  const printRows = useMemo(
    () => rows.filter((r) => Number(r.order_packs || 0) > 0),
    [rows]
  );

  const printTotalPacks = useMemo(
    () => printRows.reduce((sum, r) => sum + Number(r.order_packs || 0), 0),
    [printRows]
  );

  // Build a self-contained HTML document for A4 or thermal (80mm)
  const buildPrintHtml = (type) => {
    const isA4 = type === "a4";
    const dateLabel = new Date().toLocaleDateString();
    const supplierLabel = supplier?.label || (rows[0]?.supplier_name || "—");
    const brandLabel = brand?.label || (rows[0]?.brand_name || "—");

    const esc = (s) =>
      String(s ?? "")
        .replace(/&/g, "&" + "amp;")
        .replace(/</g, "&" + "lt;")
        .replace(/>/g, "&" + "gt;")
        .replace(/"/g, "&" + "quot;");

    const rowsHtml = printRows
      .map(
        (r) => `
          <tr>
            <td class="name">${esc(r.product_name)}</td>
            <td class="qty">${Number(r.order_packs || 0)}</td>
          </tr>`
      )
      .join("");

    // Thermal (80mm) styles
    const thermalCss = `
      @page { size: 80mm auto; margin: 0; }
      * { box-sizing: border-box; }
      body { margin:0; padding:0; color:#000; background:#fff; font-family:'Courier New',monospace; font-weight:700; font-size:12px; line-height:1.35; -webkit-print-color-adjust:exact; print-color-adjust:exact; }
      .receipt { width:80mm; max-width:100%; margin:0 auto; padding:8px 6px; }
      .center { text-align:center; }
      .store { font-size:18px; text-align:center; }
      .meta { text-align:center; font-size:10px; }
      .hr { border-top:1px dashed #000; margin:5px 0; }
      .double-hr { border-top:2px double #000; margin:6px 0; }
      .pair { display:flex; justify-content:space-between; }
      .pair + .pair { margin-top:2px; }
      table { width:100%; border-collapse:collapse; }
      thead th { text-align:left; padding:2px 0; border-bottom:1px solid #000; }
      tbody td { padding:2px 0; border-bottom:1px solid #000; }
      td.qty, th.qty { text-align:right; white-space:nowrap; }
      .total { font-size:13px; font-weight:bold; margin-top:4px; display:flex; justify-content:space-between; }
      .foot { margin-top:8px; text-align:center; font-size:9px; }
    `;

    // A4 styles
    const a4Css = `
      @page { size: A4; margin: 14mm; }
      * { box-sizing: border-box; }
      body { margin:0; padding:0; color:#111; background:#fff; font-family:Arial,Helvetica,sans-serif; }
      .page { width:100%; }
      .header { display:flex; align-items:center; gap:14px; border-bottom:1px solid #ccc; padding-bottom:12px; }
      .store h1 { margin:0; font-size:20px; letter-spacing:.4px; }
      .store .meta { margin-top:4px; font-size:12px; color:#666; line-height:1.4; }
      .title-row { display:flex; justify-content:space-between; align-items:baseline; margin:16px 0 6px; }
      .title-row .title { font-size:18px; font-weight:700; }
      .grid { display:grid; grid-template-columns:repeat(3,1fr); gap:6px 16px; font-size:12px; margin-bottom:10px; }
      .grid .lbl { color:#666; }
      .grid .val { font-weight:600; }
      table { width:100%; border-collapse:collapse; margin-top:8px; font-size:12px; }
      thead th { text-align:left; border-bottom:1px solid #ccc; padding:7px 6px; }
      tbody td { border-bottom:1px dashed #e0e0e0; padding:7px 6px; }
      td.qty, th.qty { text-align:right; }
      .footer-total { width:45%; margin-left:auto; border:1px solid #ccc; border-radius:6px; overflow:hidden; margin-top:14px; }
      .footer-total .row { display:flex; justify-content:space-between; padding:9px 12px; }
      .footer-total .row.total { font-weight:800; }
      .foot { margin-top:24px; text-align:center; font-size:12px; color:#666; }
    `;

    return `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8"/>
<title>Purchase Order</title>
<style>${isA4 ? a4Css : thermalCss}</style>
</head>
<body>
${isA4 ? `
<div class="page">
  <div class="header">
    <div class="store">
      <h1>Purchase Order</h1>
      <div class="meta">${esc(supplierLabel)}</div>
    </div>
  </div>
  <div class="title-row">
    <div class="title">Purchase Order</div>
    <div class="meta">Date: ${dateLabel}</div>
  </div>
  <div class="grid">
    <div><span class="lbl">Supplier:</span> <span class="val">${esc(supplierLabel)}</span></div>
    <div><span class="lbl">Brand:</span> <span class="val">${esc(brandLabel)}</span></div>
    <div><span class="lbl">Items:</span> <span class="val">${printRows.length}</span></div>
  </div>
  <table>
    <thead>
      <tr>
        <th style="width:85%">Product</th>
        <th class="qty" style="width:15%">Order Packs</th>
      </tr>
    </thead>
    <tbody>${rowsHtml}</tbody>
  </table>
  <div class="footer-total">
    <div class="row total"><div>Total Packs</div><div>${printTotalPacks}</div></div>
  </div>
  <div class="foot">Generated on ${dateLabel}</div>
</div>
` : `
<div class="receipt">
  <div class="store">PURCHASE ORDER</div>
  <div class="meta">${esc(supplierLabel)}</div>
  <div class="hr"></div>
  <div class="pair"><div>Date</div><div>${dateLabel}</div></div>
  <div class="pair"><div>Brand</div><div>${esc(brandLabel)}</div></div>
  <div class="pair"><div>Items</div><div>${printRows.length}</div></div>
  <div class="double-hr"></div>
  <table>
    <thead>
      <tr>
        <th>Product</th>
        <th class="qty">Packs</th>
      </tr>
    </thead>
    <tbody>${rowsHtml}</tbody>
  </table>
  <div class="hr"></div>
  <div class="total"><div>TOTAL PACKS</div><div>${printTotalPacks}</div></div>
  <div class="foot">Generated on ${dateLabel}</div>
</div>
`}
</body>
</html>`;
  };

  const doPrint = (type = printerType) => {
    if (!canView) return toast.error("You don't have permission to view/print.");
    if (!rows.length) return toast.error("Nothing to print.");
    if (!printRows.length) return toast.error("No products with Order Packs > 0 to print.");

    const html = buildPrintHtml(type);

    const width = type === "thermal" ? 400 : 900;
    const height = 700;
    const left = Math.max(0, (window.screenX || 0) + (window.outerWidth - width) / 2);
    const top = Math.max(0, (window.screenY || 0) + (window.outerHeight - height) / 2);
    const features = [
      `width=${width}`,
      `height=${height}`,
      `left=${left}`,
      `top=${top}`,
      "menubar=no",
      "toolbar=no",
      "location=no",
      "status=no",
      "scrollbars=yes",
      "resizable=yes",
    ].join(",");

    const w = window.open("", "poPrintWin", features);
    if (!w) return toast.error("Popup blocked. Please allow popups to print.");

    w.document.open();
    w.document.write(html);
    w.document.close();

    const trigger = () => {
      try { w.focus(); w.print(); } catch {}
    };
    w.onload = trigger;
    setTimeout(trigger, 400);
  };

  const handleFetch = async () => {
    if (!canGenerate) return toast.error("You don't have permission to generate.");
    if (!dateFrom || !dateTo) return toast.error("Please select both dates.");
    if (!projectedDays || projectedDays <= 0) return toast.error("Projected Days must be at least 1.");

    setLoading(true);
    try {
      const params = {
        date_from: dateFrom,
        date_to: dateTo,
        projected_days: projectedDays,
        safety_packs: safetyPacks,
        moq_packs: moqPacks,
      };
      if (supplier) params.supplier_id = supplier.value;
      if (brand) params.brand_id = brand.value;

      const { data } = await axios.get("/api/purchase-orders/forecast", { params });

      const mapped = (data.items || []).map((r, idx) => {
        const order_packs = r.suggested_packs ?? 0;
        const order_units = order_packs * (r.pack_size ?? 1);
        const order_amount = order_packs * (r.pack_price ?? 0);
        return { ...r, order_packs, order_units, order_amount, _rowId: `${r.product_id}-${idx}` };
      });

      setRows(mapped);
      setSelectedIndex(mapped.length ? 0 : -1);
      toast.success("Forecast ready.");
    } catch (err) {
      console.error(err);
      toast.error("Failed to load forecast.");
    } finally {
      setLoading(false);
    }
  };

  const pruneNoPackPrice = () => {
    if (!rows.length) return;
    const before = rows.length;
    const kept = rows.filter((r) => Number(r.pack_purchase_price || 0) > 0);
    const removed = before - kept.length;
    setRows(kept);
    setSelectedIndex((i) => {
      const newLen = kept.length;
      if (newLen === 0) return -1;
      return Math.min(i, newLen - 1);
    });
    toast[removed > 0 ? "success" : "custom"](
      removed > 0 ? `Removed ${removed} item(s) without pack purchase price.` : "No rows without pack purchase price."
    );
  };

  const totals = useMemo(() => {
    let packs = 0, units = 0, amount = 0;
    for (const r of rows) {
      packs  += Number(r.order_packs || 0);
      units  += Number(r.order_units || 0);
      amount += Number(r.order_amount || 0);
    }
    return { packs, units, amount };
  }, [rows]);

  const updateOrderPacks = (rowId, value) => {
    setRows((prev) =>
      prev.map((r) => {
        if (r._rowId !== rowId) return r;
        const v = Math.max(0, parseInt(value || 0, 10));
        const units = v * (r.pack_size ?? 1);
        const amt   = v * (r.pack_price ?? 0);
        return { ...r, order_packs: v, order_units: units, order_amount: amt };
      })
    );
  };

  // Async server-side search (prefix only)
  const makeLoadOptions = (endpoint) => {
    let timeoutId = null;
    let lastReject = null;

    return (inputValue, callback) => {
      if (lastReject) {
        lastReject("Aborted");
        lastReject = null;
      }
      if (timeoutId) clearTimeout(timeoutId);

      const query = (inputValue || "").trim();
      if (!query) {
        callback([]);
        return;
      }

      timeoutId = setTimeout(async () => {
        try {
          const controller = new AbortController();
          lastReject = controller.abort.bind(controller);
          const { data } = await axios.get(endpoint, {
            signal: controller.signal,
            params: { q: query, mode: "prefix", limit: 30 },
          });

          const list = Array.isArray(data) ? data : (data?.data || []);
          const options = list.map((x) => ({ value: x.id, label: x.name }));
          callback(options);
        } catch (e) {
          if (axios.isCancel?.(e)) return;
          console.warn(e);
          callback([]);
        } finally {
          lastReject = null;
        }
      }, 250);
    };
  };

  const loadSupplierOptions = makeLoadOptions("/api/suppliers/search");
  const loadBrandOptions    = makeLoadOptions("/api/brands/search");

  // Dynamic select styles based on dark mode
  const getSelectStyles = (isDarkMode = false) => ({
    control: (base) => ({
      ...base,
      minHeight: 32,
      height: 32,
      fontSize: 12,
      background: isDarkMode ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.7)",
      backdropFilter: "blur(6px)",
      borderRadius: 10,
      borderColor: isDarkMode ? "rgba(71,85,105,0.8)" : "rgba(226,232,240,0.7)",
      boxShadow: isDarkMode ? "0 1px 2px rgba(0,0,0,0.2)" : "none",
      color: isDarkMode ? "#f1f5f9" : "#111827",
    }),
    valueContainer: (base) => ({ ...base, height: 32, padding: "0 8px" }),
    indicatorsContainer: (base) => ({ ...base, height: 32 }),
    input: (base) => ({ ...base, margin: 0, padding: 0, color: isDarkMode ? "#f1f5f9" : "#111827" }),
    singleValue: (base) => ({ ...base, color: isDarkMode ? "#f1f5f9" : "#111827" }),
    placeholder: (base) => ({ ...base, color: isDarkMode ? "#64748b" : "#9ca3af" }),
    menu: (base) => ({ 
      ...base, 
      fontSize: 12, 
      borderRadius: 10, 
      overflow: "hidden",
      backgroundColor: isDarkMode ? "rgba(30,41,59,0.95)" : "rgba(255,255,255,0.95)",
      backdropFilter: "blur(10px)",
      boxShadow: isDarkMode ? "0 10px 30px -10px rgba(0,0,0,0.4)" : "0 10px 30px -10px rgba(30,64,175,0.18)",
      border: isDarkMode ? "1px solid rgba(71,85,105,0.5)" : "none",
    }),
    option: (base, state) => ({ 
      ...base, 
      fontSize: 12,
      backgroundColor: isDarkMode
        ? state.isFocused
          ? "rgba(71,85,105,1)"
          : "rgba(51,65,85,1)"
        : state.isFocused
          ? "rgba(241,245,249,1)"
          : "rgba(255,255,255,1)",
      color: isDarkMode ? "#f1f5f9" : "#111827",
      cursor: "pointer",
    }),
    menuPortal: (base) => ({ ...base, zIndex: 9999 }),
  });

  // Keyboard navigation handlers
  useEffect(() => {
    if (selectedIndex < 0 || selectedIndex >= rows.length) return;
    const row = rows[selectedIndex];
    const inputEl = inputRefs.current[row._rowId];
    const trEl = rowRefs.current[row._rowId];

    if (trEl && tableWrapRef.current) {
      const wrap = tableWrapRef.current;
      const trBox = trEl.getBoundingClientRect();
      const wrapBox = wrap.getBoundingClientRect();
      if (trBox.top < wrapBox.top + 40 || trBox.bottom > wrapBox.bottom - 40) {
        trEl.scrollIntoView({ block: "nearest" });
      }
    }
    if (inputEl) {
      requestAnimationFrame(() => {
        inputEl.focus();
        inputEl.select();
      });
    }
  }, [selectedIndex, rows]);

  const onKeyDownTable = (e) => {
    const key = e.key;
    if (key === "ArrowDown" || key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((idx) => {
        const next =
          key === "ArrowDown"
            ? Math.min((idx < 0 ? -1 : idx) + 1, rows.length - 1)
            : Math.max((idx < 0 ? 0 : idx) - 1, 0);
        return next;
      });
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!canView) return <div className="p-6 text-sm text-gray-700">You don't have permission to view Purchase Order (Forecast).</div>;

  return (
    <div className="p-4 space-y-3 print:p-0" onKeyDown={onKeyDownTable}>
{/* ===== Premium Gradient Hero Header ===== */}
      <div
        className="relative overflow-hidden rounded-2xl shadow-lg"
        style={{
          background: `linear-gradient(135deg, ${themeColors.secondary}, ${themeColors.primary}, ${themeColors.primaryHover})`,
        }}
      >
        {/* Decorative blurred blobs */}
        <div
          className="absolute -top-16 -right-16 w-64 h-64 rounded-full opacity-30 blur-3xl pointer-events-none"
          style={{ backgroundColor: "#ffffff" }}
        />
        <div
          className="absolute -bottom-20 -left-10 w-72 h-72 rounded-full opacity-20 blur-3xl pointer-events-none"
          style={{ backgroundColor: themeColors.tertiary }}
        />

        {/* Hero Top */}
        <div className="relative flex items-center justify-between px-5 py-4">
          <div className="flex items-center gap-3">
            <div
              className="p-2.5 rounded-xl shadow-inner"
              style={{ backgroundColor: "rgba(255,255,255,0.18)", backdropFilter: "blur(4px)" }}
            >
              <CalculatorIcon className="w-6 h-6 text-white" />
            </div>
            <div>
              <h1 className="text-xl font-extrabold tracking-wide text-white leading-none">Purchase Order (Forecast)</h1>
              <p className="text-xs text-white/80 mt-1">
                {loading ? (
                  <span className="inline-flex items-center gap-1">
                    <ArrowPathIcon className="w-3.5 h-3.5 animate-spin" />
                    Loading...
                  </span>
                ) : (
                  `${rows.length} item(s) in forecast`
                )}
              </p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2 flex-wrap justify-end">
            {/* Generate Button */}
            <button
              onClick={handleFetch}
              disabled={loading || !canGenerate}
              className={`h-10 px-4 inline-flex items-center gap-1.5 rounded-xl text-xs font-bold shadow-lg transition-all duration-200 ${
                canGenerate
                  ? "text-white bg-white/95 hover:bg-white"
                  : "bg-white/20 text-white/60 cursor-not-allowed"
              }`}
              style={canGenerate ? { color: themeColors.primaryHover } : {}}
              title={!canGenerate ? "Not permitted" : "Generate forecast (Alt+G)"}
            >
              <PlayCircleIcon className="w-4 h-4" />
              {loading ? "Loading…" : "Generate"}
            </button>

            {/* Remove Zero Button */}
            <button
              onClick={pruneNoPackPrice}
              disabled={!rows.length}
              className={`h-10 px-4 inline-flex items-center gap-1.5 rounded-xl text-xs font-bold shadow-lg transition-all duration-200 ${
                rows.length
                  ? "text-white bg-white/95 hover:bg-white"
                  : "bg-white/20 text-white/60 cursor-not-allowed"
              }`}
              style={rows.length ? { color: themeColors.primaryHover } : {}}
              title="Remove products with no Pack Purchase Price"
            >
              <span className="text-base leading-none">×</span>
              Remove Zero
            </button>

            <div className="w-px h-8 bg-white/30 mx-1" />

            {/* Refresh Button */}
            <button
              onClick={() => window.location.reload()}
              className="h-10 px-4 inline-flex items-center gap-1.5 rounded-xl text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20 transition-all duration-200 shadow-lg"
              title="Refresh"
              aria-label="Refresh page"
            >
              <ArrowPathIcon className="w-4 h-4" />
              <span className="hidden sm:inline">Refresh</span>
            </button>

            {/* Printer Type Toggle */}
            <div
              className="flex items-center rounded-xl p-0.5 backdrop-blur-sm border border-white/20 bg-white/15 overflow-hidden"
              title="Select print format"
            >
              {["a4", "thermal"].map((t) => (
                <button
                  key={t}
                  type="button"
                  onClick={() => setPrinterType(t)}
                  className={`h-8 px-3 text-[10px] font-bold uppercase tracking-wide rounded-lg transition-all duration-200 ${
                    printerType === t
                      ? "text-white shadow"
                      : "text-white/70 hover:text-white"
                  }`}
                  style={printerType === t ? { backgroundColor: "rgba(255,255,255,0.22)", backdropFilter: "blur(4px)" } : {}}
                >
                  {t}
                </button>
              ))}
            </div>

            {/* Print Button */}
            <button
              ref={printBtnRef}
              onClick={doPrint}
              className="h-10 px-4 inline-flex items-center gap-1.5 rounded-xl text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20 transition-all duration-200 shadow-lg"
              title={`Print ${printerType === "thermal" ? "Thermal (80mm)" : "A4"} (Alt+P)`}
            >
              <PrinterIcon className="w-4 h-4" />
              <span className="hidden sm:inline">Print</span>
            </button>
          </div>
        </div>

        {/* Integrated Filters */}
        <div className="relative px-5 pb-4">
          <div
            className="grid grid-cols-2 md:grid-cols-12 gap-3 rounded-xl p-3"
            style={{ backgroundColor: "rgba(255,255,255,0.12)", backdropFilter: "blur(8px)", border: "1px solid rgba(255,255,255,0.15)" }}
          >
            <div className="col-span-1 md:col-span-2 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">From</label>
              <input
                type="date"
                value={dateFrom}
                onChange={(e) => setDateFrom(e.target.value)}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/60 bg-white/15 border border-white/20 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/40 [color-scheme:dark]"
              />
            </div>
            <div className="col-span-1 md:col-span-2 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">To</label>
              <input
                type="date"
                value={dateTo}
                onChange={(e) => setDateTo(e.target.value)}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/60 bg-white/15 border border-white/20 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/40 [color-scheme:dark]"
              />
            </div>
            <div className="col-span-1 md:col-span-1 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">Proj. Days</label>
              <input
                type="number"
                min={1}
                value={projectedDays}
                onChange={(e) => setProjectedDays(parseInt(e.target.value || 0, 10))}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/60 bg-white/15 border border-white/20 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/40 no-spinners"
              />
            </div>

            <div className="col-span-1 md:col-span-1 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">Safety</label>
              <input
                type="number"
                min={0}
                value={safetyPacks}
                onChange={(e) => setSafetyPacks(parseInt(e.target.value || 0, 10))}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/60 bg-white/15 border border-white/20 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/40 no-spinners"
              />
            </div>

            <div className="col-span-1 md:col-span-1 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">MOQ</label>
              <input
                type="number"
                min={0}
                value={moqPacks}
                onChange={(e) => setMoqPacks(parseInt(e.target.value || 0, 10))}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/60 bg-white/15 border border-white/20 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/40 no-spinners"
              />
            </div>

<div className="col-span-2 md:col-span-2 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">Brand</label>
              <div className={`w-full h-9 rounded-lg border flex items-center gap-1 overflow-hidden transition-all ${
                  brand
                    ? "border-white/60 bg-white/95 text-gray-800"
                    : "border-white/30 bg-white/15 text-white/80"
                }`}>
                <button
                  type="button"
                  onClick={() => setBrandSearchOpen(true)}
                  className="flex-1 h-full px-3 text-left text-xs flex items-center gap-2 min-w-0"
                  title={brand?.label || "Click to search brand..."}
                >
                  <TagIcon className="w-4 h-4 flex-shrink-0" />
                  {brand ? (
                    <span className="truncate font-medium">{brand.label}</span>
                  ) : (
                    <span className="truncate">Search brand...</span>
                  )}
                </button>
                {brand && (
                  <button
                    type="button"
                    onClick={() => setBrand(null)}
                    className="h-full px-2 flex items-center justify-center hover:bg-black/10 dark:hover:bg-white/10 transition-colors flex-shrink-0"
                    title="Clear brand"
                    aria-label="Clear brand"
                  >
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                )}
              </div>
            </div>

            <div className="col-span-2 md:col-span-3 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">Supplier</label>
<div className={`w-full h-9 rounded-lg border flex items-center gap-1 overflow-hidden transition-all ${
                  supplier
                    ? "border-white/60 bg-white/95 text-gray-800"
                    : "border-white/30 bg-white/15 text-white/80"
                }`}>
                <button
                  type="button"
                  onClick={() => setSupplierSearchOpen(true)}
                  className="flex-1 h-full px-3 text-left text-xs flex items-center gap-2 min-w-0"
                  title={supplier?.label || "Click to search supplier..."}
                >
                  <BuildingStorefrontIcon className="w-4 h-4 flex-shrink-0" />
                  {supplier ? (
                    <span className="truncate font-medium">{supplier.label}</span>
                  ) : (
                    <span className="truncate">Search supplier...</span>
                  )}
                </button>
                {supplier && (
                  <button
                    type="button"
                    onClick={() => setSupplier(null)}
                    className="h-full px-2 flex items-center justify-center hover:bg-black/10 dark:hover:bg-white/10 transition-colors flex-shrink-0"
                    title="Clear supplier"
                    aria-label="Clear supplier"
                  >
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>

{/* ===== Table ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden hover:shadow-lg transition-shadow duration-300">
        {/* Table Header */}
        <div className="flex items-center justify-between px-4 py-3 bg-gray-50/70 dark:bg-slate-800/70 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div className="p-1.5 rounded-lg shadow-sm" style={{ background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})` }}>
              <CubeIcon className="w-4 h-4 text-white" />
            </div>
            <div>
              <span className="text-sm font-semibold text-gray-800 dark:text-gray-200">Forecast Items</span>
              <p className="text-[11px] text-gray-400">{rows.length} item(s)</p>
            </div>
          </div>
        </div>

        <div
          ref={tableWrapRef}
          className="max-h-[65vh] overflow-auto outline-none"
          tabIndex={0}
        >
          <table className="min-w-[880px] w-full text-sm">
            <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
              <tr className="text-left">
                {["#", "Product", "Pack Size", "Units Sold", "Stock (U)", "Pack Price", "Suggested (P)", "Order Packs", "Order Units", "Order Amount"]
                  .map((h, i) => (
                    <th
                      key={h}
                      className={`
                        px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider
                        ${[2, 3, 4, 5, 6, 7, 8, 9].includes(i) ? "text-right" : ""}
                        ${h === "Stock (U)" ? "bg-emerald-100 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300" : ""}
                      `}
                    >
                      {h}
                    </th>
                  ))}
              </tr>
            </thead>

            <tbody>
              {rows.map((r, idx) => {
                const isActive = idx === selectedIndex;
                return (
                  <tr
                    key={r._rowId}
                    ref={(el) => (rowRefs.current[r._rowId] = el)}
                    onClick={() => setSelectedIndex(idx)}
                    className={`
                      transition-colors
                      ${isActive 
                        ? "bg-blue-50 dark:bg-blue-900/20 ring-2 ring-blue-500/30" 
                        : "odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50"
                      }
                      border-b border-gray-100 dark:border-slate-600/30
                    `}
                  >
                    <td className="px-3 py-3 text-gray-600 dark:text-gray-300">{idx + 1}</td>
                    <td className="px-3 py-3">
                      <div className="font-medium text-gray-800 dark:text-gray-200 truncate max-w-[280px]" title={r.product_name}>
                        {r.product_name}
                      </div>
                      {r.product_code && (
                        <div className="text-xs text-gray-500 dark:text-gray-400">{r.product_code}</div>
                      )}
                    </td>
                    <td className="px-3 py-3 text-right tabular-nums whitespace-nowrap text-gray-600 dark:text-gray-300">{r.pack_size}</td>
                    <td className="px-3 py-3 text-right tabular-nums whitespace-nowrap text-gray-600 dark:text-gray-300">{r.units_sold}</td>
                    <td className="px-3 py-3 text-center tabular-nums whitespace-nowrap">
                      <span className="inline-flex items-center justify-center min-w-[3rem] px-2 py-0.5 rounded-full text-xs font-bold bg-emerald-100 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300">
                        {r.current_stock_units}
                      </span>
                    </td>
                    <td className="px-3 py-3 text-right tabular-nums whitespace-nowrap text-gray-600 dark:text-gray-300">{fmt2(r.pack_price)}</td>
                    <td className="px-3 py-3 text-right tabular-nums whitespace-nowrap text-gray-600 dark:text-gray-300">{r.suggested_packs}</td>
                    <td className="px-3 py-3 text-right">
                      <GlassInput
                        type="number"
                        min={0}
                        value={r.order_packs}
                        onFocus={() => setSelectedIndex(idx)}
                        onChange={(e) => updateOrderPacks(r._rowId, e.target.value)}
                        ref={(el) => (inputRefs.current[r._rowId] = el)}
                        className="w-16 h-8 font-bold text-right text-xs no-spinners"
                        inputMode="numeric"
                      />
                    </td>
                    <td className="px-3 py-3 text-right tabular-nums whitespace-nowrap text-gray-600 dark:text-gray-300">{r.order_units}</td>
                    <td className="px-3 py-3 text-right tabular-nums whitespace-nowrap font-semibold text-gray-800 dark:text-gray-200">{fmt2(r.order_amount)}</td>
                  </tr>
                );
              })}

              {!rows.length && (
                <tr>
                  <td colSpan={10} className="px-3 py-12 text-center">
                    <div className="flex flex-col items-center gap-2">
                      <CalculatorIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">No data. Choose filters and click Generate.</p>
                    </div>
                  </td>
                </tr>
              )}
            </tbody>

            {rows.length > 0 && (
              <tfoot>
                <tr className="font-semibold bg-gray-50 dark:bg-slate-700/50 border-t border-gray-200 dark:border-slate-600">
                  <td className="px-3 py-3 text-gray-700 dark:text-gray-200" colSpan={7}>Totals</td>
                  <td className="px-3 py-3 text-right tabular-nums text-gray-700 dark:text-gray-200">{totals.packs}</td>
                  <td className="px-3 py-3 text-right tabular-nums text-gray-700 dark:text-gray-200">{totals.units}</td>
                  <td className="px-3 py-3 text-right tabular-nums text-gray-800 dark:text-gray-100">{fmt2(totals.amount)}</td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      </div>

{/* ===== Brand & Supplier Search Modals ===== */}
      <BrandSearch
        isOpen={brandSearchOpen}
        onClose={() => setBrandSearchOpen(false)}
        onSelect={(brandObj) => {
          setBrand(brandObj ? { value: brandObj.id, label: brandObj.name } : null);
        }}
      />
      <SupplierSearch
        isOpen={supplierSearchOpen}
        onClose={() => setSupplierSearchOpen(false)}
        onSelect={(supplierObj) => {
          setSupplier(supplierObj ? { value: supplierObj.id, label: supplierObj.name } : null);
        }}
      />

      {/* Print styles */}
      <style>{`
        @media print {
          .print\\:p-0 { padding: 0 !important; }
          .rs__control, .rs__menu, input, select, button, [role="button"] { display: none !important; }
          table { font-size: 10px; }
          thead { position: sticky; top: 0; }
        }
        .no-spinners::-webkit-outer-spin-button,
        .no-spinners::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
        .no-spinners[type=number] { -moz-appearance: textfield; }
      `}</style>
    </div>
  );
}

