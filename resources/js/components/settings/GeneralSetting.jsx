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
  primaryTextColor
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
      {/* ===== Identity + Contact ===== */}
      <GlassCard>
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.primary}, ${colors.primaryHover})` }}
            >
              <BuildingStorefrontIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-gray-900 dark:text-white">Store Identity & Contact</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">Basic information about your store</p>
            </div>
          </div>
        </div>

        <GlassToolbar className="grid grid-cols-1 md:grid-cols-2 gap-3 p-4">
          {/* Store Name */}
          <div className="w-full">
            <label className={`block text-sm mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Store Name</label>
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
            <label className={`block text-sm mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Phone Number</label>
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
            <label className={`block text-sm mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Address</label>
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
            <label className={`block text-sm mb-1 ${isDark ? "text-slate-300" : "text-gray-700"}`}>Licence Number</label>
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
        </GlassToolbar>
      </GlassCard>

      {/* ===== Logo (FilePond) ===== */}
      <GlassCard>
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.secondary}, ${colors.secondaryHover})` }}
            >
              <BuildingStorefrontIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-gray-900 dark:text-white">Brand Logo</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">Upload your store logo</p>
            </div>
          </div>
        </div>

        <div className="p-4">
          <div className={`rounded-xl bg-white/60 backdrop-blur-sm ring-1 p-4 shadow-sm ${
            isDark ? "ring-slate-600/60" : "ring-gray-200/60"
          }`}>
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
              labelIdle='Drag & Drop your logo or <span class="filepond--label-action">Browse</span>'
              credits={false}
            />
            <p className={`text-xs mt-2 ${isDark ? "text-slate-400" : "text-gray-500"}`}>PNG/JPG/WEBP, up to 2 MB.</p>
          </div>
        </div>
      </GlassCard>
    </div>
  );
}
