import React, { useState, useEffect, useCallback, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import { useNavigate } from "react-router-dom";
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  BuildingStorefrontIcon,
  UsersIcon,
  BanknotesIcon,
  XMarkIcon,
  EyeIcon,
  FunnelIcon,
  ArrowTopRightOnSquareIcon,
  DocumentTextIcon,
} from "@heroicons/react/24/outline";
import { useCurrency } from "@/context/CurrencyContext.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";

function formatDisplayDate(dateStr) {
  if (!dateStr) return "—";
  const raw = String(dateStr).slice(0, 10);
  const parts = raw.split("-");
  if (parts.length !== 3) return raw;
  return `${parts[2]}-${parts[1]}-${parts[0]}`;
}

function getCreditSourceLabel(payment) {
  const idem = String(payment?.idempotency_key || "");
  if (idem.startsWith("sale_return:")) return "Sale Return Credit";
  if (idem.startsWith("purchase_return:")) return "Purchase Return Credit";
  return "Advance / Overpayment";
}

function isReturnCredit(payment) {
  const idem = String(payment?.idempotency_key || "");
  return idem.startsWith("sale_return:") || idem.startsWith("purchase_return:");
}

const SOURCE_OPTIONS = [
  { value: "all", label: "All Sources" },
  { value: "advance", label: "Advance / Overpayment" },
  { value: "return", label: "Return Credit Notes" },
];

const SORT_OPTIONS = [
  { value: "highest", label: "Highest Credit" },
  { value: "lowest", label: "Lowest Credit" },
  { value: "name_asc", label: "Name (A–Z)" },
  { value: "recent", label: "Most Recent" },
];

