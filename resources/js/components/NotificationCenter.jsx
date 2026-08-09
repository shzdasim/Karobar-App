import { useCallback, useEffect, useMemo, useState } from "react";
import axios from "axios";
import { useNavigate } from "react-router-dom";
import { useTheme } from "@/context/ThemeContext.jsx";
import {
  BellIcon,
  XMarkIcon,
  ExclamationTriangleIcon,
  CheckCircleIcon,
  ArrowPathIcon,
  ArrowRightIcon,
BellSlashIcon,
  HandRaisedIcon,
  PlusCircleIcon,
} from "@heroicons/react/24/outline";

const fmtNumber = (v) =>
  Number(v || 0).toLocaleString(undefined, { maximumFractionDigits: 0 });

export default function NotificationCenter({ open, onClose }) {
  const { theme, isDark } = useTheme();
  const navigate = useNavigate();

  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || '#3b82f6',
    primaryHover: theme?.primary_hover || '#2563eb',
    primaryLight: theme?.primary_light || '#dbeafe',
    secondary: theme?.secondary_color || '#8b5cf6',
    secondaryHover: theme?.secondary_hover || '#7c3aed',
    secondaryLight: theme?.secondary_light || '#ede9fe',
    tertiary: theme?.tertiary_color || '#06b6d4',
    tertiaryHover: theme?.tertiary_hover || '#0891b2',
    tertiaryLight: theme?.tertiary_light || '#cffafe',
    danger: theme?.danger_color || '#ef4444',
  }), [theme]);

const [rows, setRows] = useState([]);
  const [count, setCount] = useState(0);
  const [loading, setLoading] = useState(false);

  // User demand tab state
  const [tab, setTab] = useState("low-stock");
  const [demands, setDemands] = useState([]);
  const [demandCount, setDemandCount] = useState(0);
  const [demandsLoading, setDemandsLoading] = useState(false);

  const fetchLowStock = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await axios.get("/api/notifications/low-stock", {
        params: { limit: 200 },
      });
      setRows(Array.isArray(data?.rows) ? data.rows : []);
      setCount(Number(data?.count || 0));
    } catch (err) {
      console.error("Failed to fetch low stock notifications:", err);
      setRows([]);
      setCount(0);
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchDemands = useCallback(async () => {
    setDemandsLoading(true);
    try {
      const { data } = await axios.get("/api/user-demands", {
        params: { status: "pending", limit: 100 },
      });
      setDemands(Array.isArray(data?.rows) ? data.rows : []);
      setDemandCount(Number(data?.pending_count || 0));
    } catch (err) {
      console.error("Failed to fetch user demands:", err);
      setDemands([]);
      setDemandCount(0);
    } finally {
      setDemandsLoading(false);
    }
  }, []);

  // Fetch when opened
  useEffect(() => {
    if (open) {
      fetchLowStock();
      fetchDemands();
    }
  }, [open, fetchLowStock, fetchDemands]);

  // Close on Escape
  useEffect(() => {
    if (!open) return;
    const onEsc = (e) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onEsc);
    return () => window.removeEventListener("keydown", onEsc);
  }, [open, onClose]);

  // Lock body scroll when open
  useEffect(() => {
    if (open) {
      document.body.style.overflow = "hidden";
    } else {
      document.body.style.overflow = "";
    }
    return () => {
      document.body.style.overflow = "";
    };
  }, [open]);

