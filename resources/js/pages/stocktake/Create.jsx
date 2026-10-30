// resources/js/pages/stocktake/Create.jsx
import React, { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import {
  ClipboardDocumentListIcon,
  ArrowLeftIcon,
  InformationCircleIcon,
} from "@heroicons/react/24/solid";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import { GlassCard, GlassSectionHeader } from "@/components";

const getContrastText = (hex) => {
  hex = (hex || "#3b82f6").replace("#", "");
  const r = parseInt(hex.substring(0, 2), 16);
  const g = parseInt(hex.substring(2, 4), 16);
  const b = parseInt(hex.substring(4, 6), 16);
  return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.5 ? "#1f2937" : "#ffffff";
};

const SESSION_TYPE_OPTIONS = [
  { value: "full", label: "Full Stocktake — Count all products" },
  { value: "cycle", label: "Cycle Count — Count a selected subset" },
];

const SCOPE_TYPE_OPTIONS = [
  { value: "category", label: "By Category" },
  { value: "brand", label: "By Brand" },
  { value: "supplier", label: "By Supplier" },
];

export default function CreateStocktake() {
  const navigate = useNavigate();
  const { isDark, theme } = useTheme();

  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || "#3b82f6",
    primaryHover: theme?.primary_hover || "#2563eb",
    primaryLight: theme?.primary_light || "#dbeafe",
    secondary: theme?.secondary_color || "#8b5cf6",
    secondaryHover: theme?.secondary_hover || "#7c3aed",
    danger: theme?.danger_color || "#ef4444",
    dangerHover: "#dc2626",
  }), [theme]);

  const primaryTextColor = useMemo(
    () => getContrastText(themeColors.primaryHover || themeColors.primary),
    [themeColors]
  );
  const buttonStyle = theme?.button_style || "modern";
  const getButtonClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark),
    [buttonStyle, themeColors, isDark]
  );
  const btnPrimary = getButtonClasses.primary;

  // Form state
  const [sessionType, setSessionType] = useState("full");
  const [isBlind, setIsBlind] = useState(true);
  const [scopeType, setScopeType] = useState("category");
  const [scopeId, setScopeId] = useState(null);
  const [notes, setNotes] = useState("");
  const [referenceNo, setReferenceNo] = useState("");
  const [saving, setSaving] = useState(false);

  // Scope options
  const [categories, setCategories] = useState([]);
  const [brands, setBrands] = useState([]);
  const [suppliers, setSuppliers] = useState([]);
  const [selectedScopeOption, setSelectedScopeOption] = useState(null);
  const [scopeSearchLoading, setScopeSearchLoading] = useState(false);

  // React-Select styles
  const selectStyles = useMemo(() => ({
    control: (base) => ({
      ...base,
      backgroundColor: isDark ? "#1e293b" : "#ffffff",
      borderColor: isDark ? "#475569" : "#d1d5db",
      color: isDark ? "#e2e8f0" : "#111827",
      boxShadow: "none",
      "&:hover": { borderColor: themeColors.primary },
    }),
    singleValue: (base) => ({ ...base, color: isDark ? "#e2e8f0" : "#111827" }),
    menu: (base) => ({ ...base, backgroundColor: isDark ? "#1e293b" : "#ffffff", zIndex: 50 }),
    option: (base, state) => ({
      ...base,
      backgroundColor: state.isFocused ? (isDark ? "#334155" : "#f1f5f9") : "transparent",
      color: isDark ? "#e2e8f0" : "#111827",
    }),
    input: (base) => ({ ...base, color: isDark ? "#e2e8f0" : "#111827" }),
    placeholder: (base) => ({ ...base, color: isDark ? "#64748b" : "#9ca3af" }),
  }), [isDark, themeColors.primary]);

  // Load reference number and scope data on mount
  useEffect(() => {
    axios.get("/api/stocktake-sessions/new-code").then(({ data }) => {
      setReferenceNo(data.reference_no);
    }).catch(() => {});

    axios.get("/api/categories/search", { params: { limit: 2000 } })
      .catch(() => axios.get("/api/categories"))
      .then(({ data }) => {
        const list = Array.isArray(data?.data) ? data.data : (Array.isArray(data) ? data : []);
        setCategories(list.map((c) => ({ value: c.id, label: c.name })));
      }).catch(() => {});

    axios.get("/api/brands/search", { params: { limit: 2000 } })
      .catch(() => axios.get("/api/brands", { params: { per_page: 2000 } }))
      .then(({ data }) => {
        const list = Array.isArray(data?.data) ? data.data : (Array.isArray(data) ? data : []);
        setBrands(list.map((b) => ({ value: b.id, label: b.name })));
      }).catch(() => {});

    axios.get("/api/suppliers/search", { params: { limit: 2000 } })
      .catch(() => axios.get("/api/suppliers"))
      .then(({ data }) => {
        const list = Array.isArray(data?.data) ? data.data : (Array.isArray(data) ? data : []);
        setSuppliers(list.map((s) => ({ value: s.id, label: s.name })));
      }).catch(() => {});
  }, []);

  // Reset scope when session type or scope type changes
  useEffect(() => {
    setScopeId(null);
    setSelectedScopeOption(null);
  }, [sessionType, scopeType]);

  // Live server-side search when typing in scope select
  const handleScopeInputChange = (inputValue, { action }) => {
    if (action !== "input-change") return;
    const trimmed = (inputValue || "").trim();
    if (!trimmed) return;

    const endpoint =
      scopeType === "category"
        ? "/api/categories/search"
        : scopeType === "brand"
        ? "/api/brands/search"
        : "/api/suppliers/search";

    setScopeSearchLoading(true);
    axios
      .get(endpoint, { params: { q: trimmed, limit: 200 } })
      .then(({ data }) => {
        const list = Array.isArray(data?.data) ? data.data : (Array.isArray(data) ? data : []);
        const mapped = list.map((item) => ({ value: item.id, label: item.name }));
        const mergeUnique = (prev) => {
          const map = new Map(prev.map((o) => [o.value, o]));
          mapped.forEach((o) => map.set(o.value, o));
          return Array.from(map.values()).sort((a, b) => a.label.localeCompare(b.label));
        };
        if (scopeType === "category") setCategories(mergeUnique);
        else if (scopeType === "brand") setBrands(mergeUnique);
        else if (scopeType === "supplier") setSuppliers(mergeUnique);
      })
      .catch(() => {})
      .finally(() => setScopeSearchLoading(false));
  };

  const scopeOptions = useMemo(() => {
    if (scopeType === "category") return categories;
    if (scopeType === "brand") return brands;
    if (scopeType === "supplier") return suppliers;
    return [];
  }, [scopeType, categories, brands, suppliers]);

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (sessionType === "cycle" && !scopeId) {
      toast.error("Please select a scope for cycle count");
      return;
    }
    setSaving(true);
    try {
      const payload = {
        session_type: sessionType,
        is_blind: isBlind,
        notes: notes || null,
        ...(sessionType === "cycle" ? { scope_type: scopeType, scope_id: scopeId } : {}),
      };
      const { data } = await axios.post("/api/stocktake-sessions", payload);
      toast.success(`Session ${data.reference_no} created`);
      navigate(`/stocktake-sessions/${data.id}`);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to create session");
    } finally {
      setSaving(false);
    }
  };

  // Input/label class helpers
  const labelCls = `block text-sm font-medium mb-1.5 ${isDark ? "text-slate-300" : "text-slate-700"}`;
  const inputCls = `w-full rounded-lg border px-3 py-2 text-sm focus:outline-none focus:ring-2 transition-colors ${
    isDark
      ? "bg-slate-800 border-slate-600 text-slate-100 focus:ring-blue-500/40 placeholder-slate-500"
      : "bg-white border-slate-300 text-slate-900 focus:ring-blue-500/40 placeholder-slate-400"
  }`;

  return (
    <div className="max-w-2xl mx-auto space-y-5 px-1">
      {/* Header */}
      <div className="flex items-center gap-3">
        <button
          onClick={() => navigate("/stocktake-sessions")}
          className={`p-2 rounded-lg transition-colors ${
            isDark ? "hover:bg-slate-700 text-slate-400" : "hover:bg-slate-100 text-slate-500"
          }`}
        >
          <ArrowLeftIcon className="w-5 h-5" />
        </button>
        <GlassSectionHeader
          title="New Stocktake Session"
          right={
            <span className={`text-xs ${isDark ? "text-slate-400" : "text-slate-500"}`}>
              Set up a cycle count or full stocktake
            </span>
          }
        />
      </div>

      <GlassCard className="p-6">
        <form onSubmit={handleSubmit} className="space-y-6">
          {/* Reference number (read-only preview) */}
          <div>
            <label className={labelCls}>Reference Number</label>
            <input
              type="text"
              value={referenceNo || "Auto-generated"}
              readOnly
              className={`${inputCls} opacity-60 cursor-not-allowed`}
            />
          </div>

          {/* Session Type */}
          <div>
            <label className={labelCls}>Session Type</label>
            <Select
              options={SESSION_TYPE_OPTIONS}
              value={SESSION_TYPE_OPTIONS.find((o) => o.value === sessionType)}
              onChange={(opt) => setSessionType(opt.value)}
              styles={selectStyles}
              isSearchable={false}
            />
          </div>

          {/* Cycle Count Scope (only when cycle) */}
          {sessionType === "cycle" && (
            <div className={`rounded-xl border p-4 space-y-4 ${
              isDark ? "border-slate-700 bg-slate-800/40" : "border-blue-100 bg-blue-50/50"
            }`}>
              <p className="text-sm font-medium flex items-center gap-2" style={{ color: themeColors.primary }}>
                <InformationCircleIcon className="w-4 h-4" />
                Cycle Count Scope — select which products to include
              </p>

              {/* Scope Type */}
              <div>
                <label className={labelCls}>Filter Products By</label>
                <Select
                  options={SCOPE_TYPE_OPTIONS}
                  value={SCOPE_TYPE_OPTIONS.find((o) => o.value === scopeType)}
                  onChange={(opt) => setScopeType(opt.value)}
                  styles={selectStyles}
                  isSearchable={false}
                />
              </div>

              {/* Scope Value */}
              <div>
                <label className={labelCls}>
                  Select {scopeType === "category" ? "Category" : scopeType === "brand" ? "Brand" : "Supplier"}
                </label>
                <Select
                  options={scopeOptions}
                  value={selectedScopeOption || scopeOptions.find((o) => o.value === scopeId) || null}
                  onChange={(opt) => {
                    setScopeId(opt?.value || null);
                    setSelectedScopeOption(opt || null);
                  }}
                  onInputChange={handleScopeInputChange}
                  isLoading={scopeSearchLoading}
                  styles={selectStyles}
                  isClearable
                  isSearchable
                  placeholder={`Search ${scopeType}…`}
                  noOptionsMessage={() => `No ${scopeType}s found`}
                />
              </div>
            </div>
          )}

          {/* Blind Mode Toggle */}
          <div className={`rounded-xl border p-4 ${
            isDark ? "border-slate-700 bg-slate-800/40" : "border-slate-200 bg-slate-50"
          }`}>
            <div className="flex items-start gap-3">
              <button
                type="button"
                onClick={() => setIsBlind((v) => !v)}
                className={`mt-0.5 relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none ${
                  isBlind ? "bg-orange-500" : "bg-slate-300 dark:bg-slate-600"
                }`}
              >
                <span
                  className={`inline-block h-5 w-5 transform rounded-full bg-white shadow transition duration-200 ${
                    isBlind ? "translate-x-5" : "translate-x-0"
                  }`}
                />
              </button>
              <div>
                <p className={`font-medium text-sm ${isDark ? "text-slate-200" : "text-slate-800"}`}>
                  Blind Count Mode {isBlind && <span className="text-orange-500 text-xs font-semibold">(ACTIVE)</span>}
                </p>
                <p className={`text-xs mt-0.5 ${isDark ? "text-slate-400" : "text-slate-500"}`}>
                  {isBlind
                    ? "System quantities are hidden from counters — they must count independently. Prevents confirmation bias."
                    : "System quantities are visible to counters. Useful for verification or non-critical counts."}
                </p>
              </div>
            </div>
          </div>

          {/* Notes */}
          <div>
            <label className={labelCls}>Notes (optional)</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={3}
              placeholder="e.g. End of month count, Shelf A–C only…"
              className={inputCls}
            />
          </div>

          {/* Actions */}
          <div className="flex items-center justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={() => navigate("/stocktake-sessions")}
              className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                isDark
                  ? "bg-slate-700 hover:bg-slate-600 text-slate-300"
                  : "bg-slate-100 hover:bg-slate-200 text-slate-600"
              }`}
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={saving}
              className={`px-6 py-2 rounded-lg text-sm font-semibold transition-colors disabled:opacity-60 ${btnPrimary.className}`}
              style={btnPrimary.style}
            >
              {saving ? "Creating…" : "Create Session"}
            </button>
          </div>
        </form>
      </GlassCard>
    </div>
  );
}

