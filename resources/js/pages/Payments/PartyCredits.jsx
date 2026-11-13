import React, { useEffect, useMemo, useState, useCallback } from "react";
import axios from "axios";
import Select from "react-select";
import { useNavigate } from "react-router-dom";
import {
  MagnifyingGlassIcon,
  XMarkIcon,
  ArrowPathIcon,
  BanknotesIcon,
  UserGroupIcon,
  BuildingStorefrontIcon,
  ClockIcon,
  ExclamationTriangleIcon,
  DocumentTextIcon,
  ArrowTopRightOnSquareIcon,
} from "@heroicons/react/24/outline";
import { useCurrency } from "../../context/CurrencyContext";

const STATUS_OPTIONS = [
  { value: "all", label: "All Due Balances" },
  { value: "overdue", label: "Overdue Only" },
];

const SORT_OPTIONS = [
  { value: "highest", label: "Highest Due" },
  { value: "lowest", label: "Lowest Due" },
  { value: "overdue", label: "Highest Overdue" },
  { value: "name_asc", label: "Name (A-Z)" },
];

export default function PartyCredits() {
  const { formatCurrency } = useCurrency();
  const navigate = useNavigate();

  const [isDark, setIsDark] = useState(() =>
    document.documentElement.classList.contains("dark")
  );
  useEffect(() => {
    const observer = new MutationObserver(() => {
      setIsDark(document.documentElement.classList.contains("dark"));
    });
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["class"],
    });
    return () => observer.disconnect();
  }, []);

  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        minHeight: "38px",
        height: "38px",
        backgroundColor: isDark ? "#0f172a" : "#ffffff",
        borderColor: state.isFocused
          ? "#3b82f6"
          : isDark
          ? "#334155"
          : "#d1d5db",
        boxShadow: state.isFocused ? "0 0 0 1px #3b82f6" : "none",
        "&:hover": {
          borderColor: state.isFocused
            ? "#3b82f6"
            : isDark
            ? "#475569"
            : "#9ca3af",
        },
        borderRadius: "0.5rem",
        fontSize: "0.875rem",
        cursor: "pointer",
      }),
      valueContainer: (base) => ({
        ...base,
        padding: "0 10px",
      }),
      singleValue: (base) => ({
        ...base,
        color: isDark ? "#f1f5f9" : "#111827",
      }),
      input: (base) => ({
        ...base,
        color: isDark ? "#f1f5f9" : "#111827",
      }),
      placeholder: (base) => ({
        ...base,
        color: isDark ? "#94a3b8" : "#6b7280",
      }),
      indicatorSeparator: (base) => ({
        ...base,
        backgroundColor: isDark ? "#334155" : "#e5e7eb",
      }),
      dropdownIndicator: (base) => ({
        ...base,
        color: isDark ? "#94a3b8" : "#6b7280",
        padding: "6px",
        "&:hover": {
          color: isDark ? "#e2e8f0" : "#374151",
        },
      }),
      menuPortal: (base) => ({
        ...base,
        zIndex: 9999,
      }),
      menu: (base) => ({
        ...base,
        backgroundColor: isDark ? "#0f172a" : "#ffffff",
        border: `1px solid ${isDark ? "#334155" : "#e5e7eb"}`,
        borderRadius: "0.5rem",
        boxShadow:
          "0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05)",
        overflow: "hidden",
      }),
      option: (base, state) => ({
        ...base,
        fontSize: "0.875rem",
        cursor: "pointer",
        backgroundColor: state.isSelected
          ? "#2563eb"
          : state.isFocused
          ? isDark
            ? "#1e293b"
            : "#f3f4f6"
          : "transparent",
        color: state.isSelected
          ? "#ffffff"
          : isDark
          ? "#e2e8f0"
          : "#111827",
        "&:active": {
          backgroundColor: state.isSelected ? "#2563eb" : "#dbeafe",
        },
      }),
    }),
    [isDark]
  );

  // Filters
  const [partyType, setPartyType] = useState("all");
  const [statusFilter, setStatusFilter] = useState("all");
  const [sortBy, setSortBy] = useState("highest");
  const [searchInput, setSearchInput] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [minAmount, setMinAmount] = useState("");

  // Data
  const [rows, setRows] = useState([]);
  const [summary, setSummary] = useState({
    total_due: 0,
    supplier_total: 0,
    supplier_count: 0,
    customer_total: 0,
    customer_count: 0,
    overdue_total: 0,
    party_count: 0,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  // Detail modal
  const [selectedParty, setSelectedParty] = useState(null);
  const [detailData, setDetailData] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchInput.trim()), 250);
    return () => clearTimeout(timer);
  }, [searchInput]);

  const fetchPartyDues = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const params = {
        party_type: partyType,
        status: statusFilter,
        sort: sortBy,
      };
      if (debouncedSearch) params.search = debouncedSearch;
      if (minAmount !== "" && !Number.isNaN(Number(minAmount))) {
        params.min_amount = Number(minAmount);
      }

      const res = await axios.get("/api/payments/party-credits", { params });
      setRows(res.data?.rows || []);
      setSummary(
        res.data?.summary || {
          total_due: 0,
          supplier_total: 0,
          supplier_count: 0,
          customer_total: 0,
          customer_count: 0,
          overdue_total: 0,
          party_count: 0,
        }
      );
    } catch (err) {
      setError(
        err.response?.data?.message || "Failed to load party due balances."
      );
    } finally {
      setLoading(false);
    }
  }, [partyType, statusFilter, sortBy, debouncedSearch, minAmount]);

  useEffect(() => {
    fetchPartyDues();
  }, [fetchPartyDues]);

  const openPartyModal = async (party) => {
    setSelectedParty(party);
    setDetailData(null);
    setDetailError("");
    setDetailLoading(true);
    try {
      const res = await axios.get("/api/payments/party-due-details", {
        params: {
          party_type: party.party_type,
          party_id: party.party_id,
        },
      });
      setDetailData(res.data);
    } catch (err) {
      setDetailError(
        err.response?.data?.message || "Failed to load party due breakdown."
      );
    } finally {
      setDetailLoading(false);
    }
  };

  const closePartyModal = () => {
    setSelectedParty(null);
    setDetailData(null);
    setDetailError("");
  };

  const clearFilters = () => {
    setPartyType("all");
    setStatusFilter("all");
    setSortBy("highest");
    setSearchInput("");
    setDebouncedSearch("");
    setMinAmount("");
  };

  const hasActiveFilters =
    partyType !== "all" ||
    statusFilter !== "all" ||
    sortBy !== "highest" ||
    searchInput !== "" ||
    minAmount !== "";

  return (
    <div className="w-full p-4 sm:p-6 space-y-5">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">
            Party Due Balances
          </h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-0.5">
            Net outstanding due to collect from customers or owed to suppliers
            right now. Click any row for open invoices and due details.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={fetchPartyDues}
            disabled={loading}
            className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm font-medium text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors cursor-pointer disabled:opacity-50"
          >
            <ArrowPathIcon
              className={`w-4 h-4 ${loading ? "animate-spin" : ""}`}
            />
            Refresh
          </button>
          <button
            type="button"
            onClick={() => navigate("/payments")}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 text-sm font-medium text-white shadow-sm transition-colors cursor-pointer"
          >
            <BanknotesIcon className="w-4 h-4" />
            Record Payment
          </button>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
        <div className="bg-white dark:bg-slate-800/90 border border-gray-200 dark:border-slate-700 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
              Total Due (Filtered)
            </span>
            <span className="p-2 rounded-lg bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400">
              <BanknotesIcon className="w-5 h-5" />
            </span>
          </div>
          <div className="mt-2 text-2xl font-bold text-gray-900 dark:text-slate-100 tabular-nums">
            {formatCurrency(summary.total_due)}
          </div>
          <div className="mt-1 text-xs text-gray-500 dark:text-slate-400">
            Across {summary.party_count}{" "}
            {summary.party_count === 1 ? "party" : "parties"}
          </div>
        </div>

        <div className="bg-white dark:bg-slate-800/90 border border-gray-200 dark:border-slate-700 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
              To Collect (Customers)
            </span>
            <span className="p-2 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400">
              <UserGroupIcon className="w-5 h-5" />
            </span>
          </div>
          <div className="mt-2 text-2xl font-bold text-emerald-600 dark:text-emerald-400 tabular-nums">
            {formatCurrency(summary.customer_total)}
          </div>
          <div className="mt-1 text-xs text-gray-500 dark:text-slate-400">
            {summary.customer_count}{" "}
            {summary.customer_count === 1 ? "customer" : "customers"} with due
          </div>
        </div>

        <div className="bg-white dark:bg-slate-800/90 border border-gray-200 dark:border-slate-700 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
              Owed to Suppliers
            </span>
            <span className="p-2 rounded-lg bg-amber-50 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400">
              <BuildingStorefrontIcon className="w-5 h-5" />
            </span>
          </div>
          <div className="mt-2 text-2xl font-bold text-amber-600 dark:text-amber-400 tabular-nums">
            {formatCurrency(summary.supplier_total)}
          </div>
          <div className="mt-1 text-xs text-gray-500 dark:text-slate-400">
            {summary.supplier_count}{" "}
            {summary.supplier_count === 1 ? "supplier" : "suppliers"} with due
          </div>
        </div>

        <div className="bg-white dark:bg-slate-800/90 border border-gray-200 dark:border-slate-700 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
              Overdue Portion
            </span>
            <span className="p-2 rounded-lg bg-rose-50 dark:bg-rose-900/30 text-rose-600 dark:text-rose-400">
              <ExclamationTriangleIcon className="w-5 h-5" />
            </span>
          </div>
          <div className="mt-2 text-2xl font-bold text-rose-600 dark:text-rose-400 tabular-nums">
            {formatCurrency(summary.overdue_total)}
          </div>
          <div className="mt-1 text-xs text-gray-500 dark:text-slate-400">
            Past invoice due date
          </div>
        </div>
      </div>

      {/* Filters Bar */}
      <div className="bg-white dark:bg-slate-800/90 border border-gray-200 dark:border-slate-700 rounded-xl p-4 shadow-sm space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          {/* Party Type Segmented Tabs */}
          <div className="inline-flex rounded-lg bg-gray-100 dark:bg-slate-900 p-1">
            {[
              { id: "all", label: "All Parties" },
              { id: "customer", label: "Customers (Receivable)" },
              { id: "supplier", label: "Suppliers (Payable)" },
            ].map((tab) => (
              <button
                key={tab.id}
                type="button"
                onClick={() => setPartyType(tab.id)}
                className={`px-3 py-1.5 rounded-md text-xs sm:text-sm font-medium transition-colors cursor-pointer ${
                  partyType === tab.id
                    ? "bg-white dark:bg-slate-700 text-gray-900 dark:text-white shadow-sm"
                    : "text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200"
                }`}
              >
                {tab.label}
              </button>
            ))}
          </div>

          {hasActiveFilters && (
            <button
              type="button"
              onClick={clearFilters}
              className="text-xs font-medium text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
            >
              Reset filters
            </button>
          )}
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-12 gap-3">
          {/* Search */}
          <div className="lg:col-span-5 relative">
            <MagnifyingGlassIcon className="w-4 h-4 text-gray-400 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
            <input
              type="text"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="Search party name, phone, or email..."
              className="w-full h-[38px] pl-9 pr-8 rounded-lg border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            {searchInput && (
              <button
                type="button"
                onClick={() => setSearchInput("")}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 cursor-pointer"
              >
                <XMarkIcon className="w-4 h-4" />
              </button>
            )}
          </div>

          {/* Status Select */}
          <div className="lg:col-span-3">
            <Select
              value={
                STATUS_OPTIONS.find((o) => o.value === statusFilter) ||
                STATUS_OPTIONS[0]
              }
              onChange={(opt) => setStatusFilter(opt ? opt.value : "all")}
              options={STATUS_OPTIONS}
              styles={selectStyles}
              isSearchable={false}
              menuPortalTarget={document.body}
              menuPosition="fixed"
              aria-label="Filter by due status"
            />
          </div>

          {/* Min Due */}
          <div className="lg:col-span-2">
            <input
              type="number"
              min="0"
              step="any"
              value={minAmount}
              onChange={(e) => setMinAmount(e.target.value)}
              placeholder="Min due..."
              className="w-full h-[38px] px-3 rounded-lg border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>

          {/* Sort Select */}
          <div className="lg:col-span-2">
            <Select
              value={
                SORT_OPTIONS.find((o) => o.value === sortBy) || SORT_OPTIONS[0]
              }
              onChange={(opt) => setSortBy(opt ? opt.value : "highest")}
              options={SORT_OPTIONS}
              styles={selectStyles}
              isSearchable={false}
              menuPortalTarget={document.body}
              menuPosition="fixed"
              aria-label="Sort party due list"
            />
          </div>
        </div>
      </div>

      {/* Table */}
      <div className="bg-white dark:bg-slate-800/90 border border-gray-200 dark:border-slate-700 rounded-xl shadow-sm overflow-hidden">
        {error ? (
          <div className="p-8 text-center text-rose-600 dark:text-rose-400 text-sm">
            {error}
          </div>
        ) : loading ? (
          <div className="p-10 text-center text-gray-500 dark:text-slate-400 text-sm">
            Loading party due balances...
          </div>
        ) : rows.length === 0 ? (
          <div className="p-12 text-center">
            <BanknotesIcon className="w-10 h-10 mx-auto text-gray-300 dark:text-slate-600 mb-2" />
            <p className="text-sm font-medium text-gray-700 dark:text-slate-300">
              No due balances match your filters.
            </p>
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">
              All filtered parties are settled, or try resetting your filters.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-gray-200 dark:border-slate-700 bg-gray-50/80 dark:bg-slate-900/50 text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                  <th className="py-3 px-4">Party</th>
                  <th className="py-3 px-4">Type</th>
                  <th className="py-3 px-4 text-center">Open Invoices</th>
                  <th className="py-3 px-4">Oldest Due Date</th>
                  <th className="py-3 px-4 text-right">Overdue</th>
                  <th className="py-3 px-4 text-right">Total Due</th>
                  <th className="py-3 px-4 text-right">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-slate-700/70 text-sm">
                {rows.map((row) => {
                  const isCustomer = row.party_type === "customer";
                  const hasOverdue = row.overdue_amount > 0.01;
                  return (
                    <tr
                      key={`${row.party_type}-${row.party_id}`}
                      onClick={() => openPartyModal(row)}
                      className="hover:bg-blue-50/50 dark:hover:bg-slate-700/40 transition-colors cursor-pointer"
                    >
                      <td className="py-3.5 px-4">
                        <div className="font-semibold text-gray-900 dark:text-slate-100">
                          {row.party_name}
                        </div>
                        {(row.party_phone || row.party_email) && (
                          <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                            {[row.party_phone, row.party_email]
                              .filter(Boolean)
                              .join(" • ")}
                          </div>
                        )}
                      </td>
                      <td className="py-3.5 px-4">
                        <span
                          className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                            isCustomer
                              ? "bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300"
                              : "bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300"
                          }`}
                        >
                          {isCustomer ? "Customer" : "Supplier"}
                        </span>
                      </td>
                      <td className="py-3.5 px-4 text-center">
                        <span className="inline-flex items-center justify-center min-w-[28px] px-2 py-0.5 rounded-md bg-gray-100 dark:bg-slate-700 text-xs font-semibold text-gray-700 dark:text-slate-200">
                          {row.open_invoices_count}
                        </span>
                      </td>
                      <td className="py-3.5 px-4 text-gray-600 dark:text-slate-300 whitespace-nowrap">
                        {row.oldest_due_date ? (
                          <span className="inline-flex items-center gap-1 text-xs">
                            <ClockIcon className="w-3.5 h-3.5 text-gray-400" />
                            {row.oldest_due_date}
                          </span>
                        ) : (
                          <span className="text-xs text-gray-400">—</span>
                        )}
                      </td>
                      <td className="py-3.5 px-4 text-right tabular-nums whitespace-nowrap">
                        {hasOverdue ? (
                          <div>
                            <span className="font-semibold text-rose-600 dark:text-rose-400">
                              {formatCurrency(row.overdue_amount)}
                            </span>
                            <div className="text-[11px] text-rose-500 dark:text-rose-400">
                              {row.overdue_count}{" "}
                              {row.overdue_count === 1 ? "invoice" : "invoices"}
                            </div>
                          </div>
                        ) : (
                          <span className="text-xs text-gray-400">—</span>
                        )}
                      </td>
                      <td className="py-3.5 px-4 text-right whitespace-nowrap">
                        <span
                          className={`text-base font-bold tabular-nums ${
                            isCustomer
                              ? "text-emerald-600 dark:text-emerald-400"
                              : "text-amber-600 dark:text-amber-400"
                          }`}
                        >
                          {formatCurrency(row.total_due)}
                        </span>
                      </td>
                      <td className="py-3.5 px-4 text-right whitespace-nowrap">
                        <button
                          type="button"
                          onClick={(e) => {
                            e.stopPropagation();
                            openPartyModal(row);
                          }}
                          className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg text-xs font-medium bg-gray-100 dark:bg-slate-700 text-gray-700 dark:text-slate-200 hover:bg-blue-600 hover:text-white dark:hover:bg-blue-600 transition-colors cursor-pointer"
                        >
                          <DocumentTextIcon className="w-3.5 h-3.5" />
                          Details
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Party Due Detail Modal */}
      {selectedParty && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-xs p-3 sm:p-6"
          onClick={closePartyModal}
        >
          <div
            className="w-full max-w-[95vw] xl:max-w-6xl max-h-[90vh] flex flex-col bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-2xl shadow-2xl overflow-hidden"
            onClick={(e) => e.stopPropagation()}
          >
            {/* Modal Header */}
            <div className="flex items-start justify-between gap-4 px-5 py-4 border-b border-gray-200 dark:border-slate-700 bg-gray-50/70 dark:bg-slate-900/50">
              <div>
                <div className="flex items-center gap-2.5 flex-wrap">
                  <h2 className="text-lg sm:text-xl font-bold text-gray-900 dark:text-slate-100">
                    {selectedParty.party_name}
                  </h2>
                  <span
                    className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                      selectedParty.party_type === "customer"
                        ? "bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300"
                        : "bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300"
                    }`}
                  >
                    {selectedParty.party_type === "customer"
                      ? "Customer (Receivable)"
                      : "Supplier (Payable)"}
                  </span>
                </div>
                {(selectedParty.party_phone || selectedParty.party_email) && (
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">
                    {[selectedParty.party_phone, selectedParty.party_email]
                      .filter(Boolean)
                      .join(" • ")}
                  </p>
                )}
              </div>
              <button
                type="button"
                onClick={closePartyModal}
                className="p-1.5 rounded-lg text-gray-400 hover:text-gray-600 dark:hover:text-slate-200 hover:bg-gray-100 dark:hover:bg-slate-700 transition-colors cursor-pointer"
              >
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>

            {/* Modal Summary Strip */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 px-5 py-3.5 border-b border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800">
              <div className="p-3 rounded-xl bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700/70">
                <div className="text-[11px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                  Total Due
                </div>
                <div
                  className={`mt-1 text-lg font-bold tabular-nums ${
                    selectedParty.party_type === "customer"
                      ? "text-emerald-600 dark:text-emerald-400"
                      : "text-amber-600 dark:text-amber-400"
                  }`}
                >
                  {formatCurrency(
                    detailData?.total_due ?? selectedParty.total_due
                  )}
                </div>
              </div>

              <div className="p-3 rounded-xl bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700/70">
                <div className="text-[11px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                  Overdue Amount
                </div>
                <div className="mt-1 text-lg font-bold text-rose-600 dark:text-rose-400 tabular-nums">
                  {formatCurrency(
                    detailData?.total_overdue ?? selectedParty.overdue_amount
                  )}
                </div>
              </div>

              <div className="p-3 rounded-xl bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700/70">
                <div className="text-[11px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                  Open Items
                </div>
                <div className="mt-1 text-lg font-bold text-gray-900 dark:text-slate-100 tabular-nums">
                  {detailData?.items?.length ?? selectedParty.open_invoices_count}
                </div>
              </div>

              <div className="p-3 rounded-xl bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700/70">
                <div className="text-[11px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                  Credit Terms
                </div>
                <div className="mt-1 text-lg font-bold text-gray-900 dark:text-slate-100">
                  {selectedParty.credit_days
                    ? `${selectedParty.credit_days} days`
                    : "Standard"}
                </div>
              </div>
            </div>

            {/* Modal Body */}
            <div className="flex-1 overflow-y-auto p-5">
              {detailLoading ? (
                <div className="py-12 text-center text-sm text-gray-500 dark:text-slate-400">
                  Loading due breakdown...
                </div>
              ) : detailError ? (
                <div className="py-10 text-center text-sm text-rose-600 dark:text-rose-400">
                  {detailError}
                </div>
              ) : !detailData?.items?.length ? (
                <div className="py-10 text-center text-sm text-gray-500 dark:text-slate-400">
                  No open due items found for this party.
                </div>
              ) : (
                <div className="overflow-x-auto border border-gray-200 dark:border-slate-700 rounded-xl">
                  <table className="w-full text-left border-collapse">
                    <thead>
                      <tr className="border-b border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-900/60 text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                        <th className="py-2.5 px-3.5">Date</th>
                        <th className="py-2.5 px-3.5">Reference</th>
                        <th className="py-2.5 px-3.5">Type</th>
                        <th className="py-2.5 px-3.5">Due Date</th>
                        <th className="py-2.5 px-3.5 text-right">Total</th>
                        <th className="py-2.5 px-3.5 text-right">
                          {selectedParty.party_type === "customer"
                            ? "Received"
                            : "Paid"}
                        </th>
                        <th className="py-2.5 px-3.5 text-right">Due</th>
                        <th className="py-2.5 px-3.5 text-center">Status</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100 dark:divide-slate-700/70 text-sm">
                      {detailData.items.map((item) => (
                        <tr
                          key={item.id}
                          className="hover:bg-gray-50/70 dark:hover:bg-slate-700/30"
                        >
                          <td className="py-2.5 px-3.5 text-gray-700 dark:text-slate-300 whitespace-nowrap">
                            {item.date || "—"}
                          </td>
                          <td className="py-2.5 px-3.5 font-medium text-gray-900 dark:text-slate-100 whitespace-nowrap">
                            {item.reference_number || "—"}
                          </td>
                          <td className="py-2.5 px-3.5 text-gray-600 dark:text-slate-300 whitespace-nowrap">
                            {item.type}
                          </td>
                          <td className="py-2.5 px-3.5 text-gray-600 dark:text-slate-300 whitespace-nowrap">
                            {item.due_date || "—"}
                          </td>
                          <td className="py-2.5 px-3.5 text-right tabular-nums text-gray-700 dark:text-slate-300 whitespace-nowrap">
                            {formatCurrency(item.total)}
                          </td>
                          <td className="py-2.5 px-3.5 text-right tabular-nums text-gray-600 dark:text-slate-400 whitespace-nowrap">
                            {formatCurrency(item.paid_or_received)}
                          </td>
                          <td className="py-2.5 px-3.5 text-right font-semibold tabular-nums text-gray-900 dark:text-slate-100 whitespace-nowrap">
                            {formatCurrency(item.due)}
                          </td>
                          <td className="py-2.5 px-3.5 text-center whitespace-nowrap">
                            {item.is_overdue ? (
                              <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-300">
                                {item.days_overdue}d Overdue
                              </span>
                            ) : (
                              <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300">
                                Open
                              </span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {/* Modal Footer */}
            <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-3.5 border-t border-gray-200 dark:border-slate-700 bg-gray-50/70 dark:bg-slate-900/50">
              <button
                type="button"
                onClick={() => {
                  const target =
                    selectedParty.party_type === "customer"
                      ? `/customer-ledger?customer_id=${selectedParty.party_id}`
                      : `/supplier-ledger?supplier_id=${selectedParty.party_id}`;
                  closePartyModal();
                  navigate(target);
                }}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-xs sm:text-sm font-medium text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors cursor-pointer"
              >
                <ArrowTopRightOnSquareIcon className="w-4 h-4" />
                Open{" "}
                {selectedParty.party_type === "customer"
                  ? "Customer"
                  : "Supplier"}{" "}
                Ledger
              </button>

              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => {
                    closePartyModal();
                    navigate("/payments");
                  }}
                  className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 text-xs sm:text-sm font-medium text-white transition-colors cursor-pointer"
                >
                  <BanknotesIcon className="w-4 h-4" />
                  Record Payment
                </button>
                <button
                  type="button"
                  onClick={closePartyModal}
                  className="px-3.5 py-2 rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-xs sm:text-sm font-medium text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors cursor-pointer"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
