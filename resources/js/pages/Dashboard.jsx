import { useEffect, useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import AsyncSelect from "react-select/async";
import { createFilter } from "react-select";
import { useNavigate } from "react-router-dom";
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

function buildNetSeries(series) {
  const map = new Map();
  for (const row of series.sales || []) map.set(row.date, (map.get(row.date) || 0) + Number(row.value || 0));
  for (const row of series.saleReturns || [])
    map.set(row.date, (map.get(row.date) || 0) - Number(row.value || 0));
  return Array.from(map.entries()).map(([date, value]) => ({ date, value }));
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
    minHeight: 32,
    height: 32,
    borderRadius: 10,
    borderColor: isDark ? "rgba(71,85,105,0.8)" : "rgba(203,213,225,0.9)",
    backgroundColor: isDark ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.9)",
    boxShadow: "none",
  }),
  valueContainer: (base) => ({ ...base, height: 32, padding: "0 8px" }),
  indicatorsContainer: (base) => ({ ...base, height: 32 }),
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

/* ===================== Reusable pieces ===================== */
function Sparkline({ data, color, suffix }) {
  const gradId = `sparkGrad_${suffix}`;
  return (
    <div className="w-full h-10">
      {Array.isArray(data) && data.length > 0 ? (
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={data} margin={{ top: 4, right: 0, left: 0, bottom: 0 }}>
            <defs>
              <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={color} stopOpacity={0.3} />
                <stop offset="100%" stopColor={color} stopOpacity={0} />
              </linearGradient>
            </defs>
            <Area type="monotone" dataKey="value" stroke={color} strokeWidth={2} fill={`url(#${gradId})`} isAnimationActive={false} />
          </AreaChart>
        </ResponsiveContainer>
      ) : (
        <div className="w-full h-full flex items-end pb-2">
          <div className="w-full h-px bg-slate-100 dark:bg-slate-700/70" />
        </div>
      )}
    </div>
  );
}

function SectionCard({ children, className = "" }) {
  return (
    <div className={`rounded-2xl bg-white dark:bg-slate-800 border border-slate-200/80 dark:border-slate-700/70 shadow-xs ${className}`}>
      {children}
    </div>
  );
}

function CardHeader({ icon: Icon, iconStyle, title, subtitle, right }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-4 border-b border-slate-100 dark:border-slate-700/70">
      <div className="flex items-center gap-3 min-w-0">
        <div className="p-2 rounded-xl shadow-xs shrink-0" style={{ backgroundColor: iconStyle?.bg || "transparent" }}>
          <Icon className="w-4 h-4" style={{ color: iconStyle?.color || "#64748b" }} />
        </div>
        <div className="min-w-0">
          <h3 className="text-sm font-semibold text-slate-800 dark:text-slate-100 truncate">{title}</h3>
          <p className="text-xs text-slate-500 dark:text-slate-400 truncate">{subtitle}</p>
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
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold ${
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
    <li className="flex items-center gap-3 px-5 py-3">
      <span
        className="w-6 h-6 rounded-lg text-[11px] font-bold flex items-center justify-center shrink-0"
        style={
          rank <= 3
            ? { background: color, color: "#fff", boxShadow: `0 2px 8px ${color}55` }
            : { background: "transparent", color: "#94a3b8" }
        }
      >
        {rank}
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center justify-between gap-3">
          <p className="text-sm font-medium text-slate-800 dark:text-slate-100 truncate">{title}</p>
          {meta && <p className="text-xs text-slate-400 dark:text-slate-500 shrink-0 tabular-nums">{meta}</p>}
        </div>
        <div className="mt-1.5 flex items-center gap-2">
          <div className="h-1.5 flex-1 rounded-full bg-slate-100 dark:bg-slate-700 overflow-hidden">
            <div
              className="h-full rounded-full"
              style={{ width: `${Math.max(2, Math.min(100, pct))}%`, background: `linear-gradient(90deg, ${color}, ${color}B3)` }}
            />
          </div>
          <span className="text-[11px] font-semibold text-slate-500 dark:text-slate-400 tabular-nums shrink-0">{amount}</span>
        </div>
      </div>
    </li>
  );
}

