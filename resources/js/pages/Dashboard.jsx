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

function mergeTwo(a = [], b = []) {
  const map = new Map();
  for (const r of a) map.set(r.date, { date: r.date, a: Number(r.value || 0), b: 0 });
  for (const r of b) {
    const row = map.get(r.date) || { date: r.date, a: 0, b: 0 };
    row.b += Number(r.value || 0);
    map.set(r.date, row);
  }
  return Array.from(map.values());
}

function buildNetSeries(series) {
  const map = new Map();
  for (const row of series.sales || []) map.set(row.date, (map.get(row.date) || 0) + Number(row.value || 0));
  for (const row of series.saleReturns || [])
    map.set(row.date, (map.get(row.date) || 0) - Number(row.value || 0));
  return Array.from(map.entries()).map(([date, value]) => ({ date, value }));
}

/* ===================== Component ===================== */
export default function Dashboard() {
  const { isDark, theme } = useTheme();
  const [from, setFrom] = useState(todayStr());
  const [to, setTo] = useState(todayStr());
  const [expiryMonths, setExpiryMonths] = useState(3);
  const [supplierId, setSupplierId] = useState("");
  const [brandId, setBrandId] = useState("");
  const [supplierValue, setSupplierValue] = useState(null);
  const [brandValue, setBrandValue] = useState(null);
  const [nearExpiryRows, setNearExpiryRows] = useState([]);
  const [loadingExpiry, setLoadingExpiry] = useState(false);
  const [loading, setLoading] = useState(false);
  
  const [cards, setCards] = useState({
    sales: 0,
    purchases: 0,
    saleReturns: 0,
    purchaseReturns: 0,
  });
  
  const [series, setSeries] = useState({
    sales: [],
    purchases: [],
    saleReturns: [],
    purchaseReturns: [],
  });

  const [invoiceCounts, setInvoiceCounts] = useState({ total: 0, sale_invoices: 0, purchase_invoices: 0 });
  const [kpiMetrics, setKpiMetrics] = useState({
    active_products: 0,
    suppliers: 0,
    brands: 0,
    categories: 0,
    near_expiry: 0,
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

  // Get section config for styling - uses dynamic theme colors
  const getSectionConfig = (key) => {
    const colorMap = {
      dashboard: { base: themeColors.secondary, light: themeColors.secondaryLight, hover: themeColors.secondaryHover },
      sales: { base: themeColors.primary, light: themeColors.primaryLight, hover: themeColors.primaryHover },
      purchases: { base: themeColors.primary, light: themeColors.primaryLight, hover: themeColors.primaryHover },
      returns: { base: themeColors.tertiary, light: themeColors.tertiaryLight, hover: themeColors.tertiaryHover },
      kpi: { base: themeColors.primary, light: themeColors.primaryLight, hover: themeColors.primaryHover },
    };
    const colors = colorMap[key] || colorMap.sales;
    return colors;
  };

  // Dynamic tint buttons based on theme
  const tintPrimary = useMemo(() => 
    `bg-gradient-to-br from-[${themeColors.primary}] to-[${themeColors.primaryHover}] text-white shadow-lg shadow-[${themeColors.primary}]/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-[${themeColors.primary}]/30 hover:scale-[1.02] hover:from-[${themeColors.primaryHover}] hover:to-[${themeColors.primary}] active:scale-[0.98] transition-all duration-200`
  , [themeColors]);

  const tintSecondary = useMemo(() => 
    `bg-gradient-to-br from-[${themeColors.secondary}] to-[${themeColors.secondaryHover}] text-white shadow-lg shadow-[${themeColors.secondary}]/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-[${themeColors.secondary}]/30 hover:scale-[1.02] hover:from-[${themeColors.secondaryHover}] hover:to-[${themeColors.secondary}] active:scale-[0.98] transition-all duration-200`
  , [themeColors]);

  const tintTertiary = useMemo(() => 
    `bg-gradient-to-br from-[${themeColors.tertiary}] to-[${themeColors.tertiaryHover}] text-white shadow-lg shadow-[${themeColors.tertiary}]/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-[${themeColors.tertiary}]/30 hover:scale-[1.02] hover:from-[${themeColors.tertiaryHover}] hover:to-[${themeColors.tertiary}] active:scale-[0.98] transition-all duration-200`
  , [themeColors]);

  const netSales = useMemo(() => (cards.sales || 0) - (cards.saleReturns || 0), [cards]);

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
    fetchNearExpiry();
  }, [expiryMonths, supplierId, brandId]);

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

  async function fetchNearExpiry() {
    setLoadingExpiry(true);
    try {
      const params = {
        months: expiryMonths,
        supplier_id: supplierId || undefined,
        brand_id: brandId || undefined,
      };
      const { data } = await axios.get("/api/dashboard/near-expiry", { params });
      setNearExpiryRows(Array.isArray(data?.rows) ? data.rows : []);
    } catch (err) {
      console.error(err);
      toast.error("Failed to load near expiry data.");
    } finally {
      setLoadingExpiry(false);
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
        saleReturns: Number(data?.totals?.sale_returns || 0),
        purchaseReturns: Number(data?.totals?.purchase_returns || 0),
      });
      setSeries({
        sales: mergeSeries(scaf, data?.series?.sales || []),
        purchases: mergeSeries(scaf, data?.series?.purchases || []),
        saleReturns: mergeSeries(scaf, data?.series?.sale_returns || []),
        purchaseReturns: mergeSeries(scaf, data?.series?.purchase_returns || []),
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
              className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${tintSecondary}`}
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
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
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

        {/* Sale Returns Card */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden group hover:shadow-lg hover:scale-[1.02] transition-all duration-300">
          <div 
            className="h-1.5"
            style={{ background: `linear-gradient(to right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})` }}
          />
          <div className="p-4">
            <div className="flex items-center justify-between mb-2">
              <div 
                className="p-1.5 rounded-lg"
                style={{ backgroundColor: themeColors.tertiaryLight }}
              >
                <ArrowUturnLeftIcon className="w-5 h-5" style={{ color: themeColors.tertiary }} />
              </div>
              <span className="text-xs font-medium" style={{ color: themeColors.tertiary }}>Sale Returns</span>
            </div>
            <div className="text-2xl font-bold text-gray-900 dark:text-white">Rs {fmtCurrency(cards.saleReturns)}</div>
            <div className="h-6 mt-2">
              <svg viewBox="0 0 100 20" className="w-full h-full" preserveAspectRatio="none">
                <path d="M0 5 Q 25 8, 50 12 T 100 15" fill="none" stroke={themeColors.tertiary} strokeWidth="2" className="opacity-60" />
              </svg>
            </div>
          </div>
        </div>

        {/* Purchase Returns Card */}
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
                <ArrowUturnDownIcon className="w-5 h-5" style={{ color: themeColors.primary }} />
              </div>
              <span className="text-xs font-medium" style={{ color: themeColors.primary }}>Purchase Returns</span>
            </div>
            <div className="text-2xl font-bold text-gray-900 dark:text-white">Rs {fmtCurrency(cards.purchaseReturns)}</div>
            <div className="h-6 mt-2">
              <svg viewBox="0 0 100 20" className="w-full h-full" preserveAspectRatio="none">
                <path d="M0 12 Q 25 10, 50 8 T 100 5" fill="none" stroke={themeColors.primary} strokeWidth="2" className="opacity-60" />
              </svg>
            </div>
          </div>
        </div>
      </div>

      {/* KPI Metrics Row */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
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

        {/* Near Expiry */}
        <div 
          className={`rounded-xl p-3 text-white shadow-lg hover:shadow-xl transition-shadow duration-300 ${(kpiMetrics.near_expiry || nearExpiryRows.length) > 0 ? '' : 'opacity-70'}`}
          style={{ background: `linear-gradient(135deg, ${(kpiMetrics.near_expiry || nearExpiryRows.length) > 0 ? themeColors.secondary : '#64748b'} 0%, ${(kpiMetrics.near_expiry || nearExpiryRows.length) > 0 ? themeColors.secondaryHover : '#475569'} 100%)` }}
        >
          <div className="flex items-center gap-2 mb-2">
            <div className="p-1.5 bg-white/20 rounded-lg backdrop-blur-sm">
              <ClockIcon className="w-4 h-4" />
            </div>
            <span className="text-xs font-medium text-white/80">Near Expiry</span>
          </div>
          <div className="text-xl font-bold">{kpiMetrics.near_expiry || nearExpiryRows.length}</div>
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
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
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
            <div className="h-48">
              <ResponsiveContainer width="100%" height="100%">
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
            <div className="h-48">
              <ResponsiveContainer width="100%" height="100%">
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

        {/* Area Chart - Returns Trend */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
          <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-100 dark:border-slate-700">
            <div 
              className="p-1.5 rounded-lg"
              style={{ backgroundColor: themeColors.tertiaryLight }}
            >
              <ArrowUturnLeftIcon className="w-4 h-4" style={{ color: themeColors.tertiary }} />
            </div>
            <div>
              <h3 className="text-sm font-semibold text-gray-800 dark:text-gray-100">Returns Trend</h3>
              <p className="text-xs text-gray-500 dark:text-gray-400">Area chart - Over time</p>
            </div>
          </div>
          <div className="p-4">
            <div className="h-48">
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={mergeTwo(series.saleReturns, series.purchaseReturns)} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
                  <defs>
                    <linearGradient id="areaGrad2" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor={themeColors.tertiary} stopOpacity={0.8} />
                      <stop offset="100%" stopColor={themeColors.tertiary} stopOpacity={0.1} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" opacity={0.15} stroke={themeColors.tertiary} />
                  <XAxis dataKey="date" tick={{ fontSize: 10, fill: '#94a3b8' }} axisLine={{ stroke: '#475569' }} tickLine={false} />
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
                    formatter={(val, name) => [`Rs ${fmtCurrency(val)}`, name === 'a' ? 'Sale Returns' : 'Purchase Returns']}
                    labelFormatter={(label) => `Date: ${label}`}
                  />
                  <Legend iconType="circle" wrapperStyle={{ paddingTop: '10px' }} formatter={(value) => <span className="text-xs text-gray-600 dark:text-gray-300">{value}</span>} />
                  <Area type="monotone" dataKey="a" name="Sale Returns" stroke={themeColors.tertiary} strokeWidth={2} fill="url(#areaGrad2)" />
                  <Area type="monotone" dataKey="b" name="Purchase Returns" stroke={themeColors.secondary} strokeWidth={2} fill="transparent" />
                </AreaChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>
      </div>

      {/* Near Expiry Table */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div 
              className="p-1.5 rounded-lg"
              style={{ backgroundColor: themeColors.tertiaryLight }}
            >
              <ClockIcon className="w-4 h-4" style={{ color: themeColors.tertiary }} />
            </div>
            <div>
              <h3 className="text-sm font-semibold text-gray-800 dark:text-gray-100">Near Expiry</h3>
              <p className="text-xs text-gray-500 dark:text-gray-400">Products expiring soon</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <div className="flex items-center gap-1">
              {[
                { m: 1, label: "1 mo" },
                { m: 3, label: "3 mo" },
                { m: 6, label: "6 mo" },
                { m: 12, label: "1 yr" },
                { m: 18, label: "1.5 yr" },
              ].map((opt) => (
                <button
                  key={opt.m}
                  onClick={() => setExpiryMonths(opt.m)}
                  className={`px-2.5 py-1 text-xs font-medium rounded-md transition-all duration-200 ${
                    expiryMonths === opt.m
                      ? tintPrimary
                      : "bg-gray-100 dark:bg-slate-700 text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-slate-600"
                  }`}
                >
                  {opt.label}
                </button>
              ))}
            </div>
            <div className="flex items-center gap-2 shrink-0" style={{ minWidth: 180 }}>
              <span className="text-gray-700 dark:text-gray-300 text-xs">Supplier</span>
              <div className="w-36 relative z-50">
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
                  styles={getSmallSelectStyles(isDark)}
                  menuPortalTarget={document.body}
                  filterOption={createFilter({ matchFrom: "start", trim: true })}
                />
              </div>
            </div>
            <div className="flex items-center gap-2 shrink-0" style={{ minWidth: 160 }}>
              <span className="text-gray-700 dark:text-gray-300 text-xs">Brand</span>
              <div className="w-36 relative z-50">
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
                  styles={getSmallSelectStyles(isDark)}
                  menuPortalTarget={document.body}
                  filterOption={createFilter({ matchFrom: "start", trim: true })}
                />
              </div>
            </div>
            <button
              onClick={() => {
                setSupplierValue(null);
                setBrandValue(null);
                setSupplierId("");
                setBrandId("");
              }}
              className="px-3 py-1 text-xs font-medium rounded-md bg-gray-100 dark:bg-slate-700 text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-slate-600 transition-all duration-200"
            >
              Clear
            </button>
            <button
              onClick={fetchNearExpiry}
              disabled={loadingExpiry}
              className={`inline-flex items-center gap-1 rounded-md px-3 py-1 text-xs font-semibold ${loadingExpiry ? "bg-gray-300 dark:bg-slate-600 cursor-not-allowed" : tintSecondary}`}
            >
              <ArrowPathIcon className={`w-3.5 h-3.5 ${loadingExpiry ? "animate-spin" : ""}`} />
              {loadingExpiry ? "Loading…" : "Refresh"}
            </button>
          </div>
        </div>
        <div className="overflow-auto max-h-80">
          <table className="min-w-full text-sm">
            <thead className="sticky top-0 bg-gray-50 dark:bg-slate-800 z-10 border-b border-gray-200 dark:border-slate-700">
              <tr className="text-left text-gray-700 dark:text-gray-300">
                <th className="px-4 py-2.5 font-medium text-xs uppercase tracking-wider">Product</th>
                <th className="px-4 py-2.5 font-medium text-xs uppercase tracking-wider">Supplier</th>
                <th className="px-4 py-2.5 font-medium text-xs uppercase tracking-wider">Brand</th>
                <th className="px-4 py-2.5 font-medium text-xs uppercase tracking-wider">Batch #</th>
                <th className="px-4 py-2.5 font-medium text-xs uppercase tracking-wider">Expiry</th>
                <th className="px-4 py-2.5 font-medium text-xs uppercase tracking-wider text-right">Qty</th>
              </tr>
            </thead>
            <tbody>
              {nearExpiryRows.length === 0 ? (
                <tr>
                  <td className="px-4 py-8 text-center text-gray-500 dark:text-gray-400" colSpan={6}>
                    {loadingExpiry ? (
                      <span className="inline-flex items-center gap-2">
                        <ArrowPathIcon className="w-4 h-4 animate-spin" />
                        Loading…
                      </span>
                    ) : (
                      "No near-expiry items found."
                    )}
                  </td>
                </tr>
              ) : (
                nearExpiryRows.map((r) => (
                  <tr key={`b-${r.batch_id}`} className="odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50 transition-colors border-b border-gray-100 dark:border-slate-700/50">
                    <td className="px-4 py-2.5 text-gray-900 dark:text-gray-100">
                      <div className="max-w-[280px] truncate font-medium" title={r.product_name}>{r.product_name}</div>
                    </td>
                    <td className="px-4 py-2.5 text-gray-900 dark:text-gray-100">
                      <div className="max-w-[200px] truncate">{r.supplier_name || "—"}</div>
                    </td>
                    <td className="px-4 py-2.5 text-gray-900 dark:text-gray-100">
                      <div className="max-w-[180px] truncate">{r.brand_name || "—"}</div>
                    </td>
                    <td className="px-4 py-2.5 text-gray-900 dark:text-gray-100 font-mono text-xs">{r.batch_number}</td>
                    <td className="px-4 py-2.5 text-gray-900 dark:text-gray-100">{(r.expiry_date || "").slice(0, 10)}</td>
                    <td className="px-4 py-2.5 text-right text-gray-900 dark:text-gray-100 font-medium">{Number(r.quantity ?? 0).toLocaleString()}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

