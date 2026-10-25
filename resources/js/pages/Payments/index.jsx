// resources/js/pages/Payments/index.jsx
// Dedicated Payment Vouchers register — receipt and disbursement register.
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import {
  BanknotesIcon,
  PlusCircleIcon,
  ArrowPathIcon,
  MagnifyingGlassIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  ChevronDoubleLeftIcon,
  ChevronDoubleRightIcon,
  EyeIcon,
  PrinterIcon,
  ArrowUturnLeftIcon,
  CheckCircleIcon,
  XMarkIcon,
  CalendarDaysIcon,
  ArrowTrendingUpIcon,
  ArrowTrendingDownIcon,
  DocumentTextIcon,
  ShareIcon,
} from "@heroicons/react/24/solid";
import { useTheme } from "@/context/ThemeContext";
import { useCurrency } from "@/context/CurrencyContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import { GlassCard, GlassSectionHeader, GlassInput, GlassToolbar } from "@/components";
import PaymentReceiptModal from "@/components/PaymentReceiptModal.jsx";
import RecordPaymentModal from "@/components/RecordPaymentModal.jsx";
import ShareDocumentModal from "@/components/ShareDocumentModal.jsx";
import CustomerSearch from "@/components/CustomerSearch.jsx";
import SupplierSearch from "@/components/SupplierSearch.jsx";

// ── Lookup helpers ────────────────────────────────────────────────────────────
const PARTY_TYPE_OPTIONS = [
  { value: "", label: "All Parties" },
  { value: "customer", label: "Customer Receipts" },
  { value: "supplier", label: "Supplier Payments" },
];

const STATUS_OPTIONS = [
  { value: "", label: "All Status" },
  { value: "posted", label: "Posted" },
  { value: "reversed", label: "Reversed" },
];

const METHOD_LABELS = {
  cash: "Cash",
  bank_transfer: "Bank Transfer",
  card: "Card / POS",
  mobile_wallet: "Mobile Wallet",
  cheque: "Cheque",
  other: "Other",
};

function fmtDate(d) {
  if (!d) return "—";
  return new Date(d).toLocaleDateString("en-PK", { day: "2-digit", month: "short", year: "numeric" });
}

