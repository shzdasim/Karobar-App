// resources/js/pages/Reports/SaleDetailReport.jsx
import { useEffect, useMemo, useState } from "react";
import axios from "axios";
import AsyncSelect from "react-select/async";
import { createFilter } from "react-select";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions";

// 🧊 glass primitives
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";
import { useTheme } from "@/context/ThemeContext";

import { 
  ArrowDownOnSquareIcon, 
  PencilSquareIcon, 
  CheckCircleIcon, 
  XCircleIcon,
  DocumentTextIcon,
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
const todayStr = () => new Date().toISOString().split("T")[0];
const yesterdayStr = () => {
  const d = new Date();
  d.setDate(d.getDate() - 1);
  return d.toISOString().split("T")[0];
};
const n = (v) => (isFinite(Number(v)) ? Number(v) : 0);
const fmtCurrency = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Helper to merge dark mode styles - returns function-based styles for react-select
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

export default function SaleDetailReport() {
  // Default date range: Yesterday → Today
  const [fromDate, setFromDate] = useState(yesterdayStr());
  const [toDate, setToDate] = useState(todayStr());

  // Filters
  const [customerValue, setCustomerValue] = useState(null);
  const [customerId, setCustomerId] = useState("");
  const [productValue, setProductValue] = useState(null);
  const [productId, setProductId] = useState("");

  // Data + States
  const [data, setData] = useState([]);
  const [loading, setLoading] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("sale-detail-report") : {
        view: false,
        export: false,
        edit: false,
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
        sky: '#0ea5e9',
        skyHover: '#0284c7',
        rose: '#f43f5e',
        roseHover: '#e11d48',
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
      sky: '#0ea5e9',
      skyHover: '#0284c7',
      rose: theme.danger_color || '#f43f5e',
      roseHover: '#e11d48',
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
  const loadCustomers = useMemo(
    () => async (input) => {
      const q = String(input || "").trim();
      if (!q) return [{ value: "", label: "All Customers" }];
      try {
        const res = await tryEndpoints(
          ["/api/customers/search", "/customers/search"],
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
        toast.error("Customer search failed");
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
    []
  );

  /* ============ Fetch report ============ */
  const fetchReport = async () => {
    if (!can.view) return toast.error("You don't have permission to view this report.");
    if (fromDate > toDate) return toast.error("'From' date cannot be after 'To' date.");

    setLoading(true);
    try {
      const res = await axios.get("/api/reports/sale-detail", {
        params: {
          from: fromDate,
          to: toDate,
          customer_id: customerId || undefined,
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
      toast.error("Failed to fetch Sale Detail report");
      setData([]);
    } finally {
      setLoading(false);
    }
  };

  const [editingInvoiceId, setEditingInvoiceId] = useState(null);
  const [editDoctor, setEditDoctor] = useState("");
  const [editPatient, setEditPatient] = useState("");
  const [saving, setSaving] = useState(false);

  const saveInvoiceMeta = (inv) => async () => {
    setSaving(true);
    try {
      const res = await axios.put(
        `/api/sale-invoices/${inv.id}/meta`,
        {
          doctor_name: editDoctor,
          patient_name: editPatient,
        }
      );

      setData((prev) =>
        prev.map((x) =>
          x.id === inv.id
            ? {
                ...x,
                doctor_name: res.data.doctor_name,
                patient_name: res.data.patient_name,
              }
            : x
        )
      );

      toast.success("Updated");
      setEditingInvoiceId(null);
    } catch {
      toast.error("Update failed");
    } finally {
      setSaving(false);
    }
  };

  /* ============ Export PDF ============ */
  const exportPdf = async () => {
    if (!can.export) return toast.error("You don't have permission to export PDF.");
    setPdfLoading(true);
    try {
      const res = await axios.get("/api/reports/sale-detail/pdf", {
        params: {
          from: fromDate,
          to: toDate,
          customer_id: customerId || undefined,
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
              <DocumentTextIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Sale Detail Report</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{data.length} entries</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <GlassBtn
              className="h-9"
              onClick={resetFilters}
              style={{
                color: isDark ? themeColors.primary : themeColors.primary,
              }}
            >
              Reset
            </GlassBtn>
            <Guard when={can.view}>
              <GlassBtn
                className="h-9"
                onClick={fetchReport}
                disabled={loading}
                style={{
                  background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                  color: primaryTextColor,
                  boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                }}
              >
                <span className="inline-flex items-center gap-2">
                  {loading ? "Loading…" : "Load"}
                </span>
              </GlassBtn>
            </Guard>
          </div>
        </div>

        {/* Filters */}
        <GlassToolbar className="grid grid-cols-1 md:grid-cols-12 gap-3">
          {/* From Date */}
          <div className="md:col-span-2">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>From</label>
            <GlassInput
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="w-full"
            />
          </div>

          {/* To Date */}
          <div className="md:col-span-2">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>To</label>
            <GlassInput
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="w-full"
            />
          </div>

          {/* Customer */}
          <div className="md:col-span-4">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>Customer</label>
            <AsyncSelect
              cacheOptions
              defaultOptions={[{ value: "", label: "All Customers" }]}
              loadOptions={loadCustomers}
              isClearable
              value={customerValue}
              onChange={(opt) => {
                setCustomerValue(opt);
                setCustomerId(opt?.value || "");
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
          <div className="md:col-span-4">
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

          {/* Quick Filters & Export */}
          <div className="md:col-span-12 flex flex-wrap items-end gap-2">
            <GlassBtn
              className="h-9"
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 1);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
                fetchReport();
              }}
              style={{
                color: isDark ? themeColors.primary : themeColors.primary,
              }}
            >
              Today
            </GlassBtn>

            <GlassBtn
              className="h-9"
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 3);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
                fetchReport();
              }}
              style={{
                color: isDark ? themeColors.secondary : themeColors.secondary,
              }}
            >
              3 Days
            </GlassBtn>

            <GlassBtn
              className="h-9"
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 7);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
                fetchReport();
              }}
              style={{
                color: isDark ? themeColors.emerald : themeColors.emerald,
              }}
            >
              7 Days
            </GlassBtn>

            <Guard when={can.export}>
              <GlassBtn
                className="h-9"
                onClick={exportPdf}
                disabled={pdfLoading || data.length === 0}
                style={{
                  background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                  color: secondaryTextColor,
                  boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`
                }}
              >
                <span className="inline-flex items-center gap-2">
                  <ArrowDownOnSquareIcon className="w-5 h-5" />
                  {pdfLoading ? "Generating…" : "Export PDF"}
                </span>
              </GlassBtn>
            </Guard>
          </div>
        </GlassToolbar>
      </GlassCard>

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
        {data.map((inv, idxInv) => (
          <div
            key={idxInv + "-" + (inv.posted_number ?? "") + "-" + (inv.invoice_date ?? "")}
            className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden"
          >
            {/* Invoice Header (matching Products page table header style) */}
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
                  {inv.customer_name || "—"}
                </span>
              </div>
              <div className="flex items-center gap-4 text-xs text-gray-500 dark:text-gray-400">
                <span>Posted #: {inv.posted_number || "-"}</span>
                <span>{inv.invoice_date || "-"}</span>
              </div>
            </div>

            {/* Metadata Bar */}
            <div className={`px-4 py-2 border-b ${isDark ? "border-slate-700 bg-slate-800/50" : "border-gray-100 bg-gray-50/50"}`}>
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-6 text-xs">
                  <KV isDark={isDark} label="User:" value={inv.user_name || "-"} />
                  <KV isDark={isDark} label="Doctor:" value={inv.doctor_name || "-"} />
                  <KV isDark={isDark} label="Patient:" value={inv.patient_name || "-"} />
                </div>
                <Guard when={can.edit}>
                  {editingInvoiceId === inv.id ? (
                    <div className="flex gap-1">
                      <GlassBtn
                        className="h-7 px-2"
                        disabled={saving}
                        onClick={saveInvoiceMeta(inv)}
                        style={{
                          background: `linear-gradient(to bottom right, ${themeColors.emerald}, ${themeColors.emeraldHover})`,
                          color: '#ffffff',
                        }}
                      >
                        <CheckCircleIcon className="w-4 h-4" />
                      </GlassBtn>
                      <GlassBtn
                        className="h-7 px-2"
                        onClick={() => setEditingInvoiceId(null)}
                        style={{
                          background: `linear-gradient(to bottom right, ${themeColors.rose}, ${themeColors.roseHover})`,
                          color: '#ffffff',
                        }}
                      >
                        <XCircleIcon className="w-4 h-4" />
                      </GlassBtn>
                    </div>
                  ) : (
                    <GlassBtn
                      className="h-7 px-2"
                      onClick={() => {
                        setEditingInvoiceId(inv.id);
                        setEditDoctor(inv.doctor_name || "");
                        setEditPatient(inv.patient_name || "");
                      }}
                      style={{
                        background: `linear-gradient(to bottom right, ${themeColors.sky}, ${themeColors.skyHover})`,
                        color: '#ffffff',
                      }}
                    >
                      <PencilSquareIcon className="w-4 h-4" />
                    </GlassBtn>
                  )}
                </Guard>
              </div>
              
              {/* Edit inputs */}
              {editingInvoiceId === inv.id && (
                <div className="flex gap-2 mt-2">
                  <GlassInput
                    className="h-7 text-xs"
                    placeholder="Doctor"
                    value={editDoctor}
                    onChange={(e) => setEditDoctor(e.target.value)}
                  />
                  <GlassInput
                    className="h-7 text-xs"
                    placeholder="Patient"
                    value={editPatient}
                    onChange={(e) => setEditPatient(e.target.value)}
                  />
                </div>
              )}
            </div>

            {/* Items Table */}
            <div className="relative max-w-full overflow-auto">
              <table className="w-full min-w-[900px] text-sm">
                <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
                  <tr className="text-left">
                    <Th isDark={isDark}>Product Name</Th>
                    <Th isDark={isDark} align="right">Pack Size</Th>
                    <Th isDark={isDark}>Batch #</Th>
                    <Th isDark={isDark}>Expiry</Th>
                    <Th isDark={isDark} align="right">Current Qty</Th>
                    <Th isDark={isDark} align="right">Qty</Th>
                    <Th isDark={isDark} align="right">Price</Th>
                    <Th isDark={isDark} align="right">Disc %</Th>
                    <Th isDark={isDark} align="right">Sub Total</Th>
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
                      <Td isDark={isDark} align="right">{it.pack_size ?? 0}</Td>
                      <Td isDark={isDark}>{it.batch_number || "-"}</Td>
                      <Td isDark={isDark}>{it.expiry || "-"}</Td>
                      <Td isDark={isDark} align="right">{it.current_quantity ?? 0}</Td>
                      <Td isDark={isDark} align="right">{it.quantity ?? 0}</Td>
                      <Td isDark={isDark} align="right">{fmtCurrency(it.price)}</Td>
                      <Td isDark={isDark} align="right">{(it.item_discount_percentage ?? 0).toFixed(2)}</Td>
                      <Td isDark={isDark} align="right">{fmtCurrency(it.sub_total)}</Td>
                    </tr>
                  ))}

                  {(!inv.items || !inv.items.length) && (
                    <tr>
                      <td colSpan={9} className={`px-3 py-6 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>
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
                    <Td isDark={isDark} colSpan={6} align="right" strong>Discount %</Td>
                    <Td isDark={isDark} colSpan={1} align="right">{(inv.discount_percentage ?? 0).toFixed(2)}</Td>
                    <Td isDark={isDark} colSpan={1} align="right" strong>Discount Amt</Td>
                    <Td isDark={isDark} colSpan={1} align="right">{fmtCurrency(inv.discount_amount)}</Td>
                  </tr>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <Td isDark={isDark} colSpan={6} align="right" strong>Tax %</Td>
                    <Td isDark={isDark} colSpan={1} align="right">{(inv.tax_percentage ?? 0).toFixed(2)}</Td>
                    <Td isDark={isDark} colSpan={1} align="right" strong>Tax Amt</Td>
                    <Td isDark={isDark} colSpan={1} align="right">{fmtCurrency(inv.tax_amount)}</Td>
                  </tr>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <Td isDark={isDark} colSpan={8} align="right" strong>Item Discount</Td>
                    <Td isDark={isDark} colSpan={1} align="right">{fmtCurrency(inv.item_discount)}</Td>
                  </tr>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <Td isDark={isDark} colSpan={8} align="right" strong>Gross Amount</Td>
                    <Td isDark={isDark} colSpan={1} align="right">{fmtCurrency(inv.gross_amount)}</Td>
                  </tr>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <Td isDark={isDark} colSpan={8} align="right" strong>TOTAL</Td>
                    <Td isDark={isDark} colSpan={1} align="right" strong>{fmtCurrency(inv.total)}</Td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
        ))}
      </div>

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
function KV({ isDark, label, value }) {
  return (
    <div className="text-xs">
      <span className={`${isDark ? "text-slate-400" : "text-gray-500"}`}>{label}</span>{" "}
      <span className={`font-medium ${isDark ? "text-slate-200" : "text-gray-700"}`}>{value}</span>
    </div>
  );
}

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

