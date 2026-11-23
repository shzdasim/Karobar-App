import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import {
  DEFAULT_BLOB_REVOKE_DELAY_MS,
  REPORT_PDF_WINDOW_FEATURES,
  openReportPdfSafely,
  triggerBlobDownload,
} from "../../resources/js/utils/reportPdfOpener.js";

function createMockDoc({ throwOnClick = false, hasBody = true } = {}) {
  const createdAnchors = [];
  const appended = [];
  const removed = [];

  const body = hasBody
    ? {
        appendChild(node) {
          node.parentNode = body;
          appended.push(node);
        },
        removeChild(node) {
          if (node.parentNode === body) {
            node.parentNode = null;
          }
          removed.push(node);
        },
      }
    : null;

  const doc = {
    body,
    createElement(tag) {
      assert.equal(tag, "a");
      const anchor = {
        tagName: "A",
        href: "",
        download: "",
        parentNode: null,
        clicked: 0,
        click() {
          anchor.clicked += 1;
          if (throwOnClick) {
            throw new Error("Simulated click error");
          }
        },
      };
      createdAnchors.push(anchor);
      return anchor;
    },
  };

  return { doc, createdAnchors, appended, removed };
}

function createMockUrlApi({ createThrow = false, revokeThrow = false } = {}) {
  let seq = 0;
  const created = [];
  const revoked = [];
  return {
    created,
    revoked,
    urlApi: {
      createObjectURL(blob) {
        if (createThrow) {
          throw new Error("createObjectURL failed");
        }
        seq += 1;
        const url = `blob:https://karobar.local/pdf-${seq}`;
        created.push({ url, blob });
        return url;
      },
      revokeObjectURL(url) {
        revoked.push(url);
        if (revokeThrow) {
          throw new Error("revokeObjectURL failed");
        }
      },
    },
  };
}

function createMockScheduler() {
  const scheduled = [];
  const scheduleTimeout = (fn, delay) => {
    scheduled.push({ fn, delay });
    return scheduled.length;
  };
  return { scheduled, scheduleTimeout };
}

test("ISS-15 (part 2): exports expected constants", () => {
  assert.equal(REPORT_PDF_WINDOW_FEATURES, "noopener,noreferrer");
  assert.equal(DEFAULT_BLOB_REVOKE_DELAY_MS, 10000);
});

test("ISS-15 (part 2): openReportPdfSafely opens with noopener,noreferrer and does NOT trigger download when win.open returns null (HTML spec)", () => {
  const openCalls = [];
  const mockWin = {
    open(url, target, features) {
      openCalls.push({ url, target, features });
      return null; // HTML Living Standard behavior when noopener is set
    },
  };
  const { doc, createdAnchors, appended } = createMockDoc();
  const { urlApi, created, revoked } = createMockUrlApi();
  const { scheduled, scheduleTimeout } = createMockScheduler();
  const fakeBlob = { size: 128, type: "application/pdf" };

  const result = openReportPdfSafely({
    data: new Uint8Array([1, 2, 3]),
    filename: "sale-detail-2026-11-01.pdf",
    revokeDelayMs: 10000,
    win: mockWin,
    doc,
    urlApi,
    scheduleTimeout,
    createBlob: (parts, opts) => {
      assert.equal(opts.type, "application/pdf");
      assert.equal(parts.length, 1);
      return fakeBlob;
    },
  });

  assert.equal(result.url, "blob:https://karobar.local/pdf-1");
  assert.equal(result.openedInNewTab, true);
  assert.equal(result.downloaded, false);
  assert.deepEqual(openCalls, [
    {
      url: "blob:https://karobar.local/pdf-1",
      target: "_blank",
      features: "noopener,noreferrer",
    },
  ]);
  assert.equal(created.length, 1);
  assert.equal(created[0].blob, fakeBlob);

  // Zero download elements created or clicked (no double-download)
  assert.equal(createdAnchors.length, 0);
  assert.equal(appended.length, 0);

  // Revocation is deferred, not synchronous
  assert.deepEqual(revoked, []);
  assert.equal(scheduled.length, 1);
  assert.equal(scheduled[0].delay, 10000);

  scheduled[0].fn();
  assert.deepEqual(revoked, ["blob:https://karobar.local/pdf-1"]);
});

