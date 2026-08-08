// resources/js/pages/Reports/PurchaseDetailReport.jsx
import { useMemo, useState } from "react";
import axios from "axios";
import AsyncSelect from "react-select/async";
import { createFilter } from "react-select";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext";
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";
import {
  ArrowPathIcon,
  ArrowDownOnSquareIcon,
  DocumentTextIcon,
  BuildingStorefrontIcon,
  Squares2X2Icon,
} from "@heroicons/react/24/solid";
import SupplierSearch from "@/components/SupplierSearch.jsx";
import ProductSearchInput from "@/components/ProductSearchInput.jsx";

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

/* ------------------ Helpers ------------------ */
const todayStr = () => new Date().toISOString().split("T")[0];
const yesterdayStr = () => {
  const d = new Date();
  d.setDate(d.getDate() - 1);
  return d.toISOString().split("T")[0];
};
const n = (v) => (isFinite(Number(v)) ? Number(v) : 0);
const fmtCurrency = (v) =>
  n(v).toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });

// Helper for react-select controls on the dark gradient hero background
const getSelectStylesOnDark = (isDark = false) => ({
  control: (base) => ({
    ...base,
    minHeight: 36,
    height: 36,
    borderRadius: 10,
    borderColor: "rgba(255,255,255,0.3)",
    backgroundColor: "rgba(15,23,42,0.5)",
    backdropFilter: "blur(6px)",
    boxShadow: "none",
    cursor: "pointer",
    "&:hover": {
      borderColor: "rgba(255,255,255,0.5)",
    },
  }),
  valueContainer: (base) => ({ ...base, height: 36, padding: "0 8px" }),
  indicatorsContainer: (base) => ({ ...base, height: 36, color: "rgba(255,255,255,0.8)" }),
  input: (base) => ({ ...base, margin: 0, padding: 0, color: "#ffffff" }),
  singleValue: (base) => ({ ...base, color: "#ffffff" }),
  placeholder: (base) => ({ ...base, color: "rgba(255,255,255,0.7)" }),
  menuPortal: (base) => ({ ...base, zIndex: 9999 }),
  menu: (base) => ({
    ...base,
    borderRadius: 10,
    overflow: "hidden",
    backgroundColor: isDark ? "rgba(30,41,59,0.98)" : "rgba(255,255,255,0.98)",
    backdropFilter: "blur(10px)",
    boxShadow: "0 10px 30px -10px rgba(0,0,0,0.4)",
    border: isDark ? "1px solid rgba(71,85,105,0.5)" : "none",
  }),
  option: (base, state) => ({
    ...base,
    backgroundColor: isDark
      ? state.isFocused ? "rgba(71,85,105,1)" : "rgba(51,65,85,1)"
      : state.isFocused ? "rgba(241,245,249,1)" : "rgba(255,255,255,1)",
    color: isDark ? "#f1f5f9" : "#111827",
    cursor: "pointer",
  }),
});

