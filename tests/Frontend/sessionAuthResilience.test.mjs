import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import {
  isAuthInvalidStatus,
  classifyAuth401Action,
  resolveSessionModalMode,
  shouldClearAuthOnUserFetchError,
  syncDesktopNetworkStorage,
  getBrowserPairingTokenHeader,
} from "../../resources/js/utils/securityHelpers.js";

function createMemoryStorage(initial = {}) {
  const map = new Map(Object.entries(initial));
  return {
    getItem(k) {
      return map.has(k) ? map.get(k) : null;
    },
    setItem(k, v) {
      map.set(k, String(v));
    },
    removeItem(k) {
      map.delete(k);
    },
    snapshot() {
      return Object.fromEntries(map.entries());
    },
  };
}

test("ISS-03: classifyAuth401Action prompts inline reauth on workspace routes and never retries /session/extend", () => {
  assert.equal(
    classifyAuth401Action({
      status: 401,
      url: "/api/sale-invoices",
      token: "tok-123",
      pathname: "/sale-invoices/create/retail",
    }),
    "prompt_reauth"
  );

  // Unauthenticated or already on login route -> force_logout
  assert.equal(
    classifyAuth401Action({
      status: 401,
      url: "/api/sale-invoices",
      token: "",
      pathname: "/dashboard",
    }),
    "force_logout"
  );
  assert.equal(
    classifyAuth401Action({
      status: 401,
      url: "/api/sale-invoices",
      token: "tok-123",
      pathname: "/",
    }),
    "force_logout"
  );

  // Login or session/extend endpoints are ignored by global interceptor
  assert.equal(
    classifyAuth401Action({
      status: 401,
      url: "/api/login",
      token: "tok-123",
      pathname: "/dashboard",
    }),
    "ignore"
  );
  assert.equal(
    classifyAuth401Action({
      status: 401,
      url: "/api/session/extend",
      token: "tok-123",
      pathname: "/dashboard",
    }),
    "ignore"
  );

  // Verify axiosSetup.js no longer calls /api/session/extend in response error interceptor
  const axiosSetupSrc = fs.readFileSync(
    path.resolve("resources/js/context/axiosSetup.js"),
    "utf8"
  );
  assert.doesNotMatch(axiosSetupSrc, /inflightSessionExtend/);
  assert.doesNotMatch(axiosSetupSrc, /axios\s*\.\s*post\(\s*["']\/api\/session\/extend["']/);
});

test("ISS-03: resolveSessionModalMode switches to reauth on unauthorized event, expired clock, or extend 401/403", () => {
  assert.equal(isAuthInvalidStatus(401), true);
  assert.equal(isAuthInvalidStatus(403), true);
  assert.equal(isAuthInvalidStatus(500), false);

  assert.equal(
    resolveSessionModalMode({ reason: "unauthorized", nowMs: 1000, expiryMs: 999999 }),
    "reauth"
  );
  assert.equal(
    resolveSessionModalMode({ reason: "expiry_check", nowMs: 2000, expiryMs: 2000 }),
    "reauth"
  );
  assert.equal(
    resolveSessionModalMode({ reason: "expiry_check", nowMs: 1995, expiryMs: 2000, extendStatus: 401 }),
    "reauth"
  );
  assert.equal(
    resolveSessionModalMode({ reason: "expiry_check", nowMs: 1995, expiryMs: 2000 }),
    "warning"
  );
});

test("ISS-19: shouldClearAuthOnUserFetchError clears auth ONLY on 401 or non-terminal 403", () => {
  assert.equal(shouldClearAuthOnUserFetchError({ response: { status: 401 } }), true);
  assert.equal(
    shouldClearAuthOnUserFetchError({ response: { status: 403, data: { message: "Deactivated" } } }),
    true
  );

  // Must preserve session on terminal pending/blocked, 5xx, 429, 408, network errors, timeouts, cancellations
  assert.equal(
    shouldClearAuthOnUserFetchError({ response: { status: 403, data: { code: "TERMINAL_UNAUTHORIZED" } } }),
    false
  );
  assert.equal(shouldClearAuthOnUserFetchError({ response: { status: 500 } }), false);
  assert.equal(shouldClearAuthOnUserFetchError({ response: { status: 503 } }), false);
  assert.equal(shouldClearAuthOnUserFetchError({ response: { status: 429 } }), false);
  assert.equal(shouldClearAuthOnUserFetchError({ response: { status: 408 } }), false);
  assert.equal(shouldClearAuthOnUserFetchError({ code: "ERR_NETWORK" }), false);
  assert.equal(shouldClearAuthOnUserFetchError({ code: "ECONNABORTED" }), false);
  assert.equal(shouldClearAuthOnUserFetchError({ code: "ERR_CANCELED" }), false);
});

test("ISS-16: desktop mode never writes karobar_pairing_token, scrubs stale entries, and omits JS X-Network-Token while browser mode preserves it", () => {
  const desktopStorage = createMemoryStorage({ karobar_pairing_token: "stale-secret" });
  syncDesktopNetworkStorage({
    isDesktop: true,
    cfg: {
      terminalId: "TERM-1",
      terminalName: "Main Counter",
      mode: "terminal",
      hostname: "POS-PC",
      pairingToken: "super-secret-token",
    },
    storage: desktopStorage,
  });

  assert.equal(desktopStorage.getItem("karobar_pairing_token"), null);
  assert.equal(desktopStorage.getItem("karobar_terminal_uid"), "TERM-1");
  assert.equal(desktopStorage.getItem("karobar_terminal_name"), "Main Counter");
  assert.equal(desktopStorage.getItem("karobar_terminal_mode"), "terminal");
  assert.equal(desktopStorage.getItem("karobar_terminal_hostname"), "POS-PC");

  const staleDesktopStorage = createMemoryStorage({ karobar_pairing_token: "leaked-token" });
  assert.equal(
    getBrowserPairingTokenHeader({ isDesktop: true, storage: staleDesktopStorage }),
    ""
  );
  assert.equal(staleDesktopStorage.getItem("karobar_pairing_token"), null);

  // Browser LAN mode preserves reading karobar_pairing_token
  const browserStorage = createMemoryStorage({ karobar_pairing_token: "  lan-token-123  " });
  assert.equal(
    getBrowserPairingTokenHeader({ isDesktop: false, storage: browserStorage }),
    "lan-token-123"
  );
  assert.equal(browserStorage.getItem("karobar_pairing_token"), "  lan-token-123  ");
});
