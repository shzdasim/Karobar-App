import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import {
  OPEN_IN_NEW_WINDOW_FEATURES,
  clearTrackedBlobUrl,
  executeOperatorPinConfirm,
  getDefaultProductModalPos,
  getDefaultProductModalSize,
  isOperatorPinValidationError,
  openUrlInNewTabSafely,
  parseStoredProductModalPos,
  parseStoredProductModalSize,
  persistDesktopTerminalPreferences,
  replaceTrackedBlobUrl,
  resolveResetPasswordParams,
} from "../../resources/js/utils/uiResilienceHelpers.js";

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

test("ISS-18: resolveResetPasswordParams supports route params, ?email= query fallback, and missing token/email errors", () => {
  assert.deepEqual(
    resolveResetPasswordParams({
      paramToken: "tok-abc",
      paramEmail: "user@example.com",
      search: "",
    }),
    {
      token: "tok-abc",
      email: "user@example.com",
      isValid: true,
      errorMessage: "",
    }
  );

  // Query fallback when :email route param is absent
  assert.deepEqual(
    resolveResetPasswordParams({
      paramToken: "tok-abc",
      paramEmail: undefined,
      search: "?email=cashier%40karobar.pk",
    }),
    {
      token: "tok-abc",
      email: "cashier@karobar.pk",
      isValid: true,
      errorMessage: "",
    }
  );

  // Query fallback for token too
  assert.deepEqual(
    resolveResetPasswordParams({
      paramToken: "  ",
      paramEmail: undefined,
      search: "token=q-tok&email=admin%40karobar.pk",
    }),
    {
      token: "q-tok",
      email: "admin@karobar.pk",
      isValid: true,
      errorMessage: "",
    }
  );

  // Missing email still resolves without stalling and produces actionable inline error
  const missingEmail = resolveResetPasswordParams({
    paramToken: "tok-abc",
    paramEmail: undefined,
    search: "",
  });
  assert.equal(missingEmail.isValid, false);
  assert.equal(missingEmail.token, "tok-abc");
  assert.equal(missingEmail.email, "");
  assert.match(missingEmail.errorMessage, /email/i);

  // Missing token produces clear token error
  const missingToken = resolveResetPasswordParams({
    paramToken: "",
    paramEmail: "user@example.com",
    search: "",
  });
  assert.equal(missingToken.isValid, false);
  assert.match(missingToken.errorMessage, /token/i);

  // Static verification on ResetPassword.jsx
  const resetSrc = fs.readFileSync(path.resolve("resources/js/pages/ResetPassword.jsx"), "utf8");
  assert.match(resetSrc, /useSearchParams/);
  assert.match(resetSrc, /resolveResetPasswordParams/);
  assert.match(resetSrc, /setIsValidating\(false\)/);
});

test("ISS-20: replaceTrackedBlobUrl and clearTrackedBlobUrl prevent blob URL leaks on replace, close, race, and unmount", () => {
  const revoked = [];
  const revokeFn = (url) => revoked.push(url);
  const ref = { current: null };

  // First assignment does not revoke anything
  assert.equal(replaceTrackedBlobUrl(ref, "blob:first", revokeFn), "blob:first");
  assert.equal(ref.current, "blob:first");
  assert.deepEqual(revoked, []);

  // Replacing with a new URL revokes previous URL
  assert.equal(replaceTrackedBlobUrl(ref, "blob:second", revokeFn), "blob:second");
  assert.equal(ref.current, "blob:second");
  assert.deepEqual(revoked, ["blob:first"]);

  // Replacing with the same URL is a no-op for revocation
  assert.equal(replaceTrackedBlobUrl(ref, "blob:second", revokeFn), "blob:second");
  assert.deepEqual(revoked, ["blob:first"]);

  // Clearing revokes active URL and resets ref; second clear is idempotent
  assert.equal(clearTrackedBlobUrl(ref, revokeFn), "blob:second");
  assert.equal(ref.current, null);
  assert.deepEqual(revoked, ["blob:first", "blob:second"]);
  assert.equal(clearTrackedBlobUrl(ref, revokeFn), null);
  assert.deepEqual(revoked, ["blob:first", "blob:second"]);

  // Throwing revokeFn never crashes cleanup
  ref.current = "blob:third";
  assert.doesNotThrow(() =>
    clearTrackedBlobUrl(ref, () => {
      throw new Error("revoke failed");
    })
  );
  assert.equal(ref.current, null);

  // Static verification on ShareDocumentModal.jsx
  const modalSrc = fs.readFileSync(
    path.resolve("resources/js/components/ShareDocumentModal.jsx"),
    "utf8"
  );
  assert.match(modalSrc, /pdfBlobUrlRef/);
  assert.match(modalSrc, /loadSeqRef/);
  assert.match(modalSrc, /replaceTrackedBlobUrl/);
  assert.match(modalSrc, /clearTrackedBlobUrl/);
});

