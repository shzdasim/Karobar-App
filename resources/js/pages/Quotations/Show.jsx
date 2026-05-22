import React, { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";

export default function QuotationsShow() {
  const { id } = useParams();
  const [quotation, setQuotation] = useState(null);

  useEffect(() => {
    (async () => {
      try {
        const res = await axios.get(`/api/quotations/${id}`);
        setQuotation(res.data);
      } catch {
        toast.error("Failed to load quotation");
      }
    })();
  }, [id]);

  if (!quotation) return <div className="p-6 text-sm">Loading…</div>;

  return (
    <div className="p-6 space-y-4">
      <div className="text-lg font-semibold">{quotation.posted_number}</div>
      <div className="text-sm text-gray-600">Customer: {quotation.customer?.name ?? 'N/A'}</div>
      <div className="text-sm text-gray-600">Date: {quotation.date}</div>
      <div className="text-sm font-semibold">Total: {Number(quotation.total ?? 0).toLocaleString()}</div>

      <div>
        <div className="font-semibold text-sm mb-2">Items</div>
        <table className="w-full text-sm border rounded">
          <thead>
            <tr className="text-left bg-gray-100">
              <th className="p-2">Name</th>
              <th className="p-2">Qty</th>
              <th className="p-2">Price</th>
              <th className="p-2">Disc%</th>
              <th className="p-2">Sub Total</th>
            </tr>
          </thead>
          <tbody>
            {(quotation.items || []).map((it) => (
              <tr key={it.id} className="border-t">
                <td className="p-2">
                  {it.line_type === 'manual' ? (it.manual_name || '-') : (it.product?.name ?? '-')}
                </td>
                <td className="p-2">{it.quantity}</td>
                <td className="p-2">{Number(it.price ?? 0).toFixed(2)}</td>
                <td className="p-2">{Number(it.item_discount_percentage ?? 0).toFixed(2)}</td>
                <td className="p-2">{Number(it.sub_total ?? 0).toFixed(2)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

    </div>
  );
}

