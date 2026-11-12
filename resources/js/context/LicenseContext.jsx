import { createContext, useContext, useEffect, useMemo, useState, useCallback } from "react";
import axios from "axios";
import { useAuth } from "./AuthContext.jsx";

const LicenseCtx = createContext(null);

const LICENSE_STORAGE_KEY = "karobar_license_cache_v2";
// Periodic license check interval: 8 hours (3 times a day)
const LICENSE_CHECK_INTERVAL_MS = 8 * 60 * 60 * 1000;

function isFreshValidCache(entry, token) {
  if (!entry || !entry.valid || entry.checkedToken !== token) return false;
  const now = Date.now();
  const verifiedAt = Number(entry.verifiedAt || 0);
  if (!verifiedAt || now < verifiedAt || now - verifiedAt >= LICENSE_CHECK_INTERVAL_MS) {
    return false;
  }
  if (entry.expiresAt && Number(entry.expiresAt) > 0 && now >= Number(entry.expiresAt)) {
    return false;
  }
  return true;
}

function readCachedLicense(token) {
  if (!token || typeof window === "undefined") return null;
  try {
    const rootState = (window.top || window).__karobarAppState?.license;
    if (isFreshValidCache(rootState, token)) {
      return rootState;
    }
  } catch {
    /* ignore */
  }
  try {
    const raw =
      localStorage.getItem(LICENSE_STORAGE_KEY) ||
      sessionStorage.getItem(LICENSE_STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      if (isFreshValidCache(parsed, token)) {
        try {
          const rootWin = window.top || window;
          rootWin.__karobarAppState = rootWin.__karobarAppState || {};
          rootWin.__karobarAppState.license = parsed;
        } catch {}
        return parsed;
      }
    }
  } catch {
    /* ignore */
  }
  return null;
}

function writeCachedLicense(statusObj) {
  if (!statusObj?.checkedToken || typeof window === "undefined") return;
  const payloadToStore = {
    ...statusObj,
    verifiedAt: statusObj.verifiedAt || Date.now(),
  };
  try {
    const rootWin = window.top || window;
    rootWin.__karobarAppState = rootWin.__karobarAppState || {};
    rootWin.__karobarAppState.license = payloadToStore;
  } catch {
    /* ignore */
  }
  try {
    const serialized = JSON.stringify(payloadToStore);
    localStorage.setItem(LICENSE_STORAGE_KEY, serialized);
    sessionStorage.setItem(LICENSE_STORAGE_KEY, serialized);
  } catch {
    /* ignore */
  }
}

function clearCachedLicense() {
  if (typeof window === "undefined") return;
  try {
    const rootWin = window.top || window;
    if (rootWin.__karobarAppState) {
      delete rootWin.__karobarAppState.license;
    }
  } catch {}
  try {
    localStorage.removeItem(LICENSE_STORAGE_KEY);
    sessionStorage.removeItem(LICENSE_STORAGE_KEY);
  } catch {}
}

