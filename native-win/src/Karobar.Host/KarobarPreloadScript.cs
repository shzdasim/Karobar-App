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
                const timer = setTimeout(function () {
                  if (pendingInvocations.has(id)) {
                    pendingInvocations.delete(id);
                    invokeViaHttpFallback(channel, payload).then(resolve).catch(reject);
                  }
                }, 2500);

                pendingInvocations.set(id, {
                  resolve: function (val) {
                    clearTimeout(timer);
                    resolve(val);
                  },
                  reject: function (err) {
                    clearTimeout(timer);
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
                  clearTimeout(timer);
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

          // Intercept Ctrl+Tab / Ctrl+Shift+Tab for Browser Tab cycling matching desktop/main.cjs
          if (typeof window.addEventListener === "function") {
            window.addEventListener("keydown", function (e) {
              if (e.key === "Tab" && e.ctrlKey && !e.altKey && !e.metaKey) {
                e.preventDefault();
                dispatchEventToListeners("karobar:window-command", {
                  command: e.shiftKey ? "prev-tab" : "next-tab"
                });
              }
            }, true);
          }

          const desktopApi = {
            isDesktop: true,
            isOffscreenPrintWorker: Boolean(isOffscreenPrintWorker),
            platform: "win32",
            getAppInfo: () => ipcInvoke("karobar:app-info"),
            getNetworkConfig: () => ipcInvoke("karobar:get-network-config"),
            saveNetworkConfig: (payload = {}) => ipcInvoke("karobar:save-network-config", payload),
            testServerConnection: (payload = {}) => ipcInvoke("karobar:test-server-connection", payload),
            getPrinters: () => ipcInvoke("karobar:get-printers"),
            getPreferredPrinter: () => ipcInvoke("karobar:get-preferred-printer"),
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
            onNavigate: (callback) => subscribeChannel("karobar:navigate", callback),
            onOpenSubWindow: (callback) => subscribeChannel("karobar:open-subwindow", callback),
            onWindowCommand: (callback) => subscribeChannel("karobar:window-command", callback),
            onUpdateProgress: (callback) => subscribeChannel("karobar:update-progress", callback),
            onTriggerUpdateCheck: (callback) => subscribeChannel("karobar:trigger-update-check", callback)
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
          // so native OS print dialogs or popup print dialogs are never shown.
          try {
            if (isOffscreenPrintWorker) {
              window.__karobarSilentPrintWorker = true;
              window.print = function () {};
              window.close = function () {};
            } else {
              if (!window.__karobarWindowPrintPatched) {
                window.__karobarWindowPrintPatched = true;
                window.print = function () {
                  try {
                    if (window.karobarDesktop && typeof window.karobarDesktop.silentPrint === "function") {
                      var preferred = "";
                      try {
                        preferred = String(window.localStorage.getItem("karobar_preferred_printer") || "").trim();
                      } catch (e) {}
                      var deviceName =
                        preferred && preferred !== "default" && preferred !== "system_default"
                          ? preferred
                          : undefined;
                      window.karobarDesktop.silentPrint({ silent: true, deviceName: deviceName });
                    }
                  } catch (e) {}
                };
              }
            }
          } catch (_) {}
        })();
        """;
    }
}
