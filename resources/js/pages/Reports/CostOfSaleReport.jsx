// resources/js/pages/CostOfSaleReport.jsx
import { useMemo, useState } from "react";
import axios from "axios";
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
  DocumentChartBarIcon,
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

/* ========== Helpers ========== */
const todayStr = () => new Date().toISOString().split("T")[0];
const yesterdayStr = () => {
  const d = new Date();
  d.setDate(d.getDate() - 1);
  return d.toISOString().split("T")[0];
};
const n = (v) => (isFinite(Number(v)) ? Number(v) : 0);
const fmtCurrency = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const fmtPct = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + "%";

export default function CostOfSaleReport() {
  // Default from = yesterday, to = today
  const [fromDate, setFromDate] = useState(yesterdayStr());
  const [toDate, setToDate] = useState(todayStr());
  const [invoiceType, setInvoiceType] = useState("all"); // all, credit, debit
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("cost-of-sale-report") : {
        view: false,
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
        amber: '#f59e0b',
        amberHover: '#d97706',
        amberLight: '#fef3c7',
        rose: '#f43f5e',
        roseHover: '#e11d48',
        roseLight: '#ffe4e6',
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
      rose: '#f43f5e',
      roseHover: '#e11d48',
      roseLight: '#ffe4e6',
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

  // === Fetch report only when user clicks Apply/Load ===
  const fetchReport = async () => {
    if (!can.view) {
      toast.error("You don't have permission to view this report.");
      return;
    }
    if (!fromDate || !toDate) return toast.error("Please select both dates.");
    if (fromDate > toDate) return toast.error("From Date cannot be after To Date.");

    setLoading(true);
    try {
      const res = await axios.get("/api/reports/cost-of-sale", {
        params: { from: fromDate, to: toDate, invoice_type: invoiceType },
      });
      const data = Array.isArray(res.data)
        ? res.data
        : Array.isArray(res.data?.data)
        ? res.data.data
        : [];
      setRows(
        data.map((r) => ({
          sale_date: r.sale_date || r.date || "",
          gross_sale: n(r.gross_sale),
          item_discount: n(r.item_discount),
          discount_amount: n(r.discount_amount),
          tax_amount: n(r.tax_amount),
          total_sales: n(r.total_sales),
          sale_return: n(r.sale_return),
          cost_of_sales: n(r.cost_of_sales),
        }))
      );
    } catch (err) {
      console.error(err);
      toast.error("Failed to load Cost of Sale report");
      setRows([]);
    } finally {
      setLoading(false);
    }
  };

  const computed = useMemo(() => {
    const withDerived = rows.map((r) => {
      const net_sale = n(r.total_sales) - n(r.sale_return);
      const gp_amount = net_sale - n(r.cost_of_sales);
      const gp_pct = net_sale > 0 ? (gp_amount / net_sale) * 100 : 0;
      return { ...r, net_sale, gp_amount, gp_pct };
    });

    const totals = withDerived.reduce(
      (acc, r) => {
        acc.gross_sale += r.gross_sale;
        acc.item_discount += r.item_discount;
        acc.discount_amount += r.discount_amount;
        acc.tax_amount += r.tax_amount;
        acc.total_sales += r.total_sales;
        acc.sale_return += r.sale_return;
        acc.net_sale += r.net_sale;
        acc.cost_of_sales += r.cost_of_sales;
        acc.gp_amount += r.gp_amount;
        return acc;
      },
      {
        gross_sale: 0,
        item_discount: 0,
        discount_amount: 0,
        tax_amount: 0,
        total_sales: 0,
        sale_return: 0,
        net_sale: 0,
        cost_of_sales: 0,
        gp_amount: 0,
      }
    );
    const totals_gp_pct = totals.net_sale > 0 ? (totals.gp_amount / totals.net_sale) * 100 : 0;

    return { withDerived, totals, totals_gp_pct };
  }, [rows]);

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
              <DocumentChartBarIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Cost of Sale Report</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{computed.withDerived.length} entries</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <GlassBtn
              className={`h-9 ${btnPrimary.className}`}
              title="Reset to Default (Yesterday → Today)"
              onClick={() => {
                setFromDate(yesterdayStr());
                setToDate(todayStr());
                setRows([]);
              }}
              style={btnPrimary.style}
            >
              Reset
            </GlassBtn>
            <Guard when={can.view}>
              <GlassBtn
                className={`h-9 ${btnPrimary.className}`}
                title="Load / Refresh"
                onClick={fetchReport}
                disabled={loading}
                style={btnPrimary.style}
              >
                <span className="inline-flex items-center gap-2">
                  <ArrowPathIcon className={`w-5 h-5 ${loading ? "animate-spin" : ""}`} />
                  {loading ? "Loading…" : "Load"}
                </span>
              </GlassBtn>
            </Guard>
          </div>
        </div>

        {/* Filter toolbar */}
        <GlassToolbar className="grid grid-cols-1 sm:grid-cols-3 lg:grid-cols-12 gap-3">
          {/* From Date */}
          <div className="sm:col-span-1 lg:col-span-2">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>From</label>
            <GlassInput type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} className="w-full" />
          </div>
          
          {/* To Date */}
          <div className="sm:col-span-1 lg:col-span-2">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>To</label>
            <GlassInput type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} className="w-full" />
          </div>
          
          {/* Invoice Type Filter */}
          <div className="sm:col-span-1 lg:col-span-3">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>Sale Type</label>
            <div className="relative">
              <select
                value={invoiceType}
                onChange={(e) => setInvoiceType(e.target.value)}
                className={`w-full h-9 px-3 pr-8 text-sm border rounded-lg appearance-none focus:outline-none focus:ring-2 focus:ring-blue-500 cursor-pointer transition-all duration-200 ${
                  isDark 
                    ? "bg-slate-800 border-slate-600 text-slate-200" 
                    : "bg-white border-gray-200 text-gray-900"
                }`}
              >
                <option value="all">All Sales</option>
                <option value="credit">Credit Sales</option>
                <option value="debit">Debit Sales</option>
              </select>
              {/* Dropdown arrow */}
              <div className="absolute inset-y-0 right-0 flex items-center px-2 pointer-events-none">
                <svg className={`w-4 h-4 ${isDark ? "text-slate-400" : "text-gray-500"}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                </svg>
              </div>
            </div>
          </div>

          {/* Quick date filters */}
          <div className="sm:col-span-1 lg:col-span-5 flex flex-wrap items-end gap-2">
            <GlassBtn
              className={`h-9 ${btnSecondary.className}`}
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 1);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
              }}
              style={btnSecondary.style}
            >
              Today
            </GlassBtn>

            <GlassBtn
              className={`h-9 ${btnTertiary.className}`}
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 3);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
              }}
              style={btnTertiary.style}
            >
              3 Days
            </GlassBtn>

            <GlassBtn
              className={`h-9 ${btnEmerald.className}`}
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 7);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
              }}
              style={btnEmerald.style}
            >
              7 Days
            </GlassBtn>
          </div>
        </GlassToolbar>
      </GlassCard>

      {/* ===== Data Table ===== */}
      <GlassCard className="relative z-10">
        <div className="max-h-[65vh] overflow-auto rounded-b-2xl">
          <table className="min-w-[1200px] w-full text-sm">
            <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
              <tr className="text-left">
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Date</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Gross Sale</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Item Disc.</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Flat Disc.</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Tax</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Total Sales</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Sale Return</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Net Sale</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">Cost of Sales</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">GP (Amt)</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right">GP %</th>
              </tr>
            </thead>

            <tbody>
              {computed.withDerived.length === 0 && !loading && (
                <tr>
                  <td colSpan={11} className={`px-3 py-10 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                    No data for the selected date range.
                  </td>
                </tr>
              )}

              {computed.withDerived.map((r, idx) => (
                <tr
                  key={r.sale_date + "_" + idx}
                  className={`
                    transition-colors
                    border-b border-gray-100 dark:border-slate-600/30
                    odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40
                    hover:bg-blue-50 dark:hover:bg-slate-600/50
                  `}
                >
                  <td className="px-3 py-2 text-gray-700 dark:text-gray-300">{r.sale_date}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(r.gross_sale)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(r.item_discount)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(r.discount_amount)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(r.tax_amount)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(r.total_sales)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(r.sale_return)}</td>
                  <td className="px-3 py-2 text-right tabular-nums font-medium text-gray-800 dark:text-gray-200">{fmtCurrency(r.net_sale)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(r.cost_of_sales)}</td>
                  <td className="px-3 py-2 text-right tabular-nums font-medium text-emerald-600 dark:text-emerald-400">{fmtCurrency(r.gp_amount)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtPct(r.gp_pct)}</td>
                </tr>
              ))}
            </tbody>

            <tfoot className={`
              border-t-2 backdrop-blur-sm font-semibold
              ${isDark ? "border-slate-600 bg-slate-800/80" : "border-gray-300 bg-gray-50"}
            `}>
              <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                <td className="px-3 py-2 text-right text-gray-700 dark:text-gray-300">TOTALS</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(computed.totals.gross_sale)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(computed.totals.item_discount)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(computed.totals.discount_amount)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(computed.totals.tax_amount)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(computed.totals.total_sales)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(computed.totals.sale_return)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-800 dark:text-gray-200">{fmtCurrency(computed.totals.net_sale)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtCurrency(computed.totals.cost_of_sales)}</td>
                <td className="px-3 py-2 text-right tabular-nums font-bold text-emerald-700 dark:text-emerald-400">{fmtCurrency(computed.totals.gp_amount)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-700 dark:text-gray-300">{fmtPct(computed.totals_gp_pct)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </GlassCard>

      {/* ===== KPI Summary ===== */}
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3">
        <KpiCard
          label="Net Sale"
          value={fmtCurrency(computed.totals.net_sale)}
          isDark={isDark}
          themeColors={themeColors}
          highlight
        />
        <KpiCard
          label="Cost of Sales"
          value={fmtCurrency(computed.totals.cost_of_sales)}
          isDark={isDark}
          themeColors={themeColors}
        />
        <KpiCard
          label="Gross Profit"
          value={fmtCurrency(computed.totals.gp_amount)}
          isDark={isDark}
          themeColors={themeColors}
        />
        <KpiCard
          label="GP %"
          value={fmtPct(computed.totals_gp_pct)}
          isDark={isDark}
          themeColors={themeColors}
        />
        <KpiCard
          label="Gross Sale"
          value={fmtCurrency(computed.totals.gross_sale)}
          isDark={isDark}
          themeColors={themeColors}
        />
        <KpiCard
          label="Total Sales"
          value={fmtCurrency(computed.totals.total_sales)}
          isDark={isDark}
          themeColors={themeColors}
        />
      </div>

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

/* ===== KPI Card Component ===== */
function KpiCard({ label, value, isDark, highlight = false, themeColors }) {
  // Determine gradient based on label
  const getGradient = () => {
    if (label === "Net Sale") return `from-[${themeColors.primary}] to-[${themeColors.primaryHover}]`;
    if (label === "Cost of Sales") return `from-[${themeColors.rose}] to-[${themeColors.roseHover}]`;
    if (label === "Gross Profit") return `from-[${themeColors.emerald}] to-[${themeColors.emeraldHover}]`;
    if (label === "GP %") return `from-[${themeColors.secondary}] to-[${themeColors.secondaryHover}]`;
    if (label === "Gross Sale") return `from-[${themeColors.amber}] to-[${themeColors.amberHover}]`;
    if (label === "Total Sales") return `from-[${themeColors.secondary}] to-[${themeColors.secondaryHover}]`;
    return `from-[${themeColors.primary}] to-[${themeColors.primaryHover}]`;
  };

  const cardGradient = getGradient();

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
        <span className={`text-xs uppercase tracking-wide ${isDark ? "text-slate-400" : "text-gray-600"}`}>{label}</span>
      </div>
      <div className="text-lg font-bold tabular-nums text-gray-900 relative z-10">{value}</div>
    </div>
  );
}

