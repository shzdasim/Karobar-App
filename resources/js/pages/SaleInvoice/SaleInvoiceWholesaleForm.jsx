// /src/pages/sales/SaleInvoiceWholesaleForm.jsx - Wholesale Sale Invoice Form (hardcoded wholesale mode)
import { useEffect, useRef, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import ProductSearchInput from "../../components/ProductSearchInput.jsx";
import BatchSearchInput from "../../components/BatchSearchInput.jsx";
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

/* -------- utils -------- */
const normalizeFormLoaded = (f) => {
  const safe = { ...f };
  safe.invoice_type = safe.invoice_type ?? "debit";
  safe.sale_type = "wholesale"; // Always wholesale for this form
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
    ? safe.items.map((it) => {
        // For wholesale pack mode, convert quantity from units back to packs for display
        let displayQuantity = it?.quantity ?? "";
        let displayAvailable = it?.current_quantity ?? "";
        
        if (safe.wholesale_type === "pack" && it?.pack_size && Number(it.pack_size) > 0) {
          const packSize = Number(it.pack_size);
          displayQuantity = Math.floor(Number(it.quantity) / packSize);
          const currentAvailableInPacks = Math.floor(Number(it.current_quantity || 0) / packSize);
          const invoiceQtyInPacks = Math.floor(Number(it.quantity || 0) / packSize);
          displayAvailable = currentAvailableInPacks + invoiceQtyInPacks;
        }
        
        return {
          product_id: it?.product_id ?? "",
          pack_size: it?.pack_size ?? "",
          batch_number: it?.batch_number ?? "",
          expiry: it?.expiry ?? "",
          current_quantity: String(displayAvailable),
          quantity: String(displayQuantity),
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


export default function SaleInvoiceWholesaleForm({ saleId, onSuccess }) {
  /* -------- state -------- */
  const [form, setForm] = useState({
    invoice_type: "debit",
    sale_type: "wholesale", // Hardcoded to wholesale
    wholesale_type: "unit", // unit or pack
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

  // State for customer wholesale prices (fetched when customer is selected)
  const [customerWholesalePrices, setCustomerWholesalePrices] = useState({});
  // State to store product data for validation (pack_purchase_price)
  const [productDataCache, setProductDataCache] = useState({});
  // State to track which rows have invalid prices (below purchase price)
  const [invalidPriceRows, setInvalidPriceRows] = useState({});
  const [receiveTouched, setReceiveTouched] = useState(false);
  const [marginPct, setMarginPct] = useState("");
  // Store original invoice quantities for edit mode
  const [originalInvoiceQuantities, setOriginalInvoiceQuantities] = useState({});
  const navigate = useNavigate();

  // Calculate margin based on current price
  useEffect(() => {
    if (form.items.length > 0 && form.items[0].product_id) {
      const firstItem = form.items[0];
      
      if (firstItem.price) {
        const productData = productDataCache[firstItem.product_id];
        const packPurchasePrice = productData?.pack_purchase_price || 0;
        const packSize = Number(firstItem.pack_size || 1);
        
        let purchasePrice;
        if (form.wholesale_type === "pack") {
          purchasePrice = packPurchasePrice;
        } else {
          purchasePrice = packSize > 0 ? packPurchasePrice / packSize : 0;
        }
        
        const salePrice = Number(firstItem.price) || 0;
        
        if (purchasePrice > 0 && salePrice > 0) {
          const margin = ((salePrice - purchasePrice) / salePrice) * 100;
          setMarginPct(sanitizeNumberInput(String(margin.toFixed(2)), true));
          return;
        }
      }
    }
    setMarginPct("");
  }, [form.items, form.wholesale_type, productDataCache]);

  useEffect(() => {
    setMarginPct("");
    setReceiveTouched(false);
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
  const customerSelectRef = useRef(null);

  // scroll container ref for the items table
  const itemsScrollRef = useRef(null);

  /* -------- effects -------- */
  useEffect(() => {
    (async () => {
      await Promise.all([fetchCustomers(), fetchProducts()]);
      if (saleId) {
        await fetchSale();
      }
      // For wholesale create mode, focus on customer select first
      setTimeout(() => {
        if (!saleId && customerSelectRef.current) {
          customerSelectRef.current?.focus?.();
        }
      }, 100);
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
        secondary: '#8b5cf6',
        secondaryHover: '#7c3aed',
        danger: '#ef4444',
        dangerHover: '#dc2626',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      danger: theme.danger_color || '#ef4444',
      dangerHover: '#dc2626',
    };
  }, [theme]);

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

  // Helper to get react-select styles based on dark mode
  const getSelectStyles = (isDarkMode = false) => ({
    control: (base) => ({
      ...base,
      minHeight: "28px",
      height: "28px",
      fontSize: "11px",
      borderColor: isDarkMode ? "rgba(71,85,105,0.8)" : "rgba(0,0,0,0.8)",
      backgroundColor: isDarkMode ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.7)",
      backdropFilter: "blur(6px)",
      boxShadow: isDarkMode ? "0 1px 2px rgba(0,0,0,0.2)" : "none",
      borderRadius: 6,
      color: isDarkMode ? "#f1f5f9" : "#111827",
    }),
    valueContainer: (base) => ({
      ...base,
      height: "28px",
      padding: "0 6px",
    }),
    indicatorsContainer: (base) => ({
      ...base,
      height: "28px",
    }),
    input: (base) => ({
      ...base,
      margin: 0,
      padding: 0,
      color: isDarkMode ? "#f1f5f9" : "#111827",
    }),
    singleValue: (base) => ({
      ...base,
      color: isDarkMode ? "#f1f5f9" : "#111827",
    }),
    placeholder: (base) => ({
      ...base,
      color: isDarkMode ? "#64748b" : "#9ca3af",
    }),
    menu: (base) => ({
      ...base,
      fontSize: "12px",
      borderRadius: 8,
      overflow: "hidden",
      backgroundColor: isDarkMode ? "rgba(30,41,59,0.95)" : "rgba(255,255,255,0.95)",
      backdropFilter: "blur(10px)",
      boxShadow: isDarkMode ? "0 10px 30px -10px rgba(0,0,0,0.4)" : "0 10px 30px -10px rgba(30,64,175,0.18)",
      border: isDarkMode ? "1px solid rgba(71,85,105,0.5)" : "none",
    }),
    option: (base, state) => ({
      ...base,
      backgroundColor: isDarkMode
        ? state.isFocused
          ? "rgba(71,85,105,1)"
          : "rgba(51,65,85,1)"
        : state.isFocused
          ? "rgba(241,245,249,1)"
          : "rgba(255,255,255,1)",
      color: isDarkMode ? "#f1f5f9" : "#111827",
      cursor: "pointer",
    }),
    menuPortal: (base) => ({ ...base, zIndex: 9999 }),
  });

  /* -------- data -------- */
  const fetchCustomers = async () => {
    try {
      const res = await axios.get("/api/customers");
      let list = Array.isArray(res.data) ? res.data : [];
      list = list.slice().sort((a, b) => (a?.id ?? 0) - (b?.id ?? 0));
      setCustomers(list);
      // For wholesale, don't auto-select customer - require user to select
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
    
    // If editing a wholesale invoice, fetch customer wholesale prices
    if (loaded.customer_id) {
      fetchCustomerWholesalePrices(loaded.customer_id);
    }
    
    await ensureProductsForItems(loaded?.items || []);
    await ensureBatchesForItems(loaded?.items || []);
    
    // Store original invoice quantities for edit mode
    if (saleId) {
      const originalQtys = {};
      loaded.items.forEach((item, idx) => {
        if (item.product_id) {
          originalQtys[idx] = {
            product_id: item.product_id,
            quantity: item.quantity,
            pack_size: item.pack_size
          };
        }
      });
      setOriginalInvoiceQuantities(originalQtys);
    }
    
    setForm(loaded);
    setReceiveTouched(!shouldSync);
  };

  // Fetch customer-specific wholesale prices
  const fetchCustomerWholesalePrices = async (customerId) => {
    if (!customerId) return;
    try {
      const res = await axios.get(`/api/customers/${customerId}/whole-sale-prices`);
      const prices = {};
      if (Array.isArray(res.data)) {
        res.data.forEach(item => {
          prices[item.product_id] = item.pack_price;
        });
      }
      setCustomerWholesalePrices(prices);
    } catch (error) {
      console.error("Failed to fetch customer wholesale prices:", error);
      setCustomerWholesalePrices({});
    }
  };

  /* -------- handlers -------- */
  const handleInvoiceTypeChange = (type) => {
    setForm((prev) => {
      const next = { ...prev, invoice_type: type };
      if (type === "credit") {
        next.customer_id = "";
        next.total_receive = "";
        setReceiveTouched(true);
      } else {
        // Debit - auto-fill total_receive with total
        if (!receiveTouched) {
          next.total_receive = next.total ?? "";
        }
      }
      return next;
    });
  };

  // Handle Wholesale Type change (Unit vs Pack)
  const handleWholesaleTypeChange = (type) => {
    setForm((prev) => {
      let next = { ...prev, wholesale_type: type };
      
      // Recalculate prices and quantities when switching between unit and pack modes
      if (prev.items.length > 0) {
        const updatedItems = prev.items.map((item) => {
          if (!item.product_id) {
            return item;
          }
          
          const packSize = Number(item.pack_size) || 1;
          let newPrice = item.price;
          let newQuantity = item.quantity;
          
          // Check if there's a customer-specific wholesale price
          const customerPackPrice = prev.customer_id ? customerWholesalePrices[item.product_id] : null;
          
          if (type === "pack") {
            // Switching to pack mode: 
            // 1. Convert unit price to pack price (price * packSize)
            // 2. Convert unit quantity to pack quantity (quantity / packSize)
            if (customerPackPrice) {
              newPrice = customerPackPrice;
            } else if (item.price) {
              newPrice = Number(item.price) * packSize;
            }
            
            // Convert quantity from units to packs
            if (packSize > 0) {
              newQuantity = Math.floor(Number(item.quantity) / packSize);
              if (newQuantity === 0 && Number(item.quantity) > 0) {
                newQuantity = 1; // At least 1 pack if there's any quantity
              }
            }
          } else {
            // Switching to unit mode:
            // 1. Convert pack price to unit price (price / packSize)
            // 2. Convert pack quantity to unit quantity (quantity * packSize)
            if (customerPackPrice) {
              newPrice = packSize > 0 ? customerPackPrice / packSize : customerPackPrice;
            } else if (item.price) {
              newPrice = Number(item.price) / packSize;
            }
            
            // Convert quantity from packs to units
            newQuantity = Number(item.quantity) * packSize;
          }
          
          // Calculate new sub_total
          const qty = Number(newQuantity) || 0;
          const prc = Number(newPrice) || 0;
          const newSubTotal = qty * prc;
          
          return {
            ...item,
            price: newPrice ? String(newPrice.toFixed(2)) : item.price,
            quantity: String(newQuantity),
            sub_total: String(newSubTotal.toFixed(2)),
          };
        });
        
        next.items = updatedItems;
        // Recalculate footer totals with new items
        next = recalcFooter(next, "items");
      }
      
      return next;
    });
  };

  // Handle customer selection change
  const handleCustomerChange = (customerId) => {
    setForm((prev) => {
      const next = { ...prev, customer_id: customerId };
      // Fetch customer wholesale prices
      if (customerId) {
        fetchCustomerWholesalePrices(customerId);

        // Focus on first product field only if first row is empty (no product selected)
        setTimeout(() => {
          if (!prev.items[0]?.product_id) {
            productRefs.current[0]?.querySelector?.("input")?.focus?.();
          }
        }, 100);
      } else {
        setCustomerWholesalePrices({});
      }
      return next;
    });
  };

  const handleHeaderChange = (e) => {
    const { name, value } = e.target;
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
      
      // Handle price change - track custom price for wholesale
      if (field === "price") {
        const productId = items[index].product_id;
        const productData = productDataCache[productId];
        const minPrice = productData?.pack_purchase_price || 0;
        const newPrice = Number(value);
        
        items[index] = { 
          ...items[index], 
          is_custom_price: true 
        };
        
        // Track invalid price state (below purchase price)
        if (newPrice < minPrice && minPrice > 0) {
          setInvalidPriceRows(prev => ({ ...prev, [index]: true }));
        } else {
          setInvalidPriceRows(prev => {
            const newState = { ...prev };
            delete newState[index];
            return newState;
          });
        }
      }
      
      let row = recalcItem({ ...items[index], [field]: value }, field);
      if (field === "item_discount_percentage") row.item_discount_percentage = value;
      if (field === "price") {
        row.is_custom_price = true;
      }
      items[index] = row;
      let updated = recalcFooter({ ...prev, items }, "items");
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
        if (prev.invoice_type !== "credit") {
          afterFooter.total_receive = afterFooter.total ?? "";
        } else {
          afterFooter.total_receive = "";
        }
        return afterFooter;
      }
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
      if (!receiveTouched && prev.invoice_type !== "credit") next.total_receive = next.total ?? "";
      if (prev.invoice_type === "credit") next.total_receive = "";
      return next;
    });
  };

  const handleProductSelect = async (rowIndex, productIdOrObj) => {
    const productId = resolveId(productIdOrObj);
    if (!productId && productId !== 0) return;

    const currentRowProductId = form.items[rowIndex]?.product_id;
    
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

    let isNarcotic = selected?.narcotic === "yes";
    let freshProductData = null;
    
    if (productId) {
      try {
        const { data } = await axios.get(`/api/products/${productId}`);
        isNarcotic = data?.narcotic === "yes";
        freshProductData = data;
        
        setProductDataCache(prev => ({
          ...prev,
          [productId]: {
            pack_purchase_price: data?.pack_purchase_price || 0,
            whole_sale_unit_price: data?.whole_sale_unit_price || 0,
            unit_sale_price: data?.unit_sale_price || 0,
            margin: data?.margin || data?.margin_percentage || data?.default_margin || "",
          }
        }));
      } catch {
        if (selected) {
          setProductDataCache(prev => ({
            ...prev,
            [productId]: {
              pack_purchase_price: selected?.pack_purchase_price || 0,
              whole_sale_unit_price: selected?.whole_sale_unit_price || 0,
              unit_sale_price: selected?.unit_sale_price || 0,
              margin: selected?.margin || selected?.margin_percentage || selected?.default_margin || "",
            }
          }));
        }
      }
    }

    const packSize = freshProductData?.pack_size ?? selected?.pack_size ?? "";
    const baseAvailable = freshProductData?.quantity ?? freshProductData?.available_units ?? selected?.quantity ?? selected?.available_units ?? 0;
    
    // Wholesale mode pricing
    let price = "";
    let isCustomPrice = false;
    let displayAvailable = baseAvailable;
    
    // Calculate existing quantity in this row for edit mode
    let currentRowExistingQty = 0;
    if (saleId && originalInvoiceQuantities[rowIndex]) {
      const originalQty = originalInvoiceQuantities[rowIndex].quantity;
      const originalPackSize = originalInvoiceQuantities[rowIndex].pack_size;
      
      if (form.wholesale_type === "pack" && originalPackSize && Number(originalPackSize) > 0) {
        currentRowExistingQty = Number(originalQty || 0);
      } else {
        currentRowExistingQty = Number(originalQty || 0);
      }
    }
    
    if (form.wholesale_type === "pack") {
      // Pack mode
      if (packSize && Number(packSize) > 0) {
        displayAvailable = Math.floor(Number(baseAvailable) / Number(packSize));
      }
      displayAvailable = displayAvailable + currentRowExistingQty;
      
      // Check for customer-specific custom price (per pack)
      if (form.customer_id && customerWholesalePrices[productId]) {
        price = customerWholesalePrices[productId];
        isCustomPrice = true;
      } else if (freshProductData?.whole_sale_pack_price ?? selected?.whole_sale_pack_price) {
        price = freshProductData?.whole_sale_pack_price ?? selected?.whole_sale_pack_price;
      } else if (freshProductData?.pack_sale_price ?? selected?.pack_sale_price) {
        price = freshProductData?.pack_sale_price ?? selected?.pack_sale_price;
      } else if (freshProductData?.whole_sale_unit_price ?? selected?.whole_sale_unit_price) {
        const unitPrice = freshProductData?.whole_sale_unit_price ?? selected?.whole_sale_unit_price;
        price = Number(unitPrice) * Number(packSize);
      }
    } else {
      // Unit mode
      displayAvailable = Number(baseAvailable) + currentRowExistingQty;
      
      // Check for customer-specific custom price (per unit)
      if (form.customer_id && customerWholesalePrices[productId]) {
        const packPrice = customerWholesalePrices[productId];
        const packSz = Number(packSize) || 1;
        price = packSz > 0 ? packPrice / packSz : packPrice;
        isCustomPrice = true;
      } else if (freshProductData?.whole_sale_unit_price ?? selected?.whole_sale_unit_price) {
        price = freshProductData?.whole_sale_unit_price ?? selected?.whole_sale_unit_price;
      } else if (freshProductData?.unit_sale_price ?? selected?.unit_sale_price) {
        price = freshProductData?.unit_sale_price ?? selected?.unit_sale_price;
      }
    }

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
      
      const presetQty = eqId(currentRowProductId, productId)
        ? items[rowIndex].quantity
        : (items[rowIndex]?.quantity === "" || items[rowIndex]?.quantity == null
           ? "1"
           : items[rowIndex].quantity);
      
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
          handleBatchSelectWithCalculatedAvailable(rowIndex, batchNum, apiAvailable);
          return;
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

    // Wholesale requires customer selection
    if (!form.customer_id) {
      return toast.error("Please select a customer for wholesale sale");
    }

    // Validate customer selection for credit invoices
    if (form.invoice_type === "credit" && !form.customer_id) {
      return toast.error("Please select a customer for credit sale");
    }

    // Check if any item has a narcotic product
    const hasNarcoticProduct = form.items.some(item => item.is_narcotic === true);

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
      
      // Validate minimum price for wholesale
      const productData = productDataCache[it.product_id];
      const packPurchasePrice = productData?.pack_purchase_price || 0;
      const packSize = Number(it.pack_size || 1);
      
      let minPrice;
      if (form.wholesale_type === "pack") {
        minPrice = packPurchasePrice;
      } else {
        minPrice = packSize > 0 ? packPurchasePrice / packSize : 0;
      }
      
      if (Number(it.price || 0) < minPrice && minPrice > 0) {
        const priceLabel = form.wholesale_type === "pack" ? "pack purchase price" : "unit purchase price";
        return toast.error(`Row ${i + 1}: Price cannot be less than ${priceLabel} (${minPrice.toFixed(2)})`);
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

    // Always set sale_type to wholesale for this form
    const payload = {
      ...form,
      sale_type: "wholesale",
      invoice_type: form.invoice_type ?? "debit",
      discount_percentage:
        form.discount_percentage === "-" || form.discount_percentage === "-."
          ? "-0"
          : form.discount_percentage,
      items: form.items.map((it) => {
        let quantity = it.quantity;
        // For wholesale pack mode, convert pack quantity to unit quantity
        if (form.wholesale_type === "pack" && it.pack_size && Number(it.pack_size) > 0) {
          quantity = Number(it.quantity) * Number(it.pack_size);
        }
        return {
          ...it,
          quantity: quantity,
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
    } catch {
      toast.error("Failed to save sale invoice");
    }
  };

  // --- scroll helpers ---
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

  // --- keyboard nav (Wholesale: product -> batch -> quantity -> price -> disc) ---
  const COLS = ["product", "batch", "quantity", "price", "disc"];
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
  if (e.shiftKey && e.key === "Tab") {
    e.preventDefault();

    const i = COLS.indexOf(col);
    if (i > 0) {
      focusCell(row, COLS[i - 1]);
    } else if (i === 0 && row > 0) {
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
  return (
    <form
      className={`h-[calc(90vh-100px)] flex flex-col ${isDark ? "bg-slate-900" : "bg-white"}`}
      autoComplete="off" autoCapitalize="off" autoCorrect="off" spellCheck={false}
    >
      {/* Top Bar */}
      <div className={`shrink-0 sticky top-0 z-20 border-b ${isDark ? "bg-slate-800 border-slate-700" : "bg-white border-gray-200"}`}>
        <div className="px-2 py-1 flex items-center gap-2">
          <div className={`text-xs font-semibold ${isDark ? "text-slate-200" : "text-gray-800"}`}>Wholesale Sale Invoice</div>

          <div className="ml-auto flex items-center gap-2">
            {/* Invoice Type Radio Buttons */}
            <div className={`flex items-center gap-2 mr-2 px-2 py-1 rounded border ${isDark ? "bg-slate-700 border-slate-600" : "bg-gray-50 border-gray-200"}`}>
              <label className="flex items-center gap-1 cursor-pointer">
                <input
                  type="radio"
                  name="invoice_type"
                  value="debit"
                  checked={form.invoice_type === "debit"}
                  onChange={() => handleInvoiceTypeChange("debit")}
                  className="cursor-pointer"
                />
                <span className={`text-[10px] font-medium ${isDark ? "text-slate-300" : "text-gray-700"}`}>Debit</span>
              </label>
              <label className="flex items-center gap-1 cursor-pointer">
                <input
                  type="radio"
                  name="invoice_type"
                  value="credit"
                  checked={form.invoice_type === "credit"}
                  onChange={() => handleInvoiceTypeChange("credit")}
                  className="cursor-pointer"
                />
                <span className={`text-[10px] font-medium ${isDark ? "text-slate-300" : "text-gray-700"}`}>Credit</span>
              </label>
            </div>

            {/* Wholesale Type - Unit/Pack */}
            <div className={`flex items-center gap-2 mr-2 px-2 py-1 rounded border ${isDark ? "bg-purple-900/50 border-purple-700" : "bg-purple-50 border-purple-200"}`}>
              <label className="flex items-center gap-1 cursor-pointer">
                <input
                  type="radio"
                  name="wholesale_type"
                  value="unit"
                  checked={form.wholesale_type === "unit"}
                  onChange={() => handleWholesaleTypeChange("unit")}
                  className="cursor-pointer"
                />
                <span className={`text-[10px] font-semibold ${isDark ? "text-purple-300" : "text-purple-700"}`}>Unit</span>
              </label>
              <label className="flex items-center gap-1 cursor-pointer">
                <input
                  type="radio"
                  name="wholesale_type"
                  value="pack"
                  checked={form.wholesale_type === "pack"}
                  onChange={() => handleWholesaleTypeChange("pack")}
                  className="cursor-pointer"
                />
                <span className={`text-[10px] font-semibold ${isDark ? "text-purple-300" : "text-purple-700"}`}>Pack</span>
              </label>
            </div>

            <div className={`hidden sm:flex items-center gap-2 text-[10px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>
              <span className="inline-flex items-center gap-1">
                <span className={`px-1 py-0.5 border rounded ${isDark ? "bg-slate-700 border-slate-600 text-slate-300" : "bg-gray-50 border-gray-200"}`}>Alt</span>
                <span>+</span>
                <span className={`px-1 py-0.5 border rounded ${isDark ? "bg-slate-700 border-slate-600 text-slate-300" : "bg-gray-50 border-gray-200"}`}>S</span>
                <span>Save</span>
              </span>
              <span>•</span>
              <span>Enter→next</span>
              <span>•</span>
              <span>↑/↓ rows</span>
            </div>

            <button
              type="button"
              onClick={handleSubmit}
              className={`px-3 py-1.5 rounded text-[11px] font-semibold transition-all duration-200 ${btnPrimary.className}`}
              style={btnPrimary.style}
            >
              {saleId ? "Update (Alt+S)" : "Create (Alt+S)"}
            </button>
          </div>
        </div>

        {/* Meta strip */}
        <div className="px-2 pb-1 grid grid-cols-12 gap-1 text-[11px]">
          <div className="col-span-2">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Posted #</label>
            <input
              type="text"
              readOnly
              value={form.posted_number || ""}
              placeholder={saleId ? "" : "Auto on Save"}
              autoComplete="off"
              className={`w-full h-7 border-2 rounded px-1 ${
                isDark 
                  ? "border-slate-600 bg-slate-700 text-slate-200 placeholder-slate-500" 
                  : "border-black bg-gray-100 text-gray-800"
              }`}
            />
          </div>
          <div className="col-span-2">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Date</label>
            <input
              type="date"
              name="date"
              value={form.date ?? ""}
              onChange={handleHeaderChange}
              autoComplete="off"
              className={`w-full h-7 border-2 rounded px-1 ${
                isDark 
                  ? "border-slate-600 bg-slate-700 text-slate-200" 
                  : "border-black text-gray-800"
              }`}
            />
          </div>
          <div className="col-span-4">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Customer *</label>
            <Select
              ref={customerSelectRef}
              options={customers.map((c) => ({ value: c.id, label: c.name }))}
              value={
                customers
                  .map((c) => ({ value: c.id, label: c.name }))
                  .find((s) => s.value === form.customer_id) || null
              }
              onChange={(val) => handleCustomerChange(val?.value || "")}
              className="text-[11px]"
              name="customer_select" inputId="customer_select" aria-autocomplete="list"
              styles={getSelectStyles(isDark)}
              isSearchable
            />
          </div>
          <div className="col-span-2">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Doctor</label>
            <input
              type="text"
              name="doctor_name"
              value={form.doctor_name ?? ""}
              onChange={handleHeaderChange}
              autoComplete="off"
              className={`w-full h-7 border-2 rounded px-1 ${
                isDark 
                  ? "border-slate-600 bg-slate-700 text-slate-200" 
                  : "border-black text-gray-800"
              }`}
            />
          </div>
            <div className="col-span-2">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Patient</label>
            <input
              type="text"
              name="patient_name"
              value={form.patient_name ?? ""}
              onChange={handleHeaderChange}
              autoComplete="off"
              className={`w-full h-7 border-2 rounded px-1 ${
                isDark 
                  ? "border-slate-600 bg-slate-700 text-slate-200" 
                  : "border-black text-gray-800"
              }`}
            />
          </div>

          <div className="col-span-10">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Remarks</label>
            <input
              type="text"
              name="remarks"
              value={form.remarks ?? ""}
              onChange={handleHeaderChange}
              autoComplete="off"
              className={`w-full h-7 border-2 rounded px-1 ${
                isDark 
                  ? "border-slate-600 bg-slate-700 text-slate-200" 
                  : "border-black text-gray-800"
              }`}
            />
          </div>
          <div className="col-span-2">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Items</label>
            <input
              type="text"
              readOnly
              value={form.items.length}
              autoComplete="off"
              className={`w-full h-7 border-2 rounded px-1 text-center ${
                isDark 
                  ? "border-slate-600 bg-slate-700 text-slate-200" 
                  : "border-black bg-gray-100 text-gray-800"
              }`}
            />
          </div>
        </div>
      </div>

      {/* Main workspace: Items + Summary */}
      <div className={`flex-1 grid grid-cols-[1fr_240px] gap-2 px-2 py-2 overflow-hidden ${isDark ? "bg-slate-900" : "bg-white"}`}>
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
                  <tr key={i} className={`border-b ${isDark ? "border-slate-700 hover:bg-slate-700/50" : "border-gray-100 hover:bg-gray-50"}`}>
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
                      <div ref={(el) => (productRefs.current[i] = el)}>
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
                      <div ref={(el) => (batchRefs.current[i] = el)}>
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
                        onFocus={(e) => e.target.select()}
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
                        } cursor-not-allowed ${invalidPriceRows[i] ? "border-red-500 ring-1 ring-red-400" : ""}`}
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
                        onFocus={(e) => e.target.select()}
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
          <div className="sticky top-[20px] space-y-2">
            <div className={`border-2 rounded p-2 ${isDark ? "bg-slate-800 border-slate-700" : "bg-white border-gray-200"}`}>
              <div className={`text-[18px] font-semibold mb-1 ${isDark ? "text-slate-200" : "text-gray-800"}`}>Summary</div>
              <div className="grid grid-cols-2 gap-1 text-[11px]">
                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Margin %</label>
                <input
                  type="text"
                  name="margin_percentage"
                  readOnly
                  value={marginPct}
                  onChange={(e) => setMarginPct(sanitizeNumberInput(e.target.value, true))}
                  autoComplete="off"
                  className={`h-7 border-2 rounded px-1 font-extrabold text-lg ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-rose-400 placeholder-slate-500" 
                      : "border-black bg-gray-100 text-red-600"
                  }`}
                />

                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Tax %</label>
                <input
                  type="text"
                  name="tax_percentage"
                  value={form.tax_percentage ?? ""}
                  onChange={handleHeaderChange}
                  autoComplete="off"
                  className={`h-7 border-2 rounded px-1 ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-slate-200" 
                      : "border-black text-gray-800"
                  }`}
                />

                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Tax Amt</label>
                <input
                  type="text"
                  name="tax_amount"
                  value={form.tax_amount ?? ""}
                  onChange={handleHeaderChange}
                  autoComplete="off"
                  className={`h-7 border-2 rounded px-1 ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-slate-200" 
                      : "border-black text-gray-800"
                  }`}
                />

                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Disc %</label>
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
                  className={`h-7 border-2 rounded px-1 ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-slate-200" 
                      : "border-black text-gray-800"
                  }`}
                />

                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Disc Amt</label>
                <input
                  type="text"
                  name="discount_amount"
                  value={form.discount_amount ?? ""}
                  onChange={handleHeaderChange}
                  autoComplete="off"
                  className={`h-7 border-2 rounded px-1 ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-slate-200" 
                      : "border-black text-gray-800"
                  }`}
                />

                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Gross</label>
                <input
                  type="text"
                  readOnly
                  value={form.gross_amount ?? ""}
                  autoComplete="off"
                  className={`h-7 border-2 rounded px-1 ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-slate-300" 
                      : "border-black bg-gray-100 text-gray-700"
                  }`}
                />

                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Total</label>
                <input
                  type="text"
                  readOnly
                  value={form.total ?? ""}
                  autoComplete="off"
                  className={`h-7 border-2 rounded px-1 font-extrabold text-lg ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-rose-400" 
                      : "border-black bg-gray-100 text-red-600"
                  }`}
                />

                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Receive</label>
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
                  className={`h-7 border-2 rounded px-1 ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-slate-200" 
                      : "border-black text-gray-800"
                  } ${form.invoice_type === "debit" ? (isDark ? "cursor-not-allowed bg-slate-800" : "bg-gray-100 cursor-not-allowed") : ""}`}
                />

                <label className={`text-[13px] font-bold self-center ${isDark ? "text-slate-300" : "text-gray-700"}`}>Remaining</label>
                <input
                  type="text"
                  readOnly
                  value={String(
                    Math.round(Number(form.total || 0)) - Math.round(Number(form.total_receive || 0))
                  )}
                  autoComplete="off"
                  className={`h-7 border-2 rounded px-1 cursor-not-allowed ${
                    isDark 
                      ? "border-slate-600 bg-slate-700 text-slate-300" 
                      : "border-black bg-gray-100 text-gray-700"
                  }`}
                />
              </div>

              <div className="pt-2">
                <button
                  type="button"
                  onClick={handleSubmit}
                  className={`w-full h-9 rounded text-white text-[12px] font-semibold transition-all duration-200 ${btnPrimary.className}`}
                  style={btnPrimary.style}
                >
                  {saleId ? "Update Sale" : "Create Sale"}
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </form>
  );
}

