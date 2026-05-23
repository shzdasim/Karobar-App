import React, { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";
import QuotationSearch from "@/components/QuotationSearch.jsx";

const getContrastText = (hexColor) => {
  hexColor = (hexColor || "").replace("#", "");
  const r = parseInt(hexColor.substring(0, 2) || "00", 16);
  const g = parseInt(hexColor.substring(2, 4) || "00", 16);
  const b = parseInt(hexColor.substring(4, 6) || "00", 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? "#1f2937" : "#ffffff";
};

const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  return getContrastText(primaryHoverColor || primaryColor);
};

const antiFill = {
  autoComplete: "off",
  autoCorrect: "off",
  autoCapitalize: "off",
  spellCheck: false,
};

const to2 = (n) => Number(parseFloat(n || 0).toFixed(2));

export default function QuotationsShow() {
  const { id } = useParams();
  const navigate = useNavigate();

  const [quotation, setQuotation] = useState(null);
  const [loading, setLoading] = useState(true);

  const [deleteModalOpen, setDeleteModalOpen] = useState(false);
  const [deleteStep, setDeleteStep] = useState(1);
  const [password, setPassword] = useState("");
  const [deleting, setDeleting] = useState(false);

  const [searchOpen, setSearchOpen] = useState(false);

  const { loading: permsLoading, canFor } = usePermissions?.() || {};

  const can = useMemo(
    () =>
      typeof canFor === "function"
        ? canFor("quotation")
        : { view: false, create: false, update: false, delete: false },
    [canFor]
  );

  const { isDark, theme } = useTheme();

  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: "#3b82f6",
        primaryHover: "#2563eb",
        secondary: "#8b5cf6",
        secondaryHover: "#7c3aed",
        danger: "#ef4444",
        dangerHover: "#dc2626",
        tertiary: "#06b6d4",
        tertiaryHover: "#0891b2",
      };
    }
    return {
      primary: theme.primary_color || "#3b82f6",
      primaryHover: theme.primary_hover || "#2563eb",
      secondary: theme.secondary_color || "#8b5cf6",
      secondaryHover: theme.secondary_hover || "#7c3aed",
      tertiary: theme.tertiary_color || "#06b6d4",
      tertiaryHover: theme.tertiary_hover || "#0891b2",
      danger: theme.danger_color || "#ef4444",
      dangerHover: theme.danger_hover || "#dc2626",
    };
  }, [theme]);

  const buttonStyle = theme?.button_style || "rounded";

  const getButtonClasses = useMemo(() => {
    const radiusMap = {
      rounded: "rounded-lg",
      outlined: "rounded-lg",
      soft: "rounded-xl",
    };
    const radiusClass = radiusMap[buttonStyle] || "rounded-lg";

    const primaryTextColor = getButtonTextColor(themeColors.primary, themeColors.primaryHover);
    const secondaryTextColor = getButtonTextColor(themeColors.secondary, themeColors.secondaryHover);
    const dangerTextColor = getButtonTextColor(themeColors.danger, themeColors.dangerHover);

    if (buttonStyle === "outlined") {
      return {
        primary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: { borderColor: themeColors.primary, color: themeColors.primary, backgroundColor: "transparent" },
        },
        secondary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.secondary,
            color: themeColors.secondary,
            backgroundColor: "transparent",
          },
        },
        danger: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: { borderColor: themeColors.danger, color: themeColors.danger, backgroundColor: "transparent" },
        },
        glass: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: isDark ? "#475569" : "#cbd5e1",
            color: isDark ? "#e2e8f0" : "#0f172a",
            backgroundColor: "transparent",
          },
        },
      };
    }

    return {
      primary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
          color: primaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
        },
      },
      secondary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
          color: secondaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`,
        },
      },
      danger: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
          color: dangerTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.danger}40`,
        },
      },
      glass: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${isDark ? "#64748b" : "#cbd5e1"}, ${isDark ? "#475569" : "#94a3b8"})`,
          color: isDark ? "white" : "#0f172a",
          boxShadow: `0 4px 14px 0 ${isDark ? "#64748b40" : "#94a3b840"}`,
        },
      },
    };
  }, [buttonStyle, themeColors, isDark]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;
  const btnGlass = getButtonClasses.glass;

  const fmt = (v) => ((v ?? "") === "" ? "" : String(v));

  const itemsCount = quotation?.items?.length ?? 0;

  const totalAmount = useMemo(() => to2(quotation?.total), [quotation]);
  const taxAmount = useMemo(() => to2(quotation?.tax_amount), [quotation]);
  const discountAmount = useMemo(() => to2(quotation?.discount_amount), [quotation]);
  const grossAmount = useMemo(() => to2(quotation?.gross_amount), [quotation]);

  const goToPrevOrIndex = async () => {
    try {
      const res = await axios.get("/api/quotations", { params: { per_page: 100 } });
      const data = res.data;
      const list = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
      const prev = list
        .filter((x) => Number(x?.id) < Number(id))
        .sort((a, b) => Number(b?.id) - Number(a?.id))[0];

      if (prev?.id) navigate(`/quotations/${prev.id}`);
      else navigate("/quotations");
    } catch {
      navigate("/quotations");
    }
  };

  useEffect(() => {
    (async () => {
      try {
        const { data } = await axios.get(`/api/quotations/${id}`);
        setQuotation(data);
      } catch {
        toast.error("Failed to load quotation");
      } finally {
        setLoading(false);
      }
    })();
  }, [id]);

  const openDeleteModal = () => {
    if (!can.delete) return toast.error("You don't have permission to delete quotations.");
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
    if (!can.delete) return toast.error("You don't have permission to delete quotations.");
    try {
      setDeleting(true);
      await axios.post("/api/auth/confirm-password", { password });
      await axios.delete(`/api/quotations/${id}`);
      toast.success("Quotation deleted");
      closeDeleteModal();
      await goToPrevOrIndex();
    } catch (e) {
      const msg =
        e?.response?.data?.message ||
        (e?.response?.status === 422 ? "Incorrect password" : "Failed to delete quotation");
      toast.error(msg);
    } finally {
      setDeleting(false);
    }
  };

  useEffect(() => {
    const onKey = (e) => {
      if (!e.altKey) return;
      const k = (e.key || "").toLowerCase();
      if (k === "b") {
        e.preventDefault();
        navigate(-1);
      }
      if (k === "f") {
        e.preventDefault();
        setSearchOpen(true);
      }
      if (k === "e") {
        if (!can.update) return;
        e.preventDefault();
        navigate(`/quotations/${id}/edit`);
      }
      if (k === "d") {
        if (!can.delete) return;
        e.preventDefault();
        openDeleteModal();
      }
      if (k === "n") {
        if (!can.create) return;
        e.preventDefault();
        navigate("/quotations/create");
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [navigate, id, can]);

  if (loading || permsLoading) return <div className="p-4 text-sm dark:text-gray-400">Loading…</div>;
  if (!quotation) return <div className="p-4 text-sm dark:text-gray-400">Quotation not found.</div>;

  return (
    <div
      className="flex flex-col dark:bg-slate-800"
      style={{ minHeight: "74vh", maxHeight: "80vh" }}
      autoComplete="off"
    >
      {/* HEADER */}
      <div className="sticky top-0 bg-white dark:bg-slate-800 shadow p-2 z-10 dark:shadow-slate-700" autoComplete="off">
        <div className="flex items-center justify-between mb-2">
          <h2 className="text-sm font-bold dark:text-gray-200">Quotation View (Alt+E Edit, Alt+D Delete, Alt+N New, Alt+F Search, Alt+B Back)</h2>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setSearchOpen(true)}
              className={`px-3 py-1.5 rounded text-xs font-semibold transition-all duration-200 ${btnSecondary.className}`}
              style={btnSecondary.style}
              title="Alt+F"
            >
              🔍 Search
            </button>

            <Guard when={can.update}>
              <button
                type="button"
                onClick={() => navigate(`/quotations/${id}/edit`)}
                className={`px-3 py-1.5 rounded text-xs font-semibold transition-all duration-200 ${btnSecondary.className}`}
                style={btnSecondary.style}
                title="Alt+E"
              >
                ✏️ Edit
              </button>
            </Guard>

            <Guard when={can.delete}>
              <button
                type="button"
                onClick={openDeleteModal}
                className={`px-3 py-1.5 rounded text-xs font-semibold transition-all duration-200 ${btnDanger.className}`}
                style={btnDanger.style}
                title="Alt+D"
              >
                🗑️ Delete
              </button>
            </Guard>

            <Guard when={can.create}>
              <button
                type="button"
                onClick={() => navigate("/quotations/create")}
                className={`px-3 py-1.5 rounded text-xs font-semibold transition-all duration-200 ${btnPrimary.className}`}
                style={btnPrimary.style}
                title="Alt+N"
              >
                ➕ New
              </button>
            </Guard>

            <button
              type="button"
              onClick={() => navigate(-1)}
              className={`px-3 py-1.5 rounded text-xs font-medium transition-all duration-200 ${btnGlass.className}`}
              style={btnGlass.style}
              title="Alt+B"
            >
              ← Back
            </button>
          </div>
        </div>

        {/* META STRIP */}
        <div className="px-2 pb-1 grid grid-cols-12 gap-1 text-[11px]">
          <div className="col-span-2">
            <label className="block text-[9px] mb-0.5 dark:text-gray-400">Posted #</label>
            <input
              type="text"
              value={fmt(quotation.posted_number)}
              readOnly
              className="w-full h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
              {...antiFill}
            />
          </div>

          <div className="col-span-2">
            <label className="block text-[9px] mb-0.5 dark:text-gray-400">Date</label>
            <input
              type="text"
              value={fmt(quotation.date)}
              readOnly
              className="w-full h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
              {...antiFill}
            />
          </div>

          <div className="col-span-4">
            <label className="block text-[9px] mb-0.5 dark:text-gray-400">Customer</label>
            <input
              type="text"
              value={quotation.customer?.name ?? quotation.customer_id ?? ""}
              readOnly
              className="w-full h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
              {...antiFill}
            />
          </div>

          <div className="col-span-4">
            <label className="block text-[9px] mb-0.5 dark:text-gray-400">Remarks</label>
            <input
              type="text"
              value={fmt(quotation.remarks)}
              readOnly
              className="w-full h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
              {...antiFill}
            />
          </div>

          <div className="col-span-12">
            <div className="flex items-center justify-between">
              <div className="text-[9px] text-gray-500 dark:text-gray-400">Items: {itemsCount}</div>
              <div className="text-[9px] text-gray-500 dark:text-gray-400">Total: {totalAmount.toLocaleString()}</div>
            </div>
          </div>
        </div>
      </div>

      {/* MAIN */}
      <div className="flex-1 grid grid-cols-[1fr_240px] gap-2 px-2 py-2 overflow-hidden">
        {/* LEFT: ITEMS */}
        <div className="flex flex-col min-h-0">
          <div className="text-[11px] font-semibold mb-1 dark:text-gray-200">Items</div>
          <div className="flex-1 overflow-auto border-2 rounded relative dark:border-slate-600">
            <table className="w-full text-[11px] table-fixed border-collapse print-table">
              <thead className="sticky top-0 z-20 bg-white/80 dark:bg-slate-700/80 backdrop-blur-sm border-b border-gray-200/70 dark:border-slate-600">
                <tr className="[&>th]:py-1 [&>th]:px-1 [&>th]:text-left">
                  <th className="w-7 text-center dark:text-gray-200">#</th>
                  <th className="w-[180px] dark:text-gray-200">Line</th>
                  <th className="w-24 text-center dark:text-gray-200">Qty</th>
                  <th className="w-24 text-center dark:text-gray-200">Price</th>
                  <th className="w-20 text-center dark:text-gray-200">Disc%</th>
                  <th className="w-26 text-center dark:text-gray-200">Sub</th>
                </tr>
              </thead>

              <tbody className="[&>tr>td]:py-1 [&>tr>td]:px-0.5 dark:[&>tr>td]:text-gray-300">
                {(quotation.items || []).map((it, i) => (
                  <tr key={it.id ?? i} className="border-b dark:border-slate-600 text-center">
                    <td>{i + 1}</td>
                    <td className="text-left">
                      {it.line_type === "manual" ? it.manual_name ?? "—" : it.product?.name ?? it.product_id ?? "—"}
                    </td>
                    <td>{fmt(it.quantity)}</td>
                    <td>{to2(it.price).toFixed(2)}</td>
                    <td>{to2(it.item_discount_percentage).toFixed(2)}</td>
                    <td>{to2(it.sub_total).toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* RIGHT: SUMMARY */}
        <div className="min-h-0">
          <div className="sticky top-[20px] space-y-2">
            <div className="border-2 rounded p-2 dark:border-slate-600 dark:bg-slate-800/50">
              <div className="text-[18px] font-semibold mb-1 dark:text-gray-200">Summary</div>
              <div className="grid grid-cols-2 gap-1 text-[11px]">
                <label className="text-[13px] font-bold self-center dark:text-gray-300">Tax %</label>
                <input
                  type="text"
                  readOnly
                  value={fmt(quotation.tax_percentage)}
                  className="h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />

                <label className="text-[13px] font-bold self-center dark:text-gray-300">Tax Amt</label>
                <input
                  type="text"
                  readOnly
                  value={taxAmount ? taxAmount.toFixed(2) : fmt(quotation.tax_amount)}
                  className="h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />

                <label className="text-[13px] font-bold self-center dark:text-gray-300">Disc %</label>
                <input
                  type="text"
                  readOnly
                  value={fmt(quotation.discount_percentage)}
                  className="h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />

                <label className="text-[13px] font-bold self-center dark:text-gray-300">Disc Amt</label>
                <input
                  type="text"
                  readOnly
                  value={discountAmount ? discountAmount.toFixed(2) : fmt(quotation.discount_amount)}
                  className="h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />

                <label className="text-[13px] font-bold self-center dark:text-gray-300">Gross</label>
                <input
                  type="text"
                  readOnly
                  value={grossAmount ? grossAmount.toFixed(2) : fmt(quotation.gross_amount)}
                  className="h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 dark:text-gray-200"
                  {...antiFill}
                />

                <label className="text-[13px] font-bold self-center dark:text-gray-300">Total</label>
                <input
                  type="text"
                  readOnly
                  value={totalAmount.toFixed(2)}
                  className="h-7 border-2 border-black dark:border-slate-600 rounded px-1 bg-gray-100 dark:bg-slate-700 text-red-600 dark:text-red-400 font-extrabold text-lg"
                  {...antiFill}
                />
              </div>
            </div>

            <div className="pt-2">
              <button
                type="button"
                onClick={openDeleteModal}
                className={`w-full h-9 rounded text-white text-[12px] font-semibold transition-all duration-200 ${btnDanger.className}`}
                style={btnDanger.style}
                disabled={!can.delete}
                title="Alt+D"
              >
                🗑️ Delete
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* DELETE MODAL */}
      {deleteModalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
          onClick={(e) => {
            if (e.target === e.currentTarget) closeDeleteModal();
          }}
        >
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-xl w-full max-w-md p-5">
            {deleteStep === 1 && (
              <div>
                <h2 className="text-lg font-semibold mb-2 dark:text-gray-200">Delete quotation?</h2>
                <div className="text-xs text-gray-600 dark:text-gray-400 mb-2">
                  <div>
                    <b className="dark:text-gray-300">Posted #:</b> {fmt(quotation.posted_number)}
                  </div>
                  <div>
                    <b className="dark:text-gray-300">Customer:</b> {fmt(quotation.customer?.name ?? "N/A")}
                  </div>
                  <div>
                    <b className="dark:text-gray-300">Total:</b> {totalAmount.toLocaleString()}
                  </div>
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
                  For security, please re-enter your password to delete this quotation.
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

      {/* SEARCH MODAL */}
      <QuotationSearch
        isOpen={searchOpen}
        onClose={() => setSearchOpen(false)}
        onSelect={(qtn) => {
          if (qtn?.id) navigate(`/quotations/${qtn.id}`);
        }}
      />
    </div>
  );
}

