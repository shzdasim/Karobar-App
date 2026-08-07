import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import toast from "react-hot-toast";
import axios from "axios";
import SaleInvoiceWholesaleForm from "./SaleInvoiceWholesaleForm.jsx";
import { usePermissions } from "@/api/usePermissions.js";

export default function CreateSaleInvoiceWholesale() {
  const navigate = useNavigate();

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () => (typeof canFor === "function" ? canFor("sale-invoice") : {
      view:false, create:false, update:false, delete:false, import:false, export:false
    }),
    [canFor]
  );

  const onSubmit = async (payload) => {
    if (!can.create) {
      toast.error("You don't have permission to create sale invoices.");
      return;
    }
    try {
      const { data } = await axios.post("/api/sale-invoices", payload);
      toast.success("Wholesale sale invoice created");
      navigate("/sale-invoices");
      return data;
    } catch (e) {
      const msg = e?.response?.data?.message || "Create failed";
      toast.error(msg);
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.create) return <div className="p-6 text-sm text-gray-700">You don't have permission to create sale invoices.</div>;

  return (
    <div className="-mx-4 md:-mx-6 -mt-2 md:-mt-2">
      <SaleInvoiceWholesaleForm onSubmit={onSubmit} onSuccess={() => navigate("/sale-invoices")} />
    </div>
  );
}

