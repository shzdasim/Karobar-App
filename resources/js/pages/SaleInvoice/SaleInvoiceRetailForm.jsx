// /src/pages/sales/SaleInvoiceRetailForm.jsx - Retail Sale Invoice Form (hardcoded retail mode)
import { useEffect, useRef, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import ProductSearchInput from "../../components/ProductSearchInput.jsx";
import BatchSearchInput from "../../components/BatchSearchInput.jsx";
import CustomerSearch from "../../components/CustomerSearch.jsx";
import { recalcItem, recalcFooter } from "../../Formula/SaleInvoice.js";
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

/* -------- utils (unchanged) -------- */
const normalizeFormLoaded = (f) => {
  const safe = { ...f };
  safe.invoice_type = safe.invoice_type ?? "debit";
  safe.sale_type = "retail"; // Always retail for this form
  safe.wholesale_type = safe.wholesale_type ?? "unit";
  safe.remarks = safe.remarks ?? "";
  safe.doctor_name = safe.doctor_name ?? "";
  safe.patient_name = safe.patient_name ?? "";
  safe.date = safe.date ?? new Date().toISOString().split("T")[0];
  safe.discount_percentage = safe.discount_percentage ?? "";
  safe.discount_amount = safe.discount_amount ?? "";
  safe.tax_percentage = safe.tax_percentage ?? "";
  safe.tax_amount = safe.tax_amount ?? "";
  safe.item_discount = safe.item_discount ?? "";
  safe.gross_amount = safe.gross_amount ?? "";
  safe.total = safe.total ?? "";
  safe.total_receive = safe.total_receive ?? "";
  safe.items = Array.isArray(safe.items)
    ? safe.items.map((it, idx) => {
        return {
          id: it?.id ?? `row_${Date.now()}_${idx}_${Math.random().toString(36).substr(2, 9)}`,
          product_id: it?.product_id ?? "",
          pack_size: it?.pack_size ?? "",
          batch_number: it?.batch_number ?? "",
          expiry: it?.expiry ?? "",
          current_quantity: it?.current_quantity ?? "",
          quantity: it?.quantity ?? "",
          price: it?.price ?? "",
          item_discount_percentage: it?.item_discount_percentage ?? "",
          sub_total: it?.sub_total ?? "",
          is_narcotic: it?.is_narcotic ?? it?.narcotic === "yes" ?? false,
          is_custom_price: it?.is_custom_price ?? false,
        };
      })
    : [];
  return safe;
};


export default function SaleInvoiceRetailForm({ saleId, onSuccess }) {
  // Helper to generate unique ID for rows
  const generateRowId = () => `row_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

  /* -------- state -------- */
  const [form, setForm] = useState({
    invoice_type: "debit",
    sale_type: "retail", // Hardcoded to retail
    wholesale_type: "unit",
    customer_id: "",
    posted_number: "",
    date: new Date().toISOString().split("T")[0],
    remarks: "",
    doctor_name: "",
    patient_name: "",
    discount_percentage: "",
    discount_amount: "",
    tax_percentage: "",
    tax_amount: "",
    item_discount: "",
    gross_amount: "",
    total: "",
    total_receive: "",
    items: [
      {
        id: generateRowId(),
        product_id: "",
        pack_size: "",
        batch_number: "",
        expiry: "",
        current_quantity: "",
        quantity: "",
        price: "",
        item_discount_percentage: "",
        sub_total: "",
        is_narcotic: false,
        is_custom_price: false,
      },
    ],
  });

  // State to store product data for validation
  const [productDataCache, setProductDataCache] = useState({});
  const [receiveTouched, setReceiveTouched] = useState(false);
  const [marginPct, setMarginPct] = useState("");
  const [focusedRowIndex, setFocusedRowIndex] = useState(0);
  const [originalItems, setOriginalItems] = useState({}); // Store original items for edit mode to calculate available quantity correctly
  const [customerSearchOpen, setCustomerSearchOpen] = useState(false);
  const navigate = useNavigate();

  // Calculate margin based on sale type (retail) - use focused row
  useEffect(() => {
    const rowIndex = focusedRowIndex >= 0 && focusedRowIndex < form.items.length ? focusedRowIndex : 0;
    if (form.items.length > 0 && form.items[rowIndex].product_id) {
      const focusedItem = form.items[rowIndex];
      if (focusedItem.price) {
        // For retail, use the cached product margin
        const productData = productDataCache[focusedItem.product_id];
        if (productData?.margin) {
          setMarginPct(sanitizeNumberInput(String(productData.margin), true));
          return;
        }
      }
    }
    setMarginPct("");
  }, [form.items, productDataCache, focusedRowIndex]);

  useEffect(() => {
    setMarginPct("");
    setReceiveTouched(false);
    setOriginalItems({}); // Clear original items when creating a new invoice
  }, [saleId]);

  const [customers, setCustomers] = useState([]);
  const [products, setProducts] = useState([]);
  const [batchesByProduct, setBatchesByProduct] = useState({});
  const byIdsSupportedRef = useRef(true);

  // refs for grid navigation
  const productRefs = useRef([]);
  const batchRefs = useRef([]);
  const qtyRefs = useRef([]);
  const priceRefs = useRef([]);
  const discRefs = useRef([]);
  const focusedOnce = useRef(false);

  // scroll container ref for the items table
  const itemsScrollRef = useRef(null);

  /* -------- effects -------- */
  useEffect(() => {
    (async () => {
      await Promise.all([fetchCustomers(), fetchProducts()]);
      if (saleId) {
        await fetchSale();
      }
      // focus first product on brand new form
      setTimeout(() => {
        if (!focusedOnce.current && !saleId) {
          productRefs.current[0]?.querySelector?.("input")?.focus?.();
          focusedOnce.current = true;
        }
      }, 60);
    })();
  }, [saleId]);

  // Alt+S save
  useEffect(() => {
    const handle = (e) => {
      if (e.altKey && e.key.toLowerCase() === "s") {
        e.preventDefault();
        handleSubmit(e);
      }
    };
    document.addEventListener("keydown", handle);
    return () => document.removeEventListener("keydown", handle);
  }, [form]);

  /* -------- helpers -------- */
  const to2 = (n) => Number(parseFloat(n || 0).toFixed(2));
  const asISODate = (s) => {
    if (!s) return "";
    const d = new Date(s);
    if (!isNaN(d.getTime())) return d.toISOString().slice(0, 10);
    const m = /^(\d{2})[\/-](\d{2})[\/-](\d{4})$/.exec(String(s));
    if (m) return `${m[3]}-${m[2]}-${m[1]}`;
    return String(s);
  };
  const sanitizeNumberInput = (value, allowDecimal = false, allowNegative = false) => {
    if (value === "") return "";
    const sign = allowNegative ? "-?" : "";
    const regex = allowDecimal
      ? new RegExp(`^${sign}\\d*\\.?\\d*$`)
      : new RegExp(`^${sign}\\d*$`);
    if (regex.test(value)) return value;
    return value.slice(0, -1);
  };
  const eqId = (a, b) => String(a ?? "") === String(b ?? "");
  const zeroToEmpty = (v) => (v === 0 || v === "0" ? "" : (v ?? ""));

  // Get dark mode state and theme colors
  const { isDark, theme } = useTheme();

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
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
        tertiaryLight: '#cffafe',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
      tertiary: theme.tertiary_color || '#06b6d4',
      tertiaryHover: theme.tertiary_hover || '#0891b2',
      tertiaryLight: theme.tertiary_light || '#cffafe',
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

  // 🎨 Dynamic Button styles using theme colors
  const buttonStyle = theme?.button_style || 'rounded';
  
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
      };
    }
    
    // Filled styles for rounded and soft
    return {
      primary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
          color: 'white',
          boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
        }
      },
      secondary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
          color: 'white',
          boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`,
        }
      },
      danger: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
          color: 'white',
          boxShadow: '0 4px 14px 0 rgba(239, 68, 68, 0.4)',
        }
      },
    };
  }, [buttonStyle, themeColors]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;

