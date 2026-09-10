// /src/pages/sales/Show.jsx
import { useEffect, useRef, useState, useMemo } from "react";
import { useParams, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
// 🔒 permissions
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";
// Search modal
import SaleInvoiceSearch from "@/components/SaleInvoiceSearch.jsx";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";

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

export default function SaleInvoiceShow() {
  const { id } = useParams();
  const navigate = useNavigate();

  const [inv, setInv] = useState(null);
  const [loading, setLoading] = useState(true);
  const [deleting, setDeleting] = useState(false);
  const [printerType, setPrinterType] = useState("a4");
  const popupRef = useRef(null);

  // Search modal state
  const [searchOpen, setSearchOpen] = useState(false);

// Get theme colors
  const { theme, isDark } = useTheme();
  const { isPharmacy } = useSaleSystem();

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
        danger: '#ef4444',
        dangerHover: '#dc2626',
        dangerLight: '#fee2e2',
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
        tertiaryLight: '#cffafe',
        success: '#10b981',
        successHover: '#059669',
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
      success: theme.success_color || '#10b981',
      successHover: '#059669',
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
  
  const dangerTextColor = useMemo(() => 
    getButtonTextColor(themeColors.danger, themeColors.dangerHover), 
    [themeColors.danger, themeColors.dangerHover]
  );
  
  const successTextColor = useMemo(() => 
    getButtonTextColor(themeColors.success, themeColors.successHover), 
    [themeColors.success, themeColors.successHover]
  );

  // Get button style from theme
  const buttonStyle = theme?.button_style || 'rounded-sm';
  
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
        danger: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.danger,
            color: themeColors.danger,
            backgroundColor: 'transparent',
          }
        },
        success: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.success,
            color: themeColors.success,
            backgroundColor: 'transparent',
          }
        },
        glass: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: '#64748b',
            color: '#64748b',
            backgroundColor: 'transparent',
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
          color: primaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
        }
      },
      secondary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
          color: secondaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`,
        }
      },
      danger: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
          color: dangerTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.danger}40`,
        }
      },
      success: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.success}, ${themeColors.successHover})`,
          color: successTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.success}40`,
        }
      },
      glass: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, #64748b, #475569)`,
          color: 'white',
          boxShadow: `0 4px 14px 0 #64748b40`,
        }
      },
    };
  }, [buttonStyle, themeColors, primaryTextColor, secondaryTextColor, dangerTextColor, successTextColor]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;
  const btnSuccess = getButtonClasses.success;
  const btnGlass = getButtonClasses.glass;

  const chip =
    "px-1 py-0.5 border rounded-sm bg-gray-50 dark:bg-slate-700 text-[10px] leading-none text-gray-700 dark:text-gray-300";

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions?.() || {};
  const can = useMemo(
    () =>
      (typeof canFor === "function"
        ? canFor("sale-invoice")
        : { view: false, create: false, update: false, delete: false, import: false, export: false }),
    [canFor]
  );

  // ===== Delete modal state =====
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deleteStep, setDeleteStep] = useState(1);
  const [deleteMode, setDeleteMode] = useState("none"); // 'credit' | 'refund' | 'none'
  const [password, setPassword] = useState("");

  // New: items scroller ref (to mimic form table scroll)
  const itemsScrollRef = useRef(null);

  // Fetch invoice + settings
  useEffect(() => {
    (async () => {
      try {
        const [invRes, setRes] = await Promise.all([
          axios.get(`/api/sale-invoices/${id}`),
          axios.get("/api/settings").catch(() => null),
        ]);
        setInv(invRes.data);
        if (setRes?.data?.printer_type) {
          setPrinterType(String(setRes.data.printer_type).toLowerCase());
        }
      } catch {
        toast.error("Failed to load invoice");
      } finally {
        setLoading(false);
      }
    })();
  }, [id]);

  // Derived numbers for summary
  const invTotal = useMemo(
    () => Number(inv?.total ?? inv?.grand_total ?? inv?.gross_amount ?? 0),
    [inv]
  );
  const invReceived = useMemo(
    () => Number(inv?.total_receive ?? inv?.total_recieve ?? inv?.received ?? 0),
    [inv]
  );
  const invRemaining = useMemo(
    () => Math.max(invTotal - invReceived, 0),
    [invTotal, invReceived]
  );
  // Show choice dialog for all credit invoices
  const isCreditInvoice = inv?.invoice_type === 'credit';
  const needsChoice = !!isCreditInvoice;

  // After delete, go to previous or index
  const goToPrevOrIndex = async (deletedId) => {
    try {
      const res = await axios.get("/api/sale-invoices");
      const list = Array.isArray(res.data) ? res.data : [];
      const prev = list
        .filter((x) => Number(x?.id) < Number(deletedId))
        .sort((a, b) => Number(b?.id) - Number(a?.id))[0];

      if (prev?.id) navigate(`/sale-invoices/${prev.id}`);
      else navigate("/sale-invoices");
    } catch {
      navigate("/sale-invoices");
    }
  };

  // ===== Delete flow =====
  const openDeleteModal = () => {
    if (!can.delete) return toast.error("You don't have permission to delete sale invoices.");
    setDeleteMode("none");
    setPassword("");
    setDeleteStep(1);
    setDeleteModalOpen(true);
  };
  const closeDeleteModal = () => {
    if (deleting) return;
    setDeleteModalOpen(false);
    setDeleteStep(1);
    setDeleteMode("none");
    setPassword("");
  };
  const proceedAfterConfirm = () => {
    if (needsChoice) {
      setDeleteMode("credit");
      setDeleteStep(2);
    } else {
      setDeleteStep(3);
    }
  };
  const proceedToPassword = () => setDeleteStep(3);

  const confirmAndDelete = async () => {
    if (!id) return;
    if (!can.delete) return toast.error("You don't have permission to delete sale invoices.");
    try {
      setDeleting(true);
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/sale-invoices/${id}`, { params: { mode: deleteMode } });
      toast.success("Sale invoice deleted");
      await goToPrevOrIndex(id);
      closeDeleteModal();
    } catch (e) {
      const msg =
        e?.response?.data?.message ||
        (e?.response?.status === 422 ? "Incorrect password" : "Failed to delete invoice");
      toast.error(msg);
    } finally {
      setDeleting(false);
    }
  };

  // Print popup logic
  const handlePrint = () => {
    if (!id) return;

    const WEB_BASE =
      (import.meta.env.VITE_BACKEND_WEB_BASE || "").replace(/\/$/, "") ||
      window.location.origin;

    const url = `${WEB_BASE}/print/sale-invoices/${id}`;

    const width = 900;
    const height = 700;
    const left = Math.max(
      0,
      (window.screenX || window.screenLeft || 0) + (window.outerWidth - width) / 2
    );
    const top = Math.max(
      0,
      (window.screenY || window.screenTop || 0) + (window.outerHeight - height) / 2
    );

    const features = [
      `width=${Math.round(width)}`,
      `height=${Math.round(height)}`,
      `left=${Math.round(left)}`,
      `top=${Math.round(top)}`,
      "menubar=no",
      "toolbar=no",
      "location=no",
      "status=no",
      "scrollbars=yes",
      "resizable=yes",
    ].join(",");

    let w = popupRef.current;

    if (!w || w.closed) {
      w = window.open("about:blank", "salePrintWin", features);
      if (!w) {
        toast.error("Popup blocked. Please allow popups to print.");
        return;
      }
      try { w.opener = null; } catch {}
      popupRef.current = w;
    } else {
      try { w.focus(); } catch {}
    }

    try {
      w.location.replace(url);
    } catch {
      const w2 = window.open(url, "salePrintWin", features);
      if (!w2) {
        toast.error("Popup blocked. Please allow popups to print.");
        return;
      }
      try { w2.opener = null; } catch {}
      popupRef.current = w2;
      w = w2;
    }

    try {
      w.onload = () => {
        try { w.focus(); w.print(); } catch {}
      };
    } catch {}

    const timer = setInterval(() => {
      try {
        if (w.document?.readyState === "complete") {
          w.focus(); w.print(); clearInterval(timer);
        }
      } catch {}
      if (w.closed) clearInterval(timer);
    }, 400);
  };

  // Keyboard shortcuts (mimic form topbar)
  useEffect(() => {
    const onKey = (e) => {
      if (!e.altKey) return;
      const k = (e.key || "").toLowerCase();
      if (k === "b") { e.preventDefault(); navigate(-1); }
      if (k === "p") { e.preventDefault(); handlePrint(); }
      if (k === "e") {
        if (!can.update) return;
        e.preventDefault();
        navigate(`/sale-invoices/${id}/edit`);
      }
      if (k === "d") {
        if (!can.delete) return;
        e.preventDefault();
        openDeleteModal();
      }
      if (k === "n") {
        if (!can.create) return;
        e.preventDefault();
        // Navigate based on the current invoice's sale_type
        const saleType = inv?.sale_type || "retail";
        if (saleType === "wholesale") {
          navigate("/sale-invoices/create/wholesale");
        } else {
          navigate("/sale-invoices/create/retail");
        }
      }
      if (k === "f") {
        e.preventDefault();
        setSearchOpen(true);
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [navigate, id, can]);

  if (loading || permsLoading) return <div className="p-4 text-sm dark:text-gray-400">Loading…</div>;
  if (!inv) return <div className="p-4 text-sm dark:text-gray-400">Invoice not found.</div>;

  const fmt = (v) => ((v ?? "") === "" ? "" : String(v));

  return (
    <div className={`h-[calc(100vh-110px)] flex flex-col ${isDark ? "bg-slate-900" : "bg-white"}`}>
      <style>{`
        @media print {
          .no-print { display: none !important; }
          .print-table th, .print-table td { border: 1px solid #000; }
        }
      `}</style>

      {/* === Header (branded banner, matches Sale Invoice form) === */}
      <div className={`shrink-0 sticky top-0 z-20 shadow-lg border-b ${isDark ? "border-slate-700" : "border-gray-200"}`}>
        {/* Branded Banner */}
        <div
          className="px-4 py-2.5 flex items-center justify-between gap-3"
          style={{
            background: `linear-gradient(135deg, ${themeColors.tertiary || themeColors.secondary}, ${themeColors.primary}, ${themeColors.primaryHover})`,
          }}
        >
          <div className="flex items-center gap-3">
            <div
              className="w-9 h-9 rounded-xl flex items-center justify-center shadow-inner"
              style={{ backgroundColor: "rgba(255,255,255,0.18)", backdropFilter: "blur(4px)" }}
            >
              {/* Receipt icon */}
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                <path d="M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1-2 1z" />
                <line x1="8" y1="7" x2="16" y2="7" />
                <line x1="8" y1="11" x2="16" y2="11" />
                <line x1="8" y1="15" x2="13" y2="15" />
              </svg>
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-extrabold tracking-wide text-white leading-none">SALE INVOICE</h2>
                <span
                  className="px-2 py-0.5 rounded-md text-[9px] font-bold tracking-widest text-white"
                  style={{ backgroundColor: "rgba(255,255,255,0.22)", backdropFilter: "blur(4px)" }}
                >
                  {(inv?.sale_type || "retail").toUpperCase()}
                </span>
                <span
                  className={`px-2 py-0.5 rounded-md text-[9px] font-bold tracking-widest text-white`}
                  style={{ backgroundColor: "rgba(255,255,255,0.22)", backdropFilter: "blur(4px)" }}
                >
                  #{fmt(inv.posted_number)}
                </span>
              </div>
              <p className="text-[10px] text-white/80 mt-0.5">
                Alt+F search · Alt+E edit · Alt+P print · Alt+B back
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2.5 no-print">
            {/* Invoice Type Segmented Control (read-only) */}
            <div
              className="flex items-center rounded-lg p-0.5 shadow-inner"
              style={{ backgroundColor: "rgba(0,0,0,0.18)" }}
            >
              {["debit", "credit"].map((type) => {
                const active = inv?.invoice_type === type;
                return (
                  <span
                    key={type}
                    className={`px-3.5 py-1.5 text-[11px] font-semibold rounded-md ${
                      active ? "text-white shadow-sm" : "text-white/70"
                    }`}
                    style={{
                      backgroundColor: active ? "rgba(255,255,255,0.22)" : "transparent",
                      backdropFilter: active ? "blur(4px)" : "none",
                    }}
                  >
                    {type === "debit" ? "💳 Debit" : "🤝 Credit"}
                  </span>
                );
              })}
            </div>

            {/* Action buttons */}
            <button
              type="button"
              onClick={() => setSearchOpen(true)}
              className="px-3 py-2 rounded-lg text-[11px] font-bold bg-white/95 hover:bg-white shadow-lg transition-all duration-200"
              style={{ color: themeColors.primaryHover }}
            >
              🔍 Search
            </button>
            <button
              type="button"
              onClick={handlePrint}
              className="px-3 py-2 rounded-lg text-[11px] font-bold bg-white/95 hover:bg-white shadow-lg transition-all duration-200"
              style={{ color: themeColors.primaryHover }}
            >
              🖨️ Print
            </button>
            <Guard when={can.update}>
              <button
                type="button"
                onClick={() => navigate(`/sale-invoices/${id}/edit`)}
                className="px-3 py-2 rounded-lg text-[11px] font-bold bg-white/95 hover:bg-white shadow-lg transition-all duration-200"
                style={{ color: themeColors.primaryHover }}
              >
                ✏️ Edit
              </button>
            </Guard>
            <Guard when={can.delete}>
              <button
                type="button"
                onClick={openDeleteModal}
                className="px-3 py-2 rounded-lg text-[11px] font-bold bg-red-500/90 hover:bg-red-500 shadow-lg transition-all duration-200"
              >
                🗑 Delete
              </button>
            </Guard>
            <Guard when={can.create}>
              <button
                type="button"
                onClick={() => {
                  const saleType = inv?.sale_type || "retail";
                  if (saleType === "wholesale") navigate("/sale-invoices/create/wholesale");
                  else navigate("/sale-invoices/create/retail");
                }}
                className="px-3 py-2 rounded-lg text-[11px] font-bold bg-white/95 hover:bg-white shadow-lg transition-all duration-200"
                style={{ color: themeColors.primaryHover }}
              >
                ➕ New
              </button>
            </Guard>
            <button
              type="button"
              onClick={() => navigate(-1)}
              className="px-3 py-2 rounded-lg text-[11px] font-bold bg-black/25 hover:bg-black/30 shadow-lg transition-all duration-200 text-white"
            >
              ← Back
            </button>
          </div>
        </div>

        {/* Fields Card */}
        <div className="bg-white dark:bg-slate-800 px-3 py-2">
          <div className="grid grid-cols-12 gap-2 items-end">
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Posted #</label>
              <input
                type="text"
                readOnly
                value={fmt(inv.posted_number)}
                className={`w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] ${
                  isDark ? "bg-slate-700 text-slate-200" : "bg-gray-100 text-gray-800"
                }`}
              />
            </div>
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Date</label>
              <input
                type="text"
                readOnly
                value={fmt(inv.date)}
                className="w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
              />
            </div>
            <div className="col-span-4">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Customer</label>
              <div className={`w-full h-8 px-3 rounded-md border flex items-center gap-2 text-[11px] font-medium ${
                isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-blue-200 bg-blue-50 text-blue-700"
              }`}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className="shrink-0">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
                  <circle cx="12" cy="7" r="4" />
                </svg>
                <span className="truncate">{inv.customer?.name ?? inv.customer_id ?? ""}</span>
              </div>
            </div>
            {(isPharmacy || inv?.doctor_name || inv?.patient_name) && (
            <>
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Doctor</label>
              <input
                type="text"
                readOnly
                value={fmt(inv.doctor_name)}
                className="w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Patient</label>
              <input
                type="text"
                readOnly
                value={fmt(inv.patient_name)}
                className="w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
              />
            </div>
            </>
          )}

            <div className="col-span-10">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Remarks</label>
              <input
                type="text"
                readOnly
                value={fmt(inv.remarks)}
                className="w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Items</label>
              <input
                type="text"
                readOnly
                value={(inv.items || []).length}
                className={`w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] text-center ${
                  isDark ? "bg-slate-700 text-slate-200" : "bg-gray-100 text-gray-800"
                }`}
              />
            </div>
          </div>
        </div>
      </div>

      {/* === Main workspace: Items (fluid) + Summary (280px) === */}
      <div className={`flex-1 grid grid-cols-[1fr_280px] gap-2 px-2 py-2 overflow-hidden ${isDark ? "bg-slate-900" : "bg-white"}`}>
        {/* LEFT: Items */}
        <div className="flex flex-col min-h-0">
          <div className={`text-[11px] font-semibold mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Items</div>
          <div ref={itemsScrollRef} className={`flex-1 overflow-auto border-2 rounded-sm relative ${isDark ? "border-slate-700 bg-slate-800" : "border-gray-200 bg-white"}`}>
            <table className="w-full text-[11px] table-fixed border-collapse print-table">
              <thead className={`sticky top-0 z-20 ${isDark ? "bg-slate-800/90 backdrop-blur-xs" : "bg-white/80 backdrop-blur-xs"} border-b ${isDark ? "border-slate-700" : "border-gray-200/70"}`}>
                <tr className="[&>th]:py-1 [&>th]:px-1 [&>th]:text-left">
                  <th className={`w-7 text-center ${isDark ? "text-slate-400" : "text-gray-500"}`}>#</th>
                  <th className={`w-[180px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Product</th>
                  <th className={`w-14 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>PSize</th>
                  <th className={`w-24 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Batch</th>
                  <th className={`w-15 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Expiry</th>
                  <th className={`w-18 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Avail</th>
                  <th className={`w-25 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Qty</th>
                  <th className={`w-22 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Price</th>
                  <th className={`w-18 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Disc%</th>
                  <th className={`w-26 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Sub Total</th>
                </tr>
              </thead>
              <tbody className={`[&>tr>td]:py-1 [&>tr>td]:px-0.5 ${isDark ? "[&>tr]:border-slate-700 [&>tr>td]:text-slate-300" : "[&>tr]:border-gray-100 [&>tr>td]:text-gray-700"}`}>
                {(inv.items || []).map((it, i) => (
                  <tr key={i} className={`border-b ${isDark ? "border-slate-700" : "border-gray-100"} text-center`}>
                    <td className="px-1">{i + 1}</td>
                    <td className="px-1 text-left">{it.product?.name ?? it.product_id}</td>
                    <td className="px-1">{fmt(it.pack_size)}</td>
                    <td className="px-1">{fmt(it.batch_number)}</td>
                    <td className="px-1">{fmt(it.expiry)}</td>
                    <td className="px-1">{fmt(it.current_quantity)}</td>
                    <td className="px-1">{fmt(it.quantity)}</td>
                    <td className="px-1">{fmt(it.price)}</td>
                    <td className="px-1">{fmt(it.item_discount_percentage)}</td>
                    <td className="px-1">{fmt(it.sub_total)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* RIGHT: Summary panel (mirrors form layout) */}
        <div className="min-h-0">
          <div className={`h-full flex flex-col rounded-2xl overflow-hidden shadow-lg ${isDark ? "bg-slate-800" : "bg-white"}`}>
            {/* Panel Header */}
            <div
              className="px-4 py-3 flex items-center justify-between shrink-0"
              style={{ background: `linear-gradient(135deg, ${themeColors.primary}, ${themeColors.primaryHover})` }}
            >
              <span className="text-white font-bold text-sm tracking-wide">Invoice Summary</span>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="18" y1="20" x2="18" y2="10" />
                <line x1="12" y1="20" x2="12" y2="4" />
                <line x1="6" y1="20" x2="6" y2="14" />
              </svg>
            </div>

            {/* Content */}
            <div className="flex-1 flex flex-col min-h-0 px-4 py-3 text-[12px]">
              <div className="space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Tax %</label>
                  <input
                    type="text"
                    readOnly
                    value={fmt(inv.tax_percentage)}
                    className={`w-28 h-8 border rounded-lg px-2 text-center text-[12px] ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-200 bg-gray-50 text-gray-800"}`}
                  />
                </div>
                <div className={`flex items-center justify-between border-t border-dashed pt-2 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Tax Amt</label>
                  <input
                    type="text"
                    readOnly
                    value={fmt(inv.tax_amount)}
                    className={`w-28 h-8 border rounded-lg px-2 text-right font-semibold text-[12px] ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-200 text-gray-800"}`}
                  />
                </div>
                <div className="flex items-center justify-between gap-2">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Disc %</label>
                  <input
                    type="text"
                    readOnly
                    value={fmt(inv.discount_percentage)}
                    className={`w-28 h-8 border rounded-lg px-2 text-center text-[12px] ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-200 text-gray-800"}`}
                  />
                </div>
                <div className="flex items-center justify-between border-t border-dashed pt-2">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Disc Amt</label>
                  <input
                    type="text"
                    readOnly
                    value={fmt(inv.discount_amount)}
                    className={`w-28 h-8 border rounded-lg px-2 text-right font-semibold text-[12px] ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-200 text-gray-800"}`}
                  />
                </div>
              </div>

              {/* Totals */}
              <div
                className="mt-3 px-4 py-3 rounded-xl"
                style={{ background: `linear-gradient(135deg, ${themeColors.primary}1a, ${themeColors.primaryHover}26)` }}
              >
                <div className="flex items-center justify-between mb-1">
                  <span className={`text-[10px] font-medium uppercase tracking-wide ${isDark ? "text-slate-400" : "text-gray-500"}`}>Gross</span>
                  <span className={`text-[13px] font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>{fmt(inv.gross_amount) ?? 0}</span>
                </div>
                <div className="flex items-center justify-between border-t border-dashed pt-2" style={{ borderColor: `${themeColors.primary}33` }}>
                  <span className={`text-[13px] font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>Total</span>
                  <span className={`text-2xl font-extrabold ${isDark ? "text-red-400" : "text-red-600"}`}>{fmt(inv.total) ?? 0}</span>
                </div>
              </div>

              {/* Payment */}
              <div className="mt-3 space-y-2">
                <div className="flex items-center justify-between">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Received</label>
                  <input
                    type="text"
                    readOnly
                    value={invReceived.toLocaleString()}
                    className={`w-28 h-8 border rounded-lg px-2 text-right font-semibold text-[12px] ${isDark ? "border-slate-600 bg-slate-700 text-slate-200" : "border-gray-200 text-gray-800"}`}
                  />
                </div>
                <div className="flex items-center justify-between">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Remaining</label>
                  <span className={`text-lg font-extrabold ${invRemaining > 0 ? "text-amber-500" : (isDark ? "text-emerald-400" : "text-emerald-600")}`}>
                    {invRemaining}
                  </span>
                </div>
              </div>

              <div className="pt-2 text-[11px] text-gray-500 dark:text-gray-400">
                Printer: <b className="dark:text-gray-300">{(printerType || "a4").toUpperCase()}</b>
              </div>
            </div>

            {/* Actions (compact) */}
            <div className="px-3 py-2 shrink-0 border-t space-y-1.5" style={{ borderColor: isDark ? "rgba(71,85,105,0.5)" : "rgba(229,231,235,0.8)" }}>
              <div className="grid grid-cols-2 gap-1.5 no-print">
                <button
                  type="button"
                  onClick={handlePrint}
                  className={`h-8 rounded-lg text-[11px] font-semibold transition-all duration-200 ${btnSuccess.className}`}
                  style={btnSuccess.style}
                  title="Alt+P"
                >
                  🖨️ Print
                </button>
                <Guard when={can.update}>
                  <button
                    type="button"
                    onClick={() => navigate(`/sale-invoices/${id}/edit`)}
                    className={`h-8 rounded-lg text-[11px] font-semibold transition-all duration-200 ${btnPrimary.className}`}
                    style={btnPrimary.style}
                    title="Alt+E"
                  >
                    ✏️ Edit
                  </button>
                </Guard>
              </div>
              <div className="flex gap-1.5 no-print">
                <Guard when={can.create}>
                  <button
                    type="button"
                    onClick={() => {
                      const saleType = inv?.sale_type || "retail";
                      if (saleType === "wholesale") navigate("/sale-invoices/create/wholesale");
                      else navigate("/sale-invoices/create/retail");
                    }}
                    className={`flex-1 h-8 rounded-lg text-[11px] font-semibold transition-all duration-200 ${btnSecondary.className}`}
                    style={btnSecondary.style}
                  >
                    ➕ New
                  </button>
                </Guard>
                <Guard when={can.delete}>
                  <button
                    type="button"
                    onClick={openDeleteModal}
                    className={`flex-1 h-8 rounded-lg text-[11px] font-semibold transition-all duration-200 ${btnDanger.className}`}
                    style={btnDanger.style}
                  >
                    🗑 Delete
                  </button>
                </Guard>
                <button
                  type="button"
                  onClick={() => navigate(-1)}
                  className={`flex-1 h-8 rounded-lg text-[11px] font-semibold transition-all duration-200 ${btnGlass.className}`}
                  style={btnGlass.style}
                >
                  ← Back
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* ===== Delete confirmation / choice / password modal ===== */}
      {deleteModalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
          onClick={(e) => { if (e.target === e.currentTarget) closeDeleteModal(); }}
        >
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-xl w-full max-w-md p-5">
            {/* Step 1: Confirm delete */}
            {deleteStep === 1 && (
              <div>
                <h2 className="text-lg font-semibold mb-2 dark:text-gray-200">Delete sale invoice?</h2>
                <div className="text-xs text-gray-600 dark:text-gray-400 mb-2">
                  <div><b className="dark:text-gray-300">Posted #:</b> {inv?.posted_number}</div>
                  <div><b className="dark:text-gray-300">Total:</b> {invTotal.toLocaleString()}</div>
                  <div><b className="dark:text-gray-300">Received:</b> {invReceived.toLocaleString()}</div>
                  <div><b className="dark:text-gray-300">Remaining:</b> {invRemaining.toLocaleString()}</div>
                </div>
                <p className="text-sm text-gray-600 dark:text-gray-400">This action cannot be undone.</p>
                <div className="mt-4 flex justify-end gap-2">
                  <button className="px-3 py-1 rounded-sm border dark:border-slate-600 dark:text-gray-300 dark:bg-slate-700" onClick={closeDeleteModal}>
                    Cancel
                  </button>
                  <button
                    className={`px-3 py-1 rounded-sm text-white font-semibold transition-all duration-200 ${btnDanger.className}`}
                    style={btnDanger.style}
                    onClick={proceedAfterConfirm}
                  >
                    Yes, continue
                  </button>
                </div>
              </div>
            )}

            {/* Step 2: Choose Store Credit or Refund */}
            {deleteStep === 2 && (
              <div>
                <h2 className="text-lg font-semibold mb-2 dark:text-gray-200">Store Credit or Refund?</h2>
                <p className="text-sm text-gray-600 dark:text-gray-400 mb-3">
                  Choose how to handle this invoice:
                </p>
                <div className="space-y-2 text-sm dark:text-gray-300">
                  <label className="flex items-start gap-2">
                    <input
                      type="radio"
                      className="mt-1"
                      checked={deleteMode === "credit"}
                      onChange={() => setDeleteMode("credit")}
                    />
                    <span>
                      <b>Store Credit (recommended)</b><br />
                      Create a negative entry in customer's ledger (credit them).
                    </span>
                  </label>
                  <label className="flex items-start gap-2">
                    <input
                      type="radio"
                      className="mt-1"
                      checked={deleteMode === "refund"}
                      onChange={() => setDeleteMode("refund")}
                    />
                    <span>
                      <b>Refund</b><br />
                      Delete all ledger entries (no credit recorded).
                    </span>
                  </label>
                </div>
                <div className="mt-4 flex justify-between">
                  <button className="px-3 py-1 rounded-sm border dark:border-slate-600 dark:text-gray-300 dark:bg-slate-700" onClick={() => setDeleteStep(1)}>
                    ← Back
                  </button>
                  <button
                    className={`px-3 py-1 rounded-sm text-white font-semibold transition-all duration-200 ${btnPrimary.className}`}
                    style={btnPrimary.style}
                    onClick={proceedToPassword}
                  >
                    Continue
                  </button>
                </div>
              </div>
            )}

            {/* Step 3: Password confirm */}
            {deleteStep === 3 && (
              <div>
                <h2 className="text-lg font-semibold mb-2 dark:text-gray-200">Confirm with password</h2>
                <p className="text-sm text-gray-600 dark:text-gray-400">
                  For security, please re-enter your password to delete this sale invoice.
                </p>
                <input
                  type="password"
                  autoFocus
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Your password"
                  className="mt-3 w-full border rounded-sm px-3 py-2 focus:outline-hidden focus:ring-2 focus:ring-blue-500 dark:bg-slate-700 dark:border-slate-600 dark:text-gray-200 dark:placeholder-gray-500"
                  onKeyDown={(e) => {
                    if (e.key === "Enter") confirmAndDelete();
                    if (e.key === "Escape") closeDeleteModal();
                  }}
                />
                <div className="mt-4 flex justify-between">
                  <button
                    className="px-3 py-1 rounded-sm border dark:border-slate-600 dark:text-gray-300 dark:bg-slate-700"
                    onClick={() => setDeleteStep(needsChoice ? 2 : 1)}
                    disabled={deleting}
                  >
                    ← Back
                  </button>
                  <div className="flex gap-2">
                    <button className="px-3 py-1 rounded-sm border dark:border-slate-600 dark:text-gray-300 dark:bg-slate-700" onClick={closeDeleteModal} disabled={deleting}>
                      Cancel
                    </button>
                    <button
                      className={`px-3 py-1 rounded-sm text-white font-semibold transition-all duration-200 disabled:opacity-60 ${btnDanger.className}`}
                      style={btnDanger.style}
                      onClick={confirmAndDelete}
                      disabled={deleting || password.trim() === ""}
                    >
                      {deleting ? "Deleting…" : "Confirm & Delete"}
                    </button>
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* ===== Search Invoice Modal ===== */}
      <SaleInvoiceSearch 
        isOpen={searchOpen} 
        onClose={() => setSearchOpen(false)} 
        onSelect={(invoice) => {
          if (invoice?.id) {
            navigate(`/sale-invoices/${invoice.id}`);
          }
        }}
      />
    </div>
  );
}

