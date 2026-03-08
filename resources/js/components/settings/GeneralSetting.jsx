// resources/js/components/settings/GeneralSetting.jsx
import { useRef, useMemo } from "react";
import toast from "react-hot-toast";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassInput, GlassBtn } from "@/components/glass.jsx";
import { BuildingStorefrontIcon } from "@heroicons/react/24/solid";

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

// Section configuration with color schemes - will use dynamic theme colors
const SECTION_CONFIG = {
  core: {
    key: 'primary',
  },
  management: {
    key: 'secondary',
  },
};

// Helper to get color value from theme
const getThemeColor = (theme, colorKey, variant = 'color') => {
  if (!theme) return '#3b82f6';
  const key = `${colorKey}_${variant}`;
  return theme[key] || '#3b82f6';
};

// Helper to generate section styles from theme
const getSectionStyles = (theme, colorKey) => {
  const baseColor = getThemeColor(theme, colorKey, 'color');
  const hoverColor = getThemeColor(theme, colorKey, 'hover');
  const lightColor = getThemeColor(theme, colorKey, 'light');
  
  return {
    gradient: `from-[${baseColor}] to-[${hoverColor}]`,
    bgLight: `bg-[${lightColor}]`,
    bgDark: `dark:bg-[${lightColor}]`,
    borderColor: `border-[${baseColor}]/30 dark:border-[${baseColor}]/30`,
    iconColor: `text-[${baseColor}] dark:text-[${baseColor}]`,
    ringColor: `ring-[${baseColor}]/30`,
  };
};

