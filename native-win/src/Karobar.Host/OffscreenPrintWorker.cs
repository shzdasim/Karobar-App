#if WINDOWS_DESKTOP
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Karobar.Host;

/// <summary>
/// Persistent prewarmed offscreen WebView2 print worker owned by WebView2WindowManager.
/// Eliminates per-job WebView2 cold-start and temporary HTML print files while
/// attaching terminal identity headers and resetting to about:blank after each job.
/// </summary>
public sealed class OffscreenPrintWorker : IDisposable
{
    private readonly WebView2WindowManager _windowManager;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _printMutex = new(1, 1);
    private readonly DeferredSemaphoreDisposalCoordinator _lifecycle;

    private OffscreenPrintForm? _form;
    private WebView2? _webView;
    private CoreWebView2? _core;
    private SynchronizationContext? _uiSyncContext;
    private int _disposeStarted;

    public OffscreenPrintWorker(WebView2WindowManager windowManager)
    {
        _windowManager = windowManager;
        _lifecycle = new DeferredSemaphoreDisposalCoordinator(_initLock, _printMutex);
    }

    public void CaptureUiSynchronizationContext(SynchronizationContext? syncContext = null)
    {
        var candidate = syncContext ?? SynchronizationContext.Current;
        if (candidate != null)
        {
            _uiSyncContext = candidate;
        }
    }

