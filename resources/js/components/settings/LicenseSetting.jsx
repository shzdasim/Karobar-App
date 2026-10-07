// resources/js/components/settings/LicenseSetting.jsx
import { useState, useEffect } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { usePermissions } from "@/api/usePermissions";
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
} from "@heroicons/react/24/solid";

export default function LicenseSetting({
  licenseStatus,
  licenseLoading,
  fetchLicenseStatus,
}) {
  const { loading: permsLoading, canFor } = usePermissions();
  const [showPasswordModal, setShowPasswordModal] = useState(false);
  const [passwordAction, setPasswordAction] = useState(null);
  const [password, setPassword] = useState("");
  const [verifying, setVerifying] = useState(false);
  const [showLicenseDetails, setShowLicenseDetails] = useState(false);

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
    return isNaN(d) ? null : d.toLocaleDateString("en-US", {
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
        closePasswordModal();
        if (passwordAction === "view-details") {
          await fetchLicenseStatus();
          setShowLicenseDetails(true);
          toast.success("License details unlocked");
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
    const excludeKeys = ['exp', 'nbf', 'machine'];
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

  return (
    <>
      <div className="settings-block">
        {/* Header */}
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <KeyIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">License Management</h2>
              <p className="settings-block-note">Activation status, machine binding, and license payload.</p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <span
              className={`settings-chip ${licenseStatus?.valid ? "" : "settings-chip-muted"}`}
              style={licenseStatus?.valid ? { "--settings-chip": "var(--color-success)" } : undefined}
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
              onClick={() => openPasswordModal("view-details")}
              className="products-action"
            >
              <ShieldCheckIcon className="w-4 h-4" />
              <span>View details</span>
            </button>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-tiles">
            {/* License status */}
            <div className="settings-tile">
              <div className="settings-tile-title">
                {licenseStatus?.valid ? (
                  <ShieldCheckIcon aria-hidden="true" />
                ) : (
                  <ShieldExclamationIcon aria-hidden="true" />
                )}
                License status
              </div>

              {licenseLoading ? (
                <p className="settings-hint">Loading license status…</p>
              ) : licenseStatus?.valid ? (
                <>
                  <p className="settings-detail-value">License active</p>
                  <p className="settings-hint">
                    {formatExpiryDate(licenseStatus.expires_at) || "No expiration date"}
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
                  className="products-action"
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

          {/* License details — protected section */}
          {showLicenseDetails && licenseStatus?.valid && (
            <div className="settings-tile">
              <div className="settings-tile-title">
                <DocumentTextIcon aria-hidden="true" />
                <span>License details</span>
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
                  className="products-action"
                  style={{ marginLeft: "auto" }}
                >
                  Hide
                </button>
              </div>

              <div className="settings-detail-grid">
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
                        {Math.max(0, Math.ceil((Number(licenseStatus.expires_at) * 1000 - Date.now()) / (1000 * 60 * 60 * 24)))} days
                      </div>
                    </div>
                  </div>
                )}

                {/* Dynamic payload fields */}
                {getPayloadEntries().map(([key, value]) => {
                  const lower = key.toLowerCase();
                  const Icon = lower.includes('owner') || lower.includes('company')
                    ? BuildingOfficeIcon
                    : lower.includes('type') || lower.includes('edition')
                      ? ShieldCheckIcon
                      : CpuChipIcon;
                  return (
                    <div key={key} className="settings-detail">
                      <Icon aria-hidden="true" />
                      <div>
                        <div className="settings-detail-label">
                          {key.replace(/_/g, ' ').replace(/\b\w/g, l => l.toUpperCase())}
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
                  {passwordAction === "deactivate" ? "Deactivate License" : "View License Details"}
                </h3>
                <p className="settings-block-note">Enter your password to continue</p>
              </div>
            </div>

            <form onSubmit={handlePasswordVerify} className="settings-modal-body">
              <div className="settings-field">
                <label className="settings-label" htmlFor="license-password">Password</label>
                <input
                  id="license-password"
                  className="g-input w-full"
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
                  className="products-action"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={verifying || !password}
                  className="products-action products-action-primary"
                >
                  {verifying ? "Verifying…" : "Verify"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </>
  );
}
