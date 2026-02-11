// resources/js/components/settings/LicenseSetting.jsx
import { useState, useEffect, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { usePermissions } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassBtn } from "@/components/glass.jsx";
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
  CpuChipIcon
} from "@heroicons/react/24/solid";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

// Section configuration with color schemes - will use dynamic theme colors
const SECTION_CONFIG = {
  core: {
    key: 'primary',
  },
  management: {
    key: 'secondary',
  },
};

// Helper to get color value from theme
const getThemeColor = (theme, colorKey, variant = 'color') => {
  if (!theme) return '#3b82f6';
  const key = `${colorKey}_${variant}`;
  return theme[key] || '#3b82f6';
};

// Helper to generate section styles from theme
const getSectionStyles = (theme, colorKey) => {
  const baseColor = getThemeColor(theme, colorKey, 'color');
  const hoverColor = getThemeColor(theme, colorKey, 'hover');
  const lightColor = getThemeColor(theme, colorKey, 'light');
  
  return {
    gradient: `from-[${baseColor}] to-[${hoverColor}]`,
    bgLight: `bg-[${lightColor}]`,
    bgDark: `dark:bg-[${lightColor}]`,
    borderColor: `border-[${baseColor}]/30 dark:border-[${baseColor}]/30`,
    iconColor: `text-[${baseColor}] dark:text-[${baseColor}]`,
    ringColor: `ring-[${baseColor}]/30`,
  };
};

