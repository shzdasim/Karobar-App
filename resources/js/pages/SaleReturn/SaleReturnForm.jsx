import { useState, useEffect, useRef, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";

import ProductSearchInput from "../../components/ProductSearchInput.jsx";
import BatchSearchInput from "../../components/BatchSearchInput.jsx";
import CustomerSearch from "../../components/CustomerSearch.jsx";
import SaleInvoiceSearch from "../../components/SaleInvoiceSearch.jsx";
import { useTheme } from "@/context/ThemeContext";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  return getContrastText(primaryHoverColor || primaryColor);
};

// ===== helpers =====
const toNum = (v) => (v === undefined || v === null || v === "" ? 0 : Number(v));
const round2 = (n) => (Number.isFinite(Number(n)) ? Number(Number(n).toFixed(2)) : 0);
const eqId = (a, b) => String(a ?? "") === String(b ?? "");

// try multiple possible keys for unit price
const extractUnitPrice = (obj) => {
  if (!obj) return 0;
  const candidates = [
    obj.unit_sale_price,
    obj.unit_price,
    obj.sale_price,
    obj.unit_selling_price,
    obj.price,
    obj.unitRate,
  ];
  const packPrice = obj.pack_sale_price ?? obj.pack_price;
  const packSize = obj.pack_size ?? obj.size ?? obj.units_per_pack;
  if ((packPrice ?? null) !== null && toNum(packPrice) > 0 && toNum(packSize) > 0) {
    candidates.push(toNum(packPrice) / toNum(packSize));
  }
  for (const c of candidates) {
    const n = toNum(c);
    if (n > 0) return n;
  }
  return 0;
};

const extractItemDiscPct = (obj) => {
  if (!obj) return 0;
  const candidates = [
    obj.item_discount_percentage,
    obj.discount_percentage,
    obj.line_discount_percentage,
    obj.item_discount,
    obj.discount,
  ];
  for (const c of candidates) {
    const n = toNum(c);
    if (n > 0) return n;
  }
  return 0;
};

const extractUnitSaleQty = (obj) => {
  if (!obj) return 0;
  const candidates = [obj.unit_sale_quantity, obj.unit_quantity, obj.quantity, obj.units];
  for (const c of candidates) {
    const n = toNum(c);
    if (n > 0) return n;
  }
  return 0;
};

const extractBatchQty = (b) =>
  toNum(
    b?.available_quantity ?? b?.quantity_available ?? b?.available_units ?? b?.on_hand ?? b?.stock ?? b?.balance ?? b?.qty ?? b?.quantity
  );

// subtotal = qty * unit_price - (line discount %)
function recalcItem(item) {
  const qty = toNum(item.unit_return_quantity);
  const price = toNum(item.unit_sale_price);
  const discPct = toNum(item.item_discount_percentage);
  const gross = qty * price;
  const discAmt = (gross * discPct) / 100;
  return { ...item, sub_total: round2(gross - discAmt) };
}

/**
 * Recalculate footer totals.
 * `source` controls which side is authoritative for discount/tax ("pct" or "amt").
 */
function recalcFooter(form, source = {}) {
  const items = Array.isArray(form.items) ? form.items : [];
  const gross = items.reduce((s, it) => s + toNum(it.sub_total), 0);

  let discPct = toNum(form.discount_percentage);
  let discAmt = toNum(form.discount_amount);

  if (source.disc === "pct") {
    discAmt = (gross * discPct) / 100;
  } else if (source.disc === "amt") {
    discPct = gross > 0 ? (discAmt / gross) * 100 : 0;
  } else {
    if (discPct !== 0) discAmt = (gross * discPct) / 100;
    else discPct = gross > 0 ? (discAmt / gross) * 100 : 0;
  }

  const taxable = gross - discAmt;

  let taxPct = toNum(form.tax_percentage);
  let taxAmt = toNum(form.tax_amount);

  if (source.tax === "pct") {
    taxAmt = (taxable * taxPct) / 100;
  } else if (source.tax === "amt") {
    taxPct = taxable > 0 ? (taxAmt / taxable) * 100 : 0;
  } else {
    if (taxPct !== 0) taxAmt = (taxable * taxPct) / 100;
    else taxPct = taxable > 0 ? (taxAmt / taxable) * 100 : 0;
  }

  return {
    ...form,
    gross_total: round2(gross),
    discount_percentage: round2(discPct),
    discount_amount: round2(discAmt),
    tax_percentage: round2(taxPct),
    tax_amount: round2(taxAmt),
    total: round2(taxable + taxAmt),
  };
}

