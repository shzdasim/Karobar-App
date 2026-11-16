// resources/js/components/settings/LicenseSetting.jsx
import { useState, useEffect, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { usePermissions } from "@/api/usePermissions";
import {
  LICENSE_TIERS_CATALOG,
  LICENSE_MODULES_CATALOG,
  resolveClientTier,
  resolveClientModules,
} from "@/context/LicenseContext.jsx";
import { GlassInput } from "@/components/Glass.jsx";
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
  SparklesIcon,
  CheckCircleIcon,
  ArrowUpCircleIcon,
} from "@heroicons/react/24/solid";

const TIER_VISUALS = {
  basic: {
    badgeBg: "bg-slate-600 text-white",
    cardBorder: "border-slate-300 dark:border-slate-700",
    headerBg: "bg-slate-100/80 dark:bg-slate-800/70",
    accentText: "text-slate-700 dark:text-slate-200",
  },
  gold: {
    badgeBg: "bg-amber-500 text-white",
    cardBorder: "border-amber-400/80 dark:border-amber-500/60",
    headerBg: "bg-amber-500/10 dark:bg-amber-500/15",
    accentText: "text-amber-700 dark:text-amber-300",
  },
  platinum: {
    badgeBg: "bg-indigo-600 text-white",
    cardBorder: "border-indigo-400/80 dark:border-indigo-500/60",
    headerBg: "bg-indigo-500/10 dark:bg-indigo-500/15",
    accentText: "text-indigo-700 dark:text-indigo-300",
  },
};

