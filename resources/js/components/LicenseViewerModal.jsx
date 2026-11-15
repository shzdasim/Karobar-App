// resources/js/components/LicenseViewerModal.jsx
import React, { useState, useEffect } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { useNavigate } from "react-router-dom";
import { useLicense } from "@/context/LicenseContext.jsx";
import {
  KeyIcon,
  ShieldCheckIcon,
  ShieldExclamationIcon,
  ClipboardDocumentIcon,
  LockClosedIcon,
  DocumentTextIcon,
  CheckBadgeIcon,
  CalendarIcon,
  ClockIcon,
  BuildingOfficeIcon,
  CpuChipIcon,
  XMarkIcon,
  ArrowPathIcon,
  ArrowTopRightOnSquareIcon,
} from "@heroicons/react/24/solid";

function formatRemaining(ms) {
  if (ms == null) return "Perpetual / Lifetime";
  const s = Math.max(0, Math.floor(ms / 1000));
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (d > 0) return `${d}d ${h}h ${m}m`;
  if (h > 0) return `${h}h ${m}m ${sec}s`;
  if (m > 0) return `${m}m ${sec}s`;
  return `${sec}s`;
}

export default function LicenseViewerModal({ isOpen, onClose }) {
  const navigate = useNavigate();
  const {
    loading: licLoading,
    valid: licValid,
    expiresAt,
    remainingMs,
    payload,
    machineId,
    reason,
    refresh,
  } = useLicense();

  const [unlocked, setUnlocked] = useState(false);
  const [showPasswordPrompt, setShowPasswordPrompt] = useState(false);
  const [password, setPassword] = useState("");
  const [verifying, setVerifying] = useState(false);
  const [refreshing, setRefreshing] = useState(false);

  useEffect(() => {
    if (!isOpen) {
      setShowPasswordPrompt(false);
      setPassword("");
    }
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return undefined;
    const onKeyDown = (e) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onClose?.();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const copyMachineId = async () => {
    if (!machineId) return;
    try {
      await navigator.clipboard.writeText(machineId);
      toast.success("Machine ID copied to clipboard");
    } catch {
      toast.error("Could not copy Machine ID");
    }
  };

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      await refresh?.();
      toast.success("License status refreshed");
    } finally {
      setRefreshing(false);
    }
  };

  const handleVerifyPassword = async (e) => {
    e.preventDefault();
    if (!password) return;
    setVerifying(true);
    try {
      const { data } = await axios.post("/api/verify-password", { password });
      if (data?.ok) {
        await refresh?.();
        setUnlocked(true);
        setShowPasswordPrompt(false);
        setPassword("");
        toast.success("License details unlocked");
      } else {
        toast.error("Invalid password");
      }
    } catch (err) {
      toast.error(err?.response?.data?.message || "Password verification failed");
    } finally {
      setVerifying(false);
    }
  };

  const formatExpiryDate = (ms) => {
    if (!ms) return "Lifetime / No Expiration";
    const d = new Date(Number(ms));
    return Number.isNaN(d.getTime())
      ? "Lifetime / No Expiration"
      : d.toLocaleDateString("en-US", {
          year: "numeric",
          month: "short",
          day: "numeric",
          hour: "2-digit",
          minute: "2-digit",
        });
  };

  const formatPayloadValue = (value) => {
    if (value === null || value === undefined) return "Not specified";
    if (typeof value === "boolean") return value ? "Yes" : "No";
    if (Array.isArray(value)) return value.length > 0 ? value.join(", ") : "None";
    if (typeof value === "object") return JSON.stringify(value);
    return String(value);
  };

  const payloadEntries = Object.entries(payload || {}).filter(
    ([key]) => !["exp", "nbf", "machine", "iat", "iss", "sub", "ver", "_seal"].includes(key)
  );

  return (
    <div
      className="fixed inset-0 z-100000 flex items-center justify-center p-4 bg-black/50 backdrop-blur-xs animate-in fade-in duration-150"
      role="dialog"
      aria-modal="true"
      aria-labelledby="license-viewer-modal-title"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose?.();
      }}
    >
      <div className="w-full max-w-lg rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700/80 shadow-2xl overflow-hidden text-slate-900 dark:text-slate-100">
        {/* Header */}
        <div className="flex items-center justify-between gap-3 px-5 py-4 border-b border-slate-200 dark:border-slate-800 bg-slate-50/80 dark:bg-slate-800/50">
          <div className="flex items-center gap-3 min-w-0">
            <div className="grid place-items-center w-10 h-10 rounded-xl bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 shrink-0">
              <KeyIcon className="w-5 h-5" />
            </div>
            <div className="min-w-0">
              <h2 id="license-viewer-modal-title" className="text-base font-bold truncate">
                License Viewer &amp; System Binding
              </h2>
              <p className="text-xs text-slate-500 dark:text-slate-400 truncate">
                Karobar-App ERP activation status &amp; hardware fingerprint
              </p>
            </div>
          </div>

          <div className="flex items-center gap-1.5 shrink-0">
            <button
              type="button"
              onClick={handleRefresh}
              disabled={refreshing}
              title="Refresh license status"
              aria-label="Refresh license status"
              className="inline-flex items-center justify-center w-8 h-8 rounded-lg text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-100 hover:bg-slate-200/60 dark:hover:bg-slate-700/60 transition-colors cursor-pointer"
            >
              <ArrowPathIcon className={`w-4 h-4 ${refreshing ? "animate-spin" : ""}`} />
            </button>
            <button
              type="button"
              onClick={onClose}
              aria-label="Close license viewer"
              className="inline-flex items-center justify-center w-8 h-8 rounded-lg text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-100 hover:bg-slate-200/60 dark:hover:bg-slate-700/60 transition-colors cursor-pointer"
            >
              <XMarkIcon className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Body */}
        <div className="p-5 space-y-4 max-h-[75dvh] overflow-y-auto">
          {/* Status Card */}
          <div
            className={`p-4 rounded-xl border flex items-start justify-between gap-4 ${
              licValid
                ? "bg-emerald-500/5 border-emerald-500/25"
                : "bg-amber-500/5 border-amber-500/30"
            }`}
          >
            <div className="space-y-1 min-w-0">
              <div className="flex items-center gap-2">
                {licValid ? (
                  <ShieldCheckIcon className="w-5 h-5 text-emerald-500 shrink-0" />
                ) : (
                  <ShieldExclamationIcon className="w-5 h-5 text-amber-500 shrink-0" />
                )}
                <span className="text-sm font-bold">
                  {licLoading
                    ? "Checking License…"
                    : licValid
                    ? "License Active & Verified"
                    : "No Active License"}
                </span>
              </div>
              <p className="text-xs text-slate-600 dark:text-slate-300">
                {licValid
                  ? `Expires: ${formatExpiryDate(expiresAt)}`
                  : reason || "License is missing, expired, or not bound to this machine."}
              </p>
            </div>

            {licValid && (
              <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 border border-emerald-500/30 shrink-0">
                <ClockIcon className="w-3.5 h-3.5" />
                {formatRemaining(remainingMs)}
              </span>
            )}
          </div>

          {/* Machine ID */}
          <div className="p-3.5 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-800/40">
            <div className="flex items-center justify-between gap-2 mb-1.5">
              <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-slate-600 dark:text-slate-300">
                <CpuChipIcon className="w-4 h-4 text-blue-500" />
                Machine Hardware ID
              </span>
              <button
                type="button"
                onClick={copyMachineId}
                disabled={!machineId}
                className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors cursor-pointer disabled:opacity-50"
              >
                <ClipboardDocumentIcon className="w-3.5 h-3.5" />
                Copy ID
              </button>
            </div>
            <p className="font-mono text-xs break-all select-all text-slate-800 dark:text-slate-200 bg-white dark:bg-slate-900 px-2.5 py-1.5 rounded-lg border border-slate-200/70 dark:border-slate-800">
              {machineId || "Loading machine fingerprint…"}
            </p>
          </div>

          {/* Password Prompt or Unlocked Payload Details */}
          {licValid && !unlocked && !showPasswordPrompt && (
            <div className="flex items-center justify-between gap-3 p-3.5 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/40 dark:bg-slate-800/20">
              <div className="flex items-center gap-2.5 min-w-0">
                <LockClosedIcon className="w-4 h-4 text-slate-400 shrink-0" />
                <span className="text-xs text-slate-600 dark:text-slate-300">
                  Certificate payload details are protected.
                </span>
              </div>
              <button
                type="button"
                onClick={() => setShowPasswordPrompt(true)}
                className="px-3 py-1.5 rounded-lg text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white transition-colors shrink-0 cursor-pointer"
              >
                Unlock Details
              </button>
            </div>
          )}

          {showPasswordPrompt && !unlocked && (
            <form
              onSubmit={handleVerifyPassword}
              className="p-3.5 rounded-xl border border-blue-500/30 bg-blue-500/5 space-y-3"
            >
              <div className="flex items-center justify-between">
                <label
                  htmlFor="license-viewer-password"
                  className="text-xs font-bold text-slate-700 dark:text-slate-200 flex items-center gap-1.5"
                >
                  <LockClosedIcon className="w-3.5 h-3.5 text-blue-500" />
                  Confirm Account Password to View Payload
                </label>
                <button
                  type="button"
                  onClick={() => {
                    setShowPasswordPrompt(false);
                    setPassword("");
                  }}
                  className="text-xs text-slate-500 hover:text-slate-700 dark:hover:text-slate-200"
                >
                  Cancel
                </button>
              </div>
              <div className="flex items-center gap-2">
                <input
                  id="license-viewer-password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Enter your login password"
                  autoFocus
                  className="flex-1 h-9 px-3 rounded-lg text-xs bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 focus:outline-hidden focus:ring-2 focus:ring-blue-500/50"
                />
                <button
                  type="submit"
                  disabled={verifying || !password}
                  className="h-9 px-3.5 rounded-lg text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white disabled:opacity-50 cursor-pointer"
                >
                  {verifying ? "Verifying…" : "Unlock"}
                </button>
              </div>
            </form>
          )}

          {unlocked && licValid && (
            <div className="p-3.5 rounded-xl border border-emerald-500/30 bg-emerald-500/5 space-y-3">
              <div className="flex items-center justify-between">
                <span className="inline-flex items-center gap-1.5 text-xs font-bold text-emerald-700 dark:text-emerald-300">
                  <DocumentTextIcon className="w-4 h-4" />
                  Verified License Certificate
                </span>
                <span className="inline-flex items-center gap-1 text-[11px] font-semibold text-emerald-600 dark:text-emerald-400">
                  <CheckBadgeIcon className="w-3.5 h-3.5" />
                  Authentic
                </span>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                <div className="p-2.5 rounded-lg bg-white/80 dark:bg-slate-900/70 border border-slate-200/70 dark:border-slate-800">
                  <div className="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1">
                    <CalendarIcon className="w-3.5 h-3.5" />
                    Expiration
                  </div>
                  <div className="text-xs font-bold mt-0.5">{formatExpiryDate(expiresAt)}</div>
                </div>

                <div className="p-2.5 rounded-lg bg-white/80 dark:bg-slate-900/70 border border-slate-200/70 dark:border-slate-800">
                  <div className="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1">
                    <ClockIcon className="w-3.5 h-3.5" />
                    Time Remaining
                  </div>
                  <div className="text-xs font-bold mt-0.5">{formatRemaining(remainingMs)}</div>
                </div>

                {payloadEntries.map(([key, value]) => {
                  const lower = key.toLowerCase();
                  const Icon =
                    lower.includes("owner") || lower.includes("company")
                      ? BuildingOfficeIcon
                      : lower.includes("type") || lower.includes("edition")
                      ? ShieldCheckIcon
                      : CpuChipIcon;
                  return (
                    <div
                      key={key}
                      className="p-2.5 rounded-lg bg-white/80 dark:bg-slate-900/70 border border-slate-200/70 dark:border-slate-800"
                    >
                      <div className="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1">
                        <Icon className="w-3.5 h-3.5" />
                        {key.replace(/_/g, " ").replace(/\b\w/g, (l) => l.toUpperCase())}
                      </div>
                      <div className="text-xs font-bold mt-0.5 wrap-break-word">
                        {formatPayloadValue(value)}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between gap-3 px-5 py-3 border-t border-slate-200 dark:border-slate-800 bg-slate-50/80 dark:bg-slate-800/50">
          <button
            type="button"
            onClick={() => {
              onClose?.();
              navigate("/settings#license");
            }}
            className="inline-flex items-center gap-1.5 text-xs font-semibold text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
          >
            <span>Open License Settings</span>
            <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
          </button>

          <button
            type="button"
            onClick={onClose}
            className="px-4 py-1.5 rounded-lg text-xs font-semibold bg-slate-200/80 hover:bg-slate-300/80 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-800 dark:text-slate-200 transition-colors cursor-pointer"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
