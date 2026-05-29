// src/components/BankSearch.jsx
import React, { useEffect, useMemo, useRef, useState, useCallback } from "react";
import axios from "axios";
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  BuildingOfficeIcon,
  BanknotesIcon,
} from "@heroicons/react/24/solid";
import { GlassCard, GlassToolbar } from "@/components/glass";

function fmtAccount(acc) {
  if (acc == null || acc === "") return "—";
  return String(acc);
}

function ResultRow({ bank, active, onOpen, rowRef }) {
  return (
    <li
      ref={rowRef}
      onClick={() => onOpen(bank)}
      className={[
        "px-4 py-3 transition-all duration-150 cursor-pointer border-b border-slate-100 dark:border-slate-700/50 last:border-0",
        active
          ? "bg-gradient-to-r from-blue-50 to-indigo-50 dark:from-blue-900/30 dark:to-indigo-900/20"
          : "hover:bg-slate-50 dark:hover:bg-slate-700/30",
      ].join(" ")}
      title="Select bank"
      aria-label={`Select bank ${bank.bank_name}`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-start gap-3 flex-1 min-w-0">
          <div className="w-10 h-10 rounded-lg bg-blue-100 dark:bg-blue-900/40 flex items-center justify-center flex-shrink-0">
            <BuildingOfficeIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-semibold text-slate-800 dark:text-slate-100 text-sm truncate">
                {bank.bank_name || "N/A"}
              </span>
            </div>
            <div className="flex items-center gap-1 text-xs text-slate-500 dark:text-slate-400 mt-1">
              <BanknotesIcon className="w-3 h-3 flex-shrink-0" />
              <span className="truncate">Account: {fmtAccount(bank.account_number)}</span>
            </div>
          </div>
        </div>
      </div>
    </li>
  );
}

/**
 * BankSearch modal (similar style to invoice search).
 * Uses existing backend endpoint: GET /api/banks?q=&...
 */
export default function BankSearch({ isOpen, onClose, onSelect }) {
  const [q, setQ] = useState("");
  const [loading, setLoading] = useState(false);
  const [results, setResults] = useState([]);
  const [activeIdx, setActiveIdx] = useState(-1);

  const inputRef = useRef(null);
  const rowRefs = useRef([]);
  const lastTermRef = useRef("");
  const debounceRef = useRef(null);

  const fetchBanks = useCallback(async (term) => {
    const searchTerm = (term ?? "").trim();
    setLoading(true);

    try {
      const res = await axios.get("/api/banks", {
        params: {
          q: searchTerm,
        },
      });

      const payload = res.data;
      const list = Array.isArray(payload?.data)
        ? payload.data
        : Array.isArray(payload)
          ? payload
          : [];

      setResults(list);
      setActiveIdx(list.length ? 0 : -1);
      lastTermRef.current = searchTerm;
    } catch (e) {
      console.error(e);
      setResults([]);
      setActiveIdx(-1);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!isOpen) return;
    setQ("");
    setResults([]);
    setActiveIdx(-1);
    lastTermRef.current = "";

    setTimeout(() => {
      inputRef.current?.focus?.();
    }, 50);
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;

    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      const term = q.trim();
      // Avoid re-fetching identical term if we already have results
      if (term === lastTermRef.current && results.length) return;
      fetchBanks(term);
    }, 250);

    return () => clearTimeout(debounceRef.current);
  }, [q, isOpen, fetchBanks, results.length]);

  useEffect(() => {
    if (!isOpen) return;

    const onKeyDown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose?.();
      }

      if (["ArrowDown", "ArrowUp", "Enter"].includes(e.key)) {
        e.preventDefault();
      }

      if (e.key === "ArrowDown") {
        setActiveIdx((i) => Math.min(results.length - 1, i + 1));
      } else if (e.key === "ArrowUp") {
        setActiveIdx((i) => Math.max(0, i - 1));
      } else if (e.key === "Enter") {
        if (results[activeIdx]) {
          onSelect?.(results[activeIdx]);
          onClose?.();
        }
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isOpen, results, activeIdx, onClose, onSelect]);

  const canShowEmpty = useMemo(() => q.trim().length > 0, [q]);

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-[100] flex items-start justify-center pt-[10vh] bg-black/50"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose?.();
      }}
    >
      <div className="w-full max-w-2xl mx-4">
        <GlassCard className="overflow-hidden bg-white dark:bg-slate-800 shadow-2xl ring-1 ring-slate-200/50 dark:ring-slate-700/50">
          <div className="flex items-center gap-3 px-4 py-3 bg-slate-50 dark:bg-slate-800 border-b border-slate-200 dark:border-slate-700">
            <MagnifyingGlassIcon className="w-5 h-5 text-slate-400 flex-shrink-0" />
            <input
              ref={inputRef}
              type="text"
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Search by bank name or account..."
              className="flex-1 bg-transparent border-0 outline-none placeholder:text-slate-400 dark:placeholder:text-slate-500 text-sm text-slate-900 dark:text-slate-100"
              autoFocus
            />
            {loading ? (
              <ArrowPathIcon className="w-5 h-5 animate-spin text-slate-400 flex-shrink-0" />
            ) : q ? (
              <button
                type="button"
                className="p-1 rounded hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors flex-shrink-0"
                title="Clear search"
                onClick={() => {
                  setQ("");
                  setResults([]);
                  setActiveIdx(-1);
                }}
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

          <div className="max-h-[55vh] overflow-y-auto">
            {loading && (
              <div className="flex items-center justify-center py-10">
                <div className="flex flex-col items-center gap-2">
                  <ArrowPathIcon className="w-6 h-6 animate-spin text-blue-500" />
                  <span className="text-xs text-slate-500">Searching...</span>
                </div>
              </div>
            )}

            {!loading && canShowEmpty && results.length === 0 && (
              <div className="flex flex-col items-center justify-center py-10 text-slate-400">
                <MagnifyingGlassIcon className="w-8 h-8 mb-2" />
                <span className="text-sm">No banks found</span>
              </div>
            )}

            {!loading && q.trim().length === 0 && results.length === 0 && (
              <div className="flex flex-col items-center justify-center py-10 text-slate-400">
                <BuildingOfficeIcon className="w-8 h-8 mb-2" />
                <span className="text-sm">Start typing to search</span>
              </div>
            )}

            {results.length > 0 && (
              <ul className="divide-y divide-slate-100 dark:divide-slate-700/50">
                {results.map((bank, idx) => (
                  <ResultRow
                    key={bank.id}
                    bank={bank}
                    active={idx === activeIdx}
                    onOpen={(b) => onSelect?.(b)}
                    rowRef={(el) => {
                      rowRefs.current[idx] = el;
                    }}
                  />
                ))}
              </ul>
            )}
          </div>

          <GlassToolbar className="items-center justify-between py-2 px-4 bg-slate-50/80 dark:bg-slate-800/80 border-t border-slate-100 dark:border-slate-700">
            <div className="text-xs text-slate-500 dark:text-slate-400">
              {q.trim() ? (
                <>
                  <span className="font-bold">{results.length}</span> results
                </>
              ) : (
                <>Pick a bank</>
              )}
            </div>
            <div className="flex items-center gap-3 text-[10px] text-slate-400">
              <span className="flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-600 text-[9px]">
                  ↑↓
                </kbd>
                Navigate
              </span>
              <span className="flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-600 text-[9px]">
                  ↵
                </kbd>
                Select
              </span>
            </div>
          </GlassToolbar>
        </GlassCard>
      </div>
    </div>
  );
}

