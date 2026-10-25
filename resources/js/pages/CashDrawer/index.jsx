// resources/js/pages/CashDrawer/index.jsx
// Full dedicated Cash Drawer page — session ledger, running balance, history, and drawer management.
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import {
  BanknotesIcon,
  ArrowUpCircleIcon,
  ArrowDownCircleIcon,
  ArrowPathIcon,
  PlusCircleIcon,
  MinusCircleIcon,
  LockClosedIcon,
  LockOpenIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  XMarkIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  ClockIcon,
  UserIcon,
  DocumentTextIcon,
  CurrencyDollarIcon,
  InformationCircleIcon,
} from "@heroicons/react/24/solid";
import { useTheme } from "@/context/ThemeContext";
import { useCurrency } from "@/context/CurrencyContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import { GlassCard, GlassSectionHeader, GlassInput } from "@/components";

// ─── Constants ────────────────────────────────────────────────────────────────

const CASH_IN_TYPES = ["cash_sale", "customer_payment", "purchase_return", "cash_in"];

const TYPE_LABELS = {
  cash_sale: "Sale (Cash)",
  customer_payment: "Customer Receipt",
  purchase_return: "Supplier Refund",
  cash_in: "Manual Cash In",
  cash_purchase: "Purchase (Cash)",
  cash_return: "Sale Return Refund",
  supplier_payment: "Supplier Payment",
  cash_out: "Manual Cash Out",
  expense: "Operating Expense",
};

const TYPE_ICONS = {
  cash_sale: ArrowUpCircleIcon,
  customer_payment: ArrowUpCircleIcon,
  purchase_return: ArrowUpCircleIcon,
  cash_in: PlusCircleIcon,
  cash_purchase: ArrowDownCircleIcon,
  cash_return: ArrowDownCircleIcon,
  supplier_payment: ArrowDownCircleIcon,
  cash_out: MinusCircleIcon,
  expense: MinusCircleIcon,
};

const MANUAL_TXN_OPTIONS = [
  { value: "cash_in", label: "Manual Cash In (Add to Drawer)" },
  { value: "cash_out", label: "Manual Cash Out (Remove from Drawer)" },
];

// ─── Helpers ──────────────────────────────────────────────────────────────────

function fmtDateTime(dt) {
  if (!dt) return "—";
  return new Date(dt).toLocaleString("en-PK", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
  });
}

