import React, { useState, useEffect, useMemo, useRef } from "react";
import axios from "axios";
import Select from "react-select";
import toast from "react-hot-toast";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";
import { GlassInput } from "@/components/Glass.jsx";
import SettingSwitch from "@/components/settings/SettingSwitch.jsx";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import { printSlipUrl } from "@/utils/printHelper.js";
import {
  PrinterIcon,
  XMarkIcon,
  DocumentTextIcon,
  PhotoIcon,
  TrashIcon,
  CheckCircleIcon,
  ArrowTopRightOnSquareIcon,
} from "@heroicons/react/24/outline";

registerPlugin(FilePondPluginImagePreview, FilePondPluginFileValidateType);

const FREQUENCY_OPTIONS = [
  { value: "1 + 0 + 1", label: "1+0+1 • صبح اور شام (Morning & Night)" },
  { value: "1 + 1 + 1", label: "1+1+1 • صبح، دوپہر، شام (3 Times Daily)" },
  { value: "1 + 0 + 0", label: "1+0+0 • صرف صبح (Morning Only)" },
  { value: "0 + 0 + 1", label: "0+0+1 • رات سوتے وقت (Night at Bedtime)" },
  { value: "1 + 1 + 1 + 1", label: "1+1+1+1 • ہر 6 گھنٹے بعد (Every 6 Hours)" },
  { value: "SOS", label: "SOS • بوقتِ ضرورت (As Needed)" },
];

const TIMING_OPTIONS = [
  { value: "کھانے کے بعد (After Meal)", label: "کھانے کے بعد (After Meal)" },
  { value: "کھانے سے پہلے (Before Meal)", label: "کھانے سے پہلے (Before Meal)" },
  { value: "پانی کے ساتھ (With Water)", label: "پانی کے ساتھ (With Water)" },
  { value: "دودھ کے ساتھ (With Milk)", label: "دودھ کے ساتھ (With Milk)" },
];

const STICKER_SIZE_OPTIONS = [
  { value: "38x25", label: "38 × 25 mm", width_mm: 38, height_mm: 25 },
  { value: "50x25", label: "50 × 25 mm", width_mm: 50, height_mm: 25 },
  { value: "50x30", label: "50 × 30 mm", width_mm: 50, height_mm: 30 },
  { value: "60x40", label: "60 × 40 mm", width_mm: 60, height_mm: 40 },
  { value: "70x35", label: "70 × 35 mm", width_mm: 70, height_mm: 35 },
  { value: "72x35", label: "72 × 35 mm (72mm Roll)", width_mm: 72, height_mm: 35 },
  { value: "72x40", label: "72 × 40 mm (72mm Roll)", width_mm: 72, height_mm: 40 },
  { value: "80x50", label: "80 × 50 mm", width_mm: 80, height_mm: 50 },
];

function matchFrequencyOption(text) {
  if (!text) return FREQUENCY_OPTIONS[0];
  return (
    FREQUENCY_OPTIONS.find((o) => String(text).includes(o.value)) ||
    FREQUENCY_OPTIONS[0]
  );
}

function matchTimingOption(text) {
  if (!text) return TIMING_OPTIONS[0];
  return (
    TIMING_OPTIONS.find((o) => String(text).includes(o.value)) ||
    TIMING_OPTIONS[0]
  );
}

