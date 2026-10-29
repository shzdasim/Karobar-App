import React, { useEffect, useState } from "react";
import axios from "axios";
import {
  XMarkIcon,
  ArrowsRightLeftIcon,
  PlusIcon,
  CubeIcon,
  SparklesIcon,
  MapPinIcon,
} from "@heroicons/react/24/solid";
import { useTheme } from "@/context/ThemeContext";
import { useCurrency } from "@/context/CurrencyContext";

export default function SubstituteProductsModal({
  open,
  product,
  onClose,
  onSelectSubstitute,
  allowSwap = true,
}) {
  const { isDark } = useTheme();
  const { formatCurrency } = useCurrency();
  const [loading, setLoading] = useState(false);
  const [data, setData] = useState(null);
  const [searchFilter, setSearchFilter] = useState("");
  const [inStockOnly, setInStockOnly] = useState(true);

  useEffect(() => {
    if (!open || !product?.id) return;
    let active = true;
    setLoading(true);
    setSearchFilter("");

    axios
      .get(`/api/products/${product.id}/substitutes`)
      .then((res) => {
        if (active) setData(res.data);
      })
      .catch(() => {
        if (active) setData({ substitutes: [], match_type: "none" });
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [open, product?.id]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e) => {
      if (e.key === "Escape") onClose?.();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  if (!open || !product) return null;

  const substitutes = Array.isArray(data?.substitutes) ? data.substitutes : [];
  const filtered = substitutes.filter((sub) => {
    if (inStockOnly && (Number(sub.quantity) || 0) <= 0) return false;
    if (!searchFilter.trim()) return true;
    const q = searchFilter.toLowerCase();
    return (
      (sub.name || "").toLowerCase().includes(q) ||
      (sub.formulation || "").toLowerCase().includes(q) ||
      (sub.brand?.name || "").toLowerCase().includes(q) ||
      (sub.rack || "").toLowerCase().includes(q)
    );
  });

  const sourceProduct = data?.product || product;

  return (
    <div
      className="fixed inset-0 z-[120] flex items-center justify-center bg-black/60 backdrop-blur-sm p-4"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose?.();
      }}
    >
      <div
        className={`w-full max-w-3xl rounded-2xl border shadow-2xl overflow-hidden transition-all ${
          isDark
            ? "bg-slate-900 border-slate-700 text-slate-100"
            : "bg-white border-slate-200 text-slate-800"
        }`}
      >
        {/* Header */}
        <div
          className={`flex items-center justify-between px-5 py-4 border-b ${
            isDark ? "border-slate-800 bg-slate-800/60" : "border-slate-100 bg-slate-50"
          }`}
        >
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-xl bg-emerald-500/15 text-emerald-500">
              <ArrowsRightLeftIcon className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold flex items-center gap-2">
                Therapeutic / Generic Substitutes
                {data?.match_type === "formulation" && (
                  <span className="px-2 py-0.5 text-[11px] font-semibold rounded-full bg-emerald-500/15 text-emerald-500">
                    Salt / Formula Match
                  </span>
                )}
                {data?.match_type === "category" && (
                  <span className="px-2 py-0.5 text-[11px] font-semibold rounded-full bg-blue-500/15 text-blue-500">
                    Category Alternatives
                  </span>
                )}
              </h3>
              <p className="text-xs opacity-75 mt-0.5">
                Original:{" "}
                <span className="font-semibold">{sourceProduct?.name || product?.product_name}</span>
                {sourceProduct?.formulation ? (
                  <span className="ml-2 px-1.5 py-0.5 rounded bg-amber-500/15 text-amber-500 font-medium">
                    Salt: {sourceProduct.formulation}
                  </span>
                ) : null}
                <span className="ml-2">
                  • Price:{" "}
                  <strong>
                    {formatCurrency(
                      Number(sourceProduct?.unit_sale_price ?? product?.price ?? 0)
                    )}
                  </strong>
                </span>
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={onClose}
            className="p-1.5 rounded-lg opacity-70 hover:opacity-100 hover:bg-slate-500/10"
            title="Close (Esc)"
          >
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        {/* Filter Bar */}
        <div
          className={`flex flex-wrap items-center justify-between gap-3 px-5 py-3 border-b text-xs ${
            isDark ? "border-slate-800 bg-slate-900" : "border-slate-100 bg-white"
          }`}
        >
          <input
            type="text"
            value={searchFilter}
            onChange={(e) => setSearchFilter(e.target.value)}
            placeholder="Filter substitutes by brand, name, salt, or rack..."
            className={`flex-1 min-w-[220px] px-3 py-1.5 rounded-lg border text-xs outline-none ${
              isDark
                ? "bg-slate-800 border-slate-700 text-slate-100 focus:border-emerald-500"
                : "bg-slate-50 border-slate-200 text-slate-800 focus:border-emerald-500"
            }`}
          />

          <label className="flex items-center gap-2 cursor-pointer select-none font-medium">
            <input
              type="checkbox"
              checked={inStockOnly}
              onChange={(e) => setInStockOnly(e.target.checked)}
              className="rounded text-emerald-600 focus:ring-emerald-500"
            />
            <span>Show In-Stock Only ({substitutes.filter((s) => s.quantity > 0).length})</span>
          </label>
        </div>

        {/* Body */}
        <div className="max-h-[60vh] overflow-y-auto divide-y divide-slate-200/40 dark:divide-slate-800">
          {loading ? (
            <div className="py-12 text-center text-sm opacity-70">
              Searching matching formulations & alternatives...
            </div>
          ) : filtered.length === 0 ? (
            <div className="py-12 px-6 text-center">
              <CubeIcon className="w-10 h-10 mx-auto opacity-30 mb-2" />
              <p className="text-sm font-semibold">No matching substitutes found</p>
              <p className="text-xs opacity-60 mt-1">
                {inStockOnly && substitutes.length > 0
                  ? "Uncheck 'Show In-Stock Only' to view out-of-stock substitutes."
                  : "Ensure products have their Generic / Salt Formulation or Category filled in."}
              </p>
            </div>
          ) : (
            filtered.map((sub) => {
              const hasStock = Number(sub.quantity) > 0;
              const diff = Number(sub.price_diff || 0);
              return (
                <div
                  key={sub.id}
                  className={`flex flex-wrap items-center justify-between gap-3 px-5 py-3.5 transition ${
                    isDark ? "hover:bg-slate-800/50" : "hover:bg-slate-50"
                  }`}
                >
                  <div className="flex-1 min-w-[240px]">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="font-bold text-sm">{sub.name}</span>
                      {sub.is_exact_formulation && (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold bg-emerald-500/15 text-emerald-500">
                          <SparklesIcon className="w-3 h-3" /> Exact Salt
                        </span>
                      )}
                      {sub.brand?.name && (
                        <span className="text-[11px] px-2 py-0.5 rounded bg-slate-500/10 opacity-80">
                          {sub.brand.name}
                        </span>
                      )}
                      {sub.rack && (
                        <span className="inline-flex items-center gap-1 text-[11px] px-2 py-0.5 rounded bg-indigo-500/15 text-indigo-400 font-medium">
                          <MapPinIcon className="w-3 h-3" /> Rack: {sub.rack}
                        </span>
                      )}
                    </div>

                    <div className="flex items-center gap-3 mt-1 text-xs opacity-75">
                      {sub.formulation && <span>Salt: {sub.formulation}</span>}
                      <span>Pack Size: {sub.pack_size || 1}</span>
                      {sub.fefo_batch?.batch_number && (
                        <span>
                          FEFO Batch: <strong>{sub.fefo_batch.batch_number}</strong> (Exp:{" "}
                          {String(sub.fefo_batch.expiry_date || "").slice(0, 10)})
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Stock + Price + Actions */}
                  <div className="flex items-center gap-4">
                    <div className="text-right">
                      <div className="text-sm font-bold">
                        {formatCurrency(sub.unit_sale_price)}
                        <span className="text-[10px] font-normal opacity-70"> / unit</span>
                      </div>
                      <div className="text-[11px]">
                        {diff === 0 ? (
                          <span className="opacity-60">Same price</span>
                        ) : diff < 0 ? (
                          <span className="text-emerald-500 font-semibold">
                            {formatCurrency(Math.abs(diff))} cheaper
                          </span>
                        ) : (
                          <span className="text-amber-500 font-semibold">
                            +{formatCurrency(diff)} higher
                          </span>
                        )}
                      </div>
                    </div>

                    <div className="min-w-[85px] text-center">
                      <span
                        className={`inline-block px-2.5 py-1 rounded-full text-xs font-bold ${
                          hasStock
                            ? "bg-emerald-500/15 text-emerald-500"
                            : "bg-rose-500/15 text-rose-500"
                        }`}
                      >
                        {hasStock ? `${sub.quantity} in stock` : "Out of stock"}
                      </span>
                    </div>

                    <div className="flex items-center gap-1.5">
                      {allowSwap && (
                        <button
                          type="button"
                          onClick={() => onSelectSubstitute?.(sub, "swap")}
                          className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-bold bg-emerald-600 hover:bg-emerald-500 text-white shadow-sm transition"
                          title="Replace current item with this substitute"
                        >
                          <ArrowsRightLeftIcon className="w-3.5 h-3.5" />
                          Swap
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => onSelectSubstitute?.(sub, "add")}
                        className={`inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg text-xs font-semibold border transition ${
                          isDark
                            ? "border-slate-700 hover:bg-slate-800 text-slate-200"
                            : "border-slate-200 hover:bg-slate-100 text-slate-700"
                        }`}
                        title="Add substitute as a new line"
                      >
                        <PlusIcon className="w-3.5 h-3.5" />
                        Add
                      </button>
                    </div>
                  </div>
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}