test("ISS-21: executeOperatorPinConfirm keeps modal open on 422 PIN error and closes/clears/rethrows on non-PIN error", async () => {
  const pinErr = { response: { status: 422, data: { requires_operator_pin: true, message: "Wrong PIN" } } };
  const nonPin422 = { response: { status: 422, data: { message: "Validation failed" } } };
  const serverErr = { response: { status: 500, data: { message: "Server error" } } };

  assert.equal(isOperatorPinValidationError(pinErr), true);
  assert.equal(isOperatorPinValidationError(nonPin422), false);
  assert.equal(isOperatorPinValidationError(serverErr), false);
  assert.equal(isOperatorPinValidationError(new Error("network")), false);

  // Success path
  {
    let state = { open: true, err: "old", submitting: true };
    const result = await executeOperatorPinConfirm({
      action: async (pin) => `saved:${pin}`,
      pin: "1234",
      onSuccess: () => {
        state.open = false;
        state.err = "";
      },
      onPinError: (msg) => {
        state.err = msg;
      },
      onNonPinError: () => {
        state.open = false;
      },
      onFinally: () => {
        state.submitting = false;
      },
    });
    assert.equal(result, "saved:1234");
    assert.deepEqual(state, { open: false, err: "", submitting: false });
  }

  // PIN error path: modal stays open, error set, does not reject
  {
    let state = { open: true, err: "", submitting: true };
    await executeOperatorPinConfirm({
      action: async () => {
        throw pinErr;
      },
      pin: "0000",
      onSuccess: () => {
        state.open = false;
      },
      onPinError: (msg) => {
        state.err = msg;
      },
      onNonPinError: () => {
        state.open = false;
      },
      onFinally: () => {
        state.submitting = false;
      },
    });
    assert.deepEqual(state, { open: true, err: "Wrong PIN", submitting: false });
  }

  // Non-PIN error path: closes modal, clears error, resets submitting, AND rethrows exact error
  {
    let state = { open: true, err: "stale", submitting: true, actionCleared: false };
    await assert.rejects(
      () =>
        executeOperatorPinConfirm({
          action: async () => {
            throw serverErr;
          },
          pin: "1234",
          onSuccess: () => {},
          onPinError: () => {
            assert.fail("onPinError must not run for 500 error");
          },
          onNonPinError: () => {
            state.open = false;
            state.err = "";
            state.actionCleared = true;
          },
          onFinally: () => {
            state.submitting = false;
          },
        }),
      (err) => err === serverErr
    );
    assert.deepEqual(state, { open: false, err: "", submitting: false, actionCleared: true });
  }

  // Static verification on OperatorPinModal.jsx & useOperatorPinPrompt.js
  const hookSrc = fs.readFileSync(
    path.resolve("resources/js/hooks/useOperatorPinPrompt.js"),
    "utf8"
  );
  const modalSrc = fs.readFileSync(
    path.resolve("resources/js/components/OperatorPinModal.jsx"),
    "utf8"
  );
  assert.match(hookSrc, /executeOperatorPinConfirm/);
  assert.match(modalSrc, /triggerConfirm/);
  assert.match(modalSrc, /Promise\.resolve\(onConfirm\?\.\(code\)\)\.catch/);
  assert.doesNotMatch(modalSrc, /\bonConfirm\s*\(/);
});

test("ISS-22: parseStoredProductModalPos & parseStoredProductModalSize recover safely from malformed JSON and invalid shapes", () => {
  const desktop = { viewportWidth: 1440, viewportHeight: 900 };
  const compact = { viewportWidth: 800, viewportHeight: 600 };

  const defaultDesktopSize = getDefaultProductModalSize(desktop);
  const defaultDesktopPos = getDefaultProductModalPos(desktop);
  assert.deepEqual(defaultDesktopSize, { width: 800, height: 600 });
  assert.deepEqual(defaultDesktopPos, { x: 320, y: 150 });

  // Valid values
  assert.deepEqual(parseStoredProductModalPos('{"x":120,"y":80}', desktop), { x: 120, y: 80 });
  assert.deepEqual(parseStoredProductModalSize('{"width":920,"height":680}', desktop), {
    width: 920,
    height: 680,
  });

  // Compact viewport clamping
  assert.deepEqual(parseStoredProductModalPos('{"x":-50,"y":2}', compact), { x: 8, y: 8 });
  assert.deepEqual(parseStoredProductModalSize('{"width":900,"height":700}', compact), {
    width: 784,
    height: 576,
  });

  // Malformed JSON, wrong primitive/array types, missing fields, non-finite & non-positive values
  const badPayloads = [
    null,
    undefined,
    "",
    "{bad json",
    "undefined",
    "null",
    "[]",
    "123",
    '"string"',
    "true",
    "{}",
    '{"x":10}',
    '{"y":10}',
    '{"x":null,"y":20}',
    '{"x":"NaN","y":20}',
    '{"x":true,"y":20}',
    '{"width":800}',
    '{"width":0,"height":600}',
    '{"width":-100,"height":600}',
    '{"width":null,"height":600}',
    '{"width":"abc","height":600}',
  ];

  for (const payload of badPayloads) {
    assert.deepEqual(
      parseStoredProductModalPos(payload, desktop),
      defaultDesktopPos,
      `Pos fallback failed for ${String(payload)}`
    );
    assert.deepEqual(
      parseStoredProductModalSize(payload, desktop),
      defaultDesktopSize,
      `Size fallback failed for ${String(payload)}`
    );
  }

  // Static verification on ProductFormModal.jsx
  const productModalSrc = fs.readFileSync(
    path.resolve("resources/js/components/ProductFormModal.jsx"),
    "utf8"
  );
  assert.match(productModalSrc, /parseStoredProductModalPos/);
  assert.match(productModalSrc, /parseStoredProductModalSize/);
  assert.doesNotMatch(productModalSrc, /JSON\.parse\(saved\)/);
});

test("ISS-15 (part 1): openUrlInNewTabSafely passes noopener,noreferrer and clears opener", () => {
  assert.equal(OPEN_IN_NEW_WINDOW_FEATURES, "noopener,noreferrer");

  const calls = [];
  const fakeChild = { opener: { secret: true } };
  const fakeWin = {
    open(url, target, features) {
      calls.push({ url, target, features });
      return fakeChild;
    },
  };

  const ret = openUrlInNewTabSafely(fakeWin, "/sales");
  assert.equal(ret, fakeChild);
  assert.equal(fakeChild.opener, null);
  assert.deepEqual(calls, [
    { url: "/sales", target: "_blank", features: "noopener,noreferrer" },
  ]);

  // Empty path defaults to /dashboard and null return from blocked popup is handled cleanly
  const blockedWin = {
    open(url, target, features) {
      calls.push({ url, target, features });
      return null;
    },
  };
  assert.equal(openUrlInNewTabSafely(blockedWin, ""), null);
  assert.deepEqual(calls[1], {
    url: "/dashboard",
    target: "_blank",
    features: "noopener,noreferrer",
  });

  // Static verification on DashboardLayout.jsx
  const layoutSrc = fs.readFileSync(
    path.resolve("resources/js/layouts/DashboardLayout.jsx"),
    "utf8"
  );
  assert.match(layoutSrc, /openUrlInNewTabSafely/);
  assert.doesNotMatch(layoutSrc, /window\.open\s*\(\s*targetPath\s*,\s*["']_blank["']\s*\)/);
});

test("ISS-16 (Bonus parity): persistDesktopTerminalPreferences scrubs karobar_pairing_token and never writes it", () => {
  const storage = createMemoryStorage({ karobar_pairing_token: "stale-pairing-secret" });
  persistDesktopTerminalPreferences({
    storage,
    terminalName: "Counter-2",
    mode: "terminal",
  });

  assert.equal(storage.getItem("karobar_terminal_name"), "Counter-2");
  assert.equal(storage.getItem("karobar_terminal_mode"), "terminal");
  assert.equal(storage.getItem("karobar_pairing_token"), null);

  // Storage throwing errors never propagates
  assert.doesNotThrow(() =>
    persistDesktopTerminalPreferences({
      storage: {
        setItem() {
          throw new Error("QuotaExceededError");
        },
        removeItem() {
          throw new Error("SecurityError");
        },
      },
      terminalName: "Counter-2",
      mode: "terminal",
    })
  );

  // Static verification: zero localStorage.setItem("karobar_pairing_token", ...) anywhere in resources/js/
  const walk = (dir) => {
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    let files = [];
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        files = files.concat(walk(full));
      } else if (/\.(js|jsx|mjs)$/.test(entry.name)) {
        files.push(full);
      }
    }
    return files;
  };
  for (const file of walk(path.resolve("resources/js"))) {
    const content = fs.readFileSync(file, "utf8");
    assert.doesNotMatch(
      content,
      /localStorage\.setItem\(\s*["']karobar_pairing_token["']/,
      `Unexpected localStorage.setItem("karobar_pairing_token") in ${file}`
    );
  }
});

test("Bonus.1: persistDesktopTerminalPreferences scrubs karobar_pairing_token even when setItem throws QuotaExceededError", () => {
  const storage = createMemoryStorage({ karobar_pairing_token: "secret-token-123" });
  const quotaStorage = {
    getItem(k) {
      return storage.getItem(k);
    },
    setItem() {
      const err = new Error("QuotaExceededError");
      err.name = "QuotaExceededError";
      throw err;
    },
    removeItem(k) {
      storage.removeItem(k);
    },
  };

  assert.doesNotThrow(() =>
    persistDesktopTerminalPreferences({
      storage: quotaStorage,
      terminalName: "Counter-2",
      mode: "terminal",
    })
  );

  assert.equal(storage.getItem("karobar_pairing_token"), null);
});

test("ISS-21.3 & ISS-21.4: executeOperatorPinConfirm runs onFinally when action is not a function and OperatorPinModal triggerConfirm guards sync throws", async () => {
  let onSuccessCalled = false;
  let onFinallyCalled = false;
  const result = await executeOperatorPinConfirm({
    action: null,
    pin: "1234",
    onSuccess: (val) => {
      onSuccessCalled = true;
      assert.equal(val, undefined);
    },
    onFinally: () => {
      onFinallyCalled = true;
    },
  });

  assert.equal(result, undefined);
  assert.equal(onSuccessCalled, true);
  assert.equal(onFinallyCalled, true);

  const modalSrc = fs.readFileSync(
    path.resolve("resources/js/components/OperatorPinModal.jsx"),
    "utf8"
  );
  assert.match(
    modalSrc,
    /triggerConfirm\s*=\s*useCallback[\s\S]*try\s*\{\s*Promise\.resolve\(onConfirm\?\.\(code\)\)\.catch/
  );
});

test("ISS-18.6: resolveResetPasswordParams(null) safely returns invalid state and ResetPassword clears email on route change", () => {
  const res = resolveResetPasswordParams(null);
  assert.equal(res.token, "");
  assert.equal(res.email, "");
  assert.equal(res.isValid, false);
  assert.match(res.errorMessage, /token/i);

  const resetSrc = fs.readFileSync(
    path.resolve("resources/js/pages/ResetPassword.jsx"),
    "utf8"
  );
  assert.match(resetSrc, /useEffect\(\s*\(\)\s*=>\s*\{\s*setEmail\(resolvedEmail\);/);
});