export default function SaleReturnForm({ returnId, initialData, onSuccess }) {
  const defaultItem = {
    id: Date.now() + Math.random(),
    product_id: "",
    batch_number: "",
    expiry: "",
    unit_sale_quantity: 0, // READONLY, from invoice; available qty in open return
    unit_sale_price: 0, // READONLY, from invoice OR product
    unit_return_quantity: 0,
    item_discount_percentage: 0,
    sub_total: 0,
  };

  const [form, setForm] = useState({
    posted_number: "",
    date: new Date().toISOString().slice(0, 10),
    customer_id: "",
    sale_invoice_id: "", // optional
    items: [{ ...defaultItem }],
    discount_percentage: 0,
    discount_amount: 0,
    tax_percentage: 0,
    tax_amount: 0,
    gross_total: 0,
    total: 0,
  });

  const [customers, setCustomers] = useState([]);
  const refreshProducts = async (q = "") => {
    // If a sale invoice is selected, keep products limited to invoice items
    if (form.sale_invoice_id) return;
    try {
      const { data } = await axios.get("/api/products/search", { params: { q, limit: 30 } });
      setProducts(Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : []);
    } catch (e) {}
  };

  const [catalogProducts, setCatalogProducts] = useState([]);
  const [products, setProducts] = useState([]);
const [saleInvoices, setSaleInvoices] = useState([]);
  const [invoiceItems, setInvoiceItems] = useState([]);
  const [rowBatches, setRowBatches] = useState([]);
  const [selectedInvoiceObj, setSelectedInvoiceObj] = useState(null);

  const [invoiceMenuOpen, setInvoiceMenuOpen] = useState(false);
  const [customerSearchOpen, setCustomerSearchOpen] = useState(false);
  const [invoiceSearchOpen, setInvoiceSearchOpen] = useState(false);

  const customerSelectRef = useRef(null);
  const saleInvoiceRef = useRef(null);
  const productRefs = useRef([]);
  const batchRefs = useRef([]);
  const qtyRefs = useRef([]);
  const discRefs = useRef([]);

  const productBatchCache = useRef(new Map()); // productId -> [{batch_number, expiry}]

  // Get dark mode state
  const { theme, isDark } = useTheme();

  // Memoize theme colors for performance
  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#3b82f6',
        primaryHover: '#2563eb',
        primaryLight: '#dbeafe',
        secondary: '#8b5cf6',
        secondaryHover: '#7c3aed',
        secondaryLight: '#ede9fe',
        danger: '#ef4444',
        dangerHover: '#dc2626',
        dangerLight: '#fee2e2',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
      danger: theme.danger_color || '#ef4444',
      dangerHover: '#dc2626',
      dangerLight: '#fee2e2',
    };
  }, [theme]);

  // Calculate text colors based on background brightness
  const primaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.primary, themeColors.primaryHover), 
    [themeColors.primary, themeColors.primaryHover]
  );
  
  const secondaryTextColor = useMemo(() => 
    getButtonTextColor(themeColors.secondary, themeColors.secondaryHover), 
    [themeColors.secondary, themeColors.secondaryHover]
  );
  
  const dangerTextColor = useMemo(() => 
    getButtonTextColor(themeColors.danger, themeColors.dangerHover), 
    [themeColors.danger, themeColors.dangerHover]
  );

  // Get button style from theme
  const buttonStyle = theme?.button_style || 'rounded';
  
  // Get button style classes and styles based on theme button_style
  const getButtonClasses = useMemo(() => {
    const radiusMap = {
      'rounded': 'rounded-lg',
      'outlined': 'rounded-lg',
      'soft': 'rounded-xl',
    };
    const radiusClass = radiusMap[buttonStyle] || 'rounded-lg';
    
    if (buttonStyle === 'outlined') {
      return {
        primary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.primary,
            color: themeColors.primary,
            backgroundColor: 'transparent',
          }
        },
        secondary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.secondary,
            color: themeColors.secondary,
            backgroundColor: 'transparent',
          }
        },
        danger: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: themeColors.danger,
            color: themeColors.danger,
            backgroundColor: 'transparent',
          }
        },
        glass: {
          className: radiusClass,
          style: {
            background: isDark ? "rgba(71,85,105,0.6)" : "rgba(255,255,255,0.8)",
            backdropFilter: "blur(8px)",
            border: isDark ? "1px solid rgba(255,255,255,0.1)" : "1px solid rgba(0,0,0,0.1)",
            color: isDark ? "#f1f5f9" : "#1f2937",
          }
        },
      };
    }
    
    // Filled styles for rounded and soft
    return {
      primary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
          color: primaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
        }
      },
      secondary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
          color: secondaryTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`,
        }
      },
      danger: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
          color: dangerTextColor,
          boxShadow: `0 4px 14px 0 ${themeColors.danger}40`,
        }
      },
      glass: {
        className: radiusClass,
        style: {
          background: isDark ? "rgba(71,85,105,0.6)" : "rgba(255,255,255,0.8)",
          backdropFilter: "blur(8px)",
          border: isDark ? "1px solid rgba(255,255,255,0.1)" : "1px solid rgba(0,0,0,0.1)",
          color: isDark ? "#f1f5f9" : "#1f2937",
        }
      },
    };
  }, [buttonStyle, themeColors, primaryTextColor, secondaryTextColor, dangerTextColor, isDark]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;
  const btnGlass = getButtonClasses.glass;

  useEffect(() => {
    productRefs.current = productRefs.current.slice(0, form.items.length);
    batchRefs.current = batchRefs.current.slice(0, form.items.length);
    qtyRefs.current = qtyRefs.current.slice(0, form.items.length);
    discRefs.current = discRefs.current.slice(0, form.items.length);
    setRowBatches((prev) => {
      const next = prev.slice(0, form.items.length);
      while (next.length < form.items.length) next.push([]);
      return next;
    });
  }, [form.items.length]);

  // Init loads
  useEffect(() => {
    (async () => {
      try {
        const [custRes, prodRes, codeRes] = await Promise.all([
          axios.get("/api/customers"),
          axios.get("/api/products/search", { params: { q: "", limit: 30 } }),
          axios.get("/api/sale-returns/new-code"),
        ]);
        const customersArr = Array.isArray(custRes.data) ? custRes.data : [];
        setCustomers(customersArr);

        const catalog = Array.isArray(prodRes?.data?.data) ? prodRes.data.data : Array.isArray(prodRes?.data) ? prodRes.data : [];
        setCatalogProducts(catalog);
        setProducts(catalog);

        setForm((prev) => ({ ...prev, posted_number: codeRes?.data?.posted_number || "" }));

        if (customersArr.length) {
          // Select customer with lowest ID (not highest)
          const sortedCustomers = [...customersArr].sort((a, b) => a.id - b.id);
          const firstId = sortedCustomers[0]?.id;
          setForm((prev) => ({ ...prev, customer_id: firstId }));
          await fetchSaleInvoices(firstId);
        }

        if (!returnId) setTimeout(() => saleInvoiceRef.current?.focus?.(), 50);
      } catch (e) {
        console.error(e);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // If editing, normalize incoming data to unit-based fields
  useEffect(() => {
    if (!returnId) return;
    (async () => {
      try {
        const res = await axios.get(`/api/sale-returns/${returnId}`);
        const data = res.data || {};
        const items = Array.isArray(data.items) ? data.items : [];

        // Fetch products for any product_ids that might not be in the catalog
        const productIds = [...new Set(items.map((it) => it.product_id).filter(Boolean))];
        if (productIds.length > 0) {
          try {
            const prodRes = await axios.get("/api/products/search", {
              params: { ids: productIds.join(",") },
            });
            const fetchedProducts = Array.isArray(prodRes?.data?.data)
              ? prodRes.data.data
              : Array.isArray(prodRes?.data)
                ? prodRes.data
                : [];
            if (fetchedProducts.length > 0) {
              setProducts((prev) => {
                const seen = new Set(prev.map((p) => String(p.id)));
                const newProds = fetchedProducts.filter((p) => !seen.has(String(p.id)));
                return [...newProds, ...prev];
              });
              setCatalogProducts((prev) => {
                const seen = new Set(prev.map((p) => String(p.id)));
                const newProds = fetchedProducts.filter((p) => !seen.has(String(p.id)));
                return [...newProds, ...prev];
              });
            }
          } catch (e) {
            console.error("Failed to fetch products for edit:", e);
          }
        }

        const normalized = {
          posted_number: data.posted_number || "",
          date: data.date || new Date().toISOString().slice(0, 10),
          customer_id: data.customer_id || "",
          sale_invoice_id: data.sale_invoice_id || "",
          items: items.length
            ? items.map((it) => ({
                ...defaultItem,
                product_id: it.product_id,
                batch_number: it.batch_number || it.batch || "",
                expiry: it.expiry || "",
                unit_sale_quantity: toNum(it.unit_sale_quantity),
                unit_sale_price: toNum(it.unit_sale_price),
                unit_return_quantity: toNum(it.unit_return_quantity),
                item_discount_percentage: toNum(it.item_discount_percentage),
                sub_total: toNum(it.sub_total),
                id: Date.now() + Math.random(),
              }))
            : [{ ...defaultItem }],
          discount_percentage: toNum(data.discount_percentage),
          discount_amount: toNum(data.discount_amount),
          tax_percentage: toNum(data.tax_percentage),
          tax_amount: toNum(data.tax_amount),
          gross_total: toNum(data.gross_total),
          total: toNum(data.total),
        };

        setForm((prev) => recalcFooter({ ...prev, ...normalized }));

        if (normalized.customer_id) await fetchSaleInvoices(normalized.customer_id);
        if (normalized.sale_invoice_id) await loadInvoice(normalized.sale_invoice_id);
      } catch (e) {
        console.error(e);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [returnId]);

  // Alt+S quick save
  useEffect(() => {
    const onKey = (e) => {
      if (e.altKey && (e.key === "s" || e.key === "S")) {
        e.preventDefault();
        handleSubmit();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form]);

  const fetchSaleInvoices = async (customerId) => {
    if (!customerId) {
      setSaleInvoices([]);
      return;
    }
    try {
      const res = await axios.get(`/api/sale-invoices?customer_id=${customerId}`);
      setSaleInvoices(Array.isArray(res.data) ? res.data : []);
    } catch (e) {
      setSaleInvoices([]);
    }
  };

const loadInvoice = async (invoiceId) => {
    try {
      const res = await axios.get(`/api/sale-invoices/${invoiceId}`);
      const data = res.data || {};
      setSelectedInvoiceObj(data);
      const items = Array.isArray(data?.items) ? data.items : [];
      setInvoiceItems(items);

      // Reduce product list to only invoice items, merging with full catalog product object.
      const byId = new Map();
      for (const it of items) {
        const pid = Number(it?.product_id || it?.product?.id || it?.id);
        if (!pid) continue;
        const name = it?.product?.name || it?.product_name || it?.name || `#${pid}`;
        const unit_sale_price = extractUnitPrice(it);
        const item_discount_percentage = extractItemDiscPct(it);
        const unit_sale_quantity = extractUnitSaleQty(it);
        byId.set(pid, { id: pid, name, unit_sale_price, item_discount_percentage, unit_sale_quantity });
      }
      const invoiceProductsLite = Array.from(byId.values());

      const catalogById = new Map(
        (catalogProducts || []).map((p) => [String(p.id ?? p.value ?? p.product_id ?? p?.data?.id), p])
      );
      const merged = invoiceProductsLite.map((p) => {
        const base = catalogById.get(String(p.id)) || {};
        return {
          ...base,
          id: p.id,
          name: base.name ?? p.name,
          unit_sale_price: p.unit_sale_price ?? extractUnitPrice(base),
          item_discount_percentage: p.item_discount_percentage ?? extractItemDiscPct(base),
          unit_sale_quantity: p.unit_sale_quantity, // from invoice
        };
      });

      setProducts(merged.length ? merged : catalogProducts);
    } catch (e) {
      console.error(e);
      setInvoiceItems([]);
      setProducts(catalogProducts);
    }
  };

  const fetchBatchesForOpenReturn = async (productId) => {
    try {
      const res = await axios.get(`/api/products/${productId}/batches`);
      const rows = Array.isArray(res.data) ? res.data : [];
      const normalized = rows
        .map((b) => ({
          batch_number: String(b.batch_number ?? b.batch ?? b.code ?? "").trim(),
          expiry: String(b.expiry ?? b.expiry_date ?? "").trim(),
          available_units: extractBatchQty(b),
        }))
        .filter((b) => b.batch_number);
      productBatchCache.current.set(String(productId), normalized);
      return normalized;
    } catch (e) {
      productBatchCache.current.set(String(productId), []);
      return [];
    }
  };

  const fetchProductUnitPrice = async (pid) => {
    try {
      const res = await axios.get(`/api/products/${pid}`);
      const data = res.data || {};
      const fromApi = extractUnitPrice(data);
      if (fromApi > 0) return fromApi;
    } catch (e) {
      // ignore
    }
    const local =
      extractUnitPrice(products.find((p) => String(p.id ?? p.value) === String(pid))) ||
      extractUnitPrice(catalogProducts.find((p) => String(p.id ?? p.value) === String(pid)));
    return toNum(local);
  };

  // available qty (best-effort): try product API, then sum batches, then local cache
  const fetchProductAvailableQty = async (pid) => {
    try {
      const res = await axios.get(`/api/products/${pid}`);
      const d = res.data || {};
      const cands = [
        d.available_quantity,
        d.quantity_available,
        d.available_units,
        d.on_hand,
        d.stock,
        d.inventory,
        d.balance,
        d.qty,
        d.quantity,
      ];
      for (const c of cands) {
        const n = toNum(c);
        if (n > 0) return n;
      }
    } catch (e) {}
    try {
      const res = await axios.get(`/api/products/${pid}/batches`);
      const rows = Array.isArray(res.data) ? res.data : [];
      const total = rows.reduce((s, b) => s + extractBatchQty(b), 0);
      if (total > 0) return total;
    } catch (e) {}
    const local =
      products.find((p) => String(p.id ?? p.value) === String(pid)) ||
      catalogProducts.find((p) => String(p.id ?? p.value) === String(pid));
    const localCands = [
      local?.available_quantity,
      local?.quantity_available,
      local?.available_units,
      local?.on_hand,
      local?.stock,
      local?.inventory,
      local?.balance,
      local?.qty,
      local?.quantity,
    ];
    for (const c of localCands) {
      const n = toNum(c);
      if (n > 0) return n;
    }
    return 0;
  };

