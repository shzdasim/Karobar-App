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

import {
  GlassCard,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";

export default function Customers() {
  const [customers, setCustomers] = useState([]);
  const [form, setForm] = useState({ name: "", email: "", phone: "", address: "" });
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
    setForm({ name: "", email: "", phone: "", address: "" });
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

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view customers.</div>;

  const hasActions = can.update || can.delete;

  // 🎨 Modern button palette
  const tintBlue   = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] hover:from-blue-600 hover:to-blue-700 active:scale-[0.98] transition-all duration-200";
  const tintIndigo = "bg-gradient-to-br from-indigo-500 to-indigo-600 text-white shadow-lg shadow-indigo-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-indigo-500/30 hover:scale-[1.02] hover:from-indigo-600 hover:to-indigo-700 active:scale-[0.98] transition-all duration-200";
  const tintSlate  = "bg-gradient-to-br from-slate-700 to-slate-800 text-white shadow-lg shadow-slate-500/25 ring-1 ring-white/10 hover:shadow-xl hover:shadow-slate-500/30 hover:scale-[1.02] hover:from-slate-800 hover:to-slate-900 active:scale-[0.98] transition-all duration-200";
  const tintAmber  = "bg-gradient-to-br from-amber-500 to-amber-600 text-white shadow-lg shadow-amber-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-amber-500/30 hover:scale-[1.02] hover:from-amber-600 hover:to-amber-700 active:scale-[0.98] transition-all duration-200";
  const tintRed    = "bg-gradient-to-br from-rose-500 to-rose-600 text-white shadow-lg shadow-rose-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700 active:scale-[0.98] transition-all duration-200";
  const tintGlass  = "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";

  // ===== Section config =====
  const SECTION_CONFIG = {
    management: {
      gradient: "from-violet-500 to-purple-600",
      bgLight: "bg-violet-50",
      bgDark: "dark:bg-violet-900/20",
      iconColor: "text-violet-600 dark:text-violet-400",
    },
  };

  return (
    <div className="p-3 md:p-4 space-y-3">
      {/* Header Card */}
      <GlassCard>
        {/* Modern Card Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200/60 dark:border-gray-700/60">
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.management.gradient} shadow-sm`}>
              <UsersIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Customers</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">{customers.length} items</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <GlassBtn className={`h-9 px-3 ${tintSlate}`} onClick={fetchCustomers}>
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
                value={q}
                onChange={(e) => setQ(e.target.value)}
                placeholder="Search customers..."
                className="pl-9 w-full h-9"
              />
            </div>
            <div className="flex items-center gap-2 sm:ml-auto">
              <Guard when={can.import}>
                <GlassBtn className={`h-9 px-3 ${tintIndigo}`} onClick={() => setImportOpen(true)}>
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
                <div className={`p-2 rounded-lg bg-gradient-to-br ${editingId ? "from-amber-500 to-orange-600" : "from-blue-500 to-blue-600"} shadow-sm`}>
                  {editingId ? (
                    <PencilSquareIcon className="w-5 h-5 text-white" />
                  ) : (
                    <PlusIcon className="w-5 h-5 text-white" />
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
                <GlassBtn className={`h-8 px-3 ${tintGlass}`} onClick={resetForm}>
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
                  onClick={handleSave}
                  ref={saveBtnRef}
                  className={`flex-1 h-9 ${editingId ? tintAmber : tintBlue}`}
                  disabled={saving || (!can.create && !can.update)}
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
              <div className={`p-1.5 rounded ${SECTION_CONFIG.management.bgDark}`}>
                <UsersIcon className="w-4 h-4 text-violet-600 dark:text-violet-400" />
              </div>
              <span className="font-medium text-sm">Customer List</span>
            </div>
            <span className="text-xs text-gray-400">{paged.length} items</span>
          </div>

          <div className="p-3">
            <div className="rounded-xl overflow-hidden ring-1 ring-gray-200/70 bg-white/60 dark:bg-slate-800/60">
              <table className="w-full text-sm">
                <thead className="bg-white/80 dark:bg-slate-700/80 sticky top-0">
                  <tr className="border-b border-gray-200/70 dark:border-slate-600/70 text-left">
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Name</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase">Email</th>
                    <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase w-32">Phone</th>
                    {hasActions && (
                      <th className="px-3 py-2 font-medium text-gray-600 dark:text-gray-300 text-xs uppercase text-center w-32">Actions</th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {paged.length === 0 && !loading && (
                    <tr>
                      <td className="px-3 py-10 text-center text-gray-500 dark:text-gray-400" colSpan={hasActions ? 4 : 3}>
                        <div className="flex flex-col items-center gap-2">
                          <UsersIcon className="w-8 h-8 text-gray-400" />
                          <p className="text-sm">No customers found</p>
                        </div>
                      </td>
                    </tr>
                  )}

                  {paged.map((c) => {
                    const inUse = Number(c.transactions_count || 0) > 0;
                    return (
                      <tr key={c.id} className="odd:bg-white/90 even:bg-white/70 dark:odd:bg-slate-700/60 dark:even:bg-slate-800/60 hover:bg-blue-50/70 dark:hover:bg-slate-600/50 transition-colors">
                        <td className="px-3 py-2.5">
                          <span className="font-medium text-gray-900 dark:text-gray-100">{c.name}</span>
                        </td>
                        <td className="px-3 py-2.5 text-gray-600 dark:text-gray-400 break-all">{c.email}</td>
                        <td className="px-3 py-2.5 text-gray-600 dark:text-gray-400">{c.phone}</td>
                        {hasActions && (
                          <td className="px-3 py-2.5">
                            <div className="flex items-center justify-center gap-1.5">
                              <Guard when={can.update}>
                                <button onClick={() => handleEdit(c)} className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium ${tintAmber}`}>
                                  <PencilSquareIcon className="w-3.5 h-3.5" />
                                  Edit
                                </button>
                              </Guard>
                              <Guard when={can.delete}>
                                <button
                                  onClick={() => inUse ? toast.error("Cannot delete: customer has transactions.") : handleDelete(c)}
                                  disabled={inUse}
                                  className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium transition-all duration-200 ${
                                    inUse 
                                      ? "bg-gray-100 dark:bg-slate-700/50 text-gray-400 dark:text-gray-500 cursor-not-allowed" 
                                      : tintRed
                                  }`}
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
      <CustomerImportModal
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onImported={fetchCustomers}
      />
    </div>
  );
}

