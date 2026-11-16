import { createContext, useContext, useEffect, useMemo, useState, useCallback } from "react";
import axios from "axios";
import { useAuth } from "./AuthContext.jsx";

const LicenseCtx = createContext(null);

const LICENSE_STORAGE_KEY = "karobar_license_cache_v3";
// Periodic license check interval: 8 hours (3 times a day)
const LICENSE_CHECK_INTERVAL_MS = 8 * 60 * 60 * 1000;

export const LICENSE_TIERS_CATALOG = {
  basic: {
    key: "basic",
    label: "Basic",
    rank: 1,
    default_seats: 1,
    tagline: "All basic store, inventory, billing, ledger & reporting operations (1 PC)",
    modules: ["basic_operations"],
  },
  gold: {
    key: "gold",
    label: "Gold",
    rank: 2,
    default_seats: 1,
    tagline: "Everything in Basic + FBR Digital Invoicing & Barcode POS / Barcode Studio",
    modules: ["basic_operations", "barcode", "fbr"],
  },
  platinum: {
    key: "platinum",
    label: "Platinum",
    rank: 3,
    default_seats: 0,
    tagline: "Everything in Gold + Server & Multi-PC Terminals, GL Accounting, HR/Payroll & Cube AI",
    modules: [
      "basic_operations",
      "barcode",
      "fbr",
      "server_terminals",
      "accounting_gl",
      "hr_payroll",
      "cube_ai",
    ],
  },
};

export const LICENSE_MODULES_CATALOG = {
  basic_operations: {
    key: "basic_operations",
    label: "Basic Store Operations",
    description:
      "Products, Batches, Sales, Purchases, Returns, Inventory, Ledgers, Accounts, Cash Drawer, Expenses & Standard Reports",
    required_tier: "basic",
  },
  barcode: {
    key: "barcode",
    label: "Barcode POS & Barcode Studio",
    description:
      "Fast Fullscreen Barcode Scanner POS & Custom Barcode Label Designer / Printer",
    required_tier: "gold",
  },
  fbr: {
    key: "fbr",
    label: "FBR Digital Invoicing & POS Tax",
    description:
      "Official FBR QR & Logo Receipt Stamp, PRAL Real-Time Sync, Offline Queue & Statutory Tax Return Report",
    required_tier: "gold",
  },
  server_terminals: {
    key: "server_terminals",
    label: "Server & Multi-PC Terminals",
    description:
      "Multi-Counter LAN SyncPulse, Attached Terminals Dashboard & 1-Click Server DB Import/Export",
    required_tier: "platinum",
  },
  accounting_gl: {
    key: "accounting_gl",
    label: "Double-Entry Accounting & GL",
    description: "Chart of Accounts, Journal Vouchers & Financial Statements",
    required_tier: "platinum",
  },
  hr_payroll: {
    key: "hr_payroll",
    label: "HR, Attendance & Payroll",
    description:
      "Employees, Attendance Tracking, Staff Advances, Sales Commissions & Monthly Payroll Runs",
    required_tier: "platinum",
  },
  cube_ai: {
    key: "cube_ai",
    label: "Cube AI Assistant & SQLite Migration",
    description:
      "Hybrid AI Co-Pilot, Automated Task Runner & Intelligent SQLite Database Import",
    required_tier: "platinum",
  },
};

export function resolveClientTier(payload, explicitTier = null) {
  const raw = String(
    explicitTier ||
      payload?.tier ||
      payload?.license_type ||
      payload?.Edition ||
      payload?.edition ||
      payload?.plan ||
      ""
  )
    .trim()
    .toLowerCase();

  if (["basic", "basic_pos", "basic pos", "starter", "standard", "silver"].includes(raw) || raw.includes("basic")) {
    return "basic";
  }
  if (["gold", "gold_edition", "gold edition", "fbr", "fbr_edition"].includes(raw) || raw.includes("gold")) {
    return "gold";
  }
  return "platinum";
}