const handleSelectChange = async (field, value) => {
    const v = value?.value ?? value ?? "";
    if (field === "customer_id") {
      setForm((prev) => ({ ...prev, customer_id: v, sale_invoice_id: "" }));
      setSelectedInvoiceObj(null);
      await fetchSaleInvoices(v);
      setInvoiceItems([]);
      setProducts(catalogProducts);
      productBatchCache.current = new Map();
      setRowBatches([]);
      setForm((prev) =>
        recalcFooter({ ...prev, items: prev.items.map((it) => ({ ...defaultItem, id: it.id })) })
      );
      setTimeout(() => saleInvoiceRef.current?.focus?.(), 50);
      return;
    }
    if (field === "sale_invoice_id") {
      setForm((prev) => ({ ...prev, sale_invoice_id: v }));
      if (v) {
        await loadInvoice(v);
        // Fetch the full invoice to display its number and sync customer
        try {
          const invRes = await axios.get(`/api/sale-invoices/${v}`);
          const inv = invRes.data || {};
          setSelectedInvoiceObj(inv);
          // Keep the invoice's customer in sync so the form is consistent
          if (inv.customer_id) {
            setForm((prev) => ({ ...prev, customer_id: inv.customer_id, sale_invoice_id: v }));
          }
        } catch (e) {
          setSelectedInvoiceObj(null);
        }
      } else {
        setSelectedInvoiceObj(null);
        setInvoiceItems([]);
        setProducts(catalogProducts);
      }
      productBatchCache.current = new Map();
      setRowBatches([]);
      setForm((prev) =>
        recalcFooter({ ...prev, items: prev.items.map((it) => ({ ...defaultItem, id: it.id })) })
      );
      setTimeout(() => productRefs.current[0]?.querySelector("input")?.focus?.(), 50);
    }
  };

  const resolveProductId = (obj) => {
    if (obj === null || obj === undefined) return "";
    if (typeof obj === "number") return obj;
    if (typeof obj === "string") return obj.trim();
    if (typeof obj === "object") {
      return obj.product_id ?? obj.id ?? obj.value ?? obj?.product?.id ?? obj?.data?.id ?? "";
    }
    return "";
  };

  const resetRow = (row) => {
    setForm((prev) => {
      const items = [...prev.items];
      items[row] = { ...defaultItem, id: items[row].id };
      return recalcFooter({ ...prev, items });
    });
  };

  const handleProductSelect = async (row, productObj) => {
    const pid = resolveProductId(productObj);
    if (!pid) return;

    // Prevent duplicate product across rows
    const dupIndex = form.items.findIndex((it, idx) => idx !== row && eqId(it.product_id, pid));
    if (dupIndex !== -1) {
      toast.error(`Product already selected in row ${dupIndex + 1}. Each product can be used only once.`);
      resetRow(row);
      setTimeout(() => productRefs.current[row]?.querySelector("input")?.focus?.(), 40);
      return;
    }

    const newItems = [...form.items];
    let unit_sale_price = 0;
    let item_discount_percentage = 0;
    let unit_sale_quantity = 0;

    if (form.sale_invoice_id) {
      const candidates = invoiceItems.filter((it) => String(it.product_id) === String(pid));
      if (candidates.length) {
        unit_sale_price = extractUnitPrice(candidates[0]);
        item_discount_percentage = extractItemDiscPct(candidates[0]);
        unit_sale_quantity = extractUnitSaleQty(candidates[0]);
      } else {
        unit_sale_price = await fetchProductUnitPrice(pid);
      }
    } else {
      // open mode
      unit_sale_price = await fetchProductUnitPrice(pid);
      item_discount_percentage = toNum(newItems[row]?.item_discount_percentage);
      unit_sale_quantity = await fetchProductAvailableQty(pid); // Available Qty (just for reference)
    }

    newItems[row] = recalcItem({
      ...newItems[row],
      product_id: pid,
      unit_sale_price,
      item_discount_percentage,
      unit_sale_quantity,
      batch_number: "",
      expiry: "",
      unit_return_quantity: 0,
    });
    setForm((prev) => recalcFooter({ ...prev, items: newItems }));

    const batches = await fetchBatchesForOpenReturn(pid);
      setRowBatches((prev) => {
        const next = prev.slice();
        next[row] = batches;
        return next;
      });

      // 🔥 DECIDE BASED ON ACTUAL FETCH RESULT (not state)
      setTimeout(() => {
        if (Array.isArray(batches) && batches.length > 0) {
          // product has batches → jump to batch
          const el = batchRefs.current[row];
          el?.querySelector("input")?.focus?.();
        } else {
          // no batches → jump to return qty
          qtyRefs.current[row]?.focus?.();
        }
      }, 60);
  };

  const handleBatchSelect = (row, batchVal) => {
    const bn = typeof batchVal === "string" ? batchVal : batchVal?.value ?? batchVal?.batch_number ?? "";

    // Prevent duplicate product+batch pairs
    if (bn) {
      const duplicate = form.items.findIndex(
        (it, idx) => idx !== row && eqId(it.product_id, form.items[row].product_id) && eqId(it.batch_number, bn)
      );
      if (duplicate !== -1) {
        toast.error(`Same Product & Batch already used in row ${duplicate + 1}.`);
        // clear batch in this row and refocus
        setForm((prev) => {
          const items = [...prev.items];
          items[row] = { ...items[row], batch_number: "" };
          return recalcFooter({ ...prev, items });
        });
        setTimeout(() => batchRefs.current[row]?.querySelector("input")?.focus?.(), 40);
        return;
      }
    }

    const newItems = [...form.items];
    let expiry = "";

    const cached = productBatchCache.current.get(String(newItems[row].product_id)) || [];
    const matchedOpen = cached.find((b) => b.batch_number === bn);
    if (matchedOpen) expiry = matchedOpen.expiry || "";

    let unit_sale_quantity = newItems[row].unit_sale_quantity || 0;
    let unit_sale_price = newItems[row].unit_sale_price || 0;
    let item_discount_percentage = newItems[row].item_discount_percentage || 0;

    if (form.sale_invoice_id) {
      const matchedInv = invoiceItems.find(
        (it) =>
          String(it.product_id) === String(newItems[row].product_id) &&
          String(it.batch_number ?? it.batch ?? "") === String(bn)
      );
      if (matchedInv) {
        unit_sale_quantity = extractUnitSaleQty(matchedInv);
        unit_sale_price = extractUnitPrice(matchedInv) || unit_sale_price;
        item_discount_percentage = extractItemDiscPct(matchedInv);
      }
    } else {
      // open mode: if batch has available_units, prefer it for available quantity display
      const matchedOpen2 = cached.find((b) => b.batch_number === bn);
      if (matchedOpen2 && toNum(matchedOpen2.available_units) > 0) {
        unit_sale_quantity = toNum(matchedOpen2.available_units);
      }
    }

    newItems[row] = recalcItem({
      ...newItems[row],
      batch_number: bn || "",
      expiry,
      unit_sale_quantity,
      unit_sale_price,
      item_discount_percentage,
    });
    setForm((prev) => recalcFooter({ ...prev, items: newItems }));
  };

  const handleItemChange = (row, field, raw) => {
    const v = raw === "" ? "" : Number(raw);
    const newItems = [...form.items];

    // ✅ Only enforce cap in invoice-based returns
    if (field === "unit_return_quantity" && form.sale_invoice_id) {
      const allowed = toNum(newItems[row].unit_sale_quantity);
      const prevQty = toNum(newItems[row].unit_return_quantity);
      const nextQty = toNum(v);
      if (nextQty > allowed && prevQty <= allowed) {
        toast.error(`Row ${row + 1}: Return qty exceeds allowed (${allowed}).`);
      }
    }

    newItems[row] = recalcItem({ ...newItems[row], [field]: v });
    setForm((prev) => recalcFooter({ ...prev, items: newItems }));
  };

  const addItem = () => {
    setForm((prev) => ({
      ...prev,
      items: [...prev.items, { ...defaultItem, id: Date.now() + Math.random() }],
    }));
    setTimeout(
      () => productRefs.current[form.items.length]?.querySelector("input")?.focus?.(),
      50
    );
  };

  const removeItem = (row) => {
    if (form.items.length <= 1) return;
    const next = form.items.filter((_, i) => i !== row);
    setForm((prev) => recalcFooter({ ...prev, items: next }));
  };

  // Header footer field handlers (bi-directional)
  const onDiscountPctChange = (val) =>
    setForm((prev) => recalcFooter({ ...prev, discount_percentage: val }, { disc: "pct" }));
  const onDiscountAmtChange = (val) =>
    setForm((prev) => recalcFooter({ ...prev, discount_amount: val }, { disc: "amt" }));
  const onTaxPctChange = (val) =>
    setForm((prev) => recalcFooter({ ...prev, tax_percentage: val }, { tax: "pct" }));
  const onTaxAmtChange = (val) =>
    setForm((prev) => recalcFooter({ ...prev, tax_amount: val }, { tax: "amt" }));

  const handleSubmit = async () => {
    try {
      if (!form.customer_id) {
        toast.error("Please select a customer");
        customerSelectRef.current?.focus?.();
        return;
      }

      // Validations: duplicates & qty (qty cap only for invoice-based returns)
      const seenProducts = new Set();
      const seenPairs = new Set();
      for (let i = 0; i < form.items.length; i++) {
        const it = form.items[i];
        if (!it.product_id) continue; // skip empty rows

        // duplicate product
        const keyP = String(it.product_id);
        if (seenProducts.has(keyP)) {
          toast.error(`Duplicate product detected at row ${i + 1}. Each product only once.`);
          return;
        }
        seenProducts.add(keyP);

        // duplicate product+batch (only when batch present)
        const bn = String(it.batch_number || "");
        if (bn) {
          const keyPB = `${keyP}__${bn}`;
          if (seenPairs.has(keyPB)) {
            toast.error(`Duplicate Product + Batch at row ${i + 1}.`);
            return;
          }
          seenPairs.add(keyPB);
        }

        // qty > allowed — enforce only for invoice mode
        if (form.sale_invoice_id) {
          const allowed = toNum(it.unit_sale_quantity);
          if (toNum(it.unit_return_quantity) > allowed) {
            toast.error(`Row ${i + 1}: Return qty exceeds allowed (${allowed}).`);
            return;
          }
        }
      }

      // Build payload - on CREATE: don't send posted_number (let server generate)
      // On UPDATE: send posted_number (preserve existing)
      const payloadToSend = returnId
        ? {
            posted_number: form.posted_number,
            date: form.date,
            customer_id: form.customer_id,
            sale_invoice_id: form.sale_invoice_id || null,
            discount_percentage: toNum(form.discount_percentage),
            discount_amount: toNum(form.discount_amount),
            tax_percentage: toNum(form.tax_percentage),
            tax_amount: toNum(form.tax_amount),
            gross_total: toNum(form.gross_total),
            total: toNum(form.total),
            items: form.items
              .filter((it) => it.product_id)
              .map((it) => ({
                product_id: it.product_id,
                batch_number: it.batch_number || null,
                expiry: it.expiry || null,
                unit_sale_quantity: toNum(it.unit_sale_quantity),
                unit_sale_price: toNum(it.unit_sale_price),
                unit_return_quantity: toNum(it.unit_return_quantity),
                item_discount_percentage: toNum(it.item_discount_percentage),
                sub_total: toNum(it.sub_total),
              })),
          }
        : {
            // Exclude posted_number on create - let server assign it
            date: form.date,
            customer_id: form.customer_id,
            sale_invoice_id: form.sale_invoice_id || null,
            discount_percentage: toNum(form.discount_percentage),
            discount_amount: toNum(form.discount_amount),
            tax_percentage: toNum(form.tax_percentage),
            tax_amount: toNum(form.tax_amount),
            gross_total: toNum(form.gross_total),
            total: toNum(form.total),
            items: form.items
              .filter((it) => it.product_id)
              .map((it) => ({
                product_id: it.product_id,
                batch_number: it.batch_number || null,
                expiry: it.expiry || null,
                unit_sale_quantity: toNum(it.unit_sale_quantity),
                unit_sale_price: toNum(it.unit_sale_price),
                unit_return_quantity: toNum(it.unit_return_quantity),
                item_discount_percentage: toNum(it.item_discount_percentage),
                sub_total: toNum(it.sub_total),
              })),
          };

      if (returnId) {
        await axios.put(`/api/sale-returns/${returnId}`, payloadToSend);
        toast.success("Sale return updated");
      } else {
        const { data: saved } = await axios.post(`/api/sale-returns`, payloadToSend);
        setForm((prev) => ({ ...prev, posted_number: saved?.posted_number || prev.posted_number }));
        toast.success(`Sale return created: ${saved?.posted_number || ""}`);
        if (saved?.id) {
          onSuccess?.(saved.id);
        } else {
          onSuccess?.();
        }
      }
    } catch (e) {
      console.error(e);
      toast.error("Failed to save sale return");
    }
  };

  const navigate = useNavigate();
  const handleCancel = () => navigate("/sale-returns");

  // ---------- Keyboard Navigation (similar to SaleInvoiceForm) ----------
  const COLS = ["product", "batch", "quantity", "disc"];

  const focusCell = (row, col) => {
    const map = {
      product: productRefs,
      batch: batchRefs,
      quantity: { current: qtyRefs.current },
      disc: { current: discRefs.current },
    };
    const ref = map[col]?.current?.[row];
    if (!ref) return;
    const input = ref.querySelector?.("input");
    if (input) {
      input.focus();
      input.select?.();
    } else if (ref.focus) {
      ref.focus();
    }
  };

  const moveSameCol = (row, col, dir) => {
    const lastIdx = form.items.length - 1;
    if (dir === 1) {
      if (row === lastIdx) {
        addItem();
        setTimeout(() => {
          const targetCol = col === "quantity" || col === "disc" ? "product" : col;
          focusCell(row + 1, targetCol);
        }, 60);
      } else {
        focusCell(row + 1, col);
      }
    } else {
      if (row > 0) focusCell(row - 1, col);
    }
  };

  const moveNextCol = (row, col) => {
    const i = COLS.indexOf(col);
    if (i < 0) return;
    if (i < COLS.length - 1) {
      focusCell(row, COLS[i + 1]);
    } else {
      const lastIdx = form.items.length - 1;
      if (row === lastIdx) {
        addItem();
        setTimeout(() => focusCell(row + 1, COLS[0]), 60);
      } else {
        focusCell(row + 1, COLS[0]);
      }
    }
  };

  const onKeyNav = (e, row, col) => {
    // Allow Batch dropdown to handle its own Arrow keys (if it opens a menu)
    if (col === "batch" && (e.key === "ArrowDown" || e.key === "ArrowUp")) return;
    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        moveSameCol(row, col, 1);
        break;
      case "ArrowUp":
        e.preventDefault();
        moveSameCol(row, col, -1);
        break;
      case "Enter":
        e.preventDefault();
        if (col === "quantity") {
          focusCell(row, "disc");
        } else {
          moveNextCol(row, col);
        }
        break;
      default:
        break;
    }
  };

