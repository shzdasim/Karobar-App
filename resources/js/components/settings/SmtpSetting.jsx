// resources/js/components/settings/SmtpSetting.jsx
import React, { useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import Select from "react-select";
import { GlassInput } from "@/components/Glass.jsx";
import {
  EnvelopeIcon,
  PaperAirplaneIcon,
  EyeIcon,
  EyeSlashIcon,
  CheckCircleIcon,
  ShieldCheckIcon,
  ServerStackIcon,
} from "@heroicons/react/24/solid";
import { useTheme } from "@/context/ThemeContext";

const SMTP_PRESETS = [
  {
    id: "gmail",
    label: "Gmail (smtp.gmail.com — Port 587 TLS)",
    host: "smtp.gmail.com",
    port: 587,
    encryption: "tls",
    hint: "Requires a 16-character Google App Password (enable 2-Step Verification in your Google Account).",
  },
  {
    id: "outlook",
    label: "Outlook / Office 365 (smtp.office365.com — Port 587 TLS)",
    host: "smtp.office365.com",
    port: 587,
    encryption: "tls",
    hint: "Use your Microsoft 365 / Outlook email address and account or app password.",
  },
  {
    id: "yahoo",
    label: "Yahoo Mail (smtp.mail.yahoo.com — Port 465 SSL)",
    host: "smtp.mail.yahoo.com",
    port: 465,
    encryption: "ssl",
    hint: "Requires generating an App Password inside Yahoo Account Security.",
  },
  {
    id: "zoho",
    label: "Zoho Mail (smtp.zoho.com — Port 587 TLS)",
    host: "smtp.zoho.com",
    port: 587,
    encryption: "tls",
    hint: "Use your Zoho email address and password (or Zoho app-specific password).",
  },
  {
    id: "hostinger",
    label: "Hostinger / cPanel SSL (smtp.hostinger.com — Port 465 SSL)",
    host: "smtp.hostinger.com",
    port: 465,
    encryption: "ssl",
    hint: "Standard cPanel / webmail SSL configuration on port 465.",
  },
  {
    id: "custom",
    label: "Custom SMTP Server",
    host: "",
    port: 587,
    encryption: "tls",
    hint: "Enter your mail provider's SMTP hostname, port, encryption, and login credentials.",
  },
];

const ENCRYPTION_OPTIONS = [
  { value: "tls", label: "TLS / STARTTLS (Recommended — Port 587)" },
  { value: "ssl", label: "SSL / SMTPS (Implicit TLS — Port 465)" },
  { value: "none", label: "None (Unencrypted — Port 25 / 1025)" },
];

export default function SmtpSetting({
  form,
  setForm,
  handleChange,
  disableInputs,
}) {
  const { isDark, theme } = useTheme();
  const [showPassword, setShowPassword] = useState(false);
  const [testEmail, setTestEmail] = useState("");
  const [testingSmtp, setTestingSmtp] = useState(false);

  const selectStyles = useMemo(
    () => ({
      control: (base, state) => ({
        ...base,
        minHeight: "38px",
        borderRadius: 12,
        backgroundColor: isDark ? "rgba(30, 41, 59, 0.85)" : "#ffffff",
        borderColor: state.isFocused
          ? theme?.primary_color || "#3b82f6"
          : isDark
          ? "rgba(71, 85, 105, 0.7)"
          : "rgba(209, 213, 219, 0.8)",
        boxShadow: state.isFocused
          ? `0 0 0 2px ${theme?.primary_color || "#3b82f6"}33`
          : "none",
        fontSize: "14px",
        color: isDark ? "#f8fafc" : "#1e293b",
        "&:hover": {
          borderColor: theme?.primary_color || "#3b82f6",
        },
      }),
      valueContainer: (base) => ({
        ...base,
        padding: "2px 10px",
      }),
      input: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#1e293b",
        margin: 0,
        padding: 0,
      }),
      singleValue: (base) => ({
        ...base,
        color: isDark ? "#f8fafc" : "#1e293b",
        fontSize: "14px",
      }),
      placeholder: (base) => ({
        ...base,
        color: isDark ? "#94a3b8" : "#64748b",
        fontSize: "14px",
      }),
      menuPortal: (base) => ({
        ...base,
        zIndex: 9999,
      }),
      menu: (base) => ({
        ...base,
        backgroundColor: isDark ? "#1e293b" : "#ffffff",
        borderRadius: 12,
        border: isDark
          ? "1px solid rgba(71, 85, 105, 0.7)"
          : "1px solid rgba(229, 231, 235, 1)",
        boxShadow:
          "0 10px 25px -5px rgba(0, 0, 0, 0.25), 0 8px 10px -6px rgba(0, 0, 0, 0.25)",
        overflow: "hidden",
        zIndex: 9999,
      }),
      option: (base, state) => ({
        ...base,
        minHeight: 38,
        backgroundColor: state.isSelected
          ? theme?.primary_color || "#3b82f6"
          : state.isFocused
          ? isDark
            ? "rgba(51, 65, 85, 0.8)"
            : "rgba(239, 246, 255, 1)"
          : "transparent",
        color: state.isSelected
          ? "#ffffff"
          : isDark
          ? "#f8fafc"
          : "#1e293b",
        cursor: "pointer",
        fontSize: "14px",
        "&:active": {
          backgroundColor: theme?.primary_color || "#3b82f6",
          color: "#ffffff",
        },
      }),
    }),
    [isDark, theme]
  );

  const activePreset = useMemo(() => {
    const host = (form.smtp_host || "").trim().toLowerCase();
    const matched = SMTP_PRESETS.find(
      (p) => p.id !== "custom" && p.host.toLowerCase() === host
    );
    return matched || SMTP_PRESETS[SMTP_PRESETS.length - 1];
  }, [form.smtp_host]);

  const handlePresetChange = (selectedOption) => {
    const preset = SMTP_PRESETS.find((p) => p.id === selectedOption?.value);
    if (!preset) return;
    if (preset.id === "custom") {
      setForm?.((prev) => ({
        ...prev,
        smtp_enabled: true,
      }));
      return;
    }
    setForm?.((prev) => ({
      ...prev,
      smtp_enabled: true,
      smtp_host: preset.host,
      smtp_port: preset.port,
      smtp_encryption: preset.encryption,
    }));
  };

  const handleToggleEnabled = () => {
    if (disableInputs) return;
    setForm?.((prev) => ({
      ...prev,
      smtp_enabled: !prev.smtp_enabled,
    }));
  };

  const handleSendTestEmail = async () => {
    if (!form.smtp_host?.trim()) {
      toast.error("Please enter an SMTP Host first.");
      return;
    }
    const targetEmail = (testEmail || form.smtp_from_address || form.smtp_username || "").trim();
    if (!targetEmail) {
      toast.error("Please enter a recipient email address to send the test message.");
      return;
    }

    try {
      setTestingSmtp(true);
      const { data } = await axios.post("/api/settings/test-smtp", {
        smtp_host: form.smtp_host,
        smtp_port: Number(form.smtp_port || 587),
        smtp_username: form.smtp_username || "",
        smtp_password: form.smtp_password || "",
        smtp_encryption: form.smtp_encryption || "tls",
        smtp_from_address: form.smtp_from_address || form.smtp_username || "",
        smtp_from_name: form.smtp_from_name || form.store_name || "",
        test_email: targetEmail,
      });
      toast.success(data?.message || `Test email sent to ${targetEmail}`);
    } catch (error) {
      const msg =
        error?.response?.data?.message ||
        error?.response?.data?.error ||
        "SMTP connection test failed. Check your host, port, and credentials.";
      toast.error(msg);
    } finally {
      setTestingSmtp(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Block 1: Outbound Email & SMTP Server Configuration */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center justify-between gap-4 flex-wrap w-full">
            <div className="flex items-center gap-3 min-w-0">
              <div className="settings-block-icon">
                <EnvelopeIcon className="w-5 h-5" />
              </div>
              <div className="min-w-0">
                <h2 className="settings-block-title">Outbound SMTP Mail Server</h2>
                <p className="settings-block-note">
                  Configure your email provider to send Sale Invoices, Purchase Orders, and Ledgers directly as PDF attachments
                </p>
              </div>
            </div>

            {/* Enable / Disable Toggle */}
            <div className="flex items-center gap-3">
              <button
                type="button"
                role="switch"
                aria-checked={Boolean(form.smtp_enabled)}
                aria-label="Enable custom SMTP mail server"
                disabled={disableInputs}
                onClick={handleToggleEnabled}
                className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none ${
                  form.smtp_enabled
                    ? "bg-emerald-500"
                    : isDark
                    ? "bg-slate-700"
                    : "bg-slate-300"
                } ${disableInputs ? "opacity-60 cursor-not-allowed" : ""}`}
              >
                <span
                  aria-hidden="true"
                  className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow-sm ring-0 transition duration-200 ease-in-out ${
                    form.smtp_enabled ? "translate-x-5" : "translate-x-0"
                  }`}
                />
              </button>
              <span
                onClick={handleToggleEnabled}
                className={`text-sm font-medium select-none cursor-pointer ${
                  form.smtp_enabled
                    ? isDark
                      ? "text-emerald-400"
                      : "text-emerald-700"
                    : isDark
                    ? "text-slate-400"
                    : "text-slate-600"
                }`}
              >
                {form.smtp_enabled ? "SMTP Enabled" : "SMTP Disabled"}
              </span>
            </div>
          </div>
        </div>

        <div className="settings-body space-y-5">
          {/* Provider Preset */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div className="settings-field md:col-span-2">
              <label className="settings-label" htmlFor="setting-smtp-preset">
                Mail Provider Preset
              </label>
              <Select
                inputId="setting-smtp-preset"
                value={{ value: activePreset.id, label: activePreset.label }}
                onChange={handlePresetChange}
                options={SMTP_PRESETS.map((p) => ({
                  value: p.id,
                  label: p.label,
                }))}
                isDisabled={disableInputs}
                styles={selectStyles}
                menuPortalTarget={
                  typeof document !== "undefined" ? document.body : null
                }
                menuPosition="fixed"
                className="text-sm"
                isSearchable={false}
              />
              <p className="settings-hint mt-1" id="smtp-preset-hint">
                {activePreset.hint}
              </p>
            </div>

            {/* SMTP Host */}
            <div className="settings-field">
              <label className="settings-label" htmlFor="setting-smtp-host">
                SMTP Host
              </label>
              <GlassInput
                id="setting-smtp-host"
                type="text"
                name="smtp_host"
                value={form.smtp_host ?? ""}
                onChange={handleChange}
                disabled={disableInputs}
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                placeholder="e.g., smtp.gmail.com"
                className="w-full font-mono text-sm"
              />
              <p className="settings-hint mt-1">
                Outgoing mail server hostname or IP address.
              </p>
            </div>

            {/* SMTP Port & Encryption */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="settings-field">
                <label className="settings-label" htmlFor="setting-smtp-port">
                  SMTP Port
                </label>
                <GlassInput
                  id="setting-smtp-port"
                  type="text"
                  inputMode="numeric"
                  pattern="[0-9]*"
                  name="smtp_port"
                  value={form.smtp_port ?? 587}
                  onChange={handleChange}
                  disabled={disableInputs}
                  autoCorrect="off"
                  autoCapitalize="off"
                  spellCheck={false}
                  placeholder="587"
                  className="w-full font-mono text-sm"
                />
                <p className="settings-hint mt-1">Usually 587 (TLS) or 465 (SSL).</p>
              </div>

              <div className="settings-field">
                <label
                  className="settings-label"
                  htmlFor="setting-smtp-encryption"
                >
                  Encryption
                </label>
                <Select
                  inputId="setting-smtp-encryption"
                  value={
                    ENCRYPTION_OPTIONS.find(
                      (o) => o.value === (form.smtp_encryption || "tls")
                    ) || ENCRYPTION_OPTIONS[0]
                  }
                  onChange={(opt) => {
                    const nextEnc = opt?.value || "tls";
                    setForm?.((prev) => {
                      const currentPort = Number(prev.smtp_port || 587);
                      let nextPort = currentPort;
                      if (nextEnc === "ssl" && currentPort === 587) {
                        nextPort = 465;
                      } else if (nextEnc === "tls" && currentPort === 465) {
                        nextPort = 587;
                      }
                      return {
                        ...prev,
                        smtp_encryption: nextEnc,
                        smtp_port: nextPort,
                      };
                    });
                  }}
                  options={ENCRYPTION_OPTIONS}
                  isDisabled={disableInputs}
                  styles={selectStyles}
                  menuPortalTarget={
                    typeof document !== "undefined" ? document.body : null
                  }
                  menuPosition="fixed"
                  className="text-sm"
                  isSearchable={false}
                />
              </div>
            </div>

            {/* SMTP Username */}
            <div className="settings-field">
              <label
                className="settings-label"
                htmlFor="setting-smtp-username"
              >
                SMTP Username / Account Email
              </label>
              <GlassInput
                id="setting-smtp-username"
                type="text"
                name="smtp_username"
                value={form.smtp_username ?? ""}
                onChange={handleChange}
                disabled={disableInputs}
                autoComplete="username"
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                placeholder="e.g., billing@yourstore.com"
                className="w-full"
              />
              <p className="settings-hint mt-1">
                Usually your full email address or SMTP login username.
              </p>
            </div>

            {/* SMTP Password */}
            <div className="settings-field">
              <label
                className="settings-label"
                htmlFor="setting-smtp-password"
              >
                SMTP Password / App Password
              </label>
              <div className="relative">
                <GlassInput
                  id="setting-smtp-password"
                  type={showPassword ? "text" : "password"}
                  name="smtp_password"
                  value={form.smtp_password ?? ""}
                  onChange={handleChange}
                  disabled={disableInputs}
                  autoComplete="current-password"
                  autoCorrect="off"
                  autoCapitalize="off"
                  spellCheck={false}
                  placeholder="Enter SMTP password or 16-char App Password"
                  className="w-full pr-10"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((prev) => !prev)}
                  className={`absolute inset-y-0 right-0 flex items-center px-3 transition-colors ${
                    isDark
                      ? "text-slate-400 hover:text-slate-200"
                      : "text-slate-500 hover:text-slate-700"
                  }`}
                  aria-label={showPassword ? "Hide SMTP password" : "Show SMTP password"}
                  title={showPassword ? "Hide password" : "Show password"}
                >
                  {showPassword ? (
                    <EyeSlashIcon className="w-4 h-4" />
                  ) : (
                    <EyeIcon className="w-4 h-4" />
                  )}
                </button>
              </div>
              <p className="settings-hint mt-1">
                For Gmail/Yahoo, generate an App Password instead of your normal login password.
              </p>
            </div>

            {/* From Email Address */}
            <div className="settings-field">
              <label
                className="settings-label"
                htmlFor="setting-smtp-from-address"
              >
                Sender Email Address (From)
              </label>
              <GlassInput
                id="setting-smtp-from-address"
                type="email"
                name="smtp_from_address"
                value={form.smtp_from_address ?? ""}
                onChange={handleChange}
                disabled={disableInputs}
                autoComplete="email"
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                placeholder={
                  form.smtp_username || "e.g., invoices@yourstore.com"
                }
                className="w-full"
              />
              <p className="settings-hint mt-1">
                Defaults to your SMTP Username if left blank.
              </p>
            </div>

            {/* From Name */}
            <div className="settings-field">
              <label
                className="settings-label"
                htmlFor="setting-smtp-from-name"
              >
                Sender Display Name (From Name)
              </label>
              <GlassInput
                id="setting-smtp-from-name"
                type="text"
                name="smtp_from_name"
                value={form.smtp_from_name ?? ""}
                onChange={handleChange}
                disabled={disableInputs}
                placeholder={form.store_name || "e.g., Karobar Pharmacy"}
                className="w-full"
              />
              <p className="settings-hint mt-1">
                Shown as the sender name in the customer&apos;s inbox (defaults to Store Name).
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Block 2: Verify & Send Test Email */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div
              className={`settings-block-icon ${
                isDark
                  ? "text-emerald-400 bg-emerald-500/10 border border-emerald-500/20"
                  : "text-emerald-600 bg-emerald-50 border border-emerald-200"
              }`}
            >
              <ShieldCheckIcon className="w-5 h-5" />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Test SMTP Connection</h2>
              <p className="settings-block-note">
                Send a test email using the credentials entered above to verify your server before saving
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="flex flex-col sm:flex-row sm:items-end gap-3">
            <div className="settings-field flex-1">
              <label className="settings-label" htmlFor="setting-smtp-test-email">
                Recipient Email for Test
              </label>
              <GlassInput
                id="setting-smtp-test-email"
                type="email"
                value={testEmail}
                onChange={(e) => setTestEmail(e.target.value)}
                disabled={disableInputs || testingSmtp}
                autoComplete="email"
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                placeholder={
                  form.smtp_from_address ||
                  form.smtp_username ||
                  "Enter email address to receive test email..."
                }
                className="w-full"
              />
            </div>

            <button
              type="button"
              onClick={handleSendTestEmail}
              disabled={disableInputs || testingSmtp}
              className="products-action products-action-primary theme-btn-primary h-[38px] px-4 shrink-0 flex items-center gap-2"
            >
              <PaperAirplaneIcon className="w-4 h-4" />
              <span>{testingSmtp ? "Sending Test…" : "Send Test Email"}</span>
            </button>
          </div>

          <div
            className={`mt-4 p-3.5 rounded-xl border flex items-start gap-3 text-xs ${
              isDark
                ? "bg-slate-800/50 border-slate-700/60 text-slate-300"
                : "bg-slate-50 border-slate-200 text-slate-600"
            }`}
          >
            <ServerStackIcon className="w-4 h-4 shrink-0 mt-0.5 text-blue-500" />
            <div className="space-y-1">
              <p className="font-semibold">
                How Invoice Email Delivery Works
              </p>
              <p>
                When <strong>SMTP Enabled</strong> is turned on and saved, clicking{" "}
                <strong>Share → Email PDF</strong> on any Sale Invoice, Purchase Order, Quotation, or Ledger will automatically generate the PDF and email it through{" "}
                <code className="font-mono font-semibold">
                  {form.smtp_host || "your SMTP server"}:{form.smtp_port || 587}
                </code>
                . Remember to click <strong>Save settings</strong> after testing!
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

