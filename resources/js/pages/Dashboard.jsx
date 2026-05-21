import { useEffect, useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import AsyncSelect from "react-select/async";
import { createFilter } from "react-select";
import { useTheme } from "@/context/ThemeContext";
import {
  AreaChart,
  Area,
  ResponsiveContainer,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
  BarChart,
  Bar,
  LineChart,
  Line,
  Legend,
  PieChart,
  Pie,
  Cell,
} from "recharts";
import {
  CurrencyDollarIcon,
  ShoppingCartIcon,
  ArrowUturnLeftIcon,
  ArrowUturnDownIcon,
  ArrowTrendingUpIcon,
  ArrowTrendingDownIcon,
  BanknotesIcon,
  ClipboardDocumentListIcon,
  CubeIcon,
  ScaleIcon,
  ClockIcon,
  CheckCircleIcon,
  ArrowPathIcon,
  ChartPieIcon,
} from "@heroicons/react/24/outline";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassInput, GlassBtn } from "@/components/Glass";

// Get theme colors for sections - dynamically from context
const getSectionConfig = (key, theme) => {
  const themeColors = {
    primary: theme?.primary_color || '#3b82f6',
    primaryHover: theme?.primary_hover || '#2563eb',
    primaryLight: theme?.primary_light || '#dbeafe',
    secondary: theme?.secondary_color || '#8b5cf6',
    secondaryHover: theme?.secondary_hover || '#7c3aed',
    secondaryLight: theme?.secondary_light || '#ede9fe',
    tertiary: theme?.tertiary_color || '#06b6d4',
    tertiaryHover: theme?.tertiary_hover || '#0891b2',
    tertiaryLight: theme?.tertiary_light || '#cffafe',
  };
  
  const colorMap = {
    dashboard: { base: themeColors.secondary, light: themeColors.secondaryLight, hover: themeColors.secondaryHover },
    sales: { base: themeColors.primary, light: themeColors.primaryLight, hover: themeColors.primaryHover },
    purchases: { base: themeColors.primary, light: themeColors.primaryLight, hover: themeColors.primaryHover },
    returns: { base: themeColors.tertiary, light: themeColors.tertiaryLight, hover: themeColors.tertiaryHover },
    kpi: { base: themeColors.primary, light: themeColors.primaryLight, hover: themeColors.primaryHover },
  };
  
  const colors = colorMap[key] || colorMap.sales;
  
  return {
    gradient: `from-[${colors.base}] to-[${colors.hover}]`,
    bgLight: "",
    bgDark: "",
    iconColor: `text-[${colors.base}] dark:text-[${colors.base}]`,
    ringColor: `ring-[${colors.base}]`,
    baseColor: colors.base,
    hoverColor: colors.hover,
    lightColor: colors.light,
  };
};

// Modern button palette (matching products page design)
const tintViolet = "bg-gradient-to-br from-violet-500 to-violet-600 text-white shadow-lg shadow-violet-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-violet-500/30 hover:scale-[1.02] hover:from-violet-600 hover:to-violet-700 active:scale-[0.98] transition-all duration-200";
const tintEmerald = "bg-gradient-to-br from-emerald-500 to-emerald-600 text-white shadow-lg shadow-emerald-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-emerald-500/30 hover:scale-[1.02] hover:from-emerald-600 hover:to-emerald-700 active:scale-[0.98] transition-all duration-200";
const tintBlue = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] hover:from-blue-600 hover:to-blue-700 active:scale-[0.98] transition-all duration-200";
const tintRed = "bg-gradient-to-br from-rose-500 to-rose-600 text-white shadow-lg shadow-rose-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700 active:scale-[0.98] transition-all duration-200";
const tintOrange = "bg-gradient-to-br from-orange-500 to-amber-600 text-white shadow-lg shadow-orange-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-orange-500/30 hover:scale-[1.02] hover:from-orange-600 hover:to-amber-700 active:scale-[0.98] transition-all duration-200";
const tintGlass = "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";