export function LicenseProvider({ children }) {
  const auth = useAuth();
  const currentToken =
    auth?.token || (typeof window !== "undefined" ? localStorage.getItem("token") : null);

  const initialCache = readCachedLicense(currentToken);

  const [status, setStatus] = useState(() =>
    initialCache || {
      loading: true,
      valid: false,
      expiresAt: null,      // ms epoch
      payload: null,
      machineId: null,
      reason: null,
      checkedToken: null,
    }
  );

  // Track if we've seen a valid license at least once
  const [hasEverBeenValid, setHasEverBeenValid] = useState(() => Boolean(initialCache?.valid));

  const refresh = useCallback(async (retryOnFail = true) => {
    const activeToken =
      typeof window !== "undefined" ? localStorage.getItem("token") : null;

    // Do not call auth-protected /api/license/status when logged out
    if (!activeToken) {
      setStatus((s) => ({
        ...s,
        loading: true,
        checkedToken: null,
      }));
      return;
    }

    try {
      const { data } = await axios.get("/api/license/status", {
        headers: { Authorization: `Bearer ${activeToken}` },
      });
      // server returns expires_at as seconds (from payload['exp'])
      const expSec = data?.expires_at ?? data?.payload?.exp ?? null;
      const expMs =
        typeof expSec === "number" ? expSec * 1000 :
        typeof expSec === "string" ? Date.parse(expSec) : null;

      const isValid = !!data?.valid;
      const nextStatus = {
        loading: false,
        valid: isValid,
        expiresAt: expMs,
        payload: data?.payload ?? null,
        machineId: data?.machine_id ?? null,
        reason: data?.reason ?? null,
        checkedToken: activeToken,
        verifiedAt: Date.now(),
      };
      
      setStatus(nextStatus);

      if (isValid) {
        setHasEverBeenValid(true);
        writeCachedLicense(nextStatus);
      } else {
        clearCachedLicense();
      }
    } catch (error) {
      // A 401 means unauthenticated/expired session, NOT an invalid license
      if (error?.response?.status === 401 || !localStorage.getItem("token")) {
        setStatus((s) => ({
          ...s,
          loading: true,
          checkedToken: null,
        }));
        return;
      }

      // On network errors, retry once after a delay unless we've already seen a valid license
      if (retryOnFail && !hasEverBeenValid) {
        setTimeout(() => refresh(false), 2000);
        return;
      }
      setStatus((s) => ({
        ...s,
        loading: false,
        valid: hasEverBeenValid ? s.valid : false,
        checkedToken: activeToken,
      }));
    }
  }, [hasEverBeenValid]);

  useEffect(() => {
    if (!currentToken) {
      setStatus((s) => ({
        ...s,
        loading: true,
        checkedToken: null,
      }));
      return undefined;
    }

    const cached = readCachedLicense(currentToken);
    if (cached) {
      setStatus(cached);
      setHasEverBeenValid(true);
      const elapsed = Math.max(0, Date.now() - Number(cached.verifiedAt || 0));
      const nextDelay = Math.max(60_000, LICENSE_CHECK_INTERVAL_MS - elapsed);
      const timerId = setTimeout(() => {
        refresh();
      }, nextDelay);
      return () => clearTimeout(timerId);
    }

    refresh();
    const intervalId = setInterval(() => {
      refresh();
    }, LICENSE_CHECK_INTERVAL_MS);
    return () => clearInterval(intervalId);
  }, [currentToken, refresh]);

  // Only tick when a finite expiresAt exists; use 60s interval when >24h remains to avoid 1-second global re-renders
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!status.expiresAt) return undefined;
    const msLeft = status.expiresAt - Date.now();
    const intervalMs = msLeft > 86_400_000 ? 60_000 : 1_000;
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [status.expiresAt]);

  const remainingMs = useMemo(() => {
    if (!status.expiresAt) return null;
    return Math.max(0, status.expiresAt - now);
  }, [status.expiresAt, now]);

  // When license expires (remainingMs hits zero), show warning but DON'T auto-redirect
  // The server-side EnsureLicensed middleware will handle the actual enforcement
  // Auto-redirect causes issues when user has a valid license but there's a timing issue
  useEffect(() => {
    if (status.valid && status.expiresAt && remainingMs === 0) {
      // License has expired - show a warning notification
      // Don't auto-redirect as it causes issues with valid licenses
      console.warn('License has expired');
    }
  }, [status.valid, status.expiresAt, remainingMs]);

  const effectiveLoading =
    !currentToken ||
    status.loading ||
    (!status.valid && status.checkedToken !== currentToken);

  const contextValue = useMemo(
    () => ({ ...status, loading: effectiveLoading, remainingMs, refresh }),
    [status, effectiveLoading, remainingMs, refresh]
  );

  return (
    <LicenseCtx.Provider value={contextValue}>
      {children}
    </LicenseCtx.Provider>
  );
}

export function useLicense() {
  return useContext(LicenseCtx) ?? { loading: true, valid: false };
}
