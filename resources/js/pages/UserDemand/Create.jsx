import React, { useEffect, useMemo, useRef, useState } from "react";
import axios from "axios";
import { useNavigate } from "react-router-dom";
import toast from "react-hot-toast";
import {
  HandRaisedIcon,
  ArrowPathIcon,
  MagnifyingGlassIcon,
  BuildingStorefrontIcon,
  TagIcon,
  Squares2X2Icon,
  CubeIcon,
  UserIcon,
} from "@heroicons/react/24/solid";
import { usePermissions } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";
import { GlassCard } from "@/components";
import BrandSearch from "@/components/BrandSearch.jsx";
import SupplierSearch from "@/components/SupplierSearch.jsx";
import CategorySearch from "@/components/CategorySearch.jsx";
import CustomerSearch from "@/components/CustomerSearch.jsx";

const asList = (payload) =>
  Array.isArray(payload) ? payload : (payload?.data ?? payload?.items ?? []);

export default function CreateUserDemand() {
  const navigate = useNavigate();
  const { has } = usePermissions();
  const { theme } = useTheme();

  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || '#3b82f6',
    primaryHover: theme?.primary_hover || '#2563eb',
    primaryLight: theme?.primary_light || '#dbeafe',
    secondary: theme?.secondary_color || '#8b5cf6',
    secondaryHover: theme?.secondary_hover || '#7c3aed',
    success: theme?.success_color || '#10b981',
  }), [theme]);

  const canCreate = has("user-demands.create");

const [name, setName] = useState("");
  const [requestedQuantity, setRequestedQuantity] = useState(1);
  const [packSize, setPackSize] = useState(1);
  const [notes, setNotes] = useState("");

  // Product selection (optional — if an existing product is picked, use it)
  const [selectedProduct, setSelectedProduct] = useState(null);
  const [productResults, setProductResults] = useState([]);
  const [searching, setSearching] = useState(false);
  const productQueryRef = useRef("");
  const abortRef = useRef(null);
  const debounceRef = useRef(null);

  // Brand / supplier / category
  const [brand, setBrand] = useState(null);
  const [brandSearchOpen, setBrandSearchOpen] = useState(false);
  const [supplier, setSupplier] = useState(null);
  const [supplierSearchOpen, setSupplierSearchOpen] = useState(false);
const [category, setCategory] = useState(null);
  const [categorySearchOpen, setCategorySearchOpen] = useState(false);

  // Customer (optional — who requested the demand)
  const [customer, setCustomer] = useState(null);
  const [customerSearchOpen, setCustomerSearchOpen] = useState(false);

