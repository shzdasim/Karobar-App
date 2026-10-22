import React, { useState, useEffect, useRef, useMemo } from "react";
import {
  XMarkIcon,
  TrashIcon,
  ClockIcon,
  UserIcon,
  CurrencyDollarIcon,
  DocumentPlusIcon,
  MagnifyingGlassIcon,
  CheckIcon,
  CubeIcon,
} from "@heroicons/react/24/outline";

export default function InvoiceDraftsModal({
  isOpen,
  onClose,
  drafts = [],
  activeDraftId = null,
  onResumeDraft,
  onDiscardDraft,
  onStartBlank,
  type = "sale_retail",
}) {
  const [searchTerm, setSearchTerm] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [discardingId, setDiscardingId] = useState(null);
  const [confirmingDiscardId, setConfirmingDiscardId] = useState(null);

  const searchInputRef = useRef(null);
  const itemsContainerRef = useRef(null);

  const getTypeName = (t) => {
    switch (t) {
      case "sale_retail":
        return "Retail Sale";
      case "sale_wholesale":
        return "Wholesale Sale";
      case "purchase":
        return "Purchase Invoice";
      default:
        return "Draft";
    }
  };

  const formatDateTime = (isoString) => {
    if (!isoString) return "";
    const d = new Date(isoString);
    return `${d.toLocaleDateString()} ${d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`;
  };

  // Filter drafts based on search term
  const filteredDrafts = useMemo(() => {
    if (!searchTerm.trim()) return drafts;
    const q = searchTerm.toLowerCase();
    return drafts.filter((d) => {
      const title = (d.title || "").toLowerCase();
      const party = (d.party_name || "").toLowerCase();
      const amount = String(d.total_amount || "");
      const count = String(d.items_count || "");
      const productMatches = Array.isArray(d.items_summary) && d.items_summary.some((p) => (p.name || "").toLowerCase().includes(q));
      return title.includes(q) || party.includes(q) || amount.includes(q) || count.includes(q) || productMatches;
    });
  }, [drafts, searchTerm]);

  // Reset selected index when search changes
  useEffect(() => {
    setSelectedIndex(0);
  }, [searchTerm]);

  // Focus search input when modal opens
  useEffect(() => {
    if (isOpen) {
      setSearchTerm("");
      setSelectedIndex(0);
      setConfirmingDiscardId(null);
      setTimeout(() => {
        searchInputRef.current?.focus();
      }, 50);
    }
  }, [isOpen]);

  // Keyboard navigation within modal (Up/Down/Enter/Escape/Alt+N)
  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
        return;
      }

      if (e.altKey && e.key.toLowerCase() === "n") {
        e.preventDefault();
        onStartBlank && onStartBlank();
        onClose();
        return;
      }

      if (e.key === "ArrowDown") {
        e.preventDefault();
        setSelectedIndex((prev) => {
          const next = Math.min(filteredDrafts.length - 1, prev + 1);
          scrollToItem(next);
          return next;
        });
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setSelectedIndex((prev) => {
          const next = Math.max(0, prev - 1);
          scrollToItem(next);
          return next;
        });
      } else if (e.key === "Enter") {
        // If not focusing a sub-button, resume the selected draft
        const activeTag = document.activeElement?.tagName?.toLowerCase();
        if (activeTag === "button") return;

        if (filteredDrafts[selectedIndex]) {
          e.preventDefault();
          onResumeDraft(filteredDrafts[selectedIndex].id);
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, filteredDrafts, selectedIndex, onClose, onResumeDraft, onStartBlank]);

  const scrollToItem = (index) => {
    const container = itemsContainerRef.current;
    if (!container) return;
    const item = container.children[index];
    if (item) {
      item.scrollIntoView({ block: "nearest", behavior: "smooth" });
    }
  };

  const handleDiscard = async (draftId) => {
    setDiscardingId(draftId);
    try {
      await onDiscardDraft(draftId);
      setConfirmingDiscardId(null);
    } finally {
      setDiscardingId(null);
    }
  };

  const mouseDownTargetRef = useRef(null);

  const handleMouseDown = (e) => {
    mouseDownTargetRef.current = e.target;
  };

  const handleBackdropClick = (e) => {
    if (e.target === e.currentTarget && mouseDownTargetRef.current === e.currentTarget) {
      onClose();
    }
  };

  if (!isOpen) return null;

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
            <div className="p-2 rounded-xl bg-blue-600/20 text-blue-400 border border-blue-500/20">
              <ClockIcon className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-base font-bold text-white">
                Held Invoices & Drafts ({getTypeName(type)})
              </h2>
              <p className="text-xs text-slate-400">
                Search, resume, or discard held orders without affecting stock
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

        {/* Search Bar & Actions */}
        <div className="p-4 bg-slate-800/30 border-b border-slate-800/60 flex items-center gap-3">
          <div className="relative flex-1">
            <MagnifyingGlassIcon className="absolute left-3 top-2.5 w-4 h-4 text-slate-400 pointer-events-none" />
            <input
              ref={searchInputRef}
              type="text"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              placeholder="Search held drafts by customer, item, or amount… (↑/↓ to navigate)"
              className="w-full pl-9 pr-3 py-2 text-xs rounded-lg bg-slate-800 border border-slate-700 text-white placeholder-slate-400 focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
            />
          </div>
          <button
            type="button"
            onClick={() => {
              onStartBlank && onStartBlank();
              onClose();
            }}
            title="Create fresh new blank invoice (Alt+N)"
            className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg text-xs font-semibold text-slate-200 bg-slate-800 hover:bg-slate-700 border border-slate-700 transition-colors shrink-0 shadow-sm"
          >
            <DocumentPlusIcon className="w-4 h-4 text-emerald-400" />
            <span>New Blank (Alt+N)</span>
          </button>
        </div>

        {/* Drafts List */}
        <div ref={itemsContainerRef} className="flex-1 overflow-y-auto p-4 space-y-2.5">
          {filteredDrafts.length === 0 ? (
            <div className="py-12 text-center text-slate-400">
              <ClockIcon className="w-12 h-12 mx-auto mb-3 text-slate-600 stroke-1" />
              <p className="text-sm font-medium">
                {searchTerm ? `No held drafts match "${searchTerm}"` : "No held drafts found for this document type."}
              </p>
              <p className="text-xs text-slate-500 mt-1">
                Press <kbd className="px-1 py-0.5 rounded bg-slate-800 border border-slate-700">Alt+H</kbd> while entering an invoice to hold it.
              </p>
            </div>
          ) : (
            filteredDrafts.map((d, index) => {
              const isSelected = selectedIndex === index;
              const isActive = activeDraftId === d.id;
              const isConfirming = confirmingDiscardId === d.id;
              const isDeleting = discardingId === d.id;

              return (
                <div
                  key={d.id}
                  onClick={() => setSelectedIndex(index)}
                  className={`p-3.5 rounded-xl border transition-all cursor-pointer ${
                    isSelected
                      ? "bg-blue-950/40 border-blue-500/80 ring-1 ring-blue-500/40 shadow-md"
                      : "bg-slate-800/40 hover:bg-slate-800/70 border-slate-700/60"
                  }`}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-sm text-white truncate">
                          {d.title || "Untitled Draft"}
                        </span>
                        {isActive && (
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-500/20 text-blue-300 border border-blue-500/30">
                            Currently Open
                          </span>
                        )}
                        {isSelected && (
                          <span className="hidden sm:inline-block px-2 py-0.5 rounded text-[10px] font-mono bg-blue-600 text-white font-medium">
                            Press Enter
                          </span>
                        )}
                      </div>

                      <div className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-300">
                        {d.party_name && (
                          <span className="flex items-center gap-1 text-slate-300">
                            <UserIcon className="w-3.5 h-3.5 text-slate-400" />
                            {d.party_name}
                          </span>
                        )}
                        <span className="flex items-center gap-1 font-semibold text-emerald-400">
                          <CurrencyDollarIcon className="w-3.5 h-3.5" />
                          {Number(d.total_amount || 0).toLocaleString(undefined, {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2,
                          })}
                        </span>
                        <span className="text-slate-400">
                          {d.items_count || 0} items
                        </span>
                        <span className="text-slate-500 text-[11px]">
                          Updated {formatDateTime(d.updated_at)}
                        </span>
                      </div>

                      {/* Brief Product Details */}
                      {Array.isArray(d.items_summary) && d.items_summary.length > 0 && (
                        <div className="mt-2.5 pt-2 border-t border-slate-700/60 flex flex-wrap items-center gap-1.5">
                          <span className="text-[11px] text-slate-400 font-medium mr-1 flex items-center gap-1">
                            <CubeIcon className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                            Items:
                          </span>
                          {d.items_summary.slice(0, 4).map((item, idx) => (
                            <span
                              key={idx}
                              className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] bg-slate-700/70 text-slate-200 border border-slate-600/60 shadow-xs"
                            >
                              <span className="truncate max-w-[160px] font-medium">{item.name}</span>
                              <span className="text-amber-400 font-bold">×{item.quantity}</span>
                            </span>
                          ))}
                          {d.items_summary.length > 4 && (
                            <span className="text-[11px] text-slate-400 font-medium italic pl-0.5">
                              +{d.items_summary.length - 4} more
                            </span>
                          )}
                        </div>
                      )}
                    </div>

                    {/* Actions */}
                    <div className="flex items-center gap-2 shrink-0">
                      {isConfirming ? (
                        <div className="flex items-center gap-1 bg-red-950/80 p-1 rounded-lg border border-red-800">
                          <span className="text-[11px] text-red-200 px-1">Discard?</span>
                          <button
                            type="button"
                            disabled={isDeleting}
                            onClick={(e) => {
                              e.stopPropagation();
                              handleDiscard(d.id);
                            }}
                            className="px-2 py-1 rounded bg-red-600 hover:bg-red-500 text-white text-xs font-semibold"
                          >
                            {isDeleting ? "…" : "Yes"}
                          </button>
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation();
                              setConfirmingDiscardId(null);
                            }}
                            className="px-2 py-1 rounded bg-slate-700 hover:bg-slate-600 text-slate-200 text-xs"
                          >
                            No
                          </button>
                        </div>
                      ) : (
                        <>
                          <button
                            type="button"
                            title="Discard draft permanently"
                            onClick={(e) => {
                              e.stopPropagation();
                              setConfirmingDiscardId(d.id);
                            }}
                            className="p-1.5 rounded-lg text-slate-400 hover:text-red-400 hover:bg-red-950/30 transition-colors"
                          >
                            <TrashIcon className="w-4 h-4" />
                          </button>
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation();
                              onResumeDraft(d.id);
                            }}
                            className={`px-3 py-1.5 rounded-lg text-xs font-bold text-white transition-colors shadow-sm ${
                              isSelected ? "bg-blue-600 hover:bg-blue-500" : "bg-slate-700 hover:bg-slate-600"
                            }`}
                          >
                            {isActive ? "Re-load" : "Resume"}
                          </button>
                        </>
                      )}
                    </div>
                  </div>
                </div>
              );
            })
          )}
        </div>

        {/* Footer with keyboard shortcuts legend */}
        <div className="flex items-center justify-between px-6 py-2.5 border-t border-slate-800 bg-slate-800/40 text-xs text-slate-400">
          <div className="hidden sm:flex items-center gap-3 text-[11px]">
            <span><kbd className="px-1 py-0.5 rounded bg-slate-800 border border-slate-700 font-mono">↑</kbd> <kbd className="px-1 py-0.5 rounded bg-slate-800 border border-slate-700 font-mono">↓</kbd> navigate</span>
            <span><kbd className="px-1 py-0.5 rounded bg-slate-800 border border-slate-700 font-mono">Enter</kbd> resume</span>
            <span><kbd className="px-1 py-0.5 rounded bg-slate-800 border border-slate-700 font-mono">Alt+N</kbd> blank</span>
            <span><kbd className="px-1 py-0.5 rounded bg-slate-800 border border-slate-700 font-mono">Esc</kbd> close</span>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="ml-auto px-4 py-1.5 rounded-lg text-xs font-semibold text-slate-300 hover:text-white bg-slate-800 hover:bg-slate-700 border border-slate-700 transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