test("ISS-15 (part 2): openReportPdfSafely clears opener on non-null mock Window and ignores read-only opener setter throws", () => {
  const { doc, createdAnchors } = createMockDoc();
  const { urlApi } = createMockUrlApi();
  const { scheduled, scheduleTimeout } = createMockScheduler();

  // Writable opener
  const childWin = { opener: { secret: "parent" } };
  const res1 = openReportPdfSafely({
    blob: { type: "application/pdf" },
    win: {
      open: () => childWin,
    },
    doc,
    urlApi,
    scheduleTimeout,
  });
  assert.equal(res1.openedInNewTab, true);
  assert.equal(res1.downloaded, false);
  assert.equal(childWin.opener, null);
  assert.equal(createdAnchors.length, 0);

  // Read-only opener getter/setter that throws in strict mode
  const lockedChildWin = {};
  Object.defineProperty(lockedChildWin, "opener", {
    get() {
      return {};
    },
    set() {
      throw new TypeError("Cannot assign to read only property 'opener'");
    },
  });

  const res2 = openReportPdfSafely({
    blob: { type: "application/pdf" },
    win: {
      open: () => lockedChildWin,
    },
    doc,
    urlApi,
    scheduleTimeout,
    revokeDelayMs: 60000,
  });
  assert.equal(res2.openedInNewTab, true);
  assert.equal(res2.downloaded, false);
  assert.equal(scheduled[1].delay, 60000);
});

test("ISS-15 (part 2): openReportPdfSafely falls back once to triggerBlobDownload when win.open is missing or throws", () => {
  // Missing win / win.open
  {
    const { doc, createdAnchors, appended, removed } = createMockDoc();
    const { urlApi, revoked } = createMockUrlApi();
    const { scheduled, scheduleTimeout } = createMockScheduler();

    const result = openReportPdfSafely({
      data: "pdf-bytes",
      filename: "current-stock-report.pdf",
      win: null,
      doc,
      urlApi,
      scheduleTimeout,
    });

    assert.equal(result.openedInNewTab, false);
    assert.equal(result.downloaded, true);
    assert.equal(createdAnchors.length, 1);
    assert.equal(createdAnchors[0].href, result.url);
    assert.equal(createdAnchors[0].download, "current-stock-report.pdf");
    assert.equal(createdAnchors[0].clicked, 1);
    assert.equal(appended.length, 1);
    assert.equal(removed.length, 1);
    assert.equal(createdAnchors[0].parentNode, null);
    assert.deepEqual(revoked, []);
    assert.equal(scheduled.length, 1);
    assert.equal(scheduled[0].delay, 10000);
  }

  // win.open throws synchronously
  {
    const { doc, createdAnchors, appended, removed } = createMockDoc();
    const { urlApi } = createMockUrlApi();
    const { scheduled, scheduleTimeout } = createMockScheduler();

    const result = openReportPdfSafely({
      data: "pdf-bytes",
      filename: "narcotics-register-2026-11-01.pdf",
      revokeDelayMs: 60000,
      win: {
        open() {
          throw new Error("Blocked by host");
        },
      },
      doc,
      urlApi,
      scheduleTimeout,
    });

    assert.equal(result.openedInNewTab, false);
    assert.equal(result.downloaded, true);
    assert.equal(createdAnchors.length, 1);
    assert.equal(createdAnchors[0].download, "narcotics-register-2026-11-01.pdf");
    assert.equal(createdAnchors[0].clicked, 1);
    assert.equal(appended.length, 1);
    assert.equal(removed.length, 1);
    assert.equal(scheduled[0].delay, 60000);
  }
});

test("ISS-15 (part 2): triggerBlobDownload removes anchor in finally even if click() throws and schedules revokeObjectURL", () => {
  const { doc, createdAnchors, appended, removed } = createMockDoc({ throwOnClick: true });
  const { urlApi, revoked } = createMockUrlApi();
  const { scheduled, scheduleTimeout } = createMockScheduler();

  assert.throws(
    () =>
      openReportPdfSafely({
        data: "pdf-bytes",
        filename: "cost-of-sale-2026-11-01.pdf",
        revokeDelayMs: 60000,
        win: {},
        doc,
        urlApi,
        scheduleTimeout,
      }),
    /Simulated click error/
  );

  assert.equal(createdAnchors.length, 1);
  assert.equal(appended.length, 1);
  assert.equal(removed.length, 1);
  assert.equal(createdAnchors[0].parentNode, null);
  assert.equal(scheduled.length, 1);
  assert.equal(scheduled[0].delay, 60000);

  scheduled[0].fn();
  assert.equal(revoked.length, 1);
});

