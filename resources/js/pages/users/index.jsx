// src/pages/users/index.jsx
import React, { useEffect, useMemo, useRef, useState, useCallback } from "react";
import axios from "axios";
import { Link, useNavigate } from "react-router-dom";
import toast from "react-hot-toast";
import {
  TrashIcon,
  PencilSquareIcon,
  PlusCircleIcon,
  MagnifyingGlassIcon,
  ArrowPathIcon,
  UserCircleIcon,
  EnvelopeIcon,
  ShieldCheckIcon,
} from "@heroicons/react/24/solid";

// 🔒 permissions
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

export default function UsersIndex() {
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);

  // filters
  const [qSearch, setQSearch] = useState("");

  // pagination (server-side)
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);
  const [lastPage, setLastPage] = useState(1);

  // selection
  const [selectedIds, setSelectedIds] = useState(new Set());

  // Delete modal state
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deletingUser, setDeletingUser] = useState(null);
  const [deleting, setDeleting] = useState(false);

  const navigate = useNavigate();

  // AbortController + debounce for fetches
  const controllerRef = useRef(null);
  const debounceRef = useRef(null);

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function"
        ? canFor("user")
        : { view: false, create: false, update: false, delete: false }),
    [canFor]
  );

  // 🎨 Modern button palette (matching sidebar design language)
  const tintBlue = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] hover:from-blue-600 hover:to-blue-700 active:scale-[0.98] transition-all duration-200";
  const tintIndigo = "bg-gradient-to-br from-indigo-500 to-indigo-600 text-white shadow-lg shadow-indigo-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-indigo-500/30 hover:scale-[1.02] hover:from-indigo-600 hover:to-indigo-700 active:scale-[0.98] transition-all duration-200";
  const tintSlate = "bg-gradient-to-br from-slate-700 to-slate-800 text-white shadow-lg shadow-slate-500/25 ring-1 ring-white/10 hover:shadow-xl hover:shadow-slate-500/30 hover:scale-[1.02] hover:from-slate-800 hover:to-slate-900 active:scale-[0.98] transition-all duration-200";
  const tintAmber = "bg-gradient-to-br from-amber-500 to-amber-600 text-white shadow-lg shadow-amber-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-amber-500/30 hover:scale-[1.02] hover:from-amber-600 hover:to-amber-700 active:scale-[0.98] transition-all duration-200";
  const tintRed = "bg-gradient-to-br from-rose-500 to-rose-600 text-white shadow-lg shadow-rose-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700 active:scale-[0.98] transition-all duration-200";
  const tintGlass = "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";

  // Get dark mode state
  const { isDark } = useTheme();

  // === Alt+N => /users/create (only when can.create) ===
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
      navigate("/users/create");
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate, can.create]);

  const fetchUsers = useCallback(async (signal) => {
    try {
      setLoading(true);
      const { data } = await axios.get("/api/users", {
        params: {
          page,
          per_page: pageSize,
          search: qSearch.trim(),
        },
        signal,
      });

      // Expecting { data: [...], meta: {...} } or a raw array fallback
      const items = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
      const meta = data?.meta || data;
      setRows(items);
      setTotal(Number(meta?.total ?? items.length ?? 0));
      const lp = Number(meta?.last_page ?? 1);
      setLastPage(lp);
      if (page > lp) setPage(lp || 1);
    } catch (err) {
      if (axios.isCancel?.(err)) return;
      const status = err?.response?.status;
      if (status === 403 || status === 401) {
        toast.error("You don't have permission to view users.");
        return;
      }
      toast.error("Failed to load users");
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, qSearch]);

  // Initial + pager change (non-debounced)
  useEffect(() => {
    if (permsLoading || !can.view) return;
    if (controllerRef.current) controllerRef.current.abort();
    const ctrl = new AbortController();
    controllerRef.current = ctrl;
    fetchUsers(ctrl.signal);
  }, [permsLoading, can.view, page, pageSize, fetchUsers]);

  // Debounce filter changes (qSearch)
  useEffect(() => {
    if (permsLoading || !can.view) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setPage(1);
      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      fetchUsers(ctrl.signal);
    }, 300);
    return () => clearTimeout(debounceRef.current);
  }, [permsLoading, can.view, qSearch, fetchUsers]);

  const start = rows.length ? (page - 1) * pageSize + 1 : 0;
  const end = rows.length ? start + rows.length - 1 : 0;

  // selection helpers
  const pageAllChecked = rows.length > 0 && rows.every((u) => selectedIds.has(u.id));
  const pageIndeterminate = rows.some((u) => selectedIds.has(u.id)) && !pageAllChecked;

  const togglePageAll = (checked) => {
    setSelectedIds((prev) => {
      const copy = new Set(prev);
      if (checked) rows.forEach((u) => copy.add(u.id));
      else rows.forEach((u) => copy.delete(u.id));
      return copy;
    });
  };

  const toggleOne = (id, checked) => {
    setSelectedIds((prev) => {
      const copy = new Set(prev);
      if (checked) copy.add(id);
      else copy.delete(id);
      return copy;
    });
  };

  // ===== delete modal handlers =====
  const openDeleteModal = (u) => {
    if (!can.delete) return toast.error("You don't have permission to delete users.");
    setDeletingUser({ id: u.id, name: u.name, email: u.email });
    setDeleteModalOpen(true);
  };

  const closeDeleteModal = () => {
    setDeleteModalOpen(false);
    setDeletingUser(null);
  };

  const handleConfirmDelete = async (password) => {
    if (!deletingUser?.id) return;
    if (!can.delete) return toast.error("You don't have permission to delete users.");

    try {
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/users/${deletingUser.id}`);
      toast.success("User deleted");

      setSelectedIds((prev) => {
        const copy = new Set(prev);
        copy.delete(deletingUser.id);
        return copy;
      });

      closeDeleteModal();

      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      fetchUsers(ctrl.signal);
    } catch (err) {
      const status = err?.response?.status;
      const apiMsg =
        err?.response?.data?.message ||
        (status === 422 ? "Incorrect password" : status === 403 ? "You don't have permission to manage users." : "Delete failed");
      toast.error(apiMsg);
    }
  };

  // Check if has actions
  const hasActions = can.update || can.delete;

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view users.</div>;

  return (
    <div className="p-4 space-y-3">
      {/* ===== Professional Header ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.management.gradient} shadow-sm`}>
              <UserCircleIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Users</h1>
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
                fetchUsers(ctrl.signal);
              }}
              className={`h-10 min-w-[120px] ${tintSlate}`}
              title="Refresh"
              aria-label="Refresh users"
            >
              <span className="inline-flex items-center gap-2">
                <ArrowPathIcon className="w-5 h-5" />
                Refresh
              </span>
            </GlassBtn>

            <Guard when={can.create}>
              <Link
                to="/users/create"
                title="Add User (Alt+N)"
                aria-keyshortcuts="Alt+N"
                className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${tintBlue}`}
              >
                <PlusCircleIcon className="w-4 h-4" />
                Add User
              </Link>
            </Guard>
          </div>
        </div>

        {/* Search Bar */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <TextSearch
              value={qSearch}
              onChange={setQSearch}
              placeholder="Search by Name or Email…"
              icon={<MagnifyingGlassIcon className="w-4 h-4 text-gray-400" />}
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
                `${rows.length === 0 ? 0 : start}-${end} of ${total}`
              )}
            </span>
            {selectedIds.size > 0 && (
              <span className="text-xs px-2 py-0.5 rounded-full bg-violet-100 dark:bg-violet-900/30 text-violet-700 dark:text-violet-300 font-medium">
                {selectedIds.size} selected
              </span>
            )}
          </div>

          {/* Right Actions */}
          <div className="flex items-center gap-2">
            {/* Page Size Selector */}
            <div className="flex items-center gap-1.5 px-2 py-1 rounded-lg bg-white/60 dark:bg-slate-800/60 border border-gray-200/60 dark:border-slate-600/40">
              <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Show</label>
              <select
                value={pageSize}
                onChange={(e) => setPageSize(Number(e.target.value))}
                className="h-7 px-2 rounded border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-xs font-medium text-gray-700 dark:text-gray-200 focus:ring-2 focus:ring-violet-500 focus:border-transparent cursor-pointer"
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

      {/* ===== Users Table ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        {/* Table Header */}
        <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div className={`p-1 rounded ${SECTION_CONFIG.management.bgDark}`}>
              <UserCircleIcon className="w-4 h-4 text-violet-600 dark:text-violet-400" />
            </div>
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">User List</span>
          </div>
          <span className="text-xs text-gray-400">{rows.length} items</span>
        </div>

        <div className="max-h-[65vh] overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
              <tr className="text-left">
                <th className="px-3 py-2 w-8">
                  <input
                    type="checkbox"
                    aria-label="Select all on this page"
                    checked={pageAllChecked}
                    ref={(el) => {
                      if (el) el.indeterminate = pageIndeterminate;
                    }}
                    onChange={(e) => togglePageAll(e.target.checked)}
                    className="w-3.5 h-3.5 rounded border-gray-300 text-violet-600 focus:ring-violet-500 cursor-pointer"
                  />
                </th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">ID</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Name</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Email</th>
                <th className="px-3 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Status</th>
                {hasActions && (
                  <th className="px-3 py-2 font-semibold text-center text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-32">Actions</th>
                )}
              </tr>
            </thead>

            <tbody>
              {rows.length === 0 && !loading && (
                <tr>
                  <td className="px-3 py-12 text-center" colSpan={hasActions ? 6 : 5}>
                    <div className="flex flex-col items-center gap-2">
                      <UserCircleIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">No users found</p>
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

              {rows.map((u) => {
                const isSelected = selectedIds.has(u.id);

                return (
                  <tr
                    key={u.id}
                    className={`
                      transition-colors
                      ${isSelected
                        ? "bg-violet-50/60 dark:bg-violet-900/20"
                        : "odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50"
                      }
                      border-b border-gray-100 dark:border-slate-600/30
                    `}
                  >
                    <td className="px-3 py-3">
                      <input
                        type="checkbox"
                        checked={isSelected}
                        onChange={(e) => toggleOne(u.id, e.target.checked)}
                        aria-label={`Select user ${u.name}`}
                        className="w-3.5 h-3.5 rounded border-gray-300 text-violet-600 focus:ring-violet-500 cursor-pointer"
                      />
                    </td>
                    <td className="px-3 py-3 text-gray-600 dark:text-gray-300">
                      {u.id}
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <UserCircleIcon className="w-5 h-5 text-gray-400" />
                        <span className="font-medium text-gray-800 dark:text-gray-200">{u.name}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-2">
                        <EnvelopeIcon className="w-4 h-4 text-gray-400" />
                        <span className="text-gray-600 dark:text-gray-300">{u.email}</span>
                      </div>
                    </td>
                    <td className="px-3 py-3">
                      <span className="inline-flex items-center px-2.5 py-1 rounded-lg text-xs font-medium bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-200/60 dark:ring-emerald-700/40">
                        <ShieldCheckIcon className="w-3.5 h-3.5 mr-1" />
                        {u.status ?? "active"}
                      </span>
                    </td>
                    {hasActions && (
                      <td className="px-3 py-3">
                        <div className="flex items-center gap-1.5 justify-center">
                          {/* Edit Action */}
                          <Guard when={can.update}>
                            <Link
                              to={`/users/${u.id}/edit`}
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

                          {/* Delete Action */}
                          <Guard when={can.delete}>
                            <button
                              onClick={() => openDeleteModal(u)}
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
                        ? `bg-gradient-to-br ${SECTION_CONFIG.management.gradient} text-white`
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

      {/* Delete Confirmation Modal */}
      <DeleteConfirmationModal
        isOpen={deleteModalOpen}
        onClose={closeDeleteModal}
        onConfirm={handleConfirmDelete}
        itemName={deletingUser?.name || "this user"}
        title="Delete user"
        isDeleting={deleting}
        setIsDeleting={setDeleting}
        tintClasses={{ red: tintRed, glass: tintGlass }}
      />
    </div>
  );
}

