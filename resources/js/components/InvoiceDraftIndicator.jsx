import React from "react";
import {
  ArrowPathIcon,
  CheckCircleIcon,
  ExclamationCircleIcon,
  BookmarkSquareIcon,
  ClockIcon,
} from "@heroicons/react/24/outline";

export default function InvoiceDraftIndicator({
  saveStatus,
  lastSavedAt,
  saveError,
  draftsCount = 0,
  activeDraftTitle = null,
  onOpenDrafts,
  onSaveDraft,
}) {
  const formatTime = (date) => {
    if (!date) return "";
    const d = new Date(date);
    return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  };

  return (
    <div className="flex items-center gap-1.5 text-xs select-none shrink-0">
      {/* Save Status Indicator */}
      <div className="hidden lg:flex items-center h-8 px-2.5 rounded-lg bg-slate-800/80 border border-slate-700 text-slate-300 gap-1.5 shrink-0">
        {saveStatus === "saving" && (
          <>
            <ArrowPathIcon className="w-3.5 h-3.5 text-blue-400 animate-spin shrink-0" />
            <span className="text-blue-300 font-medium whitespace-nowrap">Saving…</span>
          </>
        )}
        {saveStatus === "saved" && (
          <>
            <CheckCircleIcon className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
            <span className="text-emerald-300 font-medium whitespace-nowrap">
              Saved {lastSavedAt ? formatTime(lastSavedAt) : ""}
            </span>
          </>
        )}
        {saveStatus === "conflict" && (
          <>
            <ExclamationCircleIcon className="w-3.5 h-3.5 text-amber-400 shrink-0" />
            <span className="text-amber-300 font-medium whitespace-nowrap">Conflict</span>
          </>
        )}
        {saveStatus === "error" && (
          <>
            <ExclamationCircleIcon className="w-3.5 h-3.5 text-rose-400 shrink-0" />
            <span className="text-rose-300 font-medium whitespace-nowrap" title={saveError || "Save failed"}>
              Save failed
            </span>
          </>
        )}
        {saveStatus === "idle" && (
          <>
            <ClockIcon className="w-3.5 h-3.5 text-slate-400 shrink-0" />
            <span className="text-slate-400 font-normal whitespace-nowrap">
              {activeDraftTitle ? `Draft: ${activeDraftTitle}` : "Draft ready"}
            </span>
          </>
        )}
      </div>

      {/* Manual Hold/Save Draft Button (Alt+H) */}
      <button
        type="button"
        onClick={() => onSaveDraft && onSaveDraft()}
        title="Hold / Save Draft (Shortcut: Alt+H)"
        className="inline-flex items-center justify-center gap-1.5 h-8 px-2.5 rounded-lg text-xs font-semibold text-slate-200 bg-slate-800 hover:bg-slate-700 border border-slate-600 transition-colors shrink-0 shadow-sm"
      >
        <BookmarkSquareIcon className="w-3.5 h-3.5 text-amber-400 shrink-0" />
        <span className="whitespace-nowrap">Hold</span>
        <kbd className="hidden sm:inline-block px-1 py-0.5 text-[9px] font-mono rounded bg-slate-700 text-slate-300 border border-slate-600 leading-none">
          Alt+H
        </kbd>
      </button>

      {/* Held Drafts Modal Toggle with Count Badge (Alt+D) */}
      <button
        type="button"
        onClick={() => onOpenDrafts && onOpenDrafts()}
        title="View Held Invoices & Drafts (Shortcut: Alt+D)"
        className="inline-flex items-center justify-center gap-1.5 h-8 px-2.5 rounded-lg text-xs font-semibold text-white bg-blue-700 hover:bg-blue-600 border border-blue-600 transition-colors shrink-0 shadow-sm"
      >
        <ClockIcon className="w-3.5 h-3.5 shrink-0" />
        <span className="whitespace-nowrap">Held Drafts</span>
        {draftsCount > 0 && (
          <span className="h-4 min-w-[16px] px-1 flex items-center justify-center rounded-full text-[10px] font-bold bg-amber-400 text-slate-900 shrink-0 leading-none">
            {draftsCount}
          </span>
        )}
        <kbd className="hidden sm:inline-block px-1 py-0.5 text-[9px] font-mono rounded bg-blue-800 text-blue-200 border border-blue-600 leading-none">
          Alt+D
        </kbd>
      </button>
    </div>
  );
}
