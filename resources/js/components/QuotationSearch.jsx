import React, { useEffect, useRef, useState, useCallback } from "react";
import axios from "axios";
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  DocumentTextIcon,
  UserIcon,
  CurrencyDollarIcon,
  CalendarIcon,
} from "@heroicons/react/24/solid";
import { GlassCard, GlassInput, GlassToolbar } from "@/components/glass";

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

function ResultRow({ quotation, active, onHover, onOpen, rowRef }) {
  const total = Number(quotation.total ?? 0);

  return (
    <li
      ref={rowRef}
      onMouseEnter={onHover}
      onClick={onOpen}
      className={[
        "px-4 py-3 transition-all duration-150 cursor-pointer border-b border-slate-100 dark:border-slate-700/50 last:border-0",
        active
          ? "bg-gradient-to-r from-blue-50 to-indigo-50 dark:from-blue-900/30 dark:to-indigo-900/20"
          : "hover:bg-slate-50 dark:hover:bg-slate-700/30",
      ].join(" ")}
      title="Open quotation"
      aria-label="Open quotation"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-start gap-3 flex-1 min-w-0">
          <div className="flex-shrink-0">
            <div className="w-10 h-10 rounded-lg bg-blue-100 dark:bg-blue-900/40 flex items-center justify-center">
              <DocumentTextIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            </div>
          </div>

          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-semibold text-slate-800 dark:text-slate-100 text-sm">
                {quotation.posted_number || "—"}
              </span>
              <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-slate-100 dark:bg-slate-700/30 text-slate-700 dark:text-slate-200">
                QTN
              </span>
            </div>

            <div className="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 mt-1">
              <UserIcon className="w-3 h-3 flex-shrink-0" />
              <span className="truncate">{quotation.customer?.name ?? quotation.customer_name ?? "N/A"}</span>
            </div>

            <div className="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 mt-0.5">
              <CalendarIcon className="w-3 h-3 flex-shrink-0" />
              <span className="truncate">{formatDate(quotation.date)}</span>
            </div>

            {quotation.remarks && (
              <div className="text-[10px] text-slate-400 dark:text-slate-500 mt-0.5 truncate max-w-[250px]">
                {quotation.remarks}
              </div>
            )}
          </div>
        </div>

        <div className="flex-shrink-0 text-right">
          <div className="flex items-center gap-1 justify-end">
            <CurrencyDollarIcon className="w-4 h-4 text-emerald-500" />
            <span className="font-bold text-slate-800 dark:text-slate-100">{fmtMoney(total)}</span>
          </div>
        </div>
      </div>
    </li>
  );
}

