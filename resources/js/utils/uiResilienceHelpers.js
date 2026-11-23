// Pure-ESM UI resilience helpers (zero top-level React/JSX/DOM globals)
// Used by M5b components/hooks and directly unit-tested under node:test.

export const OPEN_IN_NEW_WINDOW_FEATURES = "noopener,noreferrer";

/**
 * Resolve reset-password token & email from route params with query-string fallback.
 * Ensures loading state always resolves and returns clear inline messages when missing.
 */
export function resolveResetPasswordParams(options = {}) {
  const { paramToken, paramEmail, search = "" } = options || {};
  const normalizedSearch = typeof search === "string"
    ? (search.startsWith("?") ? search.slice(1) : search)
    : "";
  const query = new URLSearchParams(normalizedSearch);

  const rawToken =
    typeof paramToken === "string" && paramToken.trim()
      ? paramToken
      : query.get("token");
  const rawEmail =
    typeof paramEmail === "string" && paramEmail.trim()
      ? paramEmail
      : query.get("email");

  const token = String(rawToken ?? "").trim();
  const email = String(rawEmail ?? "").trim();

  let errorMessage = "";
  if (!token) {
    errorMessage = "Invalid or missing password reset token. Please request a new reset link.";
  } else if (!email) {
    errorMessage =
      "Reset link is missing the email address. Please enter your email address below or request a new reset link.";
  }

  return {
    token,
    email,
    isValid: Boolean(token && email),
    errorMessage,
  };
}

/**
 * Replace an active tracked blob URL ref, revoking the prior URL if distinct.
 */
export function replaceTrackedBlobUrl(ref, nextUrl, revokeFn) {
  const normalizedNext = nextUrl || null;
  if (!ref) return normalizedNext;
  const prevUrl = ref.current || null;
  if (prevUrl && prevUrl !== normalizedNext && typeof revokeFn === "function") {
    try {
      revokeFn(prevUrl);
    } catch {
      // Ignore revoke errors
    }
  }
  ref.current = normalizedNext;
  return normalizedNext;
}

/**
 * Clear and revoke an active tracked blob URL ref.
 */
export function clearTrackedBlobUrl(ref, revokeFn) {
  if (!ref) return null;
  const prevUrl = ref.current || null;
  ref.current = null;
  if (prevUrl && typeof revokeFn === "function") {
    try {
      revokeFn(prevUrl);
    } catch {
      // Ignore revoke errors
    }
  }
  return prevUrl;
}

/**
 * Identify server 422 responses specifically requesting or rejecting an operator PIN.
 */
export function isOperatorPinValidationError(err) {
  return Boolean(
    err?.response?.status === 422 && err?.response?.data?.requires_operator_pin
  );
}

/**
 * Execute an operator-PIN-protected action:
 * - On success: calls `onSuccess(result)`.
 * - On PIN validation 422: keeps modal open via `onPinError(message, err)` and resolves.
 * - On any non-PIN error: calls `onNonPinError(err)` (closes modal/clears state) and rethrows `err`.
 * - Always calls `onFinally()` in `finally`.
 */
export async function executeOperatorPinConfirm(options = {}) {
  const {
    action,
    pin,
    onSuccess,
    onPinError,
    onNonPinError,
    onFinally,
  } = options || {};

  try {
    if (typeof action !== "function") {
      onSuccess?.(undefined);
      return undefined;
    }

    const result = await action(pin);
    onSuccess?.(result);
    return result;
  } catch (err) {
    if (isOperatorPinValidationError(err)) {
      onPinError?.(
        err?.response?.data?.message || "Invalid 4-digit operator code. Please try again.",
        err
      );
      return undefined;
    }
    onNonPinError?.(err);
    throw err;
  } finally {
    onFinally?.();
  }
}

function toFiniteNumber(value) {
  if (typeof value === "number") {
    return Number.isFinite(value) ? value : null;
  }
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) return null;
    const parsed = Number(trimmed);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

function toPositiveFiniteNumber(value) {
  const num = toFiniteNumber(value);
  return num !== null && num > 0 ? num : null;
}

function isPlainObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function normalizeViewport({ viewportWidth, viewportHeight } = {}) {
  const width = toPositiveFiniteNumber(viewportWidth) ?? 1024;
  const height = toPositiveFiniteNumber(viewportHeight) ?? 768;
  return {
    width,
    height,
    isCompact: width < 1024,
  };
}

