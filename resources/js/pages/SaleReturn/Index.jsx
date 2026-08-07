// src/pages/sale-returns/index.jsx
import React, { useEffect, useMemo, useState, useRef, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  MagnifyingGlassIcon,
  PlusCircleIcon,
  PencilSquareIcon,
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

export default function SaleReturnsIndex() {
  const [returns, setReturns] = useState([]);
  const [loading, setLoading] = useState(true);

  // search + pagination
  const [qPosted, setQPosted] = useState("");
  const [qCustomer, setQCustomer] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);

  // Delete modal state
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deletingReturn, setDeletingReturn] = useState(null);
  const [deleting, setDeleting] = useState(false);

  const navigate = useNavigate();

  // AbortController for fetches
  const controllerRef = useRef(null);

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("sale-return") : {
        view: false, create: false, update: false, delete: false, import: false, export: false
      }),
    [canFor]
  );

  // Get theme colors
  const { theme, isDark } = useTheme();

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
        danger: '#ef4444',
        dangerHover: '#dc2626',
        dangerLight: '#fee2e2',
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
        tertiaryLight: '#cffafe',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
      tertiary: theme.tertiary_color || '#06b6d4',
      tertiaryHover: theme.tertiary_hover || '#0891b2',
      tertiaryLight: theme.tertiary_light || '#cffafe',
      danger: theme.danger_color || '#ef4444',
      dangerHover: '#dc2626',
      dangerLight: '#fee2e2',
    };
  }, [theme]);

  // Get button style from theme
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
            borderColor: themeColors.danger,
            color: themeColors.danger,
            backgroundColor: 'transparent',
          }
        },
        glass: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: '#64748b',
            color: '#64748b',
            backgroundColor: 'transparent',
          }
        },
      };
    }
    
    // Filled styles for rounded and soft
    const primaryTextColor = getButtonTextColor(themeColors.primary, themeColors.primaryHover);
    const secondaryTextColor = getButtonTextColor(themeColors.secondary, themeColors.secondaryHover);
    const dangerTextColor = getButtonTextColor(themeColors.danger, themeColors.dangerHover);
    
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
      danger: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
          color: dangerTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.danger}40`,
        }
      },
      glass: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, #64748b, #475569)`,
          color: 'white',
          boxShadow: `0 4px 14px 0 #64748b40`,
        }
      },
    };
  }, [buttonStyle, themeColors]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;
  const btnGlass = getButtonClasses.glass;

  // Fetch returns
  const fetchReturns = useCallback(async (signal) => {
    if (permsLoading || !can.view) {
      setReturns([]);
      setLoading(false);
      return;
    }
    try {
      setLoading(true);
      const res = await axios.get("/api/sale-returns", { signal });
      const data = Array.isArray(res.data) ? res.data : [];
      setReturns(data);
      setTotal(data.length);
    } catch (e) {
      if (axios.isCancel?.(e)) return;
      const status = e?.response?.status;
      if (status === 403) toast.error("You don't have permission to view sale returns.");
      else toast.error("Failed to fetch sale returns");
      setReturns([]);
    } finally {
      setLoading(false);
    }
  }, [permsLoading, can.view]);

  // Initial fetch
  useEffect(() => {
    if (permsLoading || !can.view) return;
    const ctrl = new AbortController();
    controllerRef.current = ctrl;
    fetchReturns(ctrl.signal);
    return () => ctrl.abort();
  }, [fetchReturns, permsLoading, can.view]);

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
      navigate("/sale-returns/create");
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate, can.create]);

  // ===== search + pagination =====
  const norm = (v) => (v ?? "").toString().toLowerCase().trim();
  const filtered = useMemo(() => {
    const nPosted = norm(qPosted);
    const nCust = norm(qCustomer);
    return returns.filter((r) => {
      const posted = norm(r.posted_number);
      const customer = norm(r.customer?.name);
      return posted.includes(nPosted) && customer.includes(nCust);
    });
  }, [returns, qPosted, qCustomer]);

  useEffect(() => { setPage(1); }, [qPosted, qCustomer]);

  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize));
  useEffect(() => { if (page > pageCount) setPage(pageCount); }, [page, pageCount]);

  const start = (page - 1) * pageSize;
  const paged = filtered.slice(start, start + pageSize);

  // ===== delete modal handlers =====
  const openDeleteModal = (ret) => {
    if (!can.delete) return toast.error("You don't have permission to delete sale returns.");
    setDeletingReturn({ id: ret.id, name: ret.posted_number });
    setDeleteModalOpen(true);
  };

  const closeDeleteModal = () => {
    setDeleteModalOpen(false);
    setDeletingReturn(null);
  };

  const handleConfirmDelete = async (password) => {
    if (!deletingReturn?.id) return;
    if (!can.delete) return toast.error("You don't have permission to delete sale returns.");

    try {
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/sale-returns/${deletingReturn.id}`);
      toast.success("Sale return deleted");
      closeDeleteModal();

      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      fetchReturns(ctrl.signal);
    } catch (err) {
      const status = err?.response?.status;
      const apiMsg =
        err?.response?.data?.message ||
        (status === 422 ? "Incorrect password" : status === 403 ? "You don't have permission to delete sale returns." : "Failed to delete sale return");
      toast.error(apiMsg);
    }
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

  // Check if has actions
  const hasActions = can.update || can.delete;

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view sale returns.</div>;

  return (
    <div className="p-4 space-y-3">
      {/* ===== Premium Gradient Hero Header ===== */}
      <div
        className="relative overflow-hidden rounded-2xl shadow-lg border border-white/10"
        style={{
          background: `linear-gradient(135deg, ${themeColors.secondary}, ${themeColors.primary}, ${themeColors.primaryHover})`,
        }}
      >
        {/* Decorative blurred blobs */}
        <div
          className="absolute -top-16 -right-16 w-64 h-64 rounded-full opacity-30 blur-3xl pointer-events-none"
          style={{ backgroundColor: "#ffffff" }}
        />
        <div
          className="absolute -bottom-20 -left-10 w-72 h-72 rounded-full opacity-20 blur-3xl pointer-events-none"
          style={{ backgroundColor: themeColors.tertiary }}
        />

        {/* Hero Top */}
        <div className="relative flex items-center justify-between px-5 py-4">
          <div className="flex items-center gap-3">
            <div
              className="p-2.5 rounded-xl shadow-inner"
              style={{ backgroundColor: "rgba(255,255,255,0.18)", backdropFilter: "blur(4px)" }}
            >
              <DocumentTextIcon className="w-6 h-6 text-white" />
            </div>
            <div>
              <h1 className="text-xl font-extrabold tracking-wide text-white leading-none">Sale Returns</h1>
              <p className="text-xs text-white/80 mt-1">{total} return(s) in records</p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <button
              onClick={() => {
                if (controllerRef.current) controllerRef.current.abort();
                const ctrl = new AbortController();
                controllerRef.current = ctrl;
                fetchReturns(ctrl.signal);
              }}
              className="h-10 px-4 inline-flex items-center gap-2 rounded-xl text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm border border-white/20 transition-all duration-200 shadow-lg"
              title="Refresh"
              aria-label="Refresh sale returns"
            >
              <ArrowPathIcon className="w-4 h-4" />
              Refresh
            </button>

            <Guard when={can.create}>
              <Link
                to="/sale-returns/create"
                title="Add Sale Return (Alt+N)"
                aria-keyshortcuts="Alt+N"
                className="h-10 px-4 inline-flex items-center gap-1.5 rounded-xl text-xs font-bold text-white bg-white/95 hover:bg-white shadow-lg transition-all duration-200"
                style={{ color: themeColors.primaryHover }}
              >
                <PlusCircleIcon className="w-4 h-4" />
                Add Return
              </Link>
            </Guard>
          </div>
        </div>

        {/* Integrated Search Bar */}
        <div className="relative px-5 pb-4">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3 rounded-xl p-3"
            style={{ backgroundColor: "rgba(255,255,255,0.12)", backdropFilter: "blur(8px)", border: "1px solid rgba(255,255,255,0.15)" }}>
            <div className="relative">
              <div className="absolute inset-y-0 left-3 flex items-center pointer-events-none">
                <DocumentTextIcon className="w-4 h-4 text-white/70" />
              </div>
              <input
                type="text"
                value={qPosted}
                onChange={(e) => setQPosted(e.target.value)}
                placeholder="Search by Posted No (e.g., SR-000001 or SRRET-0001)…"
                className="w-full h-10 pl-9 pr-3 rounded-lg text-sm text-white placeholder-white/60 bg-white/15 border border-white/20 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/40"
                autoComplete="off"
              />
            </div>
            <div className="relative">
              <div className="absolute inset-y-0 left-3 flex items-center pointer-events-none">
                <UserIcon className="w-4 h-4 text-white/70" />
              </div>
              <input
                type="text"
                value={qCustomer}
                onChange={(e) => setQCustomer(e.target.value)}
                placeholder="Search by Customer…"
                className="w-full h-10 pl-9 pr-3 rounded-lg text-sm text-white placeholder-white/60 bg-white/15 border border-white/20 backdrop-blur-sm focus:outline-none focus:ring-2 focus:ring-white/40"
                autoComplete="off"
              />
            </div>
          </div>
        </div>
      </div>

      {/* ===== Returns Table Card ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        {/* Table Header */}
        <div className="flex items-center justify-between px-4 py-3 bg-gray-50/70 dark:bg-slate-800/70 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div className="p-1.5 rounded-lg shadow-sm" style={{ background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})` }}>
              <DocumentTextIcon className="w-4 h-4 text-white" />
            </div>
            <div>
              <span className="text-sm font-semibold text-gray-800 dark:text-gray-200">Return List</span>
              <p className="text-[11px] text-gray-400">
                {loading ? "Loading…" : `${start + 1}-${Math.min(filtered.length, start + pageSize)} of ${filtered.length}`}
              </p>
            </div>
          </div>

          {/* Page Size Selector */}
          <div className="flex items-center gap-1.5 px-2 py-1 rounded-lg bg-white/70 dark:bg-slate-700/60 border border-gray-200 dark:border-slate-600/40">
            <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Show</label>
            <select
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
              className="h-7 px-2 rounded border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-xs font-medium text-gray-700 dark:text-gray-200 focus:ring-2 focus:border-transparent cursor-pointer"
              style={{ '--tw-ring-color': themeColors.primary }}
            >
              <option value={10}>10</option>
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
          </div>
        </div>

        <div className="max-h-[65vh] overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
              <tr className="text-left">
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">#</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Posted No</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Customer</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Date</th>
                <th className="px-3 py-2 font-semibold text-right text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Amount</th>
                {hasActions && (
                  <th className="px-3 py-2 font-semibold text-center text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-32">Actions</th>
                )}
              </tr>
            </thead>

            <tbody>
              {paged.length === 0 && !loading && (
                <tr>
                  <td className="px-3 py-12 text-center" colSpan={hasActions ? 6 : 5}>
                    <div className="flex flex-col items-center gap-2">
                      <DocumentTextIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">No returns found</p>
                    </div>
                  </td>
                </tr>
              )}

              {loading && (
                <tr>
                  <td className="px-3 py-12 text-center" colSpan={hasActions ? 6 : 5}>
                    <div className="flex flex-col items-center gap-2">
                      <ArrowPathIcon className="w-6 h-6 text-gray-400 animate-spin" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">Loading...</p>
                    </div>
                  </td>
                </tr>
              )}

              {paged.map((ret, idx) => {
                const retTotal = Number(ret.total ?? 0);

                return (
                  <tr
                    key={ret.id}
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
                      <span 
                        className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-medium"
                        style={{
                          backgroundColor: themeColors.secondaryLight,
                          color: themeColors.secondary
                        }}
                      >
                        <DocumentTextIcon className="w-3.5 h-3.5" />
                        {ret.posted_number || "-"}
                      </span>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <UserIcon 
                          className="w-4 h-4" 
                          style={{ color: themeColors.tertiary }}
                        />
                        <span className="text-gray-800 dark:text-gray-200">{ret.customer?.name ?? "N/A"}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <CalendarIcon 
                          className="w-4 h-4" 
                          style={{ color: themeColors.tertiary }}
                        />
                        <span className="text-gray-800 dark:text-gray-200">{formatDate(ret.date)}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3 text-right">
                      <div className="flex items-center justify-end gap-1">
                        <CurrencyDollarIcon 
                          className="w-4 h-4" 
                          style={{ color: themeColors.primary }}
                        />
                        <span className="font-semibold text-gray-800 dark:text-gray-200">
                          {retTotal.toLocaleString()}
                        </span>
                      </div>
                    </td>
                    {hasActions && (
                      <td className="px-3 py-3">
                        <div className="flex items-center gap-1.5 justify-center">
                          {/* Edit Action */}
                          <Guard when={can.update}>
                            <Link
                              to={`/sale-returns/${ret.id}/edit`}
                              className={`group inline-flex items-center gap-1 px-3 py-1.5 text-xs font-semibold transition-all duration-200 ${btnPrimary.className}`}
                              style={btnPrimary.style}
                              title="Edit"
                            >
                              <PencilSquareIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                              <span>Edit</span>
                            </Link>
                          </Guard>

                          {/* Delete Action */}
                          <Guard when={can.delete}>
                            <button
                              onClick={() => openDeleteModal(ret)}
                              className={`group inline-flex items-center gap-1 px-3 py-1.5 text-xs font-semibold transition-all duration-200 ${btnDanger.className}`}
                              style={btnDanger.style}
                              title="Delete"
                            >
                              <TrashIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                              <span>Delete</span>
                            </button>
                          </Guard>
                        </div>
                      </td>
                    )}
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
                      w-7 h-7 rounded text-xs font-medium transition-all duration-200
                      ${page === pageNum
                        ? ""
                        : "text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-slate-700"
                      }
                    `}
                    style={page === pageNum ? btnSecondary.style : {}}
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
        itemName={deletingReturn?.name || "this return"}
        title="Delete sale return"
        isDeleting={deleting}
        setIsDeleting={setDeleting}
        tintClasses={{ 
          red: `bg-gradient-to-br from-rose-500 to-rose-600 text-white shadow-lg shadow-rose-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700 active:scale-[0.98] transition-all duration-200`,
          glass: "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200"
        }}
      />
    </div>
  );
}

