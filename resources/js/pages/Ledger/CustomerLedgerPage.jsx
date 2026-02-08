// resources/js/pages/CustomerLedgerPage.jsx
import React, { useEffect, useMemo, useState, useRef } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import CustomerSearchInput from "../../components/CustomerSearchInput.jsx";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";

// 🧊 glass primitives
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";

import {
  ArrowPathIcon,
  PrinterIcon,
  PlusCircleIcon,
  WrenchScrewdriverIcon,
  ArrowDownOnSquareIcon,
  ShieldExclamationIcon,
  XMarkIcon,
  CubeIcon,
  Squares2X2Icon,
  UserIcon,
} from "@heroicons/react/24/solid";

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

/* =========================
   Customer Ledger Page (Modernized)
   ========================= */
export default function CustomerLedgerPage() {
  const [customers, setCustomers] = useState([]);
  const [customerId, setCustomerId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [rows, setRows] = useState([]);
  const [summary, setSummary] = useState({
    total_invoiced: 0,
    received_on_invoice: 0,
    payments_credited: 0,
    net_balance: 0,
  });

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("customer-ledger") : {
        view:false, create:false, update:false, delete:false, import:false, export:false
      }),
    [canFor]
  );

  // theme
  const { isDark } = useTheme();

  // 🎨 Modern button palette (matching SupplierLedger design)
  const tintBlue   = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] hover:from-blue-600 hover:to-blue-700 active:scale-[0.98] transition-all duration-200";
  const tintIndigo = "bg-gradient-to-br from-indigo-500 to-indigo-600 text-white shadow-lg shadow-indigo-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-indigo-500/30 hover:scale-[1.02] hover:from-indigo-600 hover:to-indigo-700 active:scale-[0.98] transition-all duration-200";
  const tintSlate  = "bg-gradient-to-br from-slate-700 to-slate-800 text-white shadow-lg shadow-slate-500/25 ring-1 ring-white/10 hover:shadow-xl hover:shadow-slate-500/30 hover:scale-[1.02] hover:from-slate-800 hover:to-slate-900 active:scale-[0.98] transition-all duration-200";
  const tintAmber  = "bg-gradient-to-br from-amber-500 to-amber-600 text-white shadow-lg shadow-amber-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-amber-500/30 hover:scale-[1.02] hover:from-amber-600 hover:to-amber-700 active:scale-[0.98] transition-all duration-200";
  const tintRed    = "bg-gradient-to-br from-rose-500 to-rose-600 text-white shadow-lg shadow-rose-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-rose-500/30 hover:scale-[1.02] hover:from-rose-600 hover:to-rose-700 active:scale-[0.98] transition-all duration-200";
  const tintGreen  = "bg-gradient-to-br from-emerald-500 to-emerald-600 text-white shadow-lg shadow-emerald-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-emerald-500/30 hover:scale-[1.02] hover:from-emerald-600 hover:to-emerald-700 active:scale-[0.98] transition-all duration-200";
  const tintGlass  = "bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";
  const tintOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 hover:shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all duration-200";
  const tintIconBtn = "bg-white/60 dark:bg-slate-700/60 backdrop-blur-sm text-slate-600 dark:text-gray-300 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-md hover:scale-[1.05] active:scale-[0.95] transition-all duration-200";

  // ---------- utils ----------
  const fmt = (v) => {
    if (v === null || v === undefined || v === "") return "0";
    const n = Number(v);
    if (!Number.isFinite(n)) return "0";
    return new Intl.NumberFormat(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(n);
  };

  // hotkeys (guarded)
  useEffect(() => {
    const onKey = (e) => {
      const k = (e.key || "").toLowerCase();
      if (e.altKey && k === "s") {
        e.preventDefault();
        if (!can.create && !can.update) return;
        openSaveModal();
      }
      if (e.altKey && k === "p") {
        e.preventDefault();
        if (!can.view) return;
        handlePrint();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [can.create, can.update, can.view]);

  // Load customers
  useEffect(() => {
    if (permsLoading || !can.view) return;
    (async () => {
      try {
        const { data } = await axios.get("/api/customers", { params: { limit: 500 } });
        const list = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
        setCustomers(list);
      } catch {
        toast.error("Failed to load customers");
        setCustomers([]);
      }
    })();
  }, [permsLoading, can.view]);

  const fetchData = async () => {
    if (!can.view) return toast.error("You don't have permission to view customer ledger.");
    if (!customerId) return toast.error("Select a customer first");
    try {
      const { data } = await axios.get("/api/customer-ledger", {
        params: { customer_id: customerId, from, to },
      });
      const clean = (data.data || []).map((r) => {
        const c = { ...r };
        Object.keys(c).forEach((k) => k.endsWith("_input") && delete c[k]);
        return c;
      });
      setRows(clean);
      setSummary(
        data.summary || {
          total_invoiced: 0,
          received_on_invoice: 0,
          payments_credited: 0,
          net_balance: 0,
        }
      );
    } catch (e) {
      toast.error(e?.response?.data?.message || "Failed to load ledger");
    }
  };

  const rebuild = async () => {
    if (!can.update) return toast.error("You don't have permission to rebuild.");
    if (!customerId) return toast.error("Select a customer first");
    try {
      await axios.post("/api/customer-ledger/rebuild", { customer_id: customerId });
      toast.success("Rebuilt from sale invoices");
      await fetchData();
    } catch (e) {
      toast.error(e?.response?.data?.message || "Rebuild failed");
    }
  };

  // ---------- number editing helpers ----------
  const getInput = (row, field) => (row[`${field}_input`] !== undefined ? row[`${field}_input`] : (row[field] ?? "") + "");
  const setInput = (idx, field, raw) => {
    setRows((prev) => {
      const next = [...prev];
      const r = { ...next[idx] };
      r[`${field}_input`] = raw;
      next[idx] = r;
      return next;
    });
  };
  const commitNumber = (idx, field) => {
    setRows((prev) => {
      const next = [...prev];
      const r = { ...next[idx] };
      const raw = r[`${field}_input`];
      const parsed = raw === undefined || String(raw).trim() === "" ? 0 : parseFloat(String(raw).replace(/,/g, ""));
      r[field] = Number.isFinite(parsed) ? Number(parsed) : 0;
      delete r[`${field}_input`];

      if (["invoice", "manual"].includes(r.entry_type)) {
        const bal = Number(((r.invoice_total || 0) - (r.total_received || 0)).toFixed(2));
        r.balance_remaining = bal < 0 ? 0 : bal;
      }
      // For payment rows, calculate the remaining balance based on credited_amount
      if (r.entry_type === "payment") {
        // For payment rows, the balance_remaining shows 0 (it's a payment, not an invoice)
        r.balance_remaining = 0;
      }
      next[idx] = r;
      return next;
    });
  };
  const handleField = (idx, field, value) => {
    setRows((prev) => {
      const next = [...prev];
      const r = { ...next[idx], [field]: value };
      if (["invoice", "manual"].includes(r.entry_type) && (field === "invoice_total" || field === "total_received")) {
        const bal = Number(((r.invoice_total || 0) - (r.total_received || 0)).toFixed(2));
        r.balance_remaining = bal < 0 ? 0 : bal;
      }
      // For payment rows, update balance_remaining to 0
      if (r.entry_type === "payment") {
        r.balance_remaining = 0;
      }
      next[idx] = r;
      return next;
    });
  };

  // ---------- add row ----------
  const addPaymentNow = () => {
    if (!can.create) return toast.error("You don't have permission to add payments.");
    if (!customerId) return toast.error("Select a customer first");
    const today = new Date().toISOString().slice(0, 10);
    setRows((prev) => [
      ...prev,
      {
        id: undefined,
        customer_id: customerId,
        entry_type: "payment",
        entry_date: today,
        credited_amount: 0,
        posted_number: "",
        description: "Cash payment received",
        invoice_total: 0,
        total_received: 0,
        balance_remaining: 0,
        is_manual: true,
      },
    ]);
  };
  const addManualNow = () => {
    if (!can.create) return toast.error("You don't have permission to add manual rows.");
    if (!customerId) return toast.error("Select a customer first");
    const today = new Date().toISOString().slice(0, 10);
    setRows((prev) => [
      ...prev,
      {
        id: undefined,
        customer_id: customerId,
        entry_type: "manual",
        entry_date: today,
        posted_number: "",
        invoice_total: 0,
        total_received: 0,
        balance_remaining: 0,
        credited_amount: 0,
        payment_ref: "",
        description: "",
        is_manual: true,
      },
    ]);
  };

  // ---------- bulk save ----------
  const doBulkSave = async () => {
    const news = rows.filter((r) => !r.id);
    const updates = rows.filter(
      (r) => r.id && (r.entry_type === "payment" || r.entry_type === "manual" || r.is_manual)
    );
    if (news.length && !can.create) return toast.error("You don't have permission to create ledger rows.");
    if (updates.length && !can.update) return toast.error("You don't have permission to update ledger rows.");

    try {
      for (const n of news) {
        const payload = {
          customer_id: customerId,
          entry_type: n.entry_type,
          is_manual: !!n.is_manual,
          entry_date: n.entry_date,
          description: n.description,
          posted_number: n.posted_number,
          invoice_total: n.invoice_total || 0,
          total_received: n.total_received || 0,
          credited_amount: n.credited_amount || 0,
          payment_ref: n.payment_ref,
          sale_invoice_id: n.sale_invoice_id || null,
        };
        await axios.post("/api/customer-ledger", payload);
      }
      if (updates.length) {
        await axios.put("/api/customer-ledger/bulk", {
          rows: updates.map((u) => ({
            id: u.id,
            entry_date: u.entry_date,
            description: u.description,
            posted_number: u.posted_number,
            invoice_total: u.invoice_total || 0,
            total_received: u.total_received || 0,
            credited_amount: u.credited_amount || 0,
            payment_ref: u.payment_ref,
          })),
        });
      }
      toast.success("Ledger saved");
      await fetchData();
    } catch (e) {
      toast.error(e?.response?.data?.message || "Save failed");
    }
  };

  // ---------- delete (secure) ----------
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deleteStep, setDeleteStep] = useState(1);
  const [deletingIdx, setDeletingIdx] = useState(null);
  const [password, setPassword] = useState("");
  const [deleting, setDeleting] = useState(false);

  const openDeleteModal = (originalIdx) => {
    if (!can.delete) return toast.error("You don't have permission to delete ledger rows.");
    setDeletingIdx(originalIdx);
    setPassword("");
    setDeleteStep(1);
    setDeleteModalOpen(true);
  };
  const closeDeleteModal = () => {
    if (deleting) return;
    setDeleteModalOpen(false);
    setDeletingIdx(null);
    setDeleteStep(1);
    setPassword("");
  };
  const proceedDeletePassword = () => setDeleteStep(2);

  const confirmAndDelete = async () => {
    if (deletingIdx === null) return;
    if (!can.delete) return toast.error("You don't have permission to delete ledger rows.");
    const r = rows[deletingIdx];
    try {
      setDeleting(true);
      await axios.post("/api/auth/confirm-password", { password });

      if (r.id && !r.is_manual && r.entry_type === "invoice") {
        toast.error("Cannot delete invoice row");
      } else if (r.id) {
        await axios.delete(`/api/customer-ledger/${r.id}`);
        toast.success("Row deleted");
        await fetchData();
      } else {
        setRows((prev) => prev.filter((_, i) => i !== deletingIdx));
        toast.success("Row removed");
      }
      closeDeleteModal();
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        (err?.response?.status === 422 ? "Incorrect password" : "Delete failed");
      toast.error(msg);
    } finally {
      setDeleting(false);
    }
  };

  // ---------- confirm add modals ----------
  const [addModal, setAddModal] = useState({ open: false, type: null }); // 'payment' | 'manual'
  const openAddPayment = () => setAddModal({ open: true, type: "payment" });
  const openAddManual  = () => setAddModal({ open: true, type: "manual" });
  const closeAddModal  = () => setAddModal({ open: false, type: null });
  const confirmAdd = () => {
    if (addModal.type === "payment") addPaymentNow();
    if (addModal.type === "manual")  addManualNow();
    closeAddModal();
  };

  // ---------- confirm save modal ----------
  const [saveModalOpen, setSaveModalOpen] = useState(false);
  const openSaveModal = () => setSaveModalOpen(true);
  const closeSaveModal = () => setSaveModalOpen(false);
  const confirmSave = async () => {
    setSaveModalOpen(false);
    await doBulkSave();
  };

  // ---------- derived running balance ----------
  // Calculate running balance locally based on current rows
  const derivedRows = useMemo(() => {
    // First sort rows by date and id
    const sorted = [...rows].sort((a, b) => {
      const ad = (a.entry_date || "").slice(0, 10);
      const bd = (b.entry_date || "").slice(0, 10);
      if (ad === bd) {
        const ai = a.id ?? Number.MAX_SAFE_INTEGER;
        const bi = b.id ?? Number.MAX_SAFE_INTEGER;
        return ai - bi;
      }
      return ad < bd ? -1 : 1;
    });

    // Calculate running balance
    let runningBalance = 0;
    const withBalance = sorted.map((r, i) => {
      if (r.entry_type === "invoice" || r.entry_type === "manual") {
        // Add the remaining balance (what customer owes)
        runningBalance += Number(r.balance_remaining || 0);
      } else if (r.entry_type === "payment") {
        // Subtract the payment amount
        runningBalance -= Number(r.credited_amount || 0);
      }
      return {
        ...r,
        __i: i,
        running_balance: Number(runningBalance.toFixed(2)),
      };
    });

    return withBalance;
  }, [rows]);

  const newCount = rows.filter((r) => !r.id).length;
  const updCount = rows.filter(
    (r) => r.id && (r.entry_type === "payment" || r.entry_type === "manual" || r.is_manual)
  ).length;

  const handlePrint = (type /* 'a4'|'thermal' optional */) => {
    if (!can.view) return toast.error("You don't have permission to print.");
    if (!customerId) return toast.error("Select a customer first");
    const qs = new URLSearchParams();
    qs.set("customer_id", customerId);
    if (from) qs.set("from", from);
    if (to) qs.set("to", to);
    if (type) qs.set("type", type);
    window.open(`/customer-ledger/print?${qs.toString()}`, "_blank", "noopener");
  };

  // ---------- UI ----------
  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don’t have permission to view customer ledger.</div>;

  return (
    <div className="p-4 space-y-3">
      {/* ===== Professional Header ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.core.gradient} shadow-sm`}>
              <UserIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Customer Ledger</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">
                {customerId ? `Managing ledger for selected customer` : 'Select a customer to view ledger'}
              </p>
            </div>
          </div>

          {/* Action Buttons - Modern card-style layout */}
          <div className="flex items-center gap-2">
            {/* Bulk Actions Dropdown-style buttons */}
            <div className="flex items-center gap-1.5 px-1.5 py-0.5 rounded-lg bg-gray-100/80 dark:bg-slate-700/60 border border-gray-200/60 dark:border-slate-600/40">
              <Guard when={can.update}>
                <button
                  onClick={rebuild}
                  disabled={!customerId}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${customerId ? `${tintSlate} cursor-pointer` : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"}`}
                >
                  <ArrowPathIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Refresh</span>
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.create}>
                <button
                  onClick={openAddPayment}
                  disabled={!customerId}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${customerId ? `${tintBlue} cursor-pointer` : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"}`}
                >
                  <PlusCircleIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Payment</span>
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.create}>
                <button
                  onClick={openAddManual}
                  disabled={!customerId}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${customerId ? `${tintAmber} cursor-pointer` : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"}`}
                >
                  <WrenchScrewdriverIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Manual</span>
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.update}>
                <button
                  onClick={fetchData}
                  disabled={!customerId}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${customerId ? `${tintGreen} cursor-pointer` : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"}`}
                >
                  <ArrowPathIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Load</span>
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.update}>
                <button
                  onClick={openSaveModal}
                  disabled={!customerId || (newCount === 0 && updCount === 0)}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${customerId && (newCount > 0 || updCount > 0) ? `${tintGlass} cursor-pointer` : "bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"}`}
                >
                  <ArrowDownOnSquareIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Save</span>
                  {(newCount > 0 || updCount > 0) && customerId && (
                    <span className="ml-0.5 px-1 py-0.5 rounded bg-white/20 text-[10px]">
                      {newCount + updCount}
                    </span>
                  )}
                </button>
              </Guard>
            </div>

            {/* Primary Print Button */}
            <button
              onClick={() => handlePrint()}
              disabled={!customerId}
              className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${customerId ? `${tintIndigo}` : "bg-gray-200/50 dark:bg-slate-700/50 text-gray-400 dark:text-gray-500 cursor-not-allowed"}`}
            >
              <PrinterIcon className="w-4 h-4" />
              <span className="hidden sm:inline">Print</span>
            </button>
          </div>
        </div>

        {/* Search Bar */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="flex-1">
            <CustomerSearchInput
              value={customerId}
              onChange={setCustomerId}
              customers={customers}
              autoFocus
              menuPortalTarget={typeof document !== "undefined" ? document.body : null}
              menuPosition="fixed"
              styles={{
                menuPortal: base => ({ ...base, zIndex: 9999 }),
                control: (base) => ({
                  ...base,
                  minHeight: '38px',
                  borderRadius: '0.5rem',
                }),
              }}
            />
          </div>
        </div>
      </div>

      {/* ===== Summary Compact ===== */}
      {customerId && (
        <div className="grid grid-cols-4 gap-2">
          <Stat isDark={isDark} label="Total Bills" value={fmt(summary.total_invoiced)} />
          <Stat isDark={isDark} label="Advance" value={fmt(summary.received_on_invoice)} />
          <Stat isDark={isDark} label="Payments" value={fmt(summary.payments_credited)} />
          <Stat isDark={isDark} label="Total Due" value={fmt(summary.net_balance)} />
        </div>
      )}

      {/* ===== Table Compact ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        {/* Table Header */}
        <div className="flex items-center justify-between px-4 py-2.5 bg-gray-50 dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div className={`p-1 rounded ${SECTION_CONFIG.core.bgDark}`}>
              <Squares2X2Icon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
            </div>
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Ledger Entries</span>
          </div>
          <span className="text-xs text-gray-400">{derivedRows.length} entries</span>
        </div>

        <div className={`max-h-[calc(100vh-280px)] overflow-auto ${isDark ? "bg-slate-800" : "bg-white"}`}>
          <table className="w-full text-xs">
            <thead className={`sticky top-0 z-10 border-b ${isDark ? "bg-slate-700" : "bg-gray-100"}`}>
              <tr className={`text-left ${isDark ? "text-slate-200" : "text-gray-700"}`}>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-24">Date</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-14">Type</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-20">Ref</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-18">Bill</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-18">Paid Now</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-16">Total Paid</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-18">Balance</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-right w-18">Running</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider w-24">Notes</th>
                <th className="px-2 py-2 font-semibold text-gray-600 dark:text-gray-300 text-xs uppercase tracking-wider text-center w-14">Action</th>
              </tr>
            </thead>

            <tbody>
              {!customerId && (
                <tr>
                  <td colSpan={11} className={`px-2 py-12 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                    <div className="flex flex-col items-center gap-2">
                      <UserIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm">Select a customer to view ledger</p>
                    </div>
                  </td>
                </tr>
              )}

              {customerId && derivedRows.map((r) => {
                const isInvoice = r.entry_type === "invoice";
                const isPayment = r.entry_type === "payment";
                return (
                  <tr key={r.id ?? `new-${r.__i}`} className={`border-b ${isDark ? "border-slate-600/30 hover:bg-slate-700/50" : "border-gray-100 hover:bg-blue-50"} odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40`}>
                    <td className="px-2 py-2">
                      <input
                        type="date"
                        value={(r.entry_date || "").slice(0,10)}
                        onChange={(e) => handleField(r.__i, "entry_date", e.target.value)}
                        className={`w-full text-xs border rounded px-1 py-1 ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-300 bg-white text-gray-800"}`}
                      />
                    </td>

                    <td className="px-2 py-2">
                      <span className={`inline-flex items-center px-2 py-0.5 rounded text-[10px] font-semibold ${
                        isInvoice 
                          ? "bg-gradient-to-br from-blue-400 to-blue-500 text-white shadow-lg shadow-blue-500/25" 
                          : isPayment
                          ? "bg-gradient-to-br from-emerald-400 to-emerald-500 text-white shadow-lg shadow-emerald-500/25"
                          : "bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300"
                      }`}>
                        {r.entry_type?.toUpperCase()}
                      </span>
                    </td>

                    <td className="px-2 py-1">
                      <input
                        type="text"
                        value={r.posted_number ?? ""}
                        onChange={(e) => handleField(r.__i, "posted_number", e.target.value)}
                        disabled={isInvoice}
                        placeholder={isInvoice ? "-" : "Ref"}
                        className={`w-full text-xs border rounded px-1 py-1 ${isDark ? "border-slate-600 bg-slate-700 text-slate-200 disabled:bg-slate-800" : "border-gray-300 bg-white text-gray-800 disabled:bg-gray-100"}`}
                      />
                    </td>

                    <td className="px-2 py-1 text-right">
                      {isInvoice || r.entry_type === "manual" ? (
                        <input
                          type="text" inputMode="decimal"
                          value={getInput(r, "invoice_total")}
                          onChange={(e) => setInput(r.__i, "invoice_total", e.target.value)}
                          onBlur={() => commitNumber(r.__i, "invoice_total")}
                          disabled={isInvoice}
                          className={`w-full text-xs text-right border rounded px-1 py-1 ${isDark ? "border-slate-600 bg-slate-700 text-slate-200 disabled:bg-slate-800" : "border-gray-300 bg-white text-gray-800 disabled:bg-gray-100"}`}
                        />
                      ) : (
                        <span className={isDark ? "text-slate-500" : "text-gray-400"}>—</span>
                      )}
                    </td>

                    <td className="px-2 py-1 text-right">
                      {isPayment ? (
                        <input
                          type="text" inputMode="decimal"
                          value={getInput(r, "credited_amount")}
                          onChange={(e) => setInput(r.__i, "credited_amount", e.target.value)}
                          onBlur={() => commitNumber(r.__i, "credited_amount")}
                          placeholder="0.00"
                          className={`w-full text-xs text-right border rounded px-1 py-1 font-medium ${isDark ? "border-slate-600 bg-slate-700 text-emerald-400" : "border-gray-300 bg-white text-emerald-600"}`}
                        />
                      ) : (
                        <span className={isDark ? "text-slate-500" : "text-gray-400"}>—</span>
                      )}
                    </td>

                    <td className="px-2 py-1 text-right">
                      {isInvoice || r.entry_type === "manual" ? (
                        <span className={`font-medium ${isDark ? "text-slate-300" : "text-gray-700"}`}>{fmt(r.total_received || 0)}</span>
                      ) : (
                        <span className={isDark ? "text-slate-500" : "text-gray-400"}>—</span>
                      )}
                    </td>

                    <td className="px-2 py-1 text-right">
                      <span className={`font-bold ${(r.balance_remaining || 0) > 0 ? 'text-red-500' : 'text-green-500'}`}>
                        {fmt(r.balance_remaining ?? 0)}
                      </span>
                    </td>

                    <td className="px-2 py-1 text-right">
                      <span className={`font-bold ${(r.running_balance || 0) > 0 ? 'text-blue-500' : 'text-green-500'}`}>
                        {fmt(r.running_balance ?? 0)}
                      </span>
                    </td>

                    <td className="px-2 py-1">
                      <input
                        type="text"
                        value={r.description ?? ""}
                        onChange={(e) => handleField(r.__i, "description", e.target.value)}
                        placeholder="..."
                        className={`w-full text-xs border rounded px-1 py-1 ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-300 bg-white text-gray-800"}`}
                      />
                    </td>

                    <td className="px-2 py-2 text-center">
                      <button
                        onClick={() => openDeleteModal(r.__i)}
                        className={`group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold transition-all duration-200 ${tintRed}`}
                      >
                        <XMarkIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                        <span>X</span>
                      </button>
                    </td>
                  </tr>
                );
              })}

              {customerId && !rows.length && (
                <tr>
                  <td colSpan={11} className={`px-2 py-12 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                    <div className="flex flex-col items-center gap-2">
                      <UserIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm">No entries. Click <b className={isDark ? "text-slate-300" : "text-gray-700"}>Load</b> or add a payment.</p>
                    </div>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* ===== Add Row modal ===== */}
      {addModal.open && (
        <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={(e)=>{ if(e.target===e.currentTarget) closeAddModal(); }}>
          <div className="absolute inset-0 bg-black/40 backdrop-blur-sm" />
          <div className="relative w-full max-w-sm">
            <GlassCard>
              <GlassSectionHeader
                title={<span className="font-semibold text-lg">{addModal.type === "payment" ? "Add Payment row?" : "Add Manual row?"}</span>}
                right={<button className={`p-1.5 rounded-lg ${tintIconBtn}`} onClick={closeAddModal}><XMarkIcon className="w-5 h-5" /></button>}
              />
              <div className="px-4 py-4">
                <p className={`text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                  A new <b>{addModal.type}</b> row will be appended for the selected customer.
                </p>
                <div className="mt-4 flex justify-end gap-2">
                  <button className={`min-w-[100px] rounded-lg px-4 py-2 text-sm font-medium ${tintOutline}`} onClick={closeAddModal}>Cancel</button>
                  <button className={`min-w-[120px] rounded-lg px-4 py-2 text-sm font-semibold ${tintBlue}`} onClick={confirmAdd}>Add row</button>
                </div>
              </div>
            </GlassCard>
          </div>
        </div>
      )}

      {/* ===== Save confirm modal ===== */}
      {saveModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={(e)=>{ if(e.target===e.currentTarget) closeSaveModal(); }}>
          <div className="absolute inset-0 bg-black/40" />
          <div className="relative w-full max-w-md">
            <GlassCard>
              <GlassSectionHeader
                title={<span className="inline-flex items-center gap-2">
                  <ArrowDownOnSquareIcon className="w-5 h-5 text-emerald-600" />
                  <span>Save changes?</span>
                </span>}
                right={<button className={`p-1.5 rounded-lg ${tintIconBtn}`} onClick={closeSaveModal}><XMarkIcon className="w-5 h-5" /></button>}
              />
              <div className="px-4 py-4">
                <p className="text-sm text-gray-700">
                  You’re about to save <b>{newCount}</b> new {newCount === 1 ? "row" : "rows"} and update{" "}
                  <b>{updCount}</b> existing {updCount === 1 ? "row" : "rows"} for this customer.
                </p>
                <div className="mt-4 flex justify-end gap-2">
                  <button className={`min-w-[100px] rounded-lg px-4 py-2 text-sm font-medium ${tintOutline}`} onClick={closeSaveModal}>Cancel</button>
                  <button className={`min-w-[120px] rounded-lg px-4 py-2 text-sm font-semibold ${tintGreen}`} onClick={confirmSave}>Yes, Save</button>
                </div>
              </div>
            </GlassCard>
          </div>
        </div>
      )}

      {/* ===== Delete (2-step) modal ===== */}
      {deleteModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={(e)=>{ if(e.target===e.currentTarget) closeDeleteModal(); }}>
          <div className="absolute inset-0 bg-black/40" />
          <div className="relative w-full max-w-md">
            <GlassCard>
              <GlassSectionHeader
                title={<span className="inline-flex items-center gap-2">
                  <ShieldExclamationIcon className="w-5 h-5 text-rose-600" />
                  <span>Delete ledger row</span>
                </span>}
                right={<button className={`p-1.5 rounded-lg ${tintIconBtn}`} onClick={closeDeleteModal}><XMarkIcon className="w-5 h-5" /></button>}
              />
              <div className="px-4 py-4 space-y-4">
                {deleteStep === 1 ? (
                  <>
                    <p className={`text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                      Are you sure you want to delete this row? This action cannot be undone.
                    </p>
                    <div className="flex justify-end gap-2">
                      <button className={`min-w-[100px] rounded-lg px-4 py-2 text-sm font-medium ${tintOutline}`} onClick={closeDeleteModal}>Cancel</button>
                      <button className={`min-w-[140px] rounded-lg px-4 py-2 text-sm font-semibold ${tintRed}`} onClick={proceedDeletePassword}>Yes, continue</button>
                    </div>
                  </>
                ) : (
                  <>
                    <p className={`text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                      For security, please re-enter your password to delete this row.
                    </p>
                    <GlassInput
                      type="password"
                      autoFocus
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="Your password"
                      onKeyDown={(e) => {
                        if (e.key === "Enter") confirmAndDelete();
                        if (e.key === "Escape") closeDeleteModal();
                      }}
                      className="w-full"
                    />
                    <div className="flex justify-between">
                      <button className={`min-w-[90px] rounded-lg px-4 py-2 text-sm font-medium ${tintOutline}`} onClick={() => setDeleteStep(1)} disabled={deleting}>
                        ← Back
                      </button>
                      <div className="flex gap-2">
                        <button className={`min-w-[100px] rounded-lg px-4 py-2 text-sm font-medium ${tintOutline}`} onClick={closeDeleteModal} disabled={deleting}>
                          Cancel
                        </button>
                        <button
                          className={`min-w-[170px] rounded-lg px-4 py-2 text-sm font-semibold ${tintRed} disabled:opacity-60`}
                          onClick={confirmAndDelete}
                          disabled={deleting || password.trim() === ""}
                        >
                          {deleting ? "Deleting…" : "Confirm & Delete"}
                        </button>
                      </div>
                    </div>
                  </>
                )}
              </div>
            </GlassCard>
          </div>
        </div>
      )}

      {/* print + portal helpers */}
      <style>{`
        @media print {
          input, button, select, [role="button"] { display: none !important; }
          table { font-size: 11px; }
          thead { position: sticky; top: 0; }
        }
      `}</style>
    </div>
  );
}

function Stat({ isDark, label, value }) {
  return (
    <div className={`${isDark ? "bg-slate-800/60 ring-slate-700" : "bg-white/60 ring-gray-200/60"} px-2 py-1.5 rounded shadow-sm`}>
      <div className={`text-[10px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>{label}</div>
      <div className={`text-sm font-semibold ${isDark ? "text-slate-200" : "text-gray-800"}`}>{value}</div>
    </div>
  );
}
