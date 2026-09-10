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

  // 🎨 Dynamic Button styles using theme colors
  const buttonStyle = theme?.button_style || 'rounded-sm';
  
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
    <div className="p-3 md:p-4 space-y-4">
      {/* ===== Premium Hero Header ===== */}
      <div
        className="relative rounded-2xl overflow-hidden shadow-lg"
        style={{ background: `linear-gradient(135deg, ${themeColors.secondary}, ${themeColors.primary}, ${themeColors.primaryHover})` }}
      >
        {/* Decorative blurred blobs */}
        <div className="absolute -top-10 -right-8 w-64 h-64 rounded-full bg-white/15 blur-3xl pointer-events-none" />
        <div className="absolute -bottom-16 left-1/4 w-72 h-72 rounded-full bg-white/10 blur-3xl pointer-events-none" />

        {/* Top row */}
        <div className="relative px-6 pt-5 pb-4 flex flex-col lg:flex-row lg:items-center justify-between gap-4">
          <div className="flex items-center gap-4">
            <div className="w-12 h-12 rounded-2xl bg-white/20 backdrop-blur-sm shadow-inner flex items-center justify-center">
              <TagIcon className="w-6 h-6 text-white" />
            </div>
            <div>
              <h1 className="text-2xl font-extrabold tracking-wide text-white leading-none">Brands</h1>
              <p className="text-xs text-white/85 mt-1.5 flex items-center gap-1.5">
                <TagIcon className="w-3.5 h-3.5" />
                {total} brands
              </p>
            </div>
          </div>

          {/* Actions */}
          <div className="flex items-center gap-2.5 flex-wrap">
            <Guard when={can.import}>
              <button
                onClick={() => setImportOpen(true)}
                className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm transition-all duration-200"
              >
                <ArrowUpTrayIcon className="w-3.5 h-3.5" />
                Import
              </button>
            </Guard>
            <Guard when={can.export}>
              <button
                onClick={handleExport}
                disabled={exporting}
                className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm transition-all duration-200"
              >
                <ArrowDownTrayIcon className={`w-3.5 h-3.5 ${exporting ? "animate-spin" : ""}`} />
                {exporting ? "..." : "Export"}
              </button>
            </Guard>
            <button
              onClick={() => {
                if (controllerRef.current) controllerRef.current.abort();
                const ctrl = new AbortController();
                controllerRef.current = ctrl;
                fetchBrands(ctrl.signal);
              }}
              title="Refresh"
              className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold text-white bg-white/15 hover:bg-white/25 backdrop-blur-sm transition-all duration-200"
            >
              <ArrowPathIcon className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Refresh</span>
            </button>
          </div>
        </div>

        {/* Filter bar integrated in hero */}
        <div className="relative px-6 pt-2 pb-5">
          <div className="flex items-center gap-2 bg-white/15 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2">
            <MagnifyingGlassIcon className="w-4 h-4 text-white/80 shrink-0" />
            <GlassInput
              value={qName}
              onChange={(e) => setQName(e.target.value)}
              placeholder="Search brands..."
              className="w-full"
            />
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-3">
        {/* Left: Form Card */}
        {(can.create || (can.update && editingId !== null)) && (
          <GlassCard>
            {/* Form Header */}
            <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200/60 dark:border-gray-700/60">
              <div className="flex items-center gap-3">
                <div 
                  className="p-2 rounded-lg shadow-xs"
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
                <GlassBtn 
                  className={`h-8 px-3 ${btnGlass.className}`} 
                  onClick={resetForm}
                  style={btnGlass.style}
                >
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
                  className={`h-9 px-3 ${btnGlass.className}`}
                  disabled={saving}
                  style={btnGlass.style}
                >
                  Clear
                </GlassBtn>
                <GlassBtn
                  type="button"
                  onClick={handleSubmit}
                  ref={saveBtnRef}
                  className={`flex-1 h-9 ${btnPrimary.className}`}
                  disabled={saving || (!can.create && !can.update)}
                  style={btnPrimary.style}
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
                className="p-1.5 rounded-sm"
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
                                  className={`inline-flex items-center gap-1 px-2 py-1 rounded-sm text-xs font-medium ${btnSecondary.className}`}
                                  style={btnSecondary.style}
                                >
                                  <PencilSquareIcon className="w-3.5 h-3.5" />
                                  Edit
                                </button>
                              </Guard>
                              <Guard when={can.delete}>
                                <button
                                  onClick={() => used ? toast.error("Cannot delete: brand is used by products.") : handleDelete(b)}
                                  disabled={used}
                                  className={`inline-flex items-center gap-1 px-2 py-1 rounded-sm text-xs font-medium transition-all duration-200 ${used ? btnGlass.className : btnDanger.className}`}
                                  style={used ? btnGlass.style : btnDanger.style}
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
                <button onClick={() => setPage(1)} disabled={page === 1} className={`p-1.5 rounded-sm hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>⏮</button>
                <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className={`p-1.5 rounded-sm hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>◀</button>
                <span className="mx-1 px-2 py-0.5 rounded-sm bg-gray-100 dark:bg-slate-700 font-medium">{page}</span>
                <button onClick={() => setPage((p) => Math.min(lastPage, p + 1))} disabled={page === lastPage} className={`p-1.5 rounded-sm hover:bg-gray-200 dark:hover:bg-slate-700 ${page === lastPage ? 'opacity-40' : ''}`}>▶</button>
                <button onClick={() => setPage(lastPage)} disabled={page === lastPage} className={`p-1.5 rounded-sm hover:bg-gray-200 dark:hover:bg-slate-700 ${page === lastPage ? 'opacity-40' : ''}`}>⏭</button>
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
