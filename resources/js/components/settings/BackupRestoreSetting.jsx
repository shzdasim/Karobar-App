// resources/js/components/settings/BackupRestoreSetting.jsx
import { useState, useEffect, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import "filepond/dist/filepond.min.css";
import { usePermissions, Guard } from "@/api/usePermissions";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassBtn } from "@/components/glass.jsx";
import { 
  CloudArrowDownIcon,
  CloudArrowUpIcon,
  ArrowPathIcon,
  ServerIcon,
  FolderIcon,
  CogIcon,
  CheckCircleIcon,
  XCircleIcon,
  TrashIcon,
  DocumentIcon,
  ExclamationTriangleIcon,
  DocumentTextIcon,
} from "@heroicons/react/24/solid";

registerPlugin(FilePondPluginImagePreview);

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

export default function BackupRestoreSetting({ 
  themeColors,
  primaryTextColor,
  emeraldTextColor
}) {
  const { isDark, theme } = useTheme();
  
  // Use passed themeColors if available, otherwise use default
  const colors = themeColors || {
    primary: '#3b82f6',
    primaryHover: '#2563eb',
    primaryLight: '#dbeafe',
    secondary: '#8b5cf6',
    secondaryHover: '#7c3aed',
    secondaryLight: '#ede9fe',
    emerald: '#10b981',
    emeraldHover: '#059669',
    emeraldLight: '#d1fae5',
    amber: '#f59e0b',
    amberHover: '#d97706',
    amberLight: '#fef3c7',
  };
  
  // Use passed text colors if available, otherwise calculate
  const textColor = primaryTextColor || getContrastText(colors.primaryHover || colors.primary);
  const emeraldColor = emeraldTextColor || getContrastText(colors.emeraldHover || colors.emerald);

  // Get button style from theme
  const buttonStyle = theme?.button_style || 'rounded';
  
  // Get button style classes and styles based on theme button_style
  const getButtonClasses = useMemo(() => {
    const radiusMap = {
      'rounded': 'rounded-lg',
      'outlined': 'rounded-lg',
      'soft': 'rounded-xl',
    };
    const radiusClass = radiusMap[buttonStyle] || 'rounded-lg';
    
    if (buttonStyle === 'outlined') {
      return {
        primary: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: colors.primary,
            color: colors.primary,
            backgroundColor: 'transparent',
          }
        },
        emerald: {
          className: `${radiusClass} border-2 transition-all duration-200`,
          style: {
            borderColor: colors.emerald,
            color: colors.emerald,
            backgroundColor: 'transparent',
          }
        },
      };
    }
    
    // Filled styles for rounded and soft
    return {
      primary: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${colors.primary}, ${colors.primaryHover})`,
          color: textColor,
          boxShadow: `0 4px 14px 0 ${colors.primary}40`,
        }
      },
      emerald: {
        className: radiusClass,
        style: {
          background: `linear-gradient(to bottom right, ${colors.emerald}, ${colors.emeraldHover})`,
          color: emeraldColor,
          boxShadow: `0 4px 14px 0 ${colors.emerald}40`,
        }
      },
    };
  }, [buttonStyle, colors, textColor, emeraldColor]);

  const btnPrimary = getButtonClasses.primary;
  const btnEmerald = getButtonClasses.emerald;
  
  const { loading: permsLoading, canFor } = usePermissions();
  
  const can = useState(() => 
    typeof canFor === "function" ? canFor("backup") : {
      view: false, create: false, delete: false, upload: false, restore: false
    }
  )[0];

  // Backup management state
  const [backups, setBackups] = useState([]);
  const [backupStats, setBackupStats] = useState(null);
  const [backupsLoading, setBackupsLoading] = useState(false);
  const [creatingBackup, setCreatingBackup] = useState(false);
  const [selectedBackupType, setSelectedBackupType] = useState("full");
  const [showRestoreModal, setShowRestoreModal] = useState(false);
  const [backupToRestore, setBackupToRestore] = useState(null);
  const [restorePassword, setRestorePassword] = useState("");
  const [restoring, setRestoring] = useState(false);
  const [deletingBackupId, setDeletingBackupId] = useState(null);

  // Upload backup state
  const [uploadFiles, setUploadFiles] = useState([]);
  const [uploading, setUploading] = useState(false);
  const [showUploadModal, setShowUploadModal] = useState(false);

  // Backup types
  const backupTypes = [
    { id: 'full', name: 'Full Backup', description: 'Complete backup including database, settings, and files', icon: ServerIcon },
    { id: 'database', name: 'Database Only', description: 'Database tables and data only', icon: FolderIcon },
    { id: 'settings', name: 'Settings Only', description: 'Application settings and configuration', icon: CogIcon },
  ];

  // 🎨 Modern button palette
  const btnOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 hover:shadow-md transition-all duration-200";
  const btnBlue   = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] transition-all duration-200";
  const btnGreen  = "bg-gradient-to-br from-emerald-500 to-emerald-600 text-white shadow-lg shadow-emerald-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-emerald-500/30 hover:scale-[1.02] transition-all duration-200";
  const btnAmber  = "bg-gradient-to-br from-amber-500 to-amber-600 text-white shadow-lg shadow-amber-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-amber-500/30 hover:scale-[1.02] transition-all duration-200";

  // Fetch backups list
  const fetchBackups = async () => {
    try {
      setBackupsLoading(true);
      const { data } = await axios.get("/api/backups");
      setBackups(data.backups || []);
    } catch (err) {
      if (err.response?.status !== 403) {
        toast.error("Failed to load backups");
      }
    } finally {
      setBackupsLoading(false);
    }
  };

  // Fetch backup statistics
  const fetchBackupStats = async () => {
    try {
      const { data } = await axios.get("/api/backups/stats");
      setBackupStats(data);
    } catch (err) {
      // Silently fail - stats are optional
    }
  };

  // Create new backup
  const handleCreateBackup = async () => {
    if (!can.create) {
      toast.error("You don't have permission to create backups.");
      return;
    }

    try {
      setCreatingBackup(true);
      const { data } = await axios.post("/api/backups", {
        type: selectedBackupType,
      });
      toast.success("Backup created successfully!");
      await fetchBackups();
      await fetchBackupStats();
    } catch (error) {
      const msg = error.response?.data?.message || error.response?.data?.error || "Failed to create backup";
      toast.error(msg);
    } finally {
      setCreatingBackup(false);
    }
  };

  // Download backup
  const handleDownloadBackup = async (backup) => {
    if (!can.view) {
      toast.error("You don't have permission to view backups.");
      return;
    }

    try {
      const response = await axios.get(`/api/backups/${backup.id}/download`, {
        responseType: 'blob',
      });
      const url = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement('a');
      link.href = url;
      const filename = `${backup.filename}.${backup.type === 'full' ? 'zip' : 'sql.gz'}`;
      link.setAttribute('download', filename);
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
      toast.success("Backup download started");
    } catch (error) {
      toast.error("Failed to download backup");
    }
  };

  // Open restore modal
  const openRestoreModal = (backup) => {
    setBackupToRestore(backup);
    setRestorePassword("");
    setShowRestoreModal(true);
  };

  // Close restore modal
  const closeRestoreModal = () => {
    setShowRestoreModal(false);
    setBackupToRestore(null);
    setRestorePassword("");
  };

  // Restore from backup
  const handleRestoreBackup = async (e) => {
    e.preventDefault();
    if (!backupToRestore) return;

    try {
      setRestoring(true);
      await axios.post(`/api/backups/${backupToRestore.id}/restore`, {
        password: restorePassword,
      });
      toast.success("Backup restored successfully! Please refresh the page.");
      closeRestoreModal();
      await fetchBackups();
      await fetchBackupStats();
    } catch (error) {
      const msg = error.response?.data?.message || error.response?.data?.error || "Restore failed. Check your password.";
      toast.error(msg);
    } finally {
      setRestoring(false);
    }
  };

  // Delete backup
  const handleDeleteBackup = async (backup) => {
    if (!can.delete) {
      toast.error("You don't have permission to delete backups.");
      return;
    }

    if (!confirm(`Are you sure you want to delete "${backup.filename}"? This action cannot be undone.`)) {
      return;
    }

    try {
      setDeletingBackupId(backup.id);
      await axios.delete(`/api/backups/${backup.id}`);
      toast.success("Backup deleted successfully");
      await fetchBackups();
      await fetchBackupStats();
    } catch (error) {
      const msg = error.response?.data?.message || "Failed to delete backup";
      toast.error(msg);
    } finally {
      setDeletingBackupId(null);
    }
  };

  // Upload backup file
  const handleUploadBackup = async () => {
    if (!can.upload) {
      toast.error("You don't have permission to upload backups.");
      return;
    }

    if (uploadFiles.length === 0 || !uploadFiles[0].file) {
      toast.error("Please select a backup file to upload.");
      return;
    }

    try {
      setUploading(true);
      const formData = new FormData();
      formData.append('file', uploadFiles[0].file);

      const { data } = await axios.post('/api/backups/upload', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });

      toast.success("Backup uploaded successfully!");
      await fetchBackups();
      await fetchBackupStats();
      setUploadFiles([]);
      setShowUploadModal(false);
      openRestoreModal(data.backup);
    } catch (error) {
      const errors = error.response?.data?.errors;
      const msg = error.response?.data?.message || error.response?.data?.error || "Failed to upload backup";
      if (errors && Array.isArray(errors) && errors.length > 0) {
        errors.forEach((err) => toast.error(err));
      } else {
        toast.error(msg);
      }
    } finally {
      setUploading(false);
    }
  };

  // Get status icon for backup
  const getBackupStatusIcon = (status) => {
    switch (status) {
      case 'completed':
        return <CheckCircleIcon className="w-5 h-5 text-emerald-500" />;
      case 'failed':
        return <XCircleIcon className="w-5 h-5 text-red-500" />;
      case 'restored':
        return <CheckCircleIcon className="w-5 h-5 text-blue-500" />;
      default:
        return <ArrowPathIcon className="w-5 h-5 text-amber-500 animate-spin" />;
    }
  };

  // Load data on mount
  useEffect(() => {
    fetchBackups();
    fetchBackupStats();
  }, []);

  if (permsLoading) {
    return (
      <div className="p-6">
        <div className={`p-4 rounded-xl ${isDark ? "bg-slate-800" : "bg-white"}`}>Checking permissions…</div>
      </div>
    );
  }

  return (
    <div className="p-4 space-y-3">
{/* ===== Backup Statistics ===== */}
      <GlassCard>
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.primary}, ${colors.primaryHover})` }}
            >
              <CloudArrowDownIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-gray-900 dark:text-white">Backup & Recovery</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">{backups.length} backups</p>
            </div>
          </div>
          <GlassBtn onClick={() => { fetchBackups(); fetchBackupStats(); }} className="h-8 px-3">
            <ArrowPathIcon className="w-4 h-4" />
          </GlassBtn>
        </div>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-2 p-4">
          <div className={`rounded-xl p-3 text-center ${isDark ? "bg-slate-800/60" : "bg-white/60"}`}>
            <div className={`text-xl font-bold ${isDark ? "text-slate-200" : "text-gray-800"}`}>
              {backupStats?.total_backups || 0}
            </div>
            <div className={`text-[10px] uppercase tracking-wider ${isDark ? "text-slate-400" : "text-gray-500"}`}>Total</div>
          </div>
          <div className={`rounded-xl p-3 text-center ${isDark ? "bg-slate-800/60" : "bg-white/60"}`}>
            <div className={`text-xl font-bold text-emerald-500`}>
              {backupStats?.completed_backups || 0}
            </div>
            <div className={`text-[10px] uppercase tracking-wider ${isDark ? "text-slate-400" : "text-gray-500"}`}>Completed</div>
          </div>
          <div className={`rounded-xl p-3 text-center ${isDark ? "bg-slate-800/60" : "bg-white/60"}`}>
            <div className={`text-xl font-bold text-red-500`}>
              {backupStats?.failed_backups || 0}
            </div>
            <div className={`text-[10px] uppercase tracking-wider ${isDark ? "text-slate-400" : "text-gray-500"}`}>Failed</div>
          </div>
          <div className={`rounded-xl p-3 text-center ${isDark ? "bg-slate-800/60" : "bg-white/60"}`}>
            <div className={`text-xl font-bold text-blue-500`}>
              {backupStats?.formatted_total_size || '0 B'}
            </div>
            <div className={`text-[10px] uppercase tracking-wider ${isDark ? "text-slate-400" : "text-gray-500"}`}>Size</div>
          </div>
        </div>
      </GlassCard>