    public async Task PrewarmAsync(CancellationToken cancellationToken = default)
    {
        if (!_lifecycle.TryEnterOperation(cancellationToken, out var linkedCts) || linkedCts == null)
        {
            return;
        }

        var lockAcquired = false;
        try
        {
            using (linkedCts)
            {
                var linkedToken = linkedCts.Token;
                await _initLock.WaitAsync(linkedToken).ConfigureAwait(false);
                lockAcquired = true;
                if (_lifecycle.IsDisposed || linkedToken.IsCancellationRequested || _core != null) return;
                await MarshalToUiAsync(EnsureInitializedOnUiAsync, linkedToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by caller or worker disposal
        }
        catch (ObjectDisposedException)
        {
            // Worker disposed during shutdown
        }
        catch
        {
            // Prewarm failure is non-fatal; PrintAsync will retry on demand.
        }
        finally
        {
            if (lockAcquired)
            {
                TryReleaseSemaphore(_initLock);
            }
            _lifecycle.ExitOperation();
        }
    }

    public async Task<(bool Ok, string? Engine, string? Error)> PrintAsync(
        string? targetUrl,
        string? targetHtml,
        string printerName,
        bool isThermal,
        CancellationToken cancellationToken = default)
    {
        if (_lifecycle.IsDisposed)
        {
            return (false, "webview2-silent-print", "Offscreen print worker has been shut down.");
        }

        if (string.IsNullOrWhiteSpace(targetUrl) && string.IsNullOrWhiteSpace(targetHtml))
        {
            return (false, "webview2-silent-print", "No print URL or HTML content supplied.");
        }

        if (!_lifecycle.TryEnterOperation(cancellationToken, out var linkedCts) || linkedCts == null)
        {
            return (false, "webview2-silent-print", "Offscreen print worker has been shut down.");
        }

        var printLockAcquired = false;
        try
        {
            using (linkedCts)
            {
                var linkedToken = linkedCts.Token;

                await _printMutex.WaitAsync(linkedToken).ConfigureAwait(false);
                printLockAcquired = true;
                if (_lifecycle.IsDisposed || linkedToken.IsCancellationRequested)
                {
                    return (false, "webview2-silent-print",
                        _lifecycle.IsDisposed
                            ? "Offscreen print worker has been shut down."
                            : "Silent print job was cancelled.");
                }

                return await MarshalToUiAsync(async () =>
                {
                    if (_lifecycle.IsDisposed || linkedToken.IsCancellationRequested)
                    {
                        return (false, "webview2-silent-print",
                            _lifecycle.IsDisposed
                                ? "Offscreen print worker has been shut down."
                                : "Silent print job was cancelled.");
                    }

                    var initLockAcquired = false;
                    try
                    {
                        await _initLock.WaitAsync(linkedToken);
                        initLockAcquired = true;
                        if (_lifecycle.IsDisposed || linkedToken.IsCancellationRequested)
                        {
                            return (false, "webview2-silent-print",
                                _lifecycle.IsDisposed
                                    ? "Offscreen print worker has been shut down."
                                    : "Silent print job was cancelled.");
                        }
                        await EnsureInitializedOnUiAsync();
                    }
                    finally
                    {
                        if (initLockAcquired)
                        {
                            TryReleaseSemaphore(_initLock);
                        }
                    }

                    if (_core == null || _form == null || _form.IsDisposed)
                    {
                        return (false, "webview2-silent-print", "Failed to initialize offscreen print WebView2.");
                    }

                    _form.Size = new Size(isThermal ? 380 : 820, 900);

                    var navTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs args)
                    {
                        navTcs.TrySetResult(args.IsSuccess);
                    }

                    _core.NavigationCompleted += OnNavCompleted;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(targetUrl))
                        {
                            _core.Navigate(targetUrl);
                        }
                        else
                        {
                            _core.NavigateToString(targetHtml!);
                        }

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
                        var completed = await Task.WhenAny(navTcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));
                        if (completed != navTcs.Task)
                        {
                            return (false, "webview2-silent-print",
                                _lifecycle.IsDisposed
                                    ? "Offscreen print worker has been shut down."
                                    : cancellationToken.IsCancellationRequested
                                        ? "Silent print job was cancelled."
                                        : "Timed out waiting for receipt page to load before printing.");
                        }

                        if (!await navTcs.Task)
                        {
                            return (false, "webview2-silent-print",
                                "Failed to load print document (navigation error or HTTP 403/404/500).");
                        }

                        try
                        {
                            await _core.ExecuteScriptAsync(
                                "(async () => { if (document.fonts && document.fonts.ready) { await document.fonts.ready; } await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); })()");
                        }
                        catch
                        {
                            await Task.Delay(120, linkedToken);
                        }

                        if (!string.IsNullOrWhiteSpace(targetUrl) &&
                            Uri.TryCreate(targetUrl, UriKind.Absolute, out var pdfUri) &&
                            pdfUri.IsFile &&
                            pdfUri.LocalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            // Give Chromium's PDFium plugin brief settle time before CoreWebView2.PrintAsync
                            await Task.Delay(350, linkedToken);
                        }

                        return await PrintCoreWebViewAsync(_core, printerName, isThermal);
                    }
                    finally
                    {
                        if (_core != null)
                        {
                            _core.NavigationCompleted -= OnNavCompleted;
                        }
                        await ResetToBlankOnUiAsync();
                    }
                }, linkedToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return (false, "webview2-silent-print",
                _lifecycle.IsDisposed
                    ? "Offscreen print worker has been shut down."
                    : "Silent print job was cancelled.");
        }
        catch (ObjectDisposedException)
        {
            return (false, "webview2-silent-print", "Offscreen print worker has been shut down.");
        }
        catch (Exception ex)
        {
            return (false, "webview2-silent-print", ex.Message);
        }
        finally
        {
            if (printLockAcquired)
            {
                TryReleaseSemaphore(_printMutex);
            }
            _lifecycle.ExitOperation();
        }
    }

    private async Task EnsureInitializedOnUiAsync()
    {
        if (_lifecycle.IsDisposed) return;

        if (_core != null && _form is { IsDisposed: false } && _webView is { IsDisposed: false })
        {
            return;
        }

        ResetWorkerOnUi();

        var form = new OffscreenPrintForm(820, 900);
        var wv = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.White
        };
        form.Controls.Add(wv);
        form.Show();

