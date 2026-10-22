// resources/js/components/settings/GeneralSetting.jsx
import React from "react";
import toast from "react-hot-toast";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";
import { GlassInput } from "@/components/Glass.jsx";
import { BuildingStorefrontIcon, BanknotesIcon } from "@heroicons/react/24/solid";

registerPlugin(FilePondPluginImagePreview, FilePondPluginFileValidateType);

const PRESET_CURRENCIES = [
  { code: "PKR", symbol: "Rs.", label: "Pakistani Rupee (PKR - Rs.)" },
  { code: "USD", symbol: "$", label: "US Dollar (USD - $)" },
  { code: "EUR", symbol: "€", label: "Euro (EUR - €)" },
  { code: "GBP", symbol: "£", label: "British Pound (GBP - £)" },
  { code: "SAR", symbol: "SR", label: "Saudi Riyal (SAR - SR)" },
  { code: "AED", symbol: "AED", label: "UAE Dirham (AED)" },
  { code: "INR", symbol: "₹", label: "Indian Rupee (INR - ₹)" },
  { code: "CAD", symbol: "CA$", label: "Canadian Dollar (CAD - CA$)" },
  { code: "AUD", symbol: "AU$", label: "Australian Dollar (AUD - AU$)" },
  { code: "CUSTOM", symbol: "", label: "Custom Currency" },
];

export default function GeneralSetting({
  form,
  setForm,
  handleChange,
  disableInputs,
  files,
  setFiles,
  storeNameRef,
  phoneRef,
  addressRef,
  licenseRef,
}) {
  const handleCurrencyPresetChange = (e) => {
    const selectedCode = e.target.value;
    if (selectedCode === "CUSTOM") {
      setForm?.((prev) => ({
        ...prev,
        currency_code: "",
      }));
      return;
    }
    const preset = PRESET_CURRENCIES.find((c) => c.code === selectedCode);
    if (preset) {
      setForm?.((prev) => ({
        ...prev,
        currency_code: preset.code,
        currency_symbol: preset.symbol,
      }));
    }
  };

  // Compute live preview formatting
  const previewAmount = 12500.5;
  const decimals = Number(form.currency_decimal_places ?? 2);
  const formattedNumber = previewAmount.toLocaleString(undefined, {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
  const symbol = form.currency_symbol || "";
  const position = form.currency_position || "before";
  const livePreview =
    position === "after" ? `${formattedNumber} ${symbol}` : `${symbol} ${formattedNumber}`;

  const currentPreset = PRESET_CURRENCIES.some((c) => c.code === form.currency_code)
    ? form.currency_code
    : "CUSTOM";

  return (
    <div className="space-y-6">
      {/* Block 1: Store Identity & Contact */}
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

      {/* Block 2: Currency & Financial Formatting */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon text-emerald-500 bg-emerald-500/10 border border-emerald-500/20">
              <BanknotesIcon className="w-5 h-5" />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Currency &amp; Price Formatting</h2>
              <p className="settings-block-note">Set store currency, symbol, placement, and decimal rounding</p>
            </div>
          </div>
        </div>

        <div className="settings-body space-y-5">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {/* Currency Preset Selector */}
            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-currency-preset">
                Currency Selection
              </label>
              <select
                id="setting-currency-preset"
                value={currentPreset}
                onChange={handleCurrencyPresetChange}
                disabled={disableInputs}
                className="w-full px-3 py-2 text-sm rounded-xl bg-slate-800/80 dark:bg-slate-800 border border-slate-700 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                {PRESET_CURRENCIES.map((c) => (
                  <option key={c.code} value={c.code}>
                    {c.label}
                  </option>
                ))}
              </select>
              <p className="settings-hint mt-1">Select a standard currency or configure a custom one.</p>
            </div>

            {/* Currency Symbol */}
            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-currency-symbol">
                Currency Symbol
              </label>
              <GlassInput
                id="setting-currency-symbol"
                type="text"
                name="currency_symbol"
                value={form.currency_symbol ?? ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="e.g. Rs., $, €, £"
                className="w-full"
              />
              <p className="settings-hint mt-1">Displayed on receipts, invoices, and reports.</p>
            </div>

            {/* Currency Code */}
            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-currency-code">
                ISO Code
              </label>
              <GlassInput
                id="setting-currency-code"
                type="text"
                name="currency_code"
                value={form.currency_code ?? ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder="e.g. PKR, USD, EUR"
                className="w-full uppercase"
              />
            </div>

            {/* Symbol Position */}
            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-currency-pos">
                Symbol Position
              </label>
              <select
                id="setting-currency-pos"
                name="currency_position"
                value={form.currency_position ?? "before"}
                onChange={handleChange}
                disabled={disableInputs}
                className="w-full px-3 py-2 text-sm rounded-xl bg-slate-800/80 dark:bg-slate-800 border border-slate-700 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option value="before">Before Amount (e.g. {form.currency_symbol || "Rs."} 1,250)</option>
                <option value="after">After Amount (e.g. 1,250 {form.currency_symbol || "Rs."})</option>
              </select>
            </div>

            {/* Decimal Places */}
            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-currency-decimals">
                Decimal Places
              </label>
              <select
                id="setting-currency-decimals"
                name="currency_decimal_places"
                value={Number(form.currency_decimal_places ?? 2)}
                onChange={handleChange}
                disabled={disableInputs}
                className="w-full px-3 py-2 text-sm rounded-xl bg-slate-800/80 dark:bg-slate-800 border border-slate-700 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option value={0}>0 Decimals (e.g. 12,500)</option>
                <option value={2}>2 Decimals (e.g. 12,500.50)</option>
              </select>
            </div>

            {/* Live Visual Preview Box */}
            <div className="p-4 rounded-xl bg-slate-800/50 border border-slate-700/60 flex flex-col justify-center">
              <span className="text-xs uppercase tracking-wider font-semibold text-slate-400">
                Invoice Live Preview
              </span>
              <div className="mt-1 flex items-baseline gap-2">
                <span className="text-2xl font-bold text-emerald-400 font-mono">
                  {livePreview}
                </span>
                <span className="text-xs text-slate-400">
                  (for {previewAmount.toLocaleString()} units)
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
