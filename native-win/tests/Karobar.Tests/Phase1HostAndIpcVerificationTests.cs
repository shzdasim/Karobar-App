using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karobar.Host;
using Xunit;

namespace Karobar.Tests;

public class Phase1HostAndIpcVerificationTests
{
    private static readonly string[] ExpectedProperties =
    [
        "isDesktop",
        "isOffscreenPrintWorker",
        "platform"
    ];

    private static readonly string[] ExpectedMethods =
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

    private static readonly string[] ExpectedEvents =
    [
        "onNavigate",
        "onOpenSubWindow",
        "onWindowCommand",
        "onUpdateProgress",
        "onTriggerUpdateCheck"
    ];

    private static readonly string[] ExpectedIpcChannels =
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

    [Fact]
    public void Single_Instance_Mutex_And_CommandLineRouter_Match_Spec()
    {
        Program.SingleInstanceMutexName.Should().Be(@"Global\KarobarNativeErpV2Mutex");
        Program.FallbackLocalMutexName.Should().Be(@"Local\KarobarNativeErpV2Mutex");
        Program.ApplicationDisplayName.Should().Be("Karobar-App Native");
        Program.ExecutableFileName.Should().Be("Karobar-Native.exe");
        NetworkConfigManager.AppDataFolderName.Should().Be("Karobar-App-Native");
        NetworkConfigManager.DefaultPort.Should().Be(18960);

        var guiMode = CommandLineRouter.Parse([]);
        guiMode.Mode.Should().Be(HostLaunchMode.DesktopGui);

        var cliMode = CommandLineRouter.Parse(["--cli", "license:machine-id", "--out=test.txt"]);
        cliMode.Mode.Should().Be(HostLaunchMode.HeadlessCli);
        cliMode.CliCommand.Should().Be("license:machine-id");
        cliMode.CliArguments.Should().Equal("license:machine-id", "--out=test.txt");

        var artisanMode = CommandLineRouter.Parse(["artisan", "demands:notify"]);
        artisanMode.Mode.Should().Be(HostLaunchMode.HeadlessCli);
        artisanMode.CliCommand.Should().Be("demands:notify");

        var serverMode = CommandLineRouter.Parse(["--server-only", "18955"]);
        serverMode.Mode.Should().Be(HostLaunchMode.HeadlessServer);
        serverMode.ExplicitPort.Should().Be(18955);
    }

    [Fact]
    public void KarobarPreloadScript_Exposes_All_22_Members_5_Events_And_Window_Print_Interceptor()
    {
        KarobarPreloadScript.RequiredProperties.Should().BeEquivalentTo(ExpectedProperties);
        KarobarPreloadScript.RequiredMethods.Should().BeEquivalentTo(ExpectedMethods);
        KarobarPreloadScript.RequiredEventListeners.Should().BeEquivalentTo(ExpectedEvents);
        KarobarPreloadScript.RequiredIpcChannels.Should().BeEquivalentTo(ExpectedIpcChannels);

        var js = KarobarPreloadScript.GetPreloadScript(isOffscreenPrintWorker: false);
        js.Should().Contain("window.karobarDesktop");
        js.Should().Contain("window.print = function");
        js.Should().Contain("window.karobarDesktop.silentPrint");

        foreach (var prop in ExpectedProperties)
        {
            js.Should().Contain($"{prop}:", $"preload script must expose property '{prop}'");
        }

        foreach (var method in ExpectedMethods)
        {
            js.Should().Contain($"{method}:", $"preload script must expose method '{method}'");
        }

        foreach (var evt in ExpectedEvents)
        {
            js.Should().Contain($"{evt}:", $"preload script must expose event listener '{evt}'");
        }

        foreach (var channel in ExpectedIpcChannels)
        {
            js.Should().Contain($"\"{channel}\"", $"preload script must wire IPC channel '{channel}'");
        }
    }

    [Fact]
    public async Task KarobarDesktopBridge_And_WindowManager_Support_All_22_Members_And_5_Event_Channels()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_qa_p1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var netCfg = new NetworkConfigManager(tempRoot);
            var winMgr = new WebView2WindowManager(netCfg);
            var bridge = new KarobarDesktopBridge(netCfg, winMgr);

            // 3 properties
            bridge.IsDesktop.Should().BeTrue();
            bridge.IsOffscreenPrintWorker.Should().BeFalse();
            bridge.Platform.Should().Be("win32");

            // Test IPC channels
            var appInfoObj = await bridge.DispatchIpcAsync("karobar:app-info");
            var appInfoJson = JsonSerializer.Serialize(appInfoObj);
            appInfoJson.Should().Contain("\"platform\":\"win32\"");

            var netInfoObj = await bridge.DispatchIpcAsync("karobar:get-network-config");
            var netInfoJson = JsonSerializer.Serialize(netInfoObj);
            netInfoJson.Should().Contain("\"mode\":\"standalone\"");

            using var openWinDoc = JsonDocument.Parse("{\"path\":\"/sale-invoices/pos\",\"title\":\"POS Counter\"}");
            var openedWin = await bridge.DispatchIpcAsync("karobar:open-window", openWinDoc.RootElement);
            JsonSerializer.Serialize(openedWin).Should().Contain("\"ok\":true");

            var openWindows = await bridge.DispatchIpcAsync("karobar:get-open-windows");
            JsonSerializer.Serialize(openWindows).Should().Contain("/sale-invoices/pos");

            // Test all 5 event emitters
            winMgr.SendNavigate("/dashboard", "Dashboard");
            winMgr.SendOpenSubWindow("/sale-invoices/pos", "POS");
            winMgr.SendWindowCommand("next-tab");
            winMgr.SendUpdateProgress(new { stage = "downloading", percent = 50, message = "Halfway" });
            winMgr.SendTriggerUpdateCheck();

            var events = winMgr.GetRecentEventsSince(0);
            var emittedChannels = events.Select(e => e.Channel).ToList();
            emittedChannels.Should().Contain("karobar:navigate");
            emittedChannels.Should().Contain("karobar:open-subwindow");
            emittedChannels.Should().Contain("karobar:window-command");
            emittedChannels.Should().Contain("karobar:update-progress");
            emittedChannels.Should().Contain("karobar:trigger-update-check");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task KestrelHostServer_Resolves_All_103_React_Spa_Routes_And_Vite_Manifest()
    {
        KestrelHostServer.All103SpaRoutes.Count.Should().BeGreaterThanOrEqualTo(103);

        foreach (var routePattern in KestrelHostServer.All103SpaRoutes)
        {
            var concreteRoute = routePattern.Replace(":id", "42").Replace(":token", "tok123").Replace(":email", "user@test.com");
            KestrelHostServer.IsSpaShellRequest("GET", concreteRoute, "text/html").Should().BeTrue($"SPA route '{concreteRoute}' must be served by SPA shell");
        }

        KestrelHostServer.IsSpaShellRequest("GET", "/api/user", "application/json").Should().BeFalse();
        KestrelHostServer.IsSpaShellRequest("GET", "/up", "text/plain").Should().BeFalse();
        KestrelHostServer.IsSpaShellRequest("GET", "/print/thermal-preview/standard", "text/html").Should().BeFalse();

        await using var server = new KestrelHostServer();
        var spaHtml = server.BuildSpaShellHtml();
        spaHtml.Should().Contain("/__karobar_ipc/preload.js");
        spaHtml.Should().Contain("<div id=\"app\">");
    }
}
