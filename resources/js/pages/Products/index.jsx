// src/pages/products/index.jsx
import React, { useEffect, useMemo, useRef, useState, useCallback } from "react";
import axios from "axios";
import { Link, useNavigate } from "react-router-dom";
import toast from "react-hot-toast";
import {
  TrashIcon,
  PencilSquareIcon,
  PlusCircleIcon,
  ArrowUpTrayIcon,
  ArrowDownTrayIcon,
  TagIcon,
  BuildingStorefrontIcon,
  Squares2X2Icon,
  ArrowPathIcon,
  CubeIcon,
} from "@heroicons/react/24/solid";
import { usePermissions, Guard } from "@/api/usePermissions.js";

// Reusable components
import {
  GlassBtn,
  ProductImportModal,
  DeleteConfirmationModal,
  BulkEditModal,
  BulkDeleteModal,
  TextSearch,
} from "@/components";
import { useTheme } from "@/context/ThemeContext";

/** ---- helpers ---- */
const normalizeList = (payload) => {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.data)) return payload.data;
  if (Array.isArray(payload?.items)) return payload.items;
  return [];
};

const debouncePromise = (fn, wait = 300) => {
  let timeout;
  let pendingReject;
  return (...args) =>
    new Promise((resolve, reject) => {
      if (timeout) clearTimeout(timeout);
      if (pendingReject) pendingReject("debounced");
      pendingReject = reject;
      timeout = setTimeout(async () => {
        try {
          const res = await fn(...args);
          resolve(res);
        } catch (e) {
          reject(e);
        } finally {
          pendingReject = null;
        }
      }, wait);
    });
};

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