test("ISS-15 (part 2): revokeObjectURL errors are swallowed safely and invalid delays fall back to DEFAULT_BLOB_REVOKE_DELAY_MS", () => {
  const { doc } = createMockDoc();
  const { urlApi, revoked } = createMockUrlApi({ revokeThrow: true });
  const { scheduled, scheduleTimeout } = createMockScheduler();

  const res = openReportPdfSafely({
    url: "blob:https://karobar.local/existing-1",
    revokeDelayMs: -50,
    win: { open: () => null },
    doc,
    urlApi,
    scheduleTimeout,
  });

  assert.equal(res.url, "blob:https://karobar.local/existing-1");
  assert.equal(res.openedInNewTab, true);
  assert.equal(scheduled.length, 1);
  assert.equal(scheduled[0].delay, DEFAULT_BLOB_REVOKE_DELAY_MS);
  assert.doesNotThrow(() => scheduled[0].fn());
  assert.deepEqual(revoked, ["blob:https://karobar.local/existing-1"]);
});

test("ISS-15 (part 2): defensive handling of null/empty options, missing urlApi/doc, and non-function scheduleTimeout", () => {
  // Missing urlApi when no existing url is provided throws clear TypeError
  assert.throws(
    () => openReportPdfSafely({ data: "abc", urlApi: null }),
    TypeError
  );
  assert.throws(() => openReportPdfSafely({ urlApi: null }), TypeError);

  // Null/empty options with a valid urlApi & stubbed scheduler does not throw destructuring TypeError
  const { urlApi } = createMockUrlApi();
  const { scheduled, scheduleTimeout } = createMockScheduler();
  const emptyRes = openReportPdfSafely({
    win: null,
    doc: null,
    urlApi,
    scheduleTimeout,
  });
  assert.equal(emptyRes.openedInNewTab, false);
  assert.equal(emptyRes.downloaded, false);
  assert.equal(scheduled.length, 1);

  // Passing null options directly does not crash on destructuring
  assert.throws(
    () => {
      // Simulate environment without URL.createObjectURL via direct check
      openReportPdfSafely({ urlApi: {}, scheduleTimeout });
    },
    TypeError
  );
  const nullTrigger = triggerBlobDownload(null);
  assert.equal(nullTrigger, false);
  assert.equal(triggerBlobDownload({ url: "", doc: createMockDoc().doc }), false);
  assert.equal(triggerBlobDownload({ url: "blob:1", doc: null }), false);

  // Existing url works even if urlApi, doc, and scheduleTimeout are all null
  const directRes = openReportPdfSafely({
    url: "blob:https://karobar.local/direct",
    win: null,
    doc: null,
    urlApi: null,
    scheduleTimeout: null,
  });
  assert.deepEqual(directRes, {
    url: "blob:https://karobar.local/direct",
    openedInNewTab: false,
    downloaded: false,
  });

  // Null options handled safely when global URL exists in Node (use noop setTimeout override? No, test triggerBlobDownload + openReportPdfSafely(null) via try/finally)
  const origSetTimeout = globalThis.setTimeout;
  const origClearTimeout = globalThis.clearTimeout;
  const timers = [];
  globalThis.setTimeout = (fn, ms) => {
    const id = origSetTimeout(fn, ms);
    id.unref?.();
    timers.push({ id, ms });
    return id;
  };
  try {
    const resNullOpts = openReportPdfSafely(null);
    assert.equal(typeof resNullOpts.url, "string");
    assert.ok(resNullOpts.url.startsWith("blob:"));
    assert.equal(resNullOpts.openedInNewTab, false);
    assert.equal(resNullOpts.downloaded, false);
    assert.equal(timers.length, 1);
    assert.equal(timers[0].ms, DEFAULT_BLOB_REVOKE_DELAY_MS);
  } finally {
    for (const t of timers) {
      origClearTimeout(t.id);
    }
    globalThis.setTimeout = origSetTimeout;
  }
});

