// resources/js/pages/Ledger/SupplierLedgerPage.jsx
import React, { useEffect, useMemo, useRef, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import SupplierSearch from "../../components/SupplierSearch.jsx";
import RecordPaymentModal from "@/components/RecordPaymentModal.jsx";
import PaymentHistoryModal from "@/components/PaymentHistoryModal.jsx";
import ShareDocumentModal from "@/components/ShareDocumentModal.jsx";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import { printUrlViaIframe } from "@/utils/printHelper.js";
import ModernDatePicker from "@/components/ModernDatePicker.jsx";

// glass primitives
import { GlassCard, GlassSectionHeader, GlassInput } from "@/components/Glass.jsx";

import {
  ArrowPathIcon,
  PrinterIcon,
  PlusCircleIcon,
  WrenchScrewdriverIcon,
  ArrowDownOnSquareIcon,
  ShieldExclamationIcon,
  XMarkIcon,
  Squares2X2Icon,
  CubeIcon,
  BuildingStorefrontIcon,
  TrashIcon,
  BanknotesIcon,
  DocumentTextIcon,
  ChevronDownIcon,
  ChevronUpDownIcon,
  CheckCircleIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  ChevronDoubleLeftIcon,
  ChevronDoubleRightIcon,
  ShareIcon,
} from "@heroicons/react/24/solid";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace("#", "");
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? "#1f2937" : "#ffffff";
};

const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  return getContrastText(primaryHoverColor || primaryColor);
};

// Today's date in YYYY-MM-DD (auto-stamped on every new ledger row)
const todayISO = () => new Date().toISOString().slice(0, 10);

// A row that has not been persisted to the backend yet (no id)
const isPendingRow = (r) => r.id === undefined || r.id === null;

/* =========================
   Supplier Ledger Page (Excel Sheet Layout)
   ========================= */
