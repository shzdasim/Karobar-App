import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import {
  OPERATOR_PIN_FORMAT_ERROR,
  buildEmployeePinPayload,
  buildUserPinPayload,
  extractApiErrorMessage,
  getDirectoryAssignedCount,
  hasStaffPin,
  isValidFourDigitPin,
  normalizePinDigits,
  validateOptionalOperatorPin,
} from "../../resources/js/utils/operatorPinHelpers.js";

test("M5d: hasStaffPin prefers boolean has_pin and falls back cleanly to legacy/pre-M4 pin_code", () => {
  assert.equal(hasStaffPin({ has_pin: true }), true);
  assert.equal(hasStaffPin({ has_pin: false }), false);
  // Boolean has_pin takes precedence over any legacy pin_code property
  assert.equal(hasStaffPin({ has_pin: false, pin_code: "1234" }), false);
  assert.equal(hasStaffPin({ has_pin: true, pin_code: "" }), true);

  // Pre-M4 HrPayrollController fallback (hashed or raw non-empty string)
  assert.equal(hasStaffPin({ pin_code: "$pbkdf2-sha256$10000$salt$hash" }), true);
  assert.equal(hasStaffPin({ pin_code: "4821" }), true);

  // Blank / null / undefined cases
  assert.equal(hasStaffPin({ pin_code: "" }), false);
  assert.equal(hasStaffPin({ pin_code: "   " }), false);
  assert.equal(hasStaffPin({ pin_code: null }), false);
  assert.equal(hasStaffPin({}), false);
  assert.equal(hasStaffPin(null), false);
  assert.equal(hasStaffPin(undefined), false);
});

test("M5d: normalizePinDigits, isValidFourDigitPin, and validateOptionalOperatorPin handle digit limits and optional validation", () => {
  assert.equal(normalizePinDigits("a1b2c3d4e5"), "1234");
  assert.equal(normalizePinDigits("0042"), "0042");
  assert.equal(normalizePinDigits(null), "");
  assert.equal(normalizePinDigits(undefined), "");

  assert.equal(isValidFourDigitPin("0000"), true);
  assert.equal(isValidFourDigitPin("4821"), true);
  assert.equal(isValidFourDigitPin(" 4821 "), true);
  assert.equal(isValidFourDigitPin("123"), false);
  assert.equal(isValidFourDigitPin("12345"), false);
  assert.equal(isValidFourDigitPin("abcd"), false);
  assert.equal(isValidFourDigitPin(""), false);
  assert.equal(isValidFourDigitPin(null), false);

  assert.equal(validateOptionalOperatorPin(""), "");
  assert.equal(validateOptionalOperatorPin("   "), "");
  assert.equal(validateOptionalOperatorPin(null), "");
  assert.equal(validateOptionalOperatorPin("0042"), "");
  assert.equal(validateOptionalOperatorPin("12"), OPERATOR_PIN_FORMAT_ERROR);
  assert.equal(validateOptionalOperatorPin("12345"), OPERATOR_PIN_FORMAT_ERROR);
  assert.equal(validateOptionalOperatorPin("abcd"), OPERATOR_PIN_FORMAT_ERROR);
});

