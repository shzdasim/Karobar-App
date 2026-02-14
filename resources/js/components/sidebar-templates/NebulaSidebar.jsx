// resources/js/components/sidebar-templates/NebulaSidebar.jsx
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

export default function NebulaSidebar() {
  const { pathname } = useLocation();
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

  // Nebula styling - space theme with stars
  const shell = "relative h-screen bg-slate-950 overflow-hidden";
  const card = "relative flex h-full flex-col w-64 overflow-hidden bg-slate-900/90";
  
  const scrollAreaCls = "flex-1 overflow-y-auto min-h-0 py-3 px-2 [&::-webkit-scrollbar]:w-1.5 [&::-webkit-scrollbar-thumb]:rounded-full [&::-webkit-scrollbar-thumb]:bg-purple-500/30";

  const itemBase = "group relative flex items-center gap-3 rounded-xl px-3 py-2.5 text-slate-300 hover:text-white transition-all duration-300";
  const itemActive = "text-white font-medium bg-gradient-to-r from-purple-500/20 via-pink-500/20 to-rose-500/20";

  const renderSectionHeader = (section, index) => {
    return (
      <div key={`sec-${section.name}-${index}`} className="px-3 py-2 mx-2 mt-4">
        <div className="flex items-center gap-2">
          <span className="text-purple-400">{React.cloneElement(section.icon, { className: "w-4 h-4" })}</span>
          <span className="text-xs font-bold uppercase tracking-wider text-purple-400/70">{section.name}</span>
        </div>
      </div>
    );
  };

  return (
    <aside className={shell}>
      {/* Starfield background */}
      <div className="absolute inset-0 pointer-events-none">
        {[...Array(50)].map((_, i) => (
          <div
            key={i}
            className="absolute w-0.5 h-0.5 bg-white rounded-full animate-pulse"
            style={{
              top: `${Math.random() * 100}%`,
              left: `${Math.random() * 100}%`,
              animationDelay: `${Math.random() * 3}s`,
              opacity: Math.random() * 0.5 + 0.2,
            }}
          />
        ))}
        {/* Nebula clouds */}
        <div className="absolute top-0 left-0 w-full h-1/2 bg-gradient-to-b from-purple-900/30 via-transparent to-transparent blur-3xl" />
        <div className="absolute bottom-0 right-0 w-full h-1/2 bg-gradient-to-t from-pink-900/30 via-transparent to-transparent blur-3xl" />
      </div>
      
      <div className={card}>
        {/* Header with Logo */}
        <div className="sticky top-0 z-10 bg-slate-900/80 backdrop-blur-sm border-b border-purple-500/20 px-4 py-4">
          <div className="flex items-center justify-center">
            <div className="relative">
              <div className="h-12 w-12 rounded-full flex items-center justify-center bg-gradient-to-br from-purple-500 via-pink-500 to-rose-500 shadow-lg shadow-purple-500/30 overflow-hidden">
                <Logo className="h-10 w-10" alt={brandName} />
              </div>
              {/* Orbital ring */}
              <div className="absolute inset-0 rounded-full border border-purple-400/30 animate-spin" style={{ animationDuration: '10s' }} />
            </div>
          </div>
          <div className="text-center mt-2">
            <span className="text-sm font-bold text-transparent bg-clip-text bg-gradient-to-r from-purple-400 to-pink-400">{brandName}</span>
          </div>
        </div>

        {/* Navigation */}
        <nav className={scrollAreaCls} role="navigation" aria-label="Main navigation">
          {menu.map((item, i) => {
            if (item.type === "section") return renderSectionHeader(item, i);
            const active = isActive(item.path);

            return (
              <Link
                key={item.path}
                to={item.path}
                className={`${itemBase} ${active ? itemActive : ''}`}
                aria-current={active ? "page" : undefined}
              >
                <span className={active ? "text-pink-400" : "text-slate-400 group-hover:text-purple-400"}>
                  {React.cloneElement(item.icon, { className: "w-5 h-5" })}
                </span>
                <span>{item.name}</span>
                {active && (
                  <div className="absolute right-2 w-2 h-2 rounded-full bg-gradient-to-r from-purple-400 to-pink-400 shadow-lg shadow-purple-400/50" />
                )}
              </Link>
            );
          })}
        </nav>
      </div>
    </aside>
  );
}

