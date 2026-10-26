import { useState, useEffect, useMemo, useCallback } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import {
  BanknotesIcon,
  PlusIcon,
  TagIcon,
  MagnifyingGlassIcon,
  ArrowPathIcon,
  PencilSquareIcon,
  TrashIcon,
  CalendarDaysIcon,
  BuildingLibraryIcon,
  CreditCardIcon,
  CurrencyDollarIcon,
  DevicePhoneMobileIcon,
} from "@heroicons/react/24/outline";
import { useCurrency } from "@/context/CurrencyContext";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";
import CategoriesModal from "./CategoriesModal.jsx";

const PAYMENT_METHODS = [
  { value: "cash", label: "Cash (Drawer)" },
  { value: "bank_transfer", label: "Bank Transfer" },
  { value: "card", label: "Credit/Debit Card" },
  { value: "mobile_wallet", label: "Mobile Wallet (JazzCash/Easypaisa)" },
  { value: "cheque", label: "Cheque" },
  { value: "other", label: "Other" },
];

export default function ExpensesIndex() {
  const { formatCurrency } = useCurrency();
  const { isDark, theme } = useTheme();

  // 🎨 Dynamic Button styles using theme colors (matching ThemeSetting.jsx)
  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || "#3b82f6",
    primaryHover: theme?.primary_hover || "#2563eb",
    primaryLight: theme?.primary_light || "#dbeafe",
    secondary: theme?.secondary_color || "#8b5cf6",
    secondaryHover: theme?.secondary_hover || "#7c3aed",
    secondaryLight: theme?.secondary_light || "#ede9fe",
    tertiary: theme?.tertiary_color || "#06b6d4",
    tertiaryHover: theme?.tertiary_hover || "#0891b2",
    danger: theme?.danger_color || "#ef4444",
    dangerHover: "#dc2626",
  }), [theme]);

  const buttonStyle = theme?.button_style || "modern";
  const getButtonClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark),
    [buttonStyle, themeColors, isDark]
  );

  const btnPrimary = getButtonClasses.primary;
  const btnSecondary = getButtonClasses.secondary;
  const btnTertiary = getButtonClasses.tertiary;
  const btnDanger = getButtonClasses.danger;

  // State
  const [expenses, setExpenses] = useState([]);
  const [totalAmount, setTotalAmount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [summary, setSummary] = useState(null);
  const [categories, setCategories] = useState([]);
  const [accounts, setAccounts] = useState([]);
  const [activeDrawer, setActiveDrawer] = useState(null);

  // Filters
  const [filters, setFilters] = useState({
    search: "",
    from: "",
    to: "",
    category_id: "",
    payment_method: "",
  });
  const [pagination, setPagination] = useState({ current_page: 1, last_page: 1, total: 0 });

  // Modals
  const [isRecordModalOpen, setIsRecordModalOpen] = useState(false);
  const [isCategoriesModalOpen, setIsCategoriesModalOpen] = useState(false);
  const [editingExpense, setEditingExpense] = useState(null);

  // Form State
  const [form, setForm] = useState({
    expense_category_id: "",
    amount: "",
    date: new Date().toISOString().slice(0, 10),
    payment_method: "cash",
    account_id: "",
    reference_number: "",
    payee: "",
    description: "",
  });
  const [saving, setSaving] = useState(false);

  // React-Select Custom Styles
  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        minHeight: "36px",
        height: "36px",
        borderRadius: "0.75rem",
        backgroundColor: isDark ? "rgba(30, 41, 59, 0.7)" : "rgba(255, 255, 255, 0.7)",
        borderColor: state.isFocused
          ? (theme?.primary_color || "#3b82f6")
          : isDark
          ? "rgba(51, 65, 85, 0.8)"
          : "rgba(229, 231, 235, 0.8)",
        boxShadow: state.isFocused ? `0 0 0 2px ${theme?.primary_color || "#3b82f6"}33` : "none",
        fontSize: "0.75rem",
        color: isDark ? "#f8fafc" : "#1e293b",
        backdropFilter: "blur(8px)",
        transition: "all 0.15s ease",
        "&:hover": {
          borderColor: theme?.primary_color || "#3b82f6",
        },
      }),
      valueContainer: (base) => ({
        ...base,
        padding: "0 8px",
      }),
      input: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#1e293b",
        margin: 0,
        padding: 0,
      }),
      placeholder: (base) => ({
        ...base,
        color: isDark ? "rgba(148, 163, 184, 0.7)" : "rgba(156, 163, 175, 0.8)",
        fontSize: "0.75rem",
      }),
      singleValue: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#1e293b",
        fontSize: "0.75rem",
        fontWeight: "500",
      }),
      indicatorsContainer: (base) => ({
        ...base,
        height: "36px",
      }),
      dropdownIndicator: (base) => ({
        ...base,
        padding: "4px",
        color: isDark ? "rgba(148, 163, 184, 0.7)" : "rgba(156, 163, 175, 0.8)",
      }),
      menuPortal: (base) => ({ ...base, zIndex: 99999 }),
      menu: (base) => ({
        ...base,
        backgroundColor: isDark ? "rgba(15, 23, 42, 0.98)" : "rgba(255, 255, 255, 0.98)",
        border: isDark ? "1px solid rgba(51, 65, 85, 0.8)" : "1px solid rgba(226, 232, 240, 0.9)",
        boxShadow: "0 20px 25px -5px rgba(0, 0, 0, 0.2), 0 8px 10px -6px rgba(0, 0, 0, 0.1)",
        borderRadius: "0.75rem",
        overflow: "hidden",
        backdropFilter: "blur(16px)",
      }),
      option: (base, state) => ({
        ...base,
        backgroundColor: state.isSelected
          ? (theme?.primary_color || "#3b82f6")
          : state.isFocused
          ? isDark
            ? "rgba(51, 65, 85, 0.6)"
            : "rgba(241, 245, 249, 0.9)"
          : "transparent",
        color: state.isSelected ? "#ffffff" : isDark ? "#f8fafc" : "#1e293b",
        fontSize: "0.75rem",
        cursor: "pointer",
        padding: "7px 12px",
        transition: "background-color 0.1s ease",
      }),
    }),
    [isDark, theme]
  );

  // React-select options
  const categoryOptions = useMemo(
    () => [
      { value: "", label: "All Categories" },
      ...categories.map((c) => ({ value: String(c.id), label: c.name })),
    ],
    [categories]
  );

  const paymentMethodOptions = useMemo(
    () => [
      { value: "", label: "All Payment Methods" },
      ...PAYMENT_METHODS,
    ],
    []
  );

  const modalCategoryOptions = useMemo(
    () => categories.map((c) => ({ value: String(c.id), label: c.name })),
    [categories]
  );

  const modalAccountOptions = useMemo(
    () => [
      { value: "", label: "Default Account for Method" },
      ...accounts.map((a) => ({
        value: String(a.id),
        label: `${a.name} (${a.type}) • Bal: ${formatCurrency(a.current_balance)}`,
      })),
    ],
    [accounts, formatCurrency]
  );

  // Fetch initial data
  const fetchData = useCallback(async (page = 1) => {
    setLoading(true);
    try {
      const params = {
        page,
        ...filters,
      };
      Object.keys(params).forEach((k) => !params[k] && delete params[k]);

      const [expRes, sumRes, catRes, accRes, drawerRes] = await Promise.all([
        axios.get("/api/expenses", { params }),
        axios.get("/api/expenses/summary"),
        axios.get("/api/expense-categories"),
        axios.get("/api/accounts"),
        axios.get("/api/cash-drawer/current").catch(() => ({ data: { active: false } })),
      ]);

      setExpenses(expRes.data.expenses?.data || []);
      setTotalAmount(expRes.data.total_amount || 0);
      setPagination({
        current_page: expRes.data.expenses?.current_page || 1,
        last_page: expRes.data.expenses?.last_page || 1,
        total: expRes.data.expenses?.total || 0,
      });

      setSummary(sumRes.data);
      setCategories(catRes.data || []);
      setAccounts(accRes.data.accounts || []);
      setActiveDrawer((drawerRes.data?.is_open || drawerRes.data?.active) ? drawerRes.data.session : null);
    } catch {
      toast.error("Failed to load expenses.");
    } finally {
      setLoading(false);
    }
  }, [filters]);

  useEffect(() => {
    fetchData(1);
  }, [fetchData]);

  // Handle open modal
  function openCreateModal() {
    setEditingExpense(null);
    setForm({
      expense_category_id: categories[0]?.id ? String(categories[0].id) : "",
      amount: "",
      date: new Date().toISOString().slice(0, 10),
      payment_method: "cash",
      account_id: accounts.find((a) => a.type === "bank")?.id ? String(accounts.find((a) => a.type === "bank")?.id) : "",
      reference_number: "",
      payee: "",
      description: "",
    });
    setIsRecordModalOpen(true);
  }

  function openEditModal(exp) {
    setEditingExpense(exp);
    setForm({
      expense_category_id: String(exp.expense_category_id),
      amount: exp.amount,
      date: exp.date,
      payment_method: exp.payment_method,
      account_id: exp.account_id ? String(exp.account_id) : "",
      reference_number: exp.reference_number || "",
      payee: exp.payee || "",
      description: exp.description || "",
    });
    setIsRecordModalOpen(true);
  }

  // Submit expense
  async function handleSubmit(e) {
    e.preventDefault();
    if (!form.expense_category_id) return toast.error("Please select an expense category.");
    if (!form.amount || Number(form.amount) <= 0) return toast.error("Please enter a valid amount.");

    setSaving(true);
    try {
      if (editingExpense) {
        await axios.put(`/api/expenses/${editingExpense.id}`, form);
        toast.success("Expense updated successfully.");
      } else {
        await axios.post("/api/expenses", form);
        toast.success("Expense recorded successfully.");
      }
      setIsRecordModalOpen(false);
      window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
      window.dispatchEvent(new CustomEvent("accounts-changed"));
      try { localStorage.setItem("cash_drawer_sync", String(Date.now())); } catch {}
      try { localStorage.setItem("accounts_sync", String(Date.now())); } catch {}
      fetchData(pagination.current_page);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to save expense.");
    } finally {
      setSaving(false);
    }
  }

  // Delete expense
  async function handleDelete(exp) {
    if (
      !confirm(
        `Are you sure you want to delete expense "${exp.expense_number}" of ${formatCurrency(
          exp.amount
        )}?\n\nThis will safely restore any deducted cash drawer or bank balance.`
      )
    ) {
      return;
    }

    try {
      await axios.delete(`/api/expenses/${exp.id}`);
      toast.success("Expense deleted and balances restored.");
      window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
      window.dispatchEvent(new CustomEvent("accounts-changed"));
      try { localStorage.setItem("cash_drawer_sync", String(Date.now())); } catch {}
      try { localStorage.setItem("accounts_sync", String(Date.now())); } catch {}
      fetchData(pagination.current_page);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to delete expense.");
    }
  }

  // Quick Date Range Helpers
  function setQuickDate(type) {
    const todayStr = new Date().toISOString().slice(0, 10);
    if (type === "today") {
      setFilters((f) => ({ ...f, from: todayStr, to: todayStr }));
    } else if (type === "this_month") {
      const monthStart = `${todayStr.slice(0, 7)}-01`;
      setFilters((f) => ({ ...f, from: monthStart, to: todayStr }));
    } else if (type === "clear") {
      setFilters((f) => ({ ...f, from: "", to: "" }));
    }
  }

  return (
    <div className="w-full max-w-full overflow-hidden space-y-4 sm:space-y-6 animate-fadeIn pb-12">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 sm:gap-4">
        <div className="min-w-0">
          <h1 className="text-xl sm:text-2xl font-bold tracking-tight text-gray-900 dark:text-white flex items-center gap-2 sm:gap-2.5 truncate">
            <BanknotesIcon className="w-6 h-6 sm:w-7 sm:h-7 text-blue-600 dark:text-blue-400 shrink-0" />
            <span>Operating Expenses (OpEx)</span>
          </h1>
          <p className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 mt-0.5 truncate">
            Track store overheads, utilities, salaries, and payouts with automatic drawer reconciliation
          </p>
        </div>
        <div className="flex items-center gap-2 w-full sm:w-auto shrink-0">
          <button
            onClick={() => setIsCategoriesModalOpen(true)}
            className={`flex-1 sm:flex-none flex items-center justify-center gap-1.5 px-3.5 py-2 text-xs font-semibold theme-btn-secondary ${btnSecondary.className}`}
            style={btnSecondary.style}
          >
            <TagIcon className="w-4 h-4 text-blue-500" />
            <span>Categories</span>
          </button>
          <button
            onClick={openCreateModal}
            className={`flex-1 sm:flex-none flex items-center justify-center gap-1.5 px-4 py-2 text-xs font-semibold theme-btn-primary ${btnPrimary.className}`}
            style={btnPrimary.style}
          >
            <PlusIcon className="w-4 h-4" />
            <span>Record Expense</span>
          </button>
        </div>
      </div>

      {/* KPI Stats Grid */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-2.5 sm:gap-4">
        {/* Month Expenses */}
        <div className="relative overflow-hidden p-3.5 sm:p-5 rounded-2xl bg-white/80 dark:bg-slate-900/80 border border-gray-100 dark:border-slate-800 shadow-sm backdrop-blur-xl">
          <div className="flex items-center justify-between text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
            <span>This Month</span>
            <CalendarDaysIcon className="w-4 h-4 sm:w-5 sm:h-5 text-indigo-500 shrink-0" />
          </div>
          <div className="text-lg sm:text-2xl font-black text-gray-900 dark:text-white mt-1 sm:mt-2 truncate">
            {formatCurrency(summary?.month_total || 0)}
          </div>
          <div className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400 mt-0.5 sm:mt-1 truncate">
            Operational overhead
          </div>
        </div>

        {/* Today's Expenses */}
        <div className="relative overflow-hidden p-3.5 sm:p-5 rounded-2xl bg-white/80 dark:bg-slate-900/80 border border-gray-100 dark:border-slate-800 shadow-sm backdrop-blur-xl">
          <div className="flex items-center justify-between text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
            <span>Today's Outflows</span>
            <BanknotesIcon className="w-4 h-4 sm:w-5 sm:h-5 text-rose-500 shrink-0" />
          </div>
          <div className="text-lg sm:text-2xl font-black text-rose-600 dark:text-rose-400 mt-1 sm:mt-2 truncate">
            {formatCurrency(summary?.today_total || 0)}
          </div>
          <div className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400 mt-0.5 sm:mt-1 truncate">
            Paid out today
          </div>
        </div>

        {/* Cash Drawer Outflows */}
        <div className="relative overflow-hidden p-3.5 sm:p-5 rounded-2xl bg-white/80 dark:bg-slate-900/80 border border-gray-100 dark:border-slate-800 shadow-sm backdrop-blur-xl">
          <div className="flex items-center justify-between text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
            <span>Cash Drawer Out</span>
            <BanknotesIcon className="w-4 h-4 sm:w-5 sm:h-5 text-amber-500 shrink-0" />
          </div>
          <div className="text-lg sm:text-2xl font-black text-gray-900 dark:text-white mt-1 sm:mt-2 truncate">
            {formatCurrency(summary?.cash_total || 0)}
          </div>
          <div className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400 mt-0.5 sm:mt-1 truncate">
            Drawer payouts
          </div>
        </div>

        {/* Bank & Digital Outflows */}
        <div className="relative overflow-hidden p-3.5 sm:p-5 rounded-2xl bg-white/80 dark:bg-slate-900/80 border border-gray-100 dark:border-slate-800 shadow-sm backdrop-blur-xl">
          <div className="flex items-center justify-between text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
            <span>Bank & Digital</span>
            <BuildingLibraryIcon className="w-4 h-4 sm:w-5 sm:h-5 text-emerald-500 shrink-0" />
          </div>
          <div className="text-lg sm:text-2xl font-black text-gray-900 dark:text-white mt-1 sm:mt-2 truncate">
            {formatCurrency(summary?.bank_total || 0)}
          </div>
          <div className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400 mt-0.5 sm:mt-1 truncate">
            Top: {summary?.top_category_name || "None"}
          </div>
        </div>
      </div>

      {/* Filter Toolbar */}
      <div className="p-3 sm:p-4 rounded-2xl bg-white/80 dark:bg-slate-900/80 border border-gray-100 dark:border-slate-800 shadow-sm backdrop-blur-xl space-y-3">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-12 gap-2.5 sm:gap-3 items-center">
          {/* Search */}
          <div className="relative lg:col-span-3">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
            <input
              type="text"
              placeholder="Search payee, notes, ref #..."
              value={filters.search}
              onChange={(e) => setFilters({ ...filters, search: e.target.value })}
              className="w-full pl-9 pr-3 py-2 text-xs rounded-xl border border-gray-200/80 dark:border-slate-700 bg-white/50 dark:bg-slate-800/50 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>

          {/* React-Select: Category Filter */}
          <div className="lg:col-span-3">
            <Select
              options={categoryOptions}
              value={categoryOptions.find((c) => c.value === filters.category_id) || categoryOptions[0]}
              onChange={(opt) => setFilters({ ...filters, category_id: opt?.value || "" })}
              styles={selectStyles}
              isSearchable
              placeholder="All Categories"
              menuPortalTarget={typeof document !== "undefined" ? document.body : null}
            />
          </div>

          {/* React-Select: Payment Method Filter */}
          <div className="lg:col-span-3">
            <Select
              options={paymentMethodOptions}
              value={paymentMethodOptions.find((m) => m.value === filters.payment_method) || paymentMethodOptions[0]}
              onChange={(opt) => setFilters({ ...filters, payment_method: opt?.value || "" })}
              styles={selectStyles}
              isSearchable={false}
              placeholder="All Payment Methods"
              menuPortalTarget={typeof document !== "undefined" ? document.body : null}
            />
          </div>

          {/* Date Range Inputs */}
          <div className="lg:col-span-3 flex items-center gap-1.5">
            <input
              type="date"
              value={filters.from}
              onChange={(e) => setFilters({ ...filters, from: e.target.value })}
              className="w-1/2 px-2.5 py-1.5 text-xs rounded-xl border border-gray-200/80 dark:border-slate-700 bg-white/50 dark:bg-slate-800/50 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <span className="text-xs text-gray-400">to</span>
            <input
              type="date"
              value={filters.to}
              onChange={(e) => setFilters({ ...filters, to: e.target.value })}
              className="w-1/2 px-2.5 py-1.5 text-xs rounded-xl border border-gray-200/80 dark:border-slate-700 bg-white/50 dark:bg-slate-800/50 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
        </div>

        {/* Quick Date Pills & Reset */}
        <div className="flex flex-wrap items-center justify-between gap-2 pt-1 border-t border-gray-100/60 dark:border-slate-800/60 text-[11px]">
          <div className="flex items-center gap-1.5">
            <span className="text-gray-400 text-xs">Quick:</span>
            <button
              onClick={() => setQuickDate("today")}
              className={`px-2.5 py-1 text-[11px] font-medium theme-btn-tertiary ${btnTertiary.className}`}
              style={btnTertiary.style}
            >
              Today
            </button>
            <button
              onClick={() => setQuickDate("this_month")}
              className={`px-2.5 py-1 text-[11px] font-medium theme-btn-tertiary ${btnTertiary.className}`}
              style={btnTertiary.style}
            >
              This Month
            </button>
          </div>

          {(filters.from || filters.to || filters.category_id || filters.payment_method || filters.search) && (
            <button
              onClick={() =>
                setFilters({ search: "", from: "", to: "", category_id: "", payment_method: "" })
              }
              className={`px-2.5 py-1 text-[11px] font-medium theme-btn-danger ${btnDanger.className}`}
              style={btnDanger.style}
            >
              Reset Filters
            </button>
          )}
        </div>
      </div>

      {/* Expenses Table Card (Fully Responsive Scroll) */}
      <div className="w-full overflow-hidden rounded-2xl bg-white/80 dark:bg-slate-900/80 border border-gray-100 dark:border-slate-800 shadow-sm backdrop-blur-xl">
        <div className="w-full overflow-x-auto">
          <table className="w-full text-left border-collapse min-w-[680px]">
            <thead>
              <tr className="border-b border-gray-100 dark:border-slate-800/80 bg-gray-50/50 dark:bg-slate-800/30 text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">
                <th className="py-3 px-4">Date / Number</th>
                <th className="py-3 px-4">Category</th>
                <th className="py-3 px-4">Payee & Notes</th>
                <th className="py-3 px-4">Payment Method / Account</th>
                <th className="py-3 px-4 text-right">Amount</th>
                <th className="py-3 px-4 text-center">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-slate-800 text-xs">
              {loading ? (
                <tr>
                  <td colSpan="6" className="py-12 text-center text-gray-400">
                    <ArrowPathIcon className="w-6 h-6 animate-spin mx-auto mb-2 opacity-50" />
                    Loading expenses...
                  </td>
                </tr>
              ) : expenses.length === 0 ? (
                <tr>
                  <td colSpan="6" className="py-12 text-center text-gray-400">
                    No expenses found matching the current filters.
                  </td>
                </tr>
              ) : (
                expenses.map((exp) => (
                  <tr
                    key={exp.id}
                    className="hover:bg-gray-50/60 dark:hover:bg-slate-800/40 transition group"
                  >
                    {/* Date / Number */}
                    <td className="py-3 px-4 whitespace-nowrap">
                      <div className="font-semibold text-gray-900 dark:text-white">
                        {new Date(exp.date).toLocaleDateString(undefined, {
                          month: "short",
                          day: "numeric",
                          year: "numeric",
                        })}
                      </div>
                      <div className="text-[10px] font-mono text-gray-400 dark:text-slate-500">
                        {exp.expense_number}
                      </div>
                    </td>

                    {/* Category */}
                    <td className="py-3 px-4 whitespace-nowrap">
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[11px] font-medium bg-blue-50 text-blue-700 dark:bg-blue-950/40 dark:text-blue-300 border border-blue-200/50 dark:border-blue-800/50">
                        <TagIcon className="w-3.5 h-3.5" />
                        {exp.category?.name || "Uncategorized"}
                      </span>
                    </td>

                    {/* Payee & Description */}
                    <td className="py-3 px-4 max-w-xs">
                      {exp.payee && (
                        <div className="font-medium text-gray-900 dark:text-white truncate">
                          {exp.payee}
                        </div>
                      )}
                      <div className="text-[11px] text-gray-500 dark:text-slate-400 truncate">
                        {exp.description || "—"}
                      </div>
                      {exp.reference_number && (
                        <div className="text-[10px] font-mono text-gray-400 truncate">
                          Ref: {exp.reference_number}
                        </div>
                      )}
                    </td>

                    {/* Payment Method / Account */}
                    <td className="py-3 px-4 whitespace-nowrap">
                      <div className="flex items-center gap-1.5">
                        <span className="capitalize font-medium text-gray-800 dark:text-slate-200">
                          {exp.payment_method.replace("_", " ")}
                        </span>
                        {(exp.cash_drawer_session_id || exp.drawer_session_id) && (
                          <span className="text-[10px] px-1.5 py-0.5 rounded bg-emerald-50 text-emerald-700 dark:bg-emerald-950/30 dark:text-emerald-400 font-mono">
                            Drawer #{exp.cash_drawer_session_id || exp.drawer_session_id}
                          </span>
                        )}
                      </div>
                      {exp.account && (
                        <div className="text-[11px] text-gray-400 dark:text-slate-500 truncate max-w-[180px]">
                          {exp.account.name}
                        </div>
                      )}
                    </td>

                    {/* Amount */}
                    <td className="py-3 px-4 text-right whitespace-nowrap">
                      <span className="text-sm font-bold text-gray-900 dark:text-white">
                        {formatCurrency(exp.amount)}
                      </span>
                    </td>

                    {/* Actions */}
                    <td className="py-3 px-4 text-center whitespace-nowrap">
                      <div className="flex items-center justify-center gap-1 opacity-80 group-hover:opacity-100 transition">
                        <button
                          onClick={() => openEditModal(exp)}
                          className="p-1.5 text-gray-400 hover:text-blue-600 dark:hover:text-blue-400 rounded-lg hover:bg-blue-50 dark:hover:bg-blue-950/30 transition"
                          title="Edit"
                        >
                          <PencilSquareIcon className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleDelete(exp)}
                          className="p-1.5 text-gray-400 hover:text-rose-600 dark:hover:text-rose-400 rounded-lg hover:bg-rose-50 dark:hover:bg-rose-950/30 transition"
                          title="Delete"
                        >
                          <TrashIcon className="w-4 h-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Table Footer with Total & Pagination */}
        <div className="flex flex-col sm:flex-row items-center justify-between p-3.5 sm:p-4 border-t border-gray-100 dark:border-slate-800/80 gap-3 text-xs text-gray-500 dark:text-slate-400">
          <div className="text-center sm:text-left">
            Showing {expenses.length} of {pagination.total} records • Total:{" "}
            <strong className="text-gray-900 dark:text-white font-bold">{formatCurrency(totalAmount)}</strong>
          </div>
          {pagination.last_page > 1 && (
            <div className="flex items-center gap-1">
              <button
                disabled={pagination.current_page <= 1}
                onClick={() => fetchData(pagination.current_page - 1)}
                className={`px-2.5 py-1 text-xs font-medium theme-btn-tertiary ${btnTertiary.className} disabled:opacity-40 transition`}
                style={btnTertiary.style}
              >
                Prev
              </button>
              <span className="px-2 text-[11px]">
                {pagination.current_page} / {pagination.last_page}
              </span>
              <button
                disabled={pagination.current_page >= pagination.last_page}
                onClick={() => fetchData(pagination.current_page + 1)}
                className={`px-2.5 py-1 text-xs font-medium theme-btn-tertiary ${btnTertiary.className} disabled:opacity-40 transition`}
                style={btnTertiary.style}
              >
                Next
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Record / Edit Expense Modal (Fully Responsive Dialog) */}
      {isRecordModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4 bg-black/50 backdrop-blur-sm animate-fadeIn">
          <div className="relative w-full max-w-lg max-h-[92vh] overflow-hidden flex flex-col rounded-2xl bg-white/95 dark:bg-slate-900/95 border border-white/20 dark:border-slate-800 shadow-2xl backdrop-blur-xl">
            {/* Modal Header */}
            <div className="flex items-center justify-between px-5 sm:px-6 py-3.5 border-b border-gray-100 dark:border-slate-800 bg-gradient-to-r from-blue-50/50 via-transparent to-transparent dark:from-blue-950/20 shrink-0">
              <div className="flex items-center gap-2.5 sm:gap-3">
                <div className="flex items-center justify-center w-9 h-9 sm:w-10 sm:h-10 rounded-xl bg-blue-500/10 text-blue-600 dark:text-blue-400 shrink-0">
                  <BanknotesIcon className="w-5 h-5" />
                </div>
                <div>
                  <h2 className="text-base sm:text-lg font-bold text-gray-900 dark:text-white">
                    {editingExpense ? "Edit Expense" : "Record Expense"}
                  </h2>
                  <p className="text-[11px] text-gray-500 dark:text-slate-400">
                    {editingExpense
                      ? `Editing ${editingExpense.expense_number}`
                      : "Enter details of the operational payout"}
                  </p>
                </div>
              </div>
              <button
                onClick={() => setIsRecordModalOpen(false)}
                className="p-1.5 text-gray-400 hover:text-gray-600 dark:hover:text-slate-200 rounded-lg transition"
              >
                ✕
              </button>
            </div>

            {/* Modal Form */}
            <form onSubmit={handleSubmit} className="p-4 sm:p-6 space-y-3.5 overflow-y-auto flex-1">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 sm:gap-4">
                {/* Category (React-Select) */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 dark:text-slate-300 mb-1">
                    Expense Category *
                  </label>
                  <Select
                    options={modalCategoryOptions}
                    value={modalCategoryOptions.find((c) => c.value === form.expense_category_id) || null}
                    onChange={(opt) => setForm({ ...form, expense_category_id: opt?.value || "" })}
                    styles={selectStyles}
                    isSearchable
                    placeholder="Select Category"
                    menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  />
                </div>

                {/* Amount */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 dark:text-slate-300 mb-1">
                    Amount *
                  </label>
                  <input
                    type="number"
                    step="0.01"
                    min="0.01"
                    required
                    placeholder="0.00"
                    value={form.amount}
                    onChange={(e) => setForm({ ...form, amount: e.target.value })}
                    className="w-full px-3 py-2 text-sm font-semibold rounded-xl border border-gray-200 dark:border-slate-700 bg-white/70 dark:bg-slate-800/70 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500 h-9"
                  />
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 sm:gap-4">
                {/* Date */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 dark:text-slate-300 mb-1">
                    Date *
                  </label>
                  <input
                    type="date"
                    required
                    value={form.date}
                    onChange={(e) => setForm({ ...form, date: e.target.value })}
                    className="w-full px-3 py-2 text-xs rounded-xl border border-gray-200 dark:border-slate-700 bg-white/70 dark:bg-slate-800/70 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500 h-9"
                  />
                </div>

                {/* Payment Method (React-Select) */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 dark:text-slate-300 mb-1">
                    Payment Method *
                  </label>
                  <Select
                    options={PAYMENT_METHODS}
                    value={PAYMENT_METHODS.find((m) => m.value === form.payment_method) || PAYMENT_METHODS[0]}
                    onChange={(opt) => setForm({ ...form, payment_method: opt?.value || "cash" })}
                    styles={selectStyles}
                    isSearchable={false}
                    menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  />
                </div>
              </div>

              {/* Drawer Status or Account Selector */}
              {form.payment_method === "cash" ? (
                <div className="p-3 rounded-xl bg-amber-50/60 dark:bg-amber-950/20 border border-amber-200/60 dark:border-amber-800/40 text-xs text-amber-800 dark:text-amber-300 flex items-center justify-between">
                  <div className="min-w-0 pr-2">
                    <strong>Active Drawer:</strong>{" "}
                    {activeDrawer ? (
                      <span>Shift #{activeDrawer.id}</span>
                    ) : (
                      <span className="text-rose-600 dark:text-rose-400 font-semibold">No active drawer open</span>
                    )}
                  </div>
                  <span className="text-[10px] uppercase font-mono px-1.5 py-0.5 rounded bg-amber-200/50 dark:bg-amber-800/40 shrink-0">
                    Cash Out
                  </span>
                </div>
              ) : (
                <div>
                  <label className="block text-xs font-medium text-gray-700 dark:text-slate-300 mb-1">
                    Bank / Digital Account
                  </label>
                  <Select
                    options={modalAccountOptions}
                    value={modalAccountOptions.find((a) => a.value === form.account_id) || modalAccountOptions[0]}
                    onChange={(opt) => setForm({ ...form, account_id: opt?.value || "" })}
                    styles={selectStyles}
                    isSearchable
                    placeholder="Default Account for Method"
                    menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  />
                </div>
              )}

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 sm:gap-4">
                {/* Payee */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 dark:text-slate-300 mb-1">
                    Payee (Recipient)
                  </label>
                  <input
                    type="text"
                    placeholder="e.g. Landlord, WAPDA, Cleaner"
                    value={form.payee}
                    onChange={(e) => setForm({ ...form, payee: e.target.value })}
                    className="w-full px-3 py-2 text-xs rounded-xl border border-gray-200 dark:border-slate-700 bg-white/70 dark:bg-slate-800/70 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500 h-9"
                  />
                </div>

                {/* Reference Number */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 dark:text-slate-300 mb-1">
                    Reference / Cheque #
                  </label>
                  <input
                    type="text"
                    placeholder="e.g. CHQ-10029, Consumer #"
                    value={form.reference_number}
                    onChange={(e) => setForm({ ...form, reference_number: e.target.value })}
                    className="w-full px-3 py-2 text-xs rounded-xl border border-gray-200 dark:border-slate-700 bg-white/70 dark:bg-slate-800/70 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500 h-9"
                  />
                </div>
              </div>

              {/* Description */}
              <div>
                <label className="block text-xs font-medium text-gray-700 dark:text-slate-300 mb-1">
                  Notes / Description
                </label>
                <textarea
                  rows="2"
                  placeholder="Optional details or remarks about this expense..."
                  value={form.description}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                  className="w-full px-3 py-2 text-xs rounded-xl border border-gray-200 dark:border-slate-700 bg-white/70 dark:bg-slate-800/70 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
                />
              </div>

              {/* Modal Actions */}
              <div className="flex items-center justify-end gap-2.5 pt-3 border-t border-gray-100 dark:border-slate-800">
                <button
                  type="button"
                  onClick={() => setIsRecordModalOpen(false)}
                  className={`px-4 py-2 text-xs font-medium theme-btn-secondary ${btnSecondary.className}`}
                  style={btnSecondary.style}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={saving}
                  className={`px-5 py-2 text-xs font-semibold theme-btn-primary ${btnPrimary.className}`}
                  style={btnPrimary.style}
                >
                  {saving ? "Saving..." : editingExpense ? "Update Expense" : "Save Expense"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Categories Modal */}
      <CategoriesModal
        isOpen={isCategoriesModalOpen}
        onClose={() => setIsCategoriesModalOpen(false)}
        categories={categories}
        onRefresh={() => {
          axios.get("/api/expense-categories").then((res) => setCategories(res.data));
        }}
      />
    </div>
  );
}