{/* ===== Create Backup ===== */}
      <GlassCard>
        <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-100 dark:border-slate-700">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.secondary}, ${colors.secondaryHover})` }}
            >
              <CloudArrowUpIcon className="w-5 h-5 text-white" />
            </div>
          <div>
            <h2 className="text-base font-semibold text-gray-900 dark:text-white">Create New Backup</h2>
            <p className="text-xs text-gray-500 dark:text-gray-400">Choose backup type and create</p>
          </div>
        </div>

        <GlassToolbar className="grid grid-cols-1 md:grid-cols-3 gap-3 p-4">
          {backupTypes.map((type) => {
            const IconComponent = type.icon;
            const isSelected = selectedBackupType === type.id;

            return (
              <div
                key={type.id}
                className={`relative p-4 rounded-xl border-2 cursor-pointer transition-all ${
                  isSelected
                    ? "border-blue-500 bg-blue-50/50 dark:bg-blue-900/20"
                    : "border-gray-200 hover:border-gray-300 dark:border-slate-600 dark:hover:border-slate-500"
                }`}
                onClick={() => setSelectedBackupType(type.id)}
              >
                {isSelected && (
                  <div className="absolute top-2 right-2">
                    <div className="w-5 h-5 rounded-full bg-blue-500 flex items-center justify-center">
                      <svg className="w-3 h-3 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="3" d="M5 13l4 4L19 7" />
                      </svg>
                    </div>
                  </div>
                )}

                <div className="flex items-start gap-3">
                  <div className={`p-2 rounded-lg ${isSelected ? "bg-blue-100 dark:bg-blue-800" : "bg-gray-100 dark:bg-slate-700"}`}>
                    <IconComponent className={`w-6 h-6 ${isSelected ? "text-blue-600" : "text-gray-600 dark:text-gray-300"}`} />
                  </div>
                  <div className="flex-1">
                    <h4 className={`font-medium ${isDark ? "text-slate-200" : "text-gray-800"}`}>{type.name}</h4>
                    <p className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"} mt-1`}>{type.description}</p>
                  </div>
                </div>
              </div>
            );
          })}
        </GlassToolbar>

        <div className="flex justify-end px-4 pb-4">
          <GlassBtn
            onClick={handleCreateBackup}
            disabled={creatingBackup || !can.create}
            className={`h-10 px-6 ${btnEmerald.className}`}
            style={btnEmerald.style}
          >
            <span className="inline-flex items-center gap-2">
              {creatingBackup ? (
                <ArrowPathIcon className="w-5 h-5 animate-spin" />
              ) : (
                <CloudArrowUpIcon className="w-5 h-5" />
              )}
              {creatingBackup ? "Creating..." : "Create Backup"}
            </span>
          </GlassBtn>
        </div>
      </GlassCard>

