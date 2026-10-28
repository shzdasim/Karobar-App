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
  BanknotesIcon,
  ExclamationTriangleIcon,
} from "@heroicons/react/24/solid";
import SupplierImportModal from "../components/SupplierImportModal.jsx";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";

import {
  GlassCard,
  GlassInput,
  GlassBtn,
} from "@/components/Glass.jsx";
import { getThemeButtonClasses } from "@/utils/buttonStyles";

export default function Suppliers() {
  const [suppliers, setSuppliers] = useState([]);
  const [form, setForm] = useState({ name: "", address: "", phone: "", email: "", credit_limit: "", credit_days: "", opening_balance: "" });
  const [editingId, setEditingId] = useState(null);

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [exporting, setExporting] = useState(false);

  // Credit summary modal state
  const [creditModalSupplier, setCreditModalSupplier] = useState(null);
  const [creditSummary, setCreditSummary] = useState(null);
  const [loadingCreditSummary, setLoadingCreditSummary] = useState(false);

  const openCreditSummary = async (supplier) => {
    setCreditModalSupplier(supplier);
    setCreditSummary(null);
    setLoadingCreditSummary(true);
    try {
      const { data } = await axios.get(`/api/suppliers/${supplier.id}/credit-summary`);
      setCreditSummary(data);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to load supplier credit summary");
    } finally {
      setLoadingCreditSummary(false);
    }
  };

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
  const buttonStyle = theme?.button_style || 'modern';
  
  const getButtonClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark),
    [buttonStyle, themeColors, isDark]
  );

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
    setForm({ name: "", address: "", phone: "", email: "", credit_limit: "", credit_days: "", opening_balance: "" });
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
    setForm({
      name: s.name || "",
      address: s.address || "",
      phone: s.phone || "",
      email: s.email || "",
      credit_limit: s.credit_limit ?? "",
      credit_days: s.credit_days ?? "",
    });
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
    <div className="directory-page">
      <div className="products-panel directory-overview">
        {/* Top row */}
        <div className="products-heading">
          <div className="flex items-center gap-4">
            <div className="products-identity-icon">
              <BuildingStorefrontIcon className="w-6 h-6" />
            </div>
            <div>
              <h1 className="products-title">Suppliers</h1>
              <p className="products-subtitle">
                <BuildingStorefrontIcon className="w-3.5 h-3.5" />
                {suppliers.length} suppliers
              </p>
            </div>
          </div>

          {/* Actions */}
          <div className="flex items-center gap-2.5 flex-wrap">
            <Guard when={can.import}>
              <button
                onClick={() => setImportOpen(true)}
                className="products-action theme-btn-secondary"
              >
                <ArrowUpTrayIcon className="w-3.5 h-3.5" />
                <span>Import</span>
              </button>
            </Guard>
            <Guard when={can.export}>
              <button
                onClick={handleExport}
                disabled={exporting}
                className="products-action theme-btn-secondary"
              >
                <ArrowDownTrayIcon className={`w-3.5 h-3.5 ${exporting ? "animate-spin" : ""}`} />
                <span>{exporting ? "..." : "Export"}</span>
              </button>
            </Guard>
            <button
              onClick={fetchSuppliers}
              title="Refresh"
              className="products-action theme-btn-tertiary"
            >
              <ArrowPathIcon className="w-3.5 h-3.5" />
              <span>Refresh</span>
            </button>
          </div>
        </div>

        {/* Filter bar integrated in hero */}
        <div className="directory-search">
          <div className="directory-search-field">
            <MagnifyingGlassIcon className="directory-search-icon" />
            <GlassInput
              value={qName}
              onChange={(e) => setQName(e.target.value)}
              aria-label="Search suppliers"
              placeholder="Search suppliers..."
              className="w-full"
            />
          </div>
        </div>
      </div>

      <div className="directory-grid">
        {/* Left: Form Card */}
        {(can.create || (can.update && editingId !== null)) && (
          <GlassCard className="directory-panel directory-form">
            {/* Form Header */}
            <div className="directory-panel-heading">
              <div className="flex items-center gap-3">
                <div 
                  className="directory-form-icon"
                >
                  {editingId ? (
                    <PencilSquareIcon className="w-5 h-5" />
                  ) : (
                    <PlusIcon className="w-5 h-5" />
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
                  className="h-8 px-3" 
                  onClick={resetForm}
                  aria-label="Cancel editing"
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
                  placeholder="Phone number (e.g. 0300-1234567)"
                  value={form.phone}
                  onChange={(e) => setForm({ ...form, phone: e.target.value })}
                  onKeyDown={(e) => onEnterFocusNext(e, saveBtnRef)}
                  ref={phoneRef}
                  className="w-full h-9"
                />
              </div>

              <div>
                <label className="block text-xs font-medium mb-1">Email (Optional)</label>
                <GlassInput
                  type="email"
                  placeholder="supplier@example.com"
                  value={form.email}
                  onChange={(e) => setForm({ ...form, email: e.target.value })}
                  className="w-full h-9"
                />
              </div>

              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="block text-xs font-medium mb-1">Credit Limit (Rs.)</label>
                  <GlassInput
                    type="number"
                    min="0"
                    step="any"
                    placeholder="e.g. 100000"
                    value={form.credit_limit}
                    onChange={(e) => setForm({ ...form, credit_limit: e.target.value })}
                    className="w-full h-9"
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium mb-1">Credit Days</label>
                  <GlassInput
                    type="number"
                    min="0"
                    max="365"
                    placeholder="e.g. 30"
                    value={form.credit_days}
                    onChange={(e) => setForm({ ...form, credit_days: e.target.value })}
                    className="w-full h-9"
                  />
                </div>
              </div>

              {!editingId && (
                <div>
                  <label className="block text-xs font-medium mb-1">Opening Balance Payable (Rs.)</label>
                  <GlassInput
                    type="number"
                    min="0"
                    step="0.01"
                    placeholder="0.00 (Optional initial payable balance)"
                    value={form.opening_balance ?? ""}
                    onChange={(e) => setForm({ ...form, opening_balance: e.target.value })}
                    className="w-full h-9"
                  />
                </div>
              )}

              <div className="flex items-center gap-2">
                <GlassBtn
                  type="button"
                  onClick={resetForm}
                  className="h-9 px-3 theme-btn-tertiary"
                  disabled={saving}
                >
                  <span>Clear</span>
                </GlassBtn>
                <GlassBtn
                  type="button"
                  variant="primary"
                  onClick={handleSave}
                  ref={saveBtnRef}
                  className="glass-save-btn flex-1 h-9 theme-btn-primary"
                  disabled={saving || (!can.create && !can.update)}
                  aria-busy={saving}
                  aria-keyshortcuts="Alt+S"
                >
                  {saving ? (
                    <ArrowPathIcon aria-hidden="true" className="w-4 h-4 animate-spin motion-reduce:animate-none" />
                  ) : (
                    <CheckCircleIcon aria-hidden="true" className="w-4 h-4" />
                  )}
                  <span>
                    {editingId ? (saving ? "Updating..." : "Update") : saving ? "Saving..." : "Save"}
                  </span>
                </GlassBtn>
              </div>

              <div className="text-[10px] text-gray-500 text-center">Alt+S to save</div>
            </form>
          </GlassCard>
        )}

        {/* Right: List Card */}
        <GlassCard className="directory-panel directory-list">
          {/* List Header */}
          <div className="directory-panel-heading">
            <div className="flex items-center gap-2">
              <div 
                className="p-1.5 rounded-sm"
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
            <div className="directory-table-scroll">
              <table className="w-full text-sm">
                <thead className="bg-white/80 dark:bg-slate-700/80 sticky top-0">
                  <tr className="border-b border-gray-200/70 dark:border-slate-600/70 text-left">
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Name</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Address</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase w-32">Phone</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase w-28">Credit Limit</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase w-20">Terms</th>
                    {(can.view || can.update || can.delete) && (
                      <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase text-center w-28">Actions</th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {paged.length === 0 && !loading && (
                    <tr>
                      <td className="px-3 py-10 text-center text-gray-500 dark:text-gray-400" colSpan={(can.view || can.update || can.delete) ? 6 : 5}>
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
                        <td className="px-3 py-2.5 text-gray-600 dark:text-gray-400">
                          <div>{s.phone}</div>
                          {s.email && <div className="text-[11px] text-gray-400 dark:text-gray-500">{s.email}</div>}
                        </td>
                        <td className="px-3 py-2.5 text-xs text-gray-700 dark:text-gray-300 font-mono">
                          {Number(s.credit_limit) > 0 ? `Rs. ${Number(s.credit_limit).toLocaleString()}` : "Unlimited"}
                        </td>
                        <td className="px-3 py-2.5 text-xs text-gray-700 dark:text-gray-300">
                          {Number(s.credit_days) > 0 ? `${s.credit_days} days` : "Immediate"}
                        </td>
                        {(can.view || can.update || can.delete) && (
                          <td className="px-3 py-2.5">
                            <div className="flex items-center justify-center gap-1.5">
                              <Guard when={can.view}>
                                <button
                                  type="button"
                                  onClick={() => openCreditSummary(s)}
                                  className="products-row-action"
                                  aria-label={`Credit Summary for ${s.name}`}
                                  title="View Credit Summary & Overdue Status"
                                >
                                  <BanknotesIcon aria-hidden="true" className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
                                </button>
                              </Guard>
                              <Guard when={can.update}>
                                <button 
                                  onClick={() => handleEdit(s)} 
                                  className="products-row-action"
                                  aria-label={`Edit ${s.name}`}
                                  title="Edit"
                                >
                                  <PencilSquareIcon aria-hidden="true" className="w-4 h-4" />
                                </button>
                              </Guard>
                              <Guard when={can.delete}>
                                <button
                                  onClick={() => used ? toast.error("Cannot delete: supplier is used by products.") : handleDelete(s)}
                                  disabled={used}
                                  className="products-row-action products-row-delete"
                                  aria-label={`Delete ${s.name}`}
                                  title={used ? "Cannot delete: supplier is used by products." : "Delete"}
                                >
                                  <TrashIcon aria-hidden="true" className="w-4 h-4" />
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
                <button onClick={() => setPage(1)} disabled={page === 1} className={`p-1.5 rounded-sm hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>⏮</button>
                <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className={`p-1.5 rounded-sm hover:bg-gray-200 dark:hover:bg-slate-700 ${page === 1 ? 'opacity-40' : ''}`}>◀</button>
                <span className="mx-1 px-2 py-0.5 rounded-sm bg-gray-100 dark:bg-slate-700 font-medium">{page}</span>
                <button onClick={() => setPage((p) => Math.min(pageCount, p + 1))} disabled={page === pageCount} className={`p-1.5 rounded-sm hover:bg-gray-200 dark:hover:bg-slate-700 ${page === pageCount ? 'opacity-40' : ''}`}>▶</button>
                <button onClick={() => setPage(pageCount)} disabled={page === pageCount} className={`p-1.5 rounded-sm hover:bg-gray-200 dark:hover:bg-slate-700 ${page === pageCount ? 'opacity-40' : ''}`}>⏭</button>
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

      {/* Supplier Credit Summary Modal */}
      {creditModalSupplier && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-xs p-4"
          onClick={() => setCreditModalSupplier(null)}
        >
          <div
            className="bg-white dark:bg-slate-800 rounded-2xl shadow-2xl border border-gray-200 dark:border-slate-700 w-full max-w-lg overflow-hidden"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-center justify-between px-5 py-4 border-b border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800/80">
              <div className="flex items-center gap-2.5">
                <div className="p-2 rounded-xl bg-emerald-500/10 text-emerald-600 dark:text-emerald-400">
                  <BanknotesIcon className="w-5 h-5" />
                </div>
                <div>
                  <h3 className="text-sm font-bold text-gray-900 dark:text-gray-100">
                    Supplier Credit Summary — {creditModalSupplier.name}
                  </h3>
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    Payables exposure, credit limit &amp; overdue bills
                  </p>
                </div>
              </div>
              <button
                type="button"
                onClick={() => setCreditModalSupplier(null)}
                className="p-1.5 rounded-lg text-gray-400 hover:text-gray-600 hover:bg-gray-100 dark:hover:bg-slate-700"
              >
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>

            <div className="p-5 space-y-4">
              {loadingCreditSummary ? (
                <div className="py-10 text-center text-xs text-gray-500">
                  <ArrowPathIcon className="w-6 h-6 mx-auto animate-spin mb-2 text-emerald-500" />
                  Loading credit exposure summary…
                </div>
              ) : creditSummary ? (
                <>
                  {creditSummary.is_limit_exceeded && (
                    <div className="p-3 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 flex items-center gap-2.5 text-xs text-rose-700 dark:text-rose-300">
                      <ExclamationTriangleIcon className="w-5 h-5 shrink-0 text-rose-600 dark:text-rose-400" />
                      <div>
                        <span className="font-bold block">Credit Limit Exceeded</span>
                        Payable balance exceeds the configured supplier credit limit.
                      </div>
                    </div>
                  )}

                  <div className="grid grid-cols-2 gap-3 text-xs">
                    <div className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-gray-50/70 dark:bg-slate-900/40">
                      <span className="text-[10px] uppercase tracking-wider text-gray-400 block">Current Payable</span>
                      <span className="text-base font-bold font-mono text-gray-900 dark:text-gray-100">
                        Rs. {Number(creditSummary.current_balance || 0).toLocaleString()}
                      </span>
                    </div>
                    <div className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-gray-50/70 dark:bg-slate-900/40">
                      <span className="text-[10px] uppercase tracking-wider text-gray-400 block">Credit Limit</span>
                      <span className="text-base font-bold font-mono text-gray-900 dark:text-gray-100">
                        {Number(creditSummary.credit_limit) > 0
                          ? `Rs. ${Number(creditSummary.credit_limit).toLocaleString()}`
                          : "Unlimited"}
                      </span>
                    </div>
                    <div className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-gray-50/70 dark:bg-slate-900/40">
                      <span className="text-[10px] uppercase tracking-wider text-gray-400 block">Available Credit</span>
                      <span className="text-base font-bold font-mono text-emerald-600 dark:text-emerald-400">
                        {creditSummary.available_credit !== null && creditSummary.available_credit !== undefined
                          ? `Rs. ${Number(creditSummary.available_credit).toLocaleString()}`
                          : "Unlimited"}
                      </span>
                    </div>
                    <div className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-gray-50/70 dark:bg-slate-900/40">
                      <span className="text-[10px] uppercase tracking-wider text-gray-400 block">Credit Terms</span>
                      <span className="text-base font-bold text-gray-900 dark:text-gray-100">
                        {Number(creditSummary.credit_days) > 0 ? `${creditSummary.credit_days} Days` : "Immediate"}
                      </span>
                    </div>
                  </div>

                  <div className="p-3.5 rounded-xl border border-amber-200/80 dark:border-amber-800/50 bg-amber-50/60 dark:bg-amber-950/20 flex items-center justify-between text-xs">
                    <div>
                      <span className="text-[10px] uppercase tracking-wider font-semibold text-amber-700 dark:text-amber-400 block">
                        Overdue Payable Amount ({creditSummary.overdue_invoices_count || 0} bill{creditSummary.overdue_invoices_count === 1 ? "" : "s"})
                      </span>
                      <span className="text-sm font-bold font-mono text-amber-900 dark:text-amber-200">
                        Rs. {Number(creditSummary.total_overdue || 0).toLocaleString()}
                      </span>
                    </div>
                    {creditSummary.oldest_due_date && (
                      <div className="text-right">
                        <span className="text-[10px] uppercase tracking-wider text-amber-600 dark:text-amber-400 block">
                          Oldest Due Date
                        </span>
                        <span className="font-mono font-semibold text-amber-900 dark:text-amber-200">
                          {creditSummary.oldest_due_date}
                        </span>
                      </div>
                    )}
                  </div>
                </>
              ) : (
                <div className="py-6 text-center text-xs text-gray-400">No credit summary available.</div>
              )}
            </div>

            <div className="px-5 py-3 border-t border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800/50 flex justify-end">
              <button
                type="button"
                onClick={() => setCreditModalSupplier(null)}
                className="px-4 py-1.5 rounded-lg text-xs font-semibold bg-gray-200 dark:bg-slate-700 text-gray-700 dark:text-gray-200 hover:bg-gray-300 dark:hover:bg-slate-600"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
