import React, { useEffect, useRef, useState } from "react";
import { GlassCard, GlassInput, GlassBtn } from "@/components/Glass.jsx";

export default function DoctorForm({
  initialData = null,
  onSubmit,
  onCancel,
  saving = false,
  isEdit = false,
  submitLabel = "Save Doctor",
}) {
  const [form, setForm] = useState({
    name: initialData?.name || "",
    pmdc_number: initialData?.pmdc_number || "",
    hospital_clinic: initialData?.hospital_clinic || "",
    specialization: initialData?.specialization || "",
    phone: initialData?.phone || "",
    address: initialData?.address || "",
    is_active: initialData?.is_active !== false,
  });

  const nameRef = useRef(null);
  const pmdcRef = useRef(null);
  const hospitalRef = useRef(null);
  const specRef = useRef(null);
  const phoneRef = useRef(null);
  const addressRef = useRef(null);
  const activeRef = useRef(null);
  const submitBtnRef = useRef(null);

  const fieldOrder = [
    nameRef,
    pmdcRef,
    hospitalRef,
    specRef,
    phoneRef,
    addressRef,
    activeRef,
    submitBtnRef,
  ];

  useEffect(() => {
    nameRef.current?.focus();
  }, []);

  useEffect(() => {
    if (initialData) {
      setForm({
        name: initialData.name || "",
        pmdc_number: initialData.pmdc_number || "",
        hospital_clinic: initialData.hospital_clinic || "",
        specialization: initialData.specialization || "",
        phone: initialData.phone || "",
        address: initialData.address || "",
        is_active: initialData.is_active !== false,
      });
    }
  }, [initialData]);

  // Keyboard shortcuts: Ctrl+S / Cmd+S to save, Esc to cancel/back
  useEffect(() => {
    const onKeyDown = (e) => {
      if (
        (e.ctrlKey || e.metaKey) &&
        !e.altKey &&
        !e.shiftKey &&
        (e.key || "").toLowerCase() === "s"
      ) {
        e.preventDefault();
        handleSubmit(e);
      } else if (e.key === "Escape" && !e.defaultPrevented) {
        if (onCancel) {
          e.preventDefault();
          onCancel();
        }
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  });

  const handleFieldKeyDown = (e, currentIdx) => {
    if (e.key === "Enter" && e.target?.type !== "submit" && e.target?.type !== "checkbox") {
      e.preventDefault();
      const next = fieldOrder[currentIdx + 1]?.current;
      next?.focus?.();
      next?.select?.();
    } else if (e.key === "ArrowDown") {
      const next = fieldOrder[currentIdx + 1]?.current;
      if (next) {
        e.preventDefault();
        next.focus?.();
        next.select?.();
      }
    } else if (e.key === "ArrowUp") {
      const prev = fieldOrder[currentIdx - 1]?.current;
      if (prev) {
        e.preventDefault();
        prev.focus?.();
        prev.select?.();
      }
    }
  };

  const handleSubmit = (e) => {
    if (e?.preventDefault) e.preventDefault();
    if (saving) return;

    onSubmit?.({
      name: (form.name || "").trim(),
      pmdc_number: (form.pmdc_number || "").trim() || null,
      hospital_clinic: (form.hospital_clinic || "").trim() || null,
      specialization: (form.specialization || "").trim() || null,
      phone: (form.phone || "").trim() || null,
      address: (form.address || "").trim() || null,
      is_active: Boolean(form.is_active),
    });
  };

  return (
    <GlassCard className="p-5">
      <form id="doctor-form" onSubmit={handleSubmit} className="space-y-6">
        <fieldset className="space-y-4">
          <legend className="text-sm font-bold text-slate-800 dark:text-slate-100 border-b border-slate-200/70 dark:border-slate-700/70 pb-2 w-full">
            Prescribing Doctor (RMP) Profile &amp; PM&amp;DC Credentials
          </legend>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            <div>
              <label
                htmlFor="doctor-name"
                className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1"
              >
                Doctor Full Name (RMP) *
              </label>
              <GlassInput
                id="doctor-name"
                name="name"
                ref={nameRef}
                type="text"
                autoComplete="name"
                placeholder="e.g. Dr. Tariq Mahmood"
                value={form.name}
                onChange={(e) => setForm((p) => ({ ...p, name: e.target.value }))}
                onKeyDown={(e) => handleFieldKeyDown(e, 0)}
                className="w-full h-[38px]"
                required
              />
            </div>

            <div>
              <label
                htmlFor="doctor-pmdc"
                className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1"
              >
                PM&amp;DC / PMC Registration #
              </label>
              <GlassInput
                id="doctor-pmdc"
                name="pmdc_number"
                ref={pmdcRef}
                type="text"
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                placeholder="e.g. PMDC-45821-P"
                value={form.pmdc_number}
                onChange={(e) =>
                  setForm((p) => ({ ...p, pmdc_number: e.target.value }))
                }
                onKeyDown={(e) => handleFieldKeyDown(e, 1)}
                className="w-full h-[38px] font-mono"
              />
            </div>

            <div>
              <label
                htmlFor="doctor-hospital"
                className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1"
              >
                Hospital / Clinic Name
              </label>
              <GlassInput
                id="doctor-hospital"
                name="hospital_clinic"
                ref={hospitalRef}
                type="text"
                placeholder="e.g. Mayo Hospital, Lahore"
                value={form.hospital_clinic}
                onChange={(e) =>
                  setForm((p) => ({ ...p, hospital_clinic: e.target.value }))
                }
                onKeyDown={(e) => handleFieldKeyDown(e, 2)}
                className="w-full h-[38px]"
              />
            </div>

            <div>
              <label
                htmlFor="doctor-specialization"
                className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1"
              >
                Specialization (Optional)
              </label>
              <GlassInput
                id="doctor-specialization"
                name="specialization"
                ref={specRef}
                type="text"
                placeholder="e.g. Consultant Physician / Psychiatrist"
                value={form.specialization}
                onChange={(e) =>
                  setForm((p) => ({ ...p, specialization: e.target.value }))
                }
                onKeyDown={(e) => handleFieldKeyDown(e, 3)}
                className="w-full h-[38px]"
              />
            </div>

            <div>
              <label
                htmlFor="doctor-phone"
                className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1"
              >
                Phone Number (Optional)
              </label>
              <GlassInput
                id="doctor-phone"
                name="phone"
                ref={phoneRef}
                type="tel"
                autoComplete="tel"
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                placeholder="e.g. 0300-1234567"
                value={form.phone}
                onChange={(e) =>
                  setForm((p) => ({ ...p, phone: e.target.value }))
                }
                onKeyDown={(e) => handleFieldKeyDown(e, 4)}
                className="w-full h-[38px] font-mono"
              />
            </div>

            <div>
              <label
                htmlFor="doctor-address"
                className="block text-xs font-semibold text-slate-700 dark:text-slate-200 mb-1"
              >
                Clinic / Chamber Address (Optional)
              </label>
              <GlassInput
                id="doctor-address"
                name="address"
                ref={addressRef}
                type="text"
                autoComplete="street-address"
                placeholder="Clinic / Hospital Address"
                value={form.address}
                onChange={(e) =>
                  setForm((p) => ({ ...p, address: e.target.value }))
                }
                onKeyDown={(e) => handleFieldKeyDown(e, 5)}
                className="w-full h-[38px]"
              />
            </div>

            <div className="md:col-span-2 lg:col-span-3 flex items-center gap-2 pt-1">
              <input
                id="doctor-is-active"
                name="is_active"
                ref={activeRef}
                type="checkbox"
                checked={Boolean(form.is_active)}
                onChange={(e) =>
                  setForm((p) => ({ ...p, is_active: e.target.checked }))
                }
                onKeyDown={(e) => handleFieldKeyDown(e, 6)}
                className="w-4 h-4 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500"
              />
              <label
                htmlFor="doctor-is-active"
                className="text-xs font-semibold text-slate-700 dark:text-slate-200 cursor-pointer"
              >
                Active in Sale Invoice Doctor Selector
              </label>
            </div>
          </div>
        </fieldset>

        <div className="pt-4 border-t border-slate-200/70 dark:border-slate-700/70 flex flex-wrap items-center justify-between gap-3">
          <div className="text-[11px] text-slate-500 dark:text-slate-400">
            Keyboard: <kbd className="px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-600 font-mono text-[10px]">Enter</kbd> /{" "}
            <kbd className="px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-600 font-mono text-[10px]">↑↓</kbd> Next/Prev field •{" "}
            <kbd className="px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-600 font-mono text-[10px]">Ctrl+S</kbd> Save •{" "}
            <kbd className="px-1.5 py-0.5 rounded border border-slate-300 dark:border-slate-600 font-mono text-[10px]">Esc</kbd> Back
          </div>

          <div className="flex items-center gap-2">
            {onCancel && (
              <button
                type="button"
                onClick={onCancel}
                className="products-action theme-btn-secondary"
              >
                Cancel
              </button>
            )}
            <GlassBtn
              ref={submitBtnRef}
              type="submit"
              disabled={saving}
              className="theme-btn-primary px-5 py-2 text-xs font-bold"
            >
              {saving
                ? isEdit
                  ? "Updating..."
                  : "Saving..."
                : `${submitLabel} (Ctrl+S)`}
            </GlassBtn>
          </div>
        </div>
      </form>
    </GlassCard>
  );
}
