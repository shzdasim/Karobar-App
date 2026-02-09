// src/pages/products/ProductForm.jsx
import React, { useEffect, useRef, useState, forwardRef, useImperativeHandle, useMemo } from "react";
import axios from "axios";
import Select from "react-select";
import AsyncSelect from "react-select/async";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";

// Icons (glassy header/buttons)
import {
  ArrowLeftIcon,
  PlusCircleIcon,
  PencilSquareIcon,
  CubeIcon,
} from "@heroicons/react/24/solid";

// FilePond
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";

// Styles for FilePond
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";

// 🧊 glass primitives
import { GlassCard, GlassInput, GlassBtn } from "@/components/glass.jsx";
import { useTheme } from "@/context/ThemeContext";

registerPlugin(FilePondPluginImagePreview, FilePondPluginFileValidateType);

// 👉 Normalize Laravel paginate payloads (or plain arrays) to a simple array
const asList = (payload) => (Array.isArray(payload) ? payload : (payload?.data ?? payload?.items ?? []));

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

// ===== Form Fields Component =====
const ProductFormFields = forwardRef(({ 
  form, 
  files, 
  batches, 
  categories, 
  suppliers, 
  brandOption, 
  isEdit, 
  handleChange, 
  loadBrandOptions, 
  getSmallSelectStyles, 
  isDark, 
  onFilesChange, 
  setFiles,
  themeColors,
  primaryTextColor
}, ref) => {
  // Create refs locally
  const nameRef = useRef(null);
  const formulationRef = useRef(null);
  const packSizeRef = useRef(null);
  const categorySelectRef = useRef(null);
  const brandSelectRef = useRef(null);
  const supplierSelectRef = useRef(null);

  // Expose focus methods to parent
  useImperativeHandle(ref, () => ({
    focusName: () => nameRef.current?.focus(),
  }));

  return (
    <>
      {/* Image - moved to top, compact */}
      <div className="flex gap-3 items-start">
        <div className="w-32 shrink-0">
          <label className="block text-xs font-medium mb-1 dark:text-slate-300">Image</label>
          <div className="rounded-xl bg-white/60 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-slate-600/60 p-1.5">
            <FilePond
              files={files}
              onupdatefiles={onFilesChange}
              allowMultiple={false}
              acceptedFileTypes={["image/*"]}
              labelIdle='<span class="text-xs dark:text-slate-300">Drop or Browse</span>'
              credits={false}
              stylePanelLayout="compact"
              styleLoadPlaceholder="Loading..."
            />
          </div>
        </div>
        
        {/* Code / Barcode inline */}
        <div className="flex-1 grid grid-cols-2 gap-2">
          <div>
            <label className="block text-xs font-medium mb-1 dark:text-slate-300">Product Code</label>
            <GlassInput type="text" name="product_code" value={form.product_code || ""} disabled className="w-full bg-white/70 dark:bg-slate-700/70 text-sm h-8 dark:text-slate-200" />
          </div>
          <div>
            <label className="block text-xs font-medium mb-1 dark:text-slate-300">Barcode</label>
            <GlassInput type="text" name="barcode" value={form.barcode || ""} disabled className="w-full bg-white/70 dark:bg-slate-700/70 text-sm h-8 dark:text-slate-200" />
          </div>
          <div>
            <label className="block text-xs font-medium mb-1 dark:text-slate-300">Rack</label>
            <GlassInput type="text" name="rack" value={form.rack || ""} onChange={handleChange} className="w-full text-sm h-8 dark:bg-slate-700/70 dark:text-slate-200" />
          </div>
          <div className="flex items-end">
            <label className="inline-flex items-center gap-1.5 text-xs dark:text-slate-300">
              <input
                type="checkbox"
                name="narcotic"
                checked={form.narcotic === "yes"}
                onChange={(e) => handleChange({ target: { name: "narcotic", value: e.target.checked ? "yes" : "no" } })}
                className="h-4 w-4 rounded border-gray-300 dark:border-slate-500 dark:bg-slate-600"
                style={{ accentColor: themeColors?.primary }}
              />
              <span>Narcotic</span>
            </label>
          </div>
        </div>
      </div>

      {/* Name / Formulation / Pack Size */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        <div>
          <label className="block text-xs font-medium mb-1 dark:text-slate-300">Name *</label>
          <GlassInput
            ref={nameRef}
            type="text"
            name="name"
            value={form.name || ""}
            onChange={handleChange}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                formulationRef.current?.focus();
              }
            }}
            className="w-full text-sm h-8 dark:bg-slate-700/70 dark:text-slate-200"
            placeholder="Product name"
          />
        </div>
        <div>
          <label className="block text-xs font-medium mb-1 dark:text-slate-300">Formulation</label>
          <GlassInput
            ref={formulationRef}
            type="text"
            name="formulation"
            value={form.formulation || ""}
            onChange={handleChange}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                packSizeRef.current?.focus();
              }
            }}
            className="w-full text-sm h-8 dark:bg-slate-700/70 dark:text-slate-200"
          />
        </div>
        <div>
          <label className="block text-xs font-medium mb-1 dark:text-slate-300">Pack Size</label>
          <GlassInput
            ref={packSizeRef}
            type="text"
            name="pack_size"
            value={form.pack_size || ""}
            onChange={handleChange}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                categorySelectRef.current?.focus();
              }
            }}
            className="w-full text-sm h-8 dark:bg-slate-700/70 dark:text-slate-200"
          />
        </div>
      </div>

      {/* Category / Brand / Supplier */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        <div>
          <label className="block text-xs font-medium mb-1 dark:text-slate-300">Category</label>
          <Select
            ref={categorySelectRef}
            options={asList(categories).map((c) => ({ value: c.id, label: c.name }))}
            value={asList(categories).map((c) => ({ value: c.id, label: c.name })).find((opt) => opt.value === Number(form.category_id)) || null}
            onChange={(opt) => {
              handleChange({ target: { name: "category_id", value: opt?.value } });
              setTimeout(() => brandSelectRef.current?.focus(), 0);
            }}
            classNamePrefix="rs"
            isSearchable
            styles={getSmallSelectStyles(isDark)}
            menuPortalTarget={typeof document !== "undefined" ? document.body : null}
          />
        </div>
        <div>
          <label className="block text-xs font-medium mb-1 dark:text-slate-300">Brand</label>
          <AsyncSelect
            ref={brandSelectRef}
            cacheOptions
            defaultOptions
            loadOptions={loadBrandOptions}
            value={brandOption}
            onChange={(opt) => {
              handleChange({ target: { name: "brand_id", value: opt?.value ?? null } });
              setTimeout(() => supplierSelectRef.current?.focus(), 0);
            }}
            classNamePrefix="rs"
            isSearchable
            styles={getSmallSelectStyles(isDark)}
            placeholder="Search brand..."
            noOptionsMessage={() => "Type to search..."}
            menuPortalTarget={typeof document !== "undefined" ? document.body : null}
          />
        </div>
        <div>
          <label className="block text-xs font-medium mb-1 dark:text-slate-300">Supplier</label>
          <Select
            ref={supplierSelectRef}
            options={asList(suppliers).map((s) => ({ value: s.id, label: s.name }))}
            value={asList(suppliers).map((s) => ({ value: s.id, label: s.name })).find((opt) => opt.value === Number(form.supplier_id)) || null}
            onChange={(opt) => {
              handleChange({ target: { name: "supplier_id", value: opt?.value } });
              setTimeout(() => saveBtnRef.current?.focus(), 0);
            }}
            classNamePrefix="rs"
            isSearchable
            styles={getSmallSelectStyles(isDark)}
            menuPortalTarget={typeof document !== "undefined" ? document.body : null}
          />
        </div>
      </div>

      {/* Description - compact */}
      <div>
        <label className="block text-xs font-medium mb-1 dark:text-slate-300">Description</label>
        <textarea
          name="description"
          value={form.description || ""}
          onChange={handleChange}
          className="w-full h-16 px-3 py-2 rounded-xl bg-white/70 dark:bg-slate-700/70 backdrop-blur-sm border border-gray-200/70 dark:border-slate-600/70 ring-1 ring-transparent focus:ring-blue-400/40 shadow-sm focus:outline-none text-sm resize-none dark:text-slate-200"
          placeholder="Optional notes..."
        />
      </div>

      {/* Compact pricing table */}
      <div>
        <div className="rounded-xl overflow-hidden ring-1 ring-gray-200/70 dark:ring-slate-600/70 bg-white/70 dark:bg-slate-700/70 backdrop-blur-sm">
          <table className="w-full text-[11px] text-gray-900 dark:text-slate-200">
            <thead className="bg-white/80 dark:bg-slate-600/80 backdrop-blur-sm border-b border-gray-200/70 dark:border-slate-500/70">
              <tr className="text-left">
                <th className="px-2 py-1.5 dark:text-slate-200">Qty</th>
                <th className="px-2 py-1.5 dark:text-slate-200">Pack P.</th>
                <th className="px-2 py-1.5 dark:text-slate-200">Pack S.</th>
                <th className="px-2 py-1.5 dark:text-slate-200">Unit P.</th>
                <th className="px-2 py-1.5 dark:text-slate-200">Unit S.</th>
                <th className="px-2 py-1.5 dark:text-slate-200">Avg</th>
                <th className="px-2 py-1.5 dark:text-slate-200">Mrg%</th>
                <th className="px-2 py-1.5 w-16 dark:text-slate-200">Max.Disc</th>
              </tr>
            </thead>
            <tbody>
              <tr className="odd:bg-white/90 even:bg-white/70 dark:odd:bg-slate-700/90 dark:even:bg-slate-700/70">
                {[
                  { name: "quantity", disabled: true, value: form.quantity || "" },
                  { name: "pack_purchase_price", disabled: true, value: form.pack_purchase_price || "" },
                  { name: "pack_sale_price", disabled: true, value: form.pack_sale_price || "" },
                  { name: "unit_purchase_price", disabled: true, value: form.unit_purchase_price || "" },
                  { name: "unit_sale_price", disabled: true, value: form.unit_sale_price || "" },
                  { name: "avg_price", disabled: true, value: form.avg_price || "" },
                  { name: "margin", disabled: true, value: form.margin || "" },
                ].map((cfg) => (
                  <td key={cfg.name} className="px-1 py-1">
                    <GlassInput
                      type="number"
                      name={cfg.name}
                      value={cfg.value}
                      disabled={cfg.disabled}
                      className="h-7 w-full bg-white/70 dark:bg-slate-600/70 text-center appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none dark:text-slate-200"
                    />
                  </td>
                ))}
                <td className="px-1 py-1">
                  <GlassInput
                    type="number"
                    name="max_discount"
                    value={form.max_discount || ""}
                    onChange={handleChange}
                    className="h-7 w-full text-center appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none dark:bg-slate-600/70 dark:text-slate-200"
                  />
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
});
ProductFormFields.displayName = "ProductFormFields";