const [saving, setSaving] = useState(false);

  // Refs for Enter-to-next-field navigation
  const nameRef = useRef(null);
  const brandRef = useRef(null);
  const categoryRef = useRef(null);
  const supplierRef = useRef(null);
  const qtyRef = useRef(null);
  const packSizeRef = useRef(null);
  const notesRef = useRef(null);
  const customerRef = useRef(null);
  const submitRef = useRef(null);

  // Keyboard shortcuts: Alt+S save, Alt+C cancel
  useEffect(() => {
    const onKey = (e) => {
      if (e.altKey && e.key.toLowerCase() === "s") {
        e.preventDefault();
        submitRef.current?.click();
      }
      if (e.altKey && e.key.toLowerCase() === "c") {
        e.preventDefault();
        navigate("/user-demands");
      }
    };
window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [navigate]);

  // Focus product name on mount
  useEffect(() => {
    const timer = setTimeout(() => nameRef.current?.focus(), 150);
    return () => clearTimeout(timer);
  }, []);

  if (!canCreate) return <div className="p-6 text-sm text-gray-700">You don't have permission to create user demands.</div>;

  const searchProducts = async (q) => {
    if (abortRef.current) abortRef.current.abort();
    abortRef.current = new AbortController();
    const signal = abortRef.current.signal;
    productQueryRef.current = q;
    setSearching(true);
    try {
      const { data } = await axios.get("/api/products/search", {
        params: { q, limit: 20 },
        signal,
      });
      setProductResults(Array.isArray(data) ? data : asList(data));
    } catch (err) {
      if (!axios.isCancel(err)) setProductResults([]);
    } finally {
      setSearching(false);
    }
  };

  const handleNameChange = (val) => {
    setName(val);
    setSelectedProduct(null);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    const q = (val || "").trim();
    if (!q) {
      setProductResults([]);
      return;
    }
    debounceRef.current = setTimeout(() => searchProducts(q), 300);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!selectedProduct && !name.trim()) {
      toast.error("Please enter a product name or select an existing product.");
      return;
    }
if (!selectedProduct && !brand) {
      toast.error("Please select a brand for the new product.");
      return;
    }
    if (!selectedProduct && !category) {
      toast.error("Please select a category for the new product.");
      return;
    }

    setSaving(true);
    try {
const payload = {
        requested_quantity: requestedQuantity || 1,
        notes: notes || null,
      };
      if (customer) payload.customer_id = customer.id;
if (selectedProduct) {
        payload.product_id = selectedProduct.id;
      } else {
        payload.name = name.trim();
        payload.pack_size = packSize || 1;
        payload.brand_id = brand.value;
        payload.category_id = category.value;
        if (supplier) payload.supplier_id = supplier.value;
      }
      await axios.post("/api/user-demands", payload);
      toast.success("User demand created! It will appear in purchase order forecast.");
      navigate("/user-demands");
    } catch (err) {
      const d = err?.response?.data;
      const msg = d?.message || (d?.errors ? Object.values(d.errors).flat().join(", ") : "Create failed.");
      toast.error(msg);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="p-4 space-y-4">
      {/* Hero */}
      <div
        className="relative overflow-hidden rounded-2xl shadow-lg"
        style={{ background: `linear-gradient(135deg, ${themeColors.secondary}, ${themeColors.primary}, ${themeColors.primaryHover})` }}
      >
        <div className="absolute -top-10 -right-8 w-64 h-64 rounded-full bg-white/15 blur-3xl pointer-events-none" />
        <div className="relative px-6 py-5 flex items-center gap-4">
          <div className="w-12 h-12 rounded-2xl bg-white/20 backdrop-blur-sm shadow-inner flex items-center justify-center">
            <HandRaisedIcon className="w-6 h-6 text-white" />
          </div>
          <div>
            <h1 className="text-2xl font-extrabold tracking-wide text-white leading-none">Request Product</h1>
            <p className="text-xs text-white/85 mt-1.5">
              Create a user demand for a product we don't have yet
            </p>
          </div>
        </div>
      </div>

      <GlassCard className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700 shadow-xs overflow-hidden">
        <form onSubmit={handleSubmit} className="p-6 space-y-5">
          {/* Product name / existing product picker */}
          <div>
            <label className="block text-sm font-medium mb-1.5 dark:text-slate-200">Product Name</label>
            <div className="relative">
              <div className="flex items-center gap-2 px-3 border border-gray-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-700 focus-within:ring-2 focus-within:ring-blue-400">
                <MagnifyingGlassIcon className="w-4 h-4 text-gray-400" />
<input
                  ref={nameRef}
                  type="text"
                  value={name}
                  onChange={(e) => handleNameChange(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      if (!selectedProduct) brandRef.current?.click();
                    }
                  }}
                  placeholder="Type a product name..."
                  className="flex-1 h-10 bg-transparent outline-hidden text-sm dark:text-slate-100"
                />
                {searching && <ArrowPathIcon className="w-4 h-4 animate-spin text-gray-400" />}
              </div>

              {selectedProduct && (
                <div className="mt-2 flex items-center gap-2 px-3 py-2 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300 text-sm">
                  <CubeIcon className="w-4 h-4" />
                  <span className="flex-1">
                    Using existing product: <b>{selectedProduct.name}</b> ({selectedProduct.product_code})
                  </span>
                  <button
                    type="button"
                    onClick={() => setSelectedProduct(null)}
                    className="text-xs font-semibold underline"
                  >
                    Change
                  </button>
                </div>
              )}

              {!selectedProduct && productResults.length > 0 && (
                <ul className="mt-2 border border-gray-200 dark:border-slate-600 rounded-lg max-h-48 overflow-auto divide-y divide-gray-100 dark:divide-slate-600">
                  {productResults.map((p) => (
                    <li key={p.id}>
                      <button
                        type="button"
onClick={() => {
                          setSelectedProduct(p);
                          setName(p.name);
                          setProductResults([]);
                          setBrand(null);
                          setSupplier(null);
                          setCategory(null);
                        }}
                        className="w-full text-left px-3 py-2 hover:bg-blue-50 dark:hover:bg-slate-600 transition-colors"
                      >
                        <div className="text-sm font-medium text-gray-800 dark:text-gray-100">{p.name}</div>
                        <div className="text-xs text-gray-500 dark:text-gray-400">
                          {p.product_code} · {p.brand?.name || "No brand"} · Qty {p.quantity ?? 0}
                        </div>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <p className="text-xs text-gray-400 mt-1.5">
              {selectedProduct
                ? "This product already exists — the demand will be linked to it."
                : "If no matching product is found, a new product will be created automatically with the selected brand & category."}
            </p>
          </div>

          {!selectedProduct && (
            <>
              {/* Brand + Category + Supplier */}
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
<div>
                  <label className="block text-sm font-medium mb-1.5 dark:text-slate-200">Brand *</label>
                  <button
                    ref={brandRef}
                    type="button"
                    onClick={() => setBrandSearchOpen(true)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        e.preventDefault();
                        setBrandSearchOpen(true);
                      }
                    }}
                    className="w-full h-10 flex items-center gap-2 px-3 border border-gray-200 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-700 text-left"
                  >
                    <TagIcon className="w-4 h-4 text-gray-400" />
                    <span className={brand ? "text-gray-800 dark:text-slate-100" : "text-gray-400"}>
                      {brand ? brand.label : "Search brand..."}
                    </span>
                  </button>
                </div>

<div>
                  <label className="block text-sm font-medium mb-1.5 dark:text-slate-200">Category *</label>
                  <button
                    ref={categoryRef}
                    type="button"
                    onClick={() => setCategorySearchOpen(true)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        e.preventDefault();
                        setCategorySearchOpen(true);
                      }
                    }}
                    className="w-full h-10 flex items-center gap-2 px-3 border border-gray-200 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-700 text-left"
                  >
                    <Squares2X2Icon className="w-4 h-4 text-gray-400" />
                    <span className={category ? "text-gray-800 dark:text-slate-100" : "text-gray-400"}>
                      {category ? category.label : "Search category..."}
                    </span>
                  </button>
                </div>

                <div>
                  <label className="block text-sm font-medium mb-1.5 dark:text-slate-200">Supplier (optional)</label>
                  <button
                    ref={supplierRef}
                    type="button"
                    onClick={() => setSupplierSearchOpen(true)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        e.preventDefault();
                        setSupplierSearchOpen(true);
                      }
                    }}
                    className="w-full h-10 flex items-center gap-2 px-3 border border-gray-200 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-700 text-left"
                  >
                    <BuildingStorefrontIcon className="w-4 h-4 text-gray-400" />
                    <span className={supplier ? "text-gray-800 dark:text-slate-100" : "text-gray-400"}>
                      {supplier ? supplier.label : "Search supplier..."}
                    </span>
                  </button>
                </div>
              </div>
            </>
          )}

