// src/pages/Customers.jsx
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
  UsersIcon,
  XMarkIcon,
} from "@heroicons/react/24/solid";
import CustomerImportModal from "../components/CustomerImportModal.jsx";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";

import {
  GlassCard,
  GlassInput,
  GlassBtn,
} from "@/components/Glass.jsx";
import { getThemeButtonClasses } from "@/utils/buttonStyles";

export default function Customers() {
  const [customers, setCustomers] = useState([]);
  const [form, setForm] = useState({
    name: "",
    email: "",
    phone: "",
    address: "",
    credit_limit: "",
    credit_days: 30,
    credit_status: "active",
  });
  const [editingId, setEditingId] = useState(null);

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [exporting, setExporting] = useState(false);

  // search + pagination
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  // focus refs
  const nameRef = useRef(null);
  const emailRef = useRef(null);
  const phoneRef = useRef(null);
  const addressRef = useRef(null);
  const saveBtnRef = useRef(null);

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("customer") : null) ?? {
        view: false,
        create: false,
        update: false,
        delete: false,
        import: false,
        export: false,
      },
    [canFor]
  );

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

  useEffect(() => {
    document.title = "Customers - Pharmacy ERP";
  }, []);

  const fetchCustomers = useCallback(async () => {
    try {
      setLoading(true);
      const res = await axios.get("/api/customers");
      setCustomers(res.data || []);
    } catch (err) {
      if (err?.response?.status === 403)
        toast.error("You don't have permission to view customers.");
      else toast.error("Failed to load customers");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!permsLoading && can.view) fetchCustomers();
  }, [permsLoading, can.view, fetchCustomers]);

  useEffect(() => {
    nameRef.current?.focus();
  }, [editingId]);

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
    if (e.key === "Enter") {
      e.preventDefault();
      nextRef?.current?.focus();
    }
  };

  const resetForm = () => {
    setForm({
      name: "",
      email: "",
      phone: "",
      address: "",
      credit_limit: "",
      credit_days: 30,
      credit_status: "active",
    });
    setEditingId(null);
    setTimeout(() => nameRef.current?.focus(), 0);
  };

  const handleSave = async () => {
    if (editingId ? !can.update : !can.create) {
      toast.error("You don't have permission to save customers.");
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
      if (editingId) {
        await axios.put(`/api/customers/${editingId}`, form);
        toast.success("Customer updated");
      } else {
        await axios.post("/api/customers", form);
        toast.success("Customer saved");
      }
      resetForm();
      fetchCustomers();
    } catch (err) {
      if (err?.response?.status === 403) {
        toast.error("You don't have permission to save customers.");
      } else {
        const msg =
          err?.response?.data?.message ||
          err?.response?.data?.errors?.name?.[0] ||
          err?.response?.data?.errors?.email?.[0] ||
          "Save failed";
        toast.error(msg);
      }
    } finally {
      setSaving(false);
    }
  };

  const handleEdit = (c) => {
    if (!can.update) return toast.error("You don't have permission to edit customers.");
    setForm({
      name: c.name || "",
      email: c.email || "",
      phone: c.phone || "",
      address: c.address || "",
      credit_limit: c.credit_limit !== null && c.credit_limit !== undefined ? c.credit_limit : "",
      credit_days: c.credit_days !== null && c.credit_days !== undefined ? c.credit_days : 30,
      credit_status: c.credit_status || "active",
    });
    setEditingId(c.id);
  };

  const handleDelete = async (c) => {
    if (!can.delete) return toast.error("You don't have permission to delete customers.");
    try {
      await axios.delete(`/api/customers/${c.id}`);
      setCustomers((prev) => prev.filter((x) => Number(x.id) !== Number(c.id)));
      if (Number(editingId) === Number(c.id)) resetForm();
      toast.success("Customer deleted");
    } catch (err) {
      if (err?.response?.status === 403)
        toast.error("You don't have permission to delete customers.");
      else toast.error(err?.response?.data?.message || "Delete failed");
    }
  };

  const handleExport = async () => {
    if (!can.export) return toast.error("You don't have permission to export customers.");
    try {
      setExporting(true);
      const res = await axios.get("/api/customers/export", { responseType: "blob" });
      const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-");
      const filename = `customers_${stamp}.csv`;
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
        toast.error("You don't have permission to export customers.");
      else toast.error("Export failed");
    } finally {
      setExporting(false);
    }
  };

  // search + pagination
  const norm = (v) => (v ?? "").toString().toLowerCase().trim();
  const filtered = useMemo(() => {
    const needle = norm(q);
    if (!needle) return customers;
    return customers.filter((c) =>
      [c.name, c.email, c.phone, c.address].some((f) => norm(f).includes(needle))
    );
  }, [customers, q]);

  useEffect(() => {
    setPage(1);
  }, [q, pageSize]);

  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize));
  useEffect(() => {
    if (page > pageCount) setPage(pageCount);
  }, [page, pageCount]);

  const start = (page - 1) * pageSize;
  const paged = filtered.slice(start, start + pageSize);

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

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view customers.</div>;

  const hasActions = can.update || can.delete;

  // ===== Section config =====
  const SECTION_CONFIG = {
    management: {
      gradient: themeColors.secondary,
      bgLight: themeColors.secondaryLight,
      bgDark: themeColors.secondaryLight,
      iconColor: themeColors.secondary,
    },
  };

