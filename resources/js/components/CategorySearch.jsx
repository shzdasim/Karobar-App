// resources/js/components/CategorySearch.jsx
import React, { useEffect, useRef, useState, useCallback } from "react";
import axios from "axios";
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  Squares2X2Icon,
} from "@heroicons/react/24/solid";
import { GlassCard, GlassToolbar } from "@/components/glass";

/* ─────────────── List Row ─────────────── */
function ResultRow({ category, active, onHover, onOpen, rowRef }) {
  return (
    <li
      ref={rowRef}
      onMouseEnter={onHover}
      onClick={onOpen}
      className={[
        "px-4 py-3 transition-all duration-150 cursor-pointer border-b border-slate-100 dark:border-slate-700/50 last:border-0",
        active
          ? "bg-linear-to-r from-amber-50 to-orange-50 dark:from-amber-900/30 dark:to-orange-900/20"
          : "hover:bg-slate-50 dark:hover:bg-slate-700/30",
      ].join(" ")}
      title="Select category"
      aria-label="Select category"
    >
      <div className="flex items-center gap-3 flex-1 min-w-0">
        <div className="shrink-0">
          <div className="w-10 h-10 rounded-lg bg-amber-100 dark:bg-amber-900/40 flex items-center justify-center">
            <Squares2X2Icon className="w-5 h-5 text-amber-600 dark:text-amber-400" />
          </div>
        </div>
        <div className="flex-1 min-w-0">
          <span className="font-semibold text-slate-800 dark:text-slate-100 text-sm block truncate">
            {category.name || "—"}
          </span>
          {category.products_count != null && (
            <span className="text-xs text-slate-500 dark:text-slate-400 mt-0.5 block">
              {category.products_count} product(s)
            </span>
          )}
        </div>
      </div>
    </li>
  );
}

