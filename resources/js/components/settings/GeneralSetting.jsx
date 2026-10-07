// resources/js/components/settings/GeneralSetting.jsx
import toast from "react-hot-toast";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";
import { GlassInput } from "@/components/glass.jsx";
import { BuildingStorefrontIcon } from "@heroicons/react/24/solid";

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
  licenseRef,
}) {
  return (
    <div className="settings-block">
      <div className="settings-block-heading">
        <div className="flex items-center gap-3 min-w-0">
          <div className="settings-block-icon">
            <BuildingStorefrontIcon />
          </div>
          <div className="min-w-0">
            <h2 className="settings-block-title">Store Identity &amp; Contact</h2>
            <p className="settings-block-note">Basic information and logo</p>
          </div>
        </div>
      </div>

      <div className="settings-body">
        <div className="flex flex-col gap-5 md:flex-row md:items-start">
          {/* Logo */}
          <div className="w-full shrink-0 md:w-40">
            <label className="settings-label">Logo</label>
            <div className="mt-2">
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
                labelIdle='Drop logo or <span class="filepond--label-action">browse</span>'
                credits={false}
                stylePanelLayout="compact"
              />
            </div>
            <p className="settings-hint mt-2">PNG, JPG or WebP</p>
          </div>

          {/* Store details */}
          <div className="settings-fields flex-1">
            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-store-name">Store Name</label>
              <GlassInput
                id="setting-store-name"
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

            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-phone">Phone Number</label>
              <GlassInput
                id="setting-phone"
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

            <div className="settings-field settings-field-wide">
              <label className="settings-label" htmlFor="setting-address">Address</label>
              <GlassInput
                id="setting-address"
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

            <div className="settings-field settings-field-wide">
              <label className="settings-label" htmlFor="setting-licence">Licence Number</label>
              <GlassInput
                id="setting-licence"
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
      </div>
    </div>
  );
}
