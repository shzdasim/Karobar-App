import React, { useRef, useEffect } from "react";
import {
  ExclamationTriangleIcon,
  BanknotesIcon,
  XMarkIcon,
  ArrowRightIcon,
} from "@heroicons/react/24/solid";
import { useCurrency } from "@/context/CurrencyContext.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";

export default function PreShiftDrawerPromptModal({
  isOpen,
  onClose,
  onConfirm,
  oldCash = 0,
  newCash = 0,
  cashDelta = 0,
  invoiceCreatedAt,
  shiftOpenedAt,
}) {
  const { formatCurrency } = useCurrency();
  const { isDark, theme } = useTheme();
  const mouseDownTargetRef = useRef(null);

  // Close on Escape key
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const isIncrease = cashDelta > 0;
  const formattedInvoiceDate = invoiceCreatedAt
    ? new Date(invoiceCreatedAt).toLocaleString([], {
        month: "short",
        day: "numeric",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      })
    : "Previous Session";

  const formattedShiftDate = shiftOpenedAt
    ? new Date(shiftOpenedAt).toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
      })
    : "Current Session";

  const handleMouseDown = (e) => {
    mouseDownTargetRef.current = e.target;
  };

  const handleBackdropClick = (e) => {
    if (e.target === e.currentTarget && mouseDownTargetRef.current === e.currentTarget) {
      onClose();
    }
  };

  return (
    <div
      onMouseDown={handleMouseDown}
      onClick={handleBackdropClick}
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-xs transition-opacity animate-in fade-in duration-200 cursor-default"
      role="dialog"
      aria-modal="true"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className={`w-full max-w-md rounded-2xl shadow-2xl overflow-hidden border flex flex-col ${
          isDark ? "bg-slate-900 border-slate-700 text-slate-100" : "bg-white border-gray-200 text-gray-900"
        }`}
      >
        {/* Header */}
        <div
          className={`flex items-center justify-between px-5 py-3.5 border-b ${
            isDark ? "border-slate-800 bg-slate-800/60" : "border-gray-100 bg-amber-50/60"
          }`}
        >
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-xl bg-amber-500/15 text-amber-500 border border-amber-500/20">
              <BanknotesIcon className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-sm font-bold">Update Cash Drawer?</h3>
              <p className={`text-[11px] ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                Invoice from previous shift edited
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className={`p-1.5 rounded-lg transition-colors ${
              isDark ? "hover:bg-slate-800 text-slate-400" : "hover:bg-gray-100 text-gray-500"
            }`}
          >
            <XMarkIcon className="w-4 h-4" />
          </button>
        </div>

        {/* Content */}
        <div className="p-5 space-y-4 text-xs">
          <div
            className={`p-3.5 rounded-xl border flex items-start gap-2.5 ${
              isDark ? "bg-slate-800/50 border-slate-700 text-slate-300" : "bg-amber-50/50 border-amber-200/60 text-gray-700"
            }`}
          >
            <ExclamationTriangleIcon className="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />
            <div className="leading-relaxed text-[11px]">
              This invoice was originally created on{" "}
              <strong className="font-semibold">{formattedInvoiceDate}</strong>, which is{" "}
              <strong>before</strong> your active shift started ({formattedShiftDate}).
            </div>
          </div>

          {/* Cash Difference Box */}
          <div
            className={`p-3.5 rounded-xl border space-y-2.5 ${
              isDark ? "bg-slate-800/30 border-slate-700" : "bg-gray-50 border-gray-200"
            }`}
          >
            <div className="flex justify-between items-center text-[11px]">
              <span className={isDark ? "text-slate-400" : "text-gray-500"}>Previous Cash Received:</span>
              <span className="font-semibold">{formatCurrency(oldCash)}</span>
            </div>
            <div className="flex justify-between items-center text-[11px]">
              <span className={isDark ? "text-slate-400" : "text-gray-500"}>New Cash Received:</span>
              <span className="font-semibold">{formatCurrency(newCash)}</span>
            </div>
            <div
              className={`pt-2 border-t flex justify-between items-center font-bold ${
                isDark ? "border-slate-700" : "border-gray-200"
              }`}
            >
              <span>Shift Cash Difference:</span>
              <span className={`text-sm ${isIncrease ? "text-emerald-500" : "text-rose-500"}`}>
                {isIncrease ? "+" : ""}{formatCurrency(cashDelta)}
              </span>
            </div>
          </div>

          <p className={`text-[11px] leading-normal ${isDark ? "text-slate-400" : "text-gray-600"}`}>
            Do you want to adjust your <strong>current cash drawer</strong> with this {formatCurrency(Math.abs(cashDelta))}{" "}
            {isIncrease ? "inflow" : "outflow"}?
          </p>
        </div>

        {/* Actions */}
        <div
          className={`px-5 py-3.5 border-t flex items-center justify-end gap-2 ${
            isDark ? "border-slate-800 bg-slate-800/30" : "border-gray-100 bg-gray-50/50"
          }`}
        >
          <button
            type="button"
            onClick={onClose}
            className={`px-3 py-2 rounded-xl text-xs font-semibold transition-colors ${
              isDark ? "hover:bg-slate-800 text-slate-400" : "hover:bg-gray-200 text-gray-600"
            }`}
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={() => onConfirm(false)}
            className={`px-3 py-2 rounded-xl border text-xs font-semibold transition-colors ${
              isDark ? "border-slate-700 hover:bg-slate-800 text-slate-200" : "border-gray-300 hover:bg-gray-100 text-gray-700"
            }`}
          >
            No, Keep Drawer As-Is
          </button>
          <button
            type="button"
            onClick={() => onConfirm(true)}
            className="px-3.5 py-2 rounded-xl font-bold text-xs text-white shadow-sm transition-all active:scale-[0.99] flex items-center gap-1.5"
            style={{ background: theme?.primary_color || "#3b82f6" }}
          >
            <span>Yes, Update Drawer</span>
            <ArrowRightIcon className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>
  );
}

