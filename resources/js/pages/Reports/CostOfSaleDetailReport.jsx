// resources/js/pages/Reports/CostOfSaleDetailReport.jsx
import { useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions";

import {
  GlassCard,
  GlassToolbar,
  GlassBtn,
} from "@/components/glass.jsx";
import { useTheme } from "@/context/ThemeContext";

// Search modal
import SaleInvoiceSearch from "@/components/SaleInvoiceSearch.jsx";

import { 
  ArrowDownOnSquareIcon, 
  DocumentTextIcon,
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

/* ======================
   Helpers
   ====================== */
const todayStr = () => new Date().toISOString().split("T")[0];
const n = (v) => (isFinite(Number(v)) ? Number(v) : 0);
const fmtCurrency = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export default function CostOfSaleDetailReport() {
  // Default date range: Current month
  const [fromDate, setFromDate] = useState(() => {
    const d = new Date();
    d.setDate(1);
    return d.toISOString().split("T")[0];
  });
  const [toDate, setToDate] = useState(todayStr());

  // Search modal state
  const [searchOpen, setSearchOpen] = useState(false);
  const [selectedInvoice, setSelectedInvoice] = useState(null);

  // Data + States
  const [data, setData] = useState({ invoices: [], summary: {} });
  const [loading, setLoading] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("cost-of-sale-report") : {
        view: false,
        export: false,
      }),
    [canFor]
  );

  // Get dark mode state and theme colors
  const { isDark, theme } = useTheme();

  // Memoize theme colors for performance
  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#3b82f6',
        primaryHover: '#2563eb',
        primaryLight: '#dbeafe',
        secondary: '#8b5cf6',
        secondaryHover: '#7c3aed',
        tertiary: '#06b6d4',
        emerald: '#10b981',
        emeraldHover: '#059669',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      tertiary: theme.tertiary_color || '#06b6d4',
      emerald: theme.success_color || '#10b981',
      emeraldHover: '#059669',
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
  const btnEmerald = getButtonClasses.emerald;

  // Handle invoice selection from search modal
  const handleInvoiceSelect = (invoice) => {
    setSelectedInvoice(invoice);
    setSearchOpen(false);
  };

  /* ============ Fetch report ============ */
  const fetchReport = async () => {
    if (!can.view) return toast.error("You don't have permission to view this report.");
    if (fromDate > toDate) return toast.error("'From' date cannot be after 'To' date.");

    setLoading(true);
    try {
      const res = await axios.get("/api/reports/cost-of-sale-detail", {
        params: {
          invoice_id: selectedInvoice?.id || undefined,
          from: fromDate || undefined,
          to: toDate || undefined,
        },
      });
      
      const responseData = res.data || { invoices: [], summary: {} };
      setData(responseData);
      
      if (!responseData.invoices?.length) {
        toast("No data found for selected filters.", { icon: "ℹ️" });
      }
    } catch (err) {
      console.error(err);
      toast.error("Failed to fetch Cost of Sale Detail report");
      setData({ invoices: [], summary: {} });
    } finally {
      setLoading(false);
    }
  };

  /* ============ Export PDF ============ */
  const exportPdf = async () => {
    if (!can.export) return toast.error("You don't have permission to export PDF.");
    if (!data.invoices?.length) return toast.error("No data to export.");
    
    setPdfLoading(true);
    try {
      const res = await axios.get("/api/reports/cost-of-sale-detail/pdf", {
        params: {
          invoice_id: selectedInvoice?.id || undefined,
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

  const resetFilters = () => {
    setFromDate(() => {
      const d = new Date();
      d.setDate(1);
      return d.toISOString().split("T")[0];
    });
    setToDate(todayStr());
    setSelectedInvoice(null);
    setData({ invoices: [], summary: {} });
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

  const { invoices, summary } = data;

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
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Cost of Sale Detail Report</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">
                {invoices.length} invoice(s) • {summary.total_items || 0} items
              </p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <GlassBtn
              className={`h-9 ${btnSecondary.className}`}
              onClick={resetFilters}
              style={btnSecondary.style}
            >
              Reset
            </GlassBtn>
            <Guard when={can.view}>
              <GlassBtn
                className={`h-9 ${btnPrimary.className}`}
                onClick={fetchReport}
                disabled={loading}
                style={btnPrimary.style}
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
            <input
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="w-full h-9 px-3 rounded-lg border border-gray-300 dark:border-slate-600 dark:bg-slate-700 dark:text-white text-sm"
            />
          </div>

          {/* To Date */}
          <div className="md:col-span-2">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>To</label>
            <input
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="w-full h-9 px-3 rounded-lg border border-gray-300 dark:border-slate-600 dark:bg-slate-700 dark:text-white text-sm"
            />
          </div>

          {/* Invoice Selector Button */}
          <div className="md:col-span-4">
            <label className={`text-sm mb-1 block ${isDark ? "text-slate-300" : "text-gray-700"}`}>
              Select Invoice
            </label>
            <button
              onClick={() => setSearchOpen(true)}
              className={`w-full h-9 px-3 rounded-lg border text-left text-sm flex items-center gap-2 transition-all
                ${selectedInvoice 
                  ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-200' 
                  : 'border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-700 text-gray-500 dark:text-gray-400 hover:border-blue-400'
                }`}
            >
              <DocumentTextIcon className="w-5 h-5" />
              {selectedInvoice ? (
                <span className="truncate">
                  {selectedInvoice.posted_number || `#${selectedInvoice.id}`} - {selectedInvoice.customer_name || 'Walk-in'}
                </span>
              ) : (
                <span>Click to search invoice...</span>
              )}
            </button>
          </div>

          {/* Quick Filters & Export */}
          <div className="md:col-span-4 flex flex-wrap items-end gap-2">
            <GlassBtn
              className={`h-9 ${btnEmerald.className}`}
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setDate(1);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
                fetchReport();
              }}
              style={btnEmerald.style}
            >
              This Month
            </GlassBtn>

            <GlassBtn
              className={`h-9 ${btnSecondary.className}`}
              onClick={() => {
                const end = new Date();
                const start = new Date();
                start.setMonth(start.getMonth() - 1);
                start.setDate(1);
                setFromDate(start.toISOString().slice(0, 10));
                setToDate(end.toISOString().slice(0, 10));
                fetchReport();
              }}
              style={btnSecondary.style}
            >
              Last Month
            </GlassBtn>

            <Guard when={can.export}>
              <GlassBtn
                className={`h-9 ${btnSecondary.className}`}
                onClick={exportPdf}
                disabled={pdfLoading || invoices.length === 0}
                style={btnSecondary.style}
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

      {/* ===== Summary Section ===== */}
      {invoices.length > 0 && (
        <GlassCard>
          <div className="px-4 py-3">
            <h3 className={`text-sm font-semibold mb-3 ${isDark ? "text-slate-200" : "text-gray-700"}`}>
              📊 Financial Summary
            </h3>
            <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
              <div className={`p-3 rounded-lg ${isDark ? "bg-slate-700" : "bg-blue-50"}`}>
                <div className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>Total Invoices</div>
                <div className={`text-lg font-bold ${isDark ? "text-white" : "text-gray-900"}`}>
                  {summary.total_invoices || 0}
                </div>
              </div>
              <div className={`p-3 rounded-lg ${isDark ? "bg-slate-700" : "bg-emerald-50"}`}>
                <div className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>Total Sales</div>
                <div className={`text-lg font-bold ${isDark ? "text-white" : "text-gray-900"}`}>
                  Rs. {fmtCurrency(summary.total_sales || 0)}
                </div>
              </div>
              <div className={`p-3 rounded-lg ${isDark ? "bg-slate-700" : "bg-red-50"}`}>
                <div className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>Total Cost</div>
                <div className={`text-lg font-bold ${isDark ? "text-white" : "text-gray-900"}`}>
                  Rs. {fmtCurrency(summary.total_cost || 0)}
                </div>
              </div>
              <div className={`p-3 rounded-lg ${isDark ? "bg-slate-700" : "bg-green-50"}`}>
                <div className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>Total Profit</div>
                <div className={`text-lg font-bold ${(summary.total_profit || 0) >= 0 ? "text-green-600" : "text-red-600"}`}>
                  Rs. {fmtCurrency(summary.total_profit || 0)}
                </div>
              </div>
              <div className={`p-3 rounded-lg ${isDark ? "bg-slate-700" : "bg-purple-50"}`}>
                <div className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>Profit Margin</div>
                <div className={`text-lg font-bold ${isDark ? "text-white" : "text-gray-900"}`}>
                  {summary.profit_margin || 0}%
                </div>
              </div>
            </div>
          </div>
        </GlassCard>
      )}

      {/* ===== No Data ===== */}
      {invoices.length === 0 && !loading && (
        <GlassCard>
          <div className={`px-4 py-8 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>
            <DocumentTextIcon className="w-12 h-12 mx-auto mb-2 opacity-50" />
            <p>No data found. Select an invoice or date range and click Load.</p>
          </div>
        </GlassCard>
      )}

      {/* ===== Invoice Cards ===== */}
      <div className="flex flex-col gap-4">
        {invoices.map((inv, idxInv) => (
          <div
            key={idxInv + "-" + (inv.invoice_id ?? inv.posted_number ?? "")}
            className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden"
          >
            {/* Invoice Header */}
            <div className="flex items-center justify-between px-4 py-3 bg-gradient-to-r from-blue-50 to-indigo-50 dark:from-slate-700 dark:to-slate-600 border-b border-gray-200 dark:border-slate-600">
              <div className="flex items-center gap-4">
                <div 
                  className="px-3 py-1 rounded-full text-white text-sm font-bold"
                  style={{ background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})` }}
                >
                  {inv.posted_number || `INV-${inv.invoice_id}`}
                </div>
                <div className={`text-sm ${isDark ? "text-slate-300" : "text-gray-600"}`}>
                  <span className="font-medium">{inv.date || '-'}</span>
                </div>
                <div className={`text-sm ${isDark ? "text-slate-300" : "text-gray-600"}`}>
                  Customer: <span className="font-medium">{inv.customer_name || 'WALK-IN-CUSTOMER'}</span>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className={`px-2 py-1 rounded text-xs font-medium ${
                  inv.invoice_type === 'credit' 
                    ? 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200'
                    : 'bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200'
                }`}>
                  {inv.invoice_type?.toUpperCase() || 'DEBIT'}
                </span>
                <span className={`px-2 py-1 rounded text-xs font-medium ${
                  inv.sale_type === 'wholesale'
                    ? 'bg-purple-100 text-purple-800 dark:bg-purple-900 dark:text-purple-200'
                    : 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200'
                }`}>
                  {inv.sale_type?.toUpperCase() || 'RETAIL'}
                </span>
              </div>
            </div>

            {/* Items Table */}
            <div className="relative max-w-full overflow-auto">
              <table className="w-full min-w-[900px] text-sm">
                <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
                  <tr className="text-left bg-gray-50 dark:bg-slate-700">
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"}`}>#</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"}`}>Product Name</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"}`}>Code</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"} text-right`}>Pack Size</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"} text-right`}>Qty</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"} text-right`}>Cost Price</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"} text-right`}>Sale Price</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"} text-right`}>Disc %</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"} text-right`}>Total Cost</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"} text-right`}>Total Sale</th>
                    <th className={`px-3 py-2 font-semibold text-xs uppercase ${isDark ? "text-slate-200" : "text-gray-600"} text-right`}>Profit</th>
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
                      <td className={`px-3 py-2 ${isDark ? "text-slate-300" : "text-gray-700"}`}>{idx + 1}</td>
                      <td className={`px-3 py-2 font-medium ${isDark ? "text-slate-200" : "text-gray-800"}`}>{it.product_name || "-"}</td>
                      <td className={`px-3 py-2 ${isDark ? "text-slate-400" : "text-gray-500"}`}>{it.product_code || "-"}</td>
                      <td className={`px-3 py-2 text-right ${isDark ? "text-slate-300" : "text-gray-700"}`}>{it.pack_size ?? 0}</td>
                      <td className={`px-3 py-2 text-right ${isDark ? "text-slate-300" : "text-gray-700"}`}>{it.quantity ?? 0}</td>
                      <td className={`px-3 py-2 text-right ${isDark ? "text-slate-300" : "text-gray-700"}`}>Rs. {fmtCurrency(it.cost_price)}</td>
                      <td className={`px-3 py-2 text-right ${isDark ? "text-slate-300" : "text-gray-700"}`}>Rs. {fmtCurrency(it.sale_price)}</td>
                      <td className={`px-3 py-2 text-right ${(it.item_discount_percentage ?? 0) > 0 ? "text-red-500 font-medium" : isDark ? "text-slate-300" : "text-gray-700"}`}>
                        {(it.item_discount_percentage ?? 0) > 0 ? `${it.item_discount_percentage}%` : '-'}
                      </td>
                      <td className={`px-3 py-2 text-right ${isDark ? "text-slate-300" : "text-gray-700"}`}>Rs. {fmtCurrency(it.total_cost)}</td>
                      <td className={`px-3 py-2 text-right ${isDark ? "text-slate-300" : "text-gray-700"}`}>Rs. {fmtCurrency(it.total_sale)}</td>
                      <td className={`px-3 py-2 text-right font-bold ${(it.profit ?? 0) >= 0 ? "text-green-600" : "text-red-600"}`}>
                        Rs. {fmtCurrency(it.profit)}
                      </td>
                    </tr>
                  ))}

                  {(!inv.items || !inv.items.length) && (
                    <tr>
                      <td colSpan={11} className={`px-3 py-6 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                        No items in this invoice.
                      </td>
                    </tr>
                  )}
                </tbody>

                <tfoot className={`
                  border-t-2 backdrop-blur-sm font-semibold
                  ${isDark ? "border-slate-600 bg-slate-800/80" : "border-gray-300 bg-gray-50"}
                `}>
                  <tr className={isDark ? "bg-slate-700" : "bg-gray-100"}>
                    <td colSpan={8} className={`px-3 py-2 text-right ${isDark ? "text-slate-200" : "text-gray-800"}`}><strong>TOTAL</strong></td>
                    <td className={`px-3 py-2 text-right ${isDark ? "text-slate-200" : "text-gray-800"}`}><strong>Rs. {fmtCurrency(inv.total_cost)}</strong></td>
                    <td className={`px-3 py-2 text-right ${isDark ? "text-slate-200" : "text-gray-800"}`}><strong>Rs. {fmtCurrency(inv.total_sale)}</strong></td>
                    <td className={`px-3 py-2 text-right ${(inv.profit ?? 0) >= 0 ? "text-green-600" : "text-red-600"}`}><strong>Rs. {fmtCurrency(inv.profit)}</strong></td>
                  </tr>
                </tfoot>
              </table>
            </div>

            {/* Invoice Footer Summary */}
            <div className={`px-4 py-3 border-t ${isDark ? "border-slate-600 bg-slate-700/50" : "border-gray-200 bg-gray-50"}`}>
              <div className="flex flex-wrap justify-end gap-x-6 gap-y-1 text-sm">
                <div>
                  <span className={`${isDark ? "text-slate-400" : "text-gray-500"}`}>Gross Amount: </span>
                  <span className={`font-medium ${isDark ? "text-slate-200" : "text-gray-800"}`}>Rs. {fmtCurrency(inv.gross_amount)}</span>
                </div>
                {(inv.discount_percentage > 0 || inv.discount_amount > 0) && (
                  <div>
                    <span className={`${isDark ? "text-slate-400" : "text-gray-500"}`}>Disc {inv.discount_percentage ? `(${inv.discount_percentage}%)` : ''}: </span>
                    <span className={`font-medium text-red-500`}>-Rs. {fmtCurrency(inv.discount_amount)}</span>
                  </div>
                )}
                {(inv.tax_percentage > 0 || inv.tax_amount > 0) && (
                  <div>
                    <span className={`${isDark ? "text-slate-400" : "text-gray-500"}`}>Tax {inv.tax_percentage ? `(${inv.tax_percentage}%)` : ''}: </span>
                    <span className={`font-medium ${isDark ? "text-slate-200" : "text-gray-800"}`}>+Rs. {fmtCurrency(inv.tax_amount)}</span>
                  </div>
                )}
                <div>
                  <span className={`${isDark ? "text-slate-400" : "text-gray-500"}`}>Invoice Total: </span>
                  <span className={`font-bold text-lg ${isDark ? "text-white" : "text-gray-900"}`}>Rs. {fmtCurrency(inv.total)}</span>
                </div>
                <div>
                  <span className={`${isDark ? "text-slate-400" : "text-gray-500"}`}>Cost: </span>
                  <span className={`font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>Rs. {fmtCurrency(inv.total_cost)}</span>
                </div>
                <div>
                  <span className={`${isDark ? "text-slate-400" : "text-gray-500"}`}>Profit: </span>
                  <span className={`font-bold ${(inv.profit ?? 0) >= 0 ? "text-green-600" : "text-red-600"}`}>Rs. {fmtCurrency(inv.profit)}</span>
                  <span className={`ml-2 text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>({inv.profit_margin || 0}%)</span>
                </div>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* ===== Search Invoice Modal ===== */}
      <SaleInvoiceSearch 
        isOpen={searchOpen} 
        onClose={() => setSearchOpen(false)} 
        onSelect={handleInvoiceSelect}
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