export default function SupplierLedgerPage() {
  const [suppliers, setSuppliers] = useState([]);
  const [supplierId, setSupplierId] = useState(() => {
    if (typeof window === "undefined") return "";
    const p = new URLSearchParams(window.location.search);
    return p.get("supplier_id") || p.get("id") || "";
  });
  const [supplierSearchOpen, setSupplierSearchOpen] = useState(() => {
    if (typeof window === "undefined") return false;
    const p = new URLSearchParams(window.location.search);
    const initialId = p.get("supplier_id") || p.get("id");
    return !initialId;
  });
  const [rows, setRows] = useState([]);
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  // Default to chronological order (oldest -> newest, top-to-bottom as transactions happen)
  const [newestFirst, setNewestFirst] = useState(false);
  const [sortField, setSortField] = useState("entry_date");
  const [sortDirection, setSortDirection] = useState("ascending");
  const [sortAnnouncement, setSortAnnouncement] = useState("");
  // table paging
  const [page, setPage] = useState(1);
  const [perPage, setPerPage] = useState(50);
  // "mark invoice as paid" flow
  const [settleTarget, setSettleTarget] = useState(null);
  const [settling, setSettling] = useState(false);
  const [settleBusyId, setSettleBusyId] = useState(null);
  const [summary, setSummary] = useState({
    opening_balance: 0,
    total_invoiced: 0,
    paid_on_invoice: 0,
    payments_debited: 0,
    net_balance: 0,
  });

  // Payment modals state
  const [recordPaymentOpen, setRecordPaymentOpen] = useState(false);
  const [paymentHistoryOpen, setPaymentHistoryOpen] = useState(false);
  const [shareModalOpen, setShareModalOpen] = useState(false);

  const tableScrollRef = useRef(null);

  const selectedSupplier = useMemo(
    () => suppliers.find((s) => String(s.id) === String(supplierId)),
    [suppliers, supplierId]
  );

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      typeof canFor === "function"
        ? canFor("supplier-ledger")
        : {
            view: false,
            create: false,
            update: false,
            delete: false,
            import: false,
            export: false,
          },
    [canFor]
  );

  // theme
  const { isDark, theme } = useTheme();

  // Icon button styling used by the modals
  const tintIconBtn = useMemo(
    () =>
      `
    bg-white/60 dark:bg-slate-700/60 backdrop-blur-xs ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-md hover:scale-[1.05] active:scale-[0.95] transition-all duration-200
  `
        .trim()
        .replace(/\s+/g, " "),
    []
  );

  // Memoize theme colors for performance
  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: "#3b82f6",
        primaryHover: "#2563eb",
        primaryLight: "#dbeafe",
        secondary: "#8b5cf6",
        secondaryHover: "#7c3aed",
        secondaryLight: "#ede9fe",
        tertiary: "#06b6d4",
        tertiaryHover: "#0891b2",
        tertiaryLight: "#cffafe",
        danger: "#ef4444",
        dangerHover: "#dc2626",
        dangerLight: "#fee2e2",
        emerald: "#10b981",
        emeraldHover: "#059669",
        emeraldLight: "#d1fae5",
      };
    }
    return {
      primary: theme.primary_color || "#3b82f6",
      primaryHover: theme.primary_hover || "#2563eb",
      primaryLight: theme.primary_light || "#dbeafe",
      secondary: theme.secondary_color || "#8b5cf6",
      secondaryHover: theme.secondary_hover || "#7c3aed",
      secondaryLight: theme.secondary_light || "#ede9fe",
      tertiary: theme.tertiary_color || "#06b6d4",
      tertiaryHover: theme.tertiary_hover || "#0891b2",
      tertiaryLight: theme.tertiary_light || "#cffafe",
      danger: theme.danger_color || "#ef4444",
      dangerHover: "#dc2626",
      dangerLight: "#fee2e2",
      emerald: theme.success_color || "#10b981",
      emeraldHover: "#059669",
      emeraldLight: "#d1fae5",
    };
  }, [theme]);

  // Calculate text colors based on background brightness
  const primaryTextColor = useMemo(
    () => getButtonTextColor(themeColors.primary, themeColors.primaryHover),
    [themeColors.primary, themeColors.primaryHover]
  );

  // Get button style from theme
  const buttonStyle = theme?.button_style || "modern";

  const getButtonClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark, primaryTextColor),
    [buttonStyle, themeColors, isDark, primaryTextColor]
  );

  const btnPrimary = getButtonClasses.primary;
  const btnEmerald = getButtonClasses.emerald || getButtonClasses.success;
  const btnDanger = getButtonClasses.danger;
  const btnOutlined = getButtonClasses.outlined || getButtonClasses.glass || getButtonClasses.secondary;

  // ---------- utils ----------
  const nf = useMemo(
    () => new Intl.NumberFormat("en-US", { minimumFractionDigits: 0, maximumFractionDigits: 2 }),
    []
  );

  const fmt = (v) => {
    if (v === null || v === undefined || v === "") return "0";
    const n = Number(v);
    if (!Number.isFinite(n)) return "0";
    return nf.format(n);
  };

  // Compact, human date shown in the table
  const fmtDate = (d) => {
    if (!d) return "—";
    const dt = new Date(`${String(d).slice(0, 10)}T00:00:00`);
    if (Number.isNaN(dt.getTime())) return String(d).slice(0, 10);
    return dt.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
  };

  // hotkeys (guarded)
  useEffect(() => {
    const onKey = (e) => {
      if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey) return;
      const k = (e.key || "").toLowerCase();
      if (k === "s") {
        e.preventDefault();
        if (!can.create && !can.update) return;
        openSaveModal();
      }
      if (k === "p") {
        e.preventDefault();
        if (!can.view) return;
        handlePrint();
      }
      if (k === "c") {
        const sel = window.getSelection?.()?.toString?.() || "";
        const el = document.activeElement;
        const hasInputSel =
          el && typeof el.selectionStart === "number" && el.selectionStart !== el.selectionEnd;
        if (!sel && !hasInputSel) {
          e.preventDefault();
          setSupplierSearchOpen(true);
        }
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [can.create, can.update, can.view, supplierId, fromDate, toDate, rows]);

  // Load suppliers
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

  const fetchData = async (overrideFrom = fromDate, overrideTo = toDate) => {
    if (!can.view) return toast.error("You don't have permission to view supplier ledger.");
    if (!supplierId) return toast.error("Select a supplier first");
    const effectiveFrom = typeof overrideFrom === "string" ? overrideFrom : fromDate;
    const effectiveTo = typeof overrideTo === "string" ? overrideTo : toDate;
    try {
      const { data } = await axios.get("/api/supplier-ledger", {
        params: {
          supplier_id: supplierId,
          from: effectiveFrom || undefined,
          to: effectiveTo || undefined,
        },
      });
      const clean = (data.data || []).map((r) => {
        const c = { ...r };
        Object.keys(c).forEach((k) => k.endsWith("_input") && delete c[k]);
        return c;
      });
      setRows(clean);
      setSummary(
        data.summary || {
          opening_balance: 0,
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

  useEffect(() => {
    if (!permsLoading && can.view && supplierId) {
      fetchData(fromDate, toDate);
    } else if (!supplierId) {
      setRows([]);
      setSummary({
        opening_balance: 0,
        total_invoiced: 0,
        paid_on_invoice: 0,
        payments_debited: 0,
        net_balance: 0,
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [supplierId]);

  const rebuild = async () => {
    if (!can.update) return toast.error("You don't have permission to rebuild.");
    if (!supplierId) return toast.error("Select a supplier first");
    try {
      await axios.post("/api/supplier-ledger/rebuild", { supplier_id: supplierId });
      toast.success("Rebuilt from purchase invoices");
      await fetchData();
    } catch (e) {
      toast.error(e?.response?.data?.message || "Rebuild failed");
    }
  };

  // ---------- number editing helpers ----------
  const getInput = (row, field) =>
    row[`${field}_input`] !== undefined ? row[`${field}_input`] : (row[field] ?? "") + "";
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
      const parsed =
        raw === undefined || String(raw).trim() === ""
          ? 0
          : parseFloat(String(raw).replace(/,/g, ""));
      r[field] = Number.isFinite(parsed) ? Number(parsed) : 0;
      delete r[`${field}_input`];

      if (["invoice", "manual"].includes(r.entry_type)) {
        const bal = Number(((r.invoice_total || 0) - (r.total_paid || 0)).toFixed(2));
        r.credit_remaining = bal < 0 ? 0 : bal;
      }
      if (r.entry_type === "payment") {
        r.credit_remaining = 0;
      }
      next[idx] = r;
      return next;
    });
  };
  const handleField = (idx, field, value) => {
    setRows((prev) => {
      const next = [...prev];
      const r = { ...next[idx], [field]: value };
      if (
        ["invoice", "manual"].includes(r.entry_type) &&
        (field === "invoice_total" || field === "total_paid")
      ) {
        const bal = Number(((r.invoice_total || 0) - (r.total_paid || 0)).toFixed(2));
        r.credit_remaining = bal < 0 ? 0 : bal;
      }
      if (r.entry_type === "payment") {
        r.credit_remaining = 0;
      }
      next[idx] = r;
      return next;
    });
  };

  // ---------- add row ----------
  // New rows are stamped with today's date and appended chronologically at the end of the sheet
  const addPaymentNow = () => {
    if (!can.create) return toast.error("You don't have permission to add payments.");
    if (!supplierId) return toast.error("Select a supplier first");
    setRows((prev) => [
      ...prev,
      {
        id: undefined,
        supplier_id: supplierId,
        entry_type: "payment",
        type_order: 3,
        entry_date: todayISO(),
        debited_amount: 0,
        posted_number: "",
        payment_ref: "",
        description: "Cash payment made",
        invoice_total: 0,
        total_paid: 0,
        credit_remaining: 0,
        is_manual: true,
      },
    ]);
  };
  const addManualNow = () => {
    if (!can.create) return toast.error("You don't have permission to add manual rows.");
    if (!supplierId) return toast.error("Select a supplier first");
    setRows((prev) => [
      ...prev,
      {
        id: undefined,
        supplier_id: supplierId,
        entry_type: "manual",
        type_order: 2,
        entry_date: todayISO(),
        posted_number: "",
        invoice_total: 0,
        total_paid: 0,
        credit_remaining: 0,
        debited_amount: 0,
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
    if (news.length && !can.create)
      return toast.error("You don't have permission to create ledger rows.");
    if (updates.length && !can.update)
      return toast.error("You don't have permission to update ledger rows.");

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
          payment_ref: n.payment_ref || n.posted_number || null,
          purchase_invoice_id: n.purchase_invoice_id || null,
        };
        await axios.post("/api/supplier-ledger", payload);
      }
      if (updates.length) {
        await axios.put("/api/supplier-ledger/bulk", {
          rows: updates.map((u) => ({
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

  // ---------- delete (secure for saved rows, instant for unsaved rows) ----------
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deleteStep, setDeleteStep] = useState(1);
  const [deletingIdx, setDeletingIdx] = useState(null);
  const [password, setPassword] = useState("");
  const [deleting, setDeleting] = useState(false);

  const openDeleteModal = (originalIdx) => {
    const target = rows[originalIdx];
    if (!target) return;
    if (!target.id) {
      setRows((prev) => prev.filter((_, i) => i !== originalIdx));
      toast.success("Unsaved row removed");
      return;
    }
    if (!can.delete) return toast.error("You don't have permission to delete ledger rows.");
    if (target.entry_type === "invoice" && !target.is_manual) {
      return toast.error("Invoice rows are locked — they come from purchases.");
    }
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
        window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
        window.dispatchEvent(new CustomEvent("accounts-changed"));
        try {
          localStorage.setItem("cash_drawer_sync", String(Date.now()));
        } catch {}
        try {
          localStorage.setItem("accounts_sync", String(Date.now()));
        } catch {}
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
  const openAddManual = () => setAddModal({ open: true, type: "manual" });
  const closeAddModal = () => setAddModal({ open: false, type: null });
  const confirmAdd = () => {
    if (addModal.type === "payment") addPaymentNow();
    if (addModal.type === "manual") addManualNow();
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

  // ---------- derived rows: chronological "as-it-happens" running balance ----------
  const derivedRows = useMemo(() => {
    const indexed = rows.map((r, i) => ({
      ...r,
      __i: i,
      __typeOrder:
        r.type_order ?? (r.entry_type === "invoice" ? 1 : r.entry_type === "manual" ? 2 : 3),
    }));

    // Sort chronologically (Oldest -> Newest, exact sequence of transactions as they happen)
    const chronological = [...indexed].sort((a, b) => {
      const ad = (a.entry_date || "").slice(0, 10);
      const bd = (b.entry_date || "").slice(0, 10);
      if (ad !== bd) return ad < bd ? -1 : 1;

      // Pending unsaved rows come after saved rows on the same date
      const ap = isPendingRow(a);
      const bp = isPendingRow(b);
      if (ap !== bp) return ap ? 1 : -1;
      if (ap && bp) return a.__i - b.__i;

      // Compare creation timestamps when available
      const ats = Number(a.sort_ts || 0);
      const bts = Number(b.sort_ts || 0);
      if (ats > 0 && bts > 0 && ats !== bts) return ats - bts;

      // On the exact same timestamp/date, invoices come before manual rows, which come before payments
      if (a.__typeOrder !== b.__typeOrder) return a.__typeOrder - b.__typeOrder;

      // Fallback to server sequence or ID
      const aSeq = a.seq ?? a.id ?? a.__i;
      const bSeq = b.seq ?? b.id ?? b.__i;
      return aSeq - bSeq;
    });

    // Running balance computed strictly in chronological order from opening balance
    let runningBalance = Number(summary?.opening_balance || 0);
    const withBalance = chronological.map((r, idx) => {
      if (r.entry_type === "invoice" || r.entry_type === "manual") {
        runningBalance += Number(r.credit_remaining || 0);
      } else if (r.entry_type === "payment") {
        runningBalance -= Number(r.debited_amount || 0);
      }
      return {
        ...r,
        __seq: idx + 1,
        running_balance: Number(runningBalance.toFixed(2)),
      };
    });

    const ordered = newestFirst ? [...withBalance].reverse() : withBalance;
    return ordered.map((r, i) => ({ ...r, __top: i + 1 }));
  }, [rows, newestFirst, summary?.opening_balance]);

  // Column totals
  const totals = useMemo(
    () =>
      derivedRows.reduce(
        (acc, r) => {
          if (r.entry_type !== "payment") {
            acc.bill += Number(r.invoice_total || 0);
            acc.paid += Number(r.total_paid || 0);
            acc.balance += Number(r.credit_remaining || 0);
          } else {
            acc.payment += Number(r.debited_amount || 0);
          }
          return acc;
        },
        { bill: 0, paid: 0, balance: 0, payment: 0 }
      ),
    [derivedRows]
  );

  // Client-side summary computed from local rows so the hero strip & footer stay in sync
  const computedSummary = useMemo(() => {
    const openingBal = Number(summary?.opening_balance || 0);
    const totalInvoiced = Number(totals.bill.toFixed(2));
    const paidOnInv = Number(totals.paid.toFixed(2));
    const paymentsDeb = Number(totals.payment.toFixed(2));
    const netBalance = Number(
      (openingBal + totalInvoiced - (paidOnInv + paymentsDeb)).toFixed(2)
    );
    return {
      opening_balance: openingBal,
      total_invoiced: totalInvoiced,
      paid_on_invoice: paidOnInv,
      payments_debited: paymentsDeb,
      net_balance: netBalance,
    };
  }, [totals, summary?.opening_balance]);

  const overallRunning = computedSummary.net_balance;

  // Sortable columns handler (follows modern-web-guidance sortable-table)
  const handleColumnSort = (field, label) => {
    if (field === "entry_date") {
      if (sortField === "entry_date") {
        const nextNewest = !newestFirst;
        const nextDir = nextNewest ? "descending" : "ascending";
        setNewestFirst(nextNewest);
        setSortDirection(nextDir);
        setSortAnnouncement(`Sorted by ${label}, ${nextDir}, ${derivedRows.length} rows`);
      } else {
        setSortField("entry_date");
        setNewestFirst(false);
        setSortDirection("ascending");
        setSortAnnouncement(`Sorted by ${label}, ascending, ${derivedRows.length} rows`);
      }
      return;
    }

    const nextDir =
      sortField === field && sortDirection === "descending" ? "ascending" : "descending";
    setSortField(field);
    setSortDirection(nextDir);
    setSortAnnouncement(`Sorted by ${label}, ${nextDir}, ${derivedRows.length} rows`);
  };

  // Optional: let users sort by amount instead of date
  const sortedRows = useMemo(() => {
    if (sortField === "entry_date") return derivedRows;
    const key = sortField === "invoice_total" ? "invoice_total" : "debited_amount";
    const factor = sortDirection === "ascending" ? 1 : -1;
    return [...derivedRows]
      .sort((a, b) => factor * (Number(a[key] || 0) - Number(b[key] || 0)))
      .map((r, i) => ({ ...r, __top: i + 1 }));
  }, [derivedRows, sortField, sortDirection]);

  const pendingCount = useMemo(() => rows.filter(isPendingRow).length, [rows]);

  // ---------- pagination ----------
  const pageCount = Math.max(1, Math.ceil(sortedRows.length / perPage));
  useEffect(() => {
    setPage(1);
  }, [supplierId, sortField, newestFirst, perPage]);
  useEffect(() => {
    if (page > pageCount) setPage(pageCount);
  }, [page, pageCount]);

  // When a new row is added in chronological mode, jump to last page so user sees it at the bottom
  useEffect(() => {
    if (pendingCount > 0 && !newestFirst && sortField === "entry_date") {
      setPage(pageCount);
    }
  }, [pendingCount, newestFirst, sortField, pageCount]);

  const pagedRows = useMemo(() => {
    const startIdx = (page - 1) * perPage;
    return sortedRows.slice(startIdx, startIdx + perPage);
  }, [sortedRows, page, perPage]);

  const rangeStart = sortedRows.length === 0 ? 0 : (page - 1) * perPage + 1;
  const rangeEnd = Math.min(page * perPage, sortedRows.length);

  // ---------- mark an invoice as paid (invoice + ledger) ----------
  const openSettle = (r) => {
    if (!can.update) return toast.error("You don't have permission to update invoices.");
    if (!r.purchase_invoice_id) return toast.error("This row is not linked to a purchase invoice.");
    const due = Number(r.invoice_due ?? r.credit_remaining ?? 0);
    if (due <= 0) return toast.error("This invoice is already fully paid.");
    setSettleTarget(r);
  };
  const closeSettle = () => {
    if (settling) return;
    setSettleTarget(null);
  };
  const confirmSettle = async () => {
    if (!settleTarget) return;
    const invoiceId = settleTarget.purchase_invoice_id;
    try {
      setSettling(true);
      setSettleBusyId(invoiceId);
      const { data } = await axios.post("/api/supplier-ledger/settle-invoice", {
        purchase_invoice_id: invoiceId,
        entry_date: settleTarget.entry_date || todayISO(),
      });
      const ref = data?.posted_number || settleTarget.posted_number || "";
      const amount = Number(data?.settled || 0);
      if (amount > 0) {
        toast.success(`Invoice ${ref} marked as paid (${fmt(amount)})`);
        window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
        window.dispatchEvent(new CustomEvent("accounts-changed"));
        try {
          localStorage.setItem("cash_drawer_sync", String(Date.now()));
        } catch {}
        try {
          localStorage.setItem("accounts_sync", String(Date.now()));
        } catch {}
      } else {
        toast.success("Invoice was already fully paid");
      }
      setSettleTarget(null);
      await fetchData();
    } catch (err) {
      const status = err?.response?.status;
      if (status === 403) toast.error("You don't have permission to mark invoices as paid.");
      else toast.error(err?.response?.data?.message || "Failed to mark invoice as paid");
    } finally {
      setSettling(false);
      setSettleBusyId(null);
    }
  };

  const newCount = rows.filter((r) => !r.id).length;
  const updCount = rows.filter(
    (r) => r.id && (r.entry_type === "payment" || r.entry_type === "manual" || r.is_manual)
  ).length;

  const handlePrint = async (type /* 'a4'|'thermal' optional */) => {
    if (!can.view) return toast.error("You don't have permission to print.");
    if (!supplierId) return toast.error("Select a supplier first");
    try {
      const { data } = await axios.get("/api/supplier-ledger/print-url", {
        params: {
          supplier_id: supplierId,
          type: type || undefined,
          from: fromDate || undefined,
          to: toDate || undefined,
        },
      });
      if (!data?.url && !data?.html) {
        toast.error("Failed to generate print link.");
        return;
      }
      await printUrlViaIframe(data);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to generate print link.");
    }
  };

  const showOpeningRow = Boolean(
    supplierId && (fromDate || Number(computedSummary.opening_balance) !== 0)
  );

  // ---------- UI ----------
  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.view)
    return (
      <div className="p-6 text-sm text-gray-700">
        You don’t have permission to view supplier ledger.
      </div>
    );

  return (
    <div className="ledger-page supplier-ledger-page space-y-3">
      {/* ===== Unified Excel Workbook Header & Action Ribbon ===== */}
      <div className="ledger-header products-panel">
        <div className="ledger-heading px-4 py-3.5">
          {/* Top Ribbon: Title + Party Selector on Left, Organized Action Groups on Right */}
          {/* Row 1: Title on Left — Utility, Sync, Print & Share Opposite on Right */}
          <div className="ledger-heading-top">
            <div className="flex items-center gap-3 min-w-0">
              <div className="products-identity-icon">
                <CubeIcon className="w-5 h-5 text-white" />
              </div>
              <div>
                <h1 className="products-title text-lg font-bold leading-tight">Supplier Ledger</h1>
                <p className="products-subtitle text-xs">
                  Chronological purchase &amp; payment statement sheet
                </p>
              </div>
            </div>

            <div className="ledger-top-actions" role="group" aria-label="Statement utility and export actions">
              <button
                type="button"
                onClick={() => fetchData()}
                disabled={!supplierId}
                title="Reload ledger entries from server"
                className="products-action theme-btn-tertiary"
              >
                <ArrowPathIcon className="w-4 h-4" />
                <span>Refresh</span>
              </button>

              <Guard when={can.update}>
                <button
                  type="button"
                  onClick={rebuild}
                  disabled={!supplierId}
                  title="Sync and rebuild ledger rows from credit purchase invoices"
                  className="products-action theme-btn-tertiary"
                >
                  <WrenchScrewdriverIcon className="w-4 h-4" />
                  <span>Sync Invoices</span>
                </button>
              </Guard>

              <button
                type="button"
                onClick={() => handlePrint()}
                disabled={!supplierId}
                title="Print statement (Ctrl+P)"
                className="products-action theme-btn-secondary"
              >
                <PrinterIcon className="w-4 h-4" />
                <span>Print</span>
              </button>

              <button
                type="button"
                onClick={() => setShareModalOpen(true)}
                disabled={!supplierId}
                title="Share statement via WhatsApp or Email PDF"
                className="products-action theme-btn-secondary"
              >
                <ShareIcon className="w-4 h-4" />
                <span>Share / PDF</span>
              </button>
            </div>
          </div>

          {/* Row 2: Supplier Selector on Left — Payment & Sheet Entry Actions on Right */}
          <div className="ledger-actions">
            <div className="ledger-party-selector">
              <button
                type="button"
                onClick={() => setSupplierSearchOpen(true)}
                aria-label="Select supplier"
                className="flex-1 h-full px-3 text-left text-xs flex items-center gap-2 min-w-0"
                title={
                  selectedSupplier
                    ? `${selectedSupplier.name} (Click to switch supplier — Ctrl+C)`
                    : "Click to search supplier (Ctrl+C)"
                }
              >
                <BuildingStorefrontIcon className="w-4 h-4 shrink-0 text-blue-500" />
                {supplierId ? (
                  <span className="truncate font-semibold">
                    {selectedSupplier?.name || "Selected supplier"}
                    {selectedSupplier?.phone ? (
                      <span className="ml-1.5 font-normal opacity-70">
                        ({selectedSupplier.phone})
                      </span>
                    ) : null}
                  </span>
                ) : (
                  <span className="truncate opacity-75">Select supplier (Ctrl+C)...</span>
                )}
              </button>
              {supplierId && (
                <button
                  type="button"
                  onClick={() => {
                    setSupplierId("");
                    setSupplierSearchOpen(true);
                  }}
                  className="h-full px-2.5 flex items-center justify-center hover:bg-black/10 dark:hover:bg-white/10 transition-colors shrink-0 border-l border-gray-200/60 dark:border-slate-700"
                  title="Clear and select another supplier"
                  aria-label="Clear supplier"
                >
                  <XMarkIcon className="w-4 h-4" />
                </button>
              )}
            </div>

            <div className="ledger-workflow-actions">
              {/* Group 1: Official Payment Voucher & History */}
              <div className="ledger-action-group" role="group" aria-label="Payment voucher actions">
                <Guard when={can.create}>
                  <button
                    type="button"
                    onClick={() => setRecordPaymentOpen(true)}
                    disabled={!supplierId}
                    title="Record an allocated supplier payment / disbursement voucher"
                    className="products-action bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-xs"
                  >
                    <BanknotesIcon className="w-4 h-4" />
                    <span>Record Payment</span>
                  </button>
                </Guard>

                <button
                  type="button"
                  onClick={() => setPaymentHistoryOpen(true)}
                  disabled={!supplierId}
                  title="View supplier payment vouchers and receipts"
                  className="products-action theme-btn-secondary"
                >
                  <DocumentTextIcon className="w-4 h-4" />
                  <span>Vouchers</span>
                </button>
              </div>

              {/* Group 2: Direct Sheet Row Entry & Save */}
              <div className="ledger-action-group" role="group" aria-label="Sheet row actions">
                <Guard when={can.create}>
                  <button
                    type="button"
                    onClick={openAddManual}
                    disabled={!supplierId}
                    title="Append a manual bill/credit row at the bottom of the sheet"
                    className="products-action theme-btn-secondary"
                  >
                    <PlusCircleIcon className="w-4 h-4" />
                    <span>+ Manual Row</span>
                  </button>

                  <button
                    type="button"
                    onClick={openAddPayment}
                    disabled={!supplierId}
                    title="Append a manual payment row at the bottom of the sheet"
                    className="products-action theme-btn-secondary"
                  >
                    <BanknotesIcon className="w-4 h-4" />
                    <span>+ Payment Row</span>
                  </button>
                </Guard>

                <Guard when={can.update || can.create}>
                  <button
                    type="button"
                    onClick={openSaveModal}
                    disabled={!supplierId || (newCount === 0 && updCount === 0)}
                    title="Save manual edits and new rows (Ctrl+S)"
                    className="products-action products-action-primary theme-btn-primary"
                  >
                    <ArrowDownOnSquareIcon className="w-4 h-4" />
                    <span>Save Sheet</span>
                    {(newCount > 0 || updCount > 0) && supplierId && (
                      <span className="ml-0.5 px-1.5 py-0.5 rounded-sm bg-white/30 text-[10px] font-bold">
                        {newCount + updCount}
                      </span>
                    )}
                  </button>
                </Guard>
              </div>
            </div>
          </div>

          {/* Excel Formula Summary Bar */}
          {supplierId && (
            <div className="ledger-summary-ribbon" role="region" aria-label="Account summary formula">
              <HeroStat
                label="Opening Balance (B/F)"
                value={fmt(computedSummary.opening_balance)}
                operator=""
              />
              <HeroStat
                label="Total Bills (Invoiced)"
                value={fmt(computedSummary.total_invoiced)}
                operator="+"
              />
              <HeroStat
                label="Paid on Bills"
                value={fmt(computedSummary.paid_on_invoice)}
                operator="−"
              />
              <HeroStat
                label="Payments Made"
                value={fmt(computedSummary.payments_debited)}
                operator="−"
                tone="ok"
              />
              <HeroStat
                label={
                  Number(computedSummary.net_balance) < 0
                    ? "Advance / Debit Balance"
                    : "Net Remaining Payable"
                }
                value={fmt(computedSummary.net_balance)}
                operator="="
                tone={Number(computedSummary.net_balance) > 0 ? "warn" : "ok"}
                highlight
              />
            </div>
          )}
        </div>
      </div>

      {/* ===== Excel Spreadsheet Panel ===== */}
      <div className="ledger-table-panel products-panel overflow-hidden">
        {/* Sheet Filter & Order Bar */}
        <div className="ledger-table-toolbar flex flex-wrap items-center justify-between gap-3 px-4 py-2.5">
          <div className="flex items-center gap-2">
            <div className="p-1 rounded-md" style={{ backgroundColor: `${themeColors.primary}1a` }}>
              <Squares2X2Icon className="w-4 h-4" style={{ color: themeColors.primary }} />
            </div>
            <span className="text-xs font-bold uppercase tracking-wider text-gray-700 dark:text-gray-200">
              Transactions Sheet
            </span>
            <span className="text-xs text-gray-500 dark:text-gray-400">
              ({rows.length} {rows.length === 1 ? "row" : "rows"})
            </span>
            <span className="hidden md:inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-medium bg-emerald-500/10 text-emerald-700 dark:text-emerald-300">
              {newestFirst ? "Order: Newest → Oldest" : "Order: Chronological (As It Happens)"}
            </span>
            {pendingCount > 0 && (
              <span className="inline-flex items-center rounded-full bg-amber-100 dark:bg-amber-500/20 px-2 py-0.5 text-[11px] font-semibold text-amber-700 dark:text-amber-300">
                {pendingCount} unsaved — click Save Sheet
              </span>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {/* Date range statement filter */}
            <div className="flex flex-wrap items-center gap-1.5 text-xs">
              <span className="text-gray-500 dark:text-gray-400 font-medium">Period:</span>
              <ModernDatePicker
                value={fromDate}
                onChange={(val) => setFromDate(val)}
                selectsStart
                startDate={fromDate}
                endDate={toDate}
                maxDate={toDate || undefined}
                ariaLabel="Statement from date"
                placeholder="From date"
                size="sm"
                className="w-36"
              />
              <span className="text-gray-400">to</span>
              <ModernDatePicker
                value={toDate}
                onChange={(val) => setToDate(val)}
                selectsEnd
                startDate={fromDate}
                endDate={toDate}
                minDate={fromDate || undefined}
                ariaLabel="Statement to date"
                placeholder="To date"
                size="sm"
                className="w-36"
              />
              <button
                type="button"
                disabled={!supplierId}
                onClick={() => fetchData(fromDate, toDate)}
                className="theme-btn-tertiary inline-flex items-center h-8 px-2.5 text-xs font-medium rounded-lg"
                title="Apply statement date filter"
              >
                Apply
              </button>
              {(fromDate || toDate) && (
                <button
                  type="button"
                  onClick={() => {
                    setFromDate("");
                    setToDate("");
                    if (supplierId) fetchData("", "");
                  }}
                  className="inline-flex items-center h-8 px-2 text-xs font-medium text-rose-600 dark:text-rose-400 hover:underline"
                  title="Clear statement date filter"
                >
                  Clear
                </button>
              )}
            </div>

            {/* Sort field */}
            <div className="relative">
              <select
                value={sortField}
                onChange={(e) => {
                  const val = e.target.value;
                  setSortField(val);
                  if (val === "entry_date") {
                    setSortDirection(newestFirst ? "descending" : "ascending");
                  } else {
                    setSortDirection("descending");
                  }
                }}
                title="Sort ledger rows"
                aria-label="Sort ledger rows"
                className={`appearance-none h-8 pl-2.5 pr-7 text-xs rounded-lg border focus:outline-hidden focus:ring-1 ${
                  isDark
                    ? "border-slate-600 bg-slate-700 text-slate-200 focus:ring-slate-500"
                    : "border-gray-200 bg-white text-gray-700 focus:ring-gray-300"
                }`}
              >
                <option value="entry_date">Sort: Date (As It Happens)</option>
                <option value="invoice_total">Sort: Bill Amount</option>
                <option value="debited_amount">Sort: Payment Amount</option>
              </select>
              <ChevronDownIcon className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" />
            </div>

            {/* Chronological / Reverse toggle */}
            <button
              type="button"
              onClick={() => {
                setSortField("entry_date");
                setNewestFirst((v) => {
                  const next = !v;
                  setSortDirection(next ? "descending" : "ascending");
                  return next;
                });
              }}
              title="Switch between Chronological (Oldest → Newest, like Excel) and Newest First"
              className="theme-btn-tertiary inline-flex items-center gap-1.5 h-8 px-2.5 text-xs font-medium rounded-lg"
            >
              <ChevronUpDownIcon className="w-3.5 h-3.5" />
              <span>{newestFirst ? "Newest → Oldest" : "Oldest → Newest"}</span>
            </button>
          </div>
        </div>

        {/* Visually hidden sort status region for screen readers */}
        <div role="status" aria-live="polite" className="ledger-sr-status">
          {sortAnnouncement}
        </div>

        {/* Excel Grid Container */}
        <div
          ref={tableScrollRef}
          className="ledger-table-scroll"
          role="region"
          aria-labelledby="supplier-ledger-table-caption"
          tabIndex={0}
        >
          <table className="ledger-excel-table w-full text-xs tabular-nums">
            <caption id="supplier-ledger-table-caption" className="ledger-sr-status">
              Supplier ledger transactions recorded chronologically with running balance
            </caption>
            <thead>
              <tr>
                <th scope="col" className="ledger-col-index text-center">
                  #
                </th>
                <th
                  scope="col"
                  className="ledger-col-date text-left"
                  aria-sort={
                    sortField === "entry_date"
                      ? newestFirst
                        ? "descending"
                        : "ascending"
                      : undefined
                  }
                >
                  <button
                    type="button"
                    onClick={() => handleColumnSort("entry_date", "Date")}
                    className="ledger-th-sort-btn"
                  >
                    <span>Date</span>
                    <ChevronUpDownIcon aria-hidden="true" className="w-3.5 h-3.5 shrink-0 opacity-70" />
                  </button>
                </th>
                <th scope="col" className="ledger-col-type text-left">
                  Type
                </th>
                <th scope="col" className="ledger-col-ref text-left">
                  Ref / Invoice #
                </th>
                <th scope="col" className="ledger-col-desc text-left">
                  Description / Particulars
                </th>
                <th
                  scope="col"
                  className="ledger-col-num text-right"
                  aria-sort={sortField === "invoice_total" ? sortDirection : undefined}
                >
                  <button
                    type="button"
                    onClick={() => handleColumnSort("invoice_total", "Bill Amount")}
                    className="ledger-th-sort-btn justify-end"
                  >
                    <span>Bill Amount</span>
                    <ChevronUpDownIcon aria-hidden="true" className="w-3.5 h-3.5 shrink-0 opacity-70" />
                  </button>
                </th>
                <th scope="col" className="ledger-col-num text-right">
                  Paid on Bill
                </th>
                <th
                  scope="col"
                  className="ledger-col-num text-right"
                  aria-sort={sortField === "debited_amount" ? sortDirection : undefined}
                >
                  <button
                    type="button"
                    onClick={() => handleColumnSort("debited_amount", "Payment Made")}
                    className="ledger-th-sort-btn justify-end"
                  >
                    <span>Payment</span>
                    <ChevronUpDownIcon aria-hidden="true" className="w-3.5 h-3.5 shrink-0 opacity-70" />
                  </button>
                </th>
                <th scope="col" className="ledger-col-num text-right">
                  Bill Balance
                </th>
                <th scope="col" className="ledger-col-running text-right">
                  Running Balance
                </th>
                <th scope="col" className="ledger-col-actions text-center">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {!supplierId && (
                <tr>
                  <td
                    colSpan={11}
                    className={`px-4 py-14 text-center ${
                      isDark ? "text-slate-400" : "text-gray-500"
                    }`}
                  >
                    <div className="flex flex-col items-center gap-2">
                      <CubeIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm font-medium">
                        Select a supplier above to open their ledger sheet
                      </p>
                    </div>
                  </td>
                </tr>
              )}

              {/* Opening Balance Brought Forward Row at the top of the sheet */}
              {showOpeningRow && !newestFirst && page === 1 && (
                <tr className="ledger-row-bf">
                  <th scope="row" className="ledger-cell-index text-center font-bold">
                    B/F
                  </th>
                  <td data-label="Date" className="font-medium">
                    {fromDate ? fmtDate(fromDate) : "Opening"}
                  </td>
                  <td data-label="Type">
                    <span className="ledger-type-badge" style={{ "--ledger-type-accent": "#64748b" }}>
                      B/F
                    </span>
                  </td>
                  <td data-label="Ref / Invoice #" className="text-gray-400">
                    —
                  </td>
                  <td data-label="Description" className="font-semibold">
                    Opening Balance Brought Forward {fromDate ? `(prior to ${fmtDate(fromDate)})` : ""}
                  </td>
                  <td data-label="Bill Amount" className="text-right text-gray-400">
                    —
                  </td>
                  <td data-label="Paid on Bill" className="text-right text-gray-400">
                    —
                  </td>
                  <td data-label="Payment" className="text-right text-gray-400">
                    —
                  </td>
                  <td data-label="Bill Balance" className="text-right text-gray-400">
                    —
                  </td>
                  <td
                    data-label="Running Balance"
                    className="ledger-cell-running text-right font-bold"
                    style={{
                      color:
                        Number(computedSummary.opening_balance) > 0
                          ? themeColors.danger
                          : themeColors.emerald,
                    }}
                  >
                    {fmt(computedSummary.opening_balance)}
                  </td>
                  <td data-label="Actions" className="text-center text-gray-400">
                    —
                  </td>
                </tr>
              )}

              {supplierId &&
                rows.length > 0 &&
                pagedRows.map((r) => {
                  const isInvoice = r.entry_type === "invoice";
                  const isPayment = r.entry_type === "payment";
                  const isManual = r.entry_type === "manual";
                  const pending = isPendingRow(r);
                  // Invoice rows come from purchases; payment rows linked to a Payment Voucher are locked
                  const locked =
                    (isInvoice && !r.is_manual) ||
                    Boolean(r.payment_id) ||
                    Boolean(r.purchase_return_id);
                  const editableManual = isManual && !locked;
                  const editablePayment = isPayment && !locked;
                  const invoiceDue = Number(r.invoice_due ?? r.credit_remaining ?? 0);
                  const refValue =
                    r.posted_number || r.invoice_number || r.payment_ref || "";

                  const accent = isInvoice
                    ? themeColors.primary
                    : isPayment
                    ? themeColors.emerald
                    : themeColors.tertiary;

                  return (
                    <tr
                      key={r.row_key ?? (r.id ? `${r.entry_type}-${r.id}` : `new-${r.__i}`)}
                      data-pending={pending}
                      data-entry-type={r.entry_type}
                      className="ledger-excel-row"
                    >
                      {/* 1. Row # (Sticky row header) */}
                      <th scope="row" className="ledger-cell-index text-center">
                        {r.__seq ?? r.__top}
                      </th>

                      {/* 2. Date */}
                      <td data-label="Date" className="ledger-cell-date">
                        {!locked ? (
                          <input
                            type="date"
                            value={(r.entry_date || "").slice(0, 10)}
                            onChange={(e) => handleField(r.__i, "entry_date", e.target.value)}
                            aria-label="Entry date"
                            className="ledger-cell-input text-left"
                          />
                        ) : (
                          <span className="font-medium whitespace-nowrap">
                            {fmtDate(r.entry_date)}
                          </span>
                        )}
                      </td>

                      {/* 3. Type */}
                      <td data-label="Type">
                        <div className="inline-flex items-center gap-1.5 flex-wrap">
                          <span
                            className="ledger-type-badge"
                            style={{ "--ledger-type-accent": accent }}
                          >
                            {isPayment && <BanknotesIcon className="w-3 h-3" />}
                            {isInvoice && <DocumentTextIcon className="w-3 h-3" />}
                            {isManual && <WrenchScrewdriverIcon className="w-3 h-3" />}
                            {isInvoice ? "Invoice" : isPayment ? "Payment" : "Manual"}
                          </span>
                          {pending && (
                            <span className="px-1.5 py-0.5 rounded-sm bg-amber-500/15 text-[10px] font-bold text-amber-600 dark:text-amber-400">
                              NEW
                            </span>
                          )}
                        </div>
                      </td>

                      {/* 4. Ref / Invoice # */}
                      <td data-label="Ref / Invoice #">
                        {editableManual || editablePayment ? (
                          <input
                            type="text"
                            value={
                              editablePayment
                                ? r.payment_ref ?? r.posted_number ?? ""
                                : r.posted_number ?? ""
                            }
                            onChange={(e) =>
                              handleField(
                                r.__i,
                                editablePayment ? "payment_ref" : "posted_number",
                                e.target.value
                              )
                            }
                            placeholder={editablePayment ? "Voucher / Ref #" : "Ref #"}
                            aria-label="Reference number"
                            className="ledger-cell-input text-left"
                          />
                        ) : (
                          <span className="font-medium" title={refValue || undefined}>
                            {refValue || "—"}
                          </span>
                        )}
                      </td>

                      {/* 5. Description */}
                      <td data-label="Description">
                        {locked ? (
                          <span className="block truncate" title={r.description || ""}>
                            {r.description || "—"}
                          </span>
                        ) : (
                          <input
                            type="text"
                            value={r.description ?? ""}
                            onChange={(e) => handleField(r.__i, "description", e.target.value)}
                            placeholder="Enter description..."
                            aria-label="Description"
                            className="ledger-cell-input text-left"
                          />
                        )}
                      </td>

                      {/* 6. Bill Amount */}
                      <td data-label="Bill Amount" className="text-right">
                        {editableManual ? (
                          <input
                            type="text"
                            inputMode="decimal"
                            value={getInput(r, "invoice_total")}
                            onChange={(e) => setInput(r.__i, "invoice_total", e.target.value)}
                            onBlur={() => commitNumber(r.__i, "invoice_total")}
                            onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
                            aria-label="Bill amount"
                            className="ledger-cell-input text-right font-medium"
                          />
                        ) : isInvoice || isManual ? (
                          <span className="font-semibold">{fmt(r.invoice_total ?? 0)}</span>
                        ) : (
                          <span className="text-gray-300 dark:text-slate-600">—</span>
                        )}
                      </td>

                      {/* 7. Paid on Bill */}
                      <td data-label="Paid on Bill" className="text-right">
                        {editableManual ? (
                          <input
                            type="text"
                            inputMode="decimal"
                            value={getInput(r, "total_paid")}
                            onChange={(e) => setInput(r.__i, "total_paid", e.target.value)}
                            onBlur={() => commitNumber(r.__i, "total_paid")}
                            onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
                            aria-label="Paid on bill"
                            className="ledger-cell-input text-right"
                          />
                        ) : isInvoice || isManual ? (
                          <span
                            className={
                              Number(r.total_paid || 0) > 0
                                ? "font-medium text-emerald-600 dark:text-emerald-400"
                                : "text-gray-400 dark:text-slate-500"
                            }
                          >
                            {fmt(r.total_paid ?? 0)}
                          </span>
                        ) : (
                          <span className="text-gray-300 dark:text-slate-600">—</span>
                        )}
                      </td>

                      {/* 8. Payment (Paid via Payment Voucher or Manual Payment Row) */}
                      <td data-label="Payment" className="text-right">
                        {editablePayment ? (
                          <input
                            type="text"
                            inputMode="decimal"
                            value={getInput(r, "debited_amount")}
                            onChange={(e) => setInput(r.__i, "debited_amount", e.target.value)}
                            onBlur={() => commitNumber(r.__i, "debited_amount")}
                            onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
                            aria-label="Payment amount"
                            className="ledger-cell-input text-right font-semibold"
                            style={{ color: themeColors.emerald }}
                          />
                        ) : isPayment ? (
                          <span className="font-bold" style={{ color: themeColors.emerald }}>
                            {fmt(r.debited_amount ?? 0)}
                          </span>
                        ) : (
                          <span className="text-gray-300 dark:text-slate-600">—</span>
                        )}
                      </td>

                      {/* 9. Bill Balance (Unpaid from that bill: Bill - Paid on Bill) */}
                      <td data-label="Bill Balance" className="text-right">
                        {isPayment ? (
                          <span className="text-gray-300 dark:text-slate-600">—</span>
                        ) : (
                          <div className="inline-flex flex-col items-end leading-tight">
                            <span className="font-semibold">
                              {fmt(r.credit_remaining ?? 0)}
                            </span>
                            {isInvoice &&
                              Number(r.credit_remaining || 0) > 0 &&
                              invoiceDue < Number(r.credit_remaining || 0) && (
                                <span
                                  className={`text-[10px] font-medium ${
                                    invoiceDue <= 0
                                      ? "text-emerald-600 dark:text-emerald-400"
                                      : "text-amber-600 dark:text-amber-400"
                                  }`}
                                >
                                  {invoiceDue <= 0 ? "Settled" : `Due: ${fmt(invoiceDue)}`}
                                </span>
                              )}
                          </div>
                        )}
                      </td>

                      {/* 10. Running Balance (Cumulative balance after this transaction) */}
                      <td
                        data-label="Running Balance"
                        className="ledger-cell-running text-right font-bold"
                      >
                        <span
                          style={{
                            color:
                              Number(r.running_balance || 0) > 0
                                ? themeColors.danger
                                : themeColors.emerald,
                          }}
                        >
                          {fmt(r.running_balance ?? 0)}
                        </span>
                      </td>

                      {/* 11. Actions (Settle Invoice / Delete Row) */}
                      <td data-label="Actions" className="text-center">
                        <div className="inline-flex items-center justify-center gap-1">
                          {r.purchase_invoice_id && (
                            <button
                              type="button"
                              onClick={() => openSettle(r)}
                              disabled={settling || invoiceDue <= 0}
                              title={
                                invoiceDue <= 0
                                  ? `${r.posted_number || "Invoice"} is settled`
                                  : `Mark ${r.posted_number || "invoice"} as paid (${fmt(invoiceDue)})`
                              }
                              className={`ledger-row-btn ${
                                invoiceDue <= 0
                                  ? "text-emerald-600 dark:text-emerald-400 bg-emerald-500/10 cursor-default"
                                  : "text-gray-700 dark:text-slate-200 bg-gray-100 dark:bg-slate-700 hover:bg-emerald-600 hover:text-white"
                              }`}
                            >
                              {settleBusyId === r.purchase_invoice_id ? (
                                <ArrowPathIcon className="w-3.5 h-3.5 animate-spin" />
                              ) : (
                                <CheckCircleIcon className="w-3.5 h-3.5" />
                              )}
                              <span>{invoiceDue <= 0 ? "Paid" : "Pay"}</span>
                            </button>
                          )}

                          {(!locked || pending) && (
                            <button
                              type="button"
                              onClick={() => openDeleteModal(r.__i)}
                              title={pending ? "Remove unsaved row" : "Delete this row"}
                              aria-label={pending ? "Remove unsaved row" : "Delete this row"}
                              className="ledger-row-icon-btn text-rose-500 hover:bg-rose-500/15"
                            >
                              <TrashIcon className="w-3.5 h-3.5" />
                            </button>
                          )}

                          {locked && !r.purchase_invoice_id && (
                            <span
                              className="text-[10px] text-gray-400 dark:text-slate-500"
                              title="Created via Payment Voucher — manage in Vouchers"
                            >
                              Voucher
                            </span>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}

              {supplierId && rows.length === 0 && (
                <tr>
                  <td
                    colSpan={11}
                    className={`px-4 py-14 text-center ${
                      isDark ? "text-slate-400" : "text-gray-500"
                    }`}
                  >
                    <div className="flex flex-col items-center gap-2">
                      <CubeIcon className="w-8 h-8 text-gray-400" />
                      <p className="text-sm">
                        No transactions recorded yet. Use{" "}
                        <b className={isDark ? "text-slate-200" : "text-gray-800"}>
                          Record Payment
                        </b>
                        ,{" "}
                        <b className={isDark ? "text-slate-200" : "text-gray-800"}>
                          + Manual Row
                        </b>
                        , or{" "}
                        <b className={isDark ? "text-slate-200" : "text-gray-800"}>
                          Sync Invoices
                        </b>
                        .
                      </p>
                    </div>
                  </td>
                </tr>
              )}
            </tbody>

            {/* Sticky Excel Totals & Closing Balance Footer Row */}
            {supplierId && rows.length > 0 && (
              <tfoot>
                <tr className="ledger-excel-tfoot">
                  <td className="ledger-cell-index text-center font-bold">Σ</td>
                  <td colSpan={4} className="font-bold uppercase tracking-wider text-[11px]">
                    Sheet Totals &amp; Closing Balance
                  </td>
                  <td className="text-right font-bold">{fmt(totals.bill)}</td>
                  <td className="text-right font-bold">{fmt(totals.paid)}</td>
                  <td className="text-right font-bold" style={{ color: themeColors.emerald }}>
                    {fmt(totals.payment)}
                  </td>
                  <td className="text-right font-bold">{fmt(totals.balance)}</td>
                  <td
                    className="ledger-cell-running text-right font-extrabold"
                    style={{
                      color: overallRunning > 0 ? themeColors.danger : themeColors.emerald,
                    }}
                  >
                    Due {fmt(overallRunning)}
                  </td>
                  <td />
                </tr>
              </tfoot>
            )}
          </table>
        </div>

        {/* ===== Pagination footer ===== */}
        {supplierId && sortedRows.length > 0 && (
          <div
            className={`flex flex-wrap items-center justify-between gap-3 px-4 py-2 border-t ${
              isDark ? "border-slate-700 bg-slate-800" : "border-gray-200 bg-gray-50"
            }`}
          >
            <div className="flex items-center gap-3 text-xs text-gray-500 dark:text-gray-400">
              <span>
                Showing <b className="text-gray-700 dark:text-gray-200">{rangeStart}</b>–
                <b className="text-gray-700 dark:text-gray-200">{rangeEnd}</b> of{" "}
                <b className="text-gray-700 dark:text-gray-200">{sortedRows.length}</b> rows
              </span>
              <label className="inline-flex items-center gap-1.5">
                <span className="hidden sm:inline">Rows per page</span>
                <div className="relative">
                  <select
                    value={perPage}
                    onChange={(e) => setPerPage(Number(e.target.value))}
                    className={`appearance-none h-7 pl-2 pr-6 text-xs rounded-md border focus:outline-hidden focus:ring-1 ${
                      isDark
                        ? "border-slate-600 bg-slate-700 text-slate-200 focus:ring-slate-500"
                        : "border-gray-200 bg-white text-gray-700 focus:ring-gray-300"
                    }`}
                  >
                    {[25, 50, 100, 250, 500].map((n) => (
                      <option key={n} value={n}>
                        {n}
                      </option>
                    ))}
                  </select>
                  <ChevronDownIcon className="pointer-events-none absolute right-1 top-1/2 -translate-y-1/2 w-3 h-3 text-gray-400" />
                </div>
              </label>
            </div>

            <div className="flex items-center gap-1">
              <PageBtn
                isDark={isDark}
                onClick={() => setPage(1)}
                disabled={page === 1}
                title="First page"
              >
                <ChevronDoubleLeftIcon className="w-3.5 h-3.5" />
              </PageBtn>
              <PageBtn
                isDark={isDark}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page === 1}
                title="Previous page"
              >
                <ChevronLeftIcon className="w-3.5 h-3.5" />
              </PageBtn>
              <span
                className={`mx-1 px-2.5 py-1 rounded-md text-xs font-semibold ${
                  isDark
                    ? "bg-slate-700 text-slate-200"
                    : "bg-white text-gray-700 ring-1 ring-gray-200"
                }`}
              >
                {page} / {pageCount}
              </span>
              <PageBtn
                isDark={isDark}
                onClick={() => setPage((p) => Math.min(pageCount, p + 1))}
                disabled={page === pageCount}
                title="Next page"
              >
                <ChevronRightIcon className="w-3.5 h-3.5" />
              </PageBtn>
              <PageBtn
                isDark={isDark}
                onClick={() => setPage(pageCount)}
                disabled={page === pageCount}
                title="Last page"
              >
                <ChevronDoubleRightIcon className="w-3.5 h-3.5" />
              </PageBtn>
            </div>
          </div>
        )}
      </div>

      {/* ===== Mark invoice as paid (settle) modal ===== */}
      {settleTarget && (
        <div
          className="ledger-modal fixed inset-0 z-50 flex items-center justify-center"
          onClick={(e) => {
            if (e.target === e.currentTarget) closeSettle();
          }}
        >
          <div className="absolute inset-0 bg-black/40 backdrop-blur-xs" />
          <div className="relative w-full max-w-md">
            <GlassCard>
              <GlassSectionHeader
                title={
                  <span className="inline-flex items-center gap-2">
                    <CheckCircleIcon className="w-5 h-5" style={{ color: themeColors.emerald }} />
                    <span>Mark invoice as paid?</span>
                  </span>
                }
                right={
                  <button
                    aria-label="Close dialog"
                    className={`p-1.5 rounded-lg ${tintIconBtn}`}
                    onClick={closeSettle}
                  >
                    <XMarkIcon className="w-5 h-5" />
                  </button>
                }
              />
              <div className="px-4 py-4 space-y-3">
                <p className={`text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                  The outstanding balance of invoice <b>{settleTarget.posted_number || "—"}</b>{" "}
                  will be recorded as paid, making it <b>fully paid</b>. A payment entry will be
                  recorded in the ledger automatically.
                </p>

                <div
                  className={`rounded-lg px-3 py-2.5 space-y-1.5 text-sm ${
                    isDark ? "bg-slate-700/60" : "bg-gray-50 ring-1 ring-gray-200"
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-gray-500 dark:text-gray-400">Bill total</span>
                    <span className="font-semibold text-gray-800 dark:text-gray-100">
                      {fmt(settleTarget.invoice_total || 0)}
                    </span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-gray-500 dark:text-gray-400">Already paid</span>
                    <span className="font-semibold text-gray-800 dark:text-gray-100">
                      {fmt(settleTarget.invoice_settled ?? settleTarget.total_paid ?? 0)}
                    </span>
                  </div>
                  <div
                    className={`flex items-center justify-between border-t pt-1.5 ${
                      isDark ? "border-slate-600" : "border-gray-200"
                    }`}
                  >
                    <span className="text-xs font-medium text-gray-600 dark:text-gray-300">
                      Amount to settle
                    </span>
                    <span className="font-bold" style={{ color: themeColors.emerald }}>
                      {fmt(settleTarget.invoice_due ?? settleTarget.credit_remaining ?? 0)}
                    </span>
                  </div>
                </div>

                <div className="flex justify-end gap-2">
                  <button
                    className={`min-w-[100px] px-4 py-2 text-sm font-medium ${btnOutlined.className}`}
                    style={btnOutlined.style}
                    onClick={closeSettle}
                    disabled={settling}
                  >
                    Cancel
                  </button>
                  <button
                    className={`min-w-[170px] px-4 py-2 text-sm font-semibold transition-all duration-200 disabled:opacity-60 inline-flex items-center justify-center gap-1.5 ${btnEmerald.className}`}
                    style={btnEmerald.style}
                    onClick={confirmSettle}
                    disabled={settling}
                  >
                    {settling ? (
                      <ArrowPathIcon className="w-4 h-4 animate-spin" />
                    ) : (
                      <CheckCircleIcon className="w-4 h-4" />
                    )}
                    <span>{settling ? "Marking…" : "Yes, mark as paid"}</span>
                  </button>
                </div>
              </div>
            </GlassCard>
          </div>
        </div>
      )}

      {/* ===== Add Row modal ===== */}
      {addModal.open && (
        <div
          className="ledger-modal fixed inset-0 z-50 flex items-center justify-center"
          onClick={(e) => {
            if (e.target === e.currentTarget) closeAddModal();
          }}
        >
          <div className="absolute inset-0 bg-black/40 backdrop-blur-xs" />
          <div className="relative w-full max-w-sm">
            <GlassCard>
              <GlassSectionHeader
                title={
                  <span className="font-semibold text-lg">
                    {addModal.type === "payment" ? "Add Payment Row?" : "Add Manual Row?"}
                  </span>
                }
                right={
                  <button
                    aria-label="Close dialog"
                    className={`p-1.5 rounded-lg ${tintIconBtn}`}
                    onClick={closeAddModal}
                  >
                    <XMarkIcon className="w-5 h-5" />
                  </button>
                }
              />
              <div className="px-4 py-4">
                <p className={`text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                  A new <b>{addModal.type}</b> row will be appended to the ledger sheet for the
                  selected supplier. Remember to click <b>Save Sheet</b> after entering the details.
                </p>
                <div className="mt-4 flex justify-end gap-2">
                  <button
                    className={`min-w-[100px] px-4 py-2 text-sm font-medium ${btnOutlined.className}`}
                    style={btnOutlined.style}
                    onClick={closeAddModal}
                  >
                    Cancel
                  </button>
                  <button
                    className={`min-w-[120px] px-4 py-2 text-sm font-semibold transition-all duration-200 ${btnPrimary.className}`}
                    style={btnPrimary.style}
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
        <div
          className="ledger-modal fixed inset-0 z-50 flex items-center justify-center"
          onClick={(e) => {
            if (e.target === e.currentTarget) closeSaveModal();
          }}
        >
          <div className="absolute inset-0 bg-black/40" />
          <div className="relative w-full max-w-md">
            <GlassCard>
              <GlassSectionHeader
                title={
                  <span className="inline-flex items-center gap-2">
                    <ArrowDownOnSquareIcon
                      className="w-5 h-5"
                      style={{ color: themeColors.emerald }}
                    />
                    <span>Save ledger sheet?</span>
                  </span>
                }
                right={
                  <button
                    aria-label="Close dialog"
                    className={`p-1.5 rounded-lg ${tintIconBtn}`}
                    onClick={closeSaveModal}
                  >
                    <XMarkIcon className="w-5 h-5" />
                  </button>
                }
              />
              <div className="px-4 py-4">
                <p className="text-sm text-gray-700">
                  You’re about to save <b>{newCount}</b> new {newCount === 1 ? "row" : "rows"} and
                  update <b>{updCount}</b> existing {updCount === 1 ? "row" : "rows"} for this
                  supplier.
                </p>
                <div className="mt-4 flex justify-end gap-2">
                  <button
                    className={`min-w-[100px] px-4 py-2 text-sm font-medium ${btnOutlined.className}`}
                    style={btnOutlined.style}
                    onClick={closeSaveModal}
                  >
                    Cancel
                  </button>
                  <button
                    className={`min-w-[120px] px-4 py-2 text-sm font-semibold transition-all duration-200 ${btnEmerald.className}`}
                    style={btnEmerald.style}
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
        <div
          className="ledger-modal fixed inset-0 z-50 flex items-center justify-center"
          onClick={(e) => {
            if (e.target === e.currentTarget) closeDeleteModal();
          }}
        >
          <div className="absolute inset-0 bg-black/40" />
          <div className="relative w-full max-w-md">
            <GlassCard>
              <GlassSectionHeader
                title={
                  <span className="inline-flex items-center gap-2">
                    <ShieldExclamationIcon
                      className="w-5 h-5"
                      style={{ color: themeColors.danger }}
                    />
                    <span>Delete ledger row</span>
                  </span>
                }
                right={
                  <button
                    aria-label="Close dialog"
                    className={`p-1.5 rounded-lg ${tintIconBtn}`}
                    onClick={closeDeleteModal}
                  >
                    <XMarkIcon className="w-5 h-5" />
                  </button>
                }
              />
              <div className="px-4 py-4 space-y-4">
                {deleteStep === 1 ? (
                  <>
                    <p className={`text-sm ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                      Are you sure you want to delete this row? This action cannot be undone.
                    </p>
                    <div className="flex justify-end gap-2">
                      <button
                        className={`min-w-[100px] px-4 py-2 text-sm font-medium ${btnOutlined.className}`}
                        style={btnOutlined.style}
                        onClick={closeDeleteModal}
                      >
                        Cancel
                      </button>
                      <button
                        className={`min-w-[140px] px-4 py-2 text-sm font-semibold transition-all duration-200 ${btnDanger.className}`}
                        style={btnDanger.style}
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
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <button
                        className={`px-4 py-2 text-sm font-medium ${btnOutlined.className}`}
                        style={btnOutlined.style}
                        onClick={() => setDeleteStep(1)}
                        disabled={deleting}
                      >
                        ← Back
                      </button>
                      <div className="flex flex-wrap gap-2 flex-1 sm:flex-initial justify-end">
                        <button
                          className={`px-4 py-2 text-sm font-medium ${btnOutlined.className}`}
                          style={btnOutlined.style}
                          onClick={closeDeleteModal}
                          disabled={deleting}
                        >
                          Cancel
                        </button>
                        <button
                          className={`flex-1 sm:flex-initial px-4 py-2 text-sm font-semibold transition-all duration-200 disabled:opacity-60 ${btnDanger.className}`}
                          style={btnDanger.style}
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

      {/* ===== Supplier Search Modal ===== */}
      <SupplierSearch
        isOpen={supplierSearchOpen}
        onClose={() => setSupplierSearchOpen(false)}
        onSelect={(cust) => {
          setSupplierId(cust ? String(cust.id) : "");
          setSupplierSearchOpen(false);
        }}
      />

      {/* Record Payment Modal */}
      <RecordPaymentModal
        isOpen={recordPaymentOpen}
        onClose={() => setRecordPaymentOpen(false)}
        partyType="supplier"
        partyId={supplierId}
        partyName={selectedSupplier?.name || ""}
        onPaymentSuccess={() => {
          fetchData();
        }}
      />

      {/* Payment Vouchers History Modal */}
      <PaymentHistoryModal
        isOpen={paymentHistoryOpen}
        onClose={() => setPaymentHistoryOpen(false)}
        partyType="supplier"
        partyId={supplierId}
        partyName={selectedSupplier?.name || ""}
        onPaymentReversed={() => {
          fetchData();
        }}
      />

      {/* Outbound Document Share Modal (WhatsApp / Email PDF) */}
      <ShareDocumentModal
        isOpen={shareModalOpen}
        onClose={() => setShareModalOpen(false)}
        documentType="supplier_ledger"
        queryParams={{
          supplier_id: supplierId,
          from: fromDate || undefined,
          to: toDate || undefined,
        }}
      />

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

/* Compact Excel-style formula summary cell */
function HeroStat({ label, value, operator = "", tone = "neutral", highlight = false }) {
  return (
    <div className={`ledger-stat ledger-stat-${tone} ${highlight ? "ledger-stat-highlight" : ""}`}>
      <div className="ledger-stat-label">
        {operator && <span className="ledger-stat-op">{operator}</span>}
        <span>{label}</span>
      </div>
      <div className="ledger-stat-value">{value}</div>
    </div>
  );
}

/* Compact square button used by the ledger pagination footer */
function PageBtn({ isDark, onClick, disabled, title, children }) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
      aria-label={title}
      className={`ledger-page-button inline-flex items-center justify-center w-7 h-7 rounded-md border transition-all duration-200 ${
        disabled
          ? "opacity-40 cursor-not-allowed"
          : isDark
          ? "border-slate-600 bg-slate-700 text-slate-200 hover:bg-slate-600"
          : "border-gray-200 bg-white text-gray-600 hover:bg-gray-100"
      }`}
    >
      {children}
    </button>
  );
}
