import React, { useEffect, useMemo, useRef, useState } from "react";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import { KeyIcon, XMarkIcon, BackspaceIcon } from "@heroicons/react/24/outline";

/**
 * Wasila/Abuzar-style 4-digit operator passcode modal.
 * Captures 4 numeric digits via keyboard, numpad, or on-screen keypad.
 */
export default function OperatorPinModal({
  isOpen,
  onClose,
  onConfirm,
  title = "Enter 4-Digit Operator Code",
  subtitle = "Enter your unique 4-digit employee/user code to save and attribute this document.",
  errorMessage = "",
  autoSubmit = true,
  submitting = false,
}) {
  const { isDark, theme } = useTheme();
  const [pin, setPin] = useState("");
  const inputRef = useRef(null);
  const autoSubmittedRef = useRef(false);

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
  const btnClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark),
    [buttonStyle, themeColors, isDark]
  );

  // Reset and focus whenever opened or whenever an error arrives
  useEffect(() => {
    if (isOpen) {
      setPin("");
      autoSubmittedRef.current = false;
      const timer = setTimeout(() => {
        inputRef.current?.focus();
      }, 40);
      return () => clearTimeout(timer);
    }
  }, [isOpen, errorMessage]);

  // Auto-submit on 4th digit when enabled
  useEffect(() => {
    if (!isOpen || !autoSubmit || submitting) return;
    if (pin.length === 4 && !autoSubmittedRef.current) {
      autoSubmittedRef.current = true;
      onConfirm(pin);
    }
  }, [isOpen, autoSubmit, submitting, pin, onConfirm]);

  // Global keyboard handler while modal is open
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        if (!submitting) onClose?.();
        return;
      }
      if (e.key === "Enter") {
        e.preventDefault();
        e.stopPropagation();
        if (pin.length === 4 && !submitting) {
          autoSubmittedRef.current = true;
          onConfirm(pin);
        }
        return;
      }
    };
    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, [isOpen, pin, submitting, onClose, onConfirm]);

  if (!isOpen) return null;

  const handlePinChange = (raw) => {
    if (submitting) return;
    const digits = String(raw || "").replace(/\D/g, "").slice(0, 4);
    autoSubmittedRef.current = false;
    setPin(digits);
  };

  const appendDigit = (digit) => {
    if (submitting || pin.length >= 4) return;
    handlePinChange(pin + String(digit));
    inputRef.current?.focus();
  };

  const handleBackspace = () => {
    if (submitting || pin.length === 0) return;
    handlePinChange(pin.slice(0, -1));
    inputRef.current?.focus();
  };

  const handleClear = () => {
    if (submitting) return;
    setPin("");
    autoSubmittedRef.current = false;
    inputRef.current?.focus();
  };

  return (
    <div
      className="fixed inset-0 z-[120] flex items-center justify-center bg-black/60 backdrop-blur-sm p-4 animate-fadeIn"
      onClick={() => {
        if (!submitting) onClose?.();
      }}
    >
      <div
        className={`relative rounded-2xl shadow-2xl border max-w-sm w-full p-5 space-y-4 backdrop-blur-xl ${
          isDark
            ? "bg-slate-900/95 border-slate-700 text-slate-100"
            : "bg-white/95 border-gray-200 text-gray-900"
        }`}
        onClick={(e) => {
          e.stopPropagation();
          inputRef.current?.focus();
        }}
      >
        <button
          type="button"
          onClick={() => !submitting && onClose?.()}
          aria-label="Close"
          className="absolute top-3.5 right-3.5 p-1 rounded-lg text-gray-400 hover:text-gray-600 dark:hover:text-slate-200 hover:bg-gray-100 dark:hover:bg-slate-800 transition-colors"
        >
          <XMarkIcon className="w-5 h-5" />
        </button>

        {/* Header */}
        <div className="flex items-start gap-3 pr-6">
          <div
            className="p-2.5 rounded-xl shrink-0"
            style={{
              backgroundColor: isDark ? "rgba(59,130,246,0.18)" : "#dbeafe",
              color: themeColors.primary,
            }}
          >
            <KeyIcon className="w-6 h-6" />
          </div>
          <div className="min-w-0">
            <h3 className="text-base font-bold leading-snug">{title}</h3>
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 leading-relaxed">
              {subtitle}
            </p>
          </div>
        </div>

        {/* Hidden native input for seamless keyboard / numpad entry */}
        <div className="relative">
          <input
            ref={inputRef}
            type="password"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={4}
            value={pin}
            onChange={(e) => handlePinChange(e.target.value)}
            aria-label="4-digit operator code"
            className="sr-only"
          />

          {/* 4 Masked Digit Boxes */}
          <div
            className="grid grid-cols-4 gap-3 px-2 py-1 cursor-text"
            onClick={() => inputRef.current?.focus()}
          >
            {[0, 1, 2, 3].map((idx) => {
              const filled = idx < pin.length;
              const isCurrent = idx === pin.length && !submitting;
              return (
                <div
                  key={idx}
                  className={`h-14 rounded-xl border-2 flex items-center justify-center text-2xl font-black transition-all ${
                    errorMessage
                      ? "border-rose-500 bg-rose-50/70 dark:bg-rose-950/30 text-rose-600"
                      : filled
                      ? "border-blue-500 bg-blue-50/70 dark:bg-blue-950/30 text-blue-600 dark:text-blue-400"
                      : isCurrent
                      ? "border-blue-400 ring-2 ring-blue-500/20 bg-white dark:bg-slate-800"
                      : "border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800/70"
                  }`}
                >
                  {filled ? "•" : ""}
                </div>
              );
            })}
          </div>
        </div>

        {/* Error banner */}
        {errorMessage ? (
          <div className="px-3 py-2 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 text-xs font-medium text-rose-700 dark:text-rose-300 text-center">
            {errorMessage}
          </div>
        ) : (
          <div className="text-[11px] text-center text-gray-500 dark:text-slate-400">
            {autoSubmit
              ? "Type 4 digits — saves automatically on the 4th digit"
              : "Type 4 digits and press Enter to confirm"}
          </div>
        )}

        {/* Touch-friendly Numeric Keypad */}
        <div className="grid grid-cols-3 gap-2 pt-1">
          {[1, 2, 3, 4, 5, 6, 7, 8, 9].map((num) => (
            <button
              key={num}
              type="button"
              disabled={submitting}
              onClick={() => appendDigit(num)}
              className={`h-10 rounded-xl font-bold text-sm border transition-colors cursor-pointer ${
                isDark
                  ? "bg-slate-800 hover:bg-slate-700 border-slate-700 text-slate-100"
                  : "bg-gray-50 hover:bg-gray-100 border-gray-200 text-gray-800"
              }`}
            >
              {num}
            </button>
          ))}
          <button
            type="button"
            disabled={submitting}
            onClick={handleClear}
            className={`h-10 rounded-xl font-semibold text-xs border transition-colors cursor-pointer ${
              isDark
                ? "bg-slate-800/70 hover:bg-slate-700 border-slate-700 text-slate-300"
                : "bg-gray-50 hover:bg-gray-100 border-gray-200 text-gray-600"
            }`}
          >
            Clear
          </button>
          <button
            type="button"
            disabled={submitting}
            onClick={() => appendDigit(0)}
            className={`h-10 rounded-xl font-bold text-sm border transition-colors cursor-pointer ${
              isDark
                ? "bg-slate-800 hover:bg-slate-700 border-slate-700 text-slate-100"
                : "bg-gray-50 hover:bg-gray-100 border-gray-200 text-gray-800"
            }`}
          >
            0
          </button>
          <button
            type="button"
            disabled={submitting}
            onClick={handleBackspace}
            aria-label="Backspace"
            className={`h-10 rounded-xl flex items-center justify-center border transition-colors cursor-pointer ${
              isDark
                ? "bg-slate-800/70 hover:bg-slate-700 border-slate-700 text-slate-300"
                : "bg-gray-50 hover:bg-gray-100 border-gray-200 text-gray-600"
            }`}
          >
            <BackspaceIcon className="w-4 h-4" />
          </button>
        </div>

        {/* Footer Buttons */}
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            disabled={submitting}
            onClick={() => onClose?.()}
            className={`px-3.5 py-2 text-xs font-medium theme-btn-secondary ${btnClasses.secondary.className}`}
            style={btnClasses.secondary.style}
          >
            Cancel (Esc)
          </button>
          <button
            type="button"
            disabled={submitting || pin.length !== 4}
            onClick={() => {
              if (pin.length === 4 && !submitting) {
                autoSubmittedRef.current = true;
                onConfirm(pin);
              }
            }}
            className={`px-4 py-2 text-xs font-semibold theme-btn-primary disabled:opacity-50 ${btnClasses.primary.className}`}
            style={btnClasses.primary.style}
          >
            {submitting ? "Verifying…" : "Verify & Save (Enter)"}
          </button>
        </div>
      </div>
    </div>
  );
}
