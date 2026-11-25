import { useCallback, useEffect, useState, useRef, useMemo } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import { usePermissions } from "@/api/usePermissions";
import { useCurrency } from "@/context/CurrencyContext.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";
import ProductSearchInput from "../components/ProductSearchInput";
import {
  ClipboardDocumentListIcon,
  PlusIcon,
  TrashIcon,
  EyeIcon,
  PencilSquareIcon,
  ArrowDownTrayIcon,
  DocumentTextIcon,
  BuildingStorefrontIcon,
  MagnifyingGlassIcon,
  ArrowsUpDownIcon,
  QrCodeIcon,
  ShareIcon,
  CalendarIcon,
  ClockIcon,
  TruckIcon,
  BanknotesIcon,
  CubeIcon,
  ExclamationTriangleIcon,
  CheckCircleIcon,
  XMarkIcon,
  ArrowPathIcon,
  CalculatorIcon,
} from "@heroicons/react/24/outline";
import SupplierSearch from "../components/SupplierSearch";
import ShareDocumentModal from "../components/ShareDocumentModal";
import ModernDatePicker from "@/components/ModernDatePicker.jsx";
import { printSlipUrl } from "@/utils/printHelper.js";

const blank = () => ({ date: new Date().toLocaleDateString('en-CA'), expected_received_date: '', notes: '', reference: '', supplier_id: '', items: [] });
const orderRow = (row) => ({ ...row,
  ordered_packs: Number(row.ordered_quantity || row.pack_size || 1) / Math.max(1, Number(row.pack_size || 1)),
  pack_purchase_price: Number(row.unit_purchase_price || 0) * Math.max(1, Number(row.pack_size || 1)),
  unit_bonus: Number(row.unit_bonus || 0),
  pack_bonus: Number(row.unit_bonus || 0) / Math.max(1, Number(row.pack_size || 1)),
});
const entryFields = ['ordered_packs', 'pack_purchase_price', 'pack_bonus', 'unit_bonus', 'item_discount_percentage'];
const newItem = () => ({ product_id: '', ordered_packs: 1, pack_size: 1, pack_purchase_price: 0, pack_bonus: 0, unit_bonus: 0, item_discount_percentage: 0 });
const detailLabels = ['Product', 'Brand', 'Pack Size', 'Order Packs', 'Received Units', 'Remaining Units', 'Pack Purchase Price', 'Pack Bonus', 'Unit Bonus', 'Discount %', 'Total'];
const itemLabels = { pack_size: 'Pack Size', ordered_packs: 'Order Packs', pack_purchase_price: 'Pack Purchase Price', pack_bonus: 'Pack Bonus', unit_bonus: 'Unit Bonus', item_discount_percentage: 'Discount %' };
const statusLabel = (s) => ({ ordered: 'Ordered', partially_received: 'Partially Received', received: 'Received', cancelled: 'Cancelled' }[s] || s);

const formatDate = (dateStr) => {
  if (!dateStr) return "—";
  try {
    const parts = String(dateStr).split('-');
    if (parts.length < 3) return dateStr;
    const y = parseInt(parts[0], 10);
    const m = parseInt(parts[1], 10) - 1;
    const d = parseInt(parts[2], 10);
    const date = new Date(y, m, d);
    if (isNaN(date.getTime())) return dateStr;
    return date.toLocaleDateString("en-US", { day: "numeric", month: "short", year: "numeric" });
  } catch {
    return dateStr;
  }
};

export const formatPoNumber = (val) => {
  if (!val) return '';
  const str = String(val).trim();
  if (/^PO-\d+$/i.test(str)) {
    return str.replace(/^PO-0*(\d+)$/i, 'PO-$1');
  }
  if (/^\d+$/.test(str)) {
    return `PO-${parseInt(str, 10)}`;
  }
  return str.replace(/^PO-0*(\d+)/i, 'PO-$1');
};

const getDeliveryUrgency = (expectedDate, status) => {
  if (!expectedDate) return { label: "Not scheduled", type: "none", days: 0 };
  if (status === "received") return { label: "Delivered", type: "received", days: 0 };
  if (status === "cancelled") return { label: "Cancelled", type: "cancelled", days: 0 };

  const today = new Date();
  today.setHours(0, 0, 0, 0);

  const parts = String(expectedDate).split('-');
  if (parts.length < 3) return { label: expectedDate, type: "none", days: 0 };
  const target = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
  target.setHours(0, 0, 0, 0);

  const diffTime = target.getTime() - today.getTime();
  const diffDays = Math.round(diffTime / (1000 * 60 * 60 * 24));

  if (diffDays < 0) {
    const overdueDays = Math.abs(diffDays);
    return {
      label: overdueDays === 1 ? "Overdue (1 day)" : `Overdue (${overdueDays} days)`,
      shortLabel: `${overdueDays}d overdue`,
      type: "overdue",
      days: diffDays,
      overdueDays,
    };
  }
  if (diffDays === 0) {
    return {
      label: "Due Today",
      shortLabel: "Today",
      type: "today",
      days: 0,
    };
  }
  if (diffDays === 1) {
    return {
      label: "Due Tomorrow",
      shortLabel: "Tomorrow",
      type: "upcoming",
      days: 1,
    };
  }
  if (diffDays <= 3) {
    return {
      label: `In ${diffDays} days`,
      shortLabel: `In ${diffDays}d`,
      type: "upcoming",
      days: diffDays,
    };
  }
  return {
    label: `In ${diffDays} days`,
    shortLabel: `${diffDays}d`,
    type: "scheduled",
    days: diffDays,
  };
};

const sortOptions = [
  ['delivery_asc', 'Delivery Date (Soonest first)'],
  ['delivery_desc', 'Delivery Date (Latest first)'],
  ['newest', 'PO Number (Newest first)'],
  ['oldest', 'PO Number (Oldest first)'],
  ['date_desc', 'Order Date (Newest first)'],
  ['date_asc', 'Order Date (Oldest first)'],
  ['supplier_asc', 'Supplier (A–Z)'],
  ['supplier_desc', 'Supplier (Z–A)'],
  ['total_desc', 'Total Amount (High–Low)'],
  ['total_asc', 'Total Amount (Low–High)'],
  ['packs_desc', 'Ordered Packs (High–Low)'],
  ['packs_asc', 'Ordered Packs (Low–High)'],
  ['status_asc', 'Status (A–Z)'],
  ['status_desc', 'Status (Z–A)'],
];
const headerSorts = {
  'PO Number': ['newest', 'oldest'],
  'Order Date': ['date_desc', 'date_asc'],
  'Expected Delivery': ['delivery_asc', 'delivery_desc'],
  'Supplier': ['supplier_asc', 'supplier_desc'],
  'Ordered Packs': ['packs_desc', 'packs_asc'],
  'Total Amount': ['total_desc', 'total_asc'],
  'Status': ['status_asc', 'status_desc'],
};
const errorMessage = (e) => Object.values(e.response?.data?.errors || {}).flat().join(' ') || e.response?.data?.message || 'Unable to complete this action. Please try again.';