test("ISS-15 (part 2): triggerBlobDownload swallows attached parentNode.removeChild errors without masking click() errors", () => {
  // 1. click() succeeds and removeChild throws on attached anchor -> returns true cleanly
  {
    let removeCalls = 0;
    const body = {
      appendChild(node) {
        node.parentNode = body;
      },
      removeChild() {
        removeCalls += 1;
        throw new Error("removeChild failed");
      },
    };
    const doc = {
      body,
      createElement: () => ({
        href: "",
        download: "",
        parentNode: null,
        clicked: 0,
        click() {
          this.clicked += 1;
        },
      }),
    };

    const ok = triggerBlobDownload({
      url: "blob:https://karobar.local/remove-fail-ok",
      filename: "report.pdf",
      doc,
    });
    assert.equal(ok, true);
    assert.equal(removeCalls, 1);
  }

  // 2. click() throws and removeChild also throws on attached anchor -> preserves original click() error
  {
    let removeCalls = 0;
    const body = {
      appendChild(node) {
        node.parentNode = body;
      },
      removeChild() {
        removeCalls += 1;
        throw new Error("removeChild cleanup error");
      },
    };
    const doc = {
      body,
      createElement: () => ({
        href: "",
        download: "",
        parentNode: null,
        click() {
          throw new Error("Original click failure");
        },
      }),
    };

    assert.throws(
      () =>
        triggerBlobDownload({
          url: "blob:https://karobar.local/remove-fail-click-fail",
          filename: "report.pdf",
          doc,
        }),
      /Original click failure/
    );
    assert.equal(removeCalls, 1);
  }
});

test("ISS-15 (part 2): openReportPdfSafely swallows synchronous scheduleTimeout errors after win.open or fallback download and preserves try errors", () => {
  // 1. After win.open succeeds
  {
    const { urlApi } = createMockUrlApi();
    let timerCalls = 0;
    const res = openReportPdfSafely({
      url: "blob:https://karobar.local/sched-fail-open",
      win: { open: () => null },
      doc: null,
      urlApi,
      scheduleTimeout() {
        timerCalls += 1;
        throw new Error("scheduleTimeout failed");
      },
    });
    assert.deepEqual(res, {
      url: "blob:https://karobar.local/sched-fail-open",
      openedInNewTab: true,
      downloaded: false,
    });
    assert.equal(timerCalls, 1);
  }

  // 2. After fallback triggerBlobDownload succeeds
  {
    const { doc, createdAnchors } = createMockDoc();
    const { urlApi } = createMockUrlApi();
    let timerCalls = 0;
    const res = openReportPdfSafely({
      url: "blob:https://karobar.local/sched-fail-download",
      filename: "fallback.pdf",
      win: {
        open() {
          throw new Error("Host blocked popup");
        },
      },
      doc,
      urlApi,
      scheduleTimeout() {
        timerCalls += 1;
        throw new Error("scheduleTimeout failed");
      },
    });
    assert.deepEqual(res, {
      url: "blob:https://karobar.local/sched-fail-download",
      openedInNewTab: false,
      downloaded: true,
    });
    assert.equal(createdAnchors.length, 1);
    assert.equal(createdAnchors[0].clicked, 1);
    assert.equal(timerCalls, 1);
  }

  // 3. When triggerBlobDownload throws inside try, synchronous scheduleTimeout failure does not mask it
  {
    const { doc } = createMockDoc({ throwOnClick: true });
    const { urlApi } = createMockUrlApi();
    let timerCalls = 0;
    assert.throws(
      () =>
        openReportPdfSafely({
          url: "blob:https://karobar.local/sched-fail-mask-check",
          win: {},
          doc,
          urlApi,
          scheduleTimeout() {
            timerCalls += 1;
            throw new Error("scheduleTimeout cleanup error");
          },
        }),
      /Simulated click error/
    );
    assert.equal(timerCalls, 1);
  }
});

