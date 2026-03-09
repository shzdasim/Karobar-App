// resources/js/pages/Reports/NearExpiryProductReport.jsx
import { useMemo, useState } from "react";
import axios from "axios";
import AsyncSelect from "react-select/async";
import { createFilter } from "react-select";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext.jsx";

// 🧊 glass primitives
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";

import { 
  ArrowDownOnSquareIcon, 
  ArrowPathIcon,
  ClockIcon,
  CalendarIcon,
  ClipboardDocumentListIcon,
} from "@heroicons/react/24/solid";

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

/* ======================
   Helpers
   ====================== */

const n = (v) => (isFinite(Number(v)) ? Number(v) : 0);
const fmtCurrency = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const fmtNumber = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });

const localISODate = (d = new Date()) => {
  const tzOffsetMs = d.getTimezoneOffset() * 60000;
  return new Date(d.getTime() - tzOffsetMs).toISOString().slice(0, 10);
};

/* react-select → glassy control */
const getSelectStyles = (isDarkMode = false) => ({
  control: (base) => ({
    ...base,
    minHeight: 36,
    height: 36,
    borderColor: isDarkMode ? "rgba(71,85,105,0.8)" : "rgba(229,231,235,0.8)",
    backgroundColor: isDarkMode ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.7)",
    backdropFilter: "blur(6px)",
    boxShadow: isDarkMode ? "0 1px 2px rgba(0,0,0,0.2)" : "0 1px 2px rgba(15,23,42,0.06)",
    borderRadius: 12,
    transition: "all .2s ease",
    "&:hover": {
      borderColor: isDarkMode ? "rgba(100,116,139,0.9)" : "rgba(148,163,184,0.9)",
      backgroundColor: isDarkMode ? "rgba(51,65,85,0.85)" : "rgba(255,255,255,0.85)",
    },
  }),
  valueContainer: (base) => ({ ...base, height: 36, padding: "0 10px" }),
  indicatorsContainer: (base) => ({ ...base, height: 36 }),
  input: (base) => ({
    ...base,
    margin: 0,
    padding: 0,
    color: isDarkMode ? "#f1f5f9" : "#111827",
  }),
  menuPortal: (base) => ({ ...base, zIndex: 9999 }),
  menu: (base) => ({
    ...base,
    borderRadius: 12,
    overflow: "hidden",
    backgroundColor: isDarkMode ? "rgba(30,41,59,0.95)" : "rgba(255,255,255,0.9)",
    backdropFilter: "blur(10px)",
    boxShadow: isDarkMode ? "0 10px 30px -10px rgba(0,0,0,0.4)" : "0 10px 30px -10px rgba(30,64,175,0.18)",
    border: isDarkMode ? "1px solid rgba(71,85,105,0.5)" : "none",
  }),
  option: (base, state) => ({
    ...base,
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
  singleValue: (base) => ({
    ...base,
    color: isDarkMode ? "#f1f5f9" : "#111827",
  }),
  placeholder: (base) => ({
    ...base,
    color: isDarkMode ? "#64748b" : "#9ca3af",
  }),
});

// helper to try /api/... then /...
async function tryEndpoints(paths, params) {
  let lastErr;
  for (const path of paths) {
    try {
      const res = await axios.get(path, { params, withCredentials: true });
      return res;
    } catch (e) {
      lastErr = e;
    }
  }
  throw lastErr;
}

export default function NearExpiryProductReport() {
  // Filters - Date range (from/to for expiry date)
  const today = localISODate();
  const defaultToDate = localISODate(new Date(Date.now() + 90 * 24 * 60 * 60 * 1000)); // 3 months ahead
  
  const [fromDate, setFromDate] = useState(today);
  const [toDate, setToDate] = useState(defaultToDate);
  
  const [supplierValue, setSupplierValue] = useState(null);
  const [supplierId, setSupplierId] = useState("");
  const [brandValue, setBrandValue] = useState(null);
  const [brandId, setBrandId] = useState("");
  const [productValue, setProductValue] = useState(null);
  const [productId, setProductId] = useState("");

  // Data + States
  const [data, setData] = useState({ rows: [], summary: {} });
  const [loading, setLoading] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("near-expiry-product-report") : {
        view: false,
        export: false,
      }),
    [canFor]
  );

  // Get dark mode state and theme colors
  const { isDark, theme } = useTheme();

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
    };
  }, [buttonStyle, themeColors, primaryTextColor, secondaryTextColor]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;

  /* ============ Async loaders ============ */
  const loadSuppliers = useMemo(
    () => async (input) => {
      const q = String(input || "").trim();
      if (!q) return [{ value: "", label: "All Suppliers" }];
      try {
        const res = await tryEndpoints(
          ["/api/suppliers/search", "/suppliers/search"],
          { q, limit: 30 }
        );
        const rows = Array.isArray(res.data?.data)
          ? res.data.data
          : Array.isArray(res.data)
          ? res.data
          : [];
        return rows.map((r) => ({
          value: r.id,
          label: r.name ?? r.label ?? `#${r.id}`,
        }));
      } catch {
        toast.error("Supplier search failed");
        return [{ value: "", label: "No results" }];
      }
    },
    []
  );

  const loadBrands = useMemo(
    () => async (input) => {
      const q = String(input || "").trim();
      if (!q) return [{ value: "", label: "All Brands" }];
      try {
        const res = await tryEndpoints(
          ["/api/brands/search", "/brands/search"],
          { q, limit: 30 }
        );
        const rows = Array.isArray(res.data?.data)
          ? res.data.data
          : Array.isArray(res.data)
          ? res.data
          : [];
        return rows.map((r) => ({
          value: r.id,
          label: r.name ?? r.label ?? `#${r.id}`,
        }));
      } catch {
        toast.error("Brand search failed");
        return [{ value: "", label: "No results" }];
      }
    },
    []
  );

  const loadProducts = useMemo(
    () => async (input) => {
      const q = String(input || "").trim();
      if (!q) return [{ value: "", label: "All Products" }];
      try {
        const res = await tryEndpoints(
          ["/api/products/search", "/products/search"],
          { q, limit: 30 }
        );
        const rows = Array.isArray(res.data?.data)
          ? res.data.data
          : Array.isArray(res.data)
          ? res.data
          : [];
        return rows.map((r) => ({
          value: r.id,
          label: r.name ? `${r.name} (${r.product_code || ''})` : `#${r.id}`,
        }));
      } catch {
        toast.error("Product search failed");
        return [{ value: "", label: "No results" }];
      }
    },
    []
  );

  /* ============ Fetch report ============ */
  const fetchReport = async () => {
    if (!can.view) return toast.error("You don't have permission to view this report.");

    setLoading(true);
    try {
      const res = await axios.get("/api/reports/near-expiry-product", {
        params: {
          from: fromDate,
          to: toDate,
          supplier_id: supplierId || undefined,
          brand_id: brandId || undefined,
          product_id: productId || undefined,
        },
      });

      const responseData = res.data || {};
      const rows = Array.isArray(responseData.rows) ? responseData.rows : [];
      const summary = responseData.summary || {};
      
      setData({ rows, summary });
      if (!rows.length) toast("No near expiry products found.", { icon: "ℹ️" });
    } catch (err) {
      console.error(err);
      toast.error("Failed to fetch Near Expiry Product report");
      setData({ rows: [], summary: {} });
    } finally {
      setLoading(false);
    }
  };

  /* ============ Export PDF ============ */
  const exportPdf = async () => {
    if (!can.export) return toast.error("You don't have permission to export PDF.");
    setPdfLoading(true);
    try {
      const res = await axios.get("/api/reports/near-expiry-product/pdf", {
        params: {
          from: fromDate,
          to: toDate,
          supplier_id: supplierId || undefined,
          brand_id: brandId || undefined,
          product_id: productId || undefined,
        },
        responseType: "blob",
      });
      const blob = new Blob([res.data], { type: "application/pdf" });
      const url = URL.createObjectURL(blob);
      window.open(url, "_blank");
      URL.revokeObjectURL(url);
    } catch (e) {
      console.error(e);
      toast.error("Failed to generate PDF");
    } finally {
      setPdfLoading(false);
    }
  };

  /* ============ Reset filters ============ */
  const resetFilters = () => {
    setFromDate(today);
    setToDate(defaultToDate);
    setSupplierValue(null);
    setSupplierId("");
    setBrandValue(null);
    setBrandId("");
    setProductValue(null);
    setProductId("");
    setData({ rows: [], summary: {} });
  };

  // Computed values
  const { rows, summary } = data;

  return (
    <div className="p-4 space-y-3">
      {/* ===== Professional Header ===== */}
      <GlassCard>
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})` }}
            >
              <ClockIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Near Expiry Product Report</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{rows.length} items expiring</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <GlassBtn
              className={`h-9 ${btnPrimary.className}`}
              title="Reset Filters"
              onClick={resetFilters}
              style={btnPrimary.style}
            >
              Reset
            </GlassBtn>
          </div>
        </div>

        {/* Filters */}
        <GlassToolbar className="grid grid-cols-1 md:grid-cols-12 gap-3">
          {/* From Date */}
          <div className="md:col-span-2">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>
              <CalendarIcon className="w-4 h-4 inline mr-1" />
              From (Expiry)
            </label>
            <GlassInput
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
            />
          </div>

          {/* To Date */}
          <div className="md:col-span-2">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>
              <CalendarIcon className="w-4 h-4 inline mr-1" />
              To (Expiry)
            </label>
            <GlassInput
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
            />
          </div>

          {/* Supplier */}
          <div className="md:col-span-3">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>Supplier</label>
            <AsyncSelect
              cacheOptions
              defaultOptions={[{ value: "", label: "All Suppliers" }]}
              loadOptions={loadSuppliers}
              isClearable
              value={supplierValue}
              onChange={(opt) => {
                setSupplierValue(opt);
                setSupplierId(opt?.value || "");
              }}
              styles={getSelectStyles(isDark)}
              menuPortalTarget={document.body}
              filterOption={createFilter({
                matchFrom: "start",
                trim: true,
              })}
            />
          </div>

          {/* Brand */}
          <div className="md:col-span-3">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>Brand</label>
            <AsyncSelect
              cacheOptions
              defaultOptions={[{ value: "", label: "All Brands" }]}
              loadOptions={loadBrands}
              isClearable
              value={brandValue}
              onChange={(opt) => {
                setBrandValue(opt);
                setBrandId(opt?.value || "");
              }}
              styles={getSelectStyles(isDark)}
              menuPortalTarget={document.body}
              filterOption={createFilter({
                matchFrom: "start",
                trim: true,
              })}
            />
          </div>

          {/* Product */}
          <div className="md:col-span-2">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>Product</label>
            <AsyncSelect
              cacheOptions
              defaultOptions={[{ value: "", label: "All Products" }]}
              loadOptions={loadProducts}
              isClearable
              value={productValue}
              onChange={(opt) => {
                setProductValue(opt);
                setProductId(opt?.value || "");
              }}
              styles={getSelectStyles(isDark)}
              menuPortalTarget={document.body}
              filterOption={createFilter({
                matchFrom: "start",
                trim: true,
              })}
            />
          </div>

          {/* Buttons */}
          <div className="md:col-span-12 flex flex-wrap gap-2">
            <Guard when={can.view}>
              <GlassBtn
                className={`h-9 min-w-[110px] ${btnPrimary.className}`}
                onClick={fetchReport}
                disabled={loading}
                style={btnPrimary.style}
              >
                {loading ? (
                  <span className="inline-flex items-center gap-2">
                    <ArrowPathIcon className="w-5 h-5 animate-spin" />
                    Loading…
                  </span>
                ) : (
                  "Load Report"
                )}
              </GlassBtn>
            </Guard>

            <Guard when={can.export}>
              <GlassBtn
                className={`h-9 flex items-center gap-2 ${btnSecondary.className}`}
                onClick={exportPdf}
                disabled={pdfLoading || rows.length === 0}
                style={btnSecondary.style}
              >
                <ArrowDownOnSquareIcon className="w-5 h-5" />
                {pdfLoading ? "Generating…" : "Export PDF"}
              </GlassBtn>
            </Guard>
          </div>
        </GlassToolbar>
      </GlassCard>

      {/* ===== Permission / Loading states ===== */}
      {permsLoading && (
        <GlassCard>
          <div className={`px-4 py-3 text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>Checking permissions…</div>
        </GlassCard>
      )}
      {!permsLoading && !can.view && (
        <GlassCard>
          <div className={`px-4 py-3 text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>You don't have permission to view this report.</div>
        </GlassCard>
      )}

      {/* ===== Results ===== */}
      {!permsLoading && can.view && (
        <>
          {rows.length === 0 && !loading && (
            <GlassCard>
              <div className={`px-4 py-4 text-sm ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                No near expiry products found. Apply filters and click "Load Report".
              </div>
            </GlassCard>
          )}

          {rows.length > 0 && (
            <>
              {/* ===== Summary KPI Cards ===== */}
              <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                <KpiCard
                  label="Total Batches"
                  value={fmtNumber(summary.total_batches)}
                  icon={ClipboardDocumentListIcon}
                  isDark={isDark}
                  themeColors={themeColors}
                />
                <KpiCard
                  label="Total Quantity"
                  value={fmtNumber(summary.total_quantity)}
                  icon={ClockIcon}
                  isDark={isDark}
                  themeColors={themeColors}
                />
              </div>

              {/* ===== Data Table ===== */}
              <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
                {/* Table Header */}
                <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
                  <div className="flex items-center gap-2">
                    <div 
                      className="p-1 rounded"
                      style={{ backgroundColor: themeColors.tertiaryLight + '40' }}
                    >
                      <ClockIcon 
                        className="w-4 h-4" 
                        style={{ color: themeColors.tertiary }} 
                      />
                    </div>
                    <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Near Expiry Products</span>
                  </div>
                  <span className="text-xs text-gray-400">{rows.length} items</span>
                </div>

                <div className="max-h-[65vh] overflow-auto">
                  <table className="w-full text-sm">
                    <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
                      <tr className="text-left">
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-8">#</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Product</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Supplier</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Brand</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Batch #</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Expiry Date</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Quantity</th>
                      </tr>
                    </thead>

                    <tbody>
                      {rows.length === 0 && !loading && (
                        <tr>
                          <td colSpan={7} className="px-2 py-12 text-center">
                            <div className="flex flex-col items-center gap-2">
                              <ClockIcon className="w-8 h-8 text-gray-400" />
                              <p className="text-sm text-gray-500 dark:text-gray-400">No near expiry products found</p>
                            </div>
                          </td>
                        </tr>
                      )}

                      {rows.map((row, idx) => (
                        <tr
                          key={row.batch_id ?? idx}
                          className={`
                            transition-colors
                            border-b border-gray-100 dark:border-slate-600/30
                            odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40
                            hover:bg-blue-50 dark:hover:bg-slate-600/50
                          `}
                        >
                          <td className="px-2 py-2 text-gray-500 dark:text-gray-400">{idx + 1}</td>
                          
                          <td className="px-2 py-2">
                            <span className="font-medium text-gray-800 dark:text-gray-200">{row.product_name || "-"}</span>
                            <div className="text-xs text-gray-500 dark:text-gray-400">{row.product_code || ""}</div>
                          </td>
                          
                          <td className="px-2 py-2">
                            <span className="px-2 py-0.5 rounded bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-300 text-xs">
                              {row.supplier_name || "—"}
                            </span>
                          </td>
                          
                          <td className="px-2 py-2">
                            <span className="px-2 py-0.5 rounded bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 text-xs">
                              {row.brand_name || "—"}
                            </span>
                          </td>
                          
                          <td className="px-2 py-2 text-gray-700 dark:text-gray-300 font-mono text-xs">
                            {row.batch_number || "—"}
                          </td>
                          
                          <td className="px-2 py-2">
                            <span className={`
                              inline-flex items-center px-2 py-0.5 rounded text-xs font-medium
                              ${isExpiryClose(row.expiry_date) 
                                ? 'bg-red-100 text-red-800 dark:bg-red-900/50 dark:text-red-300' 
                                : 'bg-orange-100 text-orange-800 dark:bg-orange-900/50 dark:text-orange-300'}
                            `}>
                              {formatDate(row.expiry_date)}
                            </span>
                          </td>
                          
                          <td className="px-2 py-2 text-right">
                            <span className={`
                              inline-flex items-center justify-center min-w-[3rem] px-2 py-0.5 rounded-full text-xs font-bold
                              bg-gradient-to-br from-amber-400 to-orange-500 text-white
                              shadow-lg shadow-amber-500/25 ring-1 ring-amber-400/30
                            `}>
                              {fmtNumber(row.quantity)}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>

                    {/* Footer with Totals */}
                    <tfoot className={`
                      border-t-2 backdrop-blur-sm font-semibold
                      ${isDark ? "border-slate-600 bg-slate-800/80" : "border-gray-300 bg-gray-50"}
                    `}>
                      <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                        <td colSpan={6} className="px-2 py-2 text-right text-gray-700 dark:text-gray-300">TOTALS</td>
                        <td className="px-2 py-2 text-right">
                          <span className={`
                            inline-flex items-center justify-center min-w-[3rem] px-2 py-0.5 rounded-full text-xs font-bold
                            bg-gradient-to-br from-amber-400 to-orange-500 text-white
                            shadow-lg shadow-amber-500/25 ring-1 ring-amber-400/30
                          `}>
                            {fmtNumber(summary.total_quantity)}
                          </span>
                        </td>
                      </tr>
                    </tfoot>
                  </table>
                </div>
              </div>
            </>
          )}
        </>
      )}

      <style>{`
        .tabular-nums { font-variant-numeric: tabular-nums; }
        @media print {
          input, button, select, [role="button"], .rs__control { display: none !important; }
          table { font-size: 10px; }
          thead { position: sticky; top: 0; }
        }
      `}</style>
    </div>
  );
}

