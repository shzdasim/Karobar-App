import { useEffect, useMemo, useState, useCallback } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import Select, { createFilter } from "react-select";
import AsyncSelect from "react-select/async";
import { Link, useNavigate } from "react-router-dom";
import { useTheme } from "@/context/ThemeContext.jsx";
import ModernDatePicker from "@/components/ModernDatePicker.jsx";
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
  Cell,
  Legend,
} from "recharts";
import {
  CurrencyDollarIcon,
  ShoppingCartIcon,
  BanknotesIcon,
  ArrowUturnLeftIcon,
  UsersIcon,
  TruckIcon,
  CubeIcon,
  TagIcon,
  CheckBadgeIcon,
  ClipboardDocumentListIcon,
  ClockIcon,
  ExclamationTriangleIcon,
  ScaleIcon,
  ChatBubbleLeftRightIcon,
  ShieldCheckIcon,
  ArrowPathIcon,
  ArrowTrendingUpIcon,
  ArrowTrendingDownIcon,
  ArrowTopRightOnSquareIcon,
  ChartPieIcon,
  SparklesIcon,
  CalendarDaysIcon,
  FireIcon,
  TrophyIcon,
  PlusIcon,
  BuildingStorefrontIcon,
  ChevronRightIcon,
} from "@heroicons/react/24/outline";

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

const firstDayOfYearStr = () => {
  const d = new Date();
  return localISODate(new Date(d.getFullYear(), 0, 1));
};

const fmtCurrency = (n) => {
  const v = Number(n || 0);
  return v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
};

const fmtCompact = (n) => {
  const v = Number(n || 0);
  const abs = Math.abs(v);
  if (abs >= 1e9) return `${(v / 1e9).toFixed(1).replace(/\.0$/, "")}B`;
  if (abs >= 1e6) return `${(v / 1e6).toFixed(1).replace(/\.0$/, "")}M`;
  if (abs >= 1e3) return `${(v / 1e3).toFixed(1).replace(/\.0$/, "")}k`;
  return `${v.toFixed(0)}`;
};

const dateKey = (d) =>
  typeof d === "string" ? d.substring(0, 10) : new Date(d).toISOString().substring(0, 10);

const inclusiveDaysUTC = (fromStr, toStr) => {
  const a = new Date(fromStr), b = new Date(toStr);
  const ua = Date.UTC(a.getFullYear(), a.getMonth(), a.getDate());
  const ub = Date.UTC(b.getFullYear(), b.getMonth(), b.getDate());
  return Math.floor((ub - ua) / 86400000) + 1;
};

const addDays = (dateStr, delta) => {
  const d = new Date(`${dateStr}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + delta);
  return d.toISOString().substring(0, 10);
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

function mergeTwo(a = [], b = [], nameA = 'a', nameB = 'b') {
  const map = new Map();
  for (const r of a) map.set(r.date, { date: r.date, [nameA]: Number(r.value || 0), [nameB]: 0 });
  for (const r of b) {
    const row = map.get(r.date) || { date: r.date, [nameA]: 0, [nameB]: 0 };
    row[nameB] += Number(r.value || 0);
    map.set(r.date, row);
  }
  return Array.from(map.values());
}

const daysLeftOf = (dateStr) => {
  if (!dateStr) return null;
  const exp = new Date(`${dateStr.slice(0, 10)}T00:00:00`);
  const now = new Date();
  now.setHours(0, 0, 0, 0);
  return Math.round((exp - now) / 86400000);
};

const pctChange = (cur, prev) => {
  const p = Number(prev || 0);
  const c = Number(cur || 0);
  if (p === 0) return c > 0 ? 100 : 0;
  return Math.round(((c - p) / Math.abs(p)) * 100 * 10) / 10;
};

const fmtDateLabel = (iso) => {
  if (!iso) return "";
  const d = new Date(`${String(iso).slice(0, 10)}T00:00:00`);
  return Number.isNaN(d.getTime())
    ? ""
    : d.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
};

/* ===================== Dark-mode aware tooltip + selects ===================== */
const tooltipStyle = (isDark) => ({
  backgroundColor: isDark ? "rgba(30, 41, 59, 0.95)" : "rgba(255, 255, 255, 0.97)",
  backdropFilter: "blur(10px)",
  border: isDark ? "1px solid rgba(71, 85, 105, 0.5)" : "1px solid rgba(226, 232, 240, 1)",
  borderRadius: "12px",
  boxShadow: "0 10px 40px rgba(0, 0, 0, 0.12)",
  padding: "10px 14px",
  fontSize: "12px",
});

const getSmallSelectStyles = (isDark = false) => ({
  control: (base) => ({
    ...base,
    minHeight: 40,
    height: 40,
    borderRadius: "var(--btn-radius, 10px)",
    borderColor: isDark ? "rgba(71,85,105,0.8)" : "rgba(203,213,225,0.9)",
    backgroundColor: isDark ? "rgba(30,41,59,0.7)" : "rgba(255,255,255,0.9)",
    boxShadow: "none",
  }),
  valueContainer: (base) => ({ ...base, height: 40, padding: "0 8px" }),
  indicatorsContainer: (base) => ({ ...base, height: 40 }),
  input: (base) => ({ ...base, margin: 0, padding: 0, color: isDark ? "#f1f5f9" : "#0f172a" }),
  menuPortal: (base) => ({ ...base, zIndex: 9999 }),
  menu: (base) => ({
    ...base,
    borderRadius: 12,
    overflow: "hidden",
    backgroundColor: isDark ? "rgba(30,41,59,0.97)" : "rgba(255,255,255,0.98)",
    backdropFilter: "blur(10px)",
    boxShadow: isDark ? "0 10px 30px -10px rgba(0,0,0,0.5)" : "0 10px 30px -10px rgba(30,64,175,0.18)",
    border: isDark ? "1px solid rgba(71,85,105,0.5)" : "1px solid rgba(226,232,240,1)",
  }),
  option: (base, state) => ({
    ...base,
    backgroundColor: isDark
      ? state.isFocused ? "rgba(71,85,105,1)" : "transparent"
      : state.isFocused ? "rgba(241,245,249,1)" : "transparent",
    color: isDark ? "#f1f5f9" : "#0f172a",
    cursor: "pointer",
  }),
  singleValue: (base) => ({ ...base, color: isDark ? "#f1f5f9" : "#0f172a" }),
  placeholder: (base) => ({ ...base, color: isDark ? "#64748b" : "#94a3b8" }),
});

const getPeriodSelectStyles = (isDark = false) => ({
  control: (base, state) => ({
    ...base,
    minHeight: 36,
    height: 36,
    fontSize: "12px",
    fontWeight: 500,
    borderRadius: "var(--btn-radius, 8px)",
    borderColor: state.isFocused
      ? "var(--workspace-accent, #3b82f6)"
      : isDark
      ? "rgba(71,85,105,0.7)"
      : "rgba(203,213,225,0.9)",
    backgroundColor: isDark ? "rgba(30,41,59,0.85)" : "var(--workspace-reading-fill, #f8fafc)",
    boxShadow: state.isFocused
      ? "0 0 0 2px color-mix(in srgb, var(--workspace-accent) 25%, transparent)"
      : "none",
    cursor: "pointer",
    transition: "border-color 0.15s ease, box-shadow 0.15s ease",
    "&:hover": {
      borderColor: "var(--workspace-accent, #3b82f6)",
    },
  }),
  valueContainer: (base) => ({
    ...base,
    height: 36,
    padding: "0 10px",
    display: "flex",
    alignItems: "center",
  }),
  indicatorsContainer: (base) => ({
    ...base,
    height: 36,
  }),
  indicatorSeparator: () => ({ display: "none" }),
  dropdownIndicator: (base, state) => ({
    ...base,
    padding: "0 8px",
    color: state.isFocused ? "var(--workspace-accent, #3b82f6)" : isDark ? "#94a3b8" : "#64748b",
  }),
  input: (base) => ({
    ...base,
    margin: 0,
    padding: 0,
    color: isDark ? "#f1f5f9" : "#0f172a",
  }),
  menuPortal: (base) => ({ ...base, zIndex: 9999 }),
  menu: (base) => ({
    ...base,
    borderRadius: "var(--btn-radius, 10px)",
    overflow: "hidden",
    backgroundColor: isDark ? "#1e293b" : "#ffffff",
    boxShadow: isDark
      ? "0 10px 25px -5px rgba(0,0,0,0.5), 0 8px 10px -6px rgba(0,0,0,0.5)"
      : "0 10px 25px -5px rgba(0,0,0,0.1), 0 8px 10px -6px rgba(0,0,0,0.1)",
    border: isDark ? "1px solid rgba(71,85,105,0.5)" : "1px solid rgba(226,232,240,1)",
    minWidth: 175,
  }),
  option: (base, state) => ({
    ...base,
    fontSize: "12px",
    fontWeight: state.isSelected ? 600 : 500,
    padding: "7px 12px",
    backgroundColor: state.isSelected
      ? "var(--workspace-accent, #3b82f6)"
      : state.isFocused
      ? isDark
        ? "rgba(51, 65, 85, 0.8)"
        : "rgba(241, 245, 249, 1)"
      : "transparent",
    color: state.isSelected ? "#ffffff" : isDark ? "#f1f5f9" : "#0f172a",
    cursor: "pointer",
  }),
  singleValue: (base) => ({
    ...base,
    color: isDark ? "#f1f5f9" : "#0f172a",
    fontWeight: 600,
  }),
  placeholder: (base) => ({
    ...base,
    color: isDark ? "#64748b" : "#94a3b8",
  }),
});

/* ===================== Reusable Modular Components ===================== */
function Sparkline({ data, color, suffix }) {
  const gradId = `sparkGrad_${suffix}`;
  return (
    <div className="w-full h-9">
      {Array.isArray(data) && data.length > 0 ? (
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
            <defs>
              <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={color} stopOpacity={0.35} />
                <stop offset="100%" stopColor={color} stopOpacity={0} />
              </linearGradient>
            </defs>
            <Area type="monotone" dataKey="value" stroke={color} strokeWidth={2} fill={`url(#${gradId})`} isAnimationActive={false} />
          </AreaChart>
        </ResponsiveContainer>
      ) : (
        <div className="w-full h-full flex items-end pb-1.5">
          <div className="w-full h-px bg-[var(--workspace-border)] opacity-60" />
        </div>
      )}
    </div>
  );
}

