// src/pages/products/index.jsx
import React, { useEffect, useMemo, useRef, useState, useCallback } from "react";
import axios from "axios";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import toast from "react-hot-toast";
import {
  TrashIcon,
  PencilSquareIcon,
  PlusCircleIcon,
  ArrowUpTrayIcon,
  TagIcon,
  BuildingStorefrontIcon,
  Squares2X2Icon,
  ArrowPathIcon,
  CubeIcon,
  ExclamationTriangleIcon,
  XMarkIcon,
} from "@heroicons/react/24/solid";
import { usePermissions, Guard } from "@/api/usePermissions.js";

// Reusable components
import {
  ProductImportModal,
  DeleteConfirmationModal,
  BulkEditModal,
  BulkDeleteModal,
  TextSearch,
} from "@/components";
import { useTheme } from "@/context/ThemeContext";

/** ---- helpers ---- */
// Helper to determine text color based on background brightness
// Returns dark text for light backgrounds, light text for dark backgrounds
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

// Helper to get button text color with fallback
const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  return getContrastText(primaryHoverColor || primaryColor);
};

// Monogram initials for the product avatar (e.g. "Basmati Rice 5kg" -> "BR")
const getInitials = (name = "") =>
  name
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0])
    .join("")
    .toUpperCase() || "?";

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

  // Low-stock filter (enabled via /products?low_stock=1)
  const [searchParams] = useSearchParams();
  const [lowStockOnly, setLowStockOnly] = useState(() => searchParams.get("low_stock") === "1");

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

  // Get button style from theme
  const buttonStyle = theme?.button_style || 'rounded-sm';
  
  // Calculate text colors based on background brightness
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

  // Get button style classes and styles based on theme button_style
  const getButtonClasses = useMemo(() => {
    const radiusMap = {
      'rounded-sm': 'rounded-lg',
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
        tertiary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.tertiary,
            color: themeColors.tertiary,
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
      tertiary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})`,
          color: 'white',
          boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40`,
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

  // Destructure button classes for easier use
  const btnPrimary = getButtonClasses.primary;
  const btnDanger = getButtonClasses.danger;
  const btnGlass = getButtonClasses.glass;

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
          low_stock: lowStockOnly ? 1 : '',
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
}, [page, pageSize, qName, qBrand, qSupplier, lowStockOnly]);


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
}, [qName, qBrand, qSupplier, lowStockOnly, permsLoading, can.view]);


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
  const visibleColumns =
    1 /*select*/ + 1 /*name*/ + 1 /*category*/ + 1 /*brand*/ + 1 /*supplier*/ + 1 /*quantity*/ + (hasActions ? 1 : 0);

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view products.</div>;

  // Translucent, tinted input styling so the search fields sit on the dark hero
  const heroInputStyle = {
    backgroundColor: "rgba(255, 255, 255, 0.06)",
    borderColor: "rgba(255, 255, 255, 0.10)",
    color: "#ffffff",
    boxShadow: "none",
  };
  const heroInputClass = "focus:ring-white/20";

  return (
    <div className="p-4 space-y-4">
      {/* ===== Hero band (matches Dashboard) ===== */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-slate-900 via-slate-900 to-slate-800 ring-1 ring-white/10 shadow-lg shadow-slate-900/20">
        {/* Glow accents */}
        <div aria-hidden="true" className="pointer-events-none absolute -top-24 -right-16 h-72 w-72 rounded-full blur-3xl opacity-25" style={{ background: themeColors.primary }} />
        <div aria-hidden="true" className="pointer-events-none absolute -bottom-28 -left-12 h-72 w-72 rounded-full blur-3xl opacity-20" style={{ background: themeColors.secondary }} />
        {/* Dot texture */}
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 opacity-[0.12]"
          style={{ backgroundImage: "radial-gradient(rgba(255,255,255,0.8) 1px, transparent 1px)", backgroundSize: "18px 18px" }}
        />

        {/* Top row */}
        <div className="relative flex flex-col gap-4 p-4 sm:p-5 lg:flex-row lg:items-center lg:justify-between lg:gap-6">
          {/* Identity */}
          <div className="flex items-center gap-3 min-w-0">
            <div className="w-10 h-10 rounded-xl bg-white/10 ring-1 ring-white/15 flex items-center justify-center shrink-0">
              <CubeIcon className="text-white w-5 h-5" />
            </div>
            <div className="min-w-0">
              <h1 className="text-base sm:text-lg font-semibold tracking-tight text-white">Products</h1>
              <p className="mt-0.5 text-[11px] sm:text-xs text-slate-400 flex items-center gap-1.5">
                <Squares2X2Icon className="w-3.5 h-3.5 shrink-0" />
                <span className="truncate">{total} products in your inventory</span>
              </p>
            </div>
          </div>

          {/* Actions */}
          <div className="flex flex-wrap items-center gap-2">
            {/* Bulk actions */}
            <Guard when={can.update}>
              <button
                onClick={openBulkModal}
                disabled={selectedIds.size === 0}
                title="Bulk edit"
                className={`
                  inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold
                  transition-all duration-200
                  ${selectedIds.size > 0
                    ? "bg-white/10 text-white ring-1 ring-white/15 hover:bg-white/15"
                    : "bg-white/5 text-slate-500 ring-1 ring-white/10 cursor-not-allowed"}
                `}
              >
                <PencilSquareIcon className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">Edit</span>
                {selectedIds.size > 0 && (
                  <span className="ml-0.5 rounded-full bg-white/15 px-1 py-0.5 text-[10px] font-medium">
                    {selectedIds.size}
                  </span>
                )}
              </button>
            </Guard>

            <Guard when={can.delete}>
              <button
                onClick={() => setShowBulkDelete(true)}
                disabled={selectedIds.size === 0}
                title="Bulk delete"
                className={`
                  inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold
                  transition-all duration-200
                  ${selectedIds.size > 0
                    ? "bg-white/10 text-white ring-1 ring-white/15 hover:bg-white/15"
                    : "bg-white/5 text-slate-500 ring-1 ring-white/10 cursor-not-allowed"}
                `}
              >
                <TrashIcon className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">Delete</span>
                {selectedIds.size > 0 && (
                  <span className="ml-0.5 rounded-full bg-white/15 px-1 py-0.5 text-[10px] font-medium">
                    {selectedIds.size}
                  </span>
                )}
              </button>
            </Guard>

            <span aria-hidden="true" className="mx-1 hidden h-5 w-px bg-white/10 sm:block" />

            {/* Import / Export */}
            <Guard when={can.import}>
              <button
                onClick={() => setImportOpen(true)}
                title="Import products"
                className="inline-flex items-center gap-1.5 rounded-lg bg-white/5 px-2.5 py-1.5 text-xs font-semibold text-white/90 ring-1 ring-white/10 transition-all duration-200 hover:bg-white/10"
              >
                <ArrowUpTrayIcon className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">Import</span>
              </button>
            </Guard>

            <Guard when={can.export}>
              <button
                onClick={handleExport}
                disabled={exporting}
                title="Export products"
                className="inline-flex items-center gap-1.5 rounded-lg bg-white/5 px-2.5 py-1.5 text-xs font-semibold text-white/90 ring-1 ring-white/10 transition-all duration-200 hover:bg-white/10 disabled:opacity-50"
              >
                <ArrowPathIcon className={`w-3.5 h-3.5 ${exporting ? "animate-spin" : ""}`} />
                <span className="hidden sm:inline">{exporting ? "Exporting…" : "Export"}</span>
              </button>
            </Guard>

            {/* Primary action */}
            <Guard when={can.create}>
              <Link
                to="/products/create"
                title="Add product"
                className="inline-flex items-center gap-1.5 rounded-lg bg-white/10 px-3 py-1.5 text-sm font-semibold text-white ring-1 ring-white/15 backdrop-blur-sm transition-all duration-200 hover:bg-white/15 active:scale-[0.98]"
              >
                <PlusCircleIcon className="w-4 h-4" />
                <span className="hidden sm:inline">Add Product</span>
                <span className="sm:hidden">Add</span>
              </Link>
            </Guard>
          </div>
        </div>

        {/* Search controls - dark translucent panels */}
        <div className="relative px-4 pb-4 sm:px-5">
          <div className="grid grid-cols-1 gap-2 rounded-lg bg-black/25 p-2.5 backdrop-blur-sm md:grid-cols-4">
            <div className="flex items-center gap-2 rounded-lg bg-white/[0.06] px-2.5 py-1.5 transition-colors duration-200 hover:bg-white/10">
              <Squares2X2Icon className="w-4 h-4 text-slate-400 shrink-0" />
              <TextSearch
                value={qName}
                onChange={setQName}
                placeholder="Search products..."
                className="w-full"
                iconClassName="text-slate-400"
                inputClassName={heroInputClass}
                inputStyle={heroInputStyle}
              />
            </div>
            <div className="flex items-center gap-2 rounded-lg bg-white/[0.06] px-2.5 py-1.5 transition-colors duration-200 hover:bg-white/10">
              <TagIcon className="w-4 h-4 text-slate-400 shrink-0" />
              <TextSearch
                value={qBrand}
                onChange={setQBrand}
                placeholder="Filter by brand..."
                className="w-full"
                iconClassName="text-slate-400"
                inputClassName={heroInputClass}
                inputStyle={heroInputStyle}
              />
            </div>
            <div className="flex items-center gap-2 rounded-lg bg-white/[0.06] px-2.5 py-1.5 transition-colors duration-200 hover:bg-white/10">
              <BuildingStorefrontIcon className="w-4 h-4 text-slate-400 shrink-0" />
              <TextSearch
                value={qSupplier}
                onChange={setQSupplier}
                placeholder="Filter by supplier..."
                className="w-full"
                iconClassName="text-slate-400"
                inputClassName={heroInputClass}
                inputStyle={heroInputStyle}
              />
            </div>
            <button
              type="button"
              onClick={() => {
                const next = !lowStockOnly;
                setLowStockOnly(next);
                const sp = new URLSearchParams(searchParams);
                if (next) sp.set("low_stock", "1");
                else sp.delete("low_stock");
                const qs = sp.toString();
                navigate(`/products${qs ? `?${qs}` : ""}`, { replace: true });
              }}
              title={
                lowStockOnly
                  ? "Low-stock filter is ON — click to clear and show all products"
                  : "Show only products whose quantity is below pack size"
              }
              className={`inline-flex items-center justify-center gap-2 rounded-lg px-3 py-2 text-xs font-medium transition-all duration-200 ${
                lowStockOnly
                  ? "bg-rose-500/25 text-rose-200 ring-1 ring-rose-400/30"
                  : "bg-white/[0.06] text-slate-300 hover:bg-white/10"
              }`}
            >
              <ExclamationTriangleIcon className="w-4 h-4" />
              <span>Low stock only</span>
              {lowStockOnly && (
                <XMarkIcon className="w-3.5 h-3.5 opacity-80" />
              )}
            </button>
          </div>
          {lowStockOnly && (
            <div className="mt-2 flex items-center gap-2 text-xs text-slate-400">
              <span className="inline-flex items-center gap-1.5 rounded-md bg-rose-500/15 px-2.5 py-1 text-rose-200">
                <ExclamationTriangleIcon className="w-3.5 h-3.5" />
                Showing only products below pack size (needs reorder)
              </span>
            </div>
          )}
        </div>
      </div>

      {/* ===== Product Table ===== */}
      <div className="relative overflow-hidden rounded-2xl border border-gray-200 bg-white shadow-sm dark:border-slate-700 dark:bg-slate-800">
        {/* Accent hairline */}
        <span
          aria-hidden="true"
          className="absolute inset-x-0 top-0 h-[3px]"
          style={{ background: `linear-gradient(90deg, ${themeColors.primary}, ${themeColors.secondary}, transparent)` }}
        />
        {/* Table header bar */}
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-gray-100 px-5 py-4 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div
              className="flex h-9 w-9 items-center justify-center rounded-lg bg-slate-100 dark:bg-slate-700/60"
            >
              <Squares2X2Icon className="h-4 w-4" style={{ color: themeColors.primary }} />
            </div>
            <div>
              <h2 className="text-sm font-semibold text-gray-800 dark:text-gray-100">Product List</h2>
              <p className="text-xs text-gray-400 dark:text-gray-500">
                {loading ? (
                  <span className="inline-flex items-center gap-1.5">
                    <ArrowPathIcon className="h-3 w-3 animate-spin" />
                    Loading…
                  </span>
                ) : (
                  `Showing ${rows.length === 0 ? 0 : start}–${end} of ${total}`
                )}
                {selectedIds.size > 0 && ` · ${selectedIds.size} selected`}
              </p>
            </div>
          </div>

          {/* Page size */}
          <div className="flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 px-3 py-1.5 dark:border-slate-600 dark:bg-slate-700/40">
            <label className="text-xs font-medium text-gray-500 dark:text-gray-400">Show</label>
            <select
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
              className="cursor-pointer rounded-md border-gray-200 bg-white px-2 py-1 text-xs font-medium text-gray-700 focus:ring-2 focus:border-transparent dark:border-slate-600 dark:bg-slate-700 dark:text-gray-200"
              style={{
                '--tw-ring-color': themeColors.primary,
                accentColor: themeColors.primary,
              }}
            >
              <option value={10}>10</option>
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
          </div>
        </div>

        <div className="max-h-[65vh] overflow-y-auto">
          <table className="w-full table-fixed text-sm">
            <thead className="sticky top-0 z-10 bg-gradient-to-r from-slate-50 via-white to-slate-50 backdrop-blur-md dark:from-slate-900 dark:via-slate-800 dark:to-slate-900">
              <tr className="border-b border-gray-200 text-left dark:border-slate-700">
                <th scope="col" className="w-10 px-3 py-3">
                  <input
                    type="checkbox"
                    aria-label="Select all on this page"
                    checked={pageAllChecked}
                    ref={(el) => {
                      if (el) el.indeterminate = pageIndeterminate;
                    }}
                    onChange={(e) => togglePageAll(e.target.checked)}
                    className="h-4 w-4 cursor-pointer rounded border-gray-300"
                    style={{ accentColor: themeColors.primary }}
                  />
                </th>
                <th scope="col" className="px-3 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400">
                  Product
                </th>
                <th scope="col" className="hidden w-32 px-3 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 md:table-cell dark:text-gray-400">
                  Category
                </th>
                <th scope="col" className="hidden w-32 px-3 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 md:table-cell dark:text-gray-400">
                  Brand
                </th>
                <th scope="col" className="hidden w-36 px-3 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 lg:table-cell dark:text-gray-400">
                  Supplier
                </th>
                <th scope="col" className="w-16 px-3 py-3 text-center text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400">
                  Qty
                </th>
                {hasActions && (
                  <th scope="col" className="w-24 px-3 py-3 text-right text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400">
                    Actions
                  </th>
                )}
              </tr>
            </thead>

            <tbody>
              {rows.length === 0 && !loading && (
                <tr>
                  <td className="px-5 py-16 text-center" colSpan={visibleColumns}>
                    <div className="flex flex-col items-center gap-3">
                      <div className="flex h-12 w-12 items-center justify-center rounded-full bg-gray-100 dark:bg-slate-700/60">
                        <CubeIcon className="h-6 w-6 text-gray-400" />
                      </div>
                      <div>
                        <p className="text-sm font-medium text-gray-600 dark:text-gray-300">No products found</p>
                        <p className="text-xs text-gray-400 dark:text-gray-500">Try adjusting your search or filters</p>
                      </div>
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
                
                // Stock tone drives the row rail + quantity chip
                const stockTone = qty > 0
                  ? {
                      rail: "#10b981",
                      chip:
                        "bg-emerald-50 text-emerald-700 ring-emerald-200/70 dark:bg-emerald-500/10 dark:text-emerald-400 dark:ring-emerald-500/30",
                      dot: "bg-emerald-500",
                    }
                  : {
                      rail: "#f59e0b",
                      chip:
                        "bg-amber-50 text-amber-700 ring-amber-200/70 dark:bg-amber-500/10 dark:text-amber-400 dark:ring-amber-500/30",
                      dot: "bg-amber-500",
                    };

                return (
                  <tr
                    key={p.id}
                    className="group border-b border-gray-100 transition-colors last:border-0 even:bg-gray-50/40 dark:border-slate-700/40 dark:even:bg-slate-800/20"
                    style={isSelected ? { backgroundColor: themeColors.primary + "0D" } : undefined}
                  >
                    {/* Select + stock rail */}
                    <td className="relative rounded-l-xl px-3 py-3 transition-colors duration-150 group-hover:bg-gray-50 dark:group-hover:bg-slate-700/25">
                      <span
                        aria-hidden="true"
                        className="absolute left-0 top-1/2 h-7 w-1 -translate-y-1/2 rounded-r-full"
                        style={{ backgroundColor: stockTone.rail }}
                      />
                      <input
                        type="checkbox"
                        checked={isSelected}
                        onChange={(e) => toggleOne(p.id, e.target.checked)}
                        aria-label={`Select ${p.name}`}
                        className="h-4 w-4 cursor-pointer rounded border-gray-300"
                        style={{ accentColor: themeColors.primary }}
                      />
                    </td>

                    {/* Product — monogram + name */}
                    <td className="px-3 py-3 transition-colors duration-150 group-hover:bg-gray-50 dark:group-hover:bg-slate-700/25">
                      <div className="flex items-center gap-3">
                        <span
                          aria-hidden="true"
                          className="hidden h-9 w-9 shrink-0 items-center justify-center rounded-xl text-[11px] font-bold tracking-wide text-white shadow-sm ring-1 ring-black/5 sm:flex"
                          style={{
                            background: `linear-gradient(135deg, ${themeColors.primary}, ${themeColors.secondary})`,
                          }}
                        >
                          {getInitials(p.name)}
                        </span>

                        <div className="min-w-0">
                          <button
                            type="button"
                            onClick={() => toggleById(p.id)}
                            title={p.name}
                            className={`
                              block w-full truncate text-left text-sm font-semibold transition-colors
                              ${isSelected ? "" : "text-gray-800 group-hover:text-gray-950 dark:text-gray-100 dark:group-hover:text-white"}
                            `}
                            style={isSelected ? { color: themeColors.primary } : undefined}
                          >
                            {p.name}
                          </button>

                          {/* Compact meta for narrow screens (columns hidden below md) */}
                          <div className="mt-0.5 flex items-center gap-1.5 text-[11px] text-gray-400 md:hidden dark:text-gray-500">
                            <span className="truncate">{p.category?.name || "—"}</span>
                            <span aria-hidden="true" className="text-gray-300 dark:text-slate-600">·</span>
                            <span className="truncate">{p.brand?.name || "—"}</span>
                            <span aria-hidden="true" className="text-gray-300 dark:text-slate-600">·</span>
                            <span className="truncate">{p.supplier?.name || "—"}</span>
                          </div>
                        </div>
                      </div>
                    </td>

                    {/* Category */}
                    <td className="hidden truncate px-3 py-3 text-gray-600 transition-colors duration-150 group-hover:bg-gray-50 md:table-cell dark:text-gray-300 dark:group-hover:bg-slate-700/25">
                      {p.category?.name || <span className="text-gray-300 dark:text-slate-600">—</span>}
                    </td>

                    {/* Brand */}
                    <td className="hidden truncate px-3 py-3 text-gray-600 transition-colors duration-150 group-hover:bg-gray-50 md:table-cell dark:text-gray-300 dark:group-hover:bg-slate-700/25">
                      {p.brand?.name || <span className="text-gray-300 dark:text-slate-600">—</span>}
                    </td>

                    {/* Supplier */}
                    <td className="hidden truncate px-3 py-3 text-gray-600 transition-colors duration-150 group-hover:bg-gray-50 lg:table-cell dark:text-gray-300 dark:group-hover:bg-slate-700/25">
                      {p.supplier?.name || <span className="text-gray-300 dark:text-slate-600">—</span>}
                    </td>

                    {/* Quantity — the single status indicator */}
                    <td className="px-3 py-3 text-center transition-colors duration-150 group-hover:bg-gray-50 dark:group-hover:bg-slate-700/25">
                      <span
                        className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-semibold ring-1 ${stockTone.chip}`}
                      >
                        <span className={`h-1.5 w-1.5 rounded-full ${stockTone.dot}`} />
                        <span className="tabular-nums">{qty}</span>
                      </span>
                    </td>

                    {/* Actions */}
                    {hasActions && (
                      <td className="rounded-r-xl px-3 py-3 transition-colors duration-150 group-hover:bg-gray-50 dark:group-hover:bg-slate-700/25">
                        <div className="flex items-center justify-end gap-1">
                          {/* Edit */}
                          <Guard when={can.update}>
                            <Link
                              to={`/products/${p.id}/edit`}
                              title="Edit"
                              aria-label={`Edit ${p.name}`}
                              className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-gray-400 opacity-80 transition-all duration-150 group-hover:opacity-100 hover:text-white hover:shadow-sm dark:text-gray-400"
                              onMouseEnter={(e) => {
                                e.currentTarget.style.backgroundColor = themeColors.primary;
                                e.currentTarget.style.color = "#fff";
                              }}
                              onMouseLeave={(e) => {
                                e.currentTarget.style.backgroundColor = "";
                                e.currentTarget.style.color = "";
                              }}
                            >
                              <PencilSquareIcon className="h-4 w-4" />
                            </Link>
                          </Guard>

                          {/* Delete */}
                          <Guard when={can.delete}>
                            <button
                              onClick={() => openDeleteModal(p)}
                              disabled={deleteDisabled}
                              title={deleteTitle}
                              aria-label={`Delete ${p.name}`}
                              className={`
                                inline-flex h-8 w-8 items-center justify-center rounded-lg transition-all duration-150
                                ${deleteDisabled
                                  ? "cursor-not-allowed text-gray-300 dark:text-slate-600"
                                  : "text-gray-400 opacity-80 hover:bg-rose-600 hover:text-white hover:opacity-100 hover:shadow-sm group-hover:opacity-100 dark:text-gray-400"}
                              `}
                            >
                              <TrashIcon className="h-4 w-4" />
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

        {/* Pagination */}
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-gray-100 bg-gray-50 px-5 py-3 dark:border-slate-700 dark:bg-slate-800/60">
          <span className="text-xs text-gray-500 dark:text-gray-400">
            Page <span className="font-medium text-gray-700 dark:text-gray-200">{page}</span> of{" "}
            <span className="font-medium text-gray-700 dark:text-gray-200">{lastPage}</span> · {total} total
          </span>

          <div className="flex items-center gap-1">
            <button
              onClick={() => setPage(1)}
              disabled={page === 1}
              title="First page"
              className={`rounded-lg p-2 transition-colors ${page === 1 ? "cursor-not-allowed text-gray-300 dark:text-slate-600" : "text-gray-500 hover:bg-gray-200 hover:text-gray-700 dark:hover:bg-slate-700 dark:hover:text-gray-200"}`}
            >
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 19l-7-7 7-7m8 14l-7-7 7-7" />
              </svg>
            </button>
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              title="Previous page"
              className={`rounded-lg p-2 transition-colors ${page === 1 ? "cursor-not-allowed text-gray-300 dark:text-slate-600" : "text-gray-500 hover:bg-gray-200 hover:text-gray-700 dark:hover:bg-slate-700 dark:hover:text-gray-200"}`}
            >
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
              </svg>
            </button>

            {/* Page numbers */}
            <div className="mx-1 flex items-center gap-0.5">
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
                      h-8 w-8 rounded-lg text-xs font-medium transition-colors
                      ${page === pageNum
                        ? "text-white shadow-sm"
                        : "text-gray-600 hover:bg-gray-200 dark:text-gray-300 dark:hover:bg-slate-700"
                      }
                    `}
                    style={page === pageNum ? { backgroundColor: themeColors.primary } : undefined}
                  >
                    {pageNum}
                  </button>
                );
              })}
            </div>

            <button
              onClick={() => setPage((p) => Math.min(lastPage, p + 1))}
              disabled={page === lastPage}
              title="Next page"
              className={`rounded-lg p-2 transition-colors ${page === lastPage ? "cursor-not-allowed text-gray-300 dark:text-slate-600" : "text-gray-500 hover:bg-gray-200 hover:text-gray-700 dark:hover:bg-slate-700 dark:hover:text-gray-200"}`}
            >
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
              </svg>
            </button>
            <button
              onClick={() => setPage(lastPage)}
              disabled={page === lastPage}
              title="Last page"
              className={`rounded-lg p-2 transition-colors ${page === lastPage ? "cursor-not-allowed text-gray-300 dark:text-slate-600" : "text-gray-500 hover:bg-gray-200 hover:text-gray-700 dark:hover:bg-slate-700 dark:hover:text-gray-200"}`}
            >
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 5l7 7-7 7M5 5l7 7-7 7" />
              </svg>
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
            primary: btnPrimary, 
            glass: btnGlass 
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
          primary: btnPrimary,
          danger: btnDanger,
          glass: btnGlass 
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
            primary: btnPrimary,
            danger: btnDanger,
            glass: btnGlass 
          }}
        />
      )}
    </div>
  );
}

