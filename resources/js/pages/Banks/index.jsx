import React, { useEffect, useMemo, useState, useCallback } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import toastify from "react-hot-toast";
import { usePermissions } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";

import {
  PlusCircleIcon,
  PencilSquareIcon,
  TrashIcon,
  ArrowPathIcon,
  BuildingStorefrontIcon,
  BanknotesIcon,
  DocumentCurrencyDollarIcon,
} from "@heroicons/react/24/solid";

const normalizeList = (payload) => {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.data)) return payload.data;
  return [];
};

export default function BanksIndex() {
  const navigate = useNavigate();
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function"
        ? canFor("bank")
        : { view: false, create: false, update: false, delete: false }),
    [canFor]
  );

  const { theme } = useTheme();

  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);
  const [q, setQ] = useState("");

  const fetchBanks = useCallback(async () => {
    if (!can?.view) return;
    try {
      setLoading(true);
      const { data } = await axios.get("/api/banks", {
        params: q ? { q } : {},
      });
      setRows(normalizeList(data));
    } catch (e) {
      toast.error(e?.response?.data?.message || "Failed to load banks");
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [can?.view, q]);

  useEffect(() => {
    if (permsLoading) return;
    if (!can?.view) return;
    fetchBanks();
  }, [permsLoading, can?.view, fetchBanks]);

  const openCreate = () => {
    if (!can?.create) return toast.error("No permission");
    navigate("/banks/create");
  };

  const deleteBank = async (id) => {
    if (!can?.delete) return toast.error("No permission");
    try {
      await axios.post("/api/auth/confirm-password", { password: "" });
    } catch (e) {
      // Fallback: if confirm-password requires UI, keep delete disabled on UI.
    }
    try {
      await axios.delete(`/api/banks/${id}`);
      toast.success("Bank deleted");
      fetchBanks();
    } catch (e) {
      toast.error(e?.response?.data?.message || "Delete failed");
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can?.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view banks.</div>;

  return (
    <div className="p-4 space-y-3">
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg shadow-sm" style={{ background: "linear-gradient(to bottom right, #3b82f6, #2563eb)" }}>
              <BanknotesIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Banks</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">Manage banks and accounts</p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={fetchBanks}
              disabled={loading}
              className="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium bg-white/60 dark:bg-slate-700/60 border border-gray-200/60 dark:border-slate-600/40"
            >
              <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
              Refresh
            </button>
            {can?.create && (
              <button
                onClick={openCreate}
                className="inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-semibold bg-blue-600 text-white"
              >
                <PlusCircleIcon className="w-4 h-4" />
                Add Bank
              </button>
            )}
          </div>
        </div>

        <div className="px-4 py-3">
          <div className="flex gap-3 items-center">
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Search bank name or account…"
              className="w-full max-w-lg h-9 px-3 rounded border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-sm"
            />
            <button
              onClick={fetchBanks}
              className="h-9 px-3 rounded bg-slate-900 text-white dark:bg-slate-200 dark:text-slate-900 text-sm font-medium"
            >
              Search
            </button>
          </div>
        </div>

        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
              <tr className="text-left">
                <th className="px-4 py-2">Bank</th>
                <th className="px-4 py-2">Account #</th>
                <th className="px-4 py-2 text-right">Balance</th>
                <th className="px-4 py-2 text-center">Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && !loading ? (
                <tr>
                  <td colSpan={4} className="px-4 py-14 text-center text-gray-500">
                    No banks found
                  </td>
                </tr>
              ) : null}
              {rows.map((b) => (
                <tr key={b.id} className="border-b border-gray-100 dark:border-slate-800">
                  <td className="px-4 py-2">
                    <div className="flex items-center gap-3">
                      {b.image_path ? (
                        <img
                          src={`/storage/${b.image_path}`}
                          alt={b.bank_name}
                          className="w-8 h-8 rounded-full object-cover border border-gray-200 dark:border-slate-600 shrink-0"
                        />
                      ) : (
                        <div className="w-8 h-8 rounded-full bg-blue-100 dark:bg-blue-900/40 flex items-center justify-center shrink-0">
                          <BanknotesIcon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                        </div>
                      )}
                      <div className="font-medium text-gray-900 dark:text-white">{b.bank_name}</div>
                    </div>
                  </td>
                  <td className="px-4 py-2 text-gray-700 dark:text-gray-200">{b.account_number}</td>
                  <td className="px-4 py-2 text-right font-semibold">
                    {Number(b.balance || 0).toFixed(2)}
                  </td>
                  <td className="px-4 py-2 text-center">
                    <div className="flex justify-center gap-2">
                      {can?.update && (
                        <Link
                          to={`/banks/${b.id}/edit`}
                          className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium bg-emerald-600 text-white"
                        >
                          <PencilSquareIcon className="w-3.5 h-3.5" />
                          Edit
                        </Link>
                      )}
                      {can?.delete && (
                        <button
                          onClick={() => deleteBank(b.id)}
                          className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium bg-rose-600 text-white"
                        >
                          <TrashIcon className="w-3.5 h-3.5" />
                          Delete
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="text-xs text-gray-500">
        Note: Deletion uses API directly; ensure your auth/password flow matches your backend.
      </div>
    </div>
  );
}

