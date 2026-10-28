import React, { useState, useEffect, useRef } from "react";
import axios from "axios";
import {
  XMarkIcon,
  ClockIcon,
  UserIcon,
  ArrowPathIcon,
  ExclamationCircleIcon,
  CheckCircleIcon,
  DocumentTextIcon,
} from "@heroicons/react/24/outline";

export default function DocumentHistoryModal({
  isOpen,
  onClose,
  entityType,
  entityId,
  documentNumber = "",
}) {
  const [loading, setLoading] = useState(true);
  const [histories, setHistories] = useState([]);
  const [error, setError] = useState(null);
  const [expandedIds, setExpandedIds] = useState({});
  const [filterEntityType, setFilterEntityType] = useState("");
  const [filterAction, setFilterAction] = useState("");
  const isGlobalMode = !entityType || !entityId;
  const mouseDownTargetRef = useRef(null);

  const toggleExpanded = (id) => {
    setExpandedIds((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  useEffect(() => {
    if (!isOpen) return;

    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const params = isGlobalMode
          ? {
              ...(filterEntityType ? { entity_type: filterEntityType } : {}),
              ...(filterAction ? { action: filterAction } : {}),
              per_page: 50,
            }
          : { entity_type: entityType, entity_id: entityId };
        const { data } = await axios.get("/api/document-histories", { params });
        if (!cancelled) {
          setHistories(Array.isArray(data?.histories) ? data.histories : []);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err?.response?.data?.message || "Failed to load document history.");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [isOpen, entityType, entityId, isGlobalMode, filterEntityType, filterAction]);

  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const handleMouseDown = (e) => {
    mouseDownTargetRef.current = e.target;
  };

  const handleBackdropClick = (e) => {
    if (e.target === e.currentTarget && mouseDownTargetRef.current === e.currentTarget) {
      onClose();
    }
  };

  const formatDateTime = (iso) => {
    if (!iso) return "";
    const d = new Date(iso);
    return `${d.toLocaleDateString()} at ${d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`;
  };

  const getActionBadge = (action) => {
    switch (action) {
      case "created":
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
            <CheckCircleIcon className="w-3.5 h-3.5" />
            Created
          </span>
        );
      case "updated":
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-blue-500/10 text-blue-400 border border-blue-500/20">
            <ArrowPathIcon className="w-3.5 h-3.5" />
            Edited
          </span>
        );
      case "reversed":
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-rose-500/10 text-rose-400 border border-rose-500/20">
            <ExclamationCircleIcon className="w-3.5 h-3.5" />
            Reversed (Voided)
          </span>
        );
      default:
        return (
          <span className="px-2 py-0.5 rounded text-xs bg-slate-800 text-slate-300">
            {action}
          </span>
        );
    }
  };

  return (
    <div
      onMouseDown={handleMouseDown}
      onClick={handleBackdropClick}
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-fadeIn cursor-default"
      role="dialog"
      aria-modal="true"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="relative w-full max-w-2xl max-h-[85vh] flex flex-col bg-slate-900 border border-slate-700/80 rounded-2xl shadow-2xl overflow-hidden text-slate-100"
      >
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-800 bg-slate-800/50">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-xl bg-purple-600/20 text-purple-400 border border-purple-500/20">
              <ClockIcon className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-base font-bold text-white flex items-center gap-2">
                Audit Trail &amp; History
                {documentNumber && (
                  <span className="text-xs px-2 py-0.5 rounded font-mono bg-slate-800 text-blue-400 border border-slate-700">
                    {documentNumber}
                  </span>
                )}
              </h2>
              <p className="text-xs text-slate-400">
                Traceable immutable ledger of creation, edits, and reversals
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors"
          >
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        {/* Content Body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-4">
          {isGlobalMode && (
            <div className="flex flex-wrap items-center gap-2 pb-2 border-b border-slate-800 text-xs">
              <select
                value={filterEntityType}
                onChange={(e) => setFilterEntityType(e.target.value)}
                className="h-8 px-2.5 rounded-lg bg-slate-800 border border-slate-700 text-slate-200"
              >
                <option value="">All Document Types</option>
                <option value="sale_invoice">Sale Invoices</option>
                <option value="purchase_invoice">Purchase Invoices</option>
                <option value="sale_return">Sale Returns</option>
                <option value="purchase_return">Purchase Returns</option>
                <option value="supplier_payment">Supplier Payments</option>
                <option value="customer_payment">Customer Payments</option>
              </select>
              <select
                value={filterAction}
                onChange={(e) => setFilterAction(e.target.value)}
                className="h-8 px-2.5 rounded-lg bg-slate-800 border border-slate-700 text-slate-200"
              >
                <option value="">All Actions</option>
                <option value="created">Created</option>
                <option value="updated">Edited</option>
                <option value="reversed">Reversed / Voided</option>
              </select>
            </div>
          )}
          {loading ? (
            <div className="py-12 text-center text-slate-400 space-y-2">
              <ArrowPathIcon className="w-8 h-8 mx-auto animate-spin text-blue-400" />
              <p className="text-xs">Loading audit trail…</p>
            </div>
          ) : error ? (
            <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm text-center">
              {error}
            </div>
          ) : histories.length === 0 ? (
            <div className="py-12 text-center text-slate-400">
              <DocumentTextIcon className="w-12 h-12 mx-auto mb-2 text-slate-600 stroke-1" />
              <p className="text-sm font-medium">No history recorded yet for this document.</p>
              <p className="text-xs text-slate-500 mt-1">Changes made to this document will be logged here automatically.</p>
            </div>
          ) : (
            <div className="relative border-l-2 border-slate-800 ml-4 pl-6 space-y-6">
              {histories.map((h) => {
                const author = h.user?.name || "System";
                return (
                  <div key={h.id} className="relative group">
                    {/* Timeline bullet dot */}
                    <div className="absolute -left-[31px] top-1 w-3 h-3 rounded-full bg-slate-700 border-2 border-slate-900 group-hover:bg-blue-400 transition-colors" />

                    <div className="p-4 rounded-xl bg-slate-800/40 border border-slate-700/60 hover:border-slate-700 transition-all space-y-2">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="flex items-center gap-2">
                          {getActionBadge(h.action)}
                          {isGlobalMode && (
                            <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-slate-900 border border-slate-700 text-blue-300">
                              {(h.entity_type || "").replace(/_/g, " ")} #{h.entity_id}
                            </span>
                          )}
                          <span className="text-xs font-semibold text-slate-200 flex items-center gap-1">
                            <UserIcon className="w-3.5 h-3.5 text-slate-400" />
                            {author}
                          </span>
                        </div>
                        <span className="text-[11px] text-slate-400 font-mono">
                          {formatDateTime(h.created_at)}
                        </span>
                      </div>

                      {/* Reason */}
                      {h.reason && (
                        <div className="text-xs text-slate-300 bg-slate-900/60 px-3 py-2 rounded-lg border border-slate-800">
                          <span className="font-semibold text-slate-400">Reason: </span>
                          <span>{h.reason}</span>
                        </div>
                      )}

                      {/* Diff details if available */}
                      {h.diff && Object.keys(h.diff).length > 0 && (
                        <div className="mt-2 text-xs text-slate-400 space-y-1 bg-slate-900/40 p-2.5 rounded-lg border border-slate-800/80">
                          {Object.entries(h.diff).map(([field, delta]) => {
                            if (field === "event") return null;
                            if (field === "items") {
                              return (
                                <div key="items" className="text-[11px]">
                                  <span className="font-medium text-slate-300">Items: </span>
                                  <span>{delta.before_count} item(s) → {delta.after_count} item(s)</span>
                                </div>
                              );
                            }
                            if (typeof delta === "object" && delta !== null && "from" in delta && "to" in delta) {
                              return (
                                <div key={field} className="text-[11px]">
                                  <span className="font-medium text-slate-300 capitalize">{field.replace(/_/g, " ")}: </span>
                                  <span className="text-rose-400 line-through mr-1.5">{String(delta.from ?? "none")}</span>
                                  <span className="text-emerald-400 font-medium">→ {String(delta.to ?? "none")}</span>
                                </div>
                              );
                            }
                            return null;
                          })}
                        </div>
                      )}

                      {/* Expandable Before/After Snapshot & Line-Item Inspector */}
                      {(h.before_payload || h.after_payload) && (
                        <div className="pt-1">
                          <button
                            type="button"
                            onClick={() => toggleExpanded(h.id)}
                            className="text-[11px] font-semibold text-blue-400 hover:text-blue-300 underline"
                          >
                            {expandedIds[h.id]
                              ? "Hide Snapshot & Line-Item Details"
                              : "View Snapshot & Line-Item Details"}
                          </button>

                          {expandedIds[h.id] && (
                            <div className="mt-2 space-y-2.5 text-[11px] bg-slate-950/70 p-3 rounded-lg border border-slate-800">
                              {Array.isArray(h.before_payload?.items) && h.before_payload.items.length > 0 && (
                                <div>
                                  <div className="font-semibold text-rose-400 mb-1">
                                    Before Line Items ({h.before_payload.items.length})
                                  </div>
                                  <div className="overflow-x-auto">
                                    <table className="w-full text-left border-collapse">
                                      <thead>
                                        <tr className="border-b border-slate-800 text-slate-400">
                                          <th className="py-1 pr-2">Product</th>
                                          <th className="py-1 pr-2">Batch</th>
                                          <th className="py-1 pr-2 text-right">Qty</th>
                                          <th className="py-1 text-right">Subtotal</th>
                                        </tr>
                                      </thead>
                                      <tbody className="divide-y divide-slate-800/60 text-slate-300">
                                        {h.before_payload.items.map((it, idx) => (
                                          <tr key={it.id || idx}>
                                            <td className="py-1 pr-2">{it.product?.name || `#${it.product_id}`}</td>
                                            <td className="py-1 pr-2 font-mono">{it.batch_number || it.batch || "—"}</td>
                                            <td className="py-1 pr-2 text-right font-mono">
                                              {it.quantity ?? it.unit_return_quantity ?? it.return_unit_quantity ?? 0}
                                            </td>
                                            <td className="py-1 text-right font-mono">
                                              {Number(it.sub_total || 0).toLocaleString()}
                                            </td>
                                          </tr>
                                        ))}
                                      </tbody>
                                    </table>
                                  </div>
                                </div>
                              )}

                              {Array.isArray(h.after_payload?.items) && h.after_payload.items.length > 0 && (
                                <div>
                                  <div className="font-semibold text-emerald-400 mb-1">
                                    {h.action === "created" ? "Recorded Line Items" : "After Line Items"} ({h.after_payload.items.length})
                                  </div>
                                  <div className="overflow-x-auto">
                                    <table className="w-full text-left border-collapse">
                                      <thead>
                                        <tr className="border-b border-slate-800 text-slate-400">
                                          <th className="py-1 pr-2">Product</th>
                                          <th className="py-1 pr-2">Batch</th>
                                          <th className="py-1 pr-2 text-right">Qty</th>
                                          <th className="py-1 text-right">Subtotal</th>
                                        </tr>
                                      </thead>
                                      <tbody className="divide-y divide-slate-800/60 text-slate-300">
                                        {h.after_payload.items.map((it, idx) => (
                                          <tr key={it.id || idx}>
                                            <td className="py-1 pr-2">{it.product?.name || `#${it.product_id}`}</td>
                                            <td className="py-1 pr-2 font-mono">{it.batch_number || it.batch || "—"}</td>
                                            <td className="py-1 pr-2 text-right font-mono">
                                              {it.quantity ?? it.unit_return_quantity ?? it.return_unit_quantity ?? 0}
                                            </td>
                                            <td className="py-1 text-right font-mono">
                                              {Number(it.sub_total || 0).toLocaleString()}
                                            </td>
                                          </tr>
                                        ))}
                                      </tbody>
                                    </table>
                                  </div>
                                </div>
                              )}
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="px-6 py-3 border-t border-slate-800 bg-slate-800/30 flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 text-xs font-semibold rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

