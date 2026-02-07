import { useState, useMemo, useEffect } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import AsyncSelect from "react-select/async";
import { XMarkIcon } from "@heroicons/react/24/solid";
import { GlassCard, GlassSectionHeader, GlassBtn } from "./Glass.jsx";
import { useTheme } from "@/context/ThemeContext";

// Helper to normalize API responses
const normalizeList = (payload) => {
  if (Array.isArray(payload)) return payload;
  if (Array.isArray(payload?.data)) return payload.data;
  if (Array.isArray(payload?.items)) return payload.items;
  return [];
};

// Debounce utility
const debouncePromise = (fn, wait = 300) => {
  let timeout;
  let pendingReject;
  return (...args) =>
    new Promise((resolve, reject) => {
      if (timeout) clearTimeout(timeout);
      if (pendingReject) pendingReject("debounced");
      pendingReject = reject;
      timeout = setTimeout(async () => {
        try {
          const res = await fn(...args);
          resolve(res);
        } catch (e) {
          reject(e);
        } finally {
          pendingReject = null;
        }
      }, wait);
    });
};

/**
 * Reusable bulk edit modal for updating metadata (category, brand, supplier) on multiple items
 * 
 * @param {Object} props
 * @param {boolean} props.isOpen - Whether the modal is visible
 * @param {Function} props.onClose - Function to call when closing the modal
 * @param {number} props.selectedCount - Number of items selected for bulk edit
 * @param {Array} props.selectedIds - Array of selected item IDs
 * @param {Function} props.onSaved - Function to call after successful save
 * @param {string} props.updateEndpoint - API endpoint for bulk update (default: "/api/products/bulk-update-meta")
 * @param {Object} props.tintClasses - Optional custom tint classes
 * @param {Array} props.fields - Array of field configs to include (default: ['category', 'brand', 'supplier'])
 */
