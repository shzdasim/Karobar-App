// src/layouts/DashboardLayout.jsx
import { useEffect, useState } from "react";
import axios from "axios";
import { useLocation } from "react-router-dom";
import Sidebar from "../components/Sidebar.jsx";
import Topbar from "../components/Topbar.jsx";
import TopNavigation from "../components/TopNavigation.jsx";
import { Toaster } from "react-hot-toast";

export default function DashboardLayout({ children }) {
  const location = useLocation();

  // Brand from /api/settings
  const [appName, setAppName] = useState("ERP");
  const [logoUrl, setLogoUrl] = useState(null);
  
  // Navigation style - read from localStorage first for instant effect
  const [navigationStyle, setNavigationStyle] = useState(() => {
    return localStorage.getItem('navigation_style') || 'sidebar';
  });

  // Fetch settings from API and sync with localStorage
  useEffect(() => {
    (async () => {
      try {
        const { data } = await axios.get("/api/settings");
        const sn = (data?.store_name || "").trim();
        setAppName(sn || "ERP");
        setLogoUrl(data?.logo_url || null);
        
        // Get navigation_style from API, fallback to localStorage, then default
        const apiNavStyle = data?.navigation_style || localStorage.getItem('navigation_style') || 'sidebar';
        setNavigationStyle(apiNavStyle);
        localStorage.setItem('navigation_style', apiNavStyle);
      } catch {
        setAppName("ERP");
        setLogoUrl(null);
        // Keep localStorage value on error
      }
    })();
  }, []);

  // Listen for settings changes from other components
  useEffect(() => {
    const handleSettingsChange = (event) => {
      const { navigation_style } = event.detail;
      if (navigation_style) {
        setNavigationStyle(navigation_style);
        localStorage.setItem('navigation_style', navigation_style);
      }
    };

    window.addEventListener('settingsChanged', handleSettingsChange);
    return () => window.removeEventListener('settingsChanged', handleSettingsChange);
  }, []);

  // Page titles
  const titles = {
    "/dashboard": "Dashboard",
    "/profile": "Profile",
    "/suppliers": "Suppliers",
    "/customers": "Customers",
    "/products": "Products",
    "/categories": "Categories",
    "/brands": "Brands",
    "/purchase-invoices": "Purchase Invoices",
    "/purchase-invoices/create": "Create Purchase Invoice",
    "/purchase-returns": "Purchase Returns",
    "/purchase-returns/create": "Create Purchase Return",
    "/sale-invoices": "Sale Invoices",
    "/sale-invoices/create": "Create Sale Invoice",
    "/sale-returns": "Sale Returns",
    "/sale-returns/create": "Create Sale Return",
    "/purchase-orders": "Purchase Orders",
    "/settings": "Settings",
    "/reports": "Reports",
    "/users": "Users",
    "/roles": "Roles",
    "/reports/sale-detail": "Sales Detail Report",
    "/reports/purchase-detail": "Purchase Detail Report",
    "/reports/cost-of-sale": "Cost of Sale Report",
    "/supplier-ledger": "Supplier Ledger",
    "/customer-ledger": "Customer Ledger",
    "/stock-adjustments": "Stock Adjustments",
  };

  const currentPath = location.pathname;
  const pageTitle = titles[currentPath] || "Dashboard";

  useEffect(() => {
    document.title = `${pageTitle} - ${appName || "ERP"}`;
  }, [pageTitle, appName]);

  // Use original grid-based layout
  return (
    <div
      className="
        h-screen bg-gray-100 dark:bg-slate-900
        grid grid-rows-[auto,1fr] grid-cols-[auto,1fr]
        overflow-hidden
        transform-gpu
      "
    >
      {/* Sidebar: left column, spans both rows */}
      <div className="row-span-2 col-start-1 relative">
        {navigationStyle === "sidebar" && (
          <div className="sticky top-0 h-screen min-h-0">
            <Sidebar appName={appName} logoUrl={logoUrl} />
          </div>
        )}
      </div>

      {/* Topbar: top-right cell */}
      <div className="row-start-1 col-start-2 relative z-40">
        <Topbar pageTitle={pageTitle} />
        {/* TopNavigation below Topbar - only in topbar mode */}
        {navigationStyle === "topbar" && <TopNavigation />}
      </div>

      {/* Main content: bottom-right cell */}
      <main className="row-start-2 col-start-2 min-h-0 overflow-y-auto">
        <div className="p-4 md:p-6">
          <Toaster position="top-right" reverseOrder={false} />
          {children}
        </div>
      </main>
    </div>
  );
}
