import React, { useMemo, useState, useEffect } from "react";
import Select from "react-select";
import { useNavigate } from "react-router-dom";
import {
  ClockIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  NoSymbolIcon,
  InformationCircleIcon,
} from "@heroicons/react/24/outline";

export const EXPIRY_POLICY_PRESETS = [
  {
    value: "before_1",
    label: "1 Month Before Expiry (30 Days)",
    policy: "before_expiry",
    months: 1,
    days: 30,
  },
  {
    value: "before_2",
    label: "2 Months Before Expiry (60 Days)",
    policy: "before_expiry",
    months: 2,
    days: 60,
  },
  {
    value: "before_3",
    label: "3 Months Before Expiry (90 Days)",
    policy: "before_expiry",
    months: 3,
    days: 90,
  },
  {
    value: "before_4",
    label: "4 Months Before Expiry (120 Days)",
    policy: "before_expiry",
    months: 4,
    days: 120,
  },
  {
    value: "before_6",
    label: "6 Months Before Expiry (180 Days)",
    policy: "before_expiry",
    months: 6,
    days: 180,
  },
  {
    value: "before_9",
    label: "9 Months Before Expiry (270 Days)",
    policy: "before_expiry",
    months: 9,
    days: 270,
  },
  {
    value: "before_12",
    label: "1 Year Before Expiry (12 Months / 365 Days)",
    policy: "before_expiry",
    months: 12,
    days: 365,
  },
  {
    value: "before_18",
    label: "1.5 Years Before Expiry (18 Months / 540 Days)",
    policy: "before_expiry",
    months: 18,
    days: 540,
  },
  {
    value: "before_24",
    label: "2 Years Before Expiry (24 Months / 730 Days)",
    policy: "before_expiry",
    months: 24,
    days: 730,
  },
  {
    value: "custom_before_expiry",
    label: "Custom Period Before Expiry…",
    policy: "before_expiry",
    months: null,
    days: null,
  },
  {
    value: "accepts_expired",
    label: "Accepts Expired Products (On or After Expiry)",
    policy: "accepts_expired",
    months: 0,
    days: 0,
  },
  {
    value: "non_returnable",
    label: "Non-Returnable (No Expiry Return Accepted)",
    policy: "non_returnable",
    months: 0,
    days: 0,
  },
];

export const EXPIRY_POLICY_FILTER_OPTIONS = [
  { value: "all", label: "All Expiry Return Policies" },
  { value: "before_3", label: "3 Months Before Expiry" },
  { value: "before_6", label: "6 Months Before Expiry" },
  { value: "before_12", label: "1 Year Before Expiry" },
  { value: "before_24", label: "2 Years Before Expiry" },
  { value: "before_expiry", label: "All Before-Expiry Policies" },
  { value: "accepts_expired", label: "Accepts Expired Products" },
  { value: "non_returnable", label: "Non-Returnable" },
];

export function getReactSelectStyles(isDark = false, primaryColor = "#3b82f6") {
  return {
    control: (base, state) => ({
      ...base,
      minHeight: "38px",
      borderRadius: "0.75rem",
      backgroundColor: isDark ? "rgba(30, 41, 59, 0.85)" : "#ffffff",
      borderColor: state.isFocused
        ? primaryColor
        : isDark
        ? "rgba(71, 85, 105, 0.8)"
        : "rgba(203, 213, 225, 0.9)",
      boxShadow: state.isFocused ? `0 0 0 2px ${primaryColor}33` : "none",
      fontSize: "0.8125rem",
    }),
    valueContainer: (base) => ({ ...base, padding: "0 10px" }),
    singleValue: (base) => ({
      ...base,
      color: isDark ? "#f8fafc" : "#0f172a",
      fontWeight: 500,
    }),
    input: (base) => ({
      ...base,
      color: isDark ? "#f8fafc" : "#0f172a",
      margin: 0,
      padding: 0,
    }),
    menuPortal: (base) => ({ ...base, zIndex: 99999 }),
    menu: (base) => ({
      ...base,
      backgroundColor: isDark ? "#0f172a" : "#ffffff",
      borderRadius: "0.75rem",
      border: isDark ? "1px solid #334155" : "1px solid #e2e8f0",
      zIndex: 99999,
      overflow: "hidden",
    }),
    option: (base, state) => ({
      ...base,
      fontSize: "0.8125rem",
      backgroundColor: state.isSelected
        ? primaryColor
        : state.isFocused
        ? isDark
          ? "#1e293b"
          : "#eff6ff"
        : "transparent",
      color: state.isSelected ? "#ffffff" : isDark ? "#f8fafc" : "#0f172a",
      cursor: "pointer",
    }),
  };
}

