// resources/js/pages/SupplierLedgerPage.jsx
import React, { useEffect, useMemo, useState, useRef } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import SupplierSearchInput from "../../components/SupplierSearchInput.jsx";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";

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
} from "@heroicons/react/24/solid";

// 🧊 glass primitives
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  return getContrastText(primaryHoverColor || primaryColor);
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

/* =========================
   Supplier Ledger Page (Modernized)
   ========================= */
export default function SupplierLedgerPage() {
  const [suppliers, setSuppliers] = useState([]);
  const [supplierId, setSupplierId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [rows, setRows] = useState([]);
  const [summary, setSummary] = useState({
    total_invoiced: 0,
    paid_on_invoice: 0,
    payments_debited: 0,
    net_balance: 0,
  });

  // perms
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("supplier-ledger") : {
        view:false, create:false, update:false, delete:false, import:false, export:false
      }),
    [canFor]
  );

  // theme
  const { isDark, theme } = useTheme();

  // 🎨 Modern button palette (will use dynamic theme colors)
  const tintPrimary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintSecondary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintTertiary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintGlass = useMemo(() => `
    bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintOutline = useMemo(() => `
    bg-transparent ring-1 ring-gray-300 dark:ring-slate-600
    hover:bg-gray-100 dark:hover:bg-slate-700/50 hover:shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintIconBtn = useMemo(() => `
    bg-white/60 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-md hover:scale-[1.05] active:scale-[0.95] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintDisabled = useMemo(() => `
    bg-gray-200/50 dark:bg-slate-600/50 text-gray-400 dark:text-gray-500 cursor-not-allowed
  `.trim().replace(/\s+/g, ' '), []);

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
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
        tertiaryLight: '#cffafe',
        danger: '#ef4444',
        dangerHover: '#dc2626',
        dangerLight: '#fee2e2',
        emerald: '#10b981',
        emeraldHover: '#059669',
        emeraldLight: '#d1fae5',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
      tertiary: theme.tertiary_color || '#06b6d4',
      tertiaryHover: theme.tertiary_hover || '#0891b2',
      tertiaryLight: theme.tertiary_light || '#cffafe',
      danger: theme.danger_color || '#ef4444',
      dangerHover: '#dc2626',
      dangerLight: '#fee2e2',
      emerald: theme.success_color || '#10b981',
      emeraldHover: '#059669',
      emeraldLight: '#d1fae5',
    };
  }, [theme]);

  // Calculate text colors based on background brightness
  const primaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.primary, themeColors.primaryHover), 
    [themeColors.primary, themeColors.primaryHover]
  );
  
  const secondaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.secondary, themeColors.secondaryHover), 
    [themeColors.secondary, themeColors.secondaryHover]
  );
  
  const tertiaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.tertiary, themeColors.tertiaryHover), 
    [themeColors.tertiary, themeColors.tertiaryHover]
  );
  
  const dangerTextColor = useMemo(() => 
    getButtonTextColor(themeColors.danger, themeColors.dangerHover), 
    [themeColors.danger, themeColors.dangerHover]
  );

  const emeraldTextColor = useMemo(() => 
    getButtonTextColor(themeColors.emerald, themeColors.emeraldHover), 
    [themeColors.emerald, themeColors.emeraldHover]
  );

  // Get section styles
  const coreStyles = useMemo(() => getSectionStyles(themeColors, 'primary'), [themeColors]);

  // utils
  const fmt = (v) => {
    if (v === null || v === undefined || v === "") return "0";
    const n = Number(v);
    if (!Number.isFinite(n)) return "0";
    return new Intl.NumberFormat(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(n);
  };

  // hotkeys
  useEffect(() => {
    const onKeyS = (e) => {
      if (e.altKey && (e.key || "").toLowerCase() === "s") {
        e.preventDefault();
        if (!can.create && !can.update) return;
        openSaveModal();
      }
    };
    const onKeyP = (e) => {
      if (e.altKey && (e.key || "").toLowerCase() === "p") {
        e.preventDefault();
        if (!can.view) return;
        handlePrint();
      }
    };
    window.addEventListener("keydown", onKeyS);
    window.addEventListener("keydown", onKeyP);
    return () => {
      window.removeEventListener("keydown", onKeyS);
      window.removeEventListener("keydown", onKeyP);
    };
  }, [can.create, can.update, can.view]);

  useEffect(() => {
    if (permsLoading || !can.view) return;
    (async () => {
      try {
        const { data } = await axios.get("/api/suppliers", { params: { limit: 500 } });
        const list = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
        setSuppliers(list);
      } catch {
        toast.error("Failed to load suppliers");
        setSuppliers([]);
      }
    })();
  }, [permsLoading, can.view]);

  const fetchData = async () => {
    if (!can.view) return toast.error("You don't have permission to view supplier ledger.");
    if (!supplierId) return toast.error("Select a supplier first");
    try {
      const { data } = await axios.get("/api/supplier-ledger", { params: { supplier_id: supplierId, from, to } });
      const clean = (data.data || []).map(r => {
        const c = { ...r };
        Object.keys(c).forEach(k => { if (k.endsWith("_input")) delete c[k]; });
        return c;
      });
      setRows(clean);
      setSummary(
        data.summary || {
          total_invoiced: 0,
          paid_on_invoice: 0,
          payments_debited: 0,
          net_balance: 0,
        }
      );
    } catch (e) {
      toast.error(e?.response?.data?.message || "Failed to load ledger");
    }
  };

  const rebuild = async () => {
    if (!can.update) return toast.error("You don't have permission to rebuild.");
    if (!supplierId) return toast.error("Select a supplier first");
    try {
      await axios.post("/api/supplier-ledger/rebuild", { supplier_id: supplierId });
      toast.success("Rebuilt from invoices");
      await fetchData();
    } catch (e) {
      toast.error(e?.response?.data?.message || "Rebuild failed");
    }
  };

  // number editing helpers
  const getInput = (row, field) => (row[`${field}_input`] !== undefined ? row[`${field}_input`] : (row[field] ?? "") + "");
  const setInput = (idx, field, raw) => {
    setRows(prev => {
      const next = [...prev];
      const r = { ...next[idx] };
      r[`${field}_input`] = raw;
      next[idx] = r;
      return next;
    });
  };
  const commitNumber = (idx, field) => {
    setRows(prev => {
      const next = [...prev];
      const r = { ...next[idx] };
      const raw = r[`${field}_input`];
      const parsed = raw === undefined || String(raw).trim() === "" ? 0 : parseFloat(String(raw).replace(/,/g, ""));
      r[field] = Number.isFinite(parsed) ? Number(parsed) : 0;
      delete r[`${field}_input`];

      if (["invoice", "manual"].includes(r.entry_type)) {
        const credit = Number(((r.invoice_total || 0) - (r.total_paid || 0)).toFixed(2));
        r.credit_remaining = credit < 0 ? 0 : credit;
      }
      // For payment rows, calculate the remaining balance based on debited_amount
      if (r.entry_type === "payment") {
        r.credit_remaining = 0;
      }
      next[idx] = r;
      return next;
    });
  };
  const handleField = (idx, field, value) => {
    setRows(prev => {
      const next = [...prev];
      const r = { ...next[idx], [field]: value };
      if (["invoice", "manual"].includes(r.entry_type) && (field === "invoice_total" || field === "total_paid")) {
        const credit = Number(((r.invoice_total || 0) - (r.total_paid || 0)).toFixed(2));
        r.credit_remaining = credit < 0 ? 0 : credit;
      }
      // For payment rows, update credit_remaining to 0
      if (r.entry_type === "payment") {
        r.credit_remaining = 0;
      }
      next[idx] = r;
      return next;
    });
  };

  // add row
  const addPaymentNow = () => {
    if (!can.create) return toast.error("You don't have permission to add payments.");
    if (!supplierId) return toast.error("Select a supplier first");
    const today = new Date().toISOString().slice(0, 10);
    setRows(prev => ([
      ...prev,
      {
        id: undefined,
        supplier_id: supplierId,
        entry_type: "payment",
        entry_date: today,
        debited_amount: 0,
        payment_ref: "",
        description: "Payment made",
        invoice_total: 0,
        total_paid: 0,
        credit_remaining: 0,
        is_manual: true,
      },
    ]));
  };
  const addManualNow = () => {
    if (!can.create) return toast.error("You don't have permission to add manual rows.");
    if (!supplierId) return toast.error("Select a supplier first");
    const today = new Date().toISOString().slice(0, 10);
    setRows(prev => ([
      ...prev,
      {
        id: undefined,
        supplier_id: supplierId,
        entry_type: "manual",
        entry_date: today,
        posted_number: "",
        invoice_total: 0,
        total_paid: 0,
        credit_remaining: 0,
        debited_amount: 0,
        payment_ref: "",
        description: "",
        is_manual: true,
      },
    ]));
  };

  // bulk save
  const doBulkSave = async () => {
    const news = rows.filter(r => !r.id);
    const updates = rows.filter(r => r.id && (r.entry_type === "payment" || r.entry_type === "manual" || r.is_manual));
    if (news.length && !can.create) return toast.error("You don't have permission to create ledger rows.");
    if (updates.length && !can.update) return toast.error("You don't have permission to update ledger rows.");

    try {
      for (const n of news) {
        const payload = {
          supplier_id: supplierId,
          entry_type: n.entry_type,
          is_manual: !!n.is_manual,
          entry_date: n.entry_date,
          description: n.description,
          posted_number: n.posted_number,
          invoice_total: n.invoice_total || 0,
          total_paid: n.total_paid || 0,
          debited_amount: n.debited_amount || 0,
          payment_ref: n.payment_ref,
          purchase_invoice_id: n.purchase_invoice_id || null,
        };
        await axios.post("/api/supplier-ledger", payload);
      }
      if (updates.length) {
        await axios.put("/api/supplier-ledger/bulk", {
          rows: updates.map(u => ({
            id: u.id,
            entry_date: u.entry_date,
            description: u.description,
            posted_number: u.posted_number,
            invoice_total: u.invoice_total || 0,
            total_paid: u.total_paid || 0,
            debited_amount: u.debited_amount || 0,
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

  // delete (secure)
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
        await axios.delete(`/api/supplier-ledger/${r.id}`);
        toast.success("Row deleted");
        await fetchData();
      } else {
        setRows(prev => prev.filter((_, i) => i !== deletingIdx));
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

  // add/ save modals
  const [addModal, setAddModal] = useState({ open: false, type: null }); // 'payment' | 'manual'
  const openAddPayment = () => setAddModal({ open: true, type: "payment" });
  const openAddManual  = () => setAddModal({ open: true, type: "manual" });
  const closeAddModal  = () => setAddModal({ open: false, type: null });
  const confirmAdd = () => {
    if (addModal.type === "payment") addPaymentNow();
    if (addModal.type === "manual")  addManualNow();
    closeAddModal();
  };

  const [saveModalOpen, setSaveModalOpen] = useState(false);
  const openSaveModal = () => setSaveModalOpen(true);
  const closeSaveModal = () => setSaveModalOpen(false);
  const confirmSave = async () => {
    setSaveModalOpen(false);
    await doBulkSave();
  };

  // derived running balance (same as CustomerLedger)
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
        // Add the remaining balance (what we owe)
        runningBalance += Number(r.credit_remaining || 0);
      } else if (r.entry_type === "payment") {
        // Subtract the payment amount
        runningBalance -= Number(r.debited_amount || 0);
      }
      return {
        ...r,
        __i: i,
        running_balance: Number(runningBalance.toFixed(2)),
      };
    });

    return withBalance;
  }, [rows]);

  const newCount = rows.filter(r => !r.id).length;
  const updCount = rows.filter(r => r.id && (r.entry_type === "payment" || r.entry_type === "manual" || r.is_manual)).length;

  const handlePrint = (type /* 'a4'|'thermal' optional */) => {
    if (!can.view) return toast.error("You don't have permission to print.");
    if (!supplierId) return toast.error("Select a supplier first");
    const qs = new URLSearchParams();
    qs.set("supplier_id", supplierId);
    if (from) qs.set("from", from);
    if (to) qs.set("to", to);
    if (type) qs.set("type", type);
    window.open(`/supplier-ledger/print?${qs.toString()}`, "_blank", "noopener");
  };

  // UI gates
  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view) return <div className="p-6 text-sm text-gray-700">You don't have permission to view supplier ledger.</div>;

  return (
    <div className="p-4 space-y-3">
      {/* ===== Professional Header ===== */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        {/* Header Top */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          {/* Title */}
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})` }}
            >
              <CubeIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Supplier Ledger</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">
                {supplierId ? `Managing ledger for selected supplier` : 'Select a supplier to view ledger'}
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
                  disabled={!supplierId}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${supplierId ? `${tintTertiary} cursor-pointer` : tintDisabled}`}
                  style={supplierId ? {
                    background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})`,
                    color: tertiaryTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40`
                  } : {}}
                >
                  <ArrowPathIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Refresh</span>
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.create}>
                <button
                  onClick={openAddPayment}
                  disabled={!supplierId}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${supplierId ? `${tintPrimary} cursor-pointer` : tintDisabled}`}
                  style={supplierId ? {
                    background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                    color: primaryTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                  } : {}}
                >
                  <PlusCircleIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Payment</span>
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.create}>
                <button
                  onClick={openAddManual}
                  disabled={!supplierId}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${supplierId ? `${tintSecondary} cursor-pointer` : tintDisabled}`}
                  style={supplierId ? {
                    background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                    color: secondaryTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`
                  } : {}}
                >
                  <WrenchScrewdriverIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Manual</span>
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.update}>
                <button
                  onClick={fetchData}
                  disabled={!supplierId}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${supplierId ? `${tintTertiary} cursor-pointer` : tintDisabled}`}
                  style={supplierId ? {
                    background: `linear-gradient(to bottom right, ${themeColors.emerald}, ${themeColors.emeraldHover})`,
                    color: emeraldTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.emerald}40`
                  } : {}}
                >
                  <ArrowPathIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Load</span>
                </button>
              </Guard>

              <div className="w-px h-5 bg-gray-300/60 dark:bg-slate-600/60" />

              <Guard when={can.update}>
                <button
                  onClick={openSaveModal}
                  disabled={!supplierId || (newCount === 0 && updCount === 0)}
                  className={`inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-200 ${supplierId && (newCount > 0 || updCount > 0) ? `${tintGlass} cursor-pointer` : tintDisabled}`}
                  style={supplierId && (newCount > 0 || updCount > 0) ? {
                    background: `linear-gradient(to bottom right, ${themeColors.tertiary}, ${themeColors.tertiaryHover})`,
                    color: tertiaryTextColor,
                    boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40`
                  } : {}}
                >
                  <ArrowDownOnSquareIcon className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Save</span>
                  {(newCount > 0 || updCount > 0) && supplierId && (
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
              disabled={!supplierId}
              className={`inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold ${supplierId ? tintPrimary : tintDisabled}`}
              style={supplierId ? {
                background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                color: secondaryTextColor,
                boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`
              } : {}}
            >
              <PrinterIcon className="w-4 h-4" />
              <span className="hidden sm:inline">Print</span>
            </button>
          </div>
        </div>

        {/* Search Bar */}
        <div className="px-4 py-3 bg-gray-50/50 dark:bg-slate-800/50">
          <div className="flex-1">
            <SupplierSearchInput
              value={supplierId}
              onChange={setSupplierId}
              suppliers={suppliers}
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
      {supplierId && (
        <div className="grid grid-cols-4 gap-2">
          <Stat isDark={isDark} label="Total Bills" value={fmt(summary.total_invoiced)} />
          <Stat isDark={isDark} label="Advance" value={fmt(summary.paid_on_invoice)} />
          <Stat isDark={isDark} label="Payments" value={fmt(summary.payments_debited)} />
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
          <table className="w-full text-sm">
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
              {!supplierId && (
                <tr>
                  <td colSpan={11} className={`px-2 py-12 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                    <div className="flex flex-col items-center gap-2">
                      <CubeIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm">Select a supplier to view ledger</p>
                    </div>
                  </td>
                </tr>
              )}

              {supplierId && derivedRows.map((r) => {
                const isInvoice = r.entry_type === "invoice";
                const isPayment = r.entry_type === "payment";
                return (
                  <tr key={r.id ?? `new-${r.__i}`} className={`border-b ${isDark ? "border-slate-600/30 hover:bg-slate-700/50" : "border-gray-100 hover:bg-blue-50"} odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/40 dark:even:bg-slate-800/40`}>
                    <td className="px-2 py-2">
                      <input
                        type="date"
                        value={(r.entry_date || "").slice(0,10)}
                        onChange={(e) => handleField(r.__i, "entry_date", e.target.value)}
                        className={`w-full text-xs border rounded px-2 py-1.5 ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-300 bg-white text-gray-800"}`}
                      />
                    </td>

                    <td className="px-2 py-2">
                      <span 
                        className={`inline-flex items-center px-2 py-0.5 rounded text-[10px] font-semibold transition-all duration-200`}
                        style={{
                          background: `linear-gradient(to bottom right, ${r.entry_type === 'invoice' ? themeColors.primary : r.entry_type === 'payment' ? themeColors.emerald : themeColors.tertiary}, ${r.entry_type === 'invoice' ? themeColors.primaryHover : r.entry_type === 'payment' ? themeColors.emeraldHover : themeColors.tertiaryHover})`,
                          color: 'white',
                          boxShadow: `0 4px 12px 0 ${r.entry_type === 'invoice' ? themeColors.primary : r.entry_type === 'payment' ? themeColors.emerald : themeColors.tertiary}40`
                        }}
                      >
                        {r.entry_type?.toUpperCase()}
                      </span>
                    </td>

                    <td className="px-2 py-2">
                      <input
                        type="text"
                        value={r.posted_number ?? ""}
                        onChange={(e) => handleField(r.__i, "posted_number", e.target.value)}
                        disabled={isInvoice}
                        placeholder={isInvoice ? "-" : "Ref"}
                        className={`w-full text-xs border rounded px-2 py-1.5 ${isDark ? "border-slate-600 bg-slate-700 text-slate-200 disabled:bg-slate-800" : "border-gray-300 bg-white text-gray-800 disabled:bg-gray-100"}`}
                      />
                    </td>

                    <td className="px-2 py-2 text-right">
                      {isInvoice || r.entry_type === "manual" ? (
                        <input
                          type="text" inputMode="decimal"
                          value={getInput(r, "invoice_total")}
                          onChange={(e) => setInput(r.__i, "invoice_total", e.target.value)}
                          onBlur={() => commitNumber(r.__i, "invoice_total")}
                          disabled={isInvoice}
                          className={`w-full text-xs text-right border rounded px-2 py-1.5 font-medium ${isDark ? "border-slate-600 bg-slate-700 text-slate-200 disabled:bg-slate-800" : "border-gray-300 bg-white text-gray-800 disabled:bg-gray-100"}`}
                        />
                      ) : (
                        <span className={isDark ? "text-slate-500" : "text-gray-400"}>—</span>
                      )}
                    </td>

                    <td className="px-2 py-2 text-right">
                      {isPayment ? (
                        <input
                          type="text" inputMode="decimal"
                          value={getInput(r, "debited_amount")}
                          onChange={(e) => setInput(r.__i, "debited_amount", e.target.value)}
                          onBlur={() => commitNumber(r.__i, "debited_amount")}
                          placeholder="0.00"
                          className={`w-full text-xs text-right border rounded px-2 py-1.5 font-medium ${isDark ? "border-slate-600 bg-slate-700 text-emerald-400" : "border-gray-300 bg-white text-emerald-600"}`}
                        />
                      ) : (
                        <span className={isDark ? "text-slate-500" : "text-gray-400"}>—</span>
                      )}
                    </td>

                    <td className="px-2 py-2 text-right">
                      {isInvoice || r.entry_type === "manual" ? (
                        <span className={`font-medium ${isDark ? "text-slate-300" : "text-gray-700"}`}>{fmt(r.total_paid || 0)}</span>
                      ) : (
                        <span className={isDark ? "text-slate-500" : "text-gray-400"}>—</span>
                      )}
                    </td>

                    <td className="px-2 py-2 text-right">
                      <span className={`font-bold ${(r.credit_remaining || 0) > 0 ? 'text-red-500' : 'text-green-500'}`}>
                        {fmt(r.credit_remaining ?? 0)}
                      </span>
                    </td>

                    <td className="px-2 py-2 text-right">
                      <span className={`font-bold ${(r.running_balance || 0) > 0 ? 'text-blue-500' : 'text-green-500'}`}>
                        {fmt(r.running_balance ?? 0)}
                      </span>
                    </td>

                    <td className="px-2 py-2">
                      <input
                        type="text"
                        value={r.description ?? ""}
                        onChange={(e) => handleField(r.__i, "description", e.target.value)}
                        placeholder="..."
                        className={`w-full text-xs border rounded px-2 py-1.5 ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-300 bg-white text-gray-800"}`}
                      />
                    </td>

                    <td className="px-2 py-2 text-center">
                      <button
                        onClick={() => openDeleteModal(r.__i)}
                        className="group inline-flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-semibold transition-all duration-200"
                        style={{
                          background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
                          color: dangerTextColor,
                          boxShadow: `0 4px 12px 0 ${themeColors.danger}40`
                        }}
                      >
                        <XMarkIcon className="w-3.5 h-3.5 transition-transform group-hover:scale-110" />
                        <span>X</span>
                      </button>
                    </td>
                  </tr>
                );
              })}

              {supplierId && !rows.length && (
                <tr>
                  <td colSpan={11} className={`px-2 py-12 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                    <div className="flex flex-col items-center gap-2">
                      <CubeIcon className="w-8 h-8 text-gray-400" />
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
                  A new <b>{addModal.type}</b> row will be appended for the selected supplier.
                </p>
                <div className="mt-4 flex justify-end gap-2">
                  <button className={`min-w-[100px] rounded-lg px-4 py-2 text-sm font-medium ${tintOutline}`} onClick={closeAddModal}>Cancel</button>
                  <button 
                    className={`min-w-[120px] rounded-lg px-4 py-2 text-sm font-semibold transition-all duration-200`}
                    style={{
                      background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                      color: primaryTextColor,
                      boxShadow: `0 4px 14px 0 ${themeColors.primary}40`
                    }}
                    onClick={confirmAdd}
                  >
                    Add row
                  </button>
                </div>
              </div>
            </GlassCard>
          </div>
        </div>
      )}

      {/* ===== Save confirm modal ===== */}
      {saveModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={(e)=>{ if(e.target===e.currentTarget) closeSaveModal(); }}>
          <div className="absolute inset-0 bg-black/40 backdrop-blur-sm" />
          <div className="relative w-full max-w-md">
            <GlassCard>
              <GlassSectionHeader
                title={<span className="inline-flex items-center gap-2 text-lg">
                  <ArrowDownOnSquareIcon 
                    className="w-5 h-5" 
                    style={{ color: themeColors.emerald }}
                  />
                  <span>Save changes?</span>
                </span>}
                right={<button className={`p-1.5 rounded-lg ${tintIconBtn}`} onClick={closeSaveModal}><XMarkIcon className="w-5 h-5" /></button>}
              />
              <div className="px-4 py-4">
                <p className={`text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                  You're about to save <b>{newCount}</b> new {newCount === 1 ? "row" : "rows"} and update{" "}
                  <b>{updCount}</b> existing {updCount === 1 ? "row" : "rows"} for this supplier.
                </p>
                <div className="mt-4 flex justify-end gap-2">
                  <button className={`min-w-[100px] rounded-lg px-4 py-2 text-sm font-medium ${tintOutline}`} onClick={closeSaveModal}>Cancel</button>
                  <button 
                    className={`min-w-[120px] rounded-lg px-4 py-2 text-sm font-semibold transition-all duration-200`}
                    style={{
                      background: `linear-gradient(to bottom right, ${themeColors.emerald}, ${themeColors.emeraldHover})`,
                      color: emeraldTextColor,
                      boxShadow: `0 4px 14px 0 ${themeColors.emerald}40`
                    }}
                    onClick={confirmSave}
                  >
                    Yes, Save
                  </button>
                </div>
              </div>
            </GlassCard>
          </div>
        </div>
      )}

      {/* ===== Delete (2-step) modal ===== */}
      {deleteModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={(e)=>{ if(e.target===e.currentTarget) closeDeleteModal(); }}>
          <div className="absolute inset-0 bg-black/40 backdrop-blur-sm" />
          <div className="relative w-full max-w-md">
            <GlassCard>
              <GlassSectionHeader
                title={<span className="inline-flex items-center gap-2 text-lg">
                  <ShieldExclamationIcon 
                    className="w-5 h-5" 
                    style={{ color: themeColors.danger }}
                  />
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
                      <button 
                        className={`min-w-[140px] rounded-lg px-4 py-2 text-sm font-semibold transition-all duration-200`}
                        style={{
                          background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
                          color: dangerTextColor,
                          boxShadow: `0 4px 14px 0 ${themeColors.danger}40`
                        }}
                        onClick={proceedDeletePassword}
                      >
                        Yes, continue
                      </button>
                    </div>
                  </>
                ) : (
                  <>
                    <p className={`text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                      For security, please re-enter your password to delete this row.
                    </p>
                    <input
                      type="password"
                      autoFocus
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="Your password"
                      onKeyDown={(e) => {
                        if (e.key === "Enter") confirmAndDelete();
                        if (e.key === "Escape") closeDeleteModal();
                      }}
                      className={`w-full text-sm border rounded-lg px-3 py-2 ${isDark ? "border-slate-600 bg-slate-700 text-slate-200 placeholder:text-slate-500" : "border-gray-300 bg-white text-gray-800 placeholder:text-gray-400"}`}
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
                          className={`min-w-[170px] rounded-lg px-4 py-2 text-sm font-semibold transition-all duration-200 disabled:opacity-60`}
                          style={{
                            background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
                            color: dangerTextColor,
                            boxShadow: `0 4px 14px 0 ${themeColors.danger}40`
                          }}
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
    <div className={`${isDark ? "bg-slate-800/60 ring-slate-700" : "bg-white/60 ring-gray-200/60"} px-3 py-2 rounded-lg shadow-sm`}>
      <div className={`text-[10px] uppercase tracking-wider font-medium ${isDark ? "text-slate-400" : "text-gray-500"}`}>{label}</div>
      <div className={`text-base font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>{value}</div>
    </div>
  );
}

