// resources/js/pages/Reports/CurrentStockReport.jsx
import { useMemo, useState } from "react";
import axios from "axios";
import AsyncSelect from "react-select/async";
import { createFilter } from "react-select";
import toast from "react-hot-toast";
import { usePermissions, Guard } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext.jsx";
import { openReportPdfSafely } from "../../utils/reportPdfOpener";

import {
  ArrowDownOnSquareIcon,
  ArrowPathIcon,
  CubeIcon,
  ClipboardDocumentListIcon,
  CurrencyDollarIcon,
  TagIcon,
  ArrowTrendingUpIcon,
  Squares2X2Icon,
} from "@heroicons/react/24/solid";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

/* ======================
   Helpers
   ====================== */

const n = (v) => (isFinite(Number(v)) ? Number(v) : 0);
const fmtCurrency = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const fmtNumber = (v) =>
  n(v).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });

/*
 * react-select → glass control.
 * Theme variables live on :root, so they also resolve inside the body portal menu.
 */
const selectStyles = {
  control: (base, state) => ({
    ...base,
    minHeight: 44,
    borderRadius: 12,
    backgroundColor: "var(--workspace-reading-fill, var(--color-surface))",
    borderColor: state.isFocused
      ? "var(--color-primary)"
      : "var(--workspace-border, var(--color-border))",
    boxShadow: state.isFocused
      ? "0 0 0 2px color-mix(in srgb, var(--color-primary) 22%, transparent)"
      : "none",
    transition: "border-color 150ms ease-out, box-shadow 150ms ease-out",
  }),
  valueContainer: (base) => ({ ...base, padding: "4px 10px" }),
  indicatorsContainer: (base) => ({ ...base, minHeight: 44 }),
  input: (base) => ({
    ...base,
    margin: 0,
    padding: 0,
    color: "var(--workspace-ink, var(--color-text-primary))",
  }),
  singleValue: (base) => ({ ...base, color: "var(--workspace-ink, var(--color-text-primary))" }),
  placeholder: (base) => ({ ...base, color: "var(--workspace-muted, var(--color-text-secondary))" }),
  menuPortal: (base) => ({ ...base, zIndex: 40 }),
  menu: (base) => ({
    ...base,
    borderRadius: 14,
    overflow: "hidden",
    backgroundColor: "var(--color-surface)",
    border: "1px solid var(--color-border)",
    boxShadow: "0 16px 40px -20px rgba(30,41,59,0.35)",
  }),
  option: (base, state) => ({
    ...base,
    minHeight: 44,
    cursor: "pointer",
    backgroundColor:
      state.isSelected || state.isFocused
        ? "color-mix(in srgb, var(--color-primary) 14%, var(--color-surface))"
        : "var(--color-surface)",
    color: "var(--color-text-primary)",
  }),
};

// helper to try /api/... then /...
async function tryEndpoints(paths, params) {
  let lastErr;
  for (const path of paths) {
    try {
      const res = await axios.get(path, { params, withCredentials: true });
      return res;
    } catch (e) {
      lastErr = e;
      // keep trying next path
    }
  }
  throw lastErr;
}