export default function PurchaseOrders() {
  const { id } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const { loading: permissionsLoading, has } = usePermissions();
  const { formatCurrency } = useCurrency();
  const { isDark } = useTheme();
  const creating = location.pathname.endsWith('/create');
  const editing = location.pathname.endsWith('/edit');
  const [form, setForm] = useState(blank);
  const [order, setOrder] = useState(null);
  const [listing, setListing] = useState(null);
  const [page, setPage] = useState(1);
  const [supplierQuery, setSupplierQuery] = useState('');
  const [supplierFilter, setSupplierFilter] = useState('');
  const [sort, setSort] = useState('newest');
  const [urgencyFilter, setUrgencyFilter] = useState('all');
  const [selectedDeliveryDate, setSelectedDeliveryDate] = useState('');
  const todayIso = useMemo(() => new Date().toLocaleDateString('en-CA'), []);
  const tomorrowIso = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() + 1);
    return d.toLocaleDateString('en-CA');
  }, []);
  const threeDaysIso = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() + 3);
    return d.toLocaleDateString('en-CA');
  }, []);
  const sevenDaysIso = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() + 7);
    return d.toLocaleDateString('en-CA');
  }, []);

  const supplyDayOptions = useMemo(() => [
    { value: '', label: 'All Supply Days' },
    { value: todayIso, label: "Today's Supply" },
    { value: tomorrowIso, label: "Tomorrow's Supply" },
    { value: threeDaysIso, label: 'In 3 Days' },
    { value: sevenDaysIso, label: 'In 7 Days' },
    { value: 'custom', label: 'Pick a Date…' },
  ], [todayIso, tomorrowIso, threeDaysIso, sevenDaysIso]);

  const currentSupplyDayOption = useMemo(() => {
    if (!selectedDeliveryDate) return supplyDayOptions[0];
    const match = supplyDayOptions.find((opt) => opt.value === selectedDeliveryDate);
    if (match) return match;
    return { value: selectedDeliveryDate, label: selectedDeliveryDate };
  }, [selectedDeliveryDate, supplyDayOptions]);

  const supplySelectStyles = useMemo(() => ({
    control: (base, state) => ({
      ...base,
      minHeight: 36,
      height: 36,
      fontSize: "12px",
      fontWeight: 500,
      borderRadius: "var(--btn-radius, 8px)",
      borderColor: state.isFocused
        ? "var(--workspace-accent, #3b82f6)"
        : isDark
        ? "rgba(71,85,105,0.7)"
        : "rgba(203,213,225,0.9)",
      backgroundColor: isDark ? "rgba(30,41,59,0.85)" : "var(--workspace-reading-fill, #f8fafc)",
      boxShadow: state.isFocused
        ? "0 0 0 2px color-mix(in srgb, var(--workspace-accent) 25%, transparent)"
        : "none",
      cursor: "pointer",
      "&:hover": {
        borderColor: "var(--workspace-accent, #3b82f6)",
      },
    }),
    valueContainer: (base) => ({
      ...base,
      height: 36,
      padding: "0 10px",
      display: "flex",
      alignItems: "center",
    }),
    indicatorsContainer: (base) => ({ ...base, height: 36 }),
    indicatorSeparator: () => ({ display: "none" }),
    dropdownIndicator: (base, state) => ({
      ...base,
      padding: "0 8px",
      color: state.isFocused ? "var(--workspace-accent, #3b82f6)" : isDark ? "#94a3b8" : "#64748b",
    }),
    input: (base) => ({ ...base, margin: 0, padding: 0, color: isDark ? "#f1f5f9" : "#0f172a" }),
    menuPortal: (base) => ({ ...base, zIndex: 9999 }),
    menu: (base) => ({
      ...base,
      borderRadius: "var(--btn-radius, 10px)",
      overflow: "hidden",
      backgroundColor: isDark ? "#1e293b" : "#ffffff",
      boxShadow: isDark
        ? "0 10px 25px -5px rgba(0,0,0,0.5)"
        : "0 10px 25px -5px rgba(0,0,0,0.1)",
      border: isDark ? "1px solid rgba(71,85,105,0.5)" : "1px solid rgba(226,232,240,1)",
      minWidth: 165,
    }),
    option: (base, state) => ({
      ...base,
      fontSize: "12px",
      fontWeight: state.isSelected ? 600 : 500,
      padding: "7px 12px",
      backgroundColor: state.isSelected
        ? "var(--workspace-accent, #3b82f6)"
        : state.isFocused
        ? isDark
          ? "rgba(51, 65, 85, 0.8)"
          : "rgba(241, 245, 249, 1)"
        : "transparent",
      color: state.isSelected ? "#ffffff" : isDark ? "#f1f5f9" : "#0f172a",
      cursor: "pointer",
    }),
    singleValue: (base) => ({
      ...base,
      color: isDark ? "#f1f5f9" : "#0f172a",
      fontWeight: 600,
    }),
  }), [isDark]);
  const [products, setProducts] = useState([]);
  const [supplier, setSupplier] = useState(null);
  const [supplierOpen, setSupplierOpen] = useState(false);
  const [sharingPoId, setSharingPoId] = useState(null);
  const [loading, setLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const inFlight = useRef(false);
  const fieldRefs = useRef({});
  const productRefs = useRef({});
  const itemRowRefs = useRef({});
  const itemsContainerRef = useRef(null);
  const scrollToItem = (index) => {
    const row = itemRowRefs.current[index];
    const container = itemsContainerRef.current;
    if (!row || !container) return;
    if (container.scrollHeight <= container.clientHeight) {
      row.scrollIntoView({ block: 'nearest', inline: 'nearest' });
      return;
    }
    const bounds = container.getBoundingClientRect();
    const rowBounds = row.getBoundingClientRect();
    const headerHeight = container.querySelector('thead')?.getBoundingClientRect().height || 0;
    const top = bounds.top + headerHeight + 4;
    const bottom = bounds.bottom - 4;
    if (rowBounds.top < top) container.scrollTop += rowBounds.top - top;
    else if (rowBounds.bottom > bottom) container.scrollTop += rowBounds.bottom - bottom;
  };
  const [pendingFocus, setPendingFocus] = useState(null);
  const entryMode = creating || editing;
  useEffect(() => {
    const timer = setTimeout(() => {
      setSupplierFilter(prev => {
        const next = supplierQuery.trim();
        if (prev !== next) setPage(1);
        return next;
      });
    }, supplierQuery.trim() ? 250 : 0);
    return () => clearTimeout(timer);
  }, [supplierQuery]);
  const handlePrintOrder = useCallback(async (orderId) => {
    if (!orderId) return;
    try {
      const { data } = await axios.get(`/api/purchase-orders/${orderId}/print-url`);
      if (!data?.url && !data?.html) {
        toast.error("Failed to generate print link.");
        return;
      }
      await printSlipUrl(data);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to generate print link.");
    }
  }, []);

  useEffect(() => {
    if (!order || editing) return;
    const onKeyDown = (e) => {
      if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey) return;
      const key = (e.key || '').toLowerCase();
      if (key === 'p' || e.code === 'KeyP') {
        e.preventDefault();
        handlePrintOrder(order.id);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [order, editing]);

  useEffect(() => {
    if (!pendingFocus || loading) return;
    const { index, field } = pendingFocus;
    if (field === 'product') productRefs.current[index]?.focus();
    else {
      const input = fieldRefs.current[`${index}:${field}`];
      input?.focus({ preventScroll: true }); input?.select();
    }
    scrollToItem(index);
    setPendingFocus(null);
  }, [pendingFocus, loading, form.items.length]);
  const addItem = () => {
    if (inFlight.current) return;
    setForm(prev => ({ ...prev, items: [...prev.items, newItem()] }));
    setPendingFocus({ index: form.items.length, field: 'product' });
  };
  const navigateItem = (event, index, field) => {
    if (!['Enter', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    if (event.repeat) { event.preventDefault(); return; }
    event.preventDefault(); event.stopPropagation();
    if (event.key === 'Enter') {
      const nextField = field === 'product' ? entryFields[0] : entryFields[entryFields.indexOf(field) + 1];
      if (nextField) setPendingFocus({ index, field: nextField });
      else if (index < form.items.length - 1) setPendingFocus({ index: index + 1, field: 'product' });
      else addItem();
    } else if (event.key === 'ArrowUp' && index > 0) setPendingFocus({ index: index - 1, field });
    else if (event.key === 'ArrowDown') {
      if (index < form.items.length - 1) setPendingFocus({ index: index + 1, field });
      else addItem();
    }
  };

  useEffect(() => {
    if (permissionsLoading || !has('purchase-order.view')) return;
    let alive = true;
    setLoading(true); setError(''); setOrder(null);
    (async () => {
      try {
        if (creating) {
          const incomingSupplier = location.state?.supplier?.id ? location.state.supplier : null;
          setForm({
            ...blank(),
            supplier_id: incomingSupplier ? incomingSupplier.id : '',
            items: location.state?.items?.length ? location.state.items.map(orderRow) : [newItem()],
          });
          setSupplier(incomingSupplier);
          setProducts((location.state?.items || []).map(item => item.product).filter(Boolean));
        } else if (id) {
          const { data } = await axios.get(`/api/purchase-orders/${id}`);
          if (!alive) return;
          setOrder(data); setForm({ ...data, items: data.items.map(orderRow) }); setSupplier(data.supplier);
          setProducts(data.items.map(i => i.product));
        } else {
          const { data } = await axios.get('/api/purchase-orders', {
            params: {
              page,
              supplier: supplierFilter || undefined,
              sort,
              delivery_date: selectedDeliveryDate || undefined,
            }
          });
          if (alive) setListing(data);
        }
      } catch (e) { if (alive) setError(errorMessage(e)); }
      finally { if (alive) setLoading(false); }
    })();
    return () => { alive = false; };
  }, [id, creating, editing, page, supplierFilter, sort, selectedDeliveryDate, permissionsLoading, has]);

  useEffect(() => {
    if (permissionsLoading || !has('purchase-order.generate')) return;
    const onShortcut = (event) => {
      if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'n') {
        event.preventDefault();
        if (!inFlight.current) navigate('/purchase-orders/forecast');
      }
    };
    window.addEventListener('keydown', onShortcut);
    return () => window.removeEventListener('keydown', onShortcut);
  }, [permissionsLoading, has, navigate]);

  const deleteOrder = async (po) => {
    const poNum = formatPoNumber(po.number || po.id);
    if (inFlight.current || !window.confirm(`Delete Purchase Order ${poNum}? This cannot be undone.`)) return;
    inFlight.current = true; setBusy(true); setError('');
    try {
      await axios.delete(`/api/purchase-orders/${po.id}`);
      toast.success(`Purchase Order ${poNum} deleted`);
      const nextPage = listing.data.length === 1 && page > 1 ? page - 1 : page;
      if (nextPage !== page) setPage(nextPage);
      else {
        const { data } = await axios.get('/api/purchase-orders', { params: { page, supplier: supplierFilter || undefined, sort } });
        setListing(data);
      }
    } catch (e) { setError(errorMessage(e)); }
    finally { inFlight.current = false; setBusy(false); }
  };

  const refreshProducts = useCallback(async (q = "") => {
    try {
    const { data } = await axios.get('/api/products/search', { params: { q, limit: 30 } });
    setProducts(prev => [...new Map([...prev, ...data].map(p => [p.id, p])).values()]);
    } catch (e) { setError(errorMessage(e)); }
  }, []);
  const changeItem = (index, field, value) => setForm(prev => ({ ...prev, items: prev.items.map((row, i) => {
    if (i !== index) return row;
    const next = { ...row, [field]: value };
    if (field === 'pack_bonus') next.unit_bonus = Number(value || 0) * row.pack_size;
    if (field === 'unit_bonus') next.pack_bonus = Number(value || 0) / row.pack_size;
    return next;
  }) }));
  // Arrange lines in the order last saved on this supplier's Purchase Order based invoice.
  const applySupplierOrder = async (chosen = supplier, { quiet = false } = {}) => {
    if (!chosen?.id) return;
    try {
      const { data } = await axios.get(`/api/purchase-orders/supplier-sort-order/${chosen.id}`);
      const rank = new Map((data?.product_ids || []).map((productId, position) => [String(productId), position]));
      if (!rank.size) { if (!quiet) toast(`No saved product order for ${chosen.name} yet. Save a Purchase Invoice from one of its orders first.`); return; }
      const position = (row, index) => (row.product_id && rank.has(String(row.product_id)) ? rank.get(String(row.product_id)) : rank.size + index);
      setForm(prev => ({ ...prev, items: prev.items.map((row, index) => ({ row, key: position(row, index) })).sort((a, b) => a.key - b.key).map(entry => entry.row) }));
      toast.success(`Products arranged in ${chosen.name}'s saved order`);
    } catch { /* sorting is a convenience; never block order entry */ }
  };
  const save = async (chosen = supplier) => {
    if (inFlight.current) return;
    if (!form.expected_received_date || form.expected_received_date < form.date) {
      setError('Please select an expected receiving date on or after the order date.'); return;
    }
    if (!form.items.length || form.items.some(i => !i.product_id || (!Number.isInteger(Number(i.ordered_packs)) || Number(i.ordered_packs) <= 0))) {
      setError('Add at least one product and enter a positive whole-pack quantity for every item.'); return;
    }
    if (form.items.some(row => !Number.isInteger(Number(row.unit_bonus || 0)) || Number(row.unit_bonus || 0) < 0)) {
      setError('Bonus must equal a nonnegative whole number of units.'); return;
    }
    if (!chosen?.id) { setError('Please select a supplier before saving the Purchase Order.'); return; }
    inFlight.current = true; setBusy(true); setError('');
    try {
      const payload = { ...form, supplier_id: chosen.id, items: form.items.map(row => ({
        product_id: row.product_id, pack_size: row.pack_size, ordered_packs: row.ordered_packs,
        pack_purchase_price: row.pack_purchase_price, unit_bonus: row.unit_bonus,
        item_discount_percentage: row.item_discount_percentage,
      })) };
      const { data } = editing ? await axios.put(`/api/purchase-orders/${id}`, payload) : await axios.post('/api/purchase-orders', payload);
      toast.success(`Purchase Order ${formatPoNumber(data.number || data.id)} saved`);
      navigate(`/purchase-orders/${data.id}`);
    } catch (e) { setError(errorMessage(e)); }
    finally { inFlight.current = false; setBusy(false); }
  };
  useEffect(() => {
    if (!entryMode || permissionsLoading || !has(creating ? 'purchase-order.create' : 'purchase-order.update')) return;
    const onSaveShortcut = (event) => {
      if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && (event.key.toLowerCase() === 's' || event.key === 'Enter')) {
        event.preventDefault();
        if (!supplierOpen && !inFlight.current) save();
      }
    };
    window.addEventListener('keydown', onSaveShortcut);
    return () => window.removeEventListener('keydown', onSaveShortcut);
  }, [entryMode, permissionsLoading, has, creating, supplierOpen, form, supplier]);

  const refreshListing = async () => {
    if (inFlight.current) return;
    setIsRefreshing(true);
    setError('');
    try {
      const { data } = await axios.get('/api/purchase-orders', {
        params: { page, supplier: supplierFilter || undefined, sort }
      });
      setListing(data);
      toast.success('Purchase orders refreshed');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setIsRefreshing(false);
    }
  };

  const receive = async () => {
    if (inFlight.current) return;
    inFlight.current = true; setBusy(true); setError('');
    try {
      const { data } = await axios.post(`/api/purchase-orders/${id}/receive`);
      navigate(`/purchase-invoices/create?purchase_order_id=${id}`, { state: { purchaseOrderReceipt: data } });
    } catch (e) { setError(errorMessage(e)); }
    finally { inFlight.current = false; setBusy(false); }
  };
  const cancel = async () => {
    const poNum = formatPoNumber(order?.number || order?.id);
    if (inFlight.current || !window.confirm(`Cancel Purchase Order ${poNum}?`)) return;
    inFlight.current = true; setBusy(true);
    try { const { data } = await axios.post(`/api/purchase-orders/${id}/cancel`); setOrder(prev => ({ ...prev, ...data })); }
    catch (e) { setError(errorMessage(e)); }
    finally { inFlight.current = false; setBusy(false); }
  };
  if (permissionsLoading) return <div className="p-6 text-sm text-[var(--workspace-muted)]">Loading permissions…</div>;
  if (!has('purchase-order.view') || (creating && !has('purchase-order.create')) || (editing && !has('purchase-order.update'))) return <div className="p-6 text-sm text-red-600">You don’t have permission to access this Purchase Order action.</div>;

  const rows = creating || editing ? form.items : order?.items || [];
  const visibleOrders = (listing?.data || []).filter(po => !['received', 'partially_received'].includes(po.status));
  const total = rows.reduce((s, i) => s + (creating || editing ? Number(i.ordered_packs || 0) * Number(i.pack_purchase_price || 0) : Number(i.ordered_quantity || 0) * Number(i.unit_purchase_price || 0)) * (1 - Number(i.item_discount_percentage || 0) / 100), 0);

  const counts = useMemo(() => {
    let overdue = 0;
    let today = 0;
    let upcoming = 0;
    let totalCommitted = 0;
    let totalPacks = 0;
    let totalItems = 0;

    visibleOrders.forEach(po => {
      const urg = getDeliveryUrgency(po.expected_received_date, po.status);
      if (urg.type === 'overdue') overdue++;
      else if (urg.type === 'today') today++;
      else if (urg.type === 'upcoming') upcoming++;

      totalCommitted += Number(po.total_amount || 0);
      totalPacks += Number(po.total_packs ?? po.items_sum_ordered_packs ?? 0);
      totalItems += Number(po.items_count || 0);
    });

    return {
      total: listing?.total ?? visibleOrders.length,
      active: visibleOrders.length,
      overdue,
      today,
      upcoming,
      totalCommitted,
      totalPacks,
      totalItems,
    };
  }, [visibleOrders, listing]);

  const displayedOrders = useMemo(() => {
    let orders = visibleOrders;
    if (selectedDeliveryDate) {
      orders = orders.filter(po => String(po.expected_received_date || '').slice(0, 10) === selectedDeliveryDate);
    }
    if (urgencyFilter === 'all') return orders;
    return orders.filter(po => {
      const urg = getDeliveryUrgency(po.expected_received_date, po.status);
      if (urgencyFilter === 'overdue') return urg.type === 'overdue';
      if (urgencyFilter === 'today') return urg.type === 'today';
      if (urgencyFilter === 'upcoming') return urg.type === 'upcoming' || urg.type === 'today';
      return true;
    });
  }, [visibleOrders, urgencyFilter, selectedDeliveryDate]);

  const daySupplyStats = useMemo(() => {
    if (!selectedDeliveryDate) return null;
    const orders = visibleOrders.filter(po => String(po.expected_received_date || '').slice(0, 10) === selectedDeliveryDate);
    const totalPacks = orders.reduce((s, po) => s + Number(po.total_packs ?? po.items_sum_ordered_packs ?? 0), 0);
    const totalAmount = orders.reduce((s, po) => s + Number(po.total_amount || 0), 0);
    return {
      date: selectedDeliveryDate,
      count: orders.length,
      totalPacks,
      totalAmount,
    };
  }, [visibleOrders, selectedDeliveryDate]);

  // When on the index view (not creating, editing, or viewing a single order)
  if (!id && !creating && !editing) {
    return (
      <div className="space-y-4 purchase-order-documents">
        {/* Header Hero Banner */}
        <div className="g-card p-4 sm:p-5 flex flex-wrap justify-between items-center gap-4">
          <div className="flex items-center gap-3.5">
            <div className="p-3 rounded-2xl bg-gradient-to-tr from-blue-600 to-indigo-600 text-white shadow-md shrink-0">
              <ClipboardDocumentListIcon className="h-6 w-6" />
            </div>
            <div>
              <div className="flex items-center gap-2.5 flex-wrap">
                <h1 className="text-xl font-bold text-gray-900 dark:text-gray-100">
                  Purchase Orders
                </h1>
                <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-blue-100 text-blue-800 dark:bg-blue-950/60 dark:text-blue-300">
                  {counts.active} Active
                </span>
                {counts.overdue > 0 && (
                  <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300 border border-rose-300 dark:border-rose-800 animate-pulse">
                    <ExclamationTriangleIcon className="w-3.5 h-3.5" />
                    {counts.overdue} Overdue
                  </span>
                )}
              </div>
              <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                Monitor delivery deadlines, track supplier orders, and receive incoming inventory.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2 print:hidden">
            <button
              type="button"
              disabled={loading || isRefreshing}
              onClick={refreshListing}
              className="g-btn-ghost theme-btn-tertiary inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg disabled:opacity-50"
              title="Refresh Purchase Orders"
            >
              <ArrowPathIcon className={`w-4 h-4 ${isRefreshing || loading ? 'animate-spin' : ''}`} />
              <span>Refresh</span>
            </button>

            {has('purchase-order.generate') && (
              <Link
                to="/purchase-orders/forecast"
                className="g-btn-secondary theme-btn-secondary inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold rounded-lg shadow-xs"
                title="Smart Reorder Forecast (Ctrl+N)"
              >
                <CalculatorIcon className="w-4 h-4 text-blue-500" />
                <span>New Forecast</span>
                <kbd className="ml-1 px-1.5 py-0.5 text-[10px] font-mono bg-black/5 dark:bg-white/10 rounded">Ctrl+N</kbd>
              </Link>
            )}

            {has('purchase-order.create') && (
              <Link
                to="/purchase-orders/create"
                className="g-btn-primary theme-btn-primary inline-flex items-center gap-1.5 px-4 py-2 text-xs font-bold rounded-lg shadow-sm"
              >
                <PlusIcon className="w-4 h-4" />
                <span>New Purchase Order</span>
              </Link>
            )}
          </div>
        </div>

        {error && (
          <div role="alert" className="p-3.5 rounded-xl bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 text-xs flex items-center justify-between">
            <span>{error}</span>
            <button type="button" onClick={() => setError('')} className="p-1 hover:opacity-75">
              <XMarkIcon className="w-4 h-4" />
            </button>
          </div>
        )}

        {/* 4 Summary KPI Cards */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3.5">
          <div className="g-card p-4 border-l-4 border-l-[var(--workspace-accent)] flex flex-col justify-between">
            <div className="flex items-center justify-between mb-1">
              <span className="text-xs font-semibold uppercase tracking-wider text-[var(--workspace-accent)]">
                Active Orders
              </span>
              <div className="p-2 rounded-lg bg-[color-mix(in_srgb,var(--workspace-accent)_12%,transparent)] text-[var(--workspace-accent)]">
                <ClipboardDocumentListIcon className="w-4 h-4" />
              </div>
            </div>
            <div>
              <p className="text-2xl font-bold font-mono text-gray-900 dark:text-gray-100">
                {counts.active}
              </p>
              <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                Awaiting supplier delivery
              </p>
            </div>
          </div>

          <div
            onClick={() => setUrgencyFilter(counts.overdue > 0 ? (urgencyFilter === 'overdue' ? 'all' : 'overdue') : (urgencyFilter === 'today' ? 'all' : 'today'))}
            className={`g-card p-4 border-l-4 cursor-pointer transition-all hover:shadow-md ${
              counts.overdue > 0
                ? 'border-l-rose-500 bg-rose-50/20 dark:bg-rose-950/10'
                : counts.today > 0
                ? 'border-l-amber-500 bg-amber-50/20 dark:bg-amber-950/10'
                : 'border-l-emerald-500'
            }`}
            title="Click to filter by delivery deadline"
          >
            <div className="flex items-center justify-between mb-1">
              <span className={`text-xs font-semibold uppercase tracking-wider ${
                counts.overdue > 0
                  ? 'text-rose-600 dark:text-rose-400'
                  : counts.today > 0
                  ? 'text-amber-600 dark:text-amber-400'
                  : 'text-emerald-600 dark:text-emerald-400'
              }`}>
                Delivery Urgency
              </span>
              <div className={`p-2 rounded-lg ${
                counts.overdue > 0
                  ? 'bg-rose-50 dark:bg-rose-900/30 text-rose-600 dark:text-rose-400'
                  : counts.today > 0
                  ? 'bg-amber-50 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400'
                  : 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400'
              }`}>
                <TruckIcon className="w-4 h-4" />
              </div>
            </div>
            <div>
              <p className={`text-2xl font-bold font-mono ${
                counts.overdue > 0
                  ? 'text-rose-600 dark:text-rose-400'
                  : counts.today > 0
                  ? 'text-amber-600 dark:text-amber-400'
                  : 'text-emerald-600 dark:text-emerald-400'
              }`}>
                {counts.overdue > 0
                  ? `${counts.overdue} Overdue`
                  : counts.today > 0
                  ? `${counts.today} Due Today`
                  : 'On Schedule'}
              </p>
              <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                {counts.overdue > 0
                  ? `${counts.today} due today • Click to filter`
                  : counts.today > 0
                  ? 'Arriving today • Click to filter'
                  : 'All shipments on track'}
              </p>
            </div>
          </div>

          <div className="g-card p-4 border-l-4 border-l-emerald-500 flex flex-col justify-between">
            <div className="flex items-center justify-between mb-1">
              <span className="text-xs font-semibold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">
                Committed Value
              </span>
              <div className="p-2 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400">
                <BanknotesIcon className="w-4 h-4" />
              </div>
            </div>
            <div>
              <p className="text-2xl font-bold font-mono text-gray-900 dark:text-gray-100">
                {formatCurrency(counts.totalCommitted)}
              </p>
              <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                Total committed purchase value
              </p>
            </div>
          </div>

          <div className="g-card p-4 border-l-4 border-l-[var(--workspace-accent)] flex flex-col justify-between">
            <div className="flex items-center justify-between mb-1">
              <span className="text-xs font-semibold uppercase tracking-wider text-[var(--workspace-accent)]">
                Packs in Pipeline
              </span>
              <div className="p-2 rounded-lg bg-[color-mix(in_srgb,var(--workspace-accent)_12%,transparent)] text-[var(--workspace-accent)]">
                <CubeIcon className="w-4 h-4" />
              </div>
            </div>
            <div>
              <p className="text-2xl font-bold font-mono text-gray-900 dark:text-gray-100">
                {Number(counts.totalPacks).toLocaleString()}
              </p>
              <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                Total packs awaiting delivery
              </p>
            </div>
          </div>
        </div>

        {/* Filter and Search Bar */}
        <div className="g-card p-4 space-y-3.5 print:hidden">
          <div className="flex flex-wrap items-center justify-between gap-3">
            {/* Urgency Filter Pills */}
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => {
                  setUrgencyFilter('all');
                  setSelectedDeliveryDate('');
                  setPage(1);
                }}
                className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                  urgencyFilter === 'all' && !selectedDeliveryDate
                    ? 'theme-btn-primary shadow-xs'
                    : 'bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_10%,transparent)] text-[var(--workspace-ink)] border border-[var(--workspace-border)]'
                }`}
              >
                All Orders ({counts.active})
              </button>

              <button
                type="button"
                onClick={() => {
                  setUrgencyFilter(urgencyFilter === 'overdue' ? 'all' : 'overdue');
                  setSelectedDeliveryDate('');
                  setPage(1);
                }}
                className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                  urgencyFilter === 'overdue'
                    ? 'bg-rose-600 text-white shadow-xs'
                    : 'bg-[var(--workspace-reading-fill)] hover:bg-rose-50 dark:hover:bg-rose-950/40 text-rose-700 dark:text-rose-300 border border-[var(--workspace-border)]'
                }`}
              >
                <ExclamationTriangleIcon className="w-3.5 h-3.5 text-rose-500" />
                <span>Overdue Deliveries</span>
                {counts.overdue > 0 && (
                  <span className={`ml-1 px-1.5 py-0.2 rounded-full text-[10px] font-bold ${
                    urgencyFilter === 'overdue' ? 'bg-white/20 text-white' : 'bg-rose-200 text-rose-900 dark:bg-rose-900 dark:text-rose-100'
                  }`}>
                    {counts.overdue}
                  </span>
                )}
              </button>

              <button
                type="button"
                onClick={() => {
                  setUrgencyFilter(urgencyFilter === 'today' ? 'all' : 'today');
                  setSelectedDeliveryDate('');
                  setPage(1);
                }}
                className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                  urgencyFilter === 'today'
                    ? 'bg-amber-600 text-white shadow-xs'
                    : 'bg-[var(--workspace-reading-fill)] hover:bg-amber-50 dark:hover:bg-amber-950/40 text-amber-700 dark:text-amber-300 border border-[var(--workspace-border)]'
                }`}
              >
                <ClockIcon className="w-3.5 h-3.5 text-amber-500" />
                <span>Due Today</span>
                {counts.today > 0 && (
                  <span className={`ml-1 px-1.5 py-0.2 rounded-full text-[10px] font-bold ${
                    urgencyFilter === 'today' ? 'bg-white/20 text-white' : 'bg-amber-200 text-amber-900 dark:bg-amber-900 dark:text-amber-100'
                  }`}>
                    {counts.today}
                  </span>
                )}
              </button>

              <button
                type="button"
                onClick={() => {
                  setUrgencyFilter(urgencyFilter === 'upcoming' ? 'all' : 'upcoming');
                  setSelectedDeliveryDate('');
                  setPage(1);
                }}
                className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                  urgencyFilter === 'upcoming'
                    ? 'bg-[var(--workspace-accent)] text-white shadow-xs'
                    : 'bg-[var(--workspace-reading-fill)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_10%,transparent)] text-[var(--workspace-ink)] border border-[var(--workspace-border)]'
                }`}
              >
                <TruckIcon className="w-3.5 h-3.5 text-[var(--workspace-accent)]" />
                <span>Upcoming ({counts.upcoming})</span>
              </button>
            </div>

            {/* Particular Day Supply Filter with React-Select (Saves Space) */}
            <div className="flex items-center gap-2 flex-wrap">
              {/* Supply Day React-Select */}
              <div className="w-44 shrink-0">
                <Select
                  options={supplyDayOptions}
                  value={currentSupplyDayOption}
                  onChange={(opt) => {
                    if (!opt || opt.value === '') {
                      setSelectedDeliveryDate('');
                    } else if (opt.value !== 'custom') {
                      setSelectedDeliveryDate(opt.value);
                      setUrgencyFilter('all');
                    }
                    setPage(1);
                  }}
                  styles={supplySelectStyles}
                  isSearchable={false}
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  aria-label="Filter supply deliveries by day"
                />
              </div>

              {/* Modern Date Picker for custom particular day */}
              <div className="w-36">
                <ModernDatePicker
                  value={selectedDeliveryDate}
                  onChange={(val) => {
                    setSelectedDeliveryDate(val || '');
                    if (val) setUrgencyFilter('all');
                    setPage(1);
                  }}
                  isClearable={true}
                  size="sm"
                  placeholder="Custom Date…"
                  ariaLabel="Select a particular day to view supply"
                />
              </div>

              {selectedDeliveryDate && (
                <button
                  type="button"
                  onClick={() => {
                    setSelectedDeliveryDate('');
                    setPage(1);
                  }}
                  className="p-1.5 rounded-md text-[var(--workspace-muted)] hover:text-[var(--workspace-ink)] hover:bg-[color-mix(in_srgb,var(--workspace-accent)_10%,transparent)] transition-colors"
                  title="Clear supply date filter"
                  aria-label="Clear supply date filter"
                >
                  <XMarkIcon className="w-4 h-4" />
                </button>
              )}
            </div>

            {(urgencyFilter !== 'all' || selectedDeliveryDate) && (
              <button
                type="button"
                onClick={() => {
                  setUrgencyFilter('all');
                  setSelectedDeliveryDate('');
                  setPage(1);
                }}
                className="text-xs text-[var(--workspace-accent)] hover:underline font-medium inline-flex items-center gap-1"
              >
                <XMarkIcon className="w-3.5 h-3.5" />
                <span>Reset filters</span>
              </button>
            )}
          </div>

          {/* Particular Day Supply Notification Banner */}
          {selectedDeliveryDate && (
            <div className="flex items-center justify-between gap-3 px-3.5 py-2.5 rounded-xl bg-[color-mix(in_srgb,var(--workspace-accent)_8%,var(--workspace-surface))] border border-[color-mix(in_srgb,var(--workspace-accent)_20%,transparent)] text-xs">
              <div className="flex items-center gap-2 flex-wrap">
                <div className="p-1 rounded-md bg-[var(--workspace-accent)] text-white">
                  <TruckIcon className="w-3.5 h-3.5" />
                </div>
                <span className="font-semibold text-[var(--workspace-ink)]">
                  Particular Day Supply for {formatDate(selectedDeliveryDate)}:
                </span>
                <span className="text-[var(--workspace-muted)]">
                  {daySupplyStats?.count || 0} {(daySupplyStats?.count || 0) === 1 ? 'order' : 'orders'} scheduled
                  {' • '}
                  {Number(daySupplyStats?.totalPacks || 0).toLocaleString()} packs
                  {' • '}
                  {formatCurrency(daySupplyStats?.totalAmount || 0)} committed value
                </span>
              </div>
              <button
                type="button"
                onClick={() => {
                  setSelectedDeliveryDate('');
                  setPage(1);
                }}
                className="inline-flex items-center gap-1 font-semibold text-[var(--workspace-accent)] hover:underline shrink-0"
              >
                <XMarkIcon className="w-3.5 h-3.5" />
                <span>Show All Days</span>
              </button>
            </div>
          )}

          <div className="flex flex-wrap items-center gap-3 pt-0.5">
            <div className="flex-1 min-w-[260px] relative">
              <MagnifyingGlassIcon className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none text-gray-400" />
              <input
                type="search"
                value={supplierQuery}
                onChange={e => setSupplierQuery(e.target.value)}
                placeholder="Search by supplier name, PO number (e.g. PO-3 or 3), or reference…"
                aria-label="Search purchase orders"
                className="w-full h-9 pl-9 pr-8 rounded-lg border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] text-xs text-gray-900 dark:text-gray-100 placeholder:text-gray-400 focus:outline-hidden focus:ring-2 focus:ring-[var(--workspace-accent)]"
              />
              {supplierQuery && (
                <button
                  type="button"
                  onClick={() => setSupplierQuery('')}
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200"
                  aria-label="Clear search"
                >
                  <XMarkIcon className="w-4 h-4" />
                </button>
              )}
            </div>

            <div className="flex items-center gap-2">
              <span className="inline-flex items-center gap-1 text-xs font-medium text-gray-500 dark:text-gray-400 whitespace-nowrap">
                <ArrowsUpDownIcon className="w-3.5 h-3.5" />
                <span>Sort by:</span>
              </span>
              <select
                value={sort}
                onChange={e => { setSort(e.target.value); setPage(1); }}
                aria-label="Sort purchase orders"
                className="h-9 px-2.5 rounded-lg border border-[var(--workspace-border)] bg-[var(--workspace-reading-fill)] text-xs font-medium text-gray-800 dark:text-gray-200 focus:outline-hidden focus:ring-2 focus:ring-blue-500 cursor-pointer"
              >
                {sortOptions.map(([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                ))}
              </select>
            </div>
          </div>
        </div>

        {/* Orders Table Panel */}
        <div className="g-card overflow-hidden products-panel purchase-invoice-catalog purchase-order-catalog">
          <div className="products-catalog-heading px-5 py-3.5 border-b border-[var(--workspace-border)] flex items-center justify-between">
            <div className="flex items-center gap-2.5">
              <div className="p-2 rounded-xl bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400">
                <ClipboardDocumentListIcon className="w-4 h-4" />
              </div>
              <div>
                <h2 className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  Purchase Order Records
                </h2>
                <p className="text-[11px] text-gray-500 dark:text-gray-400">
                  {loading ? (
                    <span className="inline-flex items-center gap-1">
                      <ArrowPathIcon className="w-3 h-3 animate-spin" />
                      Loading records…
                    </span>
                  ) : (
                    `Showing ${displayedOrders.length} of ${counts.total} orders`
                  )}
                </p>
              </div>
            </div>
          </div>

          <div className="purchase-invoice-table-scroll overflow-x-auto" tabIndex={0} role="region" aria-label="Purchase order list" aria-busy={busy || loading}>
            <table className="w-full text-xs text-left">
              <thead className="bg-[var(--workspace-surface)] text-[var(--workspace-muted)] font-semibold border-b border-[var(--workspace-border)] sticky top-0 z-10 shadow-xs">
                <tr>
                  {['PO Number', 'Order Date', 'Expected Delivery', 'Supplier', 'Ordered Packs', 'Total Amount', 'Receipts', 'Status', 'Actions'].map(h => {
                    const pair = headerSorts[h];
                    const active = pair && pair.includes(sort);
                    const dir = active ? (sort === pair[0] ? '↓' : '↑') : '';
                    const align = h === 'Actions' ? 'text-right pr-5' : h === 'Total Amount' ? 'text-right' : 'text-left';
                    return (
                      <th
                        className={`px-3 py-2.5 text-xs font-semibold uppercase tracking-wider ${align}`}
                        key={h}
                        aria-sort={active ? (sort.endsWith('_asc') || sort === 'oldest' ? 'ascending' : 'descending') : undefined}
                      >
                        {pair ? (
                          <button
                            type="button"
                            onClick={() => { setSort(sort === pair[0] ? pair[1] : pair[0]); setPage(1); }}
                            className={`inline-flex items-center gap-1 font-semibold uppercase tracking-wider cursor-pointer hover:text-[var(--workspace-accent)] ${h === 'Total Amount' ? 'ml-auto' : ''}`}
                          >
                            <span>{h}</span>
                            {dir && <span aria-hidden="true" className="font-bold">{dir}</span>}
                          </button>
                        ) : h}
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--workspace-border)]">
                {displayedOrders.length === 0 && !loading && (
                  <tr>
                    <td colSpan={9} className="px-4 py-12 text-center">
                      <ClipboardDocumentListIcon className="w-12 h-12 mx-auto text-gray-300 dark:text-gray-600 mb-3" />
                      <h3 className="text-sm font-semibold text-gray-800 dark:text-gray-200">
                        {selectedDeliveryDate
                          ? `No supplies scheduled for ${formatDate(selectedDeliveryDate)}`
                          : urgencyFilter !== 'all'
                          ? `No ${urgencyFilter} purchase orders found`
                          : supplierFilter
                          ? `No orders matching "${supplierFilter}"`
                          : 'No active purchase orders'}
                      </h3>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-1 max-w-sm mx-auto">
                        {selectedDeliveryDate
                          ? 'There are no purchase order deliveries arriving on this particular date.'
                          : urgencyFilter !== 'all'
                          ? `There are no purchase orders matching the selected ${urgencyFilter} urgency criteria.`
                          : supplierFilter
                          ? 'Try refining your search terms or clearing the filter.'
                          : 'Create a new purchase order or generate one using the smart reorder forecast.'}
                      </p>
                      <div className="mt-4 flex items-center justify-center gap-2">
                        {(urgencyFilter !== 'all' || supplierFilter || selectedDeliveryDate) && (
                          <button
                            type="button"
                            onClick={() => { setUrgencyFilter('all'); setSupplierQuery(''); setSelectedDeliveryDate(''); setPage(1); }}
                            className="g-btn-secondary theme-btn-secondary text-xs px-3 py-1.5"
                          >
                            Reset Filters
                          </button>
                        )}
                        {has('purchase-order.create') && (
                          <Link
                            to="/purchase-orders/create"
                            className="g-btn-primary theme-btn-primary text-xs px-3 py-1.5 inline-flex items-center gap-1.5"
                          >
                            <PlusIcon className="w-3.5 h-3.5" />
                            <span>New Purchase Order</span>
                          </Link>
                        )}
                      </div>
                    </td>
                  </tr>
                )}

                {displayedOrders.map(po => {
                  const urg = getDeliveryUrgency(po.expected_received_date, po.status);
                  const isOverdue = urg.type === 'overdue';
                  const poNum = formatPoNumber(po.number || po.id);

                  return (
                    <tr
                      key={po.id}
                      className={`hover:bg-[color-mix(in_srgb,var(--workspace-accent)_5%,transparent)] transition-colors ${
                        isOverdue ? 'purchase-order-row-overdue bg-rose-50/20 dark:bg-rose-950/10' : ''
                      }`}
                    >
                      {/* PO Number & Reference */}
                      <td data-label="PO Number" className="px-3 py-2.5">
                        <div className="flex flex-col">
                          <Link
                            to={`/purchase-orders/${po.id}`}
                            className="font-bold text-xs text-[var(--workspace-accent)] hover:underline"
                            title={`View details for ${poNum}`}
                          >
                            {poNum}
                          </Link>
                          {po.reference && (
                            <span className="text-[10px] text-gray-500 dark:text-gray-400 truncate max-w-[120px]" title={`Ref: ${po.reference}`}>
                              Ref: {po.reference}
                            </span>
                          )}
                        </div>
                      </td>

                      {/* Order Date */}
                      <td data-label="Order Date" className="px-3 py-2.5 whitespace-nowrap">
                        <span className="text-xs text-gray-700 dark:text-gray-300 font-medium">
                          {formatDate(po.date)}
                        </span>
                      </td>

                      {/* Expected Delivery Date (Prominently Highlighted & Clickable to view that day's supply) */}
                      <td data-label="Expected Delivery" className="px-3 py-2.5 whitespace-nowrap">
                        <div className="flex flex-col gap-1 items-start">
                          <button
                            type="button"
                            onClick={() => {
                              const d = String(po.expected_received_date || '').slice(0, 10);
                              if (d) {
                                setSelectedDeliveryDate(d);
                                setUrgencyFilter('all');
                                setPage(1);
                              }
                            }}
                            className="flex items-center gap-1.5 font-semibold text-xs text-gray-900 dark:text-gray-100 hover:text-[var(--workspace-accent)] hover:underline text-left cursor-pointer transition-colors"
                            title={`Filter and view all supplies scheduled for ${formatDate(po.expected_received_date)}`}
                          >
                            <CalendarIcon className="w-3.5 h-3.5 text-gray-400 shrink-0" />
                            <span>{formatDate(po.expected_received_date)}</span>
                          </button>

                          {/* Urgency Badge */}
                          {urg.type === 'overdue' && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-bold tracking-tight bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300 border border-rose-300 dark:border-rose-800">
                              <ExclamationTriangleIcon className="w-3 h-3 text-rose-600 dark:text-rose-400 shrink-0" />
                              <span>{urg.label}</span>
                            </span>
                          )}
                          {urg.type === 'today' && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-bold tracking-tight bg-amber-100 text-amber-900 dark:bg-amber-950/60 dark:text-amber-300 border border-amber-300 dark:border-amber-800">
                              <ClockIcon className="w-3 h-3 text-amber-600 dark:text-amber-400 shrink-0" />
                              <span>Due Today</span>
                            </span>
                          )}
                          {urg.type === 'upcoming' && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-medium bg-blue-50 text-blue-700 dark:bg-blue-950/50 dark:text-blue-300 border border-blue-200 dark:border-blue-800">
                              <TruckIcon className="w-3 h-3 text-blue-500 shrink-0" />
                              <span>{urg.label}</span>
                            </span>
                          )}
                          {urg.type === 'scheduled' && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] text-gray-500 dark:text-slate-400 bg-gray-100 dark:bg-slate-800 border border-gray-200 dark:border-slate-700">
                              <span>{urg.label}</span>
                            </span>
                          )}
                          {urg.type === 'none' && (
                            <span className="text-[10px] text-gray-400 dark:text-slate-500 italic">Not scheduled</span>
                          )}
                        </div>
                      </td>

                      {/* Supplier */}
                      <td data-label="Supplier" className="px-3 py-2.5">
                        <div className="flex items-center gap-1.5">
                          <BuildingStorefrontIcon className="w-4 h-4 text-gray-400 shrink-0" />
                          <span className="font-semibold text-xs text-gray-900 dark:text-gray-100">
                            {po.supplier?.name || '—'}
                          </span>
                        </div>
                      </td>

                      {/* Ordered Packs */}
                      <td data-label="Ordered Packs" className="px-3 py-2.5 tabular-nums text-xs text-gray-700 dark:text-gray-300 font-semibold">
                        {Number(po.total_packs ?? po.items_sum_ordered_packs ?? 0).toLocaleString()} packs
                      </td>

                      {/* Total Amount */}
                      <td data-label="Total Amount" className="px-3 py-2.5 text-right tabular-nums font-bold text-xs text-gray-900 dark:text-gray-100">
                        {formatCurrency(Number(po.total_amount || 0))}
                      </td>

                      {/* Receipts */}
                      <td data-label="Receipts" className="px-3 py-2.5 whitespace-nowrap">
                        {po.invoices?.length > 0 ? (
                          <Link
                            to={`/purchase-orders/${po.id}`}
                            className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-semibold bg-emerald-100 text-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-300 hover:bg-emerald-200 transition-colors"
                            title={`View ${po.invoices.length} receipt invoice(s)`}
                          >
                            <CheckCircleIcon className="w-3.5 h-3.5" />
                            <span>{po.invoices.length} {po.invoices.length === 1 ? 'Receipt' : 'Receipts'}</span>
                          </Link>
                        ) : (
                          <span className="text-[11px] text-gray-400 dark:text-slate-500">None yet</span>
                        )}
                      </td>

                      {/* Status */}
                      <td data-label="Status" className="px-3 py-2.5 whitespace-nowrap">
                        <span className="purchase-order-status" data-status={po.status}>
                          {statusLabel(po.status)}
                        </span>
                      </td>

                      {/* Properly Placed Action Buttons */}
                      <td data-label="Actions" className="px-3 py-2.5 text-right">
                        <div className="purchase-order-index-actions flex items-center justify-end gap-1.5 flex-nowrap">
                          {/* Primary CTA: Receive Delivery into Invoice */}
                          {po.status === 'ordered' && has('purchase-invoice.create') && (
                            <Link
                              to={`/purchase-invoices/create?purchase_order_id=${po.id}`}
                              className="purchase-order-action purchase-order-action-primary shrink-0"
                              title={`Receive delivery for ${poNum} into Purchase Invoice (GRN)`}
                              aria-label={`Receive delivery for ${poNum}`}
                            >
                              <ArrowDownTrayIcon aria-hidden="true" className="w-3.5 h-3.5 shrink-0" />
                              <span>Receive</span>
                            </Link>
                          )}

                          {!!po.invoices?.length && (
                            <Link
                              to={`/purchase-orders/${po.id}`}
                              className="purchase-order-action shrink-0 text-emerald-700 dark:text-emerald-300 border-emerald-300 dark:border-emerald-800"
                              title={`View receipt history for ${poNum}`}
                              aria-label={`View receipts for ${poNum}`}
                            >
                              <DocumentTextIcon aria-hidden="true" className="w-3.5 h-3.5 shrink-0" />
                              <span>Receipts</span>
                            </Link>
                          )}

                          {/* Secondary Icon Actions */}
                          <div className="flex items-center gap-0.5 shrink-0">
                            {/* View / Print */}
                            <Link
                              to={`/purchase-orders/${po.id}`}
                              className="products-row-action"
                              title={`View & Print ${poNum}`}
                              aria-label={`View Purchase Order ${poNum}`}
                            >
                              <EyeIcon aria-hidden="true" className="w-4 h-4" />
                            </Link>

                            {/* Share / WhatsApp PDF */}
                            <button
                              type="button"
                              onClick={() => setSharingPoId(po.id)}
                              className="products-row-action text-emerald-600 dark:text-emerald-400"
                              title={`Share ${poNum} via WhatsApp or Email PDF`}
                              aria-label={`Share Purchase Order ${poNum}`}
                            >
                              <ShareIcon aria-hidden="true" className="w-4 h-4" />
                            </button>

                            {/* Barcode labels */}
                            {(has('barcode.view') || has('barcode.print')) && (
                              <Link
                                to={`/barcode-labels?purchase_order_id=${po.id}`}
                                className="products-row-action text-indigo-600 dark:text-indigo-400"
                                title={`Print Barcodes for ${poNum}`}
                                aria-label={`Print Barcodes for ${poNum}`}
                              >
                                <QrCodeIcon aria-hidden="true" className="w-4 h-4" />
                              </Link>
                            )}

                            {/* Edit (only if unreceived) */}
                            {po.status === 'ordered' && !po.invoices?.length && has('purchase-order.update') && (
                              <Link
                                to={`/purchase-orders/${po.id}/edit`}
                                className="products-row-action text-amber-600 dark:text-amber-400"
                                title={`Edit ${poNum}`}
                                aria-label={`Edit Purchase Order ${poNum}`}
                              >
                                <PencilSquareIcon aria-hidden="true" className="w-4 h-4" />
                              </Link>
                            )}

                            {/* Delete (only if unreceived & has delete permission) */}
                            {!po.invoices?.length && ['ordered', 'cancelled'].includes(po.status) && has('purchase-order.delete') && (
                              <button
                                type="button"
                                disabled={busy}
                                onClick={() => deleteOrder(po)}
                                className="products-row-action products-row-action-danger"
                                title={`Delete ${poNum}`}
                                aria-label={`Delete Purchase Order ${poNum}`}
                              >
                                <TrashIcon aria-hidden="true" className="w-4 h-4" />
                              </button>
                            )}
                          </div>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* Pagination Footer */}
          {listing && (
            <div className="purchase-entry-footer flex flex-wrap items-center justify-between gap-3 px-5 py-3.5 border-t border-[var(--workspace-border)] bg-gray-50/50 dark:bg-slate-800/30">
              <span className="text-xs text-gray-500 dark:text-gray-400">
                Page {listing.current_page} of {listing.last_page} ({counts.total} total orders)
              </span>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  className="g-btn-ghost theme-btn-tertiary inline-flex items-center justify-center px-3 py-1.5 text-xs font-semibold rounded-lg disabled:opacity-50"
                  disabled={page <= 1}
                  onClick={() => setPage(page - 1)}
                >
                  Previous
                </button>
                <span className="text-xs font-medium px-2">
                  {page}
                </span>
                <button
                  type="button"
                  className="g-btn-ghost theme-btn-tertiary inline-flex items-center justify-center px-3 py-1.5 text-xs font-semibold rounded-lg disabled:opacity-50"
                  disabled={page >= listing.last_page}
                  onClick={() => setPage(page + 1)}
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </div>

        <ShareDocumentModal
          isOpen={Boolean(sharingPoId)}
          onClose={() => setSharingPoId(null)}
          documentType="purchase_order"
          documentId={sharingPoId}
        />
        <style>{`@media print { body * { visibility: hidden; } .purchase-order-documents, .purchase-order-documents * { visibility: visible; } .purchase-order-documents { position: absolute; left: 0; top: 0; width: 100%; color: black; background: white; } .purchase-order-documents .print\\:hidden, .purchase-order-documents .print\\:hidden * { display: none; } .purchase-order-documents .g-card { background: white; box-shadow: none; backdrop-filter: none; } }`}</style>
      </div>
    );
  }

  return <div className="purchase-entry-page space-y-4 purchase-order-documents">
    <div className={`purchase-entry g-card overflow-hidden space-y-4 ${order && !editing ? "purchase-order-show" : entryMode ? "purchase-order-entry" : ""}`}>
      <div className="purchase-entry-header purchase-entry-banner flex flex-wrap justify-between gap-3 items-center px-5 py-4">
        <div className="flex items-center gap-3"><div className="purchase-entry-icon p-2.5 rounded-xl"><ClipboardDocumentListIcon className="h-6 w-6" /></div><h1 className="purchase-entry-title text-xl font-semibold">{creating ? 'New Purchase Order' : editing ? `Edit ${formatPoNumber(order?.number || order?.id) || 'Purchase Order'}` : formatPoNumber(order?.number || order?.id) || 'Purchase Orders'}</h1></div>
        <div className="flex flex-wrap gap-2 print:hidden">
          {has('purchase-order.generate') && <Link className="g-btn-secondary theme-btn-secondary inline-flex items-center justify-center gap-2 disabled:opacity-50" to="/purchase-orders/forecast" title="New Forecast (Ctrl+N)"><span>New Forecast</span> <kbd className="text-[10px] opacity-70">Ctrl+N</kbd></Link>}
          {order && !editing && order.status === 'ordered' && has('purchase-invoice.create') && <button className="g-btn-primary theme-btn-primary inline-flex items-center justify-center gap-2 disabled:opacity-50" disabled={busy} onClick={receive}><ArrowDownTrayIcon className="w-4 h-4" /><span>{busy ? 'Preparing…' : 'Receive Delivery'}</span></button>}
          {entryMode && <button className="purchase-entry-add-product theme-btn-secondary inline-flex items-center gap-2 px-3 py-2 text-xs font-semibold rounded-lg" disabled={busy} onClick={addItem}><PlusIcon className="h-4 w-4" /><span>Add Product</span></button>}
          {(id || creating) && <Link className="g-btn-ghost theme-btn-tertiary inline-flex items-center justify-center gap-2 disabled:opacity-50" to="/purchase-orders">All Orders</Link>}
          {!id && !creating && has('purchase-order.create') && <Link className="g-btn-primary theme-btn-primary inline-flex items-center justify-center gap-2 disabled:opacity-50" to="/purchase-orders/create">New Purchase Order</Link>}
        </div>
      </div>
      {error && <p role="alert" className="text-red-600 dark:text-red-400">{error}</p>}
      {loading && (id || creating || !listing) ? <p>Loading Purchase Orders…</p> : creating || editing ? <>
        <div className="purchase-entry-details grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3 px-3 py-2.5">
          <div className="flex flex-col gap-1 text-xs font-medium text-[var(--workspace-muted)]">
            <span>Date</span>
            <ModernDatePicker
              value={form.date}
              onChange={(val) => setForm({ ...form, date: val })}
              isClearable={false}
              size="sm"
              ariaLabel="Purchase order date"
            />
          </div>
          <div className="flex flex-col gap-1 text-xs font-medium text-[var(--workspace-muted)]">
            <span>Expected Receiving Date *</span>
            <ModernDatePicker
              required
              minDate={form.date || undefined}
              value={form.expected_received_date || ""}
              onChange={(val) => setForm({ ...form, expected_received_date: val })}
              size="sm"
              ariaLabel="Expected receiving date"
              placeholder="Select date"
            />
          </div>
          <label>Reference<input className="w-full h-8 border rounded-md px-2 text-xs" value={form.reference || ''} onChange={e => setForm({ ...form, reference: e.target.value })} /></label>
          <label>Supplier *
            <button type="button" disabled={busy} onClick={() => setSupplierOpen(true)}
              className="w-full h-8 border rounded-md px-2 text-xs text-left flex items-center gap-2 disabled:opacity-70"
              title="Select supplier"
              aria-label={supplier ? `Supplier: ${supplier.name}` : 'Select supplier'}>
              <BuildingStorefrontIcon aria-hidden="true" className="w-4 h-4 shrink-0" />
              <span className="truncate">{supplier?.name || 'Click to search supplier…'}</span>
            </button>
          </label>
          <label>Notes<input className="w-full h-8 border rounded-md px-2 text-xs" value={form.notes || ''} onChange={e => setForm({ ...form, notes: e.target.value })} /></label>
        </div>
        <p className="text-sm px-5 text-[var(--workspace-muted)]">Enter: next field / new row · ↑↓: move between rows · Ctrl+S: save. Order whole packs; one supplier applies to all brands.</p>
        <div ref={itemsContainerRef} className="purchase-entry-items purchase-order-form-items px-1 py-2"><table className="purchase-order-form-table w-full border-collapse text-[11px]"><colgroup>{[3, 26, 12, 6, 8, 12, 7, 7, 7, 12].map((width, index) => <col key={index} style={{ width: `${width}%` }} />)}</colgroup><thead><tr><th rowSpan={2} aria-label="Remove item" />{['Product', 'Brand', 'Pack Size', 'Order Packs', 'Pack Purchase Price'].map(label => <th rowSpan={2} key={label}>{label}</th>)}<th colSpan={2}>Bonus Quantity</th><th rowSpan={2}>Discount %</th><th rowSpan={2}>Total</th></tr><tr><th>Pack Bonus</th><th>Unit Bonus</th></tr></thead><tbody>
          {rows.map((row, i) => <tr key={i} ref={ref => { itemRowRefs.current[i] = ref; }} onFocusCapture={event => { if (event.currentTarget.contains(event.target)) scrollToItem(i); }}>
            <td data-label="Remove"><button className="products-row-action products-row-delete" title="Remove item" aria-label={`Remove row ${i + 1}`} onClick={() => setForm({ ...form, items: rows.filter((_, n) => i !== n) })}><TrashIcon className="h-4 w-4 text-red-500" /></button></td>
            <td data-label="Product" className="text-left min-w-0" onKeyDownCapture={event => {
              if (event.target.matches('.product-search-trigger input') && (event.key === 'ArrowUp' || event.key === 'ArrowDown' || (event.key === 'Enter' && row.product_id))) navigateItem(event, i, 'product');
            }}><ProductSearchInput ref={ref => { productRefs.current[i] = ref; }} value={row.product_id} products={products} onRefreshProducts={refreshProducts} onChange={product => {
              const value = product.id;
              setForm(prev => ({ ...prev, items: prev.items.map((r, index) => index === i ? { ...r, product_id: value, pack_size: Math.max(1, Number(product?.pack_size || 1)), pack_purchase_price: product?.pack_purchase_price || Number(product?.unit_purchase_price || 0) * Math.max(1, Number(product?.pack_size || 1)), unit_bonus: 0, pack_bonus: 0, product } : r) }));
              setPendingFocus({ index: i, field: 'ordered_packs' });
            }} /></td>
            <td data-label="Brand" className="text-left break-words">{row.product?.brand?.name || row.product?.brand_name || '—'}</td>
            {['pack_size', 'ordered_packs', 'pack_purchase_price', 'pack_bonus', 'unit_bonus', 'item_discount_percentage'].map(field => <td data-label={itemLabels[field]} key={field}><input ref={ref => { fieldRefs.current[`${i}:${field}`] = ref; }} onKeyDown={event => { if (field !== "pack_size") navigateItem(event, i, field); }} aria-label={`${itemLabels[field]} row ${i + 1}`} className="w-full min-w-0 h-7 rounded-md border px-1 text-right text-xs" type="number" readOnly={field === "pack_size"} tabIndex={field === "pack_size" ? -1 : undefined} min={field === "pack_size" || field === "ordered_packs" ? 1 : 0} max={field === "item_discount_percentage" ? 100 : undefined} step={field === "pack_size" || field === "ordered_packs" || field === "unit_bonus" ? 1 : "any"} value={row[field]} onChange={e => changeItem(i, field, e.target.value)} /></td>)}
            <td data-label="Total" className="text-right font-semibold break-words">{(Number(row.ordered_packs || 0) * Number(row.pack_purchase_price || 0) * (1 - Number(row.item_discount_percentage || 0) / 100)).toFixed(2)}</td>
          </tr>)}
        </tbody></table></div>
        <div className="purchase-entry-footer flex flex-wrap items-center justify-between gap-3 px-5 py-4"><div className="flex flex-wrap gap-2"><button className="g-btn-secondary theme-btn-secondary inline-flex items-center justify-center gap-2 disabled:opacity-50" onClick={addItem}><PlusIcon className="h-4 w-4" /><span>Add Product</span></button>{supplier?.id && <button className="g-btn-ghost theme-btn-tertiary inline-flex items-center justify-center gap-2 disabled:opacity-50" disabled={busy} title={`Arrange products in ${supplier.name}'s remembered order`} onClick={() => applySupplierOrder()}>Apply Saved Order</button>}</div><div className="text-right text-xs"><p>{rows.length} items · {rows.reduce((sum, row) => sum + Number(row.ordered_packs || 0), 0)} packs</p><p className="font-semibold text-lg">Total: {total.toFixed(2)}</p></div><button className="g-btn-primary theme-btn-primary inline-flex items-center justify-center gap-2 disabled:opacity-50" disabled={busy} onClick={() => save()}>{busy ? 'Saving Purchase Order…' : 'Save Purchase Order (Ctrl+S)'}</button></div>
        <SupplierSearch title="Select Supplier" isOpen={supplierOpen} onClose={() => setSupplierOpen(false)} onSelect={chosen => { setSupplier(chosen); setSupplierOpen(false); setError(''); applySupplierOrder(chosen, { quiet: true }); }} />
      </> : order ? <>
        <div className="purchase-entry-details grid gap-2 sm:grid-cols-4 px-3 py-2 text-xs"><p>Date: {order.date}</p><p>Expected Receiving: {order.expected_received_date || "Not scheduled"}</p><p>Supplier: {order.supplier?.name}</p><p>Status: {statusLabel(order.status)}</p><p>Reference: {order.reference || '—'}</p></div>
        {order.notes && <p>{order.notes}</p>}
        <div className="purchase-entry-items purchase-order-form-items px-1 py-1"><table className="purchase-order-form-table purchase-order-detail-table w-full border-collapse text-[11px]"><colgroup>{[23, 12, 5, 7, 8, 8, 10, 6, 6, 6, 9].map((width, index) => <col key={index} style={{ width: `${width}%` }} />)}</colgroup><thead><tr>{detailLabels.map(h => <th key={h}>{h}</th>)}</tr></thead><tbody>{rows.map(row => <tr key={row.id}>{[row.product?.name, row.product?.brand?.name || '—', row.pack_size, row.ordered_quantity / row.pack_size, row.received_quantity, row.remaining_quantity, Number(row.unit_purchase_price * row.pack_size).toFixed(2), Number(row.unit_bonus || 0) / row.pack_size, row.unit_bonus || 0, row.item_discount_percentage, (row.ordered_quantity * row.unit_purchase_price * (1-row.item_discount_percentage/100)).toFixed(2)].map((v, i) => <td data-label={detailLabels[i]} className={i < 2 ? "text-left" : "text-right tabular-nums"} key={i}>{v}</td>)}</tr>)}</tbody></table></div>
        <p className="text-xs font-medium">{rows.length} items · {rows.reduce((s, r) => s + Number(r.ordered_quantity), 0)} ordered units · Total: {total.toFixed(2)}</p>
        <section className="px-3 py-3 space-y-3" aria-label="Purchase order receiving">
          <div className="flex flex-wrap gap-6 text-xs">
            <p><span className="block opacity-70">Deliveries received</span><strong className="text-lg">{order.invoices?.length || 0}</strong></p>
            <p><span className="block opacity-70">Paid units received</span><strong className="text-lg">{rows.reduce((sum, row) => sum + Number(row.received_quantity || 0), 0)}</strong></p>
            <p><span className="block opacity-70">Paid units remaining</span><strong className="text-lg">{rows.reduce((sum, row) => sum + Number(row.remaining_quantity || 0), 0)}</strong></p>
          </div>
          <h3 className="font-semibold text-sm">Receipt History</h3>
          {order.invoices?.length ? <div className="overflow-x-auto"><table className="w-full text-xs text-left"><thead><tr>{['Purchase Invoice', 'Received Date', 'Supplier Invoice', 'Amount', 'Action'].map(label => <th key={label} className="px-3 py-2 border-b">{label}</th>)}</tr></thead><tbody>{order.invoices.map(invoice => <tr key={invoice.id}><td className="px-3 py-2 border-b font-medium">{invoice.posted_number}</td><td className="px-3 py-2 border-b">{invoice.posted_date}</td><td className="px-3 py-2 border-b">{invoice.invoice_number}</td><td className="px-3 py-2 border-b tabular-nums">{Number(invoice.total_amount).toFixed(2)}</td><td className="px-3 py-2 border-b">{has('purchase-invoice.view') && <Link className="underline" to={`/purchase-invoices/${invoice.id}`}>View Invoice</Link>}</td></tr>)}</tbody></table></div> : <p className="text-xs opacity-70">No deliveries received yet. Use Receive Delivery to record the first delivery.</p>}
        </section>
        <div className="purchase-entry-footer flex flex-wrap gap-2 px-3 py-2 print:hidden">
          {order.status === 'ordered' && !order.invoices?.length && <>
            {has('purchase-order.update') && <Link className="g-btn-secondary theme-btn-secondary inline-flex items-center justify-center gap-2" to={`/purchase-orders/${id}/edit`}>Edit</Link>}
            {has('purchase-order.cancel') && <button className="g-btn-ghost theme-btn-danger inline-flex items-center justify-center gap-2" disabled={busy} onClick={cancel}>Cancel Order</button>}
          </>}
          {order.status === 'ordered' && has('purchase-invoice.create') && <button className="g-btn-primary theme-btn-primary inline-flex items-center justify-center gap-2 disabled:opacity-50" disabled={busy} onClick={receive}>{busy ? 'Preparing Purchase Invoice…' : 'Receive Order'}</button>}
          {(has('barcode.view') || has('barcode.print')) && <Link className="g-btn-ghost theme-btn-secondary inline-flex items-center justify-center gap-2 disabled:opacity-50" to={`/barcode-labels?purchase_order_id=${order.id}`} title="Print Barcode Labels for this order"><QrCodeIcon className="w-4 h-4" /><span>Print Barcodes</span></Link>}
          <button type="button" className="g-btn-secondary theme-btn-secondary inline-flex items-center justify-center gap-2 disabled:opacity-50" onClick={() => setSharingPoId(order.id)}><ShareIcon className="w-4 h-4" /><span>Share / Email PDF</span></button>
          <button className="g-btn-ghost theme-btn-tertiary inline-flex items-center justify-center gap-2 disabled:opacity-50" onClick={() => handlePrintOrder(order.id)}>Print Order</button>
        </div>
      </> : null}
    </div>
    <ShareDocumentModal
      isOpen={Boolean(sharingPoId)}
      onClose={() => setSharingPoId(null)}
      documentType="purchase_order"
      documentId={sharingPoId}
    />
    <style>{`@media print { body * { visibility: hidden; } .purchase-order-documents, .purchase-order-documents * { visibility: visible; } .purchase-order-documents { position: absolute; left: 0; top: 0; width: 100%; color: black; background: white; } .purchase-order-documents .print\\:hidden, .purchase-order-documents .print\\:hidden * { display: none; } .purchase-order-documents .g-card { background: white; box-shadow: none; backdrop-filter: none; } }`}</style>
  </div>;
}
