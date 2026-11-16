import { useEffect, useMemo, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import Select from "react-select";
import toast from "react-hot-toast";
import {
  ShieldCheckIcon,
  ExclamationTriangleIcon,
  ArrowPathIcon,
  PrinterIcon,
  DocumentArrowDownIcon,
  TableCellsIcon,
  MagnifyingGlassIcon,
  UserIcon,
  TruckIcon,
  CubeIcon,
  FireIcon,
  ClockIcon,
  CheckBadgeIcon,
  ArrowTopRightOnSquareIcon,
  BeakerIcon,
  PencilSquareIcon,
  XMarkIcon,
  BookOpenIcon,
} from "@heroicons/react/24/outline";
import ModernDatePicker from "@/components/ModernDatePicker.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";

const getSelectStyles = (isDarkMode = false) => ({
  control: (base) => ({
    ...base,
    minHeight: "38px",
    height: "38px",
    fontSize: "12px",
    borderRadius: "0.75rem",
    backgroundColor: isDarkMode ? "rgba(30, 41, 59, 0.9)" : "#ffffff",
    borderColor: isDarkMode ? "rgba(71, 85, 105, 0.8)" : "#cbd5e1",
    boxShadow: "none",
  }),
  valueContainer: (base) => ({
    ...base,
    height: "38px",
    padding: "0 10px",
  }),
  indicatorsContainer: (base) => ({
    ...base,
    height: "38px",
  }),
  singleValue: (base) => ({
    ...base,
    color: isDarkMode ? "#f1f5f9" : "#0f172a",
    fontWeight: 600,
  }),
  menu: (base) => ({
    ...base,
    fontSize: "12px",
    borderRadius: "0.75rem",
    overflow: "hidden",
    backgroundColor: isDarkMode ? "#0f172a" : "#ffffff",
    border: isDarkMode ? "1px solid #334155" : "1px solid #e2e8f0",
    zIndex: 9999,
  }),
  option: (base, state) => ({
    ...base,
    backgroundColor: isDarkMode
      ? state.isSelected
        ? "#e11d48"
        : state.isFocused
        ? "#1e293b"
        : "#0f172a"
      : state.isSelected
      ? "#ffe4e6"
      : state.isFocused
      ? "#f8fafc"
      : "#ffffff",
    color: isDarkMode
      ? "#f1f5f9"
      : state.isSelected
      ? "#881337"
      : "#0f172a",
    cursor: "pointer",
  }),
});

const todayISO = () => new Date().toISOString().slice(0, 10);
const firstDayOfMonthISO = () => {
  const d = new Date();
  d.setDate(1);
  return d.toISOString().slice(0, 10);
};

const fmtInt = (v) =>
  Number(v || 0).toLocaleString(undefined, { maximumFractionDigits: 0 });

export default function NarcoticsReport() {
  const navigate = useNavigate();
  const { isDark } = useTheme();
  const selectStyles = useMemo(() => getSelectStyles(isDark), [isDark]);

  // Filters
  const [from, setFrom] = useState(firstDayOfMonthISO());
  const [to, setTo] = useState(todayISO());
  const [selectedProduct, setSelectedProduct] = useState(null);
  const [complianceFilter, setComplianceFilter] = useState("all"); // all | compliant | missing_rx
  const [searchQuery, setSearchQuery] = useState("");
  const [activeTab, setActiveTab] = useState("chronological"); // chronological | ledger | dispensing | procurement | destruction | batches

  // Sorting state per tab (sortable-table pattern)
  const [sortField, setSortField] = useState("serial_no");
  const [sortDirection, setSortDirection] = useState("asc");
  const [sortAnnouncement, setSortAnnouncement] = useState("");

  // Data state
  const [loading, setLoading] = useState(false);
  const [pdfLoading, setPdfLoading] = useState(false);
  const [data, setData] = useState({
    period: { from: firstDayOfMonthISO(), to: todayISO() },
    store_license: {},
    summary: {},
    chronological_register: [],
    product_registers: [],
    stock_ledger: [],
    dispensing_register: [],
    procurement_register: [],
    destruction_and_returns: [],
    batch_inventory: [],
    narcotic_products: [],
  });

  // Inline Statutory Rx / PMDC / CNIC Editor Modal State
  const [editingRxRow, setEditingRxRow] = useState(null);
  const [rxForm, setRxForm] = useState({
    doctor_name: "",
    doctor_pmdc: "",
    doctor_hospital: "",
    patient_name: "",
    patient_cnic: "",
    patient_address: "",
    prescription_no: "",
    pharmacist_name: "",
  });
  const [savingRx, setSavingRx] = useState(false);

  const fetchReport = useCallback(
    async (overrideFrom, overrideTo, overrideProduct) => {
      const qFrom = overrideFrom ?? from;
      const qTo = overrideTo ?? to;
      const qProd =
        overrideProduct !== undefined ? overrideProduct : selectedProduct?.value;

      setLoading(true);
      try {
        const params = { from: qFrom, to: qTo };
        if (qProd) params.product_id = qProd;
        const { data: res } = await axios.get("/api/reports/narcotics", {
          params,
        });
        setData({
          period: res?.period || { from: qFrom, to: qTo },
          store_license: res?.store_license || {},
          summary: res?.summary || {},
          chronological_register: Array.isArray(res?.chronological_register)
            ? res.chronological_register
            : [],
          product_registers: Array.isArray(res?.product_registers)
            ? res.product_registers
            : [],
          stock_ledger: Array.isArray(res?.stock_ledger) ? res.stock_ledger : [],
          dispensing_register: Array.isArray(res?.dispensing_register)
            ? res.dispensing_register
            : [],
          procurement_register: Array.isArray(res?.procurement_register)
            ? res.procurement_register
            : [],
          destruction_and_returns: Array.isArray(res?.destruction_and_returns)
            ? res.destruction_and_returns
            : [],
          batch_inventory: Array.isArray(res?.batch_inventory)
            ? res.batch_inventory
            : [],
          narcotic_products: Array.isArray(res?.narcotic_products)
            ? res.narcotic_products
            : [],
        });
      } catch (err) {
        toast.error(
          err?.response?.data?.message ||
            "Failed to load Pakistani Statutory Narcotics Register."
        );
      } finally {
        setLoading(false);
      }
    },
    [from, to, selectedProduct]
  );

  useEffect(() => {
    fetchReport();
  }, []);

  const applyQuickRange = (preset) => {
    const now = new Date();
    let start = new Date();
    let end = new Date();

    if (preset === "today") {
      // today
    } else if (preset === "this_month") {
      start = new Date(now.getFullYear(), now.getMonth(), 1);
    } else if (preset === "last_month") {
      start = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      end = new Date(now.getFullYear(), now.getMonth(), 0);
    } else if (preset === "this_quarter") {
      const qMonth = Math.floor(now.getMonth() / 3) * 3;
      start = new Date(now.getFullYear(), qMonth, 1);
    } else if (preset === "this_year") {
      start = new Date(now.getFullYear(), 0, 1);
    }

    const fStr = start.toISOString().slice(0, 10);
    const tStr = end.toISOString().slice(0, 10);
    setFrom(fStr);
    setTo(tStr);
    fetchReport(fStr, tStr, selectedProduct?.value);
  };

  const openRxModal = (row) => {
    setEditingRxRow(row);
    setRxForm({
      doctor_name: row.doctor_name || "",
      doctor_pmdc: row.doctor_pmdc || "",
      doctor_hospital: row.doctor_hospital || "",
      patient_name: row.raw_patient_name ?? row.patient_name ?? row.party_name ?? "",
      patient_cnic: row.raw_patient_cnic ?? row.patient_cnic ?? row.patient_contact ?? row.party_cnic_or_phone ?? "",
      patient_address: row.raw_patient_address ?? row.patient_address ?? row.party_address ?? "",
      prescription_no: row.raw_prescription_no ?? "",
      pharmacist_name: row.raw_pharmacist_name ?? row.dispensed_by ?? row.pharmacist_name ?? "",
    });
  };

  const handleSaveRxMeta = async (e) => {
    e.preventDefault();
    if (!editingRxRow?.sale_invoice_id) return;
    setSavingRx(true);
    try {
      await axios.put(
        `/api/sale-invoices/${editingRxRow.sale_invoice_id}/meta`,
        rxForm
      );
      toast.success(
        `Statutory Rx, PM&DC & Patient CNIC saved for Invoice ${editingRxRow.posted_number || editingRxRow.voucher_number}.`
      );
      setEditingRxRow(null);
      fetchReport();
    } catch (err) {
      toast.error(
        err?.response?.data?.message ||
          "Failed to update statutory prescription particulars."
      );
    } finally {
      setSavingRx(false);
    }
  };

  const productOptions = useMemo(() => {
    return [
      { value: "", label: "All Narcotic & Controlled Drugs (Schedule B / D / G)" },
      ...(data.narcotic_products || []).map((p) => ({
        value: p.id,
        label: `${p.name}${p.formulation ? ` (${p.formulation})` : ""}${
          p.brand_name ? ` — ${p.brand_name}` : ""
        } [1×${p.pack_size || 1}]`,
      })),
    ];
  }, [data.narcotic_products]);

  const handleSort = (field, label) => {
    let nextDir = "asc";
    if (sortField === field) {
      nextDir = sortDirection === "asc" ? "desc" : "asc";
    }
    setSortField(field);
    setSortDirection(nextDir);
    setSortAnnouncement(
      `Sorted by ${label}, ${nextDir === "asc" ? "ascending" : "descending"}`
    );
  };

  const getAriaSort = (field) => {
    if (sortField !== field) return "none";
    return sortDirection === "asc" ? "ascending" : "descending";
  };

  const renderSortIcon = (field) => {
    if (sortField !== field) {
      return (
        <span aria-hidden="true" className="ml-1 text-slate-400 opacity-60">
          ↕
        </span>
      );
    }
    return (
      <span
        aria-hidden="true"
        className="ml-1 text-rose-600 dark:text-rose-400 font-bold"
      >
        {sortDirection === "asc" ? "↑" : "↓"}
      </span>
    );
  };

  const sortRows = useCallback(
    (rows) => {
      const copy = [...rows];
      copy.sort((a, b) => {
        const va = a?.[sortField];
        const vb = b?.[sortField];
        if (typeof va === "number" || typeof vb === "number") {
          const diff = Number(va || 0) - Number(vb || 0);
          return sortDirection === "asc" ? diff : -diff;
        }
        const sa = String(va ?? "").toLowerCase();
        const sb = String(vb ?? "").toLowerCase();
        const cmp = sa.localeCompare(sb, undefined, { numeric: true });
        return sortDirection === "asc" ? cmp : -cmp;
      });
      return copy;
    },
    [sortField, sortDirection]
  );

  const qNorm = searchQuery.trim().toLowerCase();

  // Filtered rows per tab
  const filteredChronological = useMemo(() => {
    const base = (data.chronological_register || []).filter((r) => {
      if (complianceFilter === "compliant" && !r.is_rx_compliant) return false;
      if (complianceFilter === "missing_rx" && r.is_rx_compliant) return false;
      if (!qNorm) return true;
      return (
        String(r.product_name || "").toLowerCase().includes(qNorm) ||
        String(r.formulation || "").toLowerCase().includes(qNorm) ||
        String(r.voucher_number || "").toLowerCase().includes(qNorm) ||
        String(r.reference_no || "").toLowerCase().includes(qNorm) ||
        String(r.party_name || "").toLowerCase().includes(qNorm) ||
        String(r.party_cnic_or_phone || "").toLowerCase().includes(qNorm) ||
        String(r.party_address || "").toLowerCase().includes(qNorm) ||
        String(r.doctor_name || "").toLowerCase().includes(qNorm) ||
        String(r.doctor_pmdc || "").toLowerCase().includes(qNorm) ||
        String(r.batch_number || "").toLowerCase().includes(qNorm) ||
        String(r.pharmacist_name || "").toLowerCase().includes(qNorm)
      );
    });
    if (sortField === "serial_no" && sortDirection === "asc") {
      return base;
    }
    return sortRows(base);
  }, [
    data.chronological_register,
    complianceFilter,
    qNorm,
    sortField,
    sortDirection,
    sortRows,
  ]);

  const filteredProductRegisters = useMemo(() => {
    return (data.product_registers || [])
      .map((prodReg) => {
        const prodMatch =
          !qNorm ||
          String(prodReg.product_name || "").toLowerCase().includes(qNorm) ||
          String(prodReg.product_code || "").toLowerCase().includes(qNorm) ||
          String(prodReg.formulation || "").toLowerCase().includes(qNorm) ||
          String(prodReg.brand_name || "").toLowerCase().includes(qNorm) ||
          String(prodReg.batches_summary || "").toLowerCase().includes(qNorm);

        const entries = (prodReg.entries || []).filter((r) => {
          if (complianceFilter === "compliant" && !r.is_rx_compliant)
            return false;
          if (complianceFilter === "missing_rx" && r.is_rx_compliant)
            return false;
          if (!qNorm || prodMatch) return true;
          return (
            String(r.voucher_number || "").toLowerCase().includes(qNorm) ||
            String(r.reference_no || "").toLowerCase().includes(qNorm) ||
            String(r.party_name || "").toLowerCase().includes(qNorm) ||
            String(r.party_cnic_or_phone || "").toLowerCase().includes(qNorm) ||
            String(r.party_address || "").toLowerCase().includes(qNorm) ||
            String(r.doctor_name || "").toLowerCase().includes(qNorm) ||
            String(r.doctor_pmdc || "").toLowerCase().includes(qNorm) ||
            String(r.batch_number || "").toLowerCase().includes(qNorm) ||
            String(r.pharmacist_name || "").toLowerCase().includes(qNorm)
          );
        });

        return {
          ...prodReg,
          entries,
        };
      })
      .filter((prodReg) => prodReg.entries.length > 0);
  }, [data.product_registers, complianceFilter, qNorm]);

  const filteredStockLedger = useMemo(() => {
    const base = (data.stock_ledger || []).filter((r) => {
      if (!qNorm) return true;
      return (
        String(r.product_name || "").toLowerCase().includes(qNorm) ||
        String(r.product_code || "").toLowerCase().includes(qNorm) ||
        String(r.formulation || "").toLowerCase().includes(qNorm) ||
        String(r.brand_name || "").toLowerCase().includes(qNorm) ||
        String(r.supplier_name || "").toLowerCase().includes(qNorm) ||
        String(r.batches_summary || "").toLowerCase().includes(qNorm) ||
        String(r.rack || "").toLowerCase().includes(qNorm)
      );
    });
    return sortRows(base);
  }, [data.stock_ledger, qNorm, sortRows]);

  const filteredDispensing = useMemo(() => {
    const base = (data.dispensing_register || []).filter((r) => {
      if (complianceFilter === "compliant" && !r.is_rx_compliant) return false;
      if (complianceFilter === "missing_rx" && r.is_rx_compliant) return false;
      if (!qNorm) return true;
      return (
        String(r.product_name || "").toLowerCase().includes(qNorm) ||
        String(r.posted_number || "").toLowerCase().includes(qNorm) ||
        String(r.prescription_no || "").toLowerCase().includes(qNorm) ||
        String(r.doctor_name || "").toLowerCase().includes(qNorm) ||
        String(r.doctor_pmdc || "").toLowerCase().includes(qNorm) ||
        String(r.doctor_hospital || "").toLowerCase().includes(qNorm) ||
        String(r.patient_name || "").toLowerCase().includes(qNorm) ||
        String(r.patient_contact || "").toLowerCase().includes(qNorm) ||
        String(r.patient_address || "").toLowerCase().includes(qNorm) ||
        String(r.customer_name || "").toLowerCase().includes(qNorm) ||
        String(r.batch_number || "").toLowerCase().includes(qNorm) ||
        String(r.dispensed_by || "").toLowerCase().includes(qNorm)
      );
    });
    return sortRows(base);
  }, [data.dispensing_register, complianceFilter, qNorm, sortRows]);

  const filteredProcurement = useMemo(() => {
    const base = (data.procurement_register || []).filter((r) => {
      if (!qNorm) return true;
      return (
        String(r.product_name || "").toLowerCase().includes(qNorm) ||
        String(r.posted_number || "").toLowerCase().includes(qNorm) ||
        String(r.invoice_number || "").toLowerCase().includes(qNorm) ||
        String(r.supplier_name || "").toLowerCase().includes(qNorm) ||
        String(r.supplier_address || "").toLowerCase().includes(qNorm) ||
        String(r.batch_number || "").toLowerCase().includes(qNorm)
      );
    });
    return sortRows(base);
  }, [data.procurement_register, qNorm, sortRows]);

  const filteredDestruction = useMemo(() => {
    const base = (data.destruction_and_returns || []).filter((r) => {
      if (!qNorm) return true;
      return (
        String(r.product_name || "").toLowerCase().includes(qNorm) ||
        String(r.document_number || "").toLowerCase().includes(qNorm) ||
        String(r.batch_number || "").toLowerCase().includes(qNorm) ||
        String(r.witness_name || "").toLowerCase().includes(qNorm) ||
        String(r.party_or_authority || "").toLowerCase().includes(qNorm)
      );
    });
    return sortRows(base);
  }, [data.destruction_and_returns, qNorm, sortRows]);

  const filteredBatches = useMemo(() => {
    const base = (data.batch_inventory || []).filter((r) => {
      if (!qNorm) return true;
      return (
        String(r.product_name || "").toLowerCase().includes(qNorm) ||
        String(r.product_code || "").toLowerCase().includes(qNorm) ||
        String(r.batch_number || "").toLowerCase().includes(qNorm) ||
        String(r.rack || "").toLowerCase().includes(qNorm)
      );
    });
    return sortRows(base);
  }, [data.batch_inventory, qNorm, sortRows]);

  // Ledger Physical Totals (No monetary values)
  const ledgerTotals = useMemo(() => {
    return filteredStockLedger.reduce(
      (acc, r) => {
        acc.opening += Number(r.opening_qty || 0);
        acc.purchased += Number(r.purchased_qty || 0);
        acc.saleReturn += Number(r.sale_return_qty || 0);
        acc.dispensed += Number(r.dispensed_qty || 0);
        acc.purchaseReturn += Number(r.purchase_return_qty || 0);
        acc.disposed += Number(r.disposed_qty || 0);
        acc.adjusted += Number(r.adjusted_qty || 0);
        acc.closing += Number(r.closing_qty || 0);
        return acc;
      },
      {
        opening: 0,
        purchased: 0,
        saleReturn: 0,
        dispensed: 0,
        purchaseReturn: 0,
        disposed: 0,
        adjusted: 0,
        closing: 0,
      }
    );
  }, [filteredStockLedger]);

  // Export Official PDF
  const handleExportPdf = async () => {
    try {
      setPdfLoading(true);
      const params = { from, to };
      if (selectedProduct?.value) params.product_id = selectedProduct.value;
      const response = await axios.get("/api/reports/narcotics/pdf", {
        params,
        responseType: "blob",
      });
      const blob = new Blob([response.data], { type: "application/pdf" });
      const url = window.URL.createObjectURL(blob);
      window.open(url, "_blank");
      setTimeout(() => window.URL.revokeObjectURL(url), 60000);
    } catch (err) {
      toast.error("Failed to generate Pakistani Statutory Narcotics PDF.");
    } finally {
      setPdfLoading(false);
    }
  };

  // Export Active Tab CSV (Strictly Physical Quantities & Regulatory Particulars — Zero Monetary Values)
  const handleExportCsv = () => {
    let headers = [];
    let rows = [];
    let filename = `pakistani-narcotics-register-${activeTab}-${from}-to-${to}.csv`;

    if (activeTab === "chronological") {
      headers = [
        "Col.1 S.No",
        "Col.2 Date",
        "Col.3 Entry Type",
        "Col.3 Bill / GRN / Rx Ref #",
        "Col.4 Controlled Drug",
        "Col.4 Generic Strength",
        "Col.4 Pack Size",
        "Col.5 Source (Supplier) OR Patient Name",
        "Col.5 Patient CNIC / Contact",
        "Col.5 Residential / Supplier Address",
        "Col.6 Prescribing Doctor (RMP) / Authority",
        "Col.6 PM&DC Reg # / Designation",
        "Col.6 Hospital / Clinic",
        "Col.7 Batch #",
        "Col.7 Expiry Date",
        "Col.8 Qty Received (IN Units)",
        "Col.9 Qty Dispensed (OUT Units)",
        "Col.10 Running Balance in Hand (Units)",
        "Col.10 Pack Breakdown",
        "Col.11 Supervising Pharmacist",
        "Col.11 Inspector / Compliance Remarks",
      ];
      rows = filteredChronological.map((r) => [
        r.serial_no,
        r.date,
        r.event_label,
        r.voucher_number + (r.reference_no ? ` (${r.reference_no})` : ""),
        r.product_name,
        r.formulation || "",
        `1x${r.pack_size || 1}`,
        r.party_name || "",
        r.party_cnic_or_phone || "",
        r.party_address || "",
        r.doctor_name || "",
        r.doctor_pmdc || "",
        r.doctor_hospital || "",
        r.batch_number || "",
        r.expiry || "",
        r.qty_in || 0,
        r.qty_out || 0,
        r.running_balance || 0,
        r.running_pack_breakdown || "",
        r.pharmacist_name || "",
        r.remarks || "",
      ]);
    } else if (activeTab === "ledger") {
      headers = [
        "Code",
        "Controlled Drug",
        "Generic Strength / Formulation",
        "Manufacturer",
        "Pack Size",
        "Vault / Rack Location",
        "Batches & Expiry Dates",
        "Opening B/F (Units)",
        "Purchased Inward (Units)",
        "Patient Return Inward (Units)",
        "Dispensed Outward (Units)",
        "Supplier Return Outward (Units)",
        "Destroyed / Quarantined (Units)",
        "Physical Audit Adj (Units)",
        "Closing Balance in Hand (Units)",
        "Closing Pack Breakdown",
      ];
      rows = filteredStockLedger.map((r) => [
        r.product_code,
        r.product_name,
        r.formulation,
        r.brand_name,
        `1x${r.pack_size}`,
        r.rack,
        r.batches_summary || "—",
        r.opening_qty,
        r.purchased_qty,
        r.sale_return_qty,
        r.dispensed_qty,
        r.purchase_return_qty,
        r.disposed_qty,
        r.adjusted_qty,
        r.closing_qty,
        r.closing_pack_breakdown,
      ]);
    } else if (activeTab === "dispensing") {
      headers = [
        "Date",
        "Sale Invoice #",
        "Prescription Serial / Ref #",
        "Controlled Drug",
        "Generic Strength",
        "Batch #",
        "Expiry Date",
        "Prescribing Doctor (RMP)",
        "Doctor PM&DC Reg #",
        "Hospital / Clinic",
        "Patient Full Name",
        "Patient CNIC / Contact",
        "Patient Residential Address",
        "Qty Dispensed (Units)",
        "Bonus Units",
        "Total Units Out",
        "Pack Breakdown",
        "Supervising Pharmacist",
        "Rx Audit Status",
      ];
      rows = filteredDispensing.map((r) => [
        r.date,
        r.posted_number,
        r.prescription_no || "",
        r.product_name,
        r.formulation || "",
        r.batch_number,
        r.expiry || "",
        r.doctor_name || "MISSING",
        r.doctor_pmdc || "",
        r.doctor_hospital || "",
        r.patient_name || r.customer_name || "MISSING",
        r.patient_contact || "",
        r.patient_address || "",
        r.quantity,
        r.bonus,
        r.total_units_out,
        r.pack_breakdown,
        r.dispensed_by,
        r.is_rx_compliant ? "COMPLIANT" : "MISSING RX",
      ]);
    } else if (activeTab === "procurement") {
      headers = [
        "Date",
        "GRN #",
        "Supplier Bill / Gate Pass #",
        "Authorized Distributor / Wholesaler",
        "Distributor License / NTN",
        "Distributor Phone",
        "Distributor Address",
        "Controlled Drug",
        "Generic Strength",
        "Batch #",
        "Expiry Date",
        "Packs Received",
        "Bonus Units",
        "Total Physical Units Received (IN)",
        "Pack Breakdown",
      ];
      rows = filteredProcurement.map((r) => [
        r.date,
        r.posted_number,
        r.invoice_number,
        r.supplier_name,
        r.supplier_license_or_tax || "",
        r.supplier_phone || "",
        r.supplier_address || "",
        r.product_name,
        r.formulation || "",
        r.batch_number,
        r.expiry || "",
        r.pack_quantity,
        r.unit_bonus,
        r.total_units_in,
        r.pack_breakdown,
      ]);
    } else if (activeTab === "destruction") {
      headers = [
        "Date",
        "Voucher #",
        "Event Type",
        "Direction",
        "Controlled Drug",
        "Generic Strength",
        "Batch #",
        "Expiry Date",
        "Physical Units",
        "Pack Breakdown",
        "Cause / Method / Counterparty",
        "Witness Name (Drug Inspector / Authority)",
        "Witness Designation",
        "Supervising Pharmacist",
      ];
      rows = filteredDestruction.map((r) => [
        r.date,
        r.document_number,
        r.event_type,
        r.direction,
        r.product_name,
        r.formulation || "",
        r.batch_number,
        r.expiry || "",
        r.quantity,
        r.pack_breakdown,
        r.party_or_authority,
        r.witness_name || "",
        r.witness_designation || "",
        r.handled_by,
      ]);
    } else {
      headers = [
        "Controlled Drug",
        "Code",
        "Generic Strength",
        "Manufacturer",
        "Batch #",
        "Expiry Date",
        "Days Remaining",
        "FEFO Status",
        "Vault / Rack Location",
        "Physical Units in Hand",
        "Pack Breakdown",
      ];
      rows = filteredBatches.map((r) => [
        r.product_name,
        r.product_code,
        r.formulation,
        r.brand_name,
        r.batch_number,
        r.expiry_date || "",
        r.days_remaining ?? "",
        r.expiry_status,
        r.rack,
        r.quantity,
        r.pack_breakdown,
      ]);
    }

    const csvContent = [headers, ...rows]
      .map((row) =>
        row
          .map((cell) => `"${String(cell ?? "").replace(/"/g, '""')}"`)
          .join(",")
      )
      .join("\n");

    const blob = new Blob(["\uFEFF" + csvContent], {
      type: "text/csv;charset=utf-8;",
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("Statutory CSV register exported.");
  };

  const summary = data.summary || {};
  const storeLicense = data.store_license || {};
  const complianceRate = Number(summary.compliance_rate ?? 100);
  const missingRxCount = Number(summary.missing_rx_sales_count ?? 0);

  return (
    <div className="p-4 md:p-6 space-y-5 max-w-[1640px] mx-auto print:p-0">
      {/* Screen-reader live region for table sorting announcements */}
      <div role="status" aria-live="polite" className="ledger-sr-status">
        {sortAnnouncement}
      </div>

      {/* Top Row: Heading & Pakistani Regulatory License Banner on Left, Action Toolbar Opposite on Right */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 md:p-5 shadow-sm">
        <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4">
          <div className="space-y-1.5">
            <div className="flex flex-wrap items-center gap-2">
              <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[11px] font-bold uppercase tracking-wider bg-rose-50 text-rose-700 border border-rose-200 dark:bg-rose-950/60 dark:text-rose-300 dark:border-rose-800/70">
                <ShieldCheckIcon className="w-3.5 h-3.5" />
                Drugs Act 1976 • DRAP • Schedule B, D &amp; G • CNSA 1997
              </span>
              {storeLicense.license_number && (
                <span className="inline-flex items-center px-2.5 py-1 rounded-full text-[11px] font-bold bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-200">
                  Form 9 / Drug Sale License #: {storeLicense.license_number}
                </span>
              )}
              <span className="inline-flex items-center px-2.5 py-1 rounded-full text-[11px] font-semibold bg-emerald-50 text-emerald-800 border border-emerald-200 dark:bg-emerald-950/60 dark:text-emerald-300 dark:border-emerald-800">
                Mandatory 3-Year Retention (Rule 13)
              </span>
            </div>
            <h1 className="text-xl md:text-2xl font-extrabold text-slate-900 dark:text-white tracking-tight">
              Pakistani Statutory Narcotics &amp; Controlled Drugs Register
            </h1>
            <p className="text-xs md:text-sm text-slate-500 dark:text-slate-400">
              Official Drug Inspector physical stock &amp; prescription traceability register: Chronological Running Balance (In / Out / Balance in Hand), Authorized Distributor Source, Patient CNIC &amp; Address, and Prescribing RMP (PM&amp;DC Reg #).
            </p>
          </div>

          {/* Opposite Action Toolbar */}
          <div className="flex flex-wrap items-center gap-2 print:hidden">
            <button
              type="button"
              onClick={() => fetchReport()}
              disabled={loading}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl text-xs font-semibold bg-slate-100 hover:bg-slate-200 text-slate-700 dark:bg-slate-800 dark:hover:bg-slate-700 dark:text-slate-200 transition"
              title="Refresh Register"
            >
              <ArrowPathIcon
                className={`w-4 h-4 ${loading ? "animate-spin" : ""}`}
              />
              Refresh
            </button>

            <button
              type="button"
              onClick={() => navigate("/stock-disposals")}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl text-xs font-semibold bg-amber-50 hover:bg-amber-100 text-amber-800 border border-amber-200 dark:bg-amber-950/50 dark:hover:bg-amber-900/60 dark:text-amber-300 dark:border-amber-800 transition"
              title="Open Quarantine & Witnessed Narcotic Destruction Log"
            >
              <FireIcon className="w-4 h-4" />
              Quarantine &amp; Destruction
            </button>

            <button
              type="button"
              onClick={handleExportCsv}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl text-xs font-semibold bg-emerald-50 hover:bg-emerald-100 text-emerald-800 border border-emerald-200 dark:bg-emerald-950/50 dark:hover:bg-emerald-900/60 dark:text-emerald-300 dark:border-emerald-800 transition"
            >
              <TableCellsIcon className="w-4 h-4" />
              Export CSV
            </button>

            <button
              type="button"
              onClick={handleExportPdf}
              disabled={pdfLoading}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold bg-rose-600 hover:bg-rose-700 text-white shadow-sm transition disabled:opacity-60"
            >
              <DocumentArrowDownIcon className="w-4 h-4" />
              {pdfLoading ? "Generating PDF…" : "Drug Inspector PDF"}
            </button>

            <button
              type="button"
              onClick={() => window.print()}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl text-xs font-semibold bg-slate-900 hover:bg-slate-800 text-white dark:bg-slate-700 dark:hover:bg-slate-600 transition"
            >
              <PrinterIcon className="w-4 h-4" />
              Print
            </button>
          </div>
        </div>

        {/* Second Row: Filters & Quick Date Presets */}
        <div className="mt-4 pt-4 border-t border-slate-200/80 dark:border-slate-800 grid grid-cols-1 md:grid-cols-12 gap-3 items-end print:hidden">
          <div className="md:col-span-2">
            <label className="block text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
              From Date
            </label>
            <ModernDatePicker
              value={from}
              onChange={(val) => {
                setFrom(val);
                fetchReport(val, to, selectedProduct?.value);
              }}
            />
          </div>

          <div className="md:col-span-2">
            <label className="block text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
              To Date
            </label>
            <ModernDatePicker
              value={to}
              onChange={(val) => {
                setTo(val);
                fetchReport(from, val, selectedProduct?.value);
              }}
            />
          </div>

          <div className="md:col-span-3">
            <label className="block text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
              Controlled Substance (Drug-Wise Page)
            </label>
            <Select
              options={productOptions}
              value={selectedProduct || productOptions[0]}
              onChange={(opt) => {
                const next = opt?.value ? opt : null;
                setSelectedProduct(next);
                fetchReport(from, to, next?.value || null);
              }}
              styles={selectStyles}
              placeholder="All Narcotic & Controlled Drugs..."
              isClearable
            />
          </div>

          <div className="md:col-span-2">
            <label className="block text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
              RMP / Rx Audit Filter
            </label>
            <select
              value={complianceFilter}
              onChange={(e) => setComplianceFilter(e.target.value)}
              className="w-full h-[38px] rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 px-3 text-xs font-semibold text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-rose-500"
            >
              <option value="all">All Register Entries</option>
              <option value="compliant">Compliant Only (Doctor + Patient)</option>
              <option value="missing_rx">Missing Doctor / Patient Rx</option>
            </select>
          </div>

          <div className="md:col-span-3">
            <label className="block text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">
              Search CNIC, PM&amp;DC, Batch, Patient, Doctor
            </label>
            <div className="relative">
              <MagnifyingGlassIcon className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search CNIC, PMDC #, doctor, patient, batch..."
                className="w-full h-[38px] pl-9 pr-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-rose-500"
              />
            </div>
          </div>
        </div>

        {/* Quick Period Pills */}
        <div className="mt-3 flex flex-wrap items-center justify-between gap-2 print:hidden">
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="text-[11px] font-semibold text-slate-400 mr-1">
              Inspection Period:
            </span>
            {[
              { id: "today", label: "Today" },
              { id: "this_month", label: "This Month" },
              { id: "last_month", label: "Last Month" },
              { id: "this_quarter", label: "This Quarter" },
              { id: "this_year", label: "This Year" },
            ].map((p) => (
              <button
                key={p.id}
                type="button"
                onClick={() => applyQuickRange(p.id)}
                className="px-2.5 py-1 rounded-lg text-[11px] font-semibold bg-slate-100 hover:bg-rose-50 hover:text-rose-700 text-slate-600 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-rose-950/60 dark:hover:text-rose-300 transition"
              >
                {p.label}
              </button>
            ))}
          </div>

          {missingRxCount > 0 && (
            <button
              type="button"
              onClick={() => {
                setActiveTab("dispensing");
                setComplianceFilter("missing_rx");
              }}
              className="inline-flex items-center gap-1.5 px-3 py-1 rounded-lg text-xs font-bold bg-amber-50 text-amber-800 border border-amber-300 dark:bg-amber-950/70 dark:text-amber-300 dark:border-amber-800 hover:bg-amber-100 transition"
            >
              <ExclamationTriangleIcon className="w-4 h-4 text-amber-600" />
              {missingRxCount} Dispensing{" "}
              {missingRxCount === 1 ? "entry is" : "entries are"} missing Doctor
              or Patient particulars — Click to complete Rx details
            </button>
          )}
        </div>
      </div>

      {/* 4 Physical Quantity & Regulatory Compliance KPI Cards (Zero Monetary Values) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
        {/* Card 1: Double-Lock Vault Balance */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Vault Balance in Hand
            </span>
            <span className="p-2 rounded-xl bg-rose-50 text-rose-600 dark:bg-rose-950/60 dark:text-rose-400">
              <BeakerIcon className="w-5 h-5" />
            </span>
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-extrabold text-slate-900 dark:text-white">
              {fmtInt(summary.total_units_on_hand)}
            </span>
            <span className="text-xs font-semibold text-slate-500">
              Physical Units ({fmtInt(summary.total_narcotic_products)} Drugs)
            </span>
          </div>
          <div className="mt-2 pt-2 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
            <span>Opening B/F: {fmtInt(summary.opening_units_total)} U</span>
            <span className="font-semibold text-slate-700 dark:text-slate-300">
              {fmtInt(summary.active_batches_count)} Active Batches
            </span>
          </div>
        </div>

        {/* Card 2: Inward Authorized Receipts */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Quantity Received (IN)
            </span>
            <span className="p-2 rounded-xl bg-blue-50 text-blue-600 dark:bg-blue-950/60 dark:text-blue-400">
              <TruckIcon className="w-5 h-5" />
            </span>
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-extrabold text-blue-700 dark:text-blue-400">
              +{fmtInt(summary.units_purchased)}
            </span>
            <span className="text-xs font-semibold text-slate-500">
              Units from {fmtInt(summary.purchase_invoices_count)} Supplier GRNs
            </span>
          </div>
          <div className="mt-2 pt-2 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
            <span>Patient Returns (IN):</span>
            <span className="font-bold text-teal-700 dark:text-teal-400">
              +{fmtInt(summary.units_returned_by_customers)} Units
            </span>
          </div>
        </div>

        {/* Card 3: Dispensed & RMP Prescription Audit */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Quantity Dispensed (OUT)
            </span>
            <span
              className={`p-2 rounded-xl ${
                complianceRate >= 95
                  ? "bg-emerald-50 text-emerald-600 dark:bg-emerald-950/60 dark:text-emerald-400"
                  : "bg-amber-50 text-amber-600 dark:bg-amber-950/60 dark:text-amber-400"
              }`}
            >
              <CheckBadgeIcon className="w-5 h-5" />
            </span>
          </div>
          <div className="mt-2 flex items-baseline justify-between">
            <div>
              <span className="text-2xl font-extrabold text-rose-700 dark:text-rose-400">
                -{fmtInt(summary.units_dispensed)}
              </span>
              <span className="ml-1.5 text-xs font-semibold text-slate-500">
                Units ({fmtInt(summary.dispensing_invoices_count)} Prescriptions)
              </span>
            </div>
            <span
              className={`px-2 py-0.5 rounded-full text-xs font-extrabold ${
                complianceRate >= 95
                  ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
                  : "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300"
              }`}
            >
              {complianceRate.toFixed(1)}% Rx OK
            </span>
          </div>
          <div className="mt-2 pt-2 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
            <span className="text-emerald-600 dark:text-emerald-400 font-semibold">
              ✓ {fmtInt(summary.compliant_sales_count)} Rx Verified
            </span>
            <span
              className={
                missingRxCount > 0
                  ? "text-rose-600 dark:text-rose-400 font-bold"
                  : "text-slate-400"
              }
            >
              ⚠ {fmtInt(missingRxCount)} Incomplete Rx
            </span>
          </div>
        </div>

        {/* Card 4: Witnessed Destruction & Supplier Returns */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Witnessed Destruction &amp; Returns
            </span>
            <span className="p-2 rounded-xl bg-amber-50 text-amber-600 dark:bg-amber-950/60 dark:text-amber-400">
              <FireIcon className="w-5 h-5" />
            </span>
          </div>
          <div className="mt-2 flex items-baseline justify-between">
            <div>
              <span className="text-2xl font-extrabold text-slate-900 dark:text-white">
                {fmtInt(summary.units_disposed)}
              </span>
              <span className="ml-1.5 text-xs font-semibold text-slate-500">
                Units Disposed / Quarantined
              </span>
            </div>
            <span className="text-xs font-bold text-slate-600 dark:text-slate-300">
              {fmtInt(summary.witnessed_disposals_count)}/
              {fmtInt(summary.disposal_vouchers_count)} Witnessed
            </span>
          </div>
          <div className="mt-2 pt-2 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
            <span>Returned to Distributor:</span>
            <span className="font-bold text-amber-700 dark:text-amber-400">
              -{fmtInt(summary.units_returned_to_suppliers)} Units
            </span>
          </div>
        </div>
      </div>

      {/* Register Navigation Tabs + Excel-Style Sheet Container */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm overflow-hidden">
        <div className="border-b border-slate-200 dark:border-slate-800 px-3 pt-2 flex flex-wrap items-center gap-1 bg-slate-50/70 dark:bg-slate-900/60 print:hidden">
          {[
            {
              id: "chronological",
              label: "1. Official Statutory Running Register (Col. 1–11)",
              count: filteredChronological.length,
              icon: BookOpenIcon,
            },
            {
              id: "ledger",
              label: "2. Drug-Wise Stock Balance Sheet",
              count: filteredStockLedger.length,
              icon: CubeIcon,
            },
            {
              id: "dispensing",
              label: "3. Patient, CNIC & Doctor (PM&DC) Log",
              count: filteredDispensing.length,
              icon: UserIcon,
              alert: missingRxCount > 0 ? missingRxCount : null,
            },
            {
              id: "procurement",
              label: "4. Authorized Supplier GRN Log",
              count: filteredProcurement.length,
              icon: TruckIcon,
            },
            {
              id: "destruction",
              label: "5. Destruction (Witness) & Returns",
              count: filteredDestruction.length,
              icon: FireIcon,
            },
            {
              id: "batches",
              label: "6. Vault Batches & FEFO",
              count: filteredBatches.length,
              icon: ClockIcon,
            },
          ].map((tab) => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => setActiveTab(tab.id)}
                className={`inline-flex items-center gap-1.5 px-3 py-2.5 text-xs font-bold rounded-t-xl border-b-2 transition ${
                  isActive
                    ? "border-rose-600 text-rose-700 bg-white dark:bg-slate-900 dark:text-rose-400 dark:border-rose-500 shadow-xs"
                    : "border-transparent text-slate-600 hover:text-slate-900 hover:bg-slate-100/80 dark:text-slate-400 dark:hover:text-slate-200 dark:hover:bg-slate-800/50"
                }`}
              >
                <Icon className="w-4 h-4" />
                <span>{tab.label}</span>
                <span
                  className={`px-1.5 py-0.5 rounded-full text-[10px] font-extrabold ${
                    isActive
                      ? "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300"
                      : "bg-slate-200 text-slate-700 dark:bg-slate-800 dark:text-slate-300"
                  }`}
                >
                  {tab.count}
                </span>
                {tab.alert && (
                  <span
                    className="px-1.5 py-0.5 rounded-full text-[10px] font-extrabold bg-amber-500 text-white"
                    title={`${tab.alert} entries missing Doctor or Patient Rx`}
                  >
                    ! {tab.alert}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {/* TAB 1: Complete Single-Report Statutory Register:
            (1) First: Master List of Each Narcotic Product Currently Held (with Each Batch & Expiry Date)
            (2) Second: Product-by-Product Chronological Statutory Running Register (One Product at a Time) */}
        {activeTab === "chronological" && (
          <div className="p-4 md:p-5 space-y-6">
            {/* SECTION 1: FIRST MENTION EACH NARCOTIC PRODUCT WE CURRENTLY HAVE */}
            <div className="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden">
              <div className="bg-slate-900 dark:bg-slate-800 text-white px-4 py-2.5 flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <CubeIcon className="w-4 h-4 text-rose-400" />
                  <h2 className="text-xs md:text-sm font-extrabold uppercase tracking-wider">
                    1. Master List of Controlled / Narcotic Drugs Currently Held (Stock, Batch &amp; Expiry Summary)
                  </h2>
                </div>
                <span className="text-[11px] font-semibold text-slate-300">
                  {filteredStockLedger.length} Controlled{" "}
                  {filteredStockLedger.length === 1 ? "Product" : "Products"} •{" "}
                  Click any drug to jump to its register section
                </span>
              </div>

              <div
                className="ledger-excel-scroll overflow-x-auto"
                tabIndex={0}
                role="region"
                aria-label="Master List of Controlled and Narcotic Drugs Currently Held"
              >
                <table className="ledger-excel-table w-full text-xs border-collapse">
                  <thead>
                    <tr>
                      <th scope="col" className="w-10 text-center">
                        #
                      </th>
                      <th scope="col" aria-sort={getAriaSort("product_name")}>
                        <button
                          type="button"
                          onClick={() =>
                            handleSort("product_name", "Controlled Drug")
                          }
                          className="ledger-sort-btn"
                        >
                          Controlled Drug, Generic Salt/Strength &amp; Manufacturer{" "}
                          {renderSortIcon("product_name")}
                        </button>
                      </th>
                      <th scope="col" className="text-center">
                        Pack
                      </th>
                      <th scope="col">Vault / Rack</th>
                      <th scope="col">Batch No. &amp; Expiry Date</th>
                      <th
                        scope="col"
                        className="text-right"
                        aria-sort={getAriaSort("opening_qty")}
                      >
                        <button
                          type="button"
                          onClick={() =>
                            handleSort("opening_qty", "Opening Stock")
                          }
                          className="ledger-sort-btn justify-end"
                        >
                          Opening B/F {renderSortIcon("opening_qty")}
                        </button>
                      </th>
                      <th
                        scope="col"
                        className="text-right"
                        aria-sort={getAriaSort("purchased_qty")}
                      >
                        <button
                          type="button"
                          onClick={() =>
                            handleSort("purchased_qty", "Purchased Units")
                          }
                          className="ledger-sort-btn justify-end"
                        >
                          + Purchased {renderSortIcon("purchased_qty")}
                        </button>
                      </th>
                      <th scope="col" className="text-right">
                        + Pt. Ret
                      </th>
                      <th
                        scope="col"
                        className="text-right"
                        aria-sort={getAriaSort("dispensed_qty")}
                      >
                        <button
                          type="button"
                          onClick={() =>
                            handleSort("dispensed_qty", "Dispensed Units")
                          }
                          className="ledger-sort-btn justify-end"
                        >
                          - Dispensed {renderSortIcon("dispensed_qty")}
                        </button>
                      </th>
                      <th scope="col" className="text-right">
                        - Destroyed / Ret
                      </th>
                      <th
                        scope="col"
                        className="text-right"
                        aria-sort={getAriaSort("closing_qty")}
                      >
                        <button
                          type="button"
                          onClick={() =>
                            handleSort("closing_qty", "Closing Balance")
                          }
                          className="ledger-sort-btn justify-end"
                        >
                          Closing Balance in Hand {renderSortIcon("closing_qty")}
                        </button>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredStockLedger.length === 0 ? (
                      <tr>
                        <td
                          colSpan={11}
                          className="py-8 text-center text-slate-500 dark:text-slate-400"
                        >
                          No narcotic products found for the selected criteria.
                        </td>
                      </tr>
                    ) : (
                      filteredStockLedger.map((r, idx) => {
                        const batchesList = Array.isArray(r.batches_list)
                          ? r.batches_list
                          : [];
                        const otherOut =
                          Number(r.purchase_return_qty || 0) +
                          Number(r.disposed_qty || 0) -
                          Number(r.adjusted_qty || 0);
                        return (
                          <tr
                            key={`master-${r.product_id}`}
                            className="hover:bg-slate-50/80 dark:hover:bg-slate-800/40"
                          >
                            <td className="text-center text-slate-400 font-mono">
                              {idx + 1}
                            </td>
                            <th
                              scope="row"
                              className="text-left font-semibold text-slate-900 dark:text-slate-100"
                            >
                              <a
                                href={`#narcotic-product-section-${r.product_id}`}
                                className="group inline-flex flex-col hover:text-rose-600 dark:hover:text-rose-400"
                              >
                                <span className="font-bold text-slate-900 dark:text-white group-hover:text-rose-600 dark:group-hover:text-rose-400">
                                  {r.product_name}
                                </span>
                                <span className="text-[11px] text-slate-500 dark:text-slate-400 font-normal">
                                  {r.product_code} •{" "}
                                  {r.formulation || "Controlled Substance"} •{" "}
                                  {r.brand_name}
                                </span>
                              </a>
                            </th>
                            <td className="text-center font-mono text-slate-600 dark:text-slate-300">
                              1×{r.pack_size}
                            </td>
                            <td className="font-mono text-slate-600 dark:text-slate-300">
                              {r.rack}
                            </td>
                            <td className="font-mono">
                              {batchesList.length > 0 ? (
                                <div className="flex flex-col gap-1">
                                  {batchesList.map((bItem, bIdx) => (
                                    <div
                                      key={`${r.product_id}-b-${bIdx}`}
                                      className="inline-flex flex-wrap items-center gap-1.5 text-[11px]"
                                    >
                                      <span className="px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 font-bold text-slate-800 dark:text-slate-200 border border-slate-200 dark:border-slate-700">
                                        {bItem.batch_number}
                                      </span>
                                      {bItem.expiry_date && (
                                        <span className="text-slate-600 dark:text-slate-400">
                                          Exp:{" "}
                                          <strong className="text-slate-800 dark:text-slate-200">
                                            {bItem.expiry_date}
                                          </strong>
                                        </span>
                                      )}
                                      {Number(bItem.quantity || 0) > 0 && (
                                        <span className="text-[10px] font-semibold text-emerald-700 dark:text-emerald-400">
                                          ({fmtInt(bItem.quantity)} U)
                                        </span>
                                      )}
                                    </div>
                                  ))}
                                </div>
                              ) : (
                                <span className="text-slate-400">—</span>
                              )}
                            </td>
                            <td className="text-right font-mono font-semibold text-slate-700 dark:text-slate-200">
                              <div>{fmtInt(r.opening_qty)}</div>
                              <div className="text-[10px] font-normal text-slate-400">
                                {r.opening_pack_breakdown}
                              </div>
                            </td>
                            <td className="text-right font-mono text-blue-700 dark:text-blue-400 font-semibold">
                              {r.purchased_qty > 0
                                ? `+${fmtInt(r.purchased_qty)}`
                                : "0"}
                            </td>
                            <td className="text-right font-mono text-teal-700 dark:text-teal-400">
                              {r.sale_return_qty > 0
                                ? `+${fmtInt(r.sale_return_qty)}`
                                : "0"}
                            </td>
                            <td className="text-right font-mono text-rose-700 dark:text-rose-400 font-semibold">
                              {r.dispensed_qty > 0
                                ? `-${fmtInt(r.dispensed_qty)}`
                                : "0"}
                            </td>
                            <td className="text-right font-mono text-purple-700 dark:text-purple-400">
                              {otherOut > 0
                                ? `-${fmtInt(otherOut)}`
                                : fmtInt(otherOut)}
                            </td>
                            <td className="text-right font-mono bg-slate-50/70 dark:bg-slate-800/40">
                              <div className="font-extrabold text-slate-900 dark:text-white">
                                {fmtInt(r.closing_qty)} Units
                              </div>
                              <div className="text-[10px] text-emerald-700 dark:text-emerald-400 font-semibold">
                                {r.closing_pack_breakdown}
                              </div>
                            </td>
                          </tr>
                        );
                      })
                    )}
                  </tbody>
                  {filteredStockLedger.length > 0 && (
                    <tfoot>
                      <tr className="bg-slate-100 dark:bg-slate-800 font-extrabold text-slate-900 dark:text-white border-t-2 border-slate-300 dark:border-slate-700">
                        <td
                          colSpan={5}
                          className="text-right uppercase text-[11px] tracking-wider"
                        >
                          Total Physical Narcotic Units Across All Controlled Drugs:
                        </td>
                        <td className="text-right font-mono">
                          {fmtInt(ledgerTotals.opening)}
                        </td>
                        <td className="text-right font-mono text-blue-700 dark:text-blue-400">
                          +{fmtInt(ledgerTotals.purchased)}
                        </td>
                        <td className="text-right font-mono text-teal-700 dark:text-teal-400">
                          +{fmtInt(ledgerTotals.saleReturn)}
                        </td>
                        <td className="text-right font-mono text-rose-700 dark:text-rose-400">
                          -{fmtInt(ledgerTotals.dispensed)}
                        </td>
                        <td className="text-right font-mono text-purple-700 dark:text-purple-400">
                          {fmtInt(
                            ledgerTotals.purchaseReturn +
                              ledgerTotals.disposed -
                              ledgerTotals.adjusted
                          )}
                        </td>
                        <td className="text-right font-mono text-emerald-700 dark:text-emerald-400">
                          {fmtInt(ledgerTotals.closing)} Units
                        </td>
                      </tr>
                    </tfoot>
                  )}
                </table>
              </div>
            </div>

            {/* SECTION 2: PRODUCT-BY-PRODUCT CHRONOLOGICAL STATUTORY REGISTER (ONE PRODUCT AT A TIME) */}
            <div className="space-y-5">
              <div className="bg-slate-900 dark:bg-slate-800 text-white px-4 py-2.5 rounded-xl flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <BookOpenIcon className="w-4 h-4 text-rose-400" />
                  <h2 className="text-xs md:text-sm font-extrabold uppercase tracking-wider">
                    2. Product-by-Product Official Chronological Narcotics Register (One Controlled Drug at a Time)
                  </h2>
                </div>
                <span className="text-[11px] font-semibold text-slate-300">
                  Showing {filteredProductRegisters.length} Controlled Drug{" "}
                  {filteredProductRegisters.length === 1 ? "Section" : "Sections"} in Sequence
                </span>
              </div>

              {filteredProductRegisters.length === 0 ? (
                <div className="py-12 text-center text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-800 rounded-xl">
                  No controlled substance register entries found for the selected criteria.
                </div>
              ) : (
                filteredProductRegisters.map((prodReg, pIdx) => {
                  const pBatches = Array.isArray(prodReg.batches_list)
                    ? prodReg.batches_list
                    : [];
                  const pEntries = Array.isArray(prodReg.entries)
                    ? prodReg.entries
                    : [];

                  return (
                    <div
                      key={`prod-reg-${prodReg.product_id}`}
                      id={`narcotic-product-section-${prodReg.product_id}`}
                      className="border border-rose-200 dark:border-rose-900/60 rounded-xl overflow-hidden shadow-xs bg-white dark:bg-slate-900"
                    >
                      {/* Dedicated Drug Header Banner */}
                      <div className="bg-rose-50/80 dark:bg-rose-950/30 border-b border-rose-200 dark:border-rose-900/60 px-4 py-3 flex flex-col lg:flex-row lg:items-center lg:justify-between gap-3">
                        <div className="space-y-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="px-2 py-0.5 rounded-md text-[11px] font-extrabold uppercase bg-rose-600 text-white">
                              Drug #{pIdx + 1} of {filteredProductRegisters.length}
                            </span>
                            <h3 className="text-sm md:text-base font-extrabold text-slate-900 dark:text-white">
                              {prodReg.product_name}
                            </h3>
                            {prodReg.formulation && prodReg.formulation !== "-" && (
                              <span className="text-xs font-semibold text-rose-800 dark:text-rose-300">
                                ({prodReg.formulation})
                              </span>
                            )}
                            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">
                              • {prodReg.brand_name} • Code: {prodReg.product_code} • Pack: 1×{prodReg.pack_size} • {prodReg.rack}
                            </span>
                          </div>

                          {/* Each Batch with its Expiry Date */}
                          <div className="flex flex-wrap items-center gap-1.5 text-xs">
                            <span className="font-bold text-slate-600 dark:text-slate-300">
                              Batches &amp; Expiry Dates:
                            </span>
                            {pBatches.length > 0 ? (
                              pBatches.map((bItem, bIdx) => (
                                <span
                                  key={`hdr-batch-${prodReg.product_id}-${bIdx}`}
                                  className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-white dark:bg-slate-800 border border-rose-200 dark:border-slate-700 font-mono text-[11px] text-slate-800 dark:text-slate-200"
                                >
                                  <strong>{bItem.batch_number}</strong>
                                  {bItem.expiry_date && (
                                    <span className="text-slate-500 dark:text-slate-400">
                                      (Exp: {bItem.expiry_date})
                                    </span>
                                  )}
                                  {Number(bItem.quantity || 0) > 0 && (
                                    <span className="text-emerald-700 dark:text-emerald-400 font-bold">
                                      • {fmtInt(bItem.quantity)} U
                                    </span>
                                  )}
                                </span>
                              ))
                            ) : (
                              <span className="text-slate-400">—</span>
                            )}
                          </div>
                        </div>

                        {/* Product Balance Summary Pills */}
                        <div className="flex flex-wrap items-center gap-2 text-xs font-mono">
                          <span className="px-2.5 py-1 rounded-lg bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200">
                            Opening B/F: <strong>{fmtInt(prodReg.opening_qty)} U</strong>
                          </span>
                          <span className="px-2.5 py-1 rounded-lg bg-blue-50 dark:bg-blue-950/50 border border-blue-200 dark:border-blue-800 text-blue-700 dark:text-blue-300">
                            + In: <strong>+{fmtInt(prodReg.total_in_qty)} U</strong>
                          </span>
                          <span className="px-2.5 py-1 rounded-lg bg-rose-50 dark:bg-rose-950/50 border border-rose-200 dark:border-rose-800 text-rose-700 dark:text-rose-300">
                            - Out: <strong>-{fmtInt(prodReg.total_out_qty)} U</strong>
                          </span>
                          <span className="px-3 py-1 rounded-lg bg-emerald-600 text-white font-extrabold">
                            Closing: {fmtInt(prodReg.closing_qty)} U ({prodReg.closing_pack_breakdown})
                          </span>
                        </div>
                      </div>

                      {/* Product's Own Chronological Running Register Table */}
                      <div
                        className="ledger-excel-scroll overflow-x-auto"
                        tabIndex={0}
                        role="region"
                        aria-label={`Statutory Running Register for ${prodReg.product_name}`}
                      >
                        <table className="ledger-excel-table w-full text-xs border-collapse">
                          <thead>
                            <tr>
                              <th scope="col" className="w-12 text-center">
                                Col.1 S.#
                              </th>
                              <th scope="col">Col.2 Date</th>
                              <th scope="col">Col.3 Bill / GRN / Rx Ref #</th>
                              <th scope="col">
                                Col.4 Source (Supplier &amp; Address) OR Patient Name, CNIC &amp; Address
                              </th>
                              <th scope="col">
                                Col.5 Prescribing Physician (RMP Name, PM&amp;DC # &amp; Clinic)
                              </th>
                              <th scope="col">Col.6 Batch # &amp; Expiry Date</th>
                              <th scope="col" className="text-right">
                                Col.7 Qty In
                              </th>
                              <th scope="col" className="text-right">
                                Col.8 Qty Out
                              </th>
                              <th scope="col" className="text-right">
                                Col.9 Balance in Hand
                              </th>
                              <th scope="col">Col.10 Pharmacist &amp; Rx Audit</th>
                            </tr>
                          </thead>
                          <tbody>
                            {pEntries.map((r) => {
                              const rowBatches = Array.isArray(r.batches_list)
                                ? r.batches_list
                                : [];
                              return (
                                <tr
                                  key={r.row_key}
                                  className={
                                    r.is_opening_row
                                      ? "bg-slate-100/80 dark:bg-slate-800/70 font-semibold"
                                      : !r.is_rx_compliant
                                      ? "bg-amber-50/40 dark:bg-amber-950/20"
                                      : "hover:bg-slate-50/80 dark:hover:bg-slate-800/40"
                                  }
                                >
                                  <td className="text-center font-mono text-slate-500">
                                    {r.product_serial_no ?? r.serial_no}
                                  </td>
                                  <td className="font-mono whitespace-nowrap">
                                    {r.date}
                                  </td>
                                  <th scope="row" className="text-left font-mono">
                                    <div className="flex flex-col gap-0.5">
                                      {r.document_path ? (
                                        <button
                                          type="button"
                                          onClick={() => navigate(r.document_path)}
                                          className="inline-flex items-center gap-1 font-bold text-blue-600 hover:underline dark:text-blue-400"
                                        >
                                          {r.voucher_number}
                                          <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                                        </button>
                                      ) : (
                                        <span className="font-bold text-slate-700 dark:text-slate-200">
                                          {r.voucher_number}
                                        </span>
                                      )}
                                      <span
                                        className={`inline-flex w-fit px-1.5 py-0.2 rounded text-[9px] font-extrabold uppercase ${
                                          r.is_opening_row
                                            ? "bg-slate-200 text-slate-800 dark:bg-slate-700 dark:text-slate-200"
                                            : r.qty_in > 0
                                            ? "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-300"
                                            : r.event_type === "DESTRUCTION_OUT" ||
                                              r.event_type === "QUARANTINE_OUT"
                                            ? "bg-purple-100 text-purple-800 dark:bg-purple-950 dark:text-purple-300"
                                            : "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300"
                                        }`}
                                      >
                                        {r.event_label}
                                      </span>
                                      {r.reference_no && (
                                        <span className="text-[10px] text-slate-500 font-normal">
                                          {r.reference_no}
                                        </span>
                                      )}
                                    </div>
                                  </th>
                                  <td>
                                    <div className="font-semibold text-slate-900 dark:text-white">
                                      {r.party_name}
                                    </div>
                                    {r.party_cnic_or_phone && (
                                      <div className="text-[11px] font-mono text-slate-600 dark:text-slate-300">
                                        CNIC/Tel: {r.party_cnic_or_phone}
                                      </div>
                                    )}
                                    {r.party_address && (
                                      <div className="text-[11px] text-slate-500">
                                        Addr: {r.party_address}
                                      </div>
                                    )}
                                  </td>
                                  <td>
                                    {r.is_opening_row ? (
                                      <span className="text-slate-400">—</span>
                                    ) : r.event_type === "DISPENSED_OUT" ? (
                                      <div>
                                        {r.doctor_name ? (
                                          <div className="font-semibold text-slate-900 dark:text-white">
                                            Dr.{" "}
                                            {r.doctor_name.replace(
                                              /^dr\.?\s*/i,
                                              ""
                                            )}
                                          </div>
                                        ) : (
                                          <div className="text-rose-600 dark:text-rose-400 font-bold text-[11px]">
                                            ⚠ Doctor Not Recorded
                                          </div>
                                        )}
                                        {r.doctor_pmdc ? (
                                          <div className="text-[11px] font-mono text-emerald-700 dark:text-emerald-400 font-semibold">
                                            PM&amp;DC: {r.doctor_pmdc}
                                          </div>
                                        ) : (
                                          <div className="text-[10px] text-amber-600 dark:text-amber-400">
                                            PM&amp;DC # not entered
                                          </div>
                                        )}
                                        {r.doctor_hospital && (
                                          <div className="text-[10px] text-slate-500">
                                            {r.doctor_hospital}
                                          </div>
                                        )}
                                      </div>
                                    ) : (
                                      <div>
                                        <div className="text-slate-700 dark:text-slate-300 font-medium">
                                          {r.doctor_name}
                                        </div>
                                        {r.doctor_pmdc && (
                                          <div className="text-[10px] text-slate-500 font-mono">
                                            {r.doctor_pmdc}
                                          </div>
                                        )}
                                      </div>
                                    )}
                                  </td>
                                  <td className="font-mono">
                                    {rowBatches.length > 0 ? (
                                      <div className="flex flex-col gap-0.5">
                                        {rowBatches.map((rb, rbIdx) => (
                                          <div
                                            key={`${r.row_key}-rb-${rbIdx}`}
                                            className="leading-tight"
                                          >
                                            <span className="font-semibold text-slate-800 dark:text-slate-200">
                                              {rb.batch_number}
                                            </span>
                                            {rb.expiry_date && (
                                              <span className="block text-[10px] text-slate-500">
                                                Exp: {rb.expiry_date}
                                              </span>
                                            )}
                                          </div>
                                        ))}
                                      </div>
                                    ) : (
                                      <div>
                                        <span className="font-semibold text-slate-800 dark:text-slate-200">
                                          {r.batch_number || "—"}
                                        </span>
                                        {r.expiry && (
                                          <span className="block text-[10px] text-slate-500">
                                            Exp: {r.expiry}
                                          </span>
                                        )}
                                      </div>
                                    )}
                                  </td>
                                  <td className="text-right font-mono font-bold text-blue-700 dark:text-blue-400">
                                    {r.qty_in > 0 ? (
                                      <div>
                                        <span>+{fmtInt(r.qty_in)}</span>
                                        {r.qty_in_breakdown && (
                                          <span className="block text-[10px] font-normal text-slate-500">
                                            {r.qty_in_breakdown}
                                          </span>
                                        )}
                                      </div>
                                    ) : (
                                      "—"
                                    )}
                                  </td>
                                  <td className="text-right font-mono font-bold text-rose-700 dark:text-rose-400">
                                    {r.qty_out > 0 ? (
                                      <div>
                                        <span>-{fmtInt(r.qty_out)}</span>
                                        {r.qty_out_breakdown && (
                                          <span className="block text-[10px] font-normal text-slate-500">
                                            {r.qty_out_breakdown}
                                          </span>
                                        )}
                                      </div>
                                    ) : (
                                      "—"
                                    )}
                                  </td>
                                  <td className="text-right font-mono bg-slate-50/90 dark:bg-slate-800/50">
                                    <div className="font-extrabold text-slate-900 dark:text-white text-xs">
                                      {fmtInt(r.running_balance)} U
                                    </div>
                                    <div className="text-[10px] text-slate-500 dark:text-slate-400">
                                      {r.running_pack_breakdown}
                                    </div>
                                  </td>
                                  <td>
                                    <div className="flex flex-col gap-1">
                                      <span className="font-semibold text-slate-800 dark:text-slate-200">
                                        {r.pharmacist_name}
                                      </span>
                                      {r.event_type === "DISPENSED_OUT" ? (
                                        <div className="flex flex-wrap items-center gap-1.5">
                                          {r.is_rx_compliant ? (
                                            <span className="px-1.5 py-0.5 rounded text-[9px] font-extrabold bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300">
                                              ✓ RX OK
                                            </span>
                                          ) : (
                                            <span className="px-1.5 py-0.5 rounded text-[9px] font-extrabold bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-300">
                                              ⚠ MISSING RX
                                            </span>
                                          )}
                                          <button
                                            type="button"
                                            onClick={() => openRxModal(r)}
                                            className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold bg-slate-100 hover:bg-rose-50 text-slate-700 hover:text-rose-700 border border-slate-300 dark:bg-slate-800 dark:text-slate-200 dark:border-slate-700 transition print:hidden"
                                            title="Record or update Doctor PM&DC #, Patient CNIC, Address & Prescription Ref"
                                          >
                                            <PencilSquareIcon className="w-3 h-3" />
                                            Edit Rx/CNIC
                                          </button>
                                        </div>
                                      ) : (
                                        <span className="text-[10px] text-slate-500">
                                          {r.remarks}
                                        </span>
                                      )}
                                    </div>
                                  </td>
                                </tr>
                              );
                            })}
                          </tbody>
                          <tfoot>
                            <tr className="bg-slate-100 dark:bg-slate-800 font-extrabold text-slate-900 dark:text-white border-t border-slate-300 dark:border-slate-700">
                              <td
                                colSpan={6}
                                className="text-right uppercase text-[11px] tracking-wider"
                              >
                                Closing Balance Carried Forward (C/F) for{" "}
                                {prodReg.product_name}:
                              </td>
                              <td className="text-right font-mono text-blue-700 dark:text-blue-400">
                                +{fmtInt(prodReg.total_in_qty)}
                              </td>
                              <td className="text-right font-mono text-rose-700 dark:text-rose-400">
                                -{fmtInt(prodReg.total_out_qty)}
                              </td>
                              <td className="text-right font-mono text-emerald-700 dark:text-emerald-400">
                                <div>{fmtInt(prodReg.closing_qty)} Units</div>
                                <div className="text-[10px] font-semibold">
                                  {prodReg.closing_pack_breakdown}
                                </div>
                              </td>
                              <td className="text-[10px] font-semibold text-slate-500">
                                Verified Physical Balance
                              </td>
                            </tr>
                          </tfoot>
                        </table>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        )}

        {/* TAB 2: Drug-Wise Physical Stock Balance & Movement Sheet */}
        {activeTab === "ledger" && (
          <div
            className="ledger-excel-scroll overflow-x-auto max-h-[68vh]"
            tabIndex={0}
            role="region"
            aria-label="Drug-Wise Physical Stock Balance and Movement Sheet"
          >
            <table className="ledger-excel-table w-full text-xs border-collapse">
              <thead>
                <tr>
                  <th scope="col" className="w-10 text-center">
                    #
                  </th>
                  <th scope="col" aria-sort={getAriaSort("product_name")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("product_name", "Controlled Drug")
                      }
                      className="ledger-sort-btn"
                    >
                      Controlled Drug, Generic Strength &amp; Manufacturer{" "}
                      {renderSortIcon("product_name")}
                    </button>
                  </th>
                  <th scope="col" className="text-center">
                    Pack
                  </th>
                  <th scope="col">Vault / Rack</th>
                  <th scope="col">Batch No. &amp; Expiry Date</th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("opening_qty")}
                  >
                    <button
                      type="button"
                      onClick={() => handleSort("opening_qty", "Opening Stock")}
                      className="ledger-sort-btn justify-end"
                    >
                      Opening B/F {renderSortIcon("opening_qty")}
                    </button>
                  </th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("purchased_qty")}
                  >
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("purchased_qty", "Purchased Units")
                      }
                      className="ledger-sort-btn justify-end"
                    >
                      + Purchased (IN) {renderSortIcon("purchased_qty")}
                    </button>
                  </th>
                  <th scope="col" className="text-right">
                    + Patient Ret
                  </th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("dispensed_qty")}
                  >
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("dispensed_qty", "Dispensed Units")
                      }
                      className="ledger-sort-btn justify-end"
                    >
                      - Dispensed (OUT) {renderSortIcon("dispensed_qty")}
                    </button>
                  </th>
                  <th scope="col" className="text-right">
                    - Supplier Ret
                  </th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("disposed_qty")}
                  >
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("disposed_qty", "Disposed Units")
                      }
                      className="ledger-sort-btn justify-end"
                    >
                      - Destroyed {renderSortIcon("disposed_qty")}
                    </button>
                  </th>
                  <th scope="col" className="text-right">
                    ± Audit Adj
                  </th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("closing_qty")}
                  >
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("closing_qty", "Closing Balance")
                      }
                      className="ledger-sort-btn justify-end"
                    >
                      Closing Balance in Hand {renderSortIcon("closing_qty")}
                    </button>
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredStockLedger.length === 0 ? (
                  <tr>
                    <td
                      colSpan={13}
                      className="py-12 text-center text-slate-500 dark:text-slate-400"
                    >
                      No narcotic products found. Mark products with{" "}
                      <span className="font-bold text-rose-600">
                        Narcotic = Yes
                      </span>{" "}
                      in Products catalog to track them in this statutory
                      register.
                    </td>
                  </tr>
                ) : (
                  filteredStockLedger.map((r, idx) => {
                    const batchesList = Array.isArray(r.batches_list)
                      ? r.batches_list
                      : [];
                    return (
                      <tr
                        key={r.product_id}
                        className="hover:bg-slate-50/80 dark:hover:bg-slate-800/40"
                      >
                        <td className="text-center text-slate-400 font-mono">
                          {idx + 1}
                        </td>
                        <th
                          scope="row"
                          className="text-left font-semibold text-slate-900 dark:text-slate-100"
                        >
                          <div className="flex flex-col">
                            <span className="font-bold text-slate-900 dark:text-white">
                              {r.product_name}
                            </span>
                            <span className="text-[11px] text-slate-500 dark:text-slate-400 font-normal">
                              {r.product_code} • {r.formulation || "Controlled"}{" "}
                              • {r.brand_name}
                            </span>
                          </div>
                        </th>
                        <td className="text-center font-mono text-slate-600 dark:text-slate-300">
                          1×{r.pack_size}
                        </td>
                        <td className="font-mono text-slate-600 dark:text-slate-300">
                          {r.rack}
                        </td>
                        <td className="font-mono">
                          {batchesList.length > 0 ? (
                            <div className="flex flex-col gap-0.5">
                              {batchesList.map((bItem, bIdx) => (
                                <div
                                  key={`${r.product_id}-lb-${bIdx}`}
                                  className="text-[11px]"
                                >
                                  <span className="font-bold text-slate-800 dark:text-slate-200">
                                    {bItem.batch_number}
                                  </span>
                                  {bItem.expiry_date && (
                                    <span className="text-slate-500 ml-1">
                                      (Exp: {bItem.expiry_date})
                                    </span>
                                  )}
                                </div>
                              ))}
                            </div>
                          ) : (
                            <span className="text-slate-400">—</span>
                          )}
                        </td>
                        <td className="text-right font-mono font-semibold text-slate-700 dark:text-slate-200">
                          <div>{fmtInt(r.opening_qty)}</div>
                          <div className="text-[10px] font-normal text-slate-400">
                            {r.opening_pack_breakdown}
                          </div>
                        </td>
                        <td className="text-right font-mono text-blue-700 dark:text-blue-400 font-semibold">
                          {r.purchased_qty > 0
                            ? `+${fmtInt(r.purchased_qty)}`
                            : "0"}
                        </td>
                        <td className="text-right font-mono text-teal-700 dark:text-teal-400">
                          {r.sale_return_qty > 0
                            ? `+${fmtInt(r.sale_return_qty)}`
                            : "0"}
                        </td>
                        <td className="text-right font-mono text-rose-700 dark:text-rose-400 font-semibold">
                          {r.dispensed_qty > 0
                            ? `-${fmtInt(r.dispensed_qty)}`
                            : "0"}
                        </td>
                        <td className="text-right font-mono text-amber-700 dark:text-amber-400">
                          {r.purchase_return_qty > 0
                            ? `-${fmtInt(r.purchase_return_qty)}`
                            : "0"}
                        </td>
                        <td className="text-right font-mono text-purple-700 dark:text-purple-400 font-semibold">
                          {r.disposed_qty > 0
                            ? `-${fmtInt(r.disposed_qty)}`
                            : "0"}
                        </td>
                        <td className="text-right font-mono text-slate-600 dark:text-slate-300">
                          {r.adjusted_qty > 0
                            ? `+${fmtInt(r.adjusted_qty)}`
                            : fmtInt(r.adjusted_qty)}
                        </td>
                        <td className="text-right font-mono bg-slate-50/70 dark:bg-slate-800/40">
                          <div className="font-extrabold text-slate-900 dark:text-white">
                            {fmtInt(r.closing_qty)} Units
                          </div>
                          <div className="text-[10px] text-emerald-700 dark:text-emerald-400 font-semibold">
                            {r.closing_pack_breakdown}
                          </div>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
              {filteredStockLedger.length > 0 && (
                <tfoot>
                  <tr className="bg-slate-100 dark:bg-slate-800 font-extrabold text-slate-900 dark:text-white border-t-2 border-slate-300 dark:border-slate-700">
                    <td
                      colSpan={5}
                      className="text-right uppercase text-[11px] tracking-wider"
                    >
                      Total Physical Narcotic Units Across All Controlled Drugs:
                    </td>
                    <td className="text-right font-mono">
                      {fmtInt(ledgerTotals.opening)}
                    </td>
                    <td className="text-right font-mono text-blue-700 dark:text-blue-400">
                      +{fmtInt(ledgerTotals.purchased)}
                    </td>
                    <td className="text-right font-mono text-teal-700 dark:text-teal-400">
                      +{fmtInt(ledgerTotals.saleReturn)}
                    </td>
                    <td className="text-right font-mono text-rose-700 dark:text-rose-400">
                      -{fmtInt(ledgerTotals.dispensed)}
                    </td>
                    <td className="text-right font-mono text-amber-700 dark:text-amber-400">
                      -{fmtInt(ledgerTotals.purchaseReturn)}
                    </td>
                    <td className="text-right font-mono text-purple-700 dark:text-purple-400">
                      -{fmtInt(ledgerTotals.disposed)}
                    </td>
                    <td className="text-right font-mono">
                      {fmtInt(ledgerTotals.adjusted)}
                    </td>
                    <td className="text-right font-mono text-emerald-700 dark:text-emerald-400">
                      {fmtInt(ledgerTotals.closing)} Units
                    </td>
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        )}

        {/* TAB 3: Patient, CNIC & Prescribing Doctor (PM&DC) Dispensing Register */}
        {activeTab === "dispensing" && (
          <div
            className="ledger-excel-scroll overflow-x-auto max-h-[68vh]"
            tabIndex={0}
            role="region"
            aria-label="Patient, CNIC and Prescribing Doctor Dispensing Register"
          >
            <table className="ledger-excel-table w-full text-xs border-collapse">
              <thead>
                <tr>
                  <th scope="col" aria-sort={getAriaSort("date")}>
                    <button
                      type="button"
                      onClick={() => handleSort("date", "Dispensing Date")}
                      className="ledger-sort-btn"
                    >
                      Date {renderSortIcon("date")}
                    </button>
                  </th>
                  <th scope="col" aria-sort={getAriaSort("posted_number")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("posted_number", "Invoice or Rx Number")
                      }
                      className="ledger-sort-btn"
                    >
                      Invoice / Rx Ref # {renderSortIcon("posted_number")}
                    </button>
                  </th>
                  <th scope="col" aria-sort={getAriaSort("product_name")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("product_name", "Controlled Drug")
                      }
                      className="ledger-sort-btn"
                    >
                      Controlled Drug &amp; Strength{" "}
                      {renderSortIcon("product_name")}
                    </button>
                  </th>
                  <th scope="col">Batch # &amp; Expiry</th>
                  <th scope="col" aria-sort={getAriaSort("doctor_name")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("doctor_name", "Prescribing Doctor")
                      }
                      className="ledger-sort-btn"
                    >
                      Prescribing Doctor (RMP, PM&amp;DC # &amp; Hospital){" "}
                      {renderSortIcon("doctor_name")}
                    </button>
                  </th>
                  <th scope="col" aria-sort={getAriaSort("patient_name")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("patient_name", "Patient Name and CNIC")
                      }
                      className="ledger-sort-btn"
                    >
                      Patient Name, CNIC/Contact &amp; Address{" "}
                      {renderSortIcon("patient_name")}
                    </button>
                  </th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("total_units_out")}
                  >
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("total_units_out", "Quantity Dispensed")
                      }
                      className="ledger-sort-btn justify-end"
                    >
                      Qty Dispensed (OUT) {renderSortIcon("total_units_out")}
                    </button>
                  </th>
                  <th scope="col">Supervising Pharmacist</th>
                  <th scope="col" className="text-center">
                    Statutory Rx Audit &amp; Action
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredDispensing.length === 0 ? (
                  <tr>
                    <td
                      colSpan={9}
                      className="py-12 text-center text-slate-500 dark:text-slate-400"
                    >
                      No narcotic dispensing transactions match the current
                      filters.
                    </td>
                  </tr>
                ) : (
                  filteredDispensing.map((r) => (
                    <tr
                      key={r.id}
                      className={
                        !r.is_rx_compliant
                          ? "bg-amber-50/40 dark:bg-amber-950/20"
                          : ""
                      }
                    >
                      <td className="font-mono whitespace-nowrap">{r.date}</td>
                      <th scope="row" className="font-mono font-bold text-left">
                        <div className="flex flex-col">
                          {r.sale_invoice_id ? (
                            <button
                              type="button"
                              onClick={() =>
                                navigate(`/sale-invoices/${r.sale_invoice_id}`)
                              }
                              className="inline-flex items-center gap-1 text-blue-600 hover:underline dark:text-blue-400"
                            >
                              {r.posted_number}
                              <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                            </button>
                          ) : (
                            r.posted_number
                          )}
                          {r.raw_prescription_no && (
                            <span className="text-[10px] text-slate-500 font-normal">
                              Rx #: {r.raw_prescription_no}
                            </span>
                          )}
                        </div>
                      </th>
                      <td>
                        <div className="font-bold text-slate-900 dark:text-white">
                          {r.product_name}
                        </div>
                        <div className="text-[11px] text-slate-500">
                          {r.formulation} • {r.brand_name}
                        </div>
                      </td>
                      <td className="font-mono">
                        <span className="font-semibold text-slate-800 dark:text-slate-200">
                          {r.batch_number}
                        </span>
                        {r.expiry && (
                          <span className="block text-[10px] text-slate-500">
                            Exp: {r.expiry}
                          </span>
                        )}
                      </td>
                      <td>
                        {r.doctor_name ? (
                          <div>
                            <span className="font-semibold text-slate-900 dark:text-white">
                              Dr. {r.doctor_name.replace(/^dr\.?\s*/i, "")}
                            </span>
                            {r.doctor_pmdc ? (
                              <span className="block text-[11px] font-mono text-emerald-700 dark:text-emerald-400 font-semibold">
                                PM&amp;DC: {r.doctor_pmdc}
                              </span>
                            ) : (
                              <span className="block text-[10px] text-amber-600">
                                PM&amp;DC # not entered
                              </span>
                            )}
                            {r.doctor_hospital && (
                              <span className="block text-[10px] text-slate-500">
                                {r.doctor_hospital}
                              </span>
                            )}
                          </div>
                        ) : (
                          <span className="text-rose-600 dark:text-rose-400 font-bold text-[11px]">
                            — Doctor Not Recorded —
                          </span>
                        )}
                      </td>
                      <td>
                        <div className="font-semibold text-slate-900 dark:text-white">
                          {r.patient_name || r.customer_name}
                        </div>
                        {r.patient_contact ? (
                          <div className="text-[11px] text-slate-600 dark:text-slate-300 font-mono">
                            CNIC/Tel: {r.patient_contact}
                          </div>
                        ) : (
                          <div className="text-[10px] text-amber-600">
                            CNIC / Phone not entered
                          </div>
                        )}
                        {r.patient_address && (
                          <div className="text-[10px] text-slate-500">
                            Addr: {r.patient_address}
                          </div>
                        )}
                      </td>
                      <td className="text-right font-mono">
                        <div className="font-bold text-rose-700 dark:text-rose-400">
                          -{fmtInt(r.total_units_out)} Units
                        </div>
                        <div className="text-[10px] text-slate-500">
                          {r.pack_breakdown}
                        </div>
                      </td>
                      <td className="font-semibold text-slate-700 dark:text-slate-300">
                        {r.dispensed_by}
                      </td>
                      <td className="text-center">
                        <div className="flex flex-col items-center gap-1">
                          {r.is_rx_compliant ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-extrabold bg-emerald-100 text-emerald-800 dark:bg-emerald-950/80 dark:text-emerald-300">
                              ✓ RX COMPLIANT
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-extrabold bg-amber-100 text-amber-900 dark:bg-amber-950/80 dark:text-amber-300">
                              ⚠ MISSING RX
                            </span>
                          )}
                          <button
                            type="button"
                            onClick={() => openRxModal(r)}
                            className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[10px] font-bold bg-rose-50 hover:bg-rose-100 text-rose-700 border border-rose-200 dark:bg-rose-950/60 dark:text-rose-300 dark:border-rose-800 transition print:hidden"
                          >
                            <PencilSquareIcon className="w-3.5 h-3.5" />
                            Update Rx / PMDC / CNIC
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}

        {/* TAB 4: Authorized Supplier Inward Procurement Register (Zero Monetary Values) */}
        {activeTab === "procurement" && (
          <div
            className="ledger-excel-scroll overflow-x-auto max-h-[68vh]"
            tabIndex={0}
            role="region"
            aria-label="Inward Authorized Supplier Procurement Register"
          >
            <table className="ledger-excel-table w-full text-xs border-collapse">
              <thead>
                <tr>
                  <th scope="col" aria-sort={getAriaSort("date")}>
                    <button
                      type="button"
                      onClick={() => handleSort("date", "Receipt Date")}
                      className="ledger-sort-btn"
                    >
                      Date {renderSortIcon("date")}
                    </button>
                  </th>
                  <th scope="col" aria-sort={getAriaSort("posted_number")}>
                    <button
                      type="button"
                      onClick={() => handleSort("posted_number", "GRN Number")}
                      className="ledger-sort-btn"
                    >
                      GRN # {renderSortIcon("posted_number")}
                    </button>
                  </th>
                  <th scope="col">Supplier Bill / Gate Pass #</th>
                  <th scope="col" aria-sort={getAriaSort("supplier_name")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("supplier_name", "Authorized Distributor")
                      }
                      className="ledger-sort-btn"
                    >
                      Authorized Distributor, License &amp; Address{" "}
                      {renderSortIcon("supplier_name")}
                    </button>
                  </th>
                  <th scope="col" aria-sort={getAriaSort("product_name")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("product_name", "Controlled Drug")
                      }
                      className="ledger-sort-btn"
                    >
                      Controlled Drug &amp; Strength{" "}
                      {renderSortIcon("product_name")}
                    </button>
                  </th>
                  <th scope="col">Batch # &amp; Expiry</th>
                  <th scope="col" className="text-right">
                    Packs Recv
                  </th>
                  <th scope="col" className="text-right">
                    Bonus Units
                  </th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("total_units_in")}
                  >
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("total_units_in", "Total Units Received")
                      }
                      className="ledger-sort-btn justify-end"
                    >
                      Total Qty Received (IN) {renderSortIcon("total_units_in")}
                    </button>
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredProcurement.length === 0 ? (
                  <tr>
                    <td
                      colSpan={9}
                      className="py-12 text-center text-slate-500 dark:text-slate-400"
                    >
                      No inward narcotic procurement records found for this
                      period.
                    </td>
                  </tr>
                ) : (
                  filteredProcurement.map((r) => (
                    <tr key={r.id}>
                      <td className="font-mono whitespace-nowrap">{r.date}</td>
                      <th scope="row" className="font-mono font-bold text-left">
                        {r.purchase_invoice_id ? (
                          <button
                            type="button"
                            onClick={() =>
                              navigate(
                                `/purchase-invoices/${r.purchase_invoice_id}`
                              )
                            }
                            className="inline-flex items-center gap-1 text-blue-600 hover:underline dark:text-blue-400"
                          >
                            {r.posted_number}
                            <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                          </button>
                        ) : (
                          r.posted_number
                        )}
                      </th>
                      <td className="font-mono font-semibold text-slate-700 dark:text-slate-300">
                        {r.invoice_number}
                      </td>
                      <td>
                        <div className="font-bold text-slate-900 dark:text-white">
                          {r.supplier_name}
                        </div>
                        {r.supplier_license_or_tax && (
                          <div className="text-[11px] font-mono text-slate-600 dark:text-slate-300">
                            Lic / NTN: {r.supplier_license_or_tax}
                          </div>
                        )}
                        {(r.supplier_phone || r.supplier_address) && (
                          <div className="text-[10px] text-slate-500">
                            {[r.supplier_phone, r.supplier_address]
                              .filter(Boolean)
                              .join(" • ")}
                          </div>
                        )}
                      </td>
                      <td>
                        <div className="font-bold text-slate-900 dark:text-white">
                          {r.product_name}
                        </div>
                        <div className="text-[11px] text-slate-500">
                          {r.formulation} • 1×{r.pack_size} • {r.brand_name}
                        </div>
                      </td>
                      <td className="font-mono">
                        <span className="font-semibold">{r.batch_number}</span>
                        {r.expiry && (
                          <span className="block text-[10px] text-slate-500">
                            Exp: {r.expiry}
                          </span>
                        )}
                      </td>
                      <td className="text-right font-mono">
                        {fmtInt(r.pack_quantity)}
                      </td>
                      <td className="text-right font-mono text-slate-600 dark:text-slate-300">
                        {fmtInt(r.unit_bonus)}
                      </td>
                      <td className="text-right font-mono">
                        <div className="font-extrabold text-blue-700 dark:text-blue-400">
                          +{fmtInt(r.total_units_in)} Units
                        </div>
                        <div className="text-[10px] text-slate-500">
                          {r.pack_breakdown}
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}

        {/* TAB 5: Controlled Destruction (Witnessed), Quarantine & Returns Log (Zero Monetary Values) */}
        {activeTab === "destruction" && (
          <div
            className="ledger-excel-scroll overflow-x-auto max-h-[68vh]"
            tabIndex={0}
            role="region"
            aria-label="Controlled Destruction, Witness and Returns Log"
          >
            <table className="ledger-excel-table w-full text-xs border-collapse">
              <thead>
                <tr>
                  <th scope="col" aria-sort={getAriaSort("date")}>
                    <button
                      type="button"
                      onClick={() => handleSort("date", "Event Date")}
                      className="ledger-sort-btn"
                    >
                      Date {renderSortIcon("date")}
                    </button>
                  </th>
                  <th scope="col">Voucher #</th>
                  <th scope="col">Event Type</th>
                  <th scope="col" aria-sort={getAriaSort("product_name")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("product_name", "Controlled Drug")
                      }
                      className="ledger-sort-btn"
                    >
                      Controlled Drug &amp; Strength{" "}
                      {renderSortIcon("product_name")}
                    </button>
                  </th>
                  <th scope="col">Batch # &amp; Expiry</th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("quantity")}
                  >
                    <button
                      type="button"
                      onClick={() => handleSort("quantity", "Physical Units")}
                      className="ledger-sort-btn justify-end"
                    >
                      Physical Units {renderSortIcon("quantity")}
                    </button>
                  </th>
                  <th scope="col">Cause / Destruction Method / Party</th>
                  <th scope="col">
                    Witness / Authority Sign-off (Drug Inspector)
                  </th>
                  <th scope="col">Supervising Pharmacist</th>
                </tr>
              </thead>
              <tbody>
                {filteredDestruction.length === 0 ? (
                  <tr>
                    <td
                      colSpan={9}
                      className="py-12 text-center text-slate-500 dark:text-slate-400"
                    >
                      No narcotic destructions, quarantines, or returns recorded
                      in this period.
                    </td>
                  </tr>
                ) : (
                  filteredDestruction.map((r) => (
                    <tr key={r.row_key}>
                      <td className="font-mono whitespace-nowrap">{r.date}</td>
                      <th scope="row" className="font-mono font-bold text-left">
                        {r.event_type === "DESTRUCTION" ||
                        r.event_type === "QUARANTINE" ? (
                          <button
                            type="button"
                            onClick={() =>
                              navigate(`/stock-disposals/${r.document_id}`)
                            }
                            className="inline-flex items-center gap-1 text-blue-600 hover:underline dark:text-blue-400"
                          >
                            {r.document_number}
                            <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                          </button>
                        ) : (
                          r.document_number
                        )}
                      </th>
                      <td>
                        <span
                          className={`inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-extrabold ${
                            r.event_type === "DESTRUCTION"
                              ? "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300"
                              : r.event_type === "QUARANTINE"
                              ? "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300"
                              : r.event_type === "PATIENT_RETURN"
                              ? "bg-teal-100 text-teal-800 dark:bg-teal-950 dark:text-teal-300"
                              : "bg-purple-100 text-purple-800 dark:bg-purple-950 dark:text-purple-300"
                          }`}
                        >
                          {r.event_type.replace("_", " ")} ({r.direction})
                        </span>
                      </td>
                      <td>
                        <div className="font-bold text-slate-900 dark:text-white">
                          {r.product_name}
                        </div>
                        <div className="text-[11px] text-slate-500">
                          {r.formulation}
                        </div>
                      </td>
                      <td className="font-mono">
                        {r.batch_number}
                        {r.expiry && (
                          <span className="block text-[10px] text-slate-500">
                            Exp: {r.expiry}
                          </span>
                        )}
                      </td>
                      <td
                        className={`text-right font-mono font-bold ${
                          r.direction === "IN"
                            ? "text-teal-700 dark:text-teal-400"
                            : "text-rose-700 dark:text-rose-400"
                        }`}
                      >
                        <div>
                          {r.direction === "IN" ? "+" : "-"}
                          {fmtInt(r.quantity)} U
                        </div>
                        <div className="text-[10px] font-normal text-slate-500">
                          {r.pack_breakdown}
                        </div>
                      </td>
                      <td>{r.party_or_authority}</td>
                      <td>
                        {r.witness_name ? (
                          <div>
                            <span className="font-bold text-slate-900 dark:text-white">
                              {r.witness_name}
                            </span>
                            {r.witness_designation && (
                              <span className="block text-[10px] text-slate-500">
                                {r.witness_designation}
                              </span>
                            )}
                          </div>
                        ) : r.event_type === "DESTRUCTION" ? (
                          <span className="text-rose-600 font-bold text-[11px]">
                            ⚠ Witness Not Recorded
                          </span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </td>
                      <td className="text-slate-700 dark:text-slate-300">
                        {r.handled_by}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}

        {/* TAB 6: Active Narcotics Cabinet Batches & FEFO Traceability (Zero Monetary Values) */}
        {activeTab === "batches" && (
          <div
            className="ledger-excel-scroll overflow-x-auto max-h-[68vh]"
            tabIndex={0}
            role="region"
            aria-label="Active Narcotic Batch and FEFO Traceability"
          >
            <table className="ledger-excel-table w-full text-xs border-collapse">
              <thead>
                <tr>
                  <th scope="col" aria-sort={getAriaSort("product_name")}>
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("product_name", "Controlled Drug")
                      }
                      className="ledger-sort-btn"
                    >
                      Controlled Drug, Strength &amp; Manufacturer{" "}
                      {renderSortIcon("product_name")}
                    </button>
                  </th>
                  <th scope="col">Batch #</th>
                  <th scope="col" aria-sort={getAriaSort("expiry_date")}>
                    <button
                      type="button"
                      onClick={() => handleSort("expiry_date", "Expiry Date")}
                      className="ledger-sort-btn"
                    >
                      Expiry Date {renderSortIcon("expiry_date")}
                    </button>
                  </th>
                  <th
                    scope="col"
                    className="text-center"
                    aria-sort={getAriaSort("days_remaining")}
                  >
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("days_remaining", "Days Remaining")
                      }
                      className="ledger-sort-btn justify-center"
                    >
                      FEFO Shelf Status {renderSortIcon("days_remaining")}
                    </button>
                  </th>
                  <th scope="col">Double-Lock Vault / Rack</th>
                  <th
                    scope="col"
                    className="text-right"
                    aria-sort={getAriaSort("quantity")}
                  >
                    <button
                      type="button"
                      onClick={() =>
                        handleSort("quantity", "Physical Units in Hand")
                      }
                      className="ledger-sort-btn justify-end"
                    >
                      Physical Units in Hand {renderSortIcon("quantity")}
                    </button>
                  </th>
                  <th scope="col" className="text-right">
                    Pack &amp; Loose Breakdown
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredBatches.length === 0 ? (
                  <tr>
                    <td
                      colSpan={7}
                      className="py-12 text-center text-slate-500 dark:text-slate-400"
                    >
                      No active narcotic batches with positive stock on hand.
                    </td>
                  </tr>
                ) : (
                  filteredBatches.map((b) => (
                    <tr key={b.id}>
                      <th scope="row" className="text-left">
                        <div className="font-bold text-slate-900 dark:text-white">
                          {b.product_name}
                        </div>
                        <div className="text-[11px] text-slate-500 font-normal">
                          {b.product_code} • {b.formulation} • {b.brand_name}
                        </div>
                      </th>
                      <td className="font-mono font-bold">{b.batch_number}</td>
                      <td className="font-mono">{b.expiry_date || "—"}</td>
                      <td className="text-center">
                        {b.expiry_status === "expired" ? (
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-extrabold bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300">
                            EXPIRED ({Math.abs(b.days_remaining)}d ago)
                          </span>
                        ) : b.expiry_status === "critical" ? (
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-extrabold bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300">
                            CRITICAL ({b.days_remaining}d left)
                          </span>
                        ) : b.expiry_status === "near_expiry" ? (
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-extrabold bg-yellow-100 text-yellow-800 dark:bg-yellow-950 dark:text-yellow-300">
                            NEAR EXPIRY ({b.days_remaining}d left)
                          </span>
                        ) : (
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-extrabold bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300">
                            HEALTHY{" "}
                            {b.days_remaining != null
                              ? `(${b.days_remaining}d)`
                              : ""}
                          </span>
                        )}
                      </td>
                      <td className="font-mono text-slate-600 dark:text-slate-300">
                        {b.rack}
                      </td>
                      <td className="text-right font-mono font-extrabold text-slate-900 dark:text-white">
                        {fmtInt(b.quantity)} Units
                      </td>
                      <td className="text-right font-mono font-semibold text-emerald-700 dark:text-emerald-400">
                        {b.pack_breakdown}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Inline Modal to Record / Update Pakistani Statutory Rx, PM&DC & Patient CNIC */}
      {editingRxRow && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 backdrop-blur-xs p-4 print:hidden">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-2xl max-w-xl w-full overflow-hidden">
            <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between bg-rose-50/60 dark:bg-rose-950/30">
              <div>
                <span className="text-[10px] font-extrabold uppercase tracking-wider text-rose-700 dark:text-rose-400">
                  Drugs Act 1976 / Schedule G &amp; CNSA Compliance
                </span>
                <h3 className="text-base font-extrabold text-slate-900 dark:text-white">
                  Statutory Prescription, PM&amp;DC &amp; Patient CNIC Particulars
                </h3>
                <p className="text-xs text-slate-500">
                  Invoice{" "}
                  <span className="font-mono font-bold text-slate-700 dark:text-slate-200">
                    {editingRxRow.posted_number || editingRxRow.voucher_number}
                  </span>{" "}
                  • {editingRxRow.product_name}
                </p>
              </div>
              <button
                type="button"
                onClick={() => setEditingRxRow(null)}
                className="p-1.5 rounded-lg text-slate-400 hover:text-slate-700 hover:bg-slate-200/60 dark:hover:text-slate-200 dark:hover:bg-slate-800"
              >
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleSaveRxMeta} className="p-5 space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3.5">
                <div>
                  <label className="block text-[11px] font-bold uppercase text-slate-600 dark:text-slate-300 mb-1">
                    Prescribing Doctor Name (RMP) *
                  </label>
                  <input
                    type="text"
                    required
                    value={rxForm.doctor_name}
                    onChange={(e) =>
                      setRxForm((f) => ({ ...f, doctor_name: e.target.value }))
                    }
                    placeholder="e.g. Dr. Tariq Mahmood"
                    className="w-full h-9 px-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs text-slate-900 dark:text-white"
                  />
                </div>

                <div>
                  <label className="block text-[11px] font-bold uppercase text-slate-600 dark:text-slate-300 mb-1">
                    Doctor PM&amp;DC / PMC Reg #
                  </label>
                  <input
                    type="text"
                    value={rxForm.doctor_pmdc}
                    onChange={(e) =>
                      setRxForm((f) => ({ ...f, doctor_pmdc: e.target.value }))
                    }
                    placeholder="e.g. PMDC-45821-P"
                    className="w-full h-9 px-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs font-mono text-slate-900 dark:text-white"
                  />
                </div>

                <div className="sm:col-span-2">
                  <label className="block text-[11px] font-bold uppercase text-slate-600 dark:text-slate-300 mb-1">
                    Hospital / Clinic Name &amp; City
                  </label>
                  <input
                    type="text"
                    value={rxForm.doctor_hospital}
                    onChange={(e) =>
                      setRxForm((f) => ({
                        ...f,
                        doctor_hospital: e.target.value,
                      }))
                    }
                    placeholder="e.g. Services Hospital / Gulberg Clinic, Lahore"
                    className="w-full h-9 px-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs text-slate-900 dark:text-white"
                  />
                </div>

                <div>
                  <label className="block text-[11px] font-bold uppercase text-slate-600 dark:text-slate-300 mb-1">
                    Patient / Attendant Full Name *
                  </label>
                  <input
                    type="text"
                    required
                    value={rxForm.patient_name}
                    onChange={(e) =>
                      setRxForm((f) => ({ ...f, patient_name: e.target.value }))
                    }
                    placeholder="e.g. Muhammad Bilal"
                    className="w-full h-9 px-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs text-slate-900 dark:text-white"
                  />
                </div>

                <div>
                  <label className="block text-[11px] font-bold uppercase text-slate-600 dark:text-slate-300 mb-1">
                    Patient CNIC # / Contact
                  </label>
                  <input
                    type="text"
                    value={rxForm.patient_cnic}
                    onChange={(e) =>
                      setRxForm((f) => ({ ...f, patient_cnic: e.target.value }))
                    }
                    placeholder="e.g. 35202-1234567-1 / 0300-1234567"
                    className="w-full h-9 px-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs font-mono text-slate-900 dark:text-white"
                  />
                </div>

                <div className="sm:col-span-2">
                  <label className="block text-[11px] font-bold uppercase text-slate-600 dark:text-slate-300 mb-1">
                    Patient Residential Address
                  </label>
                  <input
                    type="text"
                    value={rxForm.patient_address}
                    onChange={(e) =>
                      setRxForm((f) => ({
                        ...f,
                        patient_address: e.target.value,
                      }))
                    }
                    placeholder="e.g. House 14, Street 5, Model Town, Lahore"
                    className="w-full h-9 px-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs text-slate-900 dark:text-white"
                  />
                </div>

                <div>
                  <label className="block text-[11px] font-bold uppercase text-slate-600 dark:text-slate-300 mb-1">
                    Prescription Serial / Ref #
                  </label>
                  <input
                    type="text"
                    value={rxForm.prescription_no}
                    onChange={(e) =>
                      setRxForm((f) => ({
                        ...f,
                        prescription_no: e.target.value,
                      }))
                    }
                    placeholder="e.g. Rx-2026-089"
                    className="w-full h-9 px-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs font-mono text-slate-900 dark:text-white"
                  />
                </div>

                <div>
                  <label className="block text-[11px] font-bold uppercase text-slate-600 dark:text-slate-300 mb-1">
                    Supervising Pharmacist (Cat-A)
                  </label>
                  <input
                    type="text"
                    value={rxForm.pharmacist_name}
                    onChange={(e) =>
                      setRxForm((f) => ({
                        ...f,
                        pharmacist_name: e.target.value,
                      }))
                    }
                    placeholder="e.g. RPh. Ahmad Raza (Reg # A-9421)"
                    className="w-full h-9 px-3 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs text-slate-900 dark:text-white"
                  />
                </div>
              </div>

              <div className="pt-3 border-t border-slate-200 dark:border-slate-800 flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setEditingRxRow(null)}
                  className="px-4 py-2 rounded-xl text-xs font-semibold bg-slate-100 hover:bg-slate-200 text-slate-700 dark:bg-slate-800 dark:text-slate-300"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={savingRx}
                  className="px-4 py-2 rounded-xl text-xs font-bold bg-rose-600 hover:bg-rose-700 text-white shadow-xs disabled:opacity-60"
                >
                  {savingRx
                    ? "Saving Register Entry…"
                    : "Save Statutory Particulars"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
