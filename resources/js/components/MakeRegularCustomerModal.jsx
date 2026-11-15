// resources/js/components/MakeRegularCustomerModal.jsx
import React, { useState, useEffect, useMemo, useCallback } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import AsyncSelect from "react-select/async";
import {
  XMarkIcon,
  SparklesIcon,
  CheckCircleIcon,
  CalendarDaysIcon,
  UserIcon,
  ArrowPathIcon,
  InformationCircleIcon,
  UserPlusIcon,
} from "@heroicons/react/24/outline";
import { useTheme } from "@/context/ThemeContext";
import ModernDatePicker from "@/components/ModernDatePicker.jsx";

const DOSAGE_SHORTCUTS = [
  "1+0+1",
  "1+1+1",
  "1+0+0",
  "0+0+1",
  "After Meal",
  "Before Meal",
  "صبح اور شام",
];

const REFILL_CYCLE_OPTIONS = [
  { value: "monthly", label: "Monthly (Every 30 Days)" },
  { value: "weekly", label: "Weekly (Every 7 Days)" },
  { value: "biweekly", label: "Bi-Weekly (Every 14 Days)" },
  { value: "custom", label: "Custom Days (Set manual interval)" },
];

const REMINDER_LEAD_OPTIONS = [
  { value: 1, label: "1 Day before due date" },
  { value: 2, label: "2 Days before due date (Standard)" },
  { value: 3, label: "3 Days before due date" },
  { value: 5, label: "5 Days before due date" },
  { value: 7, label: "7 Days before due date" },
];

const LANGUAGE_OPTIONS = [
  { value: "english", label: "English Message Format" },
  { value: "urdu", label: "اردو فارمیٹ (Urdu Message Format)" },
];

const MERGE_MODE_OPTIONS = [
  { value: "replace", label: "Replace existing list with these items" },
  { value: "merge", label: "Merge (add new items & update quantities)" },
];

function isWalkInName(name) {
  if (!name) return true;
  return /walk[\s\-_]*in/i.test(String(name).trim());
}

