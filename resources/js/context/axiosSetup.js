import axios from "axios";
import { saveLastVisitedPath } from "../utils/lastVisitedPage.js";
import {
  escapeHtml,
  classifyAuth401Action,
  getBrowserPairingTokenHeader,
} from "../utils/securityHelpers.js";

function getSharedCacheStore() {
  if (typeof window !== "undefined") {
    try {
      const rootWin = window.top || window;
      if (!rootWin.__karobarSharedApiCache) {
        rootWin.__karobarSharedApiCache = {
          inflightGetMap: new Map(),
          responseCacheMap: new Map(),
          stockVersion: 1,
        };
      }
      return rootWin.__karobarSharedApiCache;
    } catch {
      if (!window.__karobarSharedApiCache) {
        window.__karobarSharedApiCache = {
          inflightGetMap: new Map(),
          responseCacheMap: new Map(),
          stockVersion: 1,
        };
      }
      return window.__karobarSharedApiCache;
    }
  }
  return {
    inflightGetMap: new Map(),
    responseCacheMap: new Map(),
    stockVersion: 1,
  };
}

const sharedCacheStore = getSharedCacheStore();
const inflightGetMap = sharedCacheStore.inflightGetMap;
const responseCacheMap = sharedCacheStore.responseCacheMap;

const LONG_TTL_MS = 60_000; // 60s for session/static config endpoints
const MEDIUM_TTL_MS = 15_000; // 15s for lookup/search endpoints (invalidated on any mutation)
const SHORT_TTL_MS = 5_000; // 5s for burst deduplication across components

const LONG_TTL_PATTERNS = [
  /^\/api\/me(\?|$)/,
  /^\/api\/user(\?|$)/,
  /^\/api\/settings(\?|$)/,
  /^\/api\/currency(\?|$)/,
  /^\/api\/sale-system(\?|$)/,
  /^\/api\/theme-settings\/active(\?|$)/,
  /^\/api\/preferences(\?|$)/,
  /^\/api\/license\/status(\?|$)/,
];

const MEDIUM_TTL_PATTERNS = [
  /^\/api\/products\/search(\?|$)/,
  /^\/api\/customers\/search(\?|$)/,
  /^\/api\/suppliers\/search(\?|$)/,
  /^\/api\/brands(\?|$)/,
  /^\/api\/categories(\?|$)/,
];

function getTtlForUrl(url) {
  if (typeof url !== "string" || !url.startsWith("/api/")) return 0;
  for (const regex of LONG_TTL_PATTERNS) {
    if (regex.test(url)) return LONG_TTL_MS;
  }
  for (const regex of MEDIUM_TTL_PATTERNS) {
    if (regex.test(url)) return MEDIUM_TTL_MS;
  }
  return SHORT_TTL_MS;
}

function stableParamsKey(params) {
  if (!params || typeof params !== "object") return "";
  try {
    const keys = Object.keys(params).sort();
    const normalized = {};
    for (const k of keys) {
      const val = params[k];
      if (val !== undefined) normalized[k] = val;
    }
    return JSON.stringify(normalized);
  } catch {
    return "";
  }
}

function cloneResponse(res) {
  if (!res || typeof res !== "object") return res;
  let clonedData = res.data;
  try {
    if (clonedData !== null && typeof clonedData === "object") {
      clonedData =
        typeof structuredClone === "function"
          ? structuredClone(clonedData)
          : JSON.parse(JSON.stringify(clonedData));
    }
  } catch {
    // Fallback to shallow reference if non-clonable (e.g. Blob)
  }
  return { ...res, data: clonedData };
}

const PERSISTED_BOOT_CACHE_PREFIX = "karobar_boot_api_v1:";

function isLongTtlUrl(url) {
  if (typeof url !== "string") return false;
  return LONG_TTL_PATTERNS.some((rx) => rx.test(url));
}

function readPersistedBootCache(cacheKey) {
  try {
    const raw = localStorage.getItem(PERSISTED_BOOT_CACHE_PREFIX + cacheKey);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (parsed && parsed.expiresAt > Date.now() && parsed.data !== undefined) {
      return {
        status: 200,
        statusText: "OK",
        headers: {},
        config: {},
        data: parsed.data,
      };
    }
  } catch {
    // ignore storage errors
  }
  return null;
}

