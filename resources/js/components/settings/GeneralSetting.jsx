// resources/js/components/settings/GeneralSetting.jsx
import { useRef } from "react";
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

// Section configuration with color schemes - matching sidebar design
const SECTION_CONFIG = {
  core: {
    gradient: "from-blue-500 to-cyan-600",
    bgLight: "bg-blue-50",
    bgDark: "dark:bg-blue-900/20",
    borderColor: "border-blue-200 dark:border-blue-700",
    iconColor: "text-blue-600 dark:text-blue-400",
    ringColor: "ring-blue-300 dark:ring-blue-700",
  },
  management: {
    gradient: "from-violet-500 to-purple-600",
    bgLight: "bg-violet-50",
    bgDark: "dark:bg-violet-900/20",
    borderColor: "border-violet-200 dark:border-violet-700",
    iconColor: "text-violet-600 dark:text-violet-400",
    ringColor: "ring-violet-300 dark:ring-violet-700",
  },
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
  tintBlue,
  tintGlass,
  tintGreen
}) {
  const { isDark } = useTheme();

  // 🎨 Modern button palette (matching other settings components)
  const btnOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 transition-all duration-200";

  return (
    <div className="space-y-3">
      {/* ===== Identity + Contact ===== */}
      <GlassCard>
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.core.gradient} shadow-sm`}>
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
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.management.gradient} shadow-sm`}>
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
