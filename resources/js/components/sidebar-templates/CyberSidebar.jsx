// resources/js/components/sidebar-templates/CyberSidebar.jsx
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

export default function CyberSidebar() {
  const [hoveredItem, setHoveredItem] = useState(null);
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

  const primaryColor = getThemeColor(theme, 'primary', 'color');

  // Cyber styling - neon, tech, futuristic
  const shell = "relative h-screen bg-slate-950";
  const card = "relative flex h-full flex-col w-64 overflow-hidden bg-slate-900 border-r border-cyan-500/30";
  
  const scrollAreaCls = "flex-1 overflow-y-auto min-h-0 py-3 px-2 [&::-webkit-scrollbar]:w-1.5 [&::-webkit-scrollbar-thumb]:rounded-full [&::-webkit-scrollbar-thumb]:bg-cyan-500/50";

  const itemBase = "group relative flex items-center gap-3 px-3 py-2.5 text-slate-400 hover:text-cyan-400 transition-all duration-200 border-l-2 border-transparent";
  const itemActive = "text-cyan-400 font-medium border-cyan-400 bg-cyan-500/10";

  const renderSectionHeader = (section, index) => {
    const config = getSectionConfig(section);
    
    return (
      <div key={`sec-${section.name}-${index}`} className="px-3 py-2 mx-2 mt-4">
        <div className="flex items-center gap-2">
          <span className="text-cyan-500">{React.cloneElement(section.icon, { className: "w-4 h-4" })}</span>
          <span className="text-xs font-bold uppercase tracking-widest text-cyan-500/70">{section.name}</span>
          <div className="flex-1 h-px bg-gradient-to-r from-cyan-500/50 to-transparent" />
        </div>
      </div>
    );
  };

  return (
    <aside className={shell}>
      <div className={card}>
        {/* Header with cyber aesthetic and Logo */}
        <div className="sticky top-0 z-10 bg-slate-900 border-b border-cyan-500/30 px-4 py-4">
          <div className="flex items-center justify-center">
            <div className="relative">
              <div 
                className="h-12 w-12 flex items-center justify-center border-2 border-cyan-400 bg-cyan-500/20 overflow-hidden"
                style={{ clipPath: 'polygon(20% 0%, 80% 0%, 100% 20%, 100% 80%, 80% 100%, 20% 100%, 0% 80%, 0% 20%)' }}
              >
                <Logo className="h-10 w-10" alt={brandName} />
              </div>
              {/* Glow effect */}
              <div className="absolute inset-0 bg-cyan-400/20 blur-xl -z-10" />
            </div>
          </div>
          <div className="text-center mt-3">
            <span className="text-sm font-bold text-cyan-400 tracking-wider">{brandName}</span>
          </div>
        </div>

        {/* Navigation */}
        <nav className={scrollAreaCls} role="navigation" aria-label="Main navigation">
          {menu.map((item, i) => {
            if (item.type === "section") return renderSectionHeader(item, i);
            const active = isActive(item.path);
            const isHovered = hoveredItem === item.path;

            return (
              <Link
                key={item.path}
                to={item.path}
                className={`${itemBase} ${active ? itemActive : ''}`}
                onMouseEnter={() => setHoveredItem(item.path)}
                onMouseLeave={() => setHoveredItem(null)}
                aria-current={active ? "page" : undefined}
              >
                <span className={active ? "text-cyan-400" : isHovered ? "text-cyan-400" : "text-slate-500"}>
                  {React.cloneElement(item.icon, { className: "w-5 h-5" })}
                </span>
                <span className="tracking-wide">{item.name}</span>
                {active && (
                  <div className="absolute right-2 w-1.5 h-1.5 bg-cyan-400 rounded-full shadow-[0_0_10px_rgba(34,211,238,0.8)]" />
                )}
              </Link>
            );
          })}
        </nav>

        {/* Footer with cyber decoration */}
        <div className="sticky bottom-0 z-10 bg-slate-900 border-t border-cyan-500/30 px-3 py-3">
          <div className="flex items-center justify-center gap-2 text-xs text-cyan-500/50">
            <div className="w-2 h-2 bg-cyan-500 rounded-full animate-pulse" />
            <span className="tracking-wider">SYSTEM ONLINE</span>
          </div>
        </div>
      </div>
    </aside>
  );
}