test("M5d: buildUserPinPayload preserves existing PIN on blank edit and supports explicit clear_pin", () => {
  // Create mode
  assert.deepEqual(buildUserPinPayload({ isEdit: false, pinInput: "4821" }), {
    pin_code: "4821",
  });
  assert.deepEqual(buildUserPinPayload({ isEdit: false, pinInput: "" }), {
    pin_code: null,
  });
  assert.deepEqual(buildUserPinPayload({ isEdit: false, pinInput: "   " }), {
    pin_code: null,
  });

  // Edit mode: blank without clearPin omits pin_code and clear_pin so backend preserves existing PIN
  assert.deepEqual(
    buildUserPinPayload({ isEdit: true, pinInput: "", clearPin: false }),
    {}
  );
  assert.deepEqual(
    buildUserPinPayload({ isEdit: true, pinInput: "  ", clearPin: false }),
    {}
  );

  // Edit mode: new PIN never sends clear_pin
  assert.deepEqual(
    buildUserPinPayload({ isEdit: true, pinInput: "9876", clearPin: false }),
    { pin_code: "9876" }
  );

  // Edit mode: explicit clearPin sends clear_pin: true, pin_code: null
  assert.deepEqual(
    buildUserPinPayload({ isEdit: true, pinInput: "", clearPin: true }),
    { clear_pin: true, pin_code: null }
  );
  assert.deepEqual(
    buildUserPinPayload({ isEdit: true, pinInput: "9876", clearPin: true }),
    { clear_pin: true, pin_code: null }
  );
});

test("M5d: buildEmployeePinPayload works safely with both pre-M4 and post-M4 HrPayrollController", () => {
  // Create mode: empty omits pin_code so StoreEmployee inherits linked user's PIN
  assert.deepEqual(buildEmployeePinPayload({ isEdit: false, pinInput: "" }), {});
  assert.deepEqual(buildEmployeePinPayload({ isEdit: false, pinInput: "4821" }), {
    pin_code: "4821",
  });

  // Edit mode: blank without clearPin omits pin_code (prevents silent PIN wipe via body.ContainsKey("pin_code"))
  assert.deepEqual(
    buildEmployeePinPayload({ isEdit: true, pinInput: "", clearPin: false }),
    {}
  );

  // Edit mode: new PIN sets pin_code
  assert.deepEqual(
    buildEmployeePinPayload({ isEdit: true, pinInput: "4821", clearPin: false }),
    { pin_code: "4821" }
  );

  // Edit mode: explicit clearPin sends both clear_pin: true (post-M4) and pin_code: null (pre-M4)
  assert.deepEqual(
    buildEmployeePinPayload({ isEdit: true, pinInput: "", clearPin: true }),
    { clear_pin: true, pin_code: null }
  );
});

test("M5d: getDirectoryAssignedCount counts active users and active unlinked employees via hasStaffPin", () => {
  const users = [
    { id: 1, status: "active", has_pin: true },
    { id: 2, status: "active", has_pin: false },
    { id: 3, status: "inactive", has_pin: true },
  ];
  const employees = [
    // Linked to user 1 -> must NOT double count
    { id: 10, user_id: 1, status: "active", has_pin: true },
    // Unlinked active employee with PIN -> counted
    { id: 11, user_id: null, status: "active", has_pin: true },
    // Unlinked active employee without PIN -> not counted
    { id: 12, user_id: null, status: "active", has_pin: false },
    // Unlinked inactive employee with PIN -> not counted
    { id: 13, user_id: null, status: "inactive", has_pin: true },
    // Legacy pin_code fallback when has_pin is absent
    { id: 14, user_id: null, status: "active", pin_code: "$pbkdf2-sha256$10000$x$y" },
  ];

  assert.equal(getDirectoryAssignedCount(users, employees), 3);
  assert.equal(getDirectoryAssignedCount([], []), 0);
  assert.equal(getDirectoryAssignedCount(null, undefined), 0);
});

test("M5d: extractApiErrorMessage prioritizes errors.pin_code, then first validation error, then message, then fallback", () => {
  assert.equal(
    extractApiErrorMessage(
      {
        response: {
          status: 422,
          data: {
            message: "Validation failed.",
            errors: {
              email: ["Email taken."],
              pin_code: ["Operator code is already in use."],
            },
          },
        },
      },
      "Fallback"
    ),
    "Operator code is already in use."
  );

  assert.equal(
    extractApiErrorMessage(
      {
        response: {
          status: 422,
          data: {
            message: "The given data was invalid.",
            errors: {
              employee_code: ["The employee code has already been taken."],
            },
          },
        },
      },
      "Fallback"
    ),
    "The employee code has already been taken."
  );

  assert.equal(
    extractApiErrorMessage(
      {
        response: {
          status: 403,
          data: { message: "Only an Administrator can assign or clear an Admin user's PIN." },
        },
      },
      "Fallback"
    ),
    "Only an Administrator can assign or clear an Admin user's PIN."
  );

  assert.equal(extractApiErrorMessage(new Error("Network Error"), "Save failed."), "Save failed.");
});

