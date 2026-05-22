import React, { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  MagnifyingGlassIcon,
  PlusCircleIcon,
  EyeIcon,
  PencilSquareIcon,
  DocumentTextIcon,
  PrinterIcon,
  ArrowPathIcon,
  TrashIcon,
  DocumentTextIcon as DocIcon,
  CalendarIcon,
  UserIcon,
  CurrencyDollarIcon,
} from "@heroicons/react/24/solid";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";
import { recalcQuotationFooter } from "../../Formula/Quotation.js";
import {
  GlassBtn,
  DeleteConfirmationModal,
  TextSearch,
} from "@/components";

const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

const getButtonTextColor = (primaryColor, primaryHoverColor) =>
  getContrastText(primaryHoverColor || primaryColor);

export default function QuotationsIndex() {
  const [quotations, setQuotations] = useState([]);
  const [loading, setLoading] = useState(true);

  const [qPosted, setQPosted] = useState("");
  const [qCustomer, setQCustomer] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);
  const [lastPage, setLastPage] = useState(1);

  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deletingQuotation, setDeletingQuotation] = useState(null);
  const [deleting, setDeleting] = useState(false);

  const navigate = useNavigate();

  const controllerRef = useRef(null);
  const debounceRef = useRef(null);

  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("quotation") : {
        view: false, create: false, update: false, delete: false,
      }),
    [canFor]
  );

  const { theme, isDark } = useTheme();

  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#3b82f6',
        primaryHover: '#2563eb',
        secondary: '#8b5cf6',
        secondaryHover: '#7c3aed',
        danger: '#ef4444',
        dangerHover: '#dc2626',
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      danger: theme.danger_color || '#ef4444',
      dangerHover: '#dc2626',
      tertiary: theme.tertiary_color || '#06b6d4',
      tertiaryHover: theme.tertiary_hover || '#0891b2',
    };
  }, [theme]);

  const buttonStyle = theme?.button_style || 'rounded';
  const getButtonClasses = useMemo(() => {
    const radiusMap = { rounded: 'rounded-lg', outlined: 'rounded-lg', soft: 'rounded-xl' };
    const radiusClass = radiusMap[buttonStyle] || 'rounded-lg';

    if (buttonStyle === 'outlined') {
      return {
        primary: { className: `${radiusClass} border-2`, style: { borderColor: themeColors.primary, color: themeColors.primary, backgroundColor: 'transparent' } },
        secondary: { className: `${radiusClass} border-2`, style: { borderColor: themeColors.secondary, color: themeColors.secondary, backgroundColor: 'transparent' } },
        danger: { className: `${radiusClass} border-2`, style: { borderColor: themeColors.danger, color: themeColors.danger, backgroundColor: 'transparent' } },
        tertiary: { className: `${radiusClass} border-2`, style: { borderColor: themeColors.tertiary, color: themeColors.tertiary, backgroundColor: 'transparent' } },
      };
    }

    return {
      primary: { className: radiusClass, style: { background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`, color: 'white', boxShadow: `0 4px 14px 0 ${themeColors.primary}40` } },
      secondary: { className: radiusClass, style: { background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`, color: 'white', boxShadow: `0 4px 14px 0 ${themeColors.secondary}40` } },
      tertiary: { className: radiusClass, style: { background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})`, color: 'white', boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40` } },
      danger: { className: radiusClass, style: { background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`, color: 'white' } },
    };
  }, [buttonStyle, themeColors, isDark]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnTertiary = getButtonClasses.tertiary;
  const btnDanger = getButtonClasses.danger;

  const fetchQuotations = async (signal, options = {}) => {
    if (permsLoading || !can.view) {
      setQuotations([]);
      setLoading(false);
      return;
    }

    const { pageArg = page, pageSizeArg = pageSize, qPostedArg = qPosted, qCustomerArg = qCustomer } = options;

    try {
      setLoading(true);
      const res = await axios.get("/api/quotations", {
        params: {
          page: pageArg,
          per_page: pageSizeArg,
          posted: qPostedArg.trim(),
          customer: qCustomerArg.trim(),
        },
        signal,
      });

      const data = res.data;
      const items = Array.isArray(data?.data) ? data.data : (Array.isArray(data) ? data : []);
      setQuotations(items);
      setTotal(Number(data?.total ?? items.length ?? 0));
      const lp = Number(data?.last_page ?? 1);
      setLastPage(lp);
    } catch (e) {
      if (axios.isCancel?.(e)) return;
      const status = e?.response?.status;
      if (status === 403) toast.error("You don't have permission to view quotations.");
      else toast.error("Failed to fetch quotations");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (permsLoading || !can.view) return;
    const ctrl = new AbortController();
    controllerRef.current = ctrl;
    fetchQuotations(ctrl.signal, { pageArg: page, pageSizeArg: pageSize, qPostedArg: qPosted, qCustomerArg: qCustomer });
    return () => ctrl.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, pageSize, permsLoading, can.view]);

  useEffect(() => {
    if (permsLoading || !can.view) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    const ctrl = new AbortController();
    debounceRef.current = setTimeout(() => {
      setPage(1);
      fetchQuotations(ctrl.signal, { pageArg: 1, qPostedArg: qPosted, qCustomerArg: qCustomer });
    }, 300);

    return () => {
      clearTimeout(debounceRef.current);
      ctrl.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [qPosted, qCustomer, permsLoading, can.view]);

  const start = quotations.length ? (page - 1) * pageSize + 1 : 0;
  const end = quotations.length ? start + quotations.length - 1 : 0;

  const openDeleteModal = (quotation) => {
    if (!can.delete) return toast.error("You don't have permission to delete quotations.");
    setDeletingQuotation({ id: quotation.id, name: quotation.posted_number });
    setDeleteModalOpen(true);
  };

  const closeDeleteModal = () => {
    setDeleteModalOpen(false);
    setDeletingQuotation(null);
  };

  const handleConfirmDelete = async (password) => {
    if (!deletingQuotation?.id) return;
    if (!can.delete) return toast.error("You don't have permission to delete quotations.");

    try {
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/quotations/${deletingQuotation.id}`);
      toast.success("Quotation deleted");
      closeDeleteModal();

      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      fetchQuotations(ctrl.signal, { pageArg: page, pageSizeArg: pageSize, qPostedArg: qPosted, qCustomerArg: qCustomer });
    } catch (err) {
      const status = err?.response?.status;
      const apiMsg =
        err?.response?.data?.message ||
        (status === 422 ? "Incorrect password" : status === 403 ? "You don't have permission to delete quotations." : "Delete failed");
      toast.error(apiMsg);
    }
  };

  const handlePrint = (id) => {
    const WEB_BASE = (import.meta.env.VITE_BACKEND_WEB_BASE || "").replace(/\/$/, "") || window.location.origin;
    const url = `${WEB_BASE}/print/quotations/${id}`;
    const w = window.open(url, "quotationPrintWin", "width=900,height=700,menubar=no,toolbar=no,location=no,status=no,resizable=yes,scrollbars=yes");
    if (!w) toast.error("Popup blocked. Please allow popups to print.");
  };

  const formatDate = (dateStr) => {
    if (!dateStr) return "-";
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return dateStr;
    const months = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"];
    return `${d.getDate()} ${months[d.getMonth()]} ${d.getFullYear()}`;
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view quotations.</div>;

  return (
    <div className="p-4 space-y-3">
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br from-[${themeColors.primary}] to-[${themeColors.primaryHover}] shadow-sm`}>
              <DocIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Quotations</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{total} items</p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <GlassBtn
              onClick={() => {
                if (controllerRef.current) controllerRef.current.abort();
                const ctrl = new AbortController();
                controllerRef.current = ctrl;
                fetchQuotations(ctrl.signal, { pageArg: page, pageSizeArg: pageSize, qPostedArg: qPosted, qCustomerArg: qCustomer });
              }}
              className={`h-10 min-w-[120px] ${btnTertiary.className}`}
              title="Refresh"
              aria-label="Refresh quotations"
              style={btnTertiary.style}
            >
              <span className="inline-flex items-center gap-2">
                <ArrowPathIcon className="w-5 h-5" />
                Refresh
              </span>
            </GlassBtn>

            <Guard when={can.create}>
              <Link
                to="/quotations/create"
                className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${btnPrimary.className}`}
                style={btnPrimary.style}
              >
                <PlusCircleIcon className="w-4 h-4" />
                Add Quotation
              </Link>
            </Guard>
          </div>
        </div>

        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <TextSearch
              value={qPosted}
              onChange={setQPosted}
              placeholder="Search by Posted No…"
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

        <div className="flex items-center justify-between px-4 py-2 border-t border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-4">
            <span className="text-xs text-gray-500 dark:text-gray-400">
              {loading ? "Loading…" : `${quotations.length === 0 ? 0 : start}-${end} of ${total}`}
            </span>
          </div>

          <div className="flex items-center gap-2">
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

      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div className="p-1 rounded bg-blue-50 dark:bg-blue-900/30">
              <DocumentTextIcon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
            </div>
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Quotation List</span>
          </div>
          <span className="text-xs text-gray-400">{quotations.length} items</span>
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
              {quotations.length === 0 && !loading && (
                <tr>
                  <td className="px-3 py-12 text-center" colSpan={6}>
                    <div className="flex flex-col items-center gap-2">
                      <DocumentTextIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">No quotations found</p>
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

              {quotations.map((q, idx) => {
                const totalNum = Number(q.total ?? 0);
                return (
                  <tr
                    key={q.id}
                    className={`transition-colors odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50 border-b border-gray-100 dark:border-slate-600/30`}
                  >
                    <td className="px-3 py-3 text-gray-600 dark:text-gray-300">{start + idx + 1}</td>
                    <td className="px-3 py-3">
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 text-xs font-medium">
                        <DocumentTextIcon className="w-3.5 h-3.5" />
                        {q.posted_number || "-"}
                      </span>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <UserIcon className="w-4 h-4 text-gray-400" />
                        <span className="text-gray-800 dark:text-gray-200">{q.customer?.name ?? "N/A"}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <CalendarIcon className="w-4 h-4 text-gray-400" />
                        <span className="text-gray-800 dark:text-gray-200">{formatDate(q.date)}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3 text-right">
                      <div className="flex items-center justify-end gap-1">
                        <CurrencyDollarIcon className="w-4 h-4 text-emerald-500" />
                        <span className="font-semibold text-gray-800 dark:text-gray-200">{totalNum.toLocaleString()}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-1.5 justify-center">
                        <Link
                          to={`/quotations/${q.id}`}
                          className={`group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold transition-all duration-200 ${btnPrimary.className}`}
                          style={btnPrimary.style}
                          title="View"
                        >
                          <EyeIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                          <span>View</span>
                        </Link>

                        <Guard when={can.update}>
                          <Link
                            to={`/quotations/${q.id}/edit`}
                            className={`group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold transition-all duration-200 ${btnSecondary.className}`}
                            style={btnSecondary.style}
                            title="Edit"
                          >
                            <PencilSquareIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                            <span>Edit</span>
                          </Link>
                        </Guard>

                        <button
                          onClick={() => handlePrint(q.id)}
                          className={`group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold transition-all duration-200 ${btnTertiary.className}`}
                          style={btnTertiary.style}
                          title="Print"
                        >
                          <PrinterIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                          <span>Print</span>
                        </button>

                        <Guard when={can.delete}>
                          <button
                            onClick={() => openDeleteModal(q)}
                            className={`group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold transition-all duration-200 ${btnDanger.className}`}
                            style={btnDanger.style}
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

        <div className="px-3 py-2 flex items-center justify-between border-t border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800/50 text-xs">
          <span className="text-gray-500 dark:text-gray-400">Page {page} of {lastPage} ({total} total)</span>
          <div className="flex items-center gap-1">
            <button onClick={() => setPage(1)} disabled={page === 1} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>⏮</button>
            <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>◀</button>
            <div className="flex items-center gap-0.5 mx-1">
              {Array.from({ length: Math.min(5, lastPage) }, (_, i) => {
                let pageNum;
                if (lastPage <= 5) pageNum = i + 1;
                else if (page <= 3) pageNum = i + 1;
                else if (page >= lastPage - 2) pageNum = lastPage - 4 + i;
                else pageNum = page - 2 + i;
                return (
                  <button
                    key={pageNum}
                    onClick={() => setPage(pageNum)}
                    className={`w-7 h-7 rounded text-xs font-medium transition-colors ${page === pageNum ? '' : 'text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-slate-700'}`}
                    style={page === pageNum ? btnPrimary.style : {}}
                  >
                    {pageNum}
                  </button>
                );
              })}
            </div>
            <button onClick={() => setPage((p) => Math.min(lastPage, p + 1))} disabled={page === lastPage} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === lastPage ? 'opacity-40' : ''}`}>▶</button>
            <button onClick={() => setPage(lastPage)} disabled={page === lastPage} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === lastPage ? 'opacity-40' : ''}`}>⏭</button>
          </div>
        </div>
      </div>

      <DeleteConfirmationModal
        isOpen={deleteModalOpen}
        onClose={closeDeleteModal}
        onConfirm={handleConfirmDelete}
        itemName={deletingQuotation?.name || "this quotation"}
        title="Delete quotation"
        isDeleting={deleting}
        setIsDeleting={setDeleting}
        tintClasses={{
          red: btnDanger.className,
          redStyle: btnDanger.style,
          glass: '',
        }}
        glassStyle={{}}
      />
    </div>
  );
}

