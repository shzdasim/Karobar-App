import React, { useCallback, useEffect, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link } from "react-router-dom";
import {
  ArrowPathIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
} from "@heroicons/react/24/solid";
import { useTheme } from "@/context/ThemeContext";
import { useCurrency } from "@/context/CurrencyContext";
import { usePermissions } from "@/api/usePermissions.js";

/**
 * Shared Cash Drawer <-> Cash in Hand (Financial Accounts) Parity & 1-Click Sync Banner.
 * Mounted across CashDrawer page, CashDrawerModal, Accounts page, AccountsModal, and Accounting page
 * so users can always inspect parity, close extra open shifts, and sync Cash in Hand in one click.
 */
export default function CashDrawerParityBanner({
  compact = false,
  currentSessionId = null,
  showWhenBalanced = true,
  showAccountsLink = false,
  showDrawerLink = false,
  onSynced = null,
  onOpenDrawer = null,
  refreshKey = 0,
}) {
  const { isDark } = useTheme();
  const { formatCurrency } = useCurrency();
  const { has } = usePermissions();

  const canCloseShift = has("cash-drawer.close");
  const canReconcile =
    canCloseShift || has("account.adjust") || has("account.update");

  const [recon, setRecon] = useState(null);
  const [loading, setLoading] = useState(false);
  const [confirmingSync, setConfirmingSync] = useState(false);
  const [reconciling, setReconciling] = useState(false);
  const [closingShiftId, setClosingShiftId] = useState(null);
  const [confirmCloseId, setConfirmCloseId] = useState(null);

  const fetchRecon = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await axios.get("/api/cash-drawer/reconciliation-check");
      setRecon(data);
    } catch {
      setRecon(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchRecon();
  }, [fetchRecon, refreshKey]);

  useEffect(() => {
    const onRefresh = () => {
      fetchRecon();
    };
    window.addEventListener("cash-drawer-changed", onRefresh);
    window.addEventListener("accounts-changed", onRefresh);
    return () => {
      window.removeEventListener("cash-drawer-changed", onRefresh);
      window.removeEventListener("accounts-changed", onRefresh);
    };
  }, [fetchRecon]);

  const broadcastSync = () => {
    window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
    window.dispatchEvent(new CustomEvent("accounts-changed"));
    try {
      localStorage.setItem("cash_drawer_sync", String(Date.now()));
    } catch {}
    try {
      localStorage.setItem("accounts_sync", String(Date.now()));
    } catch {}
  };

  const executeReconcile = async () => {
    if (!recon || reconciling) return;
    setReconciling(true);
    try {
      const isClosedMode = Number(recon.open_sessions_count || 0) === 0;
      const payload = isClosedMode
        ? {
            sync_to_last_closed: true,
            reason:
              "Reconciled Cash in Hand to match Last Closed Cash Drawer shift",
          }
        : {
            reason:
              "Reconciled Cash in Hand to match active Cash Drawer balance",
          };
      const { data } = await axios.post("/api/cash-drawer/reconcile", payload);
      toast.success(data?.message || "Cash in Hand synced with Cash Drawer.");
      setConfirmingSync(false);
      await fetchRecon();
      broadcastSync();
      if (onSynced) await onSynced(data);
    } catch (err) {
      toast.error(
        err?.response?.data?.message || "Failed to sync Cash in Hand."
      );
    } finally {
      setReconciling(false);
    }
  };

  const executeCloseShift = async (sess) => {
    if (!sess?.session_id || closingShiftId) return;
    setClosingShiftId(sess.session_id);
    try {
      const expected = Number(sess.calculated_expected || 0);
      await axios.post("/api/cash-drawer/close", {
        session_id: sess.session_id,
        closing_balance_actual: expected,
        notes: "Closed extra/previous open shift from Cash Drawer parity panel",
      });
      toast.success(`Closed Shift #${sess.session_id}.`);
      setConfirmCloseId(null);
      await fetchRecon();
      broadcastSync();
      if (onSynced) await onSynced();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to close shift.");
    } finally {
      setClosingShiftId(null);
    }
  };

  if (!recon) return null;

  const openCount = Number(recon.open_sessions_count || 0);
  const lastClosed = recon.last_closed_session || null;
  const hasComparableDrawer = openCount > 0 || Boolean(lastClosed);
  if (!hasComparableDrawer) return null;

  const cashBal = Number(recon.cash_account_balance || 0);
  const targetBal =
    openCount > 0
      ? Number(recon.open_drawers_expected_total || 0)
      : Number(lastClosed?.closing_balance_actual || 0);
  const diff = Number(recon.difference || 0);
  const hasMoneyDiff = Math.abs(diff) >= 0.01;
  const duplicateSinglePc = Boolean(recon.duplicate_single_pc_open);
  const isBalanced = Boolean(recon.is_balanced) && !hasMoneyDiff && !duplicateSinglePc;
  const otherCashAccounts = Array.isArray(recon.other_cash_accounts)
    ? recon.other_cash_accounts
    : [];
  const otherCashTotal = Number(recon.other_cash_accounts_total || 0);

  if (isBalanced) {
    if (!showWhenBalanced) return null;
    return (
      <div
        className={`flex flex-wrap items-center justify-between gap-2 ${
          compact ? "px-3 py-2" : "px-4 py-2.5"
        } rounded-xl border text-xs ${
          isDark
            ? "bg-emerald-950/25 border-emerald-800/50 text-emerald-300"
            : "bg-emerald-50/80 border-emerald-200 text-emerald-800"
        }`}
      >
        <div className="flex flex-wrap items-center gap-2">
          <CheckCircleIcon className="w-4 h-4 text-emerald-500 shrink-0" />
          <span>
            <strong>Cash Drawer &amp; Cash in Hand are Equal</strong> —{" "}
            <span className="font-mono font-bold">
              {formatCurrency(cashBal)}
            </span>
            {openCount > 1 && ` across ${openCount} open counters`}
            {openCount === 0 && lastClosed && ` (Last Closed Shift #${lastClosed.session_id})`}
          </span>
          {otherCashAccounts.length > 0 && (
            <span className="opacity-80">
              • +{otherCashAccounts.length} separate cash account
              {otherCashAccounts.length > 1 ? "s" : ""} (
              {formatCurrency(otherCashTotal)})
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={fetchRecon}
            disabled={loading}
            title="Re-check parity"
            className="p-1 rounded hover:bg-emerald-500/10"
          >
            <ArrowPathIcon
              className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`}
            />
          </button>
          {showAccountsLink && (
            <Link to="/accounts" className="font-semibold underline hover:opacity-80">
              Financial Accounts
            </Link>
          )}
          {showDrawerLink && (
            onOpenDrawer ? (
              <button
                type="button"
                onClick={onOpenDrawer}
                className="font-semibold underline hover:opacity-80"
              >
                Cash Drawer
              </button>
            ) : (
              <Link to="/cash-drawer" className="font-semibold underline hover:opacity-80">
                Cash Drawer
              </Link>
            )
          )}
        </div>
      </div>
    );
  }

  const openSessions = Array.isArray(recon.open_sessions)
    ? recon.open_sessions
    : [];
  const syncLabel =
    openCount > 0
      ? "Sync Cash in Hand to Drawer"
      : "Sync Cash in Hand to Last Closed Shift";

  return (
    <div
      className={`${
        compact ? "p-3.5" : "p-4"
      } rounded-xl border border-l-4 border-l-amber-500 ${
        isDark
          ? "bg-amber-950/25 border-amber-800/60 text-slate-100"
          : "bg-amber-50/90 border-amber-300 text-slate-900"
      }`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1.5 flex-1 min-w-[240px]">
          <div className="flex items-center gap-2 text-amber-800 dark:text-amber-300 font-bold text-xs sm:text-sm">
            <ExclamationTriangleIcon className="w-5 h-5 text-amber-500 shrink-0" />
            {duplicateSinglePc
              ? `Duplicate Open Shifts Detected (${openCount} open shifts on this PC)`
              : openCount > 0
                ? "Cash Drawer & Cash in Hand (Accounts) Mismatch"
                : "Cash in Hand differs from Last Closed Cash Drawer"}
          </div>

          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
            <span className="text-slate-700 dark:text-slate-300">
              Cash in Hand (Accounts):{" "}
              <strong className="font-mono text-slate-900 dark:text-white">
                {formatCurrency(cashBal)}
              </strong>
            </span>
            <span className="text-slate-700 dark:text-slate-300">
              {openCount > 0
                ? `Open Cash Drawer${openCount > 1 ? `s (${openCount})` : ""}`
                : `Last Closed Shift #${lastClosed?.session_id}`}
              :{" "}
              <strong className="font-mono text-blue-600 dark:text-blue-400">
                {formatCurrency(targetBal)}
              </strong>
            </span>
            {hasMoneyDiff && (
              <span className="text-red-600 dark:text-red-400 font-bold">
                Difference:{" "}
                <span className="font-mono">
                  {diff >= 0 ? "+" : "−"}
                  {formatCurrency(Math.abs(diff))}
                </span>
              </span>
            )}
          </div>

          <p className="text-[11px] sm:text-xs text-slate-600 dark:text-slate-400 leading-relaxed">
            {duplicateSinglePc
              ? "Two shifts are open at the same time on this PC. Close the extra/older shift below, then click Sync Cash in Hand to Drawer so both balances match."
              : openCount > 0
                ? "This happens after a previous double-opened drawer, opening a new shift before closing the previous one, or cash entries posted while the drawer was closed. Click Sync Cash in Hand to Drawer to align both balances with an audited reconciliation entry."
                : "Your Cash in Hand account differs from the actual cash counted in your last closed shift. Click Sync Cash in Hand to align it before opening your next shift."}
          </p>

          {otherCashAccounts.length > 0 && (
            <p className="text-[11px] text-amber-800 dark:text-amber-300 font-medium">
              Note: You also have {otherCashAccounts.length} separate cash account
              {otherCashAccounts.length > 1 ? "s" : ""} (
              {otherCashAccounts
                .map((a) => `${a.name}: ${formatCurrency(a.current_balance)}`)
                .join(", ")}
              ) — those are tracked separately from the Cash Drawer.
            </p>
          )}

          {openSessions.length > 0 && (
            <div className="pt-1 flex flex-wrap gap-2">
              {openSessions.map((os) => {
                const isOtherOrExtra =
                  openSessions.length > 1 ||
                  (currentSessionId != null &&
                    Number(os.session_id) !== Number(currentSessionId));
                return (
                  <div
                    key={os.session_id}
                    className={`inline-flex flex-wrap items-center gap-2 px-2.5 py-1.5 rounded-lg border text-xs ${
                      isDark
                        ? "bg-slate-900/70 border-slate-700 text-slate-200"
                        : "bg-white border-amber-200 text-slate-700"
                    }`}
                  >
                    <span>
                      <strong>Shift #{os.session_id}</strong>
                      {os.user_name ? ` · ${os.user_name}` : ""}
                      {os.terminal_name ? ` (${os.terminal_name})` : ""}
                      {" · "}
                      <strong className="font-mono">
                        {formatCurrency(os.calculated_expected)}
                      </strong>
                    </span>
                    {canCloseShift && isOtherOrExtra && (
                      confirmCloseId === os.session_id ? (
                        <span className="inline-flex items-center gap-1">
                          <button
                            type="button"
                            disabled={closingShiftId === os.session_id}
                            onClick={() => executeCloseShift(os)}
                            className="px-2 py-0.5 rounded bg-red-600 text-white font-bold hover:bg-red-500 disabled:opacity-60"
                          >
                            {closingShiftId === os.session_id
                              ? "Closing…"
                              : "Confirm Close"}
                          </button>
                          <button
                            type="button"
                            onClick={() => setConfirmCloseId(null)}
                            className="px-1.5 py-0.5 rounded border border-slate-400/50 text-[11px]"
                          >
                            Cancel
                          </button>
                        </span>
                      ) : (
                        <button
                          type="button"
                          onClick={() => setConfirmCloseId(os.session_id)}
                          className="px-2 py-0.5 rounded bg-red-600 text-white font-semibold hover:bg-red-500"
                        >
                          Close Shift
                        </button>
                      )
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-2 shrink-0">
          {canReconcile && hasMoneyDiff && (
            confirmingSync ? (
              <div
                className={`p-2.5 rounded-xl border text-xs space-y-2 max-w-xs ${
                  isDark
                    ? "bg-slate-900 border-amber-700 text-slate-200"
                    : "bg-white border-amber-300 text-slate-800"
                }`}
              >
                <div className="font-bold text-amber-700 dark:text-amber-300">
                  Confirm Reconciliation Entry
                </div>
                <div className="text-[11px] leading-snug">
                  Adjust Cash in Hand from{" "}
                  <strong className="font-mono">{formatCurrency(cashBal)}</strong>{" "}
                  to{" "}
                  <strong className="font-mono">
                    {formatCurrency(targetBal)}
                  </strong>{" "}
                  ({diff > 0 ? "−" : "+"}
                  {formatCurrency(Math.abs(diff))})?
                </div>
                <div className="flex items-center gap-1.5">
                  <button
                    type="button"
                    disabled={reconciling}
                    onClick={executeReconcile}
                    className="px-3 py-1.5 rounded-lg bg-amber-600 text-white font-bold hover:bg-amber-500 disabled:opacity-60"
                  >
                    {reconciling ? "Syncing…" : "Yes, Sync Now"}
                  </button>
                  <button
                    type="button"
                    disabled={reconciling}
                    onClick={() => setConfirmingSync(false)}
                    className={`px-2.5 py-1.5 rounded-lg border font-semibold ${
                      isDark
                        ? "border-slate-700 text-slate-300 hover:bg-slate-800"
                        : "border-slate-300 text-slate-700 hover:bg-slate-100"
                    }`}
                  >
                    Cancel
                  </button>
                </div>
              </div>
            ) : (
              <button
                type="button"
                disabled={reconciling}
                onClick={() => setConfirmingSync(true)}
                className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold bg-amber-600 text-white hover:bg-amber-500 disabled:opacity-60 shadow-sm transition-colors cursor-pointer"
              >
                <ArrowPathIcon
                  className={`w-4 h-4 ${reconciling ? "animate-spin" : ""}`}
                />
                {syncLabel}
              </button>
            )
          )}

          {showAccountsLink && (
            <Link
              to="/accounts"
              className={`inline-flex items-center gap-1 px-3 py-2 rounded-xl text-xs font-semibold border transition-colors ${
                isDark
                  ? "border-slate-700 bg-slate-800 text-slate-200 hover:bg-slate-700"
                  : "border-amber-300 bg-white text-slate-800 hover:bg-amber-50"
              }`}
            >
              Open Accounts
            </Link>
          )}
          {showDrawerLink && (
            onOpenDrawer ? (
              <button
                type="button"
                onClick={onOpenDrawer}
                className={`inline-flex items-center gap-1 px-3 py-2 rounded-xl text-xs font-semibold border transition-colors ${
                  isDark
                    ? "border-slate-700 bg-slate-800 text-slate-200 hover:bg-slate-700"
                    : "border-amber-300 bg-white text-slate-800 hover:bg-amber-50"
                }`}
              >
                Open Cash Drawer
              </button>
            ) : (
              <Link
                to="/cash-drawer"
                className={`inline-flex items-center gap-1 px-3 py-2 rounded-xl text-xs font-semibold border transition-colors ${
                  isDark
                    ? "border-slate-700 bg-slate-800 text-slate-200 hover:bg-slate-700"
                    : "border-amber-300 bg-white text-slate-800 hover:bg-amber-50"
                }`}
              >
                Open Cash Drawer
              </Link>
            )
          )}
        </div>
      </div>
    </div>
  );
}
