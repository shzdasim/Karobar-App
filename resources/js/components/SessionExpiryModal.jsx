import React, { useState, useEffect, useCallback, useRef } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  ClockIcon,
  ArrowRightOnRectangleIcon,
  ShieldCheckIcon,
  LockClosedIcon,
  EyeIcon,
  EyeSlashIcon,
  ExclamationCircleIcon,
} from "@heroicons/react/24/outline";
import { useAuth } from "../context/AuthContext.jsx";
import { saveLastVisitedPath } from "../utils/lastVisitedPage.js";
import { GlassCard, GlassSectionHeader } from "./Glass.jsx";
import {
  isAuthInvalidStatus,
  resolveSessionModalMode,
} from "../utils/securityHelpers.js";

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
  const [mode, setMode] = useState("warning"); // "warning" | "reauth"
  const [isExtending, setIsExtending] = useState(false);
  const [isLoggingOut, setIsLoggingOut] = useState(false);
  const [reauthEmail, setReauthEmail] = useState("");
  const [reauthPassword, setReauthPassword] = useState("");
  const [rememberMe, setRememberMe] = useState(true);
  const [showPassword, setShowPassword] = useState(false);
  const [reauthError, setReauthError] = useState("");
  const [isReauthenticating, setIsReauthenticating] = useState(false);
  const { user, login, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const autoGraceProtectedRef = useRef(false);
  const modeRef = useRef("warning");
  const primaryActionRef = useRef(null);
  const emailInputRef = useRef(null);
  const passwordInputRef = useRef(null);

  const setModalMode = useCallback((nextMode) => {
    modeRef.current = nextMode;
    setMode(nextMode);
  }, []);

  // Ensure backend token stays alive while the proactive warning popup is visible
  // so an in-progress invoice save never fails before the user clicks a button.
  // If the backend already rejects the token with 401/403, immediately switch to inline reauth.
  const ensureBackendGrace = useCallback(async () => {
    if (autoGraceProtectedRef.current) return;
    const token = localStorage.getItem("token");
    if (!token) return;
    autoGraceProtectedRef.current = true;
    try {
      await axios.post("/api/session/extend", { minutes: 10 });
    } catch (err) {
      if (isAuthInvalidStatus(err?.response?.status)) {
        setModalMode("reauth");
      }
    }
  }, [setModalMode]);

  const triggerPopup = useCallback(
    (nextMode = "warning") => {
      const token = localStorage.getItem("token");
      if (!token) return;
      const resolvedMode = modeRef.current === "reauth" ? "reauth" : nextMode;
      setModalMode(resolvedMode);
      setIsOpen(true);
      if (resolvedMode === "warning") {
        ensureBackendGrace();
      }
    },
    [ensureBackendGrace, setModalMode]
  );

  useEffect(() => {
    if (!isOpen) return;
    let remembered = "";
    try {
      remembered = localStorage.getItem("remembered_email") || "";
    } catch {
      remembered = "";
    }
    const defaultEmail = user?.email || remembered || "";
    if (defaultEmail) {
      setReauthEmail((prev) => prev || defaultEmail);
    }
  }, [isOpen, user?.email]);

  useEffect(() => {
    const checkExpiry = () => {
      const token = localStorage.getItem("token");
      if (!token) {
        setIsOpen(false);
        return;
      }
      const expiryMs = getEffectiveExpiryMs();
      if (!expiryMs) return;

      const nowMs = Date.now();
      // Pop up when session expiry time is reached (or within 15 seconds of expiring)
      if (nowMs >= expiryMs - 15 * 1000) {
        const targetMode = resolveSessionModalMode({
          reason: "expiry_check",
          nowMs,
          expiryMs,
        });
        triggerPopup(targetMode);
      }
    };

    checkExpiry();
    const interval = setInterval(() => {
      if (typeof document !== "undefined" && document.hidden) return;
      checkExpiry();
    }, 5000);

    const handleVisibilityChange = () => {
      if (typeof document !== "undefined" && !document.hidden) {
        checkExpiry();
      }
    };

    const handleSessionExpiredEvent = (event) => {
      const targetMode = resolveSessionModalMode({
        reason: event?.detail?.reason || "unauthorized",
        nowMs: Date.now(),
        expiryMs: getEffectiveExpiryMs(),
      });
      triggerPopup(targetMode);
    };

    const handleStorage = (e) => {
      if (e.key === PROMPT_AT_STORAGE_KEY || e.key === "token_expires_at") {
        const expiryMs = getEffectiveExpiryMs();
        if (expiryMs > Date.now() + 15 * 1000) {
          autoGraceProtectedRef.current = false;
          setModalMode("warning");
          setReauthPassword("");
          setReauthError("");
          setIsOpen(false);
        }
      } else if (e.key === "token" && !e.newValue) {
        setModalMode("warning");
        setReauthPassword("");
        setReauthError("");
        setIsOpen(false);
      }
    };

    document.addEventListener("visibilitychange", handleVisibilityChange);
    window.addEventListener("karobar:session-expired", handleSessionExpiredEvent);
    window.addEventListener("storage", handleStorage);

    return () => {
      clearInterval(interval);
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      window.removeEventListener("karobar:session-expired", handleSessionExpiredEvent);
      window.removeEventListener("storage", handleStorage);
    };
  }, [triggerPopup, setModalMode]);

  const handleWaitTenMinutes = async () => {
    if (isExtending || mode === "reauth") return;
    setIsExtending(true);
    try {
      const nextPromptMs = Date.now() + TEN_MINUTES_MS;
      let nextIso = new Date(nextPromptMs).toISOString();

      try {
        const { data } = await axios.post("/api/session/extend", { minutes: 10 });
        if (data?.expires_at) {
          nextIso = data.expires_at;
        }
      } catch (err) {
        if (isAuthInvalidStatus(err?.response?.status)) {
          // Never fake-extend local timestamps when the backend rejected the token;
          // switch to inline re-authentication so unsaved work is preserved.
          setModalMode("reauth");
          setReauthError(
            "Your session has expired on the server. Enter your password below to restore it without losing your work."
          );
          return;
        }
        // Only for offline/transient non-auth errors, keep local session open for 10 minutes
      }

      try {
        localStorage.setItem("token_expires_at", nextIso);
        localStorage.setItem(PROMPT_AT_STORAGE_KEY, String(nextPromptMs));
      } catch {
        // ignore storage errors
      }

      autoGraceProtectedRef.current = false;
      setModalMode("warning");
      setIsOpen(false);
      toast.success("Session extended for 10 minutes. We will remind you again in 10 minutes.", {
        duration: 3500,
      });
    } finally {
      setIsExtending(false);
    }
  };

  const handleReauthSubmit = async (e) => {
    e.preventDefault();
    if (isReauthenticating || isLoggingOut) return;
    const trimmedEmail = reauthEmail.trim();
    if (!trimmedEmail || !reauthPassword) {
      setReauthError("Please enter your email and password.");
      return;
    }

    setReauthError("");
    setIsReauthenticating(true);
    const previousUserId = user?.id ?? null;

    try {
      const { data } = await axios.post("/api/login", {
        email: trimmedEmail,
        password: reauthPassword,
        remember: rememberMe,
      });

      if (!data?.token) {
        setReauthError("Unexpected login response. Please use Switch User / Login Page.");
        return;
      }

      try {
        if (rememberMe) {
          localStorage.setItem("remembered_email", trimmedEmail);
        } else {
          localStorage.removeItem("remembered_email");
        }
        if (data.expires_at) {
          localStorage.setItem("token_expires_at", data.expires_at);
        } else {
          localStorage.removeItem("token_expires_at");
        }
        localStorage.removeItem(PROMPT_AT_STORAGE_KEY);
      } catch {
        // ignore storage errors
      }

      login(data.user, data.token, { license_revoked: data.license_revoked });
      autoGraceProtectedRef.current = false;
      setReauthPassword("");
      setReauthError("");
      setModalMode("warning");
      setIsOpen(false);

      if (
        !data.license_revoked &&
        previousUserId &&
        data.user?.id &&
        Number(previousUserId) !== Number(data.user.id)
      ) {
        navigate("/dashboard", { replace: true });
      }

      toast.success("Session restored. Your unsaved work is intact — you may save again.", {
        duration: 4000,
      });
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        (err?.response?.status === 401 ? "Invalid email or password." : "Could not restore session. Please try again.");
      setReauthError(msg);
    } finally {
      setIsReauthenticating(false);
    }
  };

  const handleLoginNow = async () => {
    if (isLoggingOut || isReauthenticating) return;
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
      setModalMode("warning");
      setReauthPassword("");
      setReauthError("");
      setIsLoggingOut(false);
      logout();
      navigate("/", { replace: true });
    }
  };

  // Focus primary action on open and trap escape/shortcuts
  useEffect(() => {
    if (!isOpen) return;
    const timer = setTimeout(() => {
      if (mode === "reauth") {
        if (reauthEmail.trim()) {
          passwordInputRef.current?.focus();
        } else {
          emailInputRef.current?.focus();
        }
      } else {
        primaryActionRef.current?.focus();
      }
    }, 50);

    const handleKeyDown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        if (mode === "warning") {
          handleWaitTenMinutes();
        }
      } else if (e.ctrlKey || e.metaKey) {
        // Prevent background global shortcuts while session alert is active
        e.stopPropagation();
      }
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => {
      clearTimeout(timer);
      window.removeEventListener("keydown", handleKeyDown, true);
    };
  }, [isOpen, mode, isExtending]);

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
                {mode === "reauth" ? (
                  <LockClosedIcon
                    className="h-5 w-5 shrink-0"
                    style={{ color: "var(--color-primary, #2563eb)" }}
                  />
                ) : (
                  <ClockIcon
                    className="h-5 w-5 shrink-0"
                    style={{ color: "var(--color-primary, #2563eb)" }}
                  />
                )}
                <span>{mode === "reauth" ? "Session Expired — Sign In to Resume" : "Session Timeout Notice"}</span>
              </span>
            }
          />

          {mode === "reauth" ? (
            <form onSubmit={handleReauthSubmit} className="px-4 py-4 space-y-3.5">
              <p className="text-sm leading-relaxed text-gray-700 dark:text-slate-200">
                Your authentication token has expired. Sign back in below to restore your session without closing this page.
              </p>

              <div className="flex items-start gap-2.5 rounded-xl border border-gray-200/80 dark:border-slate-700/80 bg-white/60 dark:bg-slate-800/60 px-3.5 py-2.5 text-xs text-gray-600 dark:text-slate-300">
                <ShieldCheckIcon
                  className="mt-0.5 h-4 w-4 shrink-0"
                  style={{ color: "var(--color-primary, #2563eb)" }}
                />
                <div>
                  <span className="font-semibold text-gray-900 dark:text-slate-100">
                    Your Current Work is Safe:
                  </span>{" "}
                  All open invoices, forms, and unsaved edits remain intact underneath.
                </div>
              </div>

              {reauthError && (
                <div
                  role="alert"
                  className="flex items-start gap-2 rounded-xl border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-xs text-rose-700 dark:text-rose-300"
                >
                  <ExclamationCircleIcon className="mt-0.5 h-4 w-4 shrink-0" />
                  <span>{reauthError}</span>
                </div>
              )}

              <div className="space-y-1">
                <label
                  htmlFor="session-reauth-email"
                  className="block text-xs font-semibold text-gray-700 dark:text-slate-300"
                >
                  Email address
                </label>
                <input
                  ref={emailInputRef}
                  id="session-reauth-email"
                  type="email"
                  autoComplete="username"
                  value={reauthEmail}
                  onChange={(e) => setReauthEmail(e.target.value)}
                  readOnly={Boolean(user?.email)}
                  className={`g-input w-full text-sm ${
                    user?.email ? "opacity-80 cursor-not-allowed" : ""
                  }`}
                  placeholder="you@example.com"
                  required
                />
              </div>

              <div className="space-y-1">
                <label
                  htmlFor="session-reauth-password"
                  className="block text-xs font-semibold text-gray-700 dark:text-slate-300"
                >
                  Password
                </label>
                <div className="relative">
                  <input
                    ref={passwordInputRef}
                    id="session-reauth-password"
                    type={showPassword ? "text" : "password"}
                    autoComplete="current-password"
                    value={reauthPassword}
                    onChange={(e) => setReauthPassword(e.target.value)}
                    className="g-input w-full pr-10 text-sm"
                    placeholder="Enter your password"
                    required
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((prev) => !prev)}
                    aria-label={showPassword ? "Hide password" : "Show password"}
                    className="absolute inset-y-0 right-0 flex items-center px-3 text-gray-500 hover:text-gray-700 dark:text-slate-400 dark:hover:text-slate-200 cursor-pointer"
                  >
                    {showPassword ? <EyeSlashIcon className="h-4 w-4" /> : <EyeIcon className="h-4 w-4" />}
                  </button>
                </div>
              </div>

              <label className="inline-flex items-center gap-2 text-xs text-gray-600 dark:text-slate-300 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={rememberMe}
                  onChange={(e) => setRememberMe(e.target.checked)}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                <span>Keep me signed in</span>
              </label>

              <div className="flex flex-wrap items-center justify-end gap-2 pt-1">
                <button
                  type="button"
                  onClick={handleLoginNow}
                  disabled={isReauthenticating || isLoggingOut}
                  className="g-btn-ghost inline-flex items-center justify-center gap-1.5 px-3.5 cursor-pointer"
                >
                  <ArrowRightOnRectangleIcon className="h-4 w-4" />
                  <span>{isLoggingOut ? "Signing Out…" : "Switch User / Login Page"}</span>
                </button>

                <button
                  type="submit"
                  disabled={isReauthenticating || isLoggingOut}
                  className="g-btn-base theme-btn-primary inline-flex items-center justify-center gap-2 px-4 cursor-pointer"
                >
                  <LockClosedIcon className="h-4 w-4" />
                  <span>{isReauthenticating ? "Signing In…" : "Resume Session"}</span>
                </button>
              </div>
            </form>
          ) : (
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
                  style={{ color: "var(--color-primary, #2563eb)" }}
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
                  ref={primaryActionRef}
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
          )}
        </GlassCard>
      </div>
    </div>
  );
}

