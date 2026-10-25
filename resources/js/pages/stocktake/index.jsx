// resources/js/pages/stocktake/index.jsx
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  ClipboardDocumentListIcon,
  PlusCircleIcon,
  TrashIcon,
  ArrowPathIcon,
  MagnifyingGlassIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  ChevronDoubleLeftIcon,
  ChevronDoubleRightIcon,
  EyeIcon,
  XMarkIcon,
  CheckCircleIcon,
  ClockIcon,
  ExclamationCircleIcon,
} from "@heroicons/react/24/solid";
import Select from "react-select";
import { usePermissions } from "@/api/usePermissions.js";
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
  DeleteConfirmationModal,
} from "@/components";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";

const getContrastText = (hex) => {
  hex = hex.replace("#", "");
  const r = parseInt(hex.substring(0, 2), 16);
  const g = parseInt(hex.substring(2, 4), 16);
  const b = parseInt(hex.substring(4, 6), 16);
  return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.5 ? "#1f2937" : "#ffffff";
};

const STATUS_LABELS = {
  draft: "Draft",
  in_progress: "In Progress",
  pending_review: "Pending Review",
  finalized: "Finalized",
  cancelled: "Cancelled",
};

const STATUS_COLORS = {
  draft: "bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300",
  in_progress: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  pending_review: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  finalized: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300",
  cancelled: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
};

const STATUS_ICON = {
  draft: <ClockIcon className="w-3.5 h-3.5" />,
  in_progress: <ArrowPathIcon className="w-3.5 h-3.5" />,
  pending_review: <ExclamationCircleIcon className="w-3.5 h-3.5" />,
  finalized: <CheckCircleIcon className="w-3.5 h-3.5" />,
  cancelled: <XMarkIcon className="w-3.5 h-3.5" />,
};

const STATUS_OPTIONS = [
  { value: "", label: "All Statuses" },
  { value: "draft", label: "Draft" },
  { value: "in_progress", label: "In Progress" },
  { value: "pending_review", label: "Pending Review" },
  { value: "finalized", label: "Finalized" },
  { value: "cancelled", label: "Cancelled" },
];

