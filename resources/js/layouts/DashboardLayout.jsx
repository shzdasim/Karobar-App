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
    <div className="workspace-shell">
      {/* Desktop sidebar — always visible on lg+ */}
      <div className="workspace-sidebar-slot hidden lg:block lg:shrink-0">
        <div className="workspace-sidebar-position">
          <Sidebar appName={appName} logoUrl={logoUrl} />
        </div>
      </div>

      {/* Mobile sidebar overlay — slide-in panel on < lg */}
      {mobileSidebarOpen && (
        <>
          {/* Backdrop */}
          <div
            className="workspace-mobile-scrim fixed inset-0 lg:hidden kd-mobile-backdrop"
            onClick={() => setMobileSidebarOpen(false)}
            aria-label="Close sidebar"
          />
          {/* Slide-in panel */}
          <div
            className="
              workspace-mobile-drawer fixed inset-y-3 left-3 w-[248px] max-w-[calc(100vw-24px)]
              kd-mobile-panel
              lg:hidden
            "
          >
            <div className="h-full">
              <Sidebar appName={appName} logoUrl={logoUrl} />
            </div>
          </div>
        </>
      )}

      {/* Main content area — topbar + page */}
      <div className="workspace-main">
        <Topbar
          pageTitle={pageTitle}
          onMobileMenuClick={() => setMobileSidebarOpen(true)}
        />
        <main className="workspace-content">
          <div className="workspace-page">
            <Toaster position="top-right" reverseOrder={false} />
            {children}
          </div>
        </main>
      </div>
    </div>
  );
}