// ===== Render =====
  const selectedCustomer = (customers || []).find(
    (c) => String(c.id) === String(form.customer_id)
  );
  const selectedInvoice =
    selectedInvoiceObj ||
    (saleInvoices || []).find(
      (inv) => String(inv.id) === String(form.sale_invoice_id)
    );

  return (
    <div className="relative">
      <form className={`h-[calc(100vh-110px)] flex flex-col ${isDark ? "bg-slate-900" : "bg-white"}`} autoComplete="off" autoCapitalize="off" autoCorrect="off" spellCheck={false}>
        {/* ===== HEADER ===== */}
        <div className={`shrink-0 sticky top-0 z-20 shadow-lg border-b ${isDark ? "border-slate-700" : "border-gray-200"}`}>
          {/* Branded Banner */}
          <div
            className="px-4 py-2.5 flex items-center justify-between gap-3"
            style={{
              background: `linear-gradient(135deg, ${themeColors.secondary}, ${themeColors.primary}, ${themeColors.primaryHover})`,
            }}
          >
            <div className="flex items-center gap-3">
              <div
                className="w-9 h-9 rounded-xl flex items-center justify-center shadow-inner"
                style={{ backgroundColor: "rgba(255,255,255,0.18)", backdropFilter: "blur(4px)" }}
              >
                {/* Return / revert icon */}
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                  <polyline points="1 4 1 10 7 10" />
                  <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
                </svg>
              </div>
              <div>
                <div className="flex items-center gap-2">
                  <h2 className="text-lg font-extrabold tracking-wide text-white leading-none">SALE RETURN</h2>
                  <span
                    className="px-2 py-0.5 rounded-md text-[9px] font-bold tracking-widest text-white"
                    style={{ backgroundColor: "rgba(255,255,255,0.22)", backdropFilter: "blur(4px)" }}
                  >
                    UNIT-BASED
                  </span>
                  <span
                    className={`px-2 py-0.5 rounded-md text-[9px] font-bold tracking-widest text-white ${
                      returnId ? "bg-amber-400/90" : "bg-emerald-400/90"
                    }`}
                    style={{ color: "#1e293b" }}
                  >
                    {returnId ? "EDIT" : "CREATE"}
                  </span>
                </div>
                <p className="text-[10px] text-white/80 mt-0.5">
                  Alt+S save · Enter=next · ↑/↓ rows
                </p>
              </div>
            </div>

            <button
              type="button"
              onClick={handleSubmit}
              className="px-4 py-2 rounded-lg text-[11px] font-bold bg-white/95 hover:bg-white shadow-lg transition-all duration-200"
              style={{ color: themeColors.primaryHover }}
            >
              {returnId ? "Edit (Alt+S)" : "Create (Alt+S)"}
            </button>
          </div>

          {/* Fields Card */}
          <div className={`bg-white dark:bg-slate-800 px-3 py-2`}>
            <div className="grid grid-cols-12 gap-2 items-end">
              {/* Posted Number */}
              <div className="col-span-2">
                <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Posted #</label>
                <input
                  type="text"
                  readOnly
                  value={form.posted_number || ""}
                  placeholder={returnId ? "" : "Auto on Save"}
                  autoComplete="off"
                  className={`w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] ${
                    isDark ? "bg-slate-700 text-slate-200 placeholder-slate-500" : "bg-gray-100 text-gray-800"
                  }`}
                />
              </div>

              {/* Date */}
              <div className="col-span-2">
                <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Date</label>
                <input
                  type="date"
                  name="date"
                  value={form.date || ""}
                  onChange={(e) => setForm({ ...form, date: e.target.value })}
                  autoComplete="off"
                  className="w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
                />
              </div>

              {/* Customer trigger button */}
              <div className="col-span-4">
                <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Customer *</label>
                <button
                  type="button"
                  onClick={() => setCustomerSearchOpen(true)}
                  className={`w-full h-9 px-3 rounded-lg border text-left text-sm flex items-center gap-2 transition-all ${
                    selectedCustomer
                      ? "border-blue-500 bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-200"
                      : "border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-700 text-gray-500 dark:text-gray-400 hover:border-blue-400"
                  }`}
                  title={selectedCustomer?.name || "Click to search customer..."}
                >
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className="flex-shrink-0">
                    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
                    <circle cx="12" cy="7" r="4" />
                  </svg>
                  {selectedCustomer ? (
                    <span className="truncate font-medium">{selectedCustomer.name}</span>
                  ) : (
                    <span className="truncate">Click to search customer...</span>
                  )}
                </button>
              </div>

              {/* Sale Invoice trigger button */}
              <div className="col-span-4">
                <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Sale Invoice (optional)</label>
                <button
                  type="button"
                  onClick={() => setInvoiceSearchOpen(true)}
                  className={`w-full h-9 px-3 rounded-lg border text-left text-sm flex items-center gap-2 transition-all ${
                    selectedInvoice
                      ? "border-blue-500 bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-200"
                      : "border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-700 text-gray-500 dark:text-gray-400 hover:border-blue-400"
                  }`}
                  title={selectedInvoice?.posted_number || "Click to search invoice..."}
                >
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className="flex-shrink-0">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                    <polyline points="14 2 14 8 20 8" />
                  </svg>
                  {selectedInvoice ? (
                    <span className="truncate font-medium">{selectedInvoice.posted_number}</span>
                  ) : (
                    <span className="truncate">Click to search invoice...</span>
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>

        {/* ITEMS */}
        <div className={`flex-1 overflow-auto p-1 ${isDark ? "bg-slate-900" : "bg-gray-50"}`}>
          <h2 className={`text-xs font-bold mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Items</h2>
          <table className={`w-full border-collapse text-[11px] ${isDark ? "bg-slate-800" : "bg-white"}`}>
            <thead className={`sticky top-0 z-5 ${isDark ? "bg-slate-700" : "bg-gray-100"}`}>
              <tr>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>#</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>Product</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>Batch</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>Expiry</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>{form.sale_invoice_id ? "Unit Sale Qty" : "Available Qty"}</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>Unit Sale Price</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>Return Qty (Units)</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>Disc %</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>Sub Total</th>
                <th className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>+</th>
              </tr>
            </thead>
            <tbody>
              {form.items.map((item, i) => {
                const exceeds = form.sale_invoice_id
                  ? toNum(item.unit_return_quantity) > toNum(item.unit_sale_quantity)
                  : false;
                return (
                  <tr key={item.id} className={`text-center ${isDark ? "hover:bg-slate-700/50" : "hover:bg-gray-50"}`}>
                    <td className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <button
                        type="button"
                        onClick={() => removeItem(i)}
                        className={`px-1 rounded text-[10px] transition-all duration-200 ${btnDanger.className}`}
                        style={btnDanger.style}
                      >
                        X
                      </button>
                    </td>
                    <td className={`border text-left w-[260px] ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <div ref={(el) => (productRefs.current[i] = el)}>
                        <ProductSearchInput
                          value={products.find((p) => eqId(p.id, item.product_id)) || item.product_id}
                          onChange={(val) => handleProductSelect(i, val)}
                          onRefreshProducts={refreshProducts}
                          products={products}
                          onKeyDown={(e) => onKeyNav(e, i, "product")}
                        />
                      </div>
                    </td>
                    <td className={`border w-20 ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <div ref={(el) => (batchRefs.current[i] = el)}>
                        <BatchSearchInput
                          value={item.batch_number}
                          batches={(rowBatches[i] || []).map((b) => ({
                            batch_number: b.batch_number,
                          }))}
                          onChange={(v) => handleBatchSelect(i, v)}
                          onKeyDown={(e) => onKeyNav(e, i, "batch")}
                        />
                      </div>
                    </td>
                    <td className={`border w-20 ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <input
                        type="text"
                        readOnly
                        value={item.expiry || ""}
                        className={`border w-full h-6 text-[11px] px-1 ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        }`}
                      />
                    </td>
                    <td className={`border w-24 ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <input
                        type="number"
                        readOnly
                        value={item.unit_sale_quantity || 0}
                        className={`border w-full h-6 text-[11px] px-1 ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        }`}
                      />
                    </td>
                    <td className={`border w-24 ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <input
                        type="number"
                        readOnly
                        value={item.unit_sale_price || 0}
                        className={`border w-full h-6 text-[11px] px-1 ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        }`}
                      />
                    </td>
                    <td className={`border w-24 ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <input
                        ref={(el) => (qtyRefs.current[i] = el)}
                        type="text"
                        value={item.unit_return_quantity === 0 ? "" : item.unit_return_quantity}
                        onChange={(e) => handleItemChange(i, "unit_return_quantity", e.target.value)}
                        className={
                          `border w-full h-6 text-[11px] px-1 ` +
                          (exceeds 
                            ? "border-red-500 ring-1 ring-red-400" 
                            : isDark 
                              ? "bg-slate-700 border-slate-600 text-slate-200" 
                              : "bg-white border-gray-300 text-gray-800"
                          )
                        }
                        onKeyDown={(e) => onKeyNav(e, i, "quantity")}
                      />
                    </td>
                    <td className={`border w-16 ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <input
                        ref={(el) => (discRefs.current[i] = el)}
                        type="text"
                        value={item.item_discount_percentage === 0 ? "" : item.item_discount_percentage}
                        onChange={(e) => handleItemChange(i, "item_discount_percentage", e.target.value)}
                        className={`border w-full h-6 text-[11px] px-1 ${
                          isDark 
                            ? "bg-slate-700 border-slate-600 text-slate-200" 
                            : "bg-white border-gray-300 text-gray-800"
                        }`}
                        onKeyDown={(e) => onKeyNav(e, i, "disc")}
                      />
                    </td>
                    <td className={`border w-20 ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <input
                        type="number"
                        readOnly
                        value={(Number(item.sub_total) || 0).toFixed(2)}
                        className={`border w-full h-6 text-[11px] px-1 ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        }`}
                      />
                    </td>
                    <td className={`border ${isDark ? "border-slate-600" : "border-gray-200"}`}>
                      <button
                        type="button"
                        onClick={addItem}
                        className={`px-1 rounded text-[10px] transition-all duration-200 ${btnSecondary.className}`}
                        style={btnSecondary.style}
                      >
                        +
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        {/* FOOTER */}
        <div className={`sticky bottom-0 shadow p-2 z-10 ${isDark ? "bg-slate-800 border-t border-slate-700" : "bg-white border-t border-gray-200"}`}>
          <table className="w-full border-collapse text-xs">
            <tbody>
              <tr>
                <td className={`border p-1 w-1/6 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`block text-[10px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Gross Total</label>
                  <input
                    type="number"
                    readOnly
                    value={(Number(form.gross_total) || 0).toFixed(2)}
                    className={`border rounded w-full p-1 h-7 text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-300" 
                        : "border-gray-300 bg-gray-100 text-gray-700"
                    }`}
                  />
                </td>
                <td className={`border p-1 w-1/6 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`block text-[10px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Discount %</label>
                  <input
                    type="number"
                    value={form.discount_percentage === 0 ? "" : form.discount_percentage}
                    onChange={(e) => onDiscountPctChange(e.target.value)}
                    className={`border rounded w-full p-1 h-7 text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-300 text-gray-800"
                    }`}
                  />
                </td>
                <td className={`border p-1 w-1/6 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`block text-[10px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Discount Amount</label>
                  <input
                    type="number"
                    value={form.discount_amount === 0 ? "" : form.discount_amount}
                    onChange={(e) => onDiscountAmtChange(e.target.value)}
                    className={`border rounded w-full p-1 h-7 text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-300 text-gray-800"
                    }`}
                  />
                </td>
                <td className={`border p-1 w-1/6 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`block text-[10px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Tax %</label>
                  <input
                    type="number"
                    value={form.tax_percentage === 0 ? "" : form.tax_percentage}
                    onChange={(e) => onTaxPctChange(e.target.value)}
                    className={`border rounded w-full p-1 h-7 text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-300 text-gray-800"
                    }`}
                  />
                </td>
                <td className={`border p-1 w-1/6 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`block text-[10px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Tax Amount</label>
                  <input
                    type="number"
                    value={form.tax_amount === 0 ? "" : form.tax_amount}
                    onChange={(e) => onTaxAmtChange(e.target.value)}
                    className={`border rounded w-full p-1 h-7 text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-300 text-gray-800"
                    }`}
                  />
                </td>
                <td className={`border p-1 w-1/6 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`block text-[10px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Total</label>
                  <input
                    type="number"
                    readOnly
                    value={(Number(form.total) || 0).toFixed(2)}
                    className={`border rounded w-full p-1 h-7 text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-rose-400" 
                        : "border-gray-300 bg-gray-100 text-red-600"
                    }`}
                  />
                </td>
              </tr>
              <tr>
                <td colSpan={6} className={`p-2 ${isDark ? "bg-slate-800" : "bg-white"}`}>
                  <div className="flex justify-end gap-2">
                    <button
                      type="button"
                      onClick={handleCancel}
                      className={`px-6 py-3 text-sm font-medium transition-all duration-200 ${btnGlass.className}`}
                      style={btnGlass.style}
                    >
                      Cancel
                    </button>
<button
                      type="button"
                      onClick={handleSubmit}
                      className={`px-8 py-3 text-sm font-semibold transition-all duration-200 ${btnPrimary.className}`}
                      style={btnPrimary.style}
                    >
                      {returnId ? "Update Return" : "Create Return"}
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        {/* Customer Search Modal */}
        <CustomerSearch
          isOpen={customerSearchOpen}
          onClose={() => setCustomerSearchOpen(false)}
          onSelect={(customer) => {
            handleSelectChange("customer_id", { value: customer?.id });
          }}
        />

        {/* Sale Invoice Search Modal */}
        <SaleInvoiceSearch
          isOpen={invoiceSearchOpen}
          onClose={() => setInvoiceSearchOpen(false)}
          onSelect={(invoice) => {
            handleSelectChange("sale_invoice_id", { value: invoice?.id });
          }}
        />
      </form>
    </div>
  );
}
