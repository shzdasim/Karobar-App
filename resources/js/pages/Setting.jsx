import { useEffect, useRef, useState, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";

// FilePond
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";

import { usePermissions } from "@/api/usePermissions.js"; // 🔒
import { useLicense } from "@/context/LicenseContext.jsx"; // 🔒 license context

// 🧊 glass primitives
import {
  GlassCard,
  GlassSectionHeader,
  GlassToolbar,
  GlassInput,
  GlassBtn,
} from "@/components/glass.jsx";

import {
  ArrowDownOnSquareIcon,
  CogIcon,
  PrinterIcon,
  DocumentTextIcon,
  ServerIcon,
  Bars3Icon,
  ViewColumnsIcon,
} from "@heroicons/react/24/solid";

// Setting Components
import GeneralSetting from "@/components/settings/GeneralSetting.jsx";
import NavigationSetting from "@/components/settings/NavigationSetting.jsx";
import PrinterSetting from "@/components/settings/PrinterSetting.jsx";
import LicenseSetting from "@/components/settings/LicenseSetting.jsx";
import BackupRestoreSetting from "@/components/settings/BackupRestoreSetting.jsx";

registerPlugin(FilePondPluginImagePreview, FilePondPluginFileValidateType);

export default function Setting() {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const license = useLicense();

  const [form, setForm] = useState({
    store_name: "",
    phone_number: "",
    address: "",
    license_number: "",
    note: "",
    printer_type: "thermal",
    thermal_template: "standard",
    navigation_style: "sidebar",
  });

  // FilePond files (supports remote preload)
  const [files, setFiles] = useState([]);
  
  // Tab state
  const [activeTab, setActiveTab] = useState("general");
  
  // Check for hash in URL on mount to open specific tab
  useEffect(() => {
    if (window.location.hash === "#license") {
      setActiveTab("license");
    }
  }, []);

  // License management state
  const [licenseStatus, setLicenseStatus] = useState(null);
  const [licenseLoading, setLicenseLoading] = useState(false);

  // Refs for focus & enter navigation
  const storeNameRef = useRef(null);
  const phoneRef = useRef(null);
  const addressRef = useRef(null);
  const licenseRef = useRef(null);
  const noteRef = useRef(null);
  const saveBtnRef = useRef(null);

  // 🔒 permissions
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function" ? canFor("settings") : {
        view:false, create:false, update:false, delete:false, import:false, export:false
      }),
    [canFor]
  );

  // tints (same palette as ledgers)
  const tintBlue   = "bg-blue-500/85 text-white ring-1 ring-white/20 shadow-[0_6px_20px_-6px_rgba(37,99,235,0.45)] hover:bg-blue-500/95";
  const tintGreen  = "bg-emerald-500/85 text-white ring-1 ring-white/20 shadow-[0_6px_20px_-6px_rgba(16,185,129,0.45)] hover:bg-emerald-500/95";
  const tintSlate  = "bg-slate-900/80 text-white ring-1 ring-white/15 shadow-[0_6px_20px_-6px_rgba(15,23,42,0.45)] hover:bg-slate-900/90";
  const tintGlass  = "bg-white/60 text-slate-700 ring-1 ring-white/30 hover:bg-white/75";

  useEffect(() => {
    if (permsLoading) return;
    if (!can.view) { setLoading(false); return; }
    fetchSettings();
    fetchLicenseStatus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [permsLoading, can.view]);

  useEffect(() => {
    if (!loading && can.view) {
      const t = setTimeout(() => storeNameRef.current?.focus(), 120);
      return () => clearTimeout(t);
    }
  }, [loading, can.view]);

  // Alt+S to save (only if can.update)
  useEffect(() => {
    const handleShortcut = (e) => {
      if (e.altKey && (e.key || "").toLowerCase() === "s") {
        e.preventDefault();
        if (can.update) handleSave();
        else toast.error("You don't have permission to update settings.");
      }
    };
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  }, [form, files, can.update]);

  const fetchSettings = async () => {
    try {
      setLoading(true);
      const { data } = await axios.get("/api/settings");
      setForm({
        store_name: data.store_name || "",
        phone_number: data.phone_number || "",
        address: data.address || "",
        license_number: data.license_number || "",
        note: data.note || "",
        printer_type: data.printer_type || "thermal",
        thermal_template: data.thermal_template || "standard",
        navigation_style: data.navigation_style || "sidebar",
      });

      // Preload existing logo into FilePond as remote file
      if (data.logo_url) {
        setFiles([{ source: data.logo_url, options: { type: "remote" } }]);
      } else {
        setFiles([]);
      }
    } catch (err) {
      const status = err?.response?.status;
      if (status === 403) toast.error("You don't have permission to view settings.");
      else toast.error("Failed to load settings");
    } finally {
      setLoading(false);
    }
  };

  const handleChange = (e) => {
    const { name, value } = e.target;
    setForm((s) => ({ ...s, [name]: value }));
  };

  const handleSave = async () => {
    if (!can.update) {
      toast.error("You don’t have permission to update settings.");
      return;
    }
    try {
      setSaving(true);
      const fd = new FormData();
      fd.append("store_name", form.store_name || "");
      fd.append("phone_number", form.phone_number || "");
      fd.append("address", form.address || "");
      fd.append("license_number", form.license_number || "");
      fd.append("note", form.note || "");
      fd.append("printer_type", form.printer_type || "a4");
      fd.append("thermal_template", form.thermal_template || "standard");
      fd.append("navigation_style", form.navigation_style || "sidebar");

      // If user selected a new file (files[0].file will exist)
      if (files.length > 0 && files[0].file) {
        fd.append("logo", files[0].file);
      }

      const { data } = await axios.post("/api/settings", fd, {
        headers: { "Content-Type": "multipart/form-data" },
      });

      toast.success("✅ Settings saved!");
      
      // Save navigation_style to localStorage for instant effect
      localStorage.setItem('navigation_style', form.navigation_style);
      
      // Dispatch custom event so DashboardLayout can react immediately
      window.dispatchEvent(new CustomEvent('settingsChanged', { detail: { navigation_style: form.navigation_style } }));
      
      // Refresh FilePond with the latest stored logo
      if (data.logo_url) {
        setFiles([{ source: data.logo_url, options: { type: "remote" } }]);
      } else {
        setFiles([]);
      }
    } catch (error) {
      if (error.response?.status === 422) {
        const errors = error.response.data.errors;
        Object.values(errors).forEach((messages) =>
          messages.forEach((msg) => toast.error(msg))
        );
      } else {
        const msg =
          error?.response?.data?.message ||
          error?.response?.data?.error ||
          "❌ Failed to save settings";
        toast.error(msg);
      }
    } finally {
      setSaving(false);
    }
  };

  // License management functions
  const fetchLicenseStatus = async () => {
    try {
      setLicenseLoading(true);
      const { data } = await axios.get("/api/license/status");
      setLicenseStatus(data);
    } catch (err) {
      toast.error("Failed to load license status");
      setLicenseStatus({ valid: false, reason: "Unable to fetch status" });
    } finally {
      setLicenseLoading(false);
    }
  };

  if (permsLoading) {
    return <div className="p-6"><div className="animate-pulse text-gray-500 dark:text-gray-400">Loading…</div></div>;
  }
  if (!can.view) {
    return <div className="p-6 text-sm text-gray-700 dark:text-gray-300">You don't have permission to view settings.</div>;
  }
  if (loading) {
    return <div className="p-6"><div className="animate-pulse text-gray-500 dark:text-gray-400">Loading settings…</div></div>;
  }

  const disableInputs = !can.update || saving;

  return (
    <div className="p-4 md:p-6 space-y-4 max-w-5xl mx-auto">
      {/* ===== Header / Save ===== */}
      <GlassCard className="relative z-30">
        <GlassSectionHeader
          title={<span className="inline-flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-blue-600" />
            <span>Application Settings</span>
          </span>}
          right={
            <GlassBtn
              ref={saveBtnRef}
              onClick={handleSave}
              disabled={!can.update || saving}
              className={`h-9 px-4 ${(!can.update || saving) ? tintGlass + " opacity-60 cursor-not-allowed" : tintGreen}`}
              title={can.update ? "Alt+S" : "You lack update permission"}
            >
              <span className="inline-flex items-center gap-2">
                <ArrowDownOnSquareIcon className="w-5 h-5" />
                {saving ? "Saving…" : (can.update ? "Save (Alt+S)" : "Save Disabled")}
              </span>
            </GlassBtn>
          }
        />

        {/* Top toolbar — optional quick info */}
        <GlassToolbar className="justify-between pt-1">
          <div className="text-xs text-gray-600 dark:text-gray-400">
            Configure store identity, default printer, and invoice footer.
          </div>
          <div className="text-[11px] text-gray-500 dark:text-gray-400">
            Changes apply across invoices and print templates.
          </div>
        </GlassToolbar>
      </GlassCard>

      {/* ===== Tab Navigation ===== */}
      <GlassCard className="!py-0 !px-0 overflow-hidden">
        <div className="flex border-b border-gray-200/60 bg-gray-50/50 dark:bg-slate-800/40 dark:border-slate-700/60">
          {/* General Tab */}
          <button
            onClick={() => setActiveTab("general")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "general"
                ? "border-blue-600 text-blue-700 bg-white/70 dark:bg-slate-800/70 dark:text-blue-400 dark:border-blue-400"
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
          >
            <CogIcon className="w-5 h-5" />
            <span>General</span>
          </button>
          
          {/* Navigation Settings Tab */}
          <button
            onClick={() => setActiveTab("navigation")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "navigation"
                ? "border-blue-600 text-blue-700 bg-white/70 dark:bg-slate-800/70 dark:text-blue-400 dark:border-blue-400"
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
          >
            <Bars3Icon className="w-5 h-5" />
            <span>Navigation</span>
          </button>

          {/* Printer Settings Tab */}
          <button
            onClick={() => setActiveTab("printer")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "printer"
                ? "border-blue-600 text-blue-700 bg-white/70 dark:bg-slate-800/70 dark:text-blue-400 dark:border-blue-400"
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
          >
            <PrinterIcon className="w-5 h-5" />
            <span>Printer Setting</span>
          </button>
          
          {/* Backup & Restore Tab */}
          <button
            onClick={() => setActiveTab("backup")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "backup"
                ? "border-blue-600 text-blue-700 bg-white/70 dark:bg-slate-800/70 dark:text-blue-400 dark:border-blue-400"
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
          >
            <ServerIcon className="w-5 h-5" />
            <span>Backup and restore</span>
          </button>

          {/* License Settings Tab */}
          <button
            onClick={() => setActiveTab("license")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "license"
                ? "border-blue-600 text-blue-700 bg-white/70 dark:bg-slate-800/70 dark:text-blue-400 dark:border-blue-400"
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
          >
            <DocumentTextIcon className="w-5 h-5" />
            <span>License</span>
          </button>
        </div>
      </GlassCard>

      {/* ===== Tab Content ===== */}
      {activeTab === "general" && (
        <GeneralSetting
          form={form}
          handleChange={handleChange}
          disableInputs={disableInputs}
          files={files}
          setFiles={setFiles}
          storeNameRef={storeNameRef}
          phoneRef={phoneRef}
          addressRef={addressRef}
          licenseRef={licenseRef}
        />
      )}

      {activeTab === "navigation" && (
        <NavigationSetting
          form={form}
          setForm={setForm}
          disableInputs={disableInputs}
          saving={saving}
          handleSave={handleSave}
          tintBlue={tintBlue}
          tintGlass={tintGlass}
          tintGreen={tintGreen}
        />
      )}

      {activeTab === "printer" && (
        <PrinterSetting
          form={form}
          handleChange={handleChange}
          disableInputs={disableInputs}
          saving={saving}
          handleSave={handleSave}
          tintBlue={tintBlue}
          tintGlass={tintGlass}
          tintGreen={tintGreen}
        />
      )}

      {activeTab === "license" && (
        <LicenseSetting
          licenseStatus={licenseStatus}
          licenseLoading={licenseLoading}
          fetchLicenseStatus={fetchLicenseStatus}
          tintBlue={tintBlue}
          tintGlass={tintGlass}
        />
      )}

      {activeTab === "backup" && (
        <BackupRestoreSetting
          canFor={canFor}
          tintBlue={tintBlue}
          tintGlass={tintGlass}
          tintGreen={tintGreen}
        />
      )}

      {/* ===== Bottom Save ===== */}
      <div className="flex justify-end">
        <GlassBtn
          ref={saveBtnRef}
          onClick={handleSave}
          disabled={!can.update || saving}
          className={`h-10 px-5 ${(!can.update || saving) ? tintGlass + " opacity-60 cursor-not-allowed" : tintGreen}`}
          title={can.update ? "Alt+S" : "You lack update permission"}
        >
          {saving ? "Saving…" : "Save (Alt+S)"}
        </GlassBtn>
      </div>

      {/* subtle helper styles (optional) */}
      <style>{`
        .filepond--panel-root { background: rgba(255,255,255,0.7); backdrop-filter: blur(6px); border: 1px solid rgba(226,232,240,0.7); }
        .filepond--drop-label { color: #334155; }
      `}</style>
    </div>
  );
}

