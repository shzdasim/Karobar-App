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
import { useSaleSystem } from "@/context/SaleSystemContext.jsx"; // Sale system context

import {
  ArrowDownOnSquareIcon,
  CogIcon,
  PrinterIcon,
  DocumentTextIcon,
  ServerIcon,
  Bars3Icon,
  ShoppingCartIcon,
} from "@heroicons/react/24/solid";

// Setting Components
import GeneralSetting from "@/components/settings/GeneralSetting.jsx";
import SaleSystemSetting from "@/components/settings/SaleSystemSetting.jsx";
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

// Settings sections shown as tabs. Ids and labels match the previous markup.
const SETTINGS_TABS = [
  { id: "general", label: "General", icon: CogIcon },
  { id: "sale", label: "Sale System", icon: ShoppingCartIcon },
  { id: "navigation", label: "Theme Setting", icon: Bars3Icon },
  { id: "printer", label: "Printer Setting", icon: PrinterIcon },
  { id: "backup", label: "Backup and restore", icon: ServerIcon },
  { id: "license", label: "License", icon: DocumentTextIcon },
];

export default function Setting() {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const license = useLicense();
  const { theme } = useTheme();
  const { refreshSaleSystem } = useSaleSystem();

  const [form, setForm] = useState({
    store_name: "",
    phone_number: "",
    address: "",
    license_number: "",
    note: "",
    printer_type: "thermal",
    thermal_template: "standard",
    a4_template: "standard",
    navigation_style: "sidebar",
    sale_system: "retail_wholesale",
    shop_type: "pharmacy",
  });

  // FilePond files (supports remote preload)
  const [files, setFiles] = useState([]);

  // Tab state
  const [activeTab, setActiveTab] = useState("general");

  // Check for hash in URL on mount to open specific tab
  useEffect(() => {
    if (window.location.hash === "#license") {
      setActiveTab("license");
    } else if (window.location.hash === "#sale") {
      setActiveTab("sale");
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
        a4_template: data.a4_template || "standard",
        navigation_style: data.navigation_style || "sidebar",
        sale_system: data.sale_system || "retail_wholesale",
        shop_type: data.shop_type || "pharmacy",
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

  const handleSaleSystemChange = (e) => {
    const { value } = e.target;
    setForm((s) => ({ ...s, sale_system: value }));
  };

  const handleShopTypeChange = (e) => {
    const { value } = e.target;
    setForm((s) => ({ ...s, shop_type: value }));
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
      fd.append("a4_template", form.a4_template || "standard");
      fd.append("navigation_style", form.navigation_style || "sidebar");
      fd.append("sale_system", form.sale_system || "retail_wholesale");
      fd.append("shop_type", form.shop_type || "pharmacy");

      // If user selected a new file (files[0].file will exist)
      if (files.length > 0 && files[0].file) {
        fd.append("logo", files[0].file);
      }

      const { data } = await axios.post("/api/settings", fd, {
        headers: { "Content-Type": "multipart/form-data" },
      });

      toast.success("✅ Settings saved!");

      // Dispatch custom event so other contexts (e.g. sale system) refresh immediately
      window.dispatchEvent(new CustomEvent('settingsChanged'));

      // Refresh sale system context to reflect changes immediately
      refreshSaleSystem();

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

  const saveDisabled = !can.update || saving;

  if (permsLoading) {
    return (
      <div className="settings-page">
        <p className="settings-hint">Loading…</p>
      </div>
    );
  }
  if (!can.view) {
    return (
      <div className="settings-page">
        <p className="settings-hint">You don't have permission to view settings.</p>
      </div>
    );
  }
  if (loading) {
    return (
      <div className="settings-page">
        <p className="settings-hint">Loading settings…</p>
      </div>
    );
  }

  const disableInputs = !can.update || saving;

  return (
    <div className="settings-page">
      {/* ===== Header / Save ===== */}
      <div className="settings-panel">
        <div className="settings-heading">
          <div className="flex items-center gap-4 min-w-0">
            <div className="settings-identity-icon">
              <CogIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="settings-title">Application Settings</h1>
              <p className="settings-subtitle">
                <CogIcon className="w-3.5 h-3.5 shrink-0" />
                Store identity, default printer, and invoice footer — applied across invoices and print templates.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2.5">
            <button
              ref={saveBtnRef}
              type="button"
              onClick={handleSave}
              disabled={saveDisabled}
              className="products-action products-action-primary"
              style={{ color: getContrastText(themeColors.primary) }}
              title={can.update ? "Save (Alt+S)" : "You lack update permission"}
            >
              <ArrowDownOnSquareIcon className="w-4 h-4" />
              <span>
                {saving ? "Saving…" : can.update ? "Save settings" : "Save disabled"}
              </span>
            </button>
          </div>
        </div>
      </div>

      {/* ===== Section tabs ===== */}
      <div className="settings-panel">
        <div className="settings-tabs" role="tablist" aria-label="Settings sections">
          {SETTINGS_TABS.map(({ id, label, icon: Icon }) => (
            <button
              key={id}
              type="button"
              role="tab"
              id={`settings-tab-${id}`}
              aria-selected={activeTab === id}
              aria-controls={`settings-panel-${id}`}
              onClick={() => setActiveTab(id)}
              className="settings-tab"
            >
              <Icon aria-hidden="true" />
              <span>{label}</span>
            </button>
          ))}
        </div>
      </div>

      {/* ===== Tab content ===== */}
      <div
        id={`settings-panel-${activeTab}`}
        role="tabpanel"
        aria-labelledby={`settings-tab-${activeTab}`}
        className="settings-page"
      >
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

        {activeTab === "sale" && (
          <SaleSystemSetting
            form={form}
            handleSaleSystemChange={handleSaleSystemChange}
            handleShopTypeChange={handleShopTypeChange}
            disableInputs={disableInputs}
            themeColors={themeColors}
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
      </div>

      {/* ===== Bottom save ===== */}
      <div className="settings-panel">
        <div className="settings-body">
          <div className="settings-actions settings-actions-between">
            <span className="settings-hint">
              Shortcut: <span className="people-shortcut">Alt+S</span> to save
            </span>
            <button
              type="button"
              onClick={handleSave}
              disabled={saveDisabled}
              className="products-action products-action-primary"
              style={{ color: getContrastText(themeColors.primary) }}
              title={can.update ? "Save (Alt+S)" : "You lack update permission"}
            >
              <ArrowDownOnSquareIcon className="w-4 h-4" />
              <span>{saving ? "Saving…" : "Save settings"}</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
