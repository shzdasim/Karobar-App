import { useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { ArrowDownOnSquareIcon, ArrowPathIcon, DocumentChartBarIcon } from "@heroicons/react/24/solid";
import { usePermissions } from "@/api/usePermissions";
import SupplierSearch from "@/components/SupplierSearch.jsx";

const today = () => new Date().toISOString().slice(0, 10);
const monthStart = () => `${today().slice(0, 7)}-01`;
const number = (value) => (Number.isFinite(Number(value)) ? Number(value) : 0);
const money = (value) => number(value).toLocaleString(undefined, {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const REPORTS = {
  "purchase-returns": {
    title: "Purchase Returns Report",
    endpoint: "/api/reports/purchase-returns",
    permission: "purchase-return.view",
    dated: true,
    summary: [
      ["return_count", "Returns"],
      ["item_count", "Item lines"],
      ["quantity", "Units returned"],
      ["total", "Return value", true],
    ],
    columns: [
      ["posted_number", "Return #"],
      ["date", "Date"],
      ["supplier_name", "Supplier"],
      ["invoice_number", "Purchase invoice"],
      ["item_count", "Lines"],
      ["quantity", "Units"],
      ["total", "Return total", true],
    ],
  },
  "sale-returns": {
    title: "Sale Returns Report",
    endpoint: "/api/reports/sale-returns",
    permission: "sale-return.view",
    dated: true,
    summary: [
      ["return_count", "Returns"],
      ["item_count", "Item lines"],
      ["quantity", "Units returned"],
      ["total", "Return value", true],
    ],
    columns: [
      ["posted_number", "Return #"],
      ["date", "Date"],
      ["customer_name", "Customer"],
      ["invoice_number", "Sale invoice"],
      ["item_count", "Lines"],
      ["quantity", "Units"],
      ["total", "Return total", true],
    ],
  },
  "purchase-summary": {
    title: "Purchase Summary Report",
    endpoint: "/api/reports/purchase-summary",
    permission: "report.purchase-detail.view",
    dated: true,
    summary: [
      ["invoice_count", "Invoices"],
      ["item_count", "Item lines"],
      ["quantity", "Units purchased"],
      ["items_value", "Item purchase value", true],
      ["invoice_total", "Invoice total", true],
      ["total_paid", "Paid", true],
    ],
    columns: [
      ["posted_number", "Posted #"],
      ["date", "Date"],
      ["invoice_number", "Invoice #"],
      ["supplier_name", "Supplier"],
      ["item_count", "Lines"],
      ["quantity", "Quantity"],
      ["items_value", "Item value", true],
      ["invoice_total", "Invoice total", true],
      ["total_paid", "Paid", true],
    ],
  },
  "outstanding-aging": {
    title: "Outstanding Aging Report",
    endpoint: "/api/reports/outstanding-aging",
    permission: null,
    dated: false,
  },
  reorder: {
    title: "Low Stock & Reorder Report",
    endpoint: "/api/reports/reorder",
    permission: "report.current-stock.view",
    dated: false,
  },
};

function downloadCsv(filename, headers, rows) {
  const escape = (value) => `"${String(value ?? "").replaceAll('"', '""')}"`;
  const content = [headers, ...rows].map((row) => row.map(escape).join(",")).join("\r\n");
  const url = URL.createObjectURL(new Blob(["\uFEFF", content], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}

export default function MissingReports({ type }) {
  const report = REPORTS[type];
  const { loading: permissionsLoading, has } = usePermissions();
  const permitted = report?.permission
    ? has(report.permission)
    : has("customer-ledger.view") || has("supplier-ledger.view");
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const [asOf, setAsOf] = useState(today());
  const [payload, setPayload] = useState(null);
  const [loading, setLoading] = useState(false);
  const [partyType, setPartyType] = useState("customers");
  const [supplier, setSupplier] = useState(null);
  const [supplierSearchOpen, setSupplierSearchOpen] = useState(false);

  const selectedPartyType = partyType === "customers" && !has("customer-ledger.view") ? "suppliers" : partyType;
  const rows = useMemo(() => {
    if (type === "outstanding-aging") return payload?.[selectedPartyType] || [];
    return payload?.rows || [];
  }, [payload, selectedPartyType, type]);

  if (!report) return <div className="report-page"><section className="products-panel"><p className="report-state">Unknown report.</p></section></div>;

  const loadReport = async () => {
    if (!permitted) return toast.error("You don't have permission to view this report.");
    if (report.dated && (!from || !to || from > to)) return toast.error("Choose a valid date range.");
    if (type === "reorder" && !supplier?.id) return toast.error("Select a supplier to run the reorder report.");
    setLoading(true);
    try {
      const { data } = await axios.get(report.endpoint, {
        params: report.dated
          ? { from, to }
          : type === "outstanding-aging"
            ? { as_of: asOf }
            : type === "reorder"
              ? { supplier_id: supplier.id }
              : undefined,
      });
      setPayload(data);
      if (!(data?.rows?.length || data?.customers?.length || data?.suppliers?.length)) {
        toast("No data found for the selected report.", { icon: "ℹ️" });
      }
    } catch (error) {
      console.error(error);
      toast.error(`Failed to load ${report.title}.`);
      setPayload(null);
    } finally {
      setLoading(false);
    }
  };

  const exportCsv = () => {
    if (!rows.length) return;
    if (type === "outstanding-aging") {
      downloadCsv(
        "outstanding-aging.csv",
        ["Party type", "Party", "Reference", "Entry type", "Date", "Invoice amount", "Paid on invoice", "Payments allocated", "Outstanding", "Age (days)", "Age bucket"],
        rows.flatMap((party) => party.entries.map((entry) => [
          selectedPartyType === "customers" ? "Customer" : "Supplier",
          party.party_name,
          entry.reference,
          entry.entry_type,
          entry.date,
          entry.invoice_amount,
          entry.paid_on_invoice,
          entry.payments_applied,
          entry.outstanding,
          entry.age_days,
          entry.aging_bucket.replaceAll("_", "-"),
        ]))
      );
      return;
    }
    if (type === "reorder") {
      const fields = ["product_code", "name", "category_name", "brand_name", "supplier_name", "quantity", "pack_size", "suggested_reorder_quantity", "unit_purchase_price", "estimated_reorder_value"];
      downloadCsv("low-stock-reorder.csv", ["Code", "Product", "Category", "Brand", "Supplier", "On hand", "Pack size", "Suggested reorder", "Unit cost", "Estimated value"],
        rows.map((row) => fields.map((field) => row[field])));
      return;
    }
    const getValue = (row, field) => {
      if (field === "item_count") return row.item_count ?? row.items?.length ?? 0;
      if (field === "quantity") return row.quantity ?? row.items?.reduce(
        (sum, item) => sum + (item.quantity != null
          ? number(item.quantity)
          : number(item.return_unit_quantity) + (number(item.return_pack_quantity) * number(item.pack_size))),
        0
      ) ?? 0;
      return row[field] ?? "";
    };
    downloadCsv(`${type}.csv`, report.columns.map(([, label]) => label),
      rows.map((row) => report.columns.map(([field]) => getValue(row, field))));
  };

  if (permissionsLoading) {
    return <div className="report-page"><section className="products-panel"><p className="report-state">Checking permissions…</p></section></div>;
  }
  if (!permitted) {
    return <div className="report-page"><section className="products-panel"><p className="report-state">You don't have permission to view this report.</p></section></div>;
  }

  const summary = payload?.summary || {};
  const selectedAgingSummary = summary[selectedPartyType] || {};
  const summaryCards = report.summary || (type === "outstanding-aging"
    ? [
        ["total", "Total outstanding", true],
        ["party_count", selectedPartyType === "customers" ? "Customers with balance" : "Suppliers with balance"],
        ["open_entry_count", "Open invoices / entries"],
        ["over_90", "Over 90 days", true],
      ]
    : type === "reorder"
      ? [
          ["product_count", "Products below threshold"],
          ["total_quantity", "Units on hand"],
          ["suggested_quantity", "Suggested reorder units"],
          ["estimated_reorder_value", "Estimated reorder value", true],
        ]
      : []);
  return (
    <div className="report-page">
      <section className="products-panel">
        <div className="products-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="products-identity-icon"><DocumentChartBarIcon className="w-6 h-6" /></div>
            <div className="min-w-0">
              <h1 className="products-title">{report.title}</h1>
              <p className="products-subtitle">
                {type === "reorder" && supplier
                  ? `${supplier.name} · ${rows.length} products`
                  : `${rows.length} records · live database results`}
              </p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <button type="button" onClick={exportCsv} disabled={!rows.length} className="products-action theme-btn-secondary">
              <ArrowDownOnSquareIcon className="w-4 h-4" /> Export CSV
            </button>
            <button type="button" onClick={() => { setPayload(null); setAsOf(today()); setSupplier(null); }} className="products-action theme-btn-tertiary">
              Reset
            </button>
            <button type="button" onClick={loadReport} disabled={loading || (type === "reorder" && !supplier)} className="products-action products-action-primary theme-btn-primary">
              <ArrowPathIcon className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
              {loading ? "Loading…" : "Load Report"}
            </button>
          </div>
        </div>
        <div className="products-filter-panel">
          <div className="report-filters">
            {report.dated && <>
              <label className="products-filter">
                <span className="products-filter-label">From Date</span>
                <input type="date" value={from} onChange={(event) => setFrom(event.target.value)} className="report-input" />
              </label>
              <label className="products-filter">
                <span className="products-filter-label">To Date</span>
                <input type="date" value={to} onChange={(event) => setTo(event.target.value)} className="report-input" />
              </label>
            </>}
            {type === "outstanding-aging" && (
              <label className="products-filter">
                <span className="products-filter-label">Age outstanding as of</span>
                <input type="date" value={asOf} max={today()} onChange={(event) => setAsOf(event.target.value)} className="report-input" />
              </label>
            )}
            {type === "outstanding-aging" && (
              <label className="products-filter">
                <span className="products-filter-label">Party Type</span>
                <select value={selectedPartyType} onChange={(event) => setPartyType(event.target.value)} className="report-input">
                  {has("customer-ledger.view") && <option value="customers">Customers</option>}
                  {has("supplier-ledger.view") && <option value="suppliers">Suppliers</option>}
                </select>
              </label>
            )}
            {type === "reorder" && (
              <>
                <label className="products-filter">
                  <span className="products-filter-label">Supplier (required)</span>
                  <button
                    type="button"
                    onClick={() => setSupplierSearchOpen(true)}
                    className="report-input flex items-center text-left cursor-pointer"
                  >
                    {supplier?.name || "Search and select a supplier…"}
                  </button>
                </label>
                <p className="report-muted self-center">Shows this supplier’s products below one pack in stock.</p>
              </>
            )}
          </div>
        </div>
      </section>

      {summaryCards.length > 0 && (
        <div className="report-kpis report-kpis-4">
          {summaryCards.map(([field, label, isMoney]) => (
            <article key={field} className="report-kpi">
              <span className="report-kpi-head">{label}</span>
              <strong className="report-kpi-value">{isMoney ? money(type === "outstanding-aging" ? selectedAgingSummary[field] : summary[field]) : number(type === "outstanding-aging" ? selectedAgingSummary[field] : summary[field]).toLocaleString()}</strong>
            </article>
          ))}
        </div>
      )}
      {type === "outstanding-aging" && payload?.summary && (
        <p className="report-muted px-2">
          Balances are aged by entry date as of {payload.as_of}. Since ledger payments are not linked to individual invoices, they are allocated FIFO to the oldest open entries.
        </p>
      )}

      <section className="products-panel products-catalog">
        <div className="products-catalog-heading">
          <div>
            <h2 className="products-section-title">{report.title} Details</h2>
            <p className="products-subtitle">{loading ? "Loading live data…" : type === "reorder" && supplier ? `Supplier: ${supplier.name} · ${rows.length} products` : `${rows.length} records`}</p>
          </div>
        </div>
        <div className="report-table-scroll" tabIndex={0} role="region" aria-label={`${report.title} data`} aria-busy={loading}>
          {type === "outstanding-aging" ? (
            <table className="report-table report-table-medium">
              <thead><tr><th>{selectedPartyType === "customers" ? "Customer" : "Supplier"}</th><th className="report-num">Open entries</th><th className="report-num">0–30 days</th><th className="report-num">31–60 days</th><th className="report-num">61–90 days</th><th className="report-num">Over 90 days</th><th className="report-num">Total outstanding</th></tr></thead>
              <tbody>{rows.length ? rows.map((row) => (
                <tr key={row.party_id}>
                  <td>
                    <details>
                      <summary className="cursor-pointer font-medium">{row.party_name}</summary>
                      <div className="mt-2 min-w-[48rem]">
                        <table className="report-table">
                          <thead><tr><th>Reference</th><th>Type</th><th>Date</th><th className="report-num">Original amount</th><th className="report-num">Paid on entry</th><th className="report-num">Other payments applied</th><th className="report-num">Outstanding</th><th className="report-num">Age</th></tr></thead>
                          <tbody>{row.entries.map((entry) => (
                            <tr key={entry.ledger_id}>
                              <td>{entry.reference}</td><td>{entry.entry_type === "manual" ? "Manual entry" : "Invoice"}</td><td>{entry.date}</td>
                              <td className="report-num">{money(entry.invoice_amount)}</td><td className="report-num">{money(entry.paid_on_invoice)}</td>
                              <td className="report-num">{money(entry.payments_applied)}</td><td className="report-num report-strong">{money(entry.outstanding)}</td>
                              <td className="report-num">{entry.age_days} days</td>
                            </tr>
                          ))}</tbody>
                        </table>
                      </div>
                    </details>
                  </td>
                  <td className="report-num">{number(row.open_entry_count).toLocaleString()}</td>
                  <td className="report-num">{money(row.days_0_30)}</td>
                  <td className="report-num">{money(row.days_31_60)}</td>
                  <td className="report-num">{money(row.days_61_90)}</td>
                  <td className="report-num">{money(row.over_90)}</td>
                  <td className="report-num report-strong">{money(row.total)}</td>
                </tr>
              )) : <tr><td colSpan={7} className="report-muted">Load the report to view data.</td></tr>}</tbody>
              {rows.length > 0 && <tfoot><tr className="report-total-row"><td>Totals</td><td className="report-num">{number(selectedAgingSummary.open_entry_count).toLocaleString()}</td><td className="report-num">{money(selectedAgingSummary.days_0_30)}</td><td className="report-num">{money(selectedAgingSummary.days_31_60)}</td><td className="report-num">{money(selectedAgingSummary.days_61_90)}</td><td className="report-num">{money(selectedAgingSummary.over_90)}</td><td className="report-num report-strong">{money(selectedAgingSummary.total)}</td></tr></tfoot>}
            </table>
          ) : type === "reorder" ? (
            <table className="report-table report-table-medium">
              <thead><tr><th>Product</th><th>Category</th><th>Brand</th><th className="report-num">On hand</th><th className="report-num">Pack size</th><th className="report-num">Suggested reorder</th><th className="report-num">Unit cost</th><th className="report-num">Estimated value</th></tr></thead>
              <tbody>{rows.length ? rows.map((row) => <tr key={row.id}><td><strong>{row.name}</strong><span className="block text-xs opacity-70">{row.product_code}</span></td><td>{row.category_name || "—"}</td><td>{row.brand_name || "—"}</td><td className="report-num">{number(row.quantity).toLocaleString()}</td><td className="report-num">{number(row.pack_size).toLocaleString()}</td><td className="report-num report-strong">{number(row.suggested_reorder_quantity).toLocaleString()}</td><td className="report-num">{money(row.unit_purchase_price)}</td><td className="report-num">{money(row.estimated_reorder_value)}</td></tr>) : <tr><td colSpan={8} className="report-muted">{supplier ? "No products for this supplier are currently below the reorder threshold." : "Select a supplier, then load the report."}</td></tr>}</tbody>
            </table>
          ) : (
            <table className="report-table report-table-medium">
              <thead><tr>{report.columns.map(([, label]) => <th key={label}>{label}</th>)}<th>Items</th></tr></thead>
              <tbody>{rows.length ? rows.map((row) => (
                <tr key={row.id}>
                  {report.columns.map(([field, , isMoney]) => <td key={field} className={isMoney ? "report-num" : ""}>{isMoney ? money(row[field]) : field === "item_count" ? row.item_count ?? row.items?.length ?? 0 : field === "quantity" ? row.quantity ?? row.items?.reduce((sum, item) => sum + number(item.quantity ?? item.return_unit_quantity), 0) ?? 0 : row[field] || "—"}</td>)}
                  <td><details><summary className="cursor-pointer">View items ({row.items?.length || 0})</summary>
                    <div className="mt-2 min-w-[32rem]">
                      <table className="report-table"><thead><tr><th>Product</th><th>Batch</th><th className="report-num">Qty</th><th className="report-num">Unit price</th><th className="report-num">Line total</th></tr></thead>
                        <tbody>{(row.items || []).map((item) => <tr key={item.id}><td>{item.product_name || "—"}<span className="block text-xs opacity-70">{item.product_code}</span></td><td>{item.batch || "—"}</td><td className="report-num">{number(item.quantity ?? item.return_unit_quantity).toLocaleString()}</td><td className="report-num">{money(item.sale_price ?? item.unit_purchase_price)}</td><td className="report-num">{money(item.sub_total)}</td></tr>)}</tbody>
                      </table>
                    </div>
                  </details></td>
                </tr>
              )) : <tr><td colSpan={report.columns.length + 1} className="report-muted">Load the report to view data.</td></tr>}</tbody>
            </table>
          )}
        </div>
      </section>
      {type === "reorder" && (
        <SupplierSearch
          isOpen={supplierSearchOpen}
          onClose={() => setSupplierSearchOpen(false)}
          onSelect={(selected) => {
            if (!selected?.id) return;
            setSupplier(selected);
            setPayload(null);
            setSupplierSearchOpen(false);
          }}
        />
      )}
    </div>
  );
}
