// src/components/Sidebar.jsx
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
} from "@heroicons/react/24/outline";
import { usePermissions } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext";

// Section configuration - will use theme colors dynamically
const SECTION_CONFIG = {
  dashboard: {
    key: 'primary',
  },
  core: {
    key: 'secondary',
  },
  invoices: {
    key: 'tertiary',
  },
  returns: {
    key: 'primary',
  },
  transactions: {
    key: 'secondary',
  },
  finance: {
    key: 'tertiary',
  },
  reports: {
    key: 'primary',
  },
  system: {
    key: 'secondary',
  },
};

// Helper to get color value from theme
const getThemeColor = (theme, colorKey, variant = 'color') => {
  if (!theme) return '#3b82f6';
  const key = `${colorKey}_${variant}`;
  return theme[key] || '#3b82f6';
};

// Helper to generate CSS styles from theme colors
const getSectionStyles = (theme, colorKey) => {
  const baseColor = getThemeColor(theme, colorKey, 'color');
  const hoverColor = getThemeColor(theme, colorKey, 'hover');
  const lightColor = getThemeColor(theme, colorKey, 'light');
  
  return {
    gradient: `from-[${baseColor}] to-[${hoverColor}]`,
    bgLight: `bg-[${lightColor}]`,
    bgDark: `dark:bg-[${lightColor}]`,
    borderColor: `border-[${baseColor}]/30 dark:border-[${baseColor}]/30`,
    iconColor: `text-[${baseColor}] dark:text-[${baseColor}]`,
    hoverBg: `hover:bg-[${lightColor}]`,
  };
};