export default function ProductForm({ initialData = null, onSubmitSuccess }) {
  const isEdit = !!initialData;
  const navigate = useNavigate();
  const formFieldsRef = useRef(null);

  const [form, setForm] = useState({
    ...initialData,
    narcotic: initialData?.narcotic || "no",
  });

  const [categories, setCategories] = useState([]);
  const [suppliers, setSuppliers] = useState([]);
  const [files, setFiles] = useState([]);
  const [batches, setBatches] = useState([]);
  const [brandOption, setBrandOption] = useState(null);
  const [isLoaded, setIsLoaded] = useState(false);

  const { isDark, theme } = useTheme();

  // Memoize theme colors for performance
  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#3b82f6',
        primaryHover: '#2563eb',
        primaryLight: '#dbeafe',
        secondary: '#8b5cf6',
        secondaryHover: '#7c3aed',
        secondaryLight: '#ede9fe',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
    };
  }, [theme]);

  // Calculate text color based on background brightness
  const primaryTextColor = useMemo(() => 
    getContrastText(themeColors.primaryHover || themeColors.primary), 
    [themeColors.primary, themeColors.primaryHover]
  );

  // ===== Load dropdown data =====
  const fetchDropdowns = async () => {
    const [catRes, supRes] = await Promise.all([axios.get("/api/categories"), axios.get("/api/suppliers")]);
    setCategories(asList(catRes.data));
    setSuppliers(asList(supRes.data));
  };

  // ===== Preload a new product code =====
  const fetchNewCodes = async () => {
    const res = await axios.get("/api/products/new-code");
    setForm((prev) => ({
      ...prev,
      product_code: res.data.product_code,
      barcode: res.data.barcode,
    }));
  };

  // ===== Load batches on edit =====
  const fetchBatches = async () => {
    if (isEdit && initialData?.id) {
      try {
        const res = await axios.get(`/api/products/${initialData.id}/batches`);
        setBatches(res.data);
      } catch (error) {
        console.error("Failed to fetch batches:", error);
      }
    }
  };

  // ===== Preload selected Brand label on edit =====
  const preloadBrandOption = async (brandId) => {
    if (!brandId) return;
    try {
      const res = await axios.get(`/api/brands/${brandId}`);
      const b = res.data;
      if (b?.id && b?.name) setBrandOption({ value: b.id, label: b.name });
    } catch {
      // ignore
    }
  };

  // Initial data fetch
  useEffect(() => {
    const loadData = async () => {
      await fetchDropdowns();
      if (!isEdit) {
        await fetchNewCodes();
      } else {
        if (initialData?.image) {
          const imageUrl = `${window.location.origin}/storage/${initialData.image}`;
          setFiles([{ source: imageUrl, options: { type: "remote" } }]);
        }
        await fetchBatches();
        if (initialData?.brand_id) await preloadBrandOption(initialData.brand_id);
      }
      setIsLoaded(true);
    };
    loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Focus Name after form is loaded and rendered
  useEffect(() => {
    if (isLoaded) {
      const timer = setTimeout(() => {
        formFieldsRef.current?.focusName();
      }, 100);
      return () => clearTimeout(timer);
    }
  }, [isLoaded, isEdit]);

  const handleChange = (e) => {
    setForm({ ...form, [e.target.name]: e.target.value });
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    try {
      const formData = new FormData();
      Object.keys(form).forEach((key) => {
        if (form[key] !== undefined && form[key] !== null) {
          formData.append(key, form[key]);
        }
      });

      if (files.length > 0 && files[0].file) {
        formData.append("image", files[0].file);
      }

      if (isEdit) {
        await axios.post(`/api/products/${initialData.id}?_method=PUT`, formData, {
          headers: { "Content-Type": "multipart/form-data" },
        });
        toast.success("✅ Product updated!");
      } else {
        await axios.post("/api/products", formData, {
          headers: { "Content-Type": "multipart/form-data" },
        });
        toast.success("✅ Product added!");
        setForm({ narcotic: "no" });
        setFiles([]);
        setBrandOption(null);
        await fetchNewCodes();
        setTimeout(() => formFieldsRef.current?.focusName(), 50);
      }

      if (onSubmitSuccess) onSubmitSuccess();
    } catch (error) {
      if (error.response?.status === 422) {
        const errors = error.response.data.errors;
        Object.values(errors).forEach((messages) => {
          messages.forEach((msg) => toast.error(msg));
        });
      } else {
        toast.error("❌ Something went wrong. Please try again.");
      }
    }
  };

  // ===== Keyboard Shortcuts =====
  useEffect(() => {
    const handleShortcut = (e) => {
      if (e.altKey && (e.key.toLowerCase() === "s" || e.key.toLowerCase() === "n")) {
        e.preventDefault();
        document.getElementById("save-product-btn-top")?.click();
      }
      if (e.altKey && e.key.toLowerCase() === "c") {
        e.preventDefault();
        navigate("/products");
      }
    };
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  }, [navigate]);

  // ===== Brand: async server-side search =====
  const loadBrandOptions = async (inputValue) => {
    try {
      const res = await axios.get("/api/brands", {
        params: { q_name: inputValue || "", per_page: 25 },
      });
      return asList(res.data).map((b) => ({ value: b.id, label: b.name }));
    } catch {
      return [];
    }
  };

  // ===== Helper for react-select styles =====
  const getSmallSelectStyles = (isDarkMode = false) => ({
    control: (base) => ({
      ...base,
      minHeight: "36px",
      height: "36px",
      fontSize: "13px",
      borderRadius: 12,
      borderColor: isDarkMode ? "rgba(71,85,105,0.8)" : "rgba(229,231,235,0.8)",
      backgroundColor: isDarkMode ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.7)",
      backdropFilter: "blur(6px)",
      boxShadow: isDarkMode ? "0 1px 2px rgba(0,0,0,0.2)" : "0 1px 2px rgba(15,23,42,0.06)",
    }),
    valueContainer: (base) => ({ ...base, height: "36px", padding: "0 10px" }),
    indicatorsContainer: (base) => ({ ...base, height: "36px" }),
    input: (base) => ({
      ...base,
      margin: 0,
      padding: 0,
      color: isDarkMode ? "#f1f5f9" : "#111827",
    }),
    menuPortal: (base) => ({ ...base, zIndex: 9999 }),
    menu: (base) => ({
      ...base,
      fontSize: "13px",
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

  // ===== Dynamic Button styles using theme colors =====
  const tintPrimary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintGlass = useMemo(() => `
    bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm text-slate-700 dark:text-gray-100 ring-1 ring-gray-200/60 dark:ring-white/10 hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // ===== Loading State =====
  if (!isLoaded) {
    return (
      <div className="p-3 md:p-4">
        <GlassCard>
          <div className="flex items-center justify-center py-12">
            <span className="text-gray-500 dark:text-slate-400">Loading...</span>
          </div>
        </GlassCard>
      </div>
    );
  }

  // ===== Add Mode: Full Width Card =====
  if (!isEdit) {
    return (
      <div className="p-3 md:p-4">
        <GlassCard>
          {/* Modern Card Header */}
          <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200/60 dark:border-gray-700/60">
            <div className="flex items-center gap-3">
              <div 
                className={`p-2 rounded-lg bg-gradient-to-br shadow-sm`}
                style={{ background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})` }}
              >
                <CubeIcon className="w-5 h-5 text-white" />
              </div>
              <div>
                <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Add Product</h1>
                <p className="text-xs text-gray-500 dark:text-gray-400">Enter product details</p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Link 
                to="/products" 
                className={`inline-flex items-center gap-1.5 h-8 px-3 rounded-lg ${tintGlass}`} 
                title="Back (Alt+C)"
              >
                <ArrowLeftIcon className="w-4 h-4" />
                <span className="text-sm">Back</span>
              </Link>
              <button
                id="save-product-btn-top"
                type="submit"
                form="product-form"
                className={`inline-flex items-center gap-1.5 h-8 px-4 rounded-lg ${tintPrimary}`}
                title="Save (Alt+S)"
                style={{ 
                  background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                  color: primaryTextColor
                }}
              >
                <PlusCircleIcon className="w-4 h-4" />
                <span className="text-sm font-medium">Save Product</span>
              </button>
            </div>
          </div>

          <form id="product-form" onSubmit={handleSubmit} className="p-3 space-y-3">
            <ProductFormFields
              ref={formFieldsRef}
              form={form}
              files={files}
              batches={batches}
              categories={categories}
              suppliers={suppliers}
              brandOption={brandOption}
              isEdit={isEdit}
              handleChange={handleChange}
              loadBrandOptions={loadBrandOptions}
              getSmallSelectStyles={getSmallSelectStyles}
              isDark={isDark}
              onFilesChange={setFiles}
              setFiles={setFiles}
              themeColors={themeColors}
              primaryTextColor={primaryTextColor}
            />
          </form>
        </GlassCard>
      </div>
    );
  }

  // ===== Edit Mode: 2-Column Grid with Batches Panel =====
  return (
    <div className="p-3 md:p-4 space-y-3">
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-3">
        {/* Left: Form */}
        <GlassCard className="lg:col-span-2">
          {/* Modern Card Header */}
          <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200/60 dark:border-gray-700/60">
            <div className="flex items-center gap-3">
              <div 
                className={`p-2 rounded-lg bg-gradient-to-br shadow-sm`}
                style={{ background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})` }}
              >
                <CubeIcon className="w-5 h-5 text-white" />
              </div>
              <div>
                <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Edit Product</h1>
                <p className="text-xs text-gray-500 dark:text-gray-400">Update product information</p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Link 
                to="/products" 
                className={`inline-flex items-center gap-1.5 h-8 px-3 rounded-lg ${tintGlass}`} 
                title="Back (Alt+C)"
              >
                <ArrowLeftIcon className="w-4 h-4" />
                <span className="text-sm">Back</span>
              </Link>
              <button
                id="save-product-btn-top"
                type="submit"
                form="product-form"
                className={`inline-flex items-center gap-1.5 h-8 px-4 rounded-lg ${tintPrimary}`}
                title="Save (Alt+S)"
                style={{ 
                  background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                  color: primaryTextColor
                }}
              >
                <PencilSquareIcon className="w-4 h-4" />
                <span className="text-sm font-medium">Save Changes</span>
              </button>
            </div>
          </div>

          <form id="product-form" onSubmit={handleSubmit} className="p-3 space-y-3">
            <ProductFormFields
              ref={formFieldsRef}
              form={form}
              files={files}
              batches={batches}
              categories={categories}
              suppliers={suppliers}
              brandOption={brandOption}
              isEdit={isEdit}
              handleChange={handleChange}
              loadBrandOptions={loadBrandOptions}
              getSmallSelectStyles={getSmallSelectStyles}
              isDark={isDark}
              onFilesChange={setFiles}
              setFiles={setFiles}
              themeColors={themeColors}
              primaryTextColor={primaryTextColor}
            />
          </form>
        </GlassCard>

        {/* Right: Batches Panel */}
        <GlassCard className="lg:col-span-1">
          <div className="px-3 py-2 border-b border-gray-200/60 dark:border-gray-700/60">
            <div className="flex items-center justify-between">
              <span className="font-semibold text-sm dark:text-slate-200">Batches</span>
              <span className="text-xs text-gray-500 dark:text-slate-400">{batches.length} items</span>
            </div>
          </div>
          <div className="p-3">
            {batches.length > 0 ? (
              <div className="rounded-xl overflow-hidden ring-1 ring-gray-200/70 dark:ring-slate-600/70 bg-white/60 dark:bg-slate-700/60">
                <table className="w-full text-xs">
                  <thead className="bg-white/80 dark:bg-slate-600/80 backdrop-blur-sm border-b border-gray-200/70 dark:border-slate-500/70 text-left">
                    <tr>
                      <th className="p-2 dark:text-slate-200">Batch #</th>
                      <th className="p-2 dark:text-slate-200">Expiry</th>
                      <th className="p-2 text-right dark:text-slate-200">Qty</th>
                    </tr>
                  </thead>
                  <tbody>
                    {batches.map((batch, i) => (
                      <tr key={batch.id} className={`text-gray-900 dark:text-slate-200 ${i % 2 ? "bg-white/70 dark:bg-slate-700/70" : "bg-white/90 dark:bg-slate-600/90"}`}>
                        <td className="p-2 font-medium">{batch.batch_number}</td>
                        <td className="p-2 text-gray-600 dark:text-slate-400">{batch.expiry_date}</td>
                        <td className="p-2 text-right">{batch.quantity}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <p className="text-gray-600 dark:text-slate-400 text-sm text-center py-4">No batches available</p>
            )}
          </div>
        </GlassCard>
      </div>
    </div>
  );
}