function writePersistedBootCache(cacheKey, data, expiresAt) {
  try {
    localStorage.setItem(
      PERSISTED_BOOT_CACHE_PREFIX + cacheKey,
      JSON.stringify({ data, expiresAt })
    );
  } catch {
    // ignore quota errors
  }
}

function clearPersistedBootCache(prefix = null) {
  try {
    const toRemove = [];
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k.startsWith(PERSISTED_BOOT_CACHE_PREFIX)) {
        if (!prefix || k.includes(prefix)) {
          toRemove.push(k);
        }
      }
    }
    for (const k of toRemove) {
      localStorage.removeItem(k);
    }
  } catch {
    // ignore storage errors
  }
}

export function clearApiCache(prefix = null) {
  clearPersistedBootCache(prefix);
  if (!prefix) {
    inflightGetMap.clear();
    responseCacheMap.clear();
    return;
  }
  for (const key of responseCacheMap.keys()) {
    if (key.includes(prefix)) {
      responseCacheMap.delete(key);
    }
  }
}

const SESSION_CONFIG_MUTATION_REGEX =
  /\/api\/(settings|theme-settings|preferences|roles|permissions|users|profile|license|login|logout)/;

function invalidateCacheForMutation(mutationUrl = "") {
  if (mutationUrl.includes("/session/extend")) {
    return;
  }
  if (!mutationUrl || SESSION_CONFIG_MUTATION_REGEX.test(mutationUrl)) {
    clearApiCache();
    return;
  }
  inflightGetMap.clear();
  for (const key of responseCacheMap.keys()) {
    const parts = key.split("|");
    const cachedUrl = parts[1] || "";
    const isStaticSessionConfig = LONG_TTL_PATTERNS.some((rx) => rx.test(cachedUrl));
    if (!isStaticSessionConfig) {
      responseCacheMap.delete(key);
    }
  }
}

let notifDebounceTimer = null;
let drawerDebounceTimer = null;
let inventoryDebounceTimer = null;
let inventoryChannel = null;

try {
  if (typeof BroadcastChannel !== "undefined") {
    inventoryChannel = new BroadcastChannel("karobar_inventory_bus");
    inventoryChannel.onmessage = (event) => {
      if (event?.data?.type === "inventory_changed") {
        clearApiCache("/api/products");
        clearApiCache("/api/batches");
        try {
          window.dispatchEvent(
            new CustomEvent("karobar:inventory-changed", { detail: event.data })
          );
          window.dispatchEvent(new CustomEvent("karobar:products-updated"));
        } catch {}
      }
    };
  }
} catch {
  // BroadcastChannel unavailable in current environment
}

if (typeof window !== "undefined") {
  window.addEventListener("storage", (e) => {
    if (e.key === "karobar_inventory_sync") {
      clearApiCache("/api/products");
      clearApiCache("/api/batches");
      try {
        window.dispatchEvent(new CustomEvent("karobar:inventory-changed"));
        window.dispatchEvent(new CustomEvent("karobar:products-updated"));
      } catch {}
    }
  });
}

function broadcastInventoryChange(meta = {}) {
  if (inventoryDebounceTimer) window.clearTimeout(inventoryDebounceTimer);
  inventoryDebounceTimer = window.setTimeout(() => {
    try {
      window.dispatchEvent(
        new CustomEvent("karobar:inventory-changed", { detail: meta })
      );
      window.dispatchEvent(new CustomEvent("karobar:products-updated"));
      if (window.top && window.top !== window) {
        window.top.dispatchEvent(
          new CustomEvent("karobar:inventory-changed", { detail: meta })
        );
        window.top.dispatchEvent(new CustomEvent("karobar:products-updated"));
      }
    } catch {}

    let sentViaChannel = false;
    try {
      if (inventoryChannel) {
        inventoryChannel.postMessage({
          type: "inventory_changed",
          ...meta,
          timestamp: Date.now(),
        });
        sentViaChannel = true;
      }
    } catch {}

    if (!sentViaChannel) {
      try {
        localStorage.setItem("karobar_inventory_sync", String(Date.now()));
      } catch {}
    }
  }, 100);
}

function dispatchSessionExpiredModal(detail = {}) {
  try {
    window.dispatchEvent(new CustomEvent("karobar:session-expired", { detail }));
    if (window.top && window.top !== window) {
      window.top.dispatchEvent(new CustomEvent("karobar:session-expired", { detail }));
    }
  } catch {
    // ignore cross-frame dispatch errors
  }
}

