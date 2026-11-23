namespace Karobar.Host;

/// <summary>
/// Provides the complete WebView2 JavaScript preload injection script matching desktop/preload.cjs.
/// Exposes all 22 window.karobarDesktop properties/methods, 5 event listeners, and the
/// global window.print() silent print interceptor before any DOM onload handlers execute.
/// </summary>
public static class KarobarPreloadScript
{
    public static readonly IReadOnlyList<string> RequiredProperties =
    [
        "isDesktop",
        "isOffscreenPrintWorker",
        "platform"
    ];

    public static readonly IReadOnlyList<string> RequiredMethods =
    [
        "getAppInfo",
        "getNetworkConfig",
        "saveNetworkConfig",
        "testServerConnection",
        "getPrinters",
        "getPreferredPrinter",
        "setPreferredPrinter",
        "silentPrint",
        "printUrl",
        "printHtml",
        "selectDirectory",
        "selectSqliteFile",
        "runBackupNow",
        "checkForUpdates",
        "downloadAndInstallUpdate",
        "openExternalUrl",
        "openWindow",
        "getOpenWindows",
        "focusWindow"
    ];

    public static readonly IReadOnlyList<string> RequiredEventListeners =
    [
        "onNavigate",
        "onOpenSubWindow",
        "onWindowCommand",
        "onUpdateProgress",
        "onTriggerUpdateCheck"
    ];

    public static readonly IReadOnlyList<string> RequiredIpcChannels =
    [
        "karobar:app-info",
        "karobar:get-network-config",
        "karobar:save-network-config",
        "karobar:test-server-connection",
        "karobar:get-printers",
        "karobar:get-preferred-printer",
        "karobar:set-preferred-printer",
        "karobar:silent-print",
        "karobar:print-url",
        "karobar:print-html",
        "karobar:select-directory",
        "karobar:select-sqlite-file",
        "karobar:run-backup-now",
        "karobar:check-for-updates",
        "karobar:download-and-install-update",
        "karobar:open-external-url",
        "karobar:open-window",
        "karobar:get-open-windows",
        "karobar:focus-window",
        "karobar:navigate",
        "karobar:open-subwindow",
        "karobar:window-command",
        "karobar:update-progress",
        "karobar:trigger-update-check"
    ];

