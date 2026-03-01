// src/components/SaleInvoiceSearch.jsx
import React, { useEffect, useRef, useState } from "react";
import axios from "axios";
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  DocumentTextIcon,
  UserIcon,
  CurrencyDollarIcon,
} from "@heroicons/react/24/solid";
import { GlassCard, GlassInput, GlassToolbar } from "@/components/glass";

/* ─────────────── Helpers ─────────────── */
function fmtMoney(v) {
  if (v == null || v === "") return "—";
  const n = Number(v);
  return Number.isNaN(n)
    ? String(v)
    : n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
}

function formatDate(dateStr) {
  if (!dateStr) return "-";
  const date = new Date(dateStr);
  if (isNaN(date.getTime())) return dateStr;
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  return `${date.getDate()} ${months[date.getMonth()]} ${date.getFullYear()}`;
}

/* ─────────────── List Row ─────────────── */
function ResultRow({ invoice, active, onHover, onOpen }) {
  const invTotal = Number(invoice.total ?? 0);
  const received = Number(invoice.total_receive ?? 0);
  const remaining = invoice.remaining ?? Math.max(invTotal - received, 0);
  const isCredit = invoice.invoice_type === 'credit';

  return (
    <li
      onMouseEnter={onHover}
      onClick={onOpen}
      className={[
        "px-4 py-3 transition-all duration-150 cursor-pointer border-b border-slate-100 dark:border-slate-700/50 last:border-0",
        active 
          ? "bg-gradient-to-r from-blue-50 to-indigo-50 dark:from-blue-900/30 dark:to-indigo-900/20" 
          : "hover:bg-slate-50 dark:hover:bg-slate-700/30",
      ].join(" ")}
      title="Open invoice"
      aria-label="Open invoice"
    >
      <div className="flex items-start justify-between">
        {/* Left: Invoice Info */}
        <div className="flex items-start gap-3 flex-1 min-w-0">
          <div className="flex-shrink-0">
            <div className="w-10 h-10 rounded-lg bg-blue-100 dark:bg-blue-900/40 flex items-center justify-center">
              <DocumentTextIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            </div>
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-semibold text-slate-800 dark:text-slate-100 text-sm">
                {invoice.posted_number || "—"}
              </span>
              {isCredit && (
                <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-400">
                  Credit
                </span>
              )}
              {invoice.sale_type === 'wholesale' && (
                <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-purple-100 dark:bg-purple-900/40 text-purple-700 dark:text-purple-400">
                  WS
                </span>
              )}
            </div>
            <div className="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 mt-1">
              <UserIcon className="w-3 h-3 flex-shrink-0" />
              <span className="truncate">{invoice.customer_name || "N/A"}</span>
            </div>
            {(invoice.doctor_name || invoice.patient_name) && (
              <div className="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                <span className="truncate">
                  {invoice.doctor_name && <span>Dr: {invoice.doctor_name}</span>}
                  {invoice.doctor_name && invoice.patient_name && <span className="mx-1">|</span>}
                  {invoice.patient_name && <span>Pt: {invoice.patient_name}</span>}
                </span>
              </div>
            )}
            {invoice.remarks && (
              <div className="text-[10px] text-slate-400 dark:text-slate-500 mt-0.5 truncate max-w-[250px]">
                {invoice.remarks}
              </div>
            )}
            <div className="text-[10px] text-slate-400 dark:text-slate-500 mt-0.5">
              {formatDate(invoice.date)}
            </div>
          </div>
        </div>

        {/* Right: Amounts */}
        <div className="flex-shrink-0 text-right">
          <div className="flex items-center gap-1 justify-end">
            <CurrencyDollarIcon className="w-4 h-4 text-emerald-500" />
            <span className="font-bold text-slate-800 dark:text-slate-100">
              {fmtMoney(invTotal)}
            </span>
          </div>
          {remaining > 0 && (
            <div className="text-[10px] text-amber-600 dark:text-amber-400 mt-0.5">
              Due: {fmtMoney(remaining)}
            </div>
          )}
        </div>
      </div>
    </li>
  );
}

