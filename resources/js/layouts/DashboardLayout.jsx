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

  // Mobile sidebar state
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);

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

  // Close mobile sidebar on route change
  useEffect(() => {
    setMobileSidebarOpen(false);
  }, [location.pathname]);

  // Prevent body scroll when mobile sidebar is open
  useEffect(() => {
    if (mobileSidebarOpen) {
      document.body.style.overflow = "hidden";
    } else {
      document.body.style.overflow = "";
    }
    return () => {
      document.body.style.overflow = "";
    };
  }, [mobileSidebarOpen]);

  // --- Responsive layout ---
  //  • Mobile (< lg):  sidebar becomes a slide-over overlay
  //  • Desktop (≥ lg):  sidebar is always visible (fixed)
  return (
    <div className="flex min-h-screen bg-gray-50 dark:bg-slate-900 text-gray-900 dark:text-gray-100 transition-colors">
      {/* Desktop sidebar — always visible on lg+ */}
      <div className="hidden lg:block lg:shrink-0">
        <div className="sticky top-0 h-screen overflow-y-auto">
          <Sidebar appName={appName} logoUrl={logoUrl} />
        </div>
      </div>

      {/* Mobile sidebar overlay — slide-in panel on < lg */}
      {mobileSidebarOpen && (
        <>
          {/* Backdrop */}
          <div
            className="fixed inset-0 bg-black/40 backdrop-blur-sm z-40 lg:hidden kd-mobile-backdrop"
            onClick={() => setMobileSidebarOpen(false)}
            aria-label="Close sidebar"
          />
          {/* Slide-in panel */}
          <div
            className="
              fixed inset-y-0 left-0 z-50 w-64 max-w-[260px]
              kd-mobile-panel
              lg:hidden
            "
          >
            <div className="h-full overflow-y-auto shadow-xl">
              <Sidebar appName={appName} logoUrl={logoUrl} />
            </div>
          </div>
        </>
      )}

      {/* Main content area — topbar + page */}
      <div className="flex-1 flex flex-col overflow-hidden">
        <Topbar
          pageTitle={pageTitle}
          onMobileMenuClick={() => setMobileSidebarOpen(true)}
        />
        <main className="flex-1 overflow-y-auto">
          <div className="p-4 sm:p-5 lg:p-6">
            <Toaster position="top-right" reverseOrder={false} />
            {children}
          </div>
        </main>
      </div>
    </div>
  );
}
