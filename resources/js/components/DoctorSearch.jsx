// resources/js/components/DoctorSearch.jsx
import React, { useEffect, useRef, useState, useCallback } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  UserIcon,
  BuildingOffice2Icon,
  PhoneIcon,
  PlusCircleIcon,
  IdentificationIcon,
} from "@heroicons/react/24/solid";
import { GlassCard, GlassToolbar } from "@/components/glass";

function DoctorResultRow({ doctor, active, onHover, onOpen, rowRef }) {
  return (
    <li
      ref={rowRef}
      onMouseEnter={onHover}
      onClick={onOpen}
      className={[
        "px-4 py-3 transition-all duration-150 cursor-pointer border-b border-slate-100 dark:border-slate-700/50 last:border-0",
        active
          ? "bg-linear-to-r from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20"
          : "hover:bg-slate-50 dark:hover:bg-slate-700/30",
      ].join(" ")}
      title="Select doctor"
      aria-label="Select doctor"
    >
      <div className="flex items-start justify-between">
        <div className="flex items-start gap-3 flex-1 min-w-0">
          <div className="shrink-0">
            <div className="w-10 h-10 rounded-lg bg-emerald-100 dark:bg-emerald-900/40 flex items-center justify-center">
              <UserIcon className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
            </div>
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-semibold text-slate-800 dark:text-slate-100 text-sm">
                {doctor.name || "—"}
              </span>
              {doctor.pmdc_number && (
                <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-emerald-100 dark:bg-emerald-950 text-emerald-800 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800">
                  PM&amp;DC: {doctor.pmdc_number}
                </span>
              )}
              {doctor.specialization && (
                <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-slate-100 dark:bg-slate-700 text-slate-600 dark:text-slate-300">
                  {doctor.specialization}
                </span>
              )}
            </div>
            {doctor.hospital_clinic && (
              <div className="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 mt-1">
                <BuildingOffice2Icon className="w-3.5 h-3.5 shrink-0" />
                <span className="truncate">{doctor.hospital_clinic}</span>
              </div>
            )}
            {doctor.phone && (
              <div className="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                <PhoneIcon className="w-3 h-3 shrink-0" />
                <span className="truncate">{doctor.phone}</span>
              </div>
            )}
          </div>
        </div>
      </div>
    </li>
  );
}

