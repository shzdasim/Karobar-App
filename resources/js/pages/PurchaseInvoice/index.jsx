// src/pages/purchase-invoices/index.jsx
import React, { useEffect, useMemo, useRef, useState, useCallback } from "react";
import axios from "axios";
import { Link, useNavigate } from "react-router-dom";
import toast from "react-hot-toast";
import {
  PlusCircleIcon,
  PencilSquareIcon,
  TrashIcon,
  MagnifyingGlassIcon,
  XMarkIcon,
  ArrowPathIcon,
  ShieldExclamationIcon,
  ClipboardDocumentListIcon,
} from "@heroicons/react/24/solid";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";

import {
  GlassCard,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";

// Section configuration with color schemes - matching sidebar design
const SECTION_CONFIG = {
  invoices: {
    gradient: "from-emerald-500 to-teal-600",
    bgLight: "bg-emerald-50",
    bgDark: "dark:bg-emerald-900/20",
    borderColor: "border-emerald-200 dark:border-emerald-700",
    iconColor: "text-emerald-600 dark:text-emerald-400",
    ringColor: "ring-emerald-300 dark:ring-emerald-700",
  },
};

// Helper function to format date in alphabet format (e.g., "1 Jan 2024", "15 Feb 2023")
const formatDate = (dateStr) => {
  if (!dateStr) return "-";
  const date = new Date(dateStr);
  if (isNaN(date.getTime())) return dateStr;
  const day = date.getDate();
  const month = date.toLocaleString("en", { month: "short" });
  const year = date.getFullYear();
  return `${day} ${month} ${year}`;
};

export default function PurchaseInvoicesIndex() {
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);

  // search filters
  const [qPosted, setQPosted] = useState("");
  const [qSupplier, setQSupplier] = useState("");

  // pagination (server-side)
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);
  const [lastPage, setLastPage] = useState(1);

  const navigate = useNavigate();

  // AbortController for fetches
  const controllerRef = useRef(null);
  const debounceRef = useRef(null);

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function"
        ? canFor("purchase-invoice")
        : { view: false, create: false, update: false, delete: false, import: false, export: false }),
    [canFor]
  );

  // 🎨 Get theme colors
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
        secondaryLight: '#ede9fe',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
    };
  }, [theme]);

  // 🎨 Dynamic Button styles using theme colors
  const buttonStyle = theme?.button_style || 'rounded';
  
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
        danger: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: '#ef4444',
            color: '#ef4444',
            backgroundColor: 'transparent',
          }
        },
        glass: {
          className: `${radiusClass} transition-all duration-200`,
          style: {
            backgroundColor: 'transparent',
            color: isDark ? '#f1f5f9' : '#111827',
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
          color: 'white',
          boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
        }
      },
      secondary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
          color: 'white',
          boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`,
        }
      },
      danger: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, #ef4444, #dc2626)`,
          color: 'white',
          boxShadow: '0 4px 14px 0 rgba(239, 68, 68, 0.4)',
        }
      },
      glass: {
        className: radiusClass,
        style: {
          backgroundColor: isDark ? 'rgba(51, 65, 85, 0.6)' : 'rgba(255, 255, 255, 0.8)',
          color: isDark ? '#f1f5f9' : '#111827',
          backdropFilter: 'blur(6px)',
          border: isDark ? '1px solid rgba(71, 85, 105, 0.5)' : '1px solid rgba(229, 231, 235, 0.6)',
        }
      },
    };
  }, [buttonStyle, themeColors, isDark]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;
  const btnGlass = getButtonClasses.glass;

  useEffect(() => {
    document.title = "Purchase Invoices - Pharmacy ERP";
  }, []);

  // Alt+N => /purchase-invoices/create (only when can.create)
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
      navigate("/purchase-invoices/create");
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate, can.create]);

  const fetchInvoices = useCallback(async (signal, options = {}) => {
    const {
      pageArg = page,
      pageSizeArg = pageSize,
      qPostedArg = qPosted,
      qSupplierArg = qSupplier,
    } = options;

    try {
      setLoading(true);
      const { data } = await axios.get("/api/purchase-invoices", {
        params: {
          page: pageArg,
          per_page: pageSizeArg,
          posted: qPostedArg.trim(),
          supplier: qSupplierArg.trim(),
        },
        signal,
      });

      const items = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
      setRows(items);
      setTotal(Number(data?.total ?? items.length ?? 0));
      const lp = Number(data?.last_page ?? 1);
      setLastPage(lp);
      if (pageArg > lp) setPage(lp || 1);
    } catch (err) {
      if (axios.isCancel?.(err)) return;
      const status = err?.response?.status;
      if (status === 403)
        toast.error("You don't have permission to view purchase invoices.");
      else toast.error("Failed to load purchase invoices");
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, qPosted, qSupplier]);

  // Fetch on page or pageSize change
  useEffect(() => {
    if (permsLoading || !can.view) return;
    const ctrl = new AbortController();
    controllerRef.current = ctrl;
    fetchInvoices(ctrl.signal);
    return () => ctrl.abort();
  }, [page, pageSize, permsLoading, can.view]);

  // Debounce on filter change only
  useEffect(() => {
    if (permsLoading || !can.view) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    const ctrl = new AbortController();
    debounceRef.current = setTimeout(() => {
      setPage(1);
      controllerRef.current = ctrl;
      fetchInvoices(ctrl.signal, {
        pageArg: 1,
        qPostedArg: qPosted,
        qSupplierArg: qSupplier,
      });
    }, 300);
    return () => {
      clearTimeout(debounceRef.current);
      ctrl.abort();
    };
  }, [qPosted, qSupplier, permsLoading, can.view]);

  const start = rows.length ? (page - 1) * pageSize + 1 : 0;
  const end = rows.length ? start + rows.length - 1 : 0;

  // ===== secure delete modal state & handlers =====
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deleteStep, setDeleteStep] = useState(1);
  const [deletingInvoice, setDeletingInvoice] = useState(null);
  const [password, setPassword] = useState("");
  const [deleting, setDeleting] = useState(false);

  const openDeleteModal = (inv) => {
    if (!can.delete) return toast.error("You don't have permission to delete invoices.");
    setDeletingInvoice({ id: inv.id, posted_number: inv.posted_number });
    setPassword("");
    setDeleteStep(1);
    setDeleteModalOpen(true);
  };

  const closeDeleteModal = () => {
    if (deleting) return;
    setDeleteModalOpen(false);
    setDeleteStep(1);
    setDeletingInvoice(null);
    setPassword("");
  };

  const proceedToPassword = () => setDeleteStep(2);

  const confirmAndDelete = async () => {
    if (!deletingInvoice?.id) return;
    if (!can.delete) return toast.error("You don't have permission to delete invoices.");
    try {
      setDeleting(true);
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/purchase-invoices/${deletingInvoice.id}`);
      toast.success("Invoice deleted");
      closeDeleteModal();

      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      fetchInvoices(ctrl.signal);
    } catch (err) {
      const status = err?.response?.status;
      const apiMsg =
        err?.response?.data?.message ||
        (status === 422 ? "Incorrect password" : status === 403 ? "You don't have permission to delete invoices." : "Delete failed");
      toast.error(apiMsg);
    } finally {
      setDeleting(false);
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view purchase invoices.</div>;

  const hasActions = can.update || can.delete;

  return (
    <div className="p-4 space-y-3">
      {/* ===== Professional Header ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})` }}
            >
              <ClipboardDocumentListIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Purchase Invoices</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{total} items</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            {/* Refresh Button */}
            <GlassBtn
              className={`h-9 px-3 ${btnSecondary.className}`}
              onClick={() => {
                if (controllerRef.current) controllerRef.current.abort();
                const ctrl = new AbortController();
                controllerRef.current = ctrl;
                fetchInvoices(ctrl.signal);
              }}
              style={btnSecondary.style}
            >
              <span className="inline-flex items-center gap-1.5">
                <ArrowPathIcon className="w-4 h-4" />
                <span className="hidden sm:inline">Refresh</span>
              </span>
            </GlassBtn>

            {/* Primary Add Button */}
            <Guard when={can.create}>
              <Link
                to="/purchase-invoices/create"
                className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${btnPrimary.className}`}
                style={btnPrimary.style}
              >
                <PlusCircleIcon className="w-4 h-4" />
                <span className="hidden sm:inline">Add Invoice</span>
                <span className="sm:hidden">Add</span>
              </Link>
            </Guard>
          </div>
        </div>

        {/* Search Bar */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div className="relative">
              <MagnifyingGlassIcon className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <GlassInput
                value={qPosted}
                onChange={(e) => setQPosted(e.target.value)}
                placeholder="Search by Posted No..."
                className="pl-9 w-full h-9"
              />
            </div>
            <div className="relative">
              <MagnifyingGlassIcon className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <GlassInput
                value={qSupplier}
                onChange={(e) => setQSupplier(e.target.value)}
                placeholder="Search by Supplier..."
                className="pl-9 w-full h-9"
              />
            </div>
          </div>
        </div>

        {/* Header Bottom */}
        <div className="flex items-center justify-between px-4 py-2 border-t border-gray-100 dark:border-slate-700">
          {/* Stats */}
          <span className="text-xs text-gray-500 dark:text-gray-400">
            {loading ? (
              <span className="inline-flex items-center gap-1">
                <ArrowPathIcon className="w-3.5 h-3.5 animate-spin" />
                Loading...
              </span>
            ) : (
              `${rows.length === 0 ? 0 : start}-${end} of ${total}`
            )}
          </span>

          {/* Right Actions */}
          <div className="flex items-center gap-2">
            {/* Page Size Selector */}
            <div className="flex items-center gap-1.5 px-2 py-1 rounded-lg bg-white/60 dark:bg-slate-800/60 border border-gray-200/60 dark:border-slate-600/40">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Show</label>
              <select
                value={pageSize}
                onChange={(e) => setPageSize(Number(e.target.value))}
                className="h-7 px-2 rounded border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-xs font-medium text-gray-700 dark:text-gray-200 focus:ring-2 focus:ring-emerald-500 focus:border-transparent cursor-pointer"
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

      {/* ===== Table Card ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        {/* Table Header */}
        <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div 
              className="p-1.5 rounded"
              style={{ backgroundColor: themeColors.primaryLight }}
            >
              <ClipboardDocumentListIcon 
                className="w-4 h-4" 
                style={{ color: themeColors.primary }} 
              />
            </div>
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Invoice List</span>
          </div>
          <span className="text-xs text-gray-400">{rows.length} items</span>
        </div>

        <div className="max-h-[65vh] overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
              <tr className="text-left">
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-12">#</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Posted No</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Invoice No</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Supplier</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Date</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-24">Amount</th>
                {hasActions && (
                  <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-center w-28">Actions</th>
                )}
              </tr>
            </thead>

            <tbody>
              {rows.length === 0 && !loading && (
                <tr>
                  <td className="px-3 py-12 text-center" colSpan={hasActions ? 7 : 6}>
                    <div className="flex flex-col items-center gap-2">
                      <ClipboardDocumentListIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">No invoices found</p>
                    </div>
                  </td>
                </tr>
              )}

              {rows.map((inv, idx) => (
                <tr
                  key={inv.id}
                  className="odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50 transition-colors border-b border-gray-100 dark:border-slate-600/30"
                >
                  <td className="px-3 py-2.5 text-gray-600 dark:text-gray-400">{(page - 1) * pageSize + idx + 1}</td>
                  <td className="px-3 py-2.5 font-medium text-gray-900 dark:text-gray-100">{inv.posted_number || "-"}</td>
                  <td className="px-3 py-2.5 text-gray-900 dark:text-gray-100">{inv.invoice_number}</td>
                  <td className="px-3 py-2.5">
                    <span className="px-2 py-0.5 rounded bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300 text-xs">
                      {inv.supplier?.name || "N/A"}
                    </span>
                  </td>
                  <td className="px-3 py-2.5 text-gray-600 dark:text-gray-400">{formatDate(inv.posted_date)}</td>
                  <td className="px-3 py-2.5 text-right font-medium text-gray-900 dark:text-gray-100">
                    {Number(inv.total_amount ?? 0).toLocaleString()}
                  </td>

                  {hasActions && (
                    <td className="px-3 py-2.5">
                      <div className="flex items-center justify-center gap-1.5">
                        {/* Edit Action */}
                        <Guard when={can.update}>
                          <Link
                            to={`/purchase-invoices/${inv.id}/edit`}
                            className={`inline-flex items-center gap-1 px-2.5 py-1 rounded text-xs font-medium ${btnSecondary.className}`}
                            style={btnSecondary.style}
                            title={`Edit ${inv.posted_number || inv.invoice_number}`}
                          >
                            <PencilSquareIcon className="w-3.5 h-3.5" />
                            Edit
                          </Link>
                        </Guard>

                        {/* Delete Action */}
                        <Guard when={can.delete}>
                          <button
                            onClick={() => openDeleteModal(inv)}
                            className={`inline-flex items-center gap-1 px-2.5 py-1 rounded text-xs font-medium ${btnDanger.className}`}
                            style={btnDanger.style}
                            title="Delete invoice"
                          >
                            <TrashIcon className="w-3.5 h-3.5" />
                            Delete
                          </button>
                        </Guard>
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Compact Pagination */}
        <div className="px-3 py-2 flex items-center justify-between border-t border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800/50 text-xs">
          <span className="text-gray-500 dark:text-gray-400">
            Page {page} of {lastPage} ({total} total)
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
              {Array.from({ length: Math.min(5, lastPage) }, (_, i) => {
                let pageNum;
                if (lastPage <= 5) {
                  pageNum = i + 1;
                } else if (page <= 3) {
                  pageNum = i + 1;
                } else if (page >= lastPage - 2) {
                  pageNum = lastPage - 4 + i;
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
                        ? ""
                        : "text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-slate-700"
                      }
                    `}
                    style={page === pageNum ? btnPrimary.style : {}}
                  >
                    {pageNum}
                  </button>
                );
              })}
            </div>
            
            <button 
              onClick={() => setPage((p) => Math.min(lastPage, p + 1))} 
              disabled={page === lastPage}
              className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === lastPage ? 'opacity-40' : ''}`}
            >
              ▶
            </button>
            <button 
              onClick={() => setPage(lastPage)} 
              disabled={page === lastPage}
              className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === lastPage ? 'opacity-40' : ''}`}
            >
              ⏭
            </button>
          </div>
        </div>
      </div>

      {/* Delete confirmation modal */}
      {deleteModalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center"
          onClick={(e) => {
            if (e.target === e.currentTarget) closeDeleteModal();
          }}
        >
          <div className="absolute inset-0 bg-black/40" />
          <div className="relative w-full max-w-md">
            <GlassCard>
              <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200/60 dark:border-gray-700/60">
                <div className="flex items-center gap-2">
                  <div 
                    className="p-1.5 rounded-lg"
                    style={{ backgroundColor: `${themeColors.primaryLight}40` }}
                  >
                    <ShieldExclamationIcon 
                      className="w-5 h-5" 
                      style={{ color: themeColors.primary }} 
                    />
                  </div>
                  <span className="font-semibold">Delete invoice</span>
                </div>
                <GlassBtn 
                  className={`h-8 px-2 ${btnGlass.className}`} 
                  onClick={closeDeleteModal}
                  style={btnGlass.style}
                >
                  <XMarkIcon className="w-4 h-4" />
                </GlassBtn>
              </div>
              <div className="p-4 space-y-4">
                {deleteStep === 1 && (
                  <>
                    <p className="text-sm text-gray-700 dark:text-gray-300">
                      {deletingInvoice?.posted_number ? (
                        <>Are you sure you want to delete invoice <strong>{deletingInvoice.posted_number}</strong>? </>
                      ) : "Are you sure you want to delete this invoice? "}
                      This action cannot be undone.
                    </p>
                    <div className="flex justify-end gap-2">
                      <GlassBtn 
                        className={`h-9 px-3 ${btnGlass.className}`} 
                        onClick={closeDeleteModal}
                        style={btnGlass.style}
                      >
                        Cancel
                      </GlassBtn>
                      <GlassBtn 
                        className={`h-9 px-4 ${btnDanger.className}`} 
                        onClick={proceedToPassword}
                        style={btnDanger.style}
                      >
                        Yes, continue
                      </GlassBtn>
                    </div>
                  </>
                )}

                {deleteStep === 2 && (
                  <>
                    <p className="text-sm text-gray-700 dark:text-gray-300">
                      For security, please re-enter your password to delete this invoice.
                    </p>
                    <GlassInput
                      type="password"
                      autoFocus
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="Your password"
                      onKeyDown={(e) => {
                        if (e.key === "Enter") confirmAndDelete();
                        if (e.key === "Escape") closeDeleteModal();
                      }}
                      className="w-full h-9"
                    />
                    <div className="flex justify-between">
                      <GlassBtn 
                        className={`h-9 px-3 ${btnGlass.className}`} 
                        onClick={() => setDeleteStep(1)} 
                        disabled={deleting}
                        style={btnGlass.style}
                      >
                        ← Back
                      </GlassBtn>
                      <div className="flex gap-2">
                        <GlassBtn 
                          className={`h-9 px-3 ${btnGlass.className}`} 
                          onClick={closeDeleteModal} 
                          disabled={deleting}
                          style={btnGlass.style}
                        >
                          Cancel
                        </GlassBtn>
                        <GlassBtn
                          className={`h-9 px-4 ${btnDanger.className}`}
                          onClick={confirmAndDelete}
                          disabled={deleting || password.trim() === ""}
                          style={(!deleting && password.trim() !== "") ? btnDanger.style : {}}
                        >
                          {deleting ? "Deleting…" : "Confirm & Delete"}
                        </GlassBtn>
                      </div>
                    </div>
                  </>
                )}
              </div>
            </GlassCard>
          </div>
        </div>
      )}
    </div>
  );
}
