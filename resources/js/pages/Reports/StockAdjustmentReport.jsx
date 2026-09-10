// resources/js/pages/Reports/StockAdjustmentReport.jsx
import { useEffect, useState, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext";

// glass primitives
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

const n = (v) => (isFinite(Number(v)) ? Number(v) : 0);
const fmtCurrency = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const fmtNumber = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
const fmtDate = (v) => {
  if (!v) return "-";
  if (typeof v === "string") {
    if (/^\d{4}-\d{2}-\d{2}$/.test(v)) return v;
    const d = new Date(v);
    return isNaN(d.getTime()) ? v : d.toISOString().split("T")[0];
  }
  if (v instanceof Date) {
    return v.toISOString().split("T")[0];
  }
  return v;
};

/* ======================
   Main Component
   ====================== */

export default function StockAdjustmentReport() {
  // Filters
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");

  // Data + States
  const [data, setData] = useState({ rows: [], summary: {} });
  const [loading, setLoading] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("stock-adjustment-report") : {
        view: false,
        export: false,
      }),
    [canFor]
  );

  // Get dark mode state and theme colors
  const { isDark, theme } = useTheme();

  // 🎨 Modern button palette (will use dynamic theme colors)
  const tintPrimary = useMemo(() => `
    bg-linear-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintSecondary = useMemo(() => `
    bg-linear-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintGlass = useMemo(() => `
    bg-white/80 dark:bg-slate-700/60 backdrop-blur-xs ring-1 ring-gray-200/60 dark:ring-white/10
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
  const buttonStyle = theme?.button_style || 'rounded-sm';
  
  // Get button style classes and styles based on theme button_style
  const getButtonClasses = useMemo(() => {
    const radiusMap = {
      'rounded-sm': 'rounded-lg',
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

  // Get section styles
  const coreStyles = useMemo(() => getSectionStyles(themeColors, 'primary'), [themeColors]);
  const managementStyles = useMemo(() => getSectionStyles(themeColors, 'secondary'), [themeColors]);

  /* ============ Set default date range on mount ============ */
  useEffect(() => {
    const now = new Date();
    const firstDay = new Date(now.getFullYear(), now.getMonth(), 1);
    const lastDay = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    const formatYMD = (date) => date.toISOString().split("T")[0];

    if (!fromDate) setFromDate(formatYMD(firstDay));
    if (!toDate) setFromDate(formatYMD(lastDay));
  }, []);

  /* ============ Fetch report ============ */
  const fetchReport = async () => {
    if (!can.view) return toast.error("You don't have permission to view this report.");

    setLoading(true);
    try {
      const res = await axios.get("/api/reports/stock-adjustment", {
        params: {
          from: fromDate || undefined,
          to: toDate || undefined,
        },
      });

      const responseData = res.data || {};
      const rows = Array.isArray(responseData.rows) ? responseData.rows : [];
      const summary = responseData.summary || {};

      setData({ rows, summary });
      if (!rows.length) toast("No stock adjustments found for the selected date range.", { icon: "ℹ️" });
    } catch (err) {
      console.error(err);
      toast.error("Failed to fetch Stock Adjustment report");
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
      const res = await axios.get("/api/reports/stock-adjustment/pdf", {
        params: {
          from: fromDate || undefined,
          to: toDate || undefined,
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
    const now = new Date();
    const firstDay = new Date(now.getFullYear(), now.getMonth(), 1);
    const lastDay = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    const formatYMD = (date) => date.toISOString().split("T")[0];

    setFromDate(formatYMD(firstDay));
    setToDate(formatYMD(lastDay));
    setData({ rows: [], summary: {} });
  };

  // Computed values
  const { rows, summary } = data;

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
              <h1 className="text-xl font-extrabold tracking-wide text-white leading-none">Stock Adjustment Report</h1>
              <p className="text-xs text-white/80 mt-1">{rows.length} adjustments</p>
            </div>
          </div>

          {/* Header Actions */}
          <div className="flex items-center gap-2 flex-wrap justify-end">
            <button
              onClick={resetFilters}
              className="h-10 px-3.5 inline-flex items-center gap-1.5 rounded-xl text-sm font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-xs border border-white/20 transition-all duration-200 shadow-lg"
            >
              <ArrowPathIcon className="w-4 h-4" />
              <span>Reset</span>
            </button>
            <Guard when={can.view}>
              <button
                onClick={fetchReport}
                disabled={loading}
                className={`h-10 px-3.5 inline-flex items-center gap-1.5 rounded-xl text-sm font-semibold text-white transition-all duration-200 ${
                  loading ? "opacity-50 cursor-not-allowed" : "bg-white/15 hover:bg-white/25 backdrop-blur-xs border border-white/20 shadow-lg"
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
            <div className="md:col-span-3 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">From Date</label>
              <input
                type="date"
                value={fromDate}
                onChange={(e) => setFromDate(e.target.value)}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/70 bg-slate-900/50 border border-white/30 backdrop-blur-xs focus:outline-hidden focus:ring-2 focus:ring-white/50 scheme-dark"
              />
            </div>

            {/* To Date */}
            <div className="md:col-span-3 flex flex-col gap-1">
              <label className="text-[10px] font-medium uppercase tracking-wide text-white/80">To Date</label>
              <input
                type="date"
                value={toDate}
                onChange={(e) => setToDate(e.target.value)}
                className="w-full h-9 px-2 rounded-lg text-xs text-white placeholder-white/70 bg-slate-900/50 border border-white/30 backdrop-blur-xs focus:outline-hidden focus:ring-2 focus:ring-white/50 scheme-dark"
              />
            </div>

            {/* Buttons */}
            <div className="md:col-span-6 flex flex-wrap gap-2 items-end">
              <Guard when={can.export}>
                <button
                  onClick={exportPdf}
                  disabled={pdfLoading || rows.length === 0}
                  className={`h-9 px-3 inline-flex items-center gap-1.5 rounded-lg text-xs font-semibold text-white transition-all duration-200 ${
                    pdfLoading || rows.length === 0 ? "opacity-40 cursor-not-allowed" : "bg-white/15 hover:bg-white/25 backdrop-blur-xs border border-white/20"
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
      {rows.length === 0 && !loading && (
        <GlassCard>
          <div className={`px-4 py-4 text-sm ${isDark ? "text-slate-400" : "text-gray-600"}`}>
            No stock adjustments found. Adjust filters and click "Load".
          </div>
        </GlassCard>
      )}

      {rows.length > 0 && (
        <>
          {/* ===== Summary KPI Cards ===== */}
          <div className="grid grid-cols-2 md:grid-cols-5 gap-2">
            <KpiCard isDark={isDark} label="Total Adjustments" value={fmtNumber(summary.total_adjustments)} />
            <KpiCard isDark={isDark} label="Total Items" value={fmtNumber(summary.total_items)} />
            <KpiCard isDark={isDark} label="Worth Adjusted" value={fmtCurrency(summary.total_worth_adjusted)} />
            <KpiCard isDark={isDark} label="Positive Adj." value={fmtNumber(summary.positive_adjustments)} />
            <KpiCard isDark={isDark} label="Negative Adj." value={fmtNumber(summary.negative_adjustments)} />
          </div>

          {/* ===== Data Table ===== */}
          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-xs overflow-hidden">
            {/* Table Header */}
            <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
              <div className="flex items-center gap-2">
                <div 
                  className="p-1 rounded-sm"
                  style={{ backgroundColor: themeColors.primaryLight + '40' }}
                >
                  <Squares2X2Icon 
                    className="w-4 h-4" 
                    style={{ color: themeColors.primary }} 
                  />
                </div>
                <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Stock Adjustments</span>
              </div>
              <span className="text-xs text-gray-400">{rows.length} adjustments</span>
            </div>

            <div className="max-h-[75vh] overflow-auto">
              <table className="min-w-[1400px] w-full text-sm">
                <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-xs">
                  <tr className="text-left">
                    <Th isDark={isDark}>#</Th>
                    <Th isDark={isDark}>Adjustment #</Th>
                    <Th isDark={isDark}>Date</Th>
                    <Th isDark={isDark}>Product</Th>
                    <Th isDark={isDark}>Batch</Th>
                    <Th isDark={isDark}>Expiry</Th>
                    <Th align="right" isDark={isDark}>Prev Qty</Th>
                    <Th align="right" isDark={isDark}>Actual Qty</Th>
                    <Th align="right" isDark={isDark}>Diff Qty</Th>
                    <Th align="right" isDark={isDark}>Unit Price</Th>
                    <Th align="right" isDark={isDark}>Worth Adj.</Th>
                    <Th isDark={isDark}>Reason/Note</Th>
                    <Th isDark={isDark}>User</Th>
                  </tr>
                </thead>

                <tbody className="tabular-nums">
                  {rows.map((row, idx) => {
                    const items = row.items || [];
                    if (items.length === 0) {
                      return (
                        <tr key={`row-${row.id || idx}`} className={`
                          border-b border-gray-100 dark:border-slate-600/30
                          odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40
                        `}>
                          <Td colSpan={13} isDark={isDark} className="italic">
                            No items in this adjustment
                          </Td>
                        </tr>
                      );
                    }
                    return items.map((item, itemIdx) => {
                      const isPositive = item.diff_qty > 0;
                      const isNegative = item.diff_qty < 0;
                      return (
                        <tr
                          key={`${row.id}-${item.id || itemIdx}`}
                          className={`
                            transition-colors
                            border-b border-gray-100 dark:border-slate-600/30
                            odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40
                            hover:bg-blue-50 dark:hover:bg-slate-600/50
                          `}
                        >
                          <Td isDark={isDark}>{idx + 1}</Td>
                          <Td isDark={isDark} className="font-medium">{row.posted_number || "-"}</Td>
                          <Td isDark={isDark}>{fmtDate(row.posted_date)}</Td>
                          <Td isDark={isDark} className="font-medium">
                            {item.product_name || "-"}
                            <div className={`text-xs ${isDark ? "text-slate-500" : "text-gray-500"}`}>{item.product_code}</div>
                          </Td>
                          <Td isDark={isDark}>{item.batch_number || "-"}</Td>
                          <Td isDark={isDark}>{fmtDate(item.expiry)}</Td>
                          <Td align="right" isDark={isDark}>{fmtNumber(item.previous_qty)}</Td>
                          <Td align="right" isDark={isDark}>{fmtNumber(item.actual_qty)}</Td>
                          <Td
                            align="right"
                            isDark={isDark}
                            className={`font-semibold ${
                              isPositive
                                ? isDark ? "text-green-400" : "text-green-600"
                                : isNegative
                                  ? isDark ? "text-red-400" : "text-red-600"
                                  : ""
                            }`}
                          >
                            {item.diff_qty > 0 ? "+" : ""}
                            {fmtNumber(item.diff_qty)}
                          </Td>
                          <Td align="right" isDark={isDark}>{fmtCurrency(item.unit_purchase_price)}</Td>
                          <Td
                            align="right"
                            isDark={isDark}
                            className={item.worth_adjusted >= 0 ? (isDark ? "text-emerald-400" : "text-emerald-700") : (isDark ? "text-red-400" : "text-red-700")}
                          >
                            {fmtCurrency(item.worth_adjusted)}
                          </Td>
                          <Td isDark={isDark} className="max-w-xs truncate" title={row.note}>
                            {row.note || "-"}
                          </Td>
                          <Td isDark={isDark}>{row.user_name || "-"}</Td>
                        </tr>
                      );
                    });
                  })}

                  {(!rows || rows.length === 0) && (
                    <tr>
                      <td colSpan={13} className={`px-3 py-6 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                        No stock adjustments found.
                      </td>
                    </tr>
                  )}
                </tbody>

                <tfoot className={`
                  border-t-2 backdrop-blur-xs font-semibold
                  ${isDark ? "border-slate-600 bg-slate-800/80" : "border-gray-300 bg-gray-50"}
                `}>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <Td colSpan={6} align="right" strong isDark={isDark}>TOTALS</Td>
                    <Td align="right" isDark={isDark}>-</Td>
                    <Td align="right" isDark={isDark}>-</Td>
                    <Td align="right" isDark={isDark}>-</Td>
                    <Td align="right" isDark={isDark}>-</Td>
                    <Td align="right" isDark={isDark} className={isDark ? "text-emerald-400" : "text-emerald-800"}>
                      {fmtCurrency(summary.total_worth_adjusted)}
                    </Td>
                    <Td colSpan={4} isDark={isDark}></Td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
        </>
      )}

      {/* Print styles */}
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
function KpiCard({ isDark, label, value }) {
  return (
    <div className={`
      rounded-xl px-3 py-2 backdrop-blur-xs ring-1 shadow-xs
      ${isDark 
        ? "bg-slate-800/60 ring-slate-700/50" 
        : "bg-white/60 ring-gray-200/60"
      }
    `}>
      <div className={`text-[10px] uppercase tracking-wider ${isDark ? "text-slate-400" : "text-gray-500"}`}>{label}</div>
      <div className={`text-lg font-bold tabular-nums ${isDark ? "text-slate-200" : "text-gray-900"}`}>{value}</div>
    </div>
  );
}

/* ===== Table Helpers ===== */
function Th({ children, align = "left", isDark = false }) {
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

function Td({ children, align = "left", colSpan, strong = false, className = "", isDark = false }) {
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
