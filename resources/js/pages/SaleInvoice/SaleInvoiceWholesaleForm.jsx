import { useInvoiceErrors, InvoiceErrorSummary, InvoiceRowError } from "../../components/InvoiceErrors";
// /src/pages/sales/SaleInvoiceWholesaleForm.jsx - Wholesale Sale Invoice Form (hardcoded wholesale mode)
import { useEffect, useRef, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import Select from "react-select";
import { TrashIcon, PlusIcon, CubeIcon, HashtagIcon, CheckIcon } from "@heroicons/react/24/outline";
import toast from "react-hot-toast";
import ProductSearchInput from "../../components/ProductSearchInput.jsx";
import BatchSearchInput from "../../components/BatchSearchInput.jsx";
import CustomerSearch from "../../components/CustomerSearch.jsx";
import { recalcItem, recalcFooter } from "../../Formula/SaleInvoice.js";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import { useInvoiceDraft } from "@/hooks/useInvoiceDraft.js";
import { useCurrency } from "@/context/CurrencyContext.jsx";
import InvoiceDraftIndicator from "@/components/InvoiceDraftIndicator.jsx";
import InvoiceDraftsModal from "@/components/InvoiceDraftsModal.jsx";
import PreShiftDrawerPromptModal from "@/components/PreShiftDrawerPromptModal.jsx";
import CreditOverrideModal from "@/components/CreditOverrideModal.jsx";
import DiscountOverrideModal from "@/components/DiscountOverrideModal.jsx";
import PaymentAccountSelector from "../../components/PaymentAccountSelector.jsx";

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

export const PAYMENT_METHOD_OPTIONS = [
  { value: "cash", label: "Cash" },
  { value: "card", label: "Card / POS" },
  { value: "bank_transfer", label: "Bank Transfer" },
  { value: "cheque", label: "Cheque" },
  { value: "mobile_wallet", label: "Mobile Wallet" },
  { value: "other", label: "Other" },
];

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
  safe.due_date = safe.due_date ?? "";
  safe.discount_percentage = safe.discount_percentage ?? "";
  safe.discount_amount = safe.discount_amount ?? "";
  safe.tax_percentage = safe.tax_percentage ?? "";
  safe.tax_amount = safe.tax_amount ?? "";
  safe.item_discount = safe.item_discount ?? "";
  safe.gross_amount = safe.gross_amount ?? "";
  safe.total = safe.total ?? "";
  safe.total_receive = safe.total_receive ?? "";
  safe.cash_given = safe.cash_given ?? "";
  safe.payment_method = safe.payment_method ?? "cash";
  safe.account_id = safe.account_id ?? null;
  safe.items = Array.isArray(safe.items)
    ? safe.items.map((it, idx) => {
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
          id: it?.id ?? `row_${Date.now()}_${idx}_${Math.random().toString(36).substr(2, 9)}`,
          product_id: it?.product_id ?? "",
          product_name: it?.product_name ?? it?.product?.name ?? "",
          product: it?.product ?? null,
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
  // Helper to generate unique ID for rows
  const generateRowId = () => `row_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

  /* -------- state -------- */
  const [customers, setCustomers] = useState([]);
  const customersRef = useRef([]);
  useEffect(() => {
    customersRef.current = customers;
  }, [customers]);

  const getDefaultCustomerId = (list = customersRef.current || customers) => {
    if (!Array.isArray(list) || list.length === 0) return "";
    const walkIn = list.find((c) => (c.name || "").toUpperCase().includes("WALK-IN"));
    if (walkIn?.id) return walkIn.id;
    const sorted = [...list].sort((a, b) => (a?.id ?? 0) - (b?.id ?? 0));
    return sorted[0]?.id || "";
  };

  const [form, setForm] = useState({
    invoice_type: "debit",
    sale_type: "wholesale", // Hardcoded to wholesale
    wholesale_type: "unit", // unit or pack
    customer_id: "",
    posted_number: "",
    date: new Date().toISOString().split("T")[0],
    due_date: "",
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
    cash_given: "",
    payment_method: "cash",
    account_id: null,
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
  const invoiceErrors = useInvoiceErrors(form.items);

  // State for customer wholesale prices (fetched when customer is selected)
const [customerWholesalePrices, setCustomerWholesalePrices] = useState({});
  // State to store product data for validation (pack_purchase_price)
  const [productDataCache, setProductDataCache] = useState({});
  // State to track which rows have invalid prices (below purchase price)
  const [invalidPriceRows, setInvalidPriceRows] = useState({});
  const [receiveTouched, setReceiveTouched] = useState(false);
  const [marginPct, setMarginPct] = useState("");
  const [customerSearchOpen, setCustomerSearchOpen] = useState(false);
  // Store original invoice quantities for edit mode
  const [originalInvoiceQuantities, setOriginalInvoiceQuantities] = useState({});
  const [drawerPrompt, setDrawerPrompt] = useState(null);
  const [customerCreditSummary, setCustomerCreditSummary] = useState(null);
  const [creditOverridePrompt, setCreditOverridePrompt] = useState(null);
  const [discountOverridePrompt, setDiscountOverridePrompt] = useState(null);
  const originalInvoiceMetaRef = useRef(null);
  const navigate = useNavigate();

  const handleStartBlank = () => {
    if (draftHook?.resetToNew) draftHook.resetToNew();
    const defaultCustId = getDefaultCustomerId();
    setReceiveTouched(false);
    setMarginPct("");
    setInvalidPriceRows({});
    invoiceErrors.clear();
    setForm(normalizeFormLoaded({
      invoice_type: "debit",
      sale_type: "wholesale",
      wholesale_type: "unit",
      customer_id: defaultCustId,
      posted_number: "",
      date: new Date().toISOString().split("T")[0],
      due_date: "",
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
      cash_given: "",
      payment_method: "cash",
      account_id: null,
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
    }));
    setTimeout(() => {
      productRefs.current[0]?.querySelector?.("input")?.focus?.();
    }, 60);
  };

  const draftHook = useInvoiceDraft({
    type: "sale_wholesale",
    form,
    setForm,
    enabled: !saleId,
    onStartBlank: () => handleStartBlank(),
    onDraftLoaded: async (loadedPayload, draft, responseData) => {
      if (Array.isArray(responseData?.products) && responseData.products.length > 0) {
        upsertProducts(responseData.products);
      }
      const loaded = normalizeFormLoaded(loadedPayload);
      if (!loaded.customer_id && loaded.invoice_type !== "credit") {
        loaded.customer_id = getDefaultCustomerId();
      }
      if (loaded.customer_id) {
        fetchCustomerWholesalePrices(loaded.customer_id);
      }
      await ensureProductsForItems(loaded?.items || []);
      await ensureBatchesForItems(loaded?.items || []);
      setForm(loaded);
      focusedOnce.current = true;

      // Close product search modal if open
      if (typeof window !== "undefined") {
        window.dispatchEvent(new CustomEvent("product-search:close"));
      }

      // Focus the last row's quantity field
      const lastIdx = Math.max(0, (loaded?.items?.length || 1) - 1);
      let retries = 0;
      const tryFocus = () => {
        const el = qtyRefs.current[lastIdx];
        if (el && typeof el.focus === "function") {
          el.focus();
          el.select?.();
          el.scrollIntoView?.({ block: "nearest" });
        } else if (retries < 10) {
          retries++;
          setTimeout(tryFocus, 50);
        }
      };
      setTimeout(tryFocus, 60);
    },
  });

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
      // For wholesale create mode, focus on customer select first (unless resuming a draft from URL)
      setTimeout(() => {
        const params = new URLSearchParams(window.location.search);
        const hasDraftInUrl = Boolean(params.get("draft_id"));
        if (hasDraftInUrl) {
          focusedOnce.current = true;
          return;
        }
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
  const { isPharmacy } = useSaleSystem();

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
  const buttonStyle = theme?.button_style || 'modern';
  
  const getButtonClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark),
    [buttonStyle, themeColors, isDark]
  );

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;

  const { formatCurrency } = useCurrency();
  const billTotal = Math.round(Number(form.total || 0));
  const cashGivenNum = form.cash_given !== "" && form.cash_given !== undefined ? Number(form.cash_given) : null;
  const changeToReturn = form.invoice_type === "credit"
    ? (cashGivenNum !== null && Number(form.total_receive || 0) < cashGivenNum ? cashGivenNum - Number(form.total_receive || 0) : 0)
    : (cashGivenNum !== null && cashGivenNum > billTotal ? cashGivenNum - billTotal : 0);
  const remainingDue = Math.max(0, billTotal - Math.round(Number(form.total_receive || 0)));
  const excessCredit = form.invoice_type === "credit" && Math.round(Number(form.total_receive || 0)) > billTotal
    ? Math.round(Number(form.total_receive || 0)) - billTotal
    : 0;

  const handleCashGivenChange = (raw) => {
    const val = sanitizeNumberInput(raw, true, false);
    const givenNum = Number(val || 0);
    const totalNum = Math.round(Number(form.total || 0));

    setForm((prev) => {
      const next = { ...prev, cash_given: val };
      if (prev.invoice_type !== "credit") {
        next.total_receive = String(totalNum);
      } else {
        if (val === "") {
          next.total_receive = "";
        } else {
          next.total_receive = String(givenNum);
        }
        setReceiveTouched(true);
      }
      return next;
    });
  };

  const paymentSelectStyles = useMemo(() => ({
    control: (base, state) => ({
      ...base,
      minHeight: "28px",
      height: "28px",
      borderRadius: 6,
      backgroundColor: isDark ? "#334155" : "#ffffff",
      borderColor: state.isFocused
        ? (themeColors.primary || "#3b82f6")
        : (isDark ? "#475569" : "#e2e8f0"),
      boxShadow: "none",
      fontSize: "11px",
      fontWeight: 600,
    }),
    valueContainer: (base) => ({ ...base, padding: "0 6px", height: "26px" }),
    indicatorsContainer: (base) => ({ ...base, height: "26px" }),
    dropdownIndicator: (base) => ({ ...base, padding: 1 }),
    input: (base) => ({ ...base, color: isDark ? "#f8fafc" : "#1e293b", margin: 0, padding: 0 }),
    singleValue: (base) => ({ ...base, color: isDark ? "#f8fafc" : "#1e293b", fontSize: "11px" }),
    menuPortal: (base) => ({ ...base, zIndex: 9999 }),
    menu: (base) => ({
      ...base,
      backgroundColor: isDark ? "#1e293b" : "#ffffff",
      border: isDark ? "1px solid #334155" : "1px solid #e2e8f0",
      boxShadow: "0 10px 25px -5px rgba(0, 0, 0, 0.2)",
      borderRadius: 8,
      zIndex: 9999,
    }),
    option: (base, state) => ({
      ...base,
      backgroundColor: state.isSelected
        ? (themeColors.primary || "#3b82f6")
        : state.isFocused
        ? (isDark ? "#334155" : "#f1f5f9")
        : "transparent",
      color: state.isSelected ? "#ffffff" : (isDark ? "#f8fafc" : "#1e293b"),
      fontSize: "11px",
      padding: "4px 8px",
      cursor: "pointer",
    }),
  }), [isDark, themeColors]);

/* -------- data -------- */
  const fetchCustomers = async () => {
    try {
      const res = await axios.get("/api/customers");
      let list = Array.isArray(res.data) ? res.data : [];
      list = list.slice().sort((a, b) => (a?.id ?? 0) - (b?.id ?? 0));
      setCustomers(list);
      customersRef.current = list;
      if (!saleId && list.length > 0) {
        setForm((prev) => {
          if (!prev.customer_id && prev.invoice_type !== "credit") {
            const defId = getDefaultCustomerId(list);
            return defId ? { ...prev, customer_id: defId } : prev;
          }
          return prev;
        });
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
    originalInvoiceMetaRef.current = {
      created_at: res.data.created_at,
      date: res.data.date,
      payment_method: res.data.payment_method,
      invoice_type: res.data.invoice_type,
      total_receive: res.data.total_receive,
      total: res.data.total,
    };
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
  const handleCustomerChange = (customerId, customerObj = null) => {
    if (customerObj?.id) {
      setCustomers((prev) => {
        const exists = prev.some((c) => String(c.id) === String(customerObj.id));
        return exists ? prev : [customerObj, ...prev];
      });
    }
    const cust = customerObj || (customers || []).find((c) => String(c.id) === String(customerId));
    const creditDays = Number(cust?.credit_days ?? 0);
    let autoDueDate = undefined;
    if (creditDays > 0 && form.date) {
      const d = new Date(form.date);
      if (!isNaN(d.getTime())) {
        d.setDate(d.getDate() + creditDays);
        autoDueDate = d.toISOString().split("T")[0];
      }
    }
    setForm((prev) => {
      const next = {
        ...prev,
        customer_id: customerId,
        ...(autoDueDate ? { due_date: autoDueDate } : {}),
      };
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

  useEffect(() => {
    if (form.invoice_type === "credit" && form.customer_id) {
      let isMounted = true;
      axios
        .get(`/api/customers/${form.customer_id}/credit-summary`)
        .then((res) => {
          if (isMounted) setCustomerCreditSummary(res.data);
        })
        .catch(() => {
          if (isMounted) setCustomerCreditSummary(null);
        });
      return () => {
        isMounted = false;
      };
    } else {
      setCustomerCreditSummary(null);
    }
  }, [form.invoice_type, form.customer_id]);

  useEffect(() => {
    if (form.invoice_type === "credit" && form.customer_id && !form.due_date && form.date) {
      const cust = (customers || []).find((c) => String(c.id) === String(form.customer_id));
      const creditDays = Number(cust?.credit_days ?? 0);
      if (creditDays > 0) {
        const d = new Date(form.date);
        if (!isNaN(d.getTime())) {
          d.setDate(d.getDate() + creditDays);
          const computedDue = d.toISOString().split("T")[0];
          setForm((prev) => ({ ...prev, due_date: computedDue }));
        }
      }
    }
  }, [form.invoice_type, form.customer_id, form.date, customers]);

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
    if (!receiveTouched && form.invoice_type === "credit") next.total_receive = "";
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
      if (!receiveTouched && prev.invoice_type === "credit") updated.total_receive = "";
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
        if (prev.invoice_type !== "credit") {
          afterFooter.total_receive = afterFooter.total ?? "";
        } else {
          afterFooter.total_receive = "";
        }
        return afterFooter;
      }

      return next;
    });
  };

  const removeRow = (i) => {
    if (form.items.length <= 1) return;
    const items = form.items.filter((_, idx) => idx !== i);
    let next = recalcFooter({ ...form, items }, "items");
    if (!receiveTouched && form.invoice_type !== "credit") next.total_receive = next.total ?? "";
    if (!receiveTouched && form.invoice_type === "credit") next.total_receive = "";
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
      if (!receiveTouched && prev.invoice_type === "credit") next.total_receive = "";
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
        invoiceErrors.showMessage(`Product already used in row ${dupIndex + 1}. Each product can be added only once.`);
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

    if (freshProductData || (selected && selected.id)) {
      upsertProducts([freshProductData || selected]);
    }

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
          product_name: freshProductData?.name ?? selected?.name ?? items[rowIndex]?.product_name ?? "",
          product: freshProductData ?? (selected?.id ? selected : null),
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
      if (!receiveTouched && prev.invoice_type === "credit") next.total_receive = "";
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
      if (!receiveTouched && prev.invoice_type === "credit") next.total_receive = "";
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
    invoiceErrors.clear();

    // Safety fallback: ensure customer_id is populated for debit sales
    let effectiveCustomerId = form.customer_id;
    if (!effectiveCustomerId && form.invoice_type !== "credit") {
      effectiveCustomerId = getDefaultCustomerId();
      if (effectiveCustomerId) {
        setForm((prev) => ({ ...prev, customer_id: effectiveCustomerId }));
      }
    }

    // Validate customer selection
    if (!effectiveCustomerId) {
      return invoiceErrors.showMessage(
        form.invoice_type === "credit"
          ? "Please select a customer for credit sale"
          : "Please select a customer for wholesale sale"
      );
    }

    // Check if any item has a narcotic product
    const hasNarcoticProduct = form.items.some(item => item.is_narcotic === true);

    if (isPharmacy && hasNarcoticProduct) {
      if (!form.doctor_name || form.doctor_name.trim() === "") {
        return invoiceErrors.showMessage("Doctor name is required for narcotic products");
      }
      if (!form.patient_name || form.patient_name.trim() === "") {
        return invoiceErrors.showMessage("Patient name is required for narcotic products");
      }
    }

    for (let i = 0; i < form.items.length; i++) {
      const it = form.items[i];
      if (!it.product_id) return invoiceErrors.showMessage(`Row ${i + 1}: select a product`);
      const list = batchesByProduct[String(it.product_id)];
      const hasBatches = Array.isArray(list) && list.length > 0;
      if (hasBatches && !it.batch_number) return invoiceErrors.showMessage(`Row ${i + 1}: select a batch`);
      if (it.quantity === "" || it.quantity == null || Number(it.quantity) <= 0)
        return invoiceErrors.showMessage(`Row ${i + 1}: enter quantity`);
      const available = Number(it.current_quantity || 0);
      if (Number(it.quantity || 0) > available) {
        return invoiceErrors.showMessage(`Row ${i + 1}: quantity exceeds available (${available})`);
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
        return invoiceErrors.showMessage(`Row ${i + 1}: Price cannot be less than ${priceLabel} (${minPrice.toFixed(2)})`);
      }
    }

    // unique product validation
    {
      const seen = new Set();
      for (let i = 0; i < form.items.length; i++) {
        const id = form.items[i].product_id;
        if (!id) continue;
        if (seen.has(String(id))) {
          invoiceErrors.showMessage(`Duplicate product in row ${i + 1}. Each product can be added only once.`);
          return;
        }
        seen.add(String(id));
      }
    }

    // total_receive validation
    {
      const totalNum = Math.round(Number(form.total || 0));
      const recvNum = Math.round(Number(form.total_receive || 0));
      if (recvNum < 0) return invoiceErrors.showMessage("Total Receive cannot be negative");
      if (form.invoice_type !== "credit" && recvNum > totalNum) {
        return invoiceErrors.showMessage("Total Receive cannot exceed Total");
      }
    }

    // Always set sale_type to wholesale for this form
    const payload = {
      ...form,
      customer_id: effectiveCustomerId,
      sale_type: "wholesale",
      invoice_type: form.invoice_type ?? "debit",
      due_date: form.due_date || undefined,
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
        // For new items (string IDs starting with 'row_'), exclude the id field
        // For existing items (numeric IDs), convert id to integer
        const isNewItem = String(it.id).startsWith('row_') || !it.id;
        const { id, ...itemWithoutId } = it;
        return {
          ...itemWithoutId,
          ...(isNewItem ? {} : { id: parseInt(it.id, 10) }),
          quantity: quantity,
          item_discount_percentage:
            it.item_discount_percentage === "-" || it.item_discount_percentage === "-."
              ? "-0"
              : it.item_discount_percentage,
        };
      }),
    };
    delete payload.cash_given;

    const executeSave = async (dataPayload, updateDrawer = null) => {
      try {
        if (saleId) {
          if (updateDrawer !== null) {
            dataPayload.update_drawer = updateDrawer;
          }
          const res = await axios.put(`/api/sale-invoices/${saleId}`, dataPayload);
          const id = res?.data?.id ?? saleId;
          window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
          window.dispatchEvent(new CustomEvent("accounts-changed"));
          try { localStorage.setItem("cash_drawer_sync", String(Date.now())); } catch {}
          try { localStorage.setItem("accounts_sync", String(Date.now())); } catch {}
          toast.success("Sale invoice updated");
          navigate(`/sale-invoices/${id}`);
        } else {
          if (draftHook.activeDraftId) {
            dataPayload.draft_id = draftHook.activeDraftId;
          }
          const res = await axios.post("/api/sale-invoices", dataPayload);
          const id = res?.data?.id;
          window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
          window.dispatchEvent(new CustomEvent("accounts-changed"));
          try { localStorage.setItem("cash_drawer_sync", String(Date.now())); } catch {}
          try { localStorage.setItem("accounts_sync", String(Date.now())); } catch {}
          draftHook.resetToNew();
          toast.success("Sale invoice created");
          if (id) navigate(`/sale-invoices/${id}`);
          else invoiceErrors.showMessage("Missing invoice ID from server response.");
        }
      } catch (err) {
        if (err?.response?.status === 422 && err?.response?.data?.requires_override) {
          setCreditOverridePrompt({
            isOpen: true,
            message: err.response.data.message,
            excessAmount: err.response.data.excess_amount,
            creditLimit: err.response.data.credit_limit,
            currentBalance: err.response.data.current_balance,
            projectedBalance: err.response.data.projected_balance,
            isHold: err.response.data.credit_status === "on_hold",
            execute: () => executeSave({ ...dataPayload, credit_override: true }, updateDrawer),
          });
          return;
        }
        if (err?.response?.status === 422 && err?.response?.data?.requires_discount_override) {
          setDiscountOverridePrompt({
            isOpen: true,
            message: err.response.data.message,
            productName: err.response.data.product_name,
            maxDiscount: err.response.data.max_discount,
            requestedDiscount: err.response.data.requested_discount,
            execute: () => executeSave({ ...dataPayload, discount_override: true }, updateDrawer),
          });
          return;
        }
        invoiceErrors.showError(err);
      }
    };

    if (saleId) {
      // Calculate cash difference
      const orig = originalInvoiceMetaRef.current || {};
      const oldPayMethod = orig.payment_method || "cash";
      const oldInvType = orig.invoice_type || "debit";
      let oldReceived = Number(orig.total_receive ?? 0);
      if (oldInvType === "debit" && oldReceived <= 0) {
        oldReceived = Number(orig.total ?? 0);
      }
      const oldCash = oldPayMethod === "cash" ? oldReceived : 0;

      const newPayMethod = payload.payment_method || "cash";
      const newInvType = payload.invoice_type || "debit";
      let newReceived = Number(payload.total_receive ?? 0);
      if (newInvType === "debit" && newReceived <= 0) {
        newReceived = Number(payload.total ?? 0);
      }
      const newCash = newPayMethod === "cash" ? newReceived : 0;
      const cashDelta = Math.round((newCash - oldCash) * 100) / 100;

      if (Math.abs(cashDelta) > 0.001) {
        try {
          const drawerRes = await axios.get("/api/cash-drawer/current");
          if (drawerRes.data?.is_open && drawerRes.data?.session) {
            const invoiceTime = new Date(orig.created_at || orig.date).getTime();
            const shiftTime = new Date(drawerRes.data.session.opened_at).getTime();

            if (invoiceTime < shiftTime) {
              setDrawerPrompt({
                isOpen: true,
                oldCash,
                newCash,
                cashDelta,
                invoiceCreatedAt: orig.created_at || orig.date,
                shiftOpenedAt: drawerRes.data.session.opened_at,
                payload,
                execute: (upd) => executeSave(payload, upd),
              });
              return;
            }
          }
        } catch {}
      }

      await executeSave(payload, true);
    } else {
      await executeSave(payload);
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
  const selectedCustomer = (customers || []).find(
    (c) => String(c.id) === String(form.customer_id)
  );

  return (
    <>
      <form
        className={`sale-entry sale-entry-form flex flex-col flex-1 h-auto lg:h-[calc(100dvh-130px)] min-h-0 overflow-y-auto lg:overflow-hidden ${isDark ? "bg-slate-900" : "bg-white"}`}
        autoComplete="off" autoCapitalize="off" autoCorrect="off" spellCheck={false}
        onSubmit={(e) => {
          e.preventDefault();
          handleSubmit(e);
        }}
      >
      <InvoiceErrorSummary errors={invoiceErrors} />
      {/* Header */}
      <div className={`sale-entry-header shrink-0 sticky top-0 z-20 border-b ${isDark ? "border-slate-700" : "border-gray-200"}`}>
        {/* Branded Banner */}
        <div
          className="sale-entry-banner px-4 py-2.5 flex items-center justify-between gap-3 flex-wrap"

        >
          <div className="flex items-center gap-3">
            <div
              className="w-9 h-9 rounded-xl flex items-center justify-center shadow-inner"
              style={{ backgroundColor: "rgba(255,255,255,0.18)", backdropFilter: "blur(4px)" }}
            >
              {/* Wholesale / box icon */}
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
                <polyline points="3.27 6.96 12 12.01 20.73 6.96" />
                <line x1="12" y1="22.08" x2="12" y2="12" />
              </svg>
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-extrabold tracking-wide text-white leading-none">Sale Invoice</h2>
                <span
                  className="px-2 py-0.5 rounded-md text-[9px] font-bold tracking-widest text-white"
                  style={{ backgroundColor: "rgba(255,255,255,0.22)", backdropFilter: "blur(4px)" }}
                >
                  WHOLESALE
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

          <div className="flex items-center gap-2.5 flex-wrap">
            {!saleId && (
              <InvoiceDraftIndicator
                saveStatus={draftHook.saveStatus}
                lastSavedAt={draftHook.lastSavedAt}
                saveError={draftHook.saveError}
                draftsCount={draftHook.draftsList.length}
                activeDraftTitle={draftHook.activeDraft?.title}
                onOpenDrafts={() => draftHook.setIsDraftsModalOpen(true)}
                onSaveDraft={() => draftHook.holdAndNew()}
              />
            )}
            {/* Wholesale Type Segmented Control */}
            <div
              className="sale-type-toggle flex items-center"
            >
              {["unit", "pack"].map((type) => {
                const active = form.wholesale_type === type;
                return (
                  <button
                    key={type}
                    type="button"
                    onClick={() => handleWholesaleTypeChange(type)}
                    className={`sale-type-option text-xs font-semibold ${active ? "is-active theme-btn-secondary shadow-sm" : "opacity-75 hover:opacity-100 hover:bg-black/5 dark:hover:bg-white/5"}`}
                    aria-pressed={active}
                  >
                    {type === "unit" ? <HashtagIcon aria-hidden="true" /> : <CubeIcon aria-hidden="true" />}
                    <span>{type === "unit" ? "Unit" : "Pack"}</span>
                  </button>
                );
              })}
            </div>

            {/* Invoice Type Segmented Control */}
            <div
              className="sale-type-toggle flex items-center"
            >
              {["debit", "credit"].map((type) => {
                const active = form.invoice_type === type;
                return (
                  <button
                    key={type}
                    type="button"
                    onClick={() => handleInvoiceTypeChange(type)}
                    className={`sale-type-option text-xs font-semibold ${active ? "is-active theme-btn-primary shadow-sm" : "opacity-75 hover:opacity-100 hover:bg-black/5 dark:hover:bg-white/5"}`}
                    aria-pressed={active}
                  >
                    {active && <CheckIcon aria-hidden="true" />}
                    <span>{type === "debit" ? "Debit" : "Credit"}</span>
                  </button>
                );
              })}
            </div>

            <button
              type="button"
              onClick={handleSubmit}
              className={`px-4 py-2 text-[11px] font-bold transition-all duration-200 ${btnPrimary.className}`}
              style={btnPrimary.style}
            >
              {saleId ? "Edit (Alt+S)" : "Create (Alt+S)"}
            </button>
          </div>
        </div>
      </div>

      {/* Main workspace: Left (Compact Top Fields + Items) and Right (Squeezed Summary) */}
      <div className={`sale-entry-workspace flex-1 min-h-0 flex flex-col lg:grid lg:grid-cols-[minmax(0,1fr)_230px] gap-2 p-2 overflow-visible lg:overflow-hidden ${isDark ? "bg-slate-900" : "bg-slate-50/50"}`}>
        {/* LEFT COLUMN: Top Banner Fields + Items Table */}
        <div className="flex flex-col min-h-0 flex-1 gap-1.5 overflow-hidden">
          {/* Top Banner Details Card (Compact) */}
          <div className={`sale-entry-details px-2.5 py-1.5 rounded-xl border shrink-0 ${isDark ? "bg-slate-800/80 border-slate-700" : "bg-white border-gray-200 shadow-2xs"}`}>
            <div className="grid grid-cols-2 sm:grid-cols-6 lg:grid-cols-12 gap-1.5 items-end">
              <div className="col-span-1 sm:col-span-2 lg:col-span-2">
                <label className="block text-[9.5px] font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400 mb-0.5">Posted #</label>
                <input
                  type="text"
                  readOnly
                  value={form.posted_number || ""}
                  placeholder={saleId ? "" : "Auto on Save"}
                  autoComplete="off"
                  className={`w-full h-7 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-xs font-mono ${
                    isDark ? "bg-slate-700/60 text-slate-200 placeholder-slate-500" : "bg-gray-100 text-gray-800"
                  }`}
                />
              </div>

              <div className="col-span-1 sm:col-span-2 lg:col-span-2">
                <label className="block text-[9.5px] font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400 mb-0.5">Date</label>
                <input
                  type="date"
                  name="date"
                  value={form.date ?? ""}
                  onChange={handleHeaderChange}
                  autoComplete="off"
                  className="w-full h-7 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-xs bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
                />
              </div>

              <div className={`col-span-2 sm:col-span-2 ${isPharmacy ? "lg:col-span-4" : "lg:col-span-8"}`}>
                <label className="block text-[9.5px] font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400 mb-0.5">Customer *</label>
                <button
                  ref={customerSelectRef}
                  type="button"
                  onClick={() => setCustomerSearchOpen(true)}
                  className={`w-full h-7 px-2 rounded-md border text-left text-xs flex items-center justify-between transition-all ${
                    selectedCustomer
                      ? "border-blue-500 bg-blue-50/80 dark:bg-blue-900/30 text-blue-700 dark:text-blue-200"
                      : "border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-gray-700 dark:text-gray-300 hover:border-blue-400"
                  }`}
                  title={selectedCustomer?.name || (form.customer_id ? `Customer #${form.customer_id}` : "Select customer...")}
                >
                  <span className="truncate font-medium flex items-center gap-1.5">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="shrink-0 text-blue-500">
                      <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
                      <circle cx="12" cy="7" r="4" />
                    </svg>
                    <span className="truncate">{selectedCustomer?.name || (form.customer_id ? `Customer #${form.customer_id}` : "Select customer...")}</span>
                  </span>
                  <span className="text-[10px] text-gray-400 dark:text-slate-400 shrink-0 font-normal">Search</span>
                </button>
              </div>

              {isPharmacy && (
                <div className="col-span-1 sm:col-span-3 lg:col-span-2">
                  <label className="block text-[9.5px] font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400 mb-0.5">Doctor</label>
                  <input
                    type="text"
                    name="doctor_name"
                    value={form.doctor_name ?? ""}
                    placeholder="Doctor"
                    onChange={handleHeaderChange}
                    autoComplete="off"
                    className="w-full h-7 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-xs bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
                  />
                </div>
              )}

              {isPharmacy && (
                <div className="col-span-1 sm:col-span-3 lg:col-span-2">
                  <label className="block text-[9.5px] font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400 mb-0.5">Patient</label>
                  <input
                    type="text"
                    name="patient_name"
                    value={form.patient_name ?? ""}
                    placeholder="Patient"
                    onChange={handleHeaderChange}
                    autoComplete="off"
                    className="w-full h-7 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-xs bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
                  />
                </div>
              )}

              {form.invoice_type === "credit" && (
                <div className="col-span-full py-1.5 px-3 rounded-md bg-amber-50/90 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-800 flex flex-wrap items-center justify-between gap-2 text-xs">
                  <div className="flex items-center gap-3 flex-wrap">
                    <span className="font-semibold text-amber-900 dark:text-amber-200 flex items-center gap-1.5">
                      <span>Credit Status:</span>
                      <span className={`px-1.5 py-0.5 rounded text-[10px] font-bold uppercase ${
                        selectedCustomer?.credit_status === 'blocked'
                          ? 'bg-red-100 text-red-700 dark:bg-red-900/60 dark:text-red-300'
                          : selectedCustomer?.credit_status === 'on_hold'
                          ? 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/60 dark:text-yellow-300'
                          : 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/60 dark:text-emerald-300'
                      }`}>
                        {selectedCustomer?.credit_status || 'active'}
                      </span>
                    </span>
                    <span className="text-gray-600 dark:text-gray-300">
                      Terms: <strong>{selectedCustomer?.credit_days ? `${selectedCustomer.credit_days} days` : 'Immediate'}</strong>
                    </span>
                    <span className="text-gray-600 dark:text-gray-300">
                      Credit Limit: <strong>{Number(selectedCustomer?.credit_limit) > 0 ? `Rs. ${Number(selectedCustomer.credit_limit).toLocaleString()}` : 'Unlimited'}</strong>
                    </span>
                    {customerCreditSummary && (
                      <>
                        <span className="text-gray-600 dark:text-gray-300">
                          Ledger Balance: <strong>Rs. {Number(customerCreditSummary.current_balance || 0).toLocaleString()}</strong>
                        </span>
                        {Number(selectedCustomer?.credit_limit) > 0 && (
                          <span className={`font-semibold ${customerCreditSummary.available_credit <= 0 ? 'text-red-600 dark:text-red-400' : 'text-emerald-600 dark:text-emerald-400'}`}>
                            Available Credit: Rs. {Number(customerCreditSummary.available_credit || 0).toLocaleString()}
                          </span>
                        )}
                      </>
                    )}
                  </div>
                  <div className="flex items-center gap-2">
                    <label className="text-[10px] font-bold text-gray-600 dark:text-gray-300 uppercase tracking-wider">Due Date:</label>
                    <input
                      type="date"
                      name="due_date"
                      value={form.due_date || ""}
                      onChange={handleHeaderChange}
                      className="h-6 px-1.5 text-xs rounded border border-amber-300 dark:border-amber-700 bg-white dark:bg-slate-800 text-gray-900 dark:text-gray-100 font-mono"
                    />
                  </div>
                </div>
              )}

              {/* Row 2: Remarks + Items */}
              <div className="col-span-2 sm:col-span-5 lg:col-span-10">
                <label className="block text-[9.5px] font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400 mb-0.5">Remarks</label>
                <input
                  type="text"
                  name="remarks"
                  value={form.remarks ?? ""}
                  placeholder="Invoice remarks / customer notes..."
                  onChange={handleHeaderChange}
                  autoComplete="off"
                  className="w-full h-7 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-xs bg-white dark:bg-slate-700 text-gray-900 dark:text-slate-200"
                />
              </div>

              <div className="col-span-1 sm:col-span-1 lg:col-span-2">
                <label className="block text-[9.5px] font-semibold uppercase tracking-wider text-gray-500 dark:text-gray-400 mb-0.5 text-center">Items</label>
                <div
                  className={`w-full h-7 border border-gray-200 dark:border-slate-600 rounded-md px-2 text-xs font-bold flex items-center justify-center ${
                    isDark ? "bg-slate-700/60 text-slate-200" : "bg-gray-100 text-gray-800"
                  }`}
                >
                  {form.items.length} {form.items.length === 1 ? "item" : "items"}
                </div>
              </div>
            </div>
          </div>

          {/* Items Table Card */}
          <div className="flex flex-col min-h-0 flex-1">
            <div className={`text-[11px] font-semibold mb-0.5 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Items</div>
            <div ref={itemsScrollRef} className={`sale-entry-items flex-1 overflow-auto border-2 rounded-sm relative ${isDark ? "border-slate-700 bg-slate-800" : "border-gray-200 bg-white"}`}>
              <table className="w-full text-[11px] table-fixed border-collapse" autoComplete="off">
              <thead className={`sticky top-0 z-20 ${isDark ? "bg-slate-800/90 backdrop-blur-xs border-slate-700" : "bg-white/80 backdrop-blur-xs border-gray-200/70"} border-b`}>
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
                  <tr key={it.id} id={invoiceErrors.rowId(i)} aria-invalid={invoiceErrors.rowMessages(i).length > 0 || undefined} className={`${invoiceErrors.rowMessages(i).length ? "invoice-row-invalid" : ""} border-b ${isDark ? "border-slate-700 hover:bg-slate-700/50" : "border-gray-100 hover:bg-gray-50"}`}>
                    <td className="text-center">
                      <button
                        type="button"
                        tabIndex={-1}
                        onClick={() => removeRow(i)}
                        aria-label="Remove item" title="Remove item"
                        className="sale-item-action sale-item-remove"
                      >
                        <TrashIcon aria-hidden="true" />
                      </button>
                    </td>
                    <td className={`text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>{i + 1}</td>

                    <td>
                      <InvoiceRowError messages={invoiceErrors.rowMessages(i)} />
                      <div ref={(el) => (productRefs.current[i] = el)}>
                        <ProductSearchInput
                          value={products.find((p) => eqId(p.id, it.product_id)) || it.product || (it.product_name ? { id: it.product_id, name: it.product_name } : it.product_id)}
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
                        name="quantity"
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
                        aria-label="Add item" title="Add item"
                        className="sale-item-action sale-item-add"
                      >
                        <PlusIcon aria-hidden="true" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* RIGHT: Squeezed Summary - compact width giving max room to items */}
      <div className="min-h-0 w-full shrink-0 flex flex-col h-auto lg:h-full lg:w-[230px]">
        <div className={`sale-entry-summary h-full min-h-0 flex flex-col rounded-xl overflow-hidden border ${isDark ? "bg-slate-800 border-slate-700" : "bg-white border-gray-200 shadow-sm"}`}>
            {/* Panel Header */}
            <div
              className="sale-entry-summary-heading px-2.5 py-1.5 flex items-center justify-between shrink-0"
              style={{ background: `linear-gradient(135deg, ${themeColors.primary}, ${themeColors.primaryHover})` }}
            >
              <span className="text-white font-bold text-xs tracking-wide">Summary</span>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="18" y1="20" x2="18" y2="10" />
                <line x1="12" y1="20" x2="12" y2="4" />
                <line x1="6" y1="20" x2="6" y2="14" />
              </svg>
            </div>

            {/* Scrollable content on small heights */}
            <div className="sale-summary-body flex-1 flex flex-col min-h-0 px-2.5 py-1.5 text-xs overflow-y-auto">
              <div className="space-y-1">
                {/* Margin % */}
                <div className="flex items-center justify-between gap-1.5">
                  <label className={`font-semibold text-xs shrink-0 ${isDark ? "text-slate-400" : "text-gray-500"}`}>Margin %</label>
                  <input
                    type="text"
                    name="margin_percentage"
                    readOnly
                    value={marginPct}
                    onChange={(e) => setMarginPct(sanitizeNumberInput(e.target.value, true))}
                    autoComplete="off"
                    className={`w-24 h-6.5 border rounded-md px-2 text-center font-extrabold text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-rose-400 placeholder-slate-500" 
                        : "border-gray-200 bg-gray-50 text-red-600"
                    }`}
                  />
                </div>

                {/* Tax % */}
                <div className="flex items-center justify-between gap-1.5">
                  <label className={`font-semibold text-xs shrink-0 ${isDark ? "text-slate-400" : "text-gray-500"}`}>Tax %</label>
                  <input
                    type="text"
                    name="tax_percentage"
                    value={form.tax_percentage ?? ""}
                    onChange={handleHeaderChange}
                    autoComplete="off"
                    className={`w-24 h-6.5 border rounded-md px-2 text-center text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    }`}
                  />
                </div>

                {/* Tax Amt */}
                <div className={`flex items-center justify-between gap-1.5 border-t border-dashed pt-0.5 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`font-semibold text-xs shrink-0 ${isDark ? "text-slate-400" : "text-gray-500"}`}>Tax Amt</label>
                  <input
                    type="text"
                    name="tax_amount"
                    value={form.tax_amount ?? ""}
                    onChange={handleHeaderChange}
                    autoComplete="off"
                    className={`w-24 h-6.5 border rounded-md px-2 text-right font-semibold text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    }`}
                  />
                </div>

                {/* Disc % */}
                <div className="flex items-center justify-between gap-1.5">
                  <label className={`font-semibold text-xs shrink-0 ${isDark ? "text-slate-400" : "text-gray-500"}`}>Disc %</label>
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
                    className={`w-24 h-6.5 border rounded-md px-2 text-center text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    }`}
                  />
                </div>

                {/* Disc Amt */}
                <div className={`flex items-center justify-between gap-1.5 border-t border-dashed pt-0.5 ${isDark ? "border-slate-700" : "border-gray-200"}`}>
                  <label className={`font-semibold text-xs shrink-0 ${isDark ? "text-slate-400" : "text-gray-500"}`}>Disc Amt</label>
                  <input
                    type="text"
                    name="discount_amount"
                    value={form.discount_amount ?? ""}
                    onChange={handleHeaderChange}
                    autoComplete="off"
                    className={`w-24 h-6.5 border rounded-md px-2 text-right font-semibold text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    }`}
                  />
                </div>
              </div>

              {/* Totals Banner */}
              <div
                className="my-1 px-2.5 py-1.5 rounded-lg shrink-0"
                style={{ background: `linear-gradient(135deg, ${themeColors.primary}18, ${themeColors.primaryHover}26)` }}
              >
                <div className="flex items-center justify-between">
                  <span className={`text-[9.5px] font-medium uppercase tracking-wide ${isDark ? "text-slate-400" : "text-gray-500"}`}>Gross</span>
                  <span className={`text-[11px] font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>{form.gross_amount ?? 0}</span>
                </div>
                <div className="flex items-center justify-between border-t border-dashed pt-0.5 mt-0.5" style={{ borderColor: `${themeColors.primary}33` }}>
                  <span className={`text-xs font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>Total</span>
                  <span className="text-lg font-black leading-none" style={{ color: themeColors.primary }}>
                    {form.total ?? 0}
                  </span>
                </div>
              </div>

              {/* Payment Method & Account Live Balance */}
              <div className="space-y-1">
                <PaymentAccountSelector
                  paymentMethod={form.payment_method || "cash"}
                  accountId={form.account_id || null}
                  onChange={({ payment_method, account_id }) => {
                    setForm((prev) => ({
                      ...prev,
                      payment_method,
                      account_id,
                    }));
                  }}
                  layout="compact"
                  showBalance={true}
                />

                {/* Cash Tendered / Given */}
                <div className="pt-1 border-t border-dashed" style={{ borderColor: isDark ? "#334155" : "#e2e8f0" }}>
                  <div className="flex items-center justify-between gap-1.5">
                    <label className={`font-bold text-xs shrink-0 ${isDark ? "text-slate-200" : "text-gray-700"}`}>
                      Cash Given
                    </label>
                    <input
                      type="text"
                      inputMode="numeric"
                      placeholder={form.invoice_type === "debit" ? String(billTotal) : "0"}
                      value={form.cash_given ?? ""}
                      onChange={(e) => handleCashGivenChange(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") {
                          e.preventDefault();
                          handleSubmit(e);
                        }
                      }}
                      autoComplete="off"
                      className={`w-24 h-6.5 border-2 rounded-md px-2 text-right font-extrabold text-xs transition-all ${
                        isDark 
                          ? "border-blue-500/60 bg-slate-700 text-blue-300 focus:border-blue-400" 
                          : "border-blue-400 bg-blue-50/50 text-blue-900 focus:border-blue-600 focus:bg-white"
                      }`}
                    />
                  </div>

                  {/* Quick suggestion chips */}
                  <div className="flex items-center gap-1 mt-1 justify-end flex-wrap">
                    <button
                      type="button"
                      onClick={() => handleCashGivenChange(String(billTotal))}
                      className="px-1.5 py-0.5 rounded text-[9.5px] font-semibold bg-gray-100 hover:bg-gray-200 dark:bg-slate-700 dark:hover:bg-slate-600 text-gray-700 dark:text-gray-200 transition-colors"
                      title="Exact bill amount"
                    >
                      Exact
                    </button>
                    {[500, 1000, 5000].map((note) => (
                      <button
                        key={note}
                        type="button"
                        onClick={() => handleCashGivenChange(String(note))}
                        className="px-1.5 py-0.5 rounded text-[9.5px] font-semibold bg-gray-100 hover:bg-gray-200 dark:bg-slate-700 dark:hover:bg-slate-600 text-gray-700 dark:text-gray-200 transition-colors"
                      >
                        {note}
                      </button>
                    ))}
                    {form.cash_given !== "" && form.cash_given !== undefined && (
                      <button
                        type="button"
                        onClick={() => handleCashGivenChange("")}
                        className="px-1 py-0.5 rounded text-[9.5px] text-red-500 hover:bg-red-50 dark:hover:bg-red-950/30 transition-colors font-bold"
                        title="Clear cash given"
                      >
                        ✕
                      </button>
                    )}
                  </div>
                </div>

                {/* Return to Customer (Change to hand over) */}
                {cashGivenNum !== null && cashGivenNum > 0 && (
                  <div
                    className={`p-1.5 rounded-lg border flex items-center justify-between transition-all ${
                      changeToReturn > 0
                        ? "bg-emerald-500/15 border-emerald-500/40 text-emerald-700 dark:text-emerald-300 shadow-xs"
                        : (isDark ? "bg-slate-700/40 border-slate-600 text-slate-300" : "bg-gray-50 border-gray-200 text-gray-700")
                    }`}
                  >
                    <div>
                      <div className="text-[9.5px] font-extrabold uppercase tracking-wider">
                        Return
                      </div>
                      <div className="text-[8.5px] opacity-80">
                        {changeToReturn > 0 ? "Change to hand over" : "Exact amount paid"}
                      </div>
                    </div>
                    <div className="text-base font-black tracking-tight">
                      {formatCurrency ? formatCurrency(changeToReturn) : changeToReturn}
                    </div>
                  </div>
                )}

                <div className="flex items-center justify-between pt-0.5">
                  <label className={`font-semibold text-xs shrink-0 ${isDark ? "text-slate-400" : "text-gray-500"}`}>Received</label>
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
                    className={`w-24 h-6.5 border rounded-md px-2 text-right font-semibold text-xs ${
                      isDark 
                        ? "border-slate-600 bg-slate-700 text-slate-200" 
                        : "border-gray-200 text-gray-800"
                    } ${form.invoice_type === "debit" ? (isDark ? "cursor-not-allowed bg-slate-800" : "bg-gray-100 cursor-not-allowed") : ""}`}
                  />
                </div>

                <div className="flex items-center justify-between">
                  <label className={`font-semibold text-xs shrink-0 ${isDark ? "text-slate-400" : "text-gray-500"}`}>Remaining Due</label>
                  <span
                    className={`text-base font-extrabold ${
                      remainingDue > 0
                        ? "text-amber-500"
                        : (isDark ? "text-emerald-400" : "text-emerald-600")
                    }`}
                  >
                    {remainingDue}
                  </span>
                </div>

                {form.invoice_type === "credit" && excessCredit > 0 && (
                  <div className="flex items-center justify-between pt-0.5">
                    <label className={`font-semibold text-xs shrink-0 ${isDark ? "text-emerald-400" : "text-emerald-600"}`}>Ledger Credit</label>
                    <span className={`text-sm font-extrabold ${isDark ? "text-emerald-400" : "text-emerald-600"}`}>
                      +{excessCredit}
                    </span>
                  </div>
                )}
              </div>
            </div>

            <div className="px-2.5 py-1.5 shrink-0 border-t" style={{ borderColor: isDark ? "rgba(71,85,105,0.5)" : "rgba(229,231,235,0.8)" }}>
              <button
                type="button"
                onClick={handleSubmit}
                className={`w-full h-8 text-xs font-bold transition-all duration-200 ${btnPrimary.className}`}
                style={btnPrimary.style}
              >
                {saleId ? "Edit Sale" : "Create Sale"}
              </button>
            </div>
          </div>
        </div>
      </div>

      </form>

      {/* Customer Search Modal */}
      <CustomerSearch
        isOpen={customerSearchOpen}
        onClose={() => setCustomerSearchOpen(false)}
        onSelect={(customer) => {
          handleCustomerChange(customer?.id || "", customer);
          setCustomerSearchOpen(false);
        }}
        onCreate={(customer) => {
          handleCustomerChange(customer?.id || "", customer);
          setCustomerSearchOpen(false);
        }}
      />

      {/* Held Drafts Modal */}
      <InvoiceDraftsModal
        isOpen={draftHook.isDraftsModalOpen}
        onClose={() => draftHook.setIsDraftsModalOpen(false)}
        drafts={draftHook.draftsList}
        activeDraftId={draftHook.activeDraftId}
        onResumeDraft={(id) => draftHook.loadDraft(id)}
        onDiscardDraft={(id) => draftHook.discardDraft(id)}
        onStartBlank={handleStartBlank}
        type="sale_wholesale"
      />

      {/* Pre-Shift Edit Cash Drawer Prompt */}
      <PreShiftDrawerPromptModal
        isOpen={!!drawerPrompt?.isOpen}
        onClose={() => setDrawerPrompt(null)}
        onConfirm={(updateDrawer) => {
          const fn = drawerPrompt?.execute;
          setDrawerPrompt(null);
          if (fn) fn(updateDrawer);
        }}
        oldCash={drawerPrompt?.oldCash}
        newCash={drawerPrompt?.newCash}
        cashDelta={drawerPrompt?.cashDelta}
        invoiceCreatedAt={drawerPrompt?.invoiceCreatedAt}
        shiftOpenedAt={drawerPrompt?.shiftOpenedAt}
      />

      {/* Credit Limit / Status Approval Override Modal */}
      <CreditOverrideModal
        isOpen={!!creditOverridePrompt?.isOpen}
        onClose={() => setCreditOverridePrompt(null)}
        onConfirm={() => {
          const fn = creditOverridePrompt?.execute;
          setCreditOverridePrompt(null);
          if (fn) fn();
        }}
        message={creditOverridePrompt?.message}
        creditLimit={creditOverridePrompt?.creditLimit}
        currentBalance={creditOverridePrompt?.currentBalance}
        excessAmount={creditOverridePrompt?.excessAmount}
        isHold={creditOverridePrompt?.isHold}
      />

      {/* Discount Limit Approval Override Modal */}
      <DiscountOverrideModal
        isOpen={!!discountOverridePrompt?.isOpen}
        onClose={() => setDiscountOverridePrompt(null)}
        onConfirm={() => {
          const fn = discountOverridePrompt?.execute;
          setDiscountOverridePrompt(null);
          if (fn) fn();
        }}
        message={discountOverridePrompt?.message}
        productName={discountOverridePrompt?.productName}
        maxDiscount={discountOverridePrompt?.maxDiscount}
        requestedDiscount={discountOverridePrompt?.requestedDiscount}
      />
    </>
  );
}

