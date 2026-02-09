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

// Section configuration with color schemes - will use dynamic theme colors
const SECTION_CONFIG = {
  core: {
    key: 'primary',
  },
  management: {
    key: 'secondary',
  },
};

// Helper to get color value from theme
const getThemeColor = (theme, colorKey, variant = 'color') => {
  if (!theme) return '#3b82f6';
  const key = `${colorKey}_${variant}`;
  return theme[key] || '#3b82f6';
};

// Helper to determine text color based on background brightness
// Returns 'white' for dark backgrounds, 'black' (or dark gray) for light backgrounds
const getContrastText = (hexColor) => {
  // Remove hash if present
  hexColor = hexColor.replace('#', '');
  
  // Parse RGB values
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  
  // Calculate relative luminance (per WCAG formula)
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  
  // Return dark text for light backgrounds, light text for dark backgrounds
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

// Helper to get button text color with fallback
const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  // Use hover color for text color calculation as it's slightly darker
  return getContrastText(primaryHoverColor || primaryColor);
};

// Helper to generate section styles from theme
const getSectionStyles = (theme, colorKey) => {
  const baseColor = getThemeColor(theme, colorKey, 'color');
  const hoverColor = getThemeColor(theme, colorKey, 'hover');
  const lightColor = getThemeColor(theme, colorKey, 'light');
  
  return {
    gradient: `from-[${baseColor}] to-[${hoverColor}]`,
    bgLight: `bg-[${lightColor}]`,
    bgDark: `dark:bg-[${lightColor}]`,
    borderColor: `border-[${baseColor}]/30 dark:border-[${baseColor}]/30`,
    iconColor: `text-[${baseColor}] dark:text-[${baseColor}]`,
    ringColor: `ring-[${baseColor}]/30`,
  };
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

  // 🎨 Dynamic button palette using theme colors
  // Primary action buttons (Add Product, Import)
  const tintPrimary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Secondary action buttons (Edit)
  const tintSecondary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Danger action buttons (Delete)
  const tintDanger = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Glass style buttons (Export)
  const tintGlass = useMemo(() => `
    bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Disabled state
  const tintDisabled = useMemo(() => `
    bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed
  `.trim().replace(/\s+/g, ' '), []);

  // Get dark mode state and theme colors
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

  // Calculate text colors based on background brightness (after themeColors is defined)
  const primaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.primary, themeColors.primaryHover), 
    [themeColors.primary, themeColors.primaryHover]
  );
  
  const secondaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.secondary, themeColors.secondaryHover), 
    [themeColors.secondary, themeColors.secondaryHover]
  );
  
  const dangerTextColor = useMemo(() => 
    getButtonTextColor('#ef4444', '#dc2626'), 
    []
  );

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
            <div 
              className="p-2 rounded-lg bg-gradient-to-br shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})` }}
            >
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
                      ? `${tintPrimary} cursor-pointer` 
                      : tintDisabled
                    }
                  `}
                  style={selectedIds.size > 0 ? {
                    background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                    color: primaryTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                  } : {}}
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
                      ? `${tintDanger} cursor-pointer` 
                      : tintDisabled
                    }
                  `}
                  style={selectedIds.size > 0 ? {
                    background: `linear-gradient(to bottom right, #ef4444, #dc2626)`,
                    color: dangerTextColor,
                    boxShadow: '0 4px 14px 0 rgba(239, 68, 68, 0.4)'
                  } : {}}
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
                className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${tintPrimary}`}
                style={{ 
                  background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                  color: primaryTextColor,
                  boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                }}
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
              <span 
                className="text-xs px-2 py-0.5 rounded-full font-medium"
                style={{ 
                  backgroundColor: themeColors.primaryLight,
                  color: themeColors.primary 
                }}
              >
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
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium ${tintPrimary}`}
                  style={{ 
                    background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                    color: primaryTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                  }}
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
                className="h-7 px-2 rounded border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-xs font-medium text-gray-700 dark:text-gray-200 focus:ring-2 focus:border-transparent cursor-pointer"
                style={{ 
                  '--tw-ring-color': themeColors.primary,
                  outlineColor: themeColors.primary
                }}
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
            <div 
              className="p-1 rounded"
              style={{ backgroundColor: themeColors.secondaryLight + '40' }}
            >
              <Squares2X2Icon 
                className="w-4 h-4" 
                style={{ color: themeColors.secondary }} 
              />
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
                    className="w-3.5 h-3.5 rounded border-gray-300 cursor-pointer"
                    style={{ 
                      accentColor: themeColors.primary 
                    }}
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
                        ? "" 
                        : "odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40 hover:bg-blue-50 dark:hover:bg-slate-600/50"
                      }
                      border-b border-gray-100 dark:border-slate-600/30
                    `}
                    style={isSelected ? {
                      backgroundColor: themeColors.primaryLight + '60'
                    } : {}}
                  >
                    <td className="px-2 py-2">
                    <input
                        type="checkbox"
                        checked={isSelected}
                        onChange={(e) => toggleOne(p.id, e.target.checked)}
                        aria-label={`Select ${p.name}`}
                        className="w-3.5 h-3.5 rounded border-gray-300 cursor-pointer"
                        style={{ 
                          accentColor: themeColors.primary 
                        }}
                      />
                    </td>

                    {/* Name cell clickable for selection */}
                    <td
                      className={`
                        px-2 py-2 cursor-pointer select-none
                        ${isSelected ? "" : "text-gray-800 dark:text-gray-200"}
                      `}
                      style={isSelected ? {
                        color: themeColors.primary
                      } : {}}
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
                        <span 
                          className={`
                            inline-flex items-center justify-center min-w-[2.5rem] px-2 py-0.5 rounded-full text-xs font-bold
                          `}
                          style={{
                            background: qty > 0 
                              ? `linear-gradient(to bottom right, #10b981, #059669)`
                              : `linear-gradient(to bottom right, #f97316, #ea580c)`,
                            boxShadow: qty > 0 
                              ? '0 4px 12px 0 rgba(16, 185, 129, 0.4)'
                              : '0 4px 12px 0 rgba(249, 115, 22, 0.4)'
                          }}
                        >
                          {qty}
                        </span>
                      </div>
                    </td>

                    <td className="px-2 py-2">
                      <span 
                        className="px-2 py-0.5 rounded text-xs"
                        style={{ 
                          backgroundColor: '#dcfce7',
                          color: '#16a34a'
                        }}
                      >
                        {p.category?.name || "—"}
                      </span>
                    </td>
                    <td className="px-2 py-2">
                      <span 
                        className="px-2 py-0.5 rounded text-xs"
                        style={{ 
                          backgroundColor: '#fef3c7',
                          color: '#d97706'
                        }}
                      >
                        {p.brand?.name || "—"}
                      </span>
                    </td>
                    <td className="px-2 py-2">
                      <span 
                        className="px-2 py-0.5 rounded text-xs"
                        style={{ 
                          backgroundColor: '#ffe4e6',
                          color: '#e11d48'
                        }}
                      >
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
                                transition-all duration-200
                              `}
                              style={{
                                background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                                color: primaryTextColor,
                                boxShadow: `0 4px 12px 0 ${themeColors.primary}40`
                              }}
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
                                ${deleteDisabled ? tintDisabled : tintDanger}
                              `}
                              style={!deleteDisabled ? {
                                background: `linear-gradient(to bottom right, #ef4444, #dc2626)`,
                                color: dangerTextColor,
                                boxShadow: '0 4px 12px 0 rgba(239, 68, 68, 0.4)'
                              } : {}}
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
                        ? ''
                        : "text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-slate-700"
                      }
                    `}
                    style={page === pageNum ? {
                      background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                      color: primaryTextColor
                    } : {}}
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
          tintClasses={{ 
            blue: tintPrimary, 
            blueStyle: { 
              background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
              boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
            },
            glass: tintGlass 
          }}
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
        tintClasses={{ 
          red: tintDanger,
          redStyle: {
            background: `linear-gradient(to bottom right, #ef4444, #dc2626)`,
            boxShadow: '0 4px 14px 0 rgba(239, 68, 68, 0.4)'
          },
          glass: tintGlass 
        }}
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
          tintClasses={{ 
            red: tintDanger,
            redStyle: {
              background: `linear-gradient(to bottom right, #ef4444, #dc2626)`,
              boxShadow: '0 4px 14px 0 rgba(239, 68, 68, 0.4)'
            },
            glass: tintGlass 
          }}
        />
      )}
    </div>
  );
}

