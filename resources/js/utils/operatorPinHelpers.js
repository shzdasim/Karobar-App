// Pure-ESM operator PIN compatibility helpers (zero top-level React/JSX/DOM globals).
// Supports M2 (has_pin boolean + clear_pin) as well as pre-M4 HrPayrollController responses.

export const OPERATOR_PIN_FORMAT_ERROR =
  "Operator code must be exactly 4 numeric digits (0000-9999).";

/**
 * Determine whether a user or employee record currently has a PIN set.
 * Prefers boolean `has_pin` (M2+ endpoints) and falls back to non-empty `pin_code`
 * for legacy/pre-M4 responses.
 */
export function hasStaffPin(record) {
  if (typeof record?.has_pin === "boolean") {
    return record.has_pin;
  }
  return Boolean(
    typeof record?.pin_code === "string"
      ? record.pin_code.trim()
      : record?.pin_code
  );
}

/**
 * Strip non-digit characters and limit to at most 4 numeric digits.
 */
export function normalizePinDigits(value) {
  return String(value ?? "").replace(/\D/g, "").slice(0, 4);
}

/**
 * Return true iff trimmed input is exactly 4 numeric digits (0000-9999).
 */
export function isValidFourDigitPin(pin) {
  return /^\d{4}$/.test(String(pin ?? "").trim());
}

/**
 * Validate an optional operator PIN input.
 * Returns "" when blank or valid 4 digits, otherwise an actionable error message.
 */
export function validateOptionalOperatorPin(pin) {
  const trimmed = String(pin ?? "").trim();
  if (trimmed === "" || isValidFourDigitPin(trimmed)) {
    return "";
  }
  return OPERATOR_PIN_FORMAT_ERROR;
}

/**
 * Build PIN-related payload fields for UserController Store/Update.
 * - Create (!isEdit): `{ pin_code: trimmed || null }`
 * - Edit (isEdit) + clearPin: `{ clear_pin: true, pin_code: null }`
 * - Edit (isEdit) + non-empty pinInput: `{ pin_code: trimmed }`
 * - Edit (isEdit) + blank pinInput + !clearPin: `{}` (preserves existing PIN)
 */
export function buildUserPinPayload({
  isEdit = false,
  pinInput = "",
  clearPin = false,
} = {}) {
  const trimmed = String(pinInput ?? "").trim();
  if (!isEdit) {
    return { pin_code: trimmed || null };
  }
  if (clearPin === true) {
    return { clear_pin: true, pin_code: null };
  }
  if (trimmed !== "") {
    return { pin_code: trimmed };
  }
  return {};
}

/**
 * Build PIN-related payload fields for HrPayrollController StoreEmployee/UpdateEmployee.
 * Compatible with both pre-M4 (`body.ContainsKey("pin_code")`) and post-M4 (`clear_pin`).
 * - Create (!isEdit): non-empty -> `{ pin_code: trimmed }`; blank -> `{}` (inherits linked user PIN)
 * - Edit (isEdit) + clearPin: `{ clear_pin: true, pin_code: null }`
 * - Edit (isEdit) + non-empty pinInput: `{ pin_code: trimmed }`
 * - Edit (isEdit) + blank pinInput + !clearPin: `{}` (omits `pin_code` to preserve existing PIN)
 */
export function buildEmployeePinPayload({
  isEdit = false,
  pinInput = "",
  clearPin = false,
} = {}) {
  const trimmed = String(pinInput ?? "").trim();
  if (!isEdit) {
    return trimmed !== "" ? { pin_code: trimmed } : {};
  }
  if (clearPin === true) {
    return { clear_pin: true, pin_code: null };
  }
  if (trimmed !== "") {
    return { pin_code: trimmed };
  }
  return {};
}

/**
 * Count active staff with assigned operator PINs, deduplicating employees linked to a user.
 */
export function getDirectoryAssignedCount(users = [], employees = []) {
  const safeUsers = Array.isArray(users) ? users : [];
  const safeEmployees = Array.isArray(employees) ? employees : [];

  const activeUsersWithPin = safeUsers.filter(
    (u) => u?.status === "active" && hasStaffPin(u)
  ).length;
  const activeUnlinkedEmployeesWithPin = safeEmployees.filter(
    (e) => !e?.user_id && e?.status === "active" && hasStaffPin(e)
  ).length;

  return activeUsersWithPin + activeUnlinkedEmployeesWithPin;
}

/**
 * Extract the most specific user-facing message from an Axios error response.
 */
export function extractApiErrorMessage(err, fallback = "Request failed.") {
  const data = err?.response?.data;
  const errors = data?.errors;

  if (errors && typeof errors === "object") {
    const pinErr = errors.pin_code;
    if (Array.isArray(pinErr) && typeof pinErr[0] === "string" && pinErr[0].trim()) {
      return pinErr[0];
    }
    if (typeof pinErr === "string" && pinErr.trim()) {
      return pinErr;
    }
    for (const val of Object.values(errors)) {
      if (Array.isArray(val) && typeof val[0] === "string" && val[0].trim()) {
        return val[0];
      }
      if (typeof val === "string" && val.trim()) {
        return val;
      }
    }
  }

  if (typeof data?.message === "string" && data.message.trim()) {
    return data.message;
  }

  return fallback;
}
