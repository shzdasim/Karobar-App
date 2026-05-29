import React, { useEffect, useMemo, useState, useCallback } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions.js";

import { BanknotesIcon, ArrowPathIcon, PrinterIcon } from "@heroicons/react/24/solid";

const normalizeList = (payload) => {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.data)) return payload.data;
  return [];
};

export default function BankLedgerPage() {
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function"
        ? canFor("bank.ledger")
        : { view: false, create: false, update: false, delete: false }),
    [canFor]
  );

  const [banks, setBanks] = useState([]);
  const [bankId, setBankId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const [rows, setRows] = useState([]);
  const [summary, setSummary] = useState({ total_in: 0, total_out: 0, net: 0 });
  const [loading, setLoading] = useState(false);

  const fetchBanks = useCallback(async () => {
    try {
      const { data } = await axios.get("/api/banks");
      setBanks(normalizeList(data));
    } catch (e) {
      toast.error(e?.response?.data?.message || "Failed to load banks");
      setBanks([]);
    }
  }, []);

  const fetchLedger = useCallback(async () => {
    if (!can?.view) return;
    try {
      setLoading(true);
      const { data } = await axios.get("/api/bank-ledger", {
        params: {
          bank_id: bankId || undefined,
          from: from || undefined,
          to: to || undefined,
        },
      });
      setRows(Array.isArray(data?.data) ? data.data : []);
      setSummary(data?.summary || { total_in: 0, total_out: 0, net: 0 });
    } catch (e) {
      toast.error(e?.response?.data?.message || "Failed to load ledger");
      setRows([]);
      setSummary({ total_in: 0, total_out: 0, net: 0 });
    } finally {
      setLoading(false);
    }
  }, [can?.view, bankId, from, to]);

  useEffect(() => {
    if (permsLoading) return;
    if (!can?.view) return;
    fetchBanks();
  }, [permsLoading, can?.view, fetchBanks]);

  useEffect(() => {
    if (permsLoading) return;
    if (!can?.view) return;
    fetchLedger();
  }, [permsLoading, can?.view, fetchLedger]);

  const print = () => {
    if (!can?.view) return;
    const qs = new URLSearchParams();
    if (bankId) qs.set("bank_id", bankId);
    if (from) qs.set("from", from);
    if (to) qs.set("to", to);
    window.open(`/bank-ledger/print?${qs.toString()}`, "_blank", "noopener");
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can?.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view bank ledger.</div>;

  return (
    <div className="p-4 space-y-3">
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg shadow-sm" style={{ background: "linear-gradient(to bottom right, #3b82f6, #2563eb)" }}>
              <BanknotesIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Bank Ledger</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">View transactions by bank & date range</p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={fetchLedger}
              disabled={loading}
              className="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium bg-white/60 dark:bg-slate-700/60 border border-gray-200/60 dark:border-slate-600/40"
            >
              <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
              {loading ? "Loading…" : "Refresh"}
            </button>
            <button
              onClick={print}
              className="inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-semibold bg-slate-900 text-white dark:bg-slate-200 dark:text-slate-900"
            >
              <PrinterIcon className="w-4 h-4" />
              Print
            </button>
          </div>
        </div>

        <div className="px-4 py-3">
          <div className="grid grid-cols-1 md:grid-cols-4 gap-3 items-end">
            <label className="block">
              <span className="text-xs text-gray-500 dark:text-gray-300">Bank</span>
              <select
                value={bankId}
                onChange={(e) => setBankId(e.target.value)}
                className="mt-1 w-full h-10 px-3 rounded border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-sm"
              >
                <option value="">All</option>
                {banks.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.bank_name}
                  </option>
                ))}
              </select>
            </label>
            <label className="block">
              <span className="text-xs text-gray-500 dark:text-gray-300">From</span>
              <input
                type="date"
                value={from}
                onChange={(e) => setFrom(e.target.value)}
                className="mt-1 w-full h-10 px-3 rounded border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-sm"
              />
            </label>
            <label className="block">
              <span className="text-xs text-gray-500 dark:text-gray-300">To</span>
              <input
                type="date"
                value={to}
                onChange={(e) => setTo(e.target.value)}
                className="mt-1 w-full h-10 px-3 rounded border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-sm"
              />
            </label>
            <button
              onClick={fetchLedger}
              className="h-10 rounded bg-blue-600 text-white font-semibold"
            >
              Apply
            </button>
          </div>
        </div>

        <div className="px-4 pb-3">
          <div className="grid grid-cols-3 gap-3">
            <div className="rounded-lg bg-slate-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-700 p-3">
              <div className="text-xs text-gray-500">Total In</div>
              <div className="text-sm font-bold text-emerald-600">{Number(summary.total_in || 0).toFixed(2)}</div>
            </div>
            <div className="rounded-lg bg-slate-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-700 p-3">
              <div className="text-xs text-gray-500">Total Out</div>
              <div className="text-sm font-bold text-rose-600">{Number(summary.total_out || 0).toFixed(2)}</div>
            </div>
            <div className="rounded-lg bg-slate-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-700 p-3">
              <div className="text-xs text-gray-500">Net</div>
              <div className="text-sm font-bold text-blue-600">{Number(summary.net || 0).toFixed(2)}</div>
            </div>
          </div>
        </div>
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
          <div className="text-sm font-semibold text-gray-700 dark:text-gray-200">Ledger Entries</div>
          <div className="text-xs text-gray-500">{rows.length} rows</div>
        </div>
        <div className="overflow-auto max-h-[65vh]">
          <table className="w-full text-xs">
            <thead className="sticky top-0 bg-gray-50 dark:bg-slate-900">
              <tr className="text-left">
                <th className="px-4 py-2">Date</th>
                <th className="px-4 py-2">Type</th>
                <th className="px-4 py-2">Ref</th>
                <th className="px-4 py-2">Direction</th>
                <th className="px-4 py-2 text-right">Amount</th>
                <th className="px-4 py-2">Description</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && !loading ? (
                <tr>
                  <td colSpan={6} className="px-4 py-14 text-center text-gray-500">No ledger entries</td>
                </tr>
              ) : null}
              {rows.map((r) => (
                <tr key={r.id} className="border-b border-gray-100 dark:border-slate-800">
                  <td className="px-4 py-2">{r.entry_date || "—"}</td>
                  <td className="px-4 py-2">{r.entry_type}</td>
                  <td className="px-4 py-2">{r.ref_id ? `${r.ref_type || "ref"} #${r.ref_id}` : "—"}</td>
                  <td className="px-4 py-2">
                    <span className={r.direction === "credit" ? "text-emerald-600 font-semibold" : "text-rose-600 font-semibold"}>
                      {r.direction}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-right font-semibold">
                    {Number(r.amount || 0).toFixed(2)}
                  </td>
                  <td className="px-4 py-2 text-gray-700 dark:text-gray-200">{r.description || ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

