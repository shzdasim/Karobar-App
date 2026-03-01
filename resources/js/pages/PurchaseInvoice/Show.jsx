// /src/pages/purchase-invoices/Show.jsx
import { useEffect, useRef, useState, useMemo } from "react";
import { useParams, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";
import PurchaseInvoiceSearch from "@/components/PurchaseInvoiceSearch.jsx";

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

// Common anti-autofill props
const antiFill = {
  autoComplete: "off",
  autoCorrect: "off",
  autoCapitalize: "off",
  spellCheck: false,
};

const to2 = (n) => Number(parseFloat(n || 0).toFixed(2));

export default function PurchaseInvoiceShow() {
  const { id } = useParams();
  const navigate = useNavigate();

  const [inv, setInv] = useState(null);
  const [loading, setLoading] = useState(true);
  const [deleting, setDeleting] = useState(false);
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deleteStep, setDeleteStep] = useState(1);
  const [password, setPassword] = useState("");
  const [searchOpen, setSearchOpen] = useState(false);

  // Get theme colors
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
        danger: '#ef4444',
        dangerHover: '#dc2626',
        dangerLight: '#fee2e2',
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
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

  // Get button style from theme
  const buttonStyle = theme?.button_style || 'rounded';
  
  // Get button style classes and styles based on theme button_style
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
            borderColor: themeColors.danger,
            color: themeColors.danger,
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
      glass: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, #64748b, #475569)`,
          color: 'white',
          boxShadow: `0 4px 14px 0 #64748b40`,
        }
      },
    };
  }, [buttonStyle, themeColors, primaryTextColor, secondaryTextColor, dangerTextColor]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;
  const btnGlass = getButtonClasses.glass;

  const chip =
    "px-1 py-0.5 border rounded bg-gray-50 dark:bg-slate-700 text-[10px] leading-none text-gray-700 dark:text-gray-300";

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions?.() || {};
  const can = useMemo(
    () =>
      (typeof canFor === "function"
        ? canFor("purchase-invoice")
        : { view: false, create: false, update: false, delete: false, import: false, export: false }),
    [canFor]
  );

  // Fetch invoice
  useEffect(() => {
    (async () => {
      try {
        const { data } = await axios.get(`/api/purchase-invoices/${id}`);
        setInv(data);
      } catch {
        toast.error("Failed to load invoice");
      } finally {
        setLoading(false);
      }
    })();
  }, [id]);

  // Calculate totals
  const totalAmount = useMemo(() => to2(inv?.total_amount), [inv]);
  const totalPaid = useMemo(() => to2(inv?.total_paid), [inv]);
  const remainingAmount = useMemo(() => Math.max(totalAmount - totalPaid, 0), [totalAmount, totalPaid]);
  const invoiceAmount = useMemo(() => Number(inv?.invoice_amount || 0), [inv]);
  const difference = useMemo(() => invoiceAmount - totalAmount, [invoiceAmount, totalAmount]);

  // After delete, go to previous or index
  const goToPrevOrIndex = async (deletedId) => {
    try {
      const res = await axios.get("/api/purchase-invoices", { params: { per_page: 100 } });
      const list = Array.isArray(res.data?.data) ? res.data.data : Array.isArray(res.data) ? res.data : [];
      const prev = list
        .filter((x) => Number(x?.id) < Number(deletedId))
        .sort((a, b) => Number(b?.id) - Number(a?.id))[0];

      if (prev?.id) navigate(`/purchase-invoices/${prev.id}`);
      else navigate("/purchase-invoices");
    } catch {
      navigate("/purchase-invoices");
    }
  };

  // ===== Delete flow =====
  const openDeleteModal = () => {
    if (!can.delete) return toast.error("You don't have permission to delete purchase invoices.");
    setPassword("");
    setDeleteStep(1);
    setDeleteModalOpen(true);
  };
  const closeDeleteModal = () => {
    if (deleting) return;
    setDeleteModalOpen(false);
    setDeleteStep(1);
    setPassword("");
  };

  const confirmAndDelete = async () => {
    if (!id) return;
    if (!can.delete) return toast.error("You don't have permission to delete purchase invoices.");
    try {
      setDeleting(true);
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/purchase-invoices/${id}`);
      toast.success("Purchase invoice deleted");
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

  // Keyboard shortcuts
  useEffect(() => {
    const onKey = (e) => {
      if (!e.altKey) return;
      const k = (e.key || "").toLowerCase();
      if (k === "b") { e.preventDefault(); navigate(-1); }
      if (k === "f") { 
        e.preventDefault(); 
        setSearchOpen(true); 
      }
      if (k === "e") {
        if (!can.update) return;
        e.preventDefault();
        navigate(`/purchase-invoices/${id}/edit`);
      }
      if (k === "d") {
        if (!can.delete) return;
        e.preventDefault();
        openDeleteModal();
      }
      if (k === "n") {
        if (!can.create) return;
        e.preventDefault();
        navigate("/purchase-invoices/create");
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [navigate, id, can]);

  if (loading || permsLoading) return <div className="p-4 text-sm dark:text-gray-400">Loading…</div>;
  if (!inv) return <div className="p-4 text-sm dark:text-gray-400">Invoice not found.</div>;

  const fmt = (v) => ((v ?? "") === "" ? "" : String(v));

  return (
    <div
      className="flex flex-col dark:bg-slate-800"
      style={{ minHeight: "74vh", maxHeight: "80vh" }}
      autoComplete="off"
    >
      {/* ================= HEADER SECTION ================= */}
      <div className="sticky top-0 bg-white dark:bg-slate-800 shadow p-2 z-10 dark:shadow-slate-700" autoComplete="off">
        <div className="flex items-center justify-between mb-2">
          <h2 className="text-sm font-bold dark:text-gray-200">
            Purchase Invoice View (Alt+E Edit, Alt+D Delete, Alt+N New, Alt+B Back)
          </h2>
          
          {/* Invoice Type Radio Buttons (read-only) */}
          <div className="flex items-center gap-3 bg-gray-50 dark:bg-slate-700 px-3 py-1.5 rounded border dark:border-slate-600">
            <label className="flex items-center gap-1 cursor-not-allowed opacity-70">
              <input
                type="radio"
                name="invoice_type"
                value="debit"
                checked={inv.invoice_type === "debit"}
                readOnly
                className="cursor-not-allowed"
              />
              <span className="text-xs font-medium text-gray-700 dark:text-gray-300">Debit</span>
            </label>
            <label className="flex items-center gap-1 cursor-not-allowed opacity-70">
              <input
                type="radio"
                name="invoice_type"
                value="credit"
                checked={inv.invoice_type === "credit"}
                readOnly
                className="cursor-not-allowed"
              />
              <span className="text-xs font-medium text-gray-700 dark:text-gray-300">Credit</span>
            </label>
          </div>
        </div>
        
        <table className="w-full border-collapse text-xs">
          <tbody>
            <tr>
              <td className="border p-1 w-1/12 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Posted Number</label>
                <input
                  type="text"
                  inputMode="decimal"
                  name="posted_number"
                  readOnly
                  value={fmt(inv.posted_number)}
                  className="bg-gray-100 dark:bg-slate-700 dark:text-gray-200 border dark:border-slate-600 rounded w-full p-1 h-7 text-xs"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/6 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Posted Date</label>
                <input
                  type="text"
                  name="posted_date"
                  readOnly
                  value={fmt(inv.posted_date)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/3 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Supplier</label>
                <input
                  type="text"
                  readOnly
                  value={inv.supplier?.name ?? inv.supplier_id ?? ""}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Invoice Number</label>
                <input
                  type="text"
                  name="invoice_number"
                  readOnly
                  value={fmt(inv.invoice_number)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Invoice Amount</label>
                <input
                  type="text"
                  name="invoice_amount"
                  readOnly
                  value={fmt(inv.invoice_amount)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Difference</label>
                <input
                  type="text"
                  readOnly
                  value={difference.toFixed(2)}
                  className={`border dark:border-slate-600 rounded w-full p-1 h-7 text-xs font-bold text-center bg-gray-100 dark:bg-slate-700 dark:text-gray-200 ${
                    difference !== 0 ? "text-red-600 dark:text-red-400" : "text-gray-700 dark:text-gray-300"
                  }`}
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/6 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Remarks</label>
                <input
                  type="text"
                  name="remarks"
                  readOnly
                  value={fmt(inv.remarks)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
            </tr>
          </tbody>
        </table>

        {/* Action Buttons */}
        <div className="mt-2 flex items-center gap-2">
          <button
            type="button"
            onClick={() => setSearchOpen(true)}
            className={`px-4 py-1.5 rounded text-xs font-semibold transition-all duration-200 ${btnSecondary.className}`}
            style={btnSecondary.style}
            title="Alt+F"
          >
            🔍 Search (Alt+F)
          </button>
          <Guard when={can.update}>
            <button
              type="button"
              onClick={() => navigate(`/purchase-invoices/${id}/edit`)}
              className={`px-4 py-1.5 rounded text-xs font-semibold transition-all duration-200 ${btnSecondary.className}`}
              style={btnSecondary.style}
              title="Alt+E"
            >
              ✏️ Edit (Alt+E)
            </button>
          </Guard>
          <Guard when={can.delete}>
            <button
              type="button"
              onClick={openDeleteModal}
              className={`px-4 py-1.5 rounded text-xs font-semibold transition-all duration-200 ${btnDanger.className}`}
              style={btnDanger.style}
              title="Alt+D"
            >
              🗑 Delete (Alt+D)
            </button>
          </Guard>
          <Guard when={can.create}>
            <button
              type="button"
              onClick={() => navigate("/purchase-invoices/create")}
              className={`px-4 py-1.5 rounded text-xs font-semibold transition-all duration-200 ${btnPrimary.className}`}
              style={btnPrimary.style}
              title="Alt+N"
            >
              ➕ New (Alt+N)
            </button>
          </Guard>
          <button
            type="button"
            onClick={() => navigate(-1)}
            className={`px-4 py-1.5 rounded text-xs font-medium transition-all duration-200 ${btnGlass.className}`}
            style={btnGlass.style}
            title="Alt+B"
          >
            ← Back (Alt+B)
          </button>
        </div>
      </div>

      {/* ================= ITEMS SECTION ================= */}
      <div className="flex-1 overflow-auto p-1 dark:bg-slate-800" autoComplete="off">
        <h2 className="text-xs font-bold mb-1 dark:text-gray-200">Items</h2>

        <table className="w-full border-collapse text-[11px]">
          <thead className="sticky top-0 bg-gray-100 dark:bg-slate-700 z-5">
            <tr>
              <th rowSpan={2} className="border w-6 dark:border-slate-600 dark:text-gray-200">#</th>
              <th rowSpan={2} colSpan={1} className="border w-[80px] dark:border-slate-600 dark:text-gray-200">Product</th>
              <th colSpan={3} className="border dark:border-slate-600 dark:text-gray-200">Pack Size / Batch / Expiry</th>
              <th colSpan={2} className="border dark:border-slate-600 dark:text-gray-200">Qty (Pack / Unit)</th>
              <th colSpan={2} className="border dark:border-slate-600 dark:text-gray-200">Purchase Price (P / U)</th>
              <th colSpan={3} className="border dark:border-slate-600 dark:text-gray-200">Disc % / Bonus (P / U)</th>
              <th colSpan={2} className="border dark:border-slate-600 dark:text-gray-200">Sale Price (P / U)</th>
              <th colSpan={3} className="border dark:border-slate-600 dark:text-gray-200">Wholesale Price (P / U)</th>
              <th colSpan={3} className="border dark:border-slate-600 dark:text-gray-200">Margin % / W.S.Mrg% / Avg / Sub Total</th>
            </tr>

            <tr>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">PSize</th>
              <th className="border w-16 dark:border-slate-600 dark:text-gray-200">Batch</th>
              <th className="border w-20 dark:border-slate-600 dark:text-gray-200">Exp</th>
              <th className="border w-12 dark:border-slate-600 dark:text-gray-200">Pack.Q</th>
              <th className="border w-12 dark:border-slate-600 dark:text-gray-200">Unit.Q</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">Pack.P</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">Unit.P</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">Disc%</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">PBonus</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">UBonus</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">Pack.S</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">Unit.S</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">W.S.Pack</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">W.S.Unit</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">Margin%</th>
              <th className="border w-14 dark:border-slate-600 dark:text-gray-200">W.S.Mrg%</th>
              <th className="border w-16 dark:border-slate-600 dark:text-gray-200">Avg</th>
              <th className="border w-20 dark:border-slate-600 dark:text-gray-200">Sub Total</th>
            </tr>
          </thead>

          <tbody>
            {(inv.items || []).map((item, i) => (
              <tr key={i} className="text-center dark:text-gray-300">
                {/* Row Number */}
                <td className="border px-1 dark:border-slate-600">{i + 1}</td>

                {/* Product */}
                <td className="border px-1 text-left dark:border-slate-600">{fmt(item.product?.name ?? item.product_id)}</td>

                {/* Pack Size */}
                <td className="border w-14 dark:border-slate-600">
                  <input
                    type="number"
                    readOnly
                    value={fmt(item.pack_size)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Batch */}
                <td className="border w-16 dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.batch)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Expiry */}
                <td className="border w-20 dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.expiry)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Pack Qty */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.pack_quantity)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Unit Qty */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.unit_quantity)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Pack Purchase */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.pack_purchase_price)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Unit Purchase */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.unit_purchase_price)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Disc% */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.item_discount_percentage)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Pack Bonus */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.pack_bonus)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Unit Bonus */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.unit_bonus)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Pack Sale */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.pack_sale_price)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Unit Sale */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.unit_sale_price)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Wholesale Pack Price */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.whole_sale_pack_price)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Wholesale Unit Price */}
                <td className="border dark:border-slate-600">
                  <input
                    type="text"
                    readOnly
                    value={fmt(item.whole_sale_unit_price)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Margin % */}
                <td className="border dark:border-slate-600">
                  <input
                    type="number"
                    readOnly
                    value={fmt(item.margin)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* W.S. Margin % */}
                <td className="border dark:border-slate-600">
                  <input
                    type="number"
                    readOnly
                    value={fmt(item.whole_sale_margin)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Avg Price */}
                <td className="border dark:border-slate-600">
                  <input
                    type="number"
                    readOnly
                    value={fmt(item.avg_price)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>

                {/* Sub Total */}
                <td className="border dark:border-slate-600">
                  <input
                    type="number"
                    readOnly
                    value={fmt(item.sub_total)}
                    className="border dark:border-slate-600 bg-gray-100 dark:bg-slate-700 dark:text-gray-200 w-full h-6 text-[11px] px-1"
                    {...antiFill}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* ================= FOOTER SECTION ================= */}
      <div className="sticky bottom-0 bg-white dark:bg-slate-800 shadow p-2 z-10 dark:shadow-slate-700" autoComplete="off">
        <table className="w-full border-collapse text-xs">
          <tbody>
            <tr>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Tax %</label>
                <input
                  type="text"
                  name="tax_percentage"
                  readOnly
                  value={fmt(inv.tax_percentage)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Tax Amount</label>
                <input
                  type="text"
                  name="tax_amount"
                  readOnly
                  value={fmt(inv.tax_amount)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Discount %</label>
                <input
                  type="text"
                  name="discount_percentage"
                  readOnly
                  value={fmt(inv.discount_percentage)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Discount Amount</label>
                <input
                  type="text"
                  name="discount_amount"
                  readOnly
                  value={fmt(inv.discount_amount)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Total Amount</label>
                <input
                  type="number"
                  name="total_amount"
                  readOnly
                  value={totalAmount.toFixed(2)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200 font-bold"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Total Paid</label>
                <input
                  type="text"
                  name="total_paid"
                  readOnly
                  value={totalPaid.toFixed(2)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 dark:border-slate-600">
                <label className="block text-[10px] dark:text-gray-400">Remaining</label>
                <input
                  type="number"
                  name="remaining_amount"
                  readOnly
                  value={remainingAmount.toFixed(2)}
                  className="border dark:border-slate-600 rounded w-full p-1 h-7 text-xs bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />
              </td>
              <td className="border p-1 w-1/8 text-center align-middle dark:border-slate-600">
                <button
                  type="button"
                  onClick={() => navigate(`/purchase-invoices/${id}/edit`)}
                  className={`w-full px-3 py-2 rounded text-xs font-semibold transition-all duration-200 mb-1 ${btnSecondary.className}`}
                  style={btnSecondary.style}
                >
                  ✏️ Edit
                </button>
                <button
                  type="button"
                  onClick={() => navigate("/purchase-invoices/create")}
                  className={`w-full px-3 py-2 rounded text-xs font-semibold transition-all duration-200 ${btnPrimary.className}`}
                  style={btnPrimary.style}
                >
                  ➕ New Invoice
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      {/* ===== Delete Modal ===== */}
      {deleteModalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
          onClick={(e) => { if (e.target === e.currentTarget) closeDeleteModal(); }}
        >
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-xl w-full max-w-md p-5">
            {deleteStep === 1 && (
              <div>
                <h2 className="text-lg font-semibold mb-2 dark:text-gray-200">Delete purchase invoice?</h2>
                <div className="text-xs text-gray-600 dark:text-gray-400 mb-2">
                  <div><b className="dark:text-gray-300">Posted #:</b> {fmt(inv?.posted_number)}</div>
                  <div><b className="dark:text-gray-300">Invoice No:</b> {fmt(inv?.invoice_number)}</div>
                  <div><b className="dark:text-gray-300">Supplier:</b> {fmt(inv.supplier?.name ?? "N/A")}</div>
                  <div><b className="dark:text-gray-300">Total:</b> {totalAmount.toLocaleString()}</div>
                  <div><b className="dark:text-gray-300">Paid:</b> {totalPaid.toLocaleString()}</div>
                  <div><b className="dark:text-gray-300">Remaining:</b> {remainingAmount.toLocaleString()}</div>
                </div>
                <p className="text-sm text-gray-600 dark:text-gray-400">This action cannot be undone.</p>
                <div className="mt-4 flex justify-end gap-2">
                  <button 
                    className="px-3 py-1 rounded border dark:border-slate-600 dark:text-gray-300 dark:bg-slate-700 transition-all duration-200" 
                    onClick={closeDeleteModal}
                  >
                    Cancel
                  </button>
                  <button
                    className={`px-3 py-1 rounded text-white font-semibold transition-all duration-200 ${btnDanger.className}`}
                    style={btnDanger.style}
                    onClick={() => setDeleteStep(2)}
                  >
                    Yes, continue
                  </button>
                </div>
              </div>
            )}

            {deleteStep === 2 && (
              <div>
                <h2 className="text-lg font-semibold mb-2 dark:text-gray-200">Confirm with password</h2>
                <p className="text-sm text-gray-600 dark:text-gray-400">
                  For security, please re-enter your password to delete this purchase invoice.
                </p>
                <input
                  type="password"
                  autoFocus
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Your password"
                  className="mt-3 w-full border rounded px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500 dark:bg-slate-700 dark:border-slate-600 dark:text-gray-200 dark:placeholder-gray-500"
                  onKeyDown={(e) => {
                    if (e.key === "Enter") confirmAndDelete();
                    if (e.key === "Escape") closeDeleteModal();
                  }}
                />
                <div className="mt-4 flex justify-between">
                  <button
                    className="px-3 py-1 rounded border dark:border-slate-600 dark:text-gray-300 dark:bg-slate-700 transition-all duration-200"
                    onClick={() => setDeleteStep(1)}
                    disabled={deleting}
                  >
                    ← Back
                  </button>
                  <div className="flex gap-2">
                    <button 
                      className="px-3 py-1 rounded border dark:border-slate-600 dark:text-gray-300 dark:bg-slate-700 transition-all duration-200" 
                      onClick={closeDeleteModal} 
                      disabled={deleting}
                    >
                      Cancel
                    </button>
                    <button
                      className={`px-3 py-1 rounded text-white font-semibold transition-all duration-200 disabled:opacity-60 ${btnDanger.className}`}
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

      {/* ===== Search Modal ===== */}
      <PurchaseInvoiceSearch
        isOpen={searchOpen}
        onClose={() => setSearchOpen(false)}
        onSelect={(invoice) => {
          navigate(`/purchase-invoices/${invoice.id}`);
        }}
      />
    </div>
  );
}