export function resolvePresetFromPolicy(policy, months, days) {
  const p = policy || "before_expiry";
  if (p === "accepts_expired") return "accepts_expired";
  if (p === "non_returnable") return "non_returnable";

  const m = Number(months ?? 0);
  const d = Number(days ?? 0);

  const matchByMonth = EXPIRY_POLICY_PRESETS.find(
    (opt) => opt.policy === "before_expiry" && opt.months !== null && opt.months === m && m > 0
  );
  if (matchByMonth && (d === 0 || d === matchByMonth.days || d === m * 30)) {
    return matchByMonth.value;
  }

  if (m === 0 && d === 0) {
    return "before_3";
  }

  return "custom_before_expiry";
}

export function formatExpiryPolicyText(policy, months, days, existingLabel) {
  if (existingLabel) return existingLabel;
  const p = policy || "before_expiry";
  if (p === "accepts_expired") return "Accepts Expired Products";
  if (p === "non_returnable") return "Non-Returnable";

  const m = Number(months ?? 0);
  const d = Number(days ?? 0);
  if (m > 0) {
    if (m % 12 === 0) {
      const yrs = m / 12;
      return yrs === 1 ? "1 Year Before Expiry" : `${yrs} Years Before Expiry`;
    }
    return m === 1 ? "1 Month Before Expiry" : `${m} Months Before Expiry`;
  }
  if (d > 0) {
    return `${d} Days Before Expiry`;
  }
  return "3 Months Before Expiry";
}