/* -------- data -------- */
  const fetchCustomers = async () => {
    try {
      const res = await axios.get("/api/customers");
      let list = Array.isArray(res.data) ? res.data : [];
      // pick the LOWEST ID as default
      list = list.slice().sort((a, b) => (a?.id ?? 0) - (b?.id ?? 0));
      setCustomers(list);
      if (!saleId && !form.customer_id && list.length > 0) {
        setForm((prev) => ({ ...prev, customer_id: list[0].id }));
      }
    } catch {}
  };

  const fetchProducts = async (q = "") => {
    try {
      const { data } = await axios.get("/api/products/search", { params: { q, limit: 30 } });
      setProducts(Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : []);
    } catch {}
  };

  const fetchBatches = async (productId) => {
    if (!productId) return [];
    const key = String(productId);
    if (batchesByProduct[key]) return batchesByProduct[key];
    const normalizeBatch = (b) => ({
      batch_number: String(b?.batch_number ?? b?.batch ?? b?.number ?? "").trim(),
      expiry: b?.expiry ?? b?.expiration_date ?? b?.expiry_date ?? "",
      available_units: Number(b?.available_units ?? b?.available_quantity ?? b?.quantity ?? 0),
      pack_size: Number(b?.pack_size ?? 0),
    });
    try {
      const res = await axios.get(`/api/products/${productId}/batches`);
      const raw = Array.isArray(res.data) ? res.data : [];
      const list = raw.map(normalizeBatch).filter((x) => x.batch_number && x.available_units > 0);
      setBatchesByProduct((m) => ({ ...m, [key]: list }));
      return list;
    } catch {
      setBatchesByProduct((m) => ({ ...m, [key]: [] }));
      return [];
    }
  };

  const fetchSale = async () => {
    const res = await axios.get(`/api/sale-invoices/${saleId}`);
    const loaded = normalizeFormLoaded(res.data);
    const tr = Math.round(Number(loaded?.total_receive ?? 0));
    const tt = Math.round(Number(loaded?.total ?? 0));
    const shouldSync = loaded?.total_receive === "" || tr === tt;
    
    await ensureProductsForItems(loaded?.items || []);
    await ensureBatchesForItems(loaded?.items || []);
    
    // Store original items for edit mode to calculate available quantity correctly
    // When editing, current available quantity should include the quantity sold in this invoice
    const originalItemsMap = {};
    if (loaded?.items && Array.isArray(loaded.items)) {
      loaded.items.forEach((item) => {
        if (item.product_id) {
          originalItemsMap[item.product_id] = {
            quantity: Number(item.quantity || 0),
            batch_number: item.batch_number,
          };
        }
      });
    }
    setOriginalItems(originalItemsMap);
    
    setForm(loaded);
    setReceiveTouched(!shouldSync);
  };

  /* -------- handlers -------- */
  const handleInvoiceTypeChange = (type) => {
    setForm((prev) => {
      const next = { ...prev, invoice_type: type };
      // When switching to credit, clear customer_id to force selection
      // and set total_receive to 0 (true credit sale)
      if (type === "credit") {
        next.customer_id = "";
        next.total_receive = "";
        setReceiveTouched(true); // Mark as touched so it stays at 0
      } else {
        // Debit - auto-select lowest ID customer if none selected
        if (!next.customer_id && customers.length > 0) {
          const sortedCustomers = [...customers].sort((a, b) => (a?.id ?? 0) - (b?.id ?? 0));
          next.customer_id = sortedCustomers[0].id;
        }
        // For debit, auto-fill total_receive with total
        if (!receiveTouched) {
          next.total_receive = next.total ?? "";
        }
      }
      return next;
    });
  };

  // Retail form - no sale type change handler needed (always retail)
  const handleSaleTypeChange = (type) => {
    // This form is always retail, ignore wholesale switch
    if (type === "wholesale") {
      toast.error("Please use Wholesale form for wholesale sales");
      return;
    }
  };

  const handleHeaderChange = (e) => {
    const { name, value } = e.target;
    // allow negatives for these summary fields
    const negativeFields = new Set([
      "discount_percentage",
      "discount_amount",
      "tax_percentage",
      "tax_amount",
    ]);
    const allowNegative = negativeFields.has(name);
    const decimalFields = new Set([
      "discount_percentage",
      "discount_amount",
      "tax_percentage",
      "tax_amount",
    ]);
    const v = decimalFields.has(name)
      ? sanitizeNumberInput(value, true, allowNegative)
      : value;
    const tmp = { ...form, [name]: v };
    let next = recalcFooter(tmp, name);
    next[name] = v;
    // For credit sales, total_receive stays at 0; for debit, auto-fill if not touched
    if (!receiveTouched && form.invoice_type !== "credit") next.total_receive = next.total ?? "";
    if (form.invoice_type === "credit") next.total_receive = "";
    setForm(next);
  };

  function handleItemChange(index, field, rawValue) {
    let value = rawValue;
    const allowNegative = field === "item_discount_percentage";
    if (field === "price" || field === "item_discount_percentage") {
      value = sanitizeNumberInput(value, true, allowNegative);
      if (field === "item_discount_percentage" && (value === "-" || value === "-."))
        return setForm((prev) => {
          const items = [...prev.items];
          items[index] = { ...items[index], item_discount_percentage: value };
          return { ...prev, items };
        });
    } else if (field === "quantity" || field === "pack_size") {
      value = sanitizeNumberInput(value, false, false);
    }
    setForm((prev) => {
      const items = [...prev.items];
      if (field === "quantity") {
        const available = Number(items[index].current_quantity || 0);
        const prevQtyNum = Number(items[index].quantity || 0);
        const nextQtyNum = Number(value || 0);
        if (nextQtyNum > available && prevQtyNum <= available) {
          toast.error(`Row ${index + 1}: quantity exceeds available (${available})`);
        }
      }
      let row = recalcItem({ ...items[index], [field]: value }, field);
      if (field === "item_discount_percentage") row.item_discount_percentage = value;
      items[index] = row;
      let updated = recalcFooter({ ...prev, items }, "items");
      // For credit sales, total_receive stays at 0; for debit, auto-fill if not touched
      if (!receiveTouched && prev.invoice_type !== "credit") updated.total_receive = updated.total ?? "";
      if (prev.invoice_type === "credit") updated.total_receive = "";
      return updated;
    });
  }

  const addRow = () => {
    setForm((prev) => {
      const next = {
        ...prev,
        items: [
          ...prev.items,
          {
            id: generateRowId(),
            product_id: "",
            pack_size: "",
            batch_number: "",
            expiry: "",
            current_quantity: "",
            quantity: "",
            price: "",
            item_discount_percentage: "",
            sub_total: "",
            is_narcotic: false,
            is_custom_price: false,
          },
        ],
      };
      if (!receiveTouched) {
        const afterFooter = recalcFooter(next, "items");
        // For credit sales, total_receive stays at 0; for debit, auto-fill if not touched
        if (prev.invoice_type !== "credit") {
          afterFooter.total_receive = afterFooter.total ?? "";
        } else {
          afterFooter.total_receive = "";
        }
        return afterFooter;
      }
      // For credit sales, ensure total_receive stays at 0
      if (prev.invoice_type === "credit") {
        next.total_receive = "";
      }
      return next;
    });
  };

  const removeRow = (i) => {
    if (form.items.length <= 1) return;
    const items = form.items.filter((_, idx) => idx !== i);
    let next = recalcFooter({ ...form, items }, "items");
    // For credit sales, total_receive stays at 0; for debit, auto-fill if not touched
    if (!receiveTouched && form.invoice_type !== "credit") next.total_receive = next.total ?? "";
    if (form.invoice_type === "credit") next.total_receive = "";
    setForm(next);
  };

  const resolveId = (val) =>
    typeof val === "object" ? val?.id ?? val?.value ?? val?.product_id : val;

  const resetRow = (rowIndex) => {
    setForm((prev) => {
      const items2 = [...prev.items];
      items2[rowIndex] = recalcItem(
        {
          product_id: "",
          pack_size: "",
          price: "",
          batch_number: "",
          expiry: "",
          current_quantity: "",
          quantity: "",
          sub_total: "",
          item_discount_percentage: "",
          is_narcotic: false,
          is_custom_price: false,
        },
        "revert_duplicate_product"
      );
      let next = recalcFooter({ ...prev, items: items2 }, "items");
      // For credit sales, total_receive stays at 0; for debit, auto-fill if not touched
      if (!receiveTouched && prev.invoice_type !== "credit") next.total_receive = next.total ?? "";
      if (prev.invoice_type === "credit") next.total_receive = "";
      return next;
    });
  };

  const handleProductSelect = async (rowIndex, productIdOrObj) => {
    const productId = resolveId(productIdOrObj);
    if (!productId && productId !== 0) return;

    const currentRowProductId = form.items[rowIndex]?.product_id;
    
    // Only check for duplicate if the product is different from current row's product
    if (!eqId(currentRowProductId, productId)) {
      const dupIndex = form.items.findIndex(
        (row, idx) => idx !== rowIndex && eqId(row.product_id, productId)
      );
      if (dupIndex !== -1) {
        toast.error(`Product already used in row ${dupIndex + 1}. Each product can be added only once.`);
        resetRow(rowIndex);
        setTimeout(() => {
          productRefs.current[rowIndex]?.querySelector?.("input")?.focus?.();
        }, 40);
        return;
      }
    }

    const selected =
      products.find((p) => eqId(p.id, productId)) ||
      (typeof productIdOrObj === "object" ? productIdOrObj : {}) ||
      {};

    // Ensure we have the narcotic field - fetch from API if not present
    let isNarcotic = selected?.narcotic === "yes";
    let freshProductData = null;
    
    if (productId) {
      try {
        const { data } = await axios.get(`/api/products/${productId}`);
        isNarcotic = data?.narcotic === "yes";
        freshProductData = data;
        
        // Cache the product data for validation
        setProductDataCache(prev => ({
          ...prev,
          [productId]: {
            pack_purchase_price: data?.pack_purchase_price || 0,
            unit_sale_price: data?.unit_sale_price || 0,
            margin: data?.margin || data?.margin_percentage || data?.default_margin || "",
          }
        }));
      } catch {
        // If API fails, use cached data
        if (selected) {
          setProductDataCache(prev => ({
            ...prev,
            [productId]: {
              pack_purchase_price: selected?.pack_purchase_price || 0,
              unit_sale_price: selected?.unit_sale_price || 0,
              margin: selected?.margin || selected?.margin_percentage || selected?.default_margin || "",
            }
          }));
        }
      }
    }

    const rawMargin = freshProductData?.margin ?? selected?.margin ?? selected?.margin_percentage ?? selected?.default_margin ?? "";
    setMarginPct(sanitizeNumberInput(String(rawMargin), true));
    const packSize = freshProductData?.pack_size ?? selected?.pack_size ?? "";
    
    // Use fresh product data if available, otherwise fall back to cached data
    const baseAvailable = freshProductData?.quantity ?? freshProductData?.available_units ?? selected?.quantity ?? selected?.available_units ?? 0;
    
    // In edit mode, add back the original quantity sold for this product
    // This ensures available quantity = current stock + quantity sold in this invoice
    const originalItem = originalItems[productId];
    const originalQty = originalItem ? originalItem.quantity : 0;
    const displayAvailable = Number(baseAvailable) + originalQty;
    
    // Retail mode: use unit_sale_price
    let price = freshProductData?.unit_sale_price ?? selected?.unit_sale_price ?? "";
    let isCustomPrice = false;

    const batchList = await fetchBatches(productId);
    const hasBatches = Array.isArray(batchList) && batchList.length > 0;

    // Preserve existing values if re-selecting the same product
    const existingBatchNumber = eqId(currentRowProductId, productId)
      ? form.items[rowIndex]?.batch_number
      : "";
    const existingExpiry = eqId(currentRowProductId, productId)
      ? form.items[rowIndex]?.expiry
      : "";

    setForm((prev) => {
      const items = [...prev.items];
      
      // If quantity is empty (new product selection), preset it to 1
      // If re-selecting same product, keep the existing quantity
      const presetQty = eqId(currentRowProductId, productId)
        ? items[rowIndex].quantity
        : (items[rowIndex]?.quantity === "" || items[rowIndex]?.quantity == null
           ? "1"
           : items[rowIndex].quantity);
      
      // When re-selecting same product, preserve batch and expiry if they exist
      const shouldPreserveBatch = eqId(currentRowProductId, productId) && existingBatchNumber;
      
      items[rowIndex] = recalcItem(
        {
          ...items[rowIndex],
          product_id: productId,
          pack_size: packSize,
          price,
          batch_number: shouldPreserveBatch ? existingBatchNumber : "",
          expiry: shouldPreserveBatch ? existingExpiry : "",
          current_quantity: displayAvailable.toString(),
          quantity: presetQty,
          item_discount_percentage: items[rowIndex]?.item_discount_percentage ?? "",
          sub_total: items[rowIndex]?.sub_total ?? "",
          is_narcotic: isNarcotic,
          is_custom_price: isCustomPrice,
        },
        "product_select"
      );
      let next = recalcFooter({ ...prev, items }, "items");
      // For credit sales, total_receive stays at 0; for debit, auto-fill if not touched
      if (!receiveTouched && prev.invoice_type !== "credit") next.total_receive = next.total ?? "";
      if (prev.invoice_type === "credit") next.total_receive = "";
      return next;
    });

    setTimeout(() => {
      if (hasBatches) {
        batchRefs.current[rowIndex]?.querySelector?.("input")?.focus?.();
      } else {
        qtyRefs.current[rowIndex]?.focus?.();
      }
    }, 40);
  };

  const handleBatchSelect = async (rowIndex, batchNum) => {
    const row0 = form.items[rowIndex];
    if (!row0.product_id || !batchNum) return;
    
    try {
      const batches = await fetchBatches(row0.product_id);
      const b = (batches || []).find((x) => String(x.batch_number) === String(batchNum));
      
      const baseAvailable = Number(b?.available_units ?? 0);
      const available = baseAvailable;
      
      const params = new URLSearchParams({
        product_id: row0.product_id || "",
        batch: batchNum || "",
      }).toString();
      
      try {
        const res = await axios.get(`/api/products/available-quantity?${params}`);
        const apiAvailable = Number(
          res?.data?.available_units ?? res?.data?.available ?? res?.data?.quantity ?? 0
        );
        if (apiAvailable > 0) {
          return handleBatchSelectWithCalculatedAvailable(rowIndex, batchNum, apiAvailable);
        }
      } catch {}
      
      handleBatchSelectWithCalculatedAvailable(rowIndex, batchNum, baseAvailable);
    } catch {}
  };

  const handleBatchSelectWithCalculatedAvailable = (rowIndex, batchNum, baseAvailable) => {
    const available = Number(baseAvailable);
    const exp = asISODate(form.items[rowIndex]?.expiry || "");
    
    setForm((prev) => {
      const items = [...prev.items];
      const updated = {
        ...items[rowIndex],
        batch_number: batchNum,
        current_quantity: String(available),
      };
      if (exp) updated.expiry = exp;
      items[rowIndex] = recalcItem(updated, "batch_select");
      let next = recalcFooter({ ...prev, items }, "items");
      // For credit sales, total_receive stays at 0; for debit, auto-fill if not touched
      if (!receiveTouched && prev.invoice_type !== "credit") next.total_receive = next.total ?? "";
      if (prev.invoice_type === "credit") next.total_receive = "";
      return next;
    });
    setTimeout(() => {
      qtyRefs.current[rowIndex]?.focus?.();
    }, 40);
  };

  const upsertProducts = (list) => {
    if (!Array.isArray(list)) return;
    setProducts((prev) => {
      const map = new Map((prev || []).map((p) => [String(p.id), p]));
      list.forEach((p) => p?.id != null && map.set(String(p.id), p));
      return Array.from(map.values());
    });
  };

  const ensureProductsForItems = async (items = []) => {
    const ids = Array.from(new Set(items.map((it) => it.product_id).filter(Boolean))).map(String);
    if (!ids.length) return;
    const have = new Set((products || []).map((p) => String(p.id)));
    const missing = ids.filter((id) => !have.has(id));
    if (!missing.length) return;

    if (byIdsSupportedRef.current) {
      try {
        const { data } = await axios.get("/api/products/by-ids", {
          params: { ids: missing.join(",") },
        });
        const list = Array.isArray(data?.data) ? data.data : Array.isArray(data) ? data : [];
        upsertProducts(list);
        return;
      } catch (err) {
        if (err?.response?.status === 404) {
          byIdsSupportedRef.current = false;
        }
      }
    }

    const fetched = await Promise.all(
      missing.map(async (id) => {
        try {
          const { data } = await axios.get(`/api/products/${id}`);
          return data;
        } catch {
          try {
            const { data } = await axios.get("/api/products/search", { params: { q: id, limit: 1 } });
            return Array.isArray(data?.data) ? data.data[0] : Array.isArray(data) ? data[0] : null;
          } catch {
            return null;
          }
        }
      })
    );
    upsertProducts(fetched.filter(Boolean));
  };

  const ensureBatchesForItems = async (items = []) => {
    const productIds = Array.from(new Set(items.map((it) => it.product_id).filter(Boolean)));
    for (const pid of productIds) {
      try {
        await fetchBatches(pid);
      } catch {}
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();

    // Validate customer selection for credit invoices
    if (form.invoice_type === "credit" && !form.customer_id) {
      return toast.error("Please select a customer for credit sale");
    }

    // Check if any item has a narcotic product
    const hasNarcoticProduct = form.items.some(item => item.is_narcotic === true);

    // If any item is narcotic, require doctor and patient names
    if (hasNarcoticProduct) {
      if (!form.doctor_name || form.doctor_name.trim() === "") {
        return toast.error("Doctor name is required for narcotic products");
      }
      if (!form.patient_name || form.patient_name.trim() === "") {
        return toast.error("Patient name is required for narcotic products");
      }
    }

    for (let i = 0; i < form.items.length; i++) {
      const it = form.items[i];
      if (!it.product_id) return toast.error(`Row ${i + 1}: select a product`);
      const list = batchesByProduct[String(it.product_id)];
      const hasBatches = Array.isArray(list) && list.length > 0;
      if (hasBatches && !it.batch_number) return toast.error(`Row ${i + 1}: select a batch`);
      if (it.quantity === "" || it.quantity == null)
        return toast.error(`Row ${i + 1}: enter quantity`);
      const available = Number(it.current_quantity || 0);
      if (Number(it.quantity || 0) > available) {
        return toast.error(`Row ${i + 1}: quantity exceeds available (${available})`);
      }
    }

    // unique product validation
    {
      const seen = new Set();
      for (let i = 0; i < form.items.length; i++) {
        const id = form.items[i].product_id;
        if (!id) continue;
        if (seen.has(String(id))) {
          toast.error(`Duplicate product in row ${i + 1}. Each product can be added only once.`);
          return;
        }
        seen.add(String(id));
      }
    }

    // total_receive validation
    {
      const totalNum = Math.round(Number(form.total || 0));
      const recvNum = Math.round(Number(form.total_receive || 0));
      if (recvNum < 0) return toast.error("Total Receive cannot be negative");
      if (recvNum > totalNum) return toast.error("Total Receive cannot exceed Total");
    }

    // Always set sale_type to retail for this form
    const payload = {
      ...form,
      sale_type: "retail",
      invoice_type: form.invoice_type ?? "debit",
      discount_percentage:
        form.discount_percentage === "-" || form.discount_percentage === "-."
          ? "-0"
          : form.discount_percentage,
      items: form.items.map((it) => {
        // For new items (string IDs starting with 'row_'), exclude the id field
        // For existing items (numeric IDs), convert id to integer
        const isNewItem = String(it.id).startsWith('row_') || !it.id;
        const { id, ...itemWithoutId } = it;
        return {
          ...itemWithoutId,
          ...(isNewItem ? {} : { id: parseInt(it.id, 10) }),
          quantity: it.quantity,
          item_discount_percentage:
            it.item_discount_percentage === "-" || it.item_discount_percentage === "-."
              ? "-0"
              : it.item_discount_percentage,
        };
      }),
    };

    try {
      if (saleId) {
        const res = await axios.put(`/api/sale-invoices/${saleId}`, payload);
        const id = res?.data?.id ?? saleId;
        toast.success("Sale invoice updated");
        navigate(`/sale-invoices/${id}`);
      } else {
        const res = await axios.post("/api/sale-invoices", payload);
        const id = res?.data?.id;
        toast.success("Sale invoice created");
        if (id) navigate(`/sale-invoices/${id}`);
        else toast.error("Missing invoice ID from server response.");
      }
    } catch (err) {
      console.error("Save invoice error:", err?.response?.data);
      const errorMsg = err?.response?.data?.message 
        || (err?.response?.data?.errors ? JSON.stringify(err.response.data.errors) : "Failed to save sale invoice");
      toast.error(errorMsg);
    }
  };

  // --- scroll helpers (unchanged logic) ---
  const scrollTargetIntoItemsView = (node) => {
    const container = itemsScrollRef.current;
    if (!container || !node) return;

    const target = node.closest?.("tr") || node;

    const head = container.querySelector("thead");
    const headH = head ? head.getBoundingClientRect().height : 0;

    const targetTop = target.offsetTop;
    const targetBottom = targetTop + target.offsetHeight;

    const viewTop = container.scrollTop;
    const viewBottom = viewTop + container.clientHeight;

    if (targetTop < viewTop + headH + 4) {
      container.scrollTop = Math.max(targetTop - headH - 4, 0);
    } else if (targetBottom > viewBottom - 4) {
      container.scrollTop = targetBottom - container.clientHeight + 4;
    }
  };

  // --- keyboard nav (Retail: product -> batch -> quantity -> disc) ---
  const COLS = ["product", "batch", "quantity", "disc"];
  const focusCell = (row, col) => {
    const map = { product: productRefs, batch: batchRefs, quantity: qtyRefs, price: priceRefs, disc: discRefs };
    const ref = map[col]?.current?.[row];
    if (!ref) return;
    const input = ref.querySelector?.("input") || ref;
    if (input?.focus) {
      input.focus();
      input.select?.();
    } else if (ref?.focus) {
      ref.focus();
    }
    requestAnimationFrame(() => {
      scrollTargetIntoItemsView(input || ref);
    });
  };
  const moveSameCol = (row, col, dir) => {
    const lastIdx = form.items.length - 1;
    if (dir === 1) {
      if (row === lastIdx) {
        addRow();
        setTimeout(() => focusCell(row + 1, col === "quantity" || col === "disc" ? "product" : col), 40);
      } else {
        focusCell(row + 1, col);
      }
    } else if (row > 0) {
      focusCell(row - 1, col);
    }
  };
  const moveNextCol = (row, col) => {
    const i = COLS.indexOf(col);
    if (i < 0) return;
    if (i < COLS.length - 1) focusCell(row, COLS[i + 1]);
    else {
      const lastIdx = form.items.length - 1;
      if (row === lastIdx) {
        addRow();
        setTimeout(() => focusCell(row + 1, COLS[0]), 40);
      } else focusCell(row + 1, COLS[0]);
    }
  };
  const onKeyNav = (e, row, col) => {
  // Handle Ctrl + Tab for reverse navigation
  if (e.shiftKey && e.key === "Tab") {
    e.preventDefault();

    const i = COLS.indexOf(col);
    if (i > 0) {
      // Go to previous editable field
      focusCell(row, COLS[i - 1]);
    } else if (i === 0 && row > 0) {
      // If first column, go to previous row's last editable col
      focusCell(row - 1, COLS[COLS.length - 1]);
    }
    return;
  }

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


  /* ===================== R E N D E R ===================== */
  const selectedCustomer = (customers || []).find(
    (c) => String(c.id) === String(form.customer_id)
  );

  return (
    <form
      className={`h-[calc(100vh-110px)] flex flex-col ${isDark ? "bg-slate-900" : "bg-white"}`}
      autoComplete="off" autoCapitalize="off" autoCorrect="off" spellCheck={false}
    >
      {/* Header */}
      <div className={`shrink-0 sticky top-0 z-20 shadow-lg border-b ${isDark ? "border-slate-700" : "border-gray-200"}`}>
        {/* Branded Banner */}
        <div
          className="px-4 py-2.5 flex items-center justify-between gap-3"
          style={{
            background: `linear-gradient(135deg, ${themeColors.tertiary || themeColors.secondary}, ${themeColors.primary}, ${themeColors.primaryHover})`,
          }}
        >
          <div className="flex items-center gap-3">
            <div
              className="w-9 h-9 rounded-xl flex items-center justify-center shadow-inner"
              style={{ backgroundColor: "rgba(255,255,255,0.18)", backdropFilter: "blur(4px)" }}
            >
              {/* Sale / receipt icon */}
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                <path d="M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1-2 1z" />
                <line x1="8" y1="7" x2="16" y2="7" />
                <line x1="8" y1="11" x2="16" y2="11" />
                <line x1="8" y1="15" x2="13" y2="15" />
              </svg>
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-extrabold tracking-wide text-white leading-none">SALE INVOICE</h2>
                <span
                  className="px-2 py-0.5 rounded-md text-[9px] font-bold tracking-widest text-white"
                  style={{ backgroundColor: "rgba(255,255,255,0.22)", backdropFilter: "blur(4px)" }}
                >
                  RETAIL
                </span>
                <span
                  className={`px-2 py-0.5 rounded-md text-[9px] font-bold tracking-widest text-white ${
                    saleId ? "bg-amber-400/90" : "bg-emerald-400/90"
                  }`}
                  style={{ color: "#1e293b" }}
                >
                  {saleId ? "EDIT" : "CREATE"}
                </span>
              </div>
              <p className="text-[10px] text-white/80 mt-0.5">
                Alt+S save · Enter=next · ↑/↓ rows
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2.5">
            {/* Invoice Type Segmented Control */}
            <div
              className="flex items-center rounded-lg p-0.5 shadow-inner"
              style={{ backgroundColor: "rgba(0,0,0,0.18)" }}
            >
              {["debit", "credit"].map((type) => {
                const active = form.invoice_type === type;
                return (
                  <button
                    key={type}
                    type="button"
                    onClick={() => handleInvoiceTypeChange(type)}
                    className={`px-3.5 py-1.5 text-[11px] font-semibold rounded-md transition-all duration-200 ${
                      active ? "text-white shadow" : "text-white/80 hover:text-white"
                    }`}
                    style={{
                      backgroundColor: active ? "rgba(255,255,255,0.22)" : "transparent",
                      backdropFilter: active ? "blur(4px)" : "none",
                    }}
                  >
                    {type === "debit" ? "💳 Debit" : "🤝 Credit"}
                  </button>
                );
              })}
            </div>

            <button
              type="button"
              onClick={handleSubmit}
              className="px-4 py-2 rounded-lg text-[11px] font-bold bg-white/95 hover:bg-white shadow-lg transition-all duration-200"
              style={{ color: themeColors.primaryHover }}
            >
              {saleId ? "Edit (Alt+S)" : "Create (Alt+S)"}
            </button>
          </div>
        </div>

        {/* Fields Card */}
        <div className="bg-white dark:bg-slate-800 px-3 py-2">
          <div className="grid grid-cols-12 gap-2 items-end">
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Posted #</label>
              <input
                type="text"
                readOnly
                value={form.posted_number || ""}
                placeholder={saleId ? "" : "Auto on Save"}
                autoComplete="off"
                className={`w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] ${
                  isDark 
                    ? "bg-slate-700 text-slate-200 placeholder-slate-500" 
                    : "bg-gray-100 text-gray-800"
                }`}
              />
            </div>
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Date</label>
              <input
                type="date"
                name="date"
                value={form.date ?? ""}
                onChange={handleHeaderChange}
                autoComplete="off"
                className={`w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200`}
              />
            </div>
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
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Doctor</label>
              <input
                type="text"
                name="doctor_name"
                value={form.doctor_name ?? ""}
                onChange={handleHeaderChange}
                autoComplete="off"
                className="w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Patient</label>
              <input
                type="text"
                name="patient_name"
                value={form.patient_name ?? ""}
                onChange={handleHeaderChange}
                autoComplete="off"
                className="w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
              />
            </div>

            <div className="col-span-10">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Remarks</label>
              <input
                type="text"
                name="remarks"
                value={form.remarks ?? ""}
                onChange={handleHeaderChange}
                autoComplete="off"
                className="w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
              />
            </div>
            <div className="col-span-2">
              <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Items</label>
              <input
                type="text"
                readOnly
                value={form.items.length}
                autoComplete="off"
                className={`w-full h-8 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-[11px] text-center ${
                  isDark ? "bg-slate-700 text-slate-200" : "bg-gray-100 text-gray-800"
                }`}
              />
            </div>
          </div>
        </div>
      </div>

      {/* Main workspace: Items + Summary */}
      <div className={`flex-1 grid grid-cols-[1fr_280px] gap-2 px-2 py-2 overflow-hidden ${isDark ? "bg-slate-900" : "bg-white"}`}>
        {/* LEFT: Items */}
        <div className="flex flex-col min-h-0">
          <div className={`text-[11px] font-semibold mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Items</div>
          <div ref={itemsScrollRef} className={`flex-1 overflow-auto border-2 rounded relative ${isDark ? "border-slate-700 bg-slate-800" : "border-gray-200 bg-white"}`}>
            <table className="w-full text-[11px] table-fixed border-collapse" autoComplete="off">
              <thead className={`sticky top-0 z-20 ${isDark ? "bg-slate-800/90 backdrop-blur-sm border-slate-700" : "bg-white/80 backdrop-blur-sm border-gray-200/70"} border-b`}>
                <tr className="[&>th]:py-1 [&>th]:px-1 [&>th]:text-left">
                  <th className={`w-7 text-center ${isDark ? "text-rose-400" : "text-red-600"}`}>DEL</th>
                  <th className={`w-7 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>#</th>
                  <th className={`w-[180px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Product</th>
                  <th className={`w-14 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>PSize</th>
                  <th className={`w-24 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Batch</th>
                  <th className={`w-15 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Expiry</th>
                  <th className={`w-18 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Avail</th>
                  <th className={`w-25 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Qty</th>
                  <th className={`w-22 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Price</th>
                  <th className={`w-18 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Disc%</th>
                  <th className={`w-26 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>Sub Total</th>
                  <th className="w-7 text-center">+</th>
                </tr>
              </thead>
              <tbody className="[&>tr>td]:py-1 [&>tr>td]:px-0.2">
                {form.items.map((it, i) => (
                  <tr key={it.id} className={`border-b ${isDark ? "border-slate-700 hover:bg-slate-700/50" : "border-gray-100 hover:bg-gray-50"}`}>
                    <td className="text-center">
                      <button
                        type="button"
                        onClick={() => removeRow(i)}
                        className={`px-2 rounded text-white text-[10px] font-semibold transition-all duration-200 ${btnDanger.className}`}
                        style={btnDanger.style}
                      >
                        X
                      </button>
                    </td>
                    <td className={`text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>{i + 1}</td>

                    <td>
                      <div ref={(el) => (productRefs.current[i] = el)} onFocus={() => setFocusedRowIndex(i)}>
                        <ProductSearchInput
                          value={products.find((p) => eqId(p.id, it.product_id)) || it.product_id}
                          onChange={(val) => handleProductSelect(i, val)}
                          onKeyDown={(e) => onKeyNav(e, i, "product")}
                          products={products}
                          onRefreshProducts={fetchProducts}
                          inputProps={{ autoComplete: "off", autoCapitalize: "off", autoCorrect: "off", spellCheck: false }}
                        />
                      </div>
                    </td>

                    <td className="text-center">
                      <input
                        type="text"
                        readOnly
                        value={it.pack_size ?? ""}
                        autoComplete="off"
                        className={`w-full h-6 border rounded px-1 text-center ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        }`}
                      />
                    </td>

                    <td>
                      <div ref={(el) => (batchRefs.current[i] = el)} onFocus={() => setFocusedRowIndex(i)}>
                        <BatchSearchInput
                          value={it.batch_number}
                          onChange={(val) => handleBatchSelect(i, val)}
                          batches={(batchesByProduct[it.product_id] || [])
                            .filter((b) => (b.available_units ?? 0) > 0)
                            .map((b) => ({
                              ...b,
                              batch_number: b.batch_number || b.batch,
                            }))}
                          usedBatches={form.items
                            .filter((row, idx) => idx !== i && row.product_id === it.product_id)
                            .map((row) => row.batch_number)
                            .filter(Boolean)}
                          onKeyDown={(e) => onKeyNav(e, i, "batch")}
                          inputProps={{ autoComplete: "off", autoCapitalize: "off", autoCorrect: "off", spellCheck: false }}
                        />
                      </div>
                    </td>

                    <td className="text-center">
                      <input
                        type="date"
                        value={it.expiry ?? ""}
                        readOnly
                        autoComplete="off"
                        className={`w-full h-6 border rounded px-1 text-center ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        }`}
                      />
                    </td>

                    <td className="text-center">
                      <input
                        type="text"
                        readOnly
                        value={it.current_quantity ?? ""}
                        autoComplete="off"
                        className={`w-full h-6 border rounded px-1 text-center ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        }`}
                      />
                    </td>

                    <td className="text-center">
                      <input
                        ref={(el) => (qtyRefs.current[i] = el)}
                        type="text"
                        inputMode="numeric"
                        value={zeroToEmpty(it.quantity)}
                        onChange={(e) => handleItemChange(i, "quantity", e.target.value)}
                        autoComplete="off"
                        className={
                          `w-full h-6 border rounded px-1 text-center ${
                            isDark 
                              ? "bg-slate-700 border-slate-600 text-slate-200" 
                              : "bg-white border-gray-300 text-gray-800"
                          } ` +
                          (Number(it.quantity || 0) > Number(it.current_quantity || 0)
                            ? "border-red-500 ring-1 ring-red-400"
                            : "")
                        }
                        onKeyDown={(e) => onKeyNav(e, i, "quantity")}
                        onFocus={(e) => {
                          setFocusedRowIndex(i);
                          e.target.select();
                        }}
                      />
                    </td>

                    <td className="text-center">
                      <input
                        ref={(el) => (priceRefs.current[i] = el)}
                        type="text"
                        value={to2(it.price ?? "")}
                        readOnly
                        autoComplete="off"
                        className={`w-full h-6 border rounded px-1 text-center ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        } cursor-not-allowed`}
                      />
                    </td>

                    <td className="text-center">
                      <input
                        ref={(el) => (discRefs.current[i] = el)}
                        type="text"
                        inputMode="decimal"
                        value={zeroToEmpty(it.item_discount_percentage)}
                        onChange={(e) => handleItemChange(i, "item_discount_percentage", e.target.value)}
                        onBlur={(e) => {
                          const v = e.target.value;
                          if (v !== "" && v !== "-" && v !== "-.") {
                            const num = Number(v);
                            if (Number.isFinite(num)) {
                              handleItemChange(i, "item_discount_percentage", num.toFixed(2));
                            }
                          }
                        }}
                        autoComplete="off"
                        className={`w-full h-6 border rounded px-1 text-center ${
                          isDark 
                            ? "bg-slate-700 border-slate-600 text-slate-200" 
                            : "bg-white border-gray-300 text-gray-800"
                        }`}
                        onKeyDown={(e) => onKeyNav(e, i, "disc")}
                        onFocus={(e) => {
                          setFocusedRowIndex(i);
                          e.target.select();
                        }}
                      />
                    </td>

                    <td className="text-center">
                      <input
                        type="text"
                        readOnly
                        value={it.sub_total ?? ""}
                        autoComplete="off"
                        className={`w-full h-6 border rounded px-1 text-center ${
                          isDark 
                            ? "border-slate-600 bg-slate-700 text-slate-300" 
                            : "border-gray-200 bg-gray-100 text-gray-700"
                        }`}
                      />
                    </td>

                    <td className="text-center">
                      <button
                        type="button"
                        onClick={addRow}
                        className={`px-2 rounded text-white text-[10px] font-semibold transition-all duration-200 ${btnSecondary.className}`}
                        style={btnSecondary.style}
                      >
                        +
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* RIGHT: Summary */}
        <div className="min-h-0">
          <div className={`h-full flex flex-col rounded-2xl overflow-hidden shadow-lg ${isDark ? "bg-slate-800" : "bg-white"}`}>
            {/* Panel Header */}
            <div
              className="px-4 py-3 flex items-center justify-between shrink-0"
              style={{ background: `linear-gradient(135deg, ${themeColors.primary}, ${themeColors.primaryHover})` }}
            >
              <span className="text-white font-bold text-sm tracking-wide">Invoice Summary</span>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="18" y1="20" x2="18" y2="10" />
                <line x1="12" y1="20" x2="12" y2="4" />
                <line x1="6" y1="20" x2="6" y2="14" />
              </svg>
            </div>

{/* Non-scrollable content */}
            <div className="flex-1 flex flex-col min-h-0 px-4 py-3 text-[12px]">
              {/* Equal-width inputs */}
              <div className="space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Margin %</label>
                  <input
                    type="text"
                    name="margin_percentage"
                    readOnly
                    value={marginPct}
                    onChange={(e) => setMarginPct(sanitizeNumberInput(e.target.value, true))}
                    autoComplete="off"
                    className={`w-28 h-8 border rounded-lg px-2 text-center font-extrabold text-[13px] ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-rose-400 placeholder-slate-500" 
                        : "border-gray-200 bg-gray-50 text-red-600"
                    }`}
                  />
                </div>

                <div className="flex items-center justify-between gap-2">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Tax %</label>
                  <input
                    type="text"
                    name="tax_percentage"
                    value={form.tax_percentage ?? ""}
                    onChange={handleHeaderChange}
                    autoComplete="off"
                    className={`w-28 h-8 border rounded-lg px-2 text-center text-[12px] ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    }`}
                  />
                </div>

                <div className={`flex items-center justify-between border-t border-dashed pt-2 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Tax Amt</label>
                  <input
                    type="text"
                    name="tax_amount"
                    value={form.tax_amount ?? ""}
                    onChange={handleHeaderChange}
                    autoComplete="off"
                    className={`w-28 h-8 border rounded-lg px-2 text-right font-semibold text-[12px] ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    }`}
                  />
                </div>

                <div className="flex items-center justify-between gap-2">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Disc %</label>
                  <input
                    type="text"
                    name="discount_percentage"
                    value={form.discount_percentage ?? ""}
                    onChange={handleHeaderChange}
                    onBlur={(e) => {
                      const v = e.target.value;
                      if (v !== "" && v !== "-" && v !== "-.") {
                        const num = Number(v);
                        if (Number.isFinite(num)) {
                          handleHeaderChange({ target: { name: "discount_percentage", value: num.toFixed(2) } });
                        }
                      }
                    }}
                    autoComplete="off"
                    className={`w-28 h-8 border rounded-lg px-2 text-center text-[12px] ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    }`}
                  />
                </div>

                <div className={`flex items-center justify-between border-t border-dashed pt-2 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Disc Amt</label>
                  <input
                    type="text"
                    name="discount_amount"
                    value={form.discount_amount ?? ""}
                    onChange={handleHeaderChange}
                    autoComplete="off"
                    className={`w-28 h-8 border rounded-lg px-2 text-right font-semibold text-[12px] ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    }`}
                  />
                </div>
              </div>

              {/* Totals */}
              <div
                className="mt-3 px-4 py-3 rounded-xl"
                style={{ background: `linear-gradient(135deg, ${themeColors.primary}1a, ${themeColors.primaryHover}26)` }}
              >
                <div className="flex items-center justify-between mb-1">
                  <span className={`text-[10px] font-medium uppercase tracking-wide ${isDark ? "text-slate-400" : "text-gray-500"}`}>Gross</span>
                  <span className={`text-[13px] font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>{form.gross_amount ?? 0}</span>
                </div>
                <div className="flex items-center justify-between border-t border-dashed pt-2" style={{ borderColor: `${themeColors.primary}33` }}>
                  <span className={`text-[13px] font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>Total</span>
                  <span className="text-2xl font-extrabold" style={{ color: themeColors.primary }}>
                    {form.total ?? 0}
                  </span>
                </div>
              </div>

              {/* Payment */}
              <div className="mt-3 space-y-2">
                <div className="flex items-center justify-between">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Received</label>
                  <input
                    type="text"
                    name="total_receive"
                    inputMode="numeric"
                    readOnly={form.invoice_type === "debit"}
                    value={form.total_receive ?? ""}
                    onChange={(e) => {
                      const v = sanitizeNumberInput(e.target.value, true, false);
                      setReceiveTouched(true);
                      setForm((prev) => ({ ...prev, total_receive: v }));
                    }}
                    onBlur={() => {
                      setForm((prev) => ({
                        ...prev,
                        total_receive: String(Math.round(Number(prev.total_receive || 0))),
                      }));
                    }}
                    autoComplete="off"
                    className={`w-28 h-8 border rounded-lg px-2 text-right font-semibold text-[12px] ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    } ${form.invoice_type === "debit" ? (isDark ? "cursor-not-allowed bg-slate-800" : "bg-gray-100 cursor-not-allowed") : ""}`}
                  />
                </div>

                <div className="flex items-center justify-between">
                  <label className={`font-semibold ${isDark ? "text-slate-400" : "text-gray-500"}`}>Remaining</label>
                  <span
                    className={`text-lg font-extrabold ${
                      Number(form.total || 0) - Number(form.total_receive || 0) > 0
                        ? "text-amber-500"
                        : (isDark ? "text-emerald-400" : "text-emerald-600")
                    }`}
                  >
                    {Math.round(Number(form.total || 0)) - Math.round(Number(form.total_receive || 0))}
                  </span>
                </div>
              </div>
            </div>

            <div className="px-4 py-3 shrink-0 border-t" style={{ borderColor: isDark ? "rgba(71,85,105,0.5)" : "rgba(229,231,235,0.8)" }}>
              <button
                type="button"
                onClick={handleSubmit}
                className={`w-full h-11 rounded-xl text-white text-[13px] font-bold transition-all duration-200 ${btnPrimary.className}`}
                style={{ ...btnPrimary.style, boxShadow: `0 8px 20px -6px ${themeColors.primary}80` }}
>
                {saleId ? "Edit Sale" : "Create Sale"}
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Customer Search Modal */}
      <CustomerSearch
        isOpen={customerSearchOpen}
        onClose={() => setCustomerSearchOpen(false)}
        onSelect={(customer) => {
          setForm((prev) => ({ ...prev, customer_id: customer?.id || "" }));
        }}
      />
    </form>
  );
}

