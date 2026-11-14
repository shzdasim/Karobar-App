// resources/js/pages/TradeSchemes/index.jsx
import React, { useEffect, useMemo, useRef, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import {
  MagnifyingGlassIcon,
  PencilSquareIcon,
  TrashIcon,
  PlusIcon,
  ArrowPathIcon,
  SparklesIcon,
  GiftIcon,
  CheckCircleIcon,
  XCircleIcon,
  TagIcon,
  BuildingStorefrontIcon,
  UsersIcon,
  CalendarIcon,
} from "@heroicons/react/24/outline";
import { useTheme } from "@/context/ThemeContext";

const TARGET_TYPE_OPTIONS = [
  { value: "product", label: "Specific Product" },
  { value: "brand", label: "Brand / Manufacturer" },
  { value: "category", label: "Category" },
];

const SALE_TYPE_OPTIONS = [
  { value: "all", label: "All Sales (Wholesale & Retail)" },
  { value: "wholesale", label: "Wholesale Only" },
  { value: "retail", label: "Retail Only" },
];

const STATUS_FILTER_OPTIONS = [
  { value: "all", label: "All Statuses" },
  { value: "active", label: "Active Only" },
  { value: "inactive", label: "Inactive Only" },
];

export default function TradeSchemesIndex() {
  const navigate = useNavigate();
  const { isDark, theme } = useTheme();
  const primaryColor = theme?.primary_color || "#3b82f6";

  const [schemes, setSchemes] = useState([]);
  const [loading, setLoading] = useState(false);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [targetTypeFilter, setTargetTypeFilter] = useState("all");
  const [saleTypeFilter, setSaleTypeFilter] = useState("all");
  const searchInputRef = useRef(null);

  // Unified select styles conforming to design system
  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        minHeight: "36px",
        borderRadius: "10px",
        backgroundColor: isDark ? "rgba(30, 41, 59, 0.85)" : "#ffffff",
        borderColor: state.isFocused
          ? primaryColor
          : isDark
          ? "rgba(71, 85, 105, 0.7)"
          : "rgba(209, 213, 219, 0.9)",
        boxShadow: state.isFocused ? `0 0 0 2px ${primaryColor}26` : "none",
        fontSize: "12px",
        color: isDark ? "#f8fafc" : "#0f172a",
        cursor: "pointer",
        "&:hover": {
          borderColor: isDark ? "rgba(100, 116, 139, 0.9)" : "rgba(156, 163, 175, 1)",
        },
      }),
      valueContainer: (base) => ({
        ...base,
        padding: "2px 8px",
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
      menu: (base) => ({
        ...base,
        backgroundColor: isDark ? "#1e293b" : "#ffffff",
        borderColor: isDark ? "rgba(71, 85, 105, 0.7)" : "#e2e8f0",
        borderRadius: "10px",
        zIndex: 50,
      }),
      option: (base, state) => ({
        ...base,
        fontSize: "12px",
        backgroundColor: state.isSelected
          ? primaryColor
          : state.isFocused
          ? isDark
            ? "rgba(51, 65, 85, 0.8)"
            : "#eff6ff"
          : "transparent",
        color: state.isSelected ? "#ffffff" : isDark ? "#f8fafc" : "#0f172a",
        cursor: "pointer",
      }),
    }),
    [isDark, primaryColor]
  );

  const fetchSchemes = useCallback(async () => {
    try {
      setLoading(true);
      const params = {};
      if (search.trim()) params.search = search.trim();
      if (statusFilter !== "all") params.status = statusFilter;
      if (targetTypeFilter !== "all") params.target_type = targetTypeFilter;
      if (saleTypeFilter !== "all") params.sale_type = saleTypeFilter;

      const { data } = await axios.get("/api/trade-schemes", { params });
      setSchemes(Array.isArray(data) ? data : data?.data || []);
    } catch {
      toast.error("Failed to load trade schemes.");
    } finally {
      setLoading(false);
    }
  }, [search, statusFilter, targetTypeFilter, saleTypeFilter]);

  useEffect(() => {
    document.title = "Trade Schemes & Bonus Slabs - Karobar App";
    const timer = setTimeout(() => {
      fetchSchemes();
    }, 200);
    return () => clearTimeout(timer);
  }, [fetchSchemes]);

  // Global keyboard shortcuts: Ctrl+N -> New Scheme, / or Ctrl+F -> Search
  useEffect(() => {
    const handleGlobalKeys = (e) => {
      const isCtrl = (e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey;
      if (isCtrl && (e.key === "n" || e.code === "KeyN")) {
        e.preventDefault();
        navigate("/trade-schemes/create");
      } else if (
        (e.key === "/" || (isCtrl && (e.key === "f" || e.code === "KeyF"))) &&
        document.activeElement?.tagName !== "INPUT" &&
        document.activeElement?.tagName !== "TEXTAREA"
      ) {
        e.preventDefault();
        searchInputRef.current?.focus();
        searchInputRef.current?.select?.();
      }
    };
    window.addEventListener("keydown", handleGlobalKeys);
    return () => window.removeEventListener("keydown", handleGlobalKeys);
  }, [navigate]);

  const handleToggleActive = async (scheme) => {
    try {
      const { data } = await axios.post(`/api/trade-schemes/${scheme.id}/toggle-active`);
      toast.success(data?.message || "Status updated");
      setSchemes((prev) =>
        prev.map((s) => (s.id === scheme.id ? { ...s, is_active: data.is_active } : s))
      );
    } catch {
      toast.error("Failed to update status.");
    }
  };

  const handleDelete = async (scheme) => {
    if (!window.confirm(`Delete trade scheme "${scheme.name}"?`)) return;
    try {
      await axios.delete(`/api/trade-schemes/${scheme.id}`);
      toast.success("Trade scheme deleted");
      setSchemes((prev) => prev.filter((s) => s.id !== scheme.id));
    } catch {
      toast.error("Failed to delete scheme.");
    }
  };

  const activeCount = useMemo(
    () => schemes.filter((s) => s.is_active).length,
    [schemes]
  );

  return (
    <div
      className={`p-4 md:p-6 space-y-4 min-h-screen ${
        isDark ? "bg-slate-900 text-slate-100" : "bg-slate-50/50 text-slate-800"
      }`}
    >
      {/* Header Banner */}
      <div
        className="px-5 py-4 rounded-2xl shadow-md border relative overflow-hidden flex flex-wrap items-center justify-between gap-4"
        style={{
          background: isDark
            ? "linear-gradient(135deg, rgba(30, 41, 59, 0.9) 0%, rgba(15, 23, 42, 0.95) 100%)"
            : `linear-gradient(135deg, ${primaryColor}10 0%, #ffffff 100%)`,
          borderColor: isDark ? "rgba(51, 65, 85, 0.8)" : "rgba(226, 232, 240, 0.9)",
        }}
      >
        <div className="flex items-center gap-3">
          <div
            className="w-10 h-10 rounded-xl flex items-center justify-center shrink-0 border"
            style={{
              backgroundColor: `${primaryColor}20`,
              borderColor: `${primaryColor}40`,
              color: primaryColor,
            }}
          >
            <GiftIcon className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-lg font-bold">Automated Trade Schemes &amp; Free Bonus Slabs</h1>
              <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-100 dark:bg-blue-950/80 text-blue-700 dark:text-blue-300 border border-blue-200 dark:border-blue-800">
                {activeCount} Active
              </span>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
              Promotional deals (10+1 free ratio or tiered brackets) automatically calculated and applied on sale invoices.
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={fetchSchemes}
            className="h-8.5 px-3 text-xs font-semibold rounded-xl border flex items-center gap-1.5 transition-colors cursor-pointer bg-white dark:bg-slate-800 border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700"
            title="Refresh schemes list"
          >
            <ArrowPathIcon className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`} />
            <span>Refresh</span>
          </button>
          <button
            type="button"
            onClick={() => navigate("/trade-schemes/create")}
            className="h-8.5 px-3.5 text-xs font-bold rounded-xl text-white shadow-xs flex items-center gap-1.5 transition-all active:scale-95 cursor-pointer"
            style={{ backgroundColor: primaryColor }}
            title="Create New Scheme (Ctrl+N)"
          >
            <PlusIcon className="w-4 h-4" />
            <span>New Scheme (Ctrl+N)</span>
          </button>
        </div>
      </div>

      {/* Filter Strip */}
      <div
        className={`p-3 rounded-xl border flex flex-wrap items-center gap-2.5 text-xs ${
          isDark ? "bg-slate-800/60 border-slate-700/80" : "bg-white border-gray-200 shadow-2xs"
        }`}
      >
        <div className="flex-1 min-w-[200px] relative">
          <MagnifyingGlassIcon className="w-4 h-4 absolute left-2.5 top-2.5 text-gray-400" />
          <input
            ref={searchInputRef}
            type="text"
            placeholder="Search scheme name, product, brand, category... (/)"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className={`w-full h-8.5 pl-8 pr-3 text-xs rounded-lg border outline-none transition-colors ${
              isDark
                ? "bg-slate-900 border-slate-700 text-slate-100 placeholder-slate-500 focus:border-blue-500"
                : "bg-gray-50 border-gray-300 text-gray-800 placeholder-gray-400 focus:border-blue-500"
            }`}
          />
        </div>

        <div className="w-40">
          <Select
            options={STATUS_FILTER_OPTIONS}
            value={STATUS_FILTER_OPTIONS.find((o) => o.value === statusFilter)}
            onChange={(opt) => setStatusFilter(opt.value)}
            styles={selectStyles}
            isSearchable={false}
          />
        </div>

        <div className="w-44">
          <Select
            options={[
              { value: "all", label: "All Target Types" },
              ...TARGET_TYPE_OPTIONS,
            ]}
            value={
              targetTypeFilter === "all"
                ? { value: "all", label: "All Target Types" }
                : TARGET_TYPE_OPTIONS.find((o) => o.value === targetTypeFilter)
            }
            onChange={(opt) => setTargetTypeFilter(opt.value)}
            styles={selectStyles}
            isSearchable={false}
          />
        </div>

        <div className="w-44">
          <Select
            options={[
              { value: "all", label: "All Sales Channels" },
              { value: "wholesale", label: "Wholesale Only" },
              { value: "retail", label: "Retail Only" },
            ]}
            value={
              saleTypeFilter === "all"
                ? { value: "all", label: "All Sales Channels" }
                : SALE_TYPE_OPTIONS.find((o) => o.value === saleTypeFilter)
            }
            onChange={(opt) => setSaleTypeFilter(opt.value)}
            styles={selectStyles}
            isSearchable={false}
          />
        </div>
      </div>

      {/* Schemes Data Table */}
      <div
        className={`rounded-2xl border overflow-hidden shadow-xs ${
          isDark ? "bg-slate-800/70 border-slate-700/80" : "bg-white border-gray-200"
        }`}
      >
        <div className="overflow-x-auto">
          <table className="w-full text-xs text-left border-collapse">
            <thead>
              <tr
                className={`border-b text-[10px] font-bold uppercase tracking-wider ${
                  isDark
                    ? "bg-slate-800 text-slate-400 border-slate-700"
                    : "bg-gray-50 text-gray-500 border-gray-200"
                }`}
              >
                <th className="px-4 py-2.5 w-24">Status</th>
                <th className="px-4 py-2.5">Scheme Name</th>
                <th className="px-4 py-2.5">Type &amp; Rule</th>
                <th className="px-4 py-2.5">Target Scope</th>
                <th className="px-4 py-2.5">Basis</th>
                <th className="px-4 py-2.5">Channel</th>
                <th className="px-4 py-2.5">Customer</th>
                <th className="px-4 py-2.5 text-right w-24">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-slate-700/60">
              {loading ? (
                <tr>
                  <td colSpan={8} className="px-4 py-8 text-center text-gray-400">
                    Loading trade schemes...
                  </td>
                </tr>
              ) : schemes.length === 0 ? (
                <tr>
                  <td colSpan={8} className="px-4 py-12 text-center text-gray-400">
                    <GiftIcon className="w-8 h-8 mx-auto mb-2 opacity-40" />
                    <p className="font-semibold">No Trade Schemes Found</p>
                    <p className="text-[11px] mt-0.5">
                      Press <strong>Ctrl+N</strong> or click "New Scheme" to create your first trade offer.
                    </p>
                  </td>
                </tr>
              ) : (
                schemes.map((s) => {
                  let targetLabel = "—";
                  if (s.target_type === "product") {
                    targetLabel = s.product
                      ? `📦 ${s.product.name}`
                      : `Product #${s.target_id}`;
                  } else if (s.target_type === "brand") {
                    targetLabel = s.brand ? `🏷️ Brand: ${s.brand.name}` : `Brand #${s.target_id}`;
                  } else if (s.target_type === "category") {
                    targetLabel = s.category
                      ? `📁 Category: ${s.category.name}`
                      : `Category #${s.target_id}`;
                  }

                  const slabsCount = s.slabs?.length || 0;
                  const firstSlab = s.slabs?.[0];

                  return (
                    <tr
                      key={s.id}
                      className={`hover:bg-gray-50/50 dark:hover:bg-slate-700/30 transition-colors ${
                        !s.is_active ? "opacity-60 bg-gray-50/30 dark:bg-slate-900/30" : ""
                      }`}
                    >
                      <td className="px-4 py-2.5">
                        <button
                          type="button"
                          onClick={() => handleToggleActive(s)}
                          className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold border transition-colors cursor-pointer ${
                            s.is_active
                              ? "bg-emerald-50 dark:bg-emerald-950/60 text-emerald-700 dark:text-emerald-300 border-emerald-300 dark:border-emerald-700 hover:bg-emerald-100"
                              : "bg-gray-100 dark:bg-slate-800 text-gray-600 dark:text-slate-400 border-gray-300 dark:border-slate-600 hover:bg-gray-200"
                          }`}
                          title="Click to toggle active status"
                        >
                          {s.is_active ? (
                            <>
                              <CheckCircleIcon className="w-3 h-3 text-emerald-600" />
                              <span>Active</span>
                            </>
                          ) : (
                            <>
                              <XCircleIcon className="w-3 h-3 text-gray-400" />
                              <span>Inactive</span>
                            </>
                          )}
                        </button>
                      </td>

                      <td className="px-4 py-2.5 font-semibold">
                        <div className="flex items-center gap-1.5">
                          <SparklesIcon className="w-3.5 h-3.5 text-amber-500 shrink-0" />
                          <span
                            onClick={() => navigate(`/trade-schemes/${s.id}/edit`)}
                            className="cursor-pointer hover:underline hover:text-blue-600 dark:hover:text-blue-400"
                          >
                            {s.name}
                          </span>
                        </div>
                        {s.notes && (
                          <p className="text-[10.5px] text-gray-400 font-normal truncate max-w-xs mt-0.5">
                            {s.notes}
                          </p>
                        )}
                      </td>

                      <td className="px-4 py-2.5 font-mono">
                        {s.scheme_type === "ratio" ? (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-blue-50 dark:bg-blue-950/50 text-blue-700 dark:text-blue-300 border border-blue-200 dark:border-blue-800 font-bold text-[10px]">
                            {firstSlab ? `${firstSlab.min_qty} + ${firstSlab.bonus_qty} Free` : "Ratio Scheme"}
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-purple-50 dark:bg-purple-950/50 text-purple-700 dark:text-purple-300 border border-purple-200 dark:border-purple-800 font-bold text-[10px]">
                            {slabsCount} Slab{slabsCount === 1 ? "" : "s"} Tier
                          </span>
                        )}
                      </td>

                      <td className="px-4 py-2.5">
                        <span className="font-medium text-gray-700 dark:text-slate-300">
                          {targetLabel}
                        </span>
                      </td>

                      <td className="px-4 py-2.5">
                        <span
                          className={`px-1.5 py-0.5 rounded text-[10px] font-semibold uppercase ${
                            s.bonus_type === "pack"
                              ? "bg-amber-50 dark:bg-amber-950/60 text-amber-700 dark:text-amber-300 border border-amber-200 dark:border-amber-800"
                              : "bg-teal-50 dark:bg-teal-950/60 text-teal-700 dark:text-teal-300 border border-teal-200 dark:border-teal-800"
                          }`}
                        >
                          {s.bonus_type}
                        </span>
                      </td>

                      <td className="px-4 py-2.5">
                        <span className="capitalize text-gray-600 dark:text-slate-300">
                          {s.sale_type}
                        </span>
                      </td>

                      <td className="px-4 py-2.5 text-gray-500 dark:text-slate-400">
                        {s.customer ? (
                          <span className="font-semibold text-gray-800 dark:text-slate-200">
                            {s.customer.name}
                          </span>
                        ) : (
                          <span>All Customers</span>
                        )}
                      </td>

                      <td className="px-4 py-2.5 text-right">
                        <div className="flex items-center justify-end gap-1">
                          <button
                            type="button"
                            onClick={() => navigate(`/trade-schemes/${s.id}/edit`)}
                            className="p-1 rounded-md text-gray-400 hover:text-blue-600 hover:bg-gray-100 dark:hover:bg-slate-700 transition-colors cursor-pointer"
                            title="Edit scheme"
                          >
                            <PencilSquareIcon className="w-4 h-4" />
                          </button>
                          <button
                            type="button"
                            onClick={() => handleDelete(s)}
                            className="p-1 rounded-md text-gray-400 hover:text-red-600 hover:bg-gray-100 dark:hover:bg-slate-700 transition-colors cursor-pointer"
                            title="Delete scheme"
                          >
                            <TrashIcon className="w-4 h-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