export function ExpiryPolicyBadge({ policy, months, days, label }) {
  const p = policy || "before_expiry";
  const text = formatExpiryPolicyText(p, months, days, label);

  if (p === "accepts_expired") {
    return (
      <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 border border-emerald-500/30">
        <CheckCircleIcon className="w-3.5 h-3.5" />
        <span>{text}</span>
      </span>
    );
  }

  if (p === "non_returnable") {
    return (
      <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-rose-500/15 text-rose-700 dark:text-rose-300 border border-rose-500/30">
        <NoSymbolIcon className="w-3.5 h-3.5" />
        <span>{text}</span>
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-blue-500/15 text-blue-700 dark:text-blue-300 border border-blue-500/30">
      <ClockIcon className="w-3.5 h-3.5" />
      <span>{text}</span>
    </span>
  );
}

/**
 * Reusable Expiry Return Policy Form Section (uses React Select for all dropdowns)
 */
export function ExpiryReturnPolicyFields({
  value = {},
  onChange,
  isDark = false,
  primaryColor = "#3b82f6",
  disabled = false,
  policySelectRef = null,
  notesInputRef = null,
  onEnterLastField = null,
}) {
  const policy = value.expiry_return_policy || "before_expiry";
  const months = value.expiry_return_months ?? 3;
  const days = value.expiry_return_days ?? 90;
  const notes = value.expiry_return_notes || "";

  const internalNotesRef = React.useRef(null);
  const customDurationRef = React.useRef(null);
  const effectiveNotesRef = notesInputRef || internalNotesRef;

  const [selectedPreset, setSelectedPreset] = useState(() =>
    resolvePresetFromPolicy(policy, months, days)
  );
  const [customUnit, setCustomUnit] = useState("months"); // 'months' | 'days'

  useEffect(() => {
    const resolved = resolvePresetFromPolicy(
      value.expiry_return_policy,
      value.expiry_return_months,
      value.expiry_return_days
    );
    setSelectedPreset(resolved);
  }, [value.expiry_return_policy, value.expiry_return_months, value.expiry_return_days]);

  const selectStyles = useMemo(
    () => getReactSelectStyles(isDark, primaryColor),
    [isDark, primaryColor]
  );

  const unitOptions = useMemo(
    () => [
      { value: "months", label: "Months Before Expiry" },
      { value: "days", label: "Days Before Expiry" },
    ],
    []
  );

  const handlePresetChange = (opt) => {
    if (!opt) return;
    setSelectedPreset(opt.value);

    if (opt.value === "custom_before_expiry") {
      onChange?.({
        ...value,
        expiry_return_policy: "before_expiry",
        expiry_return_months: Number(months) > 0 ? Number(months) : 5,
        expiry_return_days: Number(days) > 0 ? Number(days) : 150,
      });
      setTimeout(() => {
        customDurationRef.current?.focus();
        customDurationRef.current?.select?.();
      }, 30);
      return;
    }

    onChange?.({
      ...value,
      expiry_return_policy: opt.policy,
      expiry_return_months: opt.months ?? 0,
      expiry_return_days: opt.days ?? 0,
    });
    setTimeout(() => {
      effectiveNotesRef.current?.focus();
    }, 30);
  };

  const handleCustomDurationChange = (rawNumber, unit = customUnit) => {
    const num = Math.max(1, parseInt(rawNumber || "1", 10) || 1);
    if (unit === "months") {
      const calcDays = num === 12 ? 365 : num === 24 ? 730 : num * 30;
      onChange?.({
        ...value,
        expiry_return_policy: "before_expiry",
        expiry_return_months: num,
        expiry_return_days: calcDays,
      });
    } else {
      const calcMonths = Math.round(num / 30);
      onChange?.({
        ...value,
        expiry_return_policy: "before_expiry",
        expiry_return_months: calcMonths,
        expiry_return_days: num,
      });
    }
  };

  const activePresetOption =
    EXPIRY_POLICY_PRESETS.find((o) => o.value === selectedPreset) ||
    EXPIRY_POLICY_PRESETS[2];

  return (
    <div className="p-4 rounded-2xl border border-blue-200/80 dark:border-slate-700 bg-blue-50/40 dark:bg-slate-800/60 space-y-3.5">
      <div className="flex items-center justify-between flex-wrap gap-2">
        <div className="flex items-center gap-2">
          <div className="p-1.5 rounded-lg bg-blue-500/15 text-blue-600 dark:text-blue-400">
            <ClockIcon className="w-4 h-4" />
          </div>
          <div>
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-800 dark:text-slate-100">
              Expiry Return Policy (Product Intimation)
            </h3>
            <p className="text-[11px] text-slate-500 dark:text-slate-400">
              Configure when near-expiry or expired products must be intimated and returned.
            </p>
          </div>
        </div>

        <ExpiryPolicyBadge policy={policy} months={months} days={days} />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1">
            Return / Intimation Policy *
          </label>
          <Select
            ref={policySelectRef}
            options={EXPIRY_POLICY_PRESETS}
            value={activePresetOption}
            onChange={handlePresetChange}
            isDisabled={disabled}
            styles={selectStyles}
            menuPortalTarget={typeof document !== "undefined" ? document.body : null}
            placeholder="Select return policy..."
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.defaultPrevented) {
                // If user hits Enter while menu is closed, advance to next field
                setTimeout(() => {
                  if (selectedPreset === "custom_before_expiry") {
                    customDurationRef.current?.focus();
                  } else {
                    effectiveNotesRef.current?.focus();
                  }
                }, 20);
              }
            }}
          />
        </div>

        <div>
          <div className="flex items-center justify-between mb-1">
            <label className="block text-xs font-semibold text-slate-700 dark:text-slate-200">
              Policy Notes / Return Conditions (Optional)
            </label>
            <span className="text-[10px] font-semibold text-emerald-600 dark:text-emerald-400">
              Enter ↵ Saves
            </span>
          </div>
          <input
            ref={effectiveNotesRef}
            type="text"
            disabled={disabled}
            value={notes}
            onChange={(e) =>
              onChange?.({
                ...value,
                expiry_return_notes: e.target.value,
              })
            }
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                onEnterLastField?.(e);
              }
            }}
            placeholder="e.g. Intimate distributor 15 days prior with invoice reference"
            className="w-full h-[38px] px-3 rounded-xl border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-xs text-slate-900 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-blue-500"
          />
        </div>
      </div>

      {selectedPreset === "custom_before_expiry" && policy === "before_expiry" && (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 pt-1">
          <div>
            <label className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1">
              Time Unit
            </label>
            <Select
              options={unitOptions}
              value={unitOptions.find((u) => u.value === customUnit)}
              onChange={(opt) => {
                const nextUnit = opt?.value || "months";
                setCustomUnit(nextUnit);
                handleCustomDurationChange(
                  nextUnit === "months" ? months || 6 : days || 180,
                  nextUnit
                );
                setTimeout(() => {
                  customDurationRef.current?.focus();
                  customDurationRef.current?.select?.();
                }, 20);
              }}
              isDisabled={disabled}
              styles={selectStyles}
              menuPortalTarget={typeof document !== "undefined" ? document.body : null}
            />
          </div>
          <div>
            <label className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1">
              {customUnit === "months" ? "Months Before Expiry" : "Days Before Expiry"}
            </label>
            <input
              ref={customDurationRef}
              type="number"
              min="1"
              max={customUnit === "months" ? 120 : 3650}
              disabled={disabled}
              value={customUnit === "months" ? months ?? 6 : days ?? 180}
              onChange={(e) => handleCustomDurationChange(e.target.value, customUnit)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  effectiveNotesRef.current?.focus();
                }
              }}
              className="w-full h-[38px] px-3 rounded-xl border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-xs font-mono font-bold text-slate-900 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-blue-500"
            />
          </div>
        </div>
      )}

      <div className="flex items-center gap-2 text-[11px] text-slate-600 dark:text-slate-300 bg-white/80 dark:bg-slate-900/60 px-3 py-2 rounded-xl border border-slate-200/70 dark:border-slate-700/70">
        <InformationCircleIcon className="w-4 h-4 text-blue-500 shrink-0" />
        {policy === "accepts_expired" ? (
          <span>
            Products under this policy can be returned <strong>on or after expiry</strong>. Batches expiring within 30 days or already expired will appear in the Intimation Queue.
          </span>
        ) : policy === "non_returnable" ? (
          <span>
            Products under this policy are marked <strong>Non-Returnable</strong> for expiry returns.
          </span>
        ) : (
          <span>
            Batches must be returned at least{" "}
            <strong>
              {Number(months) > 0 ? `${months} month(s)` : `${days} day(s)`} ({days} days)
            </strong>{" "}
            before their expiry date. The system automatically alerts you 30 days ahead of this return cutoff.
          </span>
        )}
      </div>
    </div>
  );
}