// Helper to merge dark mode styles - returns function-based styles for react-select
const getSmallSelectStyles = (isDark = false) => ({
  control: (base) => ({
    ...base,
    minHeight: 32,
    height: 32,
    borderRadius: 12,
    borderColor: isDark ? "rgba(71,85,105,0.8)" : "rgba(229,231,235,0.8)",
    backgroundColor: isDark ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.7)",
    backdropFilter: "blur(6px)",
    boxShadow: isDark ? "0 1px 2px rgba(0,0,0,0.2)" : "0 1px 2px rgba(15,23,42,0.06)",
  }),
  valueContainer: (base) => ({ ...base, height: 32, padding: "0 8px" }),
  indicatorsContainer: (base) => ({ ...base, height: 32 }),
  input: (base) => ({ ...base, margin: 0, padding: 0, color: isDark ? "#f1f5f9" : "#111827" }),
  menuPortal: (base) => ({ ...base, zIndex: 9999 }),
  menu: (base) => ({
    ...base,
    borderRadius: 12,
    overflow: "hidden",
    backgroundColor: isDark ? "rgba(30,41,59,0.95)" : "rgba(255,255,255,0.95)",
    backdropFilter: "blur(10px)",
    boxShadow: isDark ? "0 10px 30px -10px rgba(0,0,0,0.4)" : "0 10px 30px -10px rgba(30,64,175,0.18)",
    border: isDark ? "1px solid rgba(71,85,105,0.5)" : "none",
  }),
  option: (base, state) => ({
    ...base,
    backgroundColor: isDark
      ? state.isFocused
        ? "rgba(71,85,105,1)"
        : "rgba(51,65,85,1)"
      : state.isFocused
        ? "rgba(241,245,249,1)"
        : "rgba(255,255,255,1)",
    color: isDark ? "#f1f5f9" : "#111827",
    cursor: "pointer",
  }),
  singleValue: (base) => ({
    ...base,
    color: isDark ? "#f1f5f9" : "#111827",
  }),
  placeholder: (base) => ({
    ...base,
    color: isDark ? "#64748b" : "#9ca3af",
  }),
});

/* ------------------ Helper to try multiple endpoints ------------------ */
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

/* ------------------ Component ------------------ */
export default function PurchaseDetailReport() {
  // Default: yesterday → today
  const [fromDate, setFromDate] = useState(yesterdayStr());
  const [toDate, setToDate] = useState(todayStr());
const [supplierId, setSupplierId] = useState("");
  const [supplierValue, setSupplierValue] = useState(null);
  const [productId, setProductId] = useState("");
  const [productValue, setProductValue] = useState(null);
  const [supplierSearchOpen, setSupplierSearchOpen] = useState(false);
  const [products, setProducts] = useState([]);
  const [data, setData] = useState([]);
  const [loading, setLoading] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("purchase-detail-report") : {
        view: false,
        export: false,
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

  const tintGlass = useMemo(() => `
    bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintOutline = useMemo(() => `
    bg-transparent ring-1 ring-gray-300 dark:ring-slate-600
    hover:bg-gray-100 dark:hover:bg-slate-700/50 hover:shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
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
        emerald: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.emerald,
            color: themeColors.emerald,
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
      tertiary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})`,
          color: '#ffffff',
          boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40`,
        }
      },
      emerald: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.emerald}, ${themeColors.emeraldHover})`,
          color: '#ffffff',
          boxShadow: `0 4px 14px 0 ${themeColors.emerald}40`,
        }
      },
    };
  }, [buttonStyle, themeColors, primaryTextColor, secondaryTextColor]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnTertiary = getButtonClasses.tertiary;
  const btnEmerald = getButtonClasses.emerald;

  // Get section styles
  const coreStyles = useMemo(() => getSectionStyles(themeColors, 'primary'), [themeColors]);
  const managementStyles = useMemo(() => getSectionStyles(themeColors, 'secondary'), [themeColors]);

  /* ------------------ Async Selects ------------------ */
  const loadSuppliers = useMemo(
    () =>
      async (input) => {
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
          return rows.map((r) => ({ value: r.id, label: r.name ?? `#${r.id}` }));
        } catch {
          toast.error("Supplier search failed");
          return [{ value: "", label: "No results" }];
        }
      },
    []
  );

  const loadProducts = useMemo(
    () =>
      async (input) => {
        const q = String(input || "").trim();
        if (!q) return [{ value: "", label: "All Products" }];
        try {
          const res = await tryEndpoints(
            ["/api/products/search", "/products/search"],
            { q, limit: 30, supplier_id: supplierId || undefined }
          );
          const rows = Array.isArray(res.data)
            ? res.data
            : Array.isArray(res.data?.data)
            ? res.data.data
            : [];
          return rows.map((p) => ({
            value: p.id,
            label: p.name || p.product_code || `#${p.id}`,
          }));
        } catch {
          toast.error("Product search failed");
          return [{ value: "", label: "No results" }];
        }
      },
    [supplierId]
  );

