// src/layouts/DashboardLayout.jsx
import { useEffect, useState } from "react";
import axios from "axios";
import { useLocation } from "react-router-dom";
import Sidebar from "../components/Sidebar.jsx";
import Topbar from "../components/Topbar.jsx";
import { Toaster } from "react-hot-toast";

export default function DashboardLayout({ children }) {
  const location = useLocation();

  // Brand from /api/settings
  const [appName, setAppName] = useState("ERP");
  const [logoUrl, setLogoUrl] = useState(null);

  // Fetch settings from API and sync with localStorage
  useEffect(() => {
    (async () => {
      try {
        const { data } = await axios.get("/api/settings");
        const sn = (data?.store_name || "").trim();
        setAppName(sn || "ERP");
        setLogoUrl(data?.logo_url || null);
      } catch {
        setAppName("ERP");
        setLogoUrl(null);
      }
    })();
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
    "/purchase-invoices/create": "Purchase Invoice",
    "/purchase-returns": "Purchase Returns",
    "/purchase-returns/create": "Create Purchase Return",
    "/sale-invoices": "Sale Invoices",
    "/sale-invoices/create": "Create Sale Invoice",
    "/sale-returns": "Sale Returns",
    "/sale-returns/create": "Create Sale Return",
    "/purchase-orders": "Purchase Orders",
    "/user-demands": "User Demands",
    "/user-demands/create": "Request Product",
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

  // Single fixed layout: sidebar + topbar
  return (
    <div
      className="
        h-screen bg-gray-100 dark:bg-slate-900
        grid grid-rows-[auto_1fr] grid-cols-[auto_1fr]
        overflow-hidden
        transform-gpu
      "
    >
      {/* Sidebar: left column, spans both rows */}
      <div className="row-span-2 col-start-1 relative">
        <div className="sticky top-0 h-screen min-h-0">
          <Sidebar appName={appName} logoUrl={logoUrl} />
        </div>
      </div>

      {/* Topbar: top-right cell */}
      <div className="row-start-1 col-start-2 relative z-40">
        <Topbar pageTitle={pageTitle} />
      </div>

      {/* Main content: bottom-right cell */}
      <main className="row-start-2 col-start-2 min-h-0 overflow-y-auto">
        <div className="p-4 md:p-6 h-full flex flex-col">
          <Toaster position="top-right" reverseOrder={false} />
          {children}
        </div>
      </main>
    </div>
  );
}