function ExpiryPill({ days }) {
  let styles = "";
  const d = Number(days);
  if (d <= 30) {
    styles = "bg-rose-50 text-rose-600 ring-rose-200 dark:bg-rose-500/10 dark:text-rose-400 dark:ring-rose-500/25";
  } else if (d <= 90) {
    styles = "bg-amber-50 text-amber-600 ring-amber-200 dark:bg-amber-500/10 dark:text-amber-400 dark:ring-amber-500/25";
  } else {
    styles = "bg-emerald-50 text-emerald-600 ring-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-400 dark:ring-emerald-500/25";
  }
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-semibold ring-1 whitespace-nowrap ${styles}`}>
      {d} days left
    </span>
  );
}

/* ===================== Component ===================== */
export default function Dashboard() {
  const { isDark, theme } = useTheme();
  const navigate = useNavigate();
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

  const netSales = useMemo(() => (cards.sales || 0) - (cards.saleReturns || 0), [cards]);
  const prevNetSales = useMemo(() => (prevCards.sales || 0) - (prevCards.saleReturns || 0), [prevCards]);

  const returnRate = useMemo(() => {
    const total = cards.sales || 0;
    return total > 0 ? Math.round(((cards.saleReturns || 0) / total) * 100) : 0;
  }, [cards]);

  const salesSparkData = useMemo(() => (series.sales || []).map((r) => ({ ...r })), [series.sales]);
  const purchasesSparkData = useMemo(() => (series.purchases || []).map((r) => ({ ...r })), [series.purchases]);
  const returnsSparkData = useMemo(() => (series.saleReturns || []).map((r) => ({ ...r })), [series.saleReturns]);
  const netSparkData = useMemo(() => buildNetSeries({ sales: series.sales, saleReturns: series.saleReturns }), [series.sales, series.saleReturns]);
  const revenueData = useMemo(
    () => mergeTwo(series.sales, series.purchases, 'sales', 'purchases'),
    [series.sales, series.purchases]
  );

  const maxTopProduct = useMemo(() => {
    return Math.max(1, ...(topProducts || []).map((p) => Number(p.total_sold) || 0));
  }, [topProducts]);

  const maxBrandSales = useMemo(() => {
    return Math.max(1, ...(brandSales || []).map((b) => Number(b.revenue) || 0));
  }, [brandSales]);

// Fetch all dashboard data in parallel
  async function fetchAll() {
    const f = from || todayStr();
    const t = to || todayStr();
    setLoading(true);

    try {
      const params = { date_from: f, date_to: t };

      // Compute previous equivalent-length period for trend comparison
      const days = Math.max(1, inclusiveDaysUTC(f, t));
      const prevTo = addDays(f, -1);
      const prevFrom = addDays(prevTo, -(days - 1));
      const prevParams = { date_from: prevFrom, date_to: prevTo };

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
    } catch (err) {
      console.error(err);
      toast.error("Failed to load dashboard.");
    } finally {
      setLoading(false);
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to]);

  useEffect(() => {
    fetchNearExpiry();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [expiryMonths, supplierId, brandId]);

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

  const quickPresets = [
    { label: "Today", fn: () => { setFrom(todayStr()); setTo(todayStr()); } },
    { label: "This Month", fn: () => { setFrom(firstDayOfMonthStr()); setTo(todayStr()); } },
    { label: "7 Days", fn: () => {
      const d = new Date();
      const fromDate = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate() - 6));
      setFrom(fromDate.toISOString().substring(0, 10));
      setTo(todayStr());
    } },
    { label: "30 Days", fn: () => {
      const d = new Date();
      const fromDate = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate() - 29));
      setFrom(fromDate.toISOString().substring(0, 10));
      setTo(todayStr());
    } },
    { label: "This Year", fn: () => { setFrom(firstDayOfYearStr()); setTo(todayStr()); } },
  ];

  const kpiCards = [
    {
      label: "Sales",
      value: cards.sales,
      prev: prevCards.sales,
      icon: CurrencyDollarIcon,
      color: themeColors.primary,
      bg: themeColors.primaryLight,
      spark: salesSparkData,
      sparkColor: themeColors.primary,
      footer: `Avg Rs ${fmtCurrency(daysInPeriod > 0 ? cards.sales / daysInPeriod : 0)}/day`,
      path: "/sale-invoices",
      sub: [
        { label: "Debit", value: cards.salesDebit, color: "#10b981" },
        { label: "Credit", value: cards.salesCredit, color: "#f59e0b" },
      ],
    },
    {
      label: "Purchases",
      value: cards.purchases,
      prev: prevCards.purchases,
      icon: ShoppingCartIcon,
      color: themeColors.secondary,
      bg: themeColors.secondaryLight,
      spark: purchasesSparkData,
      sparkColor: themeColors.secondary,
      footer: `Avg Rs ${fmtCurrency(daysInPeriod > 0 ? cards.purchases / daysInPeriod : 0)}/day`,
      path: "/purchase-invoices",
      sub: [
        { label: "Debit", value: cards.purchasesDebit, color: "#10b981" },
        { label: "Credit", value: cards.purchasesCredit, color: "#f59e0b" },
      ],
    },
    {
      label: "Net Sales",
      value: netSales,
      prev: prevNetSales,
      icon: BanknotesIcon,
      color: themeColors.tertiary,
      bg: themeColors.tertiaryLight,
      spark: netSparkData,
      sparkColor: themeColors.tertiary,
      footer: `Returns Rs ${fmtCurrency(cards.saleReturns)}`,
      path: "/reports/sale-detail",
    },
    {
      label: "Sale Returns",
      value: cards.saleReturns,
      prev: prevCards.saleReturns,
      icon: ArrowUturnLeftIcon,
      color: '#f43f5e',
      bg: '#ffe4e6',
      spark: returnsSparkData,
      sparkColor: '#f43f5e',
      footer: `${returnRate}% of sales`,
      path: "/sale-returns",
    },
  ];

const moduleTiles = [
    {
      label: "Products",
      value: kpiMetrics.active_products || 0,
      icon: CubeIcon,
      color: '#6366f1',
      bg: '#eef2ff',
      hint: "Catalog",
      path: "/products",
    },
    {
      label: "Customers",
      value: kpiMetrics.customers || 0,
      icon: UsersIcon,
      color: '#10b981',
      bg: '#ecfdf5',
      hint: "People",
      path: "/customers",
    },
    {
      label: "Suppliers",
      value: kpiMetrics.suppliers || 0,
      icon: TruckIcon,
      color: '#f59e0b',
      bg: '#fffbeb',
      hint: "Sources",
      path: "/suppliers",
    },
    {
      label: "Brands",
      value: kpiMetrics.brands || 0,
      icon: CheckBadgeIcon,
      color: '#06b6d4',
      bg: '#ecfeff',
      hint: "Labels",
      path: "/brands",
    },
    {
      label: "Categories",
      value: kpiMetrics.categories || 0,
      icon: TagIcon,
      color: '#ec4899',
      bg: '#fdf2f8',
      hint: "Groups",
      path: "/categories",
    },
    {
      label: "Invoices",
      value: invoiceCounts.total || 0,
      icon: ClipboardDocumentListIcon,
      color: '#0ea5e9',
      bg: '#f0f9ff',
      hint: `${invoiceCounts.sale_invoices || 0} sales · ${invoiceCounts.purchase_invoices || 0} purchases`,
      path: "/sale-invoices",
    },
    {
      label: "Low Stock",
      value: kpiMetrics.low_stock || 0,
      icon: ExclamationTriangleIcon,
      color: (kpiMetrics.low_stock || 0) > 0 ? '#ef4444' : '#94a3b8',
      bg: (kpiMetrics.low_stock || 0) > 0 ? '#fef2f2' : '#f1f5f9',
      hint: (kpiMetrics.low_stock || 0) > 0 ? "Needs reorder" : "Stock healthy",
      path: "/products?low_stock=1",
    },
    {
      label: "Near Expiry",
      value: kpiMetrics.near_expiry || 0,
      icon: ClockIcon,
      color: (kpiMetrics.near_expiry || 0) > 0 ? '#f59e0b' : '#94a3b8',
      bg: (kpiMetrics.near_expiry || 0) > 0 ? '#fffbeb' : '#f1f5f9',
      hint: "Within 3 months",
      path: "/reports/near-expiry-product",
    },
    {
      label: "Stock Adjustments",
      value: kpiMetrics.stock_adjustments || 0,
      icon: ScaleIcon,
      color: '#8b5cf6',
      bg: '#f5f3ff',
      hint: "Documents",
      path: "/stock-adjustments",
    },
    {
      label: "Pending Demands",
      value: kpiMetrics.pending_demands || 0,
      icon: ChatBubbleLeftRightIcon,
      color: (kpiMetrics.pending_demands || 0) > 0 ? '#f97316' : '#94a3b8',
      bg: (kpiMetrics.pending_demands || 0) > 0 ? '#fff7ed' : '#f1f5f9',
      hint: "User requests",
      path: "/user-demands",
    },
    {
      label: "Users",
      value: kpiMetrics.users || 0,
      icon: ShieldCheckIcon,
      color: '#14b8a6',
      bg: '#f0fdfa',
      hint: "Accounts",
      path: "/users",
    },
    {
      label: "Stock on Hand",
      value: `${Number(kpiMetrics.stock_units || 0).toLocaleString()} units`,
      icon: BanknotesIcon,
      color: '#3b82f6',
      bg: '#eff6ff',
      hint: `Rs ${fmtCompact(kpiMetrics.stock_value || 0)}`,
      path: "/reports/current-stock",
    },
  ];

return (
    <div className="min-h-full p-4 md:p-6 space-y-5 bg-slate-50 dark:bg-slate-900/60">
      {/* ===== Header ===== */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div className="flex items-center gap-3">
          <div
            className="w-11 h-11 rounded-2xl flex items-center justify-center shadow-md ring-1 ring-white/40"
            style={{ background: `linear-gradient(135deg, ${themeColors.primary}, ${themeColors.secondary})` }}
          >
            <ChartPieIcon className="text-white" style={{ width: 22, height: 22 }} />
          </div>
          <div>
            <h1 className="text-xl md:text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
              Dashboard
            </h1>
            <p className="text-sm text-slate-500 dark:text-slate-400 flex items-center gap-1.5 mt-0.5">
              <CalendarDaysIcon className="w-3.5 h-3.5" />
              {new Date().toLocaleDateString(undefined, { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' })}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2.5">
          <span className="hidden sm:inline-flex items-center gap-2 rounded-xl bg-white dark:bg-slate-800 border border-slate-200/80 dark:border-slate-700/70 px-3.5 py-2 text-xs font-semibold text-slate-500 dark:text-slate-400 shadow-xs">
            <SparklesIcon className="w-4 h-4" style={{ color: themeColors.primary }} />
            {daysInPeriod > 1 ? `${daysInPeriod}-day view` : "Today's overview"}
          </span>
          <button
            onClick={fetchAll}
            disabled={loading}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-semibold text-white shadow-md hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200 disabled:opacity-60"
            style={{ background: `linear-gradient(135deg, ${themeColors.primary}, ${themeColors.primaryHover})` }}
          >
            <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
            {loading ? "Loading…" : "Refresh"}
          </button>
        </div>
      </div>

      {/* ===== Filters Toolbar ===== */}
      <SectionCard>
        <div className="flex flex-col lg:flex-row lg:items-center gap-3 px-5 py-4">
          <div className="flex flex-col sm:flex-row sm:items-center gap-3 sm:gap-2 flex-1">
            <div className="flex items-center gap-2 flex-1 min-w-[150px]">
              <label className="text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500 whitespace-nowrap">From</label>
              <input
                type="date"
                value={from}
                onChange={(e) => setFrom(e.target.value)}
                className="w-full min-w-0 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-700/60 px-3 py-1.5 text-sm text-slate-800 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-blue-500/40 transition-shadow"
              />
            </div>
            <div className="flex items-center gap-2 flex-1 min-w-[150px]">
              <label className="text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500 whitespace-nowrap">To</label>
              <input
                type="date"
                value={to}
                onChange={(e) => setTo(e.target.value)}
                className="w-full min-w-0 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-700/60 px-3 py-1.5 text-sm text-slate-800 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-blue-500/40 transition-shadow"
              />
            </div>
            <div className="flex items-center gap-1.5 flex-wrap">
              {quickPresets.map((p) => (
                <button
                  key={p.label}
                  onClick={p.fn}
                  className="px-2.5 py-1 rounded-lg text-xs font-semibold text-slate-500 dark:text-slate-400 bg-slate-100 dark:bg-slate-700/50 hover:bg-slate-200 dark:hover:bg-slate-700 hover:text-slate-700 dark:hover:text-slate-200 active:scale-95 transition-all duration-150"
                >
                  {p.label}
                </button>
              ))}
            </div>
          </div>

          <div className="flex items-center gap-1.5 overflow-x-auto">
            {[
              { label: "Net Sales", value: fmtCurrency(netSales), color: themeColors.primary, hint: "Total sales minus sale returns in this period" },
              { label: "Daily Avg", value: fmtCurrency(daysInPeriod > 0 ? netSales / daysInPeriod : 0), color: themeColors.tertiary, hint: "Net sales ÷ days shown — what you average each day" },
              { label: "Return Rate", value: `${returnRate}%`, color: themeColors.secondary, hint: "Sale returns as a % of total sales in this period" },
            ].map((s) => (
              <div
                key={s.label}
                title={s.hint}
                className="flex flex-col items-start gap-0.5 rounded-lg px-3 py-1.5 bg-slate-100/70 dark:bg-slate-700/40 border border-slate-200/60 dark:border-slate-700/50"
              >
                <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">{s.label}</span>
                <span className="text-sm font-bold tabular-nums" style={{ color: s.color }}>
                  {s.value}
                </span>
              </div>
            ))}
          </div>
        </div>
      </SectionCard>

{/* ===== KPI Money Cards ===== */}
      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
        {kpiCards.map((k, idx) => (
          <div
            key={k.label}
            role="button"
            tabIndex={0}
            onClick={() => k.path && navigate(k.path)}
            onKeyDown={(e) => {
              if ((e.key === "Enter" || e.key === " ") && k.path) {
                e.preventDefault();
                navigate(k.path);
              }
            }}
            title={k.path ? `Open ${k.label} page` : undefined}
            className="group relative rounded-2xl bg-white dark:bg-slate-800 border border-slate-200/80 dark:border-slate-700/70 shadow-xs hover:shadow-lg hover:-translate-y-0.5 hover:ring-2 hover:ring-blue-500/20 transition-all duration-300 overflow-hidden cursor-pointer focus:outline-hidden focus-visible:ring-2 focus-visible:ring-blue-500/40"
          >
            <div className="h-1 w-full" style={{ background: `linear-gradient(to right, ${k.color}, ${k.color}B3)` }} />
            <div className="p-5">
              <div className="flex items-center justify-between gap-3 mb-1">
                <div className="p-2.5 rounded-xl shadow-xs" style={{ backgroundColor: k.bg }}>
                  <k.icon className="w-5 h-5" style={{ color: k.color }} />
                </div>
                <div className="flex items-center gap-2">
                  <span className="text-[11px] font-bold uppercase tracking-widest text-slate-400 dark:text-slate-500">
                    {k.label}
                  </span>
                  <TrendPill value={pctChange(k.value, k.prev)} />
                </div>
              </div>
              <div className="mt-3 text-2xl font-extrabold tracking-tight text-slate-900 dark:text-white tabular-nums">
                Rs {fmtCurrency(k.value)}
              </div>
              <div className="mt-3 -mb-1">
                <Sparkline data={k.spark} color={k.sparkColor} suffix={String(idx)} />
              </div>
              {k.sub && k.sub.length > 0 && (
                <div className="mt-3 pt-3 border-t border-slate-100 dark:border-slate-700/60 grid grid-cols-2 gap-3">
                  {k.sub.map((row) => (
                    <div key={row.label} className="flex items-center gap-1.5">
                      <span className="w-2 h-2 rounded-full shrink-0" style={{ background: row.color }} />
                      <div className="min-w-0 flex-1">
                        <div className="text-[10px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">{row.label}</div>
                        <div className="text-sm font-bold text-slate-800 dark:text-slate-100 tabular-nums truncate" title={`Rs ${fmtCurrency(row.value)}`}>
                          Rs {fmtCurrency(row.value)}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
            <div className="flex items-center justify-between px-5 py-3 bg-slate-50/70 dark:bg-slate-700/30 border-t border-slate-100 dark:border-slate-700/60">
              <span className="text-[11px] font-semibold" style={{ color: k.color }}>
                {k.footer}
              </span>
              <span className="flex items-center gap-2">
                <span className="text-[11px] text-slate-400 dark:text-slate-500">
                  vs prev {Math.max(daysInPeriod - 1, 1)}d
                </span>
                <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5 text-slate-300 dark:text-slate-600 group-hover:text-slate-500 dark:group-hover:text-slate-400 group-hover:translate-x-0.5 group-hover:-translate-y-0.5 transition-all duration-200" />
              </span>
            </div>
          </div>
        ))}
      </div>

      {/* ===== App Module Stats ===== */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-3">
        {moduleTiles.map((chip) => (
          <div
            key={chip.label}
            role="button"
            tabIndex={0}
            onClick={() => chip.path && navigate(chip.path)}
            onKeyDown={(e) => {
              if ((e.key === "Enter" || e.key === " ") && chip.path) {
                e.preventDefault();
                navigate(chip.path);
              }
            }}
            title={chip.path ? `Open ${chip.label} page` : undefined}
            className="group flex items-center gap-3 rounded-2xl bg-white dark:bg-slate-800 border border-slate-200/80 dark:border-slate-700/70 shadow-xs px-4 py-3.5 hover:shadow-md hover:-translate-y-0.5 hover:ring-2 hover:ring-blue-500/20 transition-all duration-200 cursor-pointer focus:outline-hidden focus-visible:ring-2 focus-visible:ring-blue-500/40"
          >
            <div className="p-2 rounded-xl shrink-0" style={{ backgroundColor: chip.bg }}>
              <chip.icon className="w-4 h-4" style={{ color: chip.color }} />
            </div>
            <div className="min-w-0 flex-1">
              <div className="text-lg font-extrabold leading-tight text-slate-900 dark:text-white tabular-nums">
                {typeof chip.value === "number" ? chip.value.toLocaleString() : chip.value}
              </div>
              <div className="text-[11px] font-medium text-slate-400 dark:text-slate-500 truncate">{chip.label}</div>
              {chip.hint && (
                <div className="text-[10px] text-slate-400 dark:text-slate-600 truncate">{chip.hint}</div>
              )}
            </div>
            <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5 shrink-0 text-slate-200 dark:text-slate-700 group-hover:text-slate-500 dark:group-hover:text-slate-400 group-hover:translate-x-0.5 group-hover:-translate-y-0.5 transition-all duration-200" />
          </div>
        ))}
      </div>

{/* ===== Revenue Overview ===== */}
      <SectionCard>
        <CardHeader
          icon={ChartPieIcon}
          iconStyle={{ bg: themeColors.primaryLight, color: themeColors.primary }}
          title="Revenue Overview"
          subtitle="Sales vs Purchases over the selected period"
          right={
            <div className="flex items-center gap-4 text-xs font-medium text-slate-500 dark:text-slate-400">
              <span className="flex items-center gap-1.5">
                <span className="w-2.5 h-2.5 rounded-full" style={{ background: themeColors.primary }} />
                Sales
              </span>
              <span className="flex items-center gap-1.5">
                <span className="w-2.5 h-2.5 rounded-full" style={{ background: themeColors.secondary }} />
                Purchases
              </span>
            </div>
          }
        />
        <div className="p-5">
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%" debounce={100}>
              <AreaChart data={revenueData} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                <defs>
                  <linearGradient id="revSales" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={themeColors.primary} stopOpacity={0.35} />
                    <stop offset="100%" stopColor={themeColors.primary} stopOpacity={0.03} />
                  </linearGradient>
                  <linearGradient id="revPurchases" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={themeColors.secondary} stopOpacity={0.28} />
                    <stop offset="100%" stopColor={themeColors.secondary} stopOpacity={0.02} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" opacity={0.12} stroke="#94a3b8" vertical={false} />
                <XAxis
                  dataKey="date"
                  tick={{ fontSize: 11, fill: '#94a3b8' }}
                  axisLine={false}
                  tickLine={false}
                  minTickGap={28}
                  tickFormatter={(val) => val.slice(5)}
                />
                <YAxis
                  tick={{ fontSize: 11, fill: '#94a3b8' }}
                  axisLine={false}
                  tickLine={false}
                  width={56}
                  tickFormatter={(val) => `Rs ${fmtCompact(val)}`}
                />
                <Tooltip
                  contentStyle={tooltipStyle(isDark)}
                  formatter={(val, name) => [`Rs ${fmtCurrency(val)}`, name === 'sales' ? 'Sales' : 'Purchases']}
                  labelFormatter={(label) => {
                    const d = new Date(`${label}T00:00:00`);
                    return d.toLocaleDateString(undefined, { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' });
                  }}
                  cursor={{ stroke: themeColors.primary, strokeOpacity: 0.25, strokeWidth: 1 }}
                />
                <Legend
                  iconType="circle"
                  iconSize={7}
                  wrapperStyle={{ paddingTop: '12px', fontSize: '12px' }}
                  formatter={(value) => (
                    <span className="text-xs text-slate-500 dark:text-slate-400">{value}</span>
                  )}
                />
                <Area
                  type="monotone"
                  dataKey="sales"
                  name="Sales"
                  stroke={themeColors.primary}
                  strokeWidth={2.5}
                  fill="url(#revSales)"
                  dot={false}
                  activeDot={{ r: 4, strokeWidth: 2, stroke: "#fff" }}
                />
                <Area
                  type="monotone"
                  dataKey="purchases"
                  name="Purchases"
                  stroke={themeColors.secondary}
                  strokeWidth={2.5}
                  fill="url(#revPurchases)"
                  dot={false}
                  activeDot={{ r: 4, strokeWidth: 2, stroke: "#fff" }}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>
      </SectionCard>

{/* ===== Chart trio: comparison + returns trend + top brands ===== */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Sales vs Purchases comparison bar */}
        <SectionCard>
          <CardHeader
            icon={ShoppingCartIcon}
            iconStyle={{ bg: themeColors.primaryLight, color: themeColors.primary }}
            title="Sales vs Purchases"
            subtitle="Total comparison for the period"
          />
          <div className="p-5">
            <div className="h-52">
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <BarChart data={[
                  { name: 'Sales', value: cards.sales },
                  { name: 'Purchases', value: cards.purchases },
                ]} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" opacity={0.12} stroke="#94a3b8" vertical={false} />
                  <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} />
                  <YAxis
                    tick={{ fontSize: 10, fill: '#94a3b8' }}
                    axisLine={false}
                    tickLine={false}
                    width={52}
                    tickFormatter={(val) => `Rs ${fmtCompact(val)}`}
                  />
                  <Tooltip
                    contentStyle={tooltipStyle(isDark)}
                    formatter={(val) => [`Rs ${fmtCurrency(val)}`, ""]}
                    cursor={{ fill: 'rgba(148,163,184,0.06)' }}
                  />
                  <Bar dataKey="value" radius={[10, 10, 0, 0]} maxBarSize={56}>
                    <Cell fill={themeColors.primary} />
                    <Cell fill={themeColors.secondary} />
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </SectionCard>

{/* Returns trend */}
        <SectionCard>
          <CardHeader
            icon={ArrowUturnLeftIcon}
            iconStyle={{ bg: themeColors.tertiaryLight, color: themeColors.tertiary }}
            title="Returns Trend"
            subtitle="Sale vs purchase returns"
          />
          <div className="p-5">
            <div className="h-52">
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <AreaChart
                  data={mergeTwo(series.saleReturns, series.purchaseReturns)}
                  margin={{ top: 8, right: 8, left: 0, bottom: 0 }}
                >
                  <defs>
                    <linearGradient id="retArea" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor={themeColors.tertiary} stopOpacity={0.45} />
                      <stop offset="100%" stopColor={themeColors.tertiary} stopOpacity={0.03} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" opacity={0.12} stroke="#94a3b8" vertical={false} />
                  <XAxis
                    dataKey="date"
                    tick={{ fontSize: 10, fill: '#94a3b8' }}
                    axisLine={false}
                    tickLine={false}
                    minTickGap={24}
                    tickFormatter={(val) => val.slice(5)}
                  />
                  <YAxis
                    tick={{ fontSize: 10, fill: '#94a3b8' }}
                    axisLine={false}
                    tickLine={false}
                    width={50}
                    tickFormatter={(val) => `Rs ${fmtCompact(val)}`}
                  />
                  <Tooltip
                    contentStyle={tooltipStyle(isDark)}
                    formatter={(val, name) => [`Rs ${fmtCurrency(val)}`, name === 'a' ? 'Sale Returns' : 'Purchase Returns']}
                    labelFormatter={(label) => `Date: ${label}`}
                    cursor={{ stroke: themeColors.tertiary, strokeOpacity: 0.25 }}
                  />
                  <Legend
                    iconType="circle"
                    iconSize={7}
                    wrapperStyle={{ paddingTop: '10px', fontSize: '12px' }}
                    formatter={(value) => <span className="text-xs text-slate-500 dark:text-slate-400">{value}</span>}
                  />
                  <Area
                    type="monotone"
                    dataKey="a"
                    name="Sale Returns"
                    stroke={themeColors.tertiary}
                    strokeWidth={2}
                    fill="url(#retArea)"
                    dot={false}
                  />
                  <Area
                    type="monotone"
                    dataKey="b"
                    name="Purchase Returns"
                    stroke={themeColors.secondary}
                    strokeWidth={2}
                    strokeDasharray="4 3"
                    fill="transparent"
                    dot={false}
                  />
                </AreaChart>
              </ResponsiveContainer>
            </div>
          </div>
        </SectionCard>

{/* Top selling brands */}
        <SectionCard>
          <CardHeader
            icon={TrophyIcon}
            iconStyle={{ bg: '#fef3c7', color: '#d97706' }}
            title="Top Brands"
            subtitle="Revenue by brand this period"
          />
          <div className="pt-1">
            {Array.isArray(brandSales) && brandSales.length > 0 ? (
              <ul className="divide-y divide-slate-100 dark:divide-slate-700/50">
                {brandSales.map((b, i) => (
                  <RankedRow
                    key={`${b.brand}-${i}`}
                    rank={i + 1}
                    title={String(b.brand || "Unknown")}
                    amount={`Rs ${fmtCompact(b.revenue)}`}
                    pct={((Number(b.revenue) || 0) / maxBrandSales) * 100}
                    color={themeColors.primary}
                  />
                ))}
              </ul>
            ) : (
              <div className="flex flex-col items-center justify-center py-10 text-center gap-2">
                <TrophyIcon className="w-8 h-8 text-slate-200 dark:text-slate-700" />
                <p className="text-sm text-slate-400 dark:text-slate-500">No brand sales in this period</p>
              </div>
            )}
          </div>
        </SectionCard>
      </div>

{/* ===== Top Products + Period Snapshot ===== */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Top Selling Products */}
        <SectionCard className="lg:col-span-2">
          <CardHeader
            icon={FireIcon}
            iconStyle={{ bg: '#fde8e8', color: '#ef4444' }}
            title="Top Selling Products"
            subtitle="Best sellers by quantity in the period"
          />
          <div className="pt-1 overflow-hidden">
            {Array.isArray(topProducts) && topProducts.length > 0 ? (
              <ul className="divide-y divide-slate-100 dark:divide-slate-700/50 max-h-80 overflow-y-auto">
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
                <FireIcon className="w-8 h-8 text-slate-200 dark:text-slate-700" />
                <p className="text-sm text-slate-400 dark:text-slate-500">No sales in this period</p>
              </div>
            )}
          </div>
        </SectionCard>

        {/* Period Snapshot */}
        <SectionCard>
          <CardHeader
            icon={SparklesIcon}
            iconStyle={{ bg: themeColors.secondaryLight, color: themeColors.secondary }}
            title="Period Snapshot"
            subtitle="Quick summary of this period"
          />
          <div className="p-5 space-y-4">
            {[
              { label: "Total Sales", value: `Rs ${fmtCurrency(cards.sales)}`, icon: CurrencyDollarIcon, color: themeColors.primary, bg: themeColors.primaryLight },
              { label: "Total Purchases", value: `Rs ${fmtCurrency(cards.purchases)}`, icon: ShoppingCartIcon, color: themeColors.secondary, bg: themeColors.secondaryLight },
              { label: "Net Sales", value: `Rs ${fmtCurrency(netSales)}`, icon: BanknotesIcon, color: themeColors.tertiary, bg: themeColors.tertiaryLight },
              { label: "Sale Returns", value: `Rs ${fmtCurrency(cards.saleReturns)}`, icon: ArrowUturnLeftIcon, color: '#f43f5e', bg: '#ffe4e6' },
              { label: "Purchase Returns", value: `Rs ${fmtCurrency(cards.purchaseReturns)}`, icon: ArrowUturnLeftIcon, color: '#f59e0b', bg: '#fef3c7' },
            ].map((row) => (
              <div key={row.label} className="flex items-center justify-between gap-3">
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="p-2 rounded-lg shrink-0" style={{ backgroundColor: row.bg }}>
                    <row.icon className="w-4 h-4" style={{ color: row.color }} />
                  </div>
                  <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">{row.label}</span>
                </div>
                <span className="text-sm font-bold text-slate-800 dark:text-slate-100 tabular-nums truncate">{row.value}</span>
              </div>
            ))}
            <div className="pt-3 border-t border-slate-100 dark:border-slate-700/60 grid grid-cols-2 gap-3">
              <div className="rounded-xl bg-slate-50 dark:bg-slate-700/40 p-3 text-center">
                <div className="text-base font-extrabold text-slate-900 dark:text-white tabular-nums">{invoiceCounts.total || 0}</div>
                <div className="text-[10px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">Invoices</div>
              </div>
              <div className="rounded-xl bg-slate-50 dark:bg-slate-700/40 p-3 text-center">
                <div className="text-base font-extrabold text-slate-900 dark:text-white tabular-nums">{kpiMetrics.active_products || 0}</div>
                <div className="text-[10px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">Products</div>
              </div>
            </div>
          </div>
        </SectionCard>
      </div>

{/* ===== Near Expiry ===== */}
      <SectionCard>
        <CardHeader
          icon={ClockIcon}
          iconStyle={{ bg: themeColors.tertiaryLight, color: themeColors.tertiary }}
          title="Near Expiry Products"
          subtitle="Stock that will expire soon"
          right={
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
                  className={`px-2.5 py-1 text-xs font-semibold rounded-lg transition-all duration-200 ${
                    expiryMonths === opt.m
                      ? "text-white shadow-xs"
                      : "text-slate-500 dark:text-slate-400 bg-slate-100 dark:bg-slate-700/50 hover:bg-slate-200 dark:hover:bg-slate-700"
                  }`}
                  style={expiryMonths === opt.m ? { background: `linear-gradient(135deg, ${themeColors.tertiary}, ${themeColors.tertiaryHover})` } : {}}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          }
        />

        <div className="flex flex-col lg:flex-row lg:items-center gap-3 px-5 py-3 border-b border-slate-100 dark:border-slate-700/60">
          <div className="flex items-center gap-2 shrink-0">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Supplier</span>
            <div className="w-44 relative z-50">
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
          <div className="flex items-center gap-2 shrink-0">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Brand</span>
            <div className="w-44 relative z-50">
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
              onClick={() => {
                setSupplierValue(null);
                setBrandValue(null);
                setSupplierId("");
                setBrandId("");
              }}
              className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-slate-100 dark:bg-slate-700/50 text-slate-500 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700 transition-all duration-200"
            >
              Clear Filters
            </button>
            <button
              onClick={fetchNearExpiry}
              disabled={loadingExpiry}
              className="inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold text-white rounded-lg shadow-xs hover:shadow-md active:scale-95 transition-all duration-200 disabled:opacity-60"
              style={{ background: `linear-gradient(135deg, ${themeColors.tertiary}, ${themeColors.tertiaryHover})` }}
            >
              <ArrowPathIcon className={`w-3.5 h-3.5 ${loadingExpiry ? "animate-spin" : ""}`} />
              {loadingExpiry ? "Loading…" : "Refresh"}
            </button>
          </div>
        </div>

{/* Expiry table */}
        <div className="overflow-auto max-h-96">
          <table className="min-w-full text-sm">
            <thead className="sticky top-0 bg-slate-50 dark:bg-slate-800 z-10 border-b border-slate-200 dark:border-slate-700">
              <tr className="text-left">
                <th className="px-5 py-3 font-semibold text-[11px] uppercase tracking-wider text-slate-500 dark:text-slate-400">Product</th>
                <th className="px-5 py-3 font-semibold text-[11px] uppercase tracking-wider text-slate-500 dark:text-slate-400">Supplier</th>
                <th className="hidden md:table-cell px-5 py-3 font-semibold text-[11px] uppercase tracking-wider text-slate-500 dark:text-slate-400">Brand</th>
                <th className="hidden sm:table-cell px-5 py-3 font-semibold text-[11px] uppercase tracking-wider text-slate-500 dark:text-slate-400">Batch #</th>
                <th className="px-5 py-3 font-semibold text-[11px] uppercase tracking-wider text-slate-500 dark:text-slate-400">Expiry</th>
                <th className="px-5 py-3 font-semibold text-[11px] uppercase tracking-wider text-slate-500 dark:text-slate-400">Days Left</th>
                <th className="px-5 py-3 font-semibold text-[11px] uppercase tracking-wider text-slate-500 dark:text-slate-400 text-right">Qty</th>
              </tr>
            </thead>
            <tbody>
              {nearExpiryRows.length === 0 ? (
                <tr>
                  <td className="px-5 py-10 text-center text-slate-400 dark:text-slate-500" colSpan={7}>
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
                nearExpiryRows.map((r) => {
                  const daysLeft = daysLeftOf(r.expiry_date);
                  return (
                    <tr
                      key={`b-${r.batch_id}`}
                      className="border-b border-slate-100 dark:border-slate-700/50 hover:bg-slate-50 dark:hover:bg-slate-700/40 transition-colors"
                    >
                      <td className="px-5 py-3 text-slate-800 dark:text-slate-100">
                        <div className="max-w-[260px] truncate font-medium" title={r.product_name}>{r.product_name}</div>
                        <div className="text-[11px] text-slate-400 dark:text-slate-500 font-mono">{r.product_code}</div>
                      </td>
                      <td className="px-5 py-3 text-slate-600 dark:text-slate-300">{(r.supplier_name || "—")}</td>
                      <td className="hidden md:table-cell px-5 py-3 text-slate-600 dark:text-slate-300">{(r.brand_name || "—")}</td>
                      <td className="hidden sm:table-cell px-5 py-3 text-slate-600 dark:text-slate-300 font-mono text-xs">{r.batch_number}</td>
                      <td className="px-5 py-3 text-slate-600 dark:text-slate-300">{String(r.expiry_date || "").slice(0, 10)}</td>
                      <td className="px-5 py-3">
                        {daysLeft != null ? <ExpiryPill days={daysLeft} /> : <span className="text-slate-400">—</span>}
                      </td>
                      <td className="px-5 py-3 text-right text-slate-800 dark:text-slate-100 font-semibold tabular-nums">
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
  );
}