import { createContext, useContext, useState, useEffect } from "react";
import axios from "axios";

const SaleSystemContext = createContext(null);

export function SaleSystemProvider({ children }) {
  const [saleSystem, setSaleSystem] = useState("retail_wholesale"); // default to retail + wholesale
  const [shopType, setShopType] = useState("pharmacy"); // default to pharmacy
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchSaleSystem();
    
    // Listen for settings changes to refresh sale system setting
    const handleSettingsChange = () => {
      fetchSaleSystem();
    };
    
    window.addEventListener('settingsChanged', handleSettingsChange);
    
    return () => {
      window.removeEventListener('settingsChanged', handleSettingsChange);
    };
  }, []);

  const fetchSaleSystem = async () => {
    try {
      const { data } = await axios.get("/api/settings");
      setSaleSystem(data?.sale_system || "retail_wholesale");
      setShopType(data?.shop_type || "pharmacy");
    } catch (error) {
      console.error("Failed to fetch sale system setting:", error);
      setSaleSystem("retail_wholesale"); // default on error
    } finally {
      setLoading(false);
    }
  };

  const hasWholesale = saleSystem === "retail_wholesale";
  const isRetailOnly = saleSystem === "retail";

  const isPharmacy = shopType === "pharmacy";
  const isGeneralStore = shopType === "general_store";

  const value = {
    saleSystem,
    hasWholesale,
    isRetailOnly,
    shopType,
    isPharmacy,
    isGeneralStore,
    loading,
    refreshSaleSystem: fetchSaleSystem,
  };

  return (
    <SaleSystemContext.Provider value={value}>
      {children}
    </SaleSystemContext.Provider>
  );
}

export function useSaleSystem() {
  const context = useContext(SaleSystemContext);
  if (!context) {
    throw new Error("useSaleSystem must be used within a SaleSystemProvider");
  }
  return context;
}

