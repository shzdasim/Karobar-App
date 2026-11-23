using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karobar.Background;
using Karobar.Host;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karobar.Tests;

public class Milestone3WindowsAndOtaTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "native-win", "installer")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    [Fact]
    public void M3_PruneStaleBuildAssets_RemovesOnlyObsoleteAssets_AndPreservesManifestIndexAndVersion()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar_m3_prune_{Guid.NewGuid():N}");
        var appRoot = Path.Combine(tempDir, "app");
        var stagedRoot = Path.Combine(tempDir, "staged");
        try
        {
            var appPublic = Path.Combine(appRoot, "public");
            var appAssets = Path.Combine(appPublic, "build", "assets");
            var appImages = Path.Combine(appPublic, "images");
            Directory.CreateDirectory(appAssets);
            Directory.CreateDirectory(appImages);

            var indexHtml = Path.Combine(appPublic, "index.html");
            var versionJson = Path.Combine(appPublic, "version.json");
            var manifestJson = Path.Combine(appPublic, "build", "manifest.json");
            var keepJs = Path.Combine(appAssets, "app-NEW123.js");
            var keepCss = Path.Combine(appAssets, "app-NEW123.css");
            var staleJs = Path.Combine(appAssets, "app-OLD999.js");
            var staleCss = Path.Combine(appAssets, "app-OLD999.css");
            var unrelatedImg = Path.Combine(appImages, "logo.png");

            File.WriteAllText(indexHtml, "<html></html>");
            File.WriteAllText(versionJson, "{\"version\":\"2.0.2\"}");
            File.WriteAllText(manifestJson, "{}");
            File.WriteAllText(keepJs, "// new js");
            File.WriteAllText(keepCss, "/* new css */");
            File.WriteAllText(staleJs, "// old js");
            File.WriteAllText(staleCss, "/* old css */");
            File.WriteAllText(unrelatedImg, "png");

            var stagedAssets = Path.Combine(stagedRoot, "public", "build", "assets");
            Directory.CreateDirectory(stagedAssets);
            File.WriteAllText(Path.Combine(stagedAssets, "app-NEW123.js"), "// new js");
            File.WriteAllText(Path.Combine(stagedAssets, "app-NEW123.css"), "/* new css */");

            var pruned = KarobarCliCommandRunner.PruneStaleBuildAssets(appRoot, stagedRoot);

            pruned.Should().Be(2);
            File.Exists(staleJs).Should().BeFalse();
            File.Exists(staleCss).Should().BeFalse();
            File.Exists(keepJs).Should().BeTrue();
            File.Exists(keepCss).Should().BeTrue();
            File.Exists(indexHtml).Should().BeTrue();
            File.Exists(versionJson).Should().BeTrue();
            File.Exists(manifestJson).Should().BeTrue();
            File.Exists(unrelatedImg).Should().BeTrue();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void M3_PruneStaleBuildAssets_NoOpsWhenStageHasNoBuildAssetsOrManifest_AndSupportsManifestOnlyStage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar_m3_prune_noop_{Guid.NewGuid():N}");
        var appRoot = Path.Combine(tempDir, "app");
        var stagedNoAssets = Path.Combine(tempDir, "staged-no-assets");
        var stagedManifestOnly = Path.Combine(tempDir, "staged-manifest-only");
        try
        {
            var appAssets = Path.Combine(appRoot, "public", "build", "assets");
            Directory.CreateDirectory(appAssets);
            var chunkA = Path.Combine(appAssets, "chunk-A.js");
            var chunkB = Path.Combine(appAssets, "chunk-B.js");
            var styleA = Path.Combine(appAssets, "style-A.css");
            File.WriteAllText(chunkA, "a");
            File.WriteAllText(chunkB, "b");
            File.WriteAllText(styleA, "c");

            // Case 1: staged patch touches only version.json (no build/assets or build/manifest.json) -> NO-OP
            Directory.CreateDirectory(Path.Combine(stagedNoAssets, "public"));
            File.WriteAllText(Path.Combine(stagedNoAssets, "public", "version.json"), "{}");

            var noopCount = KarobarCliCommandRunner.PruneStaleBuildAssets(appRoot, stagedNoAssets);
            noopCount.Should().Be(0);
            File.Exists(chunkA).Should().BeTrue();
            File.Exists(chunkB).Should().BeTrue();
            File.Exists(styleA).Should().BeTrue();

            // Case 2: staged patch has build/manifest.json referencing chunk-A.js and style-A.css -> prunes chunk-B.js
            Directory.CreateDirectory(Path.Combine(stagedManifestOnly, "public", "build"));
            File.WriteAllText(
                Path.Combine(stagedManifestOnly, "public", "build", "manifest.json"),
                """
                {
                  "resources/js/app.jsx": {
                    "file": "assets/chunk-A.js",
                    "css": ["assets/style-A.css"]
                  }
                }
                """);

            var manifestPruned = KarobarCliCommandRunner.PruneStaleBuildAssets(appRoot, stagedManifestOnly);
            manifestPruned.Should().Be(1);
            File.Exists(chunkA).Should().BeTrue();
            File.Exists(styleA).Should().BeTrue();
            File.Exists(chunkB).Should().BeFalse();

            // Case 3: manifest-only stage with non-object ("[]", "null") or malformed JSON fails safe without pruning
            foreach (var badManifest in new[] { "[]", "null", "{not_valid_json", "" })
            {
                File.WriteAllText(Path.Combine(stagedManifestOnly, "public", "build", "manifest.json"), badManifest);
                var safePruned = KarobarCliCommandRunner.PruneStaleBuildAssets(appRoot, stagedManifestOnly);
                safePruned.Should().Be(0, $"manifest '{badManifest}' should fail safe without pruning existing assets");
                File.Exists(chunkA).Should().BeTrue();
                File.Exists(styleA).Should().BeTrue();
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task M3_ExecuteApplyPatchAsync_PrunesStaleHashedAssets_AndPreservesCorePublicFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar_m3_patch_e2e_{Guid.NewGuid():N}");
        var appRoot = Path.Combine(tempDir, "app");
        var patchSrc = Path.Combine(tempDir, "patch-src");
        var patchZip = Path.Combine(tempDir, "Karobar-Native-Patch.zip");
        try
        {
            var appPublic = Path.Combine(appRoot, "public");
            var appAssets = Path.Combine(appPublic, "build", "assets");
            Directory.CreateDirectory(appAssets);
            File.WriteAllText(Path.Combine(appPublic, "index.html"), "<html>existing</html>");
            File.WriteAllText(Path.Combine(appPublic, "version.json"), "{\"version\":\"2.0.1\"}");
            File.WriteAllText(Path.Combine(appPublic, "build", "manifest.json"), "{\"old\":true}");
            var staleJs = Path.Combine(appAssets, "index-STALE001.js");
            var staleCss = Path.Combine(appAssets, "index-STALE001.css");
            File.WriteAllText(staleJs, "stale");
            File.WriteAllText(staleCss, "stale");

            var patchPublic = Path.Combine(patchSrc, "public");
            var patchAssets = Path.Combine(patchPublic, "build", "assets");
            Directory.CreateDirectory(patchAssets);
            File.WriteAllText(Path.Combine(patchPublic, "version.json"), "{\"version\":\"2.0.2\"}");
            File.WriteAllText(
                Path.Combine(patchPublic, "build", "manifest.json"),
                "{\"resources/js/app.jsx\":{\"file\":\"assets/index-FRESH002.js\",\"css\":[\"assets/index-FRESH002.css\"]}}");
            File.WriteAllText(Path.Combine(patchAssets, "index-FRESH002.js"), "fresh-js");
            File.WriteAllText(Path.Combine(patchAssets, "index-FRESH002.css"), "fresh-css");

            ZipFile.CreateFromDirectory(patchSrc, patchZip);
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(patchZip)));

            var runner = new KarobarCliCommandRunner(appRoot);
            var res = await runner.ExecuteApplyPatchAsync(new Dictionary<string, string?>
            {
                ["patch_zip"] = patchZip,
                ["verify-sha"] = sha256
            });

            res.ExitCode.Should().Be(0);
            res.Data["ok"].Should().Be(true);
            res.Data["pruned_assets"].Should().Be(2);

            File.Exists(staleJs).Should().BeFalse();
            File.Exists(staleCss).Should().BeFalse();
            File.Exists(Path.Combine(appAssets, "index-FRESH002.js")).Should().BeTrue();
            File.Exists(Path.Combine(appAssets, "index-FRESH002.css")).Should().BeTrue();
            File.Exists(Path.Combine(appPublic, "index.html")).Should().BeTrue();
            File.ReadAllText(Path.Combine(appPublic, "version.json")).Should().Contain("2.0.2");
            File.ReadAllText(Path.Combine(appPublic, "build", "manifest.json")).Should().Contain("FRESH002");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task M3_CommandLineRouter_And_Precheck_AcceptCliFlag_AndRejectArtisanAlias()
    {
        var cliSpace = CommandLineRouter.Parse(["--cli", "license:machine-id", "--out=id.txt"]);
        cliSpace.Mode.Should().Be(HostLaunchMode.HeadlessCli);
        cliSpace.CliCommand.Should().Be("license:machine-id");
        cliSpace.CliArguments.Should().Equal("license:machine-id", "--out=id.txt");

        var cliEquals = CommandLineRouter.Parse(["--cli=install", "--upgrade-only"]);
        cliEquals.Mode.Should().Be(HostLaunchMode.HeadlessCli);
        cliEquals.CliCommand.Should().Be("install");
        cliEquals.CliArguments.Should().Equal("install", "--upgrade-only");

        var artisan = CommandLineRouter.Parse(["artisan", "demands:notify"]);
        artisan.Mode.Should().Be(HostLaunchMode.DesktopGui);
        artisan.CliCommand.Should().BeNull();

        var repoRoot = FindRepoRoot();
        var precheckProgram = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Precheck", "Program.cs"));
        precheckProgram.Should().Contain("\"--cli\"");
        precheckProgram.Should().Contain("\"--cli=\"");
        precheckProgram.Should().NotContain("\"artisan\"");

        var hostProgram = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "Program.cs"));
        hostProgram.Should().NotContain("artisan");

        var precheckDllCandidates = new[]
        {
            Path.Combine(repoRoot, "native-win", "src", "Karobar.Precheck", "bin", "Release", "net9.0", "Karobar-Precheck.dll"),
            Path.Combine(repoRoot, "native-win", "src", "Karobar.Precheck", "bin", "Debug", "net9.0", "Karobar-Precheck.dll")
        };
        var precheckDll = precheckDllCandidates.FirstOrDefault(File.Exists);
        if (precheckDll != null)
        {
            var dotnetExe = ResolveDotnetHostExecutable();
            var tempMidFile = Path.Combine(Path.GetTempPath(), $"karobar_m3_mid_{Guid.NewGuid():N}.txt");
            try
            {
                var okPsi = new ProcessStartInfo
                {
                    FileName = dotnetExe,
                    Arguments = $"\"{precheckDll}\" --cli license:machine-id --out=\"{tempMidFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                okPsi.Environment["DOTNET_ROLL_FORWARD"] = "Major";
                using var okProc = Process.Start(okPsi)!;
                var okOutTask = okProc.StandardOutput.ReadToEndAsync();
                var okErrTask = okProc.StandardError.ReadToEndAsync();
                okProc.WaitForExit(15000).Should().BeTrue();
                var okOut = await okOutTask;
                var okErr = await okErrTask;
                okProc.ExitCode.Should().Be(0, okErr);
                okOut.Trim().Should().StartWith("KRB-");
                File.Exists(tempMidFile).Should().BeTrue();
                (await File.ReadAllTextAsync(tempMidFile)).Trim().Should().StartWith("KRB-");

                var rejPsi = new ProcessStartInfo
                {
                    FileName = dotnetExe,
                    Arguments = $"\"{precheckDll}\" artisan license:machine-id",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                rejPsi.Environment["DOTNET_ROLL_FORWARD"] = "Major";
                using var rejProc = Process.Start(rejPsi)!;
                var rejOutTask = rejProc.StandardOutput.ReadToEndAsync();
                var rejErrTask = rejProc.StandardError.ReadToEndAsync();
                rejProc.WaitForExit(15000).Should().BeTrue();
                await rejOutTask;
                await rejErrTask;
                rejProc.ExitCode.Should().NotBe(0);
            }
            finally
            {
                try { if (File.Exists(tempMidFile)) File.Delete(tempMidFile); } catch { }
            }
        }
    }

    private static string ResolveDotnetHostExecutable()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) &&
            string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }
        if (File.Exists("/usr/local/share/dotnet/dotnet"))
        {
            return "/usr/local/share/dotnet/dotnet";
        }
        return "dotnet";
    }

    [Fact]
    public void M3_WindowsInstallerAndBuildScript_Meet_MED7_MED9_MED10_M4_5_M4_6_StaticInvariants()
    {
        var repoRoot = FindRepoRoot();

        // 1. Karobar-Native-Setup.iss (MED-7, M4-5, MED-9 [InstallDelete])
        var iss = File.ReadAllText(Path.Combine(repoRoot, "native-win", "installer", "Karobar-Native-Setup.iss"));
        iss.Should().Contain("[InstallDelete]");
        iss.Should().Contain(@"Name: ""{app}\public\build\assets\*""");
        iss.Should().Contain("ResultCode = 3010");
        iss.Should().Contain("NeedsRestart := True");
        iss.Should().Contain("IsWebView2RuntimeInstalled()");
        iss.Should().Contain("{param:OTAFLAG|}");
        iss.Should().Contain("CleanupOtaUpgradeFlags");

        // 2. KarobarDesktopBridge.cs (MED-9 & M4-5)
        var bridge = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "KarobarDesktopBridge.cs"));
        bridge.Should().Contain("Remove-Item -Path $PSCommandPath -Force");
        bridge.Should().Contain(@"public\build\assets");
        bridge.Should().Contain("$deletedRels");
        bridge.Should().Contain("$createdRels");
        bridge.Should().Contain("$manifestValid = $false");
        bridge.Should().Contain("$trimmedManifest.StartsWith('{')");
        bridge.Should().Contain("-is [pscustomobject]");
        bridge.Should().Contain("if ($hasStageAssets -or $manifestValid)");
        bridge.Should().Contain("if ($upgProc.ExitCode -ne 0)");
        bridge.Should().Contain("throw \"Post-patch schema/upgrade command exited with code $($upgProc.ExitCode).\"");
        bridge.Should().Contain("/OTAFLAG=$otaFlagPath");
        bridge.Should().Contain("-Wait -PassThru");
        bridge.Should().Contain("Remove-Item -Path $otaFlagPath -Force");

        // 3. build-native-win.ps1 (MED-10 & M4-6)
        var ps1 = File.ReadAllText(Path.Combine(repoRoot, "native-win", "build-native-win.ps1"));
        ps1.Should().Contain("[switch]$AllowUnsigned");
        ps1.Should().Contain("$env:CI -eq \"true\" -or $env:GITHUB_ACTIONS -eq \"true\"");
        ps1.Should().Contain("$AllowUnsigned = $false");
        ps1.Should().Contain("try {");
        ps1.Should().Contain("} finally {");
        ps1.Should().Contain("Remove-Item -Path $PfxPath -Force");
        ps1.Should().Contain(@"Cert:\CurrentUser\My\");
        ps1.Should().NotContain("Invoke-KarobarAuthenticodeSign $PublishedHostExe");
    }

    [Fact]
    public void M3_WindowsHostAndFrontend_Meet_MED8_M4_7_M4_8_M4_9_StaticInvariants()
    {
        var repoRoot = FindRepoRoot();

        // 1. NativeSplashForm & GuideBot handler cleanup (MED-8 & M4-7)
        var formCs = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "NativeWebView2Form.cs"));
        formCs.Should().Contain("Size = new Size(480, 310);");
        formCs.Should().Contain("DoubleBuffered = true;");
        formCs.Should().Contain("ControlStyles.OptimizedDoubleBuffer");
        formCs.Should().Contain("Path.ChangeExtension(iconPath, \".png\")");
        formCs.Should().Contain("\"desktop\", \"icon.png\"");
        formCs.Should().Contain("\"assets\", \"icon.png\"");
        formCs.Should().Contain("ToBitmap()");
        formCs.Should().Contain("LinearGradientBrush");
        formCs.Should().Contain("Initializing local database & secure runtime…");
        formCs.Should().Contain("Karobar-App Desktop Edition • Inventory • POS • Accounting");
        formCs.Should().Contain("_animationTimer.Dispose()");
        formCs.Should().Contain("using var prevClip = g.Clip;");
        formCs.Should().Contain("_desktopBridge.OnToggleGuideBot = null;");
        formCs.Should().Contain("_desktopBridge.OnOpenGuideBot = null;");

        // 2. OffscreenPrintWorker SemaphoreSlim deferred disposal (M4-8)
        var printCs = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "OffscreenPrintWorker.cs"));
        printCs.Should().Contain("DeferredSemaphoreDisposalCoordinator");
        printCs.Should().Contain("TryEnterOperation");
        printCs.Should().Contain("ExitOperation");
        printCs.Should().Contain("Interlocked.Exchange(ref _disposeStarted, 1)");
        printCs.Should().Contain("_lifecycle.Dispose()");
        printCs.Should().Contain("Interlocked.CompareExchange(ref state, 2, 0)");
        printCs.Should().NotContain("_initLock.Dispose()");
        printCs.Should().NotContain("_printMutex.Dispose()");
        printCs.Should().NotContain("_disposeCts");
        printCs.Should().NotContain("TryCreateLinkedDisposeCts");

        // 3. Frontend labels (M4-9)
        var layoutJsx = File.ReadAllText(Path.Combine(repoRoot, "resources", "js", "layouts", "DashboardLayout.jsx"));
        layoutJsx.Should().Contain("Server & Attached Terminals Dashboard");
        layoutJsx.Should().NotContain("Linux Server & Attached Terminals Dashboard");

        var serverDashboardJsx = File.ReadAllText(Path.Combine(repoRoot, "resources", "js", "pages", "ServerDashboard.jsx"));
        serverDashboardJsx.Should().Contain("Server &amp; Attached Terminals Dashboard");
        serverDashboardJsx.Should().NotContain("Linux Server &amp; Attached Terminals Dashboard");
    }

    [Fact]
    public async Task M3_DeferredSemaphoreDisposalCoordinator_ConcurrentWaitAsyncAndDispose_NeverHangs()
    {
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var initLock = new SemaphoreSlim(1, 1);
            var printMutex = new SemaphoreSlim(1, 1);
            var coord = new DeferredSemaphoreDisposalCoordinator(initLock, printMutex);

            var holderAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseHolder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var objectDisposedEscaped = 0;

            var holderTask = Task.Run(async () =>
            {
                if (!coord.TryEnterOperation(CancellationToken.None, out var holderCts) || holderCts == null)
                {
                    holderAcquired.TrySetResult();
                    return;
                }

                var printAcquired = false;
                var initAcquired = false;
                try
                {
                    using (holderCts)
                    {
                        await printMutex.WaitAsync(holderCts.Token).ConfigureAwait(false);
                        printAcquired = true;
                        if ((iteration & 1) == 1)
                        {
                            await initLock.WaitAsync(holderCts.Token).ConfigureAwait(false);
                            initAcquired = true;
                        }
                        holderAcquired.TrySetResult();
                        await releaseHolder.Task.ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                    Interlocked.Increment(ref objectDisposedEscaped);
                }
                finally
                {
                    if (initAcquired) initLock.Release();
                    if (printAcquired) printMutex.Release();
                    coord.ExitOperation();
                }
            });

            await holderAcquired.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var waiterTasks = new List<Task>(4);
            for (var w = 0; w < 4; w++)
            {
                var nested = (w & 1) == 1;
                waiterTasks.Add(Task.Run(async () =>
                {
                    await startBarrier.Task.ConfigureAwait(false);
                    if (!coord.TryEnterOperation(CancellationToken.None, out var linkedCts) || linkedCts == null)
                    {
                        return;
                    }

                    var printAcquired = false;
                    try
                    {
                        using (linkedCts)
                        {
                            var token = linkedCts.Token;
                            await printMutex.WaitAsync(token).ConfigureAwait(false);
                            printAcquired = true;

                            if (nested)
                            {
                                var initAcquired = false;
                                try
                                {
                                    await initLock.WaitAsync(token).ConfigureAwait(false);
                                    initAcquired = true;
                                }
                                finally
                                {
                                    if (initAcquired) initLock.Release();
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (ObjectDisposedException)
                    {
                        Interlocked.Increment(ref objectDisposedEscaped);
                    }
                    finally
                    {
                        if (printAcquired) printMutex.Release();
                        coord.ExitOperation();
                    }
                }));
            }

            // 0ms gap race: release start barrier, dispose coordinator, and release holder concurrently
            startBarrier.TrySetResult();
            var disposeTask = Task.Run(() => coord.Dispose());
            var releaseTask = Task.Run(() => releaseHolder.TrySetResult());

            var allOps = Task.WhenAll(waiterTasks.Append(holderTask).Append(disposeTask).Append(releaseTask));
            var completed = await Task.WhenAny(allOps, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().BeSameAs(allOps, $"iteration {iteration} hung during concurrent WaitAsync + Dispose");
            await allOps;

            Volatile.Read(ref objectDisposedEscaped).Should().Be(0, $"iteration {iteration} leaked ObjectDisposedException");
            coord.IsDisposed.Should().BeTrue();
            coord.AreResourcesDisposed.Should().BeTrue();
            coord.TryEnterOperation(CancellationToken.None, out var afterCts).Should().BeFalse();
            afterCts.Should().BeNull();
        }
    }

    [Fact]
    public async Task M3_DeferredSemaphoreDisposalCoordinator_MultiWaiterQueued_WakesImmediatelyAndDefersGateDisposal()
    {
        var initLock = new SemaphoreSlim(1, 1);
        var printMutex = new SemaphoreSlim(1, 1);
        var coord = new DeferredSemaphoreDisposalCoordinator(initLock, printMutex);

        coord.TryEnterOperation(CancellationToken.None, out var holderCts).Should().BeTrue();
        holderCts.Should().NotBeNull();

        await printMutex.WaitAsync(holderCts!.Token);
        await initLock.WaitAsync(holderCts.Token);

        const int waiterCount = 12;
        var enteredWaitCount = 0;
        var allEnteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledFlags = new int[waiterCount];
        var objectDisposedFlags = new int[waiterCount];

        var waiterTasks = Enumerable.Range(0, waiterCount).Select(i => Task.Run(async () =>
        {
            if (!coord.TryEnterOperation(CancellationToken.None, out var linkedCts) || linkedCts == null)
            {
                return;
            }

            var printAcquired = false;
            var initAcquired = false;
            try
            {
                using (linkedCts)
                {
                    if (Interlocked.Increment(ref enteredWaitCount) == waiterCount)
                    {
                        allEnteredTcs.TrySetResult();
                    }

                    if ((i & 1) == 0)
                    {
                        await printMutex.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                        printAcquired = true;
                    }
                    else
                    {
                        // Also exercise waiters suspended directly on _initLock (PrewarmAsync / nested pattern)
                        await initLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                        initAcquired = true;
                        await printMutex.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                        printAcquired = true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Volatile.Write(ref cancelledFlags[i], 1);
            }
            catch (ObjectDisposedException)
            {
                Volatile.Write(ref objectDisposedFlags[i], 1);
            }
            finally
            {
                if (initAcquired) initLock.Release();
                if (printAcquired) printMutex.Release();
                coord.ExitOperation();
            }
        })).ToArray();

        await allEnteredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Allow queued waiter tasks to park in SemaphoreSlim.WaitUntilCountOrTimeoutAsync
        await Task.Delay(50);

        // Dispose coordinator WHILE holder still holds both gates
        coord.Dispose();

        var allWaiters = Task.WhenAll(waiterTasks);
        var wokeInTime = await Task.WhenAny(allWaiters, Task.Delay(TimeSpan.FromSeconds(2)));
        wokeInTime.Should().BeSameAs(allWaiters, "all 12 queued waiters must wake immediately on Dispose while holder still holds the gates");
        await allWaiters;

        cancelledFlags.Should().AllBeEquivalentTo(1);
        objectDisposedFlags.Should().AllBeEquivalentTo(0);

        // Gates MUST NOT be disposed until holder exits
        coord.IsDisposed.Should().BeTrue();
        coord.AreResourcesDisposed.Should().BeFalse();

        // Now holder releases gates, disposes linked CTS, and exits coordinator
        initLock.Release();
        printMutex.Release();
        holderCts.Dispose();
        coord.ExitOperation();

        coord.AreResourcesDisposed.Should().BeTrue();
        Action waitInit = () => initLock.Wait(0);
        Action waitPrint = () => printMutex.Wait(0);
        waitInit.Should().Throw<ObjectDisposedException>();
        waitPrint.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task M3_DeferredSemaphoreDisposalCoordinator_IdleAndConcurrentDoubleDispose_IsSafeAndIdempotent()
    {
        // 1. Idle Dispose immediately disposes managed semaphores
        var idleInit = new SemaphoreSlim(1, 1);
        var idlePrint = new SemaphoreSlim(1, 1);
        var idleCoord = new DeferredSemaphoreDisposalCoordinator(idleInit, idlePrint);

        idleCoord.IsDisposed.Should().BeFalse();
        idleCoord.AreResourcesDisposed.Should().BeFalse();

        idleCoord.Dispose();
        idleCoord.Dispose(); // Idempotent repeat

        idleCoord.IsDisposed.Should().BeTrue();
        idleCoord.AreResourcesDisposed.Should().BeTrue();
        idleCoord.TryEnterOperation(CancellationToken.None, out var idleCts).Should().BeFalse();
        idleCts.Should().BeNull();
        Action idleWaitInit = () => idleInit.Wait(0);
        Action idleWaitPrint = () => idlePrint.Wait(0);
        idleWaitInit.Should().Throw<ObjectDisposedException>();
        idleWaitPrint.Should().Throw<ObjectDisposedException>();

        // 2. 16-way concurrent Dispose never throws or double-disposes
        var concInit = new SemaphoreSlim(1, 1);
        var concPrint = new SemaphoreSlim(1, 1);
        var concCoord = new DeferredSemaphoreDisposalCoordinator(concInit, concPrint);
        using var go = new ManualResetEventSlim(false);

        var disposeTasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() =>
            {
                go.Wait();
                concCoord.Dispose();
            }))
            .ToArray();

        go.Set();
        var allDisposed = Task.WhenAll(disposeTasks);
        var done = await Task.WhenAny(allDisposed, Task.Delay(TimeSpan.FromSeconds(2)));
        done.Should().BeSameAs(allDisposed);
        await allDisposed;

        concCoord.IsDisposed.Should().BeTrue();
        concCoord.AreResourcesDisposed.Should().BeTrue();
    }

    [Fact]
    public void M3_OtaManifestParser_HandlesBom_NewerBuild_SameBuild_And_GitHubDigestSha256()
    {
        var patchSha = new string('a', 64);
        var installerSha = new string('b', 64);
        var local = new LocalVersionMeta
        {
            AppName = "Karobar-App Native",
            Version = "2.0.48",
            BuildNumber = 48,
            CommitSha = "old1234",
            Repository = "shzdasim/Karobar-App",
            RuntimeLayout = "split-v2",
            DepsFingerprint = "deadbeef"
        };

        // 1. BOM-prefixed manifest with newer build must be parsed cleanly and preserve SHA-256 + patch eligibility
        var bomManifest = "\uFEFF" + $$"""
        {
          "app_name": "Karobar-App Native",
          "version": "2.0.50",
          "build_number": 50,
          "commit_sha": "5364aa6",
          "release_name": "Karobar-App Native v2.0.50 (Build #50)",
          "released_at": "2026-11-23T04:20:23Z",
          "notes": "Print and update fixes",
          "runtime_layout": "split-v2",
          "deps_fingerprint": "deadbeef",
          "compatible_deps_fingerprints": ["deadbeef"],
          "download_url": "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Setup.exe",
          "installer_size": 131241184,
          "installer_sha256": "{{installerSha}}",
          "patch_url": "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Patch.zip",
          "patch_size": 4573507,
          "patch_sha256": "{{patchSha}}",
          "min_patch_version": "2.0.0"
        }
        """;

        var newerObj = KarobarDesktopBridge.BuildUpdateResultFromManifest(local, "shzdasim/Karobar-App", bomManifest, hasBundledWaRuntime: true);
        newerObj.Should().NotBeNull();
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(newerObj)))
        {
            var root = doc.RootElement;
            root.GetProperty("ok").GetBoolean().Should().BeTrue();
            root.GetProperty("update_available").GetBoolean().Should().BeTrue();
            root.GetProperty("is_patch").GetBoolean().Should().BeTrue();
            root.GetProperty("latest_version").GetString().Should().Be("2.0.50");
            root.GetProperty("latest_build").GetInt32().Should().Be(50);
            root.GetProperty("patch_sha256").GetString().Should().Be(patchSha);
            root.GetProperty("installer_sha256").GetString().Should().Be(installerSha);
        }

        // 2. Same version & build must report up-to-date
        local.Version = "2.0.50";
        local.BuildNumber = 50;
        var sameObj = KarobarDesktopBridge.BuildUpdateResultFromManifest(local, "shzdasim/Karobar-App", bomManifest, hasBundledWaRuntime: true);
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(sameObj)))
        {
            doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("update_available").GetBoolean().Should().BeFalse();
        }

        // 3. GitHub release asset digest extraction (`sha256:<64-hex>`)
        KarobarDesktopBridge.ExtractSha256FromGitHubDigest($"sha256:{installerSha.ToUpperInvariant()}")
            .Should().Be(installerSha);
        KarobarDesktopBridge.ExtractSha256FromGitHubDigest("sha256:short").Should().BeNull();
        KarobarDesktopBridge.ExtractSha256FromGitHubDigest(null).Should().BeNull();
    }

    [Fact]
    public async Task M3_SystemUpdateController_WithoutBridge_DoesNotReturnFakeOkUpToDate()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m3_upd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "public"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app"));
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempRoot, "public", "version.json"),
                """{"app_name":"Karobar-App Native","version":"2.0.48","build_number":48}""");
            var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            try
            {
                var db = new Karobar.Services.Infrastructure.KarobarDb(
                    explicitSqlitePath: Path.Combine(tempRoot, "database", "database.sqlite"));
                var controller = new Karobar.Api.Controllers.SystemUpdateController(db)
                {
                    ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
                    {
                        HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                        {
                            RequestServices = new ServiceCollection().BuildServiceProvider()
                        }
                    }
                };

                var actionResult = await controller.CheckUpdate("1");
                var okResult = actionResult.Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>().Subject;
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
                doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse(
                    "controller must return ok=false when desktop bridge is absent so the SPA falls back to GitHub Releases");
                doc.RootElement.GetProperty("offline_fallback").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("current_version").GetString().Should().Be("2.0.48");
            }
            finally
            {
                Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            }
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
}