export default function GeneralSetting({ 
  form, 
  handleChange, 
  disableInputs, 
  files, 
  setFiles,
  storeNameRef,
  phoneRef,
  addressRef,
  licenseRef,
  themeColors,
  primaryTextColor,
  saleSystemRef,
  handleSaleSystemChange
}) {
  const { isDark } = useTheme();

  // Use passed themeColors if available, otherwise use default
  const colors = themeColors || {
    primary: '#3b82f6',
    primaryHover: '#2563eb',
    primaryLight: '#dbeafe',
    secondary: '#8b5cf6',
    secondaryHover: '#7c3aed',
    secondaryLight: '#ede9fe',
  };
  
  // Use passed text color if available, otherwise calculate
  const textColor = primaryTextColor || getContrastText(colors.primaryHover || colors.primary);
  const secondaryColor = getContrastText(colors.secondaryHover || colors.secondary);

  // 🎨 Modern button palette (matching other settings components)
  const btnOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 transition-all duration-200";

  return (
    <div className="space-y-3">
{/* ===== Identity + Contact + Logo ===== */}
      <GlassCard>
        {/* Header - Compact */}
        <div className="flex items-center justify-between px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div 
              className="p-1.5 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.primary}, ${colors.primaryHover})` }}
            >
              <BuildingStorefrontIcon className="w-4 h-4 text-white" />
            </div>
            <div>
              <h2 className="text-sm font-semibold text-gray-900 dark:text-white">Store Identity & Contact</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">Basic information and logo</p>
            </div>
          </div>
        </div>

        {/* Logo + Form Fields - Match ProductForm Style */}
        <div className="flex gap-3 p-3 items-start">
          {/* Logo - Left Side, Compact */}
          <div className="w-28 shrink-0">
            <label className={`block text-xs font-medium mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Logo</label>
            <div className="rounded-xl bg-white/60 dark:bg-slate-700/60 backdrop-blur-sm ring-1 ring-gray-200/60 dark:ring-slate-600/60 p-1">
              <FilePond
                files={files}
                onupdatefiles={(fl) => {
                  if (!disableInputs) {
                    setFiles(fl);
                  } else {
                    toast.error("No permission to update settings.");
                  }
                }}
                allowMultiple={false}
                acceptedFileTypes={['image/png', 'image/jpeg', 'image/jpg', 'image/webp']}
                disabled={disableInputs}
                labelIdle='<span class="text-xs dark:text-slate-300">Drop</span>'
                credits={false}
                stylePanelLayout="compact"
              />
            </div>
            <p className={`text-xs mt-1 ${isDark ? "text-slate-400" : "text-gray-500"}`}>PNG/JPG</p>
          </div>

          {/* Form Fields - Right Side */}
          <div className="flex-1 grid grid-cols-1 md:grid-cols-2 gap-2">
            {/* Store Name */}
            <div className="w-full">
              <label className={`block text-xs mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Store Name</label>
              <GlassInput
                ref={storeNameRef}
                type="text"
                name="store_name"
                value={form.store_name}
                onChange={handleChange}
                disabled={disableInputs}
                onKeyDown={(e) => {
                  if (e.key === "Enter") { e.preventDefault(); phoneRef.current?.focus(); }
                }}
                placeholder="e.g., My Pharmacy"
                className="w-full"
              />
            </div>

            {/* Phone */}
            <div className="w-full">
              <label className={`block text-xs mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Phone Number</label>
              <GlassInput
                ref={phoneRef}
                type="text"
                name="phone_number"
                value={form.phone_number}
                onChange={handleChange}
                disabled={disableInputs}
                onKeyDown={(e) => {
                  if (e.key === "Enter") { e.preventDefault(); addressRef.current?.focus(); }
                }}
                placeholder="+92 xx xxxxxxx"
                className="w-full"
              />
            </div>

            {/* Address */}
            <div className="md:col-span-2">
              <label className={`block text-xs mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Address</label>
              <GlassInput
                ref={addressRef}
                type="text"
                name="address"
                value={form.address}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="Street, City"
                className="w-full"
              />
            </div>

            {/* Licence Number */}
            <div className="w-full">
              <label className={`block text-xs mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Licence Number</label>
              <GlassInput
                ref={licenseRef}
                type="text"
                name="license_number"
                value={form.license_number}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="e.g., ABC-12345"
                className="w-full"
              />
            </div>
          </div>
        </div>
      </GlassCard>

{/* ===== Sale System Configuration ===== */}
      <GlassCard>
        {/* Header - Compact */}
        <div className="flex items-center justify-between px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-2">
            <div 
              className="p-1.5 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.tertiary || colors.primary}, ${colors.tertiaryHover || colors.primaryHover})` }}
            >
              <BuildingStorefrontIcon className="w-4 h-4 text-white" />
            </div>
            <div>
              <h2 className="text-sm font-semibold text-gray-900 dark:text-white">Sale System Configuration</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">Configure your sales system mode</p>
            </div>
          </div>
        </div>

        <GlassToolbar className="grid grid-cols-1 md:grid-cols-2 gap-2 p-3">
          <div className="md:col-span-2">
            <label className={`block text-xs mb-2 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Select Sale System Mode</label>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
              {/* Retail Only Option */}
              <label className={`
                relative flex flex-col p-3 rounded-lg border-2 cursor-pointer transition-all duration-200
                ${form.sale_system === 'retail' 
                  ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/20' 
                  : 'border-gray-200 dark:border-slate-600 hover:border-gray-300 dark:hover:border-slate-500'}
              `}>
                <input
                  type="radio"
                  name="sale_system"
                  value="retail"
                  checked={form.sale_system === 'retail'}
                  onChange={handleSaleSystemChange}
                  disabled={disableInputs}
                  className="sr-only"
                />
                <div className="flex items-center gap-2 mb-1">
                  <div className={`w-4 h-4 rounded-full border-2 flex items-center justify-center 
                    ${form.sale_system === 'retail' 
                      ? 'border-blue-500 bg-blue-500' 
                      : 'border-gray-300 dark:border-slate-500'}
                  `}>
                    {form.sale_system === 'retail' && (
                      <div className="w-1.5 h-1.5 rounded-full bg-white" />
                    )}
                  </div>
                  <span className={`font-medium text-sm ${isDark ? "text-slate-200" : "text-gray-800"}`}>
                    Retail Only
                  </span>
                </div>
                <p className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  Only retail sale invoices. Wholesale hidden.
                </p>
              </label>

              {/* Retail + Wholesale Option */}
              <label className={`
                relative flex flex-col p-3 rounded-lg border-2 cursor-pointer transition-all duration-200
                ${form.sale_system === 'retail_wholesale' 
                  ? 'border-purple-500 bg-purple-50 dark:bg-purple-900/20' 
                  : 'border-gray-200 dark:border-slate-600 hover:border-gray-300 dark:hover:border-slate-500'}
              `}>
                <input
                  type="radio"
                  name="sale_system"
                  value="retail_wholesale"
                  checked={form.sale_system === 'retail_wholesale'}
                  onChange={handleSaleSystemChange}
                  disabled={disableInputs}
                  className="sr-only"
                />
                <div className="flex items-center gap-2 mb-1">
                  <div className={`w-4 h-4 rounded-full border-2 flex items-center justify-center 
                    ${form.sale_system === 'retail_wholesale' 
                      ? 'border-purple-500 bg-purple-500' 
                      : 'border-gray-300 dark:border-slate-500'}
                  `}>
                    {form.sale_system === 'retail_wholesale' && (
                      <div className="w-1.5 h-1.5 rounded-full bg-white" />
                    )}
                  </div>
                  <span className={`font-medium text-sm ${isDark ? "text-slate-200" : "text-gray-800"}`}>
                    Retail + Wholesale
                  </span>
                </div>
                <p className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  Both retail and wholesale. Full functionality.
                </p>
              </label>
            </div>
          </div>
        </GlassToolbar>
      </GlassCard>
    </div>
  );
}
