import { Navigate } from "react-router-dom";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import toast from "react-hot-toast";

export default function SaleSystemGuard({ children, requiredSaleSystem = "wholesale" }) {
  const { hasWholesale, isRetailOnly, loading } = useSaleSystem();
  
  // Show loading state while fetching settings
  if (loading) {
    return (
      <div className="flex items-center justify-center h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
      </div>
    );
  }
  
  // If retail only mode is enabled and wholesale is required, redirect to retail
  if (requiredSaleSystem === "wholesale" && isRetailOnly) {
    // Show a toast message explaining why they were redirected
    toast.error("Wholesale sales are disabled. Please use Retail sales.");
    
    // Redirect to retail sale page
    return <Navigate to="/sale-invoices/create/retail" replace />;
  }
  
  return children;
}

