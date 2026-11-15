// src/components/ModernDatePicker.jsx
import React, { useMemo, useRef, useImperativeHandle } from "react";
import DatePicker from "react-datepicker";
import "react-datepicker/dist/react-datepicker.css";
import {
  CalendarDaysIcon,
  XMarkIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  ChevronDownIcon,
} from "@heroicons/react/24/outline";

const MONTHS = [
  "January",
  "February",
  "March",
  "April",
  "May",
  "June",
  "July",
  "August",
  "September",
  "October",
  "November",
  "December",
];

/**
  * parseISODate — parse a "YYYY-MM-DD" or "YYYY-MM" string into a local Date
  * (avoids UTC midnight off-by-one in other timezones).
  */
export function parseISODate(str) {
  if (!str) return null;
  if (str instanceof Date) return Number.isNaN(str.getTime()) ? null : str;
  if (typeof str !== "string") return null;
  const trimmed = str.trim();
  if (!trimmed) return null;
  const datePart = trimmed.split("T")[0];
  const parts = datePart.split("-");
  if (parts.length === 2) {
    const y = parseInt(parts[0], 10);
    const m = parseInt(parts[1], 10);
    if (Number.isNaN(y) || Number.isNaN(m)) return null;
    return new Date(y, m - 1, 1);
  }
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
export function toISODate(date, monthOnly = false) {
  if (!date || !(date instanceof Date) || Number.isNaN(date.getTime())) return "";
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  if (monthOnly) return `${y}-${m}`;
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

/**
  * Custom button trigger used inside DatePicker when `editable` / `isExpiry` is false.
  */
const DatePickerInput = React.forwardRef(
  (
    {
      value,
      onClick,
      onKeyDown,
      placeholder,
      label,
      ariaLabel,
      id,
      name,
      disabled,
      size = "default",
      triggerClassName = "",
      title,
    },
    ref
  ) => {
    const isDense = size === "xs";
    const isCompact = size === "sm";

    return (
      <button
        type="button"
        id={id}
        name={name}
        ref={ref}
        disabled={disabled}
        onClick={onClick}
        onKeyDown={onKeyDown}
        title={title || value || placeholder}
        aria-label={ariaLabel || label || placeholder || "Choose date"}
        className={[
          "modern-datepicker-trigger group relative flex w-full items-center justify-between text-left transition-all duration-150",
          "border focus:outline-none focus-visible:ring-2",
          disabled ? "opacity-60 cursor-not-allowed" : "cursor-pointer",
          isDense
            ? "modern-datepicker-trigger-xs h-7 min-h-7 rounded-md px-1.5 py-0.5 text-[11px] gap-1"
            : isCompact
            ? "modern-datepicker-trigger-sm h-9 min-h-9 rounded-xl px-2.5 py-1 text-xs gap-2"
            : "modern-datepicker-trigger-default min-h-11 rounded-xl px-3.5 py-2 text-sm gap-2.5",
          triggerClassName,
        ]
          .filter(Boolean)
          .join(" ")}
      >
        <span
          className={[
            "flex items-center min-w-0 flex-1",
            isDense ? "gap-1" : isCompact ? "gap-2" : "gap-2.5",
          ].join(" ")}
        >
          <span
            className={[
              "modern-datepicker-icon-well flex shrink-0 items-center justify-center transition-colors",
              isDense
                ? "h-4.5 w-4.5 rounded"
                : isCompact
                ? "h-6 w-6 rounded-md"
                : "h-7 w-7 rounded-lg",
            ].join(" ")}
          >
            <CalendarDaysIcon
              className={
                isDense
                  ? "h-3 w-3 shrink-0"
                  : isCompact
                  ? "h-3.5 w-3.5 shrink-0"
                  : "h-4 w-4 shrink-0"
              }
            />
          </span>
          <span className="min-w-0 flex-1 truncate">
            {value ? (
              <span className="modern-datepicker-value font-semibold tabular-nums">
                {value}
              </span>
            ) : (
              <span className="modern-datepicker-placeholder font-medium">
                {placeholder}
              </span>
            )}
          </span>
        </span>
        {!isDense && (
          <ChevronDownIcon
            className={[
              "modern-datepicker-chevron shrink-0 transition-transform duration-200",
              isCompact ? "h-3.5 w-3.5" : "h-4 w-4",
            ].join(" ")}
          />
        )}
      </button>
    );
  }
);
DatePickerInput.displayName = "DatePickerInput";

/**
  * CalendarIconTrigger — icon-only button used when `editable` / `isExpiry`
  * is active so typing happens in the native date input while clicking the
  * calendar icon opens the custom ModernDatePicker popover.
  */
const CalendarIconTrigger = React.forwardRef(
  ({ onClick, disabled, size = "default", title, ariaLabel }, ref) => {
    const isDense = size === "xs";
    const isCompact = size === "sm";

    return (
      <button
        type="button"
        ref={ref}
        disabled={disabled}
        tabIndex={-1}
        onClick={(e) => {
          e.stopPropagation();
          onClick?.(e);
        }}
        title={title || "Open calendar"}
        aria-label={ariaLabel || "Open calendar"}
        className={[
          "modern-datepicker-icon-well modern-datepicker-cal-btn flex shrink-0 items-center justify-center transition-colors focus:outline-none focus-visible:ring-2",
          disabled ? "opacity-50 cursor-not-allowed" : "cursor-pointer",
          isDense
            ? "h-5 w-5 rounded"
            : isCompact
            ? "h-6.5 w-6.5 rounded-lg"
            : "h-8 w-8 rounded-lg",
        ]
          .filter(Boolean)
          .join(" ")}
      >
        <CalendarDaysIcon
          className={
            isDense
              ? "h-3.5 w-3.5 shrink-0"
              : isCompact
              ? "h-4 w-4 shrink-0"
              : "h-4.5 w-4.5 shrink-0"
          }
        />
      </button>
    );
  }
);
CalendarIconTrigger.displayName = "CalendarIconTrigger";

/**
  * ModernDatePicker
  * —————————————————————————————————————
  * A beautiful, responsive, theme-aware date picker built on
  * react-datepicker. Supports both a full button trigger and an
  * `editable` / `isExpiry` mode where the user can type dates
  * directly via keyboard and click the calendar icon to open
  * the custom popover calendar.
  */
const ModernDatePicker = React.forwardRef(function ModernDatePicker(
  {
    value,
    onChange,
    name,
    id,
    placeholder,
    label,
    ariaLabel,
    size = "default",
    editable = false,
    isExpiry = false,
    isClearable = true,
    showFooter = true,
    presets,
    className = "",
    triggerClassName = "",
    minDate,
    maxDate,
    min,
    max,
    startDate,
    endDate,
    disabled = false,
    required = false,
    showMonthYearPicker = false,
    dateFormat,
    portalId = "karobar-datepicker-portal",
    popperProps,
    onKeyDown,
    title,
    ...rest
  },
  forwardedRef
) {
  const pickerRef = useRef(null);
  const triggerBtnRef = useRef(null);
  const nativeInputRef = useRef(null);

  const isEditable = Boolean(editable || isExpiry) && !showMonthYearPicker;

  useImperativeHandle(forwardedRef, () => ({
    focus: () => {
      const el =
        nativeInputRef.current ||
        triggerBtnRef.current ||
        pickerRef.current?.input ||
        null;
      el?.focus?.();
    },
    select: () => {
      const el =
        nativeInputRef.current ||
        triggerBtnRef.current ||
        pickerRef.current?.input ||
        null;
      el?.focus?.();
    },
    setOpen: (open) => pickerRef.current?.setOpen?.(open),
  }));

  const effectiveMinDate = minDate !== undefined ? minDate : min;
  const effectiveMaxDate = maxDate !== undefined ? maxDate : max;

  // Convert the incoming string/Date → a Date object for react-datepicker
  const selected = useMemo(() => parseISODate(value), [value]);
  const parsedMinDate = useMemo(() => parseISODate(effectiveMinDate), [effectiveMinDate]);
  const parsedMaxDate = useMemo(() => parseISODate(effectiveMaxDate), [effectiveMaxDate]);
  const parsedStartDate = useMemo(() => parseISODate(startDate), [startDate]);
  const parsedEndDate = useMemo(() => parseISODate(endDate), [endDate]);

  const years = useMemo(() => {
    const currentYear = new Date().getFullYear();
    const startY = parsedMinDate ? Math.min(parsedMinDate.getFullYear(), currentYear - 15) : currentYear - 15;
    const endY = parsedMaxDate
      ? Math.max(parsedMaxDate.getFullYear(), currentYear + (isExpiry ? 15 : 12))
      : currentYear + (isExpiry ? 15 : 12);
    const list = [];
    for (let y = startY; y <= endY; y++) {
      list.push(y);
    }
    return list;
  }, [parsedMinDate, parsedMaxDate, isExpiry]);

  const emitChange = (isoStr) => {
    if (!onChange) return;
    const syntheticEvent = {
      target: { name: name || "", value: isoStr },
      currentTarget: { name: name || "", value: isoStr },
      preventDefault: () => {},
      stopPropagation: () => {},
    };
    onChange(isoStr, syntheticEvent);
  };

  const handleChange = (date) => {
    emitChange(toISODate(date, showMonthYearPicker));
    if (isEditable) {
      setTimeout(() => {
        nativeInputRef.current?.focus?.();
      }, 10);
    }
  };

  const handleClear = (e) => {
    e.stopPropagation();
    emitChange("");
    pickerRef.current?.setOpen?.(false);
    if (isEditable) {
      setTimeout(() => {
        nativeInputRef.current?.focus?.();
      }, 10);
    }
  };

  const selectQuickDate = (targetDate) => {
    const resolved = typeof targetDate === "function" ? targetDate() : targetDate;
    const parsed = parseISODate(resolved);
    if (parsed) {
      emitChange(toISODate(parsed, showMonthYearPicker));
    }
    pickerRef.current?.setOpen?.(false);
    if (isEditable) {
      setTimeout(() => {
        nativeInputRef.current?.focus?.();
      }, 10);
    }
  };

  const defaultSinglePresets = useMemo(
    () =>
      showMonthYearPicker
        ? [
            {
              label: "This Month",
              onClick: () => selectQuickDate(new Date()),
            },
            {
              label: "Last Month",
              onClick: () => {
                const d = new Date();
                selectQuickDate(new Date(d.getFullYear(), d.getMonth() - 1, 1));
              },
            },
          ]
        : isExpiry
        ? [
            {
              label: "+6M",
              onClick: () => {
                const d = new Date();
                d.setMonth(d.getMonth() + 6);
                selectQuickDate(d);
              },
            },
            {
              label: "+1Y",
              onClick: () => {
                const d = new Date();
                d.setFullYear(d.getFullYear() + 1);
                selectQuickDate(d);
              },
            },
            {
              label: "+2Y",
              onClick: () => {
                const d = new Date();
                d.setFullYear(d.getFullYear() + 2);
                selectQuickDate(d);
              },
            },
            {
              label: "Today",
              onClick: () => selectQuickDate(new Date()),
            },
          ]
        : [
            {
              label: "Today",
              onClick: () => selectQuickDate(new Date()),
            },
            {
              label: "Yesterday",
              onClick: () => {
                const d = new Date();
                d.setDate(d.getDate() - 1);
                selectQuickDate(d);
              },
            },
            {
              label: "Month Start",
              onClick: () => {
                const d = new Date();
                selectQuickDate(new Date(d.getFullYear(), d.getMonth(), 1));
              },
            },
          ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [onChange, showMonthYearPicker, isExpiry]
  );

  const activePresets = presets || defaultSinglePresets;

  const resolvedDateFormat =
    dateFormat ||
    (showMonthYearPicker
      ? "MMMM yyyy"
      : size === "xs"
      ? "MMM d, yy"
      : size === "sm"
      ? "MMM d, yyyy"
      : "EEE, MMM d, yyyy");

  const resolvedPlaceholder =
    placeholder || (showMonthYearPicker ? "Pick month" : "Pick a date");

  const customHeaderRenderer = ({
    date,
    changeYear,
    changeMonth,
    decreaseMonth,
    increaseMonth,
    decreaseYear,
    increaseYear,
    prevMonthButtonDisabled,
    nextMonthButtonDisabled,
    prevYearButtonDisabled,
    nextYearButtonDisabled,
  }) => (
    <div className="modern-datepicker-custom-header">
      <button
        type="button"
        onClick={showMonthYearPicker ? decreaseYear : decreaseMonth}
        disabled={showMonthYearPicker ? prevYearButtonDisabled : prevMonthButtonDisabled}
        className="modern-datepicker-nav-btn"
        aria-label={showMonthYearPicker ? "Previous year" : "Previous month"}
        title={showMonthYearPicker ? "Previous year" : "Previous month"}
      >
        <ChevronLeftIcon className="h-4 w-4" />
      </button>

      <div className="modern-datepicker-header-selects">
        {!showMonthYearPicker && (
          <div className="modern-datepicker-select-wrap">
            <select
              aria-label="Select month"
              value={MONTHS[date.getMonth()]}
              onChange={({ target: { value: mVal } }) =>
                changeMonth(MONTHS.indexOf(mVal))
              }
              className="modern-datepicker-select"
            >
              {MONTHS.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
            <ChevronDownIcon className="modern-datepicker-select-icon" />
          </div>
        )}

        <div className="modern-datepicker-select-wrap">
          <select
            aria-label="Select year"
            value={date.getFullYear()}
            onChange={({ target: { value: yVal } }) =>
              changeYear(Number(yVal))
            }
            className="modern-datepicker-select modern-datepicker-select-year"
          >
            {years.map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </select>
          <ChevronDownIcon className="modern-datepicker-select-icon" />
        </div>
      </div>

      <button
        type="button"
        onClick={showMonthYearPicker ? increaseYear : increaseMonth}
        disabled={showMonthYearPicker ? nextYearButtonDisabled : nextMonthButtonDisabled}
        className="modern-datepicker-nav-btn"
        aria-label={showMonthYearPicker ? "Next year" : "Next month"}
        title={showMonthYearPicker ? "Next year" : "Next month"}
      >
        <ChevronRightIcon className="h-4 w-4" />
      </button>
    </div>
  );

  const footerNode = showFooter && (
    <div className="modern-datepicker-footer">
      <div className="modern-datepicker-footer-presets">
        {activePresets.map((item) => (
          <button
            key={item.label}
            type="button"
            onClick={() => {
              if (item.onSelect) {
                item.onSelect();
                pickerRef.current?.setOpen?.(false);
              } else if (item.onClick) {
                item.onClick();
              } else if (item.date !== undefined) {
                selectQuickDate(item.date);
              }
            }}
            className="modern-datepicker-preset-pill"
          >
            {item.label}
          </button>
        ))}
      </div>
      {isClearable && selected && (
        <button
          type="button"
          onClick={handleClear}
          className="modern-datepicker-clear-footer-btn"
        >
          Clear
        </button>
      )}
    </div>
  );

  if (isEditable) {
    const isDense = size === "xs";
    const isCompact = size === "sm";
    const minIso =
      typeof effectiveMinDate === "string"
        ? effectiveMinDate
        : toISODate(parsedMinDate) || undefined;
    const maxIso =
      typeof effectiveMaxDate === "string"
        ? effectiveMaxDate
        : toISODate(parsedMaxDate) || undefined;

    return (
      <div
        className={[
          "modern-datepicker-root relative",
          isDense
            ? "modern-datepicker-root-xs"
            : isCompact
            ? "modern-datepicker-root-sm"
            : "",
          className,
        ]
          .filter(Boolean)
          .join(" ")}
      >
        <div
          className={[
            "modern-datepicker-trigger modern-datepicker-editable group relative flex w-full items-center justify-between transition-all duration-150",
            "border",
            disabled ? "opacity-60 cursor-not-allowed" : "",
            isDense
              ? "modern-datepicker-trigger-xs h-7 min-h-7 rounded-md pl-1 pr-0.5 py-0 text-[11px] gap-0.5"
              : isCompact
              ? "modern-datepicker-trigger-sm h-9 min-h-9 rounded-xl pl-2.5 pr-1 py-0.5 text-xs gap-1.5"
              : "modern-datepicker-trigger-default min-h-11 rounded-xl pl-3.5 pr-1.5 py-1 text-sm gap-2",
            triggerClassName,
          ]
            .filter(Boolean)
            .join(" ")}
        >
          <input
            ref={nativeInputRef}
            type="date"
            id={id}
            name={name}
            value={value || ""}
            min={minIso}
            max={maxIso}
            disabled={disabled}
            required={required}
            title={title || value || resolvedPlaceholder}
            aria-label={ariaLabel || rest["aria-label"] || label || resolvedPlaceholder}
            onChange={(e) => emitChange(e.target.value)}
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => {
              if (e.key === "F4" || (e.altKey && e.key === "ArrowDown")) {
                e.preventDefault();
                pickerRef.current?.setOpen?.(true);
                return;
              }
              if (onKeyDown) onKeyDown(e);
            }}
            className={[
              "modern-datepicker-native-input flex-1 min-w-0 bg-transparent border-0 p-0 focus:outline-none focus:ring-0 tabular-nums font-semibold",
              isDense
                ? "text-[10.5px] h-full"
                : isCompact
                ? "text-xs h-full"
                : "text-sm h-full",
            ].join(" ")}
            autoComplete="off"
          />

          <DatePicker
            ref={pickerRef}
            selected={selected}
            onChange={handleChange}
            minDate={parsedMinDate}
            maxDate={parsedMaxDate}
            startDate={parsedStartDate}
            endDate={parsedEndDate}
            disabled={disabled}
            portalId={portalId}
            popperProps={popperProps || { strategy: "fixed" }}
            popperPlacement={rest.popperPlacement || "bottom-end"}
            customInput={
              <CalendarIconTrigger
                disabled={disabled}
                size={size}
                title={`Open calendar (${label || ariaLabel || resolvedPlaceholder})`}
                ariaLabel={`Open calendar for ${label || ariaLabel || resolvedPlaceholder}`}
              />
            }
            dateFormat={resolvedDateFormat}
            isClearable={false}
            shouldCloseOnSelect={true}
            showPopperArrow={false}
            calendarClassName="modern-datepicker-calendar"
            wrapperClassName="modern-datepicker-icon-wrapper"
            popperClassName="modern-datepicker-popper"
            formatWeekDay={(nameOfDay) => nameOfDay.slice(0, 2)}
            renderCustomHeader={customHeaderRenderer}
            {...rest}
          >
            {footerNode}
          </DatePicker>
        </div>
      </div>
    );
  }

  return (
    <div
      className={[
        "modern-datepicker-root relative",
        size === "xs" ? "modern-datepicker-root-xs" : size === "sm" ? "modern-datepicker-root-sm" : "",
        className,
      ]
        .filter(Boolean)
        .join(" ")}
    >
      <DatePicker
        ref={pickerRef}
        selected={selected}
        onChange={handleChange}
        minDate={parsedMinDate}
        maxDate={parsedMaxDate}
        startDate={parsedStartDate}
        endDate={parsedEndDate}
        disabled={disabled}
        required={required}
        showMonthYearPicker={showMonthYearPicker}
        portalId={portalId}
        popperProps={popperProps || { strategy: "fixed" }}
        onKeyDown={onKeyDown}
        customInput={
          <DatePickerInput
            ref={triggerBtnRef}
            id={id}
            name={name}
            disabled={disabled}
            size={size}
            placeholder={resolvedPlaceholder}
            label={label}
            ariaLabel={ariaLabel || rest["aria-label"]}
            triggerClassName={triggerClassName}
            title={title}
          />
        }
        dateFormat={resolvedDateFormat}
        placeholderText={resolvedPlaceholder}
        isClearable={false}
        shouldCloseOnSelect={true}
        showPopperArrow={false}
        calendarClassName="modern-datepicker-calendar"
        wrapperClassName="modern-datepicker-wrapper"
        popperClassName="modern-datepicker-popper"
        formatWeekDay={(nameOfDay) => nameOfDay.slice(0, 2)}
        renderCustomHeader={customHeaderRenderer}
        {...rest}
      >
        {footerNode}
      </DatePicker>

      {/* Clear (X) button — only when a date is selected and isClearable is true */}
      {isClearable && selected && !disabled && (
        <button
          type="button"
          onClick={handleClear}
          className={[
            "modern-datepicker-clear-btn",
            size === "xs"
              ? "modern-datepicker-clear-btn-xs"
              : size === "sm"
              ? "modern-datepicker-clear-btn-sm"
              : "",
          ]
            .filter(Boolean)
            .join(" ")}
          aria-label="Clear date"
          title="Clear date"
        >
          <XMarkIcon className={size === "xs" ? "h-3 w-3" : "h-3.5 w-3.5"} />
        </button>
      )}
    </div>
  );
});

export default ModernDatePicker;
