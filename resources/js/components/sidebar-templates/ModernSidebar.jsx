// resources/js/components/sidebar-templates/ModernSidebar.jsx
import React, { useState, useRef, useEffect, useMemo } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import {
  HomeIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  BuildingStorefrontIcon,
  UsersIcon,
  Squares2X2Icon,
  TagIcon,
  CubeIcon,
  ClipboardDocumentListIcon,
  ArrowUturnLeftIcon,
  DocumentCurrencyDollarIcon,
  ArrowUturnDownIcon,
  ClipboardDocumentCheckIcon,
  ArrowsRightLeftIcon,
  ChartBarIcon,
  Cog6ToothIcon,
  UserGroupIcon,
  KeyIcon,
  ShoppingCartIcon,
  UserPlusIcon,
  TruckIcon,
  CurrencyDollarIcon,
  ClipboardDocumentIcon,
  DocumentTextIcon,
  CalculatorIcon,
  ClipboardDocumentListIcon as ReportIcon,
} from "@heroicons/react/24/outline";
import { usePermissions } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext";
import Logo from "./Logo";

const SECTION_CONFIG = {
  dashboard: { key: 'primary' },
  core: { key: 'secondary' },
  invoices: { key: 'tertiary' },
  returns: { key: 'primary' },
  transactions: { key: 'secondary' },
  finance: { key: 'tertiary' },
  reports: { key: 'primary' },
  system: { key: 'secondary' },
};

const getThemeColor = (theme, colorKey, variant = 'color') => {
  if (!theme) return '#3b82f6';
  const key = `${colorKey}_${variant}`;
  return theme[key] || '#3b82f6';
};