function SectionCard({ children, className = "" }) {
  return (
    <div className={`g-card dashboard-panel overflow-hidden ${className}`}>
      {children}
    </div>
  );
}

function CardHeader({ icon: Icon, iconStyle, title, subtitle, right }) {
  return (
    <div className="dashboard-panel-heading flex flex-wrap items-center justify-between gap-3 px-4 py-3.5 sm:px-5">
      <div className="flex items-center gap-2.5 min-w-0">
        <div className="p-2 rounded-xl shrink-0" style={{ backgroundColor: iconStyle?.bg || "color-mix(in srgb, var(--workspace-accent) 12%, transparent)" }}>
          <Icon className="w-4 h-4" style={{ color: iconStyle?.color || "var(--workspace-accent)" }} />
        </div>
        <div className="min-w-0">
          <h3 className="text-sm sm:text-base font-semibold text-[var(--workspace-ink)] truncate">{title}</h3>
          {subtitle && <p className="text-xs text-[var(--workspace-muted)] truncate">{subtitle}</p>}
        </div>
      </div>
      {right && <div className="flex items-center gap-2 shrink-0">{right}</div>}
    </div>
  );
}

function TrendPill({ value }) {
  const v = Number(value || 0);
  const up = v >= 0;
  return (
    <span
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold tracking-tight ${
        up
          ? "bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-400"
          : "bg-rose-50 text-rose-600 dark:bg-rose-500/10 dark:text-rose-400"
      }`}
    >
      {up ? <ArrowTrendingUpIcon className="w-3 h-3" /> : <ArrowTrendingDownIcon className="w-3 h-3" />}
      {Math.abs(v)}%
    </span>
  );
}

function RankedRow({ rank, title, meta, amount, pct, color }) {
  return (
    <li className="flex items-center gap-3 px-4 py-3 sm:px-5 hover:bg-[color-mix(in_srgb,var(--workspace-accent)_4%,transparent)] transition-colors">
      <span
        className="w-6 h-6 rounded-lg text-xs font-bold flex items-center justify-center shrink-0"
        style={
          rank <= 3
            ? { background: color, color: "#fff", boxShadow: `0 2px 8px ${color}55` }
            : { background: "var(--workspace-reading-fill)", color: "var(--workspace-muted)" }
        }
      >
        {rank}
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center justify-between gap-3">
          <p className="text-sm font-medium text-[var(--workspace-ink)] truncate">{title}</p>
          {meta && <p className="text-xs text-[var(--workspace-muted)] shrink-0 tabular-nums">{meta}</p>}
        </div>
        <div className="mt-1.5 flex items-center gap-2">
          <div className="h-1.5 flex-1 rounded-full bg-[var(--workspace-border)] overflow-hidden">
            <div
              className="h-full rounded-full transition-all duration-500"
              style={{ width: `${Math.max(2, Math.min(100, pct))}%`, background: color }}
            />
          </div>
          <span className="text-xs font-semibold text-[var(--workspace-ink)] tabular-nums shrink-0">{amount}</span>
        </div>
      </div>
    </li>
  );
}

function ExpiryPill({ days }) {
  let styles = "";
  const d = Number(days);
  if (d <= 30) {
    styles = "bg-rose-100 text-rose-700 dark:bg-rose-950/60 dark:text-rose-300 border-rose-300 dark:border-rose-800";
  } else if (d <= 90) {
    styles = "bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300 border-amber-300 dark:border-amber-800";
  } else {
    styles = "bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300 border-emerald-300 dark:border-emerald-800";
  }
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-semibold border whitespace-nowrap ${styles}`}>
      {d} days left
    </span>
  );
}

