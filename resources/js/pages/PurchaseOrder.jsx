// resources/js/pages/PurchaseOrder.jsx
import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  ArrowPathIcon,
  ArrowUturnLeftIcon,
  BanknotesIcon,
  BuildingStorefrontIcon,
  CalculatorIcon,
  ClipboardDocumentListIcon,
  CubeIcon,
  ExclamationTriangleIcon,
  HandRaisedIcon,
  MagnifyingGlassIcon,
  MinusIcon,
  PlayCircleIcon,
  PlusIcon,
  PrinterIcon,
  ShoppingCartIcon,
  SparklesIcon,
  TagIcon,
  TruckIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import { usePermissions } from "@/api/usePermissions.js";
import { useCurrency } from "@/context/CurrencyContext.jsx";
import BrandSearch from "../components/BrandSearch.jsx";
import SupplierSearch from "../components/SupplierSearch.jsx";
import ModernDatePicker, { toISODate } from "@/components/ModernDatePicker.jsx";
import { printHtmlViaIframe } from "@/utils/printHelper.js";
import {
  arrangePurchaseOrderRows,
  purchaseOrderArrangements,
} from "@/utils/purchaseOrderArrangement";
import {
  buildCreateOrderPayload,
  buildForecastQueryParams,
  calculateRowScrollOffset,
  finalizeForecastRowPacks,
  getForecastFilterSignature,
  updateForecastRowPacks,
} from "@/utils/purchaseOrderForecast";

const LOOKBACK_PRESETS = [
  { id: "today", label: "Today", days: 0 },
  { id: "7d", label: "7D", days: 6 },
  { id: "15d", label: "15D", days: 14 },
  { id: "30d", label: "30D", days: 29 },
  { id: "60d", label: "60D", days: 59 },
  { id: "90d", label: "90D", days: 89 },
];

function getLocalDateOffset(daysBack = 0) {
  const d = new Date();
  d.setDate(d.getDate() - daysBack);
  return toISODate(d);
}

