import React, {
  useState,
  useEffect,
  useRef,
  forwardRef,
  useImperativeHandle,
  useMemo,
} from "react";
import { createPortal } from "react-dom";
import {
  MagnifyingGlassIcon,
  CubeIcon,
  TagIcon,
} from "@heroicons/react/24/solid";
import { GlassCard, GlassToolbar } from "@/components/glass";

const ProductSearchInput = forwardRef(
  ({ value, onChange, products, onRefreshProducts, onKeyDown: onKeyDownProp, className = "" }, ref) => {
    const [display, setDisplay] = useState("");
    const [selectedProduct, setSelectedProduct] = useState(null);
    const [isOpen, setIsOpen] = useState(false);
    const [search, setSearch] = useState("");
    const [highlightIndex, setHighlightIndex] = useState(0);
    const [isInvalidInput, setIsInvalidInput] = useState(false);

const triggerRef = useRef(null);
    const searchRef = useRef(null);
    const listRef = useRef(null);
    const rowRefs = useRef([]);
    const modalRef = useRef(null);
    const dragRef = useRef({ isDragging: false, offsetX: 0, offsetY: 0 });
    const resizeRef = useRef(null);
    const [isResizing, setIsResizing] = useState(false);

    const MIN_WIDTH = 640;
    const MIN_HEIGHT = 420;
    const [windowPos, setWindowPos] = useState(() => {
      const width = 960;
      const height = 600;
      return {
        x: (window.innerWidth - width) / 2,
        y: Math.max((window.innerHeight - height) / 2, 10),
      };
    });
    const [windowSize, setWindowSize] = useState({ width: 960, height: 600 });

    const didRefreshRef = useRef(false);
    const debounceRef = useRef(null);

    const items = useMemo(() => {
      if (Array.isArray(products)) return products;
      if (products && Array.isArray(products.data)) return products.data;
      return [];
    }, [products]);

    // Clear refs when results change
    useEffect(() => {
      rowRefs.current = [];
    }, [items.length]);

    useEffect(() => {
      if (highlightIndex >= items.length) setHighlightIndex(0);
    }, [items.length, highlightIndex]);

    // Sync display + selectedProduct from value
    useEffect(() => {
      if (!value) {
        setDisplay("");
        setSelectedProduct(null);
        return;
      }
      if (typeof value === "object") {
        setDisplay(value?.name || "");
        setSelectedProduct(value);
        return;
      }
      const selected = items.find((p) => p?.id === value);
      if (selected) {
        setDisplay(selected.name || "");
        setSelectedProduct(selected);
      }
    }, [value, items]);

    useImperativeHandle(ref, () => ({
      focus: () => triggerRef.current?.focus(),
      refresh: () => onRefreshProducts?.(search),
      openMenu: () => openModal(),
      closeMenu: () => closeModal(),
    }));

    const openModal = (seedChar) => {
      triggerRef.current?.blur?.();
      setIsOpen(true);
      setHighlightIndex(0);
      setSearch(
        typeof seedChar === "string" && seedChar.length === 1
          ? (search + seedChar).toLowerCase() // Append character to existing search
          : (display || "")
      );
    };
    const closeModal = () => setIsOpen(false);

    useEffect(() => {
      if (!isOpen) return;
      const prev = document.body.style.overflow;
      document.body.style.overflow = "hidden";
      return () => (document.body.style.overflow = prev);
    }, [isOpen]);

    useEffect(() => {
      if (!isOpen) return;
      setTimeout(() => {
        searchRef.current?.focus();
        searchRef.current?.select?.();
      }, 50);
    }, [isOpen]);

    useEffect(() => {
      if (isOpen && onRefreshProducts && !didRefreshRef.current) {
        didRefreshRef.current = true;
        Promise.resolve(onRefreshProducts(search)).catch(() => {});
      }
      if (!isOpen) didRefreshRef.current = false;
    }, [isOpen, onRefreshProducts, search]);

    useEffect(() => {
      const handler = () => onRefreshProducts?.(search);
      window.addEventListener("product:created", handler);
      return () => window.removeEventListener("product:created", handler);
    }, [onRefreshProducts, search]);

    useEffect(() => {
      if (!onRefreshProducts || !isOpen) return;
      if (debounceRef.current) clearTimeout(debounceRef.current);
      debounceRef.current = setTimeout(() => onRefreshProducts(search), 200);
      return () => clearTimeout(debounceRef.current);
    }, [search, onRefreshProducts, isOpen]);

    // Local filter
    const filtered = useMemo(() => {
      const q = (search || "").toLowerCase().trim();
      if (!q) return items;
      const starts = (val) => (val ?? "").toString().toLowerCase().startsWith(q);
      return items.filter(
        (p) => starts(p?.name) || starts(p?.product_code) || starts(p?.barcode)
      );
    }, [items, search]);

    // Infinite scroll
    useEffect(() => {
      const container = listRef.current;
      if (!container) return;
      const handleScroll = () => {
        if (
          container.scrollTop + container.clientHeight >=
          container.scrollHeight - 80
        ) {
          onRefreshProducts?.(search);
        }
      };
      container.addEventListener("scroll", handleScroll);
      return () => container.removeEventListener("scroll", handleScroll);
    }, [onRefreshProducts, search, isOpen]);

    const getPackSize = (p) => p?.pack_size ?? p?.packSize ?? p?.packsize ?? "";
    const getSupplierName = (p) => p?.supplier?.name || p?.supplier_name || "";
    const getBrandName = (p) => p?.brand?.name || p?.brand_name || "";
const getQuantity = (p) => p?.quantity ?? p?.current_quantity ?? null;
    const getMargin = (p) =>
      p?.margin ?? p?.margin_percentage ?? p?.marginPercent ?? null;
    const getAvgPrice = (p) =>
      p?.avg_price ?? p?.average_price ?? p?.avgPrice ?? null;

    const numFmt = (v) => {
      if (v === null || v === undefined || v === "") return "—";
      const n = Number(v);
      return Number.isFinite(n) ? n.toLocaleString(undefined, { maximumFractionDigits: 3 }) : "—";
    };

    const handleSelect = (product) => {
      setDisplay(product?.name || "");
      setSelectedProduct(product);
      onChange?.(product);
      closeModal();
    };

    // Auto-scroll to active row
    useEffect(() => {
      if (!isOpen) return;
      if (highlightIndex < 0) return;
      const row = rowRefs.current[highlightIndex];
      if (row) row.scrollIntoView({ block: "nearest" });
    }, [highlightIndex, isOpen, filtered.length]);

    // Close on Escape
    useEffect(() => {
      if (!isOpen) return;
      const onEsc = (e) => {
        if (e.key === "Escape") closeModal();
      };
      window.addEventListener("keydown", onEsc);
      return () => window.removeEventListener("keydown", onEsc);
    }, [isOpen]);

    const handleModalKeyDown = (e) => {
      if (!isOpen) return;
      if (e.key === "Escape") {
        e.preventDefault();
        closeModal();
      } else if (e.key === "ArrowDown") {
        e.preventDefault();
        setHighlightIndex((i) => (i < filtered.length - 1 ? i + 1 : i));
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setHighlightIndex((i) => (i > 0 ? i - 1 : 0));
      } else if (e.key === "Enter") {
        e.preventDefault();
        if (filtered[highlightIndex]) handleSelect(filtered[highlightIndex]);
      }
    };

const handleSearchChange = (e) => {
      const val = e.target.value;
      const valid = /^[a-zA-Z0-9-.()/\s]*$/;
      if (!valid.test(val)) {
        setIsInvalidInput(true);
        setTimeout(() => setIsInvalidInput(false), 200);
        return;
      }
      setSearch(val);
      setHighlightIndex(0);
    };

    // ---------- Dragging ----------
    const startDrag = (e) => {
      if (!modalRef.current) return;
      dragRef.current = {
        isDragging: true,
        offsetX: e.clientX - windowPos.x,
        offsetY: e.clientY - windowPos.y,
      };
      document.addEventListener("mousemove", handleDrag);
      document.addEventListener("mouseup", stopDrag);
    };

    const handleDrag = (e) => {
      if (!dragRef.current.isDragging) return;
      setWindowPos({
        x: e.clientX - dragRef.current.offsetX,
        y: e.clientY - dragRef.current.offsetY,
      });
    };

    const stopDrag = () => {
      dragRef.current.isDragging = false;
      document.removeEventListener("mousemove", handleDrag);
      document.removeEventListener("mouseup", stopDrag);
    };

    // ---------- Resizing ----------
    const getResizeCursor = (direction) => {
      const cursors = {
        n: "ns-resize",
        s: "ns-resize",
        e: "ew-resize",
        w: "ew-resize",
        ne: "nesw-resize",
        sw: "nesw-resize",
        nw: "nwse-resize",
        se: "nwse-resize",
      };
      return cursors[direction] || "default";
    };

    const startResize = (e, direction) => {
      e.preventDefault();
      e.stopPropagation();
      if (!modalRef.current) return;

      const rect = modalRef.current.getBoundingClientRect();
      resizeRef.current = {
        direction,
        startX: e.clientX,
        startY: e.clientY,
        startWidth: rect.width,
        startHeight: rect.height,
        startLeft: rect.left,
        startTop: rect.top,
      };
      setIsResizing(true);
      document.body.style.userSelect = "none";
      document.body.style.cursor = getResizeCursor(direction);
      document.addEventListener("mousemove", handleResize);
      document.addEventListener("mouseup", stopResize);
    };

    const handleResize = (e) => {
      const r = resizeRef.current;
      if (!r) return;

      const dx = e.clientX - r.startX;
      const dy = e.clientY - r.startY;

      let { width, height, left, top } = {
        width: r.startWidth,
        height: r.startHeight,
        left: r.startLeft,
        top: r.startTop,
      };

      const dir = r.direction;

      if (dir.includes("e")) {
        width = Math.max(MIN_WIDTH, r.startWidth + dx);
      }
      if (dir.includes("s")) {
        height = Math.max(MIN_HEIGHT, r.startHeight + dy);
      }
      if (dir.includes("w")) {
        width = Math.max(MIN_WIDTH, r.startWidth - dx);
        left = r.startLeft + (r.startWidth - width);
      }
      if (dir.includes("n")) {
        height = Math.max(MIN_HEIGHT, r.startHeight - dy);
        top = r.startTop + (r.startHeight - height);
      }

      setWindowSize({ width, height });
      setWindowPos({ x: left, y: top });
    };

    const stopResize = () => {
      resizeRef.current = null;
      setIsResizing(false);
      document.body.style.userSelect = "";
      document.body.style.cursor = "";
      document.removeEventListener("mousemove", handleResize);
      document.removeEventListener("mouseup", stopResize);
    };

    const renderResizeHandle = (direction) => {
      const base = "absolute z-[10001]";
      const positionMap = {
        n: "top-0 left-0 w-full h-2 cursor-ns-resize",
        s: "bottom-0 left-0 w-full h-2 cursor-ns-resize",
        e: "top-0 right-0 w-2 h-full cursor-ew-resize",
        w: "top-0 left-0 w-2 h-full cursor-ew-resize",
        ne: "top-0 right-0 w-4 h-4 cursor-nesw-resize",
        nw: "top-0 left-0 w-4 h-4 cursor-nwse-resize",
        se: "bottom-0 right-0 w-4 h-4 cursor-nwse-resize",
        sw: "bottom-0 left-0 w-4 h-4 cursor-nesw-resize",
      };
      return (
        <div
          key={direction}
          className={`${base} ${positionMap[direction]}`}
          onMouseDown={(e) => startResize(e, direction)}
        />
      );
    };

    return (
      <>
{/* Trigger Input — shows selected product like other selects */}
        <div className="relative w-full">
          <input
            ref={triggerRef}
            type="text"
            value={display}
            readOnly
            placeholder="Search product…"
className={`w-full h-6 text-[11px] px-1 rounded-md text-left cursor-pointer transition-all border pr-6 ${className} ${
              selectedProduct
                ? "border-blue-400 bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-200 font-medium"
                : "border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-gray-500 dark:text-gray-400 placeholder-gray-400 dark:placeholder-gray-500"
            } focus:outline-none focus:ring-2 focus:ring-blue-400/40`}
            title={selectedProduct?.name || "Search product…"}
            onFocus={() => openModal()}
            onClick={() => openModal()}
            onKeyDown={(e) => {
              if (!e.ctrlKey && !e.metaKey && !e.altKey && e.key.length === 1) {
                e.preventDefault();
                openModal(e.key);
                return;
              }
              if (e.key === "Enter" || e.key === "ArrowDown") {
                e.preventDefault();
                openModal();
                return;
              }
              onKeyDownProp?.(e);
            }}
          />
          <CubeIcon
            className="w-3.5 h-3.5 absolute right-1.5 top-1/2 -translate-y-1/2 pointer-events-none flex-shrink-0"
          />
        </div>

{/* Modal */}
        {isOpen &&
          createPortal(
            <div
              className="fixed inset-0 z-[10000] bg-black/50"
              onKeyDown={handleModalKeyDown}
              onClick={(e) => {
                if (e.target === e.currentTarget) closeModal();
              }}
            >
              {/* Draggable + Resizable Dialog */}
              <div
                ref={modalRef}
                className="absolute bg-white dark:bg-slate-800 rounded-xl shadow-2xl ring-1 ring-slate-200/50 dark:ring-slate-700/50 flex flex-col overflow-hidden"
                style={{
                  left: `${windowPos.x}px`,
                  top: `${windowPos.y}px`,
                  width: `${windowSize.width}px`,
                  height: `${windowSize.height}px`,
                  minWidth: `${MIN_WIDTH}px`,
                  minHeight: `${MIN_HEIGHT}px`,
                  userSelect: isResizing ? "none" : undefined,
                }}
              >
                {/* Resize handles (all sides & corners) */}
                {["n", "s", "e", "w", "ne", "nw", "se", "sw"].map(renderResizeHandle)}

                {/* Header (Draggable) */}
                <div
                  className="flex items-center gap-3 px-5 py-3 bg-slate-50 dark:bg-slate-800 border-b border-slate-200 dark:border-slate-700 cursor-move select-none"
                  onMouseDown={startDrag}
                >
                  <MagnifyingGlassIcon className="w-5 h-5 text-slate-400 flex-shrink-0" />
                  <input
                    ref={searchRef}
                    type="text"
                    value={search}
                    onChange={handleSearchChange}
                    placeholder="Search by product name, code, or barcode..."
                    className={`flex-1 bg-transparent border-0 outline-none placeholder:text-slate-400 dark:placeholder:text-slate-500 text-sm text-slate-900 dark:text-slate-100 ${
                      isInvalidInput ? "animate-shake" : ""
                    }`}
                    autoFocus
                  />
                  <kbd className="text-[10px] border border-slate-200 dark:border-slate-600 rounded px-1.5 py-0.5 bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400 font-mono flex-shrink-0">
                    Esc
                  </kbd>
                </div>

                  {/* Results table */}
                  {filtered.length === 0 ? (
                    <div className="flex flex-col items-center justify-center py-14 text-slate-400">
                      <CubeIcon className="w-8 h-8 mb-2" />
                      <span className="text-sm">No products found</span>
                    </div>
                  ) : (
                    <div ref={listRef} className="max-h-[58vh] overflow-y-auto">
                      <table className="w-full border-collapse text-[12px]">
                        {/* Grouped header */}
<thead className="sticky top-0 z-10 bg-slate-100 dark:bg-slate-800 border-b border-slate-200 dark:border-slate-600">
<tr>
<th rowSpan={2} className="bg-slate-200/80 dark:bg-slate-700 px-4 py-2 text-left font-bold text-slate-600 dark:text-slate-200 w-[240px]">Product</th>
                            <th rowSpan={2} className="px-3 py-1.5 text-center font-semibold text-[10px] uppercase tracking-wide text-cyan-600 dark:text-cyan-300">Pack Size</th>
                            <th rowSpan={2} className="px-3 py-1.5 text-center font-semibold text-[10px] uppercase tracking-wide text-sky-600 dark:text-sky-300">Qty</th>
                            <th colSpan={2} className="px-2 py-1.5 text-center font-semibold text-[10px] uppercase tracking-wide text-cyan-600 dark:text-cyan-300">Purchase</th>
                            <th colSpan={2} className="px-2 py-1.5 text-center font-semibold text-[10px] uppercase tracking-wide text-green-600 dark:text-green-300">Sale</th>
                            <th className="px-3 py-1.5 text-center font-semibold text-[10px] uppercase tracking-wide text-slate-500 dark:text-slate-400">Avg</th>
                            <th className="px-3 py-1.5 text-center font-semibold text-[10px] uppercase tracking-wide text-emerald-600 dark:text-emerald-300">Mrg %</th>
                          </tr>
                          <tr>
                            <th className="px-3 py-1.5 text-center font-medium text-slate-500 dark:text-slate-400 border-l border-slate-200 dark:border-slate-600">Pack</th>
                            <th className="px-3 py-1.5 text-center font-medium text-slate-500 dark:text-slate-400">Unit</th>
                            <th className="px-3 py-1.5 text-center font-medium text-slate-500 dark:text-slate-400 border-l border-slate-200 dark:border-slate-600">Pack</th>
                            <th className="px-3 py-1.5 text-center font-medium text-slate-500 dark:text-slate-400">Unit</th>
                            <th className="px-3 py-1.5" />
                            <th className="px-3 py-1.5" />
                          </tr>
                        </thead>
                        <tbody className="text-slate-700 dark:text-slate-200">
{filtered.map((p, idx) => {
                            const active = idx === highlightIndex;
                            const qty = getQuantity(p);
                            const margin = getMargin(p);
                            const avg = getAvgPrice(p);
                            const packSizeRaw = getPackSize(p);
                            const packSizeNum = packSizeRaw !== "" && packSizeRaw != null ? Number(packSizeRaw) : null;
                            const qtyNum = qty != null ? Number(qty) : null;
                            // Low stock: on-hand quantity < pack size
                            const lowStock = qtyNum != null && packSizeNum != null && qtyNum < packSizeNum;
                            // Trend: down when below pack size, up when at/above it
                            const trend = qtyNum == null || packSizeNum == null
                              ? null
                              : (qtyNum < packSizeNum ? "down" : "up");
                            const rowCls = lowStock
                              ? active
                                ? "bg-gradient-to-r from-red-50 to-rose-50 dark:from-red-900/30 dark:to-rose-900/30"
                                : "bg-red-50/70 dark:bg-red-900/20 hover:bg-red-100/70 dark:hover:bg-red-900/30"
                              : active
                                ? "bg-gradient-to-r from-blue-50 to-indigo-50 dark:from-blue-900/30 dark:to-indigo-900/30"
                                : "hover:bg-slate-50 dark:hover:bg-slate-700/30";
                            // Text color: everything red when low stock
                            const tc = lowStock
                              ? "text-red-700 dark:text-red-300"
                              : "text-slate-700 dark:text-slate-200";
                            return (
                              <tr
                                key={p.id}
                                ref={(el) => (rowRefs.current[idx] = el)}
                                onMouseEnter={() => setHighlightIndex(idx)}
                                onClick={() => handleSelect(p)}
                                className={`cursor-pointer border-b border-slate-100 dark:border-slate-700/50 ${rowCls}`}
                                title="Select product"
                                aria-label="Select product"
                              >
{/* Product identity */}
                                <td className="px-4 py-3">
                                  <div className="min-w-0">
<div className="flex items-center gap-2 flex-wrap">
                                      <span className={`text-base leading-tight font-extrabold tracking-tight truncate ${lowStock ? "text-red-700 dark:text-red-300" : active ? "text-indigo-700 dark:text-indigo-200" : "text-slate-900 dark:text-white"}`}>
                                        {p?.name || "—"}
                                      </span>
                                      {getBrandName(p) && (
                                        <span className={`font-bold text-xs ${lowStock ? "text-red-600/80 dark:text-red-400/70" : "text-slate-500 dark:text-slate-400"}`}>
                                          {getBrandName(p)}
                                        </span>
                                      )}
                                    </div>
<div className="flex items-center gap-2 mt-0.5 flex-wrap">
                                      {getSupplierName(p) && (
                                        <span className={`font-semibold text-xs ${lowStock ? "text-red-600/80 dark:text-red-400/70" : "text-slate-500 dark:text-slate-400"}`}>
                                          • {getSupplierName(p)}
                                        </span>
                                      )}
                                    </div>
                                  </div>
                                </td>

{/* Pack Size */}
                                <td className="px-3 py-3 text-center">
                                  {getPackSize(p) ? (
                                    <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-md font-extrabold text-sm ring-1 ${lowStock ? "bg-red-100 dark:bg-red-900/40 text-red-700 dark:text-red-300 ring-red-200 dark:ring-red-800" : "bg-cyan-100 dark:bg-cyan-900/40 text-cyan-700 dark:text-cyan-300 ring-cyan-200 dark:ring-cyan-800"}`}>
                                      <TagIcon className="w-4 h-4" />
                                      {getPackSize(p)}
                                    </span>
                                  ) : (
                                    <span className={`font-bold ${lowStock ? "text-red-400 dark:text-red-500" : "text-slate-400"}`}>—</span>
                                  )}
                                </td>

{/* Qty */}
                                <td className="px-3 py-3 text-center">
                                  {qty != null ? (
                                    <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-full font-extrabold text-sm ${lowStock ? "bg-red-600 text-white dark:bg-red-500" : "bg-sky-100 dark:bg-sky-900/40 text-sky-700 dark:text-sky-300"}`}>
                                      {qty != null && trend && (
                                        <svg
                                          width="12"
                                          height="12"
                                          viewBox="0 0 24 24"
                                          fill="none"
                                          stroke="currentColor"
                                          strokeWidth="3"
                                          strokeLinecap="round"
                                          strokeLinejoin="round"
                                          className={trend === "down" ? "" : "rotate-180"}
                                        >
                                          <polyline points="6 9 12 15 18 9" />
                                        </svg>
                                      )}
                                      {qty}
                                    </span>
                                  ) : (
                                    <span className={`font-bold ${lowStock ? "text-red-400 dark:text-red-500" : "text-slate-400"}`}>—</span>
                                  )}
                                </td>

{/* Purchase prices */}
                                <td className={`px-3 py-3 text-center font-extrabold text-sm border-l border-slate-100 dark:border-slate-700 ${lowStock ? "text-red-600 dark:text-red-400" : "text-cyan-700 dark:text-cyan-300"}`}>
                                  {numFmt(p?.pack_purchase_price)}
                                </td>
                                <td className={`px-3 py-3 text-center font-bold text-sm ${lowStock ? "text-red-600/90 dark:text-red-400/90" : active ? "text-slate-800 dark:text-slate-100" : "text-slate-600 dark:text-slate-300"}`}>
                                  {numFmt(p?.unit_purchase_price)}
                                </td>

                                {/* Sale prices */}
                                <td className={`px-3 py-3 text-center font-extrabold text-sm border-l border-slate-100 dark:border-slate-700 ${lowStock ? "text-red-600 dark:text-red-400" : "text-green-700 dark:text-green-300"}`}>
                                  {numFmt(p?.pack_sale_price)}
                                </td>
                                <td className={`px-3 py-3 text-center font-bold text-sm ${lowStock ? "text-red-600/90 dark:text-red-400/90" : active ? "text-slate-800 dark:text-slate-100" : "text-slate-600 dark:text-slate-300"}`}>
                                  {numFmt(p?.unit_sale_price)}
                                </td>

                                {/* Avg */}
                                <td className={`px-3 py-3 text-center font-extrabold text-sm ${lowStock ? "text-red-600 dark:text-red-400" : active ? "text-slate-800 dark:text-slate-100" : "text-slate-700 dark:text-slate-200"}`}>
                                  {numFmt(avg)}
                                </td>

                                {/* Margin */}
                                <td className={`px-3 py-3 text-center ${lowStock ? "text-red-600 dark:text-red-400" : "text-emerald-600 dark:text-emerald-400"}`}>
                                  <span className={`inline-block px-2.5 py-1 rounded-full font-extrabold text-sm ${lowStock ? "bg-red-100 dark:bg-red-900/40" : "bg-emerald-100 dark:bg-emerald-900/40"}`}>
                                    {margin != null ? `${margin}%` : "—"}
                                  </span>
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </div>
                  )}

{/* Footer */}
                  <GlassToolbar className="items-center justify-between py-2 px-4 bg-slate-50/80 dark:bg-slate-800/80 border-t border-slate-100 dark:border-slate-700">
                    <div className="text-xs text-slate-500 dark:text-slate-400">
                      {search ? (
                        <>
                          <span className="font-bold">{filtered.length}</span> results for{" "}
                          <span className="font-medium">"{search}"</span>
                        </>
                      ) : (
                        <>All products</>
                      )}
                    </div>
                    <div className="flex items-center gap-3 text-[10px] text-slate-400">
                      <span className="flex items-center gap-1">
                        <kbd className="px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-600 text-[9px]">↑↓</kbd>
                        <span>Navigate</span>
                      </span>
                      <span className="flex items-center gap-1">
                        <kbd className="px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-600 text-[9px]">↵</kbd>
                        <span>Select</span>
                      </span>
                      <span className="flex items-center gap-1">
                        <kbd className="px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-600 text-[9px]">Esc</kbd>
                        <span>Close</span>
                      </span>
                    </div>
                  </GlassToolbar>
              </div>
            </div>,
            document.body
          )}
      </>
    );
  }
);

ProductSearchInput.displayName = "ProductSearchInput";
export default ProductSearchInput;