const goToProducts = () => {
    onClose();
    navigate("/products");
  };

  const goToDemands = () => {
    onClose();
    navigate("/user-demands");
  };

  // Notify Toolbar/topbar listeners that the notification count changed
  const notifyCountChanged = useCallback(() => {
    window.dispatchEvent(new CustomEvent("notifications-changed"));
  }, []);

  const dismissOne = async (productId) => {
    try {
      await axios.post("/api/notifications/dismiss", { product_id: productId });
      setRows((prev) => prev.filter((r) => Number(r.product_id) !== Number(productId)));
      setCount((prev) => Math.max(0, prev - 1));
      notifyCountChanged();
    } catch (err) {
      console.error("Failed to dismiss notification:", err);
    }
  };

  const dismissAll = async () => {
    if (!rows.length) return;
    try {
      await axios.post("/api/notifications/dismiss-all");
      setRows([]);
      setCount(0);
      notifyCountChanged();
    } catch (err) {
      console.error("Failed to dismiss all notifications:", err);
    }
  };

  return (
    <>
      {/* Overlay */}
      <div
        className={`fixed inset-0 z-[9998] bg-slate-900/50 backdrop-blur-sm transition-opacity duration-300 ${
          open ? "opacity-100" : "opacity-0 pointer-events-none"
        }`}
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Side Panel */}
      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Notification Center"
        className={[
          "fixed top-0 right-0 h-full w-full sm:w-[400px] z-[9999]",
          "bg-white dark:bg-slate-800 shadow-2xl",
          "transform transition-transform duration-300 ease-out",
          open ? "translate-x-0" : "translate-x-full",
        ].join(" ")}
      >
        {/* Header */}
        <div
          className="relative flex items-center justify-between px-4 py-4 text-white"
          style={{
            background: `linear-gradient(120deg, ${themeColors.secondary}, ${themeColors.primary})`,
          }}
        >
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-xl bg-white/20 backdrop-blur-md">
              <BellIcon className="w-5 h-5" />
            </div>
<div>
              <h2 className="text-base font-bold leading-none">Notification Center</h2>
              <p className="text-xs text-white/80 mt-1">
                {tab === "low-stock"
                  ? `${count} low stock alert${count === 1 ? "" : "s"}`
                  : `${demandCount} pending product demand${demandCount === 1 ? "" : "s"}`}
              </p>
            </div>
          </div>
<div className="flex items-center gap-1">
            {rows.length > 0 && (
              <button
                onClick={dismissAll}
                title="Dismiss all notifications"
                className="p-2 rounded-lg hover:bg-white/20 transition-all duration-200"
              >
                <BellSlashIcon className="w-5 h-5" />
              </button>
            )}
            <button
              onClick={fetchLowStock}
              disabled={loading}
              title="Refresh"
              className="p-2 rounded-lg hover:bg-white/20 transition-all duration-200"
            >
              <ArrowPathIcon className={`w-5 h-5 ${loading ? "animate-spin" : ""}`} />
            </button>
            <button
              onClick={onClose}
              title="Close (Esc)"
              className="p-2 rounded-lg hover:bg-white/20 transition-all duration-200"
            >
<XMarkIcon className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Tabs */}
        <div className="flex border-b border-gray-100 dark:border-slate-700 bg-white dark:bg-slate-800 px-2 pt-2 gap-1">
          <button
            onClick={() => setTab("low-stock")}
            className={`flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-t-lg text-xs font-semibold transition-all duration-200 ${
              tab === "low-stock"
                ? "text-white"
                : "text-gray-500 dark:text-gray-400 hover:bg-gray-50 dark:hover:bg-slate-700"
            }`}
            style={tab === "low-stock" ? { background: `linear-gradient(to right, ${themeColors.danger}, ${themeColors.secondary})` } : {}}
          >
            <ExclamationTriangleIcon className="w-3.5 h-3.5" />
            Low Stock
            {count > 0 && (
              <span className="min-w-[18px] h-[18px] px-1 rounded-full text-[10px] font-bold inline-flex items-center justify-center"
                style={{ backgroundColor: tab === "low-stock" ? "rgba(255,255,255,0.3)" : themeColors.danger, color: "#fff" }}>
                {count > 99 ? "99+" : count}
              </span>
            )}
          </button>
          <button
            onClick={() => setTab("demands")}
            className={`flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-t-lg text-xs font-semibold transition-all duration-200 ${
              tab === "demands"
                ? "text-white"
                : "text-gray-500 dark:text-gray-400 hover:bg-gray-50 dark:hover:bg-slate-700"
            }`}
            style={tab === "demands" ? { background: `linear-gradient(to right, ${themeColors.tertiary}, ${themeColors.primary})` } : {}}
          >
            <HandRaisedIcon className="w-3.5 h-3.5" />
            Demands
            {demandCount > 0 && (
              <span className="min-w-[18px] h-[18px] px-1 rounded-full text-[10px] font-bold inline-flex items-center justify-center"
                style={{ backgroundColor: tab === "demands" ? "rgba(255,255,255,0.3)" : themeColors.tertiary, color: "#fff" }}>
                {demandCount > 99 ? "99+" : demandCount}
              </span>
            )}
          </button>
        </div>

