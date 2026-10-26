import React, { useMemo, useState } from "react";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import { ExclamationTriangleIcon, ShieldCheckIcon, TagIcon } from "@heroicons/react/24/outline";

export default function DiscountOverrideModal({
  isOpen,
  onClose,
  onConfirm,
  message,
  productName,
  maxDiscount,
  requestedDiscount,
}) {
  const { isDark, theme } = useTheme();
  const [supervisorPassword, setSupervisorPassword] = useState("");

  const themeColors = useMemo(
    () => ({
      primary: theme?.primary_color || "#3b82f6",
      primaryHover: theme?.primary_hover || "#2563eb",
      secondary: theme?.secondary_color || "#8b5cf6",
      secondaryHover: theme?.secondary_hover || "#7c3aed",
      danger: theme?.danger_color || "#ef4444",
      dangerHover: "#dc2626",
    }),
    [theme]
  );

  const buttonStyle = theme?.button_style || "modern";
  const getButtonClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark),
    [buttonStyle, themeColors, isDark]
  );

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4 animate-fadeIn">
      <div className="bg-white/95 dark:bg-slate-900/95 rounded-2xl shadow-2xl border border-amber-400/80 dark:border-amber-500/60 max-w-md w-full p-5 space-y-4 backdrop-blur-xl">
        <div className="flex items-start gap-3">
          <div className="p-2.5 rounded-xl bg-amber-100 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 shrink-0">
            <ExclamationTriangleIcon className="w-6 h-6" />
          </div>
          <div className="min-w-0">
            <h3 className="text-base font-bold text-gray-900 dark:text-white flex items-center gap-2">
              <span>Max Discount Limit Exceeded</span>
            </h3>
            <p className="text-xs text-gray-600 dark:text-slate-300 mt-1 leading-relaxed">
              {message || "The requested item discount exceeds the authorized maximum policy limit."}
            </p>
          </div>
        </div>

        <div className="bg-gray-50/80 dark:bg-slate-800/60 p-3.5 rounded-xl text-xs space-y-2 border border-gray-200/60 dark:border-slate-700/60">
          {productName && (
            <div className="flex justify-between items-center">
              <span className="text-gray-500 dark:text-slate-400 flex items-center gap-1.5">
                <TagIcon className="w-3.5 h-3.5 text-blue-500" />
                Product:
              </span>
              <span className="font-semibold text-gray-900 dark:text-white truncate max-w-[200px]">
                {productName}
              </span>
            </div>
          )}
          {maxDiscount !== undefined && (
            <div className="flex justify-between items-center">
              <span className="text-gray-500 dark:text-slate-400">Allowed Max Discount:</span>
              <span className="font-mono font-bold text-emerald-600 dark:text-emerald-400">
                {maxDiscount}%
              </span>
            </div>
          )}
          {requestedDiscount !== undefined && (
            <div className="flex justify-between items-center pt-1.5 border-t border-dashed border-gray-200 dark:border-slate-700 text-rose-600 dark:text-rose-400 font-semibold">
              <span>Requested Discount:</span>
              <span className="font-mono font-black text-sm">
                {requestedDiscount}%
              </span>
            </div>
          )}
        </div>

        <div className="space-y-1">
          <label className="block text-xs font-medium text-gray-700 dark:text-slate-300">
            Supervisor Password (required if current cashier lacks override permission)
          </label>
          <input
            type="password"
            value={supervisorPassword}
            onChange={(e) => setSupervisorPassword(e.target.value)}
            placeholder="Enter supervisor password..."
            className="w-full px-3 py-1.5 text-xs rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-amber-500"
          />
        </div>

        <div className="p-2.5 rounded-xl bg-amber-50/60 dark:bg-amber-950/20 border border-amber-200/50 dark:border-amber-800/40 text-[11px] text-amber-800 dark:text-amber-300 flex items-center gap-2">
          <ShieldCheckIcon className="w-4 h-4 shrink-0 text-amber-500" />
          <span>Supervisor override will be logged in the audit trail with this invoice.</span>
        </div>

        <div className="flex justify-end gap-2.5 pt-2">
          <button
            type="button"
            onClick={onClose}
            className={`px-4 py-2 text-xs font-medium theme-btn-secondary ${btnSecondary.className}`}
            style={btnSecondary.style}
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={() => onConfirm({ supervisor_password: supervisorPassword })}
            className={`px-4.5 py-2 text-xs font-semibold theme-btn-primary ${btnPrimary.className}`}
            style={btnPrimary.style}
          >
            Authorize & Proceed
          </button>
        </div>
      </div>
    </div>
  );
}