/* ─────────────── Main Component ─────────────── */
export default function SaleInvoiceSearch({ isOpen, onClose, onSelect }) {
  const [q, setQ] = useState("");
  const [loading, setLoading] = useState(false);
  const [results, setResults] = useState([]);
  const [activeIdx, setActiveIdx] = useState(-1);

  const inputRef = useRef(null);
  const boxRef = useRef(null);
  const abortRef = useRef(null);
  const debounceRef = useRef(null);

  // Focus input when modal opens
  useEffect(() => {
    if (isOpen && inputRef.current) {
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

  // Fetch recent invoices when modal opens
  useEffect(() => {
    if (!isOpen) return;
    
    const fetchRecentInvoices = async () => {
      if (abortRef.current) abortRef.current.abort();
      abortRef.current = new AbortController();
      const signal = abortRef.current.signal;

      setLoading(true);
      try {
        // Fetch recent invoices without search query
        const res = await axios.get("/api/sale-invoices/search", {
          params: { q: '' },
          signal,
        });
        const arr = Array.isArray(res.data) ? res.data : [];
        setResults(arr);
        setActiveIdx(arr.length ? 0 : -1);
      } catch (err) {
        if (!axios.isCancel(err)) console.error(err);
      } finally {
        setLoading(false);
      }
    };

    fetchRecentInvoices();
  }, [isOpen]);

  // Close on Escape
  useEffect(() => {
    if (!isOpen) return;
    
    const onEsc = (e) => {
      if (e.key === "Escape") {
        handleClose();
      }
    };
    window.addEventListener("keydown", onEsc);
    return () => window.removeEventListener("keydown", onEsc);
  }, [isOpen]);

  // Debounced search
  useEffect(() => {
    if (!isOpen) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);

    debounceRef.current = setTimeout(async () => {
      if (abortRef.current) abortRef.current.abort();
      abortRef.current = new AbortController();
      const signal = abortRef.current.signal;

      const term = q.trim();
      
      setLoading(true);
      try {
        const res = await axios.get("/api/sale-invoices/search", {
          params: { q: term },
          signal,
        });
        const arr = Array.isArray(res.data) ? res.data : [];
        setResults(arr);
        setActiveIdx(arr.length ? 0 : -1);
      } catch (err) {
        if (!axios.isCancel(err)) console.error(err);
      } finally {
        setLoading(false);
      }
    }, 250);

    return () => clearTimeout(debounceRef.current);
  }, [q, isOpen]);

  // Keyboard navigation
  const handleKeyDown = (e) => {
    if (!isOpen) return;
    
    if (["ArrowDown", "ArrowUp", "Enter", "Escape", "Tab"].includes(e.key)) {
      e.preventDefault();
    }

    if (e.key === "ArrowDown") {
      setActiveIdx((i) => Math.min((results.length || 1) - 1, i + 1));
    } else if (e.key === "ArrowUp") {
      setActiveIdx((i) => Math.max(0, i - 1));
    } else if (e.key === "Enter") {
      if (results[activeIdx]) {
        handleSelect(results[activeIdx]);
      }
    } else if (e.key === "Escape") {
      handleClose();
    }
  };

  const handleSelect = (invoice) => {
    if (onSelect) {
      onSelect(invoice);
    }
    handleClose();
  };

  const handleClose = () => {
    setQ("");
    setResults([]);
    setActiveIdx(-1);
    if (onClose) onClose();
  };

  if (!isOpen) return null;

  return (
    <div 
      className="fixed inset-0 z-[100] flex items-start justify-center pt-[10vh] bg-black/50"
      onClick={(e) => {
        if (e.target === e.currentTarget) handleClose();
      }}
    >
      <div 
        ref={boxRef}
        className="w-full max-w-2xl mx-4"
      >
        <GlassCard className="overflow-hidden bg-white dark:bg-slate-800 shadow-2xl ring-1 ring-slate-200/50 dark:ring-slate-700/50">
          {/* Search Bar */}
          <div className="flex items-center gap-3 px-4 py-3 bg-slate-50 dark:bg-slate-800 border-b border-slate-200 dark:border-slate-700">
            <MagnifyingGlassIcon className="w-5 h-5 text-slate-400 flex-shrink-0" />
            <input
              ref={inputRef}
              type="text"
              value={q}
              onChange={(e) => setQ(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Search by invoice # or customer name..."
              className="flex-1 bg-transparent border-0 outline-none placeholder:text-slate-400 dark:placeholder:text-slate-500 text-sm text-slate-900 dark:text-slate-100"
              autoFocus
            />
            {loading ? (
              <ArrowPathIcon className="w-5 h-5 animate-spin text-slate-400 flex-shrink-0" />
            ) : q ? (
              <button
                onClick={() => {
                  setQ("");
                  setResults([]);
                  setActiveIdx(-1);
                }}
                className="p-1 rounded hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors flex-shrink-0"
                title="Clear search"
              >
                <svg className="w-4 h-4 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            ) : null}
            <kbd className="text-[10px] border border-slate-200 dark:border-slate-600 rounded px-1.5 py-0.5 bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400 font-mono flex-shrink-0">
              Esc
            </kbd>
          </div>

          {/* Results */}
          <div className="max-h-[60vh] overflow-y-auto">
            {loading && (
              <div className="flex items-center justify-center py-8">
                <div className="flex flex-col items-center gap-2">
                  <ArrowPathIcon className="w-6 h-6 animate-spin text-blue-500" />
                  <span className="text-xs text-slate-500">Searching...</span>
                </div>
              </div>
            )}

            {!loading && q && results.length === 0 && (
              <div className="flex flex-col items-center justify-center py-8 text-slate-400">
                <DocumentTextIcon className="w-8 h-8 mb-2" />
                <span className="text-sm">No invoices found</span>
              </div>
            )}

            {!loading && !q && results.length === 0 && (
              <div className="flex flex-col items-center justify-center py-8 text-slate-400">
                <MagnifyingGlassIcon className="w-8 h-8 mb-2" />
                <span className="text-sm">No recent invoices</span>
              </div>
            )}

            {results.length > 0 && (
              <ul role="list" className="divide-y divide-slate-100 dark:divide-slate-700/50">
                {results.map((invoice, idx) => (
                  <ResultRow
                    key={invoice.id}
                    invoice={invoice}
                    active={idx === activeIdx}
                    onHover={() => setActiveIdx(idx)}
                    onOpen={() => handleSelect(invoice)}
                  />
                ))}
              </ul>
            )}
          </div>

          {/* Footer */}
          <GlassToolbar className="items-center justify-between py-2 px-4 bg-slate-50/80 dark:bg-slate-800/80 border-t border-slate-100 dark:border-slate-700">
            <div className="text-xs text-slate-500 dark:text-slate-400">
              {q ? (
                <>
                  <span className="font-bold">{results.length}</span> results for <span className="font-medium">"{q}"</span>
                </>
              ) : (
                <>Recent invoices</>
              )}
            </div>
            <div className="flex items-center gap-3 text-[10px] text-slate-400">
              <span className="flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-600 text-[9px]">↑↓</kbd>
                <span>Navigate</span>
              </span>
              <span className="flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-600 text-[9px]">↵</kbd>
                <span>Open</span>
              </span>
              <span className="flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-600 text-[9px]">Esc</kbd>
                <span>Close</span>
              </span>
            </div>
          </GlassToolbar>
        </GlassCard>
      </div>
    </div>
  );
}