/* ===================== Main Modern Dashboard ===================== */
export default function Dashboard() {
  const { isDark, theme } = useTheme();
  const navigate = useNavigate();

  // Navigation tab: 'overview' | 'operations' | 'financials'
  const [activeTab, setActiveTab] = useState("overview");

  // Chart view metric: 'revenue' | 'margin' | 'returns' | 'activity'
  const [chartMetric, setChartMetric] = useState("revenue");

  // Top movers view: 'products' | 'brands'
  const [topMoversView, setTopMoversView] = useState("products");

  // Dates
  const [from, setFrom] = useState(todayStr());
  const [to, setTo] = useState(todayStr());

  // Near-expiry tab filters
  const [expiryMonths, setExpiryMonths] = useState(3);
  const [supplierId, setSupplierId] = useState("");
  const [brandId, setBrandId] = useState("");
  const [supplierValue, setSupplierValue] = useState(null);
  const [brandValue, setBrandValue] = useState(null);
  const [nearExpiryRows, setNearExpiryRows] = useState([]);
  const [loadingExpiry, setLoadingExpiry] = useState(false);

  // General loading & state
  const [loading, setLoading] = useState(false);
  const [booted, setBooted] = useState(false);
  const [lastUpdated, setLastUpdated] = useState(null);

  const [cards, setCards] = useState({
    sales: 0,
    purchases: 0,
    saleReturns: 0,
    purchaseReturns: 0,
    salesDebit: 0,
    salesCredit: 0,
    purchasesDebit: 0,
    purchasesCredit: 0,
  });

  const [prevCards, setPrevCards] = useState({
    sales: 0,
    purchases: 0,
    saleReturns: 0,
    purchaseReturns: 0,
    salesDebit: 0,
    salesCredit: 0,
    purchasesDebit: 0,
    purchasesCredit: 0,
  });

  const [series, setSeries] = useState({
    sales: [],
    purchases: [],
    saleReturns: [],
    purchaseReturns: [],
  });

  const [topProducts, setTopProducts] = useState([]);
  const [brandSales, setBrandSales] = useState([]);
  const [invoiceCounts, setInvoiceCounts] = useState({ total: 0, sale_invoices: 0, purchase_invoices: 0 });
  const [kpiMetrics, setKpiMetrics] = useState({
    active_products: 0,
    customers: 0,
    suppliers: 0,
    brands: 0,
    categories: 0,
    users: 0,
    near_expiry: 0,
    low_stock: 0,
    stock_adjustments: 0,
    pending_demands: 0,
    stock_units: 0,
    stock_value: 0,
    expected_orders_amount: null,
  });

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

  const daysInPeriod = useMemo(() => {
    try {
      return Math.max(1, inclusiveDaysUTC(from || todayStr(), to || todayStr()));
    } catch {
      return 1;
    }
  }, [from, to]);

  const dateRangeLabel = useMemo(() => {
    const f = fmtDateLabel(from);
    const t = fmtDateLabel(to);
    if (!f && !t) {
      return new Date().toLocaleDateString(undefined, {
        weekday: "short",
        year: "numeric",
        month: "short",
        day: "numeric",
      });
    }
    if (f === t) return f;
    return `${f} — ${t}`;
  }, [from, to]);

  const netSales = useMemo(() => (cards.sales || 0) - (cards.saleReturns || 0), [cards]);
  const prevNetSales = useMemo(() => (prevCards.sales || 0) - (prevCards.saleReturns || 0), [prevCards]);
  const netPurchases = useMemo(() => (cards.purchases || 0) - (cards.purchaseReturns || 0), [cards]);
  const grossMargin = useMemo(() => netSales - netPurchases, [netSales, netPurchases]);
  const grossMarginPct = useMemo(() => {
    return netSales > 0 ? ((grossMargin / netSales) * 100).toFixed(1) : "0.0";
  }, [grossMargin, netSales]);

  const alertCount = useMemo(() => {
    return (
      (Number(kpiMetrics.near_expiry) || 0) +
      (Number(kpiMetrics.low_stock) || 0) +
      (Number(kpiMetrics.pending_demands) || 0)
    );
  }, [kpiMetrics]);

  // Unified analytics master series
  const masterChartData = useMemo(() => {
    const sMap = new Map((series.sales || []).map((p) => [p.date, Number(p.value || 0)]));
    const pMap = new Map((series.purchases || []).map((p) => [p.date, Number(p.value || 0)]));
    const srMap = new Map((series.saleReturns || []).map((p) => [p.date, Number(p.value || 0)]));
    const prMap = new Map((series.purchaseReturns || []).map((p) => [p.date, Number(p.value || 0)]));

    const allDates = Array.from(new Set([...sMap.keys(), ...pMap.keys(), ...srMap.keys(), ...prMap.keys()]));
    allDates.sort();

    return allDates.map((d) => {
      const s = sMap.get(d) || 0;
      const p = pMap.get(d) || 0;
      const sr = srMap.get(d) || 0;
      const pr = prMap.get(d) || 0;
      const netS = s - sr;
      const margin = netS - (p - pr);

      return {
        date: d,
        sales: s,
        purchases: p,
        saleReturns: sr,
        purchaseReturns: pr,
        netSales: netS,
        margin: margin,
      };
    });
  }, [series]);

  const maxTopProduct = useMemo(() => {
    return Math.max(1, ...(topProducts || []).map((p) => Number(p.total_sold) || 0));
  }, [topProducts]);

  const maxBrandSales = useMemo(() => {
    return Math.max(1, ...(brandSales || []).map((b) => Number(b.revenue) || 0));
  }, [brandSales]);

  // Fetch all dashboard data
  const fetchAll = useCallback(async () => {
    const f = from || todayStr();
    const t = to || todayStr();
    setLoading(true);

    try {
      const params = { date_from: f, date_to: t };
      const days = Math.max(1, inclusiveDaysUTC(f, t));
      const prevTo = addDays(f, -1);
      const prevFrom = addDays(prevTo, -(days - 1));
      const prevParams = { date_from: prevFrom, date_to: prevTo, totals_only: 1 };

      const [summaryRes, prevRes, metricsRes, topRes, brandRes, invoiceRes] = await Promise.allSettled([
        axios.get("/api/dashboard/summary", { params }),
        axios.get("/api/dashboard/summary", { params: prevParams }),
        axios.get("/api/dashboard/kpi-metrics", { params }),
        axios.get("/api/dashboard/top-products", { params: { ...params, limit: 6 } }),
        axios.get("/api/dashboard/sales-by-brands", { params }),
        axios.get("/api/dashboard/invoice-counts"),
      ]);

      if (summaryRes.status === 'fulfilled') {
        const data = summaryRes.value.data;
        const scaf = scaffoldSeries(f, t);
        setCards({
          sales: Number(data?.totals?.sales || 0),
          purchases: Number(data?.totals?.purchases || 0),
          saleReturns: Number(data?.totals?.sale_returns || 0),
          purchaseReturns: Number(data?.totals?.purchase_returns || 0),
          salesDebit: Number(data?.totals?.sales_debit ?? (data?.totals?.sales || 0)),
          salesCredit: Number(data?.totals?.sales_credit || 0),
          purchasesDebit: Number(data?.totals?.purchases_debit ?? (data?.totals?.purchases || 0)),
          purchasesCredit: Number(data?.totals?.purchases_credit || 0),
        });
        setSeries({
          sales: mergeSeries(scaf, data?.series?.sales || []),
          purchases: mergeSeries(scaf, data?.series?.purchases || []),
          saleReturns: mergeSeries(scaf, data?.series?.sale_returns || []),
          purchaseReturns: mergeSeries(scaf, data?.series?.purchase_returns || []),
        });
      }

      if (prevRes.status === 'fulfilled') {
        const data = prevRes.value.data?.totals || {};
        setPrevCards({
          sales: Number(data.sales || 0),
          purchases: Number(data.purchases || 0),
          saleReturns: Number(data.sale_returns || 0),
          purchaseReturns: Number(data.purchase_returns || 0),
          salesDebit: Number(data.sales_debit ?? (data.sales || 0)),
          salesCredit: Number(data.sales_credit || 0),
          purchasesDebit: Number(data.purchases_debit ?? (data.purchases || 0)),
          purchasesCredit: Number(data.purchases_credit || 0),
        });
      }

      if (metricsRes.status === 'fulfilled') {
        setKpiMetrics((prev) => ({ ...prev, ...(metricsRes.value.data || {}) }));
      }
      if (topRes.status === 'fulfilled') {
        const rows = Array.isArray(topRes.value.data?.data) ? topRes.value.data.data : [];
        setTopProducts(rows.slice(0, 6));
      }
      if (brandRes.status === 'fulfilled') {
        const rows = Array.isArray(brandRes.value.data?.data) ? brandRes.value.data.data : [];
        setBrandSales(rows.slice(0, 5));
      }
      if (invoiceRes.status === 'fulfilled') {
        setInvoiceCounts(invoiceRes.value.data || { total: 0, sale_invoices: 0, purchase_invoices: 0 });
      }

      setLastUpdated(new Date());
      setBooted(true);
    } catch (e) {
      toast.error("Failed to load dashboard metrics");
    } finally {
      setLoading(false);
    }
  }, [from, to]);

  // Fetch near-expiry products (used in operations tab)
  const fetchNearExpiry = useCallback(async () => {
    setLoadingExpiry(true);
    try {
      const params = {
        months: expiryMonths,
        supplier_id: supplierId || undefined,
        brand_id: brandId || undefined,
      };
      const { data } = await axios.get("/api/dashboard/near-expiry", { params });
      const rows = Array.isArray(data?.rows)
        ? data.rows
        : Array.isArray(data?.data)
        ? data.data
        : Array.isArray(data)
        ? data
        : [];
      setNearExpiryRows(rows);
    } catch {
      toast.error("Failed to load near-expiry items");
    } finally {
      setLoadingExpiry(false);
    }
  }, [expiryMonths, supplierId, brandId]);

  useEffect(() => {
    fetchAll();
  }, [fetchAll]);

  useEffect(() => {
    if (activeTab === "operations") {
      fetchNearExpiry();
    }
  }, [activeTab, fetchNearExpiry]);

  // Keyboard shortcut Ctrl+R
  useEffect(() => {
    const handleKey = (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "r" && !e.shiftKey && !e.altKey) {
        e.preventDefault();
        fetchAll();
      }
    };
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [fetchAll]);

  // Quick period presets for React-Select
  const periodOptions = useMemo(() => {
    const today = todayStr();
    const yesterday = addDays(today, -1);
    const threeDaysAgo = addDays(today, -2);
    const sevenDaysAgo = addDays(today, -6);
    const thirtyDaysAgo = addDays(today, -29);
    const monthStart = firstDayOfMonthStr();
    const yearStart = firstDayOfYearStr();

    const now = new Date();
    const lastMonthEndObj = new Date(now.getFullYear(), now.getMonth(), 0);
    const lastMonthStartObj = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    const lastMonthStart = `${lastMonthStartObj.getFullYear()}-${String(lastMonthStartObj.getMonth() + 1).padStart(2, "0")}-01`;
    const lastMonthEnd = `${lastMonthEndObj.getFullYear()}-${String(lastMonthEndObj.getMonth() + 1).padStart(2, "0")}-${String(lastMonthEndObj.getDate()).padStart(2, "0")}`;

    return [
      { value: "today", label: "Today", from: today, to: today },
      { value: "yesterday", label: "Yesterday", from: yesterday, to: yesterday },
      { value: "3days", label: "Last 3 Days", from: threeDaysAgo, to: today },
      { value: "7days", label: "Last 7 Days", from: sevenDaysAgo, to: today },
      { value: "30days", label: "Last 30 Days", from: thirtyDaysAgo, to: today },
      { value: "this_month", label: "This Month", from: monthStart, to: today },
      { value: "last_month", label: "Last Month", from: lastMonthStart, to: lastMonthEnd },
      { value: "this_year", label: "This Year", from: yearStart, to: today },
      { value: "custom", label: "Custom Range…", isCustom: true },
    ];
  }, []);

  const currentPeriodOption = useMemo(() => {
    const match = periodOptions.find(
      (opt) => !opt.isCustom && opt.from === from && opt.to === to
    );
    if (match) return match;
    return periodOptions.find((opt) => opt.isCustom) || { value: "custom", label: "Custom Range…" };
  }, [from, to, periodOptions]);

  const handlePeriodChange = useCallback((opt) => {
    if (!opt) return;
    if (opt.isCustom) return;
    if (opt.from && opt.to) {
      setFrom(opt.from);
      setTo(opt.to);
    }
  }, []);

  const calendarFooterPresets = useMemo(
    () =>
      periodOptions
        .filter((p) => !p.isCustom)
        .slice(0, 4)
        .map((p) => ({
          label: p.label,
          onSelect: () => {
            setFrom(p.from);
            setTo(p.to);
          },
        })),
    [periodOptions]
  );

  // Near expiry horizon options (up to 3 years)
  const expiryHorizonOptions = useMemo(() => [
    { value: 1, label: "1 Month" },
    { value: 2, label: "2 Months" },
    { value: 3, label: "3 Months (Quarter)" },
    { value: 6, label: "6 Months (Half Year)" },
    { value: 9, label: "9 Months" },
    { value: 12, label: "1 Year" },
    { value: 18, label: "1.5 Years" },
    { value: 24, label: "2 Years" },
    { value: 30, label: "2.5 Years" },
    { value: 36, label: "3 Years" },
  ], []);

  // Async supplier and brand search
  const loadSuppliers = useCallback(async (q) => {
    try {
      const res = await axios.get("/api/suppliers/search", { params: { q, limit: 30 } });
      const rows = res.data?.data || res.data || [];
      return [
        { value: "", label: "All Suppliers" },
        ...rows.map((s) => ({ value: String(s.id), label: s.name })),
      ];
    } catch {
      return [{ value: "", label: "All Suppliers" }];
    }
  }, []);

  const loadBrands = useCallback(async (q) => {
    try {
      const res = await axios.get("/api/brands/search", { params: { q, limit: 30 } });
      const rows = res.data?.data || res.data || [];
      return [
        { value: "", label: "All Brands" },
        ...rows.map((b) => ({ value: String(b.id), label: b.name })),
      ];
    } catch {
      return [{ value: "", label: "All Brands" }];
    }
  }, []);

  return (
    <div className="dashboard-page space-y-4">
      {/* ===================== Modern Header Strip ===================== */}
      <div className="g-card p-4 sm:p-5 flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3.5 min-w-0">
          <div className="dashboard-overview-icon">
            <ChartPieIcon className="w-6 h-6" />
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2.5 flex-wrap">
              <h1 className="text-xl sm:text-2xl font-bold tracking-tight text-[var(--workspace-ink)]">
                Dashboard
              </h1>
              <span className="dashboard-live">
                <span className="relative flex h-2 w-2">
                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
                  <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
                </span>
                Live Store
              </span>
            </div>
            <p className="text-xs text-[var(--workspace-muted)] flex items-center gap-1.5 mt-0.5">
              <CalendarDaysIcon className="w-3.5 h-3.5 shrink-0 text-[var(--workspace-accent)]" />
              <span className="font-medium text-[var(--workspace-ink)]">{dateRangeLabel}</span>
              <span>•</span>
              <span>{daysInPeriod > 1 ? `${daysInPeriod}-day period` : "Today's activity"}</span>
              {lastUpdated && (
                <>
                  <span className="hidden sm:inline">•</span>
                  <span className="hidden sm:inline">
                    Updated {lastUpdated.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" })}
                  </span>
                </>
              )}
            </p>
          </div>
        </div>

        {/* Fast Action Buttons & Refresh */}
        <div className="flex items-center gap-2 flex-wrap">
          <Link
            to="/sale-invoices/create"
            className="theme-btn-primary inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold shadow-xs"
            title="Create a new sale invoice"
          >
            <PlusIcon className="w-4 h-4" />
            <span>New Sale</span>
          </Link>

          <Link
            to="/purchase-orders/create"
            className="theme-btn-secondary inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold shadow-2xs"
            title="Create purchase replenishment order"
          >
            <TruckIcon className="w-4 h-4 text-[var(--workspace-accent)]" />
            <span className="hidden sm:inline">New PO</span>
          </Link>

          <button
            type="button"
            onClick={fetchAll}
            disabled={loading}
            className="theme-btn-secondary inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold disabled:opacity-60 shadow-2xs cursor-pointer"
            title="Refresh dashboard metrics (Ctrl+R)"
          >
            <ArrowPathIcon className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`} />
            <span className="hidden sm:inline">{loading ? "Updating…" : "Refresh"}</span>
          </button>
        </div>
      </div>

      {/* ===================== Control Bar: Tabs & Period Selector ===================== */}
      <div className="g-card p-3 sm:p-4 flex flex-col lg:flex-row lg:items-center lg:justify-between gap-3">
        {/* Navigation Tabs */}
        <div className="flex items-center gap-1.5 bg-[var(--workspace-reading-fill)] p-1 rounded-xl border border-[var(--workspace-border)] overflow-x-auto no-scrollbar shrink-0">
          {[
            { id: "overview", label: "Overview", icon: ChartPieIcon },
            {
              id: "operations",
              label: "Operations & Stock",
              icon: CubeIcon,
              badge: (Number(kpiMetrics.near_expiry) || 0) + (Number(kpiMetrics.low_stock) || 0),
            },
            { id: "financials", label: "Financials & Credit", icon: BanknotesIcon },
          ].map((tab) => {
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => setActiveTab(tab.id)}
                className={`inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold rounded-lg transition-all whitespace-nowrap cursor-pointer ${
                  isActive
                    ? "theme-btn-primary shadow-xs"
                    : "text-[var(--workspace-muted)] hover:text-[var(--workspace-ink)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_8%,transparent)]"
                }`}
              >
                <tab.icon className="w-3.5 h-3.5 shrink-0" />
                <span>{tab.label}</span>
                {Boolean(tab.badge) && tab.badge > 0 && (
                  <span
                    className={`ml-1 px-1.5 py-0.2 rounded-full text-[10px] font-bold ${
                      isActive ? "bg-white/20 text-white" : "bg-rose-100 text-rose-700 dark:bg-rose-950/60 dark:text-rose-300"
                    }`}
                  >
                    {tab.badge}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {/* Date Filter Controls with React-Select (Saves Space & Theme-Consistent) */}
        <div className="flex flex-wrap items-center gap-2">
          {/* React Select Preset Dropdown */}
          <div className="w-44 shrink-0">
            <Select
              options={periodOptions}
              value={currentPeriodOption}
              onChange={handlePeriodChange}
              styles={getPeriodSelectStyles(isDark)}
              isSearchable={false}
              menuPortalTarget={typeof document !== "undefined" ? document.body : null}
              aria-label="Filter dashboard by time period"
            />
          </div>

          {/* Custom Date Range Pickers */}
          <div className="flex items-center gap-1.5 bg-[var(--workspace-reading-fill)] p-1 rounded-xl border border-[var(--workspace-border)]">
            <div className="w-28 sm:w-32">
              <ModernDatePicker
                value={from}
                onChange={(val) => val && setFrom(val)}
                placeholder="From"
                label="Start"
                isClearable={false}
                size="sm"
                selectsStart
                startDate={from}
                endDate={to}
                maxDate={to || todayStr()}
                presets={calendarFooterPresets}
              />
            </div>
            <span className="text-[var(--workspace-muted)] text-xs font-semibold px-0.5">—</span>
            <div className="w-28 sm:w-32">
              <ModernDatePicker
                value={to}
                onChange={(val) => val && setTo(val)}
                placeholder="To"
                label="End"
                isClearable={false}
                size="sm"
                selectsEnd
                startDate={from}
                endDate={to}
                minDate={from}
                maxDate={todayStr()}
                presets={calendarFooterPresets}
              />
            </div>
          </div>
        </div>
      </div>

      {/* ===================== TAB 1: EXECUTIVE OVERVIEW ===================== */}
      {activeTab === "overview" && (
        <div className="space-y-4">
          {/* 4 Hero KPI Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3.5">
            {/* Card 1: Net Sales */}
            <div
              role="button"
              tabIndex={0}
              onClick={() => navigate("/sale-invoices")}
              className="g-card p-4 sm:p-5 border-l-4 border-l-[var(--workspace-accent)] flex flex-col justify-between cursor-pointer hover:shadow-md transition-all group"
              title="Click to view all sale invoices"
            >
              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <span className="text-xs font-semibold uppercase tracking-wider text-[var(--workspace-accent)]">
                    Net Sales
                  </span>
                  <div className="flex items-center gap-1.5">
                    {booted && <TrendPill value={pctChange(netSales, prevNetSales)} />}
                    <div className="p-1.5 rounded-lg bg-[color-mix(in_srgb,var(--workspace-accent)_12%,transparent)] text-[var(--workspace-accent)]">
                      <CurrencyDollarIcon className="w-4 h-4" />
                    </div>
                  </div>
                </div>
                <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)]">
                  {booted ? `Rs ${fmtCurrency(netSales)}` : "—"}
                </div>
                <div className="text-xs text-[var(--workspace-muted)] mt-1.5 flex items-center justify-between gap-2 flex-wrap font-medium">
                  <span>Cash: <strong className="text-[var(--workspace-ink)] font-mono font-semibold">Rs {fmtCurrency(cards.salesDebit)}</strong></span>
                  <span>Credit: <strong className="text-[var(--workspace-ink)] font-mono font-semibold">Rs {fmtCurrency(cards.salesCredit)}</strong></span>
                </div>
              </div>
              <div className="mt-3 pt-2 border-t border-[var(--workspace-border)]">
                <Sparkline data={series.sales} color={themeColors.primary} suffix="sales" />
              </div>
            </div>

            {/* Card 2: Purchases */}
            <div
              role="button"
              tabIndex={0}
              onClick={() => navigate("/purchase-invoices")}
              className="g-card p-4 sm:p-5 border-l-4 border-l-purple-500 flex flex-col justify-between cursor-pointer hover:shadow-md transition-all group"
              title="Click to view all purchase invoices"
            >
              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <span className="text-xs font-semibold uppercase tracking-wider text-purple-600 dark:text-purple-400">
                    Purchases (Stock Intake)
                  </span>
                  <div className="flex items-center gap-1.5">
                    {booted && <TrendPill value={pctChange(cards.purchases, prevCards.purchases)} />}
                    <div className="p-1.5 rounded-lg bg-purple-50 dark:bg-purple-950/40 text-purple-600 dark:text-purple-400">
                      <ShoppingCartIcon className="w-4 h-4" />
                    </div>
                  </div>
                </div>
                <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)]">
                  {booted ? `Rs ${fmtCurrency(cards.purchases)}` : "—"}
                </div>
                <div className="text-xs text-[var(--workspace-muted)] mt-1.5 flex items-center justify-between gap-2 flex-wrap font-medium">
                  <span>Paid: <strong className="text-[var(--workspace-ink)] font-mono font-semibold">Rs {fmtCurrency(cards.purchasesDebit)}</strong></span>
                  <span>Dues: <strong className="text-[var(--workspace-ink)] font-mono font-semibold">Rs {fmtCurrency(cards.purchasesCredit)}</strong></span>
                </div>
              </div>
              <div className="mt-3 pt-2 border-t border-[var(--workspace-border)]">
                <Sparkline data={series.purchases} color={themeColors.secondary} suffix="purchases" />
              </div>
            </div>

            {/* Card 3: Gross Margin */}
            <div className="g-card p-4 sm:p-5 border-l-4 border-l-emerald-500 flex flex-col justify-between">
              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <span className="text-xs font-semibold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">
                    Gross Margin & Spread
                  </span>
                  <div className="flex items-center gap-1.5">
                    <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-emerald-50 text-emerald-700 dark:bg-emerald-950/50 dark:text-emerald-300">
                      {grossMarginPct}% Margin
                    </span>
                    <div className="p-1.5 rounded-lg bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400">
                      <ScaleIcon className="w-4 h-4" />
                    </div>
                  </div>
                </div>
                <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)]">
                  {booted ? `Rs ${fmtCurrency(grossMargin)}` : "—"}
                </div>
                <div className="text-xs text-[var(--workspace-muted)] mt-1.5 flex items-center justify-between gap-2 flex-wrap font-medium">
                  <span>Net: <strong className="text-[var(--workspace-ink)] font-mono font-semibold">Rs {fmtCurrency(netSales)}</strong></span>
                  <span>Returns: <strong className="text-[var(--workspace-ink)] font-mono font-semibold">Rs {fmtCurrency(cards.saleReturns)}</strong></span>
                </div>
              </div>
              <div className="mt-3 pt-2 border-t border-[var(--workspace-border)]">
                <Sparkline data={series.sales} color="#10b981" suffix="margin" />
              </div>
            </div>

            {/* Card 4: Needs Attention / Action Center */}
            <div className="g-card p-4 sm:p-5 border-l-4 border-l-amber-500 flex flex-col justify-between">
              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <span className="text-xs font-semibold uppercase tracking-wider text-amber-600 dark:text-amber-400">
                    Action Center
                  </span>
                  <div className="p-1.5 rounded-lg bg-amber-50 dark:bg-amber-950/40 text-amber-600 dark:text-amber-400">
                    <ExclamationTriangleIcon className="w-4 h-4" />
                  </div>
                </div>
                <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)]">
                  {alertCount} <span className="text-xs font-normal text-[var(--workspace-muted)]">alerts requiring attention</span>
                </div>
              </div>

              {/* Action Badges */}
              <div className="mt-3 pt-2 border-t border-[var(--workspace-border)] space-y-1.5">
                <div className="grid grid-cols-2 gap-2 text-xs">
                  <button
                    type="button"
                    onClick={() => setActiveTab("operations")}
                    className="p-1.5 rounded-lg bg-rose-50 dark:bg-rose-950/30 text-rose-700 dark:text-rose-300 font-medium text-left hover:opacity-85 transition-opacity"
                  >
                    <span className="font-bold">{kpiMetrics.near_expiry || 0}</span> Expiring Soon
                  </button>
                  <button
                    type="button"
                    onClick={() => navigate("/products")}
                    className="p-1.5 rounded-lg bg-amber-50 dark:bg-amber-950/30 text-amber-700 dark:text-amber-300 font-medium text-left hover:opacity-85 transition-opacity"
                  >
                    <span className="font-bold">{kpiMetrics.low_stock || 0}</span> Low Stock
                  </button>
                  <button
                    type="button"
                    onClick={() => navigate("/user-demands")}
                    className="p-1.5 rounded-lg bg-blue-50 dark:bg-blue-950/30 text-blue-700 dark:text-blue-300 font-medium text-left hover:opacity-85 transition-opacity"
                  >
                    <span className="font-bold">{kpiMetrics.pending_demands || 0}</span> Demands
                  </button>
                  <button
                    type="button"
                    onClick={() => navigate("/purchase-orders")}
                    className="p-1.5 rounded-lg bg-indigo-50 dark:bg-indigo-950/30 text-indigo-700 dark:text-indigo-300 font-medium text-left hover:opacity-85 transition-opacity"
                  >
                    <span className="font-bold">{kpiMetrics.expected_orders_amount ? `Rs ${fmtCompact(kpiMetrics.expected_orders_amount)}` : "POs"}</span> Inbound
                  </button>
                </div>
              </div>
            </div>
          </div>

          {/* Unified Interactive Trends Chart */}
          <SectionCard>
            <CardHeader
              icon={ChartPieIcon}
              title="Analytics & Trends"
              subtitle={`Visualizing business performance across ${daysInPeriod} ${daysInPeriod === 1 ? 'day' : 'days'}`}
              right={
                <div className="flex items-center gap-1.5 bg-[var(--workspace-reading-fill)] p-1 rounded-lg border border-[var(--workspace-border)]">
                  {[
                    { id: "revenue", label: "Sales vs Purchases" },
                    { id: "margin", label: "Gross Margin" },
                    { id: "returns", label: "Returns Trend" },
                  ].map((btn) => (
                    <button
                      key={btn.id}
                      type="button"
                      onClick={() => setChartMetric(btn.id)}
                      className={`px-3 py-1 text-xs font-semibold rounded-md transition-all cursor-pointer ${
                        chartMetric === btn.id
                          ? "theme-btn-primary shadow-2xs"
                          : "text-[var(--workspace-muted)] hover:text-[var(--workspace-ink)]"
                      }`}
                    >
                      {btn.label}
                    </button>
                  ))}
                </div>
              }
            />

            <div className="p-4 sm:p-5">
              <div className="h-64 sm:h-72 lg:h-80 w-full">
                <ResponsiveContainer width="100%" height="100%">
                  {chartMetric === "revenue" ? (
                    <AreaChart data={masterChartData} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                      <defs>
                        <linearGradient id="areaSales" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="0%" stopColor={themeColors.primary} stopOpacity={0.35} />
                          <stop offset="100%" stopColor={themeColors.primary} stopOpacity={0.02} />
                        </linearGradient>
                        <linearGradient id="areaPurchases" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="0%" stopColor={themeColors.secondary} stopOpacity={0.25} />
                          <stop offset="100%" stopColor={themeColors.secondary} stopOpacity={0.01} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" opacity={0.12} stroke="#94a3b8" vertical={false} />
                      <XAxis dataKey="date" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} minTickGap={28} tickFormatter={(val) => val.slice(5)} />
                      <YAxis tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} width={58} tickFormatter={(val) => `Rs ${fmtCompact(val)}`} />
                      <Tooltip
                        contentStyle={tooltipStyle(isDark)}
                        formatter={(val, name) => [`Rs ${fmtCurrency(val)}`, name === 'sales' ? 'Sales' : 'Purchases']}
                        labelFormatter={(label) => fmtDateLabel(label)}
                      />
                      <Legend iconType="circle" iconSize={7} wrapperStyle={{ paddingTop: '10px', fontSize: '12px' }} />
                      <Area type="monotone" dataKey="sales" name="Sales" stroke={themeColors.primary} strokeWidth={2.5} fill="url(#areaSales)" dot={false} activeDot={{ r: 4 }} />
                      <Area type="monotone" dataKey="purchases" name="Purchases" stroke={themeColors.secondary} strokeWidth={2.5} fill="url(#areaPurchases)" dot={false} activeDot={{ r: 4 }} />
                    </AreaChart>
                  ) : chartMetric === "margin" ? (
                    <AreaChart data={masterChartData} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                      <defs>
                        <linearGradient id="areaMargin" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="0%" stopColor="#10b981" stopOpacity={0.4} />
                          <stop offset="100%" stopColor="#10b981" stopOpacity={0.02} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" opacity={0.12} stroke="#94a3b8" vertical={false} />
                      <XAxis dataKey="date" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} minTickGap={28} tickFormatter={(val) => val.slice(5)} />
                      <YAxis tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} width={58} tickFormatter={(val) => `Rs ${fmtCompact(val)}`} />
                      <Tooltip
                        contentStyle={tooltipStyle(isDark)}
                        formatter={(val) => [`Rs ${fmtCurrency(val)}`, 'Net Daily Margin']}
                        labelFormatter={(label) => fmtDateLabel(label)}
                      />
                      <Area type="monotone" dataKey="margin" name="Daily Spread" stroke="#10b981" strokeWidth={2.5} fill="url(#areaMargin)" dot={false} activeDot={{ r: 4 }} />
                    </AreaChart>
                  ) : (
                    <AreaChart data={masterChartData} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                      <defs>
                        <linearGradient id="areaSaleReturns" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="0%" stopColor="#ef4444" stopOpacity={0.35} />
                          <stop offset="100%" stopColor="#ef4444" stopOpacity={0.02} />
                        </linearGradient>
                        <linearGradient id="areaPurchaseReturns" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="0%" stopColor="#f59e0b" stopOpacity={0.35} />
                          <stop offset="100%" stopColor="#f59e0b" stopOpacity={0.02} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" opacity={0.12} stroke="#94a3b8" vertical={false} />
                      <XAxis dataKey="date" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} minTickGap={28} tickFormatter={(val) => val.slice(5)} />
                      <YAxis tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} width={58} tickFormatter={(val) => `Rs ${fmtCompact(val)}`} />
                      <Tooltip
                        contentStyle={tooltipStyle(isDark)}
                        formatter={(val, name) => [`Rs ${fmtCurrency(val)}`, name === 'saleReturns' ? 'Sale Returns' : 'Purchase Returns']}
                        labelFormatter={(label) => fmtDateLabel(label)}
                      />
                      <Legend iconType="circle" iconSize={7} wrapperStyle={{ paddingTop: '10px', fontSize: '12px' }} />
                      <Area type="monotone" dataKey="saleReturns" name="Sale Returns" stroke="#ef4444" strokeWidth={2.2} fill="url(#areaSaleReturns)" dot={false} />
                      <Area type="monotone" dataKey="purchaseReturns" name="Purchase Returns" stroke="#f59e0b" strokeWidth={2.2} fill="url(#areaPurchaseReturns)" dot={false} />
                    </AreaChart>
                  )}
                </ResponsiveContainer>
              </div>
            </div>
          </SectionCard>

          {/* Performance & Store Vitals Dual Hub */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-4">
            {/* Top Movers: Switchable between Top Products & Top Brands (7 cols) */}
            <SectionCard className="lg:col-span-7">
              <CardHeader
                icon={TrophyIcon}
                iconStyle={{ bg: '#fef3c7', color: '#d97706' }}
                title="Top Performers"
                subtitle="Best selling catalog inventory in this period"
                right={
                  <div className="flex items-center gap-1 bg-[var(--workspace-reading-fill)] p-1 rounded-lg border border-[var(--workspace-border)]">
                    <button
                      type="button"
                      onClick={() => setTopMoversView("products")}
                      className={`px-2.5 py-1 text-xs font-semibold rounded-md transition-all cursor-pointer ${
                        topMoversView === "products"
                          ? "theme-btn-primary shadow-2xs"
                          : "text-[var(--workspace-muted)] hover:text-[var(--workspace-ink)]"
                      }`}
                    >
                      Products
                    </button>
                    <button
                      type="button"
                      onClick={() => setTopMoversView("brands")}
                      className={`px-2.5 py-1 text-xs font-semibold rounded-md transition-all cursor-pointer ${
                        topMoversView === "brands"
                          ? "theme-btn-primary shadow-2xs"
                          : "text-[var(--workspace-muted)] hover:text-[var(--workspace-ink)]"
                      }`}
                    >
                      Brands
                    </button>
                  </div>
                }
              />

              <div className="pt-1">
                {topMoversView === "products" ? (
                  Array.isArray(topProducts) && topProducts.length > 0 ? (
                    <ul className="divide-y divide-[var(--workspace-border)] max-h-80 overflow-y-auto">
                      {topProducts.map((p, i) => (
                        <RankedRow
                          key={`${p.id}-${i}`}
                          rank={i + 1}
                          title={String(p.product_name || `Product #${p.id}`)}
                          meta={`${Number(p.total_sold || 0).toLocaleString()} sold`}
                          amount={`Rs ${fmtCompact(p.revenue)}`}
                          pct={((Number(p.total_sold) || 0) / maxTopProduct) * 100}
                          color={themeColors.primary}
                        />
                      ))}
                    </ul>
                  ) : (
                    <div className="flex flex-col items-center justify-center py-12 text-center gap-2">
                      <FireIcon className="w-8 h-8 text-[var(--workspace-muted)] opacity-40" />
                      <p className="text-xs text-[var(--workspace-muted)]">No product sales recorded in this period</p>
                    </div>
                  )
                ) : Array.isArray(brandSales) && brandSales.length > 0 ? (
                  <ul className="divide-y divide-[var(--workspace-border)] max-h-80 overflow-y-auto">
                    {brandSales.map((b, i) => (
                      <RankedRow
                        key={`${b.brand}-${i}`}
                        rank={i + 1}
                        title={String(b.brand || "Unknown Brand")}
                        amount={`Rs ${fmtCompact(b.revenue)}`}
                        pct={((Number(b.revenue) || 0) / maxBrandSales) * 100}
                        color={themeColors.secondary}
                      />
                    ))}
                  </ul>
                ) : (
                  <div className="flex flex-col items-center justify-center py-12 text-center gap-2">
                    <TrophyIcon className="w-8 h-8 text-[var(--workspace-muted)] opacity-40" />
                    <p className="text-xs text-[var(--workspace-muted)]">No brand sales recorded in this period</p>
                  </div>
                )}
              </div>
            </SectionCard>

            {/* Store Operational Vitals (5 cols) */}
            <SectionCard className="lg:col-span-5">
              <CardHeader
                icon={SparklesIcon}
                iconStyle={{ bg: themeColors.primaryLight, color: themeColors.primary }}
                title="Store Operational Vitals"
                subtitle="High-level operational overview"
              />

              <div className="p-4 sm:p-5 space-y-4">
                <div className="grid grid-cols-2 gap-3">
                  <div className="p-3 rounded-xl bg-[var(--workspace-reading-fill)] border border-[var(--workspace-border)] text-center">
                    <div className="text-lg font-bold font-mono text-[var(--workspace-ink)]">
                      {invoiceCounts.total || 0}
                    </div>
                    <div className="text-xs text-[var(--workspace-muted)] mt-0.5">Total Invoices</div>
                    <div className="text-[10px] text-[var(--workspace-muted)] mt-1">
                      {invoiceCounts.sale_invoices || 0} Sales • {invoiceCounts.purchase_invoices || 0} Purchases
                    </div>
                  </div>

                  <div className="p-3 rounded-xl bg-[var(--workspace-reading-fill)] border border-[var(--workspace-border)] text-center">
                    <div className="text-lg font-bold font-mono text-[var(--workspace-ink)]">
                      {Number(kpiMetrics.stock_units || 0).toLocaleString()}
                    </div>
                    <div className="text-xs text-[var(--workspace-muted)] mt-0.5">Stock Units on Hand</div>
                    <div className="text-[10px] text-[var(--workspace-muted)] mt-1">
                      Across {kpiMetrics.active_products || 0} active products
                    </div>
                  </div>
                </div>

                <div className="space-y-2.5 pt-1 text-xs">
                  <div className="flex items-center justify-between p-2 rounded-lg bg-[var(--workspace-reading-fill)]">
                    <span className="text-[var(--workspace-muted)]">Inventory Valuation</span>
                    <span className="font-semibold text-[var(--workspace-ink)] tabular-nums">
                      {kpiMetrics.stock_value ? `Rs ${fmtCurrency(kpiMetrics.stock_value)}` : "—"}
                    </span>
                  </div>

                  <div className="flex items-center justify-between p-2 rounded-lg bg-[var(--workspace-reading-fill)]">
                    <span className="text-[var(--workspace-muted)]">Active Network</span>
                    <span className="font-semibold text-[var(--workspace-ink)]">
                      {kpiMetrics.customers || 0} Customers • {kpiMetrics.suppliers || 0} Suppliers
                    </span>
                  </div>

                  <div className="flex items-center justify-between p-2 rounded-lg bg-[var(--workspace-reading-fill)]">
                    <span className="text-[var(--workspace-muted)]">Customer Demands</span>
                    <span className="font-semibold text-blue-600 dark:text-blue-400">
                      {kpiMetrics.pending_demands || 0} Pending
                    </span>
                  </div>
                </div>

                {/* Quick Workflow Links */}
                <div className="pt-3 border-t border-[var(--workspace-border)] flex items-center justify-between gap-2">
                  <Link
                    to="/products"
                    className="text-xs font-semibold text-[var(--workspace-accent)] hover:underline inline-flex items-center gap-1"
                  >
                    <span>Products Catalog</span>
                    <ChevronRightIcon className="w-3 h-3" />
                  </Link>

                  <Link
                    to="/purchase-orders"
                    className="text-xs font-semibold text-[var(--workspace-accent)] hover:underline inline-flex items-center gap-1"
                  >
                    <span>Purchase Orders</span>
                    <ChevronRightIcon className="w-3 h-3" />
                  </Link>
                </div>
              </div>
            </SectionCard>
          </div>
        </div>
      )}

      {/* ===================== TAB 2: OPERATIONS & STOCK ===================== */}
      {activeTab === "operations" && (
        <div className="space-y-4">
          {/* Operations Snapshot Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3.5">
            <div className="g-card p-4 border-l-4 border-l-[var(--workspace-accent)] flex flex-col justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-[var(--workspace-accent)]">
                Stock Valuation
              </span>
              <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)] mt-2">
                {kpiMetrics.stock_value ? `Rs ${fmtCurrency(kpiMetrics.stock_value)}` : "—"}
              </div>
              <p className="text-xs text-[var(--workspace-muted)] mt-1">Total estimated inventory value</p>
            </div>

            <div className="g-card p-4 border-l-4 border-l-blue-500 flex flex-col justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-blue-600 dark:text-blue-400">
                Units in Stock
              </span>
              <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)] mt-2">
                {Number(kpiMetrics.stock_units || 0).toLocaleString()}
              </div>
              <p className="text-xs text-[var(--workspace-muted)] mt-1">Across {kpiMetrics.active_products || 0} active products</p>
            </div>

            <div className="g-card p-4 border-l-4 border-l-rose-500 flex flex-col justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-rose-600 dark:text-rose-400">
                Near Expiry Batches
              </span>
              <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)] mt-2">
                {kpiMetrics.near_expiry || 0}
              </div>
              <p className="text-xs text-[var(--workspace-muted)] mt-1">Batches expiring within 3 months</p>
            </div>

            <div className="g-card p-4 border-l-4 border-l-amber-500 flex flex-col justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-amber-600 dark:text-amber-400">
                Low Stock Warnings
              </span>
              <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)] mt-2">
                {kpiMetrics.low_stock || 0}
              </div>
              <p className="text-xs text-[var(--workspace-muted)] mt-1">Products below primary pack size</p>
            </div>
          </div>

          {/* Dedicated Near-Expiry Radar Section */}
          <SectionCard>
            <CardHeader
              icon={ClockIcon}
              iconStyle={{ bg: '#fee2e2', color: '#dc2626' }}
              title="Near Expiry Batch Radar"
              subtitle="Filter and review inventory batches approaching expiration"
              right={
                <div className="flex items-center gap-2">
                  <span className="text-xs font-semibold text-[var(--workspace-muted)] whitespace-nowrap hidden sm:inline">
                    Expiry Horizon:
                  </span>
                  <div className="w-44 sm:w-52 shrink-0">
                    <Select
                      options={expiryHorizonOptions}
                      value={expiryHorizonOptions.find((o) => o.value === expiryMonths) || expiryHorizonOptions[2]}
                      onChange={(opt) => setExpiryMonths(opt?.value || 3)}
                      styles={getPeriodSelectStyles(isDark)}
                      isSearchable={false}
                      menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                      aria-label="Select near expiry horizon"
                    />
                  </div>
                </div>
              }
            />

            {/* Filter controls */}
            <div className="flex flex-col lg:flex-row lg:items-center gap-3 px-4 py-3 sm:px-5 border-b border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)]">
              <div className="flex items-center gap-2 w-full sm:w-auto">
                <span className="text-xs font-semibold text-[var(--workspace-muted)] shrink-0">Supplier:</span>
                <div className="flex-1 sm:w-48 relative z-50">
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

              <div className="flex items-center gap-2 w-full sm:w-auto">
                <span className="text-xs font-semibold text-[var(--workspace-muted)] shrink-0">Brand:</span>
                <div className="flex-1 sm:w-48 relative z-50">
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

              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => {
                    setSupplierValue(null);
                    setBrandValue(null);
                    setSupplierId("");
                    setBrandId("");
                  }}
                  className="theme-btn-secondary px-3 py-1.5 text-xs font-semibold"
                >
                  Clear Filters
                </button>

                <button
                  type="button"
                  onClick={fetchNearExpiry}
                  disabled={loadingExpiry}
                  className="theme-btn-primary inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold disabled:opacity-60"
                >
                  <ArrowPathIcon className={`w-3.5 h-3.5 ${loadingExpiry ? "animate-spin" : ""}`} />
                  <span>{loadingExpiry ? "Refreshing…" : "Filter Batches"}</span>
                </button>
              </div>

              <div className="lg:ml-auto flex items-center gap-3">
                <Link
                  to="/batch-workbench"
                  className="text-xs font-semibold text-[var(--workspace-accent)] hover:underline inline-flex items-center gap-1"
                >
                  <span>Batch Workbench</span>
                  <ChevronRightIcon className="w-3 h-3" />
                </Link>
              </div>
            </div>

            {/* Table */}
            <div className="overflow-x-auto max-h-[26rem]">
              <table className="w-full text-xs text-left">
                <thead className="sticky top-0 bg-[var(--workspace-surface)] z-10 border-b border-[var(--workspace-border)] font-semibold text-[var(--workspace-muted)]">
                  <tr>
                    <th className="px-4 py-2.5">Product</th>
                    <th className="px-4 py-2.5">Supplier</th>
                    <th className="px-4 py-2.5">Brand</th>
                    <th className="px-4 py-2.5 font-mono">Batch #</th>
                    <th className="px-4 py-2.5">Expiry Date</th>
                    <th className="px-4 py-2.5">Status</th>
                    <th className="px-4 py-2.5 text-right">In Stock</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--workspace-border)]">
                  {nearExpiryRows.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="px-4 py-12 text-center text-[var(--workspace-muted)]">
                        {loadingExpiry ? (
                          <span className="inline-flex items-center gap-2">
                            <ArrowPathIcon className="w-4 h-4 animate-spin text-[var(--workspace-accent)]" />
                            Loading near-expiry items…
                          </span>
                        ) : (
                          "No near-expiry inventory batches found for this criteria."
                        )}
                      </td>
                    </tr>
                  ) : (
                    nearExpiryRows.map((r) => {
                      const daysLeft = daysLeftOf(r.expiry_date);
                      return (
                        <tr
                          key={`b-${r.batch_id}`}
                          className="hover:bg-[color-mix(in_srgb,var(--workspace-accent)_4%,transparent)] transition-colors"
                        >
                          <td className="px-4 py-2.5 text-[var(--workspace-ink)]">
                            <div className="font-semibold truncate max-w-[240px]" title={r.product_name}>
                              {r.product_name}
                            </div>
                            <div className="text-[10px] text-[var(--workspace-muted)] font-mono">
                              {r.product_code}
                            </div>
                          </td>
                          <td className="px-4 py-2.5 text-[var(--workspace-muted)]">{r.supplier_name || "—"}</td>
                          <td className="px-4 py-2.5 text-[var(--workspace-muted)]">{r.brand_name || "—"}</td>
                          <td className="px-4 py-2.5 font-mono text-[var(--workspace-ink)]">{r.batch_number}</td>
                          <td className="px-4 py-2.5 text-[var(--workspace-ink)] font-medium">
                            {String(r.expiry_date || "").slice(0, 10)}
                          </td>
                          <td className="px-4 py-2.5">
                            {daysLeft != null ? <ExpiryPill days={daysLeft} /> : <span className="text-[var(--workspace-muted)]">—</span>}
                          </td>
                          <td className="px-4 py-2.5 text-right font-bold text-[var(--workspace-ink)] tabular-nums">
                            {Number(r.quantity ?? 0).toLocaleString()}
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </SectionCard>
        </div>
      )}

      {/* ===================== TAB 3: FINANCIALS & CREDIT ===================== */}
      {activeTab === "financials" && (
        <div className="space-y-4">
          {/* Financial Breakdown Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3.5">
            <div className="g-card p-4 border-l-4 border-l-blue-500 flex flex-col justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-blue-600 dark:text-blue-400">
                Customer Receivables (Credit)
              </span>
              <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)] mt-2">
                Rs {fmtCurrency(cards.salesCredit)}
              </div>
              <p className="text-xs text-[var(--workspace-muted)] mt-1">Credit sales in this period</p>
            </div>

            <div className="g-card p-4 border-l-4 border-l-purple-500 flex flex-col justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-purple-600 dark:text-purple-400">
                Supplier Payables (Dues)
              </span>
              <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)] mt-2">
                Rs {fmtCurrency(cards.purchasesCredit)}
              </div>
              <p className="text-xs text-[var(--workspace-muted)] mt-1">Outstanding supplier payables</p>
            </div>

            <div className="g-card p-4 border-l-4 border-l-rose-500 flex flex-col justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-rose-600 dark:text-rose-400">
                Sale Returns
              </span>
              <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)] mt-2">
                Rs {fmtCurrency(cards.saleReturns)}
              </div>
              <p className="text-xs text-[var(--workspace-muted)] mt-1">Customer returned goods value</p>
            </div>

            <div className="g-card p-4 border-l-4 border-l-amber-500 flex flex-col justify-between">
              <span className="text-xs font-semibold uppercase tracking-wider text-amber-600 dark:text-amber-400">
                Purchase Returns
              </span>
              <div className="text-2xl font-bold font-mono text-[var(--workspace-ink)] mt-2">
                Rs {fmtCurrency(cards.purchaseReturns)}
              </div>
              <p className="text-xs text-[var(--workspace-muted)] mt-1">Stock returned to suppliers</p>
            </div>
          </div>

          {/* Quick Ledger Navigation Hub */}
          <SectionCard>
            <CardHeader
              icon={BanknotesIcon}
              title="Financial Centers & Ledgers"
              subtitle="Direct shortcuts to reconciliation, registers, and accounts"
            />

            <div className="p-4 sm:p-5 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3.5">
              <Link
                to="/customer-ledger"
                className="p-4 rounded-xl border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_6%,transparent)] transition-all group flex items-start gap-3"
              >
                <div className="p-2 rounded-lg bg-blue-50 dark:bg-blue-950/40 text-blue-600 dark:text-blue-400 shrink-0">
                  <UsersIcon className="w-5 h-5" />
                </div>
                <div>
                  <h4 className="font-semibold text-sm text-[var(--workspace-ink)] group-hover:text-[var(--workspace-accent)] transition-colors">
                    Customer Ledger
                  </h4>
                  <p className="text-xs text-[var(--workspace-muted)] mt-0.5">
                    Track customer outstanding balances, credit statements, and payment receipts.
                  </p>
                </div>
              </Link>

              <Link
                to="/supplier-ledger"
                className="p-4 rounded-xl border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_6%,transparent)] transition-all group flex items-start gap-3"
              >
                <div className="p-2 rounded-lg bg-purple-50 dark:bg-purple-950/40 text-purple-600 dark:text-purple-400 shrink-0">
                  <BuildingStorefrontIcon className="w-5 h-5" />
                </div>
                <div>
                  <h4 className="font-semibold text-sm text-[var(--workspace-ink)] group-hover:text-[var(--workspace-accent)] transition-colors">
                    Supplier Ledger
                  </h4>
                  <p className="text-xs text-[var(--workspace-muted)] mt-0.5">
                    Monitor supplier bills, payment reconciliations, and vendor dues.
                  </p>
                </div>
              </Link>

              <Link
                to="/cash-drawer"
                className="p-4 rounded-xl border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_6%,transparent)] transition-all group flex items-start gap-3"
              >
                <div className="p-2 rounded-lg bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400 shrink-0">
                  <BanknotesIcon className="w-5 h-5" />
                </div>
                <div>
                  <h4 className="font-semibold text-sm text-[var(--workspace-ink)] group-hover:text-[var(--workspace-accent)] transition-colors">
                    Cash Drawer & Registers
                  </h4>
                  <p className="text-xs text-[var(--workspace-muted)] mt-0.5">
                    Audit opening cash, shift totals, register closures, and cash flow.
                  </p>
                </div>
              </Link>

              <Link
                to="/expenses"
                className="p-4 rounded-xl border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_6%,transparent)] transition-all group flex items-start gap-3"
              >
                <div className="p-2 rounded-lg bg-rose-50 dark:bg-rose-950/40 text-rose-600 dark:text-rose-400 shrink-0">
                  <TagIcon className="w-5 h-5" />
                </div>
                <div>
                  <h4 className="font-semibold text-sm text-[var(--workspace-ink)] group-hover:text-[var(--workspace-accent)] transition-colors">
                    Operating Expenses
                  </h4>
                  <p className="text-xs text-[var(--workspace-muted)] mt-0.5">
                    Log and categorize overhead, payroll, utilities, and store expenditures.
                  </p>
                </div>
              </Link>

              <Link
                to="/accounts"
                className="p-4 rounded-xl border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_6%,transparent)] transition-all group flex items-start gap-3"
              >
                <div className="p-2 rounded-lg bg-indigo-50 dark:bg-indigo-950/40 text-indigo-600 dark:text-indigo-400 shrink-0">
                  <ScaleIcon className="w-5 h-5" />
                </div>
                <div>
                  <h4 className="font-semibold text-sm text-[var(--workspace-ink)] group-hover:text-[var(--workspace-accent)] transition-colors">
                    Chart of Accounts
                  </h4>
                  <p className="text-xs text-[var(--workspace-muted)] mt-0.5">
                    Full double-entry general ledger, bank balances, and asset/liability sheets.
                  </p>
                </div>
              </Link>

              <Link
                to="/reports/sale-detail"
                className="p-4 rounded-xl border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_6%,transparent)] transition-all group flex items-start gap-3"
              >
                <div className="p-2 rounded-lg bg-amber-50 dark:bg-amber-950/40 text-amber-600 dark:text-amber-400 shrink-0">
                  <ClipboardDocumentListIcon className="w-5 h-5" />
                </div>
                <div>
                  <h4 className="font-semibold text-sm text-[var(--workspace-ink)] group-hover:text-[var(--workspace-accent)] transition-colors">
                    Sale Detail Reports
                  </h4>
                  <p className="text-xs text-[var(--workspace-muted)] mt-0.5">
                    Deep item-level sales velocity, profit margins, and tax audits.
                  </p>
                </div>
              </Link>
            </div>
          </SectionCard>
        </div>
      )}
    </div>
  );
}