export default function ProductsIndex() {
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);

  // Import / Export
  const [importOpen, setImportOpen] = useState(false);
  const [exporting, setExporting] = useState(false);

  // search filters
  const [qName, setQName] = useState("");
  const [qBrand, setQBrand] = useState("");
  const [qSupplier, setQSupplier] = useState("");

  // pagination (server-side)
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);
  const [lastPage, setLastPage] = useState(1);

  // selection
  const [selectedIds, setSelectedIds] = useState(new Set());
  const [showBulkModal, setShowBulkModal] = useState(false);
  const [showBulkDelete, setShowBulkDelete] = useState(false);

  // Delete modal state
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deletingProduct, setDeletingProduct] = useState(null);
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
        ? canFor("product")
        : { view: false, create: false, update: false, delete: false, import: false, export: false }),
    [canFor]
  );

  // 🎨 Modern button palette (matching sidebar design language)
  // Uses smooth gradients, glass effects, and subtle shadows with section-based coloring
  const tintBlue   = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] hover:from-blue-600 hover:to-blue-700 active:scale-[0.98] transition-all duration-200";
  const tintIndigo = "bg-gradient-to-br from-indigo-500 to-indigo-600 text-white shadow-lg shadow-indigo-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-indigo-500/30 hover:scale-[1.02] hover:from-indigo-600 hover:to-indigo-700 active:scale-[0.98] transition-all duration-200";
  const tintSlate  = "bg-gradient-to-br from-slate-700 to-slate-800 text-white shadow-lg shadow-slate-500/25 ring-1 ring-white/10 hover:shadow-xl hover:shadow-slate-500/30 hover:scale-[1.02] hover:from-slate-800 hover:to-slate-900 active:scale-[0.98] transition-all duration-200";
  const tintAmber  = "bg-gradient-to-br from-amber-500 to-amber-600 text-white shadow-lg shadow-amber-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-amber-500/30 hover:scale-[1.02] hover:from-amber-600 hover:to-amber-700 active:scale-[0.98] transition-all duration-200";
  const tintRed    = "bg-gradient-to-br from-rose-500 to-rose-600 text-white shadow-lg shadow-rose-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700 active:scale-[0.98] transition-all duration-200";
  const tintViolet = "bg-gradient-to-br from-violet-500 to-violet-600 text-white shadow-lg shadow-violet-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-violet-500/30 hover:scale-[1.02] hover:from-violet-600 hover:to-violet-700 active:scale-[0.98] transition-all duration-200";
  const tintGlass  = "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";
  
  // Secondary action buttons with outline style
  const tintOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 hover:shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";
  
  // Icon-only button for compact actions
  const tintIconBtn = "bg-white/60 dark:bg-slate-700/60 backdrop-blur-sm text-slate-600 dark:text-gray-300 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-md hover:scale-[1.05] active:scale-[0.95] transition-all duration-200";

  // Get dark mode state
  const { isDark } = useTheme();

  // === Alt+N => /products/create (only when can.create) ===
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
      navigate("/products/create");
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate, can.create]);

  const handleExport = async () => {
    if (!can.export) return toast.error("You don't have permission to export products.");
    try {
      setExporting(true);
      const res = await axios.get("/api/products/export", { responseType: "blob" });
      const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-");
      const filename = `products_${stamp}.csv`;
      const blob = new Blob([res.data], { type: "text/csv;charset=utf-8" });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = filename; document.body.appendChild(a); a.click(); a.remove();
      window.URL.revokeObjectURL(url);
    } catch (e) {
      const status = e?.response?.status;
      if (status === 403) toast.error("You don't have permission to export products.");
      else toast.error("Export failed");
    } finally {
      setExporting(false);
    }
  };

  // keep fetchProducts but pass filters as args
  const fetchProducts = useCallback(async (signal, opts = {}) => {
    const { pageArg = page, pageSizeArg = pageSize, qNameArg = qName, qBrandArg = qBrand, qSupplierArg = qSupplier } = opts;
    try {
      setLoading(true);
      const { data } = await axios.get("/api/products", {
        params: {
          page: pageArg,
          per_page: pageSizeArg,
          q_name: qNameArg.trim(),
          q_brand: qBrandArg.trim(),
          q_supplier: qSupplierArg.trim(),
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
    if (status === 403) toast.error("You don't have permission to view products.");
    else toast.error("Failed to load products");
  } finally {
    setLoading(false);
  }
}, [page, pageSize, qName, qBrand, qSupplier]);


// Fetch when page or pageSize changes
useEffect(() => {
  if (permsLoading || !can.view) return;
  const ctrl = new AbortController();
  controllerRef.current = ctrl;
  fetchProducts(ctrl.signal);
  return () => ctrl.abort();
}, [page, pageSize, permsLoading, can.view]);

// Debounce only when filters change (reset to page 1)
useEffect(() => {
  if (permsLoading || !can.view) return;
  const ctrl = new AbortController();
  if (debounceRef.current) clearTimeout(debounceRef.current);
  debounceRef.current = setTimeout(() => {
    setPage(1);
    controllerRef.current = ctrl;
    fetchProducts(ctrl.signal, { pageArg: 1 });
  }, 300);
  return () => {
    clearTimeout(debounceRef.current);
    ctrl.abort();
  };
}, [qName, qBrand, qSupplier, permsLoading, can.view]);


  const start = rows.length ? (page - 1) * pageSize + 1 : 0;
  const end = rows.length ? start + rows.length - 1 : 0;

  // ===== delete modal handlers =====
  const openDeleteModal = (product) => {
    if (!can.delete) return toast.error("You don't have permission to delete products.");
    const qty = Number(product.quantity || 0);
    const hasBatches = Number(product.batches_count || 0) > 0;

    if (qty > 0 || hasBatches) {
      toast.error(qty > 0 ? "Cannot delete: product has on-hand quantity." : "Cannot delete: product has batch records.");
      return;
    }

    setDeletingProduct({ id: product.id, name: product.name });
    setDeleteModalOpen(true);
  };

  const closeDeleteModal = () => {
    setDeleteModalOpen(false);
    setDeletingProduct(null);
  };

  const handleConfirmDelete = async (password) => {
    if (!deletingProduct?.id) return;
    if (!can.delete) return toast.error("You don't have permission to delete products.");
    
    try {
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/products/${deletingProduct.id}`);
      toast.success("Product deleted");

      setSelectedIds((prev) => {
        const copy = new Set(prev);
        copy.delete(deletingProduct.id);
        return copy;
      });

      closeDeleteModal();

      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      fetchProducts(ctrl.signal);
    } catch (err) {
      const status = err?.response?.status;
      const apiMsg =
        err?.response?.data?.message ||
        (status === 422 ? "Incorrect password" : status === 403 ? "You don't have permission to delete products." : "Delete failed");
      toast.error(apiMsg);
    }
  };

  // selection helpers (operate on current page rows)
  const pageAllChecked = rows.length > 0 && rows.every((p) => selectedIds.has(p.id));
  const pageIndeterminate = rows.some((p) => selectedIds.has(p.id)) && !pageAllChecked;

  const togglePageAll = (checked) => {
    setSelectedIds((prev) => {
      const copy = new Set(prev);
      if (checked) rows.forEach((p) => copy.add(p.id));
      else rows.forEach((p) => copy.delete(p.id));
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

  // helper to toggle by clicking product name
  const toggleById = (id) =>
    setSelectedIds((prev) => {
      const copy = new Set(prev);
      copy.has(id) ? copy.delete(id) : copy.add(id);
      return copy;
    });

  const openBulkModal = () => {
    if (!can.update) return toast.error("You don't have permission to update products.");
    setShowBulkModal(true);
  };

  // permissions-driven table layout
  const hasActions = can.update || can.delete;
  const visibleColumns = 1 /*select*/ + 6 /*code,name,image,category,brand,supplier*/ + (hasActions ? 1 : 0);

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view products.</div>;

  return (
    <div className="p-4 space-y-3">
      {/* ===== Professional Header ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.management.gradient} shadow-sm`}>
              <CubeIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Products</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{total} items</p>
            </div>
          </div>

          {/* Action Buttons - Modern card-style layout */}
          <div className="flex items-center gap-2">
            {/* Bulk Actions Dropdown-style buttons */}
            <div className="flex items-center gap-1.5 px-1.5 py-0.5 rounded-lg bg-gray-100/80 dark:bg-slate-700/60 border border-gray-200/60 dark:border-slate-600/40">
              <Guard when={can.update}>
                <button
                  onClick={openBulkModal}
                  disabled={selectedIds.size === 0}
                  className={`
                    inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium
                    transition-all duration-200
                    ${selectedIds.size > 0 
                      ? `${tintAmber} cursor-pointer` 
                      : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"
                    }
                  `}
                >
                  <PencilSquareIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Edit</span>
                  {selectedIds.size > 0 && (
                    <span className="ml-0.5 px-1 py-0.5 rounded bg-white/20 text-[10px]">
                      {selectedIds.size}
                    </span>
                  )}
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.delete}>
                <button
                  onClick={() => setShowBulkDelete(true)}
                  disabled={selectedIds.size === 0}
                  className={`
                    inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium
                    transition-all duration-200
                    ${selectedIds.size > 0 
                      ? `${tintRed} cursor-pointer` 
                      : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"
                    }
                  `}
                >
                  <TrashIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Delete</span>
                  {selectedIds.size > 0 && (
                    <span className="ml-0.5 px-1 py-0.5 rounded bg-white/20 text-[10px]">
                      {selectedIds.size}
                    </span>
                  )}
                </button>
              </Guard>
            </div>

            {/* Primary Add Button */}
            <Guard when={can.create}>
              <Link
                to="/products/create"
                className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${tintBlue}`}
              >
                <PlusCircleIcon className="w-4 h-4" />
                <span className="hidden sm:inline">Add Product</span>
                <span className="sm:hidden">Add</span>
              </Link>
            </Guard>
          </div>
        </div>

        {/* Search Bar */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <TextSearch 
              value={qName} 
              onChange={setQName} 
              placeholder="Search products..." 
            />
            <TextSearch 
              value={qBrand} 
              onChange={setQBrand} 
              placeholder="Filter by brand..." 
              icon={<TagIcon className="w-4 h-4 text-gray-400" />} 
            />
            <TextSearch 
              value={qSupplier} 
              onChange={setQSupplier} 
              placeholder="Filter by supplier..." 
              icon={<BuildingStorefrontIcon className="w-4 h-4 text-gray-400" />} 
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

          {/* Right Actions - Modern card-style layout */}
          <div className="flex items-center gap-2">
            {/* Quick Actions Group */}
            <div className="flex items-center gap-1.5 px-1.5 py-0.5 rounded-lg bg-gray-100/80 dark:bg-slate-700/60 border border-gray-200/60 dark:border-slate-600/40">
              <Guard when={can.import}>
                <button
                  onClick={() => setImportOpen(true)}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium ${tintIndigo}`}
                >
                  <ArrowUpTrayIcon className="w-3.5 h-3.5" />
                  Import
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.export}>
                <button
                  onClick={handleExport}
                  disabled={exporting}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium ${tintGlass}`}
                >
                  <ArrowPathIcon className={`w-3.5 h-3.5 ${exporting ? "animate-spin" : ""}`} />
                  {exporting ? "..." : "Export"}
                </button>
              </Guard>
            </div>

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

      {/* ===== Product Table ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        {/* Table Header */}
        <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div className={`p-1 rounded ${SECTION_CONFIG.management.bgDark}`}>
              <Squares2X2Icon className="w-4 h-4 text-violet-600 dark:text-violet-400" />
            </div>
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Product List</span>
          </div>
          <span className="text-xs text-gray-400">{rows.length} items</span>
        </div>

        <div className="max-h-[65vh] overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-white dark:bg-slate-800 z-10 shadow-sm">
              <tr className="text-left">
                <th className="px-2 py-2 w-8">
                  <input
                    type="checkbox"
                    aria-label="Select all on this page"
                    checked={pageAllChecked}
                    ref={(el) => {
                      if (el) el.indeterminate = pageIndeterminate;
                    }}
                    onChange={(e) => togglePageAll(e.target.checked)}
                    className="w-3.5 h-3.5 rounded border-gray-300 text-blue-600 focus:ring-blue-500 cursor-pointer"
                  />
                </th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Name</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Category</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Brand</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider">Supplier</th>
                {hasActions && (
                  <th className="px-2 py-2 font-semibold text-center text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-32">Actions</th>
                )}
              </tr>
            </thead>

            <tbody>
              {rows.length === 0 && !loading && (
                <tr>
                  <td className="px-2 py-12 text-center" colSpan={visibleColumns}>
                    <div className="flex flex-col items-center gap-2">
                      <CubeIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm text-gray-500 dark:text-gray-400">No products found</p>
                    </div>
                  </td>
                </tr>
              )}

              {rows.map((p) => {
                const qty = Number(p.quantity || 0);
                const hasBatches = Number(p.batches_count || 0) > 0;
                const deleteDisabled = qty > 0 || hasBatches;
                const deleteTitle = deleteDisabled
                  ? qty > 0
                    ? "Cannot delete: has quantity."
                    : "Cannot delete: has batches."
                  : "Delete";
                const isSelected = selectedIds.has(p.id);
                
                return (
                  <tr
                    key={p.id}
                    className={`
                      transition-colors
                      ${isSelected 
                        ? "bg-violet-50/60 dark:bg-violet-900/20" 
                        : "odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50"
                      }
                      border-b border-gray-100 dark:border-slate-600/30
                    `}
                  >
                    <td className="px-2 py-2">
                      <input
                        type="checkbox"
                        checked={isSelected}
                        onChange={(e) => toggleOne(p.id, e.target.checked)}
                        aria-label={`Select ${p.name}`}
                        className="w-3.5 h-3.5 rounded border-gray-300 text-violet-600 focus:ring-violet-500 cursor-pointer"
                      />
                    </td>

                    {/* Name cell clickable for selection */}
                    <td
                      className={`
                        px-2 py-2 cursor-pointer select-none
                        ${isSelected 
                          ? "text-violet-700 dark:text-violet-300" 
                          : "text-gray-800 dark:text-gray-200"
                        }
                      `}
                      role="button"
                      tabIndex={0}
                      onClick={() => toggleById(p.id)}
                      onKeyDown={(e) => {
                        if (e.key === " " || e.key === "Enter") {
                          e.preventDefault();
                          toggleById(p.id);
                        }
                      }}
                    >
                      <div className="flex items-center gap-2">
                        <span>{p.name}</span>
                        <span className={`
                          inline-flex items-center justify-center min-w-[2.5rem] px-2 py-0.5 rounded-full text-xs font-bold
                          ${qty > 0 
                            ? "bg-gradient-to-br from-emerald-400 to-emerald-500 text-white shadow-lg shadow-emerald-500/25 ring-1 ring-emerald-400/30" 
                            : "bg-gradient-to-br from-orange-400 to-amber-500 text-white shadow-lg shadow-orange-500/25 ring-1 ring-orange-400/30"}
                        `}>
                          {qty}
                        </span>
                      </div>
                    </td>

                    <td className="px-2 py-2">
                      <span className="px-2 py-0.5 rounded bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300 text-xs">
                        {p.category?.name || "—"}
                      </span>
                    </td>
                    <td className="px-2 py-2">
                      <span className="px-2 py-0.5 rounded bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 text-xs">
                        {p.brand?.name || "—"}
                      </span>
                    </td>
                    <td className="px-2 py-2">
                      <span className="px-2 py-0.5 rounded bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-300 text-xs">
                        {p.supplier?.name || "—"}
                      </span>
                    </td>

                    {hasActions && (
                      <td className="px-2 py-2">
                        <div className="flex items-center gap-1.5 justify-center">
                          {/* Edit Action */}
                          <Guard when={can.update}>
                            <Link
                              to={`/products/${p.id}/edit`}
                              className={`
                                group inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold
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
                              onClick={() => openDeleteModal(p)}
                              disabled={deleteDisabled}
                              title={deleteTitle}
                              className={`
                                group inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold
                                transition-all duration-200
                                ${deleteDisabled 
                                  ? "bg-gray-100 dark:bg-slate-700/50 text-gray-400 dark:text-gray-500 cursor-not-allowed ring-1 ring-gray-200/60 dark:ring-slate-600/40"
                                  : `
                                    bg-gradient-to-br from-rose-500 to-rose-600 text-white
                                    shadow-lg shadow-rose-500/20 ring-1 ring-rose-400/30
                                    hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700
                                    active:scale-[0.98]
                                  `
                                }
                              `}
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

      {/* Bulk Edit Modal */}
      {showBulkModal && (
        <BulkEditModal
          isOpen={showBulkModal}
          onClose={() => setShowBulkModal(false)}
          selectedCount={selectedIds.size}
          selectedIds={[...selectedIds]}
          onSaved={async () => {
            if (controllerRef.current) controllerRef.current.abort();
            const ctrl = new AbortController();
            controllerRef.current = ctrl;
            await fetchProducts(ctrl.signal);
            setShowBulkModal(false);
            setSelectedIds(new Set());
          }}
          tintClasses={{ blue: tintBlue, glass: tintGlass }}
        />
      )}

      {/* Import modal */}
      <ProductImportModal open={importOpen} onClose={() => setImportOpen(false)} onImported={fetchProducts} />

      {/* Single Delete confirmation modal */}
      <DeleteConfirmationModal
        isOpen={deleteModalOpen}
        onClose={closeDeleteModal}
        onConfirm={handleConfirmDelete}
        itemName={deletingProduct?.name || "this product"}
        title="Delete product"
        isDeleting={deleting}
        setIsDeleting={setDeleting}
        tintClasses={{ red: tintRed, glass: tintGlass }}
      />

      {/* Bulk Delete Modal */}
      {showBulkDelete && (
        <BulkDeleteModal
          isOpen={showBulkDelete}
          onClose={() => setShowBulkDelete(false)}
          selectedCount={selectedIds.size}
          selectedIds={[...selectedIds]}
          onDeleted={async (result) => {
            if (result?.deleted) toast.success(`Deleted ${result.deleted} product(s).`);
            if (result?.failed?.length) {
              const firstFew = result.failed.slice(0, 3).map(f => `${f.name ?? `#${f.id}`}: ${f.reason}`);
              toast.error(
                `Couldn't delete ${result.failed.length} item(s).\n` + firstFew.join("\n")
              );
            }
            if (controllerRef.current) controllerRef.current.abort();
            const ctrl = new AbortController();
            controllerRef.current = ctrl;
            await fetchProducts(ctrl.signal);
            setSelectedIds(new Set());
            setShowBulkDelete(false);
          }}
          itemType="product(s)"
          tintClasses={{ red: tintRed, glass: tintGlass }}
        />
      )}
    </div>
  );
}

