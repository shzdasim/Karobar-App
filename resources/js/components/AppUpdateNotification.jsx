import React, { useState, useEffect, useCallback } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import {
  ArrowPathIcon,
  CheckCircleIcon,
  CloudArrowDownIcon,
  ShieldCheckIcon,
  SparklesIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import { GlassCard, GlassSectionHeader } from "./Glass.jsx";

const UPDATE_DISMISSED_UNTIL_KEY = "karobar_update_dismissed_until";
const ONE_DAY_MS = 24 * 60 * 60 * 1000;

function getDismissedUntilMs() {
  try {
    const raw = localStorage.getItem(UPDATE_DISMISSED_UNTIL_KEY);
    if (!raw) return 0;
    const parsed = Number(raw);
    return Number.isFinite(parsed) ? parsed : 0;
  } catch {
    return 0;
  }
}

function isDismissedForToday() {
  return Date.now() < getDismissedUntilMs();
}

export default function AppUpdateNotification() {
  const [updateInfo, setUpdateInfo] = useState(null);
  const [isDismissed, setIsDismissed] = useState(() => isDismissedForToday());
  const [isChecking, setIsChecking] = useState(false);
  const [isDownloading, setIsDownloading] = useState(false);
  const [isDownloaded, setIsDownloaded] = useState(false);
  const [showReadyPopup, setShowReadyPopup] = useState(false);
  const [isApplying, setIsApplying] = useState(false);
  const [progress, setProgress] = useState({
    status: "idle",
    percent: 0,
    downloadedMB: null,
    totalMB: null,
    message: "",
  });

  const dismissForOneDay = useCallback(() => {
    const until = Date.now() + ONE_DAY_MS;
    try {
      localStorage.setItem(UPDATE_DISMISSED_UNTIL_KEY, String(until));
    } catch {
      // ignore storage errors
    }
    setShowReadyPopup(false);
    setIsDismissed(true);
    window.dispatchEvent(new CustomEvent("karobar:update-dismissed", { detail: { until } }));
  }, []);

  const checkForUpdates = useCallback(async ({ manual = false } = {}) => {
    if (!manual && isDismissedForToday()) {
      setIsDismissed(true);
      return;
    }

    if (typeof navigator !== "undefined" && !navigator.onLine) {
      if (manual) {
        toast.error("You are currently offline. Connect to the internet to check for Karobar-App updates.");
      }
      return;
    }

    setIsChecking(true);
    try {
      let data = null;
      const desktop = typeof window !== "undefined" ? window.karobarDesktop : null;

      // 1. Try native Electron HTTPS updater first on Desktop (uses Chromium/Node CA store)
      if (desktop?.isDesktop && typeof desktop.checkForUpdates === "function") {
        try {
          const desktopRes = await desktop.checkForUpdates(manual);
          if (desktopRes?.ok) {
            data = desktopRes;
          }
        } catch {
          // Fall through to backend API
        }
      }

      // 2. Try Laravel backend /api/system/check-update
      if (!data) {
        try {
          const resp = await axios.get("/api/system/check-update", {
            params: manual ? { force: 1 } : {},
          });
          if (resp.data?.ok) {
            data = resp.data;
          }
        } catch {
          // Fall through to direct browser GitHub API check
        }
      }

      // 3. Direct browser fallback to GitHub Releases API (CORS enabled: access-control-allow-origin: *)
      if (!data) {
        try {
          const [verRes, ghRes] = await Promise.all([
            axios.get("/api/system/version").catch(() => ({ data: { version: "2.0.1", build_number: 1 } })),
            fetch("https://api.github.com/repos/shzdasim/Karobar-App/releases/latest", {
              headers: { Accept: "application/vnd.github+json" },
            }),
          ]);
          if (ghRes.ok) {
            const release = await ghRes.json();
            const localVer = String(verRes.data?.version || "2.0.1").replace(/^[vV]/, "");
            const localBuild = Number(verRes.data?.build_number || 1);
            const remoteVer = String(release.tag_name || "").replace(/^[vV]/, "");
            const buildMatch = remoteVer.match(/^2\.0\.(\d+)$/);
            const remoteBuild = buildMatch ? parseInt(buildMatch[1], 10) : 0;
            const isNewer =
              remoteBuild > 0 && localBuild > 0 && remoteBuild !== localBuild
                ? remoteBuild > localBuild
                : remoteVer > localVer;

            data = {
              ok: true,
              update_available: isNewer,
              app_name: "Karobar-App",
              current_version: localVer,
              current_build: localBuild,
              latest_version: remoteVer,
              latest_build: remoteBuild,
              release_name: release.name || `Karobar-App v${remoteVer}`,
              released_at: release.published_at || "",
              notes: release.body || "New Karobar-App release available.",
              download_url:
                "https://github.com/shzdasim/Karobar-App/releases/latest/download/Karobar-App-Setup.exe",
              bundle_url:
                "https://github.com/shzdasim/Karobar-App/releases/latest/download/Karobar-App-Setup-Bundle.zip",
              release_url: release.html_url || "https://github.com/shzdasim/Karobar-App/releases/latest",
              zero_data_loss: true,
            };
          }
        } catch {
          // Ignore
        }
      }

      if (!data || !data.ok) {
        if (manual) {
          toast.error("Could not reach GitHub Releases right now. Please check your internet connection.");
        }
        return;
      }

      if (data.update_available) {
        setUpdateInfo(data);
        if (manual) {
          try {
            localStorage.removeItem(UPDATE_DISMISSED_UNTIL_KEY);
          } catch {
            // ignore
          }
          setIsDismissed(false);
        } else {
          setIsDismissed(isDismissedForToday());
        }
      } else if (manual) {
        toast.success(
          `Karobar-App is up to date (v${data.current_version} • Build #${data.current_build || 1})`,
          { duration: 4000 }
        );
      }
    } catch {
      if (manual) {
        toast.error("Unable to check for updates right now. Please verify your internet connection.");
      }
    } finally {
      setIsChecking(false);
    }
  }, []);

  useEffect(() => {
    const syncDismissState = () => {
      setIsDismissed(isDismissedForToday());
    };
    window.addEventListener("storage", syncDismissState);
    window.addEventListener("karobar:update-dismissed", syncDismissState);
    return () => {
      window.removeEventListener("storage", syncDismissState);
      window.removeEventListener("karobar:update-dismissed", syncDismissState);
    };
  }, []);

  // Re-open automatically once the 24-hour dismissal window expires
  useEffect(() => {
    if (!isDismissed) return;
    const remainingMs = getDismissedUntilMs() - Date.now();
    if (remainingMs <= 0) {
      setIsDismissed(false);
      checkForUpdates({ manual: false });
      return;
    }
    const timer = setTimeout(() => {
      setIsDismissed(false);
      checkForUpdates({ manual: false });
    }, Math.min(remainingMs + 500, ONE_DAY_MS + 1000));
    return () => clearTimeout(timer);
  }, [isDismissed, checkForUpdates]);

  useEffect(() => {
    // Initial check shortly after startup when online
    const initialTimer = setTimeout(() => {
      if (typeof navigator === "undefined" || navigator.onLine) {
        checkForUpdates({ manual: false });
      }
    }, 3500);

    // Re-check automatically whenever the machine connects to the internet
    const handleOnline = () => {
      checkForUpdates({ manual: false });
    };
    window.addEventListener("online", handleOnline);

    // Manual trigger from custom DOM event
    const handleCustomTrigger = () => {
      checkForUpdates({ manual: true });
    };
    window.addEventListener("karobar:check-updates", handleCustomTrigger);

    // Periodic background check every 30 minutes
    const interval = setInterval(() => {
      if (typeof navigator === "undefined" || navigator.onLine) {
        checkForUpdates({ manual: false });
      }
    }, 30 * 60 * 1000);

    // Desktop IPC listeners (native Windows menu "Check for Software Updates…" & download progress)
    const desktop = typeof window !== "undefined" ? window.karobarDesktop : null;
    const unsubTrigger = desktop?.onTriggerUpdateCheck?.(() => {
      checkForUpdates({ manual: true });
    });
    const unsubProgress = desktop?.onUpdateProgress?.((payload) => {
      if (!payload) return;
      setProgress((prev) => ({
        ...prev,
        ...payload,
      }));
      if (payload.status === "downloaded") {
        setIsDownloading(false);
        setIsDownloaded(true);
        setShowReadyPopup(true);
      } else if (payload.status === "error") {
        setIsDownloading(false);
        setIsApplying(false);
        toast.error(payload.message || "Update operation failed.");
      }
    });

    return () => {
      clearTimeout(initialTimer);
      clearInterval(interval);
      window.removeEventListener("online", handleOnline);
      window.removeEventListener("karobar:check-updates", handleCustomTrigger);
      unsubTrigger?.();
      unsubProgress?.();
    };
  }, [checkForUpdates]);

  const handleDownloadUpdate = async () => {
    if (!updateInfo || isDownloading || isApplying) return;

    const desktop = typeof window !== "undefined" ? window.karobarDesktop : null;
    const downloadUrl =
      updateInfo.download_url ||
      "https://github.com/shzdasim/Karobar-App/releases/latest/download/Karobar-App-Setup.exe";

    setIsDownloading(true);
    setProgress({
      status: "starting",
      percent: 3,
      downloadedMB: null,
      totalMB: null,
      message: "Downloading update in background…",
    });

    // 1. Try Electron IPC background downloader first
    if (desktop?.isDesktop && typeof desktop.downloadAndInstallUpdate === "function") {
      try {
        const res = await desktop.downloadAndInstallUpdate({
          downloadUrl,
          version: updateInfo.latest_version,
          mode: "download",
        });
        if (res?.ok) {
          setIsDownloading(false);
          setIsDownloaded(true);
          setShowReadyPopup(true);
          return;
        }
      } catch {
        // Fall through to in-app backend downloader
      }
    }

    // 2. Fallback to Laravel in-app background downloader
    let pollTimer = null;
    try {
      pollTimer = setInterval(async () => {
        try {
          const { data } = await axios.get("/api/system/update-progress", {
            cache: false,
            params: { _t: Date.now() },
          });
          if (data?.status && data.status !== "idle") {
            setProgress((prev) => ({
              ...prev,
              ...data,
            }));
          }
        } catch {
          // ignore poll errors
        }
      }, 450);

      const { data } = await axios.post("/api/system/install-update", {
        download_url: downloadUrl,
        version: updateInfo.latest_version,
        mode: "download",
      });

      if (data?.ok) {
        setIsDownloading(false);
        setIsDownloaded(true);
        setShowReadyPopup(true);
      } else {
        setIsDownloading(false);
        toast.error(data?.message || "Unable to download update right now. Please try again.");
      }
    } catch (err) {
      setIsDownloading(false);
      toast.error(
        err?.response?.data?.message ||
          "Unable to download update right now. Please check your internet connection and try again."
      );
    } finally {
      if (pollTimer) clearInterval(pollTimer);
    }
  };

  const handleApplyUpdate = async () => {
    if (!updateInfo || isApplying) return;

    const desktop = typeof window !== "undefined" ? window.karobarDesktop : null;
    const downloadUrl =
      updateInfo.download_url ||
      "https://github.com/shzdasim/Karobar-App/releases/latest/download/Karobar-App-Setup.exe";

    setIsApplying(true);
    setProgress((prev) => ({
      ...prev,
      status: "backing_up",
      percent: 100,
      message: "Creating safety backup & launching installer…",
    }));

    // 1. Try Electron IPC installer (uses already-downloaded file in temp directory)
    if (desktop?.isDesktop && typeof desktop.downloadAndInstallUpdate === "function") {
      try {
        const res = await desktop.downloadAndInstallUpdate({
          downloadUrl,
          version: updateInfo.latest_version,
          mode: "install",
        });
        if (res?.ok) {
          return;
        }
      } catch {
        // Fall through to in-app backend installer
      }
    }

    // 2. Fallback to Laravel in-app installer
    try {
      const { data } = await axios.post("/api/system/install-update", {
        download_url: downloadUrl,
        version: updateInfo.latest_version,
        mode: "install",
      });

      if (!data?.ok) {
        setIsApplying(false);
        toast.error(data?.message || "Unable to launch installer right now. Please try again.");
      }
    } catch (err) {
      setIsApplying(false);
      toast.error(
        err?.response?.data?.message ||
          "Unable to apply update right now. Please try again."
      );
    }
  };

  if (!updateInfo?.update_available || (isDismissed && !isDownloading && !isDownloaded && !isApplying)) {
    return null;
  }

  return (
    <>
      {/* Non-blocking Bottom-Right Update Card (allows full app usage while viewing or downloading) */}
      <div
        className="fixed bottom-5 right-5 z-[9990] w-[410px] max-w-[calc(100vw-2rem)] print:hidden"
        role="region"
        aria-label="Software Update Notification"
      >
        <GlassCard className="overflow-hidden shadow-xl">
          <GlassSectionHeader
            title={
              <span className="inline-flex items-center gap-2">
                {isDownloaded ? (
                  <CheckCircleIcon
                    className="h-5 w-5 shrink-0"
                    style={{ color: "var(--theme-primary, #2563eb)" }}
                  />
                ) : (
                  <SparklesIcon
                    className="h-5 w-5 shrink-0"
                    style={{ color: "var(--theme-primary, #2563eb)" }}
                  />
                )}
                <span>
                  {isDownloaded
                    ? "Update Ready to Install"
                    : isDownloading
                      ? "Downloading Update…"
                      : "Software Update Available"}
                </span>
              </span>
            }
            right={
              !isDownloading && !isApplying ? (
                <button
                  type="button"
                  onClick={dismissForOneDay}
                  className="h-8 w-8 inline-flex items-center justify-center rounded-lg text-slate-500 hover:bg-slate-200/60 dark:text-slate-300 dark:hover:bg-slate-700/60 transition-colors cursor-pointer"
                  title="Remind me in 24 hours"
                  aria-label="Close"
                >
                  <XMarkIcon className="w-5 h-5" />
                </button>
              ) : null
            }
          />

          <div className="px-4 py-3.5 space-y-3">
            {/* Release Title & Version Comparison */}
            <div className="flex items-center justify-between gap-3 rounded-xl border border-gray-200/80 dark:border-slate-700/80 bg-white/70 dark:bg-slate-800/70 px-3.5 py-2">
              <div>
                <div className="text-xs text-gray-500 dark:text-slate-400">
                  Installed Version
                </div>
                <div className="mt-0.5 font-mono text-sm font-semibold text-gray-900 dark:text-slate-100">
                  v{updateInfo.current_version}
                </div>
              </div>

              <span className="text-sm font-semibold text-gray-400 dark:text-slate-500">
                →
              </span>

              <div className="text-right">
                <div className="text-xs text-gray-500 dark:text-slate-400">
                  New Release
                </div>
                <div
                  className="mt-0.5 font-mono text-sm font-semibold"
                  style={{ color: "var(--theme-primary, #2563eb)" }}
                >
                  v{updateInfo.latest_version}
                  {updateInfo.latest_build ? ` (#${updateInfo.latest_build})` : ""}
                </div>
              </div>
            </div>

            {/* Release Notes (shown before download or when ready) */}
            {!isDownloading && updateInfo.notes && (
              <div className="rounded-xl border border-gray-200/80 dark:border-slate-700/80 bg-white/60 dark:bg-slate-800/60 px-3.5 py-2">
                <div className="mb-1 text-xs font-semibold text-gray-700 dark:text-slate-300">
                  Release Notes
                </div>
                <p className="max-h-24 overflow-y-auto whitespace-pre-line text-xs leading-relaxed text-gray-600 dark:text-slate-300">
                  {String(updateInfo.notes)
                    .replace(/^#+\s*/gm, "")
                    .slice(0, 320)}
                </p>
              </div>
            )}

            {/* Zero Data Loss Notice */}
            {!isDownloading && (
              <div className="flex items-start gap-2.5 rounded-xl border border-gray-200/80 dark:border-slate-700/80 bg-white/60 dark:bg-slate-800/60 px-3.5 py-2 text-xs text-gray-600 dark:text-slate-300">
                <ShieldCheckIcon
                  className="mt-0.5 h-4 w-4 shrink-0"
                  style={{ color: "var(--theme-primary, #2563eb)" }}
                />
                <div>
                  <span className="font-semibold text-gray-900 dark:text-slate-100">
                    Safe In-Place Upgrade:
                  </span>{" "}
                  Your database, invoices, products, ledgers, and license remain intact and are backed up automatically.
                </div>
              </div>
            )}

            {/* State 1: Downloading in background */}
            {isDownloading || isApplying ? (
              <div className="space-y-2 pt-0.5">
                <div className="flex items-center justify-between text-xs">
                  <span className="inline-flex items-center gap-2 font-medium text-gray-700 dark:text-slate-200">
                    <ArrowPathIcon
                      className="h-4 w-4 animate-spin shrink-0"
                      style={{ color: "var(--theme-primary, #2563eb)" }}
                    />
                    <span className="truncate">
                      {progress.message || "Downloading update in background…"}
                    </span>
                  </span>
                  <span className="font-mono font-semibold text-gray-900 dark:text-slate-100 shrink-0 ml-2">
                    {progress.percent || 0}%
                  </span>
                </div>
                <div className="h-2 w-full overflow-hidden rounded-full bg-gray-200 dark:bg-slate-700">
                  <div
                    className="h-full rounded-full transition-all duration-300"
                    style={{
                      width: `${Math.max(4, Math.min(100, progress.percent || 5))}%`,
                      backgroundColor: "var(--theme-primary, #2563eb)",
                    }}
                  />
                </div>
                <p className="text-xs text-gray-500 dark:text-slate-400">
                  {isApplying
                    ? "Applying update and restarting Karobar-App…"
                    : "You can continue working while the update downloads. We will notify you when it is ready."}
                </p>
              </div>
            ) : isDownloaded ? (
              /* State 2: Downloaded & Ready (user chose "Later" to finish work first) */
              <div className="flex items-center justify-end gap-2 pt-1">
                <button
                  type="button"
                  onClick={dismissForOneDay}
                  className="g-btn-ghost px-3 cursor-pointer"
                >
                  Remind Tomorrow
                </button>
                <button
                  type="button"
                  onClick={handleApplyUpdate}
                  className="g-btn-base theme-btn-primary inline-flex items-center justify-center gap-2 px-4 cursor-pointer"
                >
                  <ArrowPathIcon className="h-4 w-4" />
                  <span>Update &amp; Restart Now</span>
                </button>
              </div>
            ) : (
              /* State 3: Initial notification — offer background download */
              <div className="flex items-center justify-end gap-2 pt-1">
                <button
                  type="button"
                  onClick={dismissForOneDay}
                  className="g-btn-ghost px-3 cursor-pointer"
                >
                  Later
                </button>
                <button
                  type="button"
                  onClick={handleDownloadUpdate}
                  disabled={isChecking}
                  className="g-btn-base theme-btn-primary inline-flex items-center justify-center gap-2 px-4 cursor-pointer"
                >
                  <CloudArrowDownIcon className="h-4 w-4" />
                  <span>Download Update</span>
                </button>
              </div>
            )}
          </div>
        </GlassCard>
      </div>

      {/* Completion Popup Modal: Shown when background download completes so user can choose "Update Now" or "Later" */}
      {showReadyPopup && isDownloaded && (
        <div
          className="fixed inset-0 z-[9995] flex items-center justify-center p-4 print:hidden"
          role="dialog"
          aria-modal="true"
          aria-labelledby="app-update-ready-modal-title"
          onClick={(e) => {
            if (e.target === e.currentTarget && !isApplying) {
              setShowReadyPopup(false);
            }
          }}
        >
          <div className="absolute inset-0 bg-black/40 backdrop-blur-xs" />
          <div className="relative w-full max-w-md">
            <GlassCard className="overflow-hidden">
              <GlassSectionHeader
                title={
                  <span id="app-update-ready-modal-title" className="inline-flex items-center gap-2">
                    <CheckCircleIcon
                      className="h-5 w-5 shrink-0"
                      style={{ color: "var(--theme-primary, #2563eb)" }}
                    />
                    <span>Update Download Complete</span>
                  </span>
                }
                right={
                  !isApplying ? (
                    <button
                      type="button"
                      onClick={() => setShowReadyPopup(false)}
                      className="h-8 w-8 inline-flex items-center justify-center rounded-lg text-slate-500 hover:bg-slate-200/60 dark:text-slate-300 dark:hover:bg-slate-700/60 transition-colors cursor-pointer"
                      title="Update Later"
                      aria-label="Close"
                    >
                      <XMarkIcon className="w-5 h-5" />
                    </button>
                  ) : null
                }
              />

              <div className="px-4 py-4 space-y-4">
                <p className="text-sm text-gray-600 dark:text-slate-300 leading-relaxed">
                  Karobar-App{" "}
                  <span className="font-mono font-semibold text-gray-900 dark:text-slate-100">
                    v{updateInfo.latest_version}
                  </span>{" "}
                  has finished downloading in the background and is ready to install.
                </p>

                <div className="rounded-xl border border-gray-200/80 dark:border-slate-700/80 bg-white/60 dark:bg-slate-800/60 px-3.5 py-2.5 text-xs text-gray-600 dark:text-slate-300">
                  If you are in the middle of an invoice or task, choose{" "}
                  <span className="font-semibold text-gray-900 dark:text-slate-100">
                    Update Later
                  </span>{" "}
                  to finish your work first — the update will stay ready at the bottom corner so you can install it anytime with one click.
                </div>

                {isApplying ? (
                  <div className="flex items-center gap-2.5 text-xs font-medium text-gray-700 dark:text-slate-200 pt-1">
                    <ArrowPathIcon
                      className="h-4 w-4 animate-spin shrink-0"
                      style={{ color: "var(--theme-primary, #2563eb)" }}
                    />
                    <span>{progress.message || "Creating safety backup & restarting…"}</span>
                  </div>
                ) : (
                  <div className="flex items-center justify-end gap-2 pt-1">
                    <button
                      type="button"
                      onClick={() => setShowReadyPopup(false)}
                      className="g-btn-ghost px-3.5 cursor-pointer"
                    >
                      Update Later
                    </button>
                    <button
                      type="button"
                      onClick={handleApplyUpdate}
                      className="g-btn-base theme-btn-primary inline-flex items-center justify-center gap-2 px-4 cursor-pointer"
                    >
                      <ArrowPathIcon className="h-4 w-4" />
                      <span>Update &amp; Restart Now</span>
                    </button>
                  </div>
                )}
              </div>
            </GlassCard>
          </div>
        </div>
      )}
    </>
  );
}