export default function LicenseSetting({
  licenseStatus,
  licenseLoading,
  fetchLicenseStatus,
}) {
  const { loading: permsLoading } = usePermissions();
  const [showPasswordModal, setShowPasswordModal] = useState(false);
  const [passwordAction, setPasswordAction] = useState(null);
  const [password, setPassword] = useState("");
  const [verifying, setVerifying] = useState(false);
  const [showLicenseDetails, setShowLicenseDetails] = useState(false);
  const [showUpgradeModal, setShowUpgradeModal] = useState(false);
  const [newLicenseKey, setNewLicenseKey] = useState("");
  const [activatingKey, setActivatingKey] = useState(false);

  const activeTierKey = useMemo(() => {
    if (!licenseStatus?.valid) return null;
    return resolveClientTier(licenseStatus?.payload, licenseStatus?.tier);
  }, [licenseStatus]);

  const activeTierLabel = useMemo(() => {
    if (!activeTierKey) return null;
    return (
      licenseStatus?.tier_label ||
      LICENSE_TIERS_CATALOG[activeTierKey]?.label ||
      "Platinum"
    );
  }, [activeTierKey, licenseStatus]);

  const activeModules = useMemo(() => {
    if (!licenseStatus?.valid) return [];
    if (Array.isArray(licenseStatus?.modules) && licenseStatus.modules.length > 0) {
      return licenseStatus.modules;
    }
    return resolveClientModules(licenseStatus?.payload, activeTierKey);
  }, [licenseStatus, activeTierKey]);

  const tiersCatalog = licenseStatus?.tiers_catalog || LICENSE_TIERS_CATALOG;
  const modulesCatalog = licenseStatus?.modules_catalog || LICENSE_MODULES_CATALOG;

  // Clear license details when license status changes
  useEffect(() => {
    setShowLicenseDetails(false);
  }, [licenseStatus?.valid]);

  const openPasswordModal = (action) => {
    setPasswordAction(action);
    setPassword("");
    setShowPasswordModal(true);
  };

  const closePasswordModal = () => {
    setShowPasswordModal(false);
    setPasswordAction(null);
    setPassword("");
  };

  const copyMachineId = async () => {
    try {
      await navigator.clipboard.writeText(licenseStatus?.machine_id || "");
      toast.success("Machine ID copied to clipboard");
    } catch {
      toast.error("Could not copy. Please copy manually.");
    }
  };

  const formatExpiryDate = (expSec) => {
    if (!expSec) return null;
    const d = new Date(Number(expSec) * 1000);
    return isNaN(d)
      ? null
      : d.toLocaleDateString("en-US", {
          year: "numeric",
          month: "short",
          day: "numeric",
          hour: "2-digit",
          minute: "2-digit",
        });
  };

  const handlePasswordVerify = async (e) => {
    e.preventDefault();
    setVerifying(true);
    try {
      const { data } = await axios.post("/api/verify-password", { password });
      if (data.ok) {
        const action = passwordAction;
        closePasswordModal();
        if (action === "view-details") {
          await fetchLicenseStatus();
          setShowLicenseDetails(true);
          toast.success("License details unlocked");
        } else if (action === "upgrade-key") {
          setNewLicenseKey("");
          setShowUpgradeModal(true);
        }
      } else {
        toast.error("Invalid password");
      }
    } catch (error) {
      toast.error(error.response?.data?.message || "Verification failed");
    } finally {
      setVerifying(false);
    }
  };

  const handleActivateNewKey = async (e) => {
    e.preventDefault();
    if (!newLicenseKey.trim()) return;
    setActivatingKey(true);
    try {
      const { data } = await axios.post("/api/license/activate", {
        license_key: newLicenseKey.trim(),
      });
      if (data?.ok) {
        toast.success(
          `License updated to ${data.tier_label || data.payload?.Edition || "New"} Tier!`
        );
        setShowUpgradeModal(false);
        setNewLicenseKey("");
        await fetchLicenseStatus();
      } else {
        toast.error(data?.reason || "License activation failed");
      }
    } catch (error) {
      toast.error(
        error?.response?.data?.reason ||
          error?.response?.data?.message ||
          "License activation failed"
      );
    } finally {
      setActivatingKey(false);
    }
  };

  // Helper to format license payload fields
  const formatPayloadValue = (value) => {
    if (value === null || value === undefined) return "Not specified";
    if (typeof value === "boolean") return value ? "Yes" : "No";
    if (Array.isArray(value)) return value.length > 0 ? value.join(", ") : "None";
    if (typeof value === "object") return JSON.stringify(value);
    return String(value);
  };

  // Get payload fields to display
  const getPayloadEntries = () => {
    const payload = licenseStatus?.payload || {};
    const excludeKeys = ["exp", "nbf", "machine", "iat", "iss", "sub", "ver", "_seal"];
    return Object.entries(payload).filter(([key]) => !excludeKeys.includes(key));
  };

  if (permsLoading) {
    return (
      <div className="settings-block">
        <div className="settings-body">
          <p className="settings-hint">Checking permissions…</p>
        </div>
      </div>
    );
  }

  const activeVisual = TIER_VISUALS[activeTierKey] || TIER_VISUALS.platinum;

  return (
    <>
      <div className="settings-block space-y-5">
        {/* Header */}
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <KeyIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">License Management &amp; Tiers</h2>
              <p className="settings-block-note">
                Activation status, machine binding, and 3-Tier module access (Basic, Gold, Platinum).
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {licenseStatus?.valid && activeTierLabel && (
              <span
                className={`px-3 py-1 rounded-full text-xs font-extrabold uppercase tracking-wider shadow-xs ${activeVisual.badgeBg}`}
              >
                {activeTierLabel} Tier
              </span>
            )}

            <span
              className={`settings-chip ${licenseStatus?.valid ? "" : "settings-chip-muted"}`}
              style={
                licenseStatus?.valid
                  ? { "--settings-chip": "var(--color-success)" }
                  : undefined
              }
            >
              {licenseStatus?.valid ? (
                <ShieldCheckIcon aria-hidden="true" />
              ) : (
                <ShieldExclamationIcon aria-hidden="true" />
              )}
              {licenseStatus?.valid ? "Active" : "No active license"}
            </span>

            <button
              type="button"
              onClick={() => openPasswordModal("upgrade-key")}
              className="products-action products-action-primary theme-btn-primary"
            >
              <ArrowUpCircleIcon className="w-4 h-4" />
              <span>Upgrade / Change Key</span>
            </button>

            <button
              type="button"
              onClick={() => openPasswordModal("view-details")}
              className="products-action theme-btn-secondary"
            >
              <ShieldCheckIcon className="w-4 h-4" />
              <span>View details</span>
            </button>
          </div>
        </div>

        <div className="settings-body space-y-6">
          <div className="settings-tiles">
            {/* License status */}
            <div className="settings-tile">
              <div className="settings-tile-title">
                {licenseStatus?.valid ? (
                  <ShieldCheckIcon aria-hidden="true" />
                ) : (
                  <ShieldExclamationIcon aria-hidden="true" />
                )}
                License Status &amp; Tier
              </div>

              {licenseLoading ? (
                <p className="settings-hint">Loading license status…</p>
              ) : licenseStatus?.valid ? (
                <>
                  <div className="flex items-center gap-2">
                    <p className="settings-detail-value">
                      {activeTierLabel} Edition Active
                    </p>
                  </div>
                  <p className="settings-hint">
                    {formatExpiryDate(licenseStatus.expires_at)
                      ? `Expires: ${formatExpiryDate(licenseStatus.expires_at)}`
                      : "Lifetime / Perpetual License"}
                  </p>
                </>
              ) : (
                <>
                  <p className="settings-detail-value">No active license</p>
                  <p className="settings-hint">
                    {licenseStatus?.reason || "License not found or expired"}
                  </p>
                </>
              )}
            </div>

            {/* Machine ID */}
            <div className="settings-tile">
              <div className="settings-tile-title">
                <CpuChipIcon aria-hidden="true" />
                <span>Machine ID</span>
                <button
                  type="button"
                  onClick={copyMachineId}
                  className="products-action theme-btn-tertiary"
                  style={{ marginLeft: "auto" }}
                  title="Copy machine ID"
                >
                  <ClipboardDocumentIcon className="w-4 h-4" />
                  <span>Copy</span>
                </button>
              </div>
              <p className="settings-mono">
                {licenseStatus?.machine_id || "Unable to load"}
              </p>
            </div>
          </div>

          {/* ===== 3-Tier Comparison Cards (Basic vs Gold vs Platinum) ===== */}
          <div className="space-y-3">
            <div className="flex items-center justify-between gap-2">
              <h3 className="text-sm font-bold text-slate-800 dark:text-slate-100 flex items-center gap-2">
                <SparklesIcon className="w-4 h-4 text-amber-500" />
                <span>Karobar-App License Tiers &amp; Included Modules</span>
              </h3>
              <span className="text-xs text-slate-500 dark:text-slate-400">
                Simple 3-Tier Progression: Basic → Gold → Platinum
              </span>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              {Object.values(tiersCatalog).map((tierObj) => {
                const isCurrent = activeTierKey === tierObj.key;
                const vis = TIER_VISUALS[tierObj.key] || TIER_VISUALS.basic;
                const tierModules = tierObj.modules || [];

                return (
                  <div
                    key={tierObj.key}
                    className={`rounded-2xl border p-4 flex flex-col justify-between transition-all ${
                      isCurrent
                        ? `${vis.cardBorder} ring-2 ring-blue-500/40 bg-white dark:bg-slate-900 shadow-md`
                        : "border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/40"
                    }`}
                  >
                    <div>
                      <div className="flex items-center justify-between gap-2 mb-2">
                        <span
                          className={`px-2.5 py-0.5 rounded-full text-xs font-extrabold uppercase tracking-wider ${vis.badgeBg}`}
                        >
                          {tierObj.label}
                        </span>
                        {isCurrent && (
                          <span className="inline-flex items-center gap-1 text-xs font-bold text-emerald-600 dark:text-emerald-400">
                            <CheckBadgeIcon className="w-4 h-4" />
                            Current Plan
                          </span>
                        )}
                      </div>

                      <p className="text-xs text-slate-600 dark:text-slate-300 mb-3 min-h-[32px]">
                        {tierObj.tagline}
                      </p>

                      <div className="space-y-2 border-t border-slate-200/80 dark:border-slate-800 pt-3">
                        {Object.values(modulesCatalog).map((mod) => {
                          const included = tierModules.includes(mod.key);
                          return (
                            <div
                              key={mod.key}
                              className={`flex items-start gap-2 text-xs ${
                                included
                                  ? "text-slate-800 dark:text-slate-100 font-medium"
                                  : "text-slate-400 dark:text-slate-600 line-through"
                              }`}
                            >
                              {included ? (
                                <CheckCircleIcon className="w-4 h-4 text-emerald-500 shrink-0 mt-0.5" />
                              ) : (
                                <LockClosedIcon className="w-3.5 h-3.5 text-slate-400 dark:text-slate-600 shrink-0 mt-0.5" />
                              )}
                              <span>{mod.label}</span>
                            </div>
                          );
                        })}
                      </div>
                    </div>

                    <div className="mt-4 pt-2.5 border-t border-slate-200/60 dark:border-slate-800/80 flex items-center justify-between text-[11px] text-slate-500 dark:text-slate-400">
                      <span>Terminal Seats:</span>
                      <span className="font-bold text-slate-700 dark:text-slate-200">
                        {tierObj.key === "platinum"
                          ? "Multi-PC Server & Terminals"
                          : "1 PC (Standalone)"}
                      </span>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* License details — protected section */}
          {showLicenseDetails && licenseStatus?.valid && (
            <div className="settings-tile">
              <div className="settings-tile-title">
                <DocumentTextIcon aria-hidden="true" />
                <span>License Certificate Details</span>
                <span
                  className="settings-chip"
                  style={{ "--settings-chip": "var(--color-success)" }}
                >
                  <CheckBadgeIcon aria-hidden="true" />
                  Verified
                </span>
                <button
                  type="button"
                  onClick={() => setShowLicenseDetails(false)}
                  className="products-action theme-btn-tertiary"
                  style={{ marginLeft: "auto" }}
                >
                  Hide
                </button>
              </div>

              <div className="settings-detail-grid">
                {/* Active Tier */}
                <div className="settings-detail">
                  <ShieldCheckIcon aria-hidden="true" />
                  <div>
                    <div className="settings-detail-label">License Tier</div>
                    <div className="settings-detail-value">
                      {activeTierLabel} ({activeModules.length} Modules Unlocked)
                    </div>
                  </div>
                </div>

                {/* Expiry */}
                <div className="settings-detail">
                  <CalendarIcon aria-hidden="true" />
                  <div>
                    <div className="settings-detail-label">Expires</div>
                    <div className="settings-detail-value">
                      {formatExpiryDate(licenseStatus.expires_at) || "Lifetime"}
                    </div>
                  </div>
                </div>

                {/* Days remaining */}
                {licenseStatus.expires_at && (
                  <div className="settings-detail">
                    <ClockIcon aria-hidden="true" />
                    <div>
                      <div className="settings-detail-label">Days remaining</div>
                      <div className="settings-detail-value">
                        {Math.max(
                          0,
                          Math.ceil(
                            (Number(licenseStatus.expires_at) * 1000 - Date.now()) /
                              (1000 * 60 * 60 * 24)
                          )
                        )}{" "}
                        days
                      </div>
                    </div>
                  </div>
                )}

                {/* Dynamic payload fields */}
                {getPayloadEntries().map(([key, value]) => {
                  const lower = key.toLowerCase();
                  const Icon =
                    lower.includes("owner") || lower.includes("company") || lower.includes("name")
                      ? BuildingOfficeIcon
                      : lower.includes("type") || lower.includes("edition") || lower.includes("tier")
                      ? ShieldCheckIcon
                      : CpuChipIcon;
                  return (
                    <div key={key} className="settings-detail">
                      <Icon aria-hidden="true" />
                      <div>
                        <div className="settings-detail-label">
                          {key
                            .replace(/_/g, " ")
                            .replace(/\b\w/g, (l) => l.toUpperCase())}
                        </div>
                        <div className="settings-detail-value">
                          {formatPayloadValue(value)}
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* ===== Password verification modal ===== */}
      {showPasswordModal && (
        <div
          className="settings-modal"
          role="dialog"
          aria-modal="true"
          aria-labelledby="license-modal-title"
          onClick={(e) => {
            if (e.target === e.currentTarget && !verifying) closePasswordModal();
          }}
        >
          <div className="settings-modal-card">
            <div className="settings-modal-heading">
              <div className="settings-block-icon">
                <LockClosedIcon />
              </div>
              <div className="min-w-0">
                <h3 id="license-modal-title" className="settings-block-title">
                  {passwordAction === "upgrade-key"
                    ? "Upgrade / Change License Key"
                    : "View License Details"}
                </h3>
                <p className="settings-block-note">Enter your password to continue</p>
              </div>
            </div>

            <form onSubmit={handlePasswordVerify} className="settings-modal-body">
              <div className="settings-field">
                <label className="settings-label" htmlFor="license-password">
                  Password
                </label>
                <GlassInput
                  id="license-password"
                  className="w-full"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Enter your password"
                  autoFocus
                />
              </div>

              <div className="settings-actions">
                <button
                  type="button"
                  onClick={closePasswordModal}
                  disabled={verifying}
                  className="products-action theme-btn-tertiary"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={verifying || !password}
                  className="products-action products-action-primary theme-btn-primary"
                >
                  {verifying ? "Verifying…" : "Verify"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ===== Upgrade / Activate New License Key Modal ===== */}
      {showUpgradeModal && (
        <div
          className="settings-modal"
          role="dialog"
          aria-modal="true"
          aria-labelledby="upgrade-license-modal-title"
          onClick={(e) => {
            if (e.target === e.currentTarget && !activatingKey) {
              setShowUpgradeModal(false);
            }
          }}
        >
          <div className="settings-modal-card max-w-xl">
            <div className="settings-modal-heading">
              <div className="settings-block-icon">
                <KeyIcon />
              </div>
              <div className="min-w-0">
                <h3 id="upgrade-license-modal-title" className="settings-block-title">
                  Activate or Upgrade License Key
                </h3>
                <p className="settings-block-note">
                  Paste your Basic, Gold, or Platinum KRBL. license key bound to Machine ID{" "}
                  <span className="font-mono font-bold">{licenseStatus?.machine_id}</span>
                </p>
              </div>
            </div>

            <form onSubmit={handleActivateNewKey} className="settings-modal-body space-y-4">
              <div className="settings-field">
                <label className="settings-label" htmlFor="new-license-key-input">
                  License Key (KRBL....)
                </label>
                <textarea
                  id="new-license-key-input"
                  rows={4}
                  value={newLicenseKey}
                  onChange={(e) => setNewLicenseKey(e.target.value)}
                  placeholder="Paste your KRBL. license key here..."
                  className="g-input w-full font-mono text-xs p-3"
                  autoFocus
                />
              </div>

              <div className="settings-actions">
                <button
                  type="button"
                  onClick={() => setShowUpgradeModal(false)}
                  disabled={activatingKey}
                  className="products-action theme-btn-tertiary"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={activatingKey || !newLicenseKey.trim()}
                  className="products-action products-action-primary theme-btn-primary"
                >
                  {activatingKey ? "Activating…" : "Activate License Key"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </>
  );
}
