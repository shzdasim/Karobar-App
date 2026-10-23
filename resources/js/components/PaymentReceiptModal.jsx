import React, { useRef } from "react";
import {
  XMarkIcon,
  PrinterIcon,
  CheckCircleIcon,
  ArrowUturnLeftIcon,
  DocumentTextIcon,
} from "@heroicons/react/24/solid";
import { GlassCard } from "@/components/Glass.jsx";
import { useCurrency } from "@/context/CurrencyContext.jsx";

export default function PaymentReceiptModal({
  isOpen,
  onClose,
  payment,
}) {
  const { formatCurrency } = useCurrency();
  const printAreaRef = useRef(null);

  if (!isOpen || !payment) return null;

  const handlePrint = () => {
    window.print();
  };

  const isCustomer = payment.party_type === "customer";
  const party = isCustomer ? payment.customer : payment.supplier;
  const isReversed = payment.status === "reversed";
  const allocations = payment.allocations || [];

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-xs overflow-y-auto"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <style>{`
        @media print {
          body * {
            visibility: hidden;
          }
          #payment-printable-voucher, #payment-printable-voucher * {
            visibility: visible;
          }
          #payment-printable-voucher {
            position: absolute;
            left: 0;
            top: 0;
            width: 100%;
            background: white !important;
            color: black !important;
          }
          .no-print {
            display: none !important;
          }
        }
      `}</style>

      <div className="relative w-full max-w-2xl my-8">
        <GlassCard className="border border-slate-700/80 shadow-2xl rounded-2xl overflow-hidden bg-slate-900/95 text-white">
          {/* Modal Header */}
          <div className="flex items-center justify-between px-6 py-4 border-b border-slate-700/80 bg-slate-800/50 no-print">
            <div className="flex items-center gap-2">
              <DocumentTextIcon className="w-5 h-5 text-emerald-400" />
              <span className="font-bold text-base text-white">
                Payment Receipt Voucher
              </span>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={handlePrint}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-600 hover:bg-emerald-500 text-white transition-colors"
              >
                <PrinterIcon className="w-4 h-4" />
                <span>Print</span>
              </button>
              <button
                type="button"
                onClick={onClose}
                className="p-1.5 rounded-lg text-slate-400 hover:text-white hover:bg-slate-700/60 transition-colors"
              >
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>
          </div>

          {/* Printable Voucher Content */}
          <div id="payment-printable-voucher" ref={printAreaRef} className="p-8 space-y-6 bg-slate-900 text-white print:bg-white print:text-black">
            {/* Store / Voucher Header */}
            <div className="flex items-start justify-between border-b border-slate-700 print:border-gray-300 pb-5">
              <div>
                <h2 className="text-xl font-extrabold uppercase tracking-wide text-white print:text-black">
                  {isCustomer ? "Official Receipt" : "Payment Voucher"}
                </h2>
                <p className="text-xs text-slate-400 print:text-gray-600 mt-0.5">
                  Voucher #: <span className="font-mono font-bold text-white print:text-black">{payment.payment_number}</span>
                </p>
                <p className="text-xs text-slate-400 print:text-gray-600">
                  Date: <span className="font-semibold text-white print:text-black">{payment.payment_date}</span>
                </p>
              </div>

              <div className="text-right">
                {isReversed ? (
                  <span className="inline-flex items-center gap-1 px-3 py-1 rounded-md text-xs font-bold uppercase bg-rose-600/30 text-rose-400 border border-rose-500/40 print:border-red-600 print:text-red-600">
                    <ArrowUturnLeftIcon className="w-3.5 h-3.5" />
                    <span>REVERSED</span>
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 px-3 py-1 rounded-md text-xs font-bold uppercase bg-emerald-600/30 text-emerald-400 border border-emerald-500/40 print:border-green-600 print:text-green-700">
                    <CheckCircleIcon className="w-3.5 h-3.5" />
                    <span>POSTED</span>
                  </span>
                )}
              </div>
            </div>

            {/* Party & Payment Details Grid */}
            <div className="grid grid-cols-2 gap-4 text-xs bg-slate-800/40 print:bg-gray-50 p-4 rounded-xl border border-slate-700/60 print:border-gray-200">
              <div>
                <span className="text-slate-400 print:text-gray-500 uppercase tracking-wider block font-semibold text-[10px]">
                  {isCustomer ? "Received From" : "Paid To"}
                </span>
                <p className="text-sm font-bold text-white print:text-black mt-0.5">
                  {party?.name || (isCustomer ? "Walk-in Customer" : "Supplier")}
                </p>
                {party?.phone && <p className="text-slate-400 print:text-gray-600">{party.phone}</p>}
                {party?.address && <p className="text-slate-400 print:text-gray-600">{party.address}</p>}
              </div>

              <div>
                <span className="text-slate-400 print:text-gray-500 uppercase tracking-wider block font-semibold text-[10px]">
                  Tender / Method Details
                </span>
                <p className="text-sm font-bold text-white print:text-black mt-0.5 capitalize">
                  {payment.payment_method?.replace(/_/g, " ")}
                </p>
                {payment.reference && (
                  <p className="text-slate-300 print:text-gray-700">
                    Ref: <span className="font-mono">{payment.reference}</span>
                  </p>
                )}
                {payment.notes && (
                  <p className="text-slate-400 print:text-gray-600 italic mt-0.5">
                    "{payment.notes}"
                  </p>
                )}
              </div>
            </div>

            {/* Allocations Table */}
            <div>
              <h4 className="text-xs uppercase tracking-wider font-semibold text-slate-400 print:text-gray-600 mb-2">
                Settled Invoice Allocations
              </h4>
              {allocations.length === 0 ? (
                <div className="p-3 text-xs text-slate-400 print:text-gray-500 bg-slate-800/20 print:bg-gray-50 rounded-lg text-center">
                  No direct invoice allocations. 100% of payment retained as unapplied credit / advance.
                </div>
              ) : (
                <table className="w-full text-xs text-left border-collapse border border-slate-700 print:border-gray-300">
                  <thead className="bg-slate-800/70 print:bg-gray-100 text-slate-300 print:text-gray-700">
                    <tr>
                      <th className="p-2 border border-slate-700 print:border-gray-300">#</th>
                      <th className="p-2 border border-slate-700 print:border-gray-300">Invoice Number</th>
                      <th className="p-2 border border-slate-700 print:border-gray-300">Invoice Date</th>
                      <th className="p-2 border border-slate-700 print:border-gray-300 text-right">Allocated Amount</th>
                    </tr>
                  </thead>
                  <tbody>
                    {allocations.map((alloc, idx) => {
                      const inv = alloc.sale_invoice || alloc.purchase_invoice;
                      const invNo = inv?.posted_number || inv?.invoice_number || `#${alloc.invoice_id}`;
                      const invDate = inv?.date || inv?.posted_date || "—";
                      return (
                        <tr key={alloc.id || idx}>
                          <td className="p-2 border border-slate-700 print:border-gray-300 text-slate-400 print:text-gray-600">{idx + 1}</td>
                          <td className="p-2 border border-slate-700 print:border-gray-300 font-semibold text-white print:text-black">{invNo}</td>
                          <td className="p-2 border border-slate-700 print:border-gray-300 text-slate-400 print:text-gray-600">{invDate}</td>
                          <td className="p-2 border border-slate-700 print:border-gray-300 text-right font-mono font-bold text-white print:text-black">
                            {formatCurrency(alloc.amount)}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              )}
            </div>

            {/* Totals Summary */}
            <div className="flex justify-end pt-2">
              <div className="w-64 space-y-1.5 text-xs">
                <div className="flex justify-between py-1 border-b border-slate-800 print:border-gray-200">
                  <span className="text-slate-400 print:text-gray-600">Total Tender:</span>
                  <span className="font-bold font-mono text-white print:text-black">{formatCurrency(payment.amount)}</span>
                </div>
                <div className="flex justify-between py-1 border-b border-slate-800 print:border-gray-200">
                  <span className="text-slate-400 print:text-gray-600">Allocated to Invoices:</span>
                  <span className="font-bold font-mono text-blue-400 print:text-blue-700">{formatCurrency(payment.allocated_amount)}</span>
                </div>
                <div className="flex justify-between py-1 border-b-2 border-slate-700 print:border-gray-400 font-bold">
                  <span className="text-slate-300 print:text-gray-700">Unapplied Credit:</span>
                  <span className="font-mono text-emerald-400 print:text-green-700">{formatCurrency(payment.unapplied_amount)}</span>
                </div>
              </div>
            </div>

            {/* Reversal Banner if applicable */}
            {isReversed && (
              <div className="p-3 rounded-lg bg-rose-900/30 border border-rose-700/60 text-xs text-rose-300 print:border-red-500 print:text-red-700">
                <span className="font-bold">Reversal Notice:</span> This payment was reversed on {payment.reversed_at ? new Date(payment.reversed_at).toLocaleString() : ""}.
                {payment.reversal_reason && <p className="mt-0.5 font-semibold">Reason: "{payment.reversal_reason}"</p>}
              </div>
            )}
          </div>
        </GlassCard>
      </div>
    </div>
  );
}