export function DosageStickersModal({ isOpen, onClose, invoice, onUpdated }) {
  const { dosageStickerConfig } = useSaleSystem();
  const [rows, setRows] = useState([]);
  const [saving, setSaving] = useState(false);
  const [selectedSize, setSelectedSize] = useState(null);
  const initializedKeyRef = useRef("");

  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        minHeight: "38px",
        borderRadius: 10,
        backgroundColor: "var(--color-surface, #ffffff)",
        borderColor: state.isFocused
          ? "var(--color-primary, #3b82f6)"
          : "var(--color-border, #cbd5e1)",
        boxShadow: "none",
        fontSize: "12px",
        cursor: "pointer",
      }),
      valueContainer: (base) => ({ ...base, padding: "2px 8px" }),
      singleValue: (base) => ({
        ...base,
        color: "var(--color-text-primary, #0f172a)",
      }),
      input: (base) => ({
        ...base,
        color: "var(--color-text-primary, #0f172a)",
      }),
      menuPortal: (base) => ({ ...base, zIndex: 9999999 }),
      menu: (base) => ({
        ...base,
        backgroundColor: "var(--color-surface, #ffffff)",
        borderRadius: 12,
        border: "1px solid var(--color-border, #cbd5e1)",
        fontSize: "12px",
        boxShadow: "0 12px 32px -8px rgba(15, 23, 42, 0.25)",
      }),
      option: (base, state) => ({
        ...base,
        backgroundColor: state.isSelected
          ? "color-mix(in srgb, var(--color-primary, #3b82f6) 20%, var(--color-surface, #ffffff))"
          : state.isFocused
          ? "color-mix(in srgb, var(--color-primary, #3b82f6) 10%, var(--color-surface, #ffffff))"
          : "var(--color-surface, #ffffff)",
        color: "var(--color-text-primary, #0f172a)",
        cursor: "pointer",
      }),
    }),
    []
  );

  useEffect(() => {
    if (!isOpen) {
      initializedKeyRef.current = "";
      return;
    }

    const cfgW = Number(dosageStickerConfig?.width_mm || 50);
    const cfgH = Number(dosageStickerConfig?.height_mm || 25);
    const matchedSize =
      STICKER_SIZE_OPTIONS.find((s) => s.width_mm === cfgW && s.height_mm === cfgH) || {
        value: `${cfgW}x${cfgH}`,
        label: `${cfgW} × ${cfgH} mm (Custom)`,
        width_mm: cfgW,
        height_mm: cfgH,
      };
    setSelectedSize((prev) => prev || matchedSize);

    if (!invoice?.items) return;

    const initKey = `${invoice.id}:${invoice.items.length}`;
    if (initializedKeyRef.current === initKey) return;
    initializedKeyRef.current = initKey;

    setRows(
      (invoice.items || []).map((it) => {
        const existing =
          it.dosage_instruction ||
          it.product?.default_dosage ||
          "1 + 0 + 1 • کھانے کے بعد (After Meal)";
        return {
          id: it.id,
          product_name: it.product?.name || "Medicine",
          quantity: it.quantity ?? it.unit_quantity ?? 1,
          freq: matchFrequencyOption(existing),
          timing: matchTimingOption(existing),
          dosage_instruction: existing,
          save_as_product_default: false,
        };
      })
    );
  }, [isOpen, invoice, dosageStickerConfig]);

  if (!isOpen || !invoice) return null;

  const activeW = selectedSize?.width_mm || dosageStickerConfig?.width_mm || 50;
  const activeH = selectedSize?.height_mm || dosageStickerConfig?.height_mm || 25;

  const sizeOptions = STICKER_SIZE_OPTIONS.some(
    (s) => s.width_mm === activeW && s.height_mm === activeH
  )
    ? STICKER_SIZE_OPTIONS
    : [
        {
          value: `${activeW}x${activeH}`,
          label: `${activeW} × ${activeH} mm (Settings)`,
          width_mm: activeW,
          height_mm: activeH,
        },
        ...STICKER_SIZE_OPTIONS,
      ];

  const updateRow = (idx, patch) => {
    setRows((prev) =>
      prev.map((r, i) => {
        if (i !== idx) return r;
        const next = { ...r, ...patch };
        if (patch.freq !== undefined || patch.timing !== undefined) {
          const f = (patch.freq ?? r.freq)?.value || "1 + 0 + 1";
          const t = (patch.timing ?? r.timing)?.value || "کھانے کے بعد (After Meal)";
          next.dosage_instruction = `${f} • ${t}`;
        }
        return next;
      })
    );
  };

  const handleSave = async (shouldPrint = false) => {
    try {
      setSaving(true);
      const { data } = await axios.put(`/api/sale-invoices/${invoice.id}/dosages`, {
        items: rows.map((r) => ({
          id: r.id,
          dosage_instruction: r.dosage_instruction,
          save_as_product_default: r.save_as_product_default,
        })),
      });
      if (data?.invoice && onUpdated) {
        onUpdated(data.invoice);
      }
      toast.success("Dosage instructions saved");
      if (shouldPrint) {
        const mode = dosageStickerConfig?.printer_mode || "receipt_roll";
        const targetPrinter = dosageStickerConfig?.preferred_printer || undefined;
        let url = `/sale-invoices/${invoice.id}/dosage-labels?auto_print=1&width_mm=${activeW}&height_mm=${activeH}&printer_mode=${mode}&silent_print=1`;
        let html = undefined;
        try {
          const { data: labelRes } = await axios.get(`/api/sale-invoices/${invoice.id}/dosage-labels-url`, {
            params: { width_mm: activeW, height_mm: activeH, printer_mode: mode, auto_print: 1 },
          });
          if (labelRes?.url) url = labelRes.url;
          if (labelRes?.html) html = labelRes.html;
        } catch {
          // Fallback to direct URL if signed endpoint unavailable
        }
        await printSlipUrl(url, {
          html,
          deviceName: targetPrinter,
          loadingMessage: `Printing ${activeW}×${activeH}mm dosage stickers...`,
          successMessage: "Dosage stickers sent directly to printer",
        });
      }
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to save dosage instructions");
    } finally {
      setSaving(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-100000 flex items-center justify-center p-4 bg-black/55 backdrop-blur-xs"
      onClick={(e) => {
        if (e.target === e.currentTarget && !saving) onClose?.();
      }}
    >
      <div
        onMouseDown={(e) => e.stopPropagation()}
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-3xl rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700/80 shadow-2xl overflow-hidden text-slate-900 dark:text-slate-100"
      >
        {/* Header */}
        <div className="flex items-center justify-between gap-3 px-5 py-4 border-b border-slate-200 dark:border-slate-800 bg-slate-50/80 dark:bg-slate-800/50 flex-wrap">
          <div>
            <h3 className="text-base font-bold">
              Patient Dosage &amp; Thermal Sticker Printer ({activeW}mm × {activeH}mm)
            </h3>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Invoice #{invoice.posted_number} • Select Urdu/English dosage presets or customize per medicine
            </p>
          </div>
          <div className="flex items-center gap-2">
            <div className="w-40">
              <Select
                options={sizeOptions}
                value={selectedSize}
                onChange={(opt) => opt && setSelectedSize(opt)}
                styles={selectStyles}
                classNamePrefix="rs"
                isSearchable={false}
                isClearable={false}
                menuPosition="fixed"
                menuPortalTarget={typeof document !== "undefined" ? document.body : null}
              />
            </div>
            <button
              type="button"
              onClick={onClose}
              className="w-8 h-8 rounded-lg grid place-items-center text-slate-500 hover:bg-slate-200/60 dark:hover:bg-slate-700/60 cursor-pointer"
            >
              <XMarkIcon className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Body */}
        <div className="p-5 space-y-4 max-h-[70dvh] overflow-y-auto">
          {rows.map((row, idx) => (
            <div
              key={row.id || idx}
              className="p-3.5 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-800/40 space-y-3"
            >
              <div className="flex items-center justify-between gap-2 flex-wrap">
                <div className="font-bold text-sm">
                  {idx + 1}. {row.product_name}{" "}
                  <span className="text-xs font-mono text-slate-500">(Qty: {row.quantity})</span>
                </div>
                <div className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
                  <span>Save as Product Default</span>
                  <SettingSwitch
                    checked={row.save_as_product_default}
                    onChange={(val) =>
                      updateRow(idx, { save_as_product_default: Boolean(val) })
                    }
                  />
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] font-semibold text-slate-500 mb-1">
                    Quick Frequency Preset
                  </label>
                  <Select
                    options={FREQUENCY_OPTIONS}
                    value={row.freq}
                    getOptionValue={(o) => o.value}
                    getOptionLabel={(o) => o.label}
                    onChange={(opt) => updateRow(idx, { freq: opt })}
                    styles={selectStyles}
                    classNamePrefix="rs"
                    isSearchable={false}
                    isClearable={false}
                    menuPosition="fixed"
                    menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  />
                </div>
                <div>
                  <label className="block text-[11px] font-semibold text-slate-500 mb-1">
                    Meal Timing Preset
                  </label>
                  <Select
                    options={TIMING_OPTIONS}
                    value={row.timing}
                    getOptionValue={(o) => o.value}
                    getOptionLabel={(o) => o.label}
                    onChange={(opt) => updateRow(idx, { timing: opt })}
                    styles={selectStyles}
                    classNamePrefix="rs"
                    isSearchable={false}
                    isClearable={false}
                    menuPosition="fixed"
                    menuPortalTarget={typeof document !== "undefined" ? document.body : null}
                  />
                </div>
              </div>

              <div>
                <label className="block text-[11px] font-semibold text-slate-500 mb-1">
                  Printed Sticker Text (Editable English / اردو)
                </label>
                <GlassInput
                  type="text"
                  value={row.dosage_instruction}
                  onChange={(e) =>
                    updateRow(idx, { dosage_instruction: e.target.value })
                  }
                  placeholder="1 + 0 + 1 • کھانے کے بعد (After Meal)"
                  className="w-full text-xs font-semibold"
                />
              </div>
            </div>
          ))}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between gap-3 px-5 py-3.5 border-t border-slate-200 dark:border-slate-800 bg-slate-50/80 dark:bg-slate-800/50 flex-wrap">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-xl text-xs font-semibold bg-slate-200/80 dark:bg-slate-800 text-slate-700 dark:text-slate-200 cursor-pointer"
          >
            Close
          </button>

          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={saving}
              onClick={() => handleSave(false)}
              className="px-4 py-2 rounded-xl text-xs font-bold bg-slate-700 hover:bg-slate-600 text-white cursor-pointer disabled:opacity-50"
            >
              {saving ? "Saving…" : "Save Dosages"}
            </button>
            <button
              type="button"
              disabled={saving}
              onClick={() => handleSave(true)}
              className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl text-xs font-bold bg-blue-600 hover:bg-blue-500 text-white cursor-pointer disabled:opacity-50"
            >
              <PrinterIcon className="w-4 h-4" />
              <span>Save &amp; Print {activeW}×{activeH}mm Stickers</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export function PrescriptionAttachmentModal({ isOpen, onClose, invoice, onUpdated }) {
  const [pondFiles, setPondFiles] = useState([]);
  const [prescriptionNo, setPrescriptionNo] = useState("");
  const [uploading, setUploading] = useState(false);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    if (isOpen && invoice) {
      setPondFiles([]);
      setPrescriptionNo(invoice.prescription_no || "");
    }
  }, [isOpen, invoice]);

  if (!isOpen || !invoice) return null;

  const existingUrl = invoice.prescription_image_url;
  const isPdf = String(invoice.prescription_image_path || "").toLowerCase().endsWith(".pdf");
  const selectedFile = pondFiles?.[0]?.file || null;

  const handleUpload = async (e) => {
    e.preventDefault();
    if (!selectedFile) {
      toast.error("Please select or drop an image or PDF file first.");
      return;
    }
    try {
      setUploading(true);
      const fd = new FormData();
      fd.append("prescription", selectedFile);
      if (prescriptionNo.trim()) {
        fd.append("prescription_no", prescriptionNo.trim());
      }
      const { data } = await axios.post(
        `/api/sale-invoices/${invoice.id}/prescription`,
        fd,
        { headers: { "Content-Type": "multipart/form-data" } }
      );
      if (data?.ok) {
        toast.success("Prescription attached successfully");
        onUpdated?.({
          ...invoice,
          prescription_image_path: data.prescription_image_path,
          prescription_image_url: data.prescription_image_url,
          prescription_no: data.prescription_no ?? invoice.prescription_no,
        });
        setPondFiles([]);
      }
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to upload prescription");
    } finally {
      setUploading(false);
    }
  };

  const handleDelete = async () => {
    if (!confirm("Remove the attached prescription file from this invoice?")) return;
    try {
      setDeleting(true);
      await axios.delete(`/api/sale-invoices/${invoice.id}/prescription`);
      toast.success("Prescription attachment removed");
      onUpdated?.({
        ...invoice,
        prescription_image_path: null,
        prescription_image_url: null,
      });
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to remove prescription");
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-100000 flex items-center justify-center p-4 bg-black/55 backdrop-blur-xs"
      onClick={(e) => {
        if (e.target === e.currentTarget && !uploading && !deleting) onClose?.();
      }}
    >
      <div
        onMouseDown={(e) => e.stopPropagation()}
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-lg rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700/80 shadow-2xl overflow-hidden text-slate-900 dark:text-slate-100"
      >
        {/* Header */}
        <div className="flex items-center justify-between gap-3 px-5 py-4 border-b border-slate-200 dark:border-slate-800 bg-slate-50/80 dark:bg-slate-800/50">
          <div className="flex items-center gap-2.5">
            <PhotoIcon className="w-5 h-5 text-blue-500" />
            <div>
              <h3 className="text-base font-bold">Doctor Prescription Attachment</h3>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Invoice #{invoice.posted_number} • Optional scan/photo for pharmacy records
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="w-8 h-8 rounded-lg grid place-items-center text-slate-500 hover:bg-slate-200/60 dark:hover:bg-slate-700/60 cursor-pointer"
          >
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="p-5 space-y-4 max-h-[75dvh] overflow-y-auto">
          {existingUrl && (
            <div className="p-3.5 rounded-xl border border-emerald-500/30 bg-emerald-500/5 space-y-3">
              <div className="flex items-center justify-between gap-2">
                <span className="inline-flex items-center gap-1.5 text-xs font-bold text-emerald-700 dark:text-emerald-300">
                  <CheckCircleIcon className="w-4 h-4" />
                  Attached Prescription on File
                </span>
                <div className="flex items-center gap-2">
                  <a
                    href={existingUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700"
                  >
                    <span>Open Full</span>
                    <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
                  </a>
                  <button
                    type="button"
                    disabled={deleting}
                    onClick={handleDelete}
                    className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-semibold bg-rose-500/15 text-rose-600 dark:text-rose-400 hover:bg-rose-500/25 cursor-pointer"
                  >
                    <TrashIcon className="w-3.5 h-3.5" />
                    <span>{deleting ? "Removing…" : "Remove"}</span>
                  </button>
                </div>
              </div>

              {!isPdf ? (
                <img
                  src={existingUrl}
                  alt="Attached Prescription"
                  className="w-full max-h-64 object-contain rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-950"
                />
              ) : (
                <div className="p-3 rounded-lg bg-white dark:bg-slate-800 text-xs flex items-center gap-2">
                  <DocumentTextIcon className="w-5 h-5 text-blue-500" />
                  <span>PDF Prescription Document attached. Click "Open Full" to view.</span>
                </div>
              )}
            </div>
          )}

          <form onSubmit={handleUpload} className="space-y-3">
            <div>
              <label className="block text-xs font-semibold mb-1">
                Prescription Reference No. (Optional)
              </label>
              <GlassInput
                type="text"
                value={prescriptionNo}
                onChange={(e) => setPrescriptionNo(e.target.value)}
                placeholder="e.g., RX-2026-00412"
                className="w-full text-xs"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold mb-1.5">
                Upload Prescription Photo / Scan (JPG, PNG, WEBP, PDF — Max 5MB)
              </label>
              <FilePond
                files={pondFiles}
                onupdatefiles={setPondFiles}
                allowMultiple={false}
                maxFiles={1}
                acceptedFileTypes={[
                  "image/jpeg",
                  "image/jpg",
                  "image/png",
                  "image/webp",
                  "application/pdf",
                ]}
                labelIdle='Drag &amp; Drop Prescription or <span class="filepond--label-action">Browse</span>'
                credits={false}
                disabled={uploading}
              />
            </div>

            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={onClose}
                className="px-4 py-2 rounded-xl text-xs font-semibold bg-slate-200/80 dark:bg-slate-800 text-slate-700 dark:text-slate-200 cursor-pointer"
              >
                Close
              </button>
              <button
                type="submit"
                disabled={uploading || !selectedFile}
                className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl text-xs font-bold bg-blue-600 hover:bg-blue-500 text-white cursor-pointer disabled:opacity-50"
              >
                <PhotoIcon className="w-4 h-4" />
                <span>{uploading ? "Uploading…" : existingUrl ? "Replace Prescription" : "Upload Prescription"}</span>
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  );
}
