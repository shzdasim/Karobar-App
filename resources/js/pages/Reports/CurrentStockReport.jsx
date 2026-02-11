// resources/js/pages/Reports/CurrentStockReport.jsx
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
  CubeIcon,
  ClipboardDocumentListIcon,
  CurrencyDollarIcon,
  TagIcon,
  ArrowTrendingUpIcon,
  Squares2X2Icon,
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

/* ======================
   Helpers
   ====================== */

const n = (v) => (isFinite(Number(v)) ? Number(v) : 0);
const fmtCurrency = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const fmtNumber = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });

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
      // keep trying next path
    }
  }
  throw lastErr;
}

export default function CurrentStockReport() {
  // Filters
  const [categoryValue, setCategoryValue] = useState(null);
  const [categoryId, setCategoryId] = useState("");
  const [brandValue, setBrandValue] = useState(null);
  const [brandId, setBrandId] = useState("");
  const [supplierValue, setSupplierValue] = useState(null);
  const [supplierId, setSupplierId] = useState("");

  // Data + States
  const [data, setData] = useState({ rows: [], summary: {} });
  const [loading, setLoading] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("current-stock-report") : {
        view:false, export:false
      }),
    [canFor]
  );

  // Get dark mode state and theme colors
  const { isDark, theme } = useTheme();

  // 🎨 Modern button palette (will use dynamic theme colors)
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

  const tintOutline = useMemo(() => `
    bg-transparent ring-1 ring-gray-300 dark:ring-slate-600
    hover:bg-gray-100 dark:hover:bg-slate-700/50 hover:shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintGhost = useMemo(() => `
    bg-white/60 dark:bg-slate-800/60 ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-700/80 hover:shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

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
        amber: '#f59e0b',
        amberHover: '#d97706',
        amberLight: '#fef3c7',
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
      amber: theme.warning_color || '#f59e0b',
      amberHover: '#d97706',
      amberLight: '#fef3c7',
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

  // Get section styles
  const coreStyles = useMemo(() => getSectionStyles(themeColors, 'primary'), [themeColors]);
  const managementStyles = useMemo(() => getSectionStyles(themeColors, 'secondary'), [themeColors]);

  /* ============ Async loaders ============ */
  const loadCategories = useMemo(
    () => async (input) => {
      const q = String(input || "").trim();
      if (!q) return [{ value: "", label: "All Categories" }];
      try {
        const res = await tryEndpoints(
          ["/api/categories/search", "/categories/search"],
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
        toast.error("Category search failed");
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

  /* ============ Fetch report ============ */
  const fetchReport = async () => {
    if (!can.view) return toast.error("You don't have permission to view this report.");

    setLoading(true);
    try {
      const res = await axios.get("/api/reports/current-stock", {
        params: {
          category_id: categoryId || undefined,
          brand_id: brandId || undefined,
          supplier_id: supplierId || undefined,
        },
      });

      const responseData = res.data || {};
      // Client-side filter to ensure only products with quantity > 0 are displayed
      const allRows = Array.isArray(responseData.rows) ? responseData.rows : [];
      const rows = allRows.filter(row => (Number(row.quantity) || 0) > 0);
      
      // Recalculate summary based on filtered rows
      const summary = {
        total_items: rows.length,
        total_quantity: rows.reduce((sum, row) => sum + (Number(row.quantity) || 0), 0),
        total_purchase_value: rows.reduce((sum, row) => sum + (Number(row.total_purchase_value) || 0), 0),
        total_sale_value: rows.reduce((sum, row) => sum + (Number(row.total_sale_value) || 0), 0),
      };

      setData({ rows, summary });
      if (!rows.length) toast("No stock items found with quantity > 0.", { icon: "ℹ️" });
    } catch (err) {
      console.error(err);
      toast.error("Failed to fetch Current Stock report");
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
      const res = await axios.get("/api/reports/current-stock/pdf", {
        params: {
          category_id: categoryId || undefined,
          brand_id: brandId || undefined,
          supplier_id: supplierId || undefined,
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
    setCategoryValue(null);
    setCategoryId("");
    setBrandValue(null);
    setBrandId("");
    setSupplierValue(null);
    setSupplierId("");
    setData({ rows: [], summary: {} });
  };

  // Computed values
  const { rows, summary } = data;
  const totalPurchaseValue = n(summary.total_purchase_value);
  const totalSaleValue = n(summary.total_sale_value);
  const potentialProfit = totalSaleValue - totalPurchaseValue;

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
              style={{ background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})` }}
            >
              <CubeIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Current Stock Report</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{rows.length} items</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <GlassBtn
              className={`h-9 ${tintGhost}`}
              title="Reset Filters"
              onClick={resetFilters}
              style={{
                color: isDark ? themeColors.primary : themeColors.primary,
              }}
            >
              Reset
            </GlassBtn>
          </div>
        </div>

        {/* Filters */}
        <GlassToolbar className="grid grid-cols-1 md:grid-cols-12 gap-3">
          {/* Category */}
          <div className="md:col-span-4">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>Category</label>
            <AsyncSelect
              cacheOptions
              defaultOptions={[{ value: "", label: "All Categories" }]}
              loadOptions={loadCategories}
              isClearable
              value={categoryValue}
              onChange={(opt) => {
                setCategoryValue(opt);
                setCategoryId(opt?.value || "");
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
          <div className="md:col-span-4">
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

          {/* Supplier */}
          <div className="md:col-span-4">
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

          {/* Buttons */}
          <div className="md:col-span-12 flex flex-wrap gap-2">
            <Guard when={can.view}>
              <GlassBtn
                className={`h-9 min-w-[110px]`}
                onClick={fetchReport}
                disabled={loading}
                style={{
                  background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                  color: primaryTextColor,
                  boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                }}
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
                className="h-9 flex items-center gap-2"
                onClick={exportPdf}
                disabled={pdfLoading || rows.length === 0}
                style={{
                  background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                  color: secondaryTextColor,
                  boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`
                }}
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
                No stock items found. Apply filters and click "Load Report".
              </div>
            </GlassCard>
          )}

          {rows.length > 0 && (
            <>
              {/* ===== Summary KPI Cards ===== */}
              <div className="grid grid-cols-2 md:grid-cols-5 gap-3">
                <KpiCard
                  label="Total Items"
                  value={fmtNumber(summary.total_items)}
                  icon={CubeIcon}
                  isDark={isDark}
                  themeColors={themeColors}
                />
                <KpiCard
                  label="Total Quantity"
                  value={fmtNumber(summary.total_quantity)}
                  icon={ClipboardDocumentListIcon}
                  isDark={isDark}
                  themeColors={themeColors}
                />
                <KpiCard
                  label="Purchase Value"
                  value={fmtCurrency(summary.total_purchase_value)}
                  icon={CurrencyDollarIcon}
                  isDark={isDark}
                  themeColors={themeColors}
                  highlight={false}
                />
                <KpiCard
                  label="Sale Value"
                  value={fmtCurrency(summary.total_sale_value)}
                  icon={TagIcon}
                  isDark={isDark}
                  themeColors={themeColors}
                  highlight={false}
                />
                <KpiCard
                  label="Potential Profit"
                  value={fmtCurrency(potentialProfit)}
                  icon={ArrowTrendingUpIcon}
                  isDark={isDark}
                  themeColors={themeColors}
                  highlight={true}
                />
              </div>

              {/* ===== Data Table (matching Products page style) ===== */}
              <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
                {/* Table Header */}
                <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
                  <div className="flex items-center gap-2">
                    <div 
                      className="p-1 rounded"
                      style={{ backgroundColor: themeColors.primaryLight + '40' }}
                    >
                      <Squares2X2Icon 
                        className="w-4 h-4" 
                        style={{ color: themeColors.primary }} 
                      />
                    </div>
                    <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Stock Items</span>
                  </div>
                  <span className="text-xs text-gray-400">{rows.length} items</span>
                </div>

                <div className="max-h-[65vh] overflow-auto">
                  <table className="w-full text-sm">
                    <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
                      <tr className="text-left">
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-8">#</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Product Name</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Brand</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Supplier</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-20">Pack Size</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-24">Quantity</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-24">Pack Purchase</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-24">Pack Sale</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-28">Total Purchase</th>
                        <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-28">Total Sale</th>
                      </tr>
                    </thead>

                    <tbody>
                      {rows.length === 0 && !loading && (
                        <tr>
                          <td colSpan={10} className="px-2 py-12 text-center">
                            <div className="flex flex-col items-center gap-2">
                              <CubeIcon className="w-8 h-8 text-gray-400" />
                              <p className="text-sm text-gray-500 dark:text-gray-400">No stock items found</p>
                            </div>
                          </td>
                        </tr>
                      )}

                      {rows.map((row, idx) => (
                        <tr
                          key={row.id ?? idx}
                          className={`
                            transition-colors
                            border-b border-gray-100 dark:border-slate-600/30
                            odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40
                            hover:bg-blue-50 dark:hover:bg-slate-600/50
                          `}
                        >
                          <td className="px-2 py-2 text-gray-500 dark:text-gray-400">{idx + 1}</td>
                          
                          <td className="px-2 py-2">
                            <span className="font-medium text-gray-800 dark:text-gray-200">{row.name || "-"}</span>
                          </td>
                          
                          <td className="px-2 py-2">
                            <span className="px-2 py-0.5 rounded bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 text-xs">
                              {row.brand_name || "—"}
                            </span>
                          </td>
                          
                          <td className="px-2 py-2">
                            <span className="px-2 py-0.5 rounded bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-300 text-xs">
                              {row.supplier_name || "—"}
                            </span>
                          </td>
                          
                          <td className="px-2 py-2 text-right text-gray-700 dark:text-gray-300 tabular-nums">
                            {fmtNumber(row.pack_size)}
                          </td>
                          
                          <td className="px-2 py-2 text-right">
                            <span className={`
                              inline-flex items-center justify-center min-w-[3rem] px-2 py-0.5 rounded-full text-xs font-bold
                              bg-gradient-to-br from-emerald-400 to-emerald-500 text-white
                              shadow-lg shadow-emerald-500/25 ring-1 ring-emerald-400/30
                            `}>
                              {fmtNumber(row.quantity)}
                            </span>
                          </td>
                          
                          <td className="px-2 py-2 text-right text-gray-700 dark:text-gray-300 tabular-nums">
                            {fmtCurrency(row.pack_purchase_price)}
                          </td>
                          
                          <td className="px-2 py-2 text-right text-gray-700 dark:text-gray-300 tabular-nums">
                            {fmtCurrency(row.pack_sale_price)}
                          </td>
                          
                          <td className="px-2 py-2 text-right tabular-nums">
                            <span className="font-medium text-emerald-700 dark:text-emerald-400">
                              {fmtCurrency(row.total_purchase_value)}
                            </span>
                          </td>
                          
                          <td className="px-2 py-2 text-right tabular-nums">
                            <span className="font-medium text-indigo-700 dark:text-indigo-400">
                              {fmtCurrency(row.total_sale_value)}
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
                        <td colSpan={4} className="px-2 py-2 text-right text-gray-700 dark:text-gray-300">TOTALS</td>
                        <td className="px-2 py-2 text-right text-gray-500 dark:text-gray-400">-</td>
                        <td className="px-2 py-2 text-right">
                          <span className={`
                            inline-flex items-center justify-center min-w-[3rem] px-2 py-0.5 rounded-full text-xs font-bold
                            bg-gradient-to-br from-emerald-400 to-emerald-500 text-white
                            shadow-lg shadow-emerald-500/25 ring-1 ring-emerald-400/30
                          `}>
                            {fmtNumber(summary.total_quantity)}
                          </span>
                        </td>
                        <td className="px-2 py-2 text-right text-gray-500 dark:text-gray-400">-</td>
                        <td className="px-2 py-2 text-right text-gray-500 dark:text-gray-400">-</td>
                        <td className="px-2 py-2 text-right tabular-nums">
                          <span className="font-bold text-emerald-700 dark:text-emerald-400">
                            {fmtCurrency(summary.total_purchase_value)}
                          </span>
                        </td>
                        <td className="px-2 py-2 text-right tabular-nums">
                          <span className="font-bold text-indigo-700 dark:text-indigo-400">
                            {fmtCurrency(summary.total_sale_value)}
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

/* ===== KPI Card Component ===== */
function KpiCard({ label, value, icon: Icon, highlight = false, gradient = "from-blue-500 to-cyan-600", isDark, themeColors }) {
  // Determine gradient based on label
  const getGradient = () => {
    if (label === "Total Items") return `from-[${themeColors.primary}] to-[${themeColors.primaryHover}]`;
    if (label === "Total Quantity") return `from-[${themeColors.secondary}] to-[${themeColors.secondaryHover}]`;
    if (label === "Purchase Value") return `from-[${themeColors.emerald}] to-[${themeColors.emeraldHover}]`;
    if (label === "Sale Value") return `from-[${themeColors.secondary}] to-[${themeColors.secondaryHover}]`;
    if (label === "Potential Profit") return `from-[${themeColors.amber}] to-[${themeColors.amberHover}]`;
    return `from-[${themeColors.primary}] to-[${themeColors.primaryHover}]`;
  };

  const cardGradient = getGradient();
  const cardColor = label === "Total Items" ? themeColors.primary 
    : label === "Total Quantity" ? themeColors.secondary 
    : label === "Purchase Value" ? themeColors.emerald
    : label === "Sale Value" ? themeColors.secondary
    : themeColors.amber;

  return (
    <div
      className={[
        "group rounded-xl px-4 py-3 backdrop-blur-sm bg-white/55 ring-1 ring-white/30 shadow-sm relative overflow-hidden",
        "transition-all duration-200",
        "hover:bg-white/80 hover:backdrop-blur-md hover:shadow-[0_10px_30px_-10px_rgba(59,130,246,0.35)]",
        "hover:ring-white/40",
        highlight ? "outline outline-1 outline-emerald-200/50" : "",
      ].join(" ")}
    >
      {/* Gradient accent bar */}
      <div className={`absolute top-0 left-0 right-0 h-1 bg-gradient-to-r ${cardGradient}`} />
      
      <div className="flex items-center gap-2 mb-1 relative z-10">
        <div 
          className="p-1.5 rounded-lg shadow-sm"
          style={{ background: `linear-gradient(to bottom right, ${cardColor}, ${themeColors.primaryHover})` }}
        >
          <Icon className="w-4 h-4 text-white" />
        </div>
        <span className={`text-xs uppercase tracking-wide ${isDark ? "text-slate-400" : "text-gray-600"}`}>{label}</span>
      </div>
      <div className="text-xl font-bold tabular-nums text-gray-900 relative z-10">{value}</div>
    </div>
  );
}