export function resolveClientModules(payload, tierKey = null) {
  const resolvedTier = tierKey || resolveClientTier(payload);
  const baseModules =
    LICENSE_TIERS_CATALOG[resolvedTier]?.modules ||
    LICENSE_TIERS_CATALOG.platinum.modules;

  const extra = Array.isArray(payload?.modules)
    ? payload.modules
        .map((m) => String(m || "").trim().toLowerCase())
        .filter((m) => Boolean(LICENSE_MODULES_CATALOG[m]))
    : [];

  return Array.from(new Set([...baseModules, ...extra]));
}

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
      const resolvedTier = isValid
        ? resolveClientTier(data?.payload, data?.tier)
        : null;
      const resolvedModules = isValid
        ? Array.isArray(data?.modules) && data.modules.length > 0
          ? data.modules
          : resolveClientModules(data?.payload, resolvedTier)
        : [];
      const resolvedSeats = isValid
        ? Number(data?.seats ?? data?.payload?.seats ?? (resolvedTier === "platinum" ? 0 : 1))
        : 0;

      const nextStatus = {
        loading: false,
        valid: isValid,
        expiresAt: expMs,
        payload: data?.payload ?? null,
        machineId: data?.machine_id ?? null,
        reason: data?.reason ?? null,
        tier: resolvedTier,
        tierLabel:
          data?.tier_label ??
          (resolvedTier ? LICENSE_TIERS_CATALOG[resolvedTier]?.label : null),
        modules: resolvedModules,
        seats: resolvedSeats,
        tiersCatalog: data?.tiers_catalog ?? LICENSE_TIERS_CATALOG,
        modulesCatalog: data?.modules_catalog ?? LICENSE_MODULES_CATALOG,
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

  const effectiveTier = useMemo(() => {
    if (!status.valid) return null;
    return status.tier || resolveClientTier(status.payload);
  }, [status.valid, status.tier, status.payload]);

  const effectiveTierLabel = useMemo(() => {
    if (!effectiveTier) return null;
    return status.tierLabel || LICENSE_TIERS_CATALOG[effectiveTier]?.label || "Platinum";
  }, [effectiveTier, status.tierLabel]);

  const effectiveModules = useMemo(() => {
    if (!status.valid) return [];
    if (Array.isArray(status.modules) && status.modules.length > 0) {
      return status.modules;
    }
    return resolveClientModules(status.payload, effectiveTier);
  }, [status.valid, status.modules, status.payload, effectiveTier]);

  const hasModule = useCallback(
    (moduleKey) => {
      if (!status.valid) return false;
      if (!moduleKey) return true;
      return effectiveModules.includes(String(moduleKey).trim().toLowerCase());
    },
    [status.valid, effectiveModules]
  );

  const hasTier = useCallback(
    (minimumTier) => {
      if (!status.valid || !effectiveTier) return false;
      const currentRank = LICENSE_TIERS_CATALOG[effectiveTier]?.rank ?? 3;
      const reqRank = LICENSE_TIERS_CATALOG[String(minimumTier || "").trim().toLowerCase()]?.rank ?? 3;
      return currentRank >= reqRank;
    },
    [status.valid, effectiveTier]
  );

  const contextValue = useMemo(
    () => ({
      ...status,
      tier: effectiveTier,
      tierLabel: effectiveTierLabel,
      modules: effectiveModules,
      tiersCatalog: status.tiersCatalog || LICENSE_TIERS_CATALOG,
      modulesCatalog: status.modulesCatalog || LICENSE_MODULES_CATALOG,
      hasModule,
      hasTier,
      loading: effectiveLoading,
      remainingMs,
      refresh,
    }),
    [
      status,
      effectiveTier,
      effectiveTierLabel,
      effectiveModules,
      hasModule,
      hasTier,
      effectiveLoading,
      remainingMs,
      refresh,
    ]
  );

  return (
    <LicenseCtx.Provider value={contextValue}>
      {children}
    </LicenseCtx.Provider>
  );
}

export function useLicense() {
  return (
    useContext(LicenseCtx) ?? {
      loading: true,
      valid: false,
      tier: null,
      tierLabel: null,
      modules: [],
      hasModule: () => false,
      hasTier: () => false,
    }
  );
}