{/* ===== Upload Backup ===== */}
      <GlassCard>
        <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-100 dark:border-slate-700">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.primary}, ${colors.primaryHover})` }}
            >
              <DocumentTextIcon className="w-5 h-5 text-white" />
            </div>
          <div>
            <h2 className="text-base font-semibold text-gray-900 dark:text-white">Upload Backup</h2>
            <p className="text-xs text-gray-500 dark:text-gray-400">Restore from a backup file</p>
          </div>
        </div>

        <div className="p-4 space-y-3">
          <div className={`flex items-start gap-3 p-3 rounded-xl border ${isDark ? "bg-blue-900/20 border-blue-800" : "bg-blue-50 border-blue-200"}`}>
            <div className={`p-2 rounded-lg ${isDark ? "bg-blue-800" : "bg-blue-100"}`}>
              <CloudArrowDownIcon className={`w-5 h-5 ${isDark ? "text-blue-400" : "text-blue-600"}`} />
            </div>
            <div>
              <h4 className={`font-medium ${isDark ? "text-blue-300" : "text-blue-800"}`}>Restore from Backup File</h4>
              <p className={`text-xs ${isDark ? "text-blue-400" : "text-blue-700"}`}>
                Upload a backup file (.zip, .sql.gz) that was previously downloaded.
              </p>
            </div>
          </div>

          <div className={`rounded-xl bg-white/60 backdrop-blur-sm ring-1 p-4 ${isDark ? "ring-slate-600/60" : "ring-gray-200/60"}`}>
            <FilePond
              files={uploadFiles}
              onupdatefiles={(fl) => {
                if (!can.upload) { 
                  toast.error("No permission to upload backups."); 
                  setUploadFiles([]);
                  return; 
                }
                setUploadFiles(fl);
              }}
              allowMultiple={false}
              acceptedFileTypes={['application/zip', 'application/x-zip-compressed', 'application/gzip', 'application/x-gzip', 'application/octet-stream']}
              disabled={!can.upload || uploading}
              labelIdle='Drag & Drop backup file or <span class="filepond--label-action">Browse</span>'
              labelFileTypeNotAllowed='Only .zip, .sql.gz, .gz files are allowed'
              credits={false}
              maxFileSize="500MB"
            />
            <p className={`text-xs mt-2 ${isDark ? "text-slate-400" : "text-gray-500"}`}>
              Supported formats: .zip, .sql.gz, .gz. Max size: 500MB
            </p>
          </div>

          {uploadFiles.length > 0 && uploadFiles[0] && (
            <div className={`flex items-center justify-between p-3 rounded-xl border ${isDark ? "bg-slate-800/50 border-slate-600" : "bg-gray-50 border-gray-200"}`}>
              <div className="flex items-center gap-3">
                <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${isDark ? "bg-blue-900/50" : "bg-blue-100"}`}>
                  <DocumentIcon className={`w-5 h-5 ${isDark ? "text-blue-400" : "text-blue-600"}`} />
                </div>
                <div>
                  <p className={`text-sm font-medium ${isDark ? "text-slate-200" : "text-gray-700"} truncate max-w-xs`}>
                    {uploadFiles[0].file?.name || uploadFiles[0].filename}
                  </p>
                  <p className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                    {(uploadFiles[0].file?.size / 1024 / 1024).toFixed(2)} MB
                  </p>
                </div>
              </div>
              <button
                onClick={() => setUploadFiles([])}
                disabled={uploading}
                className={`p-2 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700" : "hover:bg-gray-200"}`}
              >
                <XCircleIcon className="w-5 h-5 text-gray-500" />
              </button>
            </div>
          )}

          <div className="flex justify-end">
            <GlassBtn
              onClick={handleUploadBackup}
              disabled={uploadFiles.length === 0 || uploading || !can.upload}
              className={`h-10 px-6 ${btnPrimary.className}`}
              style={btnPrimary.style}
            >
              <span className="inline-flex items-center gap-2">
                {uploading ? <ArrowPathIcon className="w-5 h-5 animate-spin" /> : <CloudArrowUpIcon className="w-5 h-5" />}
                {uploading ? "Uploading..." : "Upload & Preview"}
              </span>
            </GlassBtn>
          </div>
        </div>
      </GlassCard>

