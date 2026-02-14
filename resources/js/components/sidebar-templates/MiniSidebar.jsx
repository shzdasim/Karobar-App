// resources/js/components/sidebar-templates/MiniSidebar.jsx
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

export default function MiniSidebar() {
  const [hoveredItem, setHoveredItem] = useState(null);
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { theme } = useTheme();
  const { loading: permsLoading, has } = usePermissions();

  const logoCandidates = ["/storage/logos/logo.png", "/logo.png"];

  const rawMenu = useMemo(() => [
    { name: "Dashboard", path: "/dashboard", icon: <HomeIcon className="w-5 h-5" /> },
    { name: "Products", path: "/products", icon: <CubeIcon className="w-5 h-5" />, perm: "product.view" },
    { name: "Categories", path: "/categories", icon: <Squares2X2Icon className="w-5 h-5" />, perm: "category.view" },
    { name: "Brands", path: "/brands", icon: <TagIcon className="w-5 h-5" />, perm: "brand.view" },
    { name: "Suppliers", path: "/suppliers", icon: <TruckIcon className="w-5 h-5" />, perm: "supplier.view" },
    { name: "Customers", path: "/customers", icon: <UserPlusIcon className="w-5 h-5" />, perm: "customer.view" },
    { name: "Purchase", path: "/purchase-invoices", icon: <ClipboardDocumentListIcon className="w-5 h-5" />, perm: "purchase-invoice.view" },
    { name: "Sales", path: "/sale-invoices", icon: <DocumentCurrencyDollarIcon className="w-5 h-5" />, perm: "sale-invoice.view" },
    { name: "Returns", path: "/purchase-returns", icon: <ArrowUturnLeftIcon className="w-5 h-5" />, perm: "purchase-return.view" },
    { name: "Stock", path: "/stock-adjustments", icon: <ArrowsRightLeftIcon className="w-5 h-5" />, perm: "stock-adjustment.view" },
    { name: "Ledger", path: "/supplier-ledger", icon: <BuildingStorefrontIcon className="w-5 h-5" />, perm: "ledger.supplier.view" },
    { name: "Reports", path: "/reports/current-stock", icon: <ChartBarIcon className="w-5 h-5" />, perm: "report.current-stock.view" },
    { name: "Settings", path: "/settings", icon: <Cog6ToothIcon className="w-5 h-5" />, perm: "settings.view" },
  ], []);

  const menu = useMemo(() => {
    return rawMenu.filter(it => it.perm ? (!permsLoading && has(it.perm)) : true);
  }, [rawMenu, permsLoading, has]);

  const isActive = (path) => pathname === path || pathname.startsWith(path + "/");

  const getItemColor = (item) => {
    const active = isActive(item.path);
    const baseColor = getThemeColor(theme, 'primary', 'color');
    const lightColor = getThemeColor(theme, 'primary', 'light');
    return { baseColor, lightColor, active };
  };

  // Mini styling - ultra compact icon-only sidebar
  const shell = "relative h-screen w-16 bg-gray-50 dark:bg-slate-900 border-r border-gray-200 dark:border-slate-700 flex flex-col items-center py-4";

  return (
    <aside className={shell}>
      {/* Logo */}
      <div className="mb-6">
        <picture>
          {logoCandidates.map((src) => (
            <img key={src} src={src} alt="Logo" className="h-8 w-8 object-contain rounded hidden"
              onLoad={(e) => {
                const imgs = e.currentTarget.parentElement.querySelectorAll("img");
                imgs.forEach((im) => (im.style.display = "none"));
                e.currentTarget.style.display = "block";
              }}
            />
          ))}
        </picture>
      </div>

      {/* Navigation */}
      <nav className="flex-1 flex flex-col items-center gap-1 w-full overflow-y-auto [&::-webkit-scrollbar]:w-0" role="navigation" aria-label="Main navigation">
        {menu.map((item) => {
          const { baseColor, lightColor, active } = getItemColor(item);
          const isHovered = hoveredItem === item.path;

          return (
            <Link
              key={item.path}
              to={item.path}
              className="relative w-12 h-12 flex items-center justify-center rounded-xl transition-all duration-200"
              style={{
                backgroundColor: active ? lightColor : (isHovered ? lightColor + '60' : 'transparent'),
                color: active ? baseColor : '#64748b',
              }}
              onMouseEnter={() => setHoveredItem(item.path)}
              onMouseLeave={() => setHoveredItem(null)}
              aria-current={active ? "page" : undefined}
            >
              <span className={`transition-transform duration-200 ${isHovered ? 'scale-110' : ''}`}>
                {React.cloneElement(item.icon, { className: "w-5 h-5" })}
              </span>
              
              {/* Tooltip */}
              {isHovered && (
                <div 
                  className="absolute left-full ml-3 px-3 py-1.5 text-xs font-medium rounded-lg shadow-lg whitespace-nowrap z-50"
                  style={{ 
                    backgroundColor: baseColor,
                    color: '#ffffff',
                  }}
                >
                  {item.name}
                  <div 
                    className="absolute left-0 top-1/2 -translate-x-1 -translate-y-1/2 w-2 h-2 rotate-45"
                    style={{ backgroundColor: baseColor }}
                  />
                </div>
              )}
              
              {/* Active indicator */}
              {active && (
                <div 
                  className="absolute left-0 top-1/2 -translate-y-1/2 w-1 h-6 rounded-r-full"
                  style={{ backgroundColor: baseColor }}
                />
              )}
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}

