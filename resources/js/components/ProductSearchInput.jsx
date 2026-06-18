import React, {
  useState,
  useEffect,
  useRef,
  forwardRef,
  useImperativeHandle,
  useMemo,
} from "react";
import { createPortal } from "react-dom";

const ProductSearchInput = forwardRef(
  ({ value, onChange, products, onRefreshProducts, onKeyDown: onKeyDownProp }, ref) => {
    const [display, setDisplay] = useState("");
    const [isOpen, setIsOpen] = useState(false);
    const [search, setSearch] = useState("");
    const [highlightIndex, setHighlightIndex] = useState(0);
    const [isInvalidInput, setIsInvalidInput] = useState(false);
    const [preferencesLoaded, setPreferencesLoaded] = useState(false);

    const [windowPos, setWindowPos] = useState(() => {
      const width = 1000;
      const height = 600;
      const x = (window.innerWidth - width) / 2;
      const y = (window.innerHeight - height) / 2;
      return { x, y };
    });

    const [windowSize, setWindowSize] = useState(() => {
      const saved = localStorage.getItem("productModalSize");
      return saved ? JSON.parse(saved) : { width: 900, height: 600 };
    });

    // Use refs to track current position/size for event handlers
    const windowPosRef = useRef(windowPos);
    const windowSizeRef = useRef(windowSize);

    // Keep refs in sync with state
    useEffect(() => {
      windowPosRef.current = windowPos;
    }, [windowPos]);

    useEffect(() => {
      windowSizeRef.current = windowSize;
    }, [windowSize]);

    // Fetch preferences from API on mount
    useEffect(() => {
      const fetchPreferences = async () => {
        const token = localStorage.getItem('token') || localStorage.getItem('api_token');
        try {
          const response = await fetch('/api/preferences', {
            headers: {
              'Authorization': `Bearer ${token}`,
              'Content-Type': 'application/json',
            },
          });
          if (response.ok) {
            const data = await response.json();
            const prefs = data.preferences || {};
            
            if (prefs.productModalPos) {
              setWindowPos(prefs.productModalPos);
            }
            if (prefs.productModalSize) {
              setWindowSize(prefs.productModalSize);
            }
          }
        } catch (error) {
          // Fallback to localStorage if API fails
          const savedPos = localStorage.getItem("productModalPos");
          if (savedPos) setWindowPos(JSON.parse(savedPos));
          const savedSize = localStorage.getItem("productModalSize");
          if (savedSize) setWindowSize(JSON.parse(savedSize));
        } finally {
          setPreferencesLoaded(true);
        }
      };
      fetchPreferences();
    }, []);

    // Save preference to API
    const savePreference = async (key, value) => {
      const token = localStorage.getItem('token') || localStorage.getItem('api_token');
      try {
        await fetch(`/api/preferences/${key}`, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ value }),
        });
      } catch (error) {
        // Fallback to localStorage if API fails
        localStorage.setItem(key, JSON.stringify(value));
      }
    };

    const triggerRef = useRef(null);
    const searchRef = useRef(null);
    const tableRef = useRef(null);
    const modalRef = useRef(null);

    const didRefreshRef = useRef(false);
    const debounceRef = useRef(null);
    const dragRef = useRef({ isDragging: false, offsetX: 0, offsetY: 0 });
    const resizeRef = useRef({ isResizing: false, startX: 0, startY: 0, startWidth: 0, startHeight: 0 });

    const items = useMemo(() => {
      if (Array.isArray(products)) return products;
      if (products && Array.isArray(products.data)) return products.data;
      return [];
    }, [products]);

    useEffect(() => {
      if (highlightIndex >= items.length) setHighlightIndex(0);
    }, [items.length, highlightIndex]);

    useEffect(() => {
      if (!value) {
        setDisplay("");
        return;
      }
      if (typeof value === "object") {
        setDisplay(value?.name || "");
        return;
      }
      const selected = items.find((p) => p?.id === value);
      if (selected) setDisplay(selected.name || "");
    }, [value, items]);

    useImperativeHandle(ref, () => ({
      focus: () => triggerRef.current?.focus(),
      refresh: () => onRefreshProducts?.(search),
      openMenu: () => openModal(),
      closeMenu: () => closeModal(),
    }));

    const openModal = (seedChar) => {
      setIsOpen(true);
      setHighlightIndex(0);
      setSearch(
        typeof seedChar === "string" && seedChar.length === 1
          ? (search + seedChar).toLowerCase()  // Append character to existing search
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
      if (isOpen) setTimeout(() => searchRef.current?.focus(), 0);
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
      const container = tableRef.current?.parentElement;
      if (!container) return;
      const handleScroll = () => {
        if (
          container.scrollTop + container.clientHeight >=
          container.scrollHeight - 50
        ) {
          onRefreshProducts?.(search);
        }
      };
      container.addEventListener("scroll", handleScroll);
      return () => container.removeEventListener("scroll", handleScroll);
    }, [onRefreshProducts, search]);

    const getPackSize = (p) => p?.pack_size ?? p?.packSize ?? p?.packsize ?? "";
    const getSupplierName = (p) => p?.supplier?.name || p?.supplier_name || "-";
    const getBrandName = (p) => p?.brand?.name || p?.brand_name || "-";
    const getMargin = (p) =>
      p?.margin ?? p?.margin_percentage ?? p?.marginPercent ?? "-";
    const getAvgPrice = (p) =>
      p?.avg_price ?? p?.average_price ?? p?.avgPrice ?? "-";

    const handleSelect = (product) => {
      setDisplay(product?.name || "");
      onChange?.(product);
      closeModal();
    };

    useEffect(() => {
      if (!isOpen) return;
      const rows = tableRef.current?.querySelectorAll("tbody tr");
      if (!rows || rows.length === 0) return;
      const el = rows[highlightIndex];
      if (el) el.scrollIntoView({ block: "nearest" });
    }, [highlightIndex, isOpen, filtered.length]);

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

    // --- Dragging Logic ---
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
      const currentPos = windowPosRef.current;
      savePreference('productModalPos', currentPos);
      localStorage.setItem("productModalPos", JSON.stringify(currentPos)); // Keep as backup
      document.removeEventListener("mousemove", handleDrag);
      document.removeEventListener("mouseup", stopDrag);
    };

    // --- Resizing Logic (all sides) ---
    const MIN_WIDTH = 600;
    const MIN_HEIGHT = 400;
    const RESIZE_HANDLE_SIZE = 8; // px (visual/active area conceptually)

    const getResizeDirection = (target) => {
      const dir = target?.getAttribute?.("data-resize-dir");
      return dir || "se";
    };

    const startResize = (e) => {
      if (!modalRef.current) return;
      const dir = getResizeDirection(e.target);

      resizeRef.current = {
        isResizing: true,
        dir,
        startX: e.clientX,
        startY: e.clientY,
        startWidth: windowSizeRef.current.width,
        startHeight: windowSizeRef.current.height,
        startLeft: windowPosRef.current.x,
        startTop: windowPosRef.current.y,
      };

      document.addEventListener("mousemove", handleResize);
      document.addEventListener("mouseup", stopResize);
    };

    const handleResize = (e) => {
      if (!resizeRef.current.isResizing) return;

      const { dir, startX, startY, startWidth, startHeight, startLeft, startTop } =
        resizeRef.current;

      const dx = e.clientX - startX;
      const dy = e.clientY - startY;

      let newLeft = startLeft;
      let newTop = startTop;
      let newWidth = startWidth;
      let newHeight = startHeight;

      // Right side
      if (dir.includes("e")) {
        newWidth = Math.max(MIN_WIDTH, startWidth + dx);
      }

      // Left side
      if (dir.includes("w")) {
        const desiredWidth = Math.max(MIN_WIDTH, startWidth - dx);
        newLeft = startLeft + (startWidth - desiredWidth);
        newWidth = desiredWidth;
      }

      // Bottom side
      if (dir.includes("s")) {
        newHeight = Math.max(MIN_HEIGHT, startHeight + dy);
      }

      // Top side
      if (dir.includes("n")) {
        const desiredHeight = Math.max(MIN_HEIGHT, startHeight - dy);
        newTop = startTop + (startHeight - desiredHeight);
        newHeight = desiredHeight;
      }

      setWindowPos({ x: newLeft, y: newTop });
      setWindowSize({ width: newWidth, height: newHeight });
    };

    const stopResize = () => {
      resizeRef.current.isResizing = false;
      const currentSize = windowSizeRef.current;
      const currentPos = windowPosRef.current;

      savePreference('productModalSize', currentSize);
      savePreference('productModalPos', currentPos);

      localStorage.setItem("productModalSize", JSON.stringify(currentSize));
      localStorage.setItem("productModalPos", JSON.stringify(currentPos));

      document.removeEventListener("mousemove", handleResize);
      document.removeEventListener("mouseup", stopResize);
    };

    return (
      <>
        {/* Trigger Input */}
        <input
          ref={triggerRef}
          type="text"
          value={display}
          readOnly
          placeholder="Search product…"
          className="border w-full h-6 text-[11px] px-1 cursor-text rounded-lg bg-white dark:bg-slate-700 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-400/40 dark:focus:ring-indigo-400/40"
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

        {/* Modal */}
        {isOpen &&
          createPortal(
            <div
              className="fixed inset-0 z-[10000] flex items-center justify-center"
              onKeyDown={handleModalKeyDown}
            >
              <div className="absolute inset-0 bg-black/40" onClick={closeModal} />

              {/* Draggable + Resizable Dialog */}
              <div
                ref={modalRef}
                className="absolute bg-white dark:bg-slate-800 rounded-xl shadow-2xl border border-gray-200 dark:border-slate-600 flex flex-col"
                style={{
                  left: `${windowPos.x}px`,
                  top: `${windowPos.y}px`,
                  width: `${windowSize.width}px`,
                  height: `${windowSize.height}px`,
                  minWidth: "600px",
                  minHeight: "400px",
                }}
                onMouseDown={(e) => {
                  // Make the whole window draggable (except interactive controls)
                  const target = e.target;
                  const tag = target?.tagName?.toLowerCase?.() || "";
                  const isInteractive =
                    tag === "input" ||
                    tag === "textarea" ||
                    tag === "select" ||
                    tag === "button" ||
                    target?.isContentEditable;

                  // Don't start drag if clicking the resize handle (handled separately)
                  if (target?.closest?.("[data-resize-handle='true']")) return;

                  // Still allow drag when clicking on the header button etc, but avoid stealing from inputs
                  if (isInteractive) return;

                  // Prevent text selection while dragging
                  e.preventDefault();
                  startDrag(e);
                }}
              >
                {/* Header (Draggable) */}
                <div
                  className="px-4 py-3 border-b flex items-center justify-between cursor-move bg-gray-50 dark:bg-slate-700 rounded-t-xl border-gray-200 dark:border-slate-600"
                  onMouseDown={startDrag}
                >
                  <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Select Product</h3>
                  <button
                    type="button"
                    onClick={closeModal}
                    className="text-xs px-2 py-1 rounded hover:bg-gray-100 dark:hover:bg-slate-600 text-gray-900 dark:text-gray-100"
                  >
                    ✕
                  </button>
                </div>

                {/* Body */}
                <div className="flex-1 overflow-hidden flex flex-col">
                  {/* Search */}
                  <div className="p-3">
                    <input
                      ref={searchRef}
                      type="text"
                      value={search}
                      onChange={handleSearchChange}
                      placeholder="Type to search… (Enter to select, Esc to close)"
                      className={`border w-full h-8 text-sm px-2 rounded-lg bg-white dark:bg-slate-700 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-400/40 dark:focus:ring-indigo-400/40 ${
                        isInvalidInput ? "animate-shake border-red-400" : ""
                      }`}
                    />
                  </div>

                  {/* Results */}
                  <div className="px-3 pb-3 flex-1 overflow-auto">
                    <div className="border rounded overflow-hidden h-full border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-800">
                      <div className="max-h-full overflow-auto">
                        <table ref={tableRef} className="w-full border-collapse text-[11px]">
                          <thead className="bg-gray-100 dark:bg-slate-700 sticky top-0">
                            <tr className="text-left text-[10px]">
                              <th colSpan="3" className="border px-1 w-1/3 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Name</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Pack Size</th>
                              <th className="border px-1 font-bold text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Quantity</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Pack Purchase</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Unit Purchase Price</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Pack Sale</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Unit Sale Price</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Supplier</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Brand</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Margin %</th>
                              <th className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">Avg. Price</th>
                            </tr>
                          </thead>
                          <tbody>
                            {filtered.map((p, idx) => (
                              <tr
                                key={p.id}
                                onClick={() => handleSelect(p)}
                                className={`cursor-pointer ${
                                  idx === highlightIndex
                                    ? "bg-green-600 text-white"
                                    : "odd:bg-white/90 even:bg-white/70 dark:odd:bg-slate-700/60 dark:even:bg-slate-800/60 hover:bg-gray-100 dark:hover:bg-slate-600"
                                }`}
                                onMouseEnter={() => setHighlightIndex(idx)}
                              >
                                <td colSpan="3" className="border px-1 text-[13px] w-1/3 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{p?.name}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{getPackSize(p)}</td>
                                <td className="border px-1 font-bold text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{p?.quantity}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{p?.pack_purchase_price}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{p?.unit_purchase_price}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{p?.pack_sale_price}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{p?.unit_sale_price}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{getSupplierName(p)}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{getBrandName(p)}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{getMargin(p)}</td>
                                <td className="border px-1 text-gray-900 dark:text-gray-100 border-gray-200 dark:border-slate-600">{getAvgPrice(p)}</td>
                              </tr>
                            ))}
                            {filtered.length === 0 && (
                              <tr>
                                <td
                                  colSpan={13}
                                  className="text-center py-6 text-gray-500 dark:text-gray-400"
                                >
                                  No products found
                                </td>
                              </tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  </div>

                  {/* Footer */}
                  <div className="px-4 py-2 border-t text-[10px] text-gray-600 dark:text-gray-400 border-gray-200 dark:border-slate-600">
                    ↑/↓ to navigate • Enter to select • Esc to close
                  </div>
                </div>

                {/* Resize Handles (all sides) */}
                <div
                  data-resize-handle="true"
                  data-resize-dir="n"
                  onMouseDown={startResize}
                  className="absolute top-1 left-1 right-1 h-2 bg-transparent cursor-n-resize"
                  title="Resize top"
                />
                <div
                  data-resize-handle="true"
                  data-resize-dir="s"
                  onMouseDown={startResize}
                  className="absolute bottom-1 left-1 right-1 h-2 bg-transparent cursor-s-resize"
                  title="Resize bottom"
                />
                <div
                  data-resize-handle="true"
                  data-resize-dir="w"
                  onMouseDown={startResize}
                  className="absolute top-1 bottom-1 left-1 w-2 bg-transparent cursor-w-resize"
                  title="Resize left"
                />
                <div
                  data-resize-handle="true"
                  data-resize-dir="e"
                  onMouseDown={startResize}
                  className="absolute top-1 bottom-1 right-1 w-2 bg-transparent cursor-e-resize"
                  title="Resize right"
                />
                <div
                  data-resize-handle="true"
                  data-resize-dir="nw"
                  onMouseDown={startResize}
                  className="absolute -top-1 -left-1 w-3 h-3 bg-transparent cursor-nw-resize"
                  title="Resize top-left"
                />
                <div
                  data-resize-handle="true"
                  data-resize-dir="ne"
                  onMouseDown={startResize}
                  className="absolute -top-1 -right-1 w-3 h-3 bg-transparent cursor-ne-resize"
                  title="Resize top-right"
                />
                <div
                  data-resize-handle="true"
                  data-resize-dir="sw"
                  onMouseDown={startResize}
                  className="absolute -bottom-1 -left-1 w-3 h-3 bg-transparent cursor-sw-resize"
                  title="Resize bottom-left"
                />
                <div
                  data-resize-handle="true"
                  data-resize-dir="se"
                  onMouseDown={startResize}
                  className="absolute -bottom-1 -right-1 w-3 h-3 bg-transparent cursor-se-resize"
                  title="Resize bottom-right"
                />
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