{/* Quantity + Notes */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
<div>
              <label className="block text-sm font-medium mb-1.5 dark:text-slate-200">Requested Quantity</label>
              <input
                ref={qtyRef}
                type="number"
                min={1}
                value={requestedQuantity}
                onChange={(e) => setRequestedQuantity(parseInt(e.target.value || 0, 10))}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault();
                    if (!selectedProduct) packSizeRef.current?.focus();
                    else notesRef.current?.focus();
                  }
                }}
                className="w-full h-10 px-3 border border-gray-200 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-700 dark:text-slate-100"
              />
            </div>
            {!selectedProduct && (
              <div>
                <label className="block text-sm font-medium mb-1.5 dark:text-slate-200">Pack Size</label>
                <input
                  ref={packSizeRef}
                  type="number"
                  min={1}
                  value={packSize}
                  onChange={(e) => setPackSize(parseInt(e.target.value || 1, 10))}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      notesRef.current?.focus();
                    }
                  }}
                  className="w-full h-10 px-3 border border-gray-200 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-700 dark:text-slate-100"
                />
              </div>
            )}
            <div>
              <label className="block text-sm font-medium mb-1.5 dark:text-slate-200">Notes</label>
              <input
                ref={notesRef}
                type="text"
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault();
                    customerRef.current?.click();
                  }
                }}
                placeholder="Optional notes..."
                className="w-full h-10 px-3 border border-gray-200 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-700 dark:text-slate-100"
              />
            </div>
          </div>