export default function ModernSidebar() {
  const [collapsed, setCollapsed] = useState(false);
  const [hoveredSection, setHoveredSection] = useState(null);
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { theme } = useTheme();
  const { loading: permsLoading, has } = usePermissions();

  const brandName = "Karobar App";

  const rawMenu = useMemo(() => [
    { name: "Dashboard", path: "/dashboard", icon: <HomeIcon className="w-5 h-5" />, standalone: true },
    { type: "section", name: "Management", key: "core", icon: <CubeIcon className="w-5 h-5" /> },
    { name: "Products", path: "/products", icon: <CubeIcon className="w-5 h-5" />, perm: "product.view", parent: "core" },
    { name: "Categories", path: "/categories", icon: <Squares2X2Icon className="w-5 h-5" />, perm: "category.view", parent: "core" },
    { name: "Brands", path: "/brands", icon: <TagIcon className="w-5 h-5" />, perm: "brand.view", parent: "core" },
    { name: "Suppliers", path: "/suppliers", icon: <TruckIcon className="w-5 h-5" />, perm: "supplier.view", parent: "core" },
    { name: "Customers", path: "/customers", icon: <UserPlusIcon className="w-5 h-5" />, perm: "customer.view", parent: "core" },
    { type: "section", name: "Invoices", key: "invoices", icon: <DocumentTextIcon className="w-5 h-5" /> },
    { name: "Purchase Invoice", path: "/purchase-invoices", icon: <ClipboardDocumentListIcon className="w-5 h-5" />, perm: "purchase-invoice.view", parent: "invoices" },
    { name: "Sale Invoice", path: "/sale-invoices", icon: <DocumentCurrencyDollarIcon className="w-5 h-5" />, perm: "sale-invoice.view", parent: "invoices" },
    { type: "section", name: "Returns", key: "returns", icon: <ArrowUturnLeftIcon className="w-5 h-5" /> },
    { name: "Purchase Return", path: "/purchase-returns", icon: <ArrowUturnDownIcon className="w-5 h-5" />, perm: "purchase-return.view", parent: "returns" },
    { name: "Sale Return", path: "/sale-returns", icon: <ArrowUturnLeftIcon className="w-5 h-5" />, perm: "sale-return.view", parent: "returns" },
    { type: "section", name: "Transactions", key: "transactions", icon: <ShoppingCartIcon className="w-5 h-5" /> },
    { name: "Purchase Orders", path: "/purchase-orders", icon: <ClipboardDocumentCheckIcon className="w-5 h-5" />, perm: "purchase-order.view", parent: "transactions" },
    { name: "Stock Adjustments", path: "/stock-adjustments", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "stock-adjustment.view", parent: "transactions" },
    { type: "section", name: "Finance", key: "finance", icon: <CurrencyDollarIcon className="w-5 h-5" /> },
    { name: "Supplier Ledger", path: "/supplier-ledger", icon: <BuildingStorefrontIcon className="w-5 h-5" />, perm: "ledger.supplier.view", parent: "finance" },
    { name: "Customer Ledger", path: "/customer-ledger", icon: <UsersIcon className="w-5 h-5" />, perm: "ledger.customer.view", parent: "finance" },
    { type: "section", name: "Reports", key: "reports", icon: <ChartBarIcon className="w-5 h-5" /> },
    { name: "Current Stock", path: "/reports/current-stock", icon: <CubeIcon className="w-5 h-5" />, perm: "report.current-stock.view", parent: "reports" },
    { name: "Cost of Sale", path: "/reports/cost-of-sale", icon: <CalculatorIcon className="w-5 h-5" />, perm: "report.cost-of-sale.view", parent: "reports" },
    { name: "Purchase Detail", path: "/reports/purchase-detail", icon: <ClipboardDocumentIcon className="w-5 h-5" />, perm: "report.purchase-detail.view", parent: "reports" },
    { name: "Sale Detail", path: "/reports/sale-detail", icon: <DocumentCurrencyDollarIcon className="w-5 h-5" />, perm: "report.sale-detail.view", parent: "reports" },
    { name: "Stock Adjustment", path: "/reports/stock-adjustment", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "report.stock-adjustment.view", parent: "reports" },
    { name: "Product Comprehensive", path: "/reports/product-comprehensive", icon: <ReportIcon className="w-5 h-5" />, perm: "report.product-comprehensive.view", parent: "reports" },
    { type: "section", name: "System", key: "system", icon: <Cog6ToothIcon className="w-5 h-5" /> },
    { name: "Settings", path: "/settings", icon: <Cog6ToothIcon className="w-5 h-5" />, perm: "settings.view", parent: "system" },
    { name: "Users", path: "/users", icon: <UserGroupIcon className="w-5 h-5" />, perm: "user.view", parent: "system" },
    { name: "Roles", path: "/roles", icon: <KeyIcon className="w-5 h-5" />, perm: "role.view", parent: "system" },
  ], []);

  const menu = useMemo(() => {
    const visible = [];
    for (let i = 0; i < rawMenu.length; i++) {
      const it = rawMenu[i];
      if (it.type === "section") { visible.push(it); continue; }
      const allowed = it.perm ? (!permsLoading && has(it.perm)) : true;
      if (allowed) visible.push(it);
    }
    const cleaned = [];
    for (let i = 0; i < visible.length; i++) {
      const it = visible[i];
      if (it.type === "section") {
        let keep = false;
        for (let j = i + 1; j < visible.length; j++) {
          if (!visible[j].type) { keep = true; break; }
          if (visible[j].type === "section") break;
        }
        if (keep) cleaned.push(it);
      } else cleaned.push(it);
    }
    if (cleaned.length && cleaned[cleaned.length - 1].type === "section") cleaned.pop();
    return cleaned;
  }, [rawMenu, permsLoading, has]);

  const itemRefs = useRef([]);
  const flatMenu = useMemo(() => menu.filter((m) => !m.type), [menu]);
  const [focusedIndex, setFocusedIndex] = useState(-1);

  useEffect(() => {
    const idx = flatMenu.findIndex((item) => pathname === item.path || pathname.startsWith(item.path + "/"));
    setFocusedIndex(idx);
  }, [pathname, flatMenu]);

  useEffect(() => {
    if (focusedIndex >= 0 && itemRefs.current[focusedIndex]) {
      itemRefs.current[focusedIndex].focus();
    }
  }, [focusedIndex]);

  function handleKeyDown(e) {
    if (flatMenu.length === 0) return;
    switch (e.key) {
      case "ArrowDown": e.preventDefault(); setFocusedIndex((p) => (p + 1) % flatMenu.length); break;
      case "ArrowUp": e.preventDefault(); setFocusedIndex((p) => (p <= 0 ? flatMenu.length - 1 : p - 1)); break;
      case "Home": e.preventDefault(); setFocusedIndex(0); break;
      case "End": e.preventDefault(); setFocusedIndex(flatMenu.length - 1); break;
      case "Enter": case " ": e.preventDefault(); if (focusedIndex >= 0) navigate(flatMenu[focusedIndex].path); break;
      default: break;
    }
  }

  const isActive = (path) => pathname === path || pathname.startsWith(path + "/");
  const widthCls = collapsed ? "w-20" : "w-64";

  const getSectionConfig = (item) => {
    if (item.standalone) {
      const baseColor = getThemeColor(theme, 'primary', 'color');
      const hoverColor = getThemeColor(theme, 'primary', 'hover');
      const lightColor = getThemeColor(theme, 'primary', 'light');
      return { key: 'primary', baseColor, hoverColor, lightColor };
    }
    const sectionKey = item.parent || item.key;
    const config = SECTION_CONFIG[sectionKey] || SECTION_CONFIG.core;
    const colorKey = config.key || 'primary';
    return {
      key: colorKey,
      baseColor: getThemeColor(theme, colorKey, 'color'),
      hoverColor: getThemeColor(theme, colorKey, 'hover'),
      lightColor: getThemeColor(theme, colorKey, 'light'),
    };
  };

  // Modern styling - clean, rounded, soft shadows
  const shell = "relative h-screen bg-slate-50 dark:bg-slate-950";
  const card = `relative flex h-full flex-col ${widthCls} transition-[width] duration-300 overflow-hidden bg-white dark:bg-slate-900 rounded-r-3xl shadow-xl`;
  
  const scrollAreaCls = "flex-1 overflow-y-auto min-h-0 py-3 px-2 [&::-webkit-scrollbar]:w-1.5 [&::-webkit-scrollbar-thumb]:rounded-full [&::-webkit-scrollbar-thumb]:bg-gray-200 dark:[&::-webkit-scrollbar-thumb]:bg-slate-700";

  const itemBase = "group relative flex items-center gap-3 rounded-xl px-3 py-2.5 text-gray-600 dark:text-gray-400 hover:bg-gray-50 dark:hover:bg-slate-800/50 transition-all duration-200";
  const itemActive = "bg-gray-100 dark:bg-slate-800 text-gray-900 dark:text-white font-medium shadow-sm";

  const renderSectionHeader = (section, index) => {
    const config = getSectionConfig(section);
    
    if (collapsed) {
      return (
        <div key={`sec-${section.name}-${index}`} className="flex items-center justify-center py-2 mx-2 mt-2">
          <div 
            className="p-2 rounded-xl"
            style={{ backgroundColor: config.lightColor + '40' }}
          >
            <span style={{ color: config.baseColor }}>{React.cloneElement(section.icon, { className: "w-4 h-4" })}</span>
          </div>
        </div>
      );
    }

    return (
      <div key={`sec-${section.name}-${index}`} className="px-3 py-2 mx-2 mt-3">
        <div className="flex items-center gap-2 px-2">
          <div 
            className="p-1.5 rounded-lg"
            style={{ backgroundColor: config.lightColor + '50' }}
          >
            <span style={{ color: config.baseColor }}>{React.cloneElement(section.icon, { className: "w-3.5 h-3.5" })}</span>
          </div>
          <span className="text-xs font-semibold uppercase tracking-wider text-gray-400 dark:text-gray-500">{section.name}</span>
        </div>
      </div>
    );
  };

  return (
    <aside className={shell}>
      <div className={card}>
        {/* Header with Logo */}
        <div className="sticky top-0 z-10 bg-white dark:bg-slate-900 px-4 py-5">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3 overflow-hidden">
              <div 
                className="h-10 w-10 rounded-2xl flex items-center justify-center overflow-hidden"
                style={{ 
                  background: `linear-gradient(135deg, ${getThemeColor(theme, 'primary', 'color')}, ${getThemeColor(theme, 'secondary', 'color')})` 
                }}
              >
                <Logo className="h-8 w-8" alt={brandName} />
              </div>
              {!collapsed && (
                <div className="flex flex-col min-w-0">
                  <span className="text-sm font-bold text-gray-900 dark:text-white">{brandName}</span>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Navigation */}
        <nav className={scrollAreaCls} role="navigation" tabIndex={0} aria-label="Main navigation" onKeyDown={handleKeyDown}>
          {menu.map((item, i) => {
            if (item.type === "section") return renderSectionHeader(item, i);
            const focusIdx = flatMenu.findIndex((fm) => fm.path === item.path);
            const active = isActive(item.path);
            const sectionConfig = getSectionConfig(item);

            return (
              <Link
                key={item.path}
                to={item.path}
                ref={(el) => (itemRefs.current[focusIdx] = el)}
                className={`${itemBase} ${active ? itemActive : ''}`}
                style={active ? { 
                  backgroundColor: sectionConfig.lightColor + '80',
                } : {}}
                tabIndex={focusedIndex === focusIdx ? 0 : -1}
                onFocus={() => setFocusedIndex(focusIdx)}
                aria-current={active ? "page" : undefined}
                title={collapsed ? item.name : undefined}
              >
                <span 
                  className={["shrink-0", active ? "" : ""].join(" ")}
                  style={{ color: active ? sectionConfig.baseColor : undefined }}
                >
                  {React.cloneElement(item.icon, { className: "w-5 h-5" })}
                </span>
                <span className={`whitespace-nowrap transition-all duration-200 ${collapsed ? 'opacity-0 w-0' : 'opacity-100'}`}>
                  {!collapsed && item.name}
                </span>
                {active && !collapsed && (
                  <div 
                    className="ml-auto w-1.5 h-1.5 rounded-full"
                    style={{ backgroundColor: sectionConfig.baseColor }}
                  />
                )}
              </Link>
            );
          })}
        </nav>

        {/* Footer */}
        <div className="sticky bottom-0 z-10 bg-white dark:bg-slate-900 px-3 py-3">
          <button
            onClick={() => setCollapsed((v) => !v)}
            className="w-full flex items-center justify-center gap-2 rounded-xl px-3 py-2.5 text-sm font-medium text-gray-500 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-slate-800 transition-all duration-200"
            aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          >
            <div 
              className="p-1 rounded-lg"
              style={{ backgroundColor: getThemeColor(theme, 'primary', 'light') }}
            >
              {collapsed ? <ChevronRightIcon className="w-4 h-4" style={{ color: getThemeColor(theme, 'primary', 'color') }} /> : <ChevronLeftIcon className="w-4 h-4" style={{ color: getThemeColor(theme, 'primary', 'color') }} />}
            </div>
            {!collapsed && <span>Collapse</span>}
          </button>
        </div>
      </div>
    </aside>
  );
}

