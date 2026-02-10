// src/pages/Brands.jsx
import { useEffect, useMemo, useRef, useState, useCallback } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import {
  MagnifyingGlassIcon,
  CheckCircleIcon,
  PencilSquareIcon,
  TrashIcon,
  ArrowUpTrayIcon,
  ArrowDownTrayIcon,
  ArrowPathIcon,
  PlusIcon,
  TagIcon,
  XMarkIcon,
} from "@heroicons/react/24/solid";
import BrandImportModal from "../components/BrandImportModal.jsx";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";

import {
  GlassCard,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";

export default function Brands() {
  // rows = current page rows (server-side)
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);
  const [exporting, setExporting] = useState(false);

  // form/edit
  const [form, setForm] = useState({ name: "", image: null });
  const [editingId, setEditingId] = useState(null);
  const [preview, setPreview] = useState(null);
  const [saving, setSaving] = useState(false);

  // import modal
  const [importOpen, setImportOpen] = useState(false);

  // search + server pagination
  const [qName, setQName] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [total, setTotal] = useState(0);
  const [lastPage, setLastPage] = useState(1);

  // focus + save
  const nameRef = useRef(null);
  const saveBtnRef = useRef(null);

  // fetch control
  const controllerRef = useRef(null);
  const debounceRef = useRef(null);

  // 🎨 Get theme colors
  const { theme } = useTheme();

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

  // Helper to determine text color based on background brightness
  const getContrastText = (hexColor) => {
    hexColor = hexColor.replace('#', '');
    const r = parseInt(hexColor.substring(0, 2), 16);
    const g = parseInt(hexColor.substring(2, 4), 16);
    const b = parseInt(hexColor.substring(4, 6), 16);
    const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    return luminance > 0.5 ? '#1f2937' : '#ffffff';
  };

  // Calculate text colors for buttons
  const primaryTextColor = useMemo(() => 
    getContrastText(themeColors.primaryHover || themeColors.primary), 
    [themeColors.primary, themeColors.primaryHover]
  );
  
  const secondaryTextColor = useMemo(() => 
    getContrastText(themeColors.secondaryHover || themeColors.secondary), 
    [themeColors.secondary, themeColors.secondaryHover]
  );

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can =
    (typeof canFor === "function" ? canFor("brand") : null) ?? {
      view: false,
      create: false,
      update: false,
      delete: false,
      import: false,
      export: false,
    };

  // 🎨 Modern button palette (organized by action type using theme colors)
  // Primary: Add, Save, Update actions
  const tintPrimary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Secondary: Refresh, Import, Edit table actions
  const tintSecondary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Danger: Delete actions
  const tintDanger = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Glass: Export and utility actions
  const tintGlass = useMemo(() => `
    bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Disabled state
  const tintDisabled = useMemo(() => `
    bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed
  `.trim().replace(/\s+/g, ' '), []);

  // ===== Section config =====
  const SECTION_CONFIG = {
    management: {
      gradient: themeColors.secondary,
      bgLight: themeColors.secondaryLight,
      bgDark: themeColors.secondaryLight,
      iconColor: themeColors.secondary,
    },
  };

  useEffect(() => {
    document.title = "Brands - Pharmacy ERP";
  }, []);

  useEffect(() => {
    nameRef.current?.focus();
  }, [editingId]);

  // Alt+S -> save
  useEffect(() => {
    const onKeyDown = (e) => {
      if (e.altKey && (e.key || "").toLowerCase() === "s") {
        if (!can.create && !can.update) return;
        e.preventDefault();
        handleSubmit();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [form, editingId, can.create, can.update]);

  const onEnterFocusSave = (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      saveBtnRef.current?.focus();
    }
  };

  // ===== Server fetch =====
  const fetchBrands = useCallback(
    async (signal) => {
      try {
        setLoading(true);
        const { data } = await axios.get("/api/brands", {
          params: { page, per_page: pageSize, q_name: qName.trim() },
          signal,
        });

        const items = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
        setRows(items);
        setTotal(Number(data?.total ?? items.length ?? 0));
        const lp = Number(data?.last_page ?? 1);
        setLastPage(lp);
        if (page > lp) setPage(lp || 1);
      } catch (err) {
        if (axios.isCancel?.(err)) return;
        if (err?.response?.status === 403) toast.error("You don't have permission to view brands.");
        else toast.error("Failed to load brands");
      } finally {
        setLoading(false);
      }
    },
    [page, pageSize, qName]
  );

  // non-debounced: page/pageSize
  useEffect(() => {
    if (permsLoading || !can.view) return;
    if (controllerRef.current) controllerRef.current.abort();
    const ctrl = new AbortController();
    controllerRef.current = ctrl;
    fetchBrands(ctrl.signal);
  }, [page, pageSize, permsLoading, can.view, fetchBrands]);

  // debounced: qName
  useEffect(() => {
    if (permsLoading || !can.view) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setPage(1);
      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      fetchBrands(ctrl.signal);
    }, 300);
    return () => clearTimeout(debounceRef.current);
  }, [qName, permsLoading, can.view, fetchBrands]);

  const start = rows.length ? (page - 1) * pageSize + 1 : 0;
  const end = rows.length ? start + rows.length - 1 : 0;

  // ===== Preview helpers =====
  const revokeIfBlob = (url) => {
    if (url && typeof url === "string" && url.startsWith("blob:")) {
      try {
        URL.revokeObjectURL(url);
      } catch {}
    }
  };

  useEffect(() => {
    return () => revokeIfBlob(preview);
  }, [preview]);

  // ===== Form handlers =====
  const handleInputChange = (e) => setForm({ ...form, [e.target.name]: e.target.value });

  const handleFileChange = (e) => {
    const file = e.target.files?.[0] ?? null;
    if (preview) revokeIfBlob(preview);
    setForm((prev) => ({ ...prev, image: file }));
    setPreview(file ? URL.createObjectURL(file) : (editingId ? null : null));
  };

  const resetForm = () => {
    revokeIfBlob(preview);
    setForm({ name: "", image: null });
    setPreview(null);
    setEditingId(null);
    setTimeout(() => nameRef.current?.focus(), 0);
  };

  const handleSubmit = async () => {
    if (editingId ? !can.update : !can.create) {
      toast.error("You don't have permission to save brands.");
      return;
    }
    if (saving) return;

    const name = (form.name || "").trim();
    if (!name) {
      toast.error("Name is required");
      nameRef.current?.focus();
      return;
    }

    try {
      setSaving(true);
      const data = new FormData();
      data.append("name", name);
      if (form.image) data.append("image", form.image);

      if (editingId) {
        data.append("_method", "PUT");
        await axios.post(`/api/brands/${editingId}`, data, {
          headers: { "Content-Type": "multipart/form-data" },
        });
        toast.success("Brand updated");
      } else {
        await axios.post("/api/brands", data, {
          headers: { "Content-Type": "multipart/form-data" },
        });
        toast.success("Brand saved");
      }

      resetForm();
      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      await fetchBrands(ctrl.signal);
    } catch (err) {
      if (err?.response?.status === 403) {
        toast.error("You don't have permission to save brands.");
      } else {
        const msg =
          err?.response?.data?.message ||
          err?.response?.data?.errors?.name?.[0] ||
          err?.response?.data?.errors?.image?.[0] ||
          "Save failed";
        toast.error(msg);
      }
    } finally {
      setSaving(false);
    }
  };

  const handleEdit = (b) => {
    if (!can.update) return toast.error("You don't have permission to edit brands.");
    setForm({ name: b.name || "", image: null });
    if (preview) revokeIfBlob(preview);
    const nextPreview = b.image ? `/storage/${b.image}` : null;
    setPreview(nextPreview);
    setEditingId(b.id);
  };

  const handleDelete = async (b) => {
    if (!can.delete) return toast.error("You don't have permission to delete brands.");
    const used = Number(b.products_count || 0) > 0;
    if (used) return toast.error("Cannot delete: brand is used by products.");

    try {
      await axios.delete(`/api/brands/${b.id}`);
      toast.success("Brand deleted");
      if (controllerRef.current) controllerRef.current.abort();
      const ctrl = new AbortController();
      controllerRef.current = ctrl;
      await fetchBrands(ctrl.signal);
      if (editingId === b.id) resetForm();
    } catch (err) {
      if (err?.response?.status === 403)
        toast.error("You don't have permission to delete brands.");
      else toast.error(err?.response?.data?.message || "Could not delete brand.");
    }
  };

  // export all brands
  const handleExport = async () => {
    if (!can.export) return toast.error("You don't have permission to export brands.");
    try {
      setExporting(true);
      const res = await axios.get("/api/brands/export", { responseType: "blob" });
      const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-");
      const filename = `brands_${stamp}.csv`;
      const blob = new Blob([res.data], { type: "text/csv;charset=utf-8" });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    } catch (e) {
      if (e?.response?.status === 403)
        toast.error("You don't have permission to export brands.");
      else toast.error("Export failed");
    } finally {
      setExporting(false);
    }
  };

  // perms loading / no-view states
  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view brands.</div>;

  const hasActions = can.update || can.delete;

  return (
    <div className="p-3 md:p-4 space-y-3">
      {/* Header Card */}
      <GlassCard>
        {/* Modern Card Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200/60 dark:border-gray-700/60">
            <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: SECTION_CONFIG.management.gradient }}
            >
              <TagIcon 
                className="w-5 h-5" 
                style={{ color: 'white' }} 
              />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Brands</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{total} items</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <GlassBtn 
              className={`h-9 px-3 ${tintSecondary}`} 
              onClick={() => {
                if (controllerRef.current) controllerRef.current.abort();
                const ctrl = new AbortController();
                controllerRef.current = ctrl;
                fetchBrands(ctrl.signal);
              }}
              style={{
                background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                color: secondaryTextColor,
                boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`
              }}
            >
              <span className="inline-flex items-center gap-1.5">
                <ArrowPathIcon className="w-4 h-4" />
                <span className="hidden sm:inline">Refresh</span>
              </span>
            </GlassBtn>
          </div>
        </div>

        {/* Search and Actions */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="flex flex-col sm:flex-row gap-3">
            <div className="relative flex-1 max-w-md">
              <MagnifyingGlassIcon className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <GlassInput
                value={qName}
                onChange={(e) => setQName(e.target.value)}
                placeholder="Search brands..."
                className="pl-9 w-full h-9"
              />
            </div>
            <div className="flex items-center gap-2 sm:ml-auto">
              <Guard when={can.import}>
                <GlassBtn 
                  className={`h-9 px-3 ${tintPrimary}`} 
                  onClick={() => setImportOpen(true)}
                  style={{ 
                    background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                    color: primaryTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                  }}
                >
                  <span className="inline-flex items-center gap-1.5">
                    <ArrowUpTrayIcon className="w-4 h-4" />
                    <span className="hidden sm:inline">Import</span>
                  </span>
                </GlassBtn>
              </Guard>
              <Guard when={can.export}>
                <GlassBtn className={`h-9 px-3 ${tintGlass}`} onClick={handleExport} disabled={exporting}>
                  <span className="inline-flex items-center gap-1.5">
                    <ArrowDownTrayIcon className={`w-4 h-4 ${exporting ? "animate-spin" : ""}`} />
                    <span className="hidden sm:inline">{exporting ? "..." : "Export"}</span>
                  </span>
                </GlassBtn>
              </Guard>
            </div>
          </div>
        </div>

        {/* Footer with stats */}
        <div className="flex items-center justify-between px-4 py-2 border-t border-gray-100 dark:border-slate-700 text-xs">
          <span className="text-gray-500 dark:text-gray-400">
            {loading ? (
              <span className="inline-flex items-center gap-1">
                <ArrowPathIcon className="w-3.5 h-3.5 animate-spin" />
                Loading...
              </span>
            ) : (
              `${rows.length === 0 ? 0 : start + 1}-${Math.min(rows.length, start + pageSize)} of ${total}`
            )}
          </span>
          <div className="flex items-center gap-2">
            <label className="text-gray-500 dark:text-gray-400">Show</label>
            <select
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
              className="h-7 px-2 rounded border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-xs"
            >
              <option value={10}>10</option>
              <option value={25}>25</option>
              <option value={50}>50</option>
            </select>
          </div>
        </div>
      </GlassCard>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-3">
        {/* Left: Form Card */}
        {(can.create || (can.update && editingId !== null)) && (
          <GlassCard>
            {/* Form Header */}
            <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200/60 dark:border-gray-700/60">
              <div className="flex items-center gap-3">
                <div 
                  className="p-2 rounded-lg shadow-sm"
                  style={{ background: `linear-gradient(to bottom right, ${editingId ? '#f59e0b' : themeColors.primary}, ${editingId ? '#d97706' : themeColors.primaryHover})` }}
                >
                  {editingId ? (
                    <PencilSquareIcon className="w-5 h-5 text-white" />
                  ) : (
                    <PlusIcon className="w-5 h-5 text-white" />
                  )}
                </div>
                <div>
                  <h2 className="text-base font-semibold text-gray-900 dark:text-white">
                    {editingId ? "Edit Brand" : "Add Brand"}
                  </h2>
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    {editingId ? "Update brand information" : "Enter brand details"}
                  </p>
                </div>
              </div>
              {editingId && (
                <GlassBtn className={`h-8 px-3 ${tintGlass}`} onClick={resetForm}>
                  <XMarkIcon className="w-4 h-4" />
                </GlassBtn>
              )}
            </div>

            <form onSubmit={(e) => e.preventDefault()} className="p-3 space-y-3" encType="multipart/form-data">
              <div>
                <label className="block text-xs font-medium mb-1">Name</label>
                <GlassInput
                  type="text"
                  name="name"
                  placeholder="Brand name"
                  value={form.name}
                  onChange={handleInputChange}
                  onKeyDown={onEnterFocusSave}
                  ref={nameRef}
                  disabled={!can.create && !editingId}
                  className="w-full h-9"
                />
              </div>

              <div className="grid grid-cols-3 gap-2 items-end">
                <div className="col-span-2">
                  <label className="block text-xs font-medium mb-1">Image (optional)</label>
                  <input
                    key={editingId || "new"}
                    type="file"
                    name="image"
                    accept="image/*"
                    onChange={handleFileChange}
                    disabled={!can.create && !editingId}
                    className="h-9 w-full rounded-lg bg-white/70 dark:bg-slate-700/70 border border-gray-200/60 dark:border-slate-600/60 text-xs file:mr-2 file:px-2 file:rounded-lg file:border-0 file:bg-white/70 file:text-slate-700"
                  />
                </div>
                <div>
                  {preview ? (
                    <img src={preview} alt="Preview" className="w-12 h-12 rounded-xl object-contain ring-1 ring-gray-200/70 bg-white/70" />
                  ) : (
                    <div className="w-12 h-12 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center">
                      <span className="text-[10px] text-gray-400">No img</span>
                    </div>
                  )}
                </div>
              </div>

              <div className="flex items-center gap-2">
                <GlassBtn
                  type="button"
                  onClick={resetForm}
                  className={`h-9 px-3 ${tintGlass}`}
                  disabled={saving}
                >
                  Clear
                </GlassBtn>
                <GlassBtn
                  type="button"
                  onClick={handleSubmit}
                  ref={saveBtnRef}
                  className={`flex-1 h-9 ${tintPrimary}`}
                  disabled={saving || (!can.create && !can.update)}
                  style={{ 
                    background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                    color: primaryTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                  }}
                >
                  <span className="inline-flex items-center justify-center gap-1.5">
                    <CheckCircleIcon className="w-4 h-4" />
                    {editingId ? (saving ? "Updating..." : "Update") : saving ? "Saving..." : "Save"}
                  </span>
                </GlassBtn>
              </div>

              <div className="text-[10px] text-gray-500 text-center">Alt+S to save</div>
            </form>
          </GlassCard>
        )}

        {/* Right: List Card */}
        <GlassCard className={can.create || can.update ? "lg:col-span-2" : "lg:col-span-3"}>
          {/* List Header */}
          <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200/60 dark:border-gray-700/60">
            <div className="flex items-center gap-2">
              <div 
                className="p-1.5 rounded"
                style={{ backgroundColor: SECTION_CONFIG.management.bgDark }}
              >
                <TagIcon 
                  className="w-4 h-4" 
                  style={{ color: SECTION_CONFIG.management.iconColor }} 
                />
              </div>
              <span className="font-medium text-sm">Brand List</span>
            </div>
            <span className="text-xs text-gray-400">{rows.length} items</span>
          </div>

          <div className="p-3">
            <div className="rounded-xl overflow-hidden ring-1 ring-gray-200/70 bg-white/60 dark:bg-slate-800/60">
              <table className="w-full text-sm">
                <thead className="bg-white/80 dark:bg-slate-700/80 sticky top-0">
                  <tr className="border-b border-gray-200/70 dark:border-slate-600/70 text-left">
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Name</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase w-20">Image</th>
                    {hasActions && (
                      <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase text-center w-32">Actions</th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && !loading && (
                    <tr>
                      <td className="px-3 py-10 text-center text-gray-500 dark:text-gray-400" colSpan={hasActions ? 3 : 2}>
                        <div className="flex flex-col items-center gap-2">
                          <TagIcon className="w-8 h-8 text-gray-400" />
                          <p className="text-sm">No brands found</p>
                        </div>
                      </td>
                    </tr>
                  )}

                  {rows.map((b) => {
                    const used = Number(b.products_count || 0) > 0;
                    return (
                      <tr key={b.id} className="odd:bg-white/90 even:bg-white/70 dark:odd:bg-slate-700/60 dark:even:bg-slate-800/60 hover:bg-blue-50/70 dark:hover:bg-slate-600/50 transition-colors">
                        <td className="px-3 py-2.5">
                          <span className="font-medium text-gray-900 dark:text-gray-100">{b.name}</span>
                        </td>
                        <td className="px-3 py-2.5">
                          {b.image ? (
                            <img src={`/storage/${b.image}`} alt={b.name} className="w-12 h-12 rounded-xl object-contain ring-1 ring-gray-200/70 bg-white/70" />
                          ) : (
                            <div className="w-12 h-12 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center">
                              <span className="text-[10px] text-gray-400">—</span>
                            </div>
                          )}
                        </td>
                        {hasActions && (
                          <td className="px-3 py-2.5">
                            <div className="flex items-center justify-center gap-1.5">
                              <Guard when={can.update}>
                                <button 
                                  onClick={() => handleEdit(b)} 
                                  className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium ${tintSecondary}`}
                                  style={{
                                    background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                                    color: secondaryTextColor,
                                    boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`
                                  }}
                                >
                                  <PencilSquareIcon className="w-3.5 h-3.5" />
                                  Edit
                                </button>
                              </Guard>
                              <Guard when={can.delete}>
                                <button
                                  onClick={() => used ? toast.error("Cannot delete: brand is used by products.") : handleDelete(b)}
                                  disabled={used}
                                  className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium transition-all duration-200 ${
                                    used 
                                      ? tintDisabled
                                      : tintDanger
                                  }`}
                                  style={!used ? {
                                    background: 'linear-gradient(to bottom right, #ef4444, #dc2626)',
                                    color: '#ffffff',
                                    boxShadow: '0 4px 14px 0 rgba(239, 68, 68, 0.4)'
                                  } : {}}
                                >
                                  <TrashIcon className="w-3.5 h-3.5" />
                                  Delete
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
            <div className="mt-3 flex items-center justify-between text-xs">
              <span className="text-gray-500 dark:text-gray-400">Page {page} of {lastPage}</span>
              <div className="flex items-center gap-1">
                <button onClick={() => setPage(1)} disabled={page === 1} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>⏮</button>
                <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>◀</button>
                <span className="mx-1 px-2 py-0.5 rounded bg-gray-100 dark:bg-slate-700 font-medium">{page}</span>
                <button onClick={() => setPage((p) => Math.min(lastPage, p + 1))} disabled={page === lastPage} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === lastPage ? 'opacity-40' : ''}`}>▶</button>
                <button onClick={() => setPage(lastPage)} disabled={page === lastPage} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === lastPage ? 'opacity-40' : ''}`}>⏭</button>
              </div>
            </div>
          </div>
        </GlassCard>
      </div>

      {/* Import modal */}
      <BrandImportModal
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onImported={() => {
          if (controllerRef.current) controllerRef.current.abort();
          const ctrl = new AbortController();
          controllerRef.current = ctrl;
          fetchBrands(ctrl.signal);
        }}
      />
    </div>
  );
}

