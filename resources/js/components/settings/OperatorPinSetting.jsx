import { useCallback, useEffect, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import SettingSwitch from "@/components/settings/SettingSwitch.jsx";
import {
  KeyIcon,
  ShieldCheckIcon,
  UserGroupIcon,
  SparklesIcon,
  EyeIcon,
  EyeSlashIcon,
  ExclamationTriangleIcon,
  CheckCircleIcon,
} from "@heroicons/react/24/solid";

const MODULE_OPTIONS = [
  {
    key: "sale_invoice",
    label: "Sale Invoices & Barcode POS",
    description: "Retail, Wholesale, and Barcode POS checkout saves & updates.",
  },
  {
    key: "purchase_invoice",
    label: "Purchase Invoices",
    description: "Supplier purchase invoice entries and edits.",
  },
  {
    key: "sale_return",
    label: "Sale Returns",
    description: "Customer sale return & refund transactions.",
  },
  {
    key: "purchase_return",
    label: "Purchase Returns",
    description: "Supplier stock return transactions.",
  },
];

export default function OperatorPinSetting({ form, setForm, disableInputs = false }) {
  const [directory, setDirectory] = useState({ users: [], employees: [] });
  const [loadingDir, setLoadingDir] = useState(false);
  const [pinDrafts, setPinDrafts] = useState({});
  const [savingKey, setSavingKey] = useState(null);
  const [revealedKeys, setRevealedKeys] = useState({});

  const requirePin = Boolean(form?.require_operator_pin);
  const autoSubmit = form?.operator_pin_auto_submit !== false;
  const activeModules = Array.isArray(form?.operator_pin_modules)
    ? form.operator_pin_modules
    : MODULE_OPTIONS.map((m) => m.key);

  const fetchDirectory = useCallback(async () => {
    setLoadingDir(true);
    try {
      const { data } = await axios.get("/api/operator-pins/directory");
      const users = Array.isArray(data?.users) ? data.users : [];
      const employees = Array.isArray(data?.employees) ? data.employees : [];
      setDirectory({ users, employees });

      const nextDrafts = {};
      users.forEach((u) => {
        nextDrafts[`user:${u.id}`] = u.pin_code || "";
      });
      employees.forEach((e) => {
        nextDrafts[`employee:${e.id}`] = e.pin_code || "";
      });
      setPinDrafts(nextDrafts);
    } catch (err) {
      console.error("Failed to load operator PIN directory:", err);
    } finally {
      setLoadingDir(false);
    }
  }, []);

  useEffect(() => {
    fetchDirectory();
  }, [fetchDirectory]);

  const toggleModule = (modKey) => {
    if (disableInputs) return;
    setForm((prev) => {
      const current = Array.isArray(prev?.operator_pin_modules)
        ? prev.operator_pin_modules
        : MODULE_OPTIONS.map((m) => m.key);
      const exists = current.includes(modKey);
      const next = exists ? current.filter((k) => k !== modKey) : [...current, modKey];
      return { ...prev, operator_pin_modules: next };
    });
  };

  const handleGenerate = async (type, id) => {
    const key = `${type}:${id}`;
    try {
      const { data } = await axios.get("/api/operator-pins/generate");
      if (data?.pin_code) {
        setPinDrafts((prev) => ({ ...prev, [key]: data.pin_code }));
        setRevealedKeys((prev) => ({ ...prev, [key]: true }));
      }
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to generate 4-digit code.");
    }
  };

  const handleAssign = async (type, id) => {
    const key = `${type}:${id}`;
    const raw = String(pinDrafts[key] ?? "").trim();
    if (raw !== "" && !/^\d{4}$/.test(raw)) {
      toast.error("Operator code must be exactly 4 numeric digits (0000-9999).");
      return;
    }

    setSavingKey(key);
    try {
      const { data } = await axios.put("/api/operator-pins/assign", {
        type,
        id,
        pin_code: raw === "" ? null : raw,
      });
      toast.success(data?.message || "4-digit code updated.");
      await fetchDirectory();
    } catch (err) {
      toast.error(
        err?.response?.data?.errors?.pin_code?.[0] ||
          err?.response?.data?.message ||
          "Failed to assign 4-digit code."
      );
    } finally {
      setSavingKey(null);
    }
  };

  const assignedCount =
    directory.users.filter((u) => Boolean(u.pin_code) && u.status === "active").length +
    directory.employees.filter((e) => !e.user_id && Boolean(e.pin_code) && e.status === "active").length;

  const renderStaffRow = (type, item) => {
    const key = `${type}:${item.id}`;
    const draft = pinDrafts[key] ?? "";
    const saved = item.pin_code || "";
    const isDirty = draft !== saved;
    const isRevealed = Boolean(revealedKeys[key]);
    const isInactive = item.status !== "active";

    return (
      <tr key={key} className="border-t border-gray-200/70 dark:border-slate-700/70">
        <td className="py-2.5 px-3">
          <div className="font-semibold text-xs text-gray-900 dark:text-white flex items-center gap-2">
            <span>{item.name}</span>
            {isInactive && (
              <span className="px-1.5 py-0.5 text-[10px] rounded bg-gray-200 dark:bg-slate-700 text-gray-600 dark:text-slate-300">
                {item.status}
              </span>
            )}
          </div>
          <div className="text-[11px] text-gray-500 dark:text-slate-400">
            {type === "user"
              ? `${item.email}${(item.roles || []).length ? ` · ${item.roles.join(", ")}` : ""}`
              : `${item.employee_code} · ${item.designation || "Staff"}${item.user_name ? ` (Linked to ${item.user_name})` : ""}`}
          </div>
        </td>
        <td className="py-2.5 px-3">
          {saved ? (
            <span className="inline-flex items-center gap-1 text-[11px] font-medium text-emerald-700 dark:text-emerald-300 bg-emerald-500/10 px-2 py-0.5 rounded-full">
              <CheckCircleIcon className="w-3.5 h-3.5" />
              Assigned
            </span>
          ) : (
            <span className="text-[11px] text-amber-600 dark:text-amber-400 font-medium">
              Not set
            </span>
          )}
        </td>
        <td className="py-2.5 px-3">
          <div className="flex items-center gap-1.5">
            <div className="relative">
              <input
                type={isRevealed ? "text" : "password"}
                inputMode="numeric"
                maxLength={4}
                disabled={disableInputs || savingKey === key}
                value={draft}
                onChange={(e) => {
                  const val = e.target.value.replace(/\D/g, "").slice(0, 4);
                  setPinDrafts((prev) => ({ ...prev, [key]: val }));
                }}
                placeholder="4 digits"
                className="w-24 px-2.5 py-1 pr-7 text-xs font-mono tracking-widest rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              <button
                type="button"
                onClick={() => setRevealedKeys((prev) => ({ ...prev, [key]: !prev[key] }))}
                className="absolute right-1.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-slate-200"
                title={isRevealed ? "Hide PIN" : "Show PIN"}
              >
                {isRevealed ? <EyeSlashIcon className="w-3.5 h-3.5" /> : <EyeIcon className="w-3.5 h-3.5" />}
              </button>
            </div>
            <button
              type="button"
              disabled={disableInputs || savingKey === key}
              onClick={() => handleGenerate(type, item.id)}
              className="px-2 py-1 text-[11px] font-medium rounded-lg border border-gray-300 dark:border-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700 text-gray-700 dark:text-slate-200 inline-flex items-center gap-1 cursor-pointer"
              title="Generate unique 4-digit code"
            >
              <SparklesIcon className="w-3 h-3 text-amber-500" />
              Auto
            </button>
            <button
              type="button"
              disabled={disableInputs || savingKey === key || !isDirty}
              onClick={() => handleAssign(type, item.id)}
              className="px-2.5 py-1 text-[11px] font-semibold rounded-lg theme-btn-primary disabled:opacity-40 cursor-pointer"
            >
              {savingKey === key ? "Saving…" : "Save"}
            </button>
          </div>
        </td>
      </tr>
    );
  };

  return (
    <div className="space-y-5">
      {/* ===== Enforcement Toggle Card ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <KeyIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">4-Digit Operator Passcode (Counter Attribution)</h2>
              <p className="settings-block-note">
                Optional store-wide control: when enabled, staff must enter their unique 4-digit code before saving
                invoices or returns so every transaction is tracked to the exact employee without logging out.
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body space-y-4">
          <div className="flex items-center justify-between gap-4 p-3.5 rounded-xl border border-gray-200/80 dark:border-slate-700/80 bg-gray-50/60 dark:bg-slate-800/40">
            <div>
              <div className="text-sm font-semibold text-gray-900 dark:text-white">
                Enforce 4-Digit Operator Code on Invoice Save
              </div>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                Off by default. Turn this on only if your store wants to require a 4-digit employee/user code on shared
                counters.
              </p>
            </div>
            <SettingSwitch
              checked={requirePin}
              disabled={disableInputs}
              onChange={(val) => setForm((s) => ({ ...s, require_operator_pin: Boolean(val) }))}
              ariaLabel="Enforce 4-digit operator code on invoice save"
            />
          </div>

          {requirePin && assignedCount === 0 && (
            <div className="p-3 rounded-xl bg-amber-50 dark:bg-amber-950/30 border border-amber-300 dark:border-amber-700 text-xs text-amber-800 dark:text-amber-200 flex items-start gap-2.5">
              <ExclamationTriangleIcon className="w-5 h-5 shrink-0 text-amber-500 mt-0.5" />
              <div>
                <span className="font-bold">No active staff has a 4-digit code yet.</span> Assign at least one
                4-digit code in the directory below before saving settings, otherwise staff will not be able to save
                invoices.
              </div>
            </div>
          )}

          <div className="flex items-center justify-between gap-4 p-3.5 rounded-xl border border-gray-200/80 dark:border-slate-700/80">
            <div>
              <div className="text-sm font-semibold text-gray-900 dark:text-white">
                Auto-Submit Immediately on 4th Digit
              </div>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                When enabled, typing the 4th digit saves the invoice right away without needing to press Enter.
              </p>
            </div>
            <SettingSwitch
              checked={autoSubmit}
              disabled={disableInputs || !requirePin}
              onChange={(val) => setForm((s) => ({ ...s, operator_pin_auto_submit: Boolean(val) }))}
              ariaLabel="Auto-submit on 4th digit"
            />
          </div>

          {/* Per-Module Checkboxes */}
          <div className="space-y-2">
            <div className="text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
              Document Types Requiring 4-Digit Code
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
              {MODULE_OPTIONS.map((mod) => {
                const checked = activeModules.includes(mod.key);
                return (
                  <label
                    key={mod.key}
                    className={`flex items-start gap-2.5 p-3 rounded-xl border transition-colors cursor-pointer ${
                      !requirePin ? "opacity-60 cursor-not-allowed" : ""
                    } ${
                      checked
                        ? "border-blue-400/70 bg-blue-50/50 dark:bg-blue-950/20"
                        : "border-gray-200 dark:border-slate-700"
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      disabled={disableInputs || !requirePin}
                      onChange={() => toggleModule(mod.key)}
                      className="mt-0.5 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                    />
                    <div className="min-w-0">
                      <div className="text-xs font-semibold text-gray-900 dark:text-white">{mod.label}</div>
                      <div className="text-[11px] text-gray-500 dark:text-slate-400">{mod.description}</div>
                    </div>
                  </label>
                );
              })}
            </div>
          </div>
        </div>
      </div>

      {/* ===== Staff PIN Directory ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <UserGroupIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Employee & User 4-Digit Codes</h2>
              <p className="settings-block-note">
                Each user and employee must have a unique 4-digit code. Linked User + Employee pairs automatically share
                the same code.
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body space-y-5">
          {loadingDir ? (
            <div className="text-xs text-gray-500 py-4 text-center">Loading users and employees…</div>
          ) : (
            <>
              {/* System Users */}
              <div>
                <div className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-2">
                  <ShieldCheckIcon className="w-4 h-4 text-blue-500" />
                  System Login Users ({directory.users.length})
                </div>
                <div className="overflow-x-auto rounded-xl border border-gray-200 dark:border-slate-700">
                  <table className="w-full text-left border-collapse">
                    <thead>
                      <tr className="bg-gray-50 dark:bg-slate-800/70 text-[11px] uppercase tracking-wider text-gray-500 dark:text-slate-400">
                        <th className="py-2 px-3">User</th>
                        <th className="py-2 px-3">Status</th>
                        <th className="py-2 px-3">4-Digit Code</th>
                      </tr>
                    </thead>
                    <tbody>
                      {directory.users.length === 0 ? (
                        <tr>
                          <td colSpan={3} className="py-4 text-center text-xs text-gray-500">
                            No users found.
                          </td>
                        </tr>
                      ) : (
                        directory.users.map((u) => renderStaffRow("user", u))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>

              {/* HR Employees */}
              <div>
                <div className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-2">
                  <UserGroupIcon className="w-4 h-4 text-purple-500" />
                  HR Employees & Counter Salesmen ({directory.employees.length})
                </div>
                <div className="overflow-x-auto rounded-xl border border-gray-200 dark:border-slate-700">
                  <table className="w-full text-left border-collapse">
                    <thead>
                      <tr className="bg-gray-50 dark:bg-slate-800/70 text-[11px] uppercase tracking-wider text-gray-500 dark:text-slate-400">
                        <th className="py-2 px-3">Employee</th>
                        <th className="py-2 px-3">Status</th>
                        <th className="py-2 px-3">4-Digit Code</th>
                      </tr>
                    </thead>
                    <tbody>
                      {directory.employees.length === 0 ? (
                        <tr>
                          <td colSpan={3} className="py-4 text-center text-xs text-gray-500">
                            No employees found. You can add employees in HR & Payroll.
                          </td>
                        </tr>
                      ) : (
                        directory.employees.map((e) => renderStaffRow("employee", e))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
