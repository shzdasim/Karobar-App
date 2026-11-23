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

    public Task<(bool Ok, string? Engine, string? Error)> PrintAsync(
        string? targetUrl,
        string? targetHtml,
        string printerName,
        bool isThermal,
        CancellationToken cancellationToken = default)
        => PrintAsync(targetUrl, targetHtml, printerName, isThermal, scaleMode: "auto", cancellationToken);

    public async Task<(bool Ok, string? Engine, string? Error)> PrintAsync(
        string? targetUrl,
        string? targetHtml,
        string printerName,
        bool isThermal,
        string scaleMode,
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

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        var result = await MarshalToUiAsync(async () =>
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

                            string? largeHtmlTempFile = null;
                            var effectiveUrl = targetUrl;
                            if (string.IsNullOrWhiteSpace(effectiveUrl) &&
                                !string.IsNullOrWhiteSpace(targetHtml) &&
                                targetHtml.Length > 1_500_000)
                            {
                                // WebView2.NavigateToString has a hard ~2MB limit; spill oversized documents (e.g. huge barcode sheets) to a temp file.
                                largeHtmlTempFile = Path.Combine(
                                    Path.GetTempPath(),
                                    $"karobar-doc-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}.html");
                                await File.WriteAllTextAsync(largeHtmlTempFile, targetHtml, System.Text.Encoding.UTF8, linkedToken);
                                effectiveUrl = new Uri(largeHtmlTempFile).AbsoluteUri;
                            }

                            var navTcs = new TaskCompletionSource<(bool IsSuccess, CoreWebView2WebErrorStatus ErrorStatus)>(
                                TaskCreationOptions.RunContinuationsAsynchronously);
                            ulong expectedNavId = 0;

                            void OnNavStarting(object? s, CoreWebView2NavigationStartingEventArgs args)
                            {
                                if (expectedNavId == 0)
                                {
                                    expectedNavId = args.NavigationId;
                                }
                            }

                            void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs args)
                            {
                                if (expectedNavId != 0 && args.NavigationId != expectedNavId)
                                {
                                    return;
                                }
                                navTcs.TrySetResult((args.IsSuccess, args.WebErrorStatus));
                            }

                            _core.NavigationStarting += OnNavStarting;
                            _core.NavigationCompleted += OnNavCompleted;
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(effectiveUrl))
                                {
                                    _core.Navigate(effectiveUrl);
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

                                var navRes = await navTcs.Task;
                                if (!navRes.IsSuccess)
                                {
                                    return (false, "webview2-silent-print",
                                        $"Failed to load print document (navigation error: {navRes.ErrorStatus}).");
                                }

                                await WaitForDocumentReadyOnUiAsync(_core, linkedToken);

                                if (!string.IsNullOrWhiteSpace(effectiveUrl) &&
                                    Uri.TryCreate(effectiveUrl, UriKind.Absolute, out var pdfUri) &&
                                    pdfUri.IsFile &&
                                    pdfUri.LocalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Give Chromium's PDFium plugin brief settle time before CoreWebView2.PrintAsync
                                    await Task.Delay(350, linkedToken);
                                }

                                return await PrintCoreWebViewAsync(
                                    _core,
                                    printerName,
                                    isThermal,
                                    scaleMode,
                                    _windowManager.NetworkConfig.AppRoot,
                                    linkedToken);
                            }
                            finally
                            {
                                if (_core != null)
                                {
                                    _core.NavigationStarting -= OnNavStarting;
                                    _core.NavigationCompleted -= OnNavCompleted;
                                }
                                await ResetToBlankOnUiAsync();
                                if (!string.IsNullOrWhiteSpace(largeHtmlTempFile))
                                {
                                    try { if (File.Exists(largeHtmlTempFile)) File.Delete(largeHtmlTempFile); } catch { }
                                }
                            }
                        }, linkedToken).ConfigureAwait(false);

                        return result;
                    }
                    catch (Exception ex) when (attempt == 0 && ex is not OperationCanceledException && !_lifecycle.IsDisposed && !linkedToken.IsCancellationRequested)
                    {
                        // Recover automatically from stale/crashed WebView2 process or COM error and retry once
                        try
                        {
                            await MarshalToUiAsync(() =>
                            {
                                ResetWorkerOnUi();
                                return Task.CompletedTask;
                            }, linkedToken).ConfigureAwait(false);
                        }
                        catch { }
                    }
                }

                return (false, "webview2-silent-print", "Offscreen print worker failed after recovery retry.");
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

    private static async Task WaitForDocumentReadyOnUiAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        try
        {
            // ExecuteScriptAsync does not await JS Promises, so poll document.readyState + images synchronously (max ~1.2s)
            const string readyScript =
                "(function(){try{if(document.readyState!=='complete')return 'loading';var imgs=Array.from(document.images||[]);if(imgs.some(function(i){return !i.complete;}))return 'images';return 'ready';}catch(e){return 'ready';}})()";
            var deadline = Environment.TickCount64 + 1200;
            while (Environment.TickCount64 < deadline && !cancellationToken.IsCancellationRequested)
            {
                var state = await core.ExecuteScriptAsync(readyScript);
                if (string.Equals(state, "\"ready\"", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                await Task.Delay(50, cancellationToken);
            }
            await Task.Delay(100, cancellationToken);
        }
        catch
        {
            try { await Task.Delay(120, cancellationToken); } catch { }
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
        bool isThermal,
        string scaleMode = "auto",
        string? appRoot = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveIsThermal = isThermal;
        var effectiveScaleMode = string.IsNullOrWhiteSpace(scaleMode) ? "auto" : scaleMode.Trim();
        var requestedCopies = 1;
        double? paperWidthMm = null;

        // Read <meta name="karobar-print-config"> for ALL silent prints (isThermal, copies, paperWidthMm, scaleMode)
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
                    if (cfgDoc.RootElement.TryGetProperty("isThermal", out var thEl) &&
                        thEl.ValueKind == JsonValueKind.True)
                    {
                        effectiveIsThermal = true;
                    }
                    if (cfgDoc.RootElement.TryGetProperty("paperWidthMm", out var pwEl) &&
                        pwEl.TryGetDouble(out var pwMm) && pwMm is >= 25 and <= 216)
                    {
                        paperWidthMm = pwMm;
                    }
                    if (cfgDoc.RootElement.TryGetProperty("copies", out var cpEl) &&
                        cpEl.TryGetInt32(out var copies) && copies is >= 1 and <= 20)
                    {
                        requestedCopies = copies;
                    }
                    if ((string.IsNullOrWhiteSpace(effectiveScaleMode) || effectiveScaleMode.Equals("auto", StringComparison.OrdinalIgnoreCase)) &&
                        cfgDoc.RootElement.TryGetProperty("scaleMode", out var scEl) &&
                        scEl.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(scEl.GetString()))
                    {
                        effectiveScaleMode = scEl.GetString()!.Trim();
                    }
                }
            }
        }
        catch { }

        var settings = core.Environment.CreatePrintSettings();
        settings.PrinterName = printerName;
        settings.Copies = requestedCopies;
        settings.Orientation = CoreWebView2PrintOrientation.Portrait;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ShouldPrintSelectionOnly = false;
        if (effectiveIsThermal)
        {
            settings.MarginTop = 0;
            settings.MarginBottom = 0;
            settings.MarginLeft = 0;
            settings.MarginRight = 0;
            settings.ScaleFactor = 1.0;
            if (paperWidthMm.HasValue)
            {
                settings.PageWidth = paperWidthMm.Value / 25.4;
            }
        }

        CoreWebView2PrintStatus status = CoreWebView2PrintStatus.OtherError;
        string? primaryException = null;
        try
        {
            status = await core.PrintAsync(settings);
        }
        catch (Exception ex)
        {
            primaryException = ex.Message;
            status = CoreWebView2PrintStatus.OtherError;
        }

        if (status == CoreWebView2PrintStatus.Succeeded)
        {
            return (true, "webview2-silent-print", null);
        }

        // Fallback 1: Retry CoreWebView2.PrintAsync with clean driver-default settings
        // (many Windows thermal & A4 drivers reject explicit 0 margins, custom PageWidth, or ScaleFactor with OtherError or COMException)
        try
        {
            var fallback = core.Environment.CreatePrintSettings();
            fallback.PrinterName = printerName;
            fallback.Copies = requestedCopies;
            fallback.ShouldPrintBackgrounds = true;
            fallback.ShouldPrintHeaderAndFooter = false;
            var fallbackStatus = await core.PrintAsync(fallback);
            if (fallbackStatus == CoreWebView2PrintStatus.Succeeded)
            {
                return (true, "webview2-silent-print-fallback", null);
            }
            status = fallbackStatus;
        }
        catch (Exception ex)
        {
            primaryException ??= ex.Message;
        }

        // Fallback 2: Render via CoreWebView2.PrintToPdfAsync and spool silently through bundled SumatraPDF.exe (GDI driver path)
        var sumatraFallback = await TryPrintViaPdfAndSumatraAsync(
            core,
            printerName,
            effectiveIsThermal,
            paperWidthMm,
            requestedCopies,
            effectiveScaleMode,
            appRoot,
            cancellationToken);
        if (sumatraFallback.Ok)
        {
            return sumatraFallback;
        }

        var reason = status == CoreWebView2PrintStatus.PrinterUnavailable
            ? $"Printer '{printerName}' is offline, disconnected, or unavailable in Windows."
            : !string.IsNullOrWhiteSpace(primaryException)
                ? $"Windows printer '{printerName}' failed to print: {primaryException}"
                : $"Windows printer '{printerName}' failed to print (status: {status}).";
        return (false, "webview2-silent-print", reason);
    }

    private static async Task<(bool Ok, string? Engine, string? Error)> TryPrintViaPdfAndSumatraAsync(
        CoreWebView2 core,
        string printerName,
        bool isThermal,
        double? paperWidthMm,
        int copies,
        string scaleMode,
        string? appRoot,
        CancellationToken cancellationToken)
    {
        var rawPrinter = new Karobar.Printing.Win32RawPrinterService(appRoot);
        var sumatraExe = rawPrinter.ResolveSumatraPdfBinary();
        if (string.IsNullOrWhiteSpace(sumatraExe))
        {
            return (false, "sumatra-fallback", "SumatraPDF.exe is not available.");
        }

        var tempPdfPath = Path.Combine(
            Path.GetTempPath(),
            $"karobar-pdf-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}.pdf");

        try
        {
            var pdfSettings = core.Environment.CreatePrintSettings();
            pdfSettings.ShouldPrintBackgrounds = true;
            pdfSettings.ShouldPrintHeaderAndFooter = false;
            pdfSettings.Orientation = CoreWebView2PrintOrientation.Portrait;
            if (isThermal)
            {
                pdfSettings.MarginTop = 0;
                pdfSettings.MarginBottom = 0;
                pdfSettings.MarginLeft = 0;
                pdfSettings.MarginRight = 0;
                if (paperWidthMm.HasValue)
                {
                    pdfSettings.PageWidth = paperWidthMm.Value / 25.4;
                }
            }

            var pdfOk = await core.PrintToPdfAsync(tempPdfPath, pdfSettings);
            if (!pdfOk || !File.Exists(tempPdfPath))
            {
                return (false, "sumatra-fallback", "WebView2 PrintToPdfAsync did not produce a PDF.");
            }

            var effectiveScale = scaleMode is "noscale" or "fit" or "shrink"
                ? scaleMode
                : (isThermal ? "noscale" : "fit");
            var printSettingsArg = copies > 1 ? $"{effectiveScale},{copies}x" : effectiveScale;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sumatraExe,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-print-to");
            psi.ArgumentList.Add(printerName);
            psi.ArgumentList.Add("-silent");
            psi.ArgumentList.Add("-print-settings");
            psi.ArgumentList.Add(printSettingsArg);
            psi.ArgumentList.Add(tempPdfPath);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                return (false, "sumatra-fallback", "Failed to start SumatraPDF.");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            if (proc.ExitCode == 0)
            {
                return (true, "webview2-pdf-sumatra", null);
            }

            return (false, "sumatra-fallback", string.IsNullOrWhiteSpace(stderr) ? $"SumatraPDF exited with code {proc.ExitCode}." : stderr.Trim());
        }
        catch (Exception ex)
        {
            return (false, "sumatra-fallback", ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPdfPath))
                {
                    File.Delete(tempPdfPath);
                }
            }
            catch { }
        }
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