// Pie chart colors
const PIE_COLORS = ["#6366f1", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6", "#ec4899"];

// Dark mode compatible select styles
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

// Modern color palette for charts
const COLORS = {
  primary: ["#6366f1", "#8b5cf6", "#a855f7", "#d946ef", "#ec4899", "#f43f5e"],
  success: ["#10b981", "#34d399", "#6ee7b7", "#a7f3d0", "#d1fae5"],
};

// Custom tooltip style
const customTooltipStyle = {
  backgroundColor: "rgba(255, 255, 255, 0.95)",
  backdropFilter: "blur(10px)",
  border: "1px solid rgba(255, 255, 255, 0.3)",
  borderRadius: "12px",
  boxShadow: "0 10px 40px rgba(0, 0, 0, 0.1)",
  padding: "12px 16px",
};

/* ===================== Helpers ===================== */
const localISODate = (d = new Date()) => {
  const tzOffsetMs = d.getTimezoneOffset() * 60000;
  return new Date(d.getTime() - tzOffsetMs).toISOString().slice(0, 10);
};

const todayStr = () => localISODate();
const firstDayOfMonthStr = () => {
  const d = new Date();
  return localISODate(new Date(d.getFullYear(), d.getMonth(), 1));
};

const fmtCurrency = (n) => {
  const v = Number(n || 0);
  return v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
};

const sum = (arr) => arr.reduce((a, b) => a + (Number(b) || 0), 0);

const dateKey = (d) =>
  typeof d === "string" ? d.substring(0, 10) : new Date(d).toISOString().substring(0, 10);

const inclusiveDaysUTC = (fromStr, toStr) => {
  const a = new Date(fromStr), b = new Date(toStr);
  const ua = Date.UTC(a.getFullYear(), a.getMonth(), a.getDate());
  const ub = Date.UTC(b.getFullYear(), b.getMonth(), b.getDate());
  return Math.floor((ub - ua) / 86400000) + 1;
};

const scaffoldSeries = (from, to) => {
  const days = inclusiveDaysUTC(from, to);
  const data = [];
  const start = new Date(from);
  for (let i = 0; i < days; i++) {
    const d = new Date(Date.UTC(start.getFullYear(), start.getMonth(), start.getDate() + i));
    const key = d.toISOString().substring(0, 10);
    data.push({ date: key, value: 0 });
  }
  return data;
};

const mergeSeries = (base, points) => {
  const map = new Map(base.map((p) => [p.date, { ...p }]));
  for (const pt of points || []) {
    const k = dateKey(pt.date);
    map.set(k, { date: k, value: (map.get(k)?.value || 0) + Number(pt.value || 0) });
  }
  return Array.from(map.values());
};



/* ===================== Component ===================== */
export default function Dashboard() {
  const { isDark, theme } = useTheme();
  const [from, setFrom] = useState(todayStr());
  const [to, setTo] = useState(todayStr());
  // Near expiry removed per requirements

  const [loading, setLoading] = useState(false);
  
  const [cards, setCards] = useState({
    sales: 0,
    purchases: 0,
  });

  
  const [series, setSeries] = useState({
    sales: [],
    purchases: [],
  });


  const [invoiceCounts, setInvoiceCounts] = useState({ total: 0, sale_invoices: 0, purchase_invoices: 0 });
  const [kpiMetrics, setKpiMetrics] = useState({
    active_products: 0,
    suppliers: 0,
    brands: 0,
    categories: 0,
  });


  // Get theme colors with defaults
  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || '#3b82f6',
    primaryHover: theme?.primary_hover || '#2563eb',
    primaryLight: theme?.primary_light || '#dbeafe',
    secondary: theme?.secondary_color || '#8b5cf6',
    secondaryHover: theme?.secondary_hover || '#7c3aed',
    secondaryLight: theme?.secondary_light || '#ede9fe',
    tertiary: theme?.tertiary_color || '#06b6d4',
    tertiaryHover: theme?.tertiary_hover || '#0891b2',
    tertiaryLight: theme?.tertiary_light || '#cffafe',
  }), [theme]);

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
      };
    }
    
    // Filled styles for rounded and soft
    return {
      primary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
          color: 'white',
          boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
        }
      },
      secondary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
          color: 'white',
          boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`,
        }
      },
      tertiary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})`,
          color: 'white',
          boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40`,
        }
      },
    };
  }, [buttonStyle, themeColors]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnTertiary = getButtonClasses.tertiary;

  const netSales = useMemo(() => cards.sales || 0, [cards.sales]);


  useEffect(() => {
    fetchAll();
    const onKey = (e) => {
      if (e.altKey && (e.key === "r" || e.key === "R")) {
        e.preventDefault();
        fetchAll();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [from, to]);



  useEffect(() => {
    fetchDashboardMetrics();
  }, [from, to]);

  /* ===================== Async Select Helpers ===================== */
  const loadSuppliers = useMemo(
    () =>
      async (input) => {
        const q = String(input || "").trim();
        if (!q) return [{ value: "", label: "All Suppliers" }];
        try {
          const res = await axios.get("/api/suppliers/search", { params: { q, limit: 30 } });
          const rows = Array.isArray(res.data?.data) ? res.data.data : Array.isArray(res.data) ? res.data : [];
          return rows.map((r) => ({ value: r.id, label: r.name ?? `#${r.id}` }));
        } catch {
          return [{ value: "", label: "No results" }];
        }
      },
    []
  );

  const loadBrands = useMemo(
    () =>
      async (input) => {
        const q = String(input || "").trim();
        if (!q) return [{ value: "", label: "All Brands" }];
        try {
          const res = await axios.get("/api/brands/search", { params: { q, limit: 30 } });
          const rows = Array.isArray(res.data?.data) ? res.data.data : Array.isArray(res.data) ? res.data : [];
          return rows.map((r) => ({ value: r.id, label: r.name ?? `#${r.id}` }));
        } catch {
          return [{ value: "", label: "No results" }];
        }
      },
    []
  );

  async function fetchDashboardMetrics() {
    try {
      const params = { date_from: from, date_to: to };
      const [invoiceRes, kpiRes] = await Promise.allSettled([
        axios.get("/api/dashboard/invoice-counts"),
        axios.get("/api/dashboard/kpi-metrics", { params }),
      ]);

      if (invoiceRes.status === 'fulfilled') {
        setInvoiceCounts(invoiceRes.value.data || { total: 0 });
      }
      if (kpiRes.status === 'fulfilled') {
        setKpiMetrics(kpiRes.value.data || {});
      }
    } catch (err) {
      console.error("Failed to fetch dashboard metrics:", err);
    }
  }



  async function fetchAll() {
    const f = from || todayStr();
    const t = to || todayStr();
    setLoading(true);

    try {
      const { data } = await axios.get("/api/dashboard/summary", {
        params: { date_from: f, date_to: t },
      });

      const scaf = scaffoldSeries(f, t);
      setCards({
        sales: Number(data?.totals?.sales || 0),
        purchases: Number(data?.totals?.purchases || 0),
      });
      setSeries({
        sales: mergeSeries(scaf, data?.series?.sales || []),
        purchases: mergeSeries(scaf, data?.series?.purchases || []),
      });

    } catch (err) {
      console.error(err);
      toast.error("Failed to load dashboard.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="p-4 space-y-4 bg-gradient-to-br from-slate-50 via-white to-blue-50/30 dark:from-slate-900 dark:via-slate-800 dark:to-slate-900">
      {/* ===== Professional Header ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(135deg, ${themeColors.secondary} 0%, ${themeColors.secondaryHover} 100%)` }}
            >
              <ChartPieIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Business Dashboard</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">Overview of your business metrics</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <button
              onClick={fetchAll}
              disabled={loading}
              className={`inline-flex items-center gap-1.5 px-3.5 py-1.5 text-sm font-semibold ${btnSecondary.className}`}
              style={btnSecondary.style}
            >
              <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
              {loading ? "Loading…" : "Refresh"}
            </button>
          </div>
        </div>

        {/* Filters Bar */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="grid grid-cols-1 md:grid-cols-6 gap-3">
            <div className="flex flex-col">
              <label className="text-gray-700 dark:text-gray-300 text-sm mb-1">From</label>
              <GlassInput type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            </div>
            <div className="flex flex-col">
              <label className="text-gray-700 dark:text-gray-300 text-sm mb-1">To</label>
              <GlassInput type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            </div>
            <div className="md:col-span-3 col-span-1 flex items-end gap-2 overflow-x-auto whitespace-nowrap">
              <GlassBtn variant="ghost" onClick={() => { setFrom(todayStr()); setTo(todayStr()); }}>
                Today
              </GlassBtn>
              <GlassBtn variant="ghost" onClick={() => { setFrom(firstDayOfMonthStr()); setTo(todayStr()); }}>
                This Month
              </GlassBtn>
              <GlassBtn variant="ghost" onClick={() => {
                const d = new Date();
                const toStr = todayStr();
                const fromDate = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate() - 6));
                setFrom(fromDate.toISOString().substring(0, 10));
                setTo(toStr);
              }}>
                Last 7 Days
              </GlassBtn>
              <GlassBtn variant="ghost" onClick={() => {
                const d = new Date();
                const toStr = todayStr();
                const fromDate = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate() - 29));
                setFrom(fromDate.toISOString().substring(0, 10));
                setTo(toStr);
              }}>
                Last 30 Days
              </GlassBtn>
            </div>
          </div>
        </div>
      </div>

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-2 gap-3">


        {/* Sales Card */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden group hover:shadow-lg hover:scale-[1.02] transition-all duration-300">
          <div 
            className="h-1.5"
            style={{ background: `linear-gradient(to right, ${themeColors.primary}, ${themeColors.primaryHover})` }}
          />
          <div className="p-4">
            <div className="flex items-center justify-between mb-2">
              <div 
                className="p-1.5 rounded-lg"
                style={{ backgroundColor: themeColors.primaryLight }}
              >
                <CurrencyDollarIcon className="w-5 h-5" style={{ color: themeColors.primary }} />
              </div>
              <span className="text-xs font-medium" style={{ color: themeColors.primary }}>Sales</span>
            </div>
            <div className="text-2xl font-bold text-gray-900 dark:text-white">Rs {fmtCurrency(cards.sales)}</div>
            <div className="h-6 mt-2">
              <svg viewBox="0 0 100 20" className="w-full h-full" preserveAspectRatio="none">
                <path d="M0 15 Q 25 12, 50 10 T 100 5" fill="none" stroke={themeColors.primary} strokeWidth="2" className="opacity-60" />
              </svg>
            </div>
          </div>
        </div>

        {/* Purchases Card */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden group hover:shadow-lg hover:scale-[1.02] transition-all duration-300">
          <div 
            className="h-1.5"
            style={{ background: `linear-gradient(to right, ${themeColors.secondary}, ${themeColors.secondaryHover})` }}
          />
          <div className="p-4">
            <div className="flex items-center justify-between mb-2">
              <div 
                className="p-1.5 rounded-lg"
                style={{ backgroundColor: themeColors.secondaryLight }}
              >
                <ShoppingCartIcon className="w-5 h-5" style={{ color: themeColors.secondary }} />
              </div>
              <span className="text-xs font-medium" style={{ color: themeColors.secondary }}>Purchases</span>
            </div>
            <div className="text-2xl font-bold text-gray-900 dark:text-white">Rs {fmtCurrency(cards.purchases)}</div>
            <div className="h-6 mt-2">
              <svg viewBox="0 0 100 20" className="w-full h-full" preserveAspectRatio="none">
                <path d="M0 15 Q 25 10, 50 8 T 100 3" fill="none" stroke={themeColors.secondary} strokeWidth="2" className="opacity-60" />
              </svg>
            </div>
          </div>
        </div>


      </div>

      {/* KPI Metrics Row */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3 items-stretch">



        {/* Net Sales */}
        <div 
          className="rounded-xl p-3 text-white shadow-lg hover:shadow-xl transition-shadow duration-300"
          style={{ background: `linear-gradient(135deg, ${themeColors.primary} 0%, ${themeColors.primaryHover} 100%)` }}
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="p-1.5 bg-white/20 rounded-lg backdrop-blur-sm">
              <BanknotesIcon className="w-4 h-4" />
            </div>
            <span className="text-xs font-medium text-white/80">Net Sales</span>
          </div>
          <div className="text-xl font-bold">Rs {fmtCurrency(netSales)}</div>
        </div>

        {/* Invoices */}
        <div 
          className="rounded-xl p-3 text-white shadow-lg hover:shadow-xl transition-shadow duration-300"
          style={{ background: `linear-gradient(135deg, ${themeColors.secondary} 0%, ${themeColors.secondaryHover} 100%)` }}
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="p-1.5 bg-white/20 rounded-lg backdrop-blur-sm">
              <ClipboardDocumentListIcon className="w-4 h-4" />
            </div>
            <span className="text-xs font-medium text-white/80">Invoices</span>
          </div>
          <div className="text-xl font-bold">{invoiceCounts.total}</div>
          <div className="text-[10px] text-white/70 mt-1 truncate">
            {invoiceCounts.sale_invoices} sales, {invoiceCounts.purchase_invoices} purchases
          </div>
        </div>

        {/* Products */}
        <div 
          className="rounded-xl p-3 text-white shadow-lg hover:shadow-xl transition-shadow duration-300"
          style={{ background: `linear-gradient(135deg, ${themeColors.primary} 0%, ${themeColors.primaryHover} 100%)` }}
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="p-1.5 bg-white/20 rounded-lg backdrop-blur-sm">
              <CubeIcon className="w-4 h-4" />
            </div>
            <span className="text-xs font-medium text-white/80">Products</span>
          </div>
          <div className="text-xl font-bold">{kpiMetrics.active_products || 0}</div>
        </div>

        {/* Suppliers */}
        <div 
          className="rounded-xl p-3 text-white shadow-lg hover:shadow-xl transition-shadow duration-300"
          style={{ background: `linear-gradient(135deg, ${themeColors.tertiary} 0%, ${themeColors.tertiaryHover} 100%)` }}
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="p-1.5 bg-white/20 rounded-lg backdrop-blur-sm">
              <ScaleIcon className="w-4 h-4" />
            </div>
            <span className="text-xs font-medium text-white/80">Suppliers</span>
          </div>
          <div className="text-xl font-bold">{kpiMetrics.suppliers || 0}</div>
        </div>

        {/* Brands */}

        <div 
          className="rounded-xl p-3 text-white shadow-lg hover:shadow-xl transition-shadow duration-300"
          style={{ background: `linear-gradient(135deg, ${themeColors.secondary} 0%, ${themeColors.secondaryHover} 100%)` }}
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="p-1.5 bg-white/20 rounded-lg backdrop-blur-sm">
              <CheckCircleIcon className="w-4 h-4" />
            </div>
            <span className="text-xs font-medium text-white/80">Brands</span>
          </div>
          <div className="text-xl font-bold">{kpiMetrics.brands || 0}</div>
        </div>
      </div>

      {/* Charts Row - Three Different Types */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">

        {/* Pie Chart - Sales Distribution */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
          <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-100 dark:border-slate-700">
            <div 
              className="p-1.5 rounded-lg"
              style={{ backgroundColor: themeColors.secondaryLight }}
            >
              <ChartPieIcon className="w-4 h-4" style={{ color: themeColors.secondary }} />
            </div>
            <div>
              <h3 className="text-sm font-semibold text-gray-800 dark:text-gray-100">Sales Distribution</h3>
              <p className="text-xs text-gray-500 dark:text-gray-400">Pie chart - Revenue split</p>
            </div>
          </div>
          <div className="p-4">
            <div className="h-48" style={{ minHeight: '192px' }}>
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <PieChart>
                  <Pie
                    data={[
                      { name: "Sales", value: cards.sales },
                      { name: "Purchases", value: cards.purchases },
                    ]}
                    cx="50%"
                    cy="50%"
                    innerRadius={40}
                    outerRadius={70}
                    paddingAngle={5}
                    dataKey="value"
                  >
                    <Cell fill={themeColors.primary} />
                    <Cell fill={themeColors.secondary} />
                  </Pie>
                  <Tooltip
                    contentStyle={{
                      backgroundColor: "rgba(255, 255, 255, 0.95)",
                      backdropFilter: "blur(10px)",
                      border: "1px solid rgba(255, 255, 255, 0.3)",
                      borderRadius: "12px",
                      boxShadow: "0 10px 40px rgba(0, 0, 0, 0.1)",
                      padding: "12px 16px",
                    }}
                    formatter={(val) => [`Rs ${fmtCurrency(val)}`, ""]}
                  />
                  <Legend iconType="circle" wrapperStyle={{ paddingTop: "10px" }} formatter={(value) => <span className="text-xs text-gray-600 dark:text-gray-300">{value}</span>} />

                </PieChart>

              </ResponsiveContainer>
            </div>
          </div>
        </div>

        {/* Bar Chart - Sales vs Purchases */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
          <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-100 dark:border-slate-700">
            <div 
              className="p-1.5 rounded-lg"
              style={{ backgroundColor: themeColors.primaryLight }}
            >
              <ShoppingCartIcon className="w-4 h-4" style={{ color: themeColors.primary }} />
            </div>
            <div>
              <h3 className="text-sm font-semibold text-gray-800 dark:text-gray-100">Sales vs Purchases</h3>
              <p className="text-xs text-gray-500 dark:text-gray-400">Bar chart - Comparison</p>
            </div>
          </div>
          <div className="p-4">
            <div className="h-48" style={{ minHeight: '192px' }}>
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <BarChart data={[
                  { name: 'Sales', value: cards.sales },
                  { name: 'Purchases', value: cards.purchases },
                ]} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" opacity={0.15} stroke={themeColors.primary} />
                  <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={{ stroke: '#475569' }} tickLine={false} />
                  <YAxis tick={{ fontSize: 10, fill: '#94a3b8' }} axisLine={false} tickLine={false} tickFormatter={(val) => `Rs ${(val / 1000).toFixed(0)}k`} />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: "rgba(255, 255, 255, 0.95)",
                      backdropFilter: "blur(10px)",
                      border: "1px solid rgba(255, 255, 255, 0.3)",
                      borderRadius: "12px",
                      boxShadow: "0 10px 40px rgba(0, 0, 0, 0.1)",
                      padding: "12px 16px",
                    }}
                    formatter={(val) => [`Rs ${fmtCurrency(val)}`, '']}
                  />
                  <Bar dataKey="value" radius={[8, 8, 0, 0]}>
                    <Cell fill={themeColors.primary} />
                    <Cell fill={themeColors.secondary} />
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>


      </div>
    </div>
  );
}