export default function PartyCredits() {
  const navigate = useNavigate();
  const { formatCurrency } = useCurrency();
  const { isDark, theme } = useTheme();
  const primaryColor = theme?.primary_color || "#2563eb";

  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        backgroundColor: isDark ? "rgba(15, 23, 42, 0.7)" : "#ffffff",
        borderColor: state.isFocused ? primaryColor : isDark ? "#334155" : "#e2e8f0",
        borderRadius: "0.75rem",
        minHeight: "42px",
        boxShadow: state.isFocused ? `0 0 0 2px ${primaryColor}33` : "none",
        "&:hover": {
          borderColor: state.isFocused ? primaryColor : isDark ? "#475569" : "#cbd5e1",
        },
      }),
      singleValue: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#0f172a",
        fontSize: "0.875rem",
      }),
      menu: (base) => ({
        ...base,
        backgroundColor: isDark ? "#0f172a" : "#ffffff",
        border: `1px solid ${isDark ? "#334155" : "#e2e8f0"}`,
        borderRadius: "0.75rem",
        zIndex: 60,
        overflow: "hidden",
      }),
      menuPortal: (base) => ({ ...base, zIndex: 9999 }),
      option: (base, state) => ({
        ...base,
        backgroundColor: state.isSelected
          ? primaryColor
          : state.isFocused
          ? isDark
            ? "#1e293b"
            : "#f1f5f9"
          : "transparent",
        color: state.isSelected ? "#ffffff" : isDark ? "#e2e8f0" : "#0f172a",
        fontSize: "0.875rem",
        cursor: "pointer",
      }),
      input: (base) => ({ ...base, color: isDark ? "#f8fafc" : "#0f172a" }),
      placeholder: (base) => ({ ...base, color: isDark ? "#64748b" : "#94a3b8" }),
      indicatorSeparator: () => ({ display: "none" }),
    }),
    [isDark, primaryColor]
  );

  // Filters
  const [partyType, setPartyType] = useState("all");
  const [source, setSource] = useState("all");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [minAmount, setMinAmount] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [sort, setSort] = useState("highest");

  // Data
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState([]);
  const [summary, setSummary] = useState({
    total_credit: 0,
    supplier_total: 0,
    supplier_count: 0,
    customer_total: 0,
    customer_count: 0,
    party_count: 0,
  });

  // Detail modal state
  const [selectedParty, setSelectedParty] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailData, setDetailData] = useState(null);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => clearTimeout(t);
  }, [search]);

  const fetchPartyCredits = useCallback(async () => {
    setLoading(true);
    try {
      const params = {
        party_type: partyType,
        source,
        sort,
      };
      if (debouncedSearch) params.search = debouncedSearch;
      if (minAmount !== "") params.min_amount = minAmount;
      if (dateFrom) params.date_from = dateFrom;
      if (dateTo) params.date_to = dateTo;

      const { data } = await axios.get("/api/payments/party-credits", {
        params,
        cache: false,
      });
      setRows(Array.isArray(data?.rows) ? data.rows : []);
      setSummary(
        data?.summary || {
          total_credit: 0,
          supplier_total: 0,
          supplier_count: 0,
          customer_total: 0,
          customer_count: 0,
          party_count: 0,
        }
      );
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to load party credits");
    } finally {
      setLoading(false);
    }
  }, [partyType, source, debouncedSearch, minAmount, dateFrom, dateTo, sort]);

  useEffect(() => {
    fetchPartyCredits();
  }, [fetchPartyCredits]);

  const openDetailModal = useCallback(async (row) => {
    setSelectedParty(row);
    setDetailData(null);
    setDetailLoading(true);
    try {
      const { data } = await axios.get("/api/payments/available-credits", {
        params: {
          party_type: row.party_type,
          party_id: row.party_id,
        },
        cache: false,
      });
      setDetailData(data);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to load credit details");
    } finally {
      setDetailLoading(false);
    }
  }, []);

  const closeDetailModal = useCallback(() => {
    setSelectedParty(null);
    setDetailData(null);
  }, []);

  useEffect(() => {
    if (!selectedParty) return;
    const onKeyDown = (e) => {
      if (e.key === "Escape") closeDetailModal();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [selectedParty, closeDetailModal]);

  const hasActiveFilters = useMemo(
    () =>
      partyType !== "all" ||
      source !== "all" ||
      search !== "" ||
      minAmount !== "" ||
      dateFrom !== "" ||
      dateTo !== "" ||
      sort !== "highest",
    [partyType, source, search, minAmount, dateFrom, dateTo, sort]
  );

  const resetFilters = () => {
    setPartyType("all");
    setSource("all");
    setSearch("");
    setDebouncedSearch("");
    setMinAmount("");
    setDateFrom("");
    setDateTo("");
    setSort("highest");
  };

  return (
    <div className="products-page w-full p-4 sm:p-6 space-y-5">
      {/* Page Header */}
      <div className="products-panel p-5 sm:p-6 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div className="flex items-start gap-3.5">
          <div
            className="w-11 h-11 rounded-2xl flex items-center justify-center shrink-0 border"
            style={{
              backgroundColor: `${primaryColor}15`,
              borderColor: `${primaryColor}30`,
              color: primaryColor,
            }}
          >
            <BanknotesIcon className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl sm:text-2xl font-semibold tracking-tight text-slate-900 dark:text-white">
              Supplier &amp; Customer Credits
            </h1>
            <p className="text-sm text-slate-600 dark:text-slate-400 mt-0.5">
              One row per supplier or customer showing total unapplied advance payments &amp; return credit notes. Click any row for full voucher details.
            </p>
          </div>
        </div>

        <button
          type="button"
          onClick={fetchPartyCredits}
          disabled={loading}
          className="inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl text-sm font-medium border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700/70 transition disabled:opacity-60 shrink-0"
        >
          <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
          Refresh
        </button>
      </div>

      {/* KPI Summary Cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="products-panel p-4 sm:p-5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Total Available Credit
            </span>
            <BanknotesIcon className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
          </div>
          <div className="mt-2 text-2xl font-bold tabular-nums text-slate-900 dark:text-white">
            {formatCurrency(summary.total_credit)}
          </div>
          <div className="mt-1 text-xs text-slate-500 dark:text-slate-400">
            Across {summary.party_count} {summary.party_count === 1 ? "party" : "parties"}
          </div>
        </div>

        <div className="products-panel p-4 sm:p-5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Credit With Suppliers
            </span>
            <BuildingStorefrontIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
          </div>
          <div className="mt-2 text-2xl font-bold tabular-nums text-blue-600 dark:text-blue-400">
            {formatCurrency(summary.supplier_total)}
          </div>
          <div className="mt-1 text-xs text-slate-500 dark:text-slate-400">
            {summary.supplier_count} {summary.supplier_count === 1 ? "supplier" : "suppliers"}
          </div>
        </div>

        <div className="products-panel p-4 sm:p-5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Customer Credit Balances
            </span>
            <UsersIcon className="w-5 h-5 text-violet-600 dark:text-violet-400" />
          </div>
          <div className="mt-2 text-2xl font-bold tabular-nums text-violet-600 dark:text-violet-400">
            {formatCurrency(summary.customer_total)}
          </div>
          <div className="mt-1 text-xs text-slate-500 dark:text-slate-400">
            {summary.customer_count} {summary.customer_count === 1 ? "customer" : "customers"}
          </div>
        </div>
      </div>

      {/* Filters Panel */}
      <div className="products-panel p-4 sm:p-5 space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          {/* Party Type Segmented Tabs */}
          <div className="inline-flex rounded-xl p-1 bg-slate-100 dark:bg-slate-800/80 border border-slate-200 dark:border-slate-700">
            {[
              { id: "all", label: "All Parties" },
              { id: "supplier", label: "Suppliers" },
              { id: "customer", label: "Customers" },
            ].map((tab) => {
              const active = partyType === tab.id;
              return (
                <button
                  key={tab.id}
                  type="button"
                  onClick={() => setPartyType(tab.id)}
                  className={`px-3.5 py-1.5 rounded-lg text-xs sm:text-sm font-medium transition ${
                    active
                      ? "bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-xs"
                      : "text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white"
                  }`}
                >
                  {tab.label}
                </button>
              );
            })}
          </div>

          {hasActiveFilters && (
            <button
              type="button"
              onClick={resetFilters}
              className="inline-flex items-center gap-1.5 text-xs font-medium text-rose-600 dark:text-rose-400 hover:underline"
            >
              <FunnelIcon className="w-3.5 h-3.5" />
              Reset Filters
            </button>
          )}
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-6 gap-3">
          {/* Search */}
          <div className="relative lg:col-span-2">
            <MagnifyingGlassIcon className="w-4 h-4 text-slate-400 absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search supplier or customer name / phone..."
              className="w-full pl-9 pr-3 py-2.5 rounded-xl text-sm border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900/70 text-slate-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500/30"
            />
          </div>

          {/* Credit Source */}
          <Select
            options={SOURCE_OPTIONS}
            value={SOURCE_OPTIONS.find((opt) => opt.value === source) || SOURCE_OPTIONS[0]}
            onChange={(opt) => setSource(opt?.value || "all")}
            styles={selectStyles}
            isSearchable={false}
            menuPortalTarget={typeof document !== "undefined" ? document.body : null}
            aria-label="Filter by credit source"
          />

          {/* Min Amount */}
          <input
            type="number"
            min="0"
            step="0.01"
            value={minAmount}
            onChange={(e) => setMinAmount(e.target.value)}
            placeholder="Min Credit"
            className="px-3 py-2.5 rounded-xl text-sm border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900/70 text-slate-900 dark:text-white"
          />

          {/* Date Range */}
          <div className="grid grid-cols-2 gap-2 lg:col-span-1">
            <input
              type="date"
              value={dateFrom}
              onChange={(e) => setDateFrom(e.target.value)}
              title="From Date"
              className="px-2.5 py-2.5 rounded-xl text-xs border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900/70 text-slate-900 dark:text-white"
            />
            <input
              type="date"
              value={dateTo}
              onChange={(e) => setDateTo(e.target.value)}
              title="To Date"
              className="px-2.5 py-2.5 rounded-xl text-xs border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900/70 text-slate-900 dark:text-white"
            />
          </div>

          {/* Sort */}
          <Select
            options={SORT_OPTIONS}
            value={SORT_OPTIONS.find((opt) => opt.value === sort) || SORT_OPTIONS[0]}
            onChange={(opt) => setSort(opt?.value || "highest")}
            styles={selectStyles}
            isSearchable={false}
            menuPortalTarget={typeof document !== "undefined" ? document.body : null}
            aria-label="Sort party credits"
          />
        </div>
      </div>

      {/* Single-Row-Per-Party Table */}
      <div className="products-panel overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="border-b border-slate-200 dark:border-slate-700/80 bg-slate-50/80 dark:bg-slate-800/50 text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
                <th className="py-3.5 px-4">Supplier / Customer</th>
                <th className="py-3.5 px-4">Type</th>
                <th className="py-3.5 px-4 text-center">Vouchers</th>
                <th className="py-3.5 px-4">Breakdown</th>
                <th className="py-3.5 px-4">Last Credit Date</th>
                <th className="py-3.5 px-4 text-right">Total Credit</th>
                <th className="py-3.5 px-4 text-right">Detail</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200/80 dark:divide-slate-700/60 text-sm">
              {loading ? (
                <tr>
                  <td colSpan={7} className="py-12 text-center text-slate-500 dark:text-slate-400">
                    Loading party credits...
                  </td>
                </tr>
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={7} className="py-14 text-center">
                    <div className="flex flex-col items-center gap-2 text-slate-500 dark:text-slate-400">
                      <BanknotesIcon className="w-9 h-9 opacity-40" />
                      <p className="font-medium text-slate-700 dark:text-slate-300">
                        No unapplied credits found
                      </p>
                      <p className="text-xs">
                        {hasActiveFilters
                          ? "Try adjusting or resetting your filters."
                          : "Advance payments, overpayments, and return credit notes will appear here."}
                      </p>
                    </div>
                  </td>
                </tr>
              ) : (
                rows.map((row) => {
                  const isSupplier = row.party_type === "supplier";
                  return (
                    <tr
                      key={`${row.party_type}-${row.party_id}`}
                      onClick={() => openDetailModal(row)}
                      className="cursor-pointer hover:bg-blue-50/60 dark:hover:bg-slate-800/60 transition-colors"
                    >
                      <td className="py-3.5 px-4">
                        <div className="font-semibold text-slate-900 dark:text-white">
                          {row.party_name}
                        </div>
                        {(row.party_phone || row.party_email) && (
                          <div className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                            {[row.party_phone, row.party_email].filter(Boolean).join(" · ")}
                          </div>
                        )}
                      </td>
                      <td className="py-3.5 px-4">
                        <span
                          className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium ${
                            isSupplier
                              ? "bg-blue-50 text-blue-700 dark:bg-blue-500/15 dark:text-blue-300"
                              : "bg-violet-50 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300"
                          }`}
                        >
                          {isSupplier ? (
                            <BuildingStorefrontIcon className="w-3.5 h-3.5" />
                          ) : (
                            <UsersIcon className="w-3.5 h-3.5" />
                          )}
                          {isSupplier ? "Supplier" : "Customer"}
                        </span>
                      </td>
                      <td className="py-3.5 px-4 text-center">
                        <span className="inline-flex items-center justify-center min-w-7 px-2 py-0.5 rounded-lg text-xs font-semibold bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 tabular-nums">
                          {row.credit_count}
                        </span>
                      </td>
                      <td className="py-3.5 px-4">
                        <div className="flex flex-wrap gap-1.5 text-xs">
                          {row.advance_credit > 0 && (
                            <span className="px-2 py-0.5 rounded-md bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300 tabular-nums">
                              Advance: {formatCurrency(row.advance_credit)}
                            </span>
                          )}
                          {row.return_credit > 0 && (
                            <span className="px-2 py-0.5 rounded-md bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300 tabular-nums">
                              Return: {formatCurrency(row.return_credit)}
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="py-3.5 px-4 text-slate-600 dark:text-slate-300 tabular-nums">
                        {formatDisplayDate(row.latest_credit_date)}
                      </td>
                      <td className="py-3.5 px-4 text-right font-bold text-emerald-600 dark:text-emerald-400 tabular-nums text-base">
                        {formatCurrency(row.total_credit)}
                      </td>
                      <td className="py-3.5 px-4 text-right">
                        <button
                          type="button"
                          onClick={(e) => {
                            e.stopPropagation();
                            openDetailModal(row);
                          }}
                          className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 transition"
                        >
                          <EyeIcon className="w-3.5 h-3.5" />
                          Details
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

      {/* Credit Detail Popup Modal */}
      {selectedParty && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-5 bg-black/55"
          onClick={closeDetailModal}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-label="Party Credit Details"
            onClick={(e) => e.stopPropagation()}
            className="w-full max-w-[95vw] xl:max-w-6xl max-h-[90vh] flex flex-col rounded-2xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 shadow-2xl overflow-hidden"
          >
            {/* Modal Header */}
            <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-700 flex items-start justify-between gap-4 bg-slate-50/70 dark:bg-slate-800/50">
              <div>
                <div className="flex items-center gap-2.5 flex-wrap">
                  <h2 className="text-lg font-semibold text-slate-900 dark:text-white">
                    {selectedParty.party_name}
                  </h2>
                  <span
                    className={`px-2.5 py-0.5 rounded-full text-xs font-medium ${
                      selectedParty.party_type === "supplier"
                        ? "bg-blue-50 text-blue-700 dark:bg-blue-500/15 dark:text-blue-300"
                        : "bg-violet-50 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300"
                    }`}
                  >
                    {selectedParty.party_type === "supplier" ? "Supplier" : "Customer"}
                  </span>
                </div>
                <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
                  {selectedParty.party_phone ? `Phone: ${selectedParty.party_phone} · ` : ""}
                  Total Available Credit:{" "}
                  <span className="font-bold text-emerald-600 dark:text-emerald-400">
                    {formatCurrency(detailData?.total_unapplied ?? selectedParty.total_credit)}
                  </span>
                </p>
              </div>

              <button
                type="button"
                onClick={closeDetailModal}
                className="p-2 rounded-xl text-slate-400 hover:text-slate-700 dark:hover:text-white hover:bg-slate-100 dark:hover:bg-slate-800 transition"
                aria-label="Close"
              >
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>

            {/* Modal Body */}
            <div className="p-5 overflow-y-auto flex-1">
              {detailLoading ? (
                <div className="py-12 text-center text-sm text-slate-500 dark:text-slate-400">
                  Loading credit breakdown...
                </div>
              ) : !detailData?.credits?.length ? (
                <div className="py-12 text-center text-sm text-slate-500 dark:text-slate-400">
                  No unapplied credit entries found for this party.
                </div>
              ) : (
                <div className="overflow-x-auto border border-slate-200 dark:border-slate-700 rounded-xl">
                  <table className="w-full text-left border-collapse text-sm">
                    <thead>
                      <tr className="border-b border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800/60 text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
                        <th className="py-3 px-3.5">Date</th>
                        <th className="py-3 px-3.5">Voucher #</th>
                        <th className="py-3 px-3.5">Source</th>
                        <th className="py-3 px-3.5">Method / Account</th>
                        <th className="py-3 px-3.5 text-right">Original</th>
                        <th className="py-3 px-3.5 text-right">Applied</th>
                        <th className="py-3 px-3.5 text-right">Remaining Credit</th>
                        <th className="py-3 px-3.5">Notes / Ref</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-200 dark:divide-slate-700/60">
                      {detailData.credits.map((item) => {
                        const ret = isReturnCredit(item);
                        return (
                          <tr key={item.id} className="hover:bg-slate-50/70 dark:hover:bg-slate-800/40">
                            <td className="py-3 px-3.5 whitespace-nowrap tabular-nums text-slate-700 dark:text-slate-300">
                              {formatDisplayDate(item.payment_date)}
                            </td>
                            <td className="py-3 px-3.5 whitespace-nowrap font-medium text-slate-900 dark:text-white">
                              <span className="inline-flex items-center gap-1.5">
                                <DocumentTextIcon className="w-4 h-4 text-slate-400" />
                                {item.payment_number || `#${item.id}`}
                              </span>
                            </td>
                            <td className="py-3 px-3.5 whitespace-nowrap">
                              <span
                                className={`px-2 py-0.5 rounded-md text-xs font-medium ${
                                  ret
                                    ? "bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300"
                                    : "bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300"
                                }`}
                              >
                                {getCreditSourceLabel(item)}
                              </span>
                            </td>
                            <td className="py-3 px-3.5 text-xs text-slate-600 dark:text-slate-300">
                              <div className="capitalize">{item.payment_method || "—"}</div>
                              {item.account?.name && (
                                <div className="text-slate-400">{item.account.name}</div>
                              )}
                            </td>
                            <td className="py-3 px-3.5 text-right tabular-nums text-slate-700 dark:text-slate-300">
                              {formatCurrency(item.amount)}
                            </td>
                            <td className="py-3 px-3.5 text-right tabular-nums text-slate-500 dark:text-slate-400">
                              {formatCurrency(item.allocated_amount)}
                            </td>
                            <td className="py-3 px-3.5 text-right font-bold tabular-nums text-emerald-600 dark:text-emerald-400">
                              {formatCurrency(item.unapplied_amount)}
                            </td>
                            <td className="py-3 px-3.5 text-xs text-slate-600 dark:text-slate-400 max-w-xs">
                              {item.reference && <div className="font-medium">Ref: {item.reference}</div>}
                              {item.notes && <div className="truncate" title={item.notes}>{item.notes}</div>}
                              {!item.reference && !item.notes && "—"}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {/* Modal Footer */}
            <div className="px-5 py-3.5 border-t border-slate-200 dark:border-slate-700 bg-slate-50/70 dark:bg-slate-800/50 flex flex-wrap items-center justify-between gap-3">
              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  onClick={() => {
                    closeDetailModal();
                    navigate(
                      selectedParty.party_type === "supplier"
                        ? "/supplier-ledger"
                        : "/customer-ledger"
                    );
                  }}
                  className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-medium border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition"
                >
                  <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                  Open {selectedParty.party_type === "supplier" ? "Supplier" : "Customer"} Ledger
                </button>
                <button
                  type="button"
                  onClick={() => {
                    closeDetailModal();
                    navigate("/payments");
                  }}
                  className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-medium border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition"
                >
                  <BanknotesIcon className="w-3.5 h-3.5" />
                  Payment Vouchers
                </button>
              </div>

              <button
                type="button"
                onClick={closeDetailModal}
                className="px-4 py-2 rounded-xl text-xs font-semibold bg-slate-900 dark:bg-white text-white dark:text-slate-900 hover:opacity-90 transition"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