export default function BulkEditModal({
  isOpen,
  onClose,
  selectedCount,
  selectedIds,
  onSaved,
  updateEndpoint = "/api/products/bulk-update-meta",
  tintClasses = {},
  fields = ["category", "brand", "supplier"],
}) {
  const [saving, setSaving] = useState(false);
  const { isDark } = useTheme();

  const [catOpt, setCatOpt] = useState(null);
  const [brandOpt, setBrandOpt] = useState(null);
  const [suppOpt, setSuppOpt] = useState(null);

  // Reset selections when modal opens
  useEffect(() => {
    if (isOpen) {
      setCatOpt(null);
      setBrandOpt(null);
      setSuppOpt(null);
    }
  }, [isOpen]);

  // Helper to merge dark mode styles for react-select
  const getSelectStyles = (isDarkMode = false) => ({
    control: (base) => ({
      ...base,
      minHeight: 36,
      height: 36,
      borderColor: isDarkMode ? "rgba(71,85,105,0.8)" : "rgba(229,231,235,0.8)",
      backgroundColor: isDarkMode ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.7)",
      backdropFilter: "blur(6px)",
      boxShadow: isDarkMode ? "0 1px 2px rgba(0,0,0,0.2)" : "0 1px 2px rgba(15,23,42,0.06)",
      borderRadius: 12,
    }),
    valueContainer: (base) => ({ ...base, height: 36, padding: "0 10px" }),
    indicatorsContainer: (base) => ({ ...base, height: 36 }),
    input: (base) => ({
      ...base,
      margin: 0,
      padding: 0,
      color: isDarkMode ? "#f1f5f9" : "#111827",
    }),
    menuPortal: (base) => ({ ...base, zIndex: 9999 }),
    menu: (base) => ({
      ...base,
      borderRadius: 12,
      overflow: "hidden",
      backgroundColor: isDarkMode ? "rgba(30,41,59,0.95)" : "rgba(255,255,255,0.95)",
      backdropFilter: "blur(10px)",
      boxShadow: isDarkMode ? "0 10px 30px -10px rgba(0,0,0,0.4)" : "0 10px 30px -10px rgba(30,64,175,0.18)",
      border: isDarkMode ? "1px solid rgba(71,85,105,0.5)" : "none",
    }),
    option: (base, state) => ({
      ...base,
      backgroundColor: isDarkMode
        ? state.isFocused
          ? "rgba(71,85,105,1)"
          : "rgba(51,65,85,1)"
        : state.isFocused
          ? "rgba(241,245,249,1)"
          : "rgba(255,255,255,1)",
      color: isDarkMode ? "#f1f5f9" : "#111827",
      cursor: "pointer",
    }),
    singleValue: (base) => ({
      ...base,
      color: isDarkMode ? "#f1f5f9" : "#111827",
    }),
    placeholder: (base) => ({
      ...base,
      color: isDarkMode ? "#64748b" : "#9ca3af",
    }),
  });

  // Fetch options from API
  const fetchOptions = async (endpoint, inputValue) => {
    const { data } = await axios.get(endpoint, { params: { q: inputValue || "", limit: 20 } });
    const list = normalizeList(data);
    return list.map((i) => ({ value: i.id, label: i.name }));
  };

  // Memoized debounced loaders
  const loadCategories = useMemo(
    () => debouncePromise((input) => fetchOptions("/api/categories/search", input), 300),
    []
  );
  const loadBrands = useMemo(
    () => debouncePromise((input) => fetchOptions("/api/brands/search", input), 300),
    []
  );
  const loadSuppliers = useMemo(
    () => debouncePromise((input) => fetchOptions("/api/suppliers/search", input), 300),
    []
  );

  const handleSubmit = async () => {
    try {
      if (!catOpt && !brandOpt && !suppOpt) {
        return toast.error("Choose at least one field to update.");
      }
      setSaving(true);
      await axios.patch(updateEndpoint, {
        product_ids: selectedIds,
        category_id: catOpt?.value ?? null,
        brand_id: brandOpt?.value ?? null,
        supplier_id: suppOpt?.value ?? null,
      });
      toast.success("Items updated successfully");
      await onSaved?.();
    } catch (e) {
      const apiMsg = e?.response?.data?.message || "Bulk update failed";
      toast.error(apiMsg);
    } finally {
      setSaving(false);
    }
  };

  if (!isOpen) return null;

  // Default tint classes
  const defaultTint = {
    blue: "bg-blue-500/85 text-white shadow-[0_6px_20px_-6px_rgba(37,99,235,0.45)] ring-1 ring-white/20 hover:bg-blue-500/95",
    glass: "bg-white/60 text-slate-700 ring-1 ring-white/30 hover:bg-white/75 dark:text-gray-100 dark:bg-slate-700/60 dark:ring-slate-600/30 dark:hover:bg-slate-600/75",
  };

  const tintBlue = tintClasses.blue || defaultTint.blue;
  const tintGlass = tintClasses.glass || defaultTint.glass;

  const hasChanges = catOpt || brandOpt || suppOpt;

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-black/40" onClick={onClose} />
      <div className="absolute inset-0 flex items-center justify-center p-4">
        <div className="w-full max-w-xl">
          <GlassCard>
            <GlassSectionHeader
              title={<span className="font-semibold">Bulk Edit ({selectedCount} selected)</span>}
              right={
                <GlassBtn className={`h-8 px-3 ${tintGlass}`} onClick={onClose} title="Close">
                  <XMarkIcon className="w-5 h-5" />
                </GlassBtn>
              }
            />
            <div className="px-4 pt-3 pb-4 space-y-4">
              <p className="text-sm text-gray-600">Leave any field blank to keep current values for that field.</p>

              <div className="grid grid-cols-1 gap-4">
                {fields.includes("category") && (
                  <div>
                    <label className="block text-sm font-medium mb-1">Category</label>
                    <AsyncSelect
                      classNamePrefix="rs"
                      cacheOptions
                      defaultOptions
                      loadOptions={loadCategories}
                      isSearchable
                      isClearable
                      value={catOpt}
                      onChange={setCatOpt}
                      menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                      styles={getSelectStyles(isDark)}
                      placeholder="(No change)"
                    />
                  </div>
                )}

                {fields.includes("brand") && (
                  <div>
                    <label className="block text-sm font-medium mb-1">Brand</label>
                    <AsyncSelect
                      classNamePrefix="rs"
                      cacheOptions
                      defaultOptions
                      loadOptions={loadBrands}
                      isSearchable
                      isClearable
                      value={brandOpt}
                      onChange={setBrandOpt}
                      menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                      styles={getSelectStyles(isDark)}
                      placeholder="(No change)"
                    />
                  </div>
                )}

                {fields.includes("supplier") && (
                  <div>
                    <label className="block text-sm font-medium mb-1">Supplier</label>
                    <AsyncSelect
                      classNamePrefix="rs"
                      cacheOptions
                      defaultOptions
                      loadOptions={loadSuppliers}
                      isSearchable
                      isClearable
                      value={suppOpt}
                      onChange={setSuppOpt}
                      menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                      styles={getSelectStyles(isDark)}
                      placeholder="(No change)"
                    />
                  </div>
                )}
              </div>
            </div>

            <div className="px-4 pb-4 flex items-center justify-end gap-2">
              <GlassBtn className={`min-w-[110px] ${tintGlass}`} onClick={onClose} disabled={saving}>
                Cancel
              </GlassBtn>
              <GlassBtn
                className={`min-w-[150px] ${tintBlue}`}
                onClick={handleSubmit}
                disabled={saving || !hasChanges}
              >
                {saving ? "Saving…" : "Save Changes"}
              </GlassBtn>
            </div>
          </GlassCard>
        </div>
      </div>
    </div>
  );
}

