// resources/js/components/CustomerPickerButton.jsx
// Button-style customer selector matching the invoice select in CostOfSaleDetailReport.
// Opens a search dropdown with the loaded customers list.
import React, { useState, useRef, useEffect, useImperativeHandle, forwardRef } from "react";
import { createPortal } from "react-dom";
import { UserIcon, MagnifyingGlassIcon, XMarkIcon } from "@heroicons/react/24/solid";

const CustomerPickerButton = forwardRef(
  ({ value, customers = [], onChange, placeholder = "Click to search customer...", isDark = false }, ref) => {
    const [open, setOpen] = useState(false);
    const [query, setQuery] = useState("");
    const [highlightIndex, setHighlightIndex] = useState(0);
    const [position, setPosition] = useState({ top: 0, left: 0, width: 0 });
    const wrapRef = useRef(null);
    const searchInputRef = useRef(null);

    // Expose focus to parent (for auto-focus on load)
    useImperativeHandle(ref, () => ({
      focus: () => wrapRef.current?.querySelector("button")?.focus?.(),
      openMenu: () => setOpen(true),
      closeMenu: () => setOpen(false),
    }));

    const selected = customers.find((c) => String(c.id) === String(value));

    // Reset query when opening
    useEffect(() => {
      if (open) {
        setQuery("");
        setHighlightIndex(0);
        updatePosition();
        setTimeout(() => searchInputRef.current?.focus?.(), 30);
      }
    }, [open]);

    const filtered = customers.filter((c) =>
      c.name.toLowerCase().includes(query.toLowerCase())
    );

    const updatePosition = () => {
      if (!wrapRef.current) return;
      const rect = wrapRef.current.getBoundingClientRect();
      setPosition({
        top: rect.bottom + window.scrollY,
        left: rect.left + window.scrollX,
        width: rect.width,
      });
    };

    // Update position on scroll/resize when open
    useEffect(() => {
      if (!open) return;
      window.addEventListener("scroll", updatePosition, true);
      window.addEventListener("resize", updatePosition);
      return () => {
        window.removeEventListener("scroll", updatePosition, true);
        window.removeEventListener("resize", updatePosition);
      };
    }, [open]);

    // Close on outside click
    useEffect(() => {
      const handleClickOutside = (e) => {
        if (wrapRef.current && !wrapRef.current.contains(e.target)) {
          setOpen(false);
        }
      };
      document.addEventListener("mousedown", handleClickOutside);
      return () => document.removeEventListener("mousedown", handleClickOutside);
    }, []);

    // Close on Escape
    useEffect(() => {
      const onEsc = (e) => {
        if (e.key === "Escape") setOpen(false);
      };
      window.addEventListener("keydown", onEsc);
      return () => window.removeEventListener("keydown", onEsc);
    }, []);

    const handleSelect = (customer) => {
      onChange?.(customer.id);
      setOpen(false);
    };

    const handleKeyDown = (e) => {
      if (e.key === "ArrowDown") {
        e.preventDefault();
        setHighlightIndex((p) => Math.min(filtered.length - 1, p + 1));
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setHighlightIndex((p) => Math.max(0, p - 1));
      } else if (e.key === "Enter") {
        e.preventDefault();
        if (filtered[highlightIndex]) handleSelect(filtered[highlightIndex]);
      }
    };

    return (
      <div ref={wrapRef} className="relative w-full">
        {/* Button — matches CostOfSaleDetailReport invoice select */}
        <button
          type="button"
          onClick={() => setOpen((o) => !o)}
          className={`w-full h-9 px-3 rounded-lg border text-left text-sm flex items-center gap-2 transition-all ${
            selected
              ? "border-blue-500 bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-200"
              : "border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-700 text-gray-500 dark:text-gray-400 hover:border-blue-400"
          }`}
          title={selected?.name || placeholder}
        >
          <UserIcon className="w-5 h-5 flex-shrink-0" />
          {selected ? (
            <span className="truncate font-medium">{selected.name}</span>
          ) : (
            <span className="truncate">{placeholder}</span>
          )}
        </button>

        {/* Dropdown */}
        {open &&
          createPortal(
            <div
              className="fixed max-h-64 overflow-hidden flex flex-col bg-white dark:bg-slate-800 shadow-xl z-[9999] rounded-lg border border-gray-200 dark:border-slate-600"
              style={{ top: position.top, left: position.left, width: position.width }}
              onMouseDown={(e) => e.preventDefault()}
            >
              {/* Search bar */}
              <div className={`flex items-center gap-2 px-3 py-2 border-b ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                <MagnifyingGlassIcon className={`w-4 h-4 flex-shrink-0 ${isDark ? "text-slate-500" : "text-gray-400"}`} />
                <input
                  ref={searchInputRef}
                  type="text"
                  value={query}
                  onChange={(e) => {
                    setQuery(e.target.value);
                    setHighlightIndex(0);
                  }}
                  onKeyDown={handleKeyDown}
                  placeholder="Search customer..."
                  className="flex-1 bg-transparent outline-none text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500"
                />
                <button
                  type="button"
                  onClick={() => {
                    setQuery("");
                    setHighlightIndex(0);
                  }}
                  className="p-1 rounded hover:bg-gray-100 dark:hover:bg-slate-700"
                >
                  <XMarkIcon className={`w-4 h-4 ${isDark ? "text-slate-500" : "text-gray-400"}`} />
                </button>
              </div>

              {/* Results */}
              <div className="overflow-y-auto max-h-48">
                {filtered.length === 0 ? (
                  <div className={`px-4 py-6 text-center text-sm ${isDark ? "text-slate-500" : "text-gray-400"}`}>
                    No customers found
                  </div>
                ) : (
                  <ul>
                    {filtered.map((c, idx) => (
                      <li
                        key={c.id}
                        onMouseDown={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          handleSelect(c);
                        }}
                        onMouseEnter={() => setHighlightIndex(idx)}
                        className={`px-3 py-2.5 cursor-pointer border-b last:border-0 ${
                          idx === highlightIndex
                            ? "bg-blue-50 dark:bg-slate-700"
                            : "hover:bg-gray-50 dark:hover:bg-slate-700/50"
                        } ${isDark ? "border-slate-700/50" : "border-gray-100"}`}
                      >
                        <div className="flex items-center gap-2">
                          <div className={`w-7 h-7 rounded-full flex items-center justify-center flex-shrink-0 text-[11px] font-bold ${
                            isDark ? "bg-blue-900/40 text-blue-300" : "bg-blue-100 text-blue-700"
                          }`}>
                            {(c.name || "?").charAt(0).toUpperCase()}
                          </div>
                          <span className={`text-sm font-medium ${isDark ? "text-slate-100" : "text-gray-900"}`}>
                            {c.name}
                          </span>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>

              {/* Footer hint */}
              <div className={`px-3 py-1.5 border-t text-[10px] ${isDark ? "border-slate-700 text-slate-500" : "border-gray-200 text-gray-400"}`}>
                ↑↓ navigate · Enter select · Esc close
              </div>
            </div>,
            document.body
          )}
      </div>
    );
  }
);

export default CustomerPickerButton;