{/* Customer (optional) */}
          <div>
<label className="block text-sm font-medium mb-1.5 dark:text-slate-200">Customer (optional)</label>
            <div
              ref={customerRef}
              role="button"
              tabIndex={0}
              onClick={() => setCustomerSearchOpen(true)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  setCustomerSearchOpen(true);
                }
              }}
              className="w-full h-10 flex items-center gap-2 px-3 border border-gray-200 dark:border-slate-600 rounded-lg text-sm bg-white dark:bg-slate-700 text-left cursor-pointer"
            >
              <UserIcon className="w-4 h-4 text-gray-400" />
              <span className={customer ? "text-gray-800 dark:text-slate-100" : "text-gray-400"}>
                {customer ? customer.name : "Search customer (who requested this product)..."}
              </span>
              {customer && (
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    setCustomer(null);
                  }}
                  className="ml-auto text-xs font-semibold underline text-red-500"
                >
                  Clear
                </button>
              )}
            </div>
          </div>

          {/* Actions */}
          <div className="flex items-center justify-end gap-2 pt-2">
<button
              type="button"
              onClick={() => navigate("/user-demands")}
              title="Cancel (Alt+C)"
              className="px-4 h-10 rounded-lg border border-gray-200 dark:border-slate-600 text-sm text-gray-700 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
            >
              Cancel
            </button>
<button
              ref={submitRef}
              type="submit"
              disabled={saving}
              title="Save (Alt+S)"
              className="px-5 h-10 rounded-lg text-sm font-bold text-white transition-all duration-200 inline-flex items-center gap-2"
              style={{
                background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                opacity: saving ? 0.6 : 1,
                cursor: saving ? "not-allowed" : "pointer",
              }}
            >
              {saving ? <ArrowPathIcon className="w-4 h-4 animate-spin" /> : <HandRaisedIcon className="w-4 h-4" />}
              {saving ? "Creating…" : "Create Demand"}
            </button>
          </div>
        </form>
      </GlassCard>

<BrandSearch
        isOpen={brandSearchOpen}
        onClose={() => setBrandSearchOpen(false)}
        onSelect={(b) => {
          setBrand(b ? { value: b.id, label: b.name } : null);
          // After selecting brand, move focus to category
          setTimeout(() => categoryRef.current?.click(), 50);
        }}
      />
<SupplierSearch
        isOpen={supplierSearchOpen}
        onClose={() => setSupplierSearchOpen(false)}
        onSelect={(s) => {
          setSupplier(s ? { value: s.id, label: s.name } : null);
          // After selecting supplier, move focus to requested quantity
          setTimeout(() => qtyRef.current?.focus(), 50);
        }}
      />
<CategorySearch
        isOpen={categorySearchOpen}
        onClose={() => setCategorySearchOpen(false)}
        onSelect={(c) => {
          setCategory(c ? { value: c.id, label: c.name } : null);
          // After selecting category, move focus to supplier
          setTimeout(() => supplierRef.current?.click(), 50);
        }}
      />
      <CustomerSearch
        isOpen={customerSearchOpen}
        onClose={() => setCustomerSearchOpen(false)}
        onSelect={(cust) => {
          setCustomer(cust ? cust : null);
        }}
        onCreate={(cust) => {
          setCustomer(cust ? cust : null);
          // After creating a customer, move focus to requested quantity
          setTimeout(() => qtyRef.current?.focus(), 50);
        }}
      />
    </div>
  );
}
