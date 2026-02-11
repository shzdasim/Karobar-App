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
import { useTheme } from "@/context/ThemeContext.jsx"; // 🎨 theme context

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
import ThemeSetting from "@/components/settings/ThemeSetting.jsx";
import PrinterSetting from "@/components/settings/PrinterSetting.jsx";
import LicenseSetting from "@/components/settings/LicenseSetting.jsx";
import BackupRestoreSetting from "@/components/settings/BackupRestoreSetting.jsx";

registerPlugin(FilePondPluginImagePreview, FilePondPluginFileValidateType);

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

export default function Setting() {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const license = useLicense();
  const { theme } = useTheme();

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

  // 🎨 Dynamic button styles using theme colors
  const tintPrimary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintSecondary = useMemo(() => `
    bg-gradient-to-br shadow-lg ring-1 ring-white/20
    hover:shadow-xl hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  const tintGlass = useMemo(() => `
    bg-white/80 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-white/10
    hover:bg-white dark:hover:bg-slate-600/80 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-200
  `.trim().replace(/\s+/g, ' '), []);

  // Memoize theme colors for performance
  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#3b82f6',
        primaryHover: '#2563eb',
        primaryLight: '#dbeafe',
        secondary: '#8b5cf6',
        secondaryHover: '#7c3aed',
        secondaryLight: '#ede9fe',
        emerald: '#10b981',
        emeraldHover: '#059669',
        emeraldLight: '#d1fae5',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
      emerald: theme.success_color || '#10b981',
      emeraldHover: '#059669',
      emeraldLight: '#d1fae5',
    };
  }, [theme]);

  // Calculate text colors based on background brightness
  const primaryTextColor = useMemo(() => 
    getContrastText(themeColors.primaryHover || themeColors.primary), 
    [themeColors.primary, themeColors.primaryHover]
  );
  
  const emeraldTextColor = useMemo(() => 
    getContrastText(themeColors.emeraldHover || themeColors.emerald), 
    [themeColors.emerald, themeColors.emeraldHover]
  );

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
            <div 
              className="w-2 h-2 rounded-full"
              style={{ background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})` }}
            />
            <span>Application Settings</span>
          </span>}
          right={
            <GlassBtn
              ref={saveBtnRef}
              onClick={handleSave}
              disabled={!can.update || saving}
              className="h-9 px-4"
              style={{
                background: `linear-gradient(to bottom right, ${themeColors.emerald}, ${themeColors.emeraldHover})`,
                color: emeraldTextColor,
                boxShadow: `0 6px 20px -6px ${themeColors.emerald}45`,
                opacity: (!can.update || saving) ? 0.6 : 1,
                cursor: (!can.update || saving) ? 'not-allowed' : 'pointer'
              }}
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
                ? ""
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
            style={activeTab === "general" ? {
              borderColor: themeColors.primary,
              color: themeColors.primary,
              backgroundColor: `${themeColors.primaryLight}70`,
            } : {}}
          >
            <CogIcon className="w-5 h-5" />
            <span>General</span>
          </button>
          
          {/* Theme Settings Tab */}
          <button
            onClick={() => setActiveTab("navigation")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "navigation"
                ? ""
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
            style={activeTab === "navigation" ? {
              borderColor: themeColors.primary,
              color: themeColors.primary,
              backgroundColor: `${themeColors.primaryLight}70`,
            } : {}}
          >
            <Bars3Icon className="w-5 h-5" />
            <span>Theme Setting</span>
          </button>

          {/* Printer Settings Tab */}
          <button
            onClick={() => setActiveTab("printer")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "printer"
                ? ""
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
            style={activeTab === "printer" ? {
              borderColor: themeColors.primary,
              color: themeColors.primary,
              backgroundColor: `${themeColors.primaryLight}70`,
            } : {}}
          >
            <PrinterIcon className="w-5 h-5" />
            <span>Printer Setting</span>
          </button>
          
          {/* Backup & Restore Tab */}
          <button
            onClick={() => setActiveTab("backup")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "backup"
                ? ""
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
            style={activeTab === "backup" ? {
              borderColor: themeColors.primary,
              color: themeColors.primary,
              backgroundColor: `${themeColors.primaryLight}70`,
            } : {}}
          >
            <ServerIcon className="w-5 h-5" />
            <span>Backup and restore</span>
          </button>

          {/* License Settings Tab */}
          <button
            onClick={() => setActiveTab("license")}
            className={`flex items-center gap-2 px-5 py-3 text-sm font-medium transition-all duration-200 border-b-2 ${
              activeTab === "license"
                ? ""
                : "border-transparent text-gray-600 hover:text-gray-800 hover:bg-white/50 dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-slate-700/50"
            }`}
            style={activeTab === "license" ? {
              borderColor: themeColors.primary,
              color: themeColors.primary,
              backgroundColor: `${themeColors.primaryLight}70`,
            } : {}}
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
          themeColors={themeColors}
          primaryTextColor={primaryTextColor}
        />
      )}

      {activeTab === "navigation" && (
        <ThemeSetting
          form={form}
          setForm={setForm}
          disableInputs={disableInputs}
        />
      )}

      {activeTab === "printer" && (
        <PrinterSetting
          form={form}
          handleChange={handleChange}
          disableInputs={disableInputs}
          saving={saving}
          handleSave={handleSave}
          themeColors={themeColors}
          emeraldTextColor={emeraldTextColor}
        />
      )}

      {activeTab === "license" && (
        <LicenseSetting
          licenseStatus={licenseStatus}
          licenseLoading={licenseLoading}
          fetchLicenseStatus={fetchLicenseStatus}
          themeColors={themeColors}
          primaryTextColor={primaryTextColor}
        />
      )}

      {activeTab === "backup" && (
        <BackupRestoreSetting
          themeColors={themeColors}
          primaryTextColor={primaryTextColor}
          emeraldTextColor={emeraldTextColor}
        />
      )}

      {/* ===== Bottom Save ===== */}
      <div className="flex justify-end">
        <GlassBtn
          ref={saveBtnRef}
          onClick={handleSave}
          disabled={!can.update || saving}
          className="h-10 px-5"
          style={{
            background: `linear-gradient(to bottom right, ${themeColors.emerald}, ${themeColors.emeraldHover})`,
            color: emeraldTextColor,
            boxShadow: `0 6px 20px -6px ${themeColors.emerald}45`,
            opacity: (!can.update || saving) ? 0.6 : 1,
            cursor: (!can.update || saving) ? 'not-allowed' : 'pointer'
          }}
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