export default function Sidebar() {
  const [collapsed, setCollapsed] = useState(false);
  const [hoveredSection, setHoveredSection] = useState(null);
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { theme } = useTheme();

  const { loading: permsLoading, has } = usePermissions();

  const brandName = "Karobar App";
  const logoCandidates = ["/storage/logos/logo.png", "/logo.png"];

  const rawMenu = useMemo(
    () => [
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
    ],
    []
  );

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
    const idx = flatMenu.findIndex(
      (item) => pathname === item.path || pathname.startsWith(item.path + "/")
    );
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
      case "ArrowDown":
        e.preventDefault();
        setFocusedIndex((p) => (p + 1) % flatMenu.length);
        break;
      case "ArrowUp":
        e.preventDefault();
        setFocusedIndex((p) => (p <= 0 ? flatMenu.length - 1 : p - 1));
        break;
      case "Home":
        e.preventDefault(); setFocusedIndex(0); break;
      case "End":
        e.preventDefault(); setFocusedIndex(flatMenu.length - 1); break;
      case "Enter":
      case " ":
        e.preventDefault();
        if (focusedIndex >= 0) navigate(flatMenu[focusedIndex].path);
        break;
      default: break;
    }
  }

  const isActive = (path) => pathname === path || pathname.startsWith(path + "/");

  const widthCls = collapsed ? "w-20" : "w-64";

  // Floating shell with subtle background
  const shell =
    "relative h-screen px-3 py-3 " +
    "bg-gradient-to-br from-gray-100/50 to-gray-50/30 dark:from-slate-900/50 dark:to-slate-800/30";

  const card =
    "relative flex h-full flex-col rounded-2xl " +
    "bg-white/80 dark:bg-slate-800/80 backdrop-blur-xl ring-1 ring-gray-200/60 dark:ring-white/10 shadow-2xl " +
    "transition-[width] duration-300 overflow-hidden " + widthCls;

  // Enhanced scroll shadows with better visibility
  const scrollShadow =
    "before:pointer-events-none before:content-[''] before:absolute before:left-0 before:right-0 before:top-[72px] before:h-5 before:bg-gradient-to-b before:from-white/80 before:to-transparent dark:before:from-slate-800/80 dark:before:to-transparent " +
    "after:pointer-events-none after:content-[''] after:absolute after:left-0 after:right-0 after:bottom-[60px] after:h-5 after:bg-gradient-to-t after:from-white/80 after:to-transparent dark:after:from-slate-800/80 dark:after:to-transparent";

  // Get section config for a menu item - using theme colors dynamically
  const getSectionConfig = (item) => {
    if (item.standalone) {
      const baseColor = getThemeColor(theme, 'primary', 'color');
      const hoverColor = getThemeColor(theme, 'primary', 'hover');
      const lightColor = getThemeColor(theme, 'primary', 'light');
      return {
        key: 'primary',
        gradient: `from-[${baseColor}] to-[${hoverColor}]`,
        bgLight: `bg-[${lightColor}]`,
        bgDark: `dark:bg-[${lightColor}]`,
        borderColor: `border-[${baseColor}]/30 dark:border-[${baseColor}]/30`,
        iconColor: `text-[${baseColor}] dark:text-[${baseColor}]`,
      };
    }
    const sectionKey = item.parent || item.key;
    const config = SECTION_CONFIG[sectionKey] || SECTION_CONFIG.core;
    const colorKey = config.key || 'primary';
    const baseColor = getThemeColor(theme, colorKey, 'color');
    const hoverColor = getThemeColor(theme, colorKey, 'hover');
    const lightColor = getThemeColor(theme, colorKey, 'light');
    return {
      key: colorKey,
      gradient: `from-[${baseColor}] to-[${hoverColor}]`,
      bgLight: `bg-[${lightColor}]`,
      bgDark: `dark:bg-[${lightColor}]`,
      borderColor: `border-[${baseColor}]/30 dark:border-[${baseColor}]/30`,
      iconColor: `text-[${baseColor}] dark:text-[${baseColor}]`,
    };
  };

  // Get color for active item based on section
  const getActiveColor = (item) => {
    const config = getSectionConfig(item);
    return `${config.iconColor.replace('text-', 'bg-').split(' ')[0]} shadow-lg`;
  };

  // Item styles with section-based coloring
  const itemBase =
    "group relative mx-2 mt-1 flex items-center gap-3 rounded-xl px-3 py-2.5 text-gray-900 dark:text-gray-100 " +
    "hover:translate-y-[-1px] hover:bg-white/90 dark:hover:bg-slate-700/90 hover:shadow-lg transition-all duration-200 " +
    "focus:outline-none focus:ring-2 focus:ring-offset-1 focus:ring-offset-transparent";

  const itemActive =
    "bg-white dark:bg-slate-700 shadow-lg ring-1 ring-gray-200/70 dark:ring-white/10 " +
    "hover:scale-[1.02]";

  const leftRail =
    "absolute left-0 top-1/2 -translate-y-1/2 h-8 w-1.5 rounded-r-lg transition-all duration-300";

  // Custom thin scrollbar with better styling
  const scrollAreaCls =
    "flex-1 overflow-y-auto min-h-0 mt-1 pb-4 " +
    "[&::-webkit-scrollbar]:w-2 [&::-webkit-scrollbar-thumb]:rounded-full " +
    "[&::-webkit-scrollbar-thumb]:bg-gray-300/70 hover:[&::-webkit-scrollbar-thumb]:bg-gray-400/70 " +
    "[&::-webkit-scrollbar-track]:bg-transparent";

  // Section header styles
  const sectionHeaderCollapsed =
    "flex items-center justify-center p-2 mx-2 mt-4 rounded-xl " +
    "bg-gradient-to-br from-gray-100/80 to-gray-50/50 dark:from-slate-700/60 dark:to-slate-800/40 " +
    "ring-1 ring-gray-200/60 dark:ring-white/10 shadow-sm";

  const sectionHeaderExpanded =
    "flex items-center gap-2 px-3 py-2 mx-2 mt-4 rounded-xl " +
    "bg-gradient-to-r from-gray-50/80 to-gray-100/60 dark:from-slate-700/60 dark:to-slate-800/40 " +
    "ring-1 ring-gray-200/60 dark:ring-white/10 shadow-sm";

  // Render section header with dynamic theme colors
  const renderSectionHeader = (section, index) => {
    const config = getSectionConfig(section);
    const isHovered = hoveredSection === section.key;
    
    // Extract colors for inline styles
    const baseColor = getThemeColor(theme, config.key || 'primary', 'color');
    const hoverColor = getThemeColor(theme, config.key || 'primary', 'hover');
    const lightColor = getThemeColor(theme, config.key || 'primary', 'light');
    
    // Rest state: subtle background
    const restBgStyle = {
      backgroundColor: lightColor + '30',
    };
    
    // Hover state: more visible background
    const hoverBgStyle = {
      backgroundColor: lightColor + '60',
    };

    if (collapsed) {
      return (
        <div
          key={`sec-${section.name}-${index}`}
          className={sectionHeaderCollapsed}
          style={isHovered ? hoverBgStyle : restBgStyle}
          onMouseEnter={() => setHoveredSection(section.key)}
          onMouseLeave={() => setHoveredSection(null)}
        >
          <div 
            className={`p-1.5 rounded-lg transition-all duration-300 ${isHovered ? 'scale-110' : ''}`}
            style={{ 
              backgroundColor: isHovered ? lightColor + '50' : 'transparent',
            }}
          >
            <span 
              className={`${isHovered ? 'scale-110' : ''} transition-transform`}
              style={{ color: isHovered ? hoverColor : baseColor }}
            >
              {React.cloneElement(section.icon, { className: "w-5 h-5" })}
            </span>
          </div>
          {/* Tooltip on hover */}
          {isHovered && (
            <div 
              className="absolute left-full ml-2 px-3 py-1.5 text-xs font-medium rounded-lg shadow-xl whitespace-nowrap z-50"
              style={{ 
                backgroundColor: baseColor,
                color: '#ffffff',
              }}
            >
              {section.name}
              <div 
                className="absolute left-0 top-1/2 -translate-x-1 -translate-y-1/2 w-2 h-2 rotate-45"
                style={{ backgroundColor: baseColor }}
              />
            </div>
          )}
        </div>
      );
    }

    return (
      <div
        key={`sec-${section.name}-${index}`}
        className={sectionHeaderExpanded}
        style={isHovered ? hoverBgStyle : restBgStyle}
        onMouseEnter={() => setHoveredSection(section.key)}
        onMouseLeave={() => setHoveredSection(null)}
      >
        <div 
          className={`p-1.5 rounded-lg transition-all duration-300 ${isHovered ? 'scale-105' : ''}`}
          style={{ 
            backgroundColor: isHovered ? lightColor + '50' : 'transparent',
          }}
        >
          <span 
            className={`${isHovered ? 'scale-110' : ''} transition-transform`}
            style={{ color: isHovered ? hoverColor : baseColor }}
          >
            {React.cloneElement(section.icon, { className: "w-4 h-4" })}
          </span>
        </div>
        <div className="flex-1 min-w-0">
          <div 
            className="text-xs font-bold uppercase tracking-wider truncate"
            style={{ color: isHovered ? hoverColor : baseColor }}
          >
            {section.name}
          </div>
        </div>
        {/* Decorative gradient bar using theme colors */}
        <div 
          className="h-1 w-8 rounded-full opacity-60"
          style={{ 
            background: `linear-gradient(to right, ${baseColor}, ${hoverColor})`,
          }} 
        />
      </div>
    );
  };

  return (
    <aside className={shell}>
      <div className={`${card} ${scrollShadow}`}>
        {/* Sticky header with enhanced design */}
        <div className="sticky top-0 z-10 bg-white/90 dark:bg-slate-800/90 backdrop-blur-xl border-b border-gray-200/60 dark:border-white/10 px-4 py-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3 overflow-hidden">
              <div className="relative">
                <picture>
                  {logoCandidates.map((src) => (
                    <img
                      key={src}
                      src={src}
                      alt={brandName}
                      className="h-10 w-10 object-contain rounded-xl shadow-md hidden"
                      onLoad={(e) => {
                        const imgs = e.currentTarget.parentElement.querySelectorAll("img");
                        imgs.forEach((im) => (im.style.display = "none"));
                        e.currentTarget.style.display = "block";
                      }}
                    />
                  ))}
                </picture>
                {/* Decorative ring for logo */}
                <div className="absolute inset-0 rounded-xl bg-gradient-to-br from-violet-500/20 to-purple-500/20 dark:from-violet-400/20 dark:to-purple-400/20" />
              </div>
              {!collapsed && (
                <div className="flex flex-col min-w-0">
                  <span className="text-sm font-bold bg-gradient-to-r from-gray-900 to-gray-700 dark:from-white dark:to-gray-300 bg-clip-text text-transparent truncate">
                    {brandName}
                  </span>
                  <span className="text-[10px] text-gray-500 dark:text-gray-400 font-medium">
                    Management System
                  </span>
                </div>
              )}
            </div>
          </div>
        </div>

{/* Scrollable NAV */}
        <nav
          className={scrollAreaCls}
          role="navigation"
          tabIndex={0}
          aria-label="Main navigation"
          onKeyDown={handleKeyDown}
        >
          {menu.map((item, i) => {
            if (item.type === "section") {
              return renderSectionHeader(item, i);
            }

            const focusIdx = flatMenu.findIndex((fm) => fm.path === item.path);
            const active = isActive(item.path);
            const sectionConfig = getSectionConfig(item);
            const isHovered = hoveredSection === item.path;
            
            // Extract the actual color values for inline styles
            const baseColor = getThemeColor(theme, sectionConfig.key || 'primary', 'color');
            const hoverColor = getThemeColor(theme, sectionConfig.key || 'primary', 'hover');
            const lightColor = getThemeColor(theme, sectionConfig.key || 'primary', 'light');

            // Rest state: light background
            const restBgStyle = {
              backgroundColor: lightColor + '40',
            };
            
            // Hover state: slightly darker background
            const hoverBgStyle = {
              backgroundColor: lightColor + '70',
            };
            
            // Active/Selected state: darker background with glow
            const activeBgStyle = {
              backgroundColor: lightColor + '90',
              boxShadow: `0 4px 12px ${baseColor}30`,
            };

            return (
              <Link
                key={item.path}
                to={item.path}
                ref={(el) => (itemRefs.current[focusIdx] = el)}
                className={itemBase}
                style={active ? activeBgStyle : (isHovered ? hoverBgStyle : restBgStyle)}
                tabIndex={focusedIndex === focusIdx ? 0 : -1}
                onFocus={() => setFocusedIndex(focusIdx)}
                aria-current={active ? "page" : undefined}
                title={collapsed ? item.name : undefined}
                onMouseEnter={() => setHoveredSection(item.path)}
                onMouseLeave={() => setHoveredSection(null)}
              >
                {/* Left rail indicator using theme colors */}
                <span
                  className={`${leftRail} ${active ? 'opacity-100' : 'bg-transparent w-0 opacity-0'}`}
                  style={active ? {
                    background: `linear-gradient(to bottom, ${baseColor}, ${hoverColor})`,
                    boxShadow: `0 0 8px ${baseColor}50`,
                  } : isHovered ? {
                    background: baseColor,
                    opacity: 0.5,
                    width: '4px',
                  } : {}}
                />

                {/* Icon with enhanced styling using theme colors */}
                <span
                  className={["shrink-0 transition-all duration-200", isHovered || active ? "scale-110" : ""].join(" ")}
                  style={{ 
                    color: active ? baseColor : (isHovered ? hoverColor : '#64748b'),
                    transition: 'color 0.2s ease',
                  }}
                >
                  {React.cloneElement(item.icon, { className: "w-5 h-5" })}
                </span>

                {/* Label with smart reveal */}
                <span
                  className="whitespace-nowrap transition-all duration-200"
                  style={{
                    opacity: collapsed ? 0 : 1,
                    transform: collapsed ? 'translateX(-8px)' : 'translateX(0)',
                    color: active ? baseColor : (isHovered ? hoverColor : '#374151'),
                    fontWeight: active ? 600 : 400,
                    transition: 'all 0.2s ease',
                  }}
                >
                  {!collapsed && item.name}
                  {collapsed && <span className="sr-only">{item.name}</span>}
                </span>

                {/* Active indicator dot for collapsed mode using theme colors */}
                {collapsed && active && (
                  <div
                    className="absolute right-2 w-1.5 h-1.5 rounded-full"
                    style={{ background: `linear-gradient(to right, ${baseColor}, ${hoverColor})` }}
                  />
                )}
              </Link>
            );
          })}
          <div className="h-3" />
        </nav>

        {/* Sticky footer (collapse/expand) with enhanced design */}
        <div className="sticky bottom-0 z-10 bg-white/90 dark:bg-slate-800/90 backdrop-blur-xl border-t border-gray-200/60 dark:border-white/10 px-2 py-3">
          <button
            onClick={() => setCollapsed((v) => !v)}
            className="w-full flex items-center justify-center gap-2 rounded-xl px-3 py-2.5 text-sm font-medium 
              text-gray-600 dark:text-gray-300 
              hover:bg-gradient-to-r hover:from-gray-100 hover:to-gray-50 dark:hover:from-slate-700 dark:hover:to-slate-600
              hover:shadow-lg hover:scale-[1.02]
              focus:outline-none focus:ring-2 focus:ring-blue-400/50 dark:focus:ring-indigo-400/50
              transition-all duration-200"
            aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          >
            <div className="relative">
              {collapsed ? (
                <ChevronRightIcon className="w-5 h-5" />
              ) : (
                <ChevronLeftIcon className="w-5 h-5" />
              )}
              {/* Subtle glow effect */}
              <div className="absolute inset-0 rounded-full bg-gradient-to-r from-blue-500/20 to-indigo-500/20 blur-sm" />
            </div>
            {!collapsed && (
              <span className="bg-gradient-to-r from-gray-700 to-gray-900 dark:from-gray-200 dark:to-gray-400 bg-clip-text text-transparent">
                Collapse
              </span>
            )}
          </button>
        </div>
      </div>
    </aside>
  );
}
