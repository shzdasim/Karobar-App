import React, { useState, useEffect, useCallback, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import {
  ShieldCheckIcon,
  KeyIcon,
  ServerStackIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  ArrowPathIcon,
  EyeIcon,
  EyeSlashIcon,
  BookOpenIcon,
  ArrowTopRightOnSquareIcon,
  XMarkIcon,
  QuestionMarkCircleIcon,
  ClipboardDocumentCheckIcon,
} from "@heroicons/react/24/outline";
import { GlassInput } from "@/components/Glass.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";

const FBR_MODE_OPTIONS = [
  {
    value: "digital_invoicing",
    label: "FBR Digital Invoicing Cloud API (gw.fbr.gov.pk)",
  },
  {
    value: "tier1_pos_cloud",
    label: "FBR Tier-1 Retail POS Cloud API (esp.fbr.gov.pk)",
  },
  {
    value: "tier1_pos_ims",
    label: "FBR Local IMS Desktop Service (localhost:8524)",
  },
];

const FBR_ENVIRONMENT_OPTIONS = [
  { value: "sandbox", label: "Sandbox (Testing & PRAL Verification)" },
  { value: "production", label: "Production (Live FBR Fiscalization)" },
];

const FBR_SYNC_BEHAVIOR_OPTIONS = [
  { value: "auto", label: "Auto-Sync in Real Time When Saving Sale" },
  { value: "manual", label: "Queue as Pending & Sync Manually" },
];

const PROVINCE_OPTIONS = [
  { value: "Punjab", label: "Punjab" },
  { value: "Sindh", label: "Sindh" },
  { value: "Khyber Pakhtunkhwa", label: "Khyber Pakhtunkhwa" },
  { value: "Balochistan", label: "Balochistan" },
  { value: "Islamabad Capital Territory", label: "Islamabad Capital Territory" },
  { value: "Gilgit-Baltistan", label: "Gilgit-Baltistan" },
  { value: "Azad Jammu & Kashmir", label: "Azad Jammu & Kashmir" },
];

const OFFICIAL_FBR_LINKS = [
  {
    titleEn: "FBR IRIS 2.0 Portal (Get Token & POSID)",
    titleUr: "ایف بی آر آئرس پورٹل (ٹوکن اور POSID حاصل کریں)",
    url: "https://iris.fbr.gov.pk",
    descEn: "Log in with NTN/CNIC to register POS or Digital Invoicing & generate your Bearer Security Token.",
    descUr: "اپنے این ٹی این (NTN) سے لاگ ان کریں اور ڈیجیٹل انوائسنگ یا پی او ایس ٹوکن حاصل کریں۔",
  },
  {
    titleEn: "FBR Digital Invoicing & POS Technical Portal",
    titleUr: "ایف بی آر ڈیجیٹل انوائسنگ تکنیکی پورٹل",
    url: "https://www.fbr.gov.pk",
    descEn: "Official FBR documentation, SRO notifications, and PRAL integration guidelines.",
    descUr: "ایف بی آر کے سرکاری قوانین، ایس آر او (SRO) اور تکنیکی ہدایات۔",
  },
  {
    titleEn: "FBR e-Portal (NTN / STRN / ATL Verification)",
    titleUr: "ایف بی آر ای پورٹل (فائلر اور NTN کی تصدیق)",
    url: "https://e.fbr.gov.pk",
    descEn: "Verify Seller/Buyer NTN, STRN, and Active Taxpayer List (Filer/Non-Filer) status.",
    descUr: "خریدار یا سپلائر کے این ٹی این اور فائلر/نان فائلر اسٹیٹس کی تصدیق کریں۔",
  },
  {
    titleEn: "FBR Tax Asaan Verification",
    titleUr: "ایف بی آر ٹیکس آسان تصدیق",
    url: "https://taxasaan.fbr.gov.pk",
    descEn: "Verify fiscalized FBR Invoice Numbers and receipt QR codes online.",
    descUr: "رسید پر موجود ایف بی آر انوائس نمبر اور کیو آر کوڈ کی آن لائن تصدیق کریں۔",
  },
];

const COMMON_PHARMACY_HS_CODES = [
  {
    code: "3004.9099",
    nameEn: "General Medicaments / Finished Medicines (Tablets, Syrups, Drops)",
    nameUr: "عام ادویات (ٹیبلٹس، سیرپ، کیپسول، ڈراپس)",
    taxNote: "Exempt / Reduced (8th Schedule)",
  },
  {
    code: "3004.1000",
    nameEn: "Penicillins & Beta-Lactam Antibiotics",
    nameUr: "پینسلین اور اینٹی بائیوٹک ادویات",
    taxNote: "Exempt / 8th Schedule",
  },
  {
    code: "3004.2000",
    nameEn: "Other Systemic Antibiotics",
    nameUr: "دیگر اینٹی بائیوٹک ادویات",
    taxNote: "Exempt / 8th Schedule",
  },
  {
    code: "3004.3100",
    nameEn: "Insulin & Diabetic Injectable Preparations",
    nameUr: "انسولین اور شوگر کے انجیکشن",
    taxNote: "Exempt / 8th Schedule",
  },
  {
    code: "3005.9090",
    nameEn: "Surgical Dressings, Bandages, Gauze & First-Aid",
    nameUr: "سرجیکل پٹیاں، بینڈیج، کاٹن اور فرسٹ ایڈ",
    taxNote: "Exempt / Reduced Rate",
  },
  {
    code: "3002.2090",
    nameEn: "Vaccines & Immunological Products",
    nameUr: "ویکسین اور حفاظتی ٹیکے",
    taxNote: "Exempt",
  },
  {
    code: "1901.1000",
    nameEn: "Infant Formula, Baby Milk & Cereal Preparations",
    nameUr: "بچوں کا دودھ اور غذائی مصنوعات",
    taxNote: "Standard / Zero / Reduced Rate",
  },
  {
    code: "3304.9990",
    nameEn: "Cosmetics, Derma Creams, Lotions & Personal Care",
    nameUr: "کاسمیٹکس، لوشن، کریم اور جنرل آئٹمز",
    taxNote: "18% GST / 3rd Schedule MRP Tax",
  },
];

function SettingSwitch({ id, checked, onChange, disabled, labelOn, labelOff, isDark }) {
  return (
    <label
      htmlFor={id}
      className={`inline-flex items-center gap-2.5 select-none shrink-0 ${
        disabled ? "opacity-60 cursor-not-allowed" : "cursor-pointer"
      }`}
    >
      <input
        id={id}
        type="checkbox"
        checked={Boolean(checked)}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="sr-only peer"
      />
      <span
        aria-hidden="true"
        className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors duration-200 ${
          checked
            ? "bg-emerald-500"
            : isDark
            ? "bg-slate-700"
            : "bg-slate-300"
        }`}
      >
        <span
          className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-xs transition-transform duration-200 ${
            checked ? "translate-x-6" : "translate-x-1"
          }`}
        />
      </span>
      <span className="text-xs font-bold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">
        {checked ? labelOn : labelOff}
      </span>
    </label>
  );
}

