// resources/js/components/sidebar-templates/MinimalSidebar.jsx
import React, { useState, useMemo } from "react";
import { Link, useLocation } from "react-router-dom";
import {
  HomeIcon,
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

export default function MinimalSidebar() {
  const { pathname } = useLocation();
  const { theme } = useTheme();
  const { loading: permsLoading, has } = usePermissions();

  const brandName = "Karobar";

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

  const isActive = (path) => pathname === path || pathname.startsWith(path + "/");

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

  // Minimal styling - ultra clean, no decorations
  const shell = "relative h-screen bg-white dark:bg-black border-r border-gray-100 dark:border-gray-900";
  const card = "relative flex h-full flex-col w-56 overflow-hidden";
  
  const scrollAreaCls = "flex-1 overflow-y-auto min-h-0 py-4 px-3 [&::-webkit-scrollbar]:w-0";

  const itemBase = "group flex items-center gap-3 px-3 py-2 text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white transition-colors duration-150";
  const itemActive = "text-gray-900 dark:text-white font-medium";

  const renderSectionHeader = (section, index) => {
    return (
      <div key={`sec-${section.name}-${index}`} className="px-3 py-2 mt-6 first:mt-0">
        <span className="text-xs font-medium text-gray-400 dark:text-gray-600 uppercase tracking-wider">{section.name}</span>
      </div>
    );
  };

  return (
    <aside className={shell}>
      <div className={card}>
        {/* Header - minimal text only */}
        <div className="px-5 py-6">
          <span className="text-lg font-light text-gray-900 dark:text-white tracking-tight">{brandName}</span>
        </div>

        {/* Navigation */}
        <nav className={scrollAreaCls} role="navigation" aria-label="Main navigation">
          {menu.map((item, i) => {
            if (item.type === "section") return renderSectionHeader(item, i);
            const active = isActive(item.path);
            const sectionConfig = getSectionConfig(item);

            return (
              <Link
                key={item.path}
                to={item.path}
                className={`${itemBase} ${active ? itemActive : ''}`}
                aria-current={active ? "page" : undefined}
              >
                <span className="text-gray-400 dark:text-gray-600">
                  {React.cloneElement(item.icon, { className: "w-4 h-4" })}
                </span>
                <span>{item.name}</span>
                {active && (
                  <span 
                    className="ml-auto w-1 h-1 rounded-full"
                    style={{ backgroundColor: sectionConfig.baseColor }}
                  />
                )}
              </Link>
            );
          })}
        </nav>
      </div>
    </aside>
  );
}

