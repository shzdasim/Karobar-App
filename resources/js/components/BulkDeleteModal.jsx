import { useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { ShieldExclamationIcon, XMarkIcon } from "@heroicons/react/24/solid";
import { GlassCard, GlassSectionHeader, GlassInput, GlassBtn } from "./Glass.jsx";

/**
 * Reusable bulk delete modal with password confirmation
 * 
 * @param {Object} props
 * @param {boolean} props.isOpen - Whether the modal is visible
 * @param {Function} props.onClose - Function to call when closing the modal
 * @param {number} props.selectedCount - Number of items selected for deletion
 * @param {Function} props.onDeleted - Function to call after successful deletion (receives result data)
 * @param {string} props.deleteEndpoint - API endpoint for bulk delete (default: "/api/products/bulk-destroy")
 * @param {string} props.passwordEndpoint - API endpoint for password confirmation (default: "/api/auth/confirm-password")
 * @param {string} props.itemType - Type of items being deleted (default: "item(s)")
 * @param {Object} props.tintClasses - Optional custom tint classes
 */
export default function BulkDeleteModal({
  isOpen,
  onClose,
  selectedCount,
  selectedIds = [],
  onDeleted,
  deleteEndpoint = "/api/products/bulk-destroy",
  passwordEndpoint = "/api/auth/confirm-password",
  itemType = "item(s)",
  tintClasses = {},
}) {
  const [password, setPassword] = useState("");
  const [working, setWorking] = useState(false);

  // Default tint classes
  const defaultTint = {
    red: "bg-rose-500/85 text-white shadow-[0_6px_20px_-6px_rgba(244,63,94,0.45)] ring-1 ring-white/20 hover:bg-rose-500/95",
    glass: "bg-white/60 text-slate-700 ring-1 ring-white/30 hover:bg-white/75 dark:text-gray-100 dark:bg-slate-700/60 dark:ring-slate-600/30 dark:hover:bg-slate-600/75",
  };

  const tintRed = tintClasses.red || defaultTint.red;
  const tintGlass = tintClasses.glass || defaultTint.glass;

  const handleSubmit = async () => {
    try {
      setWorking(true);
      await axios.post(passwordEndpoint, { password });
      const { data } = await axios.post(deleteEndpoint, {
        product_ids: selectedIds,
      });
      await onDeleted?.(data);
    } catch (e) {
      const msg = e?.response?.data?.message || "Bulk delete failed";
      toast.error(msg);
    } finally {
      setWorking(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="absolute inset-0 bg-black/40" />
      <div className="relative w-full max-w-md">
        <GlassCard>
          <GlassSectionHeader
            title={
              <span className="inline-flex items-center gap-2">
                <ShieldExclamationIcon className="w-5 h-5 text-rose-600" />
                <span>Delete selected {itemType}</span>
              </span>
            }
            right={
              <GlassBtn className={`h-8 px-3 ${tintGlass}`} onClick={onClose} title="Close">
                <XMarkIcon className="w-5 h-5" />
              </GlassBtn>
            }
          />
          <div className="px-4 py-4 space-y-4">
            <p className="text-sm text-gray-700">
              You are about to permanently delete <strong>{selectedCount}</strong> {itemType}. This action cannot be undone.
            </p>

            <div className="space-y-2">
              <label className="block text-sm font-medium">Confirm your password</label>
              <GlassInput
                type="password"
                value={password}
                autoFocus
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Your password"
                onKeyDown={(e) => {
                  if (e.key === "Enter" && password.trim()) handleSubmit();
                  if (e.key === "Escape") onClose();
                }}
              />
            </div>

            <div className="flex justify-end gap-2">
              <GlassBtn className={`min-w-[110px] ${tintGlass}`} onClick={onClose} disabled={working}>
                Cancel
              </GlassBtn>
              <GlassBtn
                className={`min-w-[160px] ${tintRed}`}
                onClick={handleSubmit}
                disabled={working || password.trim() === ""}
              >
                {working ? "Deleting…" : "Confirm & Delete"}
              </GlassBtn>
            </div>
          </div>
        </GlassCard>
      </div>
    </div>
  );
}

/**
 * Reusable bulk delete modal that accepts selectedIds directly
 */
export function BulkDeleteModalWithIds({
  isOpen,
  onClose,
  selectedIds,
  selectedCount,
  onDeleted,
  deleteEndpoint = "/api/products/bulk-destroy",
  passwordEndpoint = "/api/auth/confirm-password",
  itemType = "item(s)",
  tintClasses = {},
}) {
  const [password, setPassword] = useState("");
  const [working, setWorking] = useState(false);

  // Default tint classes
  const defaultTint = {
    red: "bg-rose-500/85 text-white shadow-[0_6px_20px_-6px_rgba(244,63,94,0.45)] ring-1 ring-white/20 hover:bg-rose-500/95",
    glass: "bg-white/60 text-slate-700 ring-1 ring-white/30 hover:bg-white/75 dark:text-gray-100 dark:bg-slate-700/60 dark:ring-slate-600/30 dark:hover:bg-slate-600/75",
  };

  const tintRed = tintClasses.red || defaultTint.red;
  const tintGlass = tintClasses.glass || defaultTint.glass;

  const handleSubmit = async () => {
    try {
      setWorking(true);
      await axios.post(passwordEndpoint, { password });
      const { data } = await axios.post(deleteEndpoint, {
        product_ids: selectedIds,
      });
      await onDeleted?.(data);
    } catch (e) {
      const msg = e?.response?.data?.message || "Bulk delete failed";
      toast.error(msg);
    } finally {
      setWorking(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="absolute inset-0 bg-black/40" />
      <div className="relative w-full max-w-md">
        <GlassCard>
          <GlassSectionHeader
            title={
              <span className="inline-flex items-center gap-2">
                <ShieldExclamationIcon className="w-5 h-5 text-rose-600" />
                <span>Delete selected {itemType}</span>
              </span>
            }
            right={
              <GlassBtn className={`h-8 px-3 ${tintGlass}`} onClick={onClose} title="Close">
                <XMarkIcon className="w-5 h-5" />
              </GlassBtn>
            }
          />
          <div className="px-4 py-4 space-y-4">
            <p className="text-sm text-gray-700">
              You are about to permanently delete <strong>{selectedCount || selectedIds.length}</strong> {itemType}. This action cannot be undone.
            </p>

            <div className="space-y-2">
              <label className="block text-sm font-medium">Confirm your password</label>
              <GlassInput
                type="password"
                value={password}
                autoFocus
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Your password"
                onKeyDown={(e) => {
                  if (e.key === "Enter" && password.trim()) handleSubmit();
                  if (e.key === "Escape") onClose();
                }}
              />
            </div>

            <div className="flex justify-end gap-2">
              <GlassBtn className={`min-w-[110px] ${tintGlass}`} onClick={onClose} disabled={working}>
                Cancel
              </GlassBtn>
              <GlassBtn
                className={`min-w-[160px] ${tintRed}`}
                onClick={handleSubmit}
                disabled={working || password.trim() === ""}
              >
                {working ? "Deleting…" : "Confirm & Delete"}
              </GlassBtn>
            </div>
          </div>
        </GlassCard>
      </div>
    </div>
  );
}

