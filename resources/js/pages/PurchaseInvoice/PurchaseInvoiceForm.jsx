// /src/pages/purchases/PurchaseInvoiceForm.jsx
import { useState, useEffect, useRef, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import ProductSearchInput from "../../components/ProductSearchInput.jsx";
import { recalcItem, recalcFooter } from "../../Formula/PurchaseInvoice.js";
import { useTheme } from "@/context/ThemeContext";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import ProductFormModal from "../../components/ProductFormModal.jsx";
import SupplierSearch from "../../components/SupplierSearch.jsx";
import { BuildingStorefrontIcon } from "@heroicons/react/24/solid";

// Helper to determine text color based on background brightness
// Returns dark text for light backgrounds, light text for dark backgrounds
const getContrastText = (hexColor) => {
  // Remove hash if present
  hexColor = hexColor.replace('#', '');
  
  // Parse RGB values
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  
  // Calculate relative luminance (per WCAG formula)
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  
  // Return dark text for light backgrounds, light text for dark backgrounds
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

// Helper to get button text color with fallback
const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  // Use hover color for text color calculation as it's slightly darker
  return getContrastText(primaryHoverColor || primaryColor);
};

// Centralized Axios error → toast mapper
function showAxiosError(err) {
  const resp = err?.response;
  if (!resp) {
    toast.error("Network error. Please check your connection.");
    return;
  }
  const { status, data } = resp;
  // Laravel validation
  if (status === 422) {
    const errs = data?.errors || {};
    const msgs = Object.values(errs).flat();
    if (msgs.length) {
      msgs.slice(0, 6).forEach((m) => toast.error(String(m)));
      return;
    }
  }
  // Conflict/duplicate
  if (status === 409) {
    toast.error(data?.message || "Conflict while saving. Please review and try again.");
    return;
  }
  // Fallback
  toast.error(data?.message || "Unable to save invoice");
}

export default function PurchaseInvoiceForm({ invoiceId, onSuccess, onSubmit }) {
  const [form, setForm] = useState({
    invoice_type: "debit", // "debit" = pay now, "credit" = pay later
    supplier_id: "",
    posted_number: "", // (auto on save, stays empty until saved)
    posted_date: new Date().toISOString().split("T")[0],
    remarks: "",
    invoice_number: "",
    invoice_amount: "",
    tax_percentage: "",
    tax_amount: "",
    discount_percentage: "",
    discount_amount: "",
    total_amount: "",
    total_paid: "",
    items: [
      {
        product_id: "",
        batch: "",
        expiry: "",
        pack_quantity: "",
        pack_size: "",
        unit_quantity: "",
        pack_purchase_price: "",
        unit_purchase_price: "",
        pack_sale_price: "",
        unit_sale_price: "",
        whole_sale_pack_price: "",
        whole_sale_unit_price: "",
        whole_sale_margin: "",
        pack_bonus: "",
        unit_bonus: "",
        item_discount_percentage: "",
        margin: "",
        sub_total: "",
        avg_price: "",
        quantity: "",
      },
    ],
  });

  // Track if user has manually edited total_paid
  const [paidTouched, setPaidTouched] = useState(false);

  // Product modal state
  const [showProductModal, setShowProductModal] = useState(false);

  // only allow numbers (and optionally decimals)
  // allow decimals for price/percentage fields + Pack.Q and PBonus
  const sanitizeNumberInput = (value, allowDecimal = false) => {
    if (value === "") return ""; // allow empty input

    if (allowDecimal) {
      // valid: numbers with at most one decimal point
      if (/^\d*\.?\d*$/.test(value)) {
        return value;
      }
      return value.slice(0, -1); // strip invalid char
    }

    // integer-only fields
    return value.replace(/\D/g, "");
  };
  const to2 = (n) => Number(parseFloat(n || 0).toFixed(2));

  const [suppliers, setSuppliers] = useState([]);
  const [products, setProducts] = useState([]);
  const [currentField, setCurrentField] = useState("supplier");
  const [currentRowIndex, setCurrentRowIndex] = useState(0);

  // Supplier search modal state
  const [supplierSearchOpen, setSupplierSearchOpen] = useState(false);
  const [selectedSupplier, setSelectedSupplier] = useState(null);

  // Refs for navigation
  const supplierRef = useRef(null);
  const invoiceNumberRef = useRef(null);
  const invoiceAmountRef = useRef(null);
  const taxPercentageRef = useRef(null);
  const discountPercentageRef = useRef(null);
  const saveButtonRef = useRef(null);

  // Scroll container for the items section
  const itemsScrollRef = useRef(null);

  // productSearchRefs will hold container DOM nodes (wrapping ProductSearchInput)
  const productSearchRefs = useRef([]);
  const batchRefs = useRef([]);
  const packQuantityRefs = useRef([]);
  const packPurchasePriceRefs = useRef([]);
  const itemDiscountRefs = useRef([]);
  const packBonusRefs = useRef([]);
  const packSalePriceRefs = useRef([]);
  const wholeSalePackPriceRefs = useRef([]);
  const wholeSaleUnitPriceRefs = useRef([]);

  useEffect(() => {
    fetchSuppliers();
    fetchProducts();
    if (invoiceId) {
      fetchInvoice();
    }
    // IMPORTANT: we NO LONGER prefetch/assign a new posted_number here.
    // It is generated server-side on SAVE to avoid collisions when multiple forms are open.
  }, [invoiceId]);

useEffect(() => {
    // On load, auto-open the supplier search modal ONLY for a new (create) form
    // so its search field is focused. In edit mode we keep the modal closed and
    // just show the existing supplier.
    if (invoiceId) return; // skip in edit mode
    const t = setTimeout(() => {
      setSupplierSearchOpen(true);
    }, 150);
    return () => clearTimeout(t);
  }, [invoiceId]);

  useEffect(() => {
    // Handle Alt+S for save (from anywhere)
    const handleKeyDown = (e) => {
      if (e.altKey && e.key.toLowerCase() === "s") {
        e.preventDefault();
        handleSubmit(e);
      }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [form, paidTouched]);

  const fetchSuppliers = async () => {
    const res = await axios.get("/api/suppliers");
    setSuppliers(res.data);
  };

  const fetchProducts = async (q = "") => {
    const { data } = await axios.get("/api/products/search", { params: { q, limit: 30 } });
    setProducts(data);
  };

  // Fetch batches for a product and return the most recent batch (sorted by expiry descending)
  const fetchProductBatches = async (productId) => {
    try {
      const { data } = await axios.get(`/api/products/${productId}/batches`);
      // Sort by expiry date descending to get the most recent/furthest expiring batch first
      const sorted = Array.isArray(data) 
        ? data.sort((a, b) => new Date(b.expiry_date || '1970-01-01') - new Date(a.expiry_date || '1970-01-01'))
        : [];
      return sorted.length > 0 ? sorted[0] : null;
    } catch (err) {
      console.error('Failed to fetch batches:', err);
      return null;
    }
  };

  const fetchInvoice = async () => {
    const res = await axios.get(`/api/purchase-invoices/${invoiceId}`);
    let next = recalcFooter(res.data, "init");
    // 🔗 Keep total_paid linked to total_amount on edit load
    next.total_paid = next.total_amount ?? "";
    setForm(next);
    setPaidTouched(false);
    // Reflect the invoice's supplier in the search button display
    if (next.supplier_id) {
      const sup = res.data?.supplier || suppliers.find((s) => String(s.id) === String(next.supplier_id));
      setSelectedSupplier(sup || { id: next.supplier_id, name: next.supplier_name || `Supplier #${next.supplier_id}` });
    }
    await ensureProductsForItems(next?.items || []);
  };

  const handleChange = (e) => {
    const { name, value } = e.target;

    // Fields that should accept decimals as the user types
    const decimalFields = new Set([
      "invoice_amount",
      "tax_percentage",
      "tax_amount",
      "discount_percentage",
      "discount_amount",
    ]);

    const newValue = decimalFields.has(name)
      ? sanitizeNumberInput(value, true)
      : value;

    // Build a temp form with the raw typed value
    const tempForm = { ...form, [name]: newValue };

    // Recalculate totals, but DO NOT overwrite the field the user is typing
    let nextForm = recalcFooter(tempForm, name);
    nextForm[name] = newValue;

    if (!paidTouched) {
      nextForm.total_paid = nextForm.total_amount ?? "";
    }

    setForm(nextForm);
  };

  // Called when a supplier is chosen from the SupplierSearch modal
  const handleSupplierSelect = (supplier) => {
    if (!supplier?.id) return;
    setSelectedSupplier(supplier);
    setForm((prev) => ({ ...prev, supplier_id: supplier.id }));
    setSupplierSearchOpen(false);
    // Advance to the Invoice Number field
    navigateToNextField("supplier");
  };

  // Handle invoice type change (debit = pay now, credit = pay later)
  const handleInvoiceTypeChange = (type) => {
    setForm((prev) => {
      const next = { ...prev, invoice_type: type };
      // When switching to credit, set total_paid to empty (we owe full amount)
      if (type === "credit") {
        next.total_paid = "";
        setPaidTouched(true); // Mark as touched so it stays at 0
      } else {
        // Debit - auto-fill total_paid with total if not touched
        if (!paidTouched) {
          next.total_paid = next.total_amount ?? "";
        }
      }
      return next;
    });
  };

  function handleItemChange(index, field, rawValue) {
    let value = rawValue;

    // fields that can have decimals (✅ added pack_quantity and pack_bonus)
    const allowDecimalFields = [
      "pack_purchase_price",
      "unit_purchase_price",
      "pack_sale_price",
      "unit_sale_price",
      "whole_sale_pack_price",
      "whole_sale_unit_price",
      "item_discount_percentage",
      "pack_quantity",
      "pack_bonus",
    ];

    // integer-only fields
    const integerFields = [
      "unit_quantity",
      "unit_bonus",
    ];

    if (allowDecimalFields.includes(field)) {
      if (!/^\d*\.?\d*$/.test(value)) return;
    } else if (integerFields.includes(field)) {
      value = value.replace(/\D/g, "");
    }

    const newItems = [...form.items];
    newItems[index] = recalcItem({ ...newItems[index], [field]: value }, field);

    let newForm = { ...form, items: newItems };
    newForm = recalcFooter(newForm, "items");

    if (!paidTouched) {
      newForm.total_paid = newForm.total_amount ?? "";
    }

    setForm(newForm);
  }

  const addItem = () => {
    setForm((prev) => {
      const nextIndex = prev.items.length;
      setTimeout(() => scrollItemsTo(nextIndex), 60);
      return {
        ...prev,
        items: [
          ...prev.items,
          {
            product_id: "",
            batch: "",
            expiry: "",
            pack_quantity: "",
            pack_size: "",
            unit_quantity: "",
            pack_purchase_price: "",
            unit_purchase_price: "",
            pack_sale_price: "",
            unit_sale_price: "",
            whole_sale_pack_price: "",
            whole_sale_unit_price: "",
            whole_sale_margin: "",
            pack_bonus: "",
            unit_bonus: "",
            item_discount_percentage: "",
            margin: "",
            sub_total: "",
            avg_price: "",
            quantity: "",
          },
        ],
      };
    });
    setCurrentRowIndex(form.items.length);
  };

  const removeItem = (index) => {
    if (form.items.length > 1) {
      const newItems = form.items.filter((_, i) => i !== index);
      setForm({ ...form, items: newItems });
      if (currentRowIndex >= newItems.length) {
        setCurrentRowIndex(newItems.length - 1);
      }
    }
  };

  // Helper: robustly focus the ProductSearchInput inside its wrapper
  const focusProductSearch = (rowIndex = 0) => {
    scrollItemsTo(rowIndex);
    const tryFocus = () => {
      const container = productSearchRefs.current[rowIndex];
      if (!container) return false;

      if (container instanceof HTMLElement) {
        const input = container.querySelector('input, [contenteditable="true"]');
        if (input && typeof input.focus === "function") {
          input.focus();
          if (typeof input.select === "function") input.select();
          setCurrentField("product");
          setCurrentRowIndex(rowIndex);
          return true;
        }
      }

      if (container && typeof container.focus === "function") {
        container.focus();
        setCurrentField("product");
        setCurrentRowIndex(rowIndex);
        return true;
      }

      return false;
    };

    if (tryFocus()) return;

    let attempts = 0;
    const maxAttempts = 10;
    const retry = () => {
      attempts += 1;
      if (tryFocus() || attempts >= maxAttempts) return;
      setTimeout(retry, 50);
    };
    setTimeout(retry, 30);
  };

// Scroll the items container so a specific row is visible.
  // Computes the delta between the row and the container's visible box and nudges
  // the container's scrollTop by exactly that delta — moving the scrollbar row by
  // row (both up and down) without ever scrolling the outer page.
  const scrollItemsTo = (rowIndex = null) => {
    const container = itemsScrollRef.current;
    if (!container) return;
    requestAnimationFrame(() => {
      if (rowIndex !== null && rowIndex !== undefined) {
        const el = productSearchRefs.current[rowIndex]?.closest?.("tr") || productSearchRefs.current[rowIndex];
        if (el) {
const contRect = container.getBoundingClientRect();
          const elRect = el.getBoundingClientRect();
          // Space reserved in the container's viewport (header + breathing room)
          const headerPad = 8;

          // Scroll DOWN: row is below the visible bottom → scroll just enough to show it
          if (elRect.bottom > contRect.bottom) {
            container.scrollTo({
              top: container.scrollTop + (elRect.bottom - contRect.bottom + headerPad),
              behavior: "smooth",
            });
          }
          // Scroll UP: row is above the visible top → scroll just enough to show it
          else if (elRect.top < contRect.top) {
            container.scrollTo({
              top: container.scrollTop - (contRect.top - elRect.top + headerPad),
              behavior: "smooth",
            });
          }
          // Otherwise the row is already fully visible — no scroll needed
          return;
        }
      }
      container.scrollTo({ top: container.scrollHeight, behavior: "smooth" });
    });
  };

  // Merge new products into state (by id, dedup)
  const upsertProducts = (list) => {
    if (!Array.isArray(list)) return;
    setProducts((prev) => {
      const map = new Map((prev || []).map((p) => [p.id, p]));
      list.forEach((p) => p?.id && map.set(p.id, p));
      return Array.from(map.values());
    });
  };

  // Make sure all product_ids used in items exist in products[]
  const ensureProductsForItems = async (items = []) => {
    const ids = Array.from(new Set(items.map(it => it.product_id).filter(Boolean)));
    if (ids.length === 0) return;

    const have = new Set((products || []).map(p => p.id));
    const missing = ids.filter(id => !have.has(id));
    if (missing.length === 0) return;

    try {
      const { data } = await axios.get("/api/products/by-ids", {
        params: { ids: missing.join(",") },
      });
      upsertProducts(data);
      return;
    } catch (_) { /* fall back to per-id */ }

    const fetched = await Promise.all(
      missing.map(async (id) => {
        try {
          const { data } = await axios.get(`/api/products/${id}`);
          return data;
        } catch {
          try {
            const { data } = await axios.get("/api/products/search", { params: { q: id, limit: 1 } });
            return Array.isArray(data) ? data[0] : data?.data?.[0];
          } catch {
            return null;
          }
        }
      })
    );
    upsertProducts(fetched.filter(Boolean));
  };

  const focusOnField = (field, rowIndex) => {
    scrollItemsTo(rowIndex);
    setTimeout(() => {
      switch (field) {
        case "batch":
          if (batchRefs.current[rowIndex]) {
            focusAndSelect(batchRefs.current[rowIndex]);
            setCurrentField("batch");
            setCurrentRowIndex(rowIndex);
          }
          break;
        case "pack_quantity":
          if (packQuantityRefs.current[rowIndex]) {
            focusAndSelect(packQuantityRefs.current[rowIndex]);
            setCurrentField("pack_quantity");
            setCurrentRowIndex(rowIndex);
          }
          break;
        case "pack_purchase_price":
          if (packPurchasePriceRefs.current[rowIndex]) {
            focusAndSelect(packPurchasePriceRefs.current[rowIndex]);
            setCurrentField("pack_purchase_price");
            setCurrentRowIndex(rowIndex);
          }
          break;
        case "item_discount":
          if (itemDiscountRefs.current[rowIndex]) {
            focusAndSelect(itemDiscountRefs.current[rowIndex]);
            setCurrentField("item_discount");
            setCurrentRowIndex(rowIndex);
          }
          break;
        case "pack_bonus":
          if (packBonusRefs.current[rowIndex]) {
            focusAndSelect(packBonusRefs.current[rowIndex]);
            setCurrentField("pack_bonus");
            setCurrentRowIndex(rowIndex);
          }
          break;
        case "pack_sale_price":
          if (packSalePriceRefs.current[rowIndex]) {
            focusAndSelect(packSalePriceRefs.current[rowIndex]);
            setCurrentField("pack_sale_price");
            setCurrentRowIndex(rowIndex);
          }
          break;
        case "whole_sale_pack_price":
          if (wholeSalePackPriceRefs.current[rowIndex]) {
            focusAndSelect(wholeSalePackPriceRefs.current[rowIndex]);
            setCurrentField("whole_sale_pack_price");
            setCurrentRowIndex(rowIndex);
          }
          break;
        case "whole_sale_unit_price":
          if (wholeSaleUnitPriceRefs.current[rowIndex]) {
            focusAndSelect(wholeSaleUnitPriceRefs.current[rowIndex]);
            setCurrentField("whole_sale_unit_price");
            setCurrentRowIndex(rowIndex);
          }
          break;
        default:
          focusProductSearch(rowIndex);
          break;
      }
    }, 50);
  };

  const navigateToNextField = (currentFieldName, rowIndex = 0) => {
    setTimeout(() => {
      switch (currentFieldName) {
        case "supplier":
          if (invoiceNumberRef.current) {
            focusAndSelect(invoiceNumberRef.current);
            setCurrentField("invoice_number");
          }
          break;
        case "invoice_number":
          if (invoiceAmountRef.current) {
            focusAndSelect(invoiceAmountRef.current);
            setCurrentField("invoice_amount");
          }
          break;
        case "invoice_amount":
          focusProductSearch(0);
          break;
        case "product":
          // If batch field has ref and is visible, check if we should focus it
          if (batchRefs.current[rowIndex]) {
            batchRefs.current[rowIndex].focus();
            setCurrentField("batch");
          } else if (packQuantityRefs.current[rowIndex]) {
            packQuantityRefs.current[rowIndex].focus();
            setCurrentField("pack_quantity");
          }
          break;
        case "batch":
          if (packQuantityRefs.current[rowIndex]) {
            packQuantityRefs.current[rowIndex].focus();
            setCurrentField("pack_quantity");
          }
          break;
        case "pack_quantity":
          if (packPurchasePriceRefs.current[rowIndex]) {
            packPurchasePriceRefs.current[rowIndex].focus();
            setCurrentField("pack_purchase_price");
          }
          break;
        case "pack_purchase_price":
          if (itemDiscountRefs.current[rowIndex]) {
            itemDiscountRefs.current[rowIndex].focus();
            setCurrentField("item_discount");
          }
          break;
        case "item_discount":
          if (packBonusRefs.current[rowIndex]) {
            packBonusRefs.current[rowIndex].focus();
            setCurrentField("pack_bonus");
          }
          break;
        case "pack_bonus":
          if (packSalePriceRefs.current[rowIndex]) {
            packSalePriceRefs.current[rowIndex].focus();
            setCurrentField("pack_sale_price");
          }
          break;
        case "pack_sale_price":
          if (wholeSalePackPriceRefs.current[rowIndex]) {
            wholeSalePackPriceRefs.current[rowIndex].focus();
            setCurrentField("whole_sale_pack_price");
          }
          break;
        case "whole_sale_pack_price":
          if (wholeSaleUnitPriceRefs.current[rowIndex]) {
            wholeSaleUnitPriceRefs.current[rowIndex].focus();
            setCurrentField("whole_sale_unit_price");
          }
          break;
        case "whole_sale_unit_price":
          if (rowIndex < form.items.length - 1) {
            focusProductSearch(rowIndex + 1);
          } else {
            addItem();
            focusProductSearch(rowIndex + 1);
          }
          break;
      }
    }, 50);
  };

  const handleKeyDown = (e, field, rowIndex = 0) => {
    if (e.key === "Enter") {
      e.preventDefault();
      navigateToNextField(field, rowIndex);
    } else if (e.key === "Tab" && field === "invoice_amount") {
      e.preventDefault();
      navigateToNextField(field, rowIndex);
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      if (rowIndex === form.items.length - 1) {
        addItem();
        setTimeout(() => {
          focusProductSearch(rowIndex + 1);
        }, 200);
      } else {
        const nextRowIndex = rowIndex + 1;
        focusOnField(field, nextRowIndex);
      }
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      if (rowIndex > 0) {
        const prevRowIndex = rowIndex - 1;
        focusOnField(field, prevRowIndex);
      }
    }
  };

  const handleProductKeyDown = (e, rowIndex) => {
    if (e.key === "Enter") {
      e.preventDefault();
      navigateToNextField("product", rowIndex);
    } else if (e.key === "ArrowUp" && rowIndex > 0) {
      e.preventDefault();
      const prevRowIndex = rowIndex - 1;
      focusProductSearch(prevRowIndex);
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      if (rowIndex === form.items.length - 1) {
        addItem();
        setTimeout(() => {
          focusProductSearch(rowIndex + 1);
        }, 200);
      } else {
        focusProductSearch(rowIndex + 1);
      }
    }
  };

  const zeroToEmpty = (v) => (v === 0 || v === "0" ? "" : (v ?? ""));
  const focusAndSelect = (el) => {
    if (!el) return;
    el.focus();
    setTimeout(() => {
      if (typeof el.select === "function") el.select();
    }, 0);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();

    // Validate supplier selection for credit invoices
    if (form.invoice_type === "credit" && !form.supplier_id) {
      return toast.error("Please select a supplier for credit purchase");
    }

    // 0) Block if any selected product has margin <= 0 (or not a number)
    const badItem = form.items.find((item) => {
      if (!item.product_id) return false;
      const m = Number(item.margin);
      return !Number.isFinite(m) || m <= 0;
    });
    if (badItem) {
      const idx = form.items.indexOf(badItem);
      const product = products.find((p) => p.id === badItem.product_id);
      const productName = product?.name || `row ${idx + 1}`;
      toast.error(`Margin must be greater than 0 for ${productName}`);
      return;
    }

    // 1) Prevent negative margin (redundant now)
    const negativeMarginItem = form.items.find(
      (item) => item.product_id && Number(item.margin) < 0
    );
    if (negativeMarginItem) {
      const product = products.find((p) => p.id === negativeMarginItem.product_id);
      const productName = product ? product.name : negativeMarginItem.product_id;
      toast.error(`Margin cannot be negative for Product ${productName}`);
      return;
    }

    // 2) Validate invoice vs total
    const invoiceAmount = Number(form.invoice_amount || 0);
    const totalAmount = Number(form.total_amount || 0);
    const totalPaid = Number(form.total_paid || 0);
    if (totalPaid < 0) {
      toast.error("Total Paid cannot be negative");
      return;
    }
    if (totalPaid > totalAmount) {
      toast.error("Total Paid cannot exceed Total Amount");
      return;
    }
    if (Math.abs(invoiceAmount - totalAmount) > 5) {
      toast.error(
        `Invoice amount (${invoiceAmount}) must be equal to total amount (${totalAmount}), difference > 5`
      );
      return;
    }

    try {
      // 3) Duplicate invoice number check per supplier
      const checkRes = await axios.get("/api/purchase-invoices/check-unique", {
        params: {
          supplier_id: form.supplier_id,
          invoice_number: form.invoice_number,
          exclude_id: invoiceId || null,
        },
      });
      if (!checkRes.data.unique) {
        toast.error(
          `Invoice number "${form.invoice_number}" already exists for this supplier`
        );
        return;
      }

      // 🔒 Final guard: keep linked if user never changed it
      const payload = {
        ...form,
        total_paid: paidTouched ? form.total_paid : (form.total_amount ?? ""),
      };

      const payloadToSend = { ...payload };
      if (!invoiceId) {
        // Do NOT send posted_number on CREATE; server will atomically generate it.
        delete payloadToSend.posted_number;
      }

      // If onSubmit prop is provided, use it for API calls
      if (onSubmit) {
        const result = await onSubmit(payloadToSend);
        toast.success("Invoice saved successfully");
        onSuccess && onSuccess();
        return result;
      }

      // Original logic when onSubmit is not provided
      if (invoiceId) {
        await axios.put(`/api/purchase-invoices/${invoiceId}`, payloadToSend);
        toast.success("Invoice updated successfully");
        onSuccess && onSuccess();
      } else {
        const { data: saved } = await axios.post("/api/purchase-invoices", payloadToSend);
        // Server returns created invoice with posted_number assigned — reflect it locally.
        setForm((prev) => ({ ...prev, posted_number: saved?.posted_number || prev.posted_number }));
        toast.success(`Invoice created: ${saved?.posted_number || "(number assigned)"}`);
        onSuccess && onSuccess();
      }
    } catch (err) { showAxiosError(err); }
  };

  // Common anti-autofill props to spread on inputs
  const antiFill = {
    autoComplete: "off",
    autoCorrect: "off",
    autoCapitalize: "off",
    spellCheck: false,
  };

  // Get dark mode state and theme colors
  const { isDark, theme } = useTheme();
  
  // Get sale system setting to conditionally show wholesale fields
  const { hasWholesale, loading: saleSystemLoading } = useSaleSystem();

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
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
        tertiaryLight: '#cffafe',
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
        glass: {
          className: `${radiusClass} transition-all duration-200`,
          style: {
            backgroundColor: 'transparent',
            color: isDark ? '#f1f5f9' : '#111827',
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
      glass: {
        className: radiusClass,
        style: {
          backgroundColor: isDark ? 'rgba(51, 65, 85, 0.6)' : 'rgba(255, 255, 255, 0.8)',
          color: isDark ? '#f1f5f9' : '#111827',
          backdropFilter: 'blur(6px)',
          border: isDark ? '1px solid rgba(71, 85, 105, 0.5)' : '1px solid rgba(229, 231, 235, 0.6)',
        }
      },
    };
  }, [buttonStyle, themeColors, isDark]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;
  const btnGlass = getButtonClasses.glass;

  return (
    <form
      className="flex flex-col h-full"
      style={{ minHeight: "calc(100vh - 130px)", maxHeight: "calc(100vh - 130px)" }}
      autoComplete="off" // disable browser suggestions globally
    >
{/* ================= HEADER SECTION ================= */}
      <div className="sticky top-0 z-10 shadow-lg" autoComplete="off">
{/* ---- Branded Title Banner ---- */}
        <div
          className="px-4 py-3 flex items-center justify-between"
          style={{
            background: themeColors.primary,
          }}
        >
          <div className="flex items-center gap-3">
            <div
              className="w-10 h-10 rounded-xl flex items-center justify-center shadow-inner"
              style={{ backgroundColor: "rgba(255,255,255,0.18)", backdropFilter: "blur(4px)" }}
            >
              {/* Document / invoice icon */}
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                <polyline points="14 2 14 8 20 8" />
                <line x1="8" y1="13" x2="16" y1="13" />
                <line x1="8" y1="17" x2="13" y1="17" />
              </svg>
            </div>
            <div>
              <h2 className="text-xl font-extrabold tracking-wide text-white leading-none">
                PURCHASE INVOICE
              </h2>
              <p className="text-[11px] text-white/80 mt-1">
                Enter to navigate · Alt+S to save
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3">
            {/* Invoice Type Segmented Control */}
            <div
              className="flex items-center rounded-lg p-1 shadow-inner"
              style={{ backgroundColor: "rgba(0,0,0,0.18)" }}
            >
              {["debit", "credit"].map((type) => {
                const active = form.invoice_type === type;
                return (
                  <button
                    key={type}
                    type="button"
                    onClick={() => handleInvoiceTypeChange(type)}
                    className={`px-4 py-1.5 text-xs font-semibold rounded-md transition-all duration-200 ${
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

            {/* Add Product Button */}
            <button
              type="button"
              onClick={() => setShowProductModal(true)}
              className={`px-4 py-2 text-xs font-semibold rounded-lg transition-all duration-200 bg-white/95 hover:bg-white shadow`}
              style={{ color: themeColors.primaryHover }}
            >
              + Add Product
            </button>
          </div>
        </div>

        {/* ---- Fields Card ---- */}
        <div className="bg-white dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700 px-3 py-2.5">
<div className="grid grid-cols-12 gap-2 items-end">
            {/* Posted Number - first */}
            <div className="col-span-2">
              <label className="block text-[10px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Posted Number</label>
              <input
                type="text"
                inputMode="decimal"
                name="posted_number"
                readOnly
                placeholder="(auto on save)"
                value={form.posted_number || ""}
                className="bg-gray-100 dark:bg-slate-600 border border-gray-200 dark:border-slate-600 rounded-md w-full px-2 h-8 text-xs text-gray-900 dark:text-gray-100"
                {...antiFill}
              />
            </div>

            {/* Date - second */}
            <div className="col-span-2">
              <label className="block text-[10px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Date</label>
              <input
                type="date"
                name="posted_date"
                value={form.posted_date}
                onChange={handleChange}
                className="border border-gray-200 dark:border-slate-600 rounded-md w-full px-2 h-8 text-xs bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                {...antiFill}
              />
            </div>

{/* Supplier - opens search modal */}
            <div className="col-span-4">
              <label className="block text-[10px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Supplier *</label>
              <button
                type="button"
                ref={supplierRef}
                onClick={() => setSupplierSearchOpen(true)}
                className={`w-full h-8 px-2 rounded-md border text-left text-xs flex items-center gap-2 transition-all duration-200
                  ${selectedSupplier
                    ? 'border-blue-400 bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-200'
                    : 'border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-700 text-gray-500 dark:text-gray-400 hover:border-blue-400'
                  }`}
                style={{ color: selectedSupplier ? undefined : (isDark ? "#94a3b8" : "#9ca3af") }}
                {...antiFill}
              >
                <BuildingStorefrontIcon className="w-4 h-4 flex-shrink-0" />
                {selectedSupplier ? (
                  <span className="truncate font-medium" style={{ color: isDark ? "#bfdbfe" : "#1d4ed8" }}>
                    {selectedSupplier.name}
                  </span>
                ) : (
                  <span className="truncate">Click to search supplier...</span>
                )}
              </button>
            </div>

{/* Invoice Number */}
            <div className="col-span-1">
              <label className="block text-[10px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Invoice Number</label>
              <input
                ref={invoiceNumberRef}
                type="text"
                name="invoice_number"
                value={form.invoice_number}
                onChange={handleChange}
                onKeyDown={(e) => handleKeyDown(e, "invoice_number")}
                onFocus={(e) => e.target.select()}
                className="border border-gray-200 dark:border-slate-600 rounded-md w-full px-2 h-8 text-xs bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                {...antiFill}
              />
            </div>

{/* Invoice Amount */}
            <div className="col-span-1">
              <label className="block text-[10px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Invoice Amount</label>
              <input
                ref={invoiceAmountRef}
                type="text"
                name="invoice_amount"
                value={form.invoice_amount}
                onChange={handleChange}
                onKeyDown={(e) => handleKeyDown(e, "invoice_amount")}
                onFocus={(e) => e.target.select()}
                className="border border-gray-200 dark:border-slate-600 rounded-md w-full px-2 h-8 text-xs bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                {...antiFill}
              />
            </div>

{/* Difference */}
            <div className="col-span-2">
              <label className="block text-[10px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Difference</label>
              <input
                type="text"
                readOnly
                value={
                  form.invoice_amount && form.total_amount
                    ? (Number(form.invoice_amount) - Number(form.total_amount)).toFixed(2)
                    : ""
                }
                className={`border border-gray-200 dark:border-slate-600 rounded-md w-full px-2 h-8 text-xs font-bold text-center bg-gray-100 dark:bg-slate-600 ${
                  Number(form.invoice_amount) - Number(form.total_amount) !== 0
                    ? "text-red-600 dark:text-red-400"
                    : "text-gray-700 dark:text-gray-300"
                }`}
                {...antiFill}
              />
            </div>
          </div>

          {/* Remarks row */}
          <div className="mt-2">
            <label className="block text-[10px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">Remarks</label>
            <input
              type="text"
              name="remarks"
              value={form.remarks}
              onChange={handleChange}
              className="border border-gray-200 dark:border-slate-600 rounded-md w-full px-2 h-8 text-xs bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
              {...antiFill}
            />
          </div>
        </div>
      </div>

      {/* ================= ITEMS SECTION ================= */}
      <div ref={itemsScrollRef} className="flex-1 min-h-0 overflow-auto pt-1 px-1 pb-16 bg-gray-50 dark:bg-slate-800/50" autoComplete="off">
        <h2 className="text-xs font-bold mb-1 text-gray-900 dark:text-gray-100">Items (↑↓ arrows to navigate rows)</h2>

        <table className="w-full border-collapse text-[11px]">
          <thead className="sticky top-0 bg-gray-100 dark:bg-slate-700 z-5">
            <tr>
              <th rowSpan={2} className="border w-6 bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">#</th>
              <th rowSpan={2} colSpan={1} className="border w-[80px] bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Product</th>
              <th colSpan={3} className="border bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Pack Size / Batch / Expiry</th>
              <th colSpan={2} className="border bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Qty (Pack / Unit)</th>
              <th colSpan={2} className="border bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Purchase Price (P / U)</th>
              <th colSpan={3} className="border bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Disc % / Bonus (P / U)</th>
              <th colSpan={2} className="border bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Sale Price (P / U)</th>
              <th colSpan={2} className={`border bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100 ${!hasWholesale ? 'hidden' : ''}`}>Wholesale Price (P / U)</th>
              <th colSpan={hasWholesale ? 4 : 3} className="border bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Margin % {hasWholesale && '/ W.S.Mrg%'} / Avg / Sub Total</th>
              <th rowSpan={2} className="border w-5 bg-gray-200 dark:bg-slate-600 text-gray-900 dark:text-gray-100">+</th>
            </tr>

            <tr>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">PSize</th>
              <th className="border w-16 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Batch</th>
              <th className="border w-20 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Exp</th>
              <th className="border w-12 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Pack.Q</th>
              <th className="border w-12 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Unit.Q</th>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Pack.P</th>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Unit.P</th>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Disc%</th>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">PBonus</th>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">UBonus</th>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Pack.S</th>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Unit.S</th>
              <th className={`border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100 ${!hasWholesale ? 'hidden' : ''}`}>W.S.Pack</th>
              <th className={`border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100 ${!hasWholesale ? 'hidden' : ''}`}>W.S.Unit</th>
              <th className="border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Margin%</th>
              <th className={`border w-14 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100 ${!hasWholesale ? 'hidden' : ''}`}>W.S.Mrg%</th>
              <th className="border w-16 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Avg</th>
              <th className="border w-24 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100">Sub Total</th>
            </tr>
          </thead>

          <tbody>
            {form.items.map((item, i) => (
              <tr key={i} className="text-center odd:bg-white even:bg-gray-50 dark:odd:bg-slate-700/30 dark:even:bg-slate-700/50">
                {/* Remove */}
                <td className="border">
                  <button
                    type="button"
                    onClick={() => removeItem(i)}
                    className={`px-1 rounded text-[10px] transition-all duration-200 ${btnDanger.className}`}
                    style={btnDanger.style}
                  >
                    X
                  </button>
                </td>

                {/* Product Search Input */}
                <td colSpan={1} className="border text-left w-[200px]">
                  <div ref={(el) => (productSearchRefs.current[i] = el)}>
                    <ProductSearchInput
                      value={products.find(p => p.id === item.product_id) || item.product_id}
                      onChange={async (val) => {
                        const list = Array.isArray(products) ? products : (Array.isArray(products?.data) ? products.data : []);
                        const selectedProduct = (val && typeof val === "object") ? val : list.find((p) => p?.id === val);
                        if (!selectedProduct) return;

                        const get = (obj, keys, d="") => {
                          for (const k of keys) {
                            const v = obj?.[k];
                            if (v !== undefined && v !== null && v !== "") return v;
                          }
                          return d;
                        };

                        const toNum = (x) => {
                          const n = Number(x);
                          return Number.isFinite(n) ? n : null;
                        };

                        const packSize = toNum(get(selectedProduct, ["pack_size","packSize","packsize"]));
                        const packPurchase = toNum(get(selectedProduct, ["pack_purchase_price","packPurchasePrice"]));
                        const unitPurchase = toNum(get(selectedProduct, ["unit_purchase_price","unitPurchasePrice"])) ?? ((packPurchase != null && packSize) ? (packPurchase / packSize) : null);
                        const packSale = toNum(get(selectedProduct, ["pack_sale_price","packSalePrice"]));
                        const unitSale = toNum(get(selectedProduct, ["unit_sale_price","unitSalePrice"])) ?? ((packSale != null && packSize) ? (packSale / packSize) : null);
                        // Get wholesale prices from product
                        const wholeSalePack = toNum(get(selectedProduct, ["whole_sale_pack_price","wholeSalePackPrice"]));
                        const wholeSaleUnit = toNum(get(selectedProduct, ["whole_sale_unit_price","wholeSaleUnitPrice"])) ?? ((wholeSalePack != null && packSize) ? (wholeSalePack / packSize) : null);
                        const margin = get(selectedProduct, ["margin","margin_percentage","marginPercent"], "");
                        const avg = get(selectedProduct, ["avg_price","average_price","avgPrice"], "");

                        // Fetch batches for the selected product and auto-populate if exists
                        const batchData = await fetchProductBatches(selectedProduct?.id);
                        const batch = batchData?.batch_number || "";
                        const expiry = batchData?.expiry_date || "";

                        const newItems = [...form.items];
                        const prepared = {
                          ...newItems[i],
                          product_id: selectedProduct?.id || "",
                          pack_size: packSize ?? "",
                          pack_purchase_price: zeroToEmpty(packPurchase),
                          unit_purchase_price: unitPurchase ?? "",
                          pack_sale_price: zeroToEmpty(packSale),
                          unit_sale_price: unitSale ?? "",
                          whole_sale_pack_price: zeroToEmpty(wholeSalePack),
                          whole_sale_unit_price: wholeSaleUnit ?? "",
                          whole_sale_margin: "",
                          batch: batch,
                          expiry: expiry,
                          pack_quantity: "",
                          unit_quantity: "",
                          pack_bonus: "",
                          unit_bonus: "",
                          item_discount_percentage: "",
                          margin: margin ?? "",
                          sub_total: "",
                          avg_price: avg ?? "",
                          quantity: "",
                        };

                        newItems[i] = recalcItem(prepared, "product");
                        let nextForm = { ...form, items: newItems };
                        nextForm = recalcFooter(nextForm, "items");
                        if (!paidTouched) nextForm.total_paid = nextForm.total_amount ?? "";
                        setForm(nextForm);
                        
                        // Navigate to next field - if batch has data, focus on batch, otherwise go to pack_quantity
                        if (batch) {
                          navigateToNextField("product", i);
                        } else {
                          // No batch, navigate directly to pack_quantity
                          setTimeout(() => {
                            if (packQuantityRefs.current[i]) {
                              packQuantityRefs.current[i].focus();
                              setCurrentField("pack_quantity");
                              setCurrentRowIndex(i);
                            }
                          }, 50);
                        }
                      }}
                      onKeyDown={(e) => handleProductKeyDown(e, i)}
                      products={products}
                      onRefreshProducts={fetchProducts}
                    />
                  </div>
                </td>

                {/* Pack Size */}
                <td className="border w-14">
                  <input
                    type="number"
                    readOnly
                    value={item.pack_size ?? ""}
                    className="border bg-gray-100 dark:bg-slate-600 w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Batch */}
                <td className="border w-16">
                  <input
                    ref={(el) => (batchRefs.current[i] = el)}
                    type="text"
                    value={item.batch ?? ""}
                    onChange={(e) => {
                      const newBatch = e.target.value;
                      const duplicateIndex = form.items.findIndex((it, idx) => {
                        if (idx === i) return false;
                        if (it.product_id !== item.product_id) return false;
                        if (it.batch && it.batch.trim() !== "") {
                          return it.batch === newBatch;
                        }
                        return !newBatch;
                      });

                      if (duplicateIndex !== -1) {
                        toast.error(
                          newBatch
                            ? `Product "${products.find((p) => p.id === item.product_id)?.name}" with batch "${newBatch}" already exists in row ${duplicateIndex + 1}`
                            : `Product "${products.find((p) => p.id === item.product_id)?.name}" without batch already exists in row ${duplicateIndex + 1}`
                        );
                        return;
                      }

                      const newItems = [...form.items];
                      newItems[i] = { ...newItems[i], batch: newBatch };
                      setForm({ ...form, items: newItems });
                    }}
                    onKeyDown={(e) => handleKeyDown(e, "batch", i)}
                    className="border w-full h-6 text-[11px] px-1 bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Expiry */}
                <td className="border w-20">
                  <input
                    type="date"
                    value={item.expiry ?? ""}
                    onChange={(e) => handleItemChange(i, "expiry", e.target.value)}
                    className="border w-full h-6 text-[11px] px-1 bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Pack Qty (✅ now supports decimals) */}
                <td className="border">
                  <input
                    ref={(el) => (packQuantityRefs.current[i] = el)}
                    type="text"
                    value={item.pack_quantity === 0 ? "" : item.pack_quantity}
                    onChange={(e) => handleItemChange(i, "pack_quantity", e.target.value)}
                    onKeyDown={(e) => handleKeyDown(e, "pack_quantity", i)}
                    onFocus={(e) => e.target.select()}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Unit Qty */}
                <td className="border">
                  <input
                    type="text"
                    value={item.unit_quantity === 0 ? "" : item.unit_quantity}
                    onChange={(e) => handleItemChange(i, "unit_quantity", e.target.value)}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Pack Purchase */}
                <td className="border">
                  <input
                    ref={(el) => (packPurchasePriceRefs.current[i] = el)}
                    type="text"
                    value={item.pack_purchase_price === 0 ? "" : item.pack_purchase_price}
                    onChange={(e) => handleItemChange(i, "pack_purchase_price", e.target.value)}
                    onKeyDown={(e) => handleKeyDown(e, "pack_purchase_price", i)}
                    onFocus={(e) => e.target.select()}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Unit Purchase */}
                <td className="border">
                  <input
                    type="text"
                    value={item.unit_purchase_price ?? ""}
                    onChange={(e) => handleItemChange(i, "unit_purchase_price", e.target.value)}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Disc% */}
                <td className="border">
                  <input
                    ref={(el) => (itemDiscountRefs.current[i] = el)}
                    type="text"
                    value={item.item_discount_percentage ?? ""}
                    onChange={(e) =>
                      handleItemChange(i, "item_discount_percentage", e.target.value)
                    }
                    onKeyDown={(e) => handleKeyDown(e, "item_discount", i)}
                    onFocus={(e) => e.target.select()}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Pack Bonus (✅ now supports decimals) */}
                <td className="border">
                  <input
                    ref={(el) => (packBonusRefs.current[i] = el)}
                    type="text"
                    value={item.pack_bonus === 0 ? "" : item.pack_bonus}
                    onChange={(e) => handleItemChange(i, "pack_bonus", e.target.value)}
                    onKeyDown={(e) => handleKeyDown(e, "pack_bonus", i)}
                    onFocus={(e) => e.target.select()}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Unit Bonus */}
                <td className="border">
                  <input
                    type="text"
                    value={item.unit_bonus === 0 ? "" : item.unit_bonus}
                    onChange={(e) => handleItemChange(i, "unit_bonus", e.target.value)}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Pack Sale */}
                <td className="border">
                  <input
                    ref={(el) => (packSalePriceRefs.current[i] = el)}
                    type="text"
                    value={item.pack_sale_price === 0 ? "" : item.pack_sale_price}
                    onChange={(e) => handleItemChange(i, "pack_sale_price", e.target.value)}
                    onKeyDown={(e) => handleKeyDown(e, "pack_sale_price", i)}
                    onFocus={(e) => e.target.select()}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Unit Sale */}
                <td className="border">
                  <input
                    type="text"
                    value={item.unit_sale_price ?? ""}
                    onChange={(e) => handleItemChange(i, "unit_sale_price", e.target.value)}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Wholesale Pack Sale */}
                <td className={`border ${!hasWholesale ? 'hidden' : ''}`}>
                  <input
                    ref={(el) => (wholeSalePackPriceRefs.current[i] = el)}
                    type="text"
                    value={item.whole_sale_pack_price === 0 ? "" : item.whole_sale_pack_price}
                    onChange={(e) => handleItemChange(i, "whole_sale_pack_price", e.target.value)}
                    onKeyDown={(e) => handleKeyDown(e, "whole_sale_pack_price", i)}
                    onFocus={(e) => e.target.select()}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

{/* Wholesale Unit Sale */}
                <td className={`border ${!hasWholesale ? 'hidden' : ''}`}>
                  <input
                    ref={(el) => (wholeSaleUnitPriceRefs.current[i] = el)}
                    type="text"
                    value={item.whole_sale_unit_price ?? ""}
                    onChange={(e) => handleItemChange(i, "whole_sale_unit_price", e.target.value)}
                    onKeyDown={(e) => handleKeyDown(e, "whole_sale_unit_price", i)}
                    onFocus={(e) => e.target.select()}
                    className="border w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Margin % */}
                <td className="border">
                  <input
                    type="number"
                    readOnly
                    value={item.margin ?? ""}
                    className="border bg-gray-100 dark:bg-slate-600 w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

{/* W.S. Margin % */}
                <td className={`border ${!hasWholesale ? 'hidden' : ''}`}>
                  <input
                    type="number"
                    readOnly
                    value={item.whole_sale_margin ?? ""}
                    className="border bg-gray-100 dark:bg-slate-600 w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Avg Price (readonly) */}
                <td className="border">
                  <input
                    type="number"
                    value={item.avg_price ?? ""}
                    readOnly
                    className="border w-full h-6 text-[11px] px-1 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none"
                    {...antiFill}
                  />
                </td>

                {/* Sub Total (readonly) */}
                <td className="border w-24">
                  <input
                    type="number"
                    value={item.sub_total ?? ""}
                    readOnly
                    className="border w-full h-6 text-[11px] px-1 bg-gray-100 dark:bg-slate-600 text-gray-900 dark:text-gray-100 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none"
                    {...antiFill}
                  />
                </td>

                {/* Quantity (hidden) */}
                <td className="border" style={{ display: "none" }}>
                  <input
                    type="number"
                    readOnly
                    hidden
                    value={item.quantity ?? ""}
                    className="border bg-gray-100 dark:bg-slate-600 w-full h-6 text-[11px] px-1 appearance-none [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none text-gray-900 dark:text-gray-100"
                    {...antiFill}
                  />
                </td>

                {/* Add */}
                <td className="border w-5">
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
            ))}
          </tbody>
        </table>
      </div>

      {/* ================= FOOTER SECTION ================= */}
      <div className="sticky bottom-0 z-10 bg-white dark:bg-slate-800 shadow-[0_-4px_16px_-6px_rgba(0,0,0,0.18)] border-t border-gray-200 dark:border-slate-700" autoComplete="off">
        <div className="px-4 py-3">
          <div className="flex gap-5">
            {/* ---- Left: Invoice Summary Breakdown ---- */}
            <div className="flex-1 min-w-0">
              <div className="text-[10px] font-bold uppercase tracking-wider text-gray-400 dark:text-gray-500 mb-2">
                Invoice Summary
              </div>
              <div className="flex gap-6 flex-wrap">
                {/* Editable adjustment fields */}
                <div className="flex gap-2 items-end">
                  <div>
                    <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-0.5">Discount %</label>
                    <input
                      ref={discountPercentageRef}
                      type="text"
                      name="discount_percentage"
                      value={form.discount_percentage ?? ""}
                      onChange={handleChange}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") {
                          e.preventDefault();
                          taxPercentageRef.current?.focus();
                        }
                      }}
                      className="border border-gray-200 dark:border-slate-600 rounded-md w-16 px-1.5 h-7 text-xs text-right bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-1 focus:ring-blue-400/40"
                      {...antiFill}
                    />
                  </div>
                  <div>
                    <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-0.5">Discount</label>
                    <input
                      type="text"
                      name="discount_amount"
                      value={form.discount_amount ?? ""}
                      onChange={handleChange}
                      className="border border-gray-200 dark:border-slate-600 rounded-md w-24 px-1.5 h-7 text-xs text-right bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-1 focus:ring-blue-400/40"
                      {...antiFill}
                    />
                  </div>
                  <div>
                    <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-0.5">Tax %</label>
                    <input
                      ref={taxPercentageRef}
                      type="text"
                      name="tax_percentage"
                      value={form.tax_percentage ?? ""}
                      onChange={handleChange}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") {
                          e.preventDefault();
                          saveButtonRef.current?.focus();
                        }
                      }}
                      className="border border-gray-200 dark:border-slate-600 rounded-md w-16 px-1.5 h-7 text-xs text-right bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-1 focus:ring-blue-400/40"
                      {...antiFill}
                    />
                  </div>
                  <div>
                    <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-0.5">Tax</label>
                    <input
                      type="text"
                      name="tax_amount"
                      value={form.tax_amount ?? ""}
                      onChange={handleChange}
                      className="border border-gray-200 dark:border-slate-600 rounded-md w-24 px-1.5 h-7 text-xs text-right bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-1 focus:ring-blue-400/40"
                      {...antiFill}
                    />
                  </div>
                </div>

                {/* Computed totals */}
                <div className="flex-1 min-w-[240px]">
                  <div className="rounded-lg border border-gray-200 dark:border-slate-600 overflow-hidden">
                    <div className="flex items-center justify-between px-3 py-1 border-b border-gray-100 dark:border-slate-700 text-[11px]">
                      <span className="text-gray-500 dark:text-gray-400">Items Total</span>
                      <span className="font-medium text-gray-900 dark:text-gray-100">
                        {to2(form.rows_total || 0).toFixed(2)}
                      </span>
                    </div>
                    {Number(form.discount_amount || 0) > 0 && (
                      <div className="flex items-center justify-between px-3 py-1 border-b border-gray-100 dark:border-slate-700 text-[11px]">
                        <span className="text-gray-500 dark:text-gray-400">Discount {form.discount_percentage ? `(${to2(form.discount_percentage)}%)` : ""}</span>
                        <span className="font-medium text-red-500 dark:text-red-400">
                          − {to2(form.discount_amount || 0).toFixed(2)}
                        </span>
                      </div>
                    )}
                    {Number(form.tax_amount || 0) > 0 && (
                      <div className="flex items-center justify-between px-3 py-1 border-b border-gray-100 dark:border-slate-700 text-[11px]">
                        <span className="text-gray-500 dark:text-gray-400">Tax {form.tax_percentage ? `(${to2(form.tax_percentage)}%)` : ""}</span>
                        <span className="font-medium text-gray-900 dark:text-gray-100">
                          + {to2(form.tax_amount || 0).toFixed(2)}
                        </span>
                      </div>
                    )}
                    <div
                      className="flex items-center justify-between px-3 py-1.5 text-xs font-bold"
                      style={{ background: `${themeColors.primary}14`, color: themeColors.primary }}
                    >
                      <span>Total Amount</span>
                      <span className="text-sm">{to2(form.total_amount || 0).toFixed(2)}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            {/* ---- Right: Payment + Action ---- */}
            <div className="w-[250px] flex-shrink-0 flex flex-col justify-between">
              <div className="flex gap-2">
                <div className="flex-1">
                  <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-0.5">Total Paid</label>
                  <div className="flex items-center gap-1">
                    <input
                      type="text"
                      name="total_paid"
                      value={form.total_paid ?? ""}
                      onChange={(e) => {
                        const v = sanitizeNumberInput(e.target.value, true);
                        setPaidTouched(true);
                        setForm((prev) => ({ ...prev, total_paid: v }));
                      }}
                      onBlur={() => {
                        setForm((prev) => {
                          const normalized = prev.total_paid === "" ? "" : to2(prev.total_paid).toFixed(2);
                          const amt = prev.total_amount === "" || prev.total_amount == null ? "" : to2(prev.total_amount).toFixed(2);
                          if (normalized !== "" && normalized === amt) {
                            setPaidTouched(false);
                          }
                          return { ...prev, total_paid: normalized };
                        });
                      }}
                      className="border border-gray-200 dark:border-slate-600 rounded-md w-full px-1.5 h-7 text-xs text-right bg-white dark:bg-slate-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-1 focus:ring-blue-400/40"
                      {...antiFill}
                    />
                    <button
                      type="button"
                      title="Relink paid to total"
                      onClick={() => {
                        setPaidTouched(false);
                        setForm((prev) => ({ ...prev, total_paid: prev.total_amount ?? "" }));
                      }}
                      className="px-1.5 py-1 text-[10px] rounded-md transition-all duration-200 flex-shrink-0"
                      style={{
                        background: isDark ? 'rgba(71, 85, 105, 0.6)' : 'rgba(255, 255, 255, 0.8)',
                        backdropFilter: 'blur(4px)',
                        border: '1px solid rgba(255, 255, 255, 0.2)',
                        color: isDark ? '#94a3b8' : '#64748b'
                      }}
                    >
                      🔗
                    </button>
                  </div>
                </div>
                <div className="w-20">
                  <label className="block text-[9px] font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-0.5">Remaining</label>
                  <div className={`h-7 flex items-center px-1.5 rounded-md text-xs font-bold border border-gray-200 dark:border-slate-600 bg-gray-50 dark:bg-slate-600/50 ${
                    (form.total_amount || 0) - (form.total_paid || 0) > 0
                      ? "text-amber-600 dark:text-amber-400"
                      : "text-emerald-600 dark:text-emerald-400"
                  }`}>
                    {to2((form.total_amount || 0) - (form.total_paid || 0)).toFixed(2)}
                  </div>
                </div>
              </div>

              <button
                ref={saveButtonRef}
                type="button"
                onClick={handleSubmit}
                className={`w-full mt-2 py-2 rounded-lg text-sm font-bold tracking-wide transition-all duration-200 ${btnPrimary.className}`}
                style={btnPrimary.style}
              >
                {invoiceId ? "Update Invoice" : "Save Invoice"}
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Product Form Modal */}
      <ProductFormModal
        open={showProductModal}
        onClose={() => setShowProductModal(false)}
        onProductCreated={(newProduct) => {
          // Refresh products list
          fetchProducts();
          // Update local products state
          setProducts(prev => [...(prev || []), newProduct]);
        }}
      />

      {/* Supplier Search Modal */}
      <SupplierSearch
        isOpen={supplierSearchOpen}
        onClose={() => setSupplierSearchOpen(false)}
        onSelect={handleSupplierSelect}
      />
    </form>
  );
}
