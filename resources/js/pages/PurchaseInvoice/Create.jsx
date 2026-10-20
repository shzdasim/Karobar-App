import { useMemo, useState, useEffect } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import PurchaseInvoiceForm from "./PurchaseInvoiceForm";
import { usePermissions } from "@/api/usePermissions.js";

export default function CreatePurchaseInvoice() {
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const orderId = params.get('purchase_order_id');
  const [initialData, setInitialData] = useState(null);
  const [sourceError, setSourceError] = useState('');
  useEffect(() => {
    if (!orderId) return;
    let active = true;
    setInitialData(null); setSourceError('');
    if (String(location.state?.purchaseOrderReceipt?.purchase_order_id) === orderId) {
      setInitialData(location.state.purchaseOrderReceipt);
      return;
    }
    axios.post(`/api/purchase-orders/${orderId}/receive`).then(({ data }) => {
      if (active) setInitialData(data);
    }).catch(e => { if (active) setSourceError(e.response?.data?.message || 'Unable to prepare Purchase Invoice.'); });
    return () => { active = false; };
  }, [orderId, location.state]);
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () => (typeof canFor === "function" ? canFor("purchase-invoice") : {
      view:false, create:false, update:false, delete:false, import:false, export:false
    }),
    [canFor]
  );

  const handleSubmit = async (payload) => {
    if (!can.create) {
      toast.error("You don't have permission to create purchase invoices.");
      return;
    }
    const { data } = await axios.post("/api/purchase-invoices", { ...payload, purchase_order_id: orderId || null });
    toast.success(
      data.purchase_order
        ? data.purchase_order.status === "partially_received"
          ? `Purchase Invoice ${data.posted_number} created from Purchase Order ${data.purchase_order.number}. Undelivered items added to User Demands.`
          : `Purchase Invoice ${data.posted_number} created from Purchase Order ${data.purchase_order.number}.`
        : "Purchase invoice created"
    );
    // Navigate to the show page
    navigate(`/purchase-invoices/${data.id}`, { replace: true });
    return data;
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.create) return <div className="p-6 text-sm text-gray-700">You don't have permission to create purchase invoices.</div>;

  if (orderId && sourceError) return <div className="p-6" role="alert">{sourceError}<button className="g-btn-secondary ml-3" onClick={() => navigate(`/purchase-orders/${orderId}`)}>Back to Purchase Order</button></div>;
  if (orderId && !initialData) return <div className="p-6">Preparing Purchase Invoice…</div>;
  return (
    <div className="purchase-entry-page">
      <PurchaseInvoiceForm key={orderId || "new"} initialData={initialData} onSubmit={handleSubmit} />
    </div>
  );
}

