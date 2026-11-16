import React, { useEffect, useMemo, useRef, useState, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  PlusCircleIcon,
  PencilSquareIcon,
  TrashIcon,
  MagnifyingGlassIcon,
  ArrowPathIcon,
  UserIcon,
  BuildingOffice2Icon,
  PhoneIcon,
  IdentificationIcon,
} from "@heroicons/react/24/solid";
import { GlassCard, GlassInput } from "@/components/Glass.jsx";

export default function DoctorsPage() {
  const navigate = useNavigate();
  const [doctors, setDoctors] = useState([]);
  const [loading, setLoading] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [activeRowIdx, setActiveRowIdx] = useState(0);

  const searchInputRef = useRef(null);
  const rowRefs = useRef([]);

  useEffect(() => {
    document.title = "Doctors (RMP) Directory - Karobar App";
  }, []);

  const fetchDoctors = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await axios.get("/api/doctors");
      setDoctors(Array.isArray(data) ? data : []);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to load doctors.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchDoctors();
  }, [fetchDoctors]);

  const filteredDoctors = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return doctors;
    return doctors.filter(
      (d) =>
        String(d.name || "").toLowerCase().includes(q) ||
        String(d.pmdc_number || "").toLowerCase().includes(q) ||
        String(d.hospital_clinic || "").toLowerCase().includes(q) ||
        String(d.specialization || "").toLowerCase().includes(q) ||
        String(d.phone || "").toLowerCase().includes(q)
    );
  }, [doctors, searchQuery]);

  useEffect(() => {
    rowRefs.current = rowRefs.current.slice(0, filteredDoctors.length);
    if (filteredDoctors.length > 0 && activeRowIdx >= filteredDoctors.length) {
      setActiveRowIdx(0);
    }
  }, [filteredDoctors.length, activeRowIdx]);

  // Global keyboard shortcuts: Ctrl+N -> Create Doctor, / or Ctrl+F -> Search
  useEffect(() => {
    const onKeyDown = (e) => {
      if (e.defaultPrevented) return;
      const key = (e.key || "").toLowerCase();
      const target = e.composedPath?.()[0] || e.target;
      const isEditable = target?.matches?.(":read-write, select, textarea, input");

      if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && key === "n") {
        e.preventDefault();
        navigate("/doctors/create");
      } else if (
        ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && key === "f") ||
        (!isEditable && !e.ctrlKey && !e.metaKey && !e.altKey && e.key === "/")
      ) {
        e.preventDefault();
        searchInputRef.current?.focus();
        searchInputRef.current?.select?.();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate]);

  // Table roving tabindex & arrow-key navigation (preserves native scroll at boundaries)
  const handleRowKeyDown = (e, idx, doc) => {
    if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
    const target = e.composedPath?.()[0] || e.target;
    if (target?.matches?.(":read-write, select")) return;

    if (e.key === "ArrowDown") {
      if (idx < filteredDoctors.length - 1) {
        e.preventDefault();
        const nextIdx = idx + 1;
        setActiveRowIdx(nextIdx);
        const nextEl = rowRefs.current[nextIdx];
        nextEl?.focus();
        nextEl?.scrollIntoView({ block: "nearest", inline: "nearest" });
      }
    } else if (e.key === "ArrowUp") {
      if (idx > 0) {
        e.preventDefault();
        const prevIdx = idx - 1;
        setActiveRowIdx(prevIdx);
        const prevEl = rowRefs.current[prevIdx];
        prevEl?.focus();
        prevEl?.scrollIntoView({ block: "nearest", inline: "nearest" });
      }
    } else if (e.key === "Enter" && target === rowRefs.current[idx]) {
      e.preventDefault();
      navigate(`/doctors/${doc.id}/edit`);
    }
  };

  const handleDelete = async (doc) => {
    if (
      !window.confirm(
        `Delete Dr. ${doc.name.replace(/^dr\.?\s*/i, "")} from Doctors directory?`
      )
    ) {
      return;
    }
    try {
      await axios.delete(`/api/doctors/${doc.id}`);
      toast.success("Doctor removed.");
      fetchDoctors();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to delete doctor.");
    }
  };

  return (
    <div className="directory-page space-y-4">
      {/* Header Panel */}
      <div className="products-panel directory-overview">
        <div className="products-heading">
          <div className="flex items-center gap-4 min-w-0">
            <div className="products-identity-icon">
              <IdentificationIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">Doctors (RMP) Directory</h1>
              <p className="products-subtitle">
                <IdentificationIcon className="w-3.5 h-3.5" />
                <span>
                  {doctors.length} registered prescribing doctors • Quick-select on Sale Invoices &amp; Narcotics Registers
                </span>
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2.5 flex-nowrap shrink-0">
            <Link
              to="/doctors/create"
              title="Add Doctor (Ctrl+N)"
              className="products-action products-action-primary theme-btn-primary whitespace-nowrap"
            >
              <PlusCircleIcon className="w-4 h-4" />
              <span>Add Doctor (Ctrl+N)</span>
            </Link>
            <button
              type="button"
              onClick={fetchDoctors}
              disabled={loading}
              title="Refresh"
              className="products-action theme-btn-tertiary whitespace-nowrap"
            >
              <ArrowPathIcon className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`} />
              <span>Refresh</span>
            </button>
          </div>
        </div>

        {/* Search & Keyboard Hint Bar */}
        <div className="mt-3 px-4 pb-4 sm:px-6 sm:pb-5 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="relative w-full sm:max-w-lg">
            <MagnifyingGlassIcon className="w-4 h-4 text-slate-400 absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
            <GlassInput
              ref={searchInputRef}
              value={searchQuery}
              onChange={(e) => {
                setSearchQuery(e.target.value);
                setActiveRowIdx(0);
              }}
              onKeyDown={(e) => {
                if (e.key === "ArrowDown" && filteredDoctors.length > 0) {
                  e.preventDefault();
                  setActiveRowIdx(0);
                  rowRefs.current[0]?.focus();
                  rowRefs.current[0]?.scrollIntoView({ block: "nearest", inline: "nearest" });
                } else if (e.key === "Escape" && searchQuery) {
                  e.preventDefault();
                  setSearchQuery("");
                }
              }}
              aria-label="Search doctors"
              placeholder="Search by Doctor Name, PM&DC Reg #, Hospital/Clinic, or Phone... (Press /)"
              className="w-full pl-9 h-[38px]"
            />
          </div>

          <div className="text-[11px] text-slate-500 dark:text-slate-400 whitespace-nowrap">
            <kbd className="px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-600 font-mono text-[10px]">Ctrl+N</kbd> New •{" "}
            <kbd className="px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-600 font-mono text-[10px]">/</kbd> Search •{" "}
            <kbd className="px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-600 font-mono text-[10px]">↑↓</kbd> Navigate •{" "}
            <kbd className="px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-600 font-mono text-[10px]">Enter</kbd> Edit
          </div>
        </div>
      </div>

      {/* Doctors Table Card */}
      <GlassCard className="directory-panel">
        <div className="directory-panel-heading">
          <div className="flex items-center gap-2">
            <IdentificationIcon className="w-4 h-4 text-emerald-500" />
            <span className="font-semibold text-sm">Prescribing Doctors List</span>
          </div>
          <span className="text-xs text-slate-400">
            Showing {filteredDoctors.length} of {doctors.length}
          </span>
        </div>

        <div className="p-3">
          <div className="directory-table-scroll">
            <table className="w-full text-sm">
              <thead className="bg-white/90 dark:bg-slate-800/90 sticky top-0 z-10">
                <tr className="border-b border-slate-200/70 dark:border-slate-700 text-left text-xs uppercase text-slate-500 dark:text-slate-300">
                  <th className="px-3 py-2.5 w-12 text-center">#</th>
                  <th className="px-3 py-2.5">Doctor Name (RMP)</th>
                  <th className="px-3 py-2.5">PM&amp;DC / PMC Reg #</th>
                  <th className="px-3 py-2.5">Hospital / Clinic</th>
                  <th className="px-3 py-2.5">Specialization</th>
                  <th className="px-3 py-2.5">Phone</th>
                  <th className="px-3 py-2.5 text-center">Invoices</th>
                  <th className="px-3 py-2.5 text-center">Status</th>
                  <th className="px-3 py-2.5 text-center w-24">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200/50 dark:divide-slate-700/50">
                {filteredDoctors.length === 0 && !loading && (
                  <tr>
                    <td colSpan={9} className="px-3 py-10 text-center text-slate-500">
                      No doctors found. Click{" "}
                      <Link
                        to="/doctors/create"
                        className="text-emerald-600 dark:text-emerald-400 font-semibold hover:underline"
                      >
                        Add Doctor
                      </Link>{" "}
                      to create one.
                    </td>
                  </tr>
                )}

                {filteredDoctors.map((doc, idx) => {
                  const isActiveRow = idx === activeRowIdx;
                  return (
                    <tr
                      key={doc.id}
                      ref={(el) => (rowRefs.current[idx] = el)}
                      tabIndex={isActiveRow ? 0 : -1}
                      onFocus={() => setActiveRowIdx(idx)}
                      onKeyDown={(e) => handleRowKeyDown(e, idx, doc)}
                      className={`transition-colors outline-none focus:ring-2 focus:ring-inset focus:ring-emerald-500 ${
                        isActiveRow
                          ? "bg-emerald-50/40 dark:bg-emerald-950/20"
                          : "hover:bg-blue-50/50 dark:hover:bg-slate-700/40"
                      }`}
                    >
                      <td className="px-3 py-2.5 text-center font-mono text-xs text-slate-400">
                        {idx + 1}
                      </td>
                      <td className="px-3 py-2.5">
                        <div className="flex items-center gap-2">
                          <UserIcon className="w-4 h-4 text-emerald-600 dark:text-emerald-400 shrink-0" />
                          <Link
                            to={`/doctors/${doc.id}/edit`}
                            className="font-semibold text-slate-900 dark:text-slate-100 hover:text-emerald-600 dark:hover:text-emerald-400 hover:underline"
                          >
                            {doc.name}
                          </Link>
                        </div>
                        {doc.address && (
                          <div className="text-xs text-slate-400 truncate max-w-xs pl-6">
                            {doc.address}
                          </div>
                        )}
                      </td>
                      <td className="px-3 py-2.5 text-xs font-mono">
                        {doc.pmdc_number ? (
                          <span className="px-2 py-0.5 rounded bg-emerald-50 dark:bg-emerald-950/60 text-emerald-800 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800 font-bold">
                            {doc.pmdc_number}
                          </span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </td>
                      <td className="px-3 py-2.5 text-xs">
                        {doc.hospital_clinic ? (
                          <span className="inline-flex items-center gap-1 text-slate-700 dark:text-slate-200 font-medium">
                            <BuildingOffice2Icon className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                            <span>{doc.hospital_clinic}</span>
                          </span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </td>
                      <td className="px-3 py-2.5 text-xs text-slate-600 dark:text-slate-300">
                        {doc.specialization || <span className="text-slate-400">—</span>}
                      </td>
                      <td className="px-3 py-2.5 text-xs font-mono">
                        {doc.phone ? (
                          <span className="inline-flex items-center gap-1 text-slate-700 dark:text-slate-300">
                            <PhoneIcon className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                            <span>{doc.phone}</span>
                          </span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </td>
                      <td className="px-3 py-2.5 text-center text-xs font-mono font-bold text-slate-700 dark:text-slate-300">
                        {Number(doc.prescriptions_count || 0)}
                      </td>
                      <td className="px-3 py-2.5 text-center">
                        {doc.is_active !== false ? (
                          <span className="inline-block px-2 py-0.5 text-[10px] font-semibold rounded-full border bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300 border-emerald-200 dark:border-emerald-700">
                            ACTIVE
                          </span>
                        ) : (
                          <span className="inline-block px-2 py-0.5 text-[10px] font-semibold rounded-full border bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400 border-slate-200 dark:border-slate-700">
                            INACTIVE
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-2.5">
                        <div className="flex items-center justify-center gap-1 whitespace-nowrap">
                          <Link
                            to={`/doctors/${doc.id}/edit`}
                            className="products-row-action text-emerald-600 dark:text-emerald-400"
                            title="Edit Doctor"
                            aria-label={`Edit ${doc.name}`}
                          >
                            <PencilSquareIcon className="w-4 h-4" />
                          </Link>
                          <button
                            type="button"
                            onClick={() => handleDelete(doc)}
                            className="products-row-action text-rose-600 dark:text-rose-400"
                            title="Delete Doctor"
                            aria-label={`Delete ${doc.name}`}
                          >
                            <TrashIcon className="w-4 h-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      </GlassCard>
    </div>
  );
}