export default function FbrSetting({
  form,
  setForm,
  handleChange,
  disableInputs,
}) {
  const { isDark, theme } = useTheme();
  const [showToken, setShowToken] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState(null);
  const [queueSummary, setQueueSummary] = useState(null);
  const [loadingQueue, setLoadingQueue] = useState(false);
  const [retryingQueue, setRetryingQueue] = useState(false);
  const [syncingInvoiceId, setSyncingInvoiceId] = useState(null);

  // Comprehensive Bilingual Guide Modal State
  const [guideModalOpen, setGuideModalOpen] = useState(false);
  const [guideLang, setGuideLang] = useState("both"); // 'both' | 'en' | 'ur'

  // Live Template Sample Preview State
  const [samplePaperType, setSamplePaperType] = useState("thermal"); // 'thermal' | 'a4'
  const [sampleTemplateName, setSampleTemplateName] = useState(
    form.thermal_template || "standard"
  );

  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        minHeight: "38px",
        borderRadius: 12,
        backgroundColor: isDark ? "rgba(30, 41, 59, 0.85)" : "#ffffff",
        borderColor: state.isFocused
          ? theme?.primary_color || "#3b82f6"
          : isDark
          ? "rgba(71, 85, 105, 0.7)"
          : "rgba(209, 213, 219, 0.8)",
        boxShadow: state.isFocused
          ? `0 0 0 2px ${theme?.primary_color || "#3b82f6"}33`
          : "none",
        fontSize: "13px",
        color: isDark ? "#f8fafc" : "#1e293b",
        "&:hover": {
          borderColor: theme?.primary_color || "#3b82f6",
        },
      }),
      valueContainer: (base) => ({
        ...base,
        padding: "2px 10px",
      }),
      input: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#1e293b",
        margin: 0,
        padding: 0,
      }),
      singleValue: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#1e293b",
        fontSize: "13px",
      }),
      placeholder: (base) => ({
        ...base,
        color: isDark ? "#94a3b8" : "#64748b",
        fontSize: "13px",
      }),
      menuPortal: (base) => ({
        ...base,
        zIndex: 9999,
      }),
      menu: (base) => ({
        ...base,
        backgroundColor: isDark ? "#1e293b" : "#ffffff",
        borderRadius: 12,
        border: isDark
          ? "1px solid rgba(71, 85, 105, 0.7)"
          : "1px solid rgba(229, 231, 235, 1)",
        boxShadow:
          "0 10px 25px -5px rgba(0, 0, 0, 0.25), 0 8px 10px -6px rgba(0, 0, 0, 0.25)",
        overflow: "hidden",
        zIndex: 9999,
      }),
      option: (base, state) => ({
        ...base,
        minHeight: 36,
        backgroundColor: state.isSelected
          ? theme?.primary_color || "#3b82f6"
          : state.isFocused
          ? isDark
            ? "rgba(51, 65, 85, 0.8)"
            : "rgba(239, 246, 255, 1)"
          : "transparent",
        color: state.isSelected
          ? "#ffffff"
          : isDark
          ? "#f8fafc"
          : "#1e293b",
        fontSize: "13px",
        cursor: "pointer",
      }),
    }),
    [isDark, theme]
  );

  const fetchQueueSummary = useCallback(async () => {
    setLoadingQueue(true);
    try {
      const { data } = await axios.get("/api/fbr/status-summary");
      setQueueSummary(data);
    } catch {
      /* ignore */
    } finally {
      setLoadingQueue(false);
    }
  }, []);

  useEffect(() => {
    fetchQueueSummary();
  }, [fetchQueueSummary]);

  const handleTestConnection = async () => {
    setTesting(true);
    setTestResult(null);
    try {
      const { data } = await axios.post("/api/fbr/test-connection", {
        fbr_environment: form.fbr_environment,
        fbr_mode: form.fbr_mode,
        fbr_bearer_token: form.fbr_bearer_token || undefined,
        fbr_pos_id: form.fbr_pos_id,
        fbr_seller_ntn: form.fbr_seller_ntn,
        fbr_seller_strn: form.fbr_seller_strn,
        fbr_seller_province: form.fbr_seller_province,
        fbr_sandbox_url: form.fbr_sandbox_url,
        fbr_production_url: form.fbr_production_url,
        fbr_default_hs_code: form.fbr_default_hs_code,
        fbr_scenario_id: form.fbr_scenario_id,
      });
      setTestResult(data);
      toast.success(data.message || "FBR Gateway verified!");
    } catch (err) {
      const errData = err?.response?.data || {
        ok: false,
        status: "failed",
        message: err?.message || "Failed to connect to FBR Gateway.",
      };
      setTestResult(errData);
      toast.error(errData.message || "FBR connection test failed.");
    } finally {
      setTesting(false);
    }
  };

  const handleRetryQueue = async () => {
    setRetryingQueue(true);
    try {
      const { data } = await axios.post("/api/fbr/retry-queue");
      toast.success(data?.message || "Processed FBR queue");
      fetchQueueSummary();
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to retry FBR queue");
    } finally {
      setRetryingQueue(false);
    }
  };

  const handleSyncSingleInvoice = async (invoiceId) => {
    setSyncingInvoiceId(invoiceId);
    try {
      const { data } = await axios.post(`/api/fbr/sale-invoices/${invoiceId}/sync`);
      toast.success(data?.message || "Synced with FBR!");
      fetchQueueSummary();
    } catch (err) {
      toast.error(err?.response?.data?.message || "FBR sync failed");
      fetchQueueSummary();
    } finally {
      setSyncingInvoiceId(null);
    }
  };

  const fillSandboxDemoCredentials = () => {
    setForm((prev) => ({
      ...prev,
      fbr_environment: "sandbox",
      fbr_mode: "tier1_pos_cloud",
      fbr_pos_id: prev.fbr_pos_id || "966130",
      fbr_bearer_token: "1298b5eb-b252-3d97-8622-a4a69d5bf818",
      fbr_default_hs_code: prev.fbr_default_hs_code || "3004.9099",
    }));
    toast.success("Loaded official FBR POS Sandbox test credentials! Click Save Settings or Test Connection.");
  };

  const selectedModeOption =
    FBR_MODE_OPTIONS.find((o) => o.value === (form.fbr_mode || "digital_invoicing")) ||
    FBR_MODE_OPTIONS[0];
  const selectedEnvOption =
    FBR_ENVIRONMENT_OPTIONS.find((o) => o.value === (form.fbr_environment || "sandbox")) ||
    FBR_ENVIRONMENT_OPTIONS[0];
  const selectedSyncOption =
    FBR_SYNC_BEHAVIOR_OPTIONS.find(
      (o) => o.value === (form.fbr_auto_sync !== false ? "auto" : "manual")
    ) || FBR_SYNC_BEHAVIOR_OPTIONS[0];
  const selectedProvinceOption =
    PROVINCE_OPTIONS.find((o) => o.value === (form.fbr_seller_province || "Punjab")) ||
    PROVINCE_OPTIONS[0];

  const showEn = guideLang === "both" || guideLang === "en";
  const showUr = guideLang === "both" || guideLang === "ur";

  return (
    <div className="space-y-5">
      {/* Step-by-Step Onboarding Guide Banner + Popup Trigger */}
      <div
        className={`p-4 sm:p-5 rounded-2xl border ${
          isDark
            ? "bg-emerald-950/30 border-emerald-800/60 text-emerald-100"
            : "bg-emerald-50/80 border-emerald-200 text-emerald-950"
        }`}
      >
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div className="flex items-start gap-3">
            <div className="p-2.5 rounded-xl bg-emerald-500/20 text-emerald-600 dark:text-emerald-300 shrink-0">
              <ShieldCheckIcon className="w-6 h-6" />
            </div>
            <div className="space-y-1 text-xs">
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="text-sm font-bold">
                  Attach Your Store to Pakistan FBR (Bring-Your-Own FBR Key)
                </h3>
                <span
                  dir="rtl"
                  className="px-2 py-0.5 rounded-md text-[11px] font-bold bg-emerald-500/20 text-emerald-700 dark:text-emerald-300"
                >
                  ایف بی آر ڈیجیٹل انوائسنگ اور پی او ایس رہنمائی
                </span>
              </div>
              <p className="opacity-90 leading-relaxed">
                Connect directly to FBR PRAL Digital Invoicing (<code>gw.fbr.gov.pk</code>) or Tier-1 Retail POS (<code>esp.fbr.gov.pk</code> / Local IMS) using your own FBR IRIS Bearer Token and POS ID.
              </p>
              <div className="flex flex-wrap items-center gap-3 pt-1">
                <a
                  href="https://iris.fbr.gov.pk"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 font-bold underline text-emerald-700 dark:text-emerald-300 hover:opacity-80"
                >
                  <span>FBR IRIS Portal (iris.fbr.gov.pk)</span>
                  <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                </a>
                <span>•</span>
                <a
                  href="https://e.fbr.gov.pk"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 font-bold underline text-emerald-700 dark:text-emerald-300 hover:opacity-80"
                >
                  <span>FBR e-Portal (e.fbr.gov.pk)</span>
                  <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                </a>
              </div>
            </div>
          </div>

          <button
            type="button"
            onClick={() => setGuideModalOpen(true)}
            className="px-4 py-2.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold flex items-center justify-center gap-2 shadow-sm shrink-0 cursor-pointer transition-colors"
          >
            <BookOpenIcon className="w-4 h-4 shrink-0" />
            <div className="text-left">
              <div>Complete FBR Token Guide</div>
              <div dir="rtl" className="text-[10px] font-normal opacity-95">
                مکمل رہنمائی (English / اردو)
              </div>
            </div>
          </button>
        </div>
      </div>

      {/* Master Enable & Protocol Mode Card */}
      <div className="settings-panel">
        <div className="settings-body space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-4 pb-3 border-b border-gray-200 dark:border-slate-700">
            <div>
              <h3 className="text-sm font-bold text-gray-900 dark:text-slate-100">
                1. FBR Fiscalization Master Switch &amp; Mode
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400">
                When enabled, Sale Invoices generate an official FBR Invoice Number and print a scannable Tax Asaan QR Code on receipts.
              </p>
            </div>
            <SettingSwitch
              id="fbr-enabled-switch"
              checked={Boolean(form.fbr_enabled)}
              disabled={disableInputs}
              onChange={(checked) => setForm((s) => ({ ...s, fbr_enabled: checked }))}
              labelOn="FBR Integration Enabled"
              labelOff="FBR Disabled"
              isDark={isDark}
            />
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                Integration Protocol Mode
              </label>
              <Select
                options={FBR_MODE_OPTIONS}
                value={selectedModeOption}
                isDisabled={disableInputs}
                onChange={(opt) =>
                  setForm((s) => ({ ...s, fbr_mode: opt?.value || "digital_invoicing" }))
                }
                styles={selectStyles}
                isSearchable={false}
                classNamePrefix="rs"
                menuPortalTarget={typeof document !== "undefined" ? document.body : null}
              />
            </div>

            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                FBR Environment
              </label>
              <Select
                options={FBR_ENVIRONMENT_OPTIONS}
                value={selectedEnvOption}
                isDisabled={disableInputs}
                onChange={(opt) =>
                  setForm((s) => ({ ...s, fbr_environment: opt?.value || "sandbox" }))
                }
                styles={selectStyles}
                isSearchable={false}
                classNamePrefix="rs"
                menuPortalTarget={typeof document !== "undefined" ? document.body : null}
              />
            </div>

            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                Invoice Sync Behavior
              </label>
              <Select
                options={FBR_SYNC_BEHAVIOR_OPTIONS}
                value={selectedSyncOption}
                isDisabled={disableInputs}
                onChange={(opt) =>
                  setForm((s) => ({
                    ...s,
                    fbr_auto_sync: (opt?.value || "auto") === "auto",
                  }))
                }
                styles={selectStyles}
                isSearchable={false}
                classNamePrefix="rs"
                menuPortalTarget={typeof document !== "undefined" ? document.body : null}
              />
            </div>
          </div>
        </div>
      </div>

      {/* FBR Key / Bearer Token & Store Tax Profile */}
      <div className="settings-panel">
        <div className="settings-body space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-2 pb-3 border-b border-gray-200 dark:border-slate-700">
            <div className="flex items-center gap-2">
              <KeyIcon className="w-5 h-5 text-blue-500" />
              <div>
                <h3 className="text-sm font-bold text-gray-900 dark:text-slate-100">
                  2. Your FBR Security Key (Bearer Token) &amp; Tax Profile
                </h3>
                <p className="text-xs text-gray-500 dark:text-slate-400">
                  Your token is stored securely on your server and sent in the <code>Authorization: Bearer</code> header to FBR.
                </p>
              </div>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => setGuideModalOpen(true)}
                className="px-3 py-1.5 rounded-xl text-xs font-semibold border border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 hover:bg-emerald-500/20 transition-colors cursor-pointer flex items-center gap-1.5"
              >
                <QuestionMarkCircleIcon className="w-4 h-4" />
                <span>How to Get FBR Key? (طریقہ کار)</span>
              </button>
              <button
                type="button"
                onClick={fillSandboxDemoCredentials}
                disabled={disableInputs}
                className="px-3 py-1.5 rounded-xl text-xs font-semibold border border-blue-500/40 bg-blue-500/10 text-blue-600 dark:text-blue-300 hover:bg-blue-500/20 transition-colors cursor-pointer"
              >
                Load FBR Sandbox Test Key
              </button>
            </div>
          </div>

          {/* Bearer Token Input */}
          <div className="space-y-1.5">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <label className="text-xs font-semibold text-gray-700 dark:text-slate-300">
                FBR Security / Bearer Token *
              </label>
              {form.has_fbr_bearer_token && (
                <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-emerald-600 dark:text-emerald-400">
                  <CheckCircleIcon className="w-4 h-4" />
                  Saved Token Active ({form.fbr_bearer_token_preview || "••••••••"})
                </span>
              )}
            </div>
            <div className="relative">
              <GlassInput
                type={showToken ? "text" : "password"}
                name="fbr_bearer_token"
                value={form.fbr_bearer_token || ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder={
                  form.has_fbr_bearer_token
                    ? "Leave blank to keep existing saved FBR token, or paste a new token to replace"
                    : "e.g. 07eabd29-fb34-3a2a-ab73-1ff4eb282aef"
                }
                className="w-full pr-10 font-mono text-xs h-10"
              />
              <button
                type="button"
                onClick={() => setShowToken((v) => !v)}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-slate-200 cursor-pointer"
                title={showToken ? "Hide token" : "Show token"}
              >
                {showToken ? (
                  <EyeSlashIcon className="w-4 h-4" />
                ) : (
                  <EyeIcon className="w-4 h-4" />
                )}
              </button>
            </div>
            {form.has_fbr_bearer_token && (
              <label className="inline-flex items-center gap-2 text-xs text-rose-600 dark:text-rose-400 cursor-pointer pt-1">
                <input
                  type="checkbox"
                  checked={Boolean(form.clear_fbr_bearer_token)}
                  onChange={(e) =>
                    setForm((s) => ({
                      ...s,
                      clear_fbr_bearer_token: e.target.checked,
                    }))
                  }
                  className="rounded text-rose-600"
                />
                <span>Clear saved FBR Bearer Token on next save</span>
              </label>
            )}
          </div>

          {/* Tax Identity Fields */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                Seller NTN / CNIC *
              </label>
              <GlassInput
                type="text"
                name="fbr_seller_ntn"
                value={form.fbr_seller_ntn || ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="e.g. 1234567-8"
                className="w-full font-mono text-xs h-10"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                Seller STRN (Sales Tax Reg #)
              </label>
              <GlassInput
                type="text"
                name="fbr_seller_strn"
                value={form.fbr_seller_strn || ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="e.g. 32-77-8761-001-45"
                className="w-full font-mono text-xs h-10"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                Seller Province
              </label>
              <Select
                options={PROVINCE_OPTIONS}
                value={selectedProvinceOption}
                isDisabled={disableInputs}
                onChange={(opt) =>
                  setForm((s) => ({ ...s, fbr_seller_province: opt?.value || "Punjab" }))
                }
                styles={selectStyles}
                isSearchable
                classNamePrefix="rs"
                menuPortalTarget={typeof document !== "undefined" ? document.body : null}
              />
            </div>

            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                FBR POS Registration ID (POSID)
              </label>
              <GlassInput
                type="text"
                name="fbr_pos_id"
                value={form.fbr_pos_id || ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="e.g. 110014 (Tier-1 POS)"
                className="w-full font-mono text-xs h-10"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                Default HS / PCT Code
              </label>
              <GlassInput
                type="text"
                name="fbr_default_hs_code"
                value={form.fbr_default_hs_code || "3004.9099"}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="3004.9099 (Medicaments)"
                className="w-full font-mono text-xs h-10"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                FBR Scenario ID
              </label>
              <GlassInput
                type="text"
                name="fbr_scenario_id"
                value={form.fbr_scenario_id || "SN001"}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="SN001"
                className="w-full font-mono text-xs h-10"
              />
            </div>

            <div className="sm:col-span-2">
              <div className="flex items-center justify-between mb-1.5">
                <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300">
                  FBR POS Service Fee (Rs.)
                </label>
                <SettingSwitch
                  id="fbr-pos-fee-switch"
                  checked={Boolean(form.fbr_pos_fee_enabled)}
                  disabled={disableInputs}
                  onChange={(checked) =>
                    setForm((s) => ({ ...s, fbr_pos_fee_enabled: checked }))
                  }
                  labelOn="Fee On"
                  labelOff="Fee Off"
                  isDark={isDark}
                />
              </div>
              <GlassInput
                type="number"
                step="0.01"
                min="0"
                name="fbr_pos_fee_amount"
                value={form.fbr_pos_fee_amount ?? 1}
                onChange={handleChange}
                disabled={disableInputs || !form.fbr_pos_fee_enabled}
                className="w-full font-mono text-xs h-10"
              />
            </div>
          </div>

          {/* Optional Custom Endpoint Overrides */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-2">
            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                Custom Sandbox Endpoint URL (Optional Override)
              </label>
              <GlassInput
                type="text"
                name="fbr_sandbox_url"
                value={form.fbr_sandbox_url || ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder={
                  form.fbr_mode === "tier1_pos_cloud"
                    ? "https://esp.fbr.gov.pk:8244/FBR/v1/api/Sandbox/PostData"
                    : form.fbr_mode === "tier1_pos_ims"
                    ? "http://127.0.0.1:8524/api/IMSFiscal/GetInvoiceNumberByModel"
                    : "https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata_sb"
                }
                className="w-full font-mono text-xs h-10"
              />
            </div>
            <div>
              <label className="block text-xs font-semibold mb-1.5 text-gray-700 dark:text-slate-300">
                Custom Production Endpoint URL (Optional Override)
              </label>
              <GlassInput
                type="text"
                name="fbr_production_url"
                value={form.fbr_production_url || ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder={
                  form.fbr_mode === "tier1_pos_cloud"
                    ? "https://esp.fbr.gov.pk:8244/FBR/v1/api/Live/PostData"
                    : form.fbr_mode === "tier1_pos_ims"
                    ? "http://127.0.0.1:8524/api/IMSFiscal/GetInvoiceNumberByModel"
                    : "https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata"
                }
                className="w-full font-mono text-xs h-10"
              />
            </div>
          </div>

          {/* Test Connection & Live Receipt Preview Bar */}
          <div className="pt-3 border-t border-gray-200 dark:border-slate-700 flex flex-wrap items-center justify-between gap-3">
            <div className="text-xs text-gray-500 dark:text-slate-400">
              Verify your FBR Bearer Token or preview how the official FBR POS Logo &amp; QR Code look on your Thermal &amp; A4 receipts.
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <a
                href={`/print/thermal-preview/${form.thermal_template || "standard"}?fbr_preview=1`}
                target="_blank"
                rel="noopener noreferrer"
                className="px-3.5 py-2 rounded-xl text-xs font-bold border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 hover:bg-gray-50 dark:hover:bg-slate-700 text-gray-800 dark:text-slate-100 flex items-center gap-1.5 shadow-2xs transition-colors"
              >
                <ArrowTopRightOnSquareIcon className="w-4 h-4 text-blue-500" />
                <span>Preview Thermal Receipt</span>
              </a>
              <a
                href={`/print/a4-preview/${form.a4_template || "standard"}?fbr_preview=1`}
                target="_blank"
                rel="noopener noreferrer"
                className="px-3.5 py-2 rounded-xl text-xs font-bold border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 hover:bg-gray-50 dark:hover:bg-slate-700 text-gray-800 dark:text-slate-100 flex items-center gap-1.5 shadow-2xs transition-colors"
              >
                <ArrowTopRightOnSquareIcon className="w-4 h-4 text-indigo-500" />
                <span>Preview A4 Invoice</span>
              </a>
              <button
                type="button"
                onClick={handleTestConnection}
                disabled={testing}
                className="px-4 py-2 rounded-xl text-xs font-bold bg-emerald-600 hover:bg-emerald-500 text-white flex items-center gap-2 shadow-xs transition-colors cursor-pointer disabled:opacity-50"
              >
                {testing ? (
                  <ArrowPathIcon className="w-4 h-4 animate-spin" />
                ) : (
                  <ShieldCheckIcon className="w-4 h-4" />
                )}
                <span>
                  {testing ? "Testing FBR Gateway…" : "Test FBR Connection & Verify Key"}
                </span>
              </button>
            </div>
          </div>

          {testResult && (
            <div
              className={`p-3.5 rounded-xl border text-xs space-y-1 ${
                testResult.ok
                  ? isDark
                    ? "bg-emerald-950/40 border-emerald-700 text-emerald-200"
                    : "bg-emerald-50 border-emerald-300 text-emerald-900"
                  : isDark
                  ? "bg-rose-950/40 border-rose-700 text-rose-200"
                  : "bg-rose-50 border-rose-300 text-rose-900"
              }`}
            >
              <div className="font-bold flex items-center gap-1.5">
                {testResult.ok ? (
                  <CheckCircleIcon className="w-4 h-4 text-emerald-500 shrink-0" />
                ) : (
                  <ExclamationTriangleIcon className="w-4 h-4 text-rose-500 shrink-0" />
                )}
                <span>{testResult.message}</span>
              </div>
              {testResult.endpoint && (
                <div className="font-mono text-[11px] opacity-80">
                  Endpoint: {testResult.endpoint}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Live Official FBR Stamp & Receipt Template Sample Preview */}
      <div className="settings-panel">
        <div className="settings-body space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 pb-3 border-b border-gray-200 dark:border-slate-700">
            <div>
              <h3 className="text-sm font-bold text-gray-900 dark:text-slate-100">
                3. Live Official FBR POS Stamp &amp; Receipt Template Sample
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400">
                Live preview of how the embedded FBR Pakistan Logo, FBR Invoice Number, NTN/STRN, and Tax Asaan QR Code print on your Thermal &amp; A4 templates.
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <div className="w-40">
                <Select
                  options={[
                    { value: "thermal", label: "Thermal Roll (POS)" },
                    { value: "a4", label: "A4 Full Page" },
                  ]}
                  value={
                    samplePaperType === "a4"
                      ? { value: "a4", label: "A4 Full Page" }
                      : { value: "thermal", label: "Thermal Roll (POS)" }
                  }
                  onChange={(opt) => setSamplePaperType(opt?.value || "thermal")}
                  styles={selectStyles}
                  isSearchable={false}
                  classNamePrefix="rs"
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                />
              </div>
              <div className="w-44">
                <Select
                  options={[
                    { value: "standard", label: "Standard Template" },
                    { value: "detailed", label: "Detailed Template" },
                    { value: "compact", label: "Compact Template" },
                    { value: "minimal", label: "Minimal Template" },
                    { value: "bold", label: "Bold Template" },
                    { value: "barcode", label: "Barcode Template" },
                  ]}
                  value={
                    [
                      { value: "standard", label: "Standard Template" },
                      { value: "detailed", label: "Detailed Template" },
                      { value: "compact", label: "Compact Template" },
                      { value: "minimal", label: "Minimal Template" },
                      { value: "bold", label: "Bold Template" },
                      { value: "barcode", label: "Barcode Template" },
                    ].find((o) => o.value === sampleTemplateName) || {
                      value: "standard",
                      label: "Standard Template",
                    }
                  }
                  onChange={(opt) => setSampleTemplateName(opt?.value || "standard")}
                  styles={selectStyles}
                  isSearchable={false}
                  classNamePrefix="rs"
                  menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                />
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-12 gap-5 items-start">
            {/* Left (5 cols): Live Interactive Official FBR Stamp Specimen */}
            <div className="lg:col-span-5 space-y-2">
              <div className="text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                Live Official FBR Stamp Specimen (Updates with Your Inputs)
              </div>
              <div className="p-4 rounded-2xl bg-slate-100 dark:bg-slate-800/80 border border-gray-200 dark:border-slate-700 flex justify-center">
                <div
                  style={{
                    width: "100%",
                    maxWidth: "330px",
                    padding: "9px 10px",
                    border: "1.5px solid #000000",
                    borderRadius: "6px",
                    background: "#ffffff",
                    color: "#000000",
                    fontFamily: "'Inter', 'Segoe UI', Arial, sans-serif",
                    boxShadow: "0 8px 20px rgba(0,0,0,0.12)",
                  }}
                >
                  {/* Top Official Header Banner */}
                  <div
                    style={{
                      background: "#000000",
                      color: "#ffffff",
                      padding: "4px 6px",
                      borderRadius: "3px",
                      textAlign: "center",
                      fontSize: "9.5px",
                      fontWeight: 800,
                      letterSpacing: "0.6px",
                      textTransform: "uppercase",
                    }}
                  >
                    {form.fbr_mode === "digital_invoicing"
                      ? "FBR DIGITAL INVOICING SYSTEM"
                      : "FBR POS INVOICING SYSTEM"}
                    {(form.fbr_environment || "sandbox") === "sandbox" ? " (SANDBOX)" : ""}
                  </div>

                  {/* FBR Invoice Number Box */}
                  <div
                    style={{
                      marginTop: "6px",
                      padding: "4px 6px",
                      border: "1px dashed #000000",
                      background: "#f8fafc",
                      textAlign: "center",
                    }}
                  >
                    <div
                      style={{
                        fontSize: "8px",
                        fontWeight: 700,
                        textTransform: "uppercase",
                        letterSpacing: "0.4px",
                        color: "#111111",
                      }}
                    >
                      FBR Invoice Number
                    </div>
                    <div
                      style={{
                        fontFamily: "'Courier New', Courier, monospace",
                        fontSize: "12px",
                        fontWeight: 900,
                        letterSpacing: "0.5px",
                        color: "#000000",
                        marginTop: "1px",
                      }}
                    >
                      {(form.fbr_pos_id || "966130") + "261116183001"}
                    </div>
                  </div>

                  {/* Seller Tax Metadata */}
                  <div
                    style={{
                      marginTop: "5px",
                      fontSize: "8.5px",
                      lineHeight: 1.4,
                      textAlign: "center",
                      fontWeight: 700,
                      color: "#000000",
                    }}
                  >
                    <div>
                      POS ID: {form.fbr_pos_id || "966130"} • NTN:{" "}
                      {form.fbr_seller_ntn || "1234567-8"}
                    </div>
                    <div>
                      STRN: {form.fbr_seller_strn || "32-77-8761-001-45"} • USIN: INV-00104
                    </div>
                    {form.fbr_pos_fee_enabled && (
                      <div style={{ fontSize: "8px", fontWeight: 600 }}>
                        FBR POS Service Fee: Rs.{" "}
                        {Number(form.fbr_pos_fee_amount ?? 1).toFixed(2)}
                      </div>
                    )}
                  </div>

                  {/* Side-by-Side Official FBR POS Emblem + 25x25 QR Code */}
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      gap: "8px",
                      marginTop: "6px",
                      padding: "2px 4px",
                    }}
                  >
                    {/* Left: Official FBR POS Crest SVG */}
                    <div style={{ flex: 1, textAlign: "center" }}>
                      <svg
                        width="124"
                        height="84"
                        viewBox="0 0 140 95"
                        xmlns="http://www.w3.org/2000/svg"
                        style={{ display: "inline-block", maxWidth: "100%", height: "auto" }}
                      >
                        <rect
                          x="2"
                          y="2"
                          width="136"
                          height="91"
                          rx="7"
                          fill="#ffffff"
                          stroke="#000000"
                          strokeWidth="2.5"
                        />
                        <circle
                          cx="32"
                          cy="34"
                          r="19"
                          fill="none"
                          stroke="#000000"
                          strokeWidth="2"
                        />
                        <circle
                          cx="32"
                          cy="34"
                          r="15.5"
                          fill="none"
                          stroke="#000000"
                          strokeWidth="0.9"
                          strokeDasharray="2,1.5"
                        />
                        <path
                          d="M35 23 A10 10 0 1 0 35 45 A8 8 0 1 1 35 23 Z"
                          fill="#000000"
                        />
                        <polygon
                          points="38,29 39.5,32.5 43,32.8 40.2,35 41,38.5 38,36.6 35,38.5 35.8,35 33,32.8 36.5,32.5"
                          fill="#000000"
                        />
                        <text
                          x="57"
                          y="35"
                          fontFamily="Arial Black, Impact, sans-serif"
                          fontSize="25"
                          fontWeight="900"
                          fill="#000000"
                          letterSpacing="1"
                        >
                          FBR
                        </text>
                        <text
                          x="58"
                          y="46"
                          fontFamily="Arial, sans-serif"
                          fontSize="7.5"
                          fontWeight="800"
                          fill="#000000"
                          letterSpacing="0.6"
                        >
                          PAKISTAN
                        </text>
                        <line
                          x1="58"
                          y1="49"
                          x2="128"
                          y2="49"
                          stroke="#000000"
                          strokeWidth="1.2"
                        />
                        <text
                          x="58"
                          y="57"
                          fontFamily="Arial, sans-serif"
                          fontSize="5.8"
                          fontWeight="700"
                          fill="#000000"
                        >
                          FEDERAL BOARD OF REVENUE
                        </text>
                        <rect x="8" y="64" width="124" height="22" rx="4" fill="#000000" />
                        <text
                          x="70"
                          y="79"
                          textAnchor="middle"
                          fontFamily="Arial, sans-serif"
                          fontSize="11.5"
                          fontWeight="900"
                          fill="#ffffff"
                          letterSpacing="1.8"
                        >
                          {form.fbr_mode === "digital_invoicing"
                            ? "DIGITAL INVOICE"
                            : "FBR POS"}
                        </text>
                      </svg>
                    </div>

                    {/* Right: 25x25 Version-2 QR Code SVG */}
                    <div style={{ textAlign: "center" }}>
                      <div
                        style={{
                          display: "inline-block",
                          padding: "4px",
                          background: "#ffffff",
                          border: "1.5px solid #000000",
                          borderRadius: "4px",
                        }}
                      >
                        <svg
                          width="76"
                          height="76"
                          viewBox="0 0 25 25"
                          xmlns="http://www.w3.org/2000/svg"
                          shapeRendering="crispEdges"
                        >
                          <rect width="25" height="25" fill="#ffffff" />
                          <rect x="0" y="0" width="7" height="7" fill="#000000" />
                          <rect x="1" y="1" width="5" height="5" fill="#ffffff" />
                          <rect x="2" y="2" width="3" height="3" fill="#000000" />
                          <rect x="18" y="0" width="7" height="7" fill="#000000" />
                          <rect x="19" y="1" width="5" height="5" fill="#ffffff" />
                          <rect x="20" y="2" width="3" height="3" fill="#000000" />
                          <rect x="0" y="18" width="7" height="7" fill="#000000" />
                          <rect x="1" y="19" width="5" height="5" fill="#ffffff" />
                          <rect x="2" y="20" width="3" height="3" fill="#000000" />
                          <rect x="16" y="16" width="5" height="5" fill="#000000" />
                          <rect x="17" y="17" width="3" height="3" fill="#ffffff" />
                          <rect x="18" y="18" width="1" height="1" fill="#000000" />
                          {Array.from({ length: 25 }).map((_, r) =>
                            Array.from({ length: 25 }).map((__, c) => {
                              const inTL = r < 8 && c < 8;
                              const inTR = r < 8 && c > 16;
                              const inBL = r > 16 && c < 8;
                              const inAl = r >= 16 && r <= 20 && c >= 16 && c <= 20;
                              if (inTL || inTR || inBL || inAl) return null;
                              const on = (r * 7 + c * 13 + (r ^ c)) % 2 === 0;
                              return on ? (
                                <rect
                                  key={`${r}-${c}`}
                                  x={c}
                                  y={r}
                                  width="1"
                                  height="1"
                                  fill="#000000"
                                />
                              ) : null;
                            })
                          )}
                        </svg>
                      </div>
                    </div>
                  </div>

                  {/* Bilingual Verification Footer */}
                  <div
                    style={{
                      marginTop: "5px",
                      paddingTop: "4px",
                      borderTop: "1px solid #000000",
                      textAlign: "center",
                      fontSize: "8px",
                      fontWeight: 700,
                      lineHeight: 1.4,
                    }}
                  >
                    <div>
                      Verify invoice via <strong>FBR Tax Asaan App</strong> or SMS to{" "}
                      <strong>9966</strong>
                    </div>
                    <div dir="rtl" style={{ fontSize: "8.5px", marginTop: "1px" }}>
                      ایف بی آر ٹیکس آسان ایپ کے ذریعے اس رسید کی تصدیق کریں
                    </div>
                  </div>
                </div>
              </div>
            </div>

            {/* Right (7 cols): Live Full Receipt / A4 Template Sample Iframe */}
            <div className="lg:col-span-7 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                  Full {samplePaperType === "a4" ? "A4 Invoice" : "Thermal Receipt"} Template Sample ({sampleTemplateName.toUpperCase()})
                </span>
                <a
                  href={`/print/${samplePaperType}-preview/${sampleTemplateName}?fbr_preview=1`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-xs font-bold text-blue-600 dark:text-blue-400 hover:underline inline-flex items-center gap-1"
                >
                  <span>Open Full Screen</span>
                  <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                </a>
              </div>
              <div className="rounded-2xl border border-gray-200 dark:border-slate-700 bg-slate-100 dark:bg-slate-900 overflow-hidden h-[430px]">
                <iframe
                  key={`${samplePaperType}-${sampleTemplateName}`}
                  title="FBR Receipt Sample Preview"
                  src={`/print/${samplePaperType}-preview/${sampleTemplateName}?fbr_preview=1`}
                  className="w-full h-full border-0 bg-white"
                />
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Live Fiscalization Queue & Offline Retry Monitor */}
      <div className="settings-panel">
        <div className="settings-body space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 pb-3 border-b border-gray-200 dark:border-slate-700">
            <div className="flex items-center gap-2">
              <ServerStackIcon className="w-5 h-5 text-indigo-500" />
              <div>
                <h3 className="text-sm font-bold text-gray-900 dark:text-slate-100">
                  4. FBR Fiscalization Queue &amp; Offline Sync Monitor
                </h3>
                <p className="text-xs text-gray-500 dark:text-slate-400">
                  If the internet drops during POS checkout, invoices are queued locally and can be synced in 1 click.
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={fetchQueueSummary}
                disabled={loadingQueue}
                className="px-3 py-1.5 rounded-xl border border-gray-300 dark:border-slate-700 text-xs font-semibold flex items-center gap-1.5 cursor-pointer"
              >
                <ArrowPathIcon
                  className={`w-3.5 h-3.5 ${loadingQueue ? "animate-spin" : ""}`}
                />
                <span>Refresh</span>
              </button>
              <button
                type="button"
                onClick={handleRetryQueue}
                disabled={
                  retryingQueue ||
                  !queueSummary ||
                  (queueSummary.counts?.pending || 0) +
                    (queueSummary.counts?.failed || 0) ===
                    0
                }
                className="px-3.5 py-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-bold flex items-center gap-1.5 disabled:opacity-40 cursor-pointer"
              >
                <ArrowPathIcon
                  className={`w-3.5 h-3.5 ${retryingQueue ? "animate-spin" : ""}`}
                />
                <span>Retry All Queued Invoices</span>
              </button>
            </div>
          </div>

          <div className="grid grid-cols-3 gap-3">
            <div
              className={`p-3 rounded-xl border ${
                isDark
                  ? "bg-emerald-950/30 border-emerald-800/50"
                  : "bg-emerald-50 border-emerald-200"
              }`}
            >
              <div className="text-[11px] font-bold uppercase text-emerald-600 dark:text-emerald-400">
                Fiscalized (Synced)
              </div>
              <div className="text-xl font-black font-mono mt-0.5 text-emerald-700 dark:text-emerald-300">
                {queueSummary?.counts?.synced ?? 0}
              </div>
            </div>
            <div
              className={`p-3 rounded-xl border ${
                isDark
                  ? "bg-amber-950/30 border-amber-800/50"
                  : "bg-amber-50 border-amber-200"
              }`}
            >
              <div className="text-[11px] font-bold uppercase text-amber-600 dark:text-amber-400">
                Pending Offline Queue
              </div>
              <div className="text-xl font-black font-mono mt-0.5 text-amber-700 dark:text-amber-300">
                {queueSummary?.counts?.pending ?? 0}
              </div>
            </div>
            <div
              className={`p-3 rounded-xl border ${
                isDark
                  ? "bg-rose-950/30 border-rose-800/50"
                  : "bg-rose-50 border-rose-200"
              }`}
            >
              <div className="text-[11px] font-bold uppercase text-rose-600 dark:text-rose-400">
                Failed / Needs Attention
              </div>
              <div className="text-xl font-black font-mono mt-0.5 text-rose-700 dark:text-rose-300">
                {queueSummary?.counts?.failed ?? 0}
              </div>
            </div>
          </div>

          {Array.isArray(queueSummary?.queue) && queueSummary.queue.length > 0 && (
            <div className="overflow-x-auto border rounded-xl border-gray-200 dark:border-slate-700">
              <table className="w-full text-left text-xs">
                <thead className="bg-gray-100 dark:bg-slate-800 text-[11px] uppercase font-bold">
                  <tr>
                    <th className="py-2 px-3">Invoice #</th>
                    <th className="py-2 px-3">Date</th>
                    <th className="py-2 px-3">Customer</th>
                    <th className="py-2 px-3 text-right">Total</th>
                    <th className="py-2 px-3">Status / Error</th>
                    <th className="py-2 px-3 text-right">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200 dark:divide-slate-700">
                  {queueSummary.queue.map((q) => (
                    <tr key={q.id}>
                      <td className="py-2 px-3 font-mono font-bold">
                        {q.posted_number}
                      </td>
                      <td className="py-2 px-3">{q.date}</td>
                      <td className="py-2 px-3">
                        {q.customer?.name || "Walk-in"}
                      </td>
                      <td className="py-2 px-3 text-right font-mono">
                        Rs. {Number(q.total || 0).toFixed(2)}
                      </td>
                      <td className="py-2 px-3 text-rose-600 dark:text-rose-400 max-w-xs truncate">
                        {q.fbr_error_message || q.fbr_sync_status}
                      </td>
                      <td className="py-2 px-3 text-right">
                        <button
                          type="button"
                          onClick={() => handleSyncSingleInvoice(q.id)}
                          disabled={syncingInvoiceId === q.id}
                          className="px-2.5 py-1 rounded-lg bg-blue-600 hover:bg-blue-500 text-white text-[11px] font-bold cursor-pointer"
                        >
                          {syncingInvoiceId === q.id ? "Syncing…" : "Sync Now"}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {/* ================================================================= */}
      {/* COMPREHENSIVE BILINGUAL (ENGLISH & URDU) FBR SETUP GUIDE MODAL    */}
      {/* ================================================================= */}
      {guideModalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-5 bg-black/70 backdrop-blur-xs overflow-y-auto"
          onClick={(e) => {
            if (e.target === e.currentTarget) setGuideModalOpen(false);
          }}
        >
          <div
            className={`w-full max-w-4xl max-h-[90dvh] flex flex-col rounded-2xl border shadow-2xl overflow-hidden ${
              isDark
                ? "bg-slate-900 border-slate-700 text-slate-100"
                : "bg-white border-gray-200 text-gray-900"
            }`}
          >
            {/* Modal Header */}
            <div
              className={`px-5 py-4 border-b flex flex-wrap items-center justify-between gap-3 shrink-0 ${
                isDark ? "bg-slate-800/80 border-slate-700" : "bg-emerald-50/70 border-emerald-200"
              }`}
            >
              <div className="flex items-center gap-3">
                <div className="p-2 rounded-xl bg-emerald-600 text-white">
                  <BookOpenIcon className="w-5 h-5" />
                </div>
                <div>
                  <h2 className="text-sm sm:text-base font-extrabold">
                    Pakistan FBR Digital Invoicing &amp; POS Token Guide
                  </h2>
                  <p dir="rtl" className="text-xs font-semibold text-emerald-700 dark:text-emerald-300">
                    ایف بی آر (FBR) سیکیورٹی ٹوکن، POS ID اور ڈیجیٹل انوائسنگ حاصل کرنے کا مکمل طریقہ کار
                  </p>
                </div>
              </div>

              <div className="flex items-center gap-2">
                {/* Language Switcher */}
                <div className="inline-flex rounded-xl border border-gray-300 dark:border-slate-600 p-0.5 text-xs font-bold">
                  {[
                    { id: "both", label: "Both (دونوں)" },
                    { id: "en", label: "English" },
                    { id: "ur", label: "اردو" },
                  ].map((tab) => (
                    <button
                      key={tab.id}
                      type="button"
                      onClick={() => setGuideLang(tab.id)}
                      className={`px-2.5 py-1 rounded-lg transition-colors cursor-pointer ${
                        guideLang === tab.id
                          ? "bg-emerald-600 text-white"
                          : "text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-800"
                      }`}
                    >
                      {tab.label}
                    </button>
                  ))}
                </div>

                <button
                  type="button"
                  onClick={() => setGuideModalOpen(false)}
                  className="p-1.5 rounded-xl hover:bg-gray-200/70 dark:hover:bg-slate-700 text-gray-500 dark:text-slate-400 cursor-pointer"
                >
                  <XMarkIcon className="w-5 h-5" />
                </button>
              </div>
            </div>

            {/* Modal Body */}
            <div className="flex-1 overflow-y-auto p-5 space-y-6 text-xs leading-relaxed">
              {/* 1. Official FBR Portal Links */}
              <div className="space-y-2.5">
                <h3 className="text-sm font-bold text-emerald-700 dark:text-emerald-400 flex items-center justify-between">
                  <span>1. Official FBR Portals &amp; Direct Links</span>
                  <span dir="rtl">۱۔ ایف بی آر کے سرکاری پورٹلز کے براہِ راست لنکس</span>
                </h3>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  {OFFICIAL_FBR_LINKS.map((item) => (
                    <a
                      key={item.url}
                      href={item.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className={`p-3.5 rounded-xl border transition-all flex flex-col justify-between gap-2 group ${
                        isDark
                          ? "bg-slate-800/70 border-slate-700 hover:border-emerald-500"
                          : "bg-gray-50/80 border-gray-200 hover:border-emerald-500 hover:bg-emerald-50/30"
                      }`}
                    >
                      <div className="space-y-1">
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-bold text-blue-600 dark:text-blue-400 group-hover:underline">
                            {item.titleEn}
                          </span>
                          <ArrowTopRightOnSquareIcon className="w-4 h-4 text-blue-500 shrink-0" />
                        </div>
                        <div dir="rtl" className="font-bold text-emerald-700 dark:text-emerald-300">
                          {item.titleUr}
                        </div>
                        {showEn && (
                          <p className="text-gray-600 dark:text-slate-300 pt-1">
                            {item.descEn}
                          </p>
                        )}
                        {showUr && (
                          <p dir="rtl" className="text-gray-600 dark:text-slate-300">
                            {item.descUr}
                          </p>
                        )}
                      </div>
                      <div className="font-mono text-[11px] text-blue-500 truncate">
                        {item.url}
                      </div>
                    </a>
                  ))}
                </div>
              </div>

              {/* 2. Step-by-Step Guide to Obtaining Your FBR Security Token & POSID */}
              <div className="space-y-3">
                <h3 className="text-sm font-bold text-emerald-700 dark:text-emerald-400 flex items-center justify-between">
                  <span>2. Step-by-Step: How to Get Your FBR Bearer Token &amp; POS ID</span>
                  <span dir="rtl">۲۔ ایف بی آر ٹوکن اور POS ID حاصل کرنے کے مراحل</span>
                </h3>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {/* Option A: Digital Invoicing */}
                  <div
                    className={`p-4 rounded-xl border space-y-2.5 ${
                      isDark ? "bg-slate-800/50 border-slate-700" : "bg-blue-50/40 border-blue-200"
                    }`}
                  >
                    <div className="font-bold text-sm text-blue-700 dark:text-blue-300">
                      Option A: FBR Digital Invoicing Cloud API (gw.fbr.gov.pk)
                    </div>
                    {showEn && (
                      <ol className="list-decimal list-inside space-y-1.5 text-gray-700 dark:text-slate-300">
                        <li>
                          Open{" "}
                          <a
                            href="https://iris.fbr.gov.pk"
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-blue-600 underline font-semibold"
                          >
                            https://iris.fbr.gov.pk
                          </a>{" "}
                          and sign in with your <strong>NTN / CNIC</strong> and Password.
                        </li>
                        <li>
                          From the top menu, open <strong>Digital Invoicing / E-Invoicing</strong> (or PRAL Taxpayer Portal).
                        </li>
                        <li>
                          Generate your <strong>Sandbox Security Token</strong> (for testing) and your <strong>Production Bearer Token</strong> (valid for 5 years, e.g., <code>07eabd29-fb34-3a2a-ab73-1ff4eb282aef</code>).
                        </li>
                        <li>
                          Note your assigned <strong>Scenario ID</strong> (typically <code>SN001</code>) and register/whitelist your internet IP address if required by PRAL.
                        </li>
                        <li>
                          In Karobar-App, select mode <strong>FBR Digital Invoicing Cloud API (gw.fbr.gov.pk)</strong> and paste your token in <strong>FBR Security / Bearer Token</strong>.
                        </li>
                      </ol>
                    )}
                    {showUr && (
                      <div
                        dir="rtl"
                        className="pt-2 border-t border-blue-200/60 dark:border-slate-700 space-y-1.5 text-gray-800 dark:text-slate-200"
                      >
                        <div className="font-bold text-blue-700 dark:text-blue-300">
                          آپشن الف: ایف بی آر ڈیجیٹل انوائسنگ کلاؤڈ اے پی آئی (فارمیسی، ہول سیلرز اور ڈسٹری بیوٹرز کے لیے)
                        </div>
                        <ol className="list-decimal list-inside space-y-1">
                          <li>
                            سب سے پہلے <a href="https://iris.fbr.gov.pk" target="_blank" rel="noopener noreferrer" className="text-blue-600 underline font-bold">iris.fbr.gov.pk</a> پر اپنے NTN اور پاس ورڈ سے لاگ ان کریں۔
                          </li>
                          <li>
                            مینیو میں سے <strong>Digital Invoicing</strong> سیکشن کھولیں۔
                          </li>
                          <li>
                            وہاں سے اپنا <strong>Sandbox Token</strong> (ٹیسٹنگ کے لیے) اور <strong>Production Bearer Token</strong> (لائو بلنگ کے لیے) کاپی کریں۔
                          </li>
                          <li>
                            اپنا <strong>Scenario ID</strong> (عام طور پر <code>SN001</code>) اور اپنا NTN/STRN کاروبار ایپ کے اس صفحے پر درج کریں۔
                          </li>
                        </ol>
                      </div>
                    )}
                  </div>

                  {/* Option B: Tier-1 Retail POS */}
                  <div
                    className={`p-4 rounded-xl border space-y-2.5 ${
                      isDark ? "bg-slate-800/50 border-slate-700" : "bg-purple-50/40 border-purple-200"
                    }`}
                  >
                    <div className="font-bold text-sm text-purple-700 dark:text-purple-300">
                      Option B: FBR Tier-1 Retail POS (esp.fbr.gov.pk / POSID)
                    </div>
                    {showEn && (
                      <ol className="list-decimal list-inside space-y-1.5 text-gray-700 dark:text-slate-300">
                        <li>
                          Log in to{" "}
                          <a
                            href="https://iris.fbr.gov.pk"
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-purple-600 underline font-semibold"
                          >
                            https://iris.fbr.gov.pk
                          </a>{" "}
                          and navigate to <strong>Registration &rarr; POS Registration (Tier-1 Retailer)</strong>.
                        </li>
                        <li>
                          Add your Store Branch and Counter Terminal details to generate a 6-digit <strong>POS Registration ID (POSID)</strong> (e.g., <code>110014</code>).
                        </li>
                        <li>
                          Copy the <strong>Bearer Security Token</strong> issued alongside your POSID (or install FBR's local IMS service on <code>localhost:8524</code>).
                        </li>
                        <li>
                          In Karobar-App, select <strong>FBR Tier-1 Retail POS Cloud API</strong>, enter your <strong>POSID</strong> and <strong>Bearer Token</strong>, and click <strong>Test FBR Connection</strong>.
                        </li>
                      </ol>
                    )}
                    {showUr && (
                      <div
                        dir="rtl"
                        className="pt-2 border-t border-purple-200/60 dark:border-slate-700 space-y-1.5 text-gray-800 dark:text-slate-200"
                      >
                        <div className="font-bold text-purple-700 dark:text-purple-300">
                          آپشن ب: ایف بی آر ٹیئر-1 ریٹیل پی او ایس (POSID اور ٹوکن)
                        </div>
                        <ol className="list-decimal list-inside space-y-1">
                          <li>
                            آئرس پورٹل (<a href="https://iris.fbr.gov.pk" target="_blank" rel="noopener noreferrer" className="text-purple-600 underline font-bold">iris.fbr.gov.pk</a>) میں <strong>Registration &rarr; POS Registration</strong> کھولیں۔
                          </li>
                          <li>
                            اپنی دکان کی برانچ اور کاؤنٹر رجسٹر کریں، جس پر ایف بی آر آپ کو 6 ہندسوں کا <strong>POSID</strong> اور <strong>Security Token</strong> جاری کرے گا۔
                          </li>
                          <li>
                            کاروبار ایپ میں <strong>FBR Tier-1 Retail POS Cloud API</strong> منتخب کریں، اپنا POSID اور ٹوکن درج کریں اور <strong>Test FBR Connection</strong> بٹن دبائیں۔
                          </li>
                        </ol>
                      </div>
                    )}
                  </div>
                </div>
              </div>

              {/* 3. Common Pharmacy & Retail HS / PCT Tariff Codes */}
              <div className="space-y-2.5">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h3 className="text-sm font-bold text-emerald-700 dark:text-emerald-400">
                    3. Pakistan Customs Tariff (HS / PCT) Codes for Pharmacies &amp; General Stores
                  </h3>
                  <span dir="rtl" className="text-xs font-bold text-emerald-700 dark:text-emerald-400">
                    ۳۔ فارمیسی اور جنرل اسٹور کے لیے عام HS / PCT کوڈز (ایک کلک سے منتخب کریں)
                  </span>
                </div>
                <div className="overflow-x-auto rounded-xl border border-gray-200 dark:border-slate-700">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-gray-100 dark:bg-slate-800 text-[11px] uppercase font-bold">
                      <tr>
                        <th className="py-2.5 px-3">HS / PCT Code</th>
                        <th className="py-2.5 px-3">Category Description (English / اردو)</th>
                        <th className="py-2.5 px-3">FBR Tax Schedule</th>
                        <th className="py-2.5 px-3 text-right">Use as Default</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-200 dark:divide-slate-700">
                      {COMMON_PHARMACY_HS_CODES.map((row) => (
                        <tr key={row.code}>
                          <td className="py-2 px-3 font-mono font-bold text-blue-600 dark:text-blue-400">
                            {row.code}
                          </td>
                          <td className="py-2 px-3">
                            {showEn && <div className="font-semibold">{row.nameEn}</div>}
                            {showUr && (
                              <div dir="rtl" className="text-gray-600 dark:text-slate-300">
                                {row.nameUr}
                              </div>
                            )}
                          </td>
                          <td className="py-2 px-3 font-medium text-emerald-700 dark:text-emerald-400">
                            {row.taxNote}
                          </td>
                          <td className="py-2 px-3 text-right">
                            <button
                              type="button"
                              onClick={() => {
                                setForm((s) => ({ ...s, fbr_default_hs_code: row.code }));
                                toast.success(`Default HS/PCT Code set to ${row.code}`);
                              }}
                              className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-[11px] font-bold cursor-pointer"
                            >
                              <ClipboardDocumentCheckIcon className="w-3.5 h-3.5" />
                              <span>Use {row.code}</span>
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>

              {/* 4. Important Notes on Offline Mode & PRAL Helpline */}
              <div
                className={`p-4 rounded-xl border space-y-2 ${
                  isDark ? "bg-amber-950/30 border-amber-800/60" : "bg-amber-50 border-amber-200"
                }`}
              >
                <div className="flex items-center justify-between font-bold text-amber-800 dark:text-amber-300">
                  <span>4. Offline Protection &amp; PRAL Helpline</span>
                  <span dir="rtl">۴۔ انٹرنیٹ بند ہونے کی صورت میں تحفظ اور ایف بی آر ہیلپ لائن</span>
                </div>
                {showEn && (
                  <p className="text-gray-700 dark:text-slate-300">
                    • <strong>Zero Checkout Downtime:</strong> If your internet or the FBR gateway is temporarily down, Karobar-App saves the invoice immediately and places it in the <strong>Pending Offline Queue</strong> so you can sync all queued bills in 1 click once internet returns.<br />
                    • <strong>FBR / PRAL Helpline:</strong> Call <strong>051-111-772-772</strong> or email <strong>helpline@fbr.gov.pk</strong> for IP whitelisting or token renewal support.
                  </p>
                )}
                {showUr && (
                  <p dir="rtl" className="text-gray-800 dark:text-slate-200 pt-1">
                    • <strong>آف لائن تحفظ:</strong> اگر بل بناتے وقت انٹرنیٹ منقطع ہو جائے تو کاروبار ایپ بل کو فوراً محفوظ کر لیتی ہے اور اسے <strong>Pending Queue</strong> میں ڈال دیتی ہے، جسے انٹرنیٹ بحال ہونے پر ایک کلک (<strong>Retry All Queued Invoices</strong>) سے ایف بی آر کو بھیجا جا سکتا ہے۔<br />
                    • <strong>ایف بی آر ہیلپ لائن:</strong> آئی پی وائٹ لسٹنگ یا ٹوکن کی مدد کے لیے ایف بی آر ہیلپ لائن <strong>051-111-772-772</strong> یا <strong>helpline@fbr.gov.pk</strong> پر رابطہ کریں۔
                  </p>
                )}
              </div>
            </div>

            {/* Modal Footer */}
            <div
              className={`px-5 py-3 border-t flex items-center justify-between shrink-0 ${
                isDark ? "bg-slate-800/80 border-slate-700" : "bg-gray-50 border-gray-200"
              }`}
            >
              <span className="text-[11px] text-gray-500 dark:text-slate-400">
                Tip: You can test connectivity anytime using the &ldquo;Load FBR Sandbox Test Key&rdquo; button.
              </span>
              <button
                type="button"
                onClick={() => setGuideModalOpen(false)}
                className="px-4 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold cursor-pointer"
              >
                Got It / بند کریں
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
