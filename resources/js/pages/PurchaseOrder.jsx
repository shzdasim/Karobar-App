// src/pages/purchase-orders/forecast.jsx
import { useEffect, useMemo, useRef, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import AsyncSelect from "react-select/async";
import {
  ArrowPathIcon,
  PlayCircleIcon,
  PrinterIcon,
  CalculatorIcon,
  CalendarIcon,
  ShieldCheckIcon,
  CubeIcon,
} from "@heroicons/react/24/solid";
import { usePermissions } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";

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

  // 🎨 Dynamic button styles using theme colors
  const tintPrimary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintSecondary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintTertiary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintGlass = useMemo(() => `
    bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const [dateFrom, setDateFrom] = useState(today);
  const [dateTo, setDateTo] = useState(today);
  const [projectedDays, setProjectedDays] = useState(7);

  const [safetyPacks, setSafetyPacks] = useState(1);
  const [moqPacks, setMoqPacks] = useState(0);

  const [supplier, setSupplier] = useState(null);
  const [brand, setBrand] = useState(null);

  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);

  // === keyboard navigation state/refs ===
  const [selectedIndex, setSelectedIndex] = useState(-1);
  const tableWrapRef = useRef(null);
  const inputRefs = useRef({});
  const rowRefs = useRef({});

  const printBtnRef = useRef(null);

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
  }, [rows, canView]);

  const doPrint = () => {
    if (!canView) return toast.error("You don't have permission to view/print.");
    if (!rows.length) return toast.error("Nothing to print.");
    window.print();
  };

  const fmt2 = (v) => Number(v ?? 0).toFixed(2);

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
      {/* ===== Professional Header ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})` }}
            >
              <CalculatorIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Purchase Order (Forecast)</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{rows.length} items</p>
            </div>
          </div>

{/* Action Buttons */}
          <div className="flex items-center gap-2">
            {/* Generate Button */}
            <button
              onClick={handleFetch}
              disabled={loading || !canGenerate}
              className={`
                inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold
                transition-all duration-200
                ${canGenerate 
                  ? `${tintPrimary} cursor-pointer` 
                  : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"
                }
              `}
              style={canGenerate ? {
                background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                color: primaryTextColor,
                boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
              } : {}}
              title={!canGenerate ? "Not permitted" : "Generate forecast"}
            >
              <PlayCircleIcon className="w-4 h-4" />
              {loading ? "Loading…" : "Generate"}
            </button>

            {/* Remove Zero Button */}
            <button
              onClick={pruneNoPackPrice}
              disabled={!rows.length}
              className={`
                inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold
                transition-all duration-200
                ${rows.length 
                  ? `${tintPrimary} cursor-pointer` 
                  : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"
                }
              `}
              style={rows.length ? {
                background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                color: primaryTextColor,
                boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
              } : {}}
              title="Remove products with no Pack Purchase Price"
            >
              <span className="text-lg">×</span>
              Remove Zero
            </button>

            <div className="w-px h-8 bg-gray-200 dark:bg-slate-600 mx-1" />

            {/* Refresh Button */}
            <button
              onClick={() => window.location.reload()}
              className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${tintGlass}`}
              style={{
                background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                color: secondaryTextColor,
                boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`
              }}
              title="Refresh"
              aria-label="Refresh page"
            >
              <ArrowPathIcon className="w-4 h-4" />
              Refresh
            </button>

            {/* Print Button */}
            <button
              ref={printBtnRef}
              onClick={doPrint}
              className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${tintGlass}`}
              style={{
                background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})`,
                color: secondaryTextColor,
                boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40`
              }}
              title="Print (Alt+P)"
            >
              <PrinterIcon className="w-4 h-4" />
              Print
            </button>
          </div>
        </div>

{/* Filters */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="grid grid-cols-2 md:grid-cols-12 gap-3">
            <div className="col-span-2 md:col-span-2 flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">From</label>
              <GlassInput 
                type="date" 
                value={dateFrom} 
                onChange={(e) => setDateFrom(e.target.value)} 
                className="w-full h-8 text-xs"
              />
            </div>
            <div className="col-span-2 md:col-span-2 flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">To</label>
              <GlassInput 
                type="date" 
                value={dateTo} 
                onChange={(e) => setDateTo(e.target.value)} 
                className="w-full h-8 text-xs"
              />
            </div>
            <div className="col-span-1 md:col-span-1 flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Proj. Days</label>
              <GlassInput
                type="number"
                min={1}
                value={projectedDays}
                onChange={(e) => setProjectedDays(parseInt(e.target.value || 0, 10))}
                className="w-full h-8 text-xs"
              />
            </div>

            <div className="col-span-1 md:col-span-1 flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Safety</label>
              <GlassInput
                type="number"
                min={0}
                value={safetyPacks}
                onChange={(e)=>setSafetyPacks(parseInt(e.target.value || 0, 10))}
                className="w-full h-8 text-xs"
              />
            </div>

            <div className="col-span-1 md:col-span-1 flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">MOQ</label>
              <GlassInput
                type="number"
                min={0}
                value={moqPacks}
                onChange={(e)=>setMoqPacks(parseInt(e.target.value || 0, 10))}
                className="w-full h-8 text-xs"
              />
            </div>

            <div className="col-span-2 md:col-span-2 flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Brand</label>
              <AsyncSelect
                cacheOptions={false}
                defaultOptions={[]}
                loadOptions={loadBrandOptions}
                styles={getSelectStyles(isDark)}
                value={brand}
                onChange={setBrand}
                placeholder="Search brand..."
                isClearable
                filterOption={() => true}
                menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                menuPosition="fixed"
                classNamePrefix="rs"
              />
            </div>

            <div className="col-span-2 md:col-span-3 flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Supplier</label>
              <AsyncSelect
                cacheOptions={false}
                defaultOptions={[]}
                loadOptions={loadSupplierOptions}
                styles={getSelectStyles(isDark)}
                value={supplier}
                onChange={setSupplier}
                placeholder="Search supplier..."
                isClearable
                filterOption={() => true}
                menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                menuPosition="fixed"
                classNamePrefix="rs"
              />
            </div>
          </div>
        </div>

        {/* Header Bottom */}
        <div className="flex items-center justify-between px-4 py-2 border-t border-gray-100 dark:border-slate-700">
          <span className="text-xs text-gray-500 dark:text-gray-400">
            {loading ? (
              <span className="inline-flex items-center gap-1">
                <ArrowPathIcon className="w-3.5 h-3.5 animate-spin" />
                Loading...
              </span>
            ) : (
              `${rows.length} items`
            )}
          </span>
        </div>
      </div>

      {/* ===== Table ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        {/* Table Header */}
        <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div className={`p-1 rounded ${SECTION_CONFIG.core.bgDark}`}>
              <CubeIcon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
            </div>
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Forecast Items</span>
          </div>
          <span className="text-xs text-gray-400">{rows.length} items</span>
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