function fmtDate(dt) {
  if (!dt) return "—";
  return new Date(dt).toLocaleDateString("en-PK", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
}

/** Attach a running `balance_after` to each transaction (mutates a copy). */
function attachRunningBalance(transactions, openingBalance) {
  let running = parseFloat(openingBalance) || 0;
  return transactions.map((txn) => {
    const amt = parseFloat(txn.amount) || 0;
    if (CASH_IN_TYPES.includes(txn.type)) {
      running += amt;
    } else {
      running -= amt;
    }
    return { ...txn, balance_after: running };
  });
}

// ─── Sub-components ───────────────────────────────────────────────────────────

/** KPI card used in the 4-grid summary row. */
function KpiCard({ label, value, sub, color = "text-slate-700 dark:text-slate-200", icon: Icon }) {
  return (
    <GlassCard className="p-4 flex flex-col gap-1">
      <div className="flex items-center gap-2 text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wide">
        {Icon && <Icon className="w-4 h-4" />}
        {label}
      </div>
      <div className={`text-2xl font-bold ${color}`}>{value}</div>
      {sub && <div className="text-xs text-slate-400">{sub}</div>}
    </GlassCard>
  );
}

/** A single transaction row in the ledger table. */
function TxnRow({ txn, index, formatCurrency, isDark }) {
  const isCashIn = CASH_IN_TYPES.includes(txn.type);
  const Icon = TYPE_ICONS[txn.type] || DocumentTextIcon;
  const label = TYPE_LABELS[txn.type] || txn.type;

  return (
    <tr className={`border-b transition-colors ${isDark ? "border-slate-700 hover:bg-slate-700/40" : "border-slate-100 hover:bg-slate-50"}`}>
      <td className="px-4 py-3 text-xs text-slate-500 text-center">{index}</td>
      <td className="px-4 py-3 text-xs text-slate-500 whitespace-nowrap">{fmtDateTime(txn.created_at)}</td>
      <td className="px-4 py-3">
        <div className="flex items-center gap-2">
          <Icon className={`w-4 h-4 flex-shrink-0 ${isCashIn ? "text-emerald-500" : "text-red-500"}`} />
          <span className={`text-sm font-medium ${isCashIn ? "text-emerald-600 dark:text-emerald-400" : "text-red-600 dark:text-red-400"}`}>
            {label}
          </span>
        </div>
      </td>
      <td className="px-4 py-3 text-sm text-slate-500 dark:text-slate-400">{txn.notes || "—"}</td>
      <td className="px-4 py-3 text-xs text-slate-400">
        {txn.reference_type ? (
          <span className="px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400">
            {txn.reference_type}#{txn.reference_id}
          </span>
        ) : "—"}
      </td>
      <td className="px-4 py-3 text-right font-semibold text-emerald-600 dark:text-emerald-400">
        {isCashIn ? `+${formatCurrency(txn.amount)}` : ""}
      </td>
      <td className="px-4 py-3 text-right font-semibold text-red-600 dark:text-red-400">
        {!isCashIn ? `−${formatCurrency(txn.amount)}` : ""}
      </td>
      <td className="px-4 py-3 text-right font-bold text-slate-700 dark:text-slate-200">
        {formatCurrency(txn.balance_after)}
      </td>
      <td className="px-4 py-3 text-xs text-slate-400">
        {txn.user?.name || "—"}
      </td>
    </tr>
  );
}

/** Inline ledger panel for a single session (used both in current and history). */
function SessionLedger({ session, summary, transactions, formatCurrency, isDark }) {
  if (!session) return null;

  const enriched = useMemo(
    () => attachRunningBalance(transactions || [], session.opening_balance),
    [transactions, session.opening_balance]
  );

  const isClosed = session.status === "closed";
  const diff = isClosed
    ? parseFloat(session.closing_balance_actual) - parseFloat(session.closing_balance_expected)
    : null;

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm min-w-[900px]">
        <thead>
          <tr className={`text-xs uppercase tracking-wide ${isDark ? "bg-slate-800 text-slate-400" : "bg-slate-50 text-slate-500"}`}>
            <th className="px-4 py-3 text-center w-10">#</th>
            <th className="px-4 py-3 text-left">Time</th>
            <th className="px-4 py-3 text-left">Type</th>
            <th className="px-4 py-3 text-left">Notes / Description</th>
            <th className="px-4 py-3 text-left">Reference</th>
            <th className="px-4 py-3 text-right">Cash In (+)</th>
            <th className="px-4 py-3 text-right">Cash Out (−)</th>
            <th className="px-4 py-3 text-right">Balance</th>
            <th className="px-4 py-3 text-left">By</th>
          </tr>
        </thead>
        <tbody>
          {/* Opening row */}
          <tr className={`border-b font-semibold ${isDark ? "border-slate-700 bg-slate-800/50 text-slate-300" : "border-slate-200 bg-blue-50 text-slate-700"}`}>
            <td className="px-4 py-3 text-center text-xs text-slate-400">—</td>
            <td className="px-4 py-3 text-xs">{fmtDateTime(session.opened_at)}</td>
            <td className="px-4 py-3" colSpan={3}>
              <div className="flex items-center gap-2">
                <LockOpenIcon className="w-4 h-4 text-blue-500" />
                <span className="text-blue-600 dark:text-blue-400">Drawer Opened</span>
              </div>
            </td>
            <td className="px-4 py-3" />
            <td className="px-4 py-3" />
            <td className="px-4 py-3 text-right font-bold">{formatCurrency(session.opening_balance)}</td>
            <td className="px-4 py-3 text-xs text-slate-400">{session.opened_by?.name || "—"}</td>
          </tr>

          {/* Transaction rows */}
          {enriched.length === 0 ? (
            <tr>
              <td colSpan={9} className="px-4 py-8 text-center text-slate-400 text-sm">
                No transactions recorded in this session.
              </td>
            </tr>
          ) : (
            enriched.map((txn, i) => (
              <TxnRow
                key={txn.id}
                txn={txn}
                index={i + 1}
                formatCurrency={formatCurrency}
                isDark={isDark}
              />
            ))
          )}

          {/* Closing row (only for closed sessions) */}
          {isClosed && (
            <tr className={`border-t-2 font-bold ${isDark ? "border-slate-600 bg-slate-800/60 text-slate-200" : "border-slate-300 bg-amber-50 text-slate-800"}`}>
              <td className="px-4 py-3 text-center text-xs text-slate-400">—</td>
              <td className="px-4 py-3 text-xs">{fmtDateTime(session.closed_at)}</td>
              <td className="px-4 py-3" colSpan={3}>
                <div className="flex items-center gap-2">
                  <LockClosedIcon className="w-4 h-4 text-amber-500" />
                  <span className="text-amber-600 dark:text-amber-400">Drawer Closed</span>
                  {diff !== null && (
                    <span className={`ml-2 text-xs px-2 py-0.5 rounded-full ${diff < 0 ? "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300" : diff > 0 ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300" : "bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-400"}`}>
                      {diff === 0 ? "Balanced" : diff > 0 ? `Surplus ${formatCurrency(Math.abs(diff))}` : `Short ${formatCurrency(Math.abs(diff))}`}
                    </span>
                  )}
                </div>
              </td>
              <td className="px-4 py-3 text-right text-xs">
                <div className="flex flex-col items-end gap-0.5">
                  <span className="text-slate-500">Expected:</span>
                  <span className="text-slate-500">Actual:</span>
                  <span className="text-slate-500">Diff:</span>
                </div>
              </td>
              <td className="px-4 py-3" />
              <td className="px-4 py-3 text-right">
                <div className="flex flex-col gap-0.5">
                  <span>{formatCurrency(session.closing_balance_expected)}</span>
                  <span className={isClosed ? (diff >= 0 ? "text-emerald-600 dark:text-emerald-400" : "text-red-600 dark:text-red-400") : ""}>
                    {formatCurrency(session.closing_balance_actual)}
                  </span>
                  <span className={diff < 0 ? "text-red-500" : diff > 0 ? "text-emerald-500" : "text-slate-400"}>
                    {diff !== null ? (diff >= 0 ? `+${formatCurrency(diff)}` : `−${formatCurrency(Math.abs(diff))}`) : "—"}
                  </span>
                </div>
              </td>
              <td className="px-4 py-3 text-xs text-slate-400">{session.closed_by?.name || "—"}</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

// ─── Modal: Open Drawer ────────────────────────────────────────────────────────

function OpenDrawerModal({ onClose, onSuccess, isDark, btnPrimary, btnSecondary }) {
  const [openingBalance, setOpeningBalance] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e) {
    e.preventDefault();
    setSaving(true);
    try {
      await axios.post("/api/cash-drawer/open", {
        opening_balance: parseFloat(openingBalance) || 0,
        notes,
      });
      toast.success("Drawer opened successfully.");
      onSuccess();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to open drawer.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ModalBackdrop onClose={onClose}>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <h2 className={`text-lg font-bold ${isDark ? "text-white" : "text-slate-800"}`}>
            Open Cash Drawer
          </h2>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600">
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Enter the opening (float) cash amount available in the drawer at the start of this session.
        </p>
        <div>
          <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
            Opening Balance
          </label>
          <GlassInput
            type="number"
            min="0"
            step="0.01"
            placeholder="0.00"
            value={openingBalance}
            onChange={(e) => setOpeningBalance(e.target.value)}
            autoFocus
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
            Notes (optional)
          </label>
          <textarea
            rows={2}
            placeholder="E.g. Morning float, shift change..."
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            className={`w-full px-3 py-2 rounded-lg border text-sm resize-none focus:outline-none focus:ring-2 focus:ring-blue-400 ${
              isDark
                ? "bg-slate-800 border-slate-600 text-white placeholder-slate-500"
                : "bg-white border-slate-300 text-slate-800 placeholder-slate-400"
            }`}
          />
        </div>
        <div className="flex justify-end gap-2 pt-2">
          <button type="button" onClick={onClose} className={`px-4 py-2.5 text-sm font-medium ${btnSecondary.className}`} style={btnSecondary.style}>
            Cancel
          </button>
          <button type="submit" disabled={saving} className={`px-4 py-2.5 text-sm font-semibold disabled:opacity-60 ${btnPrimary.className}`} style={btnPrimary.style}>
            {saving ? "Opening…" : "Open Drawer"}
          </button>
        </div>
      </form>
    </ModalBackdrop>
  );
}

// ─── Modal: Cash In / Out ─────────────────────────────────────────────────────

function TransactionModal({ onClose, onSuccess, isDark, btnSecondary, btnDanger, btnPrimary, selectStyles, defaultType = "cash_in" }) {
  const defaultOption = MANUAL_TXN_OPTIONS.find(o => o.value === defaultType) || MANUAL_TXN_OPTIONS[0];
  const [txnType, setTxnType] = useState(defaultOption);
  const [amount, setAmount] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e) {
    e.preventDefault();
    if (!amount || parseFloat(amount) <= 0) {
      toast.error("Please enter a valid amount.");
      return;
    }
    if (!notes.trim()) {
      toast.error("Notes are required for manual transactions.");
      return;
    }
    setSaving(true);
    try {
      await axios.post("/api/cash-drawer/transaction", {
        type: txnType.value,
        amount: parseFloat(amount),
        notes,
      });
      toast.success(`${txnType.label} recorded.`);
      onSuccess();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Transaction failed.");
    } finally {
      setSaving(false);
    }
  }

  const isCashIn = txnType?.value === "cash_in";
  const submitBtn = isCashIn ? btnPrimary : btnDanger;
  const SubmitIcon = isCashIn ? ArrowUpCircleIcon : ArrowDownCircleIcon;

  return (
    <ModalBackdrop onClose={onClose}>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4">
        {/* Header strip */}
        <div className={`flex items-center justify-between -mx-6 -mt-6 px-6 py-4 rounded-t-2xl ${isCashIn ? "bg-emerald-500" : "bg-red-500"}`}>
          <div className="flex items-center gap-3">
            {isCashIn
              ? <ArrowUpCircleIcon className="w-6 h-6 text-white" />
              : <ArrowDownCircleIcon className="w-6 h-6 text-white" />
            }
            <h2 className="text-base font-bold text-white">
              {isCashIn ? "Cash In" : "Cash Out"}
            </h2>
          </div>
          <button type="button" onClick={onClose} className="text-white/70 hover:text-white transition-colors">
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
            Transaction Type
          </label>
          <Select
            options={MANUAL_TXN_OPTIONS}
            value={txnType}
            onChange={setTxnType}
            styles={selectStyles}
            isSearchable={false}
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
            Amount
          </label>
          <GlassInput
            type="number"
            min="0.01"
            step="0.01"
            placeholder="0.00"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            autoFocus
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
            Notes / Reason <span className="text-red-500">*</span>
          </label>
          <textarea
            rows={3}
            required
            placeholder={isCashIn ? "E.g. Adding float for afternoon shift..." : "E.g. Petty cash for tea, stationery purchase..."}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            className={`w-full px-3 py-2 rounded-lg border text-sm resize-none focus:outline-none focus:ring-2 ${isCashIn ? "focus:ring-emerald-400" : "focus:ring-red-400"} ${
              isDark
                ? "bg-slate-800 border-slate-600 text-white placeholder-slate-500"
                : "bg-white border-slate-300 text-slate-800 placeholder-slate-400"
            }`}
          />
        </div>

        <div className="flex justify-end gap-2 pt-1">
          <button type="button" onClick={onClose} className={`px-4 py-2.5 text-sm font-medium ${btnSecondary.className}`} style={btnSecondary.style}>
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-semibold disabled:opacity-60 ${submitBtn.className}`}
            style={submitBtn.style}
          >
            <SubmitIcon className="w-4 h-4" />
            {saving ? "Saving…" : isCashIn ? "Confirm Cash In" : "Confirm Cash Out"}
          </button>
        </div>
      </form>
    </ModalBackdrop>
  );
}

// ─── Modal: Close Drawer ───────────────────────────────────────────────────────

function CloseDrawerModal({ expectedBalance, onClose, onSuccess, isDark, btnPrimary, btnSecondary, btnDanger, formatCurrency }) {
  const [actualBalance, setActualBalance] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);

  const diff = actualBalance !== ""
    ? parseFloat(actualBalance) - parseFloat(expectedBalance || 0)
    : null;

  async function handleSubmit(e) {
    e.preventDefault();
    setSaving(true);
    try {
      await axios.post("/api/cash-drawer/close", {
        closing_balance_actual: parseFloat(actualBalance) || 0,
        notes,
      });
      toast.success("Drawer closed and reconciled.");
      onSuccess();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to close drawer.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <ModalBackdrop onClose={onClose}>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <h2 className={`text-lg font-bold ${isDark ? "text-white" : "text-slate-800"}`}>
            Close & Reconcile Drawer
          </h2>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600">
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        {/* Expected summary */}
        <div className={`p-4 rounded-lg ${isDark ? "bg-slate-800" : "bg-slate-50"}`}>
          <div className="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-2">System Expected Balance</div>
          <div className="text-3xl font-bold text-blue-600 dark:text-blue-400">{formatCurrency(expectedBalance)}</div>
          <div className="text-xs text-slate-400 mt-1">Based on opening balance + all recorded transactions</div>
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
            Actual Physical Cash Count
          </label>
          <GlassInput
            type="number"
            min="0"
            step="0.01"
            placeholder="Count the cash in the drawer..."
            value={actualBalance}
            onChange={(e) => setActualBalance(e.target.value)}
            autoFocus
          />
        </div>

        {/* Live difference indicator */}
        {diff !== null && (
          <div className={`flex items-center gap-2 p-3 rounded-lg text-sm font-semibold ${
            diff === 0
              ? "bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-300"
              : diff > 0
                ? "bg-blue-50 dark:bg-blue-900/20 text-blue-700 dark:text-blue-300"
                : "bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-300"
          }`}>
            {diff === 0 ? (
              <><CheckCircleIcon className="w-5 h-5" /> Balanced — no discrepancy</>
            ) : diff > 0 ? (
              <><ExclamationTriangleIcon className="w-5 h-5" /> Surplus of {formatCurrency(Math.abs(diff))} (more than expected)</>
            ) : (
              <><ExclamationTriangleIcon className="w-5 h-5" /> Shortage of {formatCurrency(Math.abs(diff))} (less than expected)</>
            )}
          </div>
        )}

        <div>
          <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
            Closing Notes (optional)
          </label>
          <textarea
            rows={2}
            placeholder="E.g. End of day, shift handover..."
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            className={`w-full px-3 py-2 rounded-lg border text-sm resize-none focus:outline-none focus:ring-2 focus:ring-blue-400 ${
              isDark
                ? "bg-slate-800 border-slate-600 text-white placeholder-slate-500"
                : "bg-white border-slate-300 text-slate-800 placeholder-slate-400"
            }`}
          />
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" onClick={onClose} className={`px-4 py-2.5 text-sm font-medium ${btnSecondary.className}`} style={btnSecondary.style}>
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving || actualBalance === ""}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-semibold disabled:opacity-60 ${btnDanger.className}`}
            style={btnDanger.style}
          >
            <LockClosedIcon className="w-4 h-4" />
            {saving ? "Closing…" : "Close Drawer"}
          </button>
        </div>
      </form>
    </ModalBackdrop>
  );
}

// ─── Shared modal backdrop ─────────────────────────────────────────────────────

function ModalBackdrop({ onClose, children }) {
  // Close on Escape
  useEffect(() => {
    function onKey(e) { if (e.key === "Escape") onClose(); }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm px-4"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="bg-white dark:bg-slate-900 rounded-2xl shadow-2xl p-6 w-full max-w-md">
        {children}
      </div>
    </div>
  );
}

// ─── History Row (expandable) ─────────────────────────────────────────────────

function HistoryRow({ session, formatCurrency, isDark, selectStyles }) {
  const [expanded, setExpanded] = useState(false);
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(false);

  async function toggleExpand() {
    if (!expanded && !detail) {
      setLoading(true);
      try {
        const { data } = await axios.get(`/api/cash-drawer/session/${session.id}`);
        setDetail(data);
      } catch {
        toast.error("Failed to load session detail.");
      } finally {
        setLoading(false);
      }
    }
    setExpanded((v) => !v);
  }

  const isClosed = session.status === "closed";
  const diff = isClosed
    ? parseFloat(session.closing_balance_actual) - parseFloat(session.closing_balance_expected)
    : null;

  return (
    <>
      <tr
        className={`border-b cursor-pointer transition-colors ${isDark ? "border-slate-700 hover:bg-slate-700/40" : "border-slate-100 hover:bg-slate-50"} ${expanded ? (isDark ? "bg-slate-700/30" : "bg-blue-50/50") : ""}`}
        onClick={toggleExpand}
      >
        <td className="px-4 py-3 text-xs text-slate-400 font-mono">#{session.id}</td>
        <td className="px-4 py-3 text-sm text-slate-600 dark:text-slate-300">{fmtDate(session.opened_at)}</td>
        <td className="px-4 py-3 text-xs text-slate-500">{session.opened_by?.name || "—"}</td>
        <td className="px-4 py-3 text-right text-sm font-medium text-slate-700 dark:text-slate-200">
          {formatCurrency(session.opening_balance)}
        </td>
        <td className="px-4 py-3 text-right text-sm font-semibold text-emerald-600 dark:text-emerald-400">
          {formatCurrency(session.total_cash_in ?? 0)}
        </td>
        <td className="px-4 py-3 text-right text-sm font-semibold text-red-600 dark:text-red-400">
          {formatCurrency(session.total_cash_out ?? 0)}
        </td>
        <td className="px-4 py-3 text-right text-sm font-medium text-slate-700 dark:text-slate-200">
          {formatCurrency(session.closing_balance_expected ?? 0)}
        </td>
        <td className="px-4 py-3 text-right text-sm font-medium text-slate-700 dark:text-slate-200">
          {isClosed ? formatCurrency(session.closing_balance_actual) : "—"}
        </td>
        <td className="px-4 py-3 text-right text-sm font-semibold">
          {diff !== null ? (
            <span className={diff < 0 ? "text-red-500" : diff > 0 ? "text-emerald-500" : "text-slate-400"}>
              {diff >= 0 ? `+${formatCurrency(diff)}` : `−${formatCurrency(Math.abs(diff))}`}
            </span>
          ) : (
            <span className="text-slate-400">—</span>
          )}
        </td>
        <td className="px-4 py-3">
          <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold ${isClosed ? "bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300" : "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-300"}`}>
            {isClosed ? "Closed" : "Open"}
          </span>
        </td>
        <td className="px-4 py-3 text-center">
          {expanded ? <ChevronUpIcon className="w-4 h-4 text-blue-500 mx-auto" /> : <ChevronDownIcon className="w-4 h-4 text-slate-400 mx-auto" />}
        </td>
      </tr>
      {expanded && (
        <tr className={isDark ? "bg-slate-800/60" : "bg-blue-50/30"}>
          <td colSpan={11} className="p-0">
            <div className="border-t border-blue-200 dark:border-slate-700">
              {loading ? (
                <div className="flex items-center justify-center py-8 gap-2 text-slate-400 text-sm">
                  <ArrowPathIcon className="w-4 h-4 animate-spin" /> Loading session ledger…
                </div>
              ) : detail ? (
                <SessionLedger
                  session={detail.session}
                  summary={detail.summary}
                  transactions={detail.transactions}
                  formatCurrency={formatCurrency}
                  isDark={isDark}
                />
              ) : null}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

// ─── Main Page Component ───────────────────────────────────────────────────────

export default function CashDrawerPage() {
  const { isDark, theme } = useTheme();
  const { formatCurrency } = useCurrency();

  // ── Theme setup ────────────────────────────────────────────────────────────
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
    placeholder: (base) => ({ ...base, color: isDark ? "#94a3b8" : "#9ca3af" }),
    input: (base) => ({ ...base, color: isDark ? "#e2e8f0" : "#111827" }),
  }), [isDark]);

  // ── State ──────────────────────────────────────────────────────────────────
  const [currentData, setCurrentData] = useState(null); // { is_open, session, summary }
  const [history, setHistory] = useState([]);
  const [historyMeta, setHistoryMeta] = useState(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [loadingCurrent, setLoadingCurrent] = useState(true);
  const [loadingHistory, setLoadingHistory] = useState(false);

  const [showOpenModal, setShowOpenModal] = useState(false);
  const [showTxnModal, setShowTxnModal] = useState(null); // null | "cash_in" | "cash_out"
  const [showCloseModal, setShowCloseModal] = useState(false);

  // ── Data fetching ──────────────────────────────────────────────────────────
  const fetchCurrent = useCallback(async () => {
    setLoadingCurrent(true);
    try {
      const { data } = await axios.get("/api/cash-drawer/current");
      setCurrentData(data);
    } catch (err) {
      if (err?.response?.status !== 404) {
        toast.error("Failed to load cash drawer status.");
      }
      setCurrentData({ is_open: false, session: null, summary: null });
    } finally {
      setLoadingCurrent(false);
    }
  }, []);

  const fetchHistory = useCallback(async (page = 1) => {
    setLoadingHistory(true);
    try {
      const { data } = await axios.get(`/api/cash-drawer/history?per_page=10&page=${page}`);
      setHistory(data.data || []);
      setHistoryMeta(data.meta || data);
      setHistoryPage(page);
    } catch {
      toast.error("Failed to load session history.");
    } finally {
      setLoadingHistory(false);
    }
  }, []);

  useEffect(() => {
    fetchCurrent();
    fetchHistory(1);
  }, [fetchCurrent, fetchHistory]);

  function handleActionSuccess() {
    setShowOpenModal(false);
    setShowTxnModal(null);
    setShowCloseModal(false);
    fetchCurrent();
    fetchHistory(1);
  }

  // ── Derived state ──────────────────────────────────────────────────────────
  const isOpen = currentData?.is_open;
  const session = currentData?.session;
  const summary = currentData?.summary;
  const transactions = session?.transactions || [];

  const enrichedTxns = useMemo(
    () => attachRunningBalance(transactions, session?.opening_balance),
    [transactions, session?.opening_balance]
  );

  // ── Render ─────────────────────────────────────────────────────────────────
  return (
    <div className="space-y-6 pb-10">
      {/* ── Page Header ────────────────────────────────────────────────────── */}
      <GlassSectionHeader
        title="Cash Drawer"
        right={
          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={() => { fetchCurrent(); fetchHistory(historyPage); }}
              className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-medium ${btnSecondary.className}`}
              style={btnSecondary.style}
            >
              <ArrowPathIcon className="w-4 h-4" />
              Refresh
            </button>
            {isOpen && (
              <>
                <button
                  onClick={() => setShowTxnModal("cash_in")}
                  className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-semibold ${btnPrimary.className}`}
                  style={btnPrimary.style}
                >
                  <ArrowUpCircleIcon className="w-4 h-4" />
                  Cash In
                </button>
                <button
                  onClick={() => setShowTxnModal("cash_out")}
                  className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-semibold ${btnDanger.className}`}
                  style={btnDanger.style}
                >
                  <ArrowDownCircleIcon className="w-4 h-4" />
                  Cash Out
                </button>
                <button
                  onClick={() => setShowCloseModal(true)}
                  className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-medium ${btnSecondary.className}`}
                  style={btnSecondary.style}
                >
                  <LockClosedIcon className="w-4 h-4" />
                  Close Drawer
                </button>
              </>
            )}
            {!isOpen && !loadingCurrent && (
              <button
                onClick={() => setShowOpenModal(true)}
                className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-semibold ${btnPrimary.className}`}
                style={btnPrimary.style}
              >
                <LockOpenIcon className="w-4 h-4" />
                Open Drawer
              </button>
            )}
          </div>
        }
      />

      {/* ── Loading skeleton ────────────────────────────────────────────────── */}
      {loadingCurrent && (
        <div className="flex items-center justify-center py-16 text-slate-400 gap-2">
          <ArrowPathIcon className="w-5 h-5 animate-spin" />
          <span className="text-sm">Loading drawer status…</span>
        </div>
      )}

      {/* ── Status banner ───────────────────────────────────────────────────── */}
      {!loadingCurrent && (
        <div className={`flex items-center gap-3 px-5 py-4 rounded-xl border text-sm font-medium ${
          isOpen
            ? "bg-emerald-50 dark:bg-emerald-900/20 border-emerald-200 dark:border-emerald-800 text-emerald-800 dark:text-emerald-300"
            : "bg-amber-50 dark:bg-amber-900/20 border-amber-200 dark:border-amber-800 text-amber-800 dark:text-amber-300"
        }`}>
          {isOpen ? (
            <>
              <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse flex-shrink-0" />
              <span className="font-bold text-base">OPEN</span>
              <span className="text-slate-500 dark:text-slate-400 font-normal">
                — Opened {fmtDateTime(session?.opened_at)}
                {session?.opened_by?.name ? ` by ${session.opened_by.name}` : ""}
              </span>
            </>
          ) : (
            <>
              <span className="w-2.5 h-2.5 rounded-full bg-amber-500 flex-shrink-0" />
              <span className="font-bold text-base">CLOSED</span>
              <span className="text-slate-500 dark:text-slate-400 font-normal">
                — No active drawer session. Click <strong>Open Drawer</strong> to start a new session.
              </span>
            </>
          )}
        </div>
      )}

      {/* ── KPI Cards ───────────────────────────────────────────────────────── */}
      {!loadingCurrent && session && (
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <KpiCard
            label="Opening Balance"
            value={formatCurrency(session.opening_balance)}
            sub={`Session started ${fmtDate(session.opened_at)}`}
            icon={BanknotesIcon}
          />
          <KpiCard
            label="Total Cash In"
            value={formatCurrency(summary?.total_cash_in ?? 0)}
            sub={`${enrichedTxns.filter(t => CASH_IN_TYPES.includes(t.type)).length} transactions`}
            color="text-emerald-600 dark:text-emerald-400"
            icon={ArrowUpCircleIcon}
          />
          <KpiCard
            label="Total Cash Out"
            value={formatCurrency(summary?.total_cash_out ?? 0)}
            sub={`${enrichedTxns.filter(t => !CASH_IN_TYPES.includes(t.type)).length} transactions`}
            color="text-red-600 dark:text-red-400"
            icon={ArrowDownCircleIcon}
          />
          <KpiCard
            label="Expected Balance"
            value={formatCurrency(summary?.expected_closing_balance ?? (parseFloat(session.opening_balance) + (summary?.total_cash_in ?? 0) - (summary?.total_cash_out ?? 0)))}
            sub={isOpen ? "Current drawer balance" : `Actual: ${formatCurrency(session.closing_balance_actual)}`}
            color="text-blue-600 dark:text-blue-400"
            icon={CurrencyDollarIcon}
          />
        </div>
      )}

      {/* ── Current Session Ledger ───────────────────────────────────────────── */}
      {!loadingCurrent && session && (
        <GlassCard className="p-0 overflow-hidden">
          <div className={`px-5 py-4 border-b flex items-center justify-between ${isDark ? "border-slate-700" : "border-slate-100"}`}>
            <div>
              <h3 className={`font-semibold ${isDark ? "text-white" : "text-slate-800"}`}>
                {isOpen ? "Current Session — Transaction Ledger" : `Session #${session.id} — Transaction Ledger`}
              </h3>
              <p className="text-xs text-slate-400 mt-0.5">
                {enrichedTxns.length} transactions · Opening balance {formatCurrency(session.opening_balance)}
              </p>
            </div>
            <DocumentTextIcon className="w-5 h-5 text-slate-300 dark:text-slate-600" />
          </div>
          <SessionLedger
            session={session}
            summary={summary}
            transactions={transactions}
            formatCurrency={formatCurrency}
            isDark={isDark}
          />
        </GlassCard>
      )}

      {/* ── Past Sessions History ────────────────────────────────────────────── */}
      <GlassCard className="p-0 overflow-hidden">
        <div className={`px-5 py-4 border-b flex items-center justify-between ${isDark ? "border-slate-700" : "border-slate-100"}`}>
          <div>
            <h3 className={`font-semibold ${isDark ? "text-white" : "text-slate-800"}`}>Session History</h3>
            <p className="text-xs text-slate-400 mt-0.5">All past drawer sessions — click a row to expand the full ledger</p>
          </div>
          <ClockIcon className="w-5 h-5 text-slate-300 dark:text-slate-600" />
        </div>

        {loadingHistory ? (
          <div className="flex items-center justify-center py-12 gap-2 text-slate-400 text-sm">
            <ArrowPathIcon className="w-4 h-4 animate-spin" /> Loading history…
          </div>
        ) : history.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-16 gap-2 text-slate-400">
            <BanknotesIcon className="w-10 h-10 opacity-20" />
            <p className="text-sm">No past sessions found.</p>
          </div>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full text-sm min-w-[900px]">
                <thead>
                  <tr className={`text-xs uppercase tracking-wide ${isDark ? "bg-slate-800 text-slate-400" : "bg-slate-50 text-slate-500"}`}>
                    <th className="px-4 py-3 text-left">Session</th>
                    <th className="px-4 py-3 text-left">Date</th>
                    <th className="px-4 py-3 text-left">Opened By</th>
                    <th className="px-4 py-3 text-right">Opening</th>
                    <th className="px-4 py-3 text-right">Cash In</th>
                    <th className="px-4 py-3 text-right">Cash Out</th>
                    <th className="px-4 py-3 text-right">Expected</th>
                    <th className="px-4 py-3 text-right">Actual</th>
                    <th className="px-4 py-3 text-right">Diff</th>
                    <th className="px-4 py-3 text-center">Status</th>
                    <th className="px-4 py-3 text-center w-10"></th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((s) => (
                    <HistoryRow
                      key={s.id}
                      session={s}
                      formatCurrency={formatCurrency}
                      isDark={isDark}
                      selectStyles={selectStyles}
                    />
                  ))}
                </tbody>
              </table>
            </div>

            {/* Pagination */}
            {historyMeta && historyMeta.last_page > 1 && (
              <div className={`px-5 py-3 border-t flex items-center justify-between text-sm ${isDark ? "border-slate-700" : "border-slate-100"}`}>
                <span className="text-slate-400">
                  Page {historyMeta.current_page} of {historyMeta.last_page}
                </span>
                <div className="flex gap-2">
                  <button
                    disabled={historyMeta.current_page <= 1}
                    onClick={() => fetchHistory(historyMeta.current_page - 1)}
                    className={`px-4 py-2.5 text-sm font-medium disabled:opacity-40 ${btnSecondary.className}`}
                    style={btnSecondary.style}
                  >
                    Previous
                  </button>
                  <button
                    disabled={historyMeta.current_page >= historyMeta.last_page}
                    onClick={() => fetchHistory(historyMeta.current_page + 1)}
                    className={`px-4 py-2.5 text-sm font-medium disabled:opacity-40 ${btnPrimary.className}`}
                    style={btnPrimary.style}
                  >
                    Next
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </GlassCard>

      {/* ── Modals ─────────────────────────────────────────────────────────── */}
      {showOpenModal && (
        <OpenDrawerModal
          isDark={isDark}
          btnPrimary={btnPrimary}
          btnSecondary={btnSecondary}
          onClose={() => setShowOpenModal(false)}
          onSuccess={handleActionSuccess}
        />
      )}
      {showTxnModal && (
        <TransactionModal
          isDark={isDark}
          btnSecondary={btnSecondary}
          btnDanger={btnDanger}
          btnPrimary={btnPrimary}
          selectStyles={selectStyles}
          defaultType={showTxnModal}
          onClose={() => setShowTxnModal(null)}
          onSuccess={handleActionSuccess}
        />
      )}
      {showCloseModal && (
        <CloseDrawerModal
          isDark={isDark}
          btnPrimary={btnPrimary}
          btnSecondary={btnSecondary}
          btnDanger={btnDanger}
          formatCurrency={formatCurrency}
          expectedBalance={summary?.expected_closing_balance ?? 0}
          onClose={() => setShowCloseModal(false)}
          onSuccess={handleActionSuccess}
        />
      )}
    </div>
  );
}