// ── Main Component ────────────────────────────────────────────────────────────
export default function PaymentsPage() {
  const { isDark, theme } = useTheme();
  const { formatCurrency } = useCurrency();

  // Theme buttons
  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || "#3b82f6",
    primaryHover: theme?.primary_hover || "#2563eb",
    secondary: theme?.secondary_color || "#8b5cf6",
    secondaryHover: theme?.secondary_hover || "#7c3aed",
    tertiary: theme?.tertiary_color || "#06b6d4",
    tertiaryHover: theme?.tertiary_hover || "#0891b2",
    danger: theme?.danger_color || "#ef4444",
    dangerHover: "#dc2626",
  }), [theme]);

  const getButtonClasses = useMemo(
    () => getThemeButtonClasses(theme?.button_style || "modern", themeColors, isDark),
    [theme, themeColors, isDark]
  );
  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;

  const selectStyles = useMemo(() => ({
    control: (base) => ({
      ...base,
      backgroundColor: isDark ? "#1e293b" : "#ffffff",
      borderColor: isDark ? "#475569" : "#d1d5db",
      color: isDark ? "#e2e8f0" : "#111827",
      minHeight: "36px",
      boxShadow: "none",
    }),
    singleValue: (base) => ({ ...base, color: isDark ? "#e2e8f0" : "#111827" }),
    menu: (base) => ({ ...base, backgroundColor: isDark ? "#1e293b" : "#ffffff", zIndex: 50 }),
    option: (base, state) => ({
      ...base,
      backgroundColor: state.isFocused ? (isDark ? "#334155" : "#f1f5f9") : "transparent",
      color: isDark ? "#e2e8f0" : "#111827",
    }),
    input: (base) => ({ ...base, color: isDark ? "#e2e8f0" : "#111827" }),
    placeholder: (base) => ({ ...base, color: isDark ? "#64748b" : "#9ca3af" }),
  }), [isDark]);

  // ── State ─────────────────────────────────────────────────────────────────
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(15);
  const [total, setTotal] = useState(0);
  const [sharingPaymentId, setSharingPaymentId] = useState(null);

  // Filters
  const [q, setQ] = useState("");
  const [partyType, setPartyType] = useState("");
  const [status, setStatus] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");

  // Modals
  const [receiptPayment, setReceiptPayment] = useState(null); // payment object for receipt modal
  // Party picker step: user picks type+party, then RecordPaymentModal opens
  const [showPartyPicker, setShowPartyPicker] = useState(false);
  const [pickerPartyType, setPickerPartyType] = useState("customer");
  const [pickerPartyId, setPickerPartyId] = useState(null);
  const [pickerPartyName, setPickerPartyName] = useState("");
  const [showCustomerSearch, setShowCustomerSearch] = useState(false);
  const [showSupplierSearch, setShowSupplierSearch] = useState(false);
  const [recordPartyId, setRecordPartyId] = useState(null);
  const [recordPartyName, setRecordPartyName] = useState("");
  const [recordPartyType, setRecordPartyType] = useState("customer");
  const [showRecordModal, setShowRecordModal] = useState(false);
  const [reversingPayment, setReversingPayment] = useState(null);
  const [reversalReason, setReversalReason] = useState("");
  const [reversing, setReversing] = useState(false);

  const controllerRef = useRef(null);

  // ── Fetch Payments ─────────────────────────────────────────────────────────
  const fetchPayments = useCallback(() => {
    if (controllerRef.current) controllerRef.current.abort();
    const ctrl = new AbortController();
    controllerRef.current = ctrl;
    setLoading(true);

    axios.get("/api/payments", {
      params: {
        search: q || undefined,
        party_type: partyType || undefined,
        status: status || undefined,
        from: fromDate || undefined,
        to: toDate || undefined,
        per_page: pageSize,
        page,
      },
      signal: ctrl.signal,
    })
      .then(({ data }) => {
        setRows(Array.isArray(data?.data) ? data.data : (Array.isArray(data) ? data : []));
        setTotal(data?.total || 0);
      })
      .catch(err => {
        if (axios.isCancel(err)) return;
        toast.error("Failed to load payments");
      })
      .finally(() => setLoading(false));
  }, [q, partyType, status, fromDate, toDate, page, pageSize]);

  useEffect(() => {
    fetchPayments();
    return () => controllerRef.current?.abort();
  }, [fetchPayments]);

  // Reset to page 1 when filters change
  useEffect(() => { setPage(1); }, [q, partyType, status, fromDate, toDate]);

  const pageCount = Math.max(1, Math.ceil(total / pageSize));

  // ── KPI aggregations ──────────────────────────────────────────────────────
  const kpi = useMemo(() => {
    const posted = rows.filter(r => r.status === "posted");
    const customerTotal = posted.filter(r => r.party_type === "customer").reduce((s, r) => s + parseFloat(r.amount || 0), 0);
    const supplierTotal = posted.filter(r => r.party_type === "supplier").reduce((s, r) => s + parseFloat(r.amount || 0), 0);
    const reversals = rows.filter(r => r.status === "reversed").length;
    return { customerTotal, supplierTotal, net: customerTotal - supplierTotal, reversals };
  }, [rows]);

  // ── Reversal ──────────────────────────────────────────────────────────────
  const handleConfirmReverse = async () => {
    if (!reversingPayment) return;
    if (reversalReason.trim().length < 3) { toast.error("Provide a reason (min 3 chars)"); return; }
    setReversing(true);
    try {
      await axios.post(`/api/payments/${reversingPayment.id}/reverse`, { reason: reversalReason.trim() });
      toast.success(`Payment ${reversingPayment.payment_number} reversed`);
      setReversingPayment(null);
      setReversalReason("");
      fetchPayments();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Reversal failed");
    } finally {
      setReversing(false);
    }
  };

  // ── Fetch single payment for receipt ──────────────────────────────────────
  const handleViewReceipt = async (payment) => {
    try {
      const { data } = await axios.get(`/api/payments/${payment.id}`);
      setReceiptPayment(data);
    } catch {
      toast.error("Failed to load payment details");
    }
  };

  const inputCls = `rounded-lg border px-3 py-1.5 text-sm focus:outline-none focus:ring-2 transition-colors ${
    isDark
      ? "bg-slate-800 border-slate-600 text-slate-100 focus:ring-blue-500/40 placeholder-slate-500"
      : "bg-white border-slate-300 text-slate-900 focus:ring-blue-500/40 placeholder-slate-400"
  }`;

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="space-y-5 px-1">
      {/* ── Page Header ────────────────────────────────────────────────── */}
      <GlassSectionHeader
        title="Payment Vouchers"
        right={
          <div className="flex items-center gap-2.5">
            <button
              onClick={fetchPayments}
              className={`flex items-center gap-2 px-4 py-2.5 text-sm font-medium ${btnSecondary.className}`}
              style={btnSecondary.style}
            >
              <ArrowPathIcon className="w-4 h-4" />
              Refresh
            </button>
            <button
              onClick={() => {
                setPickerPartyType("customer");
                setPickerPartyId(null);
                setPickerPartyName("");
                setShowPartyPicker(true);
              }}
              className={`flex items-center gap-2 px-4 py-2.5 text-sm font-semibold ${btnPrimary.className}`}
              style={btnPrimary.style}
            >
              <PlusCircleIcon className="w-4 h-4" />
              New Payment
            </button>
          </div>
        }
      />

      {/* ── KPI Cards ──────────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <GlassCard className="p-4">
          <div className="flex items-center gap-2 mb-1">
            <ArrowTrendingDownIcon className="w-4 h-4 text-emerald-500" />
            <p className="text-xs uppercase tracking-wide font-medium text-emerald-600 dark:text-emerald-400">Received (Customers)</p>
          </div>
          <p className="text-2xl font-bold font-mono text-emerald-600 dark:text-emerald-400">
            {formatCurrency(kpi.customerTotal)}
          </p>
        </GlassCard>
        <GlassCard className="p-4">
          <div className="flex items-center gap-2 mb-1">
            <ArrowTrendingUpIcon className="w-4 h-4 text-red-500" />
            <p className="text-xs uppercase tracking-wide font-medium text-red-600 dark:text-red-400">Paid (Suppliers)</p>
          </div>
          <p className="text-2xl font-bold font-mono text-red-600 dark:text-red-400">
            {formatCurrency(kpi.supplierTotal)}
          </p>
        </GlassCard>
        <GlassCard className="p-4">
          <p className={`text-xs uppercase tracking-wide mb-1 font-medium ${isDark ? "text-slate-400" : "text-slate-500"}`}>Net Cash Flow</p>
          <p className={`text-2xl font-bold font-mono ${kpi.net >= 0 ? "text-emerald-600 dark:text-emerald-400" : "text-red-600 dark:text-red-400"}`}>
            {kpi.net >= 0 ? "+" : ""}{formatCurrency(kpi.net)}
          </p>
        </GlassCard>
        <GlassCard className="p-4">
          <p className={`text-xs uppercase tracking-wide mb-1 font-medium ${isDark ? "text-slate-400" : "text-slate-500"}`}>Reversals (shown)</p>
          <p className={`text-2xl font-bold ${kpi.reversals > 0 ? "text-amber-600 dark:text-amber-400" : isDark ? "text-slate-300" : "text-slate-700"}`}>
            {kpi.reversals}
          </p>
        </GlassCard>
      </div>

      {/* ── Filters Panel ──────────────────────────────────────────────── */}
      <GlassCard className={`px-4 py-3 flex flex-wrap items-center gap-3`}>
        {/* Search */}
        <div className="relative flex-1 min-w-[180px]">
          <MagnifyingGlassIcon className="absolute left-2.5 top-2 w-4 h-4 text-slate-400" />
          <input
            type="text"
            value={q}
            onChange={e => setQ(e.target.value)}
            placeholder="Search payment# / reference…"
            className={`${inputCls} pl-8 w-full`}
          />
        </div>

        {/* Party type */}
        <div className="min-w-[180px]">
          <Select
            options={PARTY_TYPE_OPTIONS}
            value={PARTY_TYPE_OPTIONS.find(o => o.value === partyType) || PARTY_TYPE_OPTIONS[0]}
            onChange={opt => setPartyType(opt?.value || "")}
            styles={selectStyles}
            isSearchable={false}
            placeholder="All Parties"
          />
        </div>

        {/* Status */}
        <div className="min-w-[140px]">
          <Select
            options={STATUS_OPTIONS}
            value={STATUS_OPTIONS.find(o => o.value === status) || STATUS_OPTIONS[0]}
            onChange={opt => setStatus(opt?.value || "")}
            styles={selectStyles}
            isSearchable={false}
            placeholder="All Status"
          />
        </div>

        {/* Date range */}
        <div className="flex items-center gap-2">
          <CalendarDaysIcon className={`w-4 h-4 ${isDark ? "text-slate-400" : "text-slate-400"}`} />
          <input
            type="date"
            value={fromDate}
            onChange={e => setFromDate(e.target.value)}
            className={`${inputCls} text-xs`}
          />
          <span className={`text-xs ${isDark ? "text-slate-500" : "text-slate-400"}`}>to</span>
          <input
            type="date"
            value={toDate}
            onChange={e => setToDate(e.target.value)}
            className={`${inputCls} text-xs`}
          />
          {(fromDate || toDate) && (
            <button
              onClick={() => { setFromDate(""); setToDate(""); }}
              className={`p-1.5 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700 text-slate-400" : "hover:bg-slate-100 text-slate-500"}`}
              title="Clear dates"
            >
              <XMarkIcon className="w-3.5 h-3.5" />
            </button>
          )}
        </div>
      </GlassCard>

      {/* ── Payments Table ──────────────────────────────────────────────── */}
      <GlassCard className="overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className={`border-b text-xs font-semibold uppercase tracking-wide ${isDark ? "border-slate-700 bg-slate-800/60 text-slate-400" : "border-slate-200 bg-slate-50 text-slate-500"}`}>
                <th className="py-3 px-4 text-left">Payment #</th>
                <th className="py-3 px-4 text-left">Date</th>
                <th className="py-3 px-4 text-left">Party</th>
                <th className="py-3 px-4 text-left">Type</th>
                <th className="py-3 px-4 text-left">Method</th>
                <th className="py-3 px-4 text-right">Amount</th>
                <th className="py-3 px-4 text-right">Allocated</th>
                <th className="py-3 px-4 text-right">Unapplied</th>
                <th className="py-3 px-4 text-center">Status</th>
                <th className="py-3 px-4 text-center">Actions</th>
              </tr>
            </thead>
            <tbody className={`divide-y ${isDark ? "divide-slate-800" : "divide-slate-100"}`}>
              {loading ? (
                <tr>
                  <td colSpan={10} className="py-12 text-center text-slate-400">
                    <ArrowPathIcon className="w-6 h-6 mx-auto animate-spin mb-2" />
                    Loading payments…
                  </td>
                </tr>
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={10} className="py-12 text-center">
                    <BanknotesIcon className={`w-10 h-10 mx-auto mb-3 opacity-30 ${isDark ? "text-slate-400" : "text-slate-400"}`} />
                    <p className={`text-sm ${isDark ? "text-slate-400" : "text-slate-500"}`}>No payments found.</p>
                    <button
                      onClick={() => setShowRecordModal(true)}
                      className={`mt-3 inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg ${btnPrimary.className}`}
                      style={btnPrimary.style}
                    >
                      <PlusCircleIcon className="w-3.5 h-3.5" />
                      Record First Payment
                    </button>
                  </td>
                </tr>
              ) : (
                rows.map(payment => {
                  const isCustomer = payment.party_type === "customer";
                  const party = isCustomer ? payment.customer : payment.supplier;
                  const isReversed = payment.status === "reversed";

                  return (
                    <tr
                      key={payment.id}
                      className={`transition-colors ${isDark ? "hover:bg-slate-800/40" : "hover:bg-slate-50/70"} ${isReversed ? "opacity-60" : ""}`}
                    >
                      {/* Payment # */}
                      <td className="py-3 px-4">
                        <span className={`font-mono text-xs font-semibold ${isDark ? "text-slate-200" : "text-slate-800"}`}>
                          {payment.payment_number}
                        </span>
                      </td>

                      {/* Date */}
                      <td className={`py-3 px-4 text-xs ${isDark ? "text-slate-400" : "text-slate-500"}`}>
                        {fmtDate(payment.payment_date)}
                      </td>

                      {/* Party */}
                      <td className="py-3 px-4">
                        <p className={`text-sm font-medium ${isDark ? "text-slate-200" : "text-slate-800"}`}>
                          {party?.name || "—"}
                        </p>
                      </td>

                      {/* Type */}
                      <td className="py-3 px-4">
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold ${
                          isCustomer
                            ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300"
                            : "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300"
                        }`}>
                          {isCustomer ? <ArrowTrendingDownIcon className="w-3 h-3" /> : <ArrowTrendingUpIcon className="w-3 h-3" />}
                          {isCustomer ? "Receipt" : "Payment"}
                        </span>
                      </td>

                      {/* Method */}
                      <td className={`py-3 px-4 text-xs ${isDark ? "text-slate-400" : "text-slate-500"}`}>
                        {METHOD_LABELS[payment.payment_method] || payment.payment_method || "—"}
                      </td>

                      {/* Amount */}
                      <td className={`py-3 px-4 text-right font-mono text-sm font-bold ${isDark ? "text-slate-100" : "text-slate-800"}`}>
                        {formatCurrency(parseFloat(payment.amount || 0))}
                      </td>

                      {/* Allocated */}
                      <td className={`py-3 px-4 text-right font-mono text-xs ${isDark ? "text-slate-400" : "text-slate-500"}`}>
                        {formatCurrency(parseFloat(payment.allocated_amount || 0))}
                      </td>

                      {/* Unapplied */}
                      <td className={`py-3 px-4 text-right font-mono text-xs ${
                        parseFloat(payment.unapplied_amount || 0) > 0
                          ? "text-amber-600 dark:text-amber-400 font-semibold"
                          : isDark ? "text-slate-500" : "text-slate-400"
                      }`}>
                        {formatCurrency(parseFloat(payment.unapplied_amount || 0))}
                      </td>

                      {/* Status */}
                      <td className="py-3 px-4 text-center">
                        {isReversed ? (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300">
                            <ArrowUturnLeftIcon className="w-3 h-3" />
                            Reversed
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300">
                            <CheckCircleIcon className="w-3 h-3" />
                            Posted
                          </span>
                        )}
                      </td>

                      {/* Actions */}
                      <td className="py-3 px-4">
                        <div className="flex items-center justify-center gap-1.5">
                          {/* View receipt */}
                          <button
                            onClick={() => handleViewReceipt(payment)}
                            className={`p-1.5 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700 text-slate-400 hover:text-slate-200" : "hover:bg-slate-100 text-slate-500 hover:text-slate-700"}`}
                            title="View receipt"
                          >
                            <EyeIcon className="w-4 h-4" />
                          </button>

                          {/* Print (shortcut — loads receipt then auto-prints) */}
                          <button
                            onClick={async () => {
                              await handleViewReceipt(payment);
                              // Small delay to let modal render, then print
                              setTimeout(() => window.print(), 300);
                            }}
                            className={`p-1.5 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700 text-slate-400 hover:text-slate-200" : "hover:bg-slate-100 text-slate-500 hover:text-slate-700"}`}
                            title="Print receipt"
                          >
                            <PrinterIcon className="w-4 h-4" />
                          </button>

                          {/* Share (WhatsApp / Email PDF) */}
                          <button
                            onClick={() => setSharingPaymentId(payment.id)}
                            className={`p-1.5 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700 text-emerald-400 hover:text-emerald-300" : "hover:bg-emerald-50 text-emerald-600 hover:text-emerald-700"}`}
                            title="Share via WhatsApp or Email PDF"
                          >
                            <ShareIcon className="w-4 h-4" />
                          </button>

                          {/* Reverse */}
                          {!isReversed && (
                            <button
                              onClick={() => { setReversingPayment(payment); setReversalReason(""); }}
                              className="p-1.5 rounded-lg transition-colors text-red-500 hover:bg-red-50 dark:hover:bg-red-900/30"
                              title="Reverse payment"
                            >
                              <ArrowUturnLeftIcon className="w-4 h-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        {!loading && total > pageSize && (
          <div className={`flex items-center justify-between gap-3 px-4 py-3 border-t text-xs ${isDark ? "border-slate-700 text-slate-400" : "border-slate-200 text-slate-500"}`}>
            <span>Showing {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, total)} of {total}</span>
            <div className="flex items-center gap-1">
              <button onClick={() => setPage(1)} disabled={page <= 1} className="p-1 rounded disabled:opacity-30 hover:bg-slate-200 dark:hover:bg-slate-700"><ChevronDoubleLeftIcon className="w-4 h-4" /></button>
              <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1} className="p-1 rounded disabled:opacity-30 hover:bg-slate-200 dark:hover:bg-slate-700"><ChevronLeftIcon className="w-4 h-4" /></button>
              <span className="px-2 font-medium">{page} / {pageCount}</span>
              <button onClick={() => setPage(p => Math.min(pageCount, p + 1))} disabled={page >= pageCount} className="p-1 rounded disabled:opacity-30 hover:bg-slate-200 dark:hover:bg-slate-700"><ChevronRightIcon className="w-4 h-4" /></button>
              <button onClick={() => setPage(pageCount)} disabled={page >= pageCount} className="p-1 rounded disabled:opacity-30 hover:bg-slate-200 dark:hover:bg-slate-700"><ChevronDoubleRightIcon className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </GlassCard>

      {/* ── Reversal Confirmation Modal ─────────────────────────────────── */}
      {reversingPayment && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm" onClick={e => { if (e.target === e.currentTarget) setReversingPayment(null); }}>
          <GlassCard className={`w-full max-w-md border shadow-2xl rounded-2xl overflow-hidden ${isDark ? "bg-slate-900 border-slate-700 text-slate-100" : "bg-white border-gray-200 text-gray-900"}`}>
            <div className={`flex items-center justify-between px-6 py-4 border-b ${isDark ? "border-slate-700 bg-slate-800/50" : "border-gray-200 bg-gray-50"}`}>
              <div className="flex items-center gap-2">
                <ArrowUturnLeftIcon className="w-5 h-5 text-red-500" />
                <h3 className={`font-bold text-base ${isDark ? "text-slate-100" : "text-gray-900"}`}>Reverse Payment</h3>
              </div>
              <button onClick={() => setReversingPayment(null)} className={`p-1.5 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700 text-slate-400" : "hover:bg-gray-200/70 text-gray-500"}`}>
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>
            <div className="p-6 space-y-4">
              <div className={`rounded-xl border p-3 text-sm ${isDark ? "border-red-800/50 bg-red-900/20 text-red-300" : "border-red-200 bg-red-50 text-red-700"}`}>
                <p className="font-semibold">Reverse {reversingPayment.payment_number}?</p>
                <p className="text-xs mt-1 opacity-80">
                  Amount: <span className="font-mono font-bold">{formatCurrency(parseFloat(reversingPayment.amount || 0))}</span>.
                  This will un-allocate all invoice allocations and restore the original balances.
                </p>
              </div>
              <div>
                <label className={`block text-sm font-medium mb-1.5 ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                  Reason for Reversal *
                </label>
                <textarea
                  value={reversalReason}
                  onChange={e => setReversalReason(e.target.value)}
                  rows={3}
                  placeholder="Explain why this payment is being reversed…"
                  className={`w-full rounded-lg border px-3 py-2 text-sm focus:outline-none focus:ring-2 transition-colors ${isDark ? "bg-slate-800 border-slate-600 text-slate-100 focus:ring-red-500/40 placeholder-slate-500" : "bg-white border-gray-300 text-gray-900 focus:ring-red-500/40 placeholder-gray-400"}`}
                />
              </div>
              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setReversingPayment(null)}
                  className={`px-4 py-2.5 text-sm font-semibold ${btnSecondary.className}`}
                  style={btnSecondary.style}
                >
                  Cancel
                </button>
                <button
                  onClick={handleConfirmReverse}
                  disabled={reversing || reversalReason.trim().length < 3}
                  className={`px-4 py-2.5 text-sm font-semibold disabled:opacity-60 ${btnDanger.className}`}
                  style={btnDanger.style}
                >
                  {reversing ? "Reversing…" : "Confirm Reversal"}
                </button>
              </div>
            </div>
          </GlassCard>
        </div>
      )}

      {/* ── Payment Receipt Modal ───────────────────────────────────────── */}
      <PaymentReceiptModal
        isOpen={!!receiptPayment}
        onClose={() => setReceiptPayment(null)}
        payment={receiptPayment}
      />

      {/* ── Party Picker Modal → then opens RecordPaymentModal ──────────── */}
      {showPartyPicker && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm" onClick={e => { if (e.target === e.currentTarget) setShowPartyPicker(false); }}>
          <GlassCard className={`w-full max-w-sm border shadow-2xl rounded-2xl overflow-hidden ${isDark ? "bg-slate-900 border-slate-700 text-slate-100" : "bg-white border-gray-200 text-gray-900"}`}>
            <div className={`flex items-center justify-between px-6 py-4 border-b ${isDark ? "border-slate-700 bg-slate-800/50" : "border-gray-200 bg-gray-50"}`}>
              <div className="flex items-center gap-2">
                <BanknotesIcon className="w-5 h-5 text-blue-500" />
                <h3 className={`font-bold text-base ${isDark ? "text-slate-100" : "text-gray-900"}`}>New Payment</h3>
              </div>
              <button onClick={() => setShowPartyPicker(false)} className={`p-1.5 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700 text-slate-400" : "hover:bg-gray-200/70 text-gray-500"}`}>
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>
            <div className="p-6 space-y-4">
              <div>
                <label className={`block text-sm font-medium mb-2 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Payment Type</label>
                <div className="grid grid-cols-2 gap-3">
                  {[
                    { value: "customer", label: "Customer Receipt", desc: "Money received from customer" },
                    { value: "supplier", label: "Supplier Payment", desc: "Money paid to supplier" },
                  ].map(opt => (
                    <button
                      key={opt.value}
                      type="button"
                      onClick={() => { setPickerPartyType(opt.value); setPickerPartyId(null); setPickerPartyName(""); }}
                      className={`p-3 rounded-xl border text-left transition-colors ${
                        pickerPartyType === opt.value
                          ? isDark ? "border-blue-500 bg-blue-900/30" : "border-blue-500 bg-blue-50"
                          : isDark ? "border-slate-600 hover:bg-slate-800" : "border-gray-200 hover:bg-gray-50"
                      }`}
                    >
                      <p className={`text-sm font-semibold ${isDark ? "text-slate-200" : "text-gray-800"}`}>{opt.label}</p>
                      <p className={`text-xs mt-0.5 ${isDark ? "text-slate-400" : "text-gray-500"}`}>{opt.desc}</p>
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className={`block text-sm font-medium mb-1.5 ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                  Select {pickerPartyType === "customer" ? "Customer" : "Supplier"} *
                </label>
                <button
                  type="button"
                  onClick={() => {
                    if (pickerPartyType === "customer") setShowCustomerSearch(true);
                    else setShowSupplierSearch(true);
                  }}
                  className={`w-full flex items-center gap-2 px-3 py-2.5 rounded-lg border text-sm text-left transition-colors ${
                    pickerPartyId
                      ? isDark ? "border-blue-600 bg-blue-900/20 text-blue-300" : "border-blue-400 bg-blue-50 text-blue-700"
                      : isDark ? "border-slate-600 text-slate-400 hover:bg-slate-800" : "border-gray-300 text-gray-500 hover:bg-gray-50"
                  }`}
                >
                  {pickerPartyId
                    ? <><CheckCircleIcon className="w-4 h-4 text-emerald-500 flex-shrink-0" />{pickerPartyName}</>
                    : <><MagnifyingGlassIcon className="w-4 h-4 flex-shrink-0" />Search {pickerPartyType === "customer" ? "customer" : "supplier"}…</>
                  }
                </button>
              </div>

              <div className="flex justify-end gap-3 pt-1">
                <button
                  type="button"
                  onClick={() => setShowPartyPicker(false)}
                  className={`px-4 py-2.5 text-sm font-semibold ${btnSecondary.className}`}
                  style={btnSecondary.style}
                >
                  Cancel
                </button>
                <button
                  type="button"
                  disabled={!pickerPartyId}
                  onClick={() => {
                    setShowPartyPicker(false);
                    setRecordPartyId(pickerPartyId);
                    setRecordPartyName(pickerPartyName);
                    setRecordPartyType(pickerPartyType);
                    setShowRecordModal(true);
                  }}
                  className={`px-4 py-2.5 text-sm font-semibold disabled:opacity-50 ${btnPrimary.className}`}
                  style={btnPrimary.style}
                >
                  Continue
                </button>
              </div>
            </div>
          </GlassCard>
        </div>
      )}

      {/* Customer / Supplier search modals */}
      <CustomerSearch
        isOpen={showCustomerSearch}
        onClose={() => setShowCustomerSearch(false)}
        onSelect={c => {
          setPickerPartyId(c.id);
          setPickerPartyName(c.name);
          setShowCustomerSearch(false);
        }}
      />
      <SupplierSearch
        isOpen={showSupplierSearch}
        onClose={() => setShowSupplierSearch(false)}
        onSelect={s => {
          setPickerPartyId(s.id);
          setPickerPartyName(s.name);
          setShowSupplierSearch(false);
        }}
      />

      {/* ── Record Payment Modal ────────────────────────────────────────── */}
      {showRecordModal && recordPartyId && (
        <RecordPaymentModal
          isOpen={showRecordModal}
          onClose={() => { setShowRecordModal(false); setRecordPartyId(null); }}
          partyType={recordPartyType}
          partyId={recordPartyId}
          partyName={recordPartyName}
          onPaymentSuccess={() => {
            setShowRecordModal(false);
            setRecordPartyId(null);
            fetchPayments();
          }}
        />
      )}

      {/* ── Share Document Modal (WhatsApp / Email PDF) ─────────────────── */}
      <ShareDocumentModal
        isOpen={Boolean(sharingPaymentId)}
        onClose={() => setSharingPaymentId(null)}
        documentType="payment"
        documentId={sharingPaymentId}
      />
    </div>
  );
}