export default function QuotationSearch({ isOpen, onClose, onSelect }) {
  const [q, setQ] = useState("");
  const [loading, setLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [results, setResults] = useState([]);
  const [activeIdx, setActiveIdx] = useState(-1);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(true);
  const [lastSearchTerm, setLastSearchTerm] = useState("");

  const inputRef = useRef(null);
  const listRef = useRef(null);
  const boxRef = useRef(null);
  const rowRefs = useRef([]);
  const abortRef = useRef(null);
  const debounceRef = useRef(null);

  useEffect(() => {
    rowRefs.current = [];
  }, [results]);

  useEffect(() => {
    if (isOpen && inputRef.current) {
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;
    setPage(1);
    setHasMore(true);
    setResults([]);
    setActiveIdx(-1);
    setQ("");
    setLastSearchTerm("");
  }, [isOpen]);

  const fetchQuotations = useCallback(async (searchTerm, pageNum = 1, isLoadMore = false) => {
    if (abortRef.current) abortRef.current.abort();
    abortRef.current = new AbortController();
    const signal = abortRef.current.signal;

    if (isLoadMore) setLoadingMore(true);
    else setLoading(true);

    try {
      const res = await axios.get("/api/quotations", {
        params: { posted: searchTerm, customer: searchTerm, page: pageNum, per_page: 20 },
        signal,
      });


      const paginationData = res.data;
      const newResults = Array.isArray(paginationData?.data)
        ? paginationData.data
        : Array.isArray(paginationData)
          ? paginationData
          : [];

      const totalPages = paginationData?.last_page ?? 1;
      const hasMoreData = pageNum < totalPages;

      if (isLoadMore) setResults((prev) => [...prev, ...newResults]);
      else {
        setResults(newResults);
        setActiveIdx(newResults.length ? 0 : -1);
      }

      setHasMore(hasMoreData);
      setPage(pageNum);
    } catch (err) {
      if (!axios.isCancel?.(err)) console.error(err);
    } finally {
      if (isLoadMore) setLoadingMore(false);
      else setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!isOpen) return;
    const term = q.trim();
    setLastSearchTerm(term);
    fetchQuotations(term, 1, false);
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;

    const onEsc = (e) => {
      if (e.key === "Escape") handleClose();
    };
    window.addEventListener("keydown", onEsc);
    return () => window.removeEventListener("keydown", onEsc);
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;

    if (debounceRef.current) clearTimeout(debounceRef.current);

    debounceRef.current = setTimeout(() => {
      const term = q.trim();
      if (term === lastSearchTerm) return;

      setLastSearchTerm(term);
      setPage(1);
      setHasMore(true);
      fetchQuotations(term, 1, false);
    }, 250);

    return () => clearTimeout(debounceRef.current);
  }, [q, isOpen, fetchQuotations, lastSearchTerm]);

  const handleScroll = useCallback(() => {
    if (!listRef.current || loadingMore || loading || !hasMore) return;

    const { scrollTop, scrollHeight, clientHeight } = listRef.current;
    if (scrollHeight - scrollTop - clientHeight < 100) {
      const nextPage = page + 1;
      fetchQuotations(lastSearchTerm, nextPage, true);
    }
  }, [loadingMore, loading, hasMore, page, lastSearchTerm, fetchQuotations]);

  useEffect(() => {
    if (!listRef.current) return;
    const el = listRef.current;
    return () => {};
  }, []);

  const handleClose = () => {
    setQ("");
    setResults([]);
    setActiveIdx(-1);
    setPage(1);
    setHasMore(true);
    setLastSearchTerm("");
    if (onClose) onClose();
  };

  const handleSelect = (quotation) => {
    if (onSelect) onSelect(quotation);
    handleClose();
  };

  const handleKeyDown = (e) => {
    if (!isOpen) return;

    if (["ArrowDown", "ArrowUp", "Enter", "Escape", "Tab"].includes(e.key)) {
      e.preventDefault();
    }

    if (e.key === "ArrowDown") {
      const newIdx = Math.min((results.length || 1) - 1, activeIdx + 1);
      setActiveIdx(newIdx);

      if (newIdx >= results.length - 2 && hasMore && !loadingMore) {
        const nextPage = page + 1;
        fetchQuotations(lastSearchTerm, nextPage, true);
      }
    } else if (e.key === "ArrowUp") {
      setActiveIdx((i) => Math.max(0, i - 1));
    } else if (e.key === "Enter") {
      if (results[activeIdx]) handleSelect(results[activeIdx]);
    } else if (e.key === "Escape") {
      handleClose();
    }
  };

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-[100] flex items-start justify-center pt-[10vh] bg-black/50"
      onClick={(e) => {
        if (e.target === e.currentTarget) handleClose();
      }}
    >
      <div ref={boxRef} className="w-full max-w-2xl mx-4">
        <GlassCard className="overflow-hidden bg-white dark:bg-slate-800 shadow-2xl ring-1 ring-slate-200/50 dark:ring-slate-700/50">
          <div className="flex items-center gap-3 px-4 py-3 bg-slate-50 dark:bg-slate-800 border-b border-slate-200 dark:border-slate-700">
            <MagnifyingGlassIcon className="w-5 h-5 text-slate-400 flex-shrink-0" />
            <input
              ref={inputRef}
              type="text"
              value={q}
              onChange={(e) => setQ(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Search by Posted # or Customer name..."
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
                  setPage(1);
                  setHasMore(true);
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

          <div ref={listRef} className="max-h-[60vh] overflow-y-auto" onScroll={handleScroll}>
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
                <span className="text-sm">No quotations found</span>
              </div>
            )}

            {!loading && !q && results.length === 0 && (
              <div className="flex flex-col items-center justify-center py-8 text-slate-400">
                <MagnifyingGlassIcon className="w-8 h-8 mb-2" />
                <span className="text-sm">No recent quotations</span>
              </div>
            )}

            {results.length > 0 && (
              <ul role="list" className="divide-y divide-slate-100 dark:divide-slate-700/50">
                {results.map((quotation, idx) => (
                  <ResultRow
                    key={quotation.id}
                    quotation={quotation}
                    active={idx === activeIdx}
                    onHover={() => setActiveIdx(idx)}
                    onOpen={() => handleSelect(quotation)}
                    rowRef={(el) => (rowRefs.current[idx] = el)}
                  />
                ))}
              </ul>
            )}

            {loadingMore && (
              <div className="flex items-center justify-center py-4">
                <ArrowPathIcon className="w-5 h-5 animate-spin text-blue-500" />
                <span className="text-xs text-slate-500 ml-2">Loading more...</span>
              </div>
            )}
          </div>

          <GlassToolbar className="items-center justify-between py-2 px-4 bg-slate-50/80 dark:bg-slate-800/80 border-t border-slate-100 dark:border-slate-700">
            <div className="text-xs text-slate-500 dark:text-slate-400">
              {q ? (
                <>
                  <span className="font-bold">{results.length}</span> results for <span className="font-medium">"{q}"</span>
                  {!hasMore && results.length > 0 && <span className="ml-1">(all loaded)</span>}
                </>
              ) : (
                <>Recent quotations</>
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