// Helper function to format date
function formatDate(dateStr) {
  if (!dateStr) return "—";
  if (dateStr instanceof Date) {
    return dateStr.toISOString().slice(0, 10);
  }
  if (typeof dateStr === 'string') {
    return dateStr.slice(0, 10);
  }
  return dateStr;
}

// Helper function to check if expiry is close (within 30 days)
function isExpiryClose(dateStr) {
  if (!dateStr) return false;
  const expiryDate = new Date(formatDate(dateStr));
  const today = new Date();
  const diffTime = expiryDate - today;
  const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
  return diffDays <= 30;
}

/* ===== KPI Card Component ===== */
function KpiCard({ label, value, icon: Icon, isDark, themeColors }) {
  const cardColor = themeColors.tertiary;

  return (
    <div
      className={[
        "group rounded-xl px-4 py-3 backdrop-blur-sm bg-white/55 ring-1 ring-white/30 shadow-sm relative overflow-hidden",
        "transition-all duration-200",
        "hover:bg-white/80 hover:backdrop-blur-md hover:shadow-[0_10px_30px_-10px_rgba(6,182,212,0.35)]",
        "hover:ring-white/40",
      ].join(" ")}
    >
      {/* Gradient accent bar */}
      <div 
        className="absolute top-0 left-0 right-0 h-1 bg-gradient-to-r" 
        style={{ background: `linear-gradient(to right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})` }}
      />
      
      <div className="flex items-center gap-2 mb-1 relative z-10">
        <div 
          className="p-1.5 rounded-lg shadow-sm"
          style={{ background: `linear-gradient(to bottom right, ${cardColor}, ${themeColors.tertiaryHover})` }}
        >
          <Icon className="w-4 h-4 text-white" />
        </div>
        <span className={`text-xs uppercase tracking-wide ${isDark ? "text-slate-400" : "text-gray-600"}`}>{label}</span>
      </div>
      <div className="text-xl font-bold tabular-nums text-gray-900 relative z-10">{value}</div>
    </div>
  );
}

