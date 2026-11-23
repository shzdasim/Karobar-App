// Pure-ESM helper for safely opening report PDFs in a new tab without reverse tabnabbing.
// Zero top-level React/JSX/DOM globals so it can be unit-tested directly under node:test.

export const REPORT_PDF_WINDOW_FEATURES = "noopener,noreferrer";
export const DEFAULT_BLOB_REVOKE_DELAY_MS = 10000;

/**
 * Trigger a browser download for a blob/object URL via a temporary <a download> element.
 */
export function triggerBlobDownload(options = {}) {
  const opts = options && typeof options === "object" ? options : {};
  const { url, filename = "report.pdf", doc } = opts;
  const resolvedDoc = doc === undefined ? globalThis.document : doc;

  if (!url || !resolvedDoc || typeof resolvedDoc.createElement !== "function") {
    return false;
  }

  const anchor = resolvedDoc.createElement("a");
  if (!anchor) {
    return false;
  }

  anchor.href = url;
  anchor.download = filename || "report.pdf";

  try {
    if (resolvedDoc.body && typeof resolvedDoc.body.appendChild === "function") {
      resolvedDoc.body.appendChild(anchor);
    }
    if (typeof anchor.click === "function") {
      anchor.click();
    }
    return true;
  } finally {
    if (anchor.parentNode && typeof anchor.parentNode.removeChild === "function") {
      try {
        anchor.parentNode.removeChild(anchor);
      } catch {
        // Ignore if anchor cleanup fails on parentNode
      }
    } else if (resolvedDoc.body && typeof resolvedDoc.body.removeChild === "function") {
      try {
        resolvedDoc.body.removeChild(anchor);
      } catch {
        // Ignore if anchor is not attached to doc.body
      }
    }
  }
}

/**
 * Safely create/open a report PDF blob URL in a new tab using "noopener,noreferrer".
 *
 * Per the HTML Living Standard, window.open(..., "_blank", "noopener,noreferrer")
 * returns `null` even when the tab opens successfully. Never treat `null` or
 * `undefined` from window.open as a failure; only fall back to triggerBlobDownload
 * when window.open is unavailable or throws synchronously.
 */
export function openReportPdfSafely(options = {}) {
  const opts = options && typeof options === "object" ? options : {};
  const {
    data,
    blob,
    url: existingUrl,
    filename = "report.pdf",
    revokeDelayMs = DEFAULT_BLOB_REVOKE_DELAY_MS,
    win,
    doc,
    urlApi,
    scheduleTimeout,
    createBlob,
  } = opts;

  const resolvedWin = win === undefined ? globalThis.window : win;
  const resolvedDoc = doc === undefined ? globalThis.document : doc;
  const resolvedUrlApi =
    urlApi === undefined ? (resolvedWin?.URL ?? globalThis.URL) : urlApi;
  const resolvedScheduleTimeout =
    scheduleTimeout === undefined ? globalThis.setTimeout : scheduleTimeout;
  const resolvedCreateBlob =
    createBlob ?? ((parts, blobOpts) => new Blob(parts, blobOpts));

  let url = existingUrl || "";
  if (!url) {
    if (!resolvedUrlApi || typeof resolvedUrlApi.createObjectURL !== "function") {
      throw new TypeError(
        "Cannot create PDF object URL: URL.createObjectURL is unavailable."
      );
    }
    const pdfBlob =
      blob ?? resolvedCreateBlob([data], { type: "application/pdf" });
    url = resolvedUrlApi.createObjectURL(pdfBlob);
    if (!url) {
      throw new TypeError(
        "Cannot open report PDF: URL.createObjectURL returned an empty URL."
      );
    }
  }

  let openedInNewTab = false;
  let downloaded = false;

  const delay =
    Number.isFinite(revokeDelayMs) && revokeDelayMs >= 0
      ? revokeDelayMs
      : DEFAULT_BLOB_REVOKE_DELAY_MS;

  try {
    if (resolvedWin && typeof resolvedWin.open === "function") {
      try {
        const opened = resolvedWin.open(url, "_blank", REPORT_PDF_WINDOW_FEATURES);
        if (opened) {
          try {
            opened.opener = null;
          } catch {
            // Ignore read-only or cross-origin opener assignment errors
          }
        }
        openedInNewTab = true;
      } catch {
        downloaded = triggerBlobDownload({ url, filename, doc: resolvedDoc });
      }
    } else {
      downloaded = triggerBlobDownload({ url, filename, doc: resolvedDoc });
    }
  } finally {
    if (typeof resolvedScheduleTimeout === "function") {
      try {
        resolvedScheduleTimeout(() => {
          try {
            resolvedUrlApi?.revokeObjectURL?.(url);
          } catch {
            // Ignore blob URL revocation failures
          }
        }, delay);
      } catch {
        // Ignore synchronous timer scheduling errors
      }
    }
  }

  return { url, openedInNewTab, downloaded };
}
