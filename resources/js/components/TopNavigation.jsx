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
  DocumentTextIcon,
  ArrowPathIcon,
  CalculatorIcon,
  ClipboardDocumentListIcon as ReportIcon,
} from "@heroicons/react/24/outline";
import { usePermissions } from "@/api/usePermissions";

// Section color configurations matching sidebar
const SECTION_CONFIG = {
  core: {
    gradient: "from-blue-500 to-cyan-600",
    bgLight: "bg-blue-50",
    bgDark: "dark:bg-blue-900/20",
    iconColor: "text-blue-600 dark:text-blue-400",
    ringColor: "ring-blue-300 dark:ring-blue-700",
  },
  invoices: {
    gradient: "from-emerald-500 to-teal-600",
    bgLight: "bg-emerald-50",
    bgDark: "dark:bg-emerald-900/20",
    iconColor: "text-emerald-600 dark:text-emerald-400",
    ringColor: "ring-emerald-300 dark:ring-emerald-700",
  },
  returns: {
    gradient: "from-orange-500 to-amber-600",
    bgLight: "bg-orange-50",
    bgDark: "dark:bg-orange-900/20",
    iconColor: "text-orange-600 dark:text-orange-400",
    ringColor: "ring-orange-300 dark:ring-orange-700",
  },
  transactions: {
    gradient: "from-indigo-500 to-blue-600",
    bgLight: "bg-indigo-50",
    bgDark: "dark:bg-indigo-900/20",
    iconColor: "text-indigo-600 dark:text-indigo-400",
    ringColor: "ring-indigo-300 dark:ring-indigo-700",
  },
  finance: {
    gradient: "from-green-500 to-emerald-600",
    bgLight: "bg-green-50",
    bgDark: "dark:bg-green-900/20",
    iconColor: "text-green-600 dark:text-green-400",
    ringColor: "ring-green-300 dark:ring-green-700",
  },
  reports: {
    gradient: "from-amber-500 to-orange-600",
    bgLight: "bg-amber-50",
    bgDark: "dark:bg-amber-900/20",
    iconColor: "text-amber-600 dark:text-amber-400",
    ringColor: "ring-amber-300 dark:ring-amber-700",
  },
  system: {
    gradient: "from-slate-500 to-gray-600",
    bgLight: "bg-slate-50",
    bgDark: "dark:bg-slate-700/30",
    iconColor: "text-slate-600 dark:text-slate-400",
    ringColor: "ring-slate-300 dark:ring-slate-700",
  },
};