export default function PurchaseOrder() {
  const navigate = useNavigate();
  const baseId = useId();
  const today = useMemo(() => getLocalDateOffset(0), []);
  const defaultFrom = useMemo(() => getLocalDateOffset(29), []);

  const { loading: permsLoading, has, canFor } = usePermissions?.() || {};
  const { formatCurrency } = useCurrency();

  const canView =
    typeof has === "function"
      ? has("purchase-order.view")
      : typeof canFor === "function"
      ? !!canFor("purchase-order")?.view
      : true;
  const canGenerate =
    typeof has === "function"
      ? has("purchase-order.generate")
      : typeof canFor === "function"
      ? !!canFor("purchase-order")?.create
      : true;
  const canCreate =
    typeof has === "function"
      ? has("purchase-order.create")
      : typeof canFor === "function"
      ? !!canFor("purchase-order")?.create
      : true;

  // === Forecast Query Parameters ===
  const [dateFrom, setDateFrom] = useState(defaultFrom);
  const [dateTo, setDateTo] = useState(today);
  const [projectedDays, setProjectedDays] = useState("7");
  const [safetyPacks, setSafetyPacks] = useState("1");
  const [moqPacks, setMoqPacks] = useState("0");
  const [supplier, setSupplier] = useState(null);
  const [brands, setBrands] = useState([]);
  const [deductOnOrder, setDeductOnOrder] = useState(true);
  const [includeSecondarySuppliers, setIncludeSecondarySuppliers] = useState(false);

  const [supplierSearchOpen, setSupplierSearchOpen] = useState(false);
  const [brandSearchOpen, setBrandSearchOpen] = useState(false);

  // === Forecast Results State ===
  const [rows, setRows] = useState([]);
  const [meta, setMeta] = useState(null);
  const [loading, setLoading] = useState(false);
  const [statusMessage, setStatusMessage] = useState("");
  const [appliedFilterSig, setAppliedFilterSig] = useState(null);
  const [appliedSupplier, setAppliedSupplier] = useState(null);
  const [appliedBrands, setAppliedBrands] = useState([]);

  // === Client-side Result Filters ===
  const [searchQuery, setSearchQuery] = useState("");
  const [resultSupplierKey, setResultSupplierKey] = useState("all");
  const [resultBrandKey, setResultBrandKey] = useState("all");
  const [stockFilter, setStockFilter] = useState("all");
  const [lowStockThreshold, setLowStockThreshold] = useState("10");
  const [orderFilter, setOrderFilter] = useState("all");

  const [arrangement, setArrangement] = useState(() => {
    try {
      const saved = localStorage.getItem("purchase-order-forecast-arrangement");
      return purchaseOrderArrangements.some((option) => option.id === saved)
        ? saved
        : "original";
    } catch {
      return "original";
    }
  });

  const changeArrangement = (value) => {
    setArrangement(value);
    try {
      localStorage.setItem("purchase-order-forecast-arrangement", value);
    } catch {
      /* Storage may be unavailable */
    }
  };

  // === Printer Type (a4 | thermal) ===
  const [printerType, setPrinterType] = useState("a4");

  // === Keyboard Navigation State/Refs ===
  // Track selected row by stable _rowId and only move focus/selection when explicitly navigating rows.
  // Never re-select on `rows` or `visibleRows` edits so multi-digit typing (e.g. 50, 125) works cleanly.
  const [selectedRowId, setSelectedRowId] = useState(null);
  const [pendingFocusRowId, setPendingFocusRowId] = useState(null);
  const tableWrapRef = useRef(null);
  const inputRefs = useRef({});
  const rowRefs = useRef({});

  useEffect(() => {
    axios
      .get("/api/settings")
      .then(({ data }) => {
        if (data?.printer_type) {
          setPrinterType(String(data.printer_type).toLowerCase() === "thermal" ? "thermal" : "a4");
        }
      })
      .catch(() => {});
  }, []);

  const currentFilterSig = useMemo(
    () =>
      getForecastFilterSignature({
        dateFrom,
        dateTo,
        projectedDays,
        safetyPacks,
        moqPacks,
        supplier,
        brands,
        includeSecondarySuppliers,
        deductOnOrder,
      }),
    [
      dateFrom,
      dateTo,
      projectedDays,
      safetyPacks,
      moqPacks,
      supplier,
      brands,
      includeSecondarySuppliers,
      deductOnOrder,
    ]
  );

  const isStaleResults = Boolean(appliedFilterSig && rows.length > 0 && appliedFilterSig !== currentFilterSig);

  const activePresetId = useMemo(() => {
    if (dateTo !== today) return null;
    const match = LOOKBACK_PRESETS.find((p) => getLocalDateOffset(p.days) === dateFrom);
    return match ? match.id : null;
  }, [dateFrom, dateTo, today]);

  const applyLookbackPreset = (daysBack) => {
    setDateFrom(getLocalDateOffset(daysBack));
    setDateTo(today);
  };

  // === Derived Filter Options from Current Forecast Results ===
  const availableBrands = useMemo(() => {
    const map = new Map();
    for (const r of rows) {
      const id = r.brand_id ?? null;
      const key = String(id ?? "none");
      if (!map.has(key)) {
        map.set(key, {
          key,
          id,
          name: r.brand_name || (id ? `Brand #${id}` : "No Brand"),
          count: 0,
        });
      }
      map.get(key).count += 1;
    }
    return Array.from(map.values()).sort((a, b) => a.name.localeCompare(b.name));
  }, [rows]);

  const availableSuppliers = useMemo(() => {
    const map = new Map();
    for (const r of rows) {
      const id = r.supplier_id ?? null;
      const key = String(id ?? "none");
      if (!map.has(key)) {
        map.set(key, {
          key,
          id,
          name: r.supplier_name || (id ? `Supplier #${id}` : "Unassigned Supplier"),
          count: 0,
        });
      }
      map.get(key).count += 1;
    }
    return Array.from(map.values()).sort((a, b) => a.name.localeCompare(b.name));
  }, [rows]);

  const lowThresholdNum = Math.max(0, parseInt(lowStockThreshold, 10) || 0);

  // === Filtered & Arranged Rows ===
  const visibleRows = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return arrangePurchaseOrderRows(
      rows.filter((r) => {
        if (q) {
          const haystack = `${r.product_name || ""} ${r.product_code || ""} ${r.brand_name || ""} ${r.supplier_name || ""}`.toLowerCase();
          if (!haystack.includes(q)) return false;
        }
        if (
          resultSupplierKey !== "all" &&
          String(r.supplier_id ?? "none") !== String(resultSupplierKey)
        ) {
          return false;
        }
        if (
          resultBrandKey !== "all" &&
          String(r.brand_id ?? "none") !== String(resultBrandKey)
        ) {
          return false;
        }

        const stock = Number(r.current_stock_units || 0);
        if (stockFilter === "out" && stock > 0) return false;
        if (stockFilter === "low" && stock > lowThresholdNum) return false;
        if (stockFilter === "in" && stock <= 0) return false;

        const packs = Number(r.order_packs || 0);
        if (orderFilter === "to_order" && packs <= 0) return false;
        if (orderFilter === "no_order" && packs > 0) return false;
        if (orderFilter === "demand" && Number(r.is_user_demand) !== 1) return false;
        if (orderFilter === "returned" && Number(r.has_purchase_return) !== 1) return false;

        return true;
      }),
      arrangement
    );
  }, [
    rows,
    searchQuery,
    resultSupplierKey,
    resultBrandKey,
    stockFilter,
    lowThresholdNum,
    orderFilter,
    arrangement,
  ]);

  const hasActiveResultFilters =
    searchQuery.trim() !== "" ||
    resultSupplierKey !== "all" ||
    resultBrandKey !== "all" ||
    stockFilter !== "all" ||
    orderFilter !== "all";

  const clearResultFilters = () => {
    setSearchQuery("");
    setResultSupplierKey("all");
    setResultBrandKey("all");
    setStockFilter("all");
    setOrderFilter("all");
  };

  // Rows to include in Print / Create Order: visible rows with order_packs > 0
  const orderRows = useMemo(
    () => visibleRows.filter((r) => Number(r.order_packs || 0) > 0),
    [visibleRows]
  );

  const createOrderPreview = useMemo(
    () =>
      buildCreateOrderPayload({
        orderRows,
        appliedSupplier,
        resultSupplierKey,
        availableSuppliers,
      }),
    [orderRows, appliedSupplier, resultSupplierKey, availableSuppliers]
  );

  // === KPI & Totals Summary ===
  const summary = useMemo(() => {
    let packs = 0;
    let units = 0;
    let amount = 0;
    let itemsToOrder = 0;
    let outOfStockCount = 0;
    let demandCount = 0;
    let onOrderCount = 0;

    for (const r of visibleRows) {
      const p = Number(r.order_packs || 0);
      packs += p;
      units += Number(r.order_units || 0);
      amount += Number(r.order_amount || 0);
      if (p > 0) itemsToOrder += 1;
      if (Number(r.current_stock_units || 0) <= 0) outOfStockCount += 1;
      if (Number(r.is_user_demand) === 1) demandCount += 1;
      if (Number(r.on_order_units || 0) > 0) onOrderCount += 1;
    }

    return {
      totalItems: visibleRows.length,
      itemsToOrder,
      packs,
      units,
      amount,
      outOfStockCount,
      demandCount,
      onOrderCount,
    };
  }, [visibleRows]);

  // === Keep Active / Focused Row Clear of Sticky Header & Totals Footer ===
  const ensureRowVisible = useCallback((rowId) => {
    if (!rowId) return;
    const trEl = rowRefs.current[rowId];
    const wrap = tableWrapRef.current;
    if (!trEl || !wrap) return;

    const trBox = trEl.getBoundingClientRect();
    const wrapBox = wrap.getBoundingClientRect();

    const theadEl = wrap.querySelector("thead");
    const tfootEl = wrap.querySelector("tfoot");

    const headerHeight = theadEl ? theadEl.getBoundingClientRect().height : 44;
    const footerHeight = tfootEl ? tfootEl.getBoundingClientRect().height : 54;

    const offset = calculateRowScrollOffset({
      trTop: trBox.top,
      trBottom: trBox.bottom,
      wrapTop: wrapBox.top,
      wrapBottom: wrapBox.bottom,
      headerHeight,
      footerHeight,
      padding: 12,
    });

    if (offset !== 0) {
      wrap.scrollTop += offset;
    }
  }, []);

  // === Explicit Row Focus Effect (ONLY when navigating rows via keyboard) ===
  useEffect(() => {
    if (!pendingFocusRowId) return;
    const inputEl = inputRefs.current[pendingFocusRowId];

    ensureRowVisible(pendingFocusRowId);

    if (inputEl) {
      requestAnimationFrame(() => {
        inputEl.focus({ preventScroll: true });
        inputEl.select();
        ensureRowVisible(pendingFocusRowId);
      });
    }
    setPendingFocusRowId(null);
  }, [pendingFocusRowId, ensureRowVisible]);

  // === Build Printable HTML ===
  const buildPrintHtml = useCallback(
    (type) => {
      const isA4 = type === "a4";
      const dateLabel = new Date().toLocaleDateString();
      const supplierLabel =
        createOrderPreview.supplier?.name ||
        (createOrderPreview.distinctSuppliers.length > 1
          ? `Multiple (${createOrderPreview.distinctSuppliers.length})`
          : "All Suppliers");

      const filteredBrand =
        resultBrandKey !== "all"
          ? availableBrands.find((b) => b.key === String(resultBrandKey))
          : null;
      const brandLabel = filteredBrand
        ? filteredBrand.name
        : appliedBrands.length
        ? appliedBrands.map((b) => b.label).join(", ")
        : Array.from(new Set(orderRows.map((r) => r.brand_name).filter(Boolean))).join(", ") || "—";

      const totalOrderPacks = orderRows.reduce((s, r) => s + Number(r.order_packs || 0), 0);

      const esc = (s) =>
        String(s ?? "")
          .replace(/&/g, "&amp;")
          .replace(/</g, "&lt;")
          .replace(/>/g, "&gt;")
          .replace(/"/g, "&quot;");

      const rowsHtml = orderRows
        .map(
          (r) => `
          <tr>
            <td class="name">${esc(r.product_name)}</td>
            <td class="qty">${Number(r.order_packs || 0)}</td>
          </tr>`
        )
        .join("");

      const thermalCss = `
        @page { size: 80mm auto; margin: 0; }
        * { box-sizing: border-box; }
        body { margin:0; padding:0; color:#000; background:#fff; font-family:'Courier New',monospace; font-weight:700; font-size:12px; line-height:1.35; -webkit-print-color-adjust:exact; print-color-adjust:exact; }
        .receipt { width:80mm; max-width:100%; margin:0 auto; padding:8px 6px; }
        .store { font-size:18px; text-align:center; }
        .meta { text-align:center; font-size:10px; }
        .hr { border-top:1px dashed #000; margin:5px 0; }
        .double-hr { border-top:2px double #000; margin:6px 0; }
        .pair { display:flex; justify-content:space-between; }
        .pair + .pair { margin-top:2px; }
        table { width:100%; border-collapse:collapse; }
        thead th { text-align:left; padding:2px 0; border-bottom:1px solid #000; }
        tbody td { padding:2px 0; border-bottom:1px solid #000; }
        td.qty, th.qty { text-align:right; white-space:nowrap; }
        .total { font-size:13px; font-weight:bold; margin-top:4px; display:flex; justify-content:space-between; }
        .foot { margin-top:8px; text-align:center; font-size:9px; }
      `;

      const a4Css = `
        @page { size: A4; margin: 14mm; }
        * { box-sizing: border-box; }
        body { margin:0; padding:0; color:#111; background:#fff; font-family:Arial,Helvetica,sans-serif; }
        .page { width:100%; }
        .header { display:flex; align-items:center; gap:14px; border-bottom:1px solid #ccc; padding-bottom:12px; }
        .store h1 { margin:0; font-size:20px; letter-spacing:.4px; }
        .store .meta { margin-top:4px; font-size:12px; color:#666; line-height:1.4; }
        .title-row { display:flex; justify-content:space-between; align-items:baseline; margin:16px 0 6px; }
        .title-row .title { font-size:18px; font-weight:700; }
        .grid { display:grid; grid-template-columns:repeat(3,1fr); gap:6px 16px; font-size:12px; margin-bottom:10px; }
        .grid .lbl { color:#666; }
        .grid .val { font-weight:600; }
        table { width:100%; border-collapse:collapse; margin-top:8px; font-size:12px; }
        thead th { text-align:left; border-bottom:1px solid #ccc; padding:7px 6px; }
        tbody td { border-bottom:1px dashed #e0e0e0; padding:7px 6px; }
        td.qty, th.qty { text-align:right; }
        .footer-total { width:45%; margin-left:auto; border:1px solid #ccc; border-radius:6px; overflow:hidden; margin-top:14px; }
        .footer-total .row { display:flex; justify-content:space-between; padding:9px 12px; }
        .footer-total .row.total { font-weight:800; }
        .foot { margin-top:24px; text-align:center; font-size:12px; color:#666; }
      `;

      return `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8"/>
<title>Purchase Order Forecast</title>
<style>${isA4 ? a4Css : thermalCss}</style>
</head>
<body>
${
  isA4
    ? `
<div class="page">
  <div class="header">
    <div class="store">
      <h1>Purchase Order Forecast</h1>
      <div class="meta">${esc(supplierLabel)}</div>
    </div>
  </div>
  <div class="title-row">
    <div class="title">Order List</div>
    <div class="meta">Date: ${dateLabel}</div>
  </div>
  <div class="grid">
    <div><span class="lbl">Supplier:</span> <span class="val">${esc(supplierLabel)}</span></div>
    <div><span class="lbl">Brand:</span> <span class="val">${esc(brandLabel)}</span></div>
    <div><span class="lbl">Items:</span> <span class="val">${orderRows.length}</span></div>
  </div>
  <table>
    <thead>
      <tr>
        <th style="width:85%">Product</th>
        <th class="qty" style="width:15%">Order Packs</th>
      </tr>
    </thead>
    <tbody>${rowsHtml}</tbody>
  </table>
  <div class="footer-total">
    <div class="row total"><div>Total Packs</div><div>${totalOrderPacks}</div></div>
  </div>
  <div class="foot">Generated on ${dateLabel}</div>
</div>
`
    : `
<div class="receipt">
  <div class="store">PURCHASE ORDER</div>
  <div class="meta">${esc(supplierLabel)}</div>
  <div class="hr"></div>
  <div class="pair"><div>Date</div><div>${dateLabel}</div></div>
  <div class="pair"><div>Brand</div><div>${esc(brandLabel)}</div></div>
  <div class="pair"><div>Items</div><div>${orderRows.length}</div></div>
  <div class="double-hr"></div>
  <table>
    <thead>
      <tr>
        <th>Product</th>
        <th class="qty">Packs</th>
      </tr>
    </thead>
    <tbody>${rowsHtml}</tbody>
  </table>
  <div class="hr"></div>
  <div class="total"><div>TOTAL PACKS</div><div>${totalOrderPacks}</div></div>
  <div class="foot">Generated on ${dateLabel}</div>
</div>
`
}
</body>
</html>`;
    },
    [createOrderPreview, resultBrandKey, availableBrands, appliedBrands, orderRows]
  );

  const doPrint = useCallback(
    async (type = printerType) => {
      if (!canView) return toast.error("You don't have permission to view/print.");
      if (!rows.length) return toast.error("Nothing to print. Generate a forecast first.");
      if (!orderRows.length) return toast.error("No products with Order Packs > 0 to print.");

      const html = buildPrintHtml(type);
      await printHtmlViaIframe(html);
    },
    [canView, rows.length, orderRows.length, buildPrintHtml, printerType]
  );

  // === Fetch Forecast ===
  const handleFetch = useCallback(
    async (e) => {
      if (e?.preventDefault) e.preventDefault();
      if (!canGenerate) return toast.error("You don't have permission to generate a forecast.");
      if (!dateFrom || !dateTo) return toast.error("Please select both From and To dates.");
      if (dateTo < dateFrom) return toast.error("To date must be on or after From date.");

      const projNum = parseInt(projectedDays, 10);
      if (!projNum || projNum < 1) return toast.error("Projected Days must be at least 1.");

      const safetyNum = Math.max(0, parseInt(safetyPacks, 10) || 0);
      const moqNum = Math.max(0, parseInt(moqPacks, 10) || 0);

      setLoading(true);
      setStatusMessage("Generating smart reorder forecast…");
      try {
        const queryParams = buildForecastQueryParams({
          dateFrom,
          dateTo,
          projectedDays: projNum,
          safetyPacks: safetyNum,
          moqPacks: moqNum,
          supplier,
          brands,
          includeSecondarySuppliers,
          deductOnOrder,
        });

        const { data } = await axios.get("/api/purchase-orders/forecast", {
          params: queryParams,
          cache: false,
        });

        const mapped = (data.items || []).map((r, idx) => {
          const order_packs = Math.max(0, Number(r.suggested_packs ?? 0));
          const pack_size = Math.max(1, Number(r.pack_size ?? 1));
          const pack_price = Number(r.pack_price ?? 0);
          return {
            ...r,
            pack_size,
            pack_price,
            order_packs,
            order_packs_input: String(order_packs),
            order_units: order_packs * pack_size,
            order_amount: order_packs * pack_price,
            _rowId: `${r.product_id}-${idx}`,
          };
        });

        setRows(mapped);
        setMeta(data.meta || null);
        setAppliedFilterSig(currentFilterSig);
        setAppliedSupplier(supplier);
        setAppliedBrands(brands);
        setSelectedRowId(mapped.length ? mapped[0]._rowId : null);
        setPendingFocusRowId(null);

        // Reset client-side filters for the fresh result set
        setSearchQuery("");
        setResultSupplierKey("all");
        setResultBrandKey("all");
        setStockFilter("all");
        setOrderFilter("all");

        const toOrderCount = mapped.filter((x) => x.order_packs > 0).length;
        setStatusMessage(
          `Forecast ready: ${mapped.length} item(s) analyzed, ${toOrderCount} suggested to order.`
        );
        toast.success(`Forecast ready (${mapped.length} items)`);
      } catch (err) {
        console.error(err);
        const msg =
          Object.values(err?.response?.data?.errors || {})
            .flat()
            .join(" ") ||
          err?.response?.data?.message ||
          "Failed to load forecast.";
        setStatusMessage(msg);
        toast.error(msg);
      } finally {
        setLoading(false);
      }
    },
    [
      canGenerate,
      dateFrom,
      dateTo,
      projectedDays,
      safetyPacks,
      moqPacks,
      supplier,
      brands,
      includeSecondarySuppliers,
      deductOnOrder,
      currentFilterSig,
    ]
  );

  // Ctrl+Enter: generate forecast
  useEffect(() => {
    const onKey = (e) => {
      if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey) return;
      if (e.key !== "Enter") return;
      if (!canGenerate || loading) return;
      e.preventDefault();
      handleFetch();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [canGenerate, loading, handleFetch]);

  // Ctrl+P: print
  useEffect(() => {
    const onKey = (e) => {
      if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey) return;
      if ((e.key || "").toLowerCase() !== "p") return;
      e.preventDefault();
      doPrint();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [doPrint]);

  const pruneNoPackPrice = () => {
    if (!rows.length) return;
    const before = rows.length;
    const kept = rows.filter((r) => Number(r.pack_price || r.pack_purchase_price || 0) > 0);
    const removed = before - kept.length;
    setRows(kept);
    if (selectedRowId && !kept.some((r) => r._rowId === selectedRowId)) {
      setSelectedRowId(kept.length ? kept[0]._rowId : null);
    }
    if (removed > 0) {
      toast.success(`Removed ${removed} zero-price item(s).`);
    } else {
      toast("All items already have a pack purchase price.");
    }
  };

  const resetToSuggested = () => {
    if (!rows.length) return;
    setRows((prev) =>
      prev.map((r) => {
        const packs = Math.max(0, Number(r.suggested_packs || 0));
        const packSize = Math.max(1, Number(r.pack_size || 1));
        const packPrice = Number(r.pack_price || 0);
        return {
          ...r,
          order_packs: packs,
          order_packs_input: String(packs),
          order_units: packs * packSize,
          order_amount: packs * packPrice,
        };
      })
    );
    toast.success("Restored all quantities to suggested packs.");
  };

  const resetParameters = () => {
    setDateFrom(defaultFrom);
    setDateTo(today);
    setProjectedDays("7");
    setSafetyPacks("1");
    setMoqPacks("0");
    setSupplier(null);
    setBrands([]);
    setDeductOnOrder(true);
    setIncludeSecondarySuppliers(false);
  };

  const handleOrderPacksChange = (rowId, rawValue) => {
    setRows((prev) => updateForecastRowPacks(prev, rowId, rawValue));
  };

  const handleOrderPacksBlur = (rowId) => {
    setRows((prev) => finalizeForecastRowPacks(prev, rowId));
  };

  const stepOrderPacks = (rowId, delta) => {
    setRows((prev) => {
      const target = prev.find((r) => r._rowId === rowId);
      if (!target) return prev;
      const nextPacks = Math.max(0, Number(target.order_packs || 0) + delta);
      return updateForecastRowPacks(prev, rowId, String(nextPacks));
    });
  };

  const handleRowInputKeyDown = (e, idx) => {
    if (e.nativeEvent?.isComposing) return;
    if (e.key === "ArrowDown" || e.key === "Enter") {
      e.preventDefault();
      const nextIdx = Math.min(idx + 1, visibleRows.length - 1);
      const nextRow = visibleRows[nextIdx];
      if (nextRow && nextIdx !== idx) {
        setSelectedRowId(nextRow._rowId);
        setPendingFocusRowId(nextRow._rowId);
      }
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      const prevIdx = Math.max(idx - 1, 0);
      const prevRow = visibleRows[prevIdx];
      if (prevRow && prevIdx !== idx) {
        setSelectedRowId(prevRow._rowId);
        setPendingFocusRowId(prevRow._rowId);
      }
    }
  };

  const handleCreateOrder = () => {
    if (!orderRows.length) {
      toast.error("No items with Order Packs > 0 to create a Purchase Order.");
      return;
    }

    if (isStaleResults) {
      toast.error(
        "Your Supplier/Brand/Date filters changed since the last forecast. Click Generate Forecast first so the order matches your current selection."
      );
      return;
    }

    if (createOrderPreview.hasMultipleSuppliers) {
      const proceed = window.confirm(
        `These ${orderRows.length} items come from ${createOrderPreview.distinctSuppliers.length} different suppliers (${createOrderPreview.distinctSuppliers
          .slice(0, 4)
          .map((s) => s.name)
          .join(", ")}${createOrderPreview.distinctSuppliers.length > 4 ? "…" : ""}).\n\nA Purchase Order belongs to a single supplier. Filter by one supplier first to avoid mixing items, or click OK to continue anyway.`
      );
      if (!proceed) return;
    }

    navigate("/purchase-orders/create", {
      state: {
        items: createOrderPreview.items,
        supplier: createOrderPreview.supplier,
      },
    });
  };

  if (permsLoading) {
    return <div className="p-6 text-sm text-[var(--workspace-muted)]">Loading permissions…</div>;
  }
  if (!canView) {
    return (
      <div className="p-6 text-sm text-red-600 dark:text-red-400">
        You don&apos;t have permission to view Purchase Order (Forecast).
      </div>
    );
  }

  const fromDateId = `${baseId}-date-from`;
  const toDateId = `${baseId}-date-to`;
  const projDaysId = `${baseId}-proj-days`;
  const safetyId = `${baseId}-safety-packs`;
  const moqId = `${baseId}-moq-packs`;
  const deductPoId = `${baseId}-deduct-po`;
  const altSupId = `${baseId}-alt-sup`;
  const resultsHeadingId = `${baseId}-results-heading`;
  const searchInputId = `${baseId}-search-results`;
  const arrangeSelectId = `${baseId}-arrange`;

  return (
    <div className="purchase-order-page space-y-4 print:p-0">
      {/* Screen reader status announcements */}
      <div className="sr-only" role="status" aria-live="polite">
        {statusMessage}
      </div>

      {/* Hero Header Card (matches PurchaseOrders.jsx) */}
      <div className="purchase-order-header purchase-order-heading g-card p-4 sm:p-5 flex flex-wrap justify-between items-center gap-4">
        <div className="flex items-center gap-3.5 min-w-0">
          <div className="p-3 rounded-2xl bg-gradient-to-tr from-blue-600 to-indigo-600 text-white shadow-md shrink-0">
            <CalculatorIcon className="h-6 w-6" aria-hidden="true" />
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2.5 flex-wrap">
              <h1 className="text-xl font-bold text-gray-900 dark:text-gray-100 leading-tight">
                Smart Reorder Forecast
              </h1>
              <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-blue-100 text-blue-800 dark:bg-blue-950/60 dark:text-blue-300">
                {loading
                  ? "Calculating…"
                  : rows.length > 0
                  ? `${summary.itemsToOrder} of ${rows.length} to order`
                  : "Ready"}
              </span>
              {appliedSupplier && rows.length > 0 && (
                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300">
                  <BuildingStorefrontIcon className="w-3.5 h-3.5" aria-hidden="true" />
                  <span>{appliedSupplier.label}</span>
                </span>
              )}
              {appliedBrands.length > 0 && rows.length > 0 && (
                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium bg-violet-100 text-violet-800 dark:bg-violet-950/60 dark:text-violet-300">
                  <TagIcon className="w-3.5 h-3.5" aria-hidden="true" />
                  <span>
                    {appliedBrands.length === 1
                      ? appliedBrands[0].label
                      : `${appliedBrands.length} Brands`}
                  </span>
                </span>
              )}
            </div>
            <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
              Calculate exact reorder quantities from net sales velocity, pending shortage demands, and open purchase orders.
            </p>
          </div>
        </div>

        <div className="purchase-order-actions flex flex-wrap items-center gap-2 print:hidden">
          <Link
            to="/purchase-orders"
            className="g-btn-ghost theme-btn-tertiary inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg"
          >
            <ClipboardDocumentListIcon className="w-4 h-4" aria-hidden="true" />
            <span>Orders &amp; History</span>
          </Link>

          <div className="flex items-center gap-1.5" role="group" aria-label="Print forecast">
            <div className="purchase-order-print-formats" role="group" aria-label="Print format">
              {["a4", "thermal"].map((t) => (
                <button
                  key={t}
                  type="button"
                  onClick={() => setPrinterType(t)}
                  aria-pressed={printerType === t}
                  className="purchase-order-print-option cursor-pointer"
                  title={t === "a4" ? "A4 paper format" : "Thermal 80mm receipt format"}
                >
                  {t === "a4" ? "A4" : "Thermal"}
                </button>
              ))}
            </div>
            <button
              type="button"
              onClick={() => doPrint()}
              disabled={!orderRows.length}
              className="g-btn-secondary theme-btn-secondary inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg disabled:opacity-50"
              title={`Print ${printerType === "thermal" ? "Thermal (80mm)" : "A4"} (Ctrl+P)`}
            >
              <PrinterIcon className="w-4 h-4" aria-hidden="true" />
              <span>Print</span>
            </button>
          </div>

          {canCreate && (
            <button
              type="button"
              onClick={handleCreateOrder}
              disabled={!orderRows.length}
              className="g-btn-primary theme-btn-primary inline-flex items-center gap-1.5 px-4 py-2 text-xs font-bold rounded-lg shadow-sm disabled:opacity-50"
              title={
                orderRows.length
                  ? `Create Purchase Order with ${orderRows.length} item(s) (${summary.packs} packs)`
                  : "Generate a forecast with Order Packs > 0 first"
              }
            >
              <ShoppingCartIcon className="w-4 h-4" aria-hidden="true" />
              <span>
                Create Order from Forecast
                {orderRows.length > 0 ? ` (${orderRows.length})` : ""}
              </span>
            </button>
          )}
        </div>
      </div>

      {/* Forecast Parameters Form Card */}
      <form
        onSubmit={handleFetch}
        aria-label="Forecast parameters"
        className="purchase-order-filters g-card p-4 sm:p-5 space-y-4 print:hidden"
      >
        {/* Top Row: Quick Lookback Presets + Calculation Options */}
        <div className="flex flex-wrap items-center justify-between gap-3 pb-3 border-b border-[var(--workspace-border)]">
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400 mr-1">
              Sales Window:
            </span>
            {LOOKBACK_PRESETS.map((preset) => (
              <button
                key={preset.id}
                type="button"
                onClick={() => applyLookbackPreset(preset.days)}
                aria-pressed={activePresetId === preset.id}
                className={`px-2.5 py-1 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                  activePresetId === preset.id
                    ? "theme-btn-primary shadow-xs"
                    : "bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_10%,transparent)] text-[var(--workspace-ink)] border border-[var(--workspace-border)]"
                }`}
              >
                {preset.label}
              </button>
            ))}
          </div>

          <div className="flex flex-wrap items-center gap-4 text-xs text-gray-700 dark:text-gray-300">
            <div className="inline-flex items-center gap-2">
              <input
                id={deductPoId}
                name="deduct_on_order"
                type="checkbox"
                checked={deductOnOrder}
                onChange={(e) => setDeductOnOrder(e.target.checked)}
                className="w-4 h-4 rounded accent-[var(--workspace-accent)] cursor-pointer"
              />
              <label
                htmlFor={deductPoId}
                className="font-medium cursor-pointer select-none"
                title="Subtract remaining unreceived units from open Purchase Orders so you don't double-order"
              >
                Deduct open PO stock
              </label>
            </div>

            <div className="inline-flex items-center gap-2">
              <input
                id={altSupId}
                name="include_secondary_suppliers"
                type="checkbox"
                checked={includeSecondarySuppliers}
                onChange={(e) => setIncludeSecondarySuppliers(e.target.checked)}
                className="w-4 h-4 rounded accent-[var(--workspace-accent)] cursor-pointer"
              />
              <label
                htmlFor={altSupId}
                className="font-medium cursor-pointer select-none"
                title="When unchecked, Supplier filter strictly matches each product's primary supplier"
              >
                Include alternate suppliers
              </label>
            </div>
          </div>
        </div>

        {/* Main Parameters Grid */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-12 gap-3">
          <div className="lg:col-span-2 flex flex-col gap-1">
            <label
              htmlFor={fromDateId}
              className="text-[11px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400"
            >
              From Date
            </label>
            <ModernDatePicker
              id={fromDateId}
              name="date_from"
              ariaLabel="Forecast from date"
              value={dateFrom}
              onChange={(val) => setDateFrom(val)}
              selectsStart
              startDate={dateFrom}
              endDate={dateTo}
              maxDate={dateTo || undefined}
              isClearable={false}
              size="sm"
            />
          </div>

          <div className="lg:col-span-2 flex flex-col gap-1">
            <label
              htmlFor={toDateId}
              className="text-[11px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400"
            >
              To Date
            </label>
            <ModernDatePicker
              id={toDateId}
              name="date_to"
              ariaLabel="Forecast to date"
              value={dateTo}
              onChange={(val) => setDateTo(val)}
              selectsEnd
              startDate={dateFrom}
              endDate={dateTo}
              minDate={dateFrom || undefined}
              isClearable={false}
              size="sm"
            />
          </div>

          <div className="lg:col-span-1 flex flex-col gap-1">
            <label
              htmlFor={projDaysId}
              className="text-[11px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400"
            >
              Cover Days
            </label>
            <input
              id={projDaysId}
              name="projected_days"
              type="text"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="off"
              required
              value={projectedDays}
              onChange={(e) => setProjectedDays(e.target.value.replace(/[^\d]/g, ""))}
              onBlur={() => setProjectedDays(String(Math.max(1, parseInt(projectedDays, 10) || 7)))}
              className="w-full h-9 px-2.5 rounded-lg border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] text-xs font-semibold tabular-nums text-gray-900 dark:text-gray-100 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)]"
            />
          </div>

          <div className="lg:col-span-1 flex flex-col gap-1">
            <label
              htmlFor={safetyId}
              className="text-[11px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400"
            >
              Safety (P)
            </label>
            <input
              id={safetyId}
              name="safety_packs"
              type="text"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="off"
              value={safetyPacks}
              onChange={(e) => setSafetyPacks(e.target.value.replace(/[^\d]/g, ""))}
              onBlur={() => setSafetyPacks(String(Math.max(0, parseInt(safetyPacks, 10) || 0)))}
              className="w-full h-9 px-2.5 rounded-lg border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] text-xs font-semibold tabular-nums text-gray-900 dark:text-gray-100 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)]"
            />
          </div>

          <div className="lg:col-span-1 flex flex-col gap-1">
            <label
              htmlFor={moqId}
              className="text-[11px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400"
            >
              MOQ (P)
            </label>
            <input
              id={moqId}
              name="moq_packs"
              type="text"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="off"
              value={moqPacks}
              onChange={(e) => setMoqPacks(e.target.value.replace(/[^\d]/g, ""))}
              onBlur={() => setMoqPacks(String(Math.max(0, parseInt(moqPacks, 10) || 0)))}
              className="w-full h-9 px-2.5 rounded-lg border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] text-xs font-semibold tabular-nums text-gray-900 dark:text-gray-100 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)]"
            />
          </div>

          {/* Supplier Selector */}
          <div className="lg:col-span-3 flex flex-col gap-1">
            <span className="text-[11px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400">
              Supplier
            </span>
            <div className="w-full h-9 rounded-lg border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] flex items-center gap-1 overflow-hidden">
              <button
                type="button"
                onClick={() => setSupplierSearchOpen(true)}
                className="flex-1 h-full px-3 text-left text-xs flex items-center gap-2 min-w-0 cursor-pointer hover:bg-[color-mix(in_srgb,var(--workspace-accent)_6%,transparent)] transition-colors"
                title={supplier?.label || "Select a supplier to filter forecast"}
              >
                <BuildingStorefrontIcon
                  className={`w-4 h-4 shrink-0 ${
                    supplier ? "text-emerald-600 dark:text-emerald-400" : "text-gray-400"
                  }`}
                  aria-hidden="true"
                />
                {supplier ? (
                  <span className="truncate font-semibold text-gray-900 dark:text-gray-100">
                    {supplier.label}
                  </span>
                ) : (
                  <span className="truncate text-gray-400 dark:text-gray-500">
                    All suppliers (click to filter)…
                  </span>
                )}
              </button>
              {supplier && (
                <button
                  type="button"
                  onClick={() => setSupplier(null)}
                  className="h-full px-2 flex items-center justify-center text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 cursor-pointer shrink-0"
                  title="Clear supplier"
                  aria-label="Clear supplier"
                >
                  <XMarkIcon className="w-4 h-4" aria-hidden="true" />
                </button>
              )}
            </div>
          </div>

          {/* Multi-Brand Selector */}
          <div className="lg:col-span-2 flex flex-col gap-1">
            <span className="text-[11px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400">
              Brands
            </span>
            <div className="w-full min-h-9 rounded-lg border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] p-1 flex flex-wrap items-center gap-1">
              {brands.length === 0 ? (
                <button
                  type="button"
                  onClick={() => setBrandSearchOpen(true)}
                  className="flex-1 h-7 px-2 text-left text-xs flex items-center gap-2 min-w-0 text-gray-400 dark:text-gray-500 cursor-pointer hover:text-gray-700 dark:hover:text-gray-200"
                  title="Select one or more brands"
                >
                  <TagIcon className="w-4 h-4 shrink-0" aria-hidden="true" />
                  <span className="truncate">All brands…</span>
                </button>
              ) : (
                <>
                  {brands.map((b) => (
                    <span
                      key={b.value}
                      className="inline-flex items-center gap-1 h-7 pl-2 pr-1 rounded-md bg-violet-100 dark:bg-violet-950/60 text-violet-800 dark:text-violet-300 text-[11px] font-semibold max-w-full"
                      title={b.label}
                    >
                      <span className="truncate max-w-[92px]">{b.label}</span>
                      <button
                        type="button"
                        onClick={() =>
                          setBrands((prev) => prev.filter((x) => x.value !== b.value))
                        }
                        className="p-0.5 rounded hover:bg-violet-200 dark:hover:bg-violet-800 transition-colors shrink-0 cursor-pointer"
                        title={`Remove ${b.label}`}
                        aria-label={`Remove ${b.label}`}
                      >
                        <XMarkIcon className="w-3 h-3" aria-hidden="true" />
                      </button>
                    </span>
                  ))}
                  <button
                    type="button"
                    onClick={() => setBrandSearchOpen(true)}
                    className="h-7 w-7 inline-flex items-center justify-center rounded-md text-violet-600 dark:text-violet-400 hover:bg-violet-100 dark:hover:bg-violet-900/40 transition-colors shrink-0 cursor-pointer"
                    title="Add more brands"
                    aria-label="Add more brands"
                  >
                    <PlusIcon className="w-3.5 h-3.5" aria-hidden="true" />
                  </button>
                  <button
                    type="button"
                    onClick={() => setBrands([])}
                    className="h-7 w-7 inline-flex items-center justify-center rounded-md text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 transition-colors shrink-0 cursor-pointer"
                    title="Clear all brands"
                    aria-label="Clear all brands"
                  >
                    <XMarkIcon className="w-3.5 h-3.5" aria-hidden="true" />
                  </button>
                </>
              )}
            </div>
          </div>
        </div>

        {/* Form Submit & Utility Actions */}
        <div className="flex flex-wrap items-center justify-between gap-3 pt-2 border-t border-[var(--workspace-border)]">
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="submit"
              disabled={loading || !canGenerate}
              className="g-btn-primary theme-btn-primary inline-flex items-center gap-2 px-4 py-2 text-xs font-bold rounded-lg shadow-sm disabled:opacity-50"
              title={!canGenerate ? "Not permitted" : "Generate forecast (Ctrl+Enter)"}
            >
              {loading ? (
                <ArrowPathIcon className="w-4 h-4 animate-spin" aria-hidden="true" />
              ) : (
                <PlayCircleIcon className="w-4 h-4" aria-hidden="true" />
              )}
              <span>{loading ? "Generating Forecast…" : "Generate Forecast"}</span>
              <kbd className="ml-0.5 px-1.5 py-0.5 text-[10px] font-mono bg-white/15 rounded">
                Ctrl+↵
              </kbd>
            </button>

            <button
              type="button"
              onClick={pruneNoPackPrice}
              disabled={!rows.length}
              className="g-btn-secondary theme-btn-secondary inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg disabled:opacity-50"
              title="Remove items that have zero pack purchase price"
            >
              <XMarkIcon className="w-4 h-4" aria-hidden="true" />
              <span>Exclude Zero-Price</span>
            </button>

            <button
              type="button"
              onClick={resetToSuggested}
              disabled={!rows.length}
              className="g-btn-ghost theme-btn-tertiary inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg disabled:opacity-50"
              title="Reset all edited Order Packs back to backend suggested quantities"
            >
              <SparklesIcon className="w-4 h-4" aria-hidden="true" />
              <span>Use Suggested</span>
            </button>
          </div>

          <button
            type="button"
            onClick={resetParameters}
            className="text-xs font-medium text-gray-500 dark:text-gray-400 hover:text-[var(--workspace-accent)] inline-flex items-center gap-1 cursor-pointer"
          >
            <ArrowPathIcon className="w-3.5 h-3.5" aria-hidden="true" />
            <span>Reset Parameters</span>
          </button>
        </div>
      </form>

      {/* Stale Filter Banner */}
      {isStaleResults && (
        <div
          role="alert"
          className="g-card p-3.5 border-l-4 border-l-amber-500 bg-amber-50/60 dark:bg-amber-950/30 flex flex-wrap items-center justify-between gap-3 print:hidden"
        >
          <div className="flex items-center gap-2.5 text-xs text-amber-900 dark:text-amber-200">
            <ExclamationTriangleIcon className="w-5 h-5 text-amber-600 dark:text-amber-400 shrink-0" aria-hidden="true" />
            <span>
              <strong>Filters changed since last forecast.</strong> The table below still shows the previous results. Regenerate before creating a Purchase Order.
            </span>
          </div>
          <button
            type="button"
            onClick={() => handleFetch()}
            disabled={loading}
            className="g-btn-primary theme-btn-primary inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-lg shrink-0"
          >
            <ArrowPathIcon className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`} aria-hidden="true" />
            <span>Refresh Forecast</span>
          </button>
        </div>
      )}

      {/* 4 Summary KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3.5">
        <div className="g-card p-4 border-l-4 border-l-[var(--workspace-accent)] flex flex-col justify-between">
          <div className="flex items-center justify-between mb-1">
            <span className="text-xs font-semibold uppercase tracking-wider text-[var(--workspace-accent)]">
              Items to Order
            </span>
            <div className="p-2 rounded-lg bg-[color-mix(in_srgb,var(--workspace-accent)_12%,transparent)] text-[var(--workspace-accent)]">
              <ShoppingCartIcon className="w-4 h-4" aria-hidden="true" />
            </div>
          </div>
          <div>
            <p className="text-2xl font-bold font-mono tabular-nums text-gray-900 dark:text-gray-100">
              {summary.itemsToOrder.toLocaleString()}
              <span className="text-sm font-medium text-gray-400 dark:text-gray-500 ml-1">
                / {summary.totalItems.toLocaleString()}
              </span>
            </p>
            <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
              {meta
                ? `Based on ${meta.days}d sales → ${meta.projected_days}d cover`
                : "Generate forecast to compute suggestions"}
            </p>
          </div>
        </div>

        <div className="g-card p-4 border-l-4 border-l-indigo-500 flex flex-col justify-between">
          <div className="flex items-center justify-between mb-1">
            <span className="text-xs font-semibold uppercase tracking-wider text-indigo-600 dark:text-indigo-400">
              Total Order Packs
            </span>
            <div className="p-2 rounded-lg bg-indigo-50 dark:bg-indigo-900/30 text-indigo-600 dark:text-indigo-400">
              <CubeIcon className="w-4 h-4" aria-hidden="true" />
            </div>
          </div>
          <div>
            <p className="text-2xl font-bold font-mono tabular-nums text-gray-900 dark:text-gray-100">
              {summary.packs.toLocaleString()}
            </p>
            <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
              {summary.units.toLocaleString()} total units across selected items
            </p>
          </div>
        </div>

        <div className="g-card p-4 border-l-4 border-l-emerald-500 flex flex-col justify-between">
          <div className="flex items-center justify-between mb-1">
            <span className="text-xs font-semibold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">
              Estimated Cost
            </span>
            <div className="p-2 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400">
              <BanknotesIcon className="w-4 h-4" aria-hidden="true" />
            </div>
          </div>
          <div>
            <p className="text-2xl font-bold font-mono tabular-nums text-gray-900 dark:text-gray-100">
              {formatCurrency(summary.amount)}
            </p>
            <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
              Estimated purchase value{hasActiveResultFilters ? " (filtered)" : ""}
            </p>
          </div>
        </div>

        <div
          className={`g-card p-4 border-l-4 flex flex-col justify-between ${
            summary.outOfStockCount > 0
              ? "border-l-rose-500"
              : summary.demandCount > 0
              ? "border-l-violet-500"
              : "border-l-emerald-500"
          }`}
        >
          <div className="flex items-center justify-between mb-1">
            <span
              className={`text-xs font-semibold uppercase tracking-wider ${
                summary.outOfStockCount > 0
                  ? "text-rose-600 dark:text-rose-400"
                  : summary.demandCount > 0
                  ? "text-violet-600 dark:text-violet-400"
                  : "text-emerald-600 dark:text-emerald-400"
              }`}
            >
              Stock &amp; Demand Signals
            </span>
            <div
              className={`p-2 rounded-lg ${
                summary.outOfStockCount > 0
                  ? "bg-rose-50 dark:bg-rose-900/30 text-rose-600 dark:text-rose-400"
                  : "bg-violet-50 dark:bg-violet-900/30 text-violet-600 dark:text-violet-400"
              }`}
            >
              <TruckIcon className="w-4 h-4" aria-hidden="true" />
            </div>
          </div>
          <div>
            <div className="flex items-baseline gap-3">
              <p className="text-2xl font-bold font-mono tabular-nums text-gray-900 dark:text-gray-100">
                {summary.outOfStockCount}
                <span className="text-xs font-sans font-medium text-rose-600 dark:text-rose-400 ml-1">
                  Out
                </span>
              </p>
              <p className="text-lg font-bold font-mono tabular-nums text-violet-600 dark:text-violet-400">
                {summary.demandCount}
                <span className="text-xs font-sans font-medium ml-1">Demands</span>
              </p>
            </div>
            <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
              {summary.onOrderCount > 0
                ? `${summary.onOrderCount} item(s) already on open PO`
                : "No conflicting open PO stock"}
            </p>
          </div>
        </div>
      </div>

      {/* Results Panel */}
      <div className="purchase-order-results g-card overflow-hidden">
        {/* Results Header + Search & Filter Toolbar */}
        <div className="p-4 border-b border-[var(--workspace-border)] space-y-3 bg-gray-50/50 dark:bg-slate-800/40 print:hidden">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-2.5">
              <div className="p-2 rounded-xl bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400">
                <CubeIcon className="w-4 h-4" aria-hidden="true" />
              </div>
              <div>
                <h2
                  id={resultsHeadingId}
                  className="text-sm font-semibold text-gray-900 dark:text-gray-100"
                >
                  Forecast Items
                </h2>
                <p className="text-[11px] text-gray-500 dark:text-gray-400">
                  {hasActiveResultFilters
                    ? `Showing ${visibleRows.length} of ${rows.length} items · ${orderRows.length} ready to order`
                    : `${rows.length} item(s) · ${orderRows.length} with Order Packs > 0`}
                </p>
              </div>
            </div>

            <div className="flex items-center gap-2 flex-wrap">
              <label
                htmlFor={arrangeSelectId}
                className="text-xs font-medium text-gray-500 dark:text-gray-400 whitespace-nowrap"
              >
                Arrange by:
              </label>
              <select
                id={arrangeSelectId}
                value={arrangement}
                onChange={(e) => changeArrangement(e.target.value)}
                title="Applies to displayed items, printing, and orders created from this forecast"
                className="h-8 max-w-full sm:max-w-64 px-2.5 rounded-lg text-xs font-medium bg-[var(--workspace-reading-fill)] border border-[var(--workspace-border)] text-gray-800 dark:text-gray-200 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)] cursor-pointer scheme-light dark:scheme-dark"
              >
                {[...new Set(purchaseOrderArrangements.map((option) => option.group))].map(
                  (group) => (
                    <optgroup key={group} label={group}>
                      {purchaseOrderArrangements
                        .filter((option) => option.group === group)
                        .map((option) => (
                          <option key={option.id} value={option.id}>
                            {option.label}
                          </option>
                        ))}
                    </optgroup>
                  )
                )}
              </select>
            </div>
          </div>

          {/* Semantic Search & Result Filter Bar */}
          <search className="flex flex-wrap items-center gap-2">
            <div className="relative flex-1 min-w-[200px]">
              <MagnifyingGlassIcon
                className="w-4 h-4 absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-400 pointer-events-none"
                aria-hidden="true"
              />
              <input
                id={searchInputId}
                type="search"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                disabled={!rows.length}
                placeholder="Search product, code, brand, or supplier…"
                aria-label="Filter forecast results"
                autoComplete="off"
                autoCorrect="off"
                autoCapitalize="none"
                spellCheck={false}
                className="w-full h-8 pl-8 pr-7 rounded-lg border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] text-xs text-gray-900 dark:text-gray-100 placeholder:text-gray-400 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)] disabled:opacity-50"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery("")}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 cursor-pointer"
                  aria-label="Clear product search"
                >
                  <XMarkIcon className="w-3.5 h-3.5" aria-hidden="true" />
                </button>
              )}
            </div>

            {/* Result Supplier Filter */}
            <select
              value={resultSupplierKey}
              onChange={(e) => setResultSupplierKey(e.target.value)}
              disabled={!rows.length}
              aria-label="Filter results by supplier"
              title="Filter displayed results by supplier"
              className="h-8 px-2.5 rounded-lg text-xs font-medium bg-[var(--workspace-reading-fill)] border border-[var(--workspace-border)] text-gray-800 dark:text-gray-200 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)] disabled:opacity-50 scheme-light dark:scheme-dark"
            >
              <option value="all">All Suppliers ({availableSuppliers.length})</option>
              {availableSuppliers.map((s) => (
                <option key={s.key} value={s.key}>
                  {s.name} ({s.count})
                </option>
              ))}
            </select>

            {/* Result Brand Filter */}
            <select
              value={resultBrandKey}
              onChange={(e) => setResultBrandKey(e.target.value)}
              disabled={!rows.length}
              aria-label="Filter results by brand"
              title="Filter displayed results by brand"
              className="h-8 px-2.5 rounded-lg text-xs font-medium bg-[var(--workspace-reading-fill)] border border-[var(--workspace-border)] text-gray-800 dark:text-gray-200 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)] disabled:opacity-50 scheme-light dark:scheme-dark"
            >
              <option value="all">All Brands ({availableBrands.length})</option>
              {availableBrands.map((b) => (
                <option key={b.key} value={b.key}>
                  {b.name} ({b.count})
                </option>
              ))}
            </select>

            {/* Stock Filter */}
            <select
              value={stockFilter}
              onChange={(e) => setStockFilter(e.target.value)}
              disabled={!rows.length}
              aria-label="Filter by stock status"
              className="h-8 px-2.5 rounded-lg text-xs font-medium bg-[var(--workspace-reading-fill)] border border-[var(--workspace-border)] text-gray-800 dark:text-gray-200 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)] disabled:opacity-50 scheme-light dark:scheme-dark"
            >
              <option value="all">All Stock</option>
              <option value="out">Out of Stock (≤ 0)</option>
              <option value="low">Low Stock (≤ threshold)</option>
              <option value="in">In Stock (&gt; 0)</option>
            </select>

            {stockFilter === "low" && (
              <input
                type="text"
                inputMode="numeric"
                pattern="[0-9]*"
                value={lowStockThreshold}
                onChange={(e) => setLowStockThreshold(e.target.value.replace(/[^\d]/g, ""))}
                disabled={!rows.length}
                className="h-8 w-16 px-2 rounded-lg text-xs text-right tabular-nums bg-[var(--workspace-reading-fill)] border border-[var(--workspace-border)] text-gray-800 dark:text-gray-200 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)] disabled:opacity-50"
                title="Low stock threshold (units)"
                aria-label="Low stock threshold in units"
              />
            )}

            {/* Order Status Filter */}
            <select
              value={orderFilter}
              onChange={(e) => setOrderFilter(e.target.value)}
              disabled={!rows.length}
              aria-label="Filter by order status"
              className="h-8 px-2.5 rounded-lg text-xs font-medium bg-[var(--workspace-reading-fill)] border border-[var(--workspace-border)] text-gray-800 dark:text-gray-200 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)] disabled:opacity-50 scheme-light dark:scheme-dark"
            >
              <option value="all">All Items</option>
              <option value="to_order">To Order (&gt; 0 packs)</option>
              <option value="no_order">No Order (= 0 packs)</option>
              <option value="demand">User Demands Only</option>
              <option value="returned">Purchase Returned</option>
            </select>

            {hasActiveResultFilters && (
              <button
                type="button"
                onClick={clearResultFilters}
                className="h-8 px-2.5 inline-flex items-center gap-1 rounded-lg text-xs font-semibold text-[var(--workspace-accent)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_10%,transparent)] cursor-pointer"
              >
                <XMarkIcon className="w-3.5 h-3.5" aria-hidden="true" />
                <span>Reset</span>
              </button>
            )}
          </search>

          {/* Multi-supplier warning bar when orderRows spans multiple suppliers */}
          {createOrderPreview.hasMultipleSuppliers && (
            <div className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 rounded-lg bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-800 text-xs text-amber-900 dark:text-amber-200">
              <div className="flex items-center gap-2">
                <ExclamationTriangleIcon className="w-4 h-4 text-amber-600 dark:text-amber-400 shrink-0" aria-hidden="true" />
                <span>
                  Order list currently includes <strong>{createOrderPreview.distinctSuppliers.length} suppliers</strong>. Pick one supplier below before creating a Purchase Order:
                </span>
              </div>
              <div className="flex flex-wrap items-center gap-1.5">
                {createOrderPreview.distinctSuppliers.slice(0, 6).map((s) => (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => setResultSupplierKey(String(s.id))}
                    className="px-2 py-0.5 rounded-md bg-white dark:bg-slate-800 border border-amber-300 dark:border-amber-700 text-[11px] font-semibold text-amber-900 dark:text-amber-200 hover:bg-amber-100 dark:hover:bg-amber-900/50 cursor-pointer"
                  >
                    {s.name}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Scrollable Accessible Data Table */}
        <div
          ref={tableWrapRef}
          tabIndex={0}
          role="region"
          aria-labelledby={resultsHeadingId}
          aria-busy={loading}
          className="purchase-order-table-scroll outline-hidden"
        >
          <table className="w-full text-xs text-left">
            <thead>
              <tr>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider w-10">
                  #
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider min-w-[260px]">
                  Product / Brand / Supplier
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-right">
                  Pack
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-right">
                  Net Sold
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-center">
                  Stock (U)
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-right">
                  Pack Price
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-right">
                  Suggested (P)
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-right w-36">
                  Order Packs
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-right">
                  Order Units
                </th>
                <th scope="col" className="px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-right">
                  Order Amount
                </th>
              </tr>
            </thead>

            <tbody className="divide-y divide-[var(--workspace-border)]">
              {visibleRows.map((r, idx) => {
                const isActive = r._rowId === selectedRowId;
                const stock = Number(r.current_stock_units || 0);
                const onOrderUnits = Number(r.on_order_units || 0);
                const dailyRate = Number(r.policy?.daily_units || 0);
                const orderPacksVal = Number(r.order_packs || 0);
                const isEdited = orderPacksVal !== Number(r.suggested_packs || 0);

                const stockBadgeClass =
                  stock <= 0
                    ? "bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300"
                    : stock <= lowThresholdNum
                    ? "bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300"
                    : "bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300";

                return (
                  <tr
                    key={r._rowId}
                    ref={(el) => {
                      if (el) rowRefs.current[r._rowId] = el;
                      else delete rowRefs.current[r._rowId];
                    }}
                    onClick={() => {
                      setSelectedRowId(r._rowId);
                      ensureRowVisible(r._rowId);
                    }}
                    className={`purchase-order-row transition-colors ${
                      isActive
                        ? "is-active"
                        : "hover:bg-[color-mix(in_srgb,var(--workspace-accent)_4%,transparent)]"
                    }`}
                  >
                    <td className="px-3 py-2.5 text-gray-500 dark:text-gray-400 tabular-nums">
                      {idx + 1}
                    </td>

                    <td className="px-3 py-2.5">
                      <div className="flex items-start gap-2">
                        {Number(r.has_purchase_return) === 1 && (
                          <span
                            className="shrink-0 mt-0.5 inline-flex items-center justify-center w-5 h-5 rounded-full bg-rose-100 dark:bg-rose-900/40"
                            title={`Purchase Return: ${r.purchase_return_units || 0} unit(s)${
                              r.last_purchase_return_date ? ` on ${r.last_purchase_return_date}` : ""
                            }`}
                            aria-label="Returned via purchase return"
                          >
                            <ArrowUturnLeftIcon className="w-3 h-3 text-rose-600 dark:text-rose-400" aria-hidden="true" />
                          </span>
                        )}
                        <div className="min-w-0">
                          <div
                            className={`font-semibold text-xs truncate max-w-[300px] ${
                              Number(r.has_purchase_return) === 1
                                ? "text-rose-600 dark:text-rose-400"
                                : "text-gray-900 dark:text-gray-100"
                            }`}
                            title={r.product_name}
                          >
                            {r.product_name}
                          </div>

                          <div className="flex flex-wrap items-center gap-1.5 mt-1">
                            {r.product_code && (
                              <span className="text-[10px] font-mono text-gray-500 dark:text-gray-400">
                                {r.product_code}
                              </span>
                            )}
                            {r.brand_name && (
                              <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded bg-violet-50 dark:bg-violet-950/50 text-violet-700 dark:text-violet-300 text-[10px] font-medium">
                                <TagIcon className="w-2.5 h-2.5 shrink-0" aria-hidden="true" />
                                <span className="truncate max-w-[110px]">{r.brand_name}</span>
                              </span>
                            )}
                            {r.supplier_name && (
                              <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 text-[10px] font-medium">
                                <BuildingStorefrontIcon className="w-2.5 h-2.5 shrink-0" aria-hidden="true" />
                                <span className="truncate max-w-[130px]">{r.supplier_name}</span>
                              </span>
                            )}
                          </div>

                          {Number(r.is_user_demand) === 1 && (
                            <div
                              className="text-[11px] font-medium text-violet-600 dark:text-violet-400 flex items-center gap-1 mt-1"
                              title={
                                r.user_demand_customers
                                  ? `Customer demand: ${r.user_demand_customers} (${r.user_demand_quantity || 0} units)`
                                  : `Customer shortage demand (${r.user_demand_quantity || 0} units)`
                              }
                            >
                              <HandRaisedIcon className="w-3 h-3 shrink-0" aria-hidden="true" />
                              <span className="truncate max-w-[260px]">
                                Demand: {r.user_demand_quantity || 0}u
                                {r.user_demand_customers ? ` · ${r.user_demand_customers}` : ""}
                              </span>
                            </div>
                          )}
                        </div>
                      </div>
                    </td>

                    <td className="px-3 py-2.5 text-right tabular-nums whitespace-nowrap text-gray-600 dark:text-gray-300">
                      {r.pack_size}
                    </td>

                    <td className="px-3 py-2.5 text-right tabular-nums whitespace-nowrap">
                      <div className="font-semibold text-gray-800 dark:text-gray-200">
                        {Number(r.units_sold || 0).toLocaleString()}
                      </div>
                      {dailyRate > 0 && (
                        <div className="text-[10px] text-gray-400 dark:text-gray-500">
                          {dailyRate.toFixed(1)}/day
                        </div>
                      )}
                    </td>

                    <td className="px-3 py-2.5 text-center tabular-nums whitespace-nowrap">
                      <div className="flex flex-col items-center gap-0.5">
                        <span
                          className={`inline-flex items-center justify-center min-w-11 px-2 py-0.5 rounded-full text-[11px] font-bold ${stockBadgeClass}`}
                        >
                          {stock.toLocaleString()}
                        </span>
                        {onOrderUnits > 0 && (
                          <span
                            className="inline-flex items-center px-1.5 py-0.2 rounded text-[10px] font-semibold bg-blue-50 text-blue-700 dark:bg-blue-950/60 dark:text-blue-300"
                            title={`${onOrderUnits} unit(s) (${r.on_order_packs || 0} packs) currently on open Purchase Orders`}
                          >
                            +{onOrderUnits} on PO
                          </span>
                        )}
                      </div>
                    </td>

                    <td className="px-3 py-2.5 text-right tabular-nums whitespace-nowrap text-gray-700 dark:text-gray-300">
                      {formatCurrency(r.pack_price)}
                    </td>

                    <td className="px-3 py-2.5 text-right tabular-nums whitespace-nowrap">
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          handleOrderPacksChange(r._rowId, String(r.suggested_packs || 0));
                        }}
                        className={`inline-flex items-center justify-center min-w-9 px-2 py-0.5 rounded-md text-xs font-bold transition-colors cursor-pointer ${
                          Number(r.suggested_packs || 0) > 0
                            ? "bg-blue-50 text-blue-700 hover:bg-blue-100 dark:bg-blue-950/60 dark:text-blue-300"
                            : "text-gray-400 hover:bg-gray-100 dark:hover:bg-slate-800"
                        }`}
                        title="Click to apply suggested packs"
                      >
                        {r.suggested_packs}
                      </button>
                    </td>

                    <td className="px-3 py-2.5 text-right whitespace-nowrap">
                      <div
                        className="inline-flex items-center gap-1"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <button
                          type="button"
                          tabIndex={-1}
                          onClick={() => stepOrderPacks(r._rowId, -1)}
                          disabled={orderPacksVal <= 0}
                          aria-label={`Decrease order packs for ${r.product_name}`}
                          className="w-7 h-8 inline-flex items-center justify-center rounded-md border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] text-gray-600 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-slate-700 disabled:opacity-40 cursor-pointer"
                        >
                          <MinusIcon className="w-3.5 h-3.5" aria-hidden="true" />
                        </button>
                        <input
                          ref={(el) => {
                            if (el) inputRefs.current[r._rowId] = el;
                            else delete inputRefs.current[r._rowId];
                          }}
                          type="text"
                          inputMode="numeric"
                          pattern="[0-9]*"
                          enterKeyHint="next"
                          autoComplete="off"
                          aria-label={`Order packs for ${r.product_name}`}
                          value={r.order_packs_input ?? String(orderPacksVal)}
                          onFocus={() => {
                            setSelectedRowId(r._rowId);
                            ensureRowVisible(r._rowId);
                          }}
                          onChange={(e) => handleOrderPacksChange(r._rowId, e.target.value)}
                          onBlur={() => handleOrderPacksBlur(r._rowId)}
                          onKeyDown={(e) => handleRowInputKeyDown(e, idx)}
                          className={`w-16 h-8 px-2 rounded-md border text-right text-xs font-bold tabular-nums bg-[var(--workspace-surface)] text-gray-900 dark:text-gray-100 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)] ${
                            isEdited
                              ? "border-amber-400 dark:border-amber-500"
                              : "border-[var(--workspace-border)]"
                          }`}
                        />
                        <button
                          type="button"
                          tabIndex={-1}
                          onClick={() => stepOrderPacks(r._rowId, 1)}
                          aria-label={`Increase order packs for ${r.product_name}`}
                          className="w-7 h-8 inline-flex items-center justify-center rounded-md border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] text-gray-600 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-slate-700 cursor-pointer"
                        >
                          <PlusIcon className="w-3.5 h-3.5" aria-hidden="true" />
                        </button>
                      </div>
                    </td>

                    <td className="px-3 py-2.5 text-right tabular-nums whitespace-nowrap text-gray-700 dark:text-gray-300">
                      {Number(r.order_units || 0).toLocaleString()}
                    </td>

                    <td className="px-3 py-2.5 text-right tabular-nums whitespace-nowrap font-bold text-gray-900 dark:text-gray-100">
                      {formatCurrency(r.order_amount)}
                    </td>
                  </tr>
                );
              })}

              {!rows.length && (
                <tr>
                  <td colSpan={10} className="px-4 py-14 text-center">
                    <CalculatorIcon className="w-10 h-10 mx-auto text-gray-300 dark:text-gray-600 mb-2.5" aria-hidden="true" />
                    <h3 className="text-sm font-semibold text-gray-800 dark:text-gray-200">
                      No forecast generated yet
                    </h3>
                    <p className="text-xs text-gray-500 dark:text-gray-400 mt-1 max-w-md mx-auto">
                      Pick a sales window, optionally filter by Supplier or Brand, and click <strong>Generate Forecast</strong> (or press <kbd className="px-1.5 py-0.5 rounded bg-gray-100 dark:bg-slate-800 border border-gray-200 dark:border-slate-700 font-mono text-[10px]">Ctrl+Enter</kbd>).
                    </p>
                  </td>
                </tr>
              )}

              {rows.length > 0 && !visibleRows.length && (
                <tr>
                  <td colSpan={10} className="px-4 py-12 text-center">
                    <MagnifyingGlassIcon className="w-9 h-9 mx-auto text-gray-300 dark:text-gray-600 mb-2" aria-hidden="true" />
                    <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
                      No items match your current result filters.
                    </p>
                    <button
                      type="button"
                      onClick={clearResultFilters}
                      className="mt-3 g-btn-secondary theme-btn-secondary px-3 py-1.5 text-xs font-semibold rounded-lg"
                    >
                      Reset Result Filters
                    </button>
                  </td>
                </tr>
              )}
            </tbody>

            {visibleRows.length > 0 && (
              <tfoot>
                <tr className="font-bold text-xs">
                  <td className="px-3 py-3 text-gray-800 dark:text-gray-100" colSpan={7}>
                    Totals{hasActiveResultFilters ? " (filtered)" : ""} · {summary.itemsToOrder} item(s) to order
                  </td>
                  <td className="px-3 py-3 text-right tabular-nums text-gray-900 dark:text-gray-100">
                    {summary.packs.toLocaleString()} packs
                  </td>
                  <td className="px-3 py-3 text-right tabular-nums text-gray-800 dark:text-gray-200">
                    {summary.units.toLocaleString()}
                  </td>
                  <td className="px-3 py-3 text-right tabular-nums text-gray-900 dark:text-gray-100">
                    {formatCurrency(summary.amount)}
                  </td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>

        {/* Keyboard Shortcut Footer Hint */}
        {visibleRows.length > 0 && (
          <div className="px-4 py-2.5 border-t border-[var(--workspace-border)] bg-gray-50/40 dark:bg-slate-800/30 flex flex-wrap items-center justify-between gap-2 text-[11px] text-gray-500 dark:text-gray-400 print:hidden">
            <span>
              Tip: Type any pack quantity (e.g. 50) directly, press <strong>Enter</strong> or <strong>↑ / ↓</strong> to move between rows.
            </span>
            <span>
              <strong>Ctrl+Enter</strong> Regenerate · <strong>Ctrl+P</strong> Print
            </span>
          </div>
        )}
      </div>

      {/* Brand & Supplier Search Modals */}
      <BrandSearch
        isOpen={brandSearchOpen}
        onClose={() => setBrandSearchOpen(false)}
        multiple
        selectedIds={brands.map((b) => Number(b.value))}
        onToggle={(brandObj) => {
          if (!brandObj) return;
          const id = Number(brandObj.id);
          setBrands((prev) =>
            prev.some((b) => Number(b.value) === id)
              ? prev.filter((b) => Number(b.value) !== id)
              : [...prev, { value: id, label: brandObj.name }]
          );
        }}
      />
      <SupplierSearch
        isOpen={supplierSearchOpen}
        onClose={() => setSupplierSearchOpen(false)}
        onSelect={(supplierObj) => {
          setSupplier(
            supplierObj ? { value: Number(supplierObj.id), label: supplierObj.name } : null
          );
        }}
      />

      <style>{`
        @media print {
          .print\\:p-0 { padding: 0 !important; }
          .print\\:hidden { display: none !important; }
        }
      `}</style>
    </div>
  );
}
