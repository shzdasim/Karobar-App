import { useCallback, useEffect, useState, useRef } from "react";
import axios from "axios";
import { createPortal } from "react-dom";
import { useNavigate } from "react-router-dom";
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
  const navigate = useNavigate();
  const panelRef = useRef(null);
  const [stockError, setStockError] = useState("");
  const [demandError, setDemandError] = useState("");
  const [actionError, setActionError] = useState("");
  const [dismissBusy, setDismissBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    const previousFocus = document.activeElement;
    panelRef.current?.focus();
    const trapFocus = (event) => {
      if (event.key !== "Tab") return;
      const items = [...panelRef.current.querySelectorAll('button:not(:disabled), [tabindex="0"]')];
      const first = items[0], last = items[items.length - 1];
      if (event.shiftKey && (document.activeElement === first || document.activeElement === panelRef.current)) {
        event.preventDefault(); last?.focus();
      } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === panelRef.current)) {
        event.preventDefault(); first?.focus();
      }
    };
    const panel = panelRef.current;
    panel?.addEventListener("keydown", trapFocus);
    return () => { panel?.removeEventListener("keydown", trapFocus); previousFocus?.focus(); };
  }, [open]);

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
    setStockError("");
    try {
      const { data } = await axios.get("/api/notifications/low-stock", {
        params: { limit: 200 },
      });
      setRows(Array.isArray(data?.rows) ? data.rows : []);
      setCount(Number(data?.count || 0));
    } catch (err) {
      console.error("Failed to fetch low stock notifications:", err);
      setStockError("Stock alerts could not be loaded. Please try again.");
      setRows([]);
      setCount(0);
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchDemands = useCallback(async () => {
    setDemandsLoading(true);
    setDemandError("");
    try {
      const { data } = await axios.get("/api/user-demands", {
        params: { status: "pending", limit: 100 },
      });
      setDemands(Array.isArray(data?.rows) ? data.rows : []);
      setDemandCount(Number(data?.pending_count || 0));
    } catch (err) {
      console.error("Failed to fetch user demands:", err);
      setDemandError("Product demands could not be loaded. Please try again.");
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
    setDismissBusy(true);
    setActionError("");
    try {
      await axios.post("/api/notifications/dismiss", { product_id: productId });
      setRows((prev) => prev.filter((r) => Number(r.product_id) !== Number(productId)));
      setCount((prev) => Math.max(0, prev - 1));
      notifyCountChanged();
    } catch (err) {
      console.error("Failed to dismiss notification:", err);
      setActionError("This alert could not be dismissed. Please try again.");
    } finally {
      setDismissBusy(false);
    }
  };

  const dismissAll = async () => {
    if (!rows.length) return;
    setDismissBusy(true);
    setActionError("");
    try {
      await axios.post("/api/notifications/dismiss-all");
      setRows([]);
      setCount(0);
      notifyCountChanged();
    } catch (err) {
      console.error("Failed to dismiss all notifications:", err);
      setActionError("Alerts could not be dismissed. Please try again.");
    } finally {
      setDismissBusy(false);
    }
  };

  const activeLoading = tab === "demands" ? demandsLoading : loading;
  const activeError = tab === "demands" ? demandError : stockError;
  const refresh = tab === "demands" ? fetchDemands : fetchLowStock;
  const activeCount = tab === "demands" ? demandCount : count;

  return createPortal(
    <div className="activity-center-layer" hidden={!open}>
      <div className="activity-center-scrim" onClick={onClose} aria-hidden="true" />
      <aside ref={panelRef} tabIndex={-1} role="dialog" aria-modal="true"
        aria-label="Notification Center" aria-describedby="activity-center-description"
        className="activity-center">
        <header className="activity-center-header">
          <div className="activity-center-heading-row">
            <span className="activity-center-mark" aria-hidden="true"><BellIcon className="h-6 w-6" /></span>
            <div className="activity-center-header-actions">
              <button type="button" onClick={refresh} disabled={activeLoading} className="activity-icon-button"
                aria-label={tab === "demands" ? "Refresh demands" : "Refresh stock alerts"} title="Refresh">
                <ArrowPathIcon className={`h-5 w-5 ${activeLoading ? "motion-safe:animate-spin" : ""}`} aria-hidden="true" />
              </button>
              <button type="button" onClick={onClose} className="activity-icon-button" aria-label="Close notification center" title="Close (Esc)">
                <XMarkIcon className="h-5 w-5" aria-hidden="true" />
              </button>
            </div>
          </div>
          <p className="activity-eyebrow">Your workspace inbox</p>
          <h2>Notification center</h2>
          <p id="activity-center-description" className="activity-muted">Stock alerts and product requests, in one place.</p>
        </header>

        <div className="activity-tabs" role="tablist" aria-label="Notification category">
          {[
            { id: "low-stock", label: "Low stock", icon: ExclamationTriangleIcon, total: count },
            { id: "demands", label: "Demands", icon: HandRaisedIcon, total: demandCount },
          ].map(({ id, label, icon: Icon, total }) => (
            <button key={id} id={`activity-tab-${id}`} type="button" role="tab"
              aria-selected={tab === id} aria-controls="activity-tab-panel" tabIndex={tab === id ? 0 : -1}
              onClick={() => setTab(id)} className={`activity-tab ${tab === id ? "activity-tab-active" : ""}`}
              onKeyDown={(event) => {
                if (["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
                  event.preventDefault();
                  const next = event.key === "Home" ? "low-stock" : event.key === "End" ? "demands" : tab === "demands" ? "low-stock" : "demands";
                  setTab(next); document.getElementById(`activity-tab-${next}`)?.focus();
                }
              }}>
              <Icon className="h-4 w-4" aria-hidden="true" />
              {label}<span className="activity-count">{total > 99 ? "99+" : total}</span>
            </button>
          ))}
        </div>

        <div className="activity-list-heading">
          <span>{tab === "demands" ? "Awaiting attention" : "Inventory watch"}</span>
          <span className="activity-muted" aria-live="polite">{activeLoading ? "Updating…" : `${activeCount} ${tab === "demands" ? "pending" : "alerts"}`}</span>
        </div>
        {actionError && <p role="alert" className="activity-error">{actionError}</p>}
        <div id="activity-tab-panel" role="tabpanel" aria-labelledby={`activity-tab-${tab}`}
          tabIndex={0} aria-busy={activeLoading} className="activity-center-body">
          {activeError ? (
            <div className="activity-empty" role="alert">
              <ExclamationTriangleIcon className="h-10 w-10 activity-danger" aria-hidden="true" />
              <h3>Unable to load updates</h3><p className="activity-muted">{activeError}</p>
              <button type="button" onClick={refresh} className="activity-secondary-button">Try again</button>
            </div>
          ) : activeLoading && (tab === "demands" ? demands.length : rows.length) === 0 ? (
            <div className="activity-empty" role="status">
              <ArrowPathIcon className="h-8 w-8 motion-safe:animate-spin" aria-hidden="true" />
              <p>{tab === "demands" ? "Loading product demands…" : "Checking stock levels…"}</p>
            </div>
          ) : tab === "demands" ? (
            demands.length === 0 ? (
              <div className="activity-empty">
                <span className="activity-empty-mark"><HandRaisedIcon className="h-8 w-8" aria-hidden="true" /></span>
                <h3>No pending demands</h3>
                <p className="activity-muted">New product requests will appear here. You can also create a request for a customer.</p>
                <button type="button" onClick={() => { onClose(); navigate("/user-demands/create"); }} className="activity-secondary-button">
                  <PlusCircleIcon className="h-5 w-5" aria-hidden="true" />Request product
                </button>
              </div>
            ) : (
              <ul className="activity-list">
                {demands.map((d) => (
                  <li key={d.id}>
                    <button type="button" onClick={goToDemands} className="activity-card activity-demand-card">
                      <span className="activity-card-topline"><span className="activity-status"><HandRaisedIcon className="h-4 w-4" aria-hidden="true" />Product request</span><ArrowRightIcon className="h-4 w-4 activity-muted" aria-hidden="true" /></span>
                      <h3>{d.requested_name || d.product?.name || "Product demand"}</h3>
                      <p className="activity-muted">{d.customer?.name || d.customer_name || "Walk-in customer"}</p>
                      <div className="activity-card-meta"><span>{d.requested_quantity || d.quantity_requested || 0} unit(s) requested</span><span className="activity-muted">{d.created_at ? new Date(d.created_at).toLocaleDateString() : ""}</span></div>
                    </button>
                  </li>
                ))}
              </ul>
            )
          ) : rows.length === 0 ? (
            <div className="activity-empty">
              <span className="activity-empty-mark"><CheckCircleIcon className="h-8 w-8" aria-hidden="true" /></span>
              <h3>All caught up</h3><p className="activity-muted">No running products have dropped below their pack size. New stock alerts will appear here.</p>
            </div>
          ) : (
            <ul className="activity-list">
              {rows.map((r) => {
                const short = Number(r.units_below_pack || 0);
                const soldPct = Math.min(100, Math.round((short / Math.max(Number(r.pack_size) || 1, 1)) * 100));
                return (
                  <li key={r.product_id} className="activity-card">
                    <div className="activity-card-topline">
                      <span className="activity-status activity-danger"><ExclamationTriangleIcon className="h-4 w-4" aria-hidden="true" />Low stock</span>
                      <button type="button" disabled={dismissBusy} onClick={() => dismissOne(r.product_id)} className="activity-icon-button"
                        aria-label={`Dismiss notification for ${r.product_name}`} title="Dismiss alert"><XMarkIcon className="h-4 w-4" aria-hidden="true" /></button>
                    </div>
                    <button type="button" onClick={goToProducts} className="activity-product-link">
                      <h3>{r.product_name}</h3><p className="activity-muted">{r.product_code} · {r.brand_name || "No brand"}</p>
                    </button>
                    <div className="activity-stock-values"><span>In stock <strong>{fmtNumber(r.quantity)}</strong></span><span>Pack size <strong>{fmtNumber(r.pack_size)}</strong></span></div>
                    <div className="activity-stock-track" aria-hidden="true"><div style={{ width: `${Math.max(0, 100 - soldPct)}%` }} /></div>
                    <p className="activity-stock-caption"><span className="activity-danger">{fmtNumber(short)} below pack</span><button type="button" onClick={goToProducts}>View products <ArrowRightIcon className="h-4 w-4" aria-hidden="true" /></button></p>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
        <footer className="activity-center-footer">
          {tab === "low-stock" && rows.length > 0 && !activeError && (
            <button type="button" onClick={dismissAll} disabled={dismissBusy} className="activity-dismiss-all"><BellSlashIcon className="h-4 w-4" aria-hidden="true" />{dismissBusy ? "Dismissing…" : "Dismiss all alerts"}</button>
          )}
          <button type="button" onClick={tab === "demands" ? goToDemands : goToProducts} className="activity-primary-button">
            {tab === "demands" ? "View all demands" : "View all products"}<ArrowRightIcon className="h-5 w-5" aria-hidden="true" />
          </button>
        </footer>
      </aside>
    </div>, document.body
  );
}