export default function LicenseSetting({ 
  licenseStatus, 
  licenseLoading, 
  fetchLicenseStatus,
  themeColors,
  primaryTextColor
}) {
  const { isDark } = useTheme();
  
  // Use passed themeColors if available, otherwise use default
  const colors = themeColors || {
    primary: '#3b82f6',
    primaryHover: '#2563eb',
    primaryLight: '#dbeafe',
    secondary: '#8b5cf6',
    secondaryHover: '#7c3aed',
    secondaryLight: '#ede9fe',
  };
  
  // Use passed primaryTextColor if available, otherwise calculate
  const textColor = primaryTextColor || getContrastText(colors.primaryHover || colors.primary);
  
  const { loading: permsLoading, canFor } = usePermissions();
  const [showPasswordModal, setShowPasswordModal] = useState(false);
  const [passwordAction, setPasswordAction] = useState(null);
  const [password, setPassword] = useState("");
  const [verifying, setVerifying] = useState(false);
  const [showLicenseDetails, setShowLicenseDetails] = useState(false);

  // 🎨 Modern button palette
  const btnOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 transition-all duration-200";
  const btnAmber  = "bg-gradient-to-br from-amber-500 to-amber-600 text-white shadow-lg shadow-amber-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-amber-500/30 hover:scale-[1.02] transition-all duration-200";

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
      <div className="p-6">
        <div className={`p-4 rounded-xl ${isDark ? "bg-slate-800" : "bg-white"}`}>Checking permissions…</div>
      </div>
    );
  }

  return (
    <div className="p-4 space-y-3">
{/* ===== License Management ===== */}
      <GlassCard>
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.primary}, ${colors.primaryHover})` }}
            >
              <KeyIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-gray-900 dark:text-white">License Management</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">
                {licenseStatus?.valid ? "Active" : "No Active License"}
              </p>
            </div>
          </div>
          <GlassBtn
            onClick={() => openPasswordModal("view-details")}
            className="h-8 px-3"
            style={{
              background: `linear-gradient(to bottom right, ${colors.secondary}, ${colors.secondaryHover})`,
              color: textColor,
              boxShadow: `0 2px 8px 0 ${colors.secondary}40`
            }}
          >
            <span className="inline-flex items-center gap-1 text-xs">
              <ShieldCheckIcon className="w-4 h-4" />
              View Details
            </span>
          </GlassBtn>
        </div>

        <GlassToolbar className="grid grid-cols-1 md:grid-cols-2 gap-3 p-4">
          {/* License Status */}
          <div className={`flex items-center gap-3 p-4 rounded-xl ${isDark ? "bg-slate-800/60" : "bg-white/60"}`}>
            {licenseLoading ? (
              <div className={`animate-pulse ${isDark ? "text-slate-500" : "text-gray-400"}`}>Loading license status...</div>
            ) : licenseStatus?.valid ? (
              <>
                <div className={`w-12 h-12 rounded-full flex items-center justify-center ${isDark ? "bg-emerald-900/50" : "bg-emerald-100"}`}>
                  <ShieldCheckIcon className={`w-6 h-6 ${isDark ? "text-emerald-400" : "text-emerald-600"}`} />
                </div>
                <div>
                  <div className={`font-medium ${isDark ? "text-emerald-400" : "text-emerald-700"}`}>License Active</div>
                  <div className={`text-sm ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                    {formatExpiryDate(licenseStatus.expires_at) || "No expiration date"}
                  </div>
                </div>
              </>
            ) : (
              <>
                <div className={`w-12 h-12 rounded-full flex items-center justify-center ${isDark ? "bg-red-900/50" : "bg-red-100"}`}>
                  <ShieldExclamationIcon className={`w-6 h-6 ${isDark ? "text-red-400" : "text-red-600"}`} />
                </div>
                <div>
                  <div className={`font-medium ${isDark ? "text-red-400" : "text-red-700"}`}>No Active License</div>
                  <div className={`text-sm ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                    {licenseStatus?.reason || "License not found or expired"}
                  </div>
                </div>
              </>
            )}
          </div>

          {/* Machine ID */}
          <div className={`p-4 rounded-xl ${isDark ? "bg-slate-800/60" : "bg-white/60"}`}>
            <div className="flex items-center justify-between mb-2">
              <span className={`text-sm font-medium ${isDark ? "text-slate-300" : "text-gray-700"}`}>Machine ID</span>
              <button
                onClick={copyMachineId}
                className={`inline-flex items-center gap-1 text-xs ${isDark ? "text-blue-400" : "text-blue-600"} hover:underline`}
              >
                <ClipboardDocumentIcon className="w-4 h-4" />
                Copy
              </button>
            </div>
            <div className={`text-xs font-mono p-2 rounded break-all ${isDark ? "bg-slate-900 text-slate-400" : "bg-gray-50 text-gray-600"}`}>
              {licenseStatus?.machine_id || "Unable to load"}
            </div>
          </div>

          {/* License Details - Protected Section */}
          {showLicenseDetails && licenseStatus?.valid && (
            <div className="col-span-1 md:col-span-2 mt-2">
              <div className={`p-4 rounded-xl border ${isDark ? "bg-slate-800/60 border-slate-600" : "bg-white/60 border-gray-200"}`}>
                <div className="flex items-center justify-between mb-4">
                  <div className="flex items-center gap-2">
                    <DocumentTextIcon className={`w-5 h-5 ${isDark ? "text-blue-400" : "text-blue-600"}`} />
                    <span className={`font-semibold ${isDark ? "text-slate-200" : "text-gray-800"}`}>License Details</span>
                    <span className={`px-2 py-0.5 text-xs rounded-full ${isDark ? "bg-green-900/50 text-green-400" : "bg-green-100 text-green-700"}`}>
                      <CheckBadgeIcon className="w-3.5 h-3.5 inline mr-1" />
                      Verified
                    </span>
                  </div>
                  <GlassBtn
                    onClick={() => setShowLicenseDetails(false)}
                    className={`h-7 px-3 text-xs ${isDark ? "bg-slate-700 text-slate-300" : "bg-gray-100 text-gray-600"}`}
                  >
                    Hide
                  </GlassBtn>
                </div>
                
                <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
                  {/* Expiry Date */}
                  <div className={`flex items-center gap-2 p-2 rounded-lg ${isDark ? "bg-slate-700/50" : "bg-gray-50"}`}>
                    <CalendarIcon className={`w-4 h-4 ${isDark ? "text-amber-400" : "text-amber-600"}`} />
                    <div>
                      <div className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>Expires</div>
                      <div className={`text-sm font-medium ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                        {formatExpiryDate(licenseStatus.expires_at) || "Lifetime"}
                      </div>
                    </div>
                  </div>

                  {/* Days Remaining */}
                  {licenseStatus.expires_at && (
                    <div className={`flex items-center gap-2 p-2 rounded-lg ${isDark ? "bg-slate-700/50" : "bg-gray-50"}`}>
                      <ClockIcon className={`w-4 h-4 ${isDark ? "text-emerald-400" : "text-emerald-600"}`} />
                      <div>
                        <div className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>Days Remaining</div>
                        <div className={`text-sm font-medium ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                          {Math.max(0, Math.ceil((Number(licenseStatus.expires_at) * 1000 - Date.now()) / (1000 * 60 * 60 * 24)))} days
                        </div>
                      </div>
                    </div>
                  )}

                  {/* Dynamic payload fields */}
                  {getPayloadEntries().map(([key, value]) => (
                    <div key={key} className={`flex items-start gap-2 p-2 rounded-lg ${isDark ? "bg-slate-700/50" : "bg-gray-50"}`}>
                      {key.toLowerCase().includes('owner') || key.toLowerCase().includes('company') ? (
                        <BuildingOfficeIcon className={`w-4 h-4 ${isDark ? "text-blue-400" : "text-blue-600"} mt-0.5`} />
                      ) : key.toLowerCase().includes('type') || key.toLowerCase().includes('edition') ? (
                        <ShieldCheckIcon className={`w-4 h-4 ${isDark ? "text-purple-400" : "text-purple-600"} mt-0.5`} />
                      ) : (
                        <CpuChipIcon className={`w-4 h-4 ${isDark ? "text-slate-400" : "text-gray-500"} mt-0.5`} />
                      )}
                      <div>
                        <div className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                          {key.replace(/_/g, ' ').replace(/\b\w/g, l => l.toUpperCase())}
                        </div>
                        <div className={`text-sm font-medium break-words ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                          {formatPayloadValue(value)}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}
        </GlassToolbar>
      </GlassCard>

      {/* ===== Password Verification Modal ===== */}
      {showPasswordModal && (
        <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className={`w-full max-w-md rounded-2xl shadow-2xl ${isDark ? "bg-slate-800 border-slate-600" : "bg-white border-gray-200"} border`}>
            <div className={`p-6 border-b ${isDark ? "border-slate-600" : "border-gray-200"}`}>
              <div className="flex items-center gap-3">
                <div className={`w-10 h-10 rounded-full flex items-center justify-center ${isDark ? "bg-slate-700" : "bg-gray-100"}`}>
                  <LockClosedIcon className={`w-5 h-5 ${isDark ? "text-slate-400" : "text-slate-600"}`} />
                </div>
                <div>
                  <h3 className={`font-semibold ${isDark ? "text-slate-100" : "text-gray-800"}`}>
                    {passwordAction === "deactivate" ? "Deactivate License" : "View License Details"}
                  </h3>
                  <p className={`text-sm ${isDark ? "text-slate-400" : "text-gray-500"}`}>Enter your password to continue</p>
                </div>
              </div>
            </div>
            <form onSubmit={handlePasswordVerify} className="p-6">
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter your password"
                className={`w-full px-4 py-3 rounded-xl border focus:ring-2 focus:ring-blue-400 outline-none ${
                  isDark 
                    ? "bg-slate-700 border-slate-600 text-slate-100" 
                    : "bg-white border-gray-200 text-gray-900"
                }`}
                autoFocus
              />
              <div className="flex justify-end gap-2 mt-4">
                <button
                  type="button"
                  onClick={closePasswordModal}
                  disabled={verifying}
                  className={`px-4 py-2 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700 text-slate-300" : "hover:bg-gray-100 text-gray-600"}`}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={verifying || !password}
                  className={`px-4 py-2 rounded-lg flex items-center gap-2 ${isDark ? "bg-blue-600 hover:bg-blue-700 text-white" : "bg-blue-600 hover:bg-blue-700 text-white"} disabled:opacity-60`}
                >
                  {verifying ? "Verifying…" : "Verify"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}

