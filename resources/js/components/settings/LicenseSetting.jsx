import { useState, useEffect } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { 
  KeyIcon, 
  ShieldCheckIcon, 
  ShieldExclamationIcon,
  ClipboardDocumentIcon,
  LockClosedIcon,
  EyeIcon,
  EyeSlashIcon,
  DocumentTextIcon,
  CheckBadgeIcon,
  CalendarIcon,
  ClockIcon,
  BuildingOfficeIcon,
  CpuChipIcon
} from "@heroicons/react/24/solid";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassBtn } from "@/components/glass.jsx";

export default function LicenseSetting({ 
  licenseStatus, 
  licenseLoading, 
  fetchLicenseStatus,
  tintBlue,
  tintGlass
}) {
  const [showPasswordModal, setShowPasswordModal] = useState(false);
  const [passwordAction, setPasswordAction] = useState(null);
  const [password, setPassword] = useState("");
  const [verifying, setVerifying] = useState(false);
  const [showLicenseDetails, setShowLicenseDetails] = useState(false);

  // Clear license details when license status changes (e.g., license deactivated)
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
    const excludeKeys = ['exp', 'nbf', 'machine']; // Already shown elsewhere
    return Object.entries(payload).filter(([key]) => !excludeKeys.includes(key));
  };

  return (
    <>
      {/* ===== License Management ===== */}
      <GlassCard>
        <GlassSectionHeader
          title={<span className="inline-flex items-center gap-2">
            <KeyIcon className="w-5 h-5 text-amber-600" />
            <span>License Management</span>
          </span>}
          right={
            <div className="flex items-center gap-2">
              <GlassBtn
                onClick={() => openPasswordModal("view-details")}
                className={`h-8 px-3 ${tintBlue}`}
                title="View full license details"
              >
                <span className="inline-flex items-center gap-1 text-xs">
                  <ShieldCheckIcon className="w-4 h-4" />
                  View Details
                </span>
              </GlassBtn>
            </div>
          }
        />
        <GlassToolbar className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* License Status */}
          <div className="flex items-center gap-3 p-4 rounded-xl bg-white/60 dark:bg-slate-700/60">
            {licenseLoading ? (
              <div className="animate-pulse text-gray-400 dark:text-gray-500">Loading license status...</div>
            ) : licenseStatus?.valid ? (
              <>
                <div className="flex-shrink-0 w-12 h-12 rounded-full bg-emerald-100 dark:bg-emerald-900/50 flex items-center justify-center">
                  <ShieldCheckIcon className="w-6 h-6 text-emerald-600 dark:text-emerald-400" />
                </div>
                <div>
                  <div className="font-medium text-emerald-700 dark:text-emerald-400">License Active</div>
                  <div className="text-sm text-gray-600 dark:text-gray-400">
                    {formatExpiryDate(licenseStatus.expires_at) || "No expiration date"}
                  </div>
                </div>
              </>
            ) : (
              <>
                <div className="flex-shrink-0 w-12 h-12 rounded-full bg-red-100 dark:bg-red-900/50 flex items-center justify-center">
                  <ShieldExclamationIcon className="w-6 h-6 text-red-600 dark:text-red-400" />
                </div>
                <div>
                  <div className="font-medium text-red-700 dark:text-red-400">No Active License</div>
                  <div className="text-sm text-gray-600 dark:text-gray-400">
                    {licenseStatus?.reason || "License not found or expired"}
                  </div>
                </div>
              </>
            )}
          </div>

        {/* Machine ID */}
          <div className="p-4 rounded-xl bg-white/60 dark:bg-slate-700/60">
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-medium text-gray-700 dark:text-gray-300">Machine ID</span>
              <button
                onClick={copyMachineId}
                className="inline-flex items-center gap-1 text-xs text-blue-600 hover:text-blue-800 dark:text-blue-400 dark:hover:text-blue-300"
                title="Copy Machine ID"
              >
                <ClipboardDocumentIcon className="w-4 h-4" />
                Copy
              </button>
            </div>
            <div className="text-xs font-mono text-gray-600 bg-gray-50 rounded p-2 break-all dark:bg-slate-800 dark:text-gray-400">
              {licenseStatus?.machine_id || "Unable to load"}
            </div>
          </div>

          {/* License Details - Protected Section */}
          {showLicenseDetails && licenseStatus?.valid ? (
            <div className="col-span-1 md:col-span-2 mt-4">
              <div className="p-4 rounded-xl bg-gradient-to-br from-blue-50 to-indigo-50 dark:from-blue-900/30 dark:to-indigo-900/30 border border-blue-200 dark:border-blue-700">
                <div className="flex items-center justify-between mb-4">
                  <div className="flex items-center gap-2">
                    <DocumentTextIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
                    <span className="font-semibold text-gray-800 dark:text-gray-200">License Details</span>
                    <span className="px-2 py-0.5 text-xs rounded-full bg-green-100 text-green-700 dark:bg-green-900/50 dark:text-green-400">
                      <CheckBadgeIcon className="w-3.5 h-3.5 inline mr-1" />
                      Verified
                    </span>
                  </div>
                  <GlassBtn
                    onClick={() => setShowLicenseDetails(false)}
                    className="h-7 px-3 text-xs bg-red-50 hover:bg-red-100 text-red-600 dark:bg-red-900/30 dark:text-red-400 dark:hover:bg-red-900/50"
                  >
                    <EyeSlashIcon className="w-3.5 h-3.5 mr-1" />
                    Hide
                  </GlassBtn>
                </div>
                
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                  {/* Expiry Date */}
                  <div className="flex items-center gap-2 p-2 rounded-lg bg-white/60 dark:bg-slate-800/60">
                    <CalendarIcon className="w-4 h-4 text-amber-600" />
                    <div>
                      <div className="text-xs text-gray-500 dark:text-gray-400">Expires</div>
                      <div className="text-sm font-medium text-gray-700 dark:text-gray-300">
                        {formatExpiryDate(licenseStatus.expires_at) || "Lifetime"}
                      </div>
                    </div>
                  </div>

                  {/* Days Remaining */}
                  {licenseStatus.expires_at && (
                    <div className="flex items-center gap-2 p-2 rounded-lg bg-white/60 dark:bg-slate-800/60">
                      <ClockIcon className="w-4 h-4 text-emerald-600" />
                      <div>
                        <div className="text-xs text-gray-500 dark:text-gray-400">Days Remaining</div>
                        <div className="text-sm font-medium text-gray-700 dark:text-gray-300">
                          {Math.max(0, Math.ceil((Number(licenseStatus.expires_at) * 1000 - Date.now()) / (1000 * 60 * 60 * 24)))} days
                        </div>
                      </div>
                    </div>
                  )}

                  {/* Dynamic payload fields */}
                  {getPayloadEntries().map(([key, value]) => (
                    <div key={key} className="flex items-start gap-2 p-2 rounded-lg bg-white/60 dark:bg-slate-800/60">
                      {key.toLowerCase().includes('owner') || key.toLowerCase().includes('company') ? (
                        <BuildingOfficeIcon className="w-4 h-4 text-blue-600 mt-0.5" />
                      ) : key.toLowerCase().includes('type') || key.toLowerCase().includes('edition') ? (
                        <ShieldCheckIcon className="w-4 h-4 text-purple-600 mt-0.5" />
                      ) : (
                        <CpuChipIcon className="w-4 h-4 text-gray-500 mt-0.5" />
                      )}
                      <div>
                        <div className="text-xs text-gray-500 dark:text-gray-400">
                          {key.replace(/_/g, ' ').replace(/\b\w/g, l => l.toUpperCase())}
                        </div>
                        <div className="text-sm font-medium text-gray-700 dark:text-gray-300 break-words">
                          {formatPayloadValue(value)}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          ) : null}
        </GlassToolbar>
      </GlassCard>

      {/* ===== Password Verification Modal ===== */}
      {showPasswordModal && (
        <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50">
          <div className="bg-white rounded-2xl shadow-2xl p-6 w-full max-w-md mx-4">
            <div className="flex items-center gap-3 mb-4">
              <div className="w-10 h-10 rounded-full bg-slate-100 flex items-center justify-center">
                <LockClosedIcon className="w-5 h-5 text-slate-600" />
              </div>
              <div>
                <h3 className="font-semibold text-gray-800">
                  {passwordAction === "deactivate" ? "Deactivate License" : "View License Details"}
                </h3>
                <p className="text-sm text-gray-500">Enter your password to continue</p>
              </div>
            </div>
            <form onSubmit={handlePasswordVerify}>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter your password"
                className="w-full px-4 py-3 rounded-xl border border-gray-200 focus:border-blue-400 focus:ring-2 focus:ring-blue-100 outline-none transition-all"
                autoFocus
              />
              <div className="flex justify-end gap-2 mt-4">
                <button
                  type="button"
                  onClick={closePasswordModal}
                  className="px-4 py-2 rounded-lg text-gray-600 hover:bg-gray-100 transition-colors"
                  disabled={verifying}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={verifying || !password}
                  className="px-4 py-2 rounded-lg bg-blue-600 text-white hover:bg-blue-700 transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
                >
                  {verifying ? "Verifying..." : "Verify"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </>
  );
}

