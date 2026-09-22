import React, { useEffect, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import { ArrowLeftIcon, PlusCircleIcon } from "@heroicons/react/24/solid";
import ProductForm from "./ProductForm";
import { usePermissions } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext";

export default function CreateProduct() {
  const navigate = useNavigate();
  const { theme } = useTheme();
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () => (typeof canFor === "function" ? canFor("product") : {
      view:false, create:false, update:false, delete:false, import:false, export:false
    }),
    [canFor]
  );

  const themeColors = useMemo(
    () => ({
      primary: theme?.primary_color || "#3b82f6",
      secondary: theme?.secondary_color || "#8b5cf6",
    }),
    [theme]
  );

  useEffect(() => { document.title = "Add Product - Pharmacy ERP"; }, []);

  const handleCreate = async (formData) => {
    if (!can.create) {
      toast.error("You don't have permission to create products.");
      return;
    }
    try {
      await axios.post("/api/products", formData, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      toast.success("Product created");
      navigate("/products");
    } catch (e) {
      const msg = e?.response?.data?.message || "Create failed";
      toast.error(msg);
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.create) return <div className="p-6 text-sm text-gray-700">You don't have permission to add products.</div>;

  return (
    <div className="p-3 md:p-4 space-y-4">
      {/* ===== Hero band (matches Dashboard / Products index) ===== */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-slate-900 via-slate-900 to-slate-800 ring-1 ring-white/10 shadow-lg shadow-slate-900/20">
        {/* Glow accents */}
        <div aria-hidden="true" className="pointer-events-none absolute -top-24 -right-16 h-72 w-72 rounded-full blur-3xl opacity-25" style={{ background: themeColors.primary }} />
        <div aria-hidden="true" className="pointer-events-none absolute -bottom-28 -left-12 h-72 w-72 rounded-full blur-3xl opacity-20" style={{ background: themeColors.secondary }} />
        {/* Dot texture */}
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 opacity-[0.12]"
          style={{ backgroundImage: "radial-gradient(rgba(255,255,255,0.8) 1px, transparent 1px)", backgroundSize: "18px 18px" }}
        />

        <div className="relative flex flex-col gap-4 p-4 sm:p-5 lg:flex-row lg:items-center lg:justify-between lg:gap-6">
          {/* Identity */}
          <div className="flex items-center gap-3 min-w-0">
            <div className="w-10 h-10 rounded-xl bg-white/10 ring-1 ring-white/15 flex items-center justify-center shrink-0">
              <PlusCircleIcon className="text-white w-5 h-5" />
            </div>
            <div className="min-w-0">
              <h1 className="text-base sm:text-lg font-semibold tracking-tight text-white">Add Product</h1>
              <p className="mt-0.5 text-[11px] sm:text-xs text-slate-400">
                <span className="truncate">Create a new product for your inventory</span>
              </p>
            </div>
          </div>

          {/* Actions */}
          <div className="flex flex-wrap items-center gap-2">
            <Link
              to="/products"
              title="Back to products (Alt+C)"
              className="inline-flex items-center gap-1.5 rounded-lg bg-white/10 px-3 py-1.5 text-xs font-semibold text-white ring-1 ring-white/15 backdrop-blur-sm transition-all duration-200 hover:bg-white/15 active:scale-[0.98]"
            >
              <ArrowLeftIcon className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Back to Products</span>
              <span className="sm:hidden">Back</span>
            </Link>

            <button
              id="save-product-btn-top"
              type="submit"
              form="product-form"
              title="Save (Alt+S)"
              className="inline-flex items-center gap-1.5 rounded-lg bg-white/10 px-3 py-1.5 text-sm font-semibold text-white ring-1 ring-white/15 backdrop-blur-sm transition-all duration-200 hover:bg-white/15 active:scale-[0.98]"
            >
              <PlusCircleIcon className="w-4 h-4" />
              <span className="hidden sm:inline">Save Product</span>
              <span className="sm:hidden">Save</span>
            </button>

            <kbd className="hidden xl:flex items-center rounded-md bg-white/5 ring-1 ring-white/10 px-1.5 py-1 text-[10px] font-medium text-slate-400 whitespace-nowrap">
              Alt+S
            </kbd>
          </div>
        </div>
      </div>

      {/* Form */}
      <ProductForm onSubmit={handleCreate} />
    </div>
  );
}
