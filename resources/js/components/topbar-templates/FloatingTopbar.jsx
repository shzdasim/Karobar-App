// resources/js/components/topbar-templates/FloatingTopbar.jsx
import React, { useState, useRef, useEffect, useMemo } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import {
  HomeIcon, BuildingStorefrontIcon, UsersIcon, Squares2X2Icon, TagIcon, CubeIcon,
  ClipboardDocumentListIcon, ArrowUturnLeftIcon, DocumentCurrencyDollarIcon, ArrowUturnDownIcon,
  ClipboardDocumentCheckIcon, ArrowsRightLeftIcon, ChartBarIcon, Cog6ToothIcon, UserGroupIcon,
  KeyIcon, ChevronDownIcon, ShoppingCartIcon, UserPlusIcon, TruckIcon, CurrencyDollarIcon,
  ClipboardDocumentIcon, DocumentTextIcon, CalculatorIcon, ClipboardDocumentListIcon as ReportIcon,
} from "@heroicons/react/24/outline";
import { usePermissions } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext";
import Logo from "../sidebar-templates/Logo";

const SECTION_CONFIG = {
  core: { key: 'primary' }, invoices: { key: 'secondary' }, returns: { key: 'tertiary' },
  transactions: { key: 'primary' }, finance: { key: 'secondary' }, reports: { key: 'tertiary' }, system: { key: 'primary' },
};

const getThemeColor = (theme, colorKey, variant = 'color') => {
  if (!theme) return '#3b82f6';
  return theme[`${colorKey}_${variant}`] || '#3b82f6';
};

