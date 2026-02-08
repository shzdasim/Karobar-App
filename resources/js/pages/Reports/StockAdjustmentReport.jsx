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

// Section configuration with color schemes - matching sidebar design
const SECTION_CONFIG = {
  core: {
    gradient: "from-blue-500 to-cyan-600",
    bgLight: "bg-blue-50",
    bgDark: "dark:bg-blue-900/20",
    borderColor: "border-blue-200 dark:border-blue-700",
    iconColor: "text-blue-600 dark:text-blue-400",
    ringColor: "ring-blue-300 dark:ring-blue-700",
  },
  management: {
    gradient: "from-violet-500 to-purple-600",
    bgLight: "bg-violet-50",
    bgDark: "dark:bg-violet-900/20",
    borderColor: "border-violet-200 dark:border-violet-700",
    iconColor: "text-violet-600 dark:text-violet-400",
    ringColor: "ring-violet-300 dark:ring-violet-700",
  },
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

  // Get dark mode state
  const { isDark } = useTheme();

  // 🎨 Modern button palette (matching SupplierLedger design)
  const tintBlue   = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] hover:from-blue-600 hover:to-blue-700 active:scale-[0.98] transition-all duration-200";
  const tintIndigo = "bg-gradient-to-br from-indigo-500 to-indigo-600 text-white shadow-lg shadow-indigo-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-indigo-500/30 hover:scale-[1.02] hover:from-indigo-600 hover:to-indigo-700 active:scale-[0.98] transition-all duration-200";
  const tintGlass  = "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";
  const tintOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 hover:shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";

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
      {/* ===== Professional Header ===== */}
      <GlassCard>
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.core.gradient} shadow-sm`}>
              <DocumentTextIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Stock Adjustment Report</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{rows.length} adjustments</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <GlassBtn
              className={`h-9 ${tintOutline}`}
              onClick={resetFilters}
            >
              Reset
            </GlassBtn>
            <Guard when={can.view}>
              <GlassBtn
                className={`h-9 ${tintBlue}`}
                onClick={fetchReport}
                disabled={loading}
              >
                <span className="inline-flex items-center gap-2">
                  <ArrowPathIcon className={`w-5 h-5 ${loading ? "animate-spin" : ""}`} />
                  {loading ? "Loading…" : "Load"}
                </span>
              </GlassBtn>
            </Guard>
          </div>
        </div>

        {/* Filters */}
        <GlassToolbar className="grid grid-cols-1 md:grid-cols-12 gap-3">
          {/* From Date */}
          <div className="md:col-span-3">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>From Date</label>
            <GlassInput
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="w-full"
            />
          </div>

          {/* To Date */}
          <div className="md:col-span-3">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>To Date</label>
            <GlassInput
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="w-full"
            />
          </div>

          {/* Buttons */}
          <div className="md:col-span-6 flex flex-wrap gap-2 items-end">
            <Guard when={can.export}>
              <GlassBtn
                className={`h-9 ${tintIndigo}`}
                onClick={exportPdf}
                disabled={pdfLoading || rows.length === 0}
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
          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
            {/* Table Header */}
            <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
              <div className="flex items-center gap-2">
                <div className={`p-1 rounded ${SECTION_CONFIG.core.bgDark}`}>
                  <Squares2X2Icon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                </div>
                <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Stock Adjustments</span>
              </div>
              <span className="text-xs text-gray-400">{rows.length} adjustments</span>
            </div>

            <div className="max-h-[75vh] overflow-auto">
              <table className="min-w-[1400px] w-full text-sm">
                <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
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
                  border-t-2 backdrop-blur-sm font-semibold
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
      rounded-xl px-3 py-2 backdrop-blur-sm ring-1 shadow-sm
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