export default function CurrentStockReport() {
  // Filters
  const [categoryValue, setCategoryValue] = useState(null);
  const [categoryId, setCategoryId] = useState("");
  const [brandValue, setBrandValue] = useState(null);
  const [brandId, setBrandId] = useState("");
  const [supplierValue, setSupplierValue] = useState(null);
  const [supplierId, setSupplierId] = useState("");
  const [locationValue, setLocationValue] = useState(null);
  const [storageLocationId, setStorageLocationId] = useState("");

  // Data + States
  const [data, setData] = useState({ rows: [], summary: {} });
  const [loading, setLoading] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);

  // permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("report.current-stock") : {
        view:false, export:false
      }),
    [canFor]
  );

  // Active theme colors
  const { theme } = useTheme();

  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#2563eb',
        primaryHover: '#1d4ed8',
        secondary: '#0f766e',
        success: '#15803d',
        warning: '#b45309',
      };
    }
    return {
      primary: theme.primary_color || '#2563eb',
      primaryHover: theme.primary_hover || '#1d4ed8',
      secondary: theme.secondary_color || '#0f766e',
      success: theme.success_color || '#15803d',
      warning: theme.warning_color || '#b45309',
    };
  }, [theme]);

  const primaryTextColor = useMemo(
    () => getContrastText(themeColors.primary),
    [themeColors.primary]
  );

  /* ============ Async loaders ============ */
  const loadCategories = useMemo(
    () => async (input) => {
      const q = String(input || "").trim();
      if (!q) return [{ value: "", label: "All Categories" }];
      try {
        const res = await tryEndpoints(
          ["/api/categories/search", "/categories/search"],
          { q, limit: 30 }
        );
        const rows = Array.isArray(res.data?.data)
          ? res.data.data
          : Array.isArray(res.data)
          ? res.data
          : [];
        return rows.map((r) => ({
          value: r.id,
          label: r.name ?? r.label ?? `#${r.id}`,
        }));
      } catch {
        toast.error("Category search failed");
        return [{ value: "", label: "No results" }];
      }
    },
    []
  );

  const loadBrands = useMemo(
    () => async (input) => {
      const q = String(input || "").trim();
      if (!q) return [{ value: "", label: "All Brands" }];
      try {
        const res = await tryEndpoints(
          ["/api/brands/search", "/brands/search"],
          { q, limit: 30 }
        );
        const rows = Array.isArray(res.data?.data)
          ? res.data.data
          : Array.isArray(res.data)
          ? res.data
          : [];
        return rows.map((r) => ({
          value: r.id,
          label: r.name ?? r.label ?? `#${r.id}`,
        }));
      } catch {
        toast.error("Brand search failed");
        return [{ value: "", label: "No results" }];
      }
    },
    []
  );

  const loadSuppliers = useMemo(
    () => async (input) => {
      const q = String(input || "").trim();
      if (!q) return [{ value: "", label: "All Suppliers" }];
      try {
        const res = await tryEndpoints(
          ["/api/suppliers/search", "/suppliers/search"],
          { q, limit: 30 }
        );
        const rows = Array.isArray(res.data?.data)
          ? res.data.data
          : Array.isArray(res.data)
          ? res.data
          : [];
        return rows.map((r) => ({
          value: r.id,
          label: r.name ?? r.label ?? `#${r.id}`,
        }));
      } catch {
        toast.error("Supplier search failed");
        return [{ value: "", label: "No results" }];
      }
    },
    []
  );

  const loadStorageLocations = useMemo(
    () => async (input) => {
      const q = String(input || "").trim().toLowerCase();
      try {
        const res = await axios.get("/api/storage-locations", { params: { active_only: 1 } });
        const rows = Array.isArray(res.data?.data)
          ? res.data.data
          : Array.isArray(res.data)
          ? res.data
          : [];
        const opts = rows
          .filter((r) => !q || String(r.name || "").toLowerCase().includes(q))
          .map((r) => ({
            value: r.id,
            label: r.name ?? `#${r.id}`,
          }));
        return [{ value: "", label: "All Storage Locations" }, ...opts];
      } catch {
        return [{ value: "", label: "All Storage Locations" }];
      }
    },
    []
  );

  /* ============ Fetch report ============ */
  const fetchReport = async () => {
    if (!can.view) return toast.error("You don't have permission to view this report.");

    setLoading(true);
    try {
      const res = await axios.get("/api/reports/current-stock", {
        params: {
          category_id: categoryId || undefined,
          brand_id: brandId || undefined,
          supplier_id: supplierId || undefined,
          storage_location_id: storageLocationId || undefined,
        },
      });

      const responseData = res.data || {};
      // Client-side filter to ensure only products with quantity > 0 are displayed
      const allRows = Array.isArray(responseData.rows) ? responseData.rows : [];
      const rows = allRows.filter(row => (Number(row.quantity) || 0) > 0);

      // Recalculate summary based on filtered rows
      const summary = {
        total_items: rows.length,
        total_quantity: rows.reduce((sum, row) => sum + (Number(row.quantity) || 0), 0),
        total_purchase_value: rows.reduce((sum, row) => sum + (Number(row.total_purchase_value) || 0), 0),
        total_sale_value: rows.reduce((sum, row) => sum + (Number(row.total_sale_value) || 0), 0),
      };

      setData({ rows, summary });
      if (!rows.length) toast("No stock items found with quantity > 0.");
    } catch (err) {
      console.error(err);
      toast.error("Failed to fetch Current Stock report");
      setData({ rows: [], summary: {} });
    } finally {
      setLoading(false);
    }
  };

  /* ============ Export PDF ============ */
  const exportPdf = async () => {
    if (!can.export) return toast.error("You don't have permission to export PDF.");
    setPdfLoading(true);
    try {
      const res = await axios.get("/api/reports/current-stock/pdf", {
        params: {
          category_id: categoryId || undefined,
          brand_id: brandId || undefined,
          supplier_id: supplierId || undefined,
          storage_location_id: storageLocationId || undefined,
        },
        responseType: "blob",
      });
      openReportPdfSafely({
        data: res.data,
        filename: "current-stock-report.pdf",
        revokeDelayMs: 10000,
      });
    } catch (e) {
      console.error(e);
      toast.error("Failed to generate PDF");
    } finally {
      setPdfLoading(false);
    }
  };

  /* ============ Export CSV ============ */
  const exportCsv = () => {
    if (!rows.length) return;
    const headers = [
      "Product Code",
      "Product Name",
      "Category",
      "Brand",
      "Supplier",
      "Qty (Units)",
      "Pack Size",
      "Unit Purchase Price",
      "Unit Sale Price",
      "Total Purchase Value",
      "Total Sale Value",
    ];
    const csvRows = [
      headers,
      ...rows.map((r) => [
        r.product_code || "",
        r.product_name || r.name || "",
        r.category_name || "",
        r.brand_name || "",
        r.supplier_name || "",
        n(r.quantity),
        n(r.pack_size || 1),
        n(r.unit_purchase_price),
        n(r.unit_sale_price),
        n(r.total_purchase_value),
        n(r.total_sale_value),
      ]),
    ];
    const csv = csvRows
      .map((row) => row.map((c) => `"${String(c ?? "").replace(/"/g, '""')}"`).join(","))
      .join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "current-stock-report.csv";
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  /* ============ Reset filters ============ */
  const resetFilters = () => {
    setCategoryValue(null);
    setCategoryId("");
    setBrandValue(null);
    setBrandId("");
    setSupplierValue(null);
    setSupplierId("");
    setLocationValue(null);
    setStorageLocationId("");
    setData({ rows: [], summary: {} });
  };

  // Computed values
  const { rows, summary } = data;
  const totalPurchaseValue = n(summary.total_purchase_value);
  const totalSaleValue = n(summary.total_sale_value);
  const potentialProfit = totalSaleValue - totalPurchaseValue;

  return (
    <div className="report-page">
      {/* ===== Glass overview: title, actions, filters ===== */}
      <section className="products-panel">
        <div className="products-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="products-identity-icon">
              <CubeIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">Current Stock Report</h1>
              <p className="products-subtitle">
                <Squares2X2Icon className="w-3.5 h-3.5 shrink-0" />
                <span className="truncate">{rows.length} items in stock</span>
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Guard when={can.export}>
              <button
                type="button"
                onClick={exportCsv}
                disabled={rows.length === 0}
                title="Export loaded report as CSV"
                className="products-action theme-btn-secondary"
              >
                <ArrowDownOnSquareIcon className="w-4 h-4" />
                <span>CSV</span>
              </button>
              <button
                type="button"
                onClick={() => window.print()}
                disabled={rows.length === 0}
                title="Print current view"
                className="products-action theme-btn-secondary"
              >
                <span>Print</span>
              </button>
              <button
                type="button"
                onClick={exportPdf}
                disabled={pdfLoading || rows.length === 0}
                title={
                  rows.length === 0
                    ? "Load the report before exporting"
                    : pdfLoading
                    ? "Generating PDF…"
                    : "Export report as PDF"
                }
                className="products-action theme-btn-secondary"
              >
                <ArrowDownOnSquareIcon className="w-4 h-4" />
                <span>{pdfLoading ? "Generating…" : "Export PDF"}</span>
              </button>
            </Guard>

            <button
              type="button"
              onClick={resetFilters}
              title="Clear filters and results"
              className="products-action theme-btn-tertiary"
            >
              <ArrowPathIcon className="w-4 h-4" />
              <span>Reset</span>
            </button>

            <Guard when={can.view}>
              <button
                type="button"
                onClick={fetchReport}
                disabled={loading}
                title="Load the current stock report"
                className="products-action products-action-primary theme-btn-primary"
              >
                {loading ? (
                  <ArrowPathIcon className="w-4 h-4 animate-spin" />
                ) : (
                  <CubeIcon className="w-4 h-4" />
                )}
                <span>{loading ? "Loading…" : "Load Report"}</span>
              </button>
            </Guard>
          </div>
        </div>

        <div className="products-filter-panel">
          <div className="report-filters">
            <label className="products-filter">
              <span className="products-filter-label">Category</span>
              <AsyncSelect
                cacheOptions
                defaultOptions={[{ value: "", label: "All Categories" }]}
                loadOptions={loadCategories}
                isClearable
                value={categoryValue}
                onChange={(opt) => {
                  setCategoryValue(opt);
                  setCategoryId(opt?.value || "");
                }}
                styles={selectStyles}
                menuPortalTarget={document.body}
                filterOption={createFilter({
                  matchFrom: "start",
                  trim: true,
                })}
              />
            </label>

            <label className="products-filter">
              <span className="products-filter-label">Brand</span>
              <AsyncSelect
                cacheOptions
                defaultOptions={[{ value: "", label: "All Brands" }]}
                loadOptions={loadBrands}
                isClearable
                value={brandValue}
                onChange={(opt) => {
                  setBrandValue(opt);
                  setBrandId(opt?.value || "");
                }}
                styles={selectStyles}
                menuPortalTarget={document.body}
                filterOption={createFilter({
                  matchFrom: "start",
                  trim: true,
                })}
              />
            </label>

            <label className="products-filter">
              <span className="products-filter-label">Supplier</span>
              <AsyncSelect
                cacheOptions
                defaultOptions={[{ value: "", label: "All Suppliers" }]}
                loadOptions={loadSuppliers}
                isClearable
                value={supplierValue}
                onChange={(opt) => {
                  setSupplierValue(opt);
                  setSupplierId(opt?.value || "");
                }}
                styles={selectStyles}
                menuPortalTarget={document.body}
                filterOption={createFilter({
                  matchFrom: "start",
                  trim: true,
                })}
              />
            </label>

            <label className="products-filter">
              <span className="products-filter-label">Storage Location</span>
              <AsyncSelect
                cacheOptions
                defaultOptions
                loadOptions={loadStorageLocations}
                isClearable
                value={locationValue}
                onChange={(opt) => {
                  setLocationValue(opt);
                  setStorageLocationId(opt?.value || "");
                }}
                styles={selectStyles}
                menuPortalTarget={document.body}
              />
            </label>
          </div>
        </div>
      </section>

      {/* ===== Permission states ===== */}
      {permsLoading && (
        <section className="products-panel">
          <p className="report-state">Checking permissions…</p>
        </section>
      )}
      {!permsLoading && !can.view && (
        <section className="products-panel">
          <p className="report-state">You don't have permission to view this report.</p>
        </section>
      )}

      {/* ===== Results ===== */}
      {!permsLoading && can.view && (
        <>
          {rows.length === 0 && !loading && (
            <section className="products-panel">
              <p className="report-state">
                No stock items found. Apply filters and click "Load Report".
              </p>
            </section>
          )}

          {rows.length > 0 && (
            <>
              {/* ===== Summary KPIs ===== */}
              <div className="report-kpis">
                <KpiCard
                  label="Total Items"
                  value={fmtNumber(summary.total_items)}
                  note="Products in stock"
                  icon={CubeIcon}
                  accent={themeColors.primary}
                />
                <KpiCard
                  label="Total Quantity"
                  value={fmtNumber(summary.total_quantity)}
                  note="Units on hand"
                  icon={ClipboardDocumentListIcon}
                  accent={themeColors.secondary}
                />
                <KpiCard
                  label="Purchase Value"
                  value={fmtCurrency(summary.total_purchase_value)}
                  note="At cost"
                  icon={CurrencyDollarIcon}
                  accent={themeColors.secondary}
                />
                <KpiCard
                  label="Sale Value"
                  value={fmtCurrency(summary.total_sale_value)}
                  note="At retail"
                  icon={TagIcon}
                  accent={themeColors.primary}
                />
                <KpiCard
                  label="Potential Profit"
                  value={fmtCurrency(potentialProfit)}
                  note="Sale value − purchase value"
                  icon={ArrowTrendingUpIcon}
                  accent={themeColors.success}
                />
              </div>

              {/* ===== Stock items table ===== */}
              <section className="products-panel products-catalog">
                <div className="products-catalog-heading">
                  <div className="flex items-center gap-3">
                    <div className="report-section-icon">
                      <Squares2X2Icon />
                    </div>
                    <div>
                      <h2 className="products-section-title">Stock Items</h2>
                      <p className="products-subtitle">
                        {loading ? (
                          <span className="inline-flex items-center gap-1.5">
                            <ArrowPathIcon className="h-3 w-3 animate-spin" />
                            Loading…
                          </span>
                        ) : (
                          `${rows.length} items`
                        )}
                      </p>
                    </div>
                  </div>
                </div>

                <div
                  className="report-table-scroll"
                  tabIndex={0}
                  role="region"
                  aria-label="Current stock items"
                  aria-busy={loading}
                >
                  <table className="report-table">
                    <thead>
                      <tr>
                        <th scope="col">#</th>
                        <th scope="col">Product</th>
                        <th scope="col">Brand</th>
                        <th scope="col">Supplier</th>
                        <th scope="col" className="report-num">Pack Size</th>
                        <th scope="col" className="report-num">Quantity</th>
                        <th scope="col" className="report-num">Pack Purchase</th>
                        <th scope="col" className="report-num">Pack Sale</th>
                        <th scope="col" className="report-num">Total Purchase</th>
                        <th scope="col" className="report-num">Total Sale</th>
                      </tr>
                    </thead>

                    <tbody>
                      {rows.map((row, idx) => (
                        <tr key={row.id ?? idx}>
                          <td className="report-muted">{idx + 1}</td>

                          <td className="report-product">{row.name || "-"}</td>

                          <td>
                            <span className="report-badge">{row.brand_name || "—"}</span>
                          </td>

                          <td>
                            <span className="report-badge">{row.supplier_name || "—"}</span>
                          </td>

                          <td className="report-num">{fmtNumber(row.pack_size)}</td>

                          <td className="report-num">
                            <span className="report-qty">{fmtNumber(row.quantity)}</span>
                          </td>

                          <td className="report-num">{fmtCurrency(row.pack_purchase_price)}</td>

                          <td className="report-num">{fmtCurrency(row.pack_sale_price)}</td>

                          <td className="report-num">
                            <span className="report-value-purchase">
                              {fmtCurrency(row.total_purchase_value)}
                            </span>
                          </td>

                          <td className="report-num">
                            <span className="report-value-sale">
                              {fmtCurrency(row.total_sale_value)}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>

                    <tfoot>
                      <tr className="report-total-row">
                        <td colSpan={5} className="report-num">Totals</td>
                        <td className="report-num">
                          <span className="report-qty">{fmtNumber(summary.total_quantity)}</span>
                        </td>
                        <td className="report-num report-muted">—</td>
                        <td className="report-num report-muted">—</td>
                        <td className="report-num">
                          <span className="report-value-purchase">
                            {fmtCurrency(summary.total_purchase_value)}
                          </span>
                        </td>
                        <td className="report-num">
                          <span className="report-value-sale">
                            {fmtCurrency(summary.total_sale_value)}
                          </span>
                        </td>
                      </tr>
                    </tfoot>
                  </table>
                </div>
              </section>
            </>
          )}
        </>
      )}
    </div>
  );
}

/* ===== KPI Card Component ===== */
function KpiCard({ label, value, note, icon: Icon, accent }) {
  return (
    <div className="report-kpi" style={accent ? { "--report-accent": accent } : undefined}>
      <div className="report-kpi-head">
        <span className="report-kpi-icon">
          <Icon />
        </span>
        <span>{label}</span>
      </div>
      <div className="report-kpi-value">{value}</div>
      {note && <div className="report-kpi-note">{note}</div>}
    </div>
  );
}
