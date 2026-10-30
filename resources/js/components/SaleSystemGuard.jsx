import { Navigate } from "react-router-dom";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import PageSkeleton from "./PageSkeleton.jsx";
import toast from "react-hot-toast";

export default function SaleSystemGuard({ children, requiredSaleSystem = "wholesale" }) {
  const { hasWholesale, isRetailOnly, loading } = useSaleSystem();
  
  // Show skeleton state while fetching settings
  if (loading) {
    return <PageSkeleton />;
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

