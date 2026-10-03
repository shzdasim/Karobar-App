// resources/js/components/sidebar-templates/ClassicSidebar.jsx
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
  BanknotesIcon,
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
  ArrowPathIcon,
CalculatorIcon,
  ClipboardDocumentListIcon as ReportIcon,
  ClockIcon,
  HandRaisedIcon,
} from "@heroicons/react/24/outline";
import { usePermissions } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext";

// Section configuration
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

export default function ClassicSidebar({ appName, logoUrl }) {
  const [collapsed, setCollapsed] = useState(false);
  const [hoveredSection, setHoveredSection] = useState(null);
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { theme } = useTheme();
  const { loading: permsLoading, has } = usePermissions();

  const brandName = appName && appName !== "ERP" ? appName : "Karobar App";
  const logoCandidates = [...new Set([logoUrl, "/storage/logos/logo.png", "/logo.png"].filter(Boolean))];

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
    { name: "User Demands", path: "/user-demands", icon: <HandRaisedIcon className="w-5 h-5" />, perm: "user-demands.view", parent: "transactions" },
    { name: "Stock Adjustments", path: "/stock-adjustments", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "stock-adjustment.view", parent: "transactions" },
    { type: "section", name: "Finance", key: "finance", icon: <CurrencyDollarIcon className="w-5 h-5" /> },
    { name: "Supplier Ledger", path: "/supplier-ledger", icon: <BuildingStorefrontIcon className="w-5 h-5" />, perm: "ledger.supplier.view", parent: "finance" },
    { name: "Customer Ledger", path: "/customer-ledger", icon: <UsersIcon className="w-5 h-5" />, perm: "ledger.customer.view", parent: "finance" },
    { type: "section", name: "Reports", key: "reports", icon: <ChartBarIcon className="w-5 h-5" /> },
    { name: "Current Stock", path: "/reports/current-stock", icon: <CubeIcon className="w-5 h-5" />, perm: "report.current-stock.view", parent: "reports" },
    { name: "Cost of Sale", path: "/reports/cost-of-sale", icon: <CalculatorIcon className="w-5 h-5" />, perm: "report.cost-of-sale.view", parent: "reports" },
    { name: "Cost of Sale Detail", path: "/reports/cost-of-sale-detail", icon: <DocumentTextIcon className="w-5 h-5" />, perm: "report.cost-of-sale.view", parent: "reports" },
    { name: "Purchase Detail", path: "/reports/purchase-detail", icon: <ClipboardDocumentIcon className="w-5 h-5" />, perm: "report.purchase-detail.view", parent: "reports" },
    { name: "Sale Detail", path: "/reports/sale-detail", icon: <DocumentCurrencyDollarIcon className="w-5 h-5" />, perm: "report.sale-detail.view", parent: "reports" },
    { name: "Stock Adjustment", path: "/reports/stock-adjustment", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "report.stock-adjustment.view", parent: "reports" },
    { name: "Product Comprehensive", path: "/reports/product-comprehensive", icon: <ReportIcon className="w-5 h-5" />, perm: "report.product-comprehensive.view", parent: "reports" },
    { name: "Near Expiry Product", path: "/reports/near-expiry-product", icon: <ClockIcon className="w-5 h-5" />, perm: "report.near-expiry-product.view", parent: "reports" },
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
  const widthCls = collapsed ? "workspace-sidebar-collapsed" : "workspace-sidebar-expanded";

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

  const shell = "workspace-sidebar";
  const card = `workspace-sidebar-inner ${widthCls}`;
  const scrollAreaCls = "workspace-navigation";
  const itemBase = "workspace-nav-item";
  const itemActive = "workspace-nav-item-active";

  const renderSectionHeader = (section, index) => {
    const config = getSectionConfig(section);

    if (collapsed) {
      return (
        <div key={`sec-${section.name}-${index}`} className="flex items-center justify-center py-3 mx-2 mt-2 border-b border-gray-200 dark:border-slate-700">
          <span style={{ color: config.baseColor }}>{React.cloneElement(section.icon, { className: "w-5 h-5" })}</span>
        </div>
      );
    }

    return (
      <div key={`sec-${section.name}-${index}`} className="workspace-nav-section">
        <div className="flex items-center gap-2">
          <span style={{ color: config.baseColor }}>{React.cloneElement(section.icon, { className: "w-4 h-4" })}</span>
          <span className="workspace-nav-section-label">{section.name}</span>
        </div>

      </div>
    );
  };

  return (
    <aside className={shell}>
      <div className={card}>
        {/* Header */}
        <div className="workspace-sidebar-brand">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3 overflow-hidden">
              <picture className="workspace-brand-mark">
                <span aria-hidden="true">{brandName.slice(0, 1).toUpperCase()}</span>
                {logoCandidates.map((src) => (
                  <img key={src} src={src} alt={brandName} className="absolute inset-0 h-full w-full object-contain rounded-xl hidden"
                    onLoad={(e) => {
                      const imgs = e.currentTarget.parentElement.querySelectorAll("img");
                      imgs.forEach((im) => (im.style.display = "none"));
                      e.currentTarget.style.display = "block";
                    }}
                  />
                ))}
              </picture>
              {!collapsed && (
                <div className="flex flex-col min-w-0">
                  <span className="workspace-brand-name">{brandName}</span>
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
                style={{ "--nav-accent": sectionConfig.baseColor }}
                tabIndex={focusedIndex === focusIdx ? 0 : -1}
                onFocus={() => setFocusedIndex(focusIdx)}
                aria-current={active ? "page" : undefined}
                title={collapsed ? item.name : undefined}
                onMouseEnter={() => setHoveredSection(item.path)}
                onMouseLeave={() => setHoveredSection(null)}
              >
                <span className={["shrink-0", active ? "" : "text-gray-500 dark:text-gray-400"].join(" ")}
                  style={{ color: active ? sectionConfig.baseColor : undefined }}>
                  {React.cloneElement(item.icon, { className: "w-5 h-5" })}
                </span>
                <span className={`whitespace-nowrap transition-all duration-200 ${collapsed ? 'opacity-0 w-0' : 'opacity-100'}`}>
                  {!collapsed && item.name}
                </span>
              </Link>
            );
          })}
        </nav>

        {/* Footer */}
        <div className="workspace-sidebar-footer">
          <button
            onClick={() => setCollapsed((v) => !v)}
            className="workspace-collapse"
            aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          >
            {collapsed ? <ChevronRightIcon className="w-5 h-5" /> : <ChevronLeftIcon className="w-5 h-5" />}
            {!collapsed && <span>Collapse</span>}
          </button>
        </div>
      </div>
    </aside>
  );
}