        try
        {
            var env = await _windowManager.GetOrCreateSharedWebView2EnvironmentAsync();
            await wv.EnsureCoreWebView2Async(env);
            if (_lifecycle.IsDisposed || form.IsDisposed || wv.IsDisposed || wv.CoreWebView2 == null)
            {
                try { wv.Dispose(); } catch { }
                try { form.Close(); form.Dispose(); } catch { }
                return;
            }

            var core = wv.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, reqArgs) =>
            {
                try
                {
                    var reqUri = reqArgs.Request.Uri ?? "";
                    var appBaseUrl = _windowManager.NetworkConfig.GetAppBaseUrl();
                    var localIpBase = $"http://127.0.0.1:{_windowManager.NetworkConfig.ActivePort}";
                    var localhostBase = $"http://localhost:{_windowManager.NetworkConfig.ActivePort}";

                    var isAppOrigin = (!string.IsNullOrWhiteSpace(appBaseUrl) &&
                                       reqUri.StartsWith(appBaseUrl, StringComparison.OrdinalIgnoreCase)) ||
                                      (_windowManager.NetworkConfig.ActivePort > 0 &&
                                       (reqUri.StartsWith(localIpBase, StringComparison.OrdinalIgnoreCase) ||
                                        reqUri.StartsWith(localhostBase, StringComparison.OrdinalIgnoreCase)));

                    if (isAppOrigin)
                    {
                        var headers = _windowManager.NetworkConfig.GetTerminalIdentityHeaders();
                        foreach (var kv in headers)
                        {
                            if (!string.IsNullOrWhiteSpace(kv.Value))
                            {
                                reqArgs.Request.Headers.SetHeader(kv.Key, kv.Value);
                            }
                        }
                    }
                }
                catch
                {
                    // Never block resource loading on header injection failure
                }
            };

            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                KarobarPreloadScript.GetPreloadScript(isOffscreenPrintWorker: true));

            core.ProcessFailed += (_, _) =>
            {
                try
                {
                    if (_form is { IsDisposed: false, IsHandleCreated: true } && _form.InvokeRequired)
                    {
                        _form.BeginInvoke((Action)ResetWorkerOnUi);
                    }
                    else
                    {
                        ResetWorkerOnUi();
                    }
                }
                catch { }
            };

            _form = form;
            _webView = wv;
            _core = core;
        }
        catch
        {
            try { wv.Dispose(); } catch { }
            try { form.Close(); form.Dispose(); } catch { }
            throw;
        }
    }

    private async Task ResetToBlankOnUiAsync()
    {
        var core = _core;
        if (core == null) return;

        var blankTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnBlankDone(object? s, CoreWebView2NavigationCompletedEventArgs args)
        {
            blankTcs.TrySetResult(args.IsSuccess);
        }

        core.NavigationCompleted += OnBlankDone;
        try
        {
            core.Navigate("about:blank");
            await Task.WhenAny(blankTcs.Task, Task.Delay(2000));
        }
        catch
        {
            ResetWorkerOnUi();
        }
        finally
        {
            try { core.NavigationCompleted -= OnBlankDone; } catch { }
        }
    }

    private void ResetWorkerOnUi()
    {
        _core = null;
        if (_webView != null)
        {
            try { _webView.Dispose(); } catch { }
            _webView = null;
        }
        if (_form != null)
        {
            try
            {
                if (!_form.IsDisposed)
                {
                    _form.Close();
                    _form.Dispose();
                }
            }
            catch { }
            _form = null;
        }
    }

    private Task<T> MarshalToUiAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (_form is { IsDisposed: false, IsHandleCreated: true })
        {
            if (!_form.InvokeRequired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return work();
            }

            return DispatchToUiAsync(action => _form.BeginInvoke(action), work, cancellationToken);
        }

        var openForm = Application.OpenForms.OfType<Form>().FirstOrDefault(f => !f.IsDisposed && f.IsHandleCreated);
        if (openForm != null)
        {
            if (!openForm.InvokeRequired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return work();
            }

            return DispatchToUiAsync(action => openForm.BeginInvoke(action), work, cancellationToken);
        }

        var syncCtx = _uiSyncContext ?? SynchronizationContext.Current;
        if (syncCtx != null)
        {
            return DispatchToUiAsync(action => syncCtx.Post(_ => action(), null), work, cancellationToken);
        }

        throw new InvalidOperationException("No active WinForms UI thread is available for offscreen silent printing.");
    }

    private Task MarshalToUiAsync(Func<Task> work, CancellationToken cancellationToken = default)
        => MarshalToUiAsync(async () =>
        {
            await work();
            return true;
        }, cancellationToken);

    private static async Task<T> DispatchToUiAsync<T>(
        Action<Action> postToUi,
        Func<Task<T>> work,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = 0; // 0 = pending, 1 = started, 2 = canceled

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() =>
            {
                if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
                {
                    tcs.TrySetCanceled(cancellationToken);
                }
            })
            : default;

        try
        {
            if (Volatile.Read(ref state) == 0)
            {
                postToUi(async () =>
                {
                    if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
                    {
                        return;
                    }

                    try
                    {
                        tcs.TrySetResult(await work());
                    }
                    catch (OperationCanceledException oce)
                    {
                        tcs.TrySetCanceled(oce.CancellationToken.CanBeCanceled ? oce.CancellationToken : cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                }
                else
                {
                    tcs.TrySetException(ex);
                }
            }
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    internal static async Task<(bool Ok, string? Engine, string? Error)> PrintCoreWebViewAsync(
        CoreWebView2 core,
        string printerName,
        bool isThermal)
    {
        var settings = core.Environment.CreatePrintSettings();
        settings.PrinterName = printerName;
        settings.Copies = 1;
        settings.Orientation = CoreWebView2PrintOrientation.Portrait;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ShouldPrintSelectionOnly = false;
        var requestedCopies = 1;
        if (isThermal)
        {
            settings.MarginTop = 0;
            settings.MarginBottom = 0;
            settings.MarginLeft = 0;
            settings.MarginRight = 0;
            settings.ScaleFactor = 1.0;
        }

        // Read <meta name="karobar-print-config"> for ALL silent prints (copies + optional paperWidthMm)
        try
        {
            var metaRaw = await core.ExecuteScriptAsync(
                "(function(){var m=document.querySelector('meta[name=\"karobar-print-config\"]');return m?m.getAttribute('content'):'';})()");
            if (!string.IsNullOrWhiteSpace(metaRaw) && metaRaw != "null" && metaRaw != "\"\"")
            {
                var unescaped = JsonSerializer.Deserialize<string>(metaRaw);
                if (!string.IsNullOrWhiteSpace(unescaped))
                {
                    using var cfgDoc = JsonDocument.Parse(unescaped);
                    if (isThermal && cfgDoc.RootElement.TryGetProperty("paperWidthMm", out var pwEl) &&
                        pwEl.TryGetDouble(out var pwMm) && pwMm is >= 25 and <= 216)
                    {
                        settings.PageWidth = pwMm / 25.4;
                    }
                    if (cfgDoc.RootElement.TryGetProperty("copies", out var cpEl) &&
                        cpEl.TryGetInt32(out var copies) && copies is >= 1 and <= 20)
                    {
                        requestedCopies = copies;
                        settings.Copies = copies;
                    }
                }
            }
        }
        catch { }

        CoreWebView2PrintStatus status;
        try
        {
            status = await core.PrintAsync(settings);
        }
        catch (ArgumentException)
        {
            // Fallback for thermal drivers that reject explicit 0 margins or custom scale factor
            var fallback = core.Environment.CreatePrintSettings();
            fallback.PrinterName = printerName;
            fallback.Copies = requestedCopies;
            fallback.ShouldPrintBackgrounds = true;
            fallback.ShouldPrintHeaderAndFooter = false;
            status = await core.PrintAsync(fallback);
        }

        if (status == CoreWebView2PrintStatus.Succeeded)
        {
            return (true, "webview2-silent-print", null);
        }

        var reason = status == CoreWebView2PrintStatus.PrinterUnavailable
            ? $"Printer '{printerName}' is offline, disconnected, or unavailable in Windows."
            : $"Windows printer '{printerName}' failed to print (status: {status}).";
        return (false, "webview2-silent-print", reason);
    }

    private static void TryReleaseSemaphore(SemaphoreSlim semaphore)
    {
        try
        {
            semaphore.Release();
        }
        catch (ObjectDisposedException)
        {
            // Safe during shutdown disposal race
        }
        catch (SemaphoreFullException)
        {
            // Defensive guard
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;

        // Cancels the coordinator's dispose token immediately to wake suspended WaitAsync waiters,
        // and disposes the gates now if idle or when the last in-flight operation exits.
        _lifecycle.Dispose();

        try
        {
            if (_form is { IsDisposed: false, IsHandleCreated: true } && _form.InvokeRequired)
            {
                _form.BeginInvoke((Action)ResetWorkerOnUi);
            }
            else
            {
                ResetWorkerOnUi();
            }
        }
        catch
        {
            // Ignore shutdown disposal races
        }
    }
}
#endif
