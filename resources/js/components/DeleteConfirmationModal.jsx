import { useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { ShieldExclamationIcon, XMarkIcon } from "@heroicons/react/24/solid";
import { GlassCard, GlassSectionHeader, GlassInput, GlassBtn } from "./Glass.jsx";

/**
 * Reusable delete confirmation modal with 2-step process:
 * Step 1: Confirm deletion
 * Step 2: Password confirmation
 * 
 * @param {Object} props
 * @param {boolean} props.isOpen - Whether the modal is visible
 * @param {Function} props.onClose - Function to call when closing the modal
 * @param {Function} props.onConfirm - Function to call after successful password confirmation (receives password)
 * @param {string|Object} props.itemName - Name or object with name property of item being deleted
 * @param {string} props.title - Custom title (default: "Delete")
 * @param {boolean} props.isDeleting - Whether deletion is in progress
 * @param {Function} props.setIsDeleting - Setter for deleting state
 * @param {string} props.deleteEndpoint - API endpoint for password confirmation (default: "/api/auth/confirm-password")
 * @param {Object} props.tintClasses - Optional custom tint classes
 */
export default function DeleteConfirmationModal({
  isOpen,
  onClose,
  onConfirm,
  itemName,
  title = "Delete",
  isDeleting,
  setIsDeleting,
  deleteEndpoint = "/api/auth/confirm-password",
  tintClasses = {},
}) {
  const [step, setStep] = useState(1);
  const [password, setPassword] = useState("");

  // Default tint classes
  const defaultTint = {
    red: "bg-rose-500/85 text-white shadow-[0_6px_20px_-6px_rgba(244,63,94,0.45)] ring-1 ring-white/20 hover:bg-rose-500/95",
    glass: "bg-white/60 text-slate-700 ring-1 ring-white/30 hover:bg-white/75 dark:text-gray-100 dark:bg-slate-700/60 dark:ring-slate-600/30 dark:hover:bg-slate-600/75",
  };

  const tintRed = tintClasses.red || defaultTint.red;
  const tintGlass = tintClasses.glass || defaultTint.glass;

  // Get item name string
  const getItemName = () => {
    if (typeof itemName === "string") return itemName;
    if (typeof itemName === "object" && itemName?.name) return itemName.name;
    return "";
  };

  const handleClose = () => {
    if (isDeleting) return;
    setStep(1);
    setPassword("");
    onClose();
  };

  const handleProceedToPassword = () => setStep(2);

  const handleConfirmDelete = async () => {
    if (!onConfirm) return;
    try {
      setIsDeleting?.(true);
      await axios.post(deleteEndpoint, { password });
      await onConfirm(password);
    } catch (err) {
      const status = err?.response?.status;
      const apiMsg =
        err?.response?.data?.message ||
        (status === 422 ? "Incorrect password" : status === 403 ? "You don't have permission." : "Delete failed");
      toast.error(apiMsg);
    } finally {
      setIsDeleting?.(false);
    }
  };

  if (!isOpen) return null;

  const itemNameStr = getItemName();

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      onClick={(e) => {
        if (e.target === e.currentTarget) handleClose();
      }}
    >
      <div className="absolute inset-0 bg-black/40" />
      <div className="relative w-full max-w-md">
        <GlassCard>
          <GlassSectionHeader
            title={
              <span className="inline-flex items-center gap-2">
                <ShieldExclamationIcon className="w-5 h-5 text-rose-600" />
                <span>{title}</span>
              </span>
            }
            right={
              <GlassBtn className={`h-8 px-3 ${tintGlass}`} onClick={handleClose} title="Close">
                <XMarkIcon className="w-5 h-5" />
              </GlassBtn>
            }
          />
          <div className="px-4 py-4 space-y-4">
            {step === 1 && (
              <>
                <p className="text-sm text-gray-700">
                  {itemNameStr ? (
                    <>Are you sure you want to delete <strong>{itemNameStr}</strong>? </>
                  ) : (
                    "Are you sure you want to delete this item? "
                  )}
                  This action cannot be undone.
                </p>
                <div className="flex justify-end gap-2">
                  <GlassBtn className={`min-w-[100px] ${tintGlass}`} onClick={handleClose}>
                    Cancel
                  </GlassBtn>
                  <GlassBtn className={`min-w-[140px] ${tintRed}`} onClick={handleProceedToPassword}>
                    Yes, continue
                  </GlassBtn>
                </div>
              </>
            )}

            {step === 2 && (
              <>
                <p className="text-sm text-gray-700">
                  For security, please re-enter your password to confirm this action.
                </p>
                <GlassInput
                  type="password"
                  autoFocus
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Your password"
                  onKeyDown={(e) => {
                    if (e.key === "Enter") handleConfirmDelete();
                    if (e.key === "Escape") {
                      setStep(1);
                      setPassword("");
                    }
                  }}
                  className="w-full"
                />
                <div className="flex justify-between">
                  <GlassBtn
                    className={`min-w-[90px] ${tintGlass}`}
                    onClick={() => {
                      setStep(1);
                      setPassword("");
                    }}
                    disabled={isDeleting}
                  >
                    ← Back
                  </GlassBtn>
                  <div className="flex gap-2">
                    <GlassBtn className={`min-w-[100px] ${tintGlass}`} onClick={handleClose} disabled={isDeleting}>
                      Cancel
                    </GlassBtn>
                    <GlassBtn
                      className={`min-w-[170px] ${tintRed} disabled:opacity-60`}
                      onClick={handleConfirmDelete}
                      disabled={isDeleting || password.trim() === ""}
                    >
                      {isDeleting ? "Deleting…" : "Confirm & Delete"}
                    </GlassBtn>
                  </div>
                </div>
              </>
            )}
          </div>
        </GlassCard>
      </div>
    </div>
  );
}

