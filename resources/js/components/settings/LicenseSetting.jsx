import { useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { 
  KeyIcon, 
  ShieldCheckIcon, 
  ShieldExclamationIcon,
  ClipboardDocumentIcon,
  LockClosedIcon 
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
          toast.success("License details loaded");
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

