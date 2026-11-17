import React from "react";

export default function SettingSwitch({
  id,
  checked,
  onChange,
  disabled = false,
  labelOn = "",
  labelOff = "",
  isDark = false,
}) {
  return (
    <label
      htmlFor={id}
      className={`inline-flex items-center gap-2.5 select-none shrink-0 ${
        disabled ? "opacity-60 cursor-not-allowed" : "cursor-pointer"
      }`}
    >
      <input
        id={id}
        type="checkbox"
        checked={Boolean(checked)}
        disabled={disabled}
        onChange={(e) => onChange?.(e.target.checked)}
        className="sr-only peer"
      />
      <span
        aria-hidden="true"
        className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors duration-200 ${
          checked
            ? "bg-emerald-500"
            : isDark
            ? "bg-slate-700"
            : "bg-slate-300 dark:bg-slate-700"
        }`}
      >
        <span
          className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-xs transition-transform duration-200 ${
            checked ? "translate-x-6" : "translate-x-1"
          }`}
        />
      </span>
      {(labelOn || labelOff) && (
        <span className="text-xs font-bold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">
          {checked ? labelOn : labelOff}
        </span>
      )}
    </label>
  );
}