/* ─────────────── Main Component ─────────────── */
export default function CategorySearch({ isOpen, onClose, onSelect }) {
  const [q, setQ] = useState("");
  const [loading, setLoading] = useState(false);
  const [results, setResults] = useState([]);
  const [activeIdx, setActiveIdx] = useState(-1);
  const [lastSearchTerm, setLastSearchTerm] = useState("");

  const inputRef = useRef(null);
  const listRef = useRef(null);
  const rowRefs = useRef([]);
  const abortRef = useRef(null);
  const debounceRef = useRef(null);

  // Clear refs when results change
  useEffect(() => {
    rowRefs.current = [];
  }, [results]);

  // Focus input when modal opens
  useEffect(() => {
    if (isOpen && inputRef.current) {
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

  // Reset state when modal opens
  useEffect(() => {
    if (!isOpen) return;
    setResults([]);
    setActiveIdx(-1);
    setQ("");
    setLastSearchTerm("");
  }, [isOpen]);

  // Fetch categories
  const fetchCategories = useCallback(async (searchTerm) => {
    if (abortRef.current) abortRef.current.abort();
    abortRef.current = new AbortController();
    const signal = abortRef.current.signal;

    setLoading(true);
    try {
      const res = await axios.get("/api/categories/search", {
        params: { q: searchTerm, limit: 50 },
        signal,
      });
      const data = Array.isArray(res.data) ? res.data : Array.isArray(res.data?.data) ? res.data.data : [];
      setResults(data);
      setActiveIdx(data.length ? 0 : -1);
    } catch (err) {
      if (!axios.isCancel(err)) console.error(err);
      setResults([]);
      setActiveIdx(-1);
    } finally {
      setLoading(false);
    }
  }, []);

  // Fetch recent categories when modal opens
  useEffect(() => {
    if (!isOpen) return;
    const term = q.trim();
    setLastSearchTerm(term);
    fetchCategories(term);
  }, [isOpen, fetchCategories]);

  // Close on Escape
  useEffect(() => {
    if (!isOpen) return;
    const onEsc = (e) => {
      if (e.key === "Escape") handleClose();
    };
    window.addEventListener("keydown", onEsc);
    return () => window.removeEventListener("keydown", onEsc);
  }, [isOpen]);

  // Debounced search
  useEffect(() => {
    if (!isOpen) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);

    debounceRef.current = setTimeout(() => {
      const term = q.trim();
      if (term !== lastSearchTerm) {
        setLastSearchTerm(term);
        fetchCategories(term);
      }
    }, 250);

    return () => clearTimeout(debounceRef.current);
  }, [q, isOpen, fetchCategories, lastSearchTerm]);

  // Auto-scroll to active item
  useEffect(() => {
    if (activeIdx < 0 || !listRef.current) return;
    const activeRow = rowRefs.current[activeIdx];
    if (!activeRow) return;
    activeRow.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [activeIdx]);

  // Keyboard navigation
  const handleKeyDown = (e) => {
    if (!isOpen) return;
    if (["ArrowDown", "ArrowUp", "Enter", "Escape"].includes(e.key)) {
      e.preventDefault();
    }
    if (e.key === "ArrowDown") {
      setActiveIdx((i) => Math.min(results.length - 1, i + 1));
    } else if (e.key === "ArrowUp") {
      setActiveIdx((i) => Math.max(0, i - 1));
    } else if (e.key === "Enter") {
      if (results[activeIdx]) handleSelect(results[activeIdx]);
    } else if (e.key === "Escape") {
      handleClose();
    }
  };

  const handleSelect = (category) => {
    if (onSelect) onSelect(category);
    handleClose();
  };

  const handleClose = () => {
    setQ("");
    setResults([]);
    setActiveIdx(-1);
    setLastSearchTerm("");
    if (onClose) onClose();
  };

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-100 flex items-start justify-center pt-[10vh] bg-black/50"
      onClick={(e) => {
        if (e.target === e.currentTarget) handleClose();
      }}
    >
      <div className="w-full max-w-md mx-4">
        <GlassCard className="overflow-hidden bg-white dark:bg-slate-800 shadow-2xl ring-1 ring-slate-200/50 dark:ring-slate-700/50">
          {/* Search Bar */}
          <div className="flex items-center gap-3 px-4 py-3 bg-slate-50 dark:bg-slate-800 border-b border-slate-200 dark:border-slate-700">
            <MagnifyingGlassIcon className="w-5 h-5 text-slate-400 shrink-0" />
            <input
              ref={inputRef}
              type="text"
              value={q}
              onChange={(e) => setQ(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Search by category name..."
              className="flex-1 bg-transparent border-0 outline-hidden placeholder:text-slate-400 dark:placeholder:text-slate-500 text-sm text-slate-900 dark:text-slate-100"
              autoFocus
            />
            {loading ? (
              <ArrowPathIcon className="w-5 h-5 animate-spin text-slate-400 shrink-0" />
            ) : q ? (
              <button
                onClick={() => {
                  setQ("");
                  setResults([]);
                  setActiveIdx(-1);
                }}
                className="p-1 rounded-sm hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors shrink-0"
                title="Clear search"
              >
                <svg className="w-4 h-4 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            ) : null}
            <kbd className="text-[10px] border border-slate-200 dark:border-slate-600 rounded-sm px-1.5 py-0.5 bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400 font-mono shrink-0">
              Esc
            </kbd>
          </div>

          {/* Results */}
          <div ref={listRef} className="max-h-[60vh] overflow-y-auto">
            {loading && (
              <div className="flex items-center justify-center py-8">
                <div className="flex flex-col items-center gap-2">
                  <ArrowPathIcon className="w-6 h-6 animate-spin text-amber-500" />
                  <span className="text-xs text-slate-500">Searching...</span>
                </div>
              </div>
            )}

            {!loading && q && results.length === 0 && (
              <div className="flex flex-col items-center justify-center py-8 text-slate-400">
                <Squares2X2Icon className="w-8 h-8 mb-2" />
                <span className="text-sm">No categories found</span>
              </div>
            )}

            {!loading && !q && results.length === 0 && (
              <div className="flex flex-col items-center justify-center py-8 text-slate-400">
                <MagnifyingGlassIcon className="w-8 h-8 mb-2" />
                <span className="text-sm">No recent categories</span>
              </div>
            )}

            {results.length > 0 && (
              <ul role="list" className="divide-y divide-slate-100 dark:divide-slate-700/50">
                {results.map((category, idx) => (
                  <ResultRow
                    key={category.id}
                    category={category}
                    active={idx === activeIdx}
                    onHover={() => setActiveIdx(idx)}
                    onOpen={() => handleSelect(category)}
                    rowRef={(el) => (rowRefs.current[idx] = el)}
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
                <>All categories</>
              )}
            </div>
            <div className="flex items-center gap-3 text-[10px] text-slate-400">
              <span className="flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 rounded-sm border border-slate-200 dark:border-slate-600 text-[9px]">↑↓</kbd>
                <span>Navigate</span>
              </span>
              <span className="flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 rounded-sm border border-slate-200 dark:border-slate-600 text-[9px]">↵</kbd>
                <span>Select</span>
              </span>
              <span className="flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 rounded-sm border border-slate-200 dark:border-slate-600 text-[9px]">Esc</kbd>
                <span>Close</span>
              </span>
            </div>
          </GlassToolbar>
        </GlassCard>
      </div>
    </div>
  );
}