export default function DoctorSearch({
  isOpen,
  onClose,
  onSelect,
  onCreate,
  onClear,
  hasSelectedDoctor = false,
  initialCreateMode = false,
}) {
  const [q, setQ] = useState("");
  const [loading, setLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [results, setResults] = useState([]);
  const [activeIdx, setActiveIdx] = useState(-1);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(true);
  const [lastSearchTerm, setLastSearchTerm] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [creating, setCreating] = useState(false);
  const [newDoctor, setNewDoctor] = useState({
    name: "",
    pmdc_number: "",
    hospital_clinic: "",
    specialization: "",
    phone: "",
  });

  const inputRef = useRef(null);
  const createNameRef = useRef(null);
  const boxRef = useRef(null);
  const listRef = useRef(null);
  const rowRefs = useRef([]);
  const abortRef = useRef(null);
  const debounceRef = useRef(null);

  useEffect(() => {
    rowRefs.current = [];
  }, [results]);

  useEffect(() => {
    if (!isOpen) return;
    setPage(1);
    setHasMore(true);
    setResults([]);
    setActiveIdx(-1);
    setQ("");
    setLastSearchTerm("");
    setShowCreate(Boolean(initialCreateMode));
    setTimeout(() => {
      if (initialCreateMode) {
        createNameRef.current?.focus();
      } else {
        inputRef.current?.focus();
      }
    }, 50);
  }, [isOpen, initialCreateMode]);

  const fetchDoctors = useCallback(async (searchTerm, pageNum = 1, isLoadMore = false) => {
    if (abortRef.current) abortRef.current.abort();
    abortRef.current = new AbortController();
    const signal = abortRef.current.signal;

    if (isLoadMore) {
      setLoadingMore(true);
    } else {
      setLoading(true);
    }

    try {
      const res = await axios.get("/api/doctors/search", {
        params: {
          q: searchTerm,
          page: pageNum,
          per_page: 20,
        },
        signal,
      });

      const paginationData = res.data;
      const newResults = Array.isArray(paginationData?.data)
        ? paginationData.data
        : Array.isArray(paginationData)
        ? paginationData
        : [];

      const totalPages = paginationData?.last_page || 1;
      const hasMoreData = pageNum < totalPages;

      if (isLoadMore) {
        setResults((prev) => [...prev, ...newResults]);
      } else {
        setResults(newResults);
        setActiveIdx(newResults.length ? 0 : -1);
      }

      setHasMore(hasMoreData);
      setPage(pageNum);
    } catch (err) {
      if (!axios.isCancel(err)) console.error(err);
    } finally {
      if (isLoadMore) {
        setLoadingMore(false);
      } else {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    if (!isOpen) return;
    const searchTerm = q.trim();
    setLastSearchTerm(searchTerm);
    fetchDoctors(searchTerm, 1, false);
  }, [isOpen, fetchDoctors]);

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

  useEffect(() => {
    if (!isOpen) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);

    debounceRef.current = setTimeout(() => {
      const term = q.trim();
      if (term !== lastSearchTerm) {
        setLastSearchTerm(term);
        setPage(1);
        setHasMore(true);
        fetchDoctors(term, 1, false);
      }
    }, 220);

    return () => clearTimeout(debounceRef.current);
  }, [q, isOpen, fetchDoctors, lastSearchTerm]);

  const scrollToActiveItem = useCallback(() => {
    if (activeIdx < 0 || !listRef.current) return;
    const activeRow = rowRefs.current[activeIdx];
    if (!activeRow) return;

    const listContainer = listRef.current;
    const activeRect = activeRow.getBoundingClientRect();
    const listRect = listContainer.getBoundingClientRect();

    if (activeRect.top < listRect.top) {
      activeRow.scrollIntoView({ behavior: "smooth", block: "start" });
    } else if (activeRect.bottom > listRect.bottom) {
      activeRow.scrollIntoView({ behavior: "smooth", block: "end" });
    }
  }, [activeIdx]);

  useEffect(() => {
    if (results.length > 0 && activeIdx >= 0) {
      setTimeout(scrollToActiveItem, 10);
    }
  }, [activeIdx, results.length, scrollToActiveItem]);

  const handleScroll = useCallback(() => {
    if (!listRef.current || loadingMore || loading || !hasMore) return;
    const { scrollTop, scrollHeight, clientHeight } = listRef.current;
    if (scrollHeight - scrollTop - clientHeight < 100) {
      fetchDoctors(lastSearchTerm, page + 1, true);
    }
  }, [loadingMore, loading, hasMore, page, lastSearchTerm, fetchDoctors]);

  const handleKeyDown = (e) => {
    if (!isOpen) return;
    if (["ArrowDown", "ArrowUp", "Enter", "Escape"].includes(e.key)) {
      e.preventDefault();
    }

    if (e.key === "ArrowDown") {
      const newIdx = Math.min((results.length || 1) - 1, activeIdx + 1);
      setActiveIdx(newIdx);
      if (newIdx >= results.length - 2 && hasMore && !loadingMore) {
        fetchDoctors(lastSearchTerm, page + 1, true);
      }
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

  const handleSelect = (doctor) => {
    if (onSelect) {
      onSelect(doctor);
    }
    handleClose();
  };

  const handleClose = () => {
    setQ("");
    setResults([]);
    setActiveIdx(-1);
    setPage(1);
    setHasMore(true);
    setLastSearchTerm("");
    setShowCreate(false);
    setNewDoctor({
      name: "",
      pmdc_number: "",
      hospital_clinic: "",
      specialization: "",
      phone: "",
    });
    if (onClose) onClose();
  };

  const handleCreateDoctor = async (e) => {
    e?.preventDefault?.();
    e?.stopPropagation?.();
    if (!newDoctor.name?.trim()) {
      toast?.error?.("Doctor name is required");
      return;
    }
    setCreating(true);
    try {
      const { data } = await axios.post("/api/doctors", {
        name: newDoctor.name.trim(),
        pmdc_number: newDoctor.pmdc_number?.trim() || null,
        hospital_clinic: newDoctor.hospital_clinic?.trim() || null,
        specialization: newDoctor.specialization?.trim() || null,
        phone: newDoctor.phone?.trim() || null,
      });
      toast?.success?.("Doctor saved & selected");
      if (onCreate) {
        onCreate(data);
      }
      if (onSelect && onSelect !== onCreate) {
        onSelect(data);
      }
      handleClose();
    } catch (err) {
      const d = err?.response?.data;
      toast?.error?.(
        d?.message ||
          (d?.errors ? Object.values(d.errors).flat().join(", ") : "Failed to save doctor.")
      );
    } finally {
      setCreating(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-100 flex items-start justify-center pt-[10vh] bg-black/50"
      onClick={(e) => {
        if (e.target === e.currentTarget) handleClose();
      }}
    >
      <div ref={boxRef} className="w-full max-w-2xl mx-4">
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
              placeholder="Search doctor by name, PM&DC Reg #, hospital/clinic, or phone..."
              className="flex-1 bg-transparent border-0 outline-hidden placeholder:text-slate-400 dark:placeholder:text-slate-500 text-sm text-slate-900 dark:text-slate-100"
            />
            {hasSelectedDoctor && onClear && (
              <button
                type="button"
                onClick={() => {
                  onClear();
                  handleClose();
                }}
                className="px-2 py-1 rounded text-[11px] font-semibold bg-rose-50 hover:bg-rose-100 text-rose-700 dark:bg-rose-950/60 dark:text-rose-300 transition shrink-0"
              >
                Clear Doctor
              </button>
            )}
            <kbd className="text-[10px] border border-slate-200 dark:border-slate-600 rounded-sm px-1.5 py-0.5 bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400 font-mono shrink-0">
              Esc
            </kbd>
          </div>

          {/* Results */}
          <div ref={listRef} className="max-h-[55vh] overflow-y-auto" onScroll={handleScroll}>
            {loading && (
              <div className="flex items-center justify-center py-8">
                <div className="flex flex-col items-center gap-2">
                  <ArrowPathIcon className="w-6 h-6 animate-spin text-emerald-500" />
                  <span className="text-xs text-slate-500">Searching doctors...</span>
                </div>
              </div>
            )}

            {!loading && results.length === 0 && (
              <div className="flex flex-col items-center justify-center py-8 text-slate-400">
                <IdentificationIcon className="w-8 h-8 mb-2" />
                <span className="text-sm">
                  {q ? `No doctors matching "${q}"` : "No doctors saved yet — click New Doctor below"}
                </span>
              </div>
            )}

            {results.length > 0 && (
              <ul role="list" className="divide-y divide-slate-100 dark:divide-slate-700/50">
                {results.map((doc, idx) => (
                  <DoctorResultRow
                    key={doc.id}
                    doctor={doc}
                    active={idx === activeIdx}
                    onHover={() => setActiveIdx(idx)}
                    onOpen={() => handleSelect(doc)}
                    rowRef={(el) => (rowRefs.current[idx] = el)}
                  />
                ))}
              </ul>
            )}

            {loadingMore && (
              <div className="flex items-center justify-center py-4">
                <ArrowPathIcon className="w-5 h-5 animate-spin text-emerald-500" />
                <span className="text-xs text-slate-500 ml-2">Loading more...</span>
              </div>
            )}
          </div>

          {/* Footer: Quick-Create Doctor Form or Toolbar */}
          {showCreate ? (
            <div className="px-4 py-4 bg-slate-50/80 dark:bg-slate-800/80 border-t border-slate-100 dark:border-slate-700 space-y-3">
              <div className="flex items-center gap-2 text-sm font-semibold text-slate-700 dark:text-slate-200">
                <PlusCircleIcon className="w-4 h-4 text-emerald-600" />
                Add Prescribing Doctor (RMP)
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
                <input
                  ref={createNameRef}
                  type="text"
                  name="name"
                  value={newDoctor.name}
                  onChange={(e) => setNewDoctor((s) => ({ ...s, name: e.target.value }))}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      e.stopPropagation();
                      handleCreateDoctor(e);
                    }
                  }}
                  placeholder="Doctor Full Name * (e.g. Dr. Tariq Mahmood)"
                  autoFocus
                  className="px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-slate-900 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-emerald-500/50"
                />
                <input
                  type="text"
                  name="pmdc_number"
                  autoCorrect="off"
                  autoCapitalize="off"
                  spellCheck={false}
                  value={newDoctor.pmdc_number}
                  onChange={(e) => setNewDoctor((s) => ({ ...s, pmdc_number: e.target.value }))}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      e.stopPropagation();
                      handleCreateDoctor(e);
                    }
                  }}
                  placeholder="PM&DC / PMC Reg # (e.g. PMDC-45821-P)"
                  className="px-3 py-2 text-sm font-mono rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-slate-900 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-emerald-500/50"
                />
                <input
                  type="text"
                  name="hospital_clinic"
                  value={newDoctor.hospital_clinic}
                  onChange={(e) => setNewDoctor((s) => ({ ...s, hospital_clinic: e.target.value }))}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      e.stopPropagation();
                      handleCreateDoctor(e);
                    }
                  }}
                  placeholder="Hospital / Clinic Name"
                  className="px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-slate-900 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-emerald-500/50"
                />
                <input
                  type="text"
                  name="specialization"
                  value={newDoctor.specialization}
                  onChange={(e) => setNewDoctor((s) => ({ ...s, specialization: e.target.value }))}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      e.stopPropagation();
                      handleCreateDoctor(e);
                    }
                  }}
                  placeholder="Specialization (Optional)"
                  className="px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-slate-900 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-emerald-500/50"
                />
                <input
                  type="tel"
                  name="phone"
                  autoCorrect="off"
                  autoCapitalize="off"
                  spellCheck={false}
                  value={newDoctor.phone}
                  onChange={(e) => setNewDoctor((s) => ({ ...s, phone: e.target.value }))}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      e.stopPropagation();
                      handleCreateDoctor(e);
                    }
                  }}
                  placeholder="Phone (Optional)"
                  className="px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-slate-900 dark:text-slate-100 focus:outline-hidden focus:ring-2 focus:ring-emerald-500/50"
                />
              </div>
              <div className="flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setShowCreate(false)}
                  className="theme-btn-tertiary px-3 py-1.5 text-xs font-semibold"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleCreateDoctor}
                  disabled={creating || !newDoctor.name?.trim()}
                  className="theme-btn-primary px-3 py-1.5 text-xs font-semibold"
                >
                  {creating ? "Saving..." : "Save & Select Doctor"}
                </button>
              </div>
            </div>
          ) : (
            <GlassToolbar className="items-center justify-between py-2 px-4 bg-slate-50/80 dark:bg-slate-800/80 border-t border-slate-100 dark:border-slate-700">
              <div className="text-xs text-slate-500 dark:text-slate-400">
                {q ? (
                  <>
                    <span className="font-bold">{results.length}</span> results for{" "}
                    <span className="font-medium">"{q}"</span>
                  </>
                ) : (
                  <>Registered Prescribing Doctors (RMP)</>
                )}
              </div>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={() => {
                    setShowCreate(true);
                    if (q && !newDoctor.name) {
                      setNewDoctor((s) => ({ ...s, name: q }));
                    }
                  }}
                  className="theme-btn-primary inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-semibold"
                >
                  <PlusCircleIcon className="w-3.5 h-3.5" />
                  <span>New Doctor</span>
                </button>
                <div className="flex items-center gap-3 text-[10px] text-slate-400">
                  <span className="flex items-center gap-1">
                    <kbd className="px-1.5 py-0.5 rounded-sm border border-slate-200 dark:border-slate-600 text-[9px]">
                      ↑↓
                    </kbd>
                    <span>Navigate</span>
                  </span>
                  <span className="flex items-center gap-1">
                    <kbd className="px-1.5 py-0.5 rounded-sm border border-slate-200 dark:border-slate-600 text-[9px]">
                      ↵
                    </kbd>
                    <span>Select</span>
                  </span>
                  <span className="flex items-center gap-1">
                    <kbd className="px-1.5 py-0.5 rounded-sm border border-slate-200 dark:border-slate-600 text-[9px]">
                      Esc
                    </kbd>
                    <span>Close</span>
                  </span>
                </div>
              </div>
            </GlassToolbar>
          )}
        </GlassCard>
      </div>
    </div>
  );
}