export default function MakeRegularCustomerModal({
  isOpen,
  onClose,
  invoice,
  onSuccess,
}) {
  const { isDark, theme } = useTheme();
  const primaryColor = theme?.primary_color || "#2563eb";

  const [submitting, setSubmitting] = useState(false);

  // Customer Mode: 'existing' | 'new'
  const isInvoiceCustomerWalkIn = useMemo(() => {
    if (!invoice?.customer_id) return true;
    return isWalkInName(invoice?.customer?.name);
  }, [invoice]);

  const [customerMode, setCustomerMode] = useState("existing");
  const [selectedCustomerOption, setSelectedCustomerOption] = useState(null);

  // New Customer Fields
  const [newCustName, setNewCustName] = useState("");
  const [newCustPhone, setNewCustPhone] = useState("");
  const [newCustAddress, setNewCustAddress] = useState("");
  const [updateInvoiceCustomer, setUpdateInvoiceCustomer] = useState(true);

  // Refill Schedule Fields
  const [refillCycleOption, setRefillCycleOption] = useState(REFILL_CYCLE_OPTIONS[0]);
  const [refillCustomDays, setRefillCustomDays] = useState(30);
  const [nextRefillDate, setNextRefillDate] = useState("");
  const [reminderLeadOption, setReminderLeadOption] = useState(REMINDER_LEAD_OPTIONS[1]);
  const [languageOption, setLanguageOption] = useState(LANGUAGE_OPTIONS[0]);
  const [regularNotes, setRegularNotes] = useState("");
  const [mergeModeOption, setMergeModeOption] = useState(MERGE_MODE_OPTIONS[0]);

  // Items from Invoice
  const [items, setItems] = useState([]);

  // Calculate default next refill date given a cycle
  const calculateDefaultDate = useCallback((cycle, customDays = 30) => {
    const d = new Date();
    if (cycle === "weekly") {
      d.setDate(d.getDate() + 7);
    } else if (cycle === "biweekly") {
      d.setDate(d.getDate() + 14);
    } else if (cycle === "custom") {
      d.setDate(d.getDate() + Math.max(1, Number(customDays) || 30));
    } else {
      d.setMonth(d.getMonth() + 1);
    }
    return d.toISOString().split("T")[0];
  }, []);

  // Standard React-Select Styles conforming to Design System
  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        minHeight: "38px",
        borderRadius: "10px",
        backgroundColor: isDark ? "rgba(30, 41, 59, 0.85)" : "#ffffff",
        borderColor: state.isFocused
          ? primaryColor
          : isDark
          ? "rgba(71, 85, 105, 0.7)"
          : "rgba(209, 213, 219, 0.9)",
        boxShadow: state.isFocused ? `0 0 0 2px ${primaryColor}26` : "none",
        fontSize: "12px",
        fontWeight: "500",
        color: isDark ? "#f8fafc" : "#0f172a",
        cursor: "pointer",
        transition: "all 0.15s ease",
        "&:hover": {
          borderColor: isDark ? "rgba(100, 116, 139, 0.9)" : "rgba(156, 163, 175, 1)",
        },
      }),
      valueContainer: (base) => ({
        ...base,
        padding: "2px 10px",
      }),
      input: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#0f172a",
        margin: 0,
        padding: 0,
        fontSize: "12px",
      }),
      singleValue: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#0f172a",
        fontSize: "12px",
        fontWeight: "500",
      }),
      placeholder: (base) => ({
        ...base,
        color: isDark ? "#94a3b8" : "#9ca3af",
        fontSize: "12px",
      }),
      menuPortal: (base) => ({ ...base, zIndex: 99999 }),
      menu: (base) => ({
        ...base,
        backgroundColor: isDark ? "#1e293b" : "#ffffff",
        borderRadius: "12px",
        border: isDark ? "1px solid rgba(71, 85, 105, 0.7)" : "1px solid rgba(229, 231, 235, 1)",
        boxShadow: isDark
          ? "0 14px 35px -5px rgba(0, 0, 0, 0.5)"
          : "0 14px 35px -5px rgba(15, 23, 42, 0.15)",
        overflow: "hidden",
        zIndex: 99999,
      }),
      option: (base, state) => ({
        ...base,
        padding: "8px 12px",
        backgroundColor: state.isSelected
          ? primaryColor
          : state.isFocused
          ? isDark
            ? "rgba(51, 65, 85, 0.85)"
            : "rgba(239, 246, 255, 1)"
          : "transparent",
        color: state.isSelected ? "#ffffff" : isDark ? "#f8fafc" : "#0f172a",
        cursor: "pointer",
        fontSize: "12px",
        fontWeight: state.isSelected ? "600" : "500",
        "&:active": {
          backgroundColor: primaryColor,
          color: "#ffffff",
        },
      }),
    }),
    [isDark, primaryColor]
  );

  // Initialize modal state on open
  useEffect(() => {
    if (!isOpen || !invoice) return;

    const existingCust = invoice.customer;
    const isWalkIn = isInvoiceCustomerWalkIn;

    if (isWalkIn) {
      setCustomerMode("new");
      setSelectedCustomerOption(null);
      setNewCustName(invoice.patient_name || "");
      setNewCustPhone("");
      setNewCustAddress("");
    } else if (existingCust) {
      setCustomerMode("existing");
      const opt = {
        value: existingCust.id,
        label: `${existingCust.name}${existingCust.phone ? ` (${existingCust.phone})` : ""}`,
        customer: existingCust,
      };
      setSelectedCustomerOption(opt);
      setNewCustPhone(existingCust.phone || "");
    }

    setUpdateInvoiceCustomer(true);

    // Schedule defaults
    const cycle = existingCust?.refill_cycle || "monthly";
    const foundCycle = REFILL_CYCLE_OPTIONS.find((o) => o.value === cycle) || REFILL_CYCLE_OPTIONS[0];
    setRefillCycleOption(foundCycle);

    const customDays = existingCust?.refill_custom_days || 30;
    setRefillCustomDays(customDays);

    const leadDays = existingCust?.reminder_lead_days ?? 2;
    const foundLead = REMINDER_LEAD_OPTIONS.find((o) => o.value === leadDays) || REMINDER_LEAD_OPTIONS[1];
    setReminderLeadOption(foundLead);

    const lang = existingCust?.reminder_language || "english";
    const foundLang = LANGUAGE_OPTIONS.find((o) => o.value === lang) || LANGUAGE_OPTIONS[0];
    setLanguageOption(foundLang);

    const existingNotes = existingCust?.regular_notes || "";
    const doctorOrPatientNotes = [
      invoice.doctor_name ? `Doctor: ${invoice.doctor_name}` : null,
      invoice.patient_name ? `Patient: ${invoice.patient_name}` : null,
      invoice.remarks ? `Remarks: ${invoice.remarks}` : null,
    ].filter(Boolean).join(" · ");

    setRegularNotes(existingNotes || doctorOrPatientNotes || "");

    const calculatedNext = existingCust?.next_refill_date
      ? existingCust.next_refill_date.slice(0, 10)
      : calculateDefaultDate(cycle, customDays);
    setNextRefillDate(calculatedNext);

    const hasExistingItems =
      existingCust?.is_regular &&
      (existingCust?.regularItems || existingCust?.regular_items)?.length > 0;
    setMergeModeOption(hasExistingItems ? MERGE_MODE_OPTIONS[1] : MERGE_MODE_OPTIONS[0]);

    // Map items from invoice lines
    const rawItems = Array.isArray(invoice.items) ? invoice.items : [];
    const mapped = rawItems.map((line) => {
      const prod = line.product || {};
      const packSize = Math.max(1, Number(line.pack_size || prod.pack_size || 1));
      const lineUnitQty = Number(line.unit_quantity || 0);
      const linePackQty = Number(line.pack_quantity || 0);
      const lineTotalUnits = Number(line.quantity || 0);

      let packQty = linePackQty;
      let unitQty = lineUnitQty;
      if (packQty === 0 && unitQty === 0 && lineTotalUnits > 0) {
        if (packSize > 1) {
          packQty = Math.floor(lineTotalUnits / packSize);
          unitQty = lineTotalUnits;
        } else {
          unitQty = lineTotalUnits;
          packQty = 0;
        }
      }

      return {
        selected: true,
        product_id: line.product_id,
        product_name: prod.name || `Product #${line.product_id}`,
        product_code: prod.product_code || "",
        pack_size: packSize,
        pack_quantity: packQty,
        unit_quantity: unitQty,
        dosage_notes: "",
      };
    });

    setItems(mapped);
  }, [isOpen, invoice, isInvoiceCustomerWalkIn, calculateDefaultDate]);

  // Async customer search for react-select
  const loadCustomerOptions = async (inputValue) => {
    try {
      const { data } = await axios.get("/api/customers", {
        params: { search: inputValue?.trim() || "", per_page: 20 },
      });
      const list = Array.isArray(data) ? data : data?.data || [];
      return list.map((c) => ({
        value: c.id,
        label: `${c.name}${c.phone ? ` (${c.phone})` : ""}${c.is_regular ? " ★ Regular" : ""}`,
        customer: c,
      }));
    } catch {
      return [];
    }
  };

  // Recalculate next refill date when cycle changes
  const handleCycleSelect = (selectedOpt) => {
    setRefillCycleOption(selectedOpt);
    setNextRefillDate(calculateDefaultDate(selectedOpt.value, refillCustomDays));
  };

  const handleCustomDaysChange = (days) => {
    const d = Math.max(1, Number(days) || 1);
    setRefillCustomDays(d);
    if (refillCycleOption.value === "custom") {
      setNextRefillDate(calculateDefaultDate("custom", d));
    }
  };

  // Toggle item selection
  const handleToggleItem = (index) => {
    setItems((prev) =>
      prev.map((it, idx) =>
        idx === index ? { ...it, selected: !it.selected } : it
      )
    );
  };

  const handleToggleAll = (checked) => {
    setItems((prev) => prev.map((it) => ({ ...it, selected: checked })));
  };

  const updateItemField = (index, field, value) => {
    setItems((prev) =>
      prev.map((it, idx) =>
        idx === index ? { ...it, [field]: value } : it
      )
    );
  };

  const allSelected = useMemo(
    () => items.length > 0 && items.every((it) => it.selected),
    [items]
  );
  const selectedCount = useMemo(
    () => items.filter((it) => it.selected).length,
    [items]
  );

  const handleSubmit = async (e) => {
    e?.preventDefault?.();
    if (submitting) return;

    const selectedItems = items.filter((it) => it.selected);
    if (selectedItems.length === 0) {
      toast.error("Please select at least one medicine/product to attach.");
      return;
    }

    if (customerMode === "new") {
      if (!newCustName.trim()) {
        toast.error("Please enter a customer name.");
        return;
      }
    } else {
      if (!selectedCustomerOption?.value) {
        toast.error("Please select an existing customer.");
        return;
      }
    }

    setSubmitting(true);
    try {
      const payload = {
        is_regular: true,
        refill_cycle: refillCycleOption.value,
        refill_custom_days:
          refillCycleOption.value === "custom"
            ? Math.max(1, Number(refillCustomDays) || 30)
            : null,
        reminder_lead_days: Math.max(0, Number(reminderLeadOption.value) || 2),
        next_refill_date: nextRefillDate || null,
        reminder_language: languageOption.value,
        regular_notes: regularNotes.trim() || null,
        merge_mode: mergeModeOption.value,
        update_invoice_customer: Boolean(updateInvoiceCustomer),
        items: selectedItems.map((it) => ({
          product_id: it.product_id,
          pack_quantity: Number(it.pack_quantity) || 0,
          unit_quantity: Number(it.unit_quantity) || 0,
          dosage_notes: it.dosage_notes?.trim() || null,
        })),
      };

      if (customerMode === "new") {
        payload.new_customer = {
          name: newCustName.trim(),
          phone: newCustPhone.trim() || null,
          address: newCustAddress.trim() || null,
        };
      } else {
        payload.customer_id = selectedCustomerOption.value;
      }

      const { data } = await axios.post(
        `/api/sale-invoices/${invoice.id}/make-regular-customer`,
        payload
      );

      toast.success(
        data?.message ||
          "Customer marked as Regular and medicines schedule saved!"
      );

      onSuccess?.(data);
      onClose?.();
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        "Failed to save regular customer schedule.";
      toast.error(msg);
    } finally {
      setSubmitting(false);
    }
  };

  if (!isOpen || !invoice) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4 bg-slate-950/70 backdrop-blur-xs">
      <div
        className="relative w-full max-w-4xl max-h-[92vh] flex flex-col rounded-2xl bg-white/95 dark:bg-slate-900/95 backdrop-blur-md border border-slate-200/80 dark:border-slate-800 shadow-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-150"
        role="dialog"
        aria-modal="true"
        aria-labelledby="regular-customer-modal-title"
      >
        {/* === Header (Consistent Karobar Modal Topbar) === */}
        <div
          className={`flex items-center justify-between px-6 py-4 border-b shrink-0 ${
            isDark
              ? "border-slate-700/80 bg-slate-800/50"
              : "border-gray-200 bg-gray-50/80"
          }`}
        >
          <div className="flex items-center gap-3 min-w-0">
            <div
              className={`p-2.5 rounded-xl border shrink-0 ${
                isDark
                  ? "bg-blue-500/10 border-blue-500/20 text-blue-400"
                  : "bg-blue-50 border-blue-200 text-blue-600"
              }`}
              style={{
                color: isDark ? undefined : primaryColor,
              }}
            >
              <SparklesIcon className="w-5 h-5" aria-hidden="true" />
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <h3
                  id="regular-customer-modal-title"
                  className={`text-base font-bold truncate ${
                    isDark ? "text-white" : "text-gray-900"
                  }`}
                >
                  Make Regular Customer &amp; Refill Schedule
                </h3>
                <span
                  className={`px-2 py-0.5 rounded-md text-[10px] font-mono font-bold shrink-0 ${
                    isDark
                      ? "bg-slate-700/80 text-slate-300 border border-slate-600"
                      : "bg-gray-200/80 text-gray-700 border border-gray-300"
                  }`}
                >
                  Invoice #{invoice.posted_number || invoice.id}
                </span>
              </div>
              <p
                className={`text-xs truncate ${
                  isDark ? "text-slate-400" : "text-gray-500"
                } mt-0.5`}
              >
                Save medicines from this bill as a recurring refill schedule with automated WhatsApp reminders.
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={submitting}
            className={`p-1.5 rounded-lg transition-colors cursor-pointer shrink-0 ${
              isDark
                ? "text-slate-400 hover:text-white hover:bg-slate-700/60"
                : "text-gray-500 hover:text-gray-900 hover:bg-gray-200/70"
            }`}
            aria-label="Close dialog"
          >
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        {/* === Form Body === */}
        <form
          onSubmit={handleSubmit}
          className="flex-1 overflow-y-auto p-6 space-y-6 divide-y divide-slate-100 dark:divide-slate-800 text-slate-800 dark:text-slate-100"
        >
          {/* Section 1: Customer Profile */}
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 flex items-center gap-1.5">
                <UserIcon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                <span>Customer Information</span>
              </span>

              {/* Mode Switcher */}
              <div className="flex items-center gap-1 p-0.5 rounded-xl bg-slate-100 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-xs font-semibold">
                <button
                  type="button"
                  onClick={() => setCustomerMode("existing")}
                  className={`px-3 py-1 rounded-lg transition-all cursor-pointer ${
                    customerMode === "existing"
                      ? "bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-2xs font-bold"
                      : "text-slate-600 dark:text-slate-300 hover:text-slate-900"
                  }`}
                >
                  Existing Customer
                </button>
                <button
                  type="button"
                  onClick={() => setCustomerMode("new")}
                  className={`px-3 py-1 rounded-lg transition-all cursor-pointer flex items-center gap-1 ${
                    customerMode === "new"
                      ? "bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-2xs font-bold"
                      : "text-slate-600 dark:text-slate-300 hover:text-slate-900"
                  }`}
                >
                  <UserPlusIcon className="w-3.5 h-3.5" />
                  <span>+ New Customer</span>
                </button>
              </div>
            </div>

            {customerMode === "existing" ? (
              <div className="p-4 rounded-xl bg-slate-50/80 dark:bg-slate-800/60 border border-slate-200/80 dark:border-slate-700/80 space-y-3">
                <div>
                  <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1.5">
                    Select Customer (Search by name or phone) *
                  </label>
                  <AsyncSelect
                    cacheOptions
                    defaultOptions
                    loadOptions={loadCustomerOptions}
                    value={selectedCustomerOption}
                    onChange={(opt) => {
                      setSelectedCustomerOption(opt);
                      if (opt?.customer?.phone) {
                        setNewCustPhone(opt.customer.phone);
                      }
                    }}
                    placeholder="Search customer by name or phone..."
                    styles={selectStyles}
                    menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                    menuPosition="fixed"
                    isClearable={false}
                  />
                </div>

                {selectedCustomerOption?.customer && (
                  <div className="flex flex-wrap items-center justify-between gap-2 pt-2 border-t border-slate-200/60 dark:border-slate-700/60 text-xs">
                    <div className="flex items-center gap-2">
                      <span className="font-bold text-slate-800 dark:text-slate-200">
                        {selectedCustomerOption.customer.name}
                      </span>
                      {selectedCustomerOption.customer.is_regular ? (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold bg-purple-100 text-purple-800 dark:bg-purple-950/80 dark:text-purple-300 border border-purple-300 dark:border-purple-800">
                          ★ Regular Customer Active
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-medium bg-slate-200 text-slate-700 dark:bg-slate-700 dark:text-slate-300">
                          Standard Customer
                        </span>
                      )}
                    </div>
                    <span className="text-slate-500 dark:text-slate-400">
                      {selectedCustomerOption.customer.phone
                        ? `WhatsApp: ${selectedCustomerOption.customer.phone}`
                        : "No phone registered"}
                      {selectedCustomerOption.customer.address
                        ? ` · ${selectedCustomerOption.customer.address}`
                        : ""}
                    </span>
                  </div>
                )}
              </div>
            ) : (
              <div className="p-4 rounded-xl bg-slate-50/80 dark:bg-slate-800/60 border border-slate-200/80 dark:border-slate-700/80 space-y-3">
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div className="sm:col-span-1">
                    <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1">
                      Customer Name *
                    </label>
                    <input
                      type="text"
                      required
                      value={newCustName}
                      onChange={(e) => setNewCustName(e.target.value)}
                      placeholder="e.g. Haji Muhammad Aslam"
                      className="w-full px-3 py-2 rounded-xl border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-xs focus:ring-2 focus:ring-blue-500 focus:outline-none transition-all"
                    />
                  </div>
                  <div className="sm:col-span-1">
                    <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1">
                      WhatsApp / Mobile No
                    </label>
                    <input
                      type="tel"
                      value={newCustPhone}
                      onChange={(e) => setNewCustPhone(e.target.value)}
                      placeholder="e.g. 0300-1234567"
                      className="w-full px-3 py-2 rounded-xl border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-xs focus:ring-2 focus:ring-blue-500 focus:outline-none transition-all"
                    />
                  </div>
                  <div className="sm:col-span-1">
                    <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1">
                      Address / Area (Optional)
                    </label>
                    <input
                      type="text"
                      value={newCustAddress}
                      onChange={(e) => setNewCustAddress(e.target.value)}
                      placeholder="e.g. Street 4, Model Town"
                      className="w-full px-3 py-2 rounded-xl border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-xs focus:ring-2 focus:ring-blue-500 focus:outline-none transition-all"
                    />
                  </div>
                </div>

                <label className="flex items-center gap-2 cursor-pointer pt-1">
                  <input
                    type="checkbox"
                    checked={updateInvoiceCustomer}
                    onChange={(e) => setUpdateInvoiceCustomer(e.target.checked)}
                    className="w-4 h-4 rounded text-blue-600 focus:ring-blue-500"
                  />
                  <span className="text-xs text-slate-600 dark:text-slate-300 font-medium">
                    Link Invoice #{invoice.posted_number || invoice.id} to this new customer
                  </span>
                </label>
              </div>
            )}
          </div>

          {/* Section 2: Refill Schedule & Details */}
          <div className="pt-5 space-y-4">
            <span className="text-xs font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 flex items-center gap-1.5">
              <CalendarDaysIcon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
              <span>Refill Schedule &amp; Reminders</span>
            </span>

            {/* Select Grid with React-Select components */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
              <div>
                <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1">
                  Refill Frequency *
                </label>
                <Select
                  options={REFILL_CYCLE_OPTIONS}
                  value={refillCycleOption}
                  onChange={handleCycleSelect}
                  styles={selectStyles}
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  menuPosition="fixed"
                  isSearchable={false}
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1">
                  Next Refill Due Date *
                </label>
                <ModernDatePicker
                  required
                  isClearable={false}
                  value={nextRefillDate}
                  onChange={(val) => setNextRefillDate(val)}
                  size="sm"
                  ariaLabel="Next refill due date"
                  placeholder="Select refill date"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1">
                  Reminder Lead Time
                </label>
                <Select
                  options={REMINDER_LEAD_OPTIONS}
                  value={reminderLeadOption}
                  onChange={setReminderLeadOption}
                  styles={selectStyles}
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  menuPosition="fixed"
                  isSearchable={false}
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1">
                  WhatsApp Language
                </label>
                <Select
                  options={LANGUAGE_OPTIONS}
                  value={languageOption}
                  onChange={setLanguageOption}
                  styles={selectStyles}
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  menuPosition="fixed"
                  isSearchable={false}
                />
              </div>
            </div>

            {refillCycleOption.value === "custom" && (
              <div className="p-3 rounded-xl bg-slate-50 dark:bg-slate-800/60 border border-slate-200 dark:border-slate-700 flex items-center gap-3">
                <label className="text-xs font-bold text-slate-700 dark:text-slate-300 shrink-0">
                  Custom Refill Days:
                </label>
                <input
                  type="number"
                  min="1"
                  max="365"
                  value={refillCustomDays}
                  onChange={(e) => handleCustomDaysChange(e.target.value)}
                  className="w-24 px-3 py-1.5 rounded-lg border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-xs font-bold focus:ring-2 focus:ring-blue-500 focus:outline-none"
                />
                <span className="text-xs text-slate-500 dark:text-slate-400">
                  days between each recurring medicine collection
                </span>
              </div>
            )}

            <div>
              <label className="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1">
                Doctor / Prescription / Chronic Disease Notes (Optional)
              </label>
              <input
                type="text"
                value={regularNotes}
                onChange={(e) => setRegularNotes(e.target.value)}
                placeholder="e.g. Chronic hypertension & diabetes — keep prepared on 1st of every month"
                className="w-full px-3 py-2 rounded-xl border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-xs focus:ring-2 focus:ring-blue-500 focus:outline-none"
              />
            </div>
          </div>

          {/* Section 3: Select Medicines from this Invoice */}
          <div className="pt-5 space-y-3">
            <div className="flex items-center justify-between flex-wrap gap-2">
              <div>
                <span className="text-xs font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 flex items-center gap-1.5">
                  <SparklesIcon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                  <span>Invoice Medicines &amp; Dosage Schedule ({selectedCount}/{items.length} Selected)</span>
                </span>
                <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
                  Select which medicines should be included in their regular refill and specify patient dosage.
                </p>
              </div>

              <button
                type="button"
                onClick={() => handleToggleAll(!allSelected)}
                className="px-2.5 py-1 text-xs font-semibold rounded-lg bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 transition-colors cursor-pointer"
              >
                {allSelected ? "Deselect All" : "Select All"}
              </button>
            </div>

            {/* Medicines Table */}
            <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-700">
              <table className="w-full text-xs text-left border-collapse">
                <thead className="bg-slate-100 dark:bg-slate-800 text-[11px] uppercase tracking-wider font-semibold text-slate-600 dark:text-slate-300 border-b border-slate-200 dark:border-slate-700">
                  <tr>
                    <th className="w-10 px-3 py-2.5 text-center">
                      <input
                        type="checkbox"
                        checked={allSelected}
                        onChange={(e) => handleToggleAll(e.target.checked)}
                        className="w-4 h-4 rounded text-blue-600 focus:ring-blue-500 cursor-pointer"
                        aria-label="Select all medicines"
                      />
                    </th>
                    <th className="px-3 py-2.5">Medicine / Product</th>
                    <th className="w-24 px-3 py-2.5 text-center">Pack Qty</th>
                    <th className="w-24 px-3 py-2.5 text-center">Unit Qty</th>
                    <th className="w-64 px-3 py-2.5">Dosage / Instructions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                  {items.map((it, idx) => (
                    <tr
                      key={it.product_id || idx}
                      className={`transition-colors ${
                        it.selected
                          ? "bg-white dark:bg-slate-900/90"
                          : "bg-slate-50/60 dark:bg-slate-900/30 opacity-60"
                      }`}
                    >
                      <td className="px-3 py-2.5 text-center">
                        <input
                          type="checkbox"
                          checked={it.selected}
                          onChange={() => handleToggleItem(idx)}
                          className="w-4 h-4 rounded text-blue-600 focus:ring-blue-500 cursor-pointer"
                          aria-label={`Select ${it.product_name}`}
                        />
                      </td>

                      <td className="px-3 py-2.5">
                        <span className="font-bold text-slate-900 dark:text-slate-100 block">
                          {it.product_name}
                        </span>
                        <div className="flex items-center gap-2 text-[10px] text-slate-500 dark:text-slate-400 font-mono mt-0.5">
                          {it.product_code && <span>#{it.product_code}</span>}
                          {it.pack_size > 1 && <span>Pack of {it.pack_size}</span>}
                        </div>
                      </td>

                      <td className="px-3 py-2.5 text-center">
                        <input
                          type="number"
                          min="0"
                          step="1"
                          disabled={!it.selected}
                          value={it.pack_quantity}
                          onChange={(e) =>
                            updateItemField(idx, "pack_quantity", e.target.value)
                          }
                          className="w-20 px-2 py-1 text-center rounded-lg border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-xs font-bold disabled:opacity-50 focus:ring-2 focus:ring-blue-500 focus:outline-none"
                        />
                      </td>

                      <td className="px-3 py-2.5 text-center">
                        <input
                          type="number"
                          min="0"
                          step="1"
                          disabled={!it.selected}
                          value={it.unit_quantity}
                          onChange={(e) =>
                            updateItemField(idx, "unit_quantity", e.target.value)
                          }
                          className="w-20 px-2 py-1 text-center rounded-lg border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-xs font-bold disabled:opacity-50 focus:ring-2 focus:ring-blue-500 focus:outline-none"
                        />
                      </td>

                      <td className="px-3 py-2.5 space-y-1">
                        <input
                          type="text"
                          disabled={!it.selected}
                          value={it.dosage_notes}
                          onChange={(e) =>
                            updateItemField(idx, "dosage_notes", e.target.value)
                          }
                          placeholder="e.g. 1+0+1 after meal"
                          className="w-full px-2.5 py-1 rounded-lg border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-xs disabled:opacity-50 focus:ring-2 focus:ring-blue-500 focus:outline-none"
                        />
                        {it.selected && (
                          <div className="flex flex-wrap gap-1">
                            {DOSAGE_SHORTCUTS.map((chip) => (
                              <button
                                key={chip}
                                type="button"
                                onClick={() => {
                                  const current = it.dosage_notes?.trim();
                                  updateItemField(
                                    idx,
                                    "dosage_notes",
                                    current ? `${current} · ${chip}` : chip
                                  );
                                }}
                                className="px-1.5 py-0.5 rounded text-[9.5px] font-semibold bg-slate-100 dark:bg-slate-800 hover:bg-blue-100 dark:hover:bg-blue-950/60 text-slate-700 dark:text-slate-300 hover:text-blue-700 dark:hover:text-blue-300 transition-colors cursor-pointer"
                              >
                                {chip}
                              </button>
                            ))}
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Merge vs Replace Mode if Customer already has regular items */}
            {selectedCustomerOption?.customer?.is_regular && (
              <div className="p-3.5 rounded-xl bg-purple-50/80 dark:bg-purple-950/30 border border-purple-200 dark:border-purple-800 space-y-2">
                <span className="text-xs font-bold text-purple-900 dark:text-purple-200 flex items-center gap-1.5">
                  <InformationCircleIcon className="w-4 h-4 shrink-0" />
                  <span>Existing Regular Medicines Option (React-Select)</span>
                </span>
                <div className="max-w-md">
                  <Select
                    options={MERGE_MODE_OPTIONS}
                    value={mergeModeOption}
                    onChange={setMergeModeOption}
                    styles={selectStyles}
                    menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                    menuPosition="fixed"
                    isSearchable={false}
                  />
                </div>
              </div>
            )}
          </div>

          {/* === Footer Actions === */}
          <div
            className={`pt-4 border-t flex items-center justify-between gap-3 shrink-0 ${
              isDark ? "border-slate-800" : "border-slate-100"
            }`}
          >
            <span className={`text-xs ${isDark ? "text-slate-400" : "text-slate-500"}`}>
              {selectedCount} item(s) will be attached for recurring refills.
            </span>

            <div className="flex items-center gap-2.5">
              <button
                type="button"
                onClick={onClose}
                disabled={submitting}
                className={`px-4 py-2 rounded-xl text-xs font-semibold border transition-colors cursor-pointer ${
                  isDark
                    ? "border-slate-700 text-slate-300 hover:bg-slate-800"
                    : "border-gray-300 text-gray-700 hover:bg-gray-100"
                }`}
              >
                Cancel
              </button>

              <button
                type="submit"
                disabled={submitting || selectedCount === 0}
                className="px-5 py-2 rounded-xl text-xs font-bold text-white shadow-sm flex items-center gap-1.5 transition-all disabled:opacity-50 cursor-pointer"
                style={{ backgroundColor: primaryColor }}
              >
                {submitting ? (
                  <ArrowPathIcon className="w-4 h-4 animate-spin" />
                ) : (
                  <CheckCircleIcon className="w-4 h-4" />
                )}
                <span>
                  {submitting
                    ? "Saving Schedule..."
                    : "Save Regular Customer & Medicines"}
                </span>
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}
