import React, { useState, useEffect, useRef } from "react";
import {
  ExclamationTriangleIcon,
  XMarkIcon,
  ArrowPathIcon,
} from "@heroicons/react/24/outline";

export default function VoidInvoiceModal({
  isOpen,
  onClose,
  invoiceNumber,
  onConfirm,
  isReversing = false,
}) {
  const [reason, setReason] = useState("");
  const inputRef = useRef(null);
  const mouseDownTargetRef = useRef(null);

  useEffect(() => {
    if (isOpen) {
      setReason("");
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e) => {
      if (e.key === "Escape" && !isReversing) {
        e.preventDefault();
        onClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, isReversing, onClose]);

  if (!isOpen) return null;

  const handleMouseDown = (e) => {
    mouseDownTargetRef.current = e.target;
  };

  const handleBackdropClick = (e) => {
    if (e.target === e.currentTarget && mouseDownTargetRef.current === e.currentTarget && !isReversing) {
      onClose();
    }
  };

  const handleSubmit = (e) => {
    e.preventDefault();
    const trimmed = reason.trim();
    if (!trimmed || trimmed.length < 3) return;
    onConfirm(trimmed);
  };

  return (
    <div
      onMouseDown={handleMouseDown}
      onClick={handleBackdropClick}
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm animate-fadeIn cursor-default"
      role="dialog"
      aria-modal="true"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="relative w-full max-w-lg flex flex-col bg-slate-900 border border-rose-500/30 rounded-2xl shadow-2xl overflow-hidden text-slate-100"
      >
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-800 bg-rose-500/10">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-xl bg-rose-500/20 text-rose-400 border border-rose-500/30">
              <ExclamationTriangleIcon className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-base font-bold text-white">
                Void &amp; Reverse Invoice
              </h2>
              <p className="text-xs text-rose-300">
                {invoiceNumber ? `Invoice #${invoiceNumber}` : "Posted Document Reversal"}
              </p>
            </div>
          </div>
          <button
            type="button"
            disabled={isReversing}
            onClick={onClose}
            className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors disabled:opacity-50"
          >
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        {/* Form Body */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          <div className="p-3.5 rounded-xl bg-slate-800/60 border border-slate-700/80 text-xs text-slate-300 space-y-1.5">
            <p className="font-semibold text-slate-200">Reversal Effects:</p>
            <ul className="list-disc list-inside space-y-1 text-slate-400">
              <li>Inventory items and batches will be safely restored/reversed.</li>
              <li>Party ledger balances will be adjusted to neutralize obligations.</li>
              <li>The invoice will be marked as <strong className="text-rose-400">Reversed</strong> permanently.</li>
              <li>An auditable history entry will be saved with your reason.</li>
            </ul>
          </div>

          <div className="space-y-1.5">
            <label htmlFor="reversal-reason-input" className="block text-xs font-semibold text-slate-300">
              Reason for Reversal <span className="text-rose-400">*</span>
            </label>
            <textarea
              id="reversal-reason-input"
              ref={inputRef}
              rows={3}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              disabled={isReversing}
              placeholder="e.g. Customer cancelled order at counter, duplicate invoice entered by mistake, etc."
              className="w-full px-3 py-2 text-xs rounded-xl bg-slate-800 border border-slate-700 text-white placeholder-slate-500 focus:outline-none focus:border-rose-500 focus:ring-1 focus:ring-rose-500 disabled:opacity-50 resize-none"
              required
            />
            {reason.trim().length > 0 && reason.trim().length < 3 && (
              <p className="text-[11px] text-rose-400">Reason must be at least 3 characters.</p>
            )}
          </div>

          {/* Action buttons */}
          <div className="pt-2 flex items-center justify-end gap-3">
            <button
              type="button"
              disabled={isReversing}
              onClick={onClose}
              className="px-4 py-2 text-xs font-semibold rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 transition-colors disabled:opacity-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isReversing || reason.trim().length < 3}
              className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-lg bg-rose-600 hover:bg-rose-500 text-white transition-colors shadow-sm disabled:opacity-50"
            >
              {isReversing && <ArrowPathIcon className="w-3.5 h-3.5 animate-spin" />}
              <span>{isReversing ? "Reversing…" : "Confirm Reversal"}</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