{/* ===== Backup List ===== */}
      <GlassCard>
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div 
              className="p-2 rounded-lg shadow-sm"
              style={{ background: `linear-gradient(to bottom right, ${colors.secondary}, ${colors.secondaryHover})` }}
            >
              <ServerIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-gray-900 dark:text-white">Backup History</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">{backups.length} entries</p>
            </div>
          </div>
        </div>

        {backupsLoading ? (
          <div className="p-8 text-center">
            <ArrowPathIcon className={`w-8 h-8 mx-auto animate-spin ${isDark ? "text-slate-400" : "text-gray-400"}`} />
            <p className={`mt-2 ${isDark ? "text-slate-400" : "text-gray-500"}`}>Loading backups...</p>
          </div>
        ) : backups.length === 0 ? (
          <div className="p-8 text-center">
            <CloudArrowDownIcon className={`w-12 h-12 mx-auto ${isDark ? "text-slate-600" : "text-gray-300"}`} />
            <p className={`mt-2 ${isDark ? "text-slate-400" : "text-gray-500"}`}>No backups found</p>
            <p className={`text-xs ${isDark ? "text-slate-500" : "text-gray-400"}`}>Create your first backup above</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className={`sticky top-0 ${isDark ? "bg-slate-800" : "bg-gray-50"} z-10`}>
                <tr className={`text-left ${isDark ? "text-slate-300" : "text-gray-600"}`}>
                  <th className="px-4 py-2 font-semibold text-xs uppercase tracking-wider">Status</th>
                  <th className="px-4 py-2 font-semibold text-xs uppercase tracking-wider">Type</th>
                  <th className="px-4 py-2 font-semibold text-xs uppercase tracking-wider">Created</th>
                  <th className="px-4 py-2 font-semibold text-xs uppercase tracking-wider">Size</th>
                  <th className="px-4 py-2 font-semibold text-xs uppercase tracking-wider text-right">Actions</th>
                </tr>
              </thead>
              <tbody className={`divide-y ${isDark ? "divide-slate-700" : "divide-gray-100"}`}>
                {backups.map((backup) => (
                  <tr key={backup.id} className={`${isDark ? "hover:bg-slate-700/30" : "hover:bg-gray-50"}`}>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        {getBackupStatusIcon(backup.status)}
                        <span className={`text-sm capitalize ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                          {backup.status}
                        </span>
                      </div>
                      {backup.status === 'failed' && backup.error_message && (
                        <p className="text-xs text-red-500 mt-1 truncate max-w-xs">{backup.error_message}</p>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`px-2 py-1 rounded-full text-xs font-medium ${isDark ? "bg-blue-900/50 text-blue-300" : "bg-blue-100 text-blue-700"}`}>
                        {backup.type_label}
                      </span>
                    </td>
                    <td className={`px-4 py-3 text-sm ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                      {backup.created_at_formatted}
                    </td>
                    <td className={`px-4 py-3 text-sm ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                      {backup.formatted_size}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex items-center justify-end gap-1">
                        <button
                          onClick={() => handleDownloadBackup(backup)}
                          disabled={backup.status !== 'completed' || !can.view}
                          className={`p-2 rounded-lg transition-colors ${isDark ? "hover:bg-slate-600" : "hover:bg-gray-100"} disabled:opacity-50`}
                          title="Download"
                        >
                          <CloudArrowDownIcon className={`w-4 h-4 ${isDark ? "text-slate-300" : "text-gray-600"}`} />
                        </button>
                        <button
                          onClick={() => openRestoreModal(backup)}
                          disabled={backup.status !== 'completed' || !can.restore}
                          className={`p-2 rounded-lg transition-colors ${isDark ? "hover:bg-slate-600" : "hover:bg-gray-100"} disabled:opacity-50`}
                          title="Restore"
                        >
                          <ArrowPathIcon className={`w-4 h-4 text-amber-500`} />
                        </button>
                        <button
                          onClick={() => handleDeleteBackup(backup)}
                          disabled={deletingBackupId === backup.id || !can.delete}
                          className={`p-2 rounded-lg transition-colors ${isDark ? "hover:bg-red-900/20" : "hover:bg-red-50"} disabled:opacity-50`}
                          title="Delete"
                        >
                          {deletingBackupId === backup.id ? (
                            <ArrowPathIcon className="w-4 h-4 text-red-500 animate-spin" />
                          ) : (
                            <TrashIcon className="w-4 h-4 text-red-500" />
                          )}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </GlassCard>

      {/* ===== Important Notice ===== */}
      <div className={`rounded-xl p-4 border ${isDark ? "bg-amber-900/20 border-amber-800" : "bg-amber-50 border-amber-200"}`}>
        <div className="flex items-start gap-3">
          <ExclamationTriangleIcon className={`w-5 h-5 flex-shrink-0 mt-0.5 ${isDark ? "text-amber-400" : "text-amber-600"}`} />
          <div>
            <h4 className={`font-medium ${isDark ? "text-amber-300" : "text-amber-800"}`}>Important: Backup & Recovery</h4>
            <ul className={`mt-2 text-sm space-y-1 ${isDark ? "text-amber-400" : "text-amber-700"}`}>
              <li>• Restoring a backup will overwrite current data. Create a backup first if needed.</li>
              <li>• Password confirmation is required for restore operations.</li>
              <li>• Database backups are compressed with gzip to save space.</li>
              <li>• Old backups are automatically cleaned up (max 10 backups, 30 days retention).</li>
            </ul>
          </div>
        </div>
      </div>

      {/* ===== Restore Confirmation Modal ===== */}
      {showRestoreModal && backupToRestore && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className={`w-full max-w-md rounded-2xl shadow-2xl border ${isDark ? "bg-slate-800 border-slate-600" : "bg-white border-gray-200"}`}>
            <div className={`p-6 border-b ${isDark ? "border-slate-600" : "border-gray-200"}`}>
              <div className="flex items-center gap-3">
                <div className={`w-10 h-10 rounded-full flex items-center justify-center ${isDark ? "bg-amber-900/50" : "bg-amber-100"}`}>
                  <ArrowPathIcon className={`w-5 h-5 ${isDark ? "text-amber-400" : "text-amber-600"}`} />
                </div>
                <div>
                  <h3 className={`font-semibold ${isDark ? "text-slate-100" : "text-gray-800"}`}>Restore Backup</h3>
                  <p className={`text-sm ${isDark ? "text-slate-400" : "text-gray-500"}`}>This action cannot be undone</p>
                </div>
              </div>
            </div>

            <div className="p-6">
              <div className={`rounded-xl p-4 mb-4 border ${isDark ? "bg-amber-900/20 border-amber-800" : "bg-amber-50 border-amber-200"}`}>
                <p className={`text-sm ${isDark ? "text-amber-300" : "text-amber-800"}`}>
                  You are about to restore from backup:
                </p>
                <div className={`mt-2 space-y-1 text-xs ${isDark ? "text-amber-400" : "text-amber-700"}`}>
                  <p><strong>File:</strong> {backupToRestore.filename}</p>
                  <p><strong>Type:</strong> {backupToRestore.type_label}</p>
                  <p><strong>Created:</strong> {backupToRestore.created_at_formatted}</p>
                </div>
              </div>

              <form onSubmit={handleRestoreBackup}>
                <label className={`block text-sm font-medium mb-2 ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                  Enter your password to confirm
                </label>
                <input
                  type="password"
                  value={restorePassword}
                  onChange={(e) => setRestorePassword(e.target.value)}
                  placeholder="Enter your password"
                  className={`w-full px-4 py-3 rounded-xl border focus:ring-2 focus:ring-blue-400 outline-none ${
                    isDark 
                      ? "bg-slate-700 border-slate-600 text-slate-100" 
                      : "bg-white border-gray-200 text-gray-900"
                  }`}
                  autoFocus
                  required
                />
                <p className={`text-xs mt-2 ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  This ensures only authorized users can restore backups.
                </p>

                <div className="flex justify-end gap-2 mt-6">
                  <button
                    type="button"
                    onClick={closeRestoreModal}
                    disabled={restoring}
                    className={`px-4 py-2 rounded-lg transition-colors ${isDark ? "hover:bg-slate-600 text-slate-300" : "hover:bg-gray-100 text-gray-600"}`}
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={restoring || !restorePassword}
                    className={`px-4 py-2 rounded-lg flex items-center gap-2 ${isDark ? "bg-amber-600 hover:bg-amber-700 text-white" : "bg-amber-600 hover:bg-amber-700 text-white"} disabled:opacity-60`}
                  >
                    {restoring && <ArrowPathIcon className="w-4 h-4 animate-spin" />}
                    {restoring ? "Restoring..." : "Restore Backup"}
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