/**
 * Live Product Batches Intimation Table for a Brand or Supplier
 */
export function ExpiryIntimationTable({
  batches = [],
  entityLabel = "Brand",
  policy,
  months,
  days,
}) {
  const navigate = useNavigate();

  const dueCount = useMemo(
    () => batches.filter((b) => b.is_intimation_due).length,
    [batches]
  );

  return (
    <div className="bg-white dark:bg-slate-800/90 border border-slate-200/80 dark:border-slate-700/80 rounded-2xl shadow-xs overflow-hidden">
      <div className="p-4 border-b border-slate-200/80 dark:border-slate-700/80 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold text-slate-900 dark:text-white flex items-center gap-2">
            <ClockIcon className="w-4 h-4 text-blue-500" />
            <span>Product Expiry Return Intimation Status</span>
            <span className="px-2 py-0.5 rounded-full text-[11px] font-bold bg-slate-100 dark:bg-slate-700 text-slate-700 dark:text-slate-300">
              {batches.length} Active Batches
            </span>
            {dueCount > 0 && (
              <span className="px-2.5 py-0.5 rounded-full text-[11px] font-bold bg-amber-500/15 text-amber-700 dark:text-amber-300 border border-amber-500/30">
                {dueCount} Due for Intimation / Return
              </span>
            )}
          </h3>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            Active batches evaluated against this {entityLabel.toLowerCase()}&apos;s{" "}
            <strong>{formatExpiryPolicyText(policy, months, days)}</strong> policy.
          </p>
        </div>

        <ExpiryPolicyBadge policy={policy} months={months} days={days} />
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-left border-collapse text-xs">
          <thead>
            <tr className="border-b border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
              <th className="py-2.5 px-3.5 font-semibold">Product</th>
              <th className="py-2.5 px-3.5 font-semibold">Batch #</th>
              <th className="py-2.5 px-3.5 font-semibold text-right">On-Hand Qty</th>
              <th className="py-2.5 px-3.5 font-semibold">Expiry Date</th>
              <th className="py-2.5 px-3.5 font-semibold">Last Return / Intimation Date</th>
              <th className="py-2.5 px-3.5 font-semibold">Intimation Status</th>
              <th className="py-2.5 px-3.5 font-semibold text-right">Action</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-200/70 dark:divide-slate-700/60">
            {batches.length === 0 ? (
              <tr>
                <td colSpan={7} className="py-8 text-center text-slate-400">
                  No active batches with expiry dates found for this {entityLabel.toLowerCase()}.
                </td>
              </tr>
            ) : (
              batches.map((b) => {
                const status = b.intimation_status;
                return (
                  <tr
                    key={b.batch_id}
                    className="hover:bg-blue-50/40 dark:hover:bg-slate-700/40 transition-colors"
                  >
                    <td className="py-2.5 px-3.5">
                      <div className="font-bold text-slate-900 dark:text-slate-100">
                        {b.product_name}
                      </div>
                      <div className="text-[11px] text-slate-500 dark:text-slate-400">
                        {b.product_code || "—"}
                        {b.supplier_name ? ` • Supplier: ${b.supplier_name}` : ""}
                        {b.brand_name ? ` • Brand: ${b.brand_name}` : ""}
                      </div>
                    </td>
                    <td className="py-2.5 px-3.5 font-mono font-bold text-slate-800 dark:text-slate-200">
                      {b.batch_number}
                    </td>
                    <td className="py-2.5 px-3.5 text-right font-mono font-bold text-slate-900 dark:text-white">
                      {b.quantity}
                    </td>
                    <td className="py-2.5 px-3.5 font-mono">
                      <div>{b.expiry_date}</div>
                      <div className="text-[10px] text-slate-500">
                        {b.days_remaining < 0
                          ? `Expired ${Math.abs(b.days_remaining)}d ago`
                          : `${b.days_remaining}d until expiry`}
                      </div>
                    </td>
                    <td className="py-2.5 px-3.5 font-mono">
                      {b.return_deadline_date ? (
                        <>
                          <div className="font-bold text-slate-800 dark:text-slate-200">
                            {b.return_deadline_date}
                          </div>
                          <div className="text-[10px] text-slate-500">
                            {b.days_until_return_deadline < 0
                              ? `${Math.abs(b.days_until_return_deadline)}d past prior cutoff`
                              : `${b.days_until_return_deadline}d left to intimate`}
                          </div>
                        </>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="py-2.5 px-3.5">
                      {status === "intimate_now" ? (
                        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-bold bg-amber-500/15 text-amber-700 dark:text-amber-300 border border-amber-500/30">
                          <ExclamationTriangleIcon className="w-3.5 h-3.5" />
                          <span>Intimate Now (Cutoff Approaching)</span>
                        </span>
                      ) : status === "return_window_open" ? (
                        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-bold bg-rose-500/15 text-rose-700 dark:text-rose-300 border border-rose-500/30">
                          <ExclamationTriangleIcon className="w-3.5 h-3.5" />
                          <span>Inside Return Window — Return Immediately</span>
                        </span>
                      ) : status === "expired_returnable" ? (
                        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-bold bg-purple-500/15 text-purple-700 dark:text-purple-300 border border-purple-500/30">
                          <CheckCircleIcon className="w-3.5 h-3.5" />
                          <span>Expired — Returnable Under Policy</span>
                        </span>
                      ) : status === "expired_window_missed" ? (
                        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-bold bg-red-500/15 text-red-700 dark:text-red-300 border border-red-500/30">
                          <NoSymbolIcon className="w-3.5 h-3.5" />
                          <span>Expired (Before-Expiry Cutoff Missed)</span>
                        </span>
                      ) : status === "non_returnable" || status === "expired_non_returnable" ? (
                        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-slate-200 dark:bg-slate-700 text-slate-600 dark:text-slate-300">
                          <span>Non-Returnable</span>
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-emerald-500/15 text-emerald-700 dark:text-emerald-300">
                          <CheckCircleIcon className="w-3.5 h-3.5" />
                          <span>Safe</span>
                        </span>
                      )}
                    </td>
                    <td className="py-2.5 px-3.5 text-right">
                      <button
                        type="button"
                        onClick={() =>
                          navigate(
                            `/product-batches?product_id=${b.product_id}&batch_id=${b.batch_id}`
                          )
                        }
                        className="px-2.5 py-1 rounded-lg text-[11px] font-semibold bg-blue-500/15 text-blue-700 dark:text-blue-300 hover:bg-blue-500/25 cursor-pointer"
                      >
                        Manage Batch
                      </button>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