return (
    <div className="directory-page">
      <div className="products-panel directory-overview">
        {/* Top row */}
        <div className="products-heading">
          <div className="flex items-center gap-4">
            <div className="products-identity-icon">
              <UsersIcon className="w-6 h-6" />
            </div>
            <div>
              <h1 className="products-title">Customers</h1>
              <p className="products-subtitle">
                <UsersIcon className="w-3.5 h-3.5" />
                {customers.length} customers
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
              onClick={fetchCustomers}
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
              value={q}
              onChange={(e) => setQ(e.target.value)}
              aria-label="Search customers"
              placeholder="Search customers..."
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
                    {editingId ? "Edit Customer" : "Add Customer"}
                  </h2>
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    {editingId ? "Update customer information" : "Enter customer details"}
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
                <label className="block text-xs font-medium mb-1">Name *</label>
                <GlassInput
                  type="text"
                  placeholder="Customer name"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  onKeyDown={(e) => onEnterFocusNext(e, emailRef)}
                  ref={nameRef}
                  disabled={!can.create && !editingId}
                  className="w-full h-9"
                />
              </div>

              <div>
                <label className="block text-xs font-medium mb-1">Email</label>
                <GlassInput
                  type="email"
                  placeholder="Email address"
                  value={form.email || ""}
                  onChange={(e) => setForm({ ...form, email: e.target.value })}
                  onKeyDown={(e) => onEnterFocusNext(e, phoneRef)}
                  ref={emailRef}
                  className="w-full h-9"
                />
              </div>

              <div>
                <label className="block text-xs font-medium mb-1">Phone</label>
                <GlassInput
                  type="text"
                  placeholder="Phone number"
                  value={form.phone || ""}
                  onChange={(e) => setForm({ ...form, phone: e.target.value })}
                  onKeyDown={(e) => onEnterFocusNext(e, addressRef)}
                  ref={phoneRef}
                  className="w-full h-9"
                />
              </div>

              <div>
                <label className="block text-xs font-medium mb-1">Address</label>
                <GlassInput
                  type="text"
                  placeholder="Address"
                  value={form.address || ""}
                  onChange={(e) => setForm({ ...form, address: e.target.value })}
                  onKeyDown={(e) => onEnterFocusNext(e, saveBtnRef)}
                  ref={addressRef}
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
                    placeholder="e.g. 50000 (0 or empty = unlimited)"
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

              <div>
                <label className="block text-xs font-medium mb-1">Credit Status</label>
                <select
                  value={form.credit_status}
                  onChange={(e) => setForm({ ...form, credit_status: e.target.value })}
                  className="w-full h-9 px-3 rounded text-xs bg-white/70 dark:bg-slate-700/70 border border-gray-300 dark:border-slate-600 text-gray-800 dark:text-gray-200 focus:outline-none focus:ring-1 focus:ring-blue-500"
                >
                  <option value="active">Active (Credit permitted)</option>
                  <option value="on_hold">On Hold (Requires override)</option>
                  <option value="blocked">Blocked (Credit blocked)</option>
                </select>
              </div>

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
                <UsersIcon 
                  className="w-4 h-4" 
                  style={{ color: SECTION_CONFIG.management.iconColor }} 
                />
              </div>
              <span className="font-medium text-sm">Customer List</span>
            </div>
            <span className="text-xs text-gray-400">{paged.length} items</span>
          </div>

          <div className="p-3">
            <div className="directory-table-scroll">
              <table className="w-full text-sm">
                <thead className="bg-white/80 dark:bg-slate-700/80 sticky top-0">
                  <tr className="border-b border-gray-200/70 dark:border-slate-600/70 text-left">
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Name</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Email / Phone</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase text-right">Credit Limit</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase text-center">Terms</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase text-center">Status</th>
                    {hasActions && (
                      <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase text-center w-24">Actions</th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {paged.length === 0 && !loading && (
                    <tr>
                      <td className="px-3 py-10 text-center text-gray-500 dark:text-gray-400" colSpan={hasActions ? 6 : 5}>
                        <div className="flex flex-col items-center gap-2">
                          <UsersIcon className="w-8 h-8 text-gray-400" />
                          <p className="text-sm">No customers found</p>
                        </div>
                      </td>
                    </tr>
                  )}

                  {paged.map((c) => {
                    const inUse = Number(c.transactions_count || 0) > 0;
                    const status = c.credit_status || "active";
                    const statusBadge =
                      status === "blocked"
                        ? "bg-rose-100 text-rose-700 dark:bg-rose-900/40 dark:text-rose-300 border-rose-200"
                        : status === "on_hold"
                        ? "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300 border-amber-200"
                        : "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 border-emerald-200";

                    return (
                      <tr key={c.id} className="odd:bg-white/90 even:bg-white/70 dark:odd:bg-slate-700/60 dark:even:bg-slate-800/60 hover:bg-blue-50/70 dark:hover:bg-slate-600/50 transition-colors">
                        <td className="px-3 py-2.5">
                          <span className="font-medium text-gray-900 dark:text-gray-100">{c.name}</span>
                          {c.address && <span className="block text-[11px] text-gray-400 truncate max-w-xs">{c.address}</span>}
                        </td>
                        <td className="px-3 py-2.5 text-xs text-gray-600 dark:text-gray-400">
                          <div>{c.phone || "—"}</div>
                          <div className="text-[11px] text-gray-400">{c.email}</div>
                        </td>
                        <td className="px-3 py-2.5 text-right font-medium text-xs text-gray-800 dark:text-gray-200">
                          {c.credit_limit && Number(c.credit_limit) > 0 ? (
                            `Rs. ${Number(c.credit_limit).toLocaleString()}`
                          ) : (
                            <span className="text-gray-400 italic">Unlimited</span>
                          )}
                        </td>
                        <td className="px-3 py-2.5 text-center text-xs text-gray-600 dark:text-gray-400">
                          {c.credit_days !== null && c.credit_days !== undefined ? `${c.credit_days}d` : "30d"}
                        </td>
                        <td className="px-3 py-2.5 text-center">
                          <span className={`inline-block px-2 py-0.5 text-[10px] font-semibold rounded-full border ${statusBadge}`}>
                            {status === "on_hold" ? "Hold" : status.toUpperCase()}
                          </span>
                        </td>
                        {hasActions && (
                          <td className="px-3 py-2.5">
                            <div className="flex items-center justify-center gap-1.5">
                              <Guard when={can.update}>
                                <button 
                                  onClick={() => handleEdit(c)} 
                                  className="products-row-action"
                                  aria-label={`Edit ${c.name}`}
                                  title="Edit"
                                >
                                  <PencilSquareIcon aria-hidden="true" className="w-4 h-4" />
                                </button>
                              </Guard>
                              <Guard when={can.delete}>
                                <button
                                  onClick={() => inUse ? toast.error("Cannot delete: customer has transactions.") : handleDelete(c)}
                                  disabled={inUse}
                                  className="products-row-action products-row-delete"
                                  aria-label={`Delete ${c.name}`}
                                  title={inUse ? "Cannot delete: customer has transactions." : "Delete"}
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
      <CustomerImportModal
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onImported={fetchCustomers}
      />
    </div>
  );
}