function forceLogoutAndRedirect() {
  saveLastVisitedPath(window.location);
  clearApiCache();
  localStorage.removeItem("token");
  localStorage.removeItem("user");
  localStorage.removeItem("token_expires_at");
  localStorage.removeItem("karobar_session_prompt_at");
  delete axios.defaults.headers.common["Authorization"];
  if (window.location.pathname !== "/") {
    window.location.assign("/");
  }
}

let isInitialized = false;

export function initAxiosAuth() {
  if (isInitialized) return;
  isInitialized = true;

  axios.defaults.headers.common["X-Requested-With"] = "XMLHttpRequest";

  // Wrap axios.get for in-flight deduplication and short-TTL memory caching
  const rawGet = axios.get.bind(axios);
  axios.get = function cachedAxiosGet(url, config) {
    const ttl = getTtlForUrl(url);
    const hasAbortControl = Boolean(config?.signal || config?.cancelToken);
    const skipCacheHeader =
      config?.headers?.["X-Skip-Cache"] ??
      config?.headers?.["x-skip-cache"];
    const skipCache =
      !ttl ||
      config?.cache === false ||
      skipCacheHeader === "1" ||
      skipCacheHeader === 1 ||
      skipCacheHeader === true ||
      config?.signal?.aborted ||
      (config?.responseType && config.responseType !== "json");

    if (skipCache) {
      return rawGet(url, config);
    }

    const token = localStorage.getItem("token") || "anon";
    const cacheKey = `${token.slice(-12)}|${url}|${stableParamsKey(config?.params)}`;
    const now = Date.now();

    const cached = responseCacheMap.get(cacheKey);
    if (cached && cached.expiresAt > now) {
      return Promise.resolve(cloneResponse(cached.response));
    }

    const isBootEndpoint = isLongTtlUrl(url);
    if (isBootEndpoint) {
      const persisted = readPersistedBootCache(cacheKey);
      if (persisted) {
        responseCacheMap.set(cacheKey, {
          response: cloneResponse(persisted),
          expiresAt: now + ttl,
        });
        return Promise.resolve(cloneResponse(persisted));
      }
    }

    if (!hasAbortControl) {
      const existingInflight = inflightGetMap.get(cacheKey);
      if (existingInflight) {
        return Promise.resolve(existingInflight).then((res) => cloneResponse(res));
      }
    }

    const reqPromise = rawGet(url, config)
      .then((res) => {
        if (!hasAbortControl) {
          inflightGetMap.delete(cacheKey);
        }
        if (res && res.status >= 200 && res.status < 300) {
          // Bound cache size to prevent memory bloat
          if (responseCacheMap.size > 300) {
            const oldestKey = responseCacheMap.keys().next().value;
            if (oldestKey) responseCacheMap.delete(oldestKey);
          }
          const expiresAt = Date.now() + ttl;
          responseCacheMap.set(cacheKey, {
            response: cloneResponse(res),
            expiresAt,
          });
          if (isBootEndpoint) {
            writePersistedBootCache(cacheKey, res.data, expiresAt);
          }
        }
        return res;
      })
      .catch((err) => {
        if (!hasAbortControl) {
          inflightGetMap.delete(cacheKey);
        }
        throw err;
      });

    if (!hasAbortControl) {
      inflightGetMap.set(cacheKey, reqPromise);
    }
    return reqPromise.then((res) => cloneResponse(res));
  };

  // Request interceptor to attach token on every request
  // This ensures the token is always available even if localStorage changes
  axios.interceptors.request.use(
    (config) => {
      const token = localStorage.getItem("token");
      if (token) {
        config.headers.Authorization = `Bearer ${token}`;
      }
      try {
        const isDesktopApp = Boolean(window.karobarDesktop?.isDesktop);
        let termUid = localStorage.getItem("karobar_terminal_uid");
        // In the native desktop app, never invent a random TERM-DESK-* ID before getNetworkConfig() loads;
        // KestrelHostServer authoritatively injects the PC's canonical TerminalId from network-mode.json.
        if (isDesktopApp && termUid && termUid.startsWith("TERM-DESK-")) {
          localStorage.removeItem("karobar_terminal_uid");
          termUid = null;
        }
        if (!termUid && !isDesktopApp) {
          const rand = Math.random().toString(36).slice(2, 10).toUpperCase();
          termUid = `TERM-WEB-${rand}`;
          localStorage.setItem("karobar_terminal_uid", termUid);
        }
        const termName = localStorage.getItem("karobar_terminal_name") || "";
        const termMode = localStorage.getItem("karobar_terminal_mode") || (isDesktopApp ? "standalone" : "browser");
        const termHost = localStorage.getItem("karobar_terminal_hostname") || "";
        const pairingToken = getBrowserPairingTokenHeader({
          isDesktop: isDesktopApp,
          storage: localStorage,
        });
        if (termUid && !config.headers["X-Terminal-Id"]) {
          config.headers["X-Terminal-Id"] = termUid;
        }
        if (termName && !config.headers["X-Terminal-Name"]) {
          config.headers["X-Terminal-Name"] = termName;
        }
        if (!config.headers["X-Terminal-Mode"]) {
          config.headers["X-Terminal-Mode"] = termMode;
        }
        if (termHost && !config.headers["X-Terminal-Hostname"]) {
          config.headers["X-Terminal-Hostname"] = termHost;
        }
        if (pairingToken && !config.headers["X-Network-Token"]) {
          config.headers["X-Network-Token"] = pairingToken;
        }
      } catch {
        // ignore storage access errors
      }
      const method = (config?.method || "get").toLowerCase();
      const reqUrl = config?.url || "";
      if (
        ["post", "put", "patch", "delete"].includes(method) &&
        !reqUrl.includes("/api/cube/") &&
        !reqUrl.includes("/session/extend")
      ) {
        invalidateCacheForMutation(reqUrl);
      }
      return config;
    },
    (error) => {
      return Promise.reject(error);
    }
  );

  axios.interceptors.response.use(
    (r) => {
      try {
        const method = (r?.config?.method || "get").toLowerCase();
        const url = r?.config?.url || "";
        const isCubeEndpoint = url.includes("/api/cube/");
        const cubeDidMutateProducts = Boolean(r?.data?.task_meta?.refresh_products);
        if (
          ["post", "put", "patch", "delete"].includes(method) &&
          (!isCubeEndpoint || cubeDidMutateProducts) &&
          !url.includes("/session/extend")
        ) {
          invalidateCacheForMutation(url);
        }
        if (
          ["post", "put", "patch", "delete"].includes(method) &&
          /\/api\/(sale-invoices|purchase-invoices|purchase-orders|sale-returns|purchase-returns|user-demands|stock-adjustments|stock-disposals|stocktake-sessions|batches|products)/.test(
            url
          ) &&
          !url.includes("/notifications/")
        ) {
          clearApiCache("/api/products");
          clearApiCache("/api/batches");
          broadcastInventoryChange({ url, method });

          if (notifDebounceTimer) window.clearTimeout(notifDebounceTimer);
          notifDebounceTimer = window.setTimeout(() => {
            window.dispatchEvent(
              new CustomEvent("notifications-changed", { detail: { url, method } })
            );
          }, 250);
        }
        if (
          ["post", "put", "patch", "delete"].includes(method) &&
          /\/api\/(sale-invoices|purchase-invoices|sale-returns|purchase-returns|payments|accounts|expenses|cash-drawer)/.test(
            url
          )
        ) {
          if (drawerDebounceTimer) window.clearTimeout(drawerDebounceTimer);
          drawerDebounceTimer = window.setTimeout(() => {
            window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
            window.dispatchEvent(new CustomEvent("accounts-changed"));
          }, 200);
        }
      } catch {
        /* ignore event dispatch errors */
      }
      return r;
    },
    (err) => {
      const status = err?.response?.status;
      const url = err?.config?.url || "";

      // Only handle license errors from actual license-protected endpoints
      // Don't redirect for /license/status - let the LicenseContext handle it
      const isLicenseApi = url.includes("/license/");
      const isLicenseStatusEndpoint = url === "/api/license/status";
      const isAuth = url.includes("/login") || url.includes("/user") || url.includes("/me");
      const isCubeApi = url.includes("/api/cube/");

      // Never trigger a full-page browser navigation for background Cube / GuideBot requests;
      // let FeatureGuideBot catch errors and gracefully fall back without refreshing the screen.
      if (isCubeApi) {
        return Promise.reject(err);
      }

      // Handle blocked / pending-approval LAN Terminal (HTTP 403 TERMINAL_UNAUTHORIZED)
      if (status === 403 && err?.response?.data?.code === "TERMINAL_UNAUTHORIZED") {
        try {
          const detail = err.response.data || {};
          window.dispatchEvent(new CustomEvent("karobar:terminal-unauthorized", { detail }));
          if (typeof document !== "undefined" && !document.getElementById("karobar-terminal-blocked-overlay")) {
            const overlay = document.createElement("div");
            overlay.id = "karobar-terminal-blocked-overlay";
            overlay.style.cssText =
              "position:fixed;inset:0;z-index:2147483640;background:rgba(15,23,42,0.92);backdrop-filter:blur(6px);display:flex;align-items:center;justify-content:center;padding:24px;font-family:system-ui,-apple-system,Segoe UI,sans-serif;color:#f8fafc;";
            const tName = escapeHtml(detail.terminal_name || localStorage.getItem("karobar_terminal_name") || "This Terminal");
            const tUid = escapeHtml(detail.terminal_uid || localStorage.getItem("karobar_terminal_uid") || "");
            const isDesktopHost = Boolean(window.karobarDesktop?.isDesktop);
            overlay.innerHTML =
              '<div style="max-width:520px;width:100%;background:#1e293b;border:1px solid #475569;border-radius:16px;padding:28px;box-shadow:0 25px 50px rgba(0,0,0,0.5);">' +
              '<div style="display:inline-flex;align-items:center;gap:8px;padding:5px 12px;border-radius:999px;background:rgba(245,158,11,0.18);color:#fcd34d;font-size:12px;font-weight:700;margin-bottom:14px;">Terminal Authorization Required</div>' +
              '<h2 style="font-size:20px;font-weight:800;margin:0 0 8px 0;color:#fff;">' + tName + ' Is Pending Approval or Blocked</h2>' +
              '<p style="font-size:13.5px;color:#cbd5e1;line-height:1.55;margin:0 0 14px 0;">Ask the Store Administrator to open <strong>Settings &rarr; Server &amp; Terminals</strong> on the Store Server PC and click <strong>Authorize</strong> for Terminal ID <code style="background:#0f172a;padding:2px 6px;border-radius:6px;color:#38bdf8;">' + tUid + '</code>.</p>' +
              '<div style="display:flex;gap:10px;flex-wrap:wrap;margin-top:18px;">' +
              '<button id="krb-term-retry-btn" style="flex:1;padding:10px 16px;border-radius:10px;border:none;background:#2563eb;color:#fff;font-weight:700;cursor:pointer;">Retry Connection</button>' +
              (isDesktopHost ? '<button id="krb-term-standalone-btn" style="padding:10px 16px;border-radius:10px;border:1px solid #475569;background:transparent;color:#cbd5e1;font-weight:600;cursor:pointer;">Switch to Standalone Mode</button>' : '') +
              '</div></div>';
            document.body.appendChild(overlay);
            document.getElementById("krb-term-retry-btn")?.addEventListener("click", () => {
              overlay.remove();
              window.location.reload();
            });
            document.getElementById("krb-term-standalone-btn")?.addEventListener("click", async () => {
              try {
                await window.karobarDesktop?.saveNetworkConfig?.({ mode: "standalone", relaunch: true });
              } catch {}
            });
          }
        } catch {}
        return Promise.reject(err);
      }

      // Only redirect to activate for 402 from protected API endpoints (not status check)
      if (status === 402 && !isLicenseApi && !isLicenseStatusEndpoint && !isAuth) {
        // stop console spam & bounce to activate
        window.location.assign("/activate");
        return Promise.reject(err);
      }

      // When a 401 occurs while logged in, do NOT abruptly close the session, reload the page,
      // or spam `/api/session/extend` with an already-rejected token. Instead, open the
      // SessionExpiryModal in inline re-authentication mode so unsaved work remains intact.
      if (status === 401) {
        const currentToken = localStorage.getItem("token");
        const action = classifyAuth401Action({
          status,
          url,
          token: currentToken,
          pathname: typeof window !== "undefined" ? window.location.pathname : "/",
        });
        if (action === "force_logout") {
          forceLogoutAndRedirect();
          return Promise.reject(err);
        }
        if (action === "prompt_reauth") {
          dispatchSessionExpiredModal({ reason: "unauthorized", status: 401, url });
          return Promise.reject(err);
        }
      }

      return Promise.reject(err);
    }
  );
}