/**
 * Compute default ProductFormModal dimensions for the given viewport.
 */
export function getDefaultProductModalSize(viewport = {}) {
  const { width: vw, height: vh, isCompact } = normalizeViewport(viewport);
  return {
    width: isCompact ? Math.max(1, Math.min(800, vw - 16)) : 800,
    height: isCompact ? Math.max(1, Math.min(600, vh - 24)) : 600,
  };
}

/**
 * Compute centered default ProductFormModal coordinates for the given viewport.
 */
export function getDefaultProductModalPos(viewport = {}) {
  const { width: vw, height: vh, isCompact } = normalizeViewport(viewport);
  const { width, height } = getDefaultProductModalSize(viewport);
  const x = isCompact ? Math.max(8, (vw - width) / 2) : (vw - width) / 2;
  const y = isCompact ? Math.max(8, (vh - height) / 2) : (vh - height) / 2;
  return { x, y };
}

/**
 * Safely parse stored ProductFormModal position from localStorage, returning
 * centered defaults on malformed JSON, wrong types, or missing/non-finite fields.
 */
export function parseStoredProductModalPos(rawSaved, viewport = {}) {
  const fallback = getDefaultProductModalPos(viewport);
  if (!rawSaved || typeof rawSaved !== "string") {
    return fallback;
  }

  let parsed;
  try {
    parsed = JSON.parse(rawSaved);
  } catch {
    return fallback;
  }

  if (!isPlainObject(parsed)) {
    return fallback;
  }

  const x = toFiniteNumber(parsed.x);
  const y = toFiniteNumber(parsed.y);
  if (x === null || y === null) {
    return fallback;
  }

  const { isCompact } = normalizeViewport(viewport);
  return isCompact ? { x: Math.max(8, x), y: Math.max(8, y) } : { x, y };
}

/**
 * Safely parse stored ProductFormModal dimensions from localStorage, returning
 * defaults on malformed JSON, wrong types, or missing/non-positive fields.
 */
export function parseStoredProductModalSize(rawSaved, viewport = {}) {
  const fallback = getDefaultProductModalSize(viewport);
  if (!rawSaved || typeof rawSaved !== "string") {
    return fallback;
  }

  let parsed;
  try {
    parsed = JSON.parse(rawSaved);
  } catch {
    return fallback;
  }

  if (!isPlainObject(parsed)) {
    return fallback;
  }

  const width = toPositiveFiniteNumber(parsed.width);
  const height = toPositiveFiniteNumber(parsed.height);
  if (width === null || height === null) {
    return fallback;
  }

  const { width: vw, height: vh, isCompact } = normalizeViewport(viewport);
  if (isCompact) {
    return {
      width: Math.max(1, Math.min(800, width, vw - 16)),
      height: Math.max(1, Math.min(600, height, vh - 24)),
    };
  }
  return { width, height };
}

/**
 * Open a URL in a new browser tab/window with reverse-tabnabbing protections.
 */
export function openUrlInNewTabSafely(win, targetPath) {
  if (!win || typeof win.open !== "function") return null;
  const resolvedPath = targetPath || "/dashboard";
  const opened = win.open(resolvedPath, "_blank", OPEN_IN_NEW_WINDOW_FEATURES);
  if (opened) {
    try {
      opened.opener = null;
    } catch {
      // Ignore read-only opener or cross-origin window errors
    }
  }
  return opened || null;
}

/**
 * Persist non-secret desktop terminal UI preferences while unconditionally scrubbing
 * any stale `karobar_pairing_token` from browser localStorage in desktop mode.
 */
export function persistDesktopTerminalPreferences(options = {}) {
  const { storage, terminalName, mode } = options || {};
  if (!storage) return;
  try {
    storage.removeItem?.("karobar_pairing_token");
  } catch {
    // Ignore storage security errors
  }
  try {
    if (typeof storage.setItem === "function") {
      storage.setItem("karobar_terminal_name", terminalName ?? "");
      storage.setItem("karobar_terminal_mode", mode ?? "");
    }
  } catch {
    // Ignore storage quota or security errors
  }
}