test("M5d static regression checks: PIN callers use operatorPinHelpers and never pre-fill .pin_code; PharmacyClinicalModals uses rel=noopener noreferrer", () => {
  const operatorPinSettingSrc = fs.readFileSync(
    path.resolve("resources/js/components/settings/OperatorPinSetting.jsx"),
    "utf8"
  );
  assert.match(operatorPinSettingSrc, /operatorPinHelpers\.js/);
  assert.match(operatorPinSettingSrc, /hasStaffPin/);
  assert.match(operatorPinSettingSrc, /getDirectoryAssignedCount/);
  assert.match(operatorPinSettingSrc, /isValidFourDigitPin/);
  assert.match(operatorPinSettingSrc, /handleClearPin/);
  assert.doesNotMatch(operatorPinSettingSrc, /u\.pin_code/);
  assert.doesNotMatch(operatorPinSettingSrc, /e\.pin_code/);
  assert.doesNotMatch(operatorPinSettingSrc, /item\.pin_code/);

  const userFormSrc = fs.readFileSync(
    path.resolve("resources/js/pages/users/UserForm.jsx"),
    "utf8"
  );
  assert.match(userFormSrc, /operatorPinHelpers\.js/);
  assert.match(userFormSrc, /hasStaffPin/);
  assert.match(userFormSrc, /buildUserPinPayload/);
  assert.match(userFormSrc, /validateOptionalOperatorPin/);
  assert.match(userFormSrc, /Clear PIN/);
  assert.doesNotMatch(userFormSrc, /initial\.pin_code/);

  const usersIndexSrc = fs.readFileSync(
    path.resolve("resources/js/pages/users/index.jsx"),
    "utf8"
  );
  assert.match(usersIndexSrc, /operatorPinHelpers\.js/);
  assert.match(usersIndexSrc, /hasStaffPin\(u\)/);
  assert.match(usersIndexSrc, /PIN Set/);
  assert.match(usersIndexSrc, /No PIN/);

  const createUserSrc = fs.readFileSync(
    path.resolve("resources/js/pages/users/Create.jsx"),
    "utf8"
  );
  assert.match(createUserSrc, /extractApiErrorMessage/);
  assert.match(createUserSrc, /toast\.error/);

  const editUserSrc = fs.readFileSync(
    path.resolve("resources/js/pages/users/Edit.jsx"),
    "utf8"
  );
  assert.match(editUserSrc, /extractApiErrorMessage/);
  assert.match(editUserSrc, /toast\.error/);

  const hrPayrollSrc = fs.readFileSync(
    path.resolve("resources/js/pages/HrPayroll/HrPayrollCenter.jsx"),
    "utf8"
  );
  assert.match(hrPayrollSrc, /operatorPinHelpers\.js/);
  assert.match(hrPayrollSrc, /hasStaffPin\(editingEmployee\)/);
  assert.match(hrPayrollSrc, /buildEmployeePinPayload/);
  assert.match(hrPayrollSrc, /validateOptionalOperatorPin/);
  assert.doesNotMatch(hrPayrollSrc, /emp\.pin_code/);

  const pharmacyModalsSrc = fs.readFileSync(
    path.resolve("resources/js/components/PharmacyClinicalModals.jsx"),
    "utf8"
  );
  assert.match(pharmacyModalsSrc, /target="_blank"[\s\S]{0,80}rel="noopener noreferrer"/);
  assert.doesNotMatch(pharmacyModalsSrc, /rel="noreferrer"/);
});