{/* Body */}
        <div className="h-[calc(100%-64px)] overflow-y-auto">
          {tab === "demands" ? (
            demandsLoading && demands.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full gap-3 text-gray-500 dark:text-gray-400">
                <ArrowPathIcon className="w-8 h-8 animate-spin" style={{ color: themeColors.tertiary }} />
                <span className="text-sm">Loading demands…</span>
              </div>
            ) : demands.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full gap-3 p-6 text-center">
                <div className="p-4 rounded-2xl" style={{ backgroundColor: themeColors.tertiaryLight }}>
                  <HandRaisedIcon className="w-10 h-10" style={{ color: themeColors.tertiary }} />
                </div>
                <h3 className="text-base font-semibold text-gray-800 dark:text-gray-100">No pending demands</h3>
                <p className="text-sm text-gray-500 dark:text-gray-400 max-w-[260px]">
                  Customers haven't requested any new products yet. Click below to add one.
                </p>
                <button
                  onClick={() => { onClose(); navigate("/user-demands/create"); }}
                  className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl text-sm font-semibold text-white mt-2"
                  style={{ background: `linear-gradient(to right, ${themeColors.tertiary}, ${themeColors.primary})` }}
                >
                  <PlusCircleIcon className="w-4 h-4" />
                  Request Product
                </button>
              </div>
            ) : (
              <div className="divide-y divide-gray-100 dark:divide-slate-700">
                {demands.map((d) => (
                  <div
                    key={d.id}
                    className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/50 transition-colors cursor-pointer"
                    onClick={goToDemands}
                  >
                    <div className="flex items-start gap-3">
                      <div className="p-2 rounded-xl shrink-0" style={{ backgroundColor: themeColors.tertiaryLight }}>
                        <HandRaisedIcon className="w-5 h-5" style={{ color: themeColors.tertiary }} />
                      </div>
<div className="flex-1 min-w-0">
                        <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {d.requested_name || d.product?.name || "Product demand"}
                        </p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                          {(d.customer?.name || d.customer_name || "Walk-in customer")} · {d.requested_quantity || d.quantity_requested || 0} unit(s)
                        </p>
                        <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">
                          Requested {d.created_at ? new Date(d.created_at).toLocaleDateString() : ""}
                        </p>
                      </div>
                    </div>
                  </div>
                ))}
                <div className="p-3">
                  <button
                    onClick={goToDemands}
                    className="w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white transition-all hover:scale-[1.01]"
                    style={{ background: `linear-gradient(to right, ${themeColors.tertiary}, ${themeColors.primary})` }}
                  >
                    View All Demands
                    <ArrowRightIcon className="w-4 h-4" />
                  </button>
                </div>
              </div>
            )
          ) : loading && rows.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full gap-3 text-gray-500 dark:text-gray-400">
              <ArrowPathIcon className="w-8 h-8 animate-spin" style={{ color: themeColors.secondary }} />
              <span className="text-sm">Checking stock levels…</span>
            </div>
          ) : rows.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full gap-3 p-6 text-center">
              <div
                className="p-4 rounded-2xl"
                style={{ backgroundColor: themeColors.successLight || themeColors.tertiaryLight }}
              >
                <CheckCircleIcon className="w-10 h-10" style={{ color: themeColors.success_color || themeColors.tertiary }} />
              </div>
              <h3 className="text-base font-semibold text-gray-800 dark:text-gray-100">
                All caught up!
              </h3>
              <p className="text-sm text-gray-500 dark:text-gray-400 max-w-[260px]">
                No running products have dropped below their pack size. You'll be notified here when stock gets low.
              </p>
            </div>
          ) : (
            <div className="divide-y divide-gray-100 dark:divide-slate-700">
              {rows.map((r) => {
                const short = Number(r.units_below_pack || 0);
                const soldPct = Math.min(
                  100,
                  Math.round((short / Math.max(Number(r.pack_size) || 1, 1)) * 100)
                );
return (
                  <div
                    key={r.product_id}
                    className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/50 transition-colors cursor-pointer"
                    onClick={goToProducts}
                  >
                    <div className="flex items-start gap-3">
                      <div
                        className="p-2 rounded-xl shrink-0"
                        style={{ backgroundColor: themeColors.dangerLight || "#fee2e2" }}
                      >
                        <ExclamationTriangleIcon
                          className="w-5 h-5"
                          style={{ color: themeColors.danger }}
                        />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-start justify-between gap-2">
                          <div className="min-w-0">
                            <p className="text-sm font-semibold text-gray-900 dark:text-gray-100 truncate">
                              {r.product_name}
                            </p>
                            <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                              {r.product_code} · {r.brand_name || "No brand"}
                            </p>
                          </div>
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              dismissOne(r.product_id);
                            }}
                            title="Dismiss this notification"
                            aria-label={`Dismiss notification for ${r.product_name}`}
                            className="p-1.5 rounded-lg text-gray-400 hover:text-white hover:bg-red-500 transition-all duration-200 shrink-0"
                          >
                            <XMarkIcon className="w-4 h-4" />
                          </button>
                        </div>

                        {/* Progress bar */}
                        <div className="mt-3">
                          <div className="flex items-center justify-between text-xs mb-1">
                            <span className="text-gray-500 dark:text-gray-400">
                              Stock: <span className="font-bold text-gray-800 dark:text-gray-100">{fmtNumber(r.quantity)}</span> / pack {fmtNumber(r.pack_size)}
                            </span>
                            <span className="font-semibold" style={{ color: themeColors.danger }}>
                              {fmtNumber(short)} below pack
                            </span>
                          </div>
                          <div className="h-2 rounded-full bg-gray-100 dark:bg-slate-700 overflow-hidden">
                            <div
                              className="h-full rounded-full transition-all duration-500"
                              style={{
                                width: `${Math.max(0, 100 - soldPct)}%`,
                                background: `linear-gradient(to right, ${themeColors.danger}, ${themeColors.secondary})`,
                              }}
                            />
                          </div>
                        </div>

                        {(r.supplier_name || "") && (
                          <p className="text-xs text-gray-400 dark:text-gray-500 mt-2">
                            Supplier: {r.supplier_name}
                          </p>
                        )}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

{/* Footer */}
        {tab === "demands" && demands.length > 0 ? (
          <div className="absolute bottom-0 left-0 right-0 p-3 border-t border-gray-100 dark:border-slate-700 bg-white dark:bg-slate-800">
            <button
              onClick={goToDemands}
              className="w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white transition-all hover:scale-[1.01] active:scale-[0.99]"
              style={{
                background: `linear-gradient(to right, ${themeColors.tertiary}, ${themeColors.primary})`,
                boxShadow: `0 4px 14px 0 ${themeColors.tertiary}40`,
              }}
            >
              View All Demands
              <ArrowRightIcon className="w-4 h-4" />
            </button>
          </div>
        ) : tab === "low-stock" && rows.length > 0 ? (
          <div className="absolute bottom-0 left-0 right-0 p-3 border-t border-gray-100 dark:border-slate-700 bg-white dark:bg-slate-800">
            <button
              onClick={goToProducts}
              className="w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white transition-all hover:scale-[1.01] active:scale-[0.99]"
              style={{
                background: `linear-gradient(to right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
              }}
            >
              View Products
              <ArrowRightIcon className="w-4 h-4" />
            </button>
          </div>
        ) : null}
      </aside>
    </>
  );
}
