// resources/js/pages/Quotations/QuotationForm.jsx
import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";

import ProductSearchInput from "../../components/ProductSearchInput.jsx";
import BatchSearchInput from "../../components/BatchSearchInput.jsx";

import { recalcQuotationItem, recalcQuotationFooter } from "../../Formula/Quotation.js";
import { useTheme } from "@/context/ThemeContext";

const getContrastText = (hexColor) => {
  hexColor = (hexColor || "").replace("#", "");
  if (!hexColor || hexColor.length < 6) return "#ffffff";
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? "#1f2937" : "#ffffff";
};

const getButtonTextColor = (primaryColor, primaryHoverColor) => {
  return getContrastText(primaryHoverColor || primaryColor);
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

const to2 = (n) => Number(parseFloat(n || 0).toFixed(2));
const asISODate = (s) => {
  if (!s) return "";
  const d = new Date(s);
  if (!isNaN(d.getTime())) return d.toISOString().slice(0, 10);
  const m = /^(\d{2})[\/-](\d{2})[\/-](\d{4})$/.exec(String(s));
  if (m) return `${m[3]}-${m[2]}-${m[1]}`;
  return String(s);
};

const eqId = (a, b) => String(a ?? "") === String(b ?? "");
const zeroToEmpty = (v) => (v === 0 || v === "0" ? "" : v ?? "");

const normalizeFormLoaded = (f) => {
  const safe = { ...f };
  safe.date = safe.date ?? new Date().toISOString().split("T")[0];
  safe.remarks = safe.remarks ?? "";
  safe.discount_percentage = safe.discount_percentage ?? "";
  safe.discount_amount = safe.discount_amount ?? "";
  safe.tax_percentage = safe.tax_percentage ?? "";
  safe.tax_amount = safe.tax_amount ?? "";
  safe.item_discount = safe.item_discount ?? "";
  safe.gross_amount = safe.gross_amount ?? "";
  safe.total = safe.total ?? "";

  safe.items = Array.isArray(safe.items)
    ? safe.items.map((it, idx) => ({
        id: it?.id ?? `row_${Date.now()}_${idx}_${Math.random().toString(36).substr(2, 9)}`,
        line_type: it?.line_type ?? ((it?.manual_name ?? "") ? "manual" : "product"),
        product_id: it?.product_id ?? "",
        manual_name: it?.manual_name ?? "",
        quantity: it?.quantity ?? "",
        price: it?.price ?? "",
        item_discount_percentage: it?.item_discount_percentage ?? "",
        sub_total: it?.sub_total ?? "",
      }))
    : [];

  // Ensure at least one row
  if (!safe.items.length) {
    safe.items = [
      {
        id: `row_${Date.now()}_0_${Math.random().toString(36).substr(2, 9)}`,
        line_type: "product",
        product_id: "",
        manual_name: "",
        quantity: "",
        price: "",
        item_discount_percentage: "",
        sub_total: "",
      },
    ];
  }

  return safe;
};

export default function QuotationForm({ quotationId, onSuccess }) {
  const navigate = useNavigate();
  const { isDark, theme } = useTheme();

  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: "#3b82f6",
        primaryHover: "#2563eb",
        secondary: "#8b5cf6",
        secondaryHover: "#7c3aed",
        danger: "#ef4444",
        dangerHover: "#dc2626",
      };
    }
    return {
      primary: theme.primary_color || "#3b82f6",
      primaryHover: theme.primary_hover || "#2563eb",
      secondary: theme.secondary_color || "#8b5cf6",
      secondaryHover: theme.secondary_hover || "#7c3aed",
      danger: theme.danger_color || "#ef4444",
      dangerHover: theme.danger_hover || "#dc2626",
    };
  }, [theme]);

  const buttonStyle = theme?.button_style || "rounded";
  const getButtonClasses = useMemo(() => {
    const radiusMap = {
      rounded: "rounded-lg",
      outlined: "rounded-lg",
      soft: "rounded-xl",
    };
    const radiusClass = radiusMap[buttonStyle] || "rounded-lg";

    return {
      primary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
          color: "white",
          boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
        },
      },
      secondary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
          color: "white",
          boxShadow: `0 4px 14px 0 ${themeColors.secondary}40`,
        },
      },
      danger: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${themeColors.danger}, ${themeColors.dangerHover})`,
          color: "white",
          boxShadow: "0 4px 14px 0 rgba(239, 68, 68, 0.4)",
        },
      },
    };
  }, [buttonStyle, themeColors]);

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnDanger = getButtonClasses.danger;

  const generateRowId = () => `row_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

  const [form, setForm] = useState({
    customer_id: "",
    posted_number: "",
    date: new Date().toISOString().split("T")[0],
    remarks: "",
    discount_percentage: "",
    discount_amount: "",
    tax_percentage: "",
    tax_amount: "",
    item_discount: "",
    gross_amount: "",
    total: "",
    items: [
      {
        id: generateRowId(),
        line_type: "product",
        product_id: "",
        manual_name: "",
        quantity: "",
        price: "",
        item_discount_percentage: "",
        sub_total: "",
      },
    ],
  });

  const [customers, setCustomers] = useState([]);
  const [products, setProducts] = useState([]);
  const [batchesByProduct, setBatchesByProduct] = useState({}); // optional (for UI; quotation doesn't enforce stock)
  const [marginPct, setMarginPct] = useState("");

  const byIdsSupportedRef = useRef(true);
  const productDataCache = useRef({});

  // refs for grid navigation
  const productRefs = useRef([]);
  const batchRefs = useRef([]);
  const qtyRefs = useRef([]);
  const priceRefs = useRef([]);
  const discRefs = useRef([]);
  const manualRefs = useRef([]);
  const focusedOnce = useRef(false);
  const itemsScrollRef = useRef(null);

  const fetchCustomers = async () => {
    try {
      const res = await axios.get("/api/customers");
      let list = Array.isArray(res.data) ? res.data : [];
      list = list.slice().sort((a, b) => (a?.id ?? 0) - (b?.id ?? 0));
      setCustomers(list);
      if (!quotationId && !form.customer_id && list.length > 0) {
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
        if (err?.response?.status === 404) byIdsSupportedRef.current = false;
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

  // batches are purely for UI reuse; not validated at submit
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
      const list = raw.map(normalizeBatch).filter((x) => x.batch_number);
      setBatchesByProduct((m) => ({ ...m, [key]: list }));
      return list;
    } catch {
      setBatchesByProduct((m) => ({ ...m, [key]: [] }));
      return [];
    }
  };

  const fetchQuotation = async () => {
    const res = await axios.get(`/api/quotations/${quotationId}`);
    const loaded = normalizeFormLoaded(res.data);
    await ensureProductsForItems(loaded.items || []);
    setForm(loaded);
  };

  useEffect(() => {
    (async () => {
      await Promise.all([fetchCustomers(), fetchProducts()]);
      if (quotationId) await fetchQuotation();
      setTimeout(() => {
        if (!focusedOnce.current && !quotationId) {
          productRefs.current[0]?.querySelector?.("input")?.focus?.();
          focusedOnce.current = true;
        }
      }, 60);
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [quotationId]);

  // Recalc margin based on focused row product (optional)
  useEffect(() => {
    const firstProductRow = form.items.find((it) => it.line_type === "product" && it.product_id);
    if (!firstProductRow?.product_id) {
      setMarginPct("");
      return;
    }
    const pid = firstProductRow.product_id;
    const cached = productDataCache.current[pid];
    if (cached?.margin != null && cached?.margin !== "") {
      setMarginPct(sanitizeNumberInput(String(cached.margin), true));
    } else {
      setMarginPct("");
    }
  }, [form.items]);

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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form]);

  const handleHeaderChange = (e) => {
    const { name, value } = e.target;
    const negativeFields = new Set(["discount_percentage", "discount_amount", "tax_percentage", "tax_amount"]);
    const allowNegative = negativeFields.has(name);
    const decimalFields = new Set(["discount_percentage", "discount_amount", "tax_percentage", "tax_amount"]);

    const v = decimalFields.has(name)
      ? sanitizeNumberInput(value, true, allowNegative)
      : value;

    const tmp = { ...form, [name]: v };
    const next = recalcQuotationFooter(tmp, name);
    next[name] = v;
    setForm(next);
  };

  const handleItemChange = (index, field, rawValue) => {
    let value = rawValue;
    const allowNegative = field === "item_discount_percentage";

    if (field === "price" || field === "item_discount_percentage") {
      value = sanitizeNumberInput(value, true, allowNegative);
      if (field === "item_discount_percentage" && (value === "-" || value === "-.")) {
        return setForm((prev) => {
          const items = [...prev.items];
          items[index] = { ...items[index], item_discount_percentage: value };
          return { ...prev, items };
        });
      }
    } else if (field === "quantity") {
      value = sanitizeNumberInput(value, false, false);
    }

    setForm((prev) => {
      const items = [...prev.items];
      const updatedRow = recalcQuotationItem({ ...items[index], [field]: value }, field);
      if (field === "item_discount_percentage") updatedRow.item_discount_percentage = value;
      items[index] = updatedRow;
      return recalcQuotationFooter({ ...prev, items }, "items");
    });
  };

  const addRow = () => {
    setForm((prev) => {
      const nextItems = [
        ...prev.items,
        {
          id: generateRowId(),
          line_type: "product",
          product_id: "",
          manual_name: "",
          quantity: "",
          price: "",
          item_discount_percentage: "",
          sub_total: "",
        },
      ];
      return recalcQuotationFooter({ ...prev, items: nextItems }, "items");
    });
  };

  const removeRow = (i) => {
    if (form.items.length <= 1) return;
    const items = form.items.filter((_, idx) => idx !== i);
    setForm((prev) => recalcQuotationFooter({ ...prev, items }, "items"));
  };

  const resetRow = (rowIndex) => {
    setForm((prev) => {
      const items2 = [...prev.items];
      items2[rowIndex] = recalcQuotationItem(
        {
          line_type: "product",
          product_id: "",
          manual_name: "",
          quantity: "",
          price: "",
          item_discount_percentage: "",
          sub_total: "",
        },
        "revert"
      );
      return recalcQuotationFooter({ ...prev, items: items2 }, "items");
    });
  };

  const resolveSelectedProduct = (productIdOrObj) => {
    if (typeof productIdOrObj === "object") {
      return productIdOrObj?.id ?? productIdOrObj?.value ?? productIdOrObj?.product_id;
    }
    return productIdOrObj;
  };

  const handleProductSelect = async (rowIndex, productIdOrObj) => {
    const productId = resolveSelectedProduct(productIdOrObj);
    if (!productId && productId !== 0) return;

    // duplicate product check (only for product line type)
    const currentRowType = form.items[rowIndex]?.line_type;
    if (currentRowType === "product") {
      const dupIndex = form.items.findIndex(
        (row, idx) => idx !== rowIndex && row.line_type === "product" && eqId(row.product_id, productId)
      );
      if (dupIndex !== -1) {
        toast.error(`Product already used in row ${dupIndex + 1}. Each product can be added only once.`);
        resetRow(rowIndex);
        return;
      }
    }

    const selected =
      products.find((p) => eqId(p.id, productId)) ||
      (typeof productIdOrObj === "object" ? productIdOrObj : {}) ||
      {};

    let freshProductData = null;
    try {
      const { data } = await axios.get(`/api/products/${productId}`);
      freshProductData = data;
    } catch {}

    const packSize = freshProductData?.pack_size ?? selected?.pack_size ?? "";

    // For quotations: use unit_sale_price as default price
    const price = freshProductData?.unit_sale_price ?? selected?.unit_sale_price ?? "";

    const rawMargin = freshProductData?.margin ?? freshProductData?.margin_percentage ?? selected?.margin ?? selected?.margin_percentage ?? "";
    productDataCache.current[String(productId)] = { margin: rawMargin };

    setMarginPct(sanitizeNumberInput(String(rawMargin || ""), true));

    // Preserve existing quantity/discount for this row if re-selecting
    setForm((prev) => {
      const items = [...prev.items];
      const current = items[rowIndex] || {};
      const quantity = current.quantity === "" || current.quantity == null ? "1" : current.quantity;

      items[rowIndex] = recalcQuotationItem(
        {
          ...current,
          line_type: "product",
          product_id: productId,
          manual_name: "",
          quantity,
          price,
          item_discount_percentage: current.item_discount_percentage ?? "",
          sub_total: current.sub_total ?? "",
        },
        "product_select"
      );

      return recalcQuotationFooter({ ...prev, items }, "items");
    });

    // Optional: fetch batches for product (UI compatibility)
    if (packSize) {
      fetchBatches(productId).catch(() => {});
    }

    // ✅ After product selection, move focus to quantity AND select value
    setTimeout(() => {
      const qtyNode = qtyRefs.current[rowIndex];
      const input = qtyNode?.querySelector?.("input") || qtyNode;
      if (input?.focus) input.focus();
      if (input?.select) {
        // Ensure select happens after focus.
        requestAnimationFrame(() => input.select());
      }
    }, 0);
  };

  const handleManualNameChange = (rowIndex, value) => {
    setForm((prev) => {
      const items = [...prev.items];
      items[rowIndex] = { ...items[rowIndex], line_type: "manual", manual_name: value, product_id: "" };
      return recalcQuotationFooter({ ...prev, items }, "items");
    });
  };

  const handleSubmit = async (e) => {
    e?.preventDefault?.();

    if (!form.customer_id) return toast.error("Customer is required");
    if (!form.date) return toast.error("Date is required");

    for (let i = 0; i < form.items.length; i++) {
      const it = form.items[i];
      if (it.line_type === "product") {
        if (!it.product_id) return toast.error(`Row ${i + 1}: select a product`);
      } else {
        if (!it.manual_name || !String(it.manual_name).trim()) {
          return toast.error(`Row ${i + 1}: enter manual product name`);
        }
      }

      if (it.quantity === "" || it.quantity == null) return toast.error(`Row ${i + 1}: enter quantity`);
      const qn = Number(it.quantity);
      if (!Number.isFinite(qn) || qn < 1) return toast.error(`Row ${i + 1}: quantity must be >= 1`);

      const pn = Number(it.price);
      if (!Number.isFinite(pn) || pn < 0) return toast.error(`Row ${i + 1}: price is invalid`);

      // normalize discount pct default to 0
      if (it.item_discount_percentage === "") {
        // will be recalculated by footer; still validate later
      }
    }

    const payload = {
      customer_id: form.customer_id,
      date: form.date,
      remarks: form.remarks ?? null,
      discount_percentage:
        form.discount_percentage === "-" || form.discount_percentage === "-." ? "-0" : form.discount_percentage,
      discount_amount: form.discount_amount === "-" || form.discount_amount === "-." ? "-0" : form.discount_amount,
      tax_percentage: form.tax_percentage === "-" || form.tax_percentage === "-." ? "-0" : form.tax_percentage,
      tax_amount: form.tax_amount === "-" || form.tax_amount === "-." ? "-0" : form.tax_amount,
      gross_amount: form.gross_amount ?? 0,
      total: form.total ?? 0,
      items: form.items.map((it) => {
        const isNewItem = String(it.id || "").startsWith("row_");
        // QuotationItem model does not need id in payload; backend validates shape
        const quantity = it.quantity;

        return {
          manual_product: it.line_type === "manual",
          ...(it.line_type === "product" ? { product_id: parseInt(it.product_id, 10) } : {}),
          ...(it.line_type === "manual" ? { manual_name: it.manual_name } : {}),
          quantity: Number(quantity),
          price: Number(it.price ?? 0),
          item_discount_percentage:
            it.item_discount_percentage === "-" || it.item_discount_percentage === "-." || it.item_discount_percentage === ""
              ? -0
              : Number(it.item_discount_percentage),
          sub_total: it.sub_total ?? 0,
          ...(isNewItem ? {} : {}),
        };
      }),
    };

    try {
      const res = quotationId
        ? await axios.put(`/api/quotations/${quotationId}`, payload)
        : await axios.post("/api/quotations", payload);

      const id = res?.data?.id ?? quotationId;
      toast.success(quotationId ? "Quotation updated" : "Quotation created");
      const cbRes = res?.data || { id };
      onSuccess?.(cbRes);
      if (!onSuccess) {
        navigate(`/quotations/${id}`);
      }
    } catch (err) {
      const errorMsg =
        err?.response?.data?.message ||
        (err?.response?.data?.errors ? JSON.stringify(err.response.data.errors) : "Failed to save quotation");
      toast.error(errorMsg);
    }
  };

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

    if (targetTop < viewTop + headH + 4) container.scrollTop = Math.max(targetTop - headH - 4, 0);
    else if (targetBottom > viewBottom - 4)
      container.scrollTop = targetBottom - container.clientHeight + 4;
  };

  const COLS = ["product", "quantity", "price", "disc"];
  const focusCell = (row, col) => {
    const map = {
      product: productRefs,
      quantity: qtyRefs,
      price: priceRefs,
      disc: discRefs,
    };

    const ref = map[col]?.current?.[row];
    if (!ref) return;
    const input = ref.querySelector?.("input") || ref;
    if (input?.focus) {
      input.focus();
      input.select?.();
    }
    requestAnimationFrame(() => scrollTargetIntoItemsView(input || ref));
  };

  const moveSameCol = (row, col, dir) => {
    const lastIdx = form.items.length - 1;
    if (dir === 1) {
      if (row === lastIdx) {
        addRow();
        setTimeout(() => focusCell(row + 1, col === "quantity" || col === "disc" ? "product" : col), 40);
      } else focusCell(row + 1, col);
    } else if (row > 0) focusCell(row - 1, col);
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
      if (i > 0) focusCell(row, COLS[i - 1]);
      else if (i === 0 && row > 0) focusCell(row - 1, COLS[COLS.length - 1]);
      return;
    }

    if (col === "quantity" && (e.key === "ArrowDown" || e.key === "ArrowUp")) return;

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
          focusCell(row, "price");
        } else if (col === "price") {
          focusCell(row, "disc");
        } else {
          moveNextCol(row, col);
        }
        break;
      default:
        break;
    }
  };

  return (
    <form
      className={`h-[calc(90vh-100px)] flex flex-col ${isDark ? "bg-slate-900" : "bg-white"}`}
      autoComplete="off"
      autoCapitalize="off"
      autoCorrect="off"
      spellCheck={false}
      onSubmit={handleSubmit}
    >
      <div className={`shrink-0 sticky top-0 z-20 border-b ${isDark ? "bg-slate-800 border-slate-700" : "bg-white border-gray-200"}`}>
        <div className="px-2 py-1 flex items-center gap-2">
          <div className={`text-xs font-semibold ${isDark ? "text-slate-200" : "text-gray-800"}`}>Quotation</div>

          <div className="ml-auto flex items-center gap-2">
            <div
              className={`mr-2 px-2 py-1 rounded border ${isDark ? "bg-slate-700 border-slate-600" : "bg-gray-50 border-gray-200"}`}
            >
              <span className={`text-[10px] font-semibold ${isDark ? "text-blue-300" : "text-blue-700"}`}>QTN</span>
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
              {quotationId ? "Update (Alt+S)" : "Create (Alt+S)"}
            </button>
          </div>
        </div>

        <div className="px-2 pb-1 grid grid-cols-12 gap-1 text-[11px]">
          <div className="col-span-1">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Posted #</label>
            <input
              type="text"
              readOnly
              value={form.posted_number || ""}
              placeholder={quotationId ? "" : "Auto on Save"}
              autoComplete="off"
              className={`w-full h-7 border-2 rounded px-1 ${
                isDark
                  ? "border-slate-600 bg-slate-700 text-slate-200 placeholder-slate-500"
                  : "border-black bg-gray-100 text-gray-800"
              }`}
            />
          </div>

          <div className="col-span-1">
            <label className={`block text-[9px] mb-0.5 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Date</label>
            <input
              type="date"
              name="date"
              value={form.date ?? ""}
              onChange={(e) => handleHeaderChange(e)}
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
              options={customers.map((c) => ({ value: c.id, label: c.name }))}
              value={
                customers
                  .map((c) => ({ value: c.id, label: c.name }))
                  .find((s) => s.value === form.customer_id) || null
              }
              onChange={(val) => setForm((prev) => ({ ...prev, customer_id: val?.value || "" }))}
              className="text-[11px]"
              name="customer_select"
              inputId="customer_select"
              styles={{
                control: (base) => ({
                  ...base,
                  minHeight: "28px",
                  height: "28px",
                  fontSize: "11px",
                  borderColor: isDark ? "rgba(71,85,105,0.8)" : "rgba(0,0,0,0.8)",
                  backgroundColor: isDark ? "rgba(51,65,85,0.7)" : "rgba(255,255,255,0.7)",
                  backdropFilter: "blur(6px)",
                  boxShadow: isDark ? "0 1px 2px rgba(0,0,0,0.2)" : "none",
                  borderRadius: 6,
                  color: isDark ? "#f1f5f9" : "#111827",
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
                  color: isDark ? "#f1f5f9" : "#111827",
                }),
                singleValue: (base) => ({
                  ...base,
                  color: isDark ? "#f1f5f9" : "#111827",
                }),
                placeholder: (base) => ({
                  ...base,
                  color: isDark ? "#64748b" : "#9ca3af",
                }),
                menu: (base) => ({
                  ...base,
                  fontSize: "12px",
                  borderRadius: 8,
                  overflow: "hidden",
                  backgroundColor: isDark ? "rgba(30,41,59,0.95)" : "rgba(255,255,255,0.95)",
                  backdropFilter: "blur(10px)",
                  boxShadow: isDark ? "0 10px 30px -10px rgba(0,0,0,0.4)" : "0 10px 30px -10px rgba(30,64,175,0.18)",
                  border: isDark ? "1px solid rgba(71,85,105,0.5)" : "none",
                }),
                option: (base, state) => ({
                  ...base,
                  backgroundColor: isDark
                    ? state.isFocused
                      ? "rgba(71,85,105,1)"
                      : "rgba(51,65,85,1)"
                    : state.isFocused
                      ? "rgba(241,245,249,1)"
                      : "rgba(255,255,255,1)",
                  color: isDark ? "#f1f5f9" : "#111827",
                  cursor: "pointer",
                }),
                menuPortal: (base) => ({ ...base, zIndex: 9999 }),
              }}
              isSearchable
            />
          </div>

          <div className="col-span-4">
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

      <div className={`flex-1 grid grid-cols-[1fr_240px] gap-2 px-2 py-2 overflow-hidden ${isDark ? "bg-slate-900" : "bg-white"}`}>
        <div className="flex flex-col min-h-0">
          <div className={`text-[11px] font-semibold mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Items</div>
          <div
            ref={itemsScrollRef}
            className={`flex-1 overflow-auto border-2 rounded relative ${isDark ? "border-slate-700 bg-slate-800" : "border-gray-200 bg-white"}`}
          >
            <table className="w-full text-[11px] table-fixed border-collapse" autoComplete="off">
              <thead
                className={`sticky top-0 z-20 ${isDark ? "bg-slate-800/90 backdrop-blur-sm border-slate-700" : "bg-white/80 backdrop-blur-sm border-gray-200/70"} border-b`}
              >
                <tr className="[&>th]:py-1 [&>th]:px-1 [&>th]:text-left">
                  <th className={`w-7 text-center ${isDark ? "text-rose-400" : "text-red-600"}`}>DEL</th>
                  <th className={`w-7 text-center ${isDark ? "text-slate-400" : "text-gray-600"}`}>#</th>
                  <th className={`w-[190px] ${isDark ? "text-slate-400" : "text-gray-600"}`}>Line</th>
                  <th className={`w-24 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Qty</th>
                  <th className={`w-24 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Price</th>
                  <th className={`w-20 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Disc%</th>
                  <th className={`w-28 ${isDark ? "text-slate-400" : "text-gray-600"}`}>Sub</th>
                  <th className="w-7 text-center">+</th>
                </tr>
              </thead>

              <tbody className="[&>tr>td]:py-1 [&>tr>td]:px-0.2">
                {form.items.map((it, i) => (
                  <tr
                    key={it.id}
                    className={`border-b ${isDark ? "border-slate-700 hover:bg-slate-700/50" : "border-gray-100 hover:bg-gray-50"}`}
                  >
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
                      <div className="flex items-center gap-1">
                        <label className="flex items-center gap-1 text-[10px]">
                          <input
                            type="radio"
                            name={`line_type_${i}`}
                            checked={it.line_type === "product"}
                            onChange={() =>
                              setForm((prev) => {
                                const items = [...prev.items];
                                items[i] = { ...items[i], line_type: "product", product_id: "", manual_name: "" };
                                return recalcQuotationFooter({ ...prev, items }, "items");
                              })
                            }
                          />
                          <span>Product</span>
                        </label>

                        <label className="flex items-center gap-1 text-[10px]">
                          <input
                            type="radio"
                            name={`line_type_${i}`}
                            checked={it.line_type === "manual"}
                            onChange={() =>
                              setForm((prev) => {
                                const items = [...prev.items];
                                items[i] = { ...items[i], line_type: "manual", product_id: "", manual_name: "" };
                                return recalcQuotationFooter({ ...prev, items }, "items");
                              })
                            }
                          />
                          <span>Manual</span>
                        </label>
                      </div>

                      {it.line_type === "product" ? (
                        <div ref={(el) => (productRefs.current[i] = el)} onFocus={() => {}}>
                          <ProductSearchInput
                            value={products.find((p) => eqId(p.id, it.product_id)) || it.product_id}
                            onChange={(val) => handleProductSelect(i, val)}
                            onKeyDown={(e) => onKeyNav(e, i, "product")}
                            products={products}
                            onRefreshProducts={fetchProducts}
                            inputProps={{ autoComplete: "off", autoCapitalize: "off", autoCorrect: "off", spellCheck: false }}
                          />
                        </div>
                      ) : (
                        <input
                          ref={(el) => (manualRefs.current[i] = el)}
                          type="text"
                          value={it.manual_name ?? ""}
                          onChange={(e) => handleManualNameChange(i, e.target.value)}
                          placeholder="Manual product name"
                          autoComplete="off"
                          onKeyDown={(e) => {
                            if (e.key === "Enter") {
                              e.preventDefault();
                              // focus qty
                              setTimeout(() => qtyRefs.current[i]?.focus?.(), 0);
                            }
                          }}
                          className={`w-full h-6 border rounded px-1 text-[11px] ${
                            isDark
                              ? "bg-slate-700 border-slate-600 text-slate-200"
                              : "bg-white border-gray-300 text-gray-800"
                          }`}
                        />
                      )}
                    </td>

                    <td className="text-center">
                      <input
                        ref={(el) => (qtyRefs.current[i] = el)}
                        type="text"
                        inputMode="numeric"
                        value={zeroToEmpty(it.quantity)}
                        onChange={(e) => handleItemChange(i, "quantity", e.target.value)}
                        autoComplete="off"
                        className={`w-full h-6 border rounded px-1 text-center ${
                          isDark
                            ? "bg-slate-700 border-slate-600 text-slate-200"
                            : "bg-white border-gray-300 text-gray-800"
                        }`}
                        onKeyDown={(e) => onKeyNav(e, i, "quantity")}
                        onFocus={(e) => {
                          e.target.select();
                        }}
                      />
                    </td>

                    <td className="text-center">
                      <input
                        ref={(el) => (priceRefs.current[i] = el)}
                        type="text"
                        inputMode="decimal"
                        value={to2(it.price ?? "")}
                        onChange={(e) => handleItemChange(i, "price", e.target.value)}
                        autoComplete="off"
                        className={`w-full h-6 border rounded px-1 text-center ${
                          isDark
                            ? "bg-slate-700 border-slate-600 text-slate-200"
                            : "bg-white border-gray-300 text-gray-800"
                        }`}
                        onKeyDown={(e) => onKeyNav(e, i, "price")}
                        onFocus={(e) => e.target.select()}
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
                            if (Number.isFinite(num))
                              handleItemChange(i, "item_discount_percentage", num.toFixed(2));
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
              </div>

              <div className="pt-2">
                <button
                  type="button"
                  onClick={handleSubmit}
                  className={`w-full h-9 rounded text-white text-[12px] font-semibold transition-all duration-200 ${btnPrimary.className}`}
                  style={btnPrimary.style}
                >
                  {quotationId ? "Update Quotation" : "Create Quotation"}
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </form>
  );
}

