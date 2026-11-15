// resources/js/pages/TradeSchemes/TradeSchemeForm.jsx
import React, { useEffect, useMemo, useRef, useState, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import AsyncSelect from "react-select/async";
import {
  ArrowLeftIcon,
  CheckIcon,
  PlusIcon,
  TrashIcon,
  GiftIcon,
  SparklesIcon,
  TagIcon,
  BuildingStorefrontIcon,
  UsersIcon,
  CalendarIcon,
  InformationCircleIcon,
  CubeIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import ProductSearchInput from "@/components/ProductSearchInput.jsx";
import ModernDatePicker from "@/components/ModernDatePicker.jsx";

const SCHEME_TYPE_OPTIONS = [
  { value: "ratio", label: "Every X Get Y (Ratio / Multiplier e.g. 10 + 1)" },
  { value: "slab", label: "Tiered Quantity Slabs (Brackets e.g. 10-24, 25-49)" },
];

const TARGET_TYPE_OPTIONS = [
  { value: "product", label: "Specific Product" },
  { value: "brand", label: "Brand / Manufacturer (All Products)" },
  { value: "category", label: "Category (All Products)" },
];

const BONUS_TYPE_OPTIONS = [
  { value: "pack", label: "Pack Quantity (Full Boxes/Cartons)" },
  { value: "unit", label: "Unit Quantity (Loose Pieces/Strips)" },
];

const SALE_TYPE_OPTIONS = [
  { value: "all", label: "All Sales (Wholesale & Retail)" },
  { value: "wholesale", label: "Wholesale Only (B2B)" },
  { value: "retail", label: "Retail Counter Only" },
];

export default function TradeSchemeForm({
  initialData = null,
  onSubmit,
  saving = false,
  isEdit = false,
}) {
  const navigate = useNavigate();
  const { isDark, theme } = useTheme();
  const primaryColor = theme?.primary_color || "#3b82f6";
  const primaryHoverColor = theme?.primary_hover || "#2563eb";
  const buttonStyle = theme?.button_style || "modern";

  const btnClasses = useMemo(
    () =>
      getThemeButtonClasses(
        buttonStyle,
        {
          primary: primaryColor,
          primaryHover: primaryHoverColor,
          danger: "#ef4444",
          dangerHover: "#dc2626",
          success: "#10b981",
          successHover: "#059669",
        },
        isDark
      ),
    [buttonStyle, primaryColor, primaryHoverColor, isDark]
  );

  // Form State
  const [name, setName] = useState(initialData?.name || "");
  const [schemeType, setSchemeType] = useState(initialData?.scheme_type || "ratio");
  const [targetType, setTargetType] = useState(initialData?.target_type || "product");
  const [targetProduct, setTargetProduct] = useState(
    initialData?.target_type === "product" && initialData?.product
      ? initialData.product
      : null
  );
  const [targetBrand, setTargetBrand] = useState(
    initialData?.target_type === "brand" && initialData?.brand
      ? { value: initialData.brand.id, label: initialData.brand.name }
      : null
  );
  const [targetCategory, setTargetCategory] = useState(
    initialData?.target_type === "category" && initialData?.category
      ? { value: initialData.category.id, label: initialData.category.name }
      : null
  );

  const [bonusType, setBonusType] = useState(initialData?.bonus_type || "pack");
  const [saleType, setSaleType] = useState(initialData?.sale_type || "all");
  const [customer, setCustomer] = useState(
    initialData?.customer
      ? {
          value: initialData.customer.id,
          label: `${initialData.customer.name}${
            initialData.customer.phone ? ` (${initialData.customer.phone})` : ""
          }`,
        }
      : null
  );

  const [startDate, setStartDate] = useState(
    initialData?.start_date ? String(initialData.start_date).slice(0, 10) : ""
  );
  const [endDate, setEndDate] = useState(
    initialData?.end_date ? String(initialData.end_date).slice(0, 10) : ""
  );
  const [isActive, setIsActive] = useState(
    initialData?.is_active !== undefined ? Boolean(initialData.is_active) : true
  );
  const [priority, setPriority] = useState(initialData?.priority ?? 0);
  const [notes, setNotes] = useState(initialData?.notes || "");

  // Slabs array
  const [slabs, setSlabs] = useState(() => {
    if (Array.isArray(initialData?.slabs) && initialData.slabs.length > 0) {
      return initialData.slabs.map((s) => ({
        min_qty: s.min_qty,
        max_qty: s.max_qty ?? "",
        bonus_qty: s.bonus_qty,
        bonus_product: s.bonus_product || s.bonusProduct || null,
      }));
    }
    return [{ min_qty: 10, max_qty: "", bonus_qty: 1, bonus_product: null }];
  });

  // Live test preview input
  const [testQty, setTestQty] = useState(25);

  // Field refs for Enter-key navigation
  const nameRef = useRef(null);
  const schemeTypeSelectRef = useRef(null);
  const targetTypeSelectRef = useRef(null);
  const productSelectRef = useRef(null);
  const brandSelectRef = useRef(null);
  const categorySelectRef = useRef(null);
  const bonusTypeSelectRef = useRef(null);
  const saleTypeSelectRef = useRef(null);
  const customerSelectRef = useRef(null);
  const startDateRef = useRef(null);
  const endDateRef = useRef(null);
  const priorityRef = useRef(null);
  const notesRef = useRef(null);
  const slabMinRefs = useRef([]);
  const slabMaxRefs = useRef([]);
  const slabBonusRefs = useRef([]);
  const submitBtnRef = useRef(null);

  const focusTargetSelector = () => {
    if (targetType === "product") {
      if (typeof productSelectRef.current?.openMenu === "function") {
        productSelectRef.current.openMenu();
      } else {
        productSelectRef.current?.focus?.();
      }
    } else if (targetType === "brand") {
      brandSelectRef.current?.focus?.();
    } else if (targetType === "category") {
      categorySelectRef.current?.focus?.();
    }
  };

  // Populate data when editing
  useEffect(() => {
    if (initialData) {
      setName(initialData.name || "");
      setSchemeType(initialData.scheme_type || "ratio");
      setTargetType(initialData.target_type || "product");
      if (initialData.target_type === "product" && initialData.product) {
        setTargetProduct(initialData.product);
      }
      if (initialData.target_type === "brand" && initialData.brand) {
        setTargetBrand({ value: initialData.brand.id, label: initialData.brand.name });
      }
      if (initialData.target_type === "category" && initialData.category) {
        setTargetCategory({
          value: initialData.category.id,
          label: initialData.category.name,
        });
      }
      setBonusType(initialData.bonus_type || "pack");
      setSaleType(initialData.sale_type || "all");
      if (initialData.customer) {
        setCustomer({
          value: initialData.customer.id,
          label: `${initialData.customer.name}${
            initialData.customer.phone ? ` (${initialData.customer.phone})` : ""
          }`,
        });
      }
      setStartDate(initialData.start_date ? String(initialData.start_date).slice(0, 10) : "");
      setEndDate(initialData.end_date ? String(initialData.end_date).slice(0, 10) : "");
      setIsActive(initialData.is_active !== undefined ? Boolean(initialData.is_active) : true);
      setPriority(initialData.priority ?? 0);
      setNotes(initialData.notes || "");
      if (Array.isArray(initialData.slabs) && initialData.slabs.length > 0) {
        setSlabs(
          initialData.slabs.map((s) => ({
            min_qty: s.min_qty,
            max_qty: s.max_qty ?? "",
            bonus_qty: s.bonus_qty,
            bonus_product: s.bonus_product || s.bonusProduct || null,
          }))
        );
      }
    }
  }, [initialData]);

  // Unified select styles conforming to Karobar modern design system
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
        fontSize: "12.5px",
        color: isDark ? "#f8fafc" : "#0f172a",
        cursor: "pointer",
        transition: "all 0.15s ease",
        "&:hover": {
          borderColor: primaryColor,
        },
      }),
      menu: (base) => ({
        ...base,
        borderRadius: "10px",
        backgroundColor: isDark ? "#1e293b" : "#ffffff",
        boxShadow: isDark
          ? "0 10px 25px -5px rgba(0,0,0,0.5), 0 8px 10px -6px rgba(0,0,0,0.5)"
          : "0 10px 25px -5px rgba(0,0,0,0.1), 0 8px 10px -6px rgba(0,0,0,0.1)",
        border: isDark ? "1px solid rgba(71, 85, 105, 0.6)" : "1px solid rgba(226, 232, 240, 0.9)",
        overflow: "hidden",
        zIndex: 60,
      }),
      option: (base, state) => ({
        ...base,
        fontSize: "12.5px",
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
      singleValue: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#0f172a",
        fontSize: "12.5px",
        fontWeight: "500",
      }),
      input: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#0f172a",
        fontSize: "12.5px",
      }),
      placeholder: (base) => ({
        ...base,
        color: isDark ? "#94a3b8" : "#9ca3af",
        fontSize: "12.5px",
      }),
    }),
    [isDark, primaryColor]
  );

  // Async search loaders
  const loadBrands = useCallback(async (inputValue) => {
    try {
      const { data } = await axios.get("/api/brands/search", {
        params: { q: inputValue || "" },
      });
      const list = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
      return list.map((b) => ({ value: b.id, label: b.name }));
    } catch {
      return [];
    }
  }, []);

  const loadCategories = useCallback(async (inputValue) => {
    try {
      const { data } = await axios.get("/api/categories/search", {
        params: { q: inputValue || "" },
      });
      const list = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
      return list.map((c) => ({ value: c.id, label: c.name }));
    } catch {
      return [];
    }
  }, []);

  const loadCustomers = useCallback(async (inputValue) => {
    try {
      const { data } = await axios.get("/api/customers", {
        params: { search: inputValue || "", per_page: 25 },
      });
      const list = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
      return list.map((c) => ({
        value: c.id,
        label: `${c.name}${c.phone ? ` (${c.phone})` : ""}`,
      }));
    } catch {
      return [];
    }
  }, []);

  // Slab handlers
  const handleAddSlab = () => {
    setSlabs((prev) => {
      const last = prev[prev.length - 1];
      const nextMin = last ? Number(last.max_qty || last.min_qty || 0) + 1 : 10;
      return [
        ...prev,
        {
          min_qty: nextMin,
          max_qty: "",
          bonus_qty: 1,
          bonus_product: null,
        },
      ];
    });
    setTimeout(() => {
      const newIdx = slabs.length;
      slabMinRefs.current[newIdx]?.focus();
      slabMinRefs.current[newIdx]?.select();
    }, 50);
  };

  const handleRemoveSlab = (idx) => {
    if (slabs.length <= 1) {
      toast.error("At least one bonus slab tier is required.");
      return;
    }
    setSlabs((prev) => prev.filter((_, i) => i !== idx));
  };

  const handleSlabChange = (idx, field, val) => {
    setSlabs((prev) => {
      const copy = [...prev];
      copy[idx] = { ...copy[idx], [field]: val };
      return copy;
    });
  };

  // Keyboard navigation on Enter
  const handleKeyDown = (e, nextRef) => {
    if (e.key === "Enter") {
      e.preventDefault();
      if (nextRef && typeof nextRef.focus === "function") {
        nextRef.focus();
        nextRef.select?.();
      } else if (nextRef?.current && typeof nextRef.current.focus === "function") {
        nextRef.current.focus();
        nextRef.current.select?.();
      }
    }
  };

  // Global form hotkeys: Ctrl+S or Ctrl+Enter save, Esc cancel, Ctrl+N add slab
  useEffect(() => {
    const handleGlobalKeys = (e) => {
      const isCtrl = (e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey;
      if (isCtrl && (e.key === "s" || e.code === "KeyS" || e.key === "Enter")) {
        e.preventDefault();
        handleSubmit(e);
      } else if (e.key === "Escape") {
        e.preventDefault();
        navigate("/trade-schemes");
      } else if (isCtrl && (e.key === "n" || e.code === "KeyN") && schemeType === "slab") {
        e.preventDefault();
        handleAddSlab();
      }
    };
    window.addEventListener("keydown", handleGlobalKeys);
    return () => window.removeEventListener("keydown", handleGlobalKeys);
  }, [schemeType, slabs, name, targetType, targetProduct, targetBrand, targetCategory, bonusType, saleType, customer, startDate, endDate, isActive, priority, notes]);

  // Fetch full product pricing details when targetProduct is chosen
  useEffect(() => {
    const pid = targetProduct?.id || targetProduct?.value;
    if (targetType === "product" && pid && targetProduct?.avg_price === undefined && targetProduct?.unit_purchase_price === undefined) {
      axios
        .get(`/api/products/${pid}`)
        .then(({ data }) => {
          if (data?.id) {
            setTargetProduct((prev) => (prev && (prev.id === data.id || prev.value === data.id) ? { ...prev, ...data } : prev));
          }
        })
        .catch(() => {});
    }
  }, [targetType, targetProduct?.id, targetProduct?.value]);

  // Live calculation preview + Profit & Margin Impact Simulator
  const livePreview = useMemo(() => {
    const qty = Math.max(0, Number(testQty || 0));
    if (qty <= 0 || slabs.length === 0) {
      return { bonus: 0, text: "Enter a test quantity to see live scheme bonus calculation.", profitMetrics: null };
    }

    let awarded = 0;
    let text = "";

    if (schemeType === "ratio") {
      const s0 = slabs[0];
      const minQ = Number(s0?.min_qty || 0);
      const bQ = Number(s0?.bonus_qty || 0);
      if (minQ > 0) {
        const mult = Math.floor(qty / minQ);
        awarded = mult * bQ;
        text =
          awarded > 0
            ? `Customer purchasing ${qty} ${bonusType}s gets ${awarded} FREE ${bonusType}${
                awarded === 1 ? "" : "s"
              } (${mult} × ${bQ})!`
            : `Customer purchasing ${qty} ${bonusType}s gets 0 free. Minimum qualifying quantity is ${minQ} ${bonusType}s.`;
      }
    } else {
      const sorted = [...slabs].sort((a, b) => Number(a.min_qty) - Number(b.min_qty));
      let matched = false;
      for (const s of sorted) {
        const minQ = Number(s.min_qty || 0);
        const maxQ = s.max_qty !== "" && s.max_qty !== null ? Number(s.max_qty) : null;
        if (qty >= minQ && (maxQ === null || qty <= maxQ)) {
          awarded = Number(s.bonus_qty || 0);
          text = `Customer purchasing ${qty} ${bonusType}s falls into slab [${minQ} – ${
            maxQ ?? "∞"
          }] and gets ${awarded} FREE ${bonusType}${awarded === 1 ? "" : "s"}!`;
          matched = true;
          break;
        }
      }
      if (!matched) {
        text = `Customer purchasing ${qty} ${bonusType}s does not qualify for any slab bracket.`;
      }
    }

    let profitMetrics = null;
    if (targetType === "product" && targetProduct) {
      const packSize = Math.max(1, Number(targetProduct.pack_size || 1));
      const unitCost =
        Number(targetProduct.avg_price || 0) ||
        Number(targetProduct.unit_purchase_price || 0) ||
        (Number(targetProduct.pack_purchase_price || 0) > 0
          ? Number(targetProduct.pack_purchase_price) / packSize
          : 0);
      const unitSalePrice =
        saleType === "wholesale"
          ? Number(targetProduct.whole_sale_unit_price || 0) ||
            (Number(targetProduct.whole_sale_pack_price || 0) > 0
              ? Number(targetProduct.whole_sale_pack_price) / packSize
              : 0) ||
            Number(targetProduct.unit_sale_price || 0)
          : Number(targetProduct.unit_sale_price || 0) ||
            (Number(targetProduct.pack_sale_price || 0) > 0
              ? Number(targetProduct.pack_sale_price) / packSize
              : 0);

      if (unitCost > 0 && unitSalePrice > 0) {
        const paidUnits = bonusType === "pack" ? qty * packSize : qty;
        const bonusUnits = bonusType === "pack" ? awarded * packSize : awarded;
        const totalUnitsOut = paidUnits + bonusUnits;
        const baseMarginPct = Number((((unitSalePrice - unitCost) / unitSalePrice) * 100).toFixed(2));
        const revenue = Number((paidUnits * unitSalePrice).toFixed(2));
        const totalCost = Number((totalUnitsOut * unitCost).toFixed(2));
        const netProfit = Number((revenue - totalCost).toFixed(2));
        const realizedMarginPct =
          revenue > 0 ? Number((((revenue - totalCost) / revenue) * 100).toFixed(2)) : -100;
        const effectiveUnitPrice =
          totalUnitsOut > 0 ? Number((revenue / totalUnitsOut).toFixed(2)) : unitSalePrice;
        const minPaidForOneFree =
          unitSalePrice > unitCost ? Math.ceil(unitCost / (unitSalePrice - unitCost)) : null;

        profitMetrics = {
          unitCost: Number(unitCost.toFixed(2)),
          unitSalePrice: Number(unitSalePrice.toFixed(2)),
          paidUnits,
          bonusUnits,
          totalUnitsOut,
          baseMarginPct,
          realizedMarginPct,
          revenue,
          totalCost,
          netProfit,
          effectiveUnitPrice,
          minPaidForOneFree,
        };
      }
    }

    return { bonus: awarded, text, profitMetrics };
  }, [testQty, schemeType, slabs, bonusType, targetType, targetProduct, saleType]);

  // Submission handler
  const handleSubmit = (e) => {
    if (e?.preventDefault) e.preventDefault();

    if (!name.trim()) {
      toast.error("Please enter a scheme name.");
      nameRef.current?.focus();
      return;
    }

    let targetId = null;
    if (targetType === "product") {
      targetId = targetProduct?.id || targetProduct?.value;
      if (!targetId) {
        toast.error("Please select a target product for this scheme.");
        return;
      }
    } else if (targetType === "brand") {
      targetId = targetBrand?.value || targetBrand?.id;
      if (!targetId) {
        toast.error("Please select a target brand.");
        brandSelectRef.current?.focus();
        return;
      }
    } else if (targetType === "category") {
      targetId = targetCategory?.value || targetCategory?.id;
      if (!targetId) {
        toast.error("Please select a target category.");
        categorySelectRef.current?.focus();
        return;
      }
    }

    if (slabs.length === 0) {
      toast.error("At least one bonus slab must be defined.");
      return;
    }

    for (let i = 0; i < slabs.length; i++) {
      const s = slabs[i];
      if (s.min_qty === "" || Number(s.min_qty) <= 0) {
        toast.error(`Slab tier #${i + 1}: Min quantity must be greater than 0.`);
        slabMinRefs.current[i]?.focus();
        return;
      }
      if (s.bonus_qty === "" || Number(s.bonus_qty) <= 0) {
        toast.error(`Slab tier #${i + 1}: Bonus quantity must be greater than 0.`);
        slabBonusRefs.current[i]?.focus();
        return;
      }
      if (schemeType === "slab" && s.max_qty !== "" && Number(s.max_qty) < Number(s.min_qty)) {
        toast.error(`Slab tier #${i + 1}: Max quantity cannot be less than Min quantity.`);
        slabMaxRefs.current[i]?.focus();
        return;
      }
    }

    const payload = {
      name: name.trim(),
      scheme_type: schemeType,
      target_type: targetType,
      target_id: Number(targetId),
      bonus_type: bonusType,
      sale_type: saleType,
      customer_id: customer?.value ? Number(customer.value) : null,
      start_date: startDate || null,
      end_date: endDate || null,
      is_active: Boolean(isActive),
      priority: Number(priority || 0),
      notes: notes.trim() || null,
      slabs: slabs.map((s) => ({
        min_qty: Number(s.min_qty),
        max_qty: s.max_qty !== "" && s.max_qty !== null ? Number(s.max_qty) : null,
        bonus_qty: Number(s.bonus_qty),
        bonus_product_id: s.bonus_product?.id || s.bonus_product?.value || null,
      })),
    };

    if (typeof onSubmit === "function") {
      onSubmit(payload);
    }
  };

  return (
    <div className="w-full pb-16 space-y-4">
      {/* Top Banner & Header Bar */}
      <div
        className={`px-4 py-3 rounded-2xl border transition-all ${
          isDark
            ? "bg-slate-800/90 border-slate-700/80 shadow-lg shadow-black/20"
            : "bg-white border-gray-200/90 shadow-sm"
        }`}
      >
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => navigate("/trade-schemes")}
              className={`p-2 rounded-xl border transition-all ${
                isDark
                  ? "bg-slate-700/60 border-slate-600 text-slate-300 hover:bg-slate-700 hover:text-white"
                  : "bg-gray-100/80 border-gray-200 text-gray-700 hover:bg-gray-200"
              }`}
              title="Back to Schemes (Esc)"
            >
              <ArrowLeftIcon className="w-5 h-5" />
            </button>

            <div
              className="w-10 h-10 rounded-xl flex items-center justify-center shadow-inner"
              style={{
                background: `linear-gradient(135deg, ${primaryColor} 0%, ${primaryHoverColor} 100%)`,
              }}
            >
              <GiftIcon className="w-5 h-5 text-white" />
            </div>

            <div>
              <div className="flex items-center gap-2 flex-wrap">
                <h1 className="text-lg font-bold text-gray-900 dark:text-white">
                  {isEdit ? "Edit Trade Scheme" : "Create Trade Scheme & Bonus Slabs"}
                </h1>
                <span
                  className="px-2 py-0.5 rounded-md text-[10px] font-bold uppercase tracking-wider text-white"
                  style={{ backgroundColor: primaryColor }}
                >
                  {schemeType === "ratio" ? "Ratio 10+1" : "Tiered Slabs"}
                </span>
                <span className="text-[10px] px-1.5 py-0.5 rounded font-mono bg-gray-100 dark:bg-slate-700 text-gray-600 dark:text-slate-300">
                  Ctrl+S Save
                </span>
                <span className="text-[10px] px-1.5 py-0.5 rounded font-mono bg-gray-100 dark:bg-slate-700 text-gray-600 dark:text-slate-300">
                  Esc Back
                </span>
              </div>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                Configure automated promotional trade offers, free bonus items, and tiered quantity slabs.
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2.5">
            <button
              type="button"
              onClick={() => navigate("/trade-schemes")}
              disabled={saving}
              className={`px-4 py-2 text-xs font-semibold rounded-xl border transition-all ${
                isDark
                  ? "border-slate-600 bg-slate-700/60 text-slate-300 hover:bg-slate-700"
                  : "border-gray-300 bg-white text-gray-700 hover:bg-gray-50"
              }`}
            >
              Cancel (Esc)
            </button>

            <button
              ref={submitBtnRef}
              type="button"
              onClick={handleSubmit}
              disabled={saving}
              className={`px-5 py-2 text-xs font-bold rounded-xl flex items-center gap-2 shadow-sm transition-all ${btnClasses.primary.className}`}
              style={btnClasses.primary.style}
            >
              <CheckIcon className="w-4 h-4" />
              <span>{saving ? "Saving..." : isEdit ? "Update Scheme (Ctrl+S)" : "Create Scheme (Ctrl+S)"}</span>
            </button>
          </div>
        </div>
      </div>

      <form onSubmit={handleSubmit} className="space-y-4">
        {/* Card 1: Scheme Configuration & Scope */}
        <div
          className={`p-5 rounded-2xl border transition-all ${
            isDark
              ? "bg-slate-800/80 border-slate-700/80 backdrop-blur-md shadow-md"
              : "bg-white border-gray-200/90 shadow-sm"
          }`}
        >
          <div className="flex items-center gap-2 pb-3 mb-4 border-b border-gray-100 dark:border-slate-700/80">
            <SparklesIcon className="w-4 h-4 text-amber-500" />
            <h2 className="text-sm font-bold text-gray-900 dark:text-white uppercase tracking-wider">
              1. Scheme Definition & Target Scope
            </h2>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {/* Scheme Name */}
            <div className="col-span-1 md:col-span-2 lg:col-span-2">
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Scheme Title / Name <span className="text-red-500">*</span>
              </label>
              <input
                ref={nameRef}
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                onKeyDown={(e) => handleKeyDown(e, schemeTypeSelectRef)}
                placeholder="e.g. Panadol Summer 10+1 Free, GSK Antibiotic Tier Scheme"
                className="w-full h-9.5 px-3 rounded-xl border text-xs font-medium bg-white dark:bg-slate-700/80 border-gray-300 dark:border-slate-600 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                autoFocus
              />
            </div>

            {/* Active Toggle Switch */}
            <div className="col-span-1 flex flex-col justify-center">
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Scheme Status
              </label>
              <button
                type="button"
                onClick={() => setIsActive(!isActive)}
                className={`h-9.5 px-3.5 rounded-xl border flex items-center justify-between text-xs font-semibold transition-all ${
                  isActive
                    ? "bg-emerald-500/10 border-emerald-500/30 text-emerald-700 dark:text-emerald-400"
                    : "bg-gray-100 dark:bg-slate-700/60 border-gray-300 dark:border-slate-600 text-gray-500 dark:text-slate-400"
                }`}
              >
                <span className="flex items-center gap-1.5">
                  <span
                    className={`w-2 h-2 rounded-full ${
                      isActive ? "bg-emerald-500 animate-pulse" : "bg-gray-400"
                    }`}
                  />
                  <span>{isActive ? "Active (Currently Applying)" : "Inactive (Paused)"}</span>
                </span>
                <span className="text-[11px] underline opacity-80">Toggle</span>
              </button>
            </div>

            {/* Scheme Type */}
            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Offer Logic / Formula <span className="text-red-500">*</span>
              </label>
              <Select
                ref={schemeTypeSelectRef}
                value={SCHEME_TYPE_OPTIONS.find((o) => o.value === schemeType)}
                onChange={(opt) => {
                  setSchemeType(opt.value);
                  if (opt.value === "ratio" && slabs.length > 1) {
                    setSlabs([slabs[0]]);
                  }
                }}
                onKeyDown={(e) => handleKeyDown(e, targetTypeSelectRef)}
                options={SCHEME_TYPE_OPTIONS}
                styles={selectStyles}
                isSearchable={false}
              />
            </div>

            {/* Target Type */}
            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Target Scope <span className="text-red-500">*</span>
              </label>
              <Select
                ref={targetTypeSelectRef}
                value={TARGET_TYPE_OPTIONS.find((o) => o.value === targetType)}
                onChange={(opt) => {
                  setTargetType(opt.value);
                  setTargetProduct(null);
                  setTargetBrand(null);
                  setTargetCategory(null);
                }}
                onKeyDown={(e) => handleKeyDown(e, bonusTypeSelectRef)}
                options={TARGET_TYPE_OPTIONS}
                styles={selectStyles}
                isSearchable={false}
              />
            </div>

            {/* Bonus Unit Type */}
            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Bonus Counted In <span className="text-red-500">*</span>
              </label>
              <Select
                ref={bonusTypeSelectRef}
                value={BONUS_TYPE_OPTIONS.find((o) => o.value === bonusType)}
                onChange={(opt) => setBonusType(opt.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault();
                    focusTargetSelector();
                  }
                }}
                options={BONUS_TYPE_OPTIONS}
                styles={selectStyles}
                isSearchable={false}
              />
            </div>

            {/* Dynamic Target Selector */}
            <div className="col-span-1 md:col-span-2 lg:col-span-3">
              {targetType === "product" && (
                <div className="space-y-1.5">
                  <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300">
                    Select Target Product <span className="text-red-500">*</span>
                  </label>
                  <div className="max-w-2xl">
                    <ProductSearchInput
                      ref={productSelectRef}
                      value={targetProduct}
                      onChange={(prod) => {
                        setTargetProduct(prod);
                        if (!name && prod?.name) {
                          setName(`${prod.name} 10+1`);
                        }
                        setTimeout(() => {
                          saleTypeSelectRef.current?.focus?.();
                        }, 40);
                      }}
                      onKeyDown={(e) => handleKeyDown(e, saleTypeSelectRef)}
                    />
                  </div>
                  {targetProduct && (
                    <div className="mt-2 inline-flex items-center gap-2 px-3 py-1.5 rounded-xl bg-blue-50 dark:bg-blue-950/40 border border-blue-200 dark:border-blue-800 text-xs text-blue-800 dark:text-blue-300">
                      <CubeIcon className="w-4 h-4 text-blue-500" />
                      <span className="font-semibold">{targetProduct.name}</span>
                      {targetProduct.product_code && (
                        <span className="opacity-70 font-mono">({targetProduct.product_code})</span>
                      )}
                      {targetProduct.pack_size && (
                        <span className="px-1.5 py-0.5 rounded bg-blue-200/50 dark:bg-blue-900/60 font-mono text-[11px]">
                          Pack Size: {targetProduct.pack_size}
                        </span>
                      )}
                      <button
                        type="button"
                        onClick={() => setTargetProduct(null)}
                        className="ml-1 text-blue-400 hover:text-red-500"
                        title="Clear product"
                      >
                        <XMarkIcon className="w-4 h-4" />
                      </button>
                    </div>
                  )}
                </div>
              )}

              {targetType === "brand" && (
                <div className="max-w-xl">
                  <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                    Select Brand / Manufacturer <span className="text-red-500">*</span>
                  </label>
                  <AsyncSelect
                    ref={brandSelectRef}
                    defaultOptions
                    loadOptions={loadBrands}
                    value={targetBrand}
                    onChange={(b) => setTargetBrand(b)}
                    onKeyDown={(e) => handleKeyDown(e, saleTypeSelectRef)}
                    placeholder="Search brand by name..."
                    styles={selectStyles}
                    isClearable
                  />
                  <p className="text-[11px] text-gray-500 dark:text-slate-400 mt-1">
                    This scheme will automatically apply to any sale of products belonging to this brand.
                  </p>
                </div>
              )}

              {targetType === "category" && (
                <div className="max-w-xl">
                  <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                    Select Category <span className="text-red-500">*</span>
                  </label>
                  <AsyncSelect
                    ref={categorySelectRef}
                    defaultOptions
                    loadOptions={loadCategories}
                    value={targetCategory}
                    onChange={(c) => setTargetCategory(c)}
                    onKeyDown={(e) => handleKeyDown(e, saleTypeSelectRef)}
                    placeholder="Search category by name..."
                    styles={selectStyles}
                    isClearable
                  />
                  <p className="text-[11px] text-gray-500 dark:text-slate-400 mt-1">
                    This scheme will automatically apply to any sale of products within this category.
                  </p>
                </div>
              )}
            </div>

            {/* Sale Channel */}
            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Sale Channel Scope
              </label>
              <Select
                ref={saleTypeSelectRef}
                value={SALE_TYPE_OPTIONS.find((o) => o.value === saleType)}
                onChange={(opt) => setSaleType(opt.value)}
                onKeyDown={(e) => handleKeyDown(e, customerSelectRef)}
                options={SALE_TYPE_OPTIONS}
                styles={selectStyles}
                isSearchable={false}
              />
            </div>

            {/* Specific Customer */}
            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Target Customer <span className="text-gray-400 font-normal">(Optional)</span>
              </label>
              <AsyncSelect
                ref={customerSelectRef}
                defaultOptions
                loadOptions={loadCustomers}
                value={customer}
                onChange={(c) => setCustomer(c)}
                onKeyDown={(e) => handleKeyDown(e, priorityRef)}
                placeholder="All Customers (or search specific)"
                styles={selectStyles}
                isClearable
              />
            </div>

            {/* Priority */}
            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Evaluation Priority <span className="text-gray-400 font-normal">(Higher = first)</span>
              </label>
              <input
                ref={priorityRef}
                type="number"
                value={priority}
                onChange={(e) => setPriority(parseInt(e.target.value, 10) || 0)}
                onKeyDown={(e) => handleKeyDown(e, startDateRef)}
                placeholder="0"
                className="w-full h-9.5 px-3 rounded-xl border text-xs font-medium bg-white dark:bg-slate-700/80 border-gray-300 dark:border-slate-600 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
              />
            </div>

            {/* Date Range */}
            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Valid From Date <span className="text-gray-400 font-normal">(Optional)</span>
              </label>
              <ModernDatePicker
                ref={startDateRef}
                editable
                value={startDate}
                onChange={(val) => setStartDate(val)}
                selectsStart
                startDate={startDate}
                endDate={endDate}
                maxDate={endDate || undefined}
                onKeyDown={(e) => handleKeyDown(e, endDateRef)}
                size="sm"
                ariaLabel="Valid from date"
                placeholder="Valid from date"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Valid Until Date <span className="text-gray-400 font-normal">(Optional)</span>
              </label>
              <ModernDatePicker
                ref={endDateRef}
                editable
                value={endDate}
                onChange={(val) => setEndDate(val)}
                selectsEnd
                startDate={startDate}
                endDate={endDate}
                minDate={startDate || undefined}
                onKeyDown={(e) => handleKeyDown(e, slabMinRefs.current[0])}
                size="sm"
                ariaLabel="Valid until date"
                placeholder="Valid until date"
              />
            </div>
          </div>
        </div>

        {/* Card 2: Bonus Slabs & Brackets Builder */}
        <div
          className={`p-5 rounded-2xl border transition-all ${
            isDark
              ? "bg-slate-800/80 border-slate-700/80 backdrop-blur-md shadow-md"
              : "bg-white border-gray-200/90 shadow-sm"
          }`}
        >
          <div className="flex items-center justify-between pb-3 mb-4 border-b border-gray-100 dark:border-slate-700/80">
            <div className="flex items-center gap-2">
              <GiftIcon className="w-4 h-4 text-emerald-500" />
              <h2 className="text-sm font-bold text-gray-900 dark:text-white uppercase tracking-wider">
                2. Bonus Slabs & Quantity Brackets
              </h2>
            </div>

            {schemeType === "slab" && (
              <button
                type="button"
                onClick={handleAddSlab}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-bold text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-500/30 hover:bg-emerald-100 dark:hover:bg-emerald-900/50 transition-all"
                title="Add Tier (Ctrl+N)"
              >
                <PlusIcon className="w-3.5 h-3.5" />
                <span>Add Tier (Ctrl+N)</span>
              </button>
            )}
          </div>

          {schemeType === "ratio" ? (
            /* Ratio 10+1 UI Builder */
            <div className="p-4 rounded-xl bg-slate-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-700">
              <div className="flex flex-wrap items-center gap-3 text-xs sm:text-sm font-semibold text-gray-800 dark:text-slate-200">
                <span>For every</span>
                <input
                  ref={(el) => (slabMinRefs.current[0] = el)}
                  type="number"
                  min="1"
                  step="1"
                  value={slabs[0]?.min_qty ?? 10}
                  onChange={(e) => handleSlabChange(0, "min_qty", e.target.value)}
                  onKeyDown={(e) => handleKeyDown(e, slabBonusRefs.current[0])}
                  className="w-20 h-9 px-2 text-center font-bold text-base rounded-lg border border-blue-400 dark:border-blue-500 bg-white dark:bg-slate-800 text-blue-600 dark:text-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
                <span className="uppercase text-xs font-bold text-blue-600 dark:text-blue-400">
                  {bonusType}s
                </span>
                <span>purchased, give</span>
                <input
                  ref={(el) => (slabBonusRefs.current[0] = el)}
                  type="number"
                  min="1"
                  step="1"
                  value={slabs[0]?.bonus_qty ?? 1}
                  onChange={(e) => handleSlabChange(0, "bonus_qty", e.target.value)}
                  onKeyDown={(e) => handleKeyDown(e, notesRef)}
                  className="w-20 h-9 px-2 text-center font-bold text-base rounded-lg border border-emerald-400 dark:border-emerald-500 bg-white dark:bg-slate-800 text-emerald-600 dark:text-emerald-400 focus:outline-none focus:ring-2 focus:ring-emerald-500"
                />
                <span className="uppercase text-xs font-bold text-emerald-600 dark:text-emerald-400">
                  {bonusType}s FREE
                </span>
              </div>

              {/* Optional Different Bonus Product for ratio */}
              <div className="mt-4 pt-3 border-t border-gray-200 dark:border-slate-800 max-w-xl">
                <label className="block text-xs font-medium text-gray-600 dark:text-slate-400 mb-1">
                  Give a different product as free bonus? <span className="font-normal">(Default is same product)</span>
                </label>
                <ProductSearchInput
                  value={slabs[0]?.bonus_product}
                  onChange={(prod) => handleSlabChange(0, "bonus_product", prod)}
                />
                {slabs[0]?.bonus_product && (
                  <div className="mt-1.5 inline-flex items-center gap-2 px-2.5 py-1 rounded-lg bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-500/30 text-xs text-emerald-700 dark:text-emerald-300">
                    <span>🎁 Free Item: <strong>{slabs[0].bonus_product.name}</strong></span>
                    <button
                      type="button"
                      onClick={() => handleSlabChange(0, "bonus_product", null)}
                      className="text-gray-400 hover:text-red-500"
                    >
                      <XMarkIcon className="w-3.5 h-3.5" />
                    </button>
                  </div>
                )}
              </div>
            </div>
          ) : (
            /* Tiered Quantity Slabs Table */
            <div className="overflow-x-auto rounded-xl border border-gray-200 dark:border-slate-700">
              <table className="w-full text-left border-collapse text-xs">
                <thead>
                  <tr className="border-b border-gray-200 dark:border-slate-700 bg-gray-50/80 dark:bg-slate-900/60 text-gray-500 dark:text-slate-400">
                    <th className="py-2.5 px-3 font-semibold w-12 text-center">Tier</th>
                    <th className="py-2.5 px-3 font-semibold min-w-[120px]">
                      Min Quantity ({bonusType}s) <span className="text-red-500">*</span>
                    </th>
                    <th className="py-2.5 px-3 font-semibold min-w-[120px]">
                      Max Quantity ({bonusType}s) <span className="text-gray-400 font-normal">(Empty = ∞)</span>
                    </th>
                    <th className="py-2.5 px-3 font-semibold min-w-[120px]">
                      Free Bonus ({bonusType}s) <span className="text-red-500">*</span>
                    </th>
                    <th className="py-2.5 px-3 font-semibold min-w-[200px]">
                      Free Bonus Product <span className="text-gray-400 font-normal">(Default = Same)</span>
                    </th>
                    <th className="py-2.5 px-3 font-semibold w-14 text-center">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 dark:divide-slate-700/60">
                  {slabs.map((slab, i) => (
                    <tr
                      key={i}
                      className="hover:bg-gray-50/60 dark:hover:bg-slate-700/30 transition-colors"
                    >
                      <td className="py-2.5 px-3 text-center font-bold text-gray-400 dark:text-slate-500">
                        #{i + 1}
                      </td>

                      <td className="py-2.5 px-3">
                        <input
                          ref={(el) => (slabMinRefs.current[i] = el)}
                          type="number"
                          min="0.01"
                          step="any"
                          value={slab.min_qty}
                          onChange={(e) => handleSlabChange(i, "min_qty", e.target.value)}
                          onKeyDown={(e) => handleKeyDown(e, slabMaxRefs.current[i])}
                          placeholder="e.g. 10"
                          className="w-full h-8 px-2.5 rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-gray-900 dark:text-white font-mono text-xs focus:outline-none focus:ring-1 focus:ring-blue-500"
                        />
                      </td>

                      <td className="py-2.5 px-3">
                        <input
                          ref={(el) => (slabMaxRefs.current[i] = el)}
                          type="number"
                          min="0.01"
                          step="any"
                          value={slab.max_qty}
                          onChange={(e) => handleSlabChange(i, "max_qty", e.target.value)}
                          onKeyDown={(e) => handleKeyDown(e, slabBonusRefs.current[i])}
                          placeholder="Unlimited / ∞"
                          className="w-full h-8 px-2.5 rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-gray-900 dark:text-white font-mono text-xs focus:outline-none focus:ring-1 focus:ring-blue-500"
                        />
                      </td>

                      <td className="py-2.5 px-3">
                        <input
                          ref={(el) => (slabBonusRefs.current[i] = el)}
                          type="number"
                          min="0.01"
                          step="any"
                          value={slab.bonus_qty}
                          onChange={(e) => handleSlabChange(i, "bonus_qty", e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === "Enter") {
                              e.preventDefault();
                              if (i < slabs.length - 1) {
                                slabMinRefs.current[i + 1]?.focus();
                              } else {
                                handleAddSlab();
                              }
                            }
                          }}
                          placeholder="e.g. 1"
                          className="w-full h-8 px-2.5 rounded-lg border border-emerald-400 dark:border-emerald-500/60 bg-emerald-50/50 dark:bg-emerald-950/20 text-emerald-700 dark:text-emerald-400 font-mono font-bold text-xs focus:outline-none focus:ring-1 focus:ring-emerald-500"
                        />
                      </td>

                      <td className="py-2.5 px-3">
                        <div className="min-w-[180px]">
                          <ProductSearchInput
                            value={slab.bonus_product}
                            onChange={(prod) => handleSlabChange(i, "bonus_product", prod)}
                          />
                        </div>
                      </td>

                      <td className="py-2.5 px-3 text-center">
                        <button
                          type="button"
                          onClick={() => handleRemoveSlab(i)}
                          disabled={slabs.length <= 1}
                          className="p-1 rounded-md text-red-500 hover:bg-red-50 dark:hover:bg-red-950/40 disabled:opacity-30 disabled:pointer-events-none transition-colors"
                          title="Remove tier"
                        >
                          <TrashIcon className="w-4 h-4" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Interactive Live Calculation & Profit/Margin Impact Preview */}
          <div className="mt-4 p-3.5 rounded-xl bg-gradient-to-r from-blue-500/5 to-emerald-500/5 border border-blue-500/20 dark:border-blue-500/30 space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-gray-700 dark:text-slate-300">
                  Live Calculation Simulator:
                </span>
                <div className="flex items-center gap-1.5">
                  <span className="text-xs text-gray-500 dark:text-slate-400">If customer buys</span>
                  <input
                    type="number"
                    min="1"
                    value={testQty}
                    onChange={(e) => setTestQty(e.target.value)}
                    className="w-16 h-7 text-center rounded border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-xs font-bold"
                  />
                  <span className="text-xs text-gray-500 dark:text-slate-400">{bonusType}s</span>
                </div>
              </div>

              <div className="text-xs font-semibold text-emerald-700 dark:text-emerald-400">
                {livePreview.text}
              </div>
            </div>

            {livePreview.profitMetrics && (
              <div className="pt-2.5 border-t border-gray-200/70 dark:border-slate-700/70 space-y-2">
                <div className="grid grid-cols-2 sm:grid-cols-5 gap-2 text-xs">
                  <div className="p-2 rounded-lg bg-white/80 dark:bg-slate-800/80 border border-gray-200/80 dark:border-slate-700">
                    <div className="text-[10px] font-semibold uppercase tracking-wider text-gray-400 dark:text-slate-400">
                      Base Margin (No Bonus)
                    </div>
                    <div className="font-mono font-bold text-sm text-gray-800 dark:text-slate-100 mt-0.5">
                      {livePreview.profitMetrics.baseMarginPct}%
                    </div>
                    <div className="text-[10px] text-gray-500 dark:text-slate-400">
                      Cost {livePreview.profitMetrics.unitCost} → Sale {livePreview.profitMetrics.unitSalePrice}/u
                    </div>
                  </div>

                  <div className="p-2 rounded-lg bg-white/80 dark:bg-slate-800/80 border border-gray-200/80 dark:border-slate-700">
                    <div className="text-[10px] font-semibold uppercase tracking-wider text-gray-400 dark:text-slate-400">
                      Net Revenue ({livePreview.profitMetrics.paidUnits} Paid u)
                    </div>
                    <div className="font-mono font-bold text-sm text-blue-600 dark:text-blue-400 mt-0.5">
                      Rs. {livePreview.profitMetrics.revenue.toLocaleString()}
                    </div>
                    <div className="text-[10px] text-gray-500 dark:text-slate-400">
                      Eff. Rate: Rs. {livePreview.profitMetrics.effectiveUnitPrice}/u
                    </div>
                  </div>

                  <div className="p-2 rounded-lg bg-white/80 dark:bg-slate-800/80 border border-gray-200/80 dark:border-slate-700">
                    <div className="text-[10px] font-semibold uppercase tracking-wider text-gray-400 dark:text-slate-400">
                      Total COGS ({livePreview.profitMetrics.totalUnitsOut} Total u)
                    </div>
                    <div className="font-mono font-bold text-sm text-gray-800 dark:text-slate-200 mt-0.5">
                      Rs. {livePreview.profitMetrics.totalCost.toLocaleString()}
                    </div>
                    <div className="text-[10px] text-gray-500 dark:text-slate-400">
                      {livePreview.profitMetrics.paidUnits} paid + {livePreview.profitMetrics.bonusUnits} free u
                    </div>
                  </div>

                  <div
                    className={`p-2 rounded-lg border ${
                      livePreview.profitMetrics.netProfit < 0
                        ? "bg-red-50/90 dark:bg-red-950/40 border-red-300 dark:border-red-800"
                        : "bg-emerald-50/90 dark:bg-emerald-950/40 border-emerald-300 dark:border-emerald-800"
                    }`}
                  >
                    <div className="text-[10px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                      Net Profit / Loss
                    </div>
                    <div
                      className={`font-mono font-extrabold text-sm mt-0.5 ${
                        livePreview.profitMetrics.netProfit < 0
                          ? "text-red-600 dark:text-red-400"
                          : "text-emerald-700 dark:text-emerald-300"
                      }`}
                    >
                      {livePreview.profitMetrics.netProfit >= 0 ? "+" : ""}Rs.{" "}
                      {livePreview.profitMetrics.netProfit.toLocaleString()}
                    </div>
                    <div className="text-[10px] text-gray-500 dark:text-slate-400">
                      Revenue − Total COGS
                    </div>
                  </div>

                  <div
                    className={`p-2 rounded-lg border ${
                      livePreview.profitMetrics.realizedMarginPct < 0
                        ? "bg-red-50/90 dark:bg-red-950/40 border-red-400 dark:border-red-700"
                        : "bg-emerald-50/90 dark:bg-emerald-950/40 border-emerald-400 dark:border-emerald-700"
                    }`}
                  >
                    <div className="text-[10px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                      Realized Margin %
                    </div>
                    <div
                      className={`font-mono font-black text-base mt-0.5 ${
                        livePreview.profitMetrics.realizedMarginPct < 0
                          ? "text-red-600 dark:text-red-400"
                          : "text-emerald-700 dark:text-emerald-300"
                      }`}
                    >
                      {livePreview.profitMetrics.realizedMarginPct}%
                    </div>
                    <div className="text-[10px] text-gray-500 dark:text-slate-400">
                      After scheme bonus
                    </div>
                  </div>
                </div>

                {livePreview.profitMetrics.realizedMarginPct < 0 && (
                  <div className="px-3 py-2 rounded-lg bg-red-100/80 dark:bg-red-950/60 border border-red-300 dark:border-red-800 text-xs text-red-800 dark:text-red-200 flex items-center justify-between gap-2">
                    <span>
                      <strong>Margin Warning:</strong> At a {livePreview.profitMetrics.baseMarginPct}% base margin, giving{" "}
                      {livePreview.profitMetrics.bonusUnits} free unit(s) on {livePreview.profitMetrics.paidUnits} paid unit(s) results in a{" "}
                      <strong>{livePreview.profitMetrics.realizedMarginPct}% loss</strong> (Rs. {livePreview.profitMetrics.netProfit}) unless covered by a supplier purchase bonus.
                      {livePreview.profitMetrics.minPaidForOneFree
                        ? ` Break-even requires at least ${livePreview.profitMetrics.minPaidForOneFree}+1 at current cost.`
                        : ""}
                    </span>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Card 3: Notes & Remarks */}
        <div
          className={`p-5 rounded-2xl border transition-all ${
            isDark
              ? "bg-slate-800/80 border-slate-700/80 backdrop-blur-md shadow-md"
              : "bg-white border-gray-200/90 shadow-sm"
          }`}
        >
          <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
            Internal Notes / Instructions for Sales Counter
          </label>
          <textarea
            ref={notesRef}
            rows={2}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                submitBtnRef.current?.focus();
              }
            }}
            placeholder="e.g. Valid during June promotion cycle only. Cash payment or clear invoices only."
            className="w-full px-3 py-2 rounded-xl border text-xs bg-white dark:bg-slate-700/80 border-gray-300 dark:border-slate-600 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
          />
        </div>

        {/* Bottom Actions Bar */}
        <div className="flex items-center justify-between pt-2">
          <button
            type="button"
            onClick={() => navigate("/trade-schemes")}
            disabled={saving}
            className={`px-4 py-2 text-xs font-semibold rounded-xl border transition-all ${
              isDark
                ? "border-slate-600 bg-slate-700/60 text-slate-300 hover:bg-slate-700"
                : "border-gray-300 bg-white text-gray-700 hover:bg-gray-50"
            }`}
          >
            Cancel & Return (Esc)
          </button>

          <button
            type="submit"
            disabled={saving}
            className={`px-6 py-2.5 text-xs font-bold rounded-xl flex items-center gap-2 shadow-md transition-all ${btnClasses.primary.className}`}
            style={btnClasses.primary.style}
          >
            <CheckIcon className="w-4 h-4" />
            <span>{saving ? "Saving..." : isEdit ? "Update Scheme (Ctrl+S)" : "Create Scheme (Ctrl+S)"}</span>
          </button>
        </div>
      </form>
    </div>
  );
}