/* ------------------ Product fetch (for ProductSearchInput) ------------------ */
  const fetchProducts = async (q = "") => {
    try {
      const { data } = await axios.get("/api/products/search", { params: { q, limit: 30 } });
      setProducts(Array.isArray(data) ? data : []);
    } catch {
      setProducts([]);
    }
  };

  /* ------------------ Supplier selected from modal ------------------ */
  const handleSupplierSelect = (supplier) => {
    if (!supplier?.id) return;
    setSupplierValue(supplier);
    setSupplierId(String(supplier.id));
    setProductId("");
    setProductValue(null);
    setSupplierSearchOpen(false);
  };

  /* ------------------ Fetch report ------------------ */
  const fetchReport = async () => {
    if (!can.view) return toast.error("You don't have permission to view this report.");
    if (fromDate > toDate)
      return toast.error("'From' date cannot be after 'To' date.");

    setLoading(true);
    try {
      const res = await axios.get("/api/reports/purchase-detail", {
        params: {
          from: fromDate,
          to: toDate,
          supplier_id: supplierId || undefined,
          product_id: productId || undefined,
        },
      });
      const rows = Array.isArray(res.data)
        ? res.data
        : Array.isArray(res.data?.data)
        ? res.data.data
        : [];
      setData(rows);
      if (!rows.length) toast("No data found for selected range.", { icon: "ℹ️" });
    } catch (err) {
      console.error(err);
      toast.error("Failed to fetch Purchase Detail report");
      setData([]);
    } finally {
      setLoading(false);
    }
  };

  const exportPdf = async () => {
    if (!can.export) return toast.error("You don't have permission to export PDF.");
    setPdfLoading(true);
    try {
      const res = await axios.get("/api/reports/purchase-detail/pdf", {
        params: {
          from: fromDate,
          to: toDate,
          supplier_id: supplierId || undefined,
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

const resetFilters = () => {
    setFromDate(yesterdayStr());
    setToDate(todayStr());
    setSupplierId("");
    setSupplierValue(null);
    setSupplierSearchOpen(false);
    setProductId("");
    setProductValue(null);
    setProducts([]);
    setData([]);
  };

  // Permission gating
  if (permsLoading) {
    return (
      <div className="p-6">
        <GlassCard>
          <div className={`px-4 py-3 text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>Checking permissions…</div>
        </GlassCard>
      </div>
    );
  }

  if (!can.view) {
    return (
      <div className="p-6">
        <GlassCard>
          <div className={`px-4 py-3 text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
            You don't have permission to view this report.
          </div>
        </GlassCard>
      </div>
    );
  }

  return (
    <div className="p-4 space-y-3">
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
        <div className="relative flex items-center justify-between px-5 py-4 flex-wrap gap-2">
          <div className="flex items-center gap-3">
            <div
              className="p-2.5 rounded-xl shadow-inner"
              style={{ backgroundColor: "rgba(255,255,255,0.18)", backdropFilter: "blur(4px)" }}
            >
              <DocumentTextIcon className="w-6 h-6 text-white" />
            </div>
            <div>
              <h1 className="text-xl font-extrabold tracking-wide text-white leading-none">Purchase Detail Report</h1>
              <p className="text-xs text-white/80 mt-1">{data.length} entries</p>
            </div>
          </div>

          {/* Header Actions */}
          <div className="flex items-center gap-2 flex-wrap justify-end">
            <button
              onClick={resetFilters}
              className="h-10 px-3.5 inline-flex items-center gap-1.5 rounded-xl text-sm font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20 transition-all duration-200 shadow-lg"
            >
              <ArrowPathIcon className="w-4 h-4" />
              <span>Reset</span>
            </button>
            <Guard when={can.view}>
              <button
                onClick={fetchReport}
                disabled={loading}
                className={`h-10 px-3.5 inline-flex items-center gap-1.5 rounded-xl text-sm font-semibold text-white transition-all duration-200 ${
                  loading ? "opacity-50 cursor-not-allowed" : "bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20 shadow-lg"
                }`}
              >
                <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
                {loading ? "Loading…" : "Load"}
              </button>
            </Guard>
          </div>
        </div>

        {/* Filters */}
        <div className="relative px-5 pb-4">
          <div
            className="grid grid-cols-1 md:grid-cols-12 gap-3 rounded-xl p-3"
            style={{ backgroundColor: "rgba(255,255,255,0.12)", backdropFilter: "blur(8px)", border: "1px solid rgba(255,255,255,0.15)" }}
          >
            {/* From Date */}
            <div className="md:col-span-2 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">From</label>
              <input
                type="date"
                value={fromDate}
                onChange={(e) => setFromDate(e.target.value)}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/70 bg-slate-900/50 border border-white/30 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/50 [color-scheme:dark]"
              />
            </div>

            {/* To Date */}
            <div className="md:col-span-2 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">To</label>
              <input
                type="date"
                value={toDate}
                onChange={(e) => setToDate(e.target.value)}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/70 bg-slate-900/50 border border-white/30 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/50 [color-scheme:dark]"
              />
            </div>

{/* Supplier */}
            <div className="md:col-span-4 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">Supplier</label>
              <div className="relative">
                <button
                  type="button"
                  onClick={() => setSupplierSearchOpen(true)}
                  className={`w-full h-9 px-3 rounded-lg border text-left text-xs flex items-center gap-2 transition-all
                    ${supplierValue 
                      ? 'border-white/40 bg-white/20 text-white' 
                      : 'border-white/30 bg-slate-900/50 text-white/70 hover:border-white/50'
                    }`}
                >
                  <BuildingStorefrontIcon className="w-4 h-4 shrink-0" />
                  <span className="truncate flex-1">
                    {supplierValue?.name || "All Suppliers"}
                  </span>
                </button>
                {supplierValue && (
                  <button
                    type="button"
                    onClick={() => {
                      setSupplierValue(null);
                      setSupplierId("");
                      setProductId("");
                      setProductValue(null);
                    }}
                    className="absolute right-2 top-1/2 -translate-y-1/2 w-4 h-4 flex items-center justify-center rounded-full bg-white/20 text-white/80 hover:bg-white/35 text-[10px] leading-none"
                    title="Clear supplier"
                  >
                    ×
                  </button>
                )}
              </div>
            </div>

            {/* Product */}
            <div className="md:col-span-4 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">Product</label>
              <div className="flex items-center gap-1">
                <div className="flex-1">
<ProductSearchInput
                    className="h-9 text-xs px-3 rounded-lg"
                    value={productValue || productId}
                    onChange={(val) => {
                      const selected = val && typeof val === "object" ? val : null;
                      setProductValue(selected);
                      setProductId(selected?.id ? String(selected.id) : "");
                    }}
                    products={products}
                    onRefreshProducts={fetchProducts}
                  />
                </div>
                {productId && (
                  <button
                    type="button"
                    onClick={() => {
                      setProductId("");
                      setProductValue(null);
                    }}
                    className="w-5 h-5 flex items-center justify-center rounded-full bg-white/20 text-white/80 hover:bg-white/35 text-[10px] leading-none flex-shrink-0"
                    title="Clear product"
                  >
                    ×
                  </button>
                )}
              </div>
            </div>

            {/* Quick Filters & Export */}
            <div className="md:col-span-12 flex flex-wrap items-end gap-2">
              <button
                onClick={() => {
                  const end = new Date();
                  const start = new Date();
                  start.setDate(end.getDate() - 1);
                  setFromDate(start.toISOString().slice(0, 10));
                  setToDate(end.toISOString().slice(0, 10));
                }}
                className="h-9 px-3 rounded-lg text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20 transition-all duration-200"
              >
                Today
              </button>

              <button
                onClick={() => {
                  const end = new Date();
                  const start = new Date();
                  start.setDate(end.getDate() - 3);
                  setFromDate(start.toISOString().slice(0, 10));
                  setToDate(end.toISOString().slice(0, 10));
                }}
                className="h-9 px-3 rounded-lg text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20 transition-all duration-200"
              >
                3 Days
              </button>

              <button
                onClick={() => {
                  const end = new Date();
                  const start = new Date();
                  start.setDate(end.getDate() - 7);
                  setFromDate(start.toISOString().slice(0, 10));
                  setToDate(end.toISOString().slice(0, 10));
                }}
                className="h-9 px-3 rounded-lg text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20 transition-all duration-200"
              >
                7 Days
              </button>

              <Guard when={can.export}>
                <button
                  onClick={exportPdf}
                  disabled={pdfLoading || data.length === 0}
                  className={`h-9 px-3 inline-flex items-center gap-1.5 rounded-lg text-xs font-semibold text-white transition-all duration-200 ${
                    pdfLoading || data.length === 0 ? "opacity-40 cursor-not-allowed" : "bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20"
                  }`}
                >
                  <ArrowDownOnSquareIcon className="w-4 h-4" />
                  {pdfLoading ? "Generating…" : "Export PDF"}
                </button>
              </Guard>
            </div>
          </div>
        </div>
      </div>

      {/* ===== Results ===== */}
      {data.length === 0 && !loading && (
        <GlassCard>
          <div className={`px-4 py-4 text-sm ${isDark ? "text-slate-400" : "text-gray-600"}`}>
            No data found for the selected filters.
          </div>
        </GlassCard>
      )}

      {/* ===== Invoice Cards ===== */}
      <div className="flex flex-col gap-4">
        {data.map((inv) => (
          <div
            key={inv.id || `${inv.posted_number}-${inv.invoice_number}-${inv.invoice_date}`}
            className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden"
          >
            {/* Table Header (matching Products page style) */}
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
                <span className="text-sm font-medium text-gray-700 dark:text-gray-200">
                  {inv.supplier_name || "—"}
                </span>
              </div>
              <div className="flex items-center gap-4 text-xs text-gray-500 dark:text-gray-400">
                <span>Posted #: {inv.posted_number || "-"}</span>
                <span>Invoice #: {inv.invoice_number || "-"}</span>
                <span>{inv.invoice_date || "-"}</span>
              </div>
            </div>

            {/* Items table */}
            <div className="relative max-w-full overflow-auto">
              <table className="w-full min-w-[900px] text-sm">
                <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
                  <tr className="text-left">
                    <Th isDark={isDark}>Product Name</Th>
                    <Th isDark={isDark}>Batch</Th>
                    <Th isDark={isDark}>Expiry</Th>
                    <Th isDark={isDark} align="right">Pack Qty</Th>
                    <Th isDark={isDark} align="right">Pack Size</Th>
                    <Th isDark={isDark} align="right">Pack Purchase</Th>
                    <Th isDark={isDark} align="right">Pack Sale</Th>
                    <Th isDark={isDark} align="right">Pack Bonus</Th>
                    <Th isDark={isDark} align="right">Disc %</Th>
                    <Th isDark={isDark} align="right">Margin</Th>
                    <Th isDark={isDark} align="right">Sub Total</Th>
                    <Th isDark={isDark} align="right">Quantity</Th>
                  </tr>
                </thead>

                <tbody className="tabular-nums">
                  {(inv.items || []).map((it, idx) => (
                    <tr
                      key={(it.id ?? idx) + "-" + (it.product_id ?? "p") + "-" + idx}
                      className={`
                        transition-colors
                        border-b border-gray-100 dark:border-slate-600/30
                        odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40
                        hover:bg-blue-50 dark:hover:bg-slate-600/50
                      `}
                    >
                      <Td isDark={isDark}>{it.product_name || "-"}</Td>
                      <Td isDark={isDark}>{it.batch || "-"}</Td>
                      <Td isDark={isDark}>{it.expiry || "-"}</Td>
                      <Td isDark={isDark} align="right">{it.pack_quantity ?? 0}</Td>
                      <Td isDark={isDark} align="right">{it.pack_size ?? 0}</Td>
                      <Td isDark={isDark} align="right">{fmtCurrency(it.pack_purchase_price)}</Td>
                      <Td isDark={isDark} align="right">{fmtCurrency(it.pack_sale_price)}</Td>
                      <Td isDark={isDark} align="right">{it.pack_bonus ?? 0}</Td>
                      <Td isDark={isDark} align="right">{(it.item_discount_percentage ?? 0).toFixed(2)}</Td>
                      <Td isDark={isDark} align="right">{(it.margin ?? 0).toFixed(2)}</Td>
                      <Td isDark={isDark} align="right">{fmtCurrency(it.sub_total)}</Td>
                      <Td isDark={isDark} align="right">{it.quantity ?? 0}</Td>
                    </tr>
                  ))}

                  {(!inv.items || !inv.items.length) && (
                    <tr>
                      <td colSpan={12} className={`px-3 py-6 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                        No items match this filter in this invoice.
                      </td>
                    </tr>
                  )}
                </tbody>

                <tfoot className={`
                  border-t-2 backdrop-blur-sm font-semibold
                  ${isDark ? "border-slate-600 bg-slate-800/80" : "border-gray-300 bg-gray-50"}
                `}>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <Td isDark={isDark} colSpan={6} align="right" strong>Tax %</Td>
                    <Td isDark={isDark} colSpan={2} align="right">{(inv.tax_percentage ?? 0).toFixed(2)}</Td>
                    <Td isDark={isDark} colSpan={2} align="right" strong>Tax Amount</Td>
                    <Td isDark={isDark} colSpan={2} align="right">{fmtCurrency(inv.tax_amount)}</Td>
                  </tr>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <Td isDark={isDark} colSpan={6} align="right" strong>Discount %</Td>
                    <Td isDark={isDark} colSpan={2} align="right">{(inv.discount_percentage ?? 0).toFixed(2)}</Td>
                    <Td isDark={isDark} colSpan={2} align="right" strong>Discount Amount</Td>
                    <Td isDark={isDark} colSpan={2} align="right">{fmtCurrency(inv.discount_amount)}</Td>
                  </tr>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <Td isDark={isDark} colSpan={10} align="right" strong>TOTAL</Td>
                    <Td isDark={isDark} colSpan={2} align="right" strong>{fmtCurrency(inv.total_amount)}</Td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
        ))}
      </div>

{/* ===== Supplier Search Modal ===== */}
      <SupplierSearch
        isOpen={supplierSearchOpen}
        onClose={() => setSupplierSearchOpen(false)}
        onSelect={handleSupplierSelect}
      />

      {/* Print styles */}
      <style>{`
        .tabular-nums { font-variant-numeric: tabular-nums; }
        @media print {
          input, button, select, [role="button"], .rs__control { display: none !important; }
          table { font-size: 11px; }
          thead { position: sticky; top: 0; }
        }
      `}</style>
    </div>
  );
}

/* ===== Table helpers ===== */
function Th({ isDark, children, align = "left" }) {
  return (
    <th className={`
      px-3 py-2 font-semibold text-xs uppercase tracking-wider
      ${align === "right" ? "text-right" : "text-left"}
      ${isDark ? "bg-slate-700 text-slate-200" : "bg-gray-100 text-gray-600"}
    `}>
      {children}
    </th>
  );
}

function Td({ isDark, children, align = "left", colSpan, strong = false, className = "" }) {
  return (
    <td
      colSpan={colSpan}
      className={[
        "px-3 py-2 border-t",
        isDark ? "border-slate-600/30" : "border-gray-200/70",
        align === "right" ? "text-right" : "text-left",
        strong ? `font-semibold ${isDark ? "text-slate-200" : "text-gray-800"}` : "",
        isDark ? "text-slate-300" : "text-gray-700",
        className,
      ].join(" ")}
    >
      {children}
    </td>
  );
}

