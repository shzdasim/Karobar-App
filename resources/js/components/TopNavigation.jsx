import React, { useState, useRef, useEffect, useMemo } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
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
  BanknotesIcon,
  ChartBarIcon,
  Cog6ToothIcon,
  UserGroupIcon,
  KeyIcon,
  ChevronDownIcon,
  ShoppingCartIcon,
  UserPlusIcon,
  TruckIcon,
  CurrencyDollarIcon,
  ClipboardDocumentIcon,
} from "@heroicons/react/24/outline";
import { usePermissions } from "@/api/usePermissions";

export default function TopNavigation() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { loading: permsLoading, has } = usePermissions();
  const [openDropdown, setOpenDropdown] = useState(null);
  const [dropdownPosition, setDropdownPosition] = useState({ top: 0, left: 0 });
  const buttonRefs = useRef({});
  const dropdownRef = useRef(null);

  // Organized menu with sections and items - Reorganized for logical flow
  const menuSections = useMemo(() => [
    {
      name: "Products",
      icon: <CubeIcon className="w-5 h-5" />,
      items: [
        { name: "Products", path: "/products", icon: <CubeIcon className="w-5 h-5" />, perm: "product.view" },
        { name: "Categories", path: "/categories", icon: <Squares2X2Icon className="w-5 h-5" />, perm: "category.view" },
        { name: "Brands", path: "/brands", icon: <TagIcon className="w-5 h-5" />, perm: "brand.view" },
      ]
    },
    {
      name: "Parties",
      icon: <UserGroupIcon className="w-5 h-5" />,
      items: [
        { name: "Suppliers", path: "/suppliers", icon: <TruckIcon className="w-5 h-5" />, perm: "supplier.view" },
        { name: "Customers", path: "/customers", icon: <UserPlusIcon className="w-5 h-5" />, perm: "customer.view" },
      ]
    },
    {
      name: "Transactions",
      icon: <ShoppingCartIcon className="w-5 h-5" />,
      items: [
        { name: "Purchase Invoice", path: "/purchase-invoices", icon: <ClipboardDocumentListIcon className="w-5 h-5" />, perm: "purchase-invoice.view" },
        { name: "Sale Invoice", path: "/sale-invoices", icon: <DocumentCurrencyDollarIcon className="w-5 h-5" />, perm: "sale-invoice.view" },
        { name: "Purchase Return", path: "/purchase-returns", icon: <ArrowUturnLeftIcon className="w-5 h-5" />, perm: "purchase-return.view" },
        { name: "Sale Return", path: "/sale-returns", icon: <ArrowUturnDownIcon className="w-5 h-5" />, perm: "sale-return.view" },
        { name: "Purchase Orders", path: "/purchase-orders", icon: <ClipboardDocumentCheckIcon className="w-5 h-5" />, perm: "purchase-order.view" },
        { name: "Stock Adjustments", path: "/stock-adjustments", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "stock-adjustment.view" },
      ]
    },
    {
      name: "Finance",
      icon: <CurrencyDollarIcon className="w-5 h-5" />,
      items: [
        { name: "Supplier Ledger", path: "/supplier-ledger", icon: <BuildingStorefrontIcon className="w-5 h-5" />, perm: "ledger.supplier.view" },
        { name: "Customer Ledger", path: "/customer-ledger", icon: <UsersIcon className="w-5 h-5" />, perm: "ledger.customer.view" },
      ]
    },
    {
      name: "Reports",
      icon: <ChartBarIcon className="w-5 h-5" />,
      items: [
        { name: "Current Stock", path: "/reports/current-stock", icon: <CubeIcon className="w-5 h-5" />, perm: "report.current-stock.view" },
        { name: "Cost of Sale", path: "/reports/cost-of-sale", icon: <ChartBarIcon className="w-5 h-5" />, perm: "report.cost-of-sale.view" },
        { name: "Purchase Detail", path: "/reports/purchase-detail", icon: <ClipboardDocumentIcon className="w-5 h-5" />, perm: "report.purchase-detail.view" },
        { name: "Sale Detail", path: "/reports/sale-detail", icon: <DocumentCurrencyDollarIcon className="w-5 h-5" />, perm: "report.sale-detail.view" },
        { name: "Stock Adjustment", path: "/reports/stock-adjustment", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "report.stock-adjustment.view" },
        { name: "Product Comprehensive", path: "/reports/product-comprehensive", icon: <ChartBarIcon className="w-5 h-5" />, perm: "report.product-comprehensive.view" },
      ]
    },
    {
      name: "System",
      icon: <Cog6ToothIcon className="w-5 h-5" />,
      items: [
        { name: "Settings", path: "/settings", icon: <Cog6ToothIcon className="w-5 h-5" />, perm: "settings.view" },
        { name: "Users", path: "/users", icon: <UserGroupIcon className="w-5 h-5" />, perm: "user.view" },
        { name: "Roles", path: "/roles", icon: <KeyIcon className="w-5 h-5" />, perm: "role.view" },
      ]
    },
  ], []);

  // Flat menu for Dashboard
  const flatMenu = useMemo(() => [
    { name: "Dashboard", path: "/dashboard", icon: <HomeIcon className="w-5 h-5" />, perm: null },
  ], []);

  // Filter items based on permissions
  const getFilteredItems = (items) => {
    return items.filter(item => !item.perm || (!permsLoading && has(item.perm)));
  };

  const isActive = (path) => pathname === path || pathname.startsWith(path + "/");

  // Check if any item in section is active
  const isSectionActive = (items) => {
    return items.some(item => isActive(item.path));
  };

  // Handle dropdown toggle with position calculation
  const toggleDropdown = (sectionName) => {
    if (openDropdown === sectionName) {
      setOpenDropdown(null);
      return;
    }

    const button = buttonRefs.current[sectionName];
    if (button) {
      const rect = button.getBoundingClientRect();
      setDropdownPosition({
        top: rect.bottom + 4,
        left: rect.left
      });
    }
    setOpenDropdown(sectionName);
  };

  // Handle menu item click
  const handleMenuClick = (path) => {
    setOpenDropdown(null);
    navigate(path);
  };

  // Close dropdown when clicking outside (only if not clicking inside dropdown)
  useEffect(() => {
    const handleClickOutside = (event) => {
      // Check if click is inside dropdown
      if (dropdownRef.current && dropdownRef.current.contains(event.target)) {
        return;
      }
      // Check if click is on a dropdown button
      const isOnButton = Object.values(buttonRefs.current).some(
        ref => ref && ref.contains(event.target)
      );
      if (!isOnButton) {
        setOpenDropdown(null);
      }
    };
    document.addEventListener('click', handleClickOutside);
    return () => document.removeEventListener('click', handleClickOutside);
  }, []);

  return (
    <>
      <div className="
        bg-white/70 dark:bg-slate-800/70
        backdrop-blur-md
        border-b border-gray-200/60 dark:border-slate-700/60
        shadow-sm
      ">
        <div className="flex items-stretch h-12">
          {/* Logo & Brand */}
          <div className="flex items-center gap-2 px-4 border-r border-gray-200/60 dark:border-slate-700/60 flex-shrink-0">
            <picture>
              <source srcSet="/storage/logos/logo.png" media="(prefers-color-scheme: light)" />
              <img
                src="/logo.png"
                alt="Karobar App"
                className="h-7 w-7 object-contain rounded"
              />
            </picture>
            <span className="text-sm font-semibold text-gray-900 dark:text-gray-100 whitespace-nowrap">
              Karobar App
            </span>
          </div>
          
          {/* Navigation Items */}
          <nav
            className="
              flex-1 flex items-center gap-1
              overflow-x-auto
              px-3
            "
            role="navigation"
            aria-label="Main navigation"
          >
            {/* Dashboard Link */}
            {getFilteredItems(flatMenu).map((item) => (
              <Link
                key={item.path}
                to={item.path}
                className={`
                  flex-shrink-0 flex items-center gap-1.5 px-3 py-2
                  rounded-lg text-sm font-medium
                  transition-all duration-200
                  whitespace-nowrap
                  ${isActive(item.path)
                    ? "bg-blue-100 text-blue-700 dark:bg-blue-900/50 dark:text-blue-300"
                    : "text-gray-600 hover:text-gray-900 hover:bg-gray-100/80 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/60"
                  }
                `}
              >
                <span className={isActive(item.path) ? "text-blue-600 dark:text-blue-400" : "text-gray-400 dark:text-gray-500"}>
                  {item.icon}
                </span>
                <span>{item.name}</span>
              </Link>
            ))}

            {/* Section Divider */}
            <div className="flex-shrink-0 w-px h-6 bg-gray-200 dark:bg-slate-600 my-auto mx-2" />

            {/* Dropdown Sections */}
            {menuSections.map((section) => {
              const filteredItems = getFilteredItems(section.items);
              if (filteredItems.length === 0) return null;

              const isOpen = openDropdown === section.name;
              const isActiveSection = isSectionActive(filteredItems);

              return (
                <div key={section.name} className="relative flex-shrink-0">
                  <button
                    ref={el => buttonRefs.current[section.name] = el}
                    onClick={() => toggleDropdown(section.name)}
                    className={`
                      flex items-center gap-1.5 px-3 py-2
                      rounded-lg text-sm font-medium
                      transition-all duration-200
                      whitespace-nowrap
                      ${isActiveSection
                        ? "bg-blue-100 text-blue-700 dark:bg-blue-900/50 dark:text-blue-300"
                        : "text-gray-600 hover:text-gray-900 hover:bg-gray-100/80 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/60"
                      }
                    `}
                  >
                    <span className={isActiveSection ? "text-blue-600 dark:text-blue-400" : "text-gray-400 dark:text-gray-500"}>
                      {section.icon}
                    </span>
                    <span>{section.name}</span>
                    <ChevronDownIcon className={`w-4 h-4 transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`} />
                  </button>
                </div>
              );
            })}
          </nav>
        </div>
      </div>

      {/* Fixed Position Dropdown Overlay */}
      {openDropdown && (
        <div
          ref={dropdownRef}
          className="fixed bg-white dark:bg-slate-800 rounded-xl shadow-xl ring-1 ring-black/5 dark:ring-white/10 py-2 z-[99999]"
          style={{
            top: dropdownPosition.top,
            left: dropdownPosition.left,
            minWidth: '220px',
            maxHeight: '450px',
            overflowY: 'auto'
          }}
        >
          {getFilteredItems(menuSections.find(s => s.name === openDropdown)?.items || []).map((item) => (
            <button
              key={item.path}
              onClick={() => handleMenuClick(item.path)}
              className={`
                w-full flex items-center gap-3 px-4 py-2.5
                text-sm font-medium
                transition-colors duration-150
                text-left
                ${isActive(item.path)
                  ? "bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300"
                  : "text-gray-600 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-slate-700/60"
                }
              `}
            >
              <span className={isActive(item.path) ? "text-blue-600 dark:text-blue-400" : "text-gray-400 dark:text-gray-500"}>
                {item.icon}
              </span>
              <span>{item.name}</span>
            </button>
          ))}
        </div>
      )}
    </>
  );
}
