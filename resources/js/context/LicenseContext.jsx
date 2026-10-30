import { createContext, useContext, useEffect, useMemo, useState, useCallback } from "react";
import axios from "axios";

const LicenseCtx = createContext(null);

export function LicenseProvider({ children }) {
  const [status, setStatus] = useState({
    loading: true,
    valid: false,
    expiresAt: null,      // ms epoch
    payload: null,
    machineId: null,
    reason: null,
  });

  // Track if we've seen a valid license at least once
  const [hasEverBeenValid, setHasEverBeenValid] = useState(false);

  const refresh = useCallback(async (retryOnFail = true) => {
    try {
      const { data } = await axios.get("/api/license/status");
      // server returns expires_at as seconds (from payload['exp'])
      const expSec = data?.expires_at ?? data?.payload?.exp ?? null;
      const expMs =
        typeof expSec === "number" ? expSec * 1000 :
        typeof expSec === "string" ? Date.parse(expSec) : null;

      const isValid = !!data?.valid;
      
      setStatus({
        loading: false,
        valid: isValid,
        expiresAt: expMs,
        payload: data?.payload ?? null,
        machineId: data?.machine_id ?? null,
        reason: data?.reason ?? null,
      });

      if (isValid) {
        setHasEverBeenValid(true);
      }
    } catch (error) {
      // On network errors, retry once after a delay unless we've already seen a valid license
      if (retryOnFail && !hasEverBeenValid) {
        setTimeout(() => refresh(false), 2000);
        return;
      }
      setStatus((s) => ({ ...s, loading: false, valid: false }));
    }
  }, [hasEverBeenValid]);

  useEffect(() => { refresh(); }, [refresh]);

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

  const contextValue = useMemo(
    () => ({ ...status, remainingMs, refresh }),
    [status, remainingMs, refresh]
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
