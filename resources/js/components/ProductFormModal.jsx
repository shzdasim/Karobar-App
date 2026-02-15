// src/components/ProductFormModal.jsx
import React, { useEffect, useRef, useState, forwardRef, useImperativeHandle, useMemo } from "react";
import axios from "axios";
import Select from "react-select";
import AsyncSelect from "react-select/async";
import toast from "react-hot-toast";
import { useTheme } from "@/context/ThemeContext.jsx";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

// 👉 Normalize Laravel paginate payloads (or plain arrays) to a simple array
const asList = (payload) => (Array.isArray(payload) ? payload : (payload?.data ?? payload?.items ?? []));

const ProductFormModal = forwardRef(({ open, onClose, onProductCreated }, ref) => {
  const [form, setForm] = useState({
    product_code: "",
    barcode: "",
    name: "",
    formulation: "",
    pack_size: "",
    category_id: "",
    brand_id: "",
    supplier_id: "",
    description: "",
    quantity: "",
    pack_purchase_price: "",
    pack_sale_price: "",
    unit_purchase_price: "",
    unit_sale_price: "",
    whole_sale_pack_price: "",
    whole_sale_unit_price: "",
    whole_sale_margin: "",
    avg_price: "",
    margin: "",
    max_discount: "",
    rack: "",
    narcotic: "no",
  });

  const [categories, setCategories] = useState([]);
  const [suppliers, setSuppliers] = useState([]);
  const [brandOption, setBrandOption] = useState(null);
  const [saving, setSaving] = useState(false);
  const [windowPos, setWindowPos] = useState(() => {
    const saved = localStorage.getItem("productModalPos");
    if (saved) return JSON.parse(saved);
    const width = 800;
    const height = 600;
    const x = (window.innerWidth - width) / 2;
    const y = (window.innerHeight - height) / 2;
    return { x, y };
  });

  const [windowSize, setWindowSize] = useState(() => {
    const saved = localStorage.getItem("productModalSize");
    return saved ? JSON.parse(saved) : { width: 800, height: 600 };
  });

  const modalRef = useRef(null);
  const dragRef = useRef({ isDragging: false, offsetX: 0, offsetY: 0 });
  const nameInputRef = useRef(null);

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
        danger: '#ef4444',
        dangerHover: '#dc2626',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
      danger: theme.danger_color || '#ef4444',
      dangerHover: '#dc2626',
    };
  }, [theme]);

  const primaryTextColor = useMemo(() => 
    getContrastText(themeColors.primaryHover || themeColors.primary), 
    [themeColors.primary, themeColors.primaryHover]
  );

  const dangerTextColor = useMemo(() => 
    getContrastText(themeColors.dangerHover || themeColors.danger), 
    [themeColors.danger, themeColors.dangerHover]
  );

  // Fetch dropdowns and new code
  useEffect(() => {
    if (open) {
      const fetchData = async () => {
        try {
          const [catRes, supRes, codeRes] = await Promise.all([
            axios.get("/api/categories"),
            axios.get("/api/suppliers"),
            axios.get("/api/products/new-code")
          ]);
          setCategories(asList(catRes.data));
          setSuppliers(asList(supRes.data));
          setForm(prev => ({
            ...prev,
            product_code: codeRes.data.product_code || "",
            barcode: codeRes.data.barcode || ""
          }));
        } catch (error) {
          console.error("Failed to fetch data:", error);
        }
      };
      fetchData();
      
      // Focus name input after modal opens
      setTimeout(() => nameInputRef.current?.focus(), 100);
    }
  }, [open]);

  // Keyboard shortcut: Alt+S to save, Escape to close
  useEffect(() => {
    if (!open) return;
    
    const handleKeyDown = (e) => {
      if (e.altKey && e.key.toLowerCase() === "s") {
        e.preventDefault();
        handleSubmit(e);
      }
    };
    
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, form]);

  const handleChange = (e) => {
    const { name, value } = e.target;
    setForm(prev => ({ ...prev, [name]: value }));
  };

  const handleSubmit = async (e) => {
    e?.preventDefault();
    
    if (!form.name?.trim()) {
      toast.error("Product name is required");
      return;
    }

    try {
      setSaving(true);
      const formData = new FormData();
      Object.keys(form).forEach((key) => {
        if (form[key] !== undefined && form[key] !== null) {
          formData.append(key, form[key]);
        }
      });

      const { data } = await axios.post("/api/products", formData, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      
      toast.success("Product added successfully!");
      
      // Dispatch event to notify other components
      window.dispatchEvent(new CustomEvent("product:created", { detail: data }));
      
      // Reset form for next entry
      setForm(prev => ({
        ...prev,
        name: "",
        formulation: "",
        pack_size: "",
        category_id: "",
        brand_id: "",
        supplier_id: "",
        description: "",
        quantity: "",
        pack_purchase_price: "",
        pack_sale_price: "",
        unit_purchase_price: "",
        unit_sale_price: "",
        whole_sale_pack_price: "",
        whole_sale_unit_price: "",
        whole_sale_margin: "",
        avg_price: "",
        margin: "",
        max_discount: "",
        rack: "",
        narcotic: "no",
      }));
      
      // Fetch new code
      const codeRes = await axios.get("/api/products/new-code");
      setForm(prev => ({
        ...prev,
        product_code: codeRes.data.product_code || "",
        barcode: codeRes.data.barcode || ""
      }));
      
      // Focus name for next entry
      setTimeout(() => nameInputRef.current?.focus(), 50);
      
      if (onProductCreated) onProductCreated(data);
    } catch (error) {
      if (error.response?.status === 422) {
        const errors = error.response.data.errors;
        Object.values(errors).forEach((messages) => {
          messages.forEach((msg) => toast.error(msg));
        });
      } else {
        toast.error("Failed to create product");
      }
    } finally {
      setSaving(false);
    }
  };

  const handleClose = () => {
    onClose?.();
  };

  // Dragging
  const startDrag = (e) => {
    if (!modalRef.current) return;
    dragRef.current = {
      isDragging: true,
      offsetX: e.clientX - windowPos.x,
      offsetY: e.clientY - windowPos.y,
    };
    document.addEventListener("mousemove", handleDrag);
    document.addEventListener("mouseup", stopDrag);
  };

  const handleDrag = (e) => {
    if (!dragRef.current.isDragging) return;
    setWindowPos({
      x: e.clientX - dragRef.current.offsetX,
      y: e.clientY - dragRef.current.offsetY,
    });
  };

  const stopDrag = () => {
    dragRef.current.isDragging = false;
    localStorage.setItem("productModalPos", JSON.stringify(windowPos));
    document.removeEventListener("mousemove", handleDrag);
    document.removeEventListener("mouseup", stopDrag);
  };

  // Select styles
  const getSmallSelectStyles = (isDarkMode = false) => ({
    control: (base) => ({
      ...base,
      minHeight: "32px",
      height: "32px",
      fontSize: "12px",
      borderRadius: 8,
      borderColor: isDarkMode ? "rgba(71,85,105,0.8)" : "rgba(229,231,235,0.8)",
      backgroundColor: isDarkMode ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.7)",
      backdropFilter: "blur(6px)",
    }),
    valueContainer: (base) => ({ ...base, height: "32px", padding: "0 8px" }),
    input: (base) => ({ ...base, margin: 0, padding: 0 }),
    menuPortal: (base) => ({ ...base, zIndex: 9999 }),
    menu: (base) => ({
      ...base,
      fontSize: "12px",
      borderRadius: 8,
      backgroundColor: isDarkMode ? "rgba(30,41,59,0.95)" : "rgba(255,255,255,0.95)",
      backdropFilter: "blur(10px)",
    }),
    option: (base, state) => ({
      ...base,
      backgroundColor: isDarkMode
        ? state.isFocused ? "rgba(71,85,105,1)" : "rgba(51,65,85,1)"
        : state.isFocused ? "rgba(241,245,249,1)" : "rgba(255,255,255,1)",
      cursor: "pointer",
    }),
  });

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-[10000] flex items-center justify-center">
      <div className="absolute inset-0 bg-black/40" onClick={handleClose} />

      {/* Draggable Dialog */}
      <div
        ref={modalRef}
        className="absolute bg-white dark:bg-slate-800 rounded-xl shadow-2xl border border-gray-200 dark:border-slate-600 flex flex-col"
        style={{
          left: `${windowPos.x}px`,
          top: `${windowPos.y}px`,
          width: `${windowSize.width}px`,
          height: `${windowSize.height}px`,
          minWidth: "600px",
          minHeight: "400px",
        }}
      >
        {/* Header (Draggable) */}
        <div
          className="px-4 py-3 border-b flex items-center justify-between cursor-move bg-gray-50 dark:bg-slate-700 rounded-t-xl border-gray-200 dark:border-slate-600"
          onMouseDown={startDrag}
        >
          <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Add New Product</h3>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handleClose}
              className="text-xs px-2 py-1 rounded hover:bg-gray-100 dark:hover:bg-slate-600 text-gray-900 dark:text-gray-100"
            >
              ✕
            </button>
          </div>
        </div>

        {/* Body */}
        <form onSubmit={handleSubmit} className="flex-1 overflow-auto p-4">
          <div className="space-y-3">
            {/* Code / Barcode inline */}
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Product Code</label>
                <input
                  type="text"
                  name="product_code"
                  value={form.product_code || ""}
                  disabled
                  className="w-full h-8 px-2 text-xs border rounded bg-gray-100 dark:bg-slate-600 dark:text-slate-200"
                />
              </div>
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Barcode</label>
                <input
                  type="text"
                  name="barcode"
                  value={form.barcode || ""}
                  disabled
                  className="w-full h-8 px-2 text-xs border rounded bg-gray-100 dark:bg-slate-600 dark:text-slate-200"
                />
              </div>
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Rack</label>
                <input
                  type="text"
                  name="rack"
                  value={form.rack || ""}
                  onChange={handleChange}
                  className="w-full h-8 px-2 text-xs border rounded dark:bg-slate-700 dark:text-slate-200"
                />
              </div>
            </div>

            {/* Name / Formulation / Pack Size */}
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Name *</label>
                <input
                  ref={nameInputRef}
                  type="text"
                  name="name"
                  value={form.name || ""}
                  onChange={handleChange}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      document.getElementById("product-form-input")?.focus();
                    }
                  }}
                  className="w-full h-8 px-2 text-xs border rounded dark:bg-slate-700 dark:text-slate-200"
                  placeholder="Product name"
                />
              </div>
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Formulation</label>
                <input
                  type="text"
                  name="formulation"
                  id="product-form-input"
                  value={form.formulation || ""}
                  onChange={handleChange}
                  className="w-full h-8 px-2 text-xs border rounded dark:bg-slate-700 dark:text-slate-200"
                />
              </div>
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Pack Size</label>
                <input
                  type="text"
                  name="pack_size"
                  value={form.pack_size || ""}
                  onChange={handleChange}
                  className="w-full h-8 px-2 text-xs border rounded dark:bg-slate-700 dark:text-slate-200"
                />
              </div>
            </div>

            {/* Category / Brand / Supplier */}
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Category</label>
                <Select
                  options={categories.map((c) => ({ value: c.id, label: c.name }))}
                  value={categories.map((c) => ({ value: c.id, label: c.name })).find((opt) => opt.value === Number(form.category_id)) || null}
                  onChange={(opt) => setForm(prev => ({ ...prev, category_id: opt?.value || "" }))}
                  isSearchable
                  classNamePrefix="rs"
                  styles={getSmallSelectStyles(isDark)}
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  placeholder="Select..."
                />
              </div>
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Brand</label>
                <AsyncSelect
                  cacheOptions
                  defaultOptions
                  loadOptions={async (inputValue) => {
                    try {
                      const res = await axios.get("/api/brands", { params: { q_name: inputValue || "", per_page: 25 } });
                      return asList(res.data).map((b) => ({ value: b.id, label: b.name }));
                    } catch { return []; }
                  }}
                  value={brandOption}
                  onChange={(opt) => {
                    setBrandOption(opt);
                    setForm(prev => ({ ...prev, brand_id: opt?.value ?? null }));
                  }}
                  isSearchable
                  classNamePrefix="rs"
                  styles={getSmallSelectStyles(isDark)}
                  placeholder="Search brand..."
                  noOptionsMessage={() => "Type to search..."}
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                />
              </div>
              <div>
                <label className="block text-xs font-medium mb-1 dark:text-slate-300">Supplier</label>
                <Select
                  options={suppliers.map((s) => ({ value: s.id, label: s.name }))}
                  value={suppliers.map((s) => ({ value: s.id, label: s.name })).find((opt) => opt.value === Number(form.supplier_id)) || null}
                  onChange={(opt) => setForm(prev => ({ ...prev, supplier_id: opt?.value || "" }))}
                  isSearchable
                  classNamePrefix="rs"
                  styles={getSmallSelectStyles(isDark)}
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  placeholder="Select..."
                />
              </div>
            </div>

            {/* Description */}
            <div>
              <label className="block text-xs font-medium mb-1 dark:text-slate-300">Description</label>
              <textarea
                name="description"
                value={form.description || ""}
                onChange={handleChange}
                className="w-full h-16 px-2 py-1 text-xs border rounded dark:bg-slate-700 dark:text-slate-200 resize-none"
                placeholder="Optional notes..."
              />
            </div>

            {/* Pricing table - compact */}
            <div>
              <div className="rounded-lg overflow-hidden ring-1 ring-gray-200 dark:ring-slate-600 bg-white dark:bg-slate-700">
                <table className="w-full text-[11px] text-gray-900 dark:text-slate-200">
                  <thead className="bg-gray-50 dark:bg-slate-600">
                    <tr className="text-left">
                      <th className="px-2 py-1.5">Pack P.</th>
                      <th className="px-2 py-1.5">Pack S.</th>
                      <th className="px-2 py-1.5">Unit P.</th>
                      <th className="px-2 py-1.5">Unit S.</th>
                      <th className="px-2 py-1.5">W.S.Pack</th>
                      <th className="px-2 py-1.5">W.S.Unit</th>
                      <th className="px-2 py-1.5">W.S.Mrg%</th>
                      <th className="px-2 py-1.5">Avg</th>
                      <th className="px-2 py-1.5">Mrg%</th>
                      <th className="px-2 py-1.5">Max.Disc</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr className="bg-white dark:bg-slate-700">
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="pack_purchase_price"
                          value={form.pack_purchase_price || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="pack_sale_price"
                          value={form.pack_sale_price || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="unit_purchase_price"
                          value={form.unit_purchase_price || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="unit_sale_price"
                          value={form.unit_sale_price || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="whole_sale_pack_price"
                          value={form.whole_sale_pack_price || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="whole_sale_unit_price"
                          value={form.whole_sale_unit_price || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="whole_sale_margin"
                          value={form.whole_sale_margin || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="avg_price"
                          value={form.avg_price || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="margin"
                          value={form.margin || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                      <td className="px-1 py-1">
                        <input
                          type="number"
                          name="max_discount"
                          value={form.max_discount || ""}
                          onChange={handleChange}
                          className="h-6 w-full px-1 text-center border rounded dark:bg-slate-600"
                          placeholder="0"
                        />
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>

            {/* Narcotic checkbox */}
            <div className="flex items-center gap-2">
              <label className="inline-flex items-center gap-1.5 text-xs dark:text-slate-300">
                <input
                  type="checkbox"
                  name="narcotic"
                  checked={form.narcotic === "yes"}
                  onChange={(e) => setForm(prev => ({ ...prev, narcotic: e.target.checked ? "yes" : "no" }))}
                  className="h-4 w-4 rounded border-gray-300 dark:border-slate-500 dark:bg-slate-600"
                  style={{ accentColor: themeColors?.primary }}
                />
                <span>Narcotic</span>
              </label>
            </div>
          </div>
        </form>

        {/* Footer */}
        <div className="px-4 py-3 border-t flex items-center justify-between bg-gray-50 dark:bg-slate-700 rounded-b-xl border-gray-200 dark:border-slate-600">
          <div className="text-[10px] text-gray-500 dark:text-gray-400">
            Alt+S to save • Esc to close
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handleClose}
              className="px-3 py-1.5 text-xs rounded border dark:border-slate-600 dark:text-slate-300 dark:hover:bg-slate-600"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={handleSubmit}
              disabled={saving}
              className="px-4 py-1.5 text-xs rounded text-white transition-all duration-200"
              style={{ 
                background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                color: primaryTextColor,
                opacity: saving ? 0.6 : 1,
                cursor: saving ? 'not-allowed' : 'pointer'
              }}
            >
              {saving ? 'Saving...' : 'Save Product'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
});

ProductFormModal.displayName = "ProductFormModal";
export default ProductFormModal;