export default function TopNavigation() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { loading: permsLoading, has } = usePermissions();
  const [openDropdown, setOpenDropdown] = useState(null);
  const [dropdownPosition, setDropdownPosition] = useState({ top: 0, left: 0 });
  const [hoveredDropdown, setHoveredDropdown] = useState(null);
  const buttonRefs = useRef({});
  const dropdownRef = useRef(null);
  const brandName = "Karobar App";
  const logoCandidates = ["/storage/logos/logo.png", "/logo.png"];

  // Organized menu with sections and items - Matching sidebar structure
  const menuSections = useMemo(() => [
    {
      name: "Management",
      key: "core",
      icon: <CubeIcon className="w-5 h-5" />,
      items: [
        { name: "Products", path: "/products", icon: <CubeIcon className="w-5 h-5" />, perm: "product.view" },
        { name: "Categories", path: "/categories", icon: <Squares2X2Icon className="w-5 h-5" />, perm: "category.view" },
        { name: "Brands", path: "/brands", icon: <TagIcon className="w-5 h-5" />, perm: "brand.view" },
        { name: "Suppliers", path: "/suppliers", icon: <TruckIcon className="w-5 h-5" />, perm: "supplier.view" },
        { name: "Customers", path: "/customers", icon: <UserPlusIcon className="w-5 h-5" />, perm: "customer.view" },
      ]
    },
    {
      name: "Invoices",
      key: "invoices",
      icon: <DocumentTextIcon className="w-5 h-5" />,
      items: [
        { name: "Purchase Invoice", path: "/purchase-invoices", icon: <ClipboardDocumentListIcon className="w-5 h-5" />, perm: "purchase-invoice.view" },
        { name: "Sale Invoice", path: "/sale-invoices", icon: <DocumentCurrencyDollarIcon className="w-5 h-5" />, perm: "sale-invoice.view" },
      ]
    },
    {
      name: "Returns",
      key: "returns",
      icon: <ArrowUturnLeftIcon className="w-5 h-5" />,
      items: [
        { name: "Purchase Return", path: "/purchase-returns", icon: <ArrowUturnDownIcon className="w-5 h-5" />, perm: "purchase-return.view" },
        { name: "Sale Return", path: "/sale-returns", icon: <ArrowUturnLeftIcon className="w-5 h-5" />, perm: "sale-return.view" },
      ]
    },
    {
      name: "Transactions",
      key: "transactions",
      icon: <ShoppingCartIcon className="w-5 h-5" />,
      items: [
        { name: "Purchase Orders", path: "/purchase-orders", icon: <ClipboardDocumentCheckIcon className="w-5 h-5" />, perm: "purchase-order.view" },
        { name: "Stock Adjustments", path: "/stock-adjustments", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "stock-adjustment.view" },
      ]
    },
    {
      name: "Finance",
      key: "finance",
      icon: <CurrencyDollarIcon className="w-5 h-5" />,
      items: [
        { name: "Supplier Ledger", path: "/supplier-ledger", icon: <BuildingStorefrontIcon className="w-5 h-5" />, perm: "ledger.supplier.view" },
        { name: "Customer Ledger", path: "/customer-ledger", icon: <UsersIcon className="w-5 h-5" />, perm: "ledger.customer.view" },
      ]
    },
    {
      name: "Reports",
      key: "reports",
      icon: <ChartBarIcon className="w-5 h-5" />,
      items: [
        { name: "Current Stock", path: "/reports/current-stock", icon: <CubeIcon className="w-5 h-5" />, perm: "report.current-stock.view" },
        { name: "Cost of Sale", path: "/reports/cost-of-sale", icon: <CalculatorIcon className="w-5 h-5" />, perm: "report.cost-of-sale.view" },
        { name: "Purchase Detail", path: "/reports/purchase-detail", icon: <ClipboardDocumentIcon className="w-5 h-5" />, perm: "report.purchase-detail.view" },
        { name: "Sale Detail", path: "/reports/sale-detail", icon: <DocumentCurrencyDollarIcon className="w-5 h-5" />, perm: "report.sale-detail.view" },
        { name: "Stock Adjustment", path: "/reports/stock-adjustment", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "report.stock-adjustment.view" },
        { name: "Product Comprehensive", path: "/reports/product-comprehensive", icon: <ReportIcon className="w-5 h-5" />, perm: "report.product-comprehensive.view" },
      ]
    },
    {
      name: "System",
      key: "system",
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

  // Get section config for styling
  const getSectionConfig = (key) => {
    return SECTION_CONFIG[key] || SECTION_CONFIG.core;
  };

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
        top: rect.bottom + 8,
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
        bg-white/90 dark:bg-slate-800/90
        backdrop-blur-xl
        border-b border-gray-200/60 dark:border-slate-700/60
        shadow-lg
      ">
        <div className="flex items-stretch h-13">
          {/* Logo & Brand - Enhanced visibility */}
          <div className="flex items-center gap-2.5 px-4 border-r border-gray-200/60 dark:border-slate-700/60 flex-shrink-0">
            <div className="relative group">
              <picture>
                {logoCandidates.map((src) => (
                  <img
                    key={src}
                    src={src}
                    alt={brandName}
                    className="h-9 w-9 object-contain rounded-xl shadow-md hidden"
                    onLoad={(e) => {
                      const imgs = e.currentTarget.parentElement.querySelectorAll("img");
                      imgs.forEach((im) => (im.style.display = "none"));
                      e.currentTarget.style.display = "block";
                    }}
                  />
                ))}
              </picture>
              {/* Decorative gradient ring */}
              <div className="absolute inset-0 rounded-xl bg-gradient-to-br from-violet-500/30 to-purple-500/30 dark:from-violet-400/30 dark:to-purple-400/30" />
            </div>
            <div className="flex flex-col">
              <span className="text-sm font-bold bg-gradient-to-r from-gray-900 to-gray-700 dark:from-white dark:to-gray-300 bg-clip-text text-transparent whitespace-nowrap">
                {brandName}
              </span>
              <span className="text-[10px] text-gray-500 dark:text-gray-400 font-medium">
                Management
              </span>
            </div>
          </div>
          
          {/* Navigation Items */}
          <nav
            className="
              flex-1 flex items-center gap-0.5
              overflow-x-auto
              px-2
              scrollbar-thin
            "
            role="navigation"
            aria-label="Main navigation"
          >
            {/* Dashboard Link - Violet themed */}
            {getFilteredItems(flatMenu).map((item) => {
              const active = isActive(item.path);
              return (
                <Link
                  key={item.path}
                  to={item.path}
                  className={`
                    flex-shrink-0 flex items-center gap-1 px-2.5 py-1.5
                    rounded-lg text-sm font-medium
                    transition-all duration-200
                    whitespace-nowrap
                    ${active
                      ? "bg-violet-100 text-violet-700 dark:bg-violet-900/50 dark:text-violet-300"
                      : "text-gray-600 hover:text-gray-900 hover:bg-gray-100/80 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/60"
                    }
                  `}
                >
                  <span className={active ? 'text-violet-600 dark:text-violet-400' : 'text-gray-400 dark:text-gray-500'}>
                    <HomeIcon className="w-4 h-4" />
                  </span>
                  <span>{item.name}</span>
                </Link>
              );
            })}

            {/* Section Divider */}
            <div className="flex-shrink-0 w-px h-5 bg-gray-200 dark:bg-slate-600 my-auto mx-1" />

            {/* Dropdown Sections - Color coded */}
            {menuSections.map((section) => {
              const filteredItems = getFilteredItems(section.items);
              if (filteredItems.length === 0) return null;

              const isOpen = openDropdown === section.name;
              const isActiveSection = isSectionActive(filteredItems);
              const config = getSectionConfig(section.key);

              return (
                <div 
                  key={section.name} 
                  className="relative flex-shrink-0"
                  onMouseEnter={() => setHoveredDropdown(section.key)}
                  onMouseLeave={() => setHoveredDropdown(null)}
                >
                  <button
                    ref={el => buttonRefs.current[section.name] = el}
                    onClick={() => toggleDropdown(section.name)}
                    className={`
                      flex items-center gap-1 px-2.5 py-1.5
                      rounded-lg text-sm font-medium
                      transition-all duration-200
                      whitespace-nowrap
                      ${isActiveSection || hoveredDropdown === section.key
                        ? `${config.bgLight} ${config.bgDark}`
                        : "text-gray-600 hover:text-gray-900 hover:bg-gray-100/80 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/60"
                      }
                    `}
                  >
                    <span className={isActiveSection ? config.iconColor : "text-gray-400 dark:text-gray-500"}>
                      {React.cloneElement(section.icon, { className: "w-4 h-4" })}
                    </span>
                    <span className={isActiveSection ? config.iconColor : ""}>{section.name}</span>
                    <ChevronDownIcon className={`w-3.5 h-3.5 transition-transform duration-200 ${isOpen ? 'rotate-180' : ''} ${isActiveSection ? config.iconColor.split(' ')[0] : 'text-gray-400'}`} />
                  </button>
                </div>
              );
            })}
          </nav>
        </div>
      </div>

      {/* Fixed Position Dropdown Overlay - Enhanced */}
      {openDropdown && (() => {
        const section = menuSections.find(s => s.name === openDropdown);
        const config = section ? getSectionConfig(section.key) : SECTION_CONFIG.core;
        const filteredItems = getFilteredItems(section?.items || []);
        
        return (
          <div
            ref={dropdownRef}
            className="fixed bg-white/95 dark:bg-slate-800/95 backdrop-blur-xl rounded-xl shadow-2xl ring-1 ring-black/5 dark:ring-white/10 py-2 z-[99999] overflow-hidden"
            style={{
              top: dropdownPosition.top,
              left: dropdownPosition.left,
              minWidth: '240px',
              maxHeight: '400px',
              overflowY: 'auto'
            }}
          >
            {/* Dropdown header with gradient */}
            <div className={`px-4 py-2 border-b border-gray-100 dark:border-slate-700 bg-gradient-to-r ${config.bgLight} ${config.bgDark}`}>
              <div className="flex items-center gap-2">
                <div className={`p-1 rounded-lg bg-white/50 dark:bg-slate-700/50`}>
                  <span className={config.iconColor}>
                    {React.cloneElement(section?.icon, { className: "w-4 h-4" })}
                  </span>
                </div>
                <span className={`text-sm font-semibold ${config.iconColor}`}>
                  {section?.name}
                </span>
              </div>
            </div>
            
            {/* Dropdown items */}
            <div className="py-1">
              {filteredItems.map((item, index) => {
                const itemActive = isActive(item.path);
                return (
                  <button
                    key={item.path}
                    onClick={() => handleMenuClick(item.path)}
                    className={`
                      w-full flex items-center gap-3 px-4 py-2.5
                      text-sm font-medium
                      transition-all duration-150
                      text-left
                      relative
                      ${itemActive
                        ? `bg-gradient-to-r ${config.bgLight} ${config.bgDark} ${config.iconColor}`
                        : "text-gray-600 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-slate-700/60"
                      }
                    `}
                  >
                    {/* Active indicator */}
                    {itemActive && (
                      <div className={`absolute left-0 top-1/2 -translate-y-1/2 w-1 h-6 rounded-r-full bg-gradient-to-b ${config.gradient}`} />
                    )}
                    <span className={`transition-transform duration-200 ${itemActive ? 'scale-110' : ''}`}>
                      <div className={itemActive ? '' : "text-gray-400 dark:text-gray-500"}>
                        {React.cloneElement(item.icon, { className: "w-5 h-5" })}
                      </div>
                    </span>
                    <span className={itemActive ? "font-semibold" : ""}>{item.name}</span>
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