export default function StocktakeIndex() {
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [q, setQ] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize] = useState(15);
  const [total, setTotal] = useState(0);

  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deletingSession, setDeletingSession] = useState(null);
  const [deleting, setDeleting] = useState(false);

  const navigate = useNavigate();
  const controllerRef = useRef(null);
  const { loading: permsLoading, has } = usePermissions();
  const { isDark, theme } = useTheme();

  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || "#3b82f6",
    primaryHover: theme?.primary_hover || "#2563eb",
    primaryLight: theme?.primary_light || "#dbeafe",
    secondary: theme?.secondary_color || "#8b5cf6",
    secondaryHover: theme?.secondary_hover || "#7c3aed",
    secondaryLight: theme?.secondary_light || "#ede9fe",
    danger: theme?.danger_color || "#ef4444",
    dangerHover: "#dc2626",
    dangerLight: "#fee2e2",
  }), [theme]);

  const primaryTextColor = useMemo(
    () => getContrastText(themeColors.primaryHover || themeColors.primary),
    [themeColors]
  );
  const buttonStyle = theme?.button_style || "modern";
  const getButtonClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark, primaryTextColor),
    [buttonStyle, themeColors, isDark, primaryTextColor]
  );
  const btnPrimary = getButtonClasses.primary;
  const btnDanger = getButtonClasses.danger;

  // React-Select dark mode styles
  const selectStyles = useMemo(() => ({
    control: (base) => ({
      ...base,
      backgroundColor: isDark ? "#1e293b" : "#ffffff",
      borderColor: isDark ? "#475569" : "#d1d5db",
      color: isDark ? "#e2e8f0" : "#111827",
      minHeight: "36px",
    }),
    singleValue: (base) => ({ ...base, color: isDark ? "#e2e8f0" : "#111827" }),
    menu: (base) => ({ ...base, backgroundColor: isDark ? "#1e293b" : "#ffffff", zIndex: 50 }),
    option: (base, state) => ({
      ...base,
      backgroundColor: state.isFocused ? (isDark ? "#334155" : "#f1f5f9") : "transparent",
      color: isDark ? "#e2e8f0" : "#111827",
    }),
    input: (base) => ({ ...base, color: isDark ? "#e2e8f0" : "#111827" }),
  }), [isDark]);

  const fetchSessions = useCallback(() => {
    if (controllerRef.current) controllerRef.current.abort();
    const ctrl = new AbortController();
    controllerRef.current = ctrl;
    setLoading(true);

    axios
      .get("/api/stocktake-sessions", {
        params: { q, status: statusFilter, page, per_page: pageSize },
        signal: ctrl.signal,
      })
      .then(({ data }) => {
        setRows(Array.isArray(data?.data) ? data.data : (Array.isArray(data) ? data : []));
        setTotal(data?.total || 0);
      })
      .catch((err) => {
        if (axios.isCancel(err)) return;
        toast.error("Failed to load stocktake sessions");
      })
      .finally(() => setLoading(false));
  }, [q, statusFilter, page, pageSize]);

  useEffect(() => {
    fetchSessions();
    return () => controllerRef.current?.abort();
  }, [fetchSessions]);

  // Reset page when filter/search changes
  useEffect(() => { setPage(1); }, [q, statusFilter]);

  const pageCount = Math.max(1, Math.ceil(total / pageSize));

  const handleDeleteClick = (session) => {
    setDeletingSession(session);
    setDeleteModalOpen(true);
  };

  const handleDeleteConfirm = async (password) => {
    if (!deletingSession) return;
    setDeleting(true);
    try {
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/stocktake-sessions/${deletingSession.id}`);
      toast.success("Session deleted");
      setDeleteModalOpen(false);
      setDeletingSession(null);
      fetchSessions();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Delete failed");
    } finally {
      setDeleting(false);
    }
  };

  const canCreate = !permsLoading && has("stocktake.create");
  const canDelete = !permsLoading && has("stocktake.delete");
  const canView = !permsLoading && has("stocktake.view");

  return (
    <div className="space-y-4 px-1">
      {/* Header */}
      <GlassSectionHeader
        title="Stocktake Sessions"
        right={
          <p className={`text-xs ${isDark ? "text-slate-400" : "text-slate-500"}`}>
            Cycle counts &amp; blind audits for physical inventory accuracy
          </p>
        }
      />

      {/* Toolbar */}
      <GlassToolbar>
        <div className="flex flex-wrap items-center gap-3">
          {/* Search */}
          <div className="relative min-w-[200px]">
            <MagnifyingGlassIcon className="absolute left-2.5 top-2 w-4 h-4 text-slate-400" />
            <GlassInput
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Search ref / notes…"
              className="pl-8 text-sm h-9"
            />
          </div>

          {/* Status filter */}
          <div className="min-w-[180px]">
            <Select
              options={STATUS_OPTIONS}
              value={STATUS_OPTIONS.find((o) => o.value === statusFilter) || STATUS_OPTIONS[0]}
              onChange={(opt) => setStatusFilter(opt?.value || "")}
              styles={selectStyles}
              isSearchable={false}
              placeholder="All Statuses"
            />
          </div>

          {/* Refresh */}
          <GlassBtn onClick={fetchSessions} title="Refresh" className="h-9 px-3">
            <ArrowPathIcon className="w-4 h-4" />
          </GlassBtn>
        </div>

        {canCreate && (
          <GlassBtn
            onClick={() => navigate("/stocktake-sessions/create")}
            className={`h-9 px-4 gap-1.5 ${btnPrimary.className}`}
            style={btnPrimary.style}
          >
            <PlusCircleIcon className="w-4 h-4" />
            New Session
          </GlassBtn>
        )}
      </GlassToolbar>

      {/* Table */}
      <GlassCard className="overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className={`border-b text-xs font-semibold uppercase tracking-wide ${
                isDark ? "border-slate-700 bg-slate-800/60 text-slate-400" : "border-slate-200 bg-slate-50 text-slate-500"
              }`}>
                <th className="py-3 px-4 text-left">Reference</th>
                <th className="py-3 px-4 text-left">Type</th>
                <th className="py-3 px-4 text-left">Scope</th>
                <th className="py-3 px-4 text-center">Blind</th>
                <th className="py-3 px-4 text-left">Status</th>
                <th className="py-3 px-4 text-center">Progress</th>
                <th className="py-3 px-4 text-left">Created</th>
                <th className="py-3 px-4 text-center">Actions</th>
              </tr>
            </thead>
            <tbody className={`divide-y ${isDark ? "divide-slate-800" : "divide-slate-100"}`}>
              {loading ? (
                <tr>
                  <td colSpan={8} className="py-12 text-center text-slate-400">
                    <ArrowPathIcon className="w-6 h-6 mx-auto animate-spin mb-2" />
                    Loading…
                  </td>
                </tr>
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={8} className="py-12 text-center text-slate-400">
                    <ClipboardDocumentListIcon className="w-10 h-10 mx-auto mb-2 opacity-30" />
                    No sessions found.{" "}
                    {canCreate && (
                      <Link to="/stocktake-sessions/create" className="underline text-blue-500">
                        Create one
                      </Link>
                    )}
                  </td>
                </tr>
              ) : (
                rows.map((s) => {
                  const counted = s.counted_items_count ?? 0;
                  const total_ = s.items_count ?? 0;
                  const pct = total_ > 0 ? Math.round((counted / total_) * 100) : 0;
                  return (
                    <tr
                      key={s.id}
                      className={`transition-colors ${
                        isDark ? "hover:bg-slate-800/40" : "hover:bg-slate-50/70"
                      }`}
                    >
                      {/* Reference */}
                      <td className="py-3 px-4 font-mono font-semibold text-xs">
                        <Link
                          to={`/stocktake-sessions/${s.id}`}
                          className="hover:underline"
                          style={{ color: themeColors.primary }}
                        >
                          {s.reference_no}
                        </Link>
                      </td>

                      {/* Type */}
                      <td className="py-3 px-4">
                        <span className={`capitalize px-2 py-0.5 rounded text-xs font-medium ${
                          s.session_type === "full"
                            ? "bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300"
                            : "bg-cyan-100 text-cyan-700 dark:bg-cyan-900/40 dark:text-cyan-300"
                        }`}>
                          {s.session_type === "full" ? "Full Stocktake" : "Cycle Count"}
                        </span>
                      </td>

                      {/* Scope */}
                      <td className="py-3 px-4 text-xs text-slate-500 dark:text-slate-400">
                        {s.scope_type
                          ? <span className="capitalize">{s.scope_type}: #{s.scope_id}</span>
                          : <span className="italic">All Products</span>
                        }
                      </td>

                      {/* Blind */}
                      <td className="py-3 px-4 text-center">
                        {s.is_blind ? (
                          <span className="text-xs bg-orange-100 text-orange-700 dark:bg-orange-900/40 dark:text-orange-300 px-2 py-0.5 rounded-full font-medium">
                            Blind
                          </span>
                        ) : (
                          <span className="text-xs text-slate-400">Open</span>
                        )}
                      </td>

                      {/* Status */}
                      <td className="py-3 px-4">
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium ${
                          STATUS_COLORS[s.status] || STATUS_COLORS.draft
                        }`}>
                          {STATUS_ICON[s.status]}
                          {STATUS_LABELS[s.status] || s.status}
                        </span>
                      </td>

                      {/* Progress */}
                      <td className="py-3 px-4 text-center">
                        {total_ > 0 ? (
                          <div className="flex flex-col items-center gap-0.5">
                            <div className="w-24 h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
                              <div
                                className="h-full rounded-full bg-emerald-500 transition-all"
                                style={{ width: `${pct}%` }}
                              />
                            </div>
                            <span className="text-xs text-slate-500 dark:text-slate-400">
                              {counted}/{total_}
                            </span>
                          </div>
                        ) : (
                          <span className="text-xs text-slate-400">—</span>
                        )}
                      </td>

                      {/* Created */}
                      <td className="py-3 px-4 text-xs text-slate-500 dark:text-slate-400">
                        {new Date(s.created_at).toLocaleDateString()}
                      </td>

                      {/* Actions */}
                      <td className="py-3 px-4">
                        <div className="flex items-center justify-center gap-1.5">
                          {canView && (
                            <Link
                              to={`/stocktake-sessions/${s.id}`}
                              className="p-1.5 rounded-lg transition-colors text-blue-500 hover:bg-blue-50 dark:hover:bg-blue-900/30"
                              title="View / Count"
                            >
                              <EyeIcon className="w-4 h-4" />
                            </Link>
                          )}
                          {canDelete && ["draft", "cancelled"].includes(s.status) && (
                            <button
                              onClick={() => handleDeleteClick(s)}
                              className="p-1.5 rounded-lg transition-colors text-red-500 hover:bg-red-50 dark:hover:bg-red-900/30"
                              title="Delete"
                            >
                              <TrashIcon className="w-4 h-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        {!loading && total > pageSize && (
          <div className={`flex items-center justify-between gap-3 px-4 py-3 border-t text-xs ${
            isDark ? "border-slate-700 text-slate-400" : "border-slate-200 text-slate-500"
          }`}>
            <span>
              Showing {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, total)} of {total}
            </span>
            <div className="flex items-center gap-1">
              <button onClick={() => setPage(1)} disabled={page <= 1} className="p-1 rounded disabled:opacity-30 hover:bg-slate-200 dark:hover:bg-slate-700">
                <ChevronDoubleLeftIcon className="w-4 h-4" />
              </button>
              <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1} className="p-1 rounded disabled:opacity-30 hover:bg-slate-200 dark:hover:bg-slate-700">
                <ChevronLeftIcon className="w-4 h-4" />
              </button>
              <span className="px-2 font-medium">{page} / {pageCount}</span>
              <button onClick={() => setPage((p) => Math.min(pageCount, p + 1))} disabled={page >= pageCount} className="p-1 rounded disabled:opacity-30 hover:bg-slate-200 dark:hover:bg-slate-700">
                <ChevronRightIcon className="w-4 h-4" />
              </button>
              <button onClick={() => setPage(pageCount)} disabled={page >= pageCount} className="p-1 rounded disabled:opacity-30 hover:bg-slate-200 dark:hover:bg-slate-700">
                <ChevronDoubleRightIcon className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}
      </GlassCard>

      {/* Delete Confirmation Modal */}
      <DeleteConfirmationModal
        isOpen={deleteModalOpen}
        onClose={() => { setDeleteModalOpen(false); setDeletingSession(null); }}
        onConfirm={handleDeleteConfirm}
        isLoading={deleting}
        itemName={deletingSession?.reference_no}
      />
    </div>
  );
}

