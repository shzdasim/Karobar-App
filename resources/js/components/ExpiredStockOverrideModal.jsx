import React, { useMemo } from "react";
import { ExclamationTriangleIcon, ShieldExclamationIcon, XMarkIcon, CheckIcon } from "@heroicons/react/24/outline";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";

export default function ExpiredStockOverrideModal({
  open,
  onClose,
  data,
  onConfirmOverride,
}) {
  const { isDark, theme, buttonStyle } = useTheme();

  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || "#3b82f6",
    primaryHover: theme?.primary_hover || "#2563eb",
    primaryLight: theme?.primary_light || "#dbeafe",
  }), [theme]);

  const themeButtonClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark),
    [buttonStyle, themeColors, isDark]
  );

  if (!open || !data) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-xs animate-fadeIn">
      <div className="relative w-full max-w-md p-6 overflow-hidden transition-all transform bg-white/95 dark:bg-slate-900/95 border border-red-500/30 rounded-2xl shadow-2xl backdrop-blur-md">
        {/* Close Button */}
        <button
          onClick={onClose}
          className="absolute top-4 right-4 p-1 rounded-lg text-gray-400 hover:text-gray-600 dark:hover:text-slate-200 transition-colors"
        >
          <XMarkIcon className="w-5 h-5" />
        </button>

        {/* Header with warning icon */}
        <div className="flex items-center gap-3 mb-4">
          <div className="flex items-center justify-center w-12 h-12 rounded-xl bg-red-100 dark:bg-red-950/50 text-red-600 dark:text-red-400 border border-red-200 dark:border-red-800">
            <ShieldExclamationIcon className="w-7 h-7" />
          </div>
          <div>
            <h3 className="text-lg font-bold text-gray-900 dark:text-white">
              Expired Stock Lock
            </h3>
            <p className="text-xs text-red-600 dark:text-red-400 font-medium">
              Supervisor authorization required to sell expired items
            </p>
          </div>
        </div>

        {/* Warning Banner */}
        <div className="p-3 mb-4 rounded-xl bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-800/60 text-xs text-red-800 dark:text-red-300 flex items-start gap-2">
          <ExclamationTriangleIcon className="w-4 h-4 shrink-0 text-red-500 mt-0.5" />
          <div>
            <strong>Notice:</strong> This batch has passed its expiration date. Selling expired products may violate pharmaceutical or regulatory standards.
          </div>
        </div>

        {/* Detail Breakdown */}
        <div className="p-4 mb-5 rounded-xl bg-gray-50/80 dark:bg-slate-800/60 border border-gray-200/80 dark:border-slate-700/80 space-y-2 text-sm">
          <div className="flex justify-between items-center">
            <span className="text-gray-500 dark:text-slate-400">Product:</span>
            <span className="font-semibold text-gray-800 dark:text-slate-100 truncate max-w-[200px]">
              {data.product_name || "Product"}
            </span>
          </div>
          {data.batch_number && (
            <div className="flex justify-between items-center">
              <span className="text-gray-500 dark:text-slate-400">Batch Number:</span>
              <span className="font-mono font-medium text-gray-800 dark:text-slate-200">
                {data.batch_number}
              </span>
            </div>
          )}
          <div className="flex justify-between items-center">
            <span className="text-gray-500 dark:text-slate-400">Expiry Date:</span>
            <span className="font-mono font-bold text-red-600 dark:text-red-400">
              {data.expiry_date || "Expired"}
            </span>
          </div>
        </div>

        {/* Actions */}
        <div className="flex items-center justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className={`${themeButtonClasses.secondary} px-4 py-2 text-sm rounded-xl transition-all cursor-pointer`}
          >
            Cancel Sale
          </button>
          <button
            type="button"
            onClick={onConfirmOverride}
            className={`${themeButtonClasses.danger || themeButtonClasses.primary} px-4 py-2 text-sm rounded-xl font-semibold flex items-center gap-2 shadow-lg transition-all cursor-pointer`}
          >
            <CheckIcon className="w-4 h-4" />
            Supervisor Authorize
          </button>
        </div>
      </div>
    </div>
  );
}

