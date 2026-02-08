// src/pages/sale-invoices/index.jsx
import React, { useEffect, useMemo, useState, useRef, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  MagnifyingGlassIcon,
  PlusCircleIcon,
  PencilSquareIcon,
  EyeIcon,
  PrinterIcon,
  TrashIcon,
  ArrowPathIcon,
  DocumentTextIcon,
  UserIcon,
  CalendarIcon,
  CurrencyDollarIcon,
} from "@heroicons/react/24/solid";
import { usePermissions, Guard } from "@/api/usePermissions.js";

// Reusable components
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
  TextSearch,
  DeleteConfirmationModal,
} from "@/components";
import { useTheme } from "@/context/ThemeContext";

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

export default function SaleInvoicesIndex() {
  const [invoices, setInvoices] = useState([]);
  const [loading, setLoading] = useState(true);

  // search + pagination
  const [qPosted, setQPosted] = useState("");
  const [qCustomer, setQCustomer] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);

  // Delete modal state
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deletingInvoice, setDeletingInvoice] = useState(null);
  const [deleting, setDeleting] = useState(false);

  const navigate = useNavigate();

  // AbortController for fetches
  const controllerRef = useRef(null);

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("sale-invoice") : {
        view: false, create: false, update: false, delete: false, import: false, export: false
      }),
    [canFor]
  );

  // 🎨 Modern button palette (matching sidebar design language)
  const tintBlue = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] hover:from-blue-600 hover:to-blue-700 active:scale-[0.98] transition-all duration-200";
  const tintIndigo = "bg-gradient-to-br from-indigo-500 to-indigo-600 text-white shadow-lg shadow-indigo-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-indigo-500/30 hover:scale-[1.02] hover:from-indigo-600 hover:to-indigo-700 active:scale-[0.98] transition-all duration-200";
  const tintSlate = "bg-gradient-to-br from-slate-700 to-slate-800 text-white shadow-lg shadow-slate-500/25 ring-1 ring-white/10 hover:shadow-xl hover:shadow-slate-500/30 hover:scale-[1.02] hover:from-slate-800 hover:to-slate-900 active:scale-[0.98] transition-all duration-200";
  const tintAmber = "bg-gradient-to-br from-amber-500 to-amber-600 text-white shadow-lg shadow-amber-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-amber-500/30 hover:scale-[1.02] hover:from-amber-600 hover:to-amber-700 active:scale-[0.98] transition-all duration-200";
  const tintRed = "bg-gradient-to-br from-rose-500 to-rose-600 text-white shadow-lg shadow-rose-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700 active:scale-[0.98] transition-all duration-200";
  const tintGreen = "bg-gradient-to-br from-emerald-500 to-emerald-600 text-white shadow-lg shadow-emerald-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-emerald-500/30 hover:scale-[1.02] hover:from-emerald-600 hover:to-emerald-700 active:scale-[0.98] transition-all duration-200";
  const tintGlass = "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";

  // Get dark mode state
  const { isDark } = useTheme();

  // Fetch invoices
  const fetchInvoices = useCallback(async (signal) => {
    if (permsLoading || !can.view) {
      setInvoices([]);
      setLoading(false);
      return;
    }
    try {
      setLoading(true);
      const res = await axios.get("/api/sale-invoices", { signal });
      const data = res.data || [];
      setInvoices(data);
      setTotal(data.length);
    } catch (e) {
      if (axios.isCancel?.(e)) return;
      const status = e?.response?.status;
      if (status === 403) toast.error("You don't have permission to view sale invoices.");
      else toast.error("Failed to fetch sale invoices");
    } finally {
      setLoading(false);
    }
  }, [permsLoading, can.view]);

  // Initial fetch
  useEffect(() => {
    if (permsLoading || !can.view) return;
    const ctrl = new AbortController();
    controllerRef.current = ctrl;
    fetchInvoices(ctrl.signal);
    return () => ctrl.abort();
  }, [fetchInvoices, permsLoading, can.view]);

  // Alt+N -> create
  useEffect(() => {
    const onKeyDown = (e) => {
      if (!e.altKey) return;
      const key = (e.key || "").toLowerCase();
      if (key !== "n") return;
      const tag = (e.target?.tagName || "").toLowerCase();
      const isTyping = ["input", "textarea", "select"].includes(tag) || e.target?.isContentEditable;
      if (isTyping) return;
      if (!can.create) return;
      e.preventDefault();
      navigate("/sale-invoices/create");
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate, can.create]);

  // ===== search + pagination =====
  const norm = (v) => (v ?? "").toString().toLowerCase().trim();
  const filtered = useMemo(() => {
    const nPosted = norm(qPosted);
    const nCust = norm(qCustomer);
    return invoices.filter((inv) => {
      const posted = norm(inv.posted_number);
      const customer = norm(inv.customer?.name);
      return posted.includes(nPosted) && customer.includes(nCust);
    });
  }, [invoices, qPosted, qCustomer]);

  useEffect(() => { setPage(1); }, [qPosted, qCustomer]);

  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize));
  useEffect(() => { if (page > pageCount) setPage(pageCount); }, [page, pageCount]);

  const start = (page - 1) * pageSize;
  const paged = filtered.slice(start, start + pageSize);

  // ===== delete modal handlers =====
  const openDeleteModal = (invoice) => {
    if (!can.delete) return toast.error("You don't have permission to delete sale invoices.");
    setDeletingInvoice({ id: invoice.id, name: invoice.posted_number });
    setDeleteModalOpen(true);
  };

  const closeDeleteModal = () => {
    setDeleteModalOpen(false);
    setDeletingInvoice(null);
  };

  const handleConfirmDelete = async (password) => {
    if (!deletingInvoice?.id) return;
    if (!can.delete) return toast.error("You don't have permission to delete sale invoices.");

    try {
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/sale-invoices/${deletingInvoice.id}`);
      toast.success("Sale invoice deleted");
      setInvoices((prev) => prev.filter((i) => i.id !== deletingInvoice.id));
      closeDeleteModal();

      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      fetchInvoices(ctrl.signal);
    } catch (err) {
      const status = err?.response?.status;
      const apiMsg =
        err?.response?.data?.message ||
        (status === 422 ? "Incorrect password" : status === 403 ? "You don't have permission to delete sale invoices." : "Delete failed");
      toast.error(apiMsg);
    }
  };

  // Print popup logic
  const handlePrint = (id) => {
    if (!id) return;

    const WEB_BASE =
      (import.meta.env.VITE_BACKEND_WEB_BASE || "").replace(/\/$/, "") ||
      window.location.origin;

    const url = `${WEB_BASE}/print/sale-invoices/${id}`;

    const width = 900;
    const height = 700;
    const left = Math.max(
      0,
      (window.screenX || window.screenLeft || 0) + (window.outerWidth - width) / 2
    );
    const top = Math.max(
      0,
      (window.screenY || window.screenTop || 0) + (window.outerHeight - height) / 2
    );

    const features = [
      `width=${Math.round(width)}`,
      `height=${Math.round(height)}`,
      `left=${Math.round(left)}`,
      `top=${Math.round(top)}`,
      "menubar=no",
      "toolbar=no",
      "location=no",
      "status=no",
      "scrollbars=yes",
      "resizable=yes",
    ].join(",");

    const w = window.open(url, "salePrintWin", features);
    if (!w) {
      toast.error("Popup blocked. Please allow popups to print.");
      return;
    }

    try { w.opener = null; } catch {}

    w.onload = () => {
      try { w.focus(); w.print(); } catch {}
    };

    const timer = setInterval(() => {
      try {
        if (w.document?.readyState === "complete") {
          w.focus(); w.print(); clearInterval(timer);
        }
      } catch {}
      if (w.closed) clearInterval(timer);
    }, 400);
  };

  // Format date with alphabetic month (e.g., "1 Jan 2024" or "15 Feb 2024")
  const formatDate = (dateStr) => {
    if (!dateStr) return "-";
    const date = new Date(dateStr);
    if (isNaN(date.getTime())) return dateStr;

    const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    const day = date.getDate();
    const month = months[date.getMonth()];
    const year = date.getFullYear();

    return `${day} ${month} ${year}`;
  };

  // 🔒 permissions gates
  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view sale invoices.</div>;

  return (
    <div className="p-4 space-y-3">
      {/* ===== Professional Header ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.core.gradient} shadow-sm`}>
              <DocumentTextIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Sale Invoices</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{total} items</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            {/* Refresh Button */}
            <GlassBtn
              onClick={() => {
                if (controllerRef.current) controllerRef.current.abort();
                const ctrl = new AbortController();
                controllerRef.current = ctrl;
                fetchInvoices(ctrl.signal);
              }}
              className={`h-10 min-w-[120px] ${tintSlate}`}
              title="Refresh"
              aria-label="Refresh sale invoices"
            >
              <span className="inline-flex items-center gap-2">
                <ArrowPathIcon className="w-5 h-5" />
                Refresh
              </span>
            </GlassBtn>

            <Guard when={can.create}>
              <Link
                to="/sale-invoices/create"
                title="Add Sale Invoice (Alt+N)"
                aria-keyshortcuts="Alt+N"
                className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${tintBlue}`}
              >
                <PlusCircleIcon className="w-4 h-4" />
                Add Invoice
              </Link>
            </Guard>
          </div>
        </div>

        {/* Search Bar */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <TextSearch
              value={qPosted}
              onChange={setQPosted}
              placeholder="Search by Posted No (e.g., SI-000001)…"
              icon={<DocumentTextIcon className="w-4 h-4 text-gray-400" />}
            />
            <TextSearch
              value={qCustomer}
              onChange={setQCustomer}
              placeholder="Search by Customer…"
              icon={<UserIcon className="w-4 h-4 text-gray-400" />}
            />
          </div>
        </div>

        {/* Header Bottom */}
        <div className="flex items-center justify-between px-4 py-2 border-t border-gray-100 dark:border-slate-700">
          {/* Stats */}
          <div className="flex items-center gap-4">
            <span className="text-xs text-gray-500 dark:text-gray-400">
              {loading ? (
                <span className="inline-flex items-center gap-1">
                  <ArrowPathIcon className="w-3.5 h-3.5 animate-spin" />
                  Loading...
                </span>
              ) : (
                `${filtered.length === 0 ? 0 : start + 1}-${Math.min(filtered.length, start + pageSize)} of ${total}`
              )}
            </span>
          </div>

          {/* Right Actions */}
          <div className="flex items-center gap-2">
            {/* Page Size Selector */}
            <div className="flex items-center gap-1.5 px-2 py-1 rounded-lg bg-white/60 dark:bg-slate-800/60 border border-gray-200/60 dark:border-slate-600/40">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Show</label>
              <select
                value={pageSize}
                onChange={(e) => setPageSize(Number(e.target.value))}
                className="h-7 px-2 rounded border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-xs font-medium text-gray-700 dark:text-gray-200 focus:ring-2 focus:ring-blue-500 focus:border-transparent cursor-pointer"
              >
                <option value={10}>10</option>
                <option value={25}>25</option>
                <option value={50}>50</option>
                <option value={100}>100</option>
              </select>
            </div>
          </div>
        </div>
      </div>

      {/* ===== Invoices Table ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        {/* Table Header */}
        <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div className={`p-1 rounded ${SECTION_CONFIG.core.bgDark}`}>
              <DocumentTextIcon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
            </div>
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Invoice List</span>
          </div>
          <span className="text-xs text-gray-400">{paged.length} items</span>
        </div>

        <div className="max-h-[65vh] overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
              <tr className="text-left">
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">#</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Posted No</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Customer</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Date</th>
                <th className="px-3 py-2 font-semibold text-right text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Total</th>
                <th className="px-3 py-2 font-semibold text-center text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-40">Actions</th>
              </tr>
            </thead>

            <tbody>
              {paged.length === 0 && !loading && (
                <tr>
                  <td className="px-3 py-12 text-center" colSpan={6}>
                    <div className="flex flex-col items-center gap-2">
                      <DocumentTextIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">No sale invoices found</p>
                    </div>
                  </td>
                </tr>
              )}

              {loading && (
                <tr>
                  <td className="px-3 py-12 text-center" colSpan={6}>
                    <div className="flex flex-col items-center gap-2">
                      <ArrowPathIcon className="w-6 h-6 text-gray-400 animate-spin" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">Loading...</p>
                    </div>
                  </td>
                </tr>
              )}

              {paged.map((invoice, idx) => {
                const invTotal = Number(invoice.total ?? 0);
                const invReceived = Number(invoice.total_receive ?? invoice.total_recieve ?? 0);
                const invRemaining = Math.max(invTotal - invReceived, 0);
                const isCredit = invoice.invoice_type === 'credit';

                return (
                  <tr
                    key={invoice.id}
                    className={`
                      transition-colors
                      odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50
                      border-b border-gray-100 dark:border-slate-600/30
                    `}
                  >
                    <td className="px-3 py-3 text-gray-600 dark:text-gray-300">
                      {start + idx + 1}
                    </td>
                    <td className="px-3 py-3">
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 text-xs font-medium">
                        <DocumentTextIcon className="w-3.5 h-3.5" />
                        {invoice.posted_number || "-"}
                      </span>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <UserIcon className="w-4 h-4 text-gray-400" />
                        <span className="text-gray-800 dark:text-gray-200">{invoice.customer?.name ?? "N/A"}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <CalendarIcon className="w-4 h-4 text-gray-400" />
                        <span className="text-gray-800 dark:text-gray-200">{formatDate(invoice.date)}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3 text-right">
                      <div className="flex items-center justify-end gap-1">
                        <CurrencyDollarIcon className="w-4 h-4 text-emerald-500" />
                        <span className="font-semibold text-gray-800 dark:text-gray-200">
                          {invTotal.toLocaleString()}
                        </span>
                      </div>
                      {/* Show remaining if any */}
                      {invRemaining > 0 && (
                        <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                          Due: {invRemaining.toLocaleString()}
                        </div>
                      )}
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-1.5 justify-center">
                        {/* View Action */}
                        <Link
                          to={`/sale-invoices/${invoice.id}`}
                          className={`
                            group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold
                            bg-gradient-to-br from-violet-500 to-violet-600 text-white
                            shadow-lg shadow-violet-500/20 ring-1 ring-violet-400/30
                            hover:shadow-xl hover:shadow-violet-500/30 hover:scale-[1.02] hover:from-violet-600 hover:to-violet-700
                            active:scale-[0.98] transition-all duration-200
                          `}
                          title="View"
                        >
                          <EyeIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                          <span>View</span>
                        </Link>

                        {/* Edit Action */}
                        <Guard when={can.update}>
                          <Link
                            to={`/sale-invoices/${invoice.id}/edit`}
                            className={`
                              group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold
                              bg-gradient-to-br from-amber-400 to-amber-500 text-white
                              shadow-lg shadow-amber-500/20 ring-1 ring-amber-400/30
                              hover:shadow-xl hover:shadow-amber-500/30 hover:scale-[1.02] hover:from-amber-500 hover:to-amber-600
                              active:scale-[0.98] transition-all duration-200
                            `}
                            title="Edit"
                          >
                            <PencilSquareIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                            <span>Edit</span>
                          </Link>
                        </Guard>

                        {/* Print Action */}
                        <button
                          onClick={() => handlePrint(invoice.id)}
                          className={`
                            group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold
                            bg-gradient-to-br from-emerald-500 to-emerald-600 text-white
                            shadow-lg shadow-emerald-500/20 ring-1 ring-emerald-400/30
                            hover:shadow-xl hover:shadow-emerald-500/30 hover:scale-[1.02] hover:from-emerald-600 hover:to-emerald-700
                            active:scale-[0.98] transition-all duration-200
                          `}
                          title="Print"
                        >
                          <PrinterIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                          <span>Print</span>
                        </button>

                        {/* Delete Action */}
                        <Guard when={can.delete}>
                          <button
                            onClick={() => openDeleteModal(invoice)}
                            className={`
                              group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold
                              bg-gradient-to-br from-rose-500 to-rose-600 text-white
                              shadow-lg shadow-rose-500/20 ring-1 ring-rose-400/30
                              hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700
                              active:scale-[0.98] transition-all duration-200
                            `}
                            title="Delete"
                          >
                            <TrashIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                            <span>Delete</span>
                          </button>
                        </Guard>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        {/* Compact Pagination */}
        <div className="px-3 py-2 flex items-center justify-between border-t border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800/50 text-xs">
          <span className="text-gray-500 dark:text-gray-400">
            Page {page} of {pageCount} ({filtered.length} total)
          </span>

          <div className="flex items-center gap-1">
            <button
              onClick={() => setPage(1)}
              disabled={page === 1}
              className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}
            >
              ⏮
            </button>
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}
            >
              ◀
            </button>

            {/* Page numbers */}
            <div className="flex items-center gap-0.5 mx-1">
              {Array.from({ length: Math.min(5, pageCount) }, (_, i) => {
                let pageNum;
                if (pageCount <= 5) {
                  pageNum = i + 1;
                } else if (page <= 3) {
                  pageNum = i + 1;
                } else if (page >= pageCount - 2) {
                  pageNum = pageCount - 4 + i;
                } else {
                  pageNum = page - 2 + i;
                }
                return (
                  <button
                    key={pageNum}
                    onClick={() => setPage(pageNum)}
                    className={`
                      w-7 h-7 rounded text-xs font-medium transition-colors
                      ${page === pageNum
                        ? `bg-gradient-to-br ${SECTION_CONFIG.core.gradient} text-white`
                        : "text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-slate-700"
                      }
                    `}
                  >
                    {pageNum}
                  </button>
                );
              })}
            </div>

            <button
              onClick={() => setPage((p) => Math.min(pageCount, p + 1))}
              disabled={page === pageCount}
              className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === pageCount ? 'opacity-40' : ''}`}
            >
              ▶
            </button>
            <button
              onClick={() => setPage(pageCount)}
              disabled={page === pageCount}
              className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === pageCount ? 'opacity-40' : ''}`}
            >
              ⏭
            </button>
          </div>
        </div>
      </div>

      {/* Delete Confirmation Modal */}
      <DeleteConfirmationModal
        isOpen={deleteModalOpen}
        onClose={closeDeleteModal}
        onConfirm={handleConfirmDelete}
        itemName={deletingInvoice?.name || "this invoice"}
        title="Delete sale invoice"
        isDeleting={deleting}
        setIsDeleting={setDeleting}
        tintClasses={{ red: tintRed, glass: tintGlass }}
      />
    </div>
  );
}