    public static string GetPreloadScript(bool isOffscreenPrintWorker = false)
    {
        var offscreenFlag = isOffscreenPrintWorker ? "true" : "false";
        return $$"""
        (function () {
          if (window.karobarDesktop && window.__karobarDesktopBridgeInitialized) {
            return;
          }
          window.__karobarDesktopBridgeInitialized = true;

          const isOffscreenPrintWorker = {{offscreenFlag}} ||
            (typeof window !== "undefined" &&
              window.location &&
              /[?&](?:silent_print=1|__karobar_offscreen_print=1)/i.test(String(window.location.search || "")));

          const eventListeners = {
            "karobar:navigate": new Set(),
            "karobar:open-subwindow": new Set(),
            "karobar:window-command": new Set(),
            "karobar:update-progress": new Set(),
            "karobar:trigger-update-check": new Set()
          };

          const pendingInvocations = new Map();
          let nextInvokeId = 1;

          function dispatchEventToListeners(channel, payload) {
            const set = eventListeners[channel];
            if (!set) return;
            for (const handler of Array.from(set)) {
              try {
                handler(payload);
              } catch (err) {
                console.error("[karobarDesktop] Event listener error on " + channel, err);
              }
            }
          }

          window.__karobarDispatchDesktopEvent = function (channel, payload) {
            dispatchEventToListeners(channel, payload);
          };

          if (window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === "function") {
            window.chrome.webview.addEventListener("message", function (event) {
              try {
                const msg = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
                if (!msg || typeof msg !== "object") return;

                if (msg.type === "ipc-response" && msg.id) {
                  const pending = pendingInvocations.get(msg.id);
                  if (pending) {
                    pendingInvocations.delete(msg.id);
                    if (msg.error) {
                      pending.reject(new Error(String(msg.error)));
                    } else {
                      pending.resolve(msg.result);
                    }
                  }
                  return;
                }

                if (msg.type === "ipc-event" && msg.channel) {
                  dispatchEventToListeners(msg.channel, msg.payload);
                }
              } catch (e) {
                // Ignore malformed WebView2 message
              }
            });
          }

          async function invokeViaHttpFallback(channel, payload) {
            const response = await fetch("/__karobar_ipc/invoke", {
              method: "POST",
              headers: {
                "Content-Type": "application/json",
                "Accept": "application/json"
              },
              body: JSON.stringify({ channel: channel, payload: payload ?? {} })
            });
            if (!response.ok) {
              throw new Error("IPC HTTP error " + response.status + " on " + channel);
            }
            const envelope = await response.json();
            if (envelope && Object.prototype.hasOwnProperty.call(envelope, "result")) {
              return envelope.result;
            }
            return envelope;
          }

          function ipcInvoke(channel, payload) {
            if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function" && window.__karobarUseNativeWebView2PostMessage) {
              return new Promise(function (resolve, reject) {
                const id = "ipc_" + (nextInvokeId++) + "_" + Date.now();
                pendingInvocations.set(id, {
                  resolve: function (val) {
                    resolve(val);
                  },
                  reject: function (err) {
                    reject(err);
                  }
                });

                try {
                  window.chrome.webview.postMessage(JSON.stringify({
                    type: "ipc-invoke",
                    id: id,
                    channel: channel,
                    payload: payload ?? {}
                  }));
                } catch (err) {
                  pendingInvocations.delete(id);
                  invokeViaHttpFallback(channel, payload).then(resolve).catch(reject);
                }
              });
            }

            return invokeViaHttpFallback(channel, payload);
          }

          function subscribeChannel(channel, callback) {
            if (typeof callback !== "function") {
              return function () {};
            }
            if (!eventListeners[channel]) {
              eventListeners[channel] = new Set();
            }
            eventListeners[channel].add(callback);
            return function () {
              eventListeners[channel].delete(callback);
            };
          }

          const hasNativeHost = Boolean(
            window.__karobarUseNativeWebView2PostMessage ||
            (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
          );

          const desktopApi = {
            isDesktop: true,
            isNative: true,
            hasNativeHost: hasNativeHost,
            isOffscreenPrintWorker: Boolean(isOffscreenPrintWorker),
            platform: "win32",
            getAppInfo: () => ipcInvoke("karobar:app-info"),
            getNetworkConfig: () => ipcInvoke("karobar:get-network-config"),
            saveNetworkConfig: (payload = {}) => ipcInvoke("karobar:save-network-config", payload),
            testServerConnection: (payload = {}) => ipcInvoke("karobar:test-server-connection", payload),
            getPrinters: () => ipcInvoke("karobar:get-printers"),
            getPreferredPrinter: () => ipcInvoke("karobar:get-preferred-printer"),
            kickCashDrawer: (payload = {}) => ipcInvoke("karobar:kick-cash-drawer", payload),
            printThermalReceipt: (payload = {}) => ipcInvoke("karobar:print-thermal-raw", payload),
            setPreferredPrinter: (deviceName) =>
              ipcInvoke("karobar:set-preferred-printer", { deviceName }),
            silentPrint: (options = {}) => ipcInvoke("karobar:silent-print", options),
            printUrl: (url, options = {}) => ipcInvoke("karobar:print-url", { url, ...options }),
            printHtml: (html, options = {}) => ipcInvoke("karobar:print-html", { html, ...options }),
            selectDirectory: () => ipcInvoke("karobar:select-directory"),
            selectSqliteFile: () => ipcInvoke("karobar:select-sqlite-file"),
            runBackupNow: () => ipcInvoke("karobar:run-backup-now"),
            checkForUpdates: (force = false) => ipcInvoke("karobar:check-for-updates", { force }),
            downloadAndInstallUpdate: (payload = {}) => ipcInvoke("karobar:download-and-install-update", payload),
            openExternalUrl: (url) => ipcInvoke("karobar:open-external-url", url),
            openWindow: (path, title, options = {}) =>
              ipcInvoke("karobar:open-window", { path, title, ...options }),
            getOpenWindows: () => ipcInvoke("karobar:get-open-windows"),
            focusWindow: (winId) => ipcInvoke("karobar:focus-window", { winId }),
            toggleGuideBot: () => ipcInvoke("karobar:toggle-guide-bot"),
            openGuideBot: (options = {}) => ipcInvoke("karobar:open-guide-bot", options),
            getMascotPrefs: () => ipcInvoke("karobar:get-mascot-prefs"),
            setMascotPrefs: (payload = {}) => ipcInvoke("karobar:set-mascot-prefs", payload),
            onNavigate: (callback) => subscribeChannel("karobar:navigate", callback),
            onOpenSubWindow: (callback) => subscribeChannel("karobar:open-subwindow", callback),
            onWindowCommand: (callback) => subscribeChannel("karobar:window-command", callback),
            onUpdateProgress: (callback) => subscribeChannel("karobar:update-progress", callback),
            onTriggerUpdateCheck: (callback) => subscribeChannel("karobar:trigger-update-check", callback),
            onGuideTourCommand: (callback) => subscribeChannel("karobar:guide-tour", callback)
          };

          try {
            Object.defineProperty(window, "karobarDesktop", {
              value: desktopApi,
              writable: false,
              configurable: true,
              enumerable: true
            });
          } catch (_) {
            window.karobarDesktop = desktopApi;
          }

          // Intercept window.print() in the page's main world before any DOM onload handlers execute
          // so native OS print dialogs or popup print dialogs are never shown inside WebView2,
          // while preserving native browser print when opened in a regular browser on localhost.
          try {
            var __nativeWindowPrint = typeof window.print === "function" ? window.print.bind(window) : null;
            if (__nativeWindowPrint && !window.__karobarNativeWindowPrint) {
              window.__karobarNativeWindowPrint = __nativeWindowPrint;
            }
            if (isOffscreenPrintWorker) {
              window.__karobarSilentPrintWorker = true;
              window.print = function () {};
              window.close = function () {};
            } else {
              if (!window.__karobarWindowPrintPatched) {
                window.__karobarWindowPrintPatched = true;
                window.print = function () {
                  try {
                    if (!hasNativeHost && typeof window.__karobarNativeWindowPrint === "function") {
                      window.__karobarNativeWindowPrint();
                      return;
                    }
                    if (window.karobarDesktop && typeof window.karobarDesktop.silentPrint === "function") {
                      var preferred = "";
                      try {
                        preferred = String(window.localStorage.getItem("karobar_preferred_printer") || "").trim();
                      } catch (e) {}
                      var deviceName =
                        preferred && preferred !== "default" && preferred !== "system_default"
                          ? preferred
                          : undefined;
                      Promise.resolve(window.karobarDesktop.silentPrint({ silent: true, deviceName: deviceName }))
                        .then(function (res) {
                          if (res && res.fallbackToBrowserPrint && typeof window.__karobarNativeWindowPrint === "function") {
                            window.__karobarNativeWindowPrint();
                          }
                        })
                        .catch(function () {});
                    } else if (typeof window.__karobarNativeWindowPrint === "function") {
                      window.__karobarNativeWindowPrint();
                    }
                  } catch (e) {}
                };
              }
            }
          } catch (_) {}

          // Waseela-style Multi-Tab ergonomics: Middle-click or Ctrl+Click any internal link opens it
          // in a new tab inside the single native main window instead of spawning a separate OS window.
          try {
            if (!isOffscreenPrintWorker && !window.__karobarTabLinkHooked) {
              window.__karobarTabLinkHooked = true;
              var openInternalLinkInTab = function (e) {
                var anchor = e.target && e.target.closest ? e.target.closest("a[href]") : null;
                if (!anchor) return;
                var href = anchor.getAttribute("href") || "";
                if (!href || href.startsWith("#") || href.startsWith("javascript:") || href.startsWith("mailto:")) return;
                try {
                  var url = new URL(href, window.location.origin);
                  if (url.origin !== window.location.origin) return;
                  if (url.pathname.startsWith("/print/")) return;
                  e.preventDefault();
                  e.stopPropagation();
                  var label = (anchor.getAttribute("title") || anchor.textContent || "").trim().slice(0, 40);
                  desktopApi.openWindow(url.pathname + url.search, label || "Workspace");
                } catch (_) {}
              };
              document.addEventListener("auxclick", function (e) {
                if (e.button === 1) openInternalLinkInTab(e);
              }, true);
              document.addEventListener("click", function (e) {
                if (e.button === 0 && (e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey) {
                  openInternalLinkInTab(e);
                }
              }, true);
            }
          } catch (_) {}

          // Waseela-style Tab-Switching Keyboard Shortcuts (forwarded straight to native tab host)
          try {
            if (!isOffscreenPrintWorker && !window.__karobarTabShortcutsHooked) {
              window.__karobarTabShortcutsHooked = true;
              var sendTabShortcut = function (e, action, index) {
                if (!window.chrome || !window.chrome.webview || typeof window.chrome.webview.postMessage !== "function") {
                  return;
                }
                e.preventDefault();
                e.stopPropagation();
                window.chrome.webview.postMessage(JSON.stringify({
                  type: "tab-shortcut",
                  action: action,
                  index: typeof index === "number" ? index : -1
                }));
              };
              window.addEventListener("keydown", function (e) {
                var code = e.code || "";
                var key = (e.key || "").toLowerCase();
                var ctrl = Boolean(e.ctrlKey && !e.altKey && !e.metaKey);
                var alt = Boolean(e.altKey && !e.ctrlKey && !e.shiftKey && !e.metaKey);

                if (ctrl && !e.shiftKey && (code === "Tab" || code === "PageDown" || code === "BracketRight" || code === "F6")) {
                  sendTabShortcut(e, "cycle-next");
                  return;
                }
                if (ctrl && e.shiftKey && (code === "Tab" || code === "PageUp" || code === "BracketLeft" || code === "F6")) {
                  sendTabShortcut(e, "cycle-prev");
                  return;
                }
                if (ctrl && e.shiftKey && code === "PageDown") {
                  sendTabShortcut(e, "cycle-next");
                  return;
                }
                if (ctrl && !e.shiftKey && (code === "PageUp" || code === "BracketLeft")) {
                  sendTabShortcut(e, "cycle-prev");
                  return;
                }
                if (ctrl && !e.shiftKey && code === "F4") {
                  sendTabShortcut(e, "close-active");
                  return;
                }
                if (ctrl && e.shiftKey && key === "q") {
                  sendTabShortcut(e, "close-active");
                  return;
                }
                if (!e.ctrlKey && !e.altKey && !e.metaKey && !e.shiftKey && code === "F5") {
                  sendTabShortcut(e, "reload-active");
                  return;
                }
                if (ctrl && !e.shiftKey && key === "g") {
                  sendTabShortcut(e, "toggle-guide-bot");
                  return;
                }
                var curPath = (window.location && window.location.pathname) || "";
                var isInvoiceFormRoute = /^\/(?:sale-invoices|wholesale-invoices|purchase-invoices)\/(?:create(?:-[a-z]+|\/[a-z]+)?|\d+\/edit)\/?$/i.test(curPath);
                var isSaleInvoiceFormRoute = /^\/(?:sale-invoices|wholesale-invoices)\/(?:create(?:-[a-z]+|\/[a-z]+)?|\d+\/edit)\/?$/i.test(curPath);
                if ((ctrl && !e.shiftKey && key === "t") || (ctrl && e.shiftKey && key === "n")) {
                  // Preserve Ctrl+T on Sale / Wholesale / Purchase Invoice screens
                  if (key === "t" && !e.shiftKey && isInvoiceFormRoute) {
                    return;
                  }
                  sendTabShortcut(e, "new-tab");
                  return;
                }
                if (ctrl && e.shiftKey && key === "t") {
                  if (isInvoiceFormRoute) return;
                  sendTabShortcut(e, "reopen-closed");
                  return;
                }
                if (ctrl && e.shiftKey && key === "d") {
                  if (isInvoiceFormRoute) return;
                  sendTabShortcut(e, "duplicate");
                  return;
                }
                if (ctrl && e.shiftKey && key === "x") {
                  sendTabShortcut(e, "close-others");
                  return;
                }
                if (ctrl && e.shiftKey && key === "p") {
                  if (isSaleInvoiceFormRoute) return;
                  sendTabShortcut(e, "open-pos");
                  return;
                }
                if (ctrl && e.shiftKey && key === "g") {
                  sendTabShortcut(e, "new-purchase");
                  return;
                }
                if (ctrl && e.shiftKey && key === "l") {
                  sendTabShortcut(e, "open-ledger");
                  return;
                }

                // Alt+1..9 or Ctrl+Shift+1..9 jump directly to tab 1..8 or last tab (9)
                if (alt || (ctrl && e.shiftKey)) {
                  var digitMatch = /^(?:Digit|Numpad)([1-9])$/.exec(code);
                  if (digitMatch) {
                    var n = parseInt(digitMatch[1], 10);
                    if (n === 9) {
                      sendTabShortcut(e, "focus-last");
                    } else {
                      sendTabShortcut(e, "focus-index", n - 1);
                    }
                  }
                }
              }, true);
            }
          } catch (_) {}

          // Native GuideBot & Bottom Mascot Bridge (syncs route, auth token, focused-input rect, user activity, and cross-tab events)
          try {
            if (!isOffscreenPrintWorker && !window.__karobarGuideBridgeHooked) {
              window.__karobarGuideBridgeHooked = true;
              var postGuideMsg = function (obj) {
                try {
                  if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function") {
                    window.chrome.webview.postMessage(JSON.stringify(obj));
                  }
                } catch (_) {}
              };
              var lastSentRoute = "";
              var lastSentToken = "";
              var pushRouteContext = function () {
                try {
                  var p = (window.location && window.location.pathname) || "/dashboard";
                  var tok = "";
                  try { tok = String(window.localStorage.getItem("token") || ""); } catch (_) {}
                  if (p !== lastSentRoute || tok !== lastSentToken) {
                    lastSentRoute = p;
                    lastSentToken = tok;
                    postGuideMsg({ type: "guide-context", route: p, token: tok });
                  }
                } catch (_) {}
              };
              var origPush = history.pushState;
              if (typeof origPush === "function") {
                history.pushState = function () {
                  var res = origPush.apply(this, arguments);
                  setTimeout(pushRouteContext, 20);
                  return res;
                };
              }
              window.addEventListener("popstate", pushRouteContext);
              setInterval(pushRouteContext, 2500);
              setTimeout(pushRouteContext, 300);

              var lastActivityPing = 0;
              var pingActivity = function () {
                var now = Date.now();
                if (now - lastActivityPing > 1500) {
                  lastActivityPing = now;
                  postGuideMsg({ type: "guide-activity" });
                }
              };
              window.addEventListener("keydown", pingActivity, true);
              window.addEventListener("pointerdown", function (e) {
                pingActivity();
                try {
                  var target = e && e.target;
                  var isTrigger = Boolean(target && target.closest && target.closest('[data-guide-bot-trigger="true"]'));
                  if (!isTrigger) {
                    postGuideMsg({ type: "guide-outside-click" });
                  }
                } catch (_) {}
              }, true);
              window.addEventListener("focusin", function (e) {
                pingActivity();
                try {
                  var el = e.target;
                  if (el && typeof el.getBoundingClientRect === "function") {
                    var r = el.getBoundingClientRect();
                    if (r.width > 0 && r.height > 0) {
                      postGuideMsg({
                        type: "guide-focus-rect",
                        left: Math.round(r.left),
                        top: Math.round(r.top),
                        right: Math.round(r.right),
                        bottom: Math.round(r.bottom)
                      });
                    }
                  }
                } catch (_) {}
              }, true);

              // Relay native host GuideTour & Cross-Tab refresh commands into DOM CustomEvents
              subscribeChannel("karobar:guide-tour", function (payload) {
                try { window.dispatchEvent(new CustomEvent("karobar:guide-tour", { detail: payload })); } catch (_) {}
              });
              subscribeChannel("karobar:products-updated", function (payload) {
                try { window.dispatchEvent(new CustomEvent("karobar:products-updated", { detail: payload || {} })); } catch (_) {}
              });
            }
          } catch (_) {}
        })();
        """;
    }
}
