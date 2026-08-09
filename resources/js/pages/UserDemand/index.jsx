import React, { useEffect, useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import {
  PlusCircleIcon,
  ArrowPathIcon,
  TrashIcon,
  HandRaisedIcon,
  CheckCircleIcon,
  ShoppingCartIcon,
  XCircleIcon,
  CubeIcon,
} from "@heroicons/react/24/solid";
import { usePermissions } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";
import { DeleteConfirmationModal } from "@/components";

const STATUS_STYLES = {
  pending: { label: "Pending", bg: "#fef3c7", text: "#d97706" },
  ordered: { label: "Ordered", bg: "#dbeafe", text: "#2563eb" },
  fulfilled: { label: "Fulfilled", bg: "#dcfce7", text: "#16a34a" },
  cancelled: { label: "Cancelled", bg: "#fee2e2", text: "#dc2626" },
};

const STATUS_ORDER = ["pending", "ordered", "fulfilled", "cancelled"];

export default function UserDemandsIndex() {
  const { has } = usePermissions();
  const { theme } = useTheme();
  const navigate = useNavigate();

  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || '#3b82f6',
    primaryHover: theme?.primary_hover || '#2563eb',
    primaryLight: theme?.primary_light || '#dbeafe',
    secondary: theme?.secondary_color || '#8b5cf6',
    secondaryHover: theme?.secondary_hover || '#7c3aed',
    secondaryLight: theme?.secondary_light || '#ede9fe',
    danger: theme?.danger_color || '#ef4444',
  }), [theme]);

  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);
  const [statusFilter, setStatusFilter] = useState("pending");
  const [stats, setStats] = useState({ count: 0, pending_count: 0 });

  const canView = has("user-demands.view");
  const canCreate = has("user-demands.create");
  const canUpdate = has("user-demands.update");
  const canDelete = has("user-demands.delete");

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleting, setDeleting] = useState(null);
  const [deletingLoading, setDeletingLoading] = useState(false);

  const fetchDemands = async () => {
    setLoading(true);
    try {
      const { data } = await axios.get("/api/user-demands", {
        params: { status: statusFilter || undefined, limit: 200 },
      });
      setRows(Array.isArray(data?.rows) ? data.rows : []);
      setStats({ count: data?.count || 0, pending_count: data?.pending_count || 0 });
    } catch (err) {
      console.error(err);
      toast.error("Failed to load user demands.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (canView) fetchDemands();
  }, [statusFilter, canView]);

  const changeStatus = async (demand, status) => {
    if (!canUpdate) return toast.error("You don't have permission to update demands.");
    try {
      await axios.put(`/api/user-demands/${demand.id}`, { status });
      toast.success(`Marked as ${status}.`);
      fetchDemands();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Update failed.");
    }
  };

  const openDelete = (d) => {
    if (!canDelete) return toast.error("You don't have permission to delete demands.");
    setDeleting(d);
    setDeleteOpen(true);
  };

  const confirmDelete = async (_password) => {
    if (!deleting) return;
    try {
      await axios.delete(`/api/user-demands/${deleting.id}`);
      toast.success("User demand deleted.");
      setDeleteOpen(false);
      setDeleting(null);
      fetchDemands();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Delete failed.");
    }
  };

  if (!canView) return <div className="p-6 text-sm text-gray-700">You don't have permission to view User Demands.</div>;

  return (
    <div className="p-4 space-y-4">
      {/* Hero */}
      <div
        className="relative overflow-hidden rounded-2xl shadow-lg"
        style={{ background: `linear-gradient(135deg, ${themeColors.secondary}, ${themeColors.primary}, ${themeColors.primaryHover})` }}
      >
        <div className="absolute -top-10 -right-8 w-64 h-64 rounded-full bg-white/15 blur-3xl pointer-events-none" />
        <div className="relative px-6 pt-5 pb-4 flex flex-col lg:flex-row lg:items-center justify-between gap-4">
          <div className="flex items-center gap-4">
            <div className="w-12 h-12 rounded-2xl bg-white/20 backdrop-blur shadow-inner flex items-center justify-center">
              <HandRaisedIcon className="w-6 h-6 text-white" />
            </div>
            <div>
              <h1 className="text-2xl font-extrabold tracking-wide text-white leading-none">User Demands</h1>
              <p className="text-xs text-white/85 mt-1.5 flex items-center gap-1.5">
                <CubeIcon className="w-3.5 h-3.5" />
                {stats.pending_count} pending · {stats.count} total
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2.5 flex-wrap">
            <button
              onClick={fetchDemands}
              disabled={loading}
              className="inline-flex items-center gap-1.5 rounded-lg px-3 py-2 text-xs font-semibold text-white bg-white/15 hover:bg-white/25 transition-all duration-200"
            >
              <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
              {loading ? "Loading…" : "Refresh"}
            </button>
            {canCreate && (
              <Link
                to="/user-demands/create"
                className="inline-flex items-center gap-1.5 rounded-lg px-3.5 py-2 text-sm font-bold text-white bg-white/95 hover:bg-white shadow-lg transition-all duration-200"
                style={{ color: themeColors.primaryHover }}
              >
                <PlusCircleIcon className="w-4 h-4" />
                <span className="hidden sm:inline">Request Product</span>
                <span className="sm:hidden">Add</span>
              </Link>
            )}
          </div>
        </div>

        {/* Status filter */}
        <div className="relative px-6 pt-2 pb-5">
          <div className="flex flex-wrap items-center gap-2 bg-white/15 backdrop-blur border border-white/20 rounded-xl p-2">
            {["all", ...STATUS_ORDER].map((s) => (
              <button
                key={s}
                onClick={() => setStatusFilter(s === "all" ? "" : s)}
                className="px-3 py-1.5 rounded-lg text-xs font-semibold transition-all duration-200"
                style={{
                  background: statusFilter === s ? "rgba(255,255,255,0.95)" : "transparent",
                  color: statusFilter === s ? themeColors.primaryHover : "white",
                }}
              >
                {s === "all" ? "All" : STATUS_STYLES[s]?.label}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Table */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-xl shadow-sm" style={{ backgroundColor: themeColors.secondaryLight }}>
              <HandRaisedIcon className="w-4 h-4" style={{ color: themeColors.secondary }} />
            </div>
            <div>
              <span className="text-sm font-semibold text-gray-700 dark:text-gray-200">Demand List</span>
              <p className="text-[11px] text-gray-400 dark:text-gray-500">{rows.length} item(s)</p>
            </div>
          </div>
        </div>

        <div className="max-h-[65vh] overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
              <tr className="text-left text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">
                <th className="px-3 py-2 font-semibold">Product</th>
                <th className="px-3 py-2 font-semibold">Brand</th>
                <th className="px-3 py-2 font-semibold">Supplier</th>
<th className="px-3 py-2 font-semibold text-right">Qty</th>
                <th className="px-3 py-2 font-semibold">Requested By</th>
                <th className="px-3 py-2 font-semibold">Customer</th>
                <th className="px-3 py-2 font-semibold">Status</th>
                <th className="px-3 py-2 font-semibold text-center">Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && !loading && (
                <tr>
<td colSpan={8} className="px-3 py-12 text-center text-gray-500 dark:text-gray-400">
                    <div className="flex flex-col items-center gap-2">
                      <HandRaisedIcon className="w-8 h-8 text-gray-400" />
                      <p>No user demands found.</p>
                      {canCreate && (
                        <Link
                          to="/user-demands/create"
                          className="inline-flex items-center gap-1 text-xs font-semibold"
                          style={{ color: themeColors.primary }}
                        >
                          <PlusCircleIcon className="w-3.5 h-3.5" /> Request a product
                        </Link>
                      )}
                    </div>
                  </td>
                </tr>
              )}

              {rows.map((d) => {
                const st = STATUS_STYLES[d.status] || STATUS_STYLES.pending;
                return (
                  <tr key={d.id} className="odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50 transition-colors border-b border-gray-100 dark:border-slate-700/50">
                    <td className="px-3 py-2.5">
                      <div className="font-medium text-gray-800 dark:text-gray-200">
                        {d.requested_name || d.product?.name || "—"}
                      </div>
                      {d.product?.product_code && (
                        <div className="text-xs text-gray-500 dark:text-gray-400">{d.product.product_code}</div>
                      )}
                    </td>
                    <td className="px-3 py-2.5 text-gray-700 dark:text-gray-300">{d.product_brand_name || "—"}</td>
                    <td className="px-3 py-2.5 text-gray-700 dark:text-gray-300">{d.product_supplier_name || "—"}</td>
                    <td className="px-3 py-2.5 text-right font-medium text-gray-800 dark:text-gray-200">{d.requested_quantity}</td>
<td className="px-3 py-2.5 text-gray-700 dark:text-gray-300">
                      {d.requester?.name || "—"}
                      {d.customer?.name && (
                        <div className="text-xs mt-0.5 text-blue-600 dark:text-blue-400">
                          Customer: {d.customer.name}
                        </div>
                      )}
                    </td>
                    <td className="px-3 py-2.5 text-gray-700 dark:text-gray-300">{d.customer?.name || "—"}</td>
                    <td className="px-3 py-2.5">
                      <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-bold" style={{ backgroundColor: st.bg, color: st.text }}>
                        {st.label}
                      </span>
                    </td>
                    <td className="px-3 py-2.5">
                      <div className="flex items-center justify-center gap-1.5">
                        {d.status === "pending" && (
                          <>
                            <button
                              onClick={() => changeStatus(d, "ordered")}
                              title="Mark as Ordered"
                              className="p-1.5 rounded-lg text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/30 transition-colors"
                            >
                              <ShoppingCartIcon className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => changeStatus(d, "fulfilled")}
                              title="Mark as Fulfilled"
                              className="p-1.5 rounded-lg text-green-600 hover:bg-green-50 dark:hover:bg-green-900/30 transition-colors"
                            >
                              <CheckCircleIcon className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => changeStatus(d, "cancelled")}
                              title="Cancel demand"
                              className="p-1.5 rounded-lg text-red-500 hover:bg-red-50 dark:hover:bg-red-900/30 transition-colors"
                            >
                              <XCircleIcon className="w-4 h-4" />
                            </button>
                          </>
                        )}
                        {canDelete && (
                          <button
                            onClick={() => openDelete(d)}
                            title="Delete"
                            className="p-1.5 rounded-lg text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 transition-colors"
                          >
                            <TrashIcon className="w-4 h-4" />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      <DeleteConfirmationModal
        isOpen={deleteOpen}
        onClose={() => setDeleteOpen(false)}
        onConfirm={confirmDelete}
        itemName={deleting?.requested_name || "this demand"}
        title="Delete user demand"
        isDeleting={deletingLoading}
        setIsDeleting={setDeletingLoading}
        tintClasses={{
          primary: { className: "rounded-lg", style: { background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`, color: "#fff" } },
          danger: { className: "rounded-lg", style: { background: "linear-gradient(to bottom right, #ef4444, #dc2626)", color: "#fff" } },
          glass: { className: "rounded-lg", style: {} },
        }}
      />
    </div>
  );
}
