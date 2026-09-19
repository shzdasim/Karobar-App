// src/components/ModernDatePicker.jsx
import React, { useMemo } from "react";
import DatePicker from "react-datepicker";
import "react-datepicker/dist/react-datepicker.css";
import {
  CalendarDaysIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";

/**
 * parseISODate — parse a "YYYY-MM-DD" string into a local Date
 * (avoids UTC midnight off-by-one in other timezones).
 */
function parseISODate(str) {
  if (!str) return null;
  if (str instanceof Date) return str;
  if (typeof str !== "string") return null;
  const parts = str.split("-");
  if (parts.length !== 3) return null;
  const y = parseInt(parts[0], 10);
  const m = parseInt(parts[1], 10);
  const d = parseInt(parts[2], 10);
  if (Number.isNaN(y) || Number.isNaN(m) || Number.isNaN(d)) return null;
  return new Date(y, m - 1, d);
}

/**
 * toISODate — format a Date into a "YYYY-MM-DD" string (local).
 */
function toISODate(date) {
  if (!date) return "";
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

/**
 * formatDisplay — short human-readable date for the input field.
 */
function formatDisplay(date) {
  if (!date) return "";
  return date.toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

/**
 * Custom input element used inside the DatePicker.
 * Receives `value`, `onClick`, and `ref` from react-datepicker
 * via React.cloneElement.
 */
const DatePickerInput = React.forwardRef(
  ({ value, onClick, placeholder }, ref) => (
    <div className="relative flex items-center">
      <input
        type="text"
        readOnly
        ref={ref}
        value={value || ""}
        onClick={onClick}
        placeholder={placeholder}
        className={[
          "w-full cursor-pointer rounded-xl border bg-white/70",
          "px-3 py-2 pl-9 pr-8 text-sm text-gray-800",
          "placeholder-gray-400 dark:text-gray-100 dark:placeholder-gray-500",
          "focus:outline-none focus:ring-2 focus:ring-blue-500/40",
          "dark:bg-slate-700/60 dark:border-slate-600 dark:focus:ring-indigo-400/40",
          "transition-all duration-200",
        ].join(" ")}
      />
      <CalendarDaysIcon className="absolute left-2.5 w-4 h-4 text-gray-400 dark:text-gray-500 pointer-events-none" />
    </div>
  )
);
DatePickerInput.displayName = "DatePickerInput";

/**
 * ModernDatePicker
 * —————————————————————————————————————
 * A beautiful, responsive, theme-aware date picker built on
 * react-datepicker.  Drop-in replacement for native
 * <input type="date"> when the value is a "YYYY-MM-DD" string.
 *
 * Props:
 *   value          string | Date | null   (YYYY-MM-DD)
 *   onChange       (string:YYYY-MM-DD | "") => void
 *   placeholder    string
 *   isClearable    boolean (default true)
 *   ...rest         forwarded to <DatePicker />
 */
export default function ModernDatePicker({
  value,
  onChange,
  placeholder = "Pick a date",
  isClearable = true,
  showMonthDropdown = true,
  showYearDropdown = true,
  dropdownMode = "select",
  className = "",
  ...rest
}) {
  // Convert the incoming string/Date → a Date object for react-datepicker
  const selected = useMemo(() => parseISODate(value), [value]);

  const handleChange = (date) => {
    onChange?.(toISODate(date));
  };

  const handleClear = (e) => {
    e.stopPropagation();
    onChange?.("");
  };

  return (
    <div className={["relative", className].join(" ")}>
      <DatePicker
        selected={selected}
        onChange={handleChange}
        customInput={<DatePickerInput placeholder={placeholder} />}
        dateFormat="MMM dd, yyyy"
        placeholderText={placeholder}
        showMonthDropdown={showMonthDropdown}
        showYearDropdown={showYearDropdown}
        dropdownMode={dropdownMode}
        isClearable={false}
        calendarClassName="modern-datepicker-calendar"
        wrapperClassName="modern-datepicker-wrapper"
        popperClassName="modern-datepicker-popper"
        transitionTime={150}
        {...rest}
      />

      {/* Clear (X) button — only when a date is selected */}
      {isClearable && selected && (
        <button
          type="button"
          onClick={handleClear}
          className={[
            "absolute right-1.5 top-1/2 -translate-y-1/2 z-10",
            "rounded-full p-0.5",
            "hover:bg-gray-200 dark:hover:bg-slate-600",
            "transition-colors",
          ].join(" ")}
          aria-label="Clear date"
        >
          <XMarkIcon className="w-3.5 h-3.5 text-gray-500 dark:text-gray-400" />
        </button>
      )}
    </div>
  );
}
