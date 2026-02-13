// src/pages/Suppliers.jsx
import { useState, useEffect, useMemo, useRef, useCallback } from "react";
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
  BuildingStorefrontIcon,
  XMarkIcon,
} from "@heroicons/react/24/solid";
import SupplierImportModal from "../components/SupplierImportModal.jsx";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";

import {
  GlassCard,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";

export default function Suppliers() {
  const [suppliers, setSuppliers] = useState([]);
  const [form, setForm] = useState({ name: "", address: "", phone: "" });
  const [editingId, setEditingId] = useState(null);

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [exporting, setExporting] = useState(false);

  // search + pagination
  const [qName, setQName] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  // focus refs
  const nameRef = useRef(null);
  const addressRef = useRef(null);
  const phoneRef = useRef(null);
  const saveBtnRef = useRef(null);

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
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("supplier") : null) ?? {
        view: false,
        create: false,
        update: false,
        delete: false,
        import: false,
        export: false,
      },
    [canFor]
  );

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
    document.title = "Suppliers - Pharmacy ERP";
  }, []);

  const fetchSuppliers = useCallback(async () => {
    try {
      setLoading(true);
      const res = await axios.get("/api/suppliers");
      setSuppliers(res.data || []);
    } catch (err) {
      const status = err?.response?.status;
      if (status === 403) toast.error("You don't have permission to view suppliers.");
      else toast.error("Failed to fetch suppliers");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!permsLoading && can.view) fetchSuppliers();
  }, [permsLoading, can.view, fetchSuppliers]);

  useEffect(() => { nameRef.current?.focus(); }, [editingId]);

  // Alt+S -> Save
  useEffect(() => {
    const onKeyDown = (e) => {
      if (e.altKey && (e.key || "").toLowerCase() === "s") {
        if (!can.create && !can.update) return;
        e.preventDefault();
        handleSave();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [form, editingId, can.create, can.update]);

  const onEnterFocusNext = (e, nextRef) => {
    if (e.key === "Enter") { e.preventDefault(); nextRef?.current?.focus(); }
  };

  const resetForm = () => {
    setForm({ name: "", address: "", phone: "" });
    setEditingId(null);
    setTimeout(() => nameRef.current?.focus(), 0);
  };

  const handleSave = async () => {
    if (editingId ? !can.update : !can.create) {
      toast.error("You don't have permission to save suppliers.");
      return;
    }
    if (saving) return;
    if (!(form.name || "").trim()) {
      toast.error("Name is required");
      nameRef.current?.focus();
      return;
    }

    try {
      setSaving(true);
      if (editingId) {
        await axios.put(`/api/suppliers/${editingId}`, form);
        toast.success("Supplier updated");
      } else {
        await axios.post("/api/suppliers", form);
        toast.success("Supplier saved");
      }
      resetForm();
      fetchSuppliers();
    } catch (err) {
      const status = err?.response?.status;
      if (status === 403) toast.error("You don't have permission to save suppliers.");
      else toast.error(err?.response?.data?.message || "Save failed");
    } finally {
      setSaving(false);
    }
  };

  const handleEdit = (s) => {
    if (!can.update) return toast.error("You don't have permission to edit suppliers.");
    setForm({ name: s.name || "", address: s.address || "", phone: s.phone || "" });
    setEditingId(s.id);
  };

  const handleDelete = async (s) => {
    if (!can.delete) return toast.error("You don't have permission to delete suppliers.");
    try {
      await axios.delete(`/api/suppliers/${s.id}`);
      setSuppliers((prev) => prev.filter((x) => Number(x.id) !== Number(s.id)));
      if (Number(editingId) === Number(s.id)) resetForm();
      toast.success("Supplier deleted");
    } catch (err) {
      const status = err?.response?.status;
      if (status === 403) toast.error("You don't have permission to delete suppliers.");
      else toast.error(err?.response?.data?.message || "Delete failed");
    }
  };

  const handleExport = async () => {
    if (!can.export) return toast.error("You don't have permission to export suppliers.");
    try {
      setExporting(true);
      const res = await axios.get("/api/suppliers/export", { responseType: "blob" });
      const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-");
      const filename = `suppliers_${stamp}.csv`;
      const blob = new Blob([res.data], { type: "text/csv;charset=utf-8" });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = filename; document.body.appendChild(a); a.click(); a.remove();
      window.URL.revokeObjectURL(url);
    } catch (e) {
      const status = e?.response?.status;
      if (status === 403) toast.error("You don't have permission to export suppliers.");
      else toast.error("Export failed");
    } finally {
      setExporting(false);
    }
  };

  // client-side search + pagination
  const norm = (v) => (v ?? "").toString().toLowerCase().trim();
  const filtered = useMemo(() => {
    const needle = norm(qName);
    if (!needle) return suppliers;
    return suppliers.filter((s) => norm(s.name).includes(needle));
  }, [suppliers, qName]);

  useEffect(() => { setPage(1); }, [qName, pageSize]);

  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize));
  useEffect(() => { if (page > pageCount) setPage(pageCount); }, [page, pageCount]);

  const start = (page - 1) * pageSize;
  const paged = filtered.slice(start, start + pageSize);

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view suppliers.</div>;

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
              <BuildingStorefrontIcon 
                className="w-5 h-5" 
                style={{ color: 'white' }} 
              />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Suppliers</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{suppliers.length} items</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <GlassBtn 
              className={`h-9 px-3 ${btnSecondary.className}`} 
              onClick={fetchSuppliers}
              style={btnSecondary.style}
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
                placeholder="Search suppliers..."
                className="pl-9 w-full h-9"
              />
            </div>
            <div className="flex items-center gap-2 sm:ml-auto">
              <Guard when={can.import}>
                <GlassBtn 
                  className={`h-9 px-3 ${btnPrimary.className}`} 
                  onClick={() => setImportOpen(true)}
                  style={btnPrimary.style}
                >
                  <span className="inline-flex items-center gap-1.5">
                    <ArrowUpTrayIcon className="w-4 h-4" />
                    <span className="hidden sm:inline">Import</span>
                  </span>
                </GlassBtn>
              </Guard>
              <Guard when={can.export}>
                <GlassBtn 
                  className={`h-9 px-3 ${btnGlass.className}`} 
                  onClick={handleExport} 
                  disabled={exporting}
                  style={btnGlass.style}
                >
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
              `${filtered.length === 0 ? 0 : start + 1}-${Math.min(filtered.length, start + pageSize)} of ${filtered.length}`
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
                    {editingId ? "Edit Supplier" : "Add Supplier"}
                  </h2>
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    {editingId ? "Update supplier information" : "Enter supplier details"}
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

            <form onSubmit={(e) => e.preventDefault()} className="p-3 space-y-3">
              <div>
                <label className="block text-xs font-medium mb-1">Name</label>
                <GlassInput
                  type="text"
                  placeholder="Supplier name"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  onKeyDown={(e) => onEnterFocusNext(e, addressRef)}
                  ref={nameRef}
                  disabled={!can.create && !editingId}
                  className="w-full h-9"
                />
              </div>

              <div>
                <label className="block text-xs font-medium mb-1">Address</label>
                <GlassInput
                  type="text"
                  placeholder="Address"
                  value={form.address}
                  onChange={(e) => setForm({ ...form, address: e.target.value })}
                  onKeyDown={(e) => onEnterFocusNext(e, phoneRef)}
                  ref={addressRef}
                  className="w-full h-9"
                />
              </div>

              <div>
                <label className="block text-xs font-medium mb-1">Phone</label>
                <GlassInput
                  type="text"
                  placeholder="Phone number"
                  value={form.phone}
                  onChange={(e) => setForm({ ...form, phone: e.target.value })}
                  onKeyDown={(e) => onEnterFocusNext(e, saveBtnRef)}
                  ref={phoneRef}
                  className="w-full h-9"
                />
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
                  onClick={handleSave}
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
                className="p-1.5 rounded"
                style={{ backgroundColor: SECTION_CONFIG.management.bgDark }}
              >
                <BuildingStorefrontIcon 
                  className="w-4 h-4" 
                  style={{ color: SECTION_CONFIG.management.iconColor }} 
                />
              </div>
              <span className="font-medium text-sm">Supplier List</span>
            </div>
            <span className="text-xs text-gray-400">{paged.length} items</span>
          </div>

          <div className="p-3">
            <div className="rounded-xl overflow-hidden ring-1 ring-gray-200/70 bg-white/60 dark:bg-slate-800/60">
              <table className="w-full text-sm">
                <thead className="bg-white/80 dark:bg-slate-700/80 sticky top-0">
                  <tr className="border-b border-gray-200/70 dark:border-slate-600/70 text-left">
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Name</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Address</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase w-32">Phone</th>
                    {(can.update || can.delete) && (
                      <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase text-center w-32">Actions</th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {paged.length === 0 && !loading && (
                    <tr>
                      <td className="px-3 py-10 text-center text-gray-500 dark:text-gray-400" colSpan={(can.update || can.delete) ? 4 : 3}>
                        <div className="flex flex-col items-center gap-2">
                          <BuildingStorefrontIcon className="w-8 h-8 text-gray-400" />
                          <p className="text-sm">No suppliers found</p>
                        </div>
                      </td>
                    </tr>
                  )}

                  {paged.map((s) => {
                    const used = Number(s.products_count || 0) > 0;
                    return (
                      <tr key={s.id} className="odd:bg-white/90 even:bg-white/70 dark:odd:bg-slate-700/60 dark:even:bg-slate-800/60 hover:bg-blue-50/70 dark:hover:bg-slate-600/50 transition-colors">
                        <td className="px-3 py-2.5">
                          <span className="font-medium text-gray-900 dark:text-gray-100">{s.name}</span>
                        </td>
                        <td className="px-3 py-2.5 text-gray-600 dark:text-gray-400">{s.address}</td>
                        <td className="px-3 py-2.5 text-gray-600 dark:text-gray-400">{s.phone}</td>
                        {(can.update || can.delete) && (
                          <td className="px-3 py-2.5">
                            <div className="flex items-center justify-center gap-1.5">
                              <Guard when={can.update}>
                                <button 
                                  onClick={() => handleEdit(s)} 
                                  className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium ${btnSecondary.className}`}
                                  style={btnSecondary.style}
                                >
                                  <PencilSquareIcon className="w-3.5 h-3.5" />
                                  Edit
                                </button>
                              </Guard>
                              <Guard when={can.delete}>
                                <button
                                  onClick={() => used ? toast.error("Cannot delete: supplier is used by products.") : handleDelete(s)}
                                  disabled={used}
                                  className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium transition-all duration-200 ${used ? btnGlass.className : btnDanger.className}`}
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
              <span className="text-gray-500 dark:text-gray-400">Page {page} of {pageCount}</span>
              <div className="flex items-center gap-1">
                <button onClick={() => setPage(1)} disabled={page === 1} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>⏮</button>
                <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>◀</button>
                <span className="mx-1 px-2 py-0.5 rounded bg-gray-100 dark:bg-slate-700 font-medium">{page}</span>
                <button onClick={() => setPage((p) => Math.min(pageCount, p + 1))} disabled={page === pageCount} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === pageCount ? 'opacity-40' : ''}`}>▶</button>
                <button onClick={() => setPage(pageCount)} disabled={page === pageCount} className={`p-1.5 rounded hover:bg-gray-200 dark:hover:bg-slate-700 ${page === pageCount ? 'opacity-40' : ''}`}>⏭</button>
              </div>
            </div>
          </div>
        </GlassCard>
      </div>

      {/* Import modal */}
      <SupplierImportModal
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onImported={fetchSuppliers}
      />
    </div>
  );
}
