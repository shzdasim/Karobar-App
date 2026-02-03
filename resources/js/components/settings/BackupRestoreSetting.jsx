import { useState, useEffect } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { 
  FilePond, 
  registerPlugin 
} from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import "filepond/dist/filepond.min.css";
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
  ExclamationTriangleIcon
} from "@heroicons/react/24/solid";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassBtn } from "@/components/glass.jsx";

registerPlugin(FilePondPluginImagePreview);

export default function BackupRestoreSetting({ 
  canFor,
  tintBlue,
  tintGlass,
  tintGreen
}) {
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
    if (!canFor?.("backup")?.create) {
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
    if (!canFor?.("backup")?.view) {
      toast.error("You don't have permission to view backups.");
      return;
    }

    try {
      const response = await axios.get(`/api/backups/${backup.id}/download`, {
        responseType: 'blob',
      });

      // Create download link
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
      // Refresh backup list and stats
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
    if (!canFor?.("backup")?.delete) {
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
    if (!canFor?.("backup")?.upload) {
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
        headers: {
          'Content-Type': 'multipart/form-data',
        },
      });

      toast.success("Backup uploaded successfully!");
      
      // Refresh backup list
      await fetchBackups();
      await fetchBackupStats();
      
      // Clear upload files
      setUploadFiles([]);
      
      // Open the restore modal for the uploaded file
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

  return (
    <>
      {/* ===== Backup Statistics ===== */}
      <GlassCard>
        <GlassSectionHeader
          title={<span className="inline-flex items-center gap-2">
            <CloudArrowDownIcon className="w-5 h-5 text-blue-600" />
            <span>Backup & Recovery</span>
          </span>}
          right={
            <GlassBtn
              onClick={() => { fetchBackups(); fetchBackupStats(); }}
              className={`h-8 px-3 ${tintGlass}`}
              title="Refresh backups"
            >
              <ArrowPathIcon className="w-4 h-4" />
            </GlassBtn>
          }
        />
        <GlassToolbar className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {/* Total Backups */}
          <div className="p-4 rounded-xl bg-white/60 dark:bg-slate-700/60 text-center">
            <div className="text-2xl font-bold text-gray-800 dark:text-gray-100">
              {backupStats?.total_backups || 0}
            </div>
            <div className="text-xs text-gray-500 dark:text-gray-400">Total Backups</div>
          </div>

          {/* Completed */}
          <div className="p-4 rounded-xl bg-white/60 dark:bg-slate-700/60 text-center">
            <div className="text-2xl font-bold text-emerald-600 dark:text-emerald-400">
              {backupStats?.completed_backups || 0}
            </div>
            <div className="text-xs text-gray-500 dark:text-gray-400">Completed</div>
          </div>

          {/* Failed */}
          <div className="p-4 rounded-xl bg-white/60 dark:bg-slate-700/60 text-center">
            <div className="text-2xl font-bold text-red-600 dark:text-red-400">
              {backupStats?.failed_backups || 0}
            </div>
            <div className="text-xs text-gray-500 dark:text-gray-400">Failed</div>
          </div>

          {/* Total Size */}
          <div className="p-4 rounded-xl bg-white/60 dark:bg-slate-700/60 text-center">
            <div className="text-2xl font-bold text-blue-600 dark:text-blue-400">
              {backupStats?.formatted_total_size || '0 B'}
            </div>
            <div className="text-xs text-gray-500 dark:text-gray-400">Total Size</div>
          </div>
        </GlassToolbar>
      </GlassCard>

      {/* ===== Create Backup ===== */}
      <GlassCard>
        <GlassSectionHeader
          title="Create New Backup"
          subtitle="Choose backup type and create a new backup"
        />
        <GlassToolbar className="grid grid-cols-1 md:grid-cols-3 gap-4">
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
                  <div className={`p-2 rounded-lg ${isSelected ? "bg-blue-100 dark:bg-blue-800" : "bg-gray-100 dark:bg-slate-600"}`}>
                    <IconComponent className={`w-6 h-6 ${isSelected ? "text-blue-600" : "text-gray-600 dark:text-gray-300"}`} />
                  </div>
                  <div className="flex-1">
                    <h4 className="font-medium text-gray-800 dark:text-gray-100">{type.name}</h4>
                    <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">{type.description}</p>
                  </div>
                </div>
              </div>
            );
          })}
        </GlassToolbar>

        <div className="flex justify-end mt-4">
          <GlassBtn
            onClick={handleCreateBackup}
            disabled={creatingBackup || !canFor?.("backup")?.create}
            className={`h-10 px-6 ${!canFor?.("backup")?.create ? tintGlass + " opacity-60 cursor-not-allowed" : tintGreen}`}
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
        <GlassSectionHeader
          title="Upload Backup"
          subtitle="Upload a previously downloaded backup file to restore"
        />
        <GlassToolbar className="space-y-4">
          {/* Upload Info */}
          <div className="flex items-start gap-3 p-4 rounded-xl bg-blue-50 dark:bg-blue-900/30 border border-blue-200 dark:border-blue-800">
            <div className="p-2 rounded-lg bg-blue-100 dark:bg-blue-800">
              <CloudArrowDownIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            </div>
            <div>
              <h4 className="font-medium text-blue-800 dark:text-blue-300">Restore from Backup File</h4>
              <p className="text-sm text-blue-700 dark:text-blue-400 mt-1">
                Upload a backup file (.zip, .sql.gz) that was previously downloaded from this system.
              </p>
            </div>
          </div>

          {/* FilePond Upload Area */}
          <div className="rounded-2xl bg-white/60 backdrop-blur-sm ring-1 ring-gray-200/60 p-4 shadow-sm dark:bg-slate-700/60 dark:ring-slate-600/60">
            <FilePond
              files={uploadFiles}
              onupdatefiles={(fl) => {
                if (!canFor?.("backup")?.upload) { 
                  toast.error("No permission to upload backups."); 
                  setUploadFiles([]);
                  return; 
                }
                setUploadFiles(fl);
              }}
              allowMultiple={false}
              acceptedFileTypes={['application/zip', 'application/x-zip-compressed', 'application/gzip', 'application/x-gzip', 'application/octet-stream']}
              allowFileTypeValidation={false}
              disabled={!canFor?.("backup")?.upload || uploading}
              labelIdle='Drag & Drop backup file or <span class="filepond--label-action">Browse</span>'
              labelFileTypeNotAllowed='Only .zip, .sql.gz, .gz files are allowed'
              credits={false}
              maxFileSize="500MB"
            />
            <p className="text-xs text-gray-500 mt-2 dark:text-gray-400">
              Supported formats: .zip (full backup), .sql.gz, .gz (database backup). Max size: 500MB
            </p>
          </div>

          {/* Upload Actions */}
          {uploadFiles.length > 0 && uploadFiles[0] && (
            <div className="flex items-center justify-between p-4 rounded-xl bg-gray-50 dark:bg-slate-700/50 border border-gray-200 dark:border-slate-600">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-lg bg-blue-100 dark:bg-blue-900/50 flex items-center justify-center">
                  <DocumentIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
                </div>
                <div>
                  <p className="text-sm font-medium text-gray-700 dark:text-gray-200 truncate max-w-xs">
                    {uploadFiles[0].file?.name || uploadFiles[0].filename}
                  </p>
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    {(uploadFiles[0].file?.size / 1024 / 1024).toFixed(2)} MB
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setUploadFiles([])}
                  disabled={uploading}
                  className="p-2 rounded-lg hover:bg-gray-200 dark:hover:bg-slate-600 transition-colors"
                  title="Remove file"
                >
                  <XCircleIcon className="w-5 h-5 text-gray-500" />
                </button>
              </div>
            </div>
          )}

          {/* Upload Button */}
          <div className="flex justify-end">
            <GlassBtn
              onClick={() => setShowUploadModal(true)}
              disabled={uploadFiles.length === 0 || uploading || !canFor?.("backup")?.upload}
              className={`h-10 px-6 ${
                !canFor?.("backup")?.upload || uploadFiles.length === 0 || uploading
                  ? tintGlass + " opacity-60 cursor-not-allowed"
                  : tintBlue
              }`}
            >
              <span className="inline-flex items-center gap-2">
                {uploading ? (
                  <ArrowPathIcon className="w-5 h-5 animate-spin" />
                ) : (
                  <CloudArrowUpIcon className="w-5 h-5" />
                )}
                {uploading ? "Uploading..." : "Upload & Preview"}
              </span>
            </GlassBtn>
          </div>
        </GlassToolbar>
      </GlassCard>

      {/* ===== Backup List ===== */}
      <GlassCard>
        <GlassSectionHeader
          title="Backup History"
          subtitle="Download, restore, or delete existing backups"
        />
        {backupsLoading ? (
          <div className="p-8 text-center">
            <ArrowPathIcon className="w-8 h-8 text-gray-400 animate-spin mx-auto" />
            <p className="text-gray-500 mt-2">Loading backups...</p>
          </div>
        ) : backups.length === 0 ? (
          <div className="p-8 text-center">
            <CloudArrowDownIcon className="w-12 h-12 text-gray-300 mx-auto" />
            <p className="text-gray-500 mt-2">No backups found</p>
            <p className="text-xs text-gray-400">Create your first backup above</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-gray-200 dark:border-slate-600">
                  <th className="text-left py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Status</th>
                  <th className="text-left py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Type</th>
                  <th className="text-left py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Created</th>
                  <th className="text-left py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Size</th>
                  <th className="text-right py-3 px-4 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-slate-700">
                {backups.map((backup) => (
                  <tr key={backup.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30">
                    <td className="py-3 px-4">
                      <div className="flex items-center gap-2">
                        {getBackupStatusIcon(backup.status)}
                        <span className="text-sm capitalize text-gray-700 dark:text-gray-300">
                          {backup.status}
                        </span>
                      </div>
                      {backup.status === 'failed' && backup.error_message && (
                        <p className="text-xs text-red-500 mt-1 truncate max-w-xs">{backup.error_message}</p>
                      )}
                    </td>
                    <td className="py-3 px-4">
                      <span className="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-blue-100 text-blue-700 dark:bg-blue-900/50 dark:text-blue-300">
                        {backup.type_label}
                      </span>
                    </td>
                    <td className="py-3 px-4 text-sm text-gray-600 dark:text-gray-300">
                      {backup.created_at_formatted}
                    </td>
                    <td className="py-3 px-4 text-sm text-gray-600 dark:text-gray-300">
                      {backup.formatted_size}
                    </td>
                    <td className="py-3 px-4 text-right">
                      <div className="flex items-center justify-end gap-2">
                        {/* Download */}
                        <button
                          onClick={() => handleDownloadBackup(backup)}
                          disabled={backup.status !== 'completed' || !canFor?.("backup")?.view}
                          className="p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-slate-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                          title="Download backup"
                        >
                          <CloudArrowDownIcon className="w-4 h-4 text-gray-600 dark:text-gray-300" />
                        </button>

                        {/* Restore */}
                        <button
                          onClick={() => openRestoreModal(backup)}
                          disabled={backup.status !== 'completed' || !canFor?.("backup")?.restore}
                          className="p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-slate-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                          title="Restore backup"
                        >
                          <ArrowPathIcon className="w-4 h-4 text-amber-600 dark:text-amber-400" />
                        </button>

                        {/* Delete */}
                        <button
                          onClick={() => handleDeleteBackup(backup)}
                          disabled={deletingBackupId === backup.id || !canFor?.("backup")?.delete}
                          className="p-2 rounded-lg hover:bg-red-50 dark:hover:bg-red-900/20 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                          title="Delete backup"
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
      <div className="bg-amber-50 dark:bg-amber-900/20 rounded-xl p-4 border border-amber-200 dark:border-amber-800">
        <div className="flex items-start gap-3">
          <ExclamationTriangleIcon className="w-5 h-5 text-amber-600 dark:text-amber-400 flex-shrink-0 mt-0.5" />
          <div>
            <h4 className="font-medium text-amber-800 dark:text-amber-300">Important: Backup & Recovery</h4>
            <ul className="mt-2 text-sm text-amber-700 dark:text-amber-400 space-y-1">
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
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-2xl w-full max-w-md mx-4 border border-gray-200 dark:border-slate-600">
            {/* Header */}
            <div className="p-6 border-b border-gray-200 dark:border-slate-600">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-full bg-amber-100 dark:bg-amber-900/50 flex items-center justify-center">
                  <ArrowPathIcon className="w-5 h-5 text-amber-600 dark:text-amber-400" />
                </div>
                <div>
                  <h3 className="font-semibold text-gray-800 dark:text-gray-100">Restore Backup</h3>
                  <p className="text-sm text-gray-500 dark:text-gray-400">This action cannot be undone</p>
                </div>
              </div>
            </div>

            {/* Content */}
            <div className="p-6">
              <div className="bg-amber-50 dark:bg-amber-900/30 rounded-xl p-4 mb-4 border border-amber-200 dark:border-amber-800">
                <p className="text-sm text-amber-800 dark:text-amber-300">
                  You are about to restore from backup:
                </p>
                <div className="mt-2 space-y-1 text-xs text-amber-700 dark:text-amber-400">
                  <p><strong>File:</strong> {backupToRestore.filename}</p>
                  <p><strong>Type:</strong> {backupToRestore.type_label}</p>
                  <p><strong>Created:</strong> {backupToRestore.created_at_formatted}</p>
                </div>
              </div>

              <form onSubmit={handleRestoreBackup}>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  Enter your password to confirm
                </label>
                <input
                  type="password"
                  value={restorePassword}
                  onChange={(e) => setRestorePassword(e.target.value)}
                  placeholder="Enter your password"
                  className="w-full px-4 py-3 rounded-xl border border-gray-200 dark:border-slate-600 focus:border-blue-400 focus:ring-2 focus:ring-blue-100 outline-none transition-all dark:bg-slate-700 dark:text-gray-100 dark:placeholder-gray-400"
                  autoFocus
                  required
                />
                <p className="text-xs text-gray-500 dark:text-gray-400 mt-2">
                  This ensures only authorized users can restore backups.
                </p>

                <div className="flex justify-end gap-2 mt-6">
                  <button
                    type="button"
                    onClick={closeRestoreModal}
                    className="px-4 py-2 rounded-lg text-gray-600 hover:bg-gray-100 transition-colors dark:text-gray-300 dark:hover:bg-slate-600"
                    disabled={restoring}
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={restoring || !restorePassword}
                    className="px-4 py-2 rounded-lg bg-amber-600 hover:bg-amber-700 text-white transition-colors disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-2"
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

      {/* ===== Upload Confirmation Modal ===== */}
      {showUploadModal && uploadFiles.length > 0 && uploadFiles[0] && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-2xl w-full max-w-md mx-4 border border-gray-200 dark:border-slate-600">
            {/* Header */}
            <div className="p-6 border-b border-gray-200 dark:border-slate-600">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-full bg-blue-100 dark:bg-blue-900/50 flex items-center justify-center">
                  <CloudArrowUpIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
                </div>
                <div>
                  <h3 className="font-semibold text-gray-800 dark:text-gray-100">Upload Backup</h3>
                  <p className="text-sm text-gray-500 dark:text-gray-400">Review and confirm upload</p>
                </div>
              </div>
            </div>

            {/* Content */}
            <div className="p-6">
              {/* File Info */}
              <div className="bg-gray-50 dark:bg-slate-700/50 rounded-xl p-4 mb-4 border border-gray-200 dark:border-slate-600">
                <div className="flex items-center gap-3 mb-3">
                  <div className="w-12 h-12 rounded-lg bg-blue-100 dark:bg-blue-900/50 flex items-center justify-center">
                    <DocumentIcon className="w-6 h-6 text-blue-600 dark:text-blue-400" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-gray-800 dark:text-gray-100 truncate">
                      {uploadFiles[0].file?.name || uploadFiles[0].filename}
                    </p>
                    <p className="text-xs text-gray-500 dark:text-gray-400">
                      {(uploadFiles[0].file?.size / 1024 / 1024).toFixed(2)} MB
                    </p>
                  </div>
                </div>
                <div className="text-xs text-gray-500 dark:text-gray-400">
                  <p className="font-medium text-gray-700 dark:text-gray-300 mb-1">File Type:</p>
                  <p className="capitalize">
                    {uploadFiles[0].file?.type?.includes('zip') 
                      ? 'Full Backup (ZIP)' 
                      : uploadFiles[0].file?.name?.endsWith('.sql.gz')
                        ? 'Database Backup (SQL.GZ)'
                        : 'Database Backup (GZ)'}
                  </p>
                </div>
              </div>

              {/* Warning */}
              <div className="bg-amber-50 dark:bg-amber-900/30 rounded-xl p-4 mb-4 border border-amber-200 dark:border-amber-800">
                <div className="flex items-start gap-3">
                  <ExclamationTriangleIcon className="w-5 h-5 text-amber-600 dark:text-amber-400 flex-shrink-0 mt-0.5" />
                  <div>
                    <p className="text-sm font-medium text-amber-800 dark:text-amber-300">Important</p>
                    <ul className="mt-1 text-xs text-amber-700 dark:text-amber-400 space-y-1">
                      <li>• Uploaded backup will be stored on the server</li>
                      <li>• You can preview before restoring</li>
                      <li>• Password will be required for restore</li>
                    </ul>
                  </div>
                </div>
              </div>

              <div className="flex justify-end gap-2 mt-6">
                <button
                  onClick={() => {
                    setShowUploadModal(false);
                    setUploadFiles([]);
                  }}
                  className="px-4 py-2 rounded-lg text-gray-600 hover:bg-gray-100 transition-colors dark:text-gray-300 dark:hover:bg-slate-600"
                  disabled={uploading}
                >
                  Cancel
                </button>
                <button
                  onClick={handleUploadBackup}
                  disabled={uploading}
                  className="px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 text-white transition-colors disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-2"
                >
                  {uploading && <ArrowPathIcon className="w-4 h-4 animate-spin" />}
                  {uploading ? "Uploading..." : "Upload Backup"}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