export default function FloatingTopbar() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { loading: permsLoading, has } = usePermissions();
  const { theme } = useTheme();
  const [openDropdown, setOpenDropdown] = useState(null);
  const [dropdownPosition, setDropdownPosition] = useState({ top: 0, left: 0 });
  const buttonRefs = useRef({});
  const dropdownRef = useRef(null);
  const brandName = "Karobar App";

  const getSectionConfig = (key) => {
    const config = SECTION_CONFIG[key] || SECTION_CONFIG.core;
    const colorKey = config.key || 'primary';
    return {
      base: getThemeColor(theme, colorKey, 'color'),
      light: getThemeColor(theme, colorKey, 'light'),
      hover: getThemeColor(theme, colorKey, 'hover'),
    };
  };

  const menuSections = useMemo(() => [
    { name: "Management", key: "core", icon: <CubeIcon className="w-4 h-4" />, items: [
      { name: "Products", path: "/products", icon: <CubeIcon className="w-4 h-4" />, perm: "product.view" },
      { name: "Categories", path: "/categories", icon: <Squares2X2Icon className="w-4 h-4" />, perm: "category.view" },
      { name: "Brands", path: "/brands", icon: <TagIcon className="w-4 h-4" />, perm: "brand.view" },
      { name: "Suppliers", path: "/suppliers", icon: <TruckIcon className="w-4 h-4" />, perm: "supplier.view" },
      { name: "Customers", path: "/customers", icon: <UserPlusIcon className="w-4 h-4" />, perm: "customer.view" },
    ]},
    { name: "Invoices", key: "invoices", icon: <DocumentTextIcon className="w-4 h-4" />, items: [
      { name: "Purchase Invoice", path: "/purchase-invoices", icon: <ClipboardDocumentListIcon className="w-4 h-4" />, perm: "purchase-invoice.view" },
      { name: "Sale Invoice", path: "/sale-invoices", icon: <DocumentCurrencyDollarIcon className="w-4 h-4" />, perm: "sale-invoice.view" },
    ]},
    { name: "Returns", key: "returns", icon: <ArrowUturnLeftIcon className="w-4 h-4" />, items: [
      { name: "Purchase Return", path: "/purchase-returns", icon: <ArrowUturnDownIcon className="w-4 h-4" />, perm: "purchase-return.view" },
      { name: "Sale Return", path: "/sale-returns", icon: <ArrowUturnLeftIcon className="w-4 h-4" />, perm: "sale-return.view" },
    ]},
    { name: "Transactions", key: "transactions", icon: <ShoppingCartIcon className="w-4 h-4" />, items: [
      { name: "Purchase Orders", path: "/purchase-orders", icon: <ClipboardDocumentCheckIcon className="w-4 h-4" />, perm: "purchase-order.view" },
      { name: "Stock Adjustments", path: "/stock-adjustments", icon: <ArrowsRightLeftIcon className="w-4 h-4" />, perm: "stock-adjustment.view" },
    ]},
    { name: "Finance", key: "finance", icon: <CurrencyDollarIcon className="w-4 h-4" />, items: [
      { name: "Supplier Ledger", path: "/supplier-ledger", icon: <BuildingStorefrontIcon className="w-4 h-4" />, perm: "ledger.supplier.view" },
      { name: "Customer Ledger", path: "/customer-ledger", icon: <UsersIcon className="w-4 h-4" />, perm: "ledger.customer.view" },
    ]},
    { name: "Reports", key: "reports", icon: <ChartBarIcon className="w-4 h-4" />, items: [
      { name: "Current Stock", path: "/reports/current-stock", icon: <CubeIcon className="w-4 h-4" />, perm: "report.current-stock.view" },
      { name: "Cost of Sale", path: "/reports/cost-of-sale", icon: <CalculatorIcon className="w-4 h-4" />, perm: "report.cost-of-sale.view" },
      { name: "Purchase Detail", path: "/reports/purchase-detail", icon: <ClipboardDocumentIcon className="w-4 h-4" />, perm: "report.purchase-detail.view" },
      { name: "Sale Detail", path: "/reports/sale-detail", icon: <DocumentCurrencyDollarIcon className="w-4 h-4" />, perm: "report.sale-detail.view" },
      { name: "Stock Adjustment", path: "/reports/stock-adjustment", icon: <ArrowsRightLeftIcon className="w-4 h-4" />, perm: "report.stock-adjustment.view" },
      { name: "Product Comprehensive", path: "/reports/product-comprehensive", icon: <ReportIcon className="w-4 h-4" />, perm: "report.product-comprehensive.view" },
    ]},
    { name: "System", key: "system", icon: <Cog6ToothIcon className="w-4 h-4" />, items: [
      { name: "Settings", path: "/settings", icon: <Cog6ToothIcon className="w-4 h-4" />, perm: "settings.view" },
      { name: "Users", path: "/users", icon: <UserGroupIcon className="w-4 h-4" />, perm: "user.view" },
      { name: "Roles", path: "/roles", icon: <KeyIcon className="w-4 h-4" />, perm: "role.view" },
    ]},
  ], []);

  const flatMenu = useMemo(() => [
    { name: "Dashboard", path: "/dashboard", icon: <HomeIcon className="w-4 h-4" />, perm: null },
  ], []);

  const getFilteredItems = (items) => items.filter(item => !item.perm || (!permsLoading && has(item.perm)));
  const isActive = (path) => pathname === path || pathname.startsWith(path + "/");
  const isSectionActive = (items) => items.some(item => isActive(item.path));

  const toggleDropdown = (sectionName) => {
    if (openDropdown === sectionName) { setOpenDropdown(null); return; }
    const button = buttonRefs.current[sectionName];
    if (button) {
      const rect = button.getBoundingClientRect();
      setDropdownPosition({ top: rect.bottom + 12, left: rect.left });
    }
    setOpenDropdown(sectionName);
  };

  const handleMenuClick = (path) => { setOpenDropdown(null); navigate(path); };

  useEffect(() => {
    const handleClickOutside = (event) => {
      if (dropdownRef.current && dropdownRef.current.contains(event.target)) return;
      const isOnButton = Object.values(buttonRefs.current).some(ref => ref && ref.contains(event.target));
      if (!isOnButton) setOpenDropdown(null);
    };
    document.addEventListener('click', handleClickOutside);
    return () => document.removeEventListener('click', handleClickOutside);
  }, []);

  return (
    <>
      <div className="bg-gray-100 dark:bg-slate-950 p-2 w-full max-w-full overflow-hidden">
        <div className="bg-white/90 dark:bg-slate-900/90 backdrop-blur-xl rounded-2xl shadow-2xl border border-white/50 dark:border-slate-700/50 w-full max-w-full overflow-hidden">
          <div className="flex items-center h-12 px-3 w-full min-w-0">
            {/* Floating Logo */}
            <div className="flex items-center gap-2 mr-4 flex-shrink-0">
              <div className="h-8 w-8 rounded-xl overflow-hidden shadow-lg flex-shrink-0">
                <Logo className="h-8 w-8" alt={brandName} />
              </div>
              <span className="text-sm font-bold text-gray-900 dark:text-white whitespace-nowrap">{brandName}</span>
            </div>
            
            {/* Floating Navigation */}
            <nav className="flex-1 flex items-center gap-0.5 overflow-x-auto min-w-0 px-1" role="navigation">
              {getFilteredItems(flatMenu).map((item) => {
                const active = isActive(item.path);
                const colors = getSectionConfig('core');
                return (
                  <Link
                    key={item.path}
                    to={item.path}
                    className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-sm font-medium transition-all duration-200 hover:bg-white dark:hover:bg-slate-800 hover:shadow-md"
                    style={{ color: active ? colors.base : undefined }}
                  >
                    <HomeIcon className="w-4 h-4" />
                    {item.name}
                  </Link>
                );
              })}

              {menuSections.map((section) => {
                const filteredItems = getFilteredItems(section.items);
                if (filteredItems.length === 0) return null;
                const isOpen = openDropdown === section.name;
                const isActiveSection = isSectionActive(filteredItems);
                const colors = getSectionConfig(section.key);

                return (
                  <div key={section.name} className="relative">
                    <button
                      ref={el => buttonRefs.current[section.name] = el}
                      onClick={() => toggleDropdown(section.name)}
                      className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-sm font-medium transition-all duration-200 hover:bg-white dark:hover:bg-slate-800 hover:shadow-md"
                      style={{ color: isActiveSection ? colors.base : undefined }}
                    >
                      {React.cloneElement(section.icon, { className: "w-4 h-4" })}
                      {section.name}
                      <ChevronDownIcon className={`w-4 h-4 transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`} />
                    </button>
                  </div>
                );
              })}
            </nav>
          </div>
        </div>
      </div>

      {/* Floating Dropdown */}
      {openDropdown && (() => {
        const section = menuSections.find(s => s.name === openDropdown);
        const colors = section ? getSectionConfig(section.key) : getSectionConfig('core');
        const filteredItems = getFilteredItems(section?.items || []);
        
        return (
          <div
            ref={dropdownRef}
            className="fixed bg-white/95 dark:bg-slate-800/95 backdrop-blur-xl rounded-2xl shadow-2xl border border-white/50 dark:border-slate-700/50 py-2 z-[99999]"
            style={{ top: dropdownPosition.top, left: dropdownPosition.left, minWidth: '180px' }}
          >
            <div className="px-3 py-2 mx-2 rounded-xl mb-1" style={{ backgroundColor: colors.light + '60' }}>
              <div className="flex items-center gap-2">
                <span style={{ color: colors.base }}>{React.cloneElement(section?.icon, { className: "w-4 h-4" })}</span>
                <span className="text-xs font-semibold" style={{ color: colors.base }}>{section?.name}</span>
              </div>
            </div>
            <div className="py-1 px-2">
              {filteredItems.map((item) => {
                const itemActive = isActive(item.path);
                return (
                  <button
                    key={item.path}
                    onClick={() => handleMenuClick(item.path)}
                    className="w-full flex items-center gap-2 px-3 py-2 text-sm font-medium transition-all duration-150 text-left rounded-xl"
                    style={{ backgroundColor: itemActive ? colors.light : undefined, color: itemActive ? colors.base : undefined }}
                  >
                    {React.cloneElement(item.icon, { className: "w-4 h-4" })}
                    <span>{item.name}</span>
                  </button>
                );
              })}
            </div>
          </div>
        );
      })()}
    </>
  );
}
