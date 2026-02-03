import { useRef } from "react";
import toast from "react-hot-toast";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassInput } from "@/components/glass.jsx";

registerPlugin(FilePondPluginImagePreview, FilePondPluginFileValidateType);

export default function GeneralSetting({ 
  form, 
  handleChange, 
  disableInputs, 
  files, 
  setFiles,
  storeNameRef,
  phoneRef,
  addressRef,
  licenseRef
}) {
  return (
    <>
      {/* ===== Identity + Contact ===== */}
      <GlassCard>
        <GlassSectionHeader title="Store Identity & Contact" />
        <GlassToolbar className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {/* Store Name */}
          <div className="w-full">
            <label className="block text-sm text-gray-700 mb-1 dark:text-gray-300">Store Name</label>
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
            <label className="block text-sm text-gray-700 mb-1 dark:text-gray-300">Phone Number</label>
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
            <label className="block text-sm text-gray-700 mb-1 dark:text-gray-300">Address</label>
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
            <label className="block text-sm text-gray-700 mb-1 dark:text-gray-300">Licence Number</label>
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
      <GlassCard className="relative z-10">
        <GlassSectionHeader title="Brand Logo" />
        <div className="px-4 pb-4">
          <div className="rounded-2xl bg-white/60 backdrop-blur-sm ring-1 ring-gray-200/60 p-3 shadow-sm dark:bg-slate-700/60 dark:ring-slate-600/60">
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
            <p className="text-xs text-gray-500 mt-2 dark:text-gray-400">PNG/JPG/WEBP, up to 2 MB.</p>
          </div>
        </div>
      </GlassCard>
    </>
  );
}