test("ISS-15 (part 2) static regression: all 12 Reports/*.jsx files have zero unguarded _blank window.open or if(!win) fallbacks", () => {
  const reportsDir = path.resolve("resources/js/pages/Reports");
  const allReportFiles = fs
    .readdirSync(reportsDir)
    .filter((name) => name.endsWith(".jsx"))
    .sort();

  assert.equal(allReportFiles.length, 12);

  const targetReports = [
    {
      file: "NearExpiryProductReport.jsx",
      filenamePattern: /filename:\s*`near-expiry-products-\$\{fromDate \|\| "report"\}\.pdf`/,
      delay: 10000,
    },
    {
      file: "ProductComprehensiveReport.jsx",
      filenamePattern: /filename:\s*`product-comprehensive-\$\{productId \|\| "report"\}\.pdf`/,
      delay: 10000,
    },
    {
      file: "NarcoticsReport.jsx",
      filenamePattern: /filename:\s*`narcotics-register-\$\{from \|\| "report"\}\.pdf`/,
      delay: 60000,
    },
    {
      file: "CurrentStockReport.jsx",
      filenamePattern: /filename:\s*"current-stock-report\.pdf"/,
      delay: 10000,
    },
    {
      file: "SaleDetailReport.jsx",
      filenamePattern: /filename:\s*`sale-detail-\$\{fromDate \|\| "report"\}\.pdf`/,
      delay: 10000,
    },
    {
      file: "CostOfSaleReport.jsx",
      filenamePattern: /filename:\s*`cost-of-sale-\$\{fromDate \|\| "report"\}\.pdf`/,
      delay: 60000,
    },
    {
      file: "PurchaseDetailReport.jsx",
      filenamePattern: /filename:\s*`purchase-detail-\$\{fromDate \|\| "report"\}\.pdf`/,
      delay: 10000,
    },
    {
      file: "CostOfSaleDetailReport.jsx",
      filenamePattern: /filename:\s*`cost-of-sale-detail-\$\{fromDate \|\| "report"\}\.pdf`/,
      delay: 10000,
    },
    {
      file: "StockAdjustmentReport.jsx",
      filenamePattern: /filename:\s*`stock-adjustment-\$\{fromDate \|\| "report"\}\.pdf`/,
      delay: 10000,
    },
  ];

  for (const fileName of allReportFiles) {
    const fullPath = path.join(reportsDir, fileName);
    const src = fs.readFileSync(fullPath, "utf8");

    // 0 unguarded window.open(..., "_blank") calls anywhere in Reports/*.jsx
    assert.doesNotMatch(
      src,
      /window\.open\s*\([^)]*["']_blank["']\s*\)/,
      `Unguarded window.open(..., "_blank") found in ${fileName}`
    );

    // 0 if (!win) / if (!pdfWindow) fallbacks
    assert.doesNotMatch(
      src,
      /if\s*\(\s*!\s*(win|pdfWindow)\s*\)/,
      `if (!win) / if (!pdfWindow) fallback still present in ${fileName}`
    );

    // Any target="_blank" JSX link must have rel="noopener noreferrer"
    const blankTags = src.match(/<a\b[^>]*target=["']_blank["'][^>]*>/gs) || [];
    for (const tag of blankTags) {
      assert.match(
        tag,
        /rel=["']noopener noreferrer["']/,
        `target="_blank" anchor missing rel="noopener noreferrer" in ${fileName}`
      );
    }
  }

  for (const spec of targetReports) {
    const src = fs.readFileSync(path.join(reportsDir, spec.file), "utf8");
    assert.match(
      src,
      /import\s*\{\s*openReportPdfSafely\s*\}\s*from\s*["']\.\.\/\.\.\/utils\/reportPdfOpener["']/,
      `${spec.file} must import openReportPdfSafely from ../../utils/reportPdfOpener`
    );
    assert.match(src, /openReportPdfSafely\s*\(/, `${spec.file} must call openReportPdfSafely`);
    assert.match(src, spec.filenamePattern, `${spec.file} filename template mismatch`);
    assert.match(
      src,
      new RegExp(`revokeDelayMs:\\s*${spec.delay}\\b`),
      `${spec.file} revokeDelayMs=${spec.delay} mismatch`
    );
  }

  // MissingReports.jsx retains rel="noopener noreferrer" on its target="_blank" link
  const missingReportsSrc = fs.readFileSync(
    path.join(reportsDir, "MissingReports.jsx"),
    "utf8"
  );
  assert.match(missingReportsSrc, /target="_blank"/);
  assert.match(missingReportsSrc, /rel="noopener noreferrer"/);
});
