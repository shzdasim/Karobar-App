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

  // Get dark mode state
  const { isDark } = useTheme();

  // 🎨 Modern button palette (matching SupplierLedger design)
  const tintBlue   = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] hover:from-blue-600 hover:to-blue-700 active:scale-[0.98] transition-all duration-200";
  const tintIndigo = "bg-gradient-to-br from-indigo-500 to-indigo-600 text-white shadow-lg shadow-indigo-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-indigo-500/30 hover:scale-[1.02] hover:from-indigo-600 hover:to-indigo-700 active:scale-[0.98] transition-all duration-200";
  const tintGlass  = "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";
  const tintOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 hover:shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";

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
    setProductId("");
    setProductValue(null);
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
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.core.gradient} shadow-sm`}>
              <DocumentTextIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Purchase Detail Report</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{data.length} entries</p>
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
                setProductId("");
                setProductValue(null);
              }}
              styles={getSmallSelectStyles(isDark)}
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
              styles={getSmallSelectStyles(isDark)}
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
              className={`h-9 ${tintGlass}`}
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 1);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
              }}
            >
              Today
            </GlassBtn>

            <GlassBtn
              className={`h-9 ${tintGlass}`}
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 3);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
              }}
            >
              3 Days
            </GlassBtn>

            <GlassBtn
              className={`h-9 ${tintGlass}`}
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(end.getDate() - 7);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
              }}
            >
              7 Days
            </GlassBtn>

            <Guard when={can.export}>
              <GlassBtn
                className={`h-9 ${tintIndigo}`}
                onClick={exportPdf}
                disabled={pdfLoading || data.length === 0}
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
        {data.map((inv) => (
          <div
            key={inv.id || `${inv.posted_number}-${inv.invoice_number}-${inv.invoice_date}`}
            className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden"
          >
            {/* Table Header (matching Products page style) */}
            <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
              <div className="flex items-center gap-2">
                <div className={`p-1 rounded ${SECTION_CONFIG.core.bgDark}`}>
                  <Squares2X2Icon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
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

