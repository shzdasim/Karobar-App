import React, { useState, useEffect, useMemo, useCallback, useRef } from "react";
import axios from "axios";
import Select from "react-select";
import {
  XMarkIcon,
  BanknotesIcon,
  ArrowDownLeftIcon,
  ArrowUpRightIcon,
  LockClosedIcon,
  ClockIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  ArrowPathIcon,
} from "@heroicons/react/24/solid";
import { useCurrency } from "@/context/CurrencyContext.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";

const TRANSACTION_TYPE_OPTIONS = [
  { value: "cash_in", label: "Cash In (Add Money / Float)" },
  { value: "cash_out", label: "Cash Out (Petty Cash / Expense / Withdrawal)" },
];

export default function CashDrawerModal({ isOpen, onClose, onSessionUpdated }) {
  const { formatCurrency } = useCurrency();
  const { isDark, theme } = useTheme();

  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [sessionData, setSessionData] = useState(null); // { is_open, session, summary }
  const [activeTab, setActiveTab] = useState("overview"); // 'overview' | 'actions' | 'close' | 'history'

  // Open shift form
  const [openingBalance, setOpeningBalance] = useState("0");
  const [openingNotes, setOpeningNotes] = useState("");

  // Cash in / cash out form
  const [selectedTxnType, setSelectedTxnType] = useState(TRANSACTION_TYPE_OPTIONS[0]);
  const [txnAmount, setTxnAmount] = useState("");
  const [txnNotes, setTxnNotes] = useState("");

  // Close shift form
  const [actualClosingBalance, setActualClosingBalance] = useState("");
  const [closingNotes, setClosingNotes] = useState("");

  // History state
  const [historyList, setHistoryList] = useState([]);
  const [loadingHistory, setLoadingHistory] = useState(false);

  const [errorMsg, setErrorMsg] = useState("");
  const [successMsg, setSuccessMsg] = useState("");

  const selectStyles = useMemo(() => ({
    control: (base, state) => ({
      ...base,
      minHeight: "38px",
      borderRadius: 10,
      backgroundColor: isDark ? "rgba(30, 41, 59, 0.9)" : "#ffffff",
      borderColor: state.isFocused
        ? (theme?.primary_color || "#3b82f6")
        : (isDark ? "rgba(71, 85, 105, 0.7)" : "rgba(209, 213, 219, 0.8)"),
      boxShadow: state.isFocused
        ? `0 0 0 2px ${theme?.primary_color || "#3b82f6"}33`
        : "none",
      fontSize: "13px",
      color: isDark ? "#f8fafc" : "#1e293b",
    }),
    valueContainer: (base) => ({ ...base, padding: "2px 10px" }),
    input: (base) => ({ ...base, color: isDark ? "#f8fafc" : "#1e293b", margin: 0, padding: 0 }),
    singleValue: (base) => ({ ...base, color: isDark ? "#f8fafc" : "#1e293b", fontSize: "13px" }),
    menuPortal: (base) => ({ ...base, zIndex: 9999 }),
    menu: (base) => ({
      ...base,
      backgroundColor: isDark ? "#1e293b" : "#ffffff",
      border: isDark ? "1px solid #334155" : "1px solid #e2e8f0",
      boxShadow: "0 10px 25px -5px rgba(0, 0, 0, 0.2)",
      borderRadius: 10,
      overflow: "hidden",
    }),
    option: (base, state) => ({
      ...base,
      backgroundColor: state.isSelected
        ? (theme?.primary_color || "#3b82f6")
        : state.isFocused
        ? (isDark ? "#334155" : "#f1f5f9")
        : "transparent",
      color: state.isSelected ? "#ffffff" : (isDark ? "#f8fafc" : "#1e293b"),
      fontSize: "13px",
      cursor: "pointer",
    }),
  }), [isDark, theme]);

  const fetchCurrentDrawer = useCallback(async () => {
    setLoading(true);
    setErrorMsg("");
    try {
      const res = await axios.get("/api/cash-drawer/current");
      setSessionData(res.data);
      if (res.data?.is_open && res.data?.summary) {
        setActualClosingBalance(String(res.data.summary.expected_balance || 0));
      }
    } catch (err) {
      setErrorMsg(err.response?.data?.message || "Failed to load cash drawer status.");
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchHistory = useCallback(async () => {
    setLoadingHistory(true);
    try {
      const res = await axios.get("/api/cash-drawer/history");
      setHistoryList(res.data?.data || []);
    } catch {
      // silent
    } finally {
      setLoadingHistory(false);
    }
  }, []);

  useEffect(() => {
    if (isOpen) {
      fetchCurrentDrawer();
      setActiveTab("overview");
      setSuccessMsg("");
      setErrorMsg("");
    }
  }, [isOpen, fetchCurrentDrawer]);

  useEffect(() => {
    if (isOpen && activeTab === "history") {
      fetchHistory();
    }
  }, [isOpen, activeTab, fetchHistory]);

  // Close on Escape key
  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, onClose]);

  const mouseDownTargetRef = useRef(null);

  const handleMouseDown = (e) => {
    mouseDownTargetRef.current = e.target;
  };

  const handleBackdropClick = (e) => {
    if (e.target === e.currentTarget && mouseDownTargetRef.current === e.currentTarget && !submitting) {
      onClose();
    }
  };

  const handleOpenDrawer = async (e) => {
    e.preventDefault();
    setSubmitting(true);
    setErrorMsg("");
    setSuccessMsg("");
    try {
      const res = await axios.post("/api/cash-drawer/open", {
        opening_balance: Number(openingBalance) || 0,
        notes: openingNotes,
      });
      setSuccessMsg("Cash drawer register opened successfully.");
      setOpeningBalance("0");
      setOpeningNotes("");
      await fetchCurrentDrawer();
      setActiveTab("overview");
      window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
      if (onSessionUpdated) onSessionUpdated(res.data?.session);
    } catch (err) {
      setErrorMsg(err.response?.data?.message || err.response?.data?.errors?.session?.[0] || "Could not open drawer.");
    } finally {
      setSubmitting(false);
    }
  };

  const handleRecordTxn = async (e) => {
    e.preventDefault();
    if (!txnAmount || Number(txnAmount) <= 0) {
      setErrorMsg("Please enter a valid positive amount.");
      return;
    }
    if (!txnNotes.trim()) {
      setErrorMsg("Please provide a reason/note for this cash adjustment.");
      return;
    }
    setSubmitting(true);
    setErrorMsg("");
    setSuccessMsg("");
    try {
      await axios.post("/api/cash-drawer/transaction", {
        type: selectedTxnType.value,
        amount: Number(txnAmount),
        notes: txnNotes.trim(),
      });
      setSuccessMsg(`${selectedTxnType.label} recorded successfully.`);
      setTxnAmount("");
      setTxnNotes("");
      await fetchCurrentDrawer();
      setActiveTab("overview");
      window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
    } catch (err) {
      setErrorMsg(err.response?.data?.message || "Failed to record cash transaction.");
    } finally {
      setSubmitting(false);
    }
  };

  const handleCloseDrawer = async (e) => {
    e.preventDefault();
    if (actualClosingBalance === "" || isNaN(Number(actualClosingBalance))) {
      setErrorMsg("Please enter the actual counted cash in drawer.");
      return;
    }
    setSubmitting(true);
    setErrorMsg("");
    setSuccessMsg("");
    try {
      const res = await axios.post("/api/cash-drawer/close", {
        closing_balance_actual: Number(actualClosingBalance),
        notes: closingNotes.trim(),
      });
      const diff = Number(res.data?.session?.difference || 0);
      const diffLabel = diff === 0 ? "Perfect balance" : diff > 0 ? `Over by ${formatCurrency(diff)}` : `Short by ${formatCurrency(Math.abs(diff))}`;
      setSuccessMsg(`Register shift closed successfully (${diffLabel}).`);
      setClosingNotes("");
      await fetchCurrentDrawer();
      setActiveTab("overview");
      window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
      if (onSessionUpdated) onSessionUpdated(null);
    } catch (err) {
      setErrorMsg(err.response?.data?.message || err.response?.data?.errors?.session?.[0] || "Failed to close drawer.");
    } finally {
      setSubmitting(false);
    }
  };

  if (!isOpen) return null;

  const isOpenShift = !!sessionData?.is_open;
  const summary = sessionData?.summary;
  const session = sessionData?.session;

  const expectedClosing = summary?.expected_balance ?? 0;
  const counted = Number(actualClosingBalance || 0);
  const liveDiff = counted - expectedClosing;

  return (
    <div
      onMouseDown={handleMouseDown}
      onClick={handleBackdropClick}
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-xs transition-opacity animate-in fade-in duration-200 cursor-default"
      role="dialog"
      aria-modal="true"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className={`w-full max-w-2xl rounded-2xl shadow-2xl overflow-hidden border flex flex-col max-h-[90vh] ${
          isDark ? "bg-slate-900 border-slate-700 text-slate-100" : "bg-white border-gray-200 text-gray-900"
        }`}
      >
        {/* Header */}
        <div
          className={`flex items-center justify-between px-6 py-4 border-b ${
            isDark ? "border-slate-800 bg-slate-800/60" : "border-gray-100 bg-gray-50/80"
          }`}
        >
          <div className="flex items-center gap-3">
            <div
              className="p-2 rounded-xl text-white shadow-sm"
              style={{ background: theme?.primary_color || "#3b82f6" }}
            >
              <BanknotesIcon className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-base font-bold">Cash Drawer & Register</h2>
                <span
                  className={`text-[11px] font-semibold px-2 py-0.5 rounded-full inline-flex items-center gap-1 ${
                    isOpenShift
                      ? "bg-emerald-500/10 text-emerald-500 border border-emerald-500/30"
                      : "bg-amber-500/10 text-amber-500 border border-amber-500/30"
                  }`}
                >
                  <span className={`w-1.5 h-1.5 rounded-full ${isOpenShift ? "bg-emerald-500" : "bg-amber-500"}`} />
                  {isOpenShift ? "Shift Open" : "Register Closed"}
                </span>
              </div>
              <p className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                {isOpenShift && session
                  ? `Shift started ${new Date(session.opened_at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} by ${session.user?.name || "Cashier"}`
                  : "Start a new register shift with an opening cash float"}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-1">
            <button
              onClick={fetchCurrentDrawer}
              disabled={loading}
              title="Refresh register stats"
              className={`p-2 rounded-lg transition-colors ${
                isDark ? "hover:bg-slate-800 text-slate-400 hover:text-slate-200" : "hover:bg-gray-100 text-gray-500 hover:text-gray-800"
              }`}
            >
              <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
            </button>
            <button
              onClick={onClose}
              className={`p-2 rounded-lg transition-colors ${
                isDark ? "hover:bg-slate-800 text-slate-400 hover:text-slate-200" : "hover:bg-gray-100 text-gray-500 hover:text-gray-800"
              }`}
            >
              <XMarkIcon className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Tab Navigation if open */}
        {isOpenShift && (
          <div
            className={`flex items-center gap-1 px-6 pt-2 border-b text-xs font-semibold ${
              isDark ? "border-slate-800 bg-slate-900" : "border-gray-100 bg-white"
            }`}
          >
            {[
              { id: "overview", label: "Overview & Balance" },
              { id: "actions", label: "Cash In / Cash Out" },
              { id: "close", label: "Close Register" },
              { id: "history", label: "Shift History" },
            ].map((tab) => (
              <button
                key={tab.id}
                onClick={() => {
                  setActiveTab(tab.id);
                  setErrorMsg("");
                  setSuccessMsg("");
                }}
                className={`px-3 py-2 border-b-2 transition-all ${
                  activeTab === tab.id
                    ? "border-blue-500 text-blue-500 font-bold"
                    : "border-transparent text-gray-500 hover:text-gray-700 dark:text-slate-400 dark:hover:text-slate-200"
                }`}
              >
                {tab.label}
              </button>
            ))}
          </div>
        )}

        {/* Alerts */}
        {errorMsg && (
          <div className="mx-6 mt-4 p-3 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-500 text-xs flex items-center gap-2">
            <ExclamationTriangleIcon className="w-4 h-4 shrink-0" />
            <span>{errorMsg}</span>
          </div>
        )}
        {successMsg && (
          <div className="mx-6 mt-4 p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-500 text-xs flex items-center gap-2">
            <CheckCircleIcon className="w-4 h-4 shrink-0" />
            <span>{successMsg}</span>
          </div>
        )}

        {/* Body */}
        <div className="p-6 overflow-y-auto space-y-4 flex-1">
          {/* View 1: When drawer is CLOSED -> Open Shift Form */}
          {!isOpenShift && (
            <form onSubmit={handleOpenDrawer} className="space-y-4">
              <div
                className={`p-4 rounded-xl border flex items-start gap-3 ${
                  isDark ? "bg-slate-800/40 border-slate-700" : "bg-blue-50/50 border-blue-100"
                }`}
              >
                <BanknotesIcon className="w-6 h-6 text-blue-500 shrink-0 mt-0.5" />
                <div className="text-xs space-y-1">
                  <p className="font-semibold text-sm">Register is currently closed</p>
                  <p className={isDark ? "text-slate-400" : "text-gray-600"}>
                    Count the physical cash in the drawer (opening float / change) and record it below to begin recording cash transactions for this shift.
                  </p>
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1">
                  Opening Cash Float Amount
                </label>
                <div className="relative">
                  <input
                    type="number"
                    step="any"
                    min="0"
                    required
                    value={openingBalance}
                    onChange={(e) => setOpeningBalance(e.target.value)}
                    placeholder="0.00"
                    className={`w-full px-3 py-2.5 rounded-xl border text-base font-bold text-right transition-colors ${
                      isDark
                        ? "bg-slate-800 border-slate-700 text-slate-100 focus:border-blue-500"
                        : "bg-white border-gray-300 text-gray-900 focus:border-blue-500"
                    } focus:outline-hidden`}
                  />
                  <span className="absolute left-3 top-3 text-xs font-semibold text-gray-400">
                    Float
                  </span>
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1">
                  Shift Notes (Optional)
                </label>
                <textarea
                  rows={2}
                  value={openingNotes}
                  onChange={(e) => setOpeningNotes(e.target.value)}
                  placeholder="e.g. Started morning shift with change"
                  className={`w-full px-3 py-2 rounded-xl border text-xs transition-colors ${
                    isDark
                      ? "bg-slate-800 border-slate-700 text-slate-100 focus:border-blue-500"
                      : "bg-white border-gray-300 text-gray-900 focus:border-blue-500"
                  } focus:outline-hidden`}
                />
              </div>

              <button
                type="submit"
                disabled={submitting}
                className="w-full py-3 rounded-xl font-bold text-sm text-white shadow-md transition-all active:scale-[0.99] flex items-center justify-center gap-2"
                style={{ background: theme?.primary_color || "#3b82f6" }}
              >
                {submitting ? "Opening Register…" : "Open Cash Register Shift"}
              </button>
            </form>
          )}

          {/* View 2: When drawer is OPEN -> Overview Tab */}
          {isOpenShift && activeTab === "overview" && summary && (
            <div className="space-y-4">
              {/* Expected cash in drawer banner */}
              <div
                className="p-5 rounded-2xl border text-center relative overflow-hidden"
                style={{
                  background: isDark
                    ? "linear-gradient(135deg, rgba(30, 41, 59, 0.9), rgba(15, 23, 42, 0.95))"
                    : "linear-gradient(135deg, rgba(239, 246, 255, 0.9), rgba(240, 253, 244, 0.95))",
                  borderColor: isDark ? "rgba(51, 65, 85, 0.6)" : "rgba(191, 219, 254, 0.8)",
                }}
              >
                <p className={`text-xs font-semibold uppercase tracking-wider ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  Expected Cash in Drawer
                </p>
                <h3 className="text-3xl font-black mt-1 text-emerald-500">
                  {formatCurrency(summary.expected_balance)}
                </h3>
                <p className={`text-xs mt-1 ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  Opening float: <span className="font-semibold">{formatCurrency(summary.opening_balance)}</span> • {summary.transactions_count} cash transactions
                </p>
              </div>

              {/* Inflow vs Outflow grid */}
              <div className="grid grid-cols-2 gap-3 text-xs">
                {/* Cash Inflow */}
                <div className={`p-3.5 rounded-xl border ${isDark ? "bg-slate-800/40 border-slate-700/60" : "bg-emerald-50/40 border-emerald-100"}`}>
                  <div className="flex items-center justify-between font-bold text-emerald-500 mb-2">
                    <span className="flex items-center gap-1">
                      <ArrowDownLeftIcon className="w-3.5 h-3.5" /> Total Cash In
                    </span>
                    <span>+{formatCurrency(summary.total_cash_in)}</span>
                  </div>
                  <div className="space-y-1.5 text-[11px] opacity-90">
                    <div className="flex justify-between">
                      <span className={isDark ? "text-slate-400" : "text-gray-600"}>Cash Sales:</span>
                      <span className="font-semibold">+{formatCurrency(summary.cash_sales)}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className={isDark ? "text-slate-400" : "text-gray-600"}>Customer Receipts:</span>
                      <span className="font-semibold">+{formatCurrency(summary.customer_payments)}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className={isDark ? "text-slate-400" : "text-gray-600"}>Manual Cash In:</span>
                      <span className="font-semibold">+{formatCurrency(summary.manual_cash_in)}</span>
                    </div>
                  </div>
                </div>

                {/* Cash Outflow */}
                <div className={`p-3.5 rounded-xl border ${isDark ? "bg-slate-800/40 border-slate-700/60" : "bg-rose-50/40 border-rose-100"}`}>
                  <div className="flex items-center justify-between font-bold text-rose-500 mb-2">
                    <span className="flex items-center gap-1">
                      <ArrowUpRightIcon className="w-3.5 h-3.5" /> Total Cash Out
                    </span>
                    <span>-{formatCurrency(summary.total_cash_out)}</span>
                  </div>
                  <div className="space-y-1.5 text-[11px] opacity-90">
                    <div className="flex justify-between">
                      <span className={isDark ? "text-slate-400" : "text-gray-600"}>Cash Purchases:</span>
                      <span className="font-semibold">-{formatCurrency(summary.cash_purchases)}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className={isDark ? "text-slate-400" : "text-gray-600"}>Supplier Payments:</span>
                      <span className="font-semibold">-{formatCurrency(summary.supplier_payments)}</span>
                    </div>
                    <div className="flex justify-between">
                      <span className={isDark ? "text-slate-400" : "text-gray-600"}>Manual Cash Out:</span>
                      <span className="font-semibold">-{formatCurrency(summary.manual_cash_out)}</span>
                    </div>
                  </div>
                </div>
              </div>

              {/* Action shortcuts */}
              <div className="flex items-center gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => {
                    setSelectedTxnType(TRANSACTION_TYPE_OPTIONS[0]);
                    setActiveTab("actions");
                  }}
                  className={`flex-1 py-2.5 px-3 rounded-xl border text-xs font-bold transition-all flex items-center justify-center gap-1.5 ${
                    isDark ? "border-slate-700 hover:bg-slate-800 text-emerald-400" : "border-emerald-200 hover:bg-emerald-50 text-emerald-700"
                  }`}
                >
                  <ArrowDownLeftIcon className="w-4 h-4" /> Add Cash In
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setSelectedTxnType(TRANSACTION_TYPE_OPTIONS[1]);
                    setActiveTab("actions");
                  }}
                  className={`flex-1 py-2.5 px-3 rounded-xl border text-xs font-bold transition-all flex items-center justify-center gap-1.5 ${
                    isDark ? "border-slate-700 hover:bg-slate-800 text-rose-400" : "border-rose-200 hover:bg-rose-50 text-rose-700"
                  }`}
                >
                  <ArrowUpRightIcon className="w-4 h-4" /> Cash Out / Expense
                </button>
                <button
                  type="button"
                  onClick={() => setActiveTab("close")}
                  className={`flex-1 py-2.5 px-3 rounded-xl border text-xs font-bold transition-all flex items-center justify-center gap-1.5 ${
                    isDark ? "border-slate-700 hover:bg-slate-800 text-amber-400" : "border-amber-200 hover:bg-amber-50 text-amber-700"
                  }`}
                >
                  <LockClosedIcon className="w-4 h-4" /> Close Shift
                </button>
              </div>

              {/* Recent transactions in this shift */}
              <div className="pt-2">
                <h4 className="text-xs font-bold uppercase tracking-wider mb-2">
                  Shift Transactions ({session?.transactions?.length || 0})
                </h4>
                {session?.transactions?.length > 0 ? (
                  <div className="space-y-1.5 max-h-48 overflow-y-auto pr-1">
                    {session.transactions.map((tx) => {
                      const isPlus = ["cash_sale", "customer_payment", "cash_in"].includes(tx.type);
                      return (
                        <div
                          key={tx.id}
                          className={`p-2.5 rounded-lg border flex items-center justify-between text-xs ${
                            isDark ? "bg-slate-800/30 border-slate-700/50" : "bg-gray-50 border-gray-100"
                          }`}
                        >
                          <div>
                            <span className="font-semibold capitalize">
                              {tx.type.replace("_", " ")}
                            </span>
                            {tx.notes && (
                              <span className={`ml-2 text-[11px] ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                                • {tx.notes}
                              </span>
                            )}
                            <div className={`text-[10px] ${isDark ? "text-slate-500" : "text-gray-400"}`}>
                              {new Date(tx.created_at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                            </div>
                          </div>
                          <span className={`font-bold ${isPlus ? "text-emerald-500" : "text-rose-500"}`}>
                            {isPlus ? "+" : "-"}{formatCurrency(tx.amount)}
                          </span>
                        </div>
                      );
                    })}
                  </div>
                ) : (
                  <p className={`text-xs italic ${isDark ? "text-slate-500" : "text-gray-400"}`}>
                    No transactions recorded in this shift yet.
                  </p>
                )}
              </div>
            </div>
          )}

          {/* View 3: Actions Tab (Cash In / Out) */}
          {isOpenShift && activeTab === "actions" && (
            <form onSubmit={handleRecordTxn} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1">
                  Transaction Type
                </label>
                <Select
                  options={TRANSACTION_TYPE_OPTIONS}
                  value={selectedTxnType}
                  onChange={(opt) => setSelectedTxnType(opt)}
                  styles={selectStyles}
                  isSearchable={false}
                />
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1">
                  Amount
                </label>
                <input
                  type="number"
                  step="any"
                  min="0.01"
                  required
                  value={txnAmount}
                  onChange={(e) => setTxnAmount(e.target.value)}
                  placeholder="0.00"
                  className={`w-full px-3 py-2.5 rounded-xl border text-sm font-bold text-right transition-colors ${
                    isDark
                      ? "bg-slate-800 border-slate-700 text-slate-100 focus:border-blue-500"
                      : "bg-white border-gray-300 text-gray-900 focus:border-blue-500"
                  } focus:outline-hidden`}
                />
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1">
                  Reason / Purpose (Required)
                </label>
                <input
                  type="text"
                  required
                  value={txnNotes}
                  onChange={(e) => setTxnNotes(e.target.value)}
                  placeholder="e.g. Office tea expense, Petty cash top up, Owner draw"
                  className={`w-full px-3 py-2 rounded-xl border text-xs transition-colors ${
                    isDark
                      ? "bg-slate-800 border-slate-700 text-slate-100 focus:border-blue-500"
                      : "bg-white border-gray-300 text-gray-900 focus:border-blue-500"
                  } focus:outline-hidden`}
                />
              </div>

              <div className="flex gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setActiveTab("overview")}
                  className={`flex-1 py-2.5 rounded-xl border text-xs font-semibold transition-colors ${
                    isDark ? "border-slate-700 hover:bg-slate-800" : "border-gray-300 hover:bg-gray-100"
                  }`}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="flex-1 py-2.5 rounded-xl font-bold text-xs text-white shadow-sm transition-all"
                  style={{
                    background: selectedTxnType.value === "cash_in" ? "#10b981" : "#f43f5e",
                  }}
                >
                  {submitting ? "Saving…" : `Confirm ${selectedTxnType.label.split(" ")[0]}`}
                </button>
              </div>
            </form>
          )}

          {/* View 4: Close Tab */}
          {isOpenShift && activeTab === "close" && (
            <form onSubmit={handleCloseDrawer} className="space-y-4">
              <div
                className={`p-4 rounded-xl border ${
                  isDark ? "bg-slate-800/40 border-slate-700" : "bg-amber-50/50 border-amber-100"
                }`}
              >
                <div className="flex justify-between items-center text-xs">
                  <span className="font-semibold text-gray-500 dark:text-slate-400">System Expected Cash:</span>
                  <span className="font-bold text-base text-emerald-500">{formatCurrency(expectedClosing)}</span>
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1">
                  Actual Cash Counted in Hand
                </label>
                <input
                  type="number"
                  step="any"
                  min="0"
                  required
                  value={actualClosingBalance}
                  onChange={(e) => setActualClosingBalance(e.target.value)}
                  placeholder="0.00"
                  className={`w-full px-3 py-2.5 rounded-xl border text-base font-bold text-right transition-colors ${
                    isDark
                      ? "bg-slate-800 border-slate-700 text-slate-100 focus:border-blue-500"
                      : "bg-white border-gray-300 text-gray-900 focus:border-blue-500"
                  } focus:outline-hidden`}
                />
              </div>

              {/* Over / Short indicator */}
              <div
                className={`p-3 rounded-xl border flex items-center justify-between text-xs font-bold ${
                  liveDiff === 0
                    ? "bg-emerald-500/10 border-emerald-500/20 text-emerald-500"
                    : liveDiff > 0
                    ? "bg-blue-500/10 border-blue-500/20 text-blue-500"
                    : "bg-rose-500/10 border-rose-500/20 text-rose-500"
                }`}
              >
                <span>Reconciliation Difference:</span>
                <span>
                  {liveDiff === 0
                    ? "Perfect Match (0.00)"
                    : liveDiff > 0
                    ? `Over by +${formatCurrency(liveDiff)}`
                    : `Short by -${formatCurrency(Math.abs(liveDiff))}`}
                </span>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1">
                  Closing Notes (Optional)
                </label>
                <textarea
                  rows={2}
                  value={closingNotes}
                  onChange={(e) => setClosingNotes(e.target.value)}
                  placeholder="e.g. Count matched evening tally"
                  className={`w-full px-3 py-2 rounded-xl border text-xs transition-colors ${
                    isDark
                      ? "bg-slate-800 border-slate-700 text-slate-100 focus:border-blue-500"
                      : "bg-white border-gray-300 text-gray-900 focus:border-blue-500"
                  } focus:outline-hidden`}
                />
              </div>

              <div className="flex gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setActiveTab("overview")}
                  className={`flex-1 py-2.5 rounded-xl border text-xs font-semibold transition-colors ${
                    isDark ? "border-slate-700 hover:bg-slate-800" : "border-gray-300 hover:bg-gray-100"
                  }`}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="flex-1 py-2.5 rounded-xl font-bold text-xs text-white bg-rose-600 hover:bg-rose-700 shadow-sm transition-all"
                >
                  {submitting ? "Closing Shift…" : "Confirm & Close Shift"}
                </button>
              </div>
            </form>
          )}

          {/* View 5: History Tab */}
          {activeTab === "history" && (
            <div className="space-y-3">
              <div className="flex items-center justify-between text-xs">
                <h4 className="font-bold uppercase tracking-wider">Past Register Shifts</h4>
                <button
                  onClick={fetchHistory}
                  className="text-blue-500 hover:underline inline-flex items-center gap-1 font-semibold"
                >
                  <ArrowPathIcon className={`w-3.5 h-3.5 ${loadingHistory ? "animate-spin" : ""}`} /> Refresh
                </button>
              </div>

              {loadingHistory ? (
                <div className="py-8 text-center text-xs text-gray-500">Loading history…</div>
              ) : historyList.length === 0 ? (
                <div className="py-8 text-center text-xs text-gray-500 italic">No past shifts recorded yet.</div>
              ) : (
                <div className="space-y-2 max-h-72 overflow-y-auto pr-1">
                  {historyList.map((h) => {
                    const diff = Number(h.difference || 0);
                    return (
                      <div
                        key={h.id}
                        className={`p-3 rounded-xl border text-xs flex items-center justify-between ${
                          isDark ? "bg-slate-800/40 border-slate-700" : "bg-gray-50 border-gray-200"
                        }`}
                      >
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="font-bold">Shift #{h.id}</span>
                            <span
                              className={`text-[10px] px-1.5 py-0.2 rounded ${
                                h.status === "open"
                                  ? "bg-emerald-500/10 text-emerald-500"
                                  : "bg-slate-500/10 text-slate-400"
                              }`}
                            >
                              {h.status}
                            </span>
                          </div>
                          <p className={`text-[11px] mt-0.5 ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                            {new Date(h.opened_at).toLocaleDateString()} {new Date(h.opened_at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                            {h.closed_at ? ` - ${new Date(h.closed_at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}` : " (Active)"}
                            {h.user?.name && ` • ${h.user.name}`}
                          </p>
                          {h.notes && (
                            <p className="text-[10px] italic text-gray-400 mt-0.5 line-clamp-1">{h.notes}</p>
                          )}
                        </div>

                        <div className="text-right">
                          <div className="font-bold">
                            {formatCurrency(h.closing_balance_actual ?? h.closing_balance_expected)}
                          </div>
                          {h.status === "closed" && (
                            <span
                              className={`text-[10px] font-semibold ${
                                diff === 0
                                  ? "text-emerald-500"
                                  : diff > 0
                                  ? "text-blue-500"
                                  : "text-rose-500"
                              }`}
                            >
                              {diff === 0
                                ? "Balanced"
                                : diff > 0
                                ? `+${formatCurrency(diff)}`
                                : `-${formatCurrency(Math.abs(diff))}`}
                            </span>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

