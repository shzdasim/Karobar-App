import React, { useState, useEffect, useCallback, useRef } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  ClockIcon,
  ArrowRightOnRectangleIcon,
  ShieldCheckIcon,
} from "@heroicons/react/24/outline";
import { useAuth } from "../context/AuthContext.jsx";
import { saveLastVisitedPath } from "../utils/lastVisitedPage.js";
import { GlassCard, GlassSectionHeader } from "./Glass.jsx";

const PROMPT_AT_STORAGE_KEY = "karobar_session_prompt_at";
const TEN_MINUTES_MS = 10 * 60 * 1000;

function getEffectiveExpiryMs() {
  try {
    const promptAtRaw = localStorage.getItem(PROMPT_AT_STORAGE_KEY);
    if (promptAtRaw) {
      const parsed = Number(promptAtRaw);
      if (Number.isFinite(parsed) && parsed > 0) {
        return parsed;
      }
    }
    const tokenExpRaw = localStorage.getItem("token_expires_at");
    if (tokenExpRaw) {
      const parsed = Date.parse(tokenExpRaw);
      if (Number.isFinite(parsed) && parsed > 0) {
        return parsed;
      }
    }
  } catch {
    // ignore storage errors
  }
  return 0;
}

export default function SessionExpiryModal() {
  const [isOpen, setIsOpen] = useState(false);
  const [isExtending, setIsExtending] = useState(false);
  const [isLoggingOut, setIsLoggingOut] = useState(false);
  const { logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const autoGraceProtectedRef = useRef(false);

  // Ensure backend token stays alive while the popup is visible on screen
  // so an in-progress invoice save never fails with 401 before the user clicks a button
  const ensureBackendGrace = useCallback(async () => {
    if (autoGraceProtectedRef.current) return;
    const token = localStorage.getItem("token");
    if (!token) return;
    autoGraceProtectedRef.current = true;
    try {
      await axios.post("/api/session/extend", { minutes: 10 });
    } catch {
      // ignore background grace error
    }
  }, []);

  const triggerPopup = useCallback(() => {
    const token = localStorage.getItem("token");
    if (!token) return;
    setIsOpen(true);
    ensureBackendGrace();
  }, [ensureBackendGrace]);

  useEffect(() => {
    const checkExpiry = () => {
      const token = localStorage.getItem("token");
      if (!token) {
        setIsOpen(false);
        return;
      }
      const expiryMs = getEffectiveExpiryMs();
      if (!expiryMs) return;

      // Pop up when session expiry time is reached (or within 15 seconds of expiring)
      if (Date.now() >= expiryMs - 15 * 1000) {
        triggerPopup();
      }
    };

    checkExpiry();
    const interval = setInterval(checkExpiry, 5000);

    const handleSessionExpiredEvent = () => {
      triggerPopup();
    };

    const handleStorage = (e) => {
      if (e.key === PROMPT_AT_STORAGE_KEY || e.key === "token_expires_at") {
        const expiryMs = getEffectiveExpiryMs();
        if (expiryMs > Date.now() + 15 * 1000) {
          autoGraceProtectedRef.current = false;
          setIsOpen(false);
        }
      } else if (e.key === "token" && !e.newValue) {
        setIsOpen(false);
      }
    };

    window.addEventListener("karobar:session-expired", handleSessionExpiredEvent);
    window.addEventListener("storage", handleStorage);

    return () => {
      clearInterval(interval);
      window.removeEventListener("karobar:session-expired", handleSessionExpiredEvent);
      window.removeEventListener("storage", handleStorage);
    };
  }, [triggerPopup]);

  const handleWaitTenMinutes = async () => {
    if (isExtending) return;
    setIsExtending(true);
    try {
      const nextPromptMs = Date.now() + TEN_MINUTES_MS;
      let nextIso = new Date(nextPromptMs).toISOString();

      try {
        const { data } = await axios.post("/api/session/extend", { minutes: 10 });
        if (data?.expires_at) {
          nextIso = data.expires_at;
        }
      } catch {
        // Even if offline/transient error, keep local session open for 10 minutes
      }

      try {
        localStorage.setItem("token_expires_at", nextIso);
        localStorage.setItem(PROMPT_AT_STORAGE_KEY, String(nextPromptMs));
      } catch {
        // ignore storage errors
      }

      autoGraceProtectedRef.current = false;
      setIsOpen(false);
      toast.success("Session extended for 10 minutes. We will remind you again in 10 minutes.", {
        duration: 3500,
      });
    } finally {
      setIsExtending(false);
    }
  };

  const handleLoginNow = async () => {
    if (isLoggingOut) return;
    setIsLoggingOut(true);
    try {
      saveLastVisitedPath(location);
      const token = localStorage.getItem("token");
      if (token) {
        await fetch("/api/logout", {
          method: "POST",
          headers: { Authorization: `Bearer ${token}` },
        }).catch(() => {});
      }
    } finally {
      setIsOpen(false);
      setIsLoggingOut(false);
      logout();
      navigate("/", { replace: true });
    }
  };

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 print:hidden"
      role="dialog"
      aria-modal="true"
      aria-labelledby="session-expiry-title"
    >
      <div className="absolute inset-0 bg-black/40 backdrop-blur-xs" />
      <div className="relative w-full max-w-md">
        <GlassCard className="overflow-hidden">
          <GlassSectionHeader
            title={
              <span id="session-expiry-title" className="inline-flex items-center gap-2">
                <ClockIcon
                  className="h-5 w-5 shrink-0"
                  style={{ color: "var(--theme-primary, #2563eb)" }}
                />
                <span>Session Timeout Notice</span>
              </span>
            }
          />

          <div className="px-4 py-4 space-y-4">
            <p className="text-sm leading-relaxed text-gray-700 dark:text-slate-200">
              Your login session time has ended. Need more time to finish your current invoice or task? Click{" "}
              <strong className="font-semibold text-gray-900 dark:text-white">
                Wait 10 Minutes
              </strong>{" "}
              to continue working without losing any progress.
            </p>

            <div className="flex items-start gap-2.5 rounded-xl border border-gray-200/80 dark:border-slate-700/80 bg-white/60 dark:bg-slate-800/60 px-3.5 py-2.5 text-xs text-gray-600 dark:text-slate-300">
              <ShieldCheckIcon
                className="mt-0.5 h-4 w-4 shrink-0"
                style={{ color: "var(--theme-primary, #2563eb)" }}
              />
              <div>
                <span className="font-semibold text-gray-900 dark:text-slate-100">
                  Your Current Work is Safe:
                </span>{" "}
                All open invoices, forms, and tabs remain intact while you decide.
              </div>
            </div>

            <div className="flex items-center justify-end gap-2 pt-1">
              <button
                type="button"
                onClick={handleLoginNow}
                disabled={isExtending || isLoggingOut}
                className="g-btn-ghost inline-flex items-center justify-center gap-1.5 px-3.5 cursor-pointer"
              >
                <ArrowRightOnRectangleIcon className="h-4 w-4" />
                <span>{isLoggingOut ? "Signing Out…" : "Login Now"}</span>
              </button>

              <button
                type="button"
                onClick={handleWaitTenMinutes}
                disabled={isExtending || isLoggingOut}
                className="g-btn-base theme-btn-primary inline-flex items-center justify-center gap-2 px-4 cursor-pointer"
              >
                <ClockIcon className="h-4 w-4" />
                <span>{isExtending ? "Extending…" : "Wait 10 Minutes"}</span>
              </button>
            </div>
          </div>
        </GlassCard>
      </div>
    </div>
  );
}

