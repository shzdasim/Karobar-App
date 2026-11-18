using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Host;

public enum HostLaunchMode
{
    DesktopGui,
    HeadlessCli,
    HeadlessServer
}

public sealed class ParsedCommandLine
{
    public HostLaunchMode Mode { get; init; } = HostLaunchMode.DesktopGui;
    public string? CliCommand { get; init; }
    public string[] CliArguments { get; init; } = [];
    public int? ExplicitPort { get; init; }
    public string? InitialRoute { get; init; }
}

/// <summary>
/// Parses command-line arguments and routes between headless --cli &lt;command&gt; execution,
/// headless Kestrel server mode (--server-only), and normal Native Windows Desktop launch.
/// </summary>
public static class CommandLineRouter
{
    public static ParsedCommandLine Parse(string[]? args)
    {
        args ??= [];
        if (args.Length == 0)
        {
            return new ParsedCommandLine { Mode = HostLaunchMode.DesktopGui };
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i]?.Trim() ?? "";

            // Support `--cli <command> [args...]` or `--cli=<command>` or `artisan <command> [args...]`
            if (string.Equals(arg, "--cli", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "artisan", StringComparison.OrdinalIgnoreCase))
            {
                var cmd = i + 1 < args.Length ? args[i + 1] : "list";
                var rest = i + 2 < args.Length ? args[(i + 2)..] : [];
                return new ParsedCommandLine
                {
                    Mode = HostLaunchMode.HeadlessCli,
                    CliCommand = cmd,
                    CliArguments = [cmd, .. rest]
                };
            }

            if (arg.StartsWith("--cli=", StringComparison.OrdinalIgnoreCase))
            {
                var cmd = arg["--cli=".Length..].Trim();
                var rest = i + 1 < args.Length ? args[(i + 1)..] : [];
                return new ParsedCommandLine
                {
                    Mode = HostLaunchMode.HeadlessCli,
                    CliCommand = cmd,
                    CliArguments = [cmd, .. rest]
                };
            }

            if (string.Equals(arg, "--server-only", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase))
            {
                int? port = null;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
                {
                    port = p;
                }

                return new ParsedCommandLine
                {
                    Mode = HostLaunchMode.HeadlessServer,
                    ExplicitPort = port
                };
            }
        }

        return new ParsedCommandLine
        {
            Mode = HostLaunchMode.DesktopGui
        };
    }

    public static async Task<int> ExecuteHeadlessCliAsync(
        string[] cliArgs,
        string? appRoot = null,
        CancellationToken cancellationToken = default)
    {
        if (cliArgs.Length == 0 || string.IsNullOrWhiteSpace(cliArgs[0]))
        {
            Console.WriteLine("Karobar-App Native CLI Runner");
            return 0;
        }

        var networkConfig = new NetworkConfigManager(appRoot);
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", networkConfig.AppRoot);

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(networkConfig);

        var assemblies = KestrelHostServer.LoadKarobarAssemblies();
        KestrelHostServer.RegisterKarobarAssemblyServices(services, config, assemblies);

        await using var provider = services.BuildServiceProvider();
        var dispatched = await TryExecuteInProcessCliAsync(provider, cliArgs, cancellationToken);
        if (dispatched.Handled)
        {
            return dispatched.ExitCode;
        }

        Console.Error.WriteLine($"Unknown CLI command: {cliArgs[0]}");
        return 1;
    }

    public static async Task<(bool Handled, int ExitCode)> TryExecuteInProcessCliAsync(
        IServiceProvider serviceProvider,
        string[] cliArgs,
        CancellationToken cancellationToken = default)
    {
        if (cliArgs.Length == 0)
        {
            return (false, 1);
        }

        var assemblies = KestrelHostServer.LoadKarobarAssemblies();
        foreach (var asm in assemblies)
        {
            Type? runnerType = null;
            try
            {
                runnerType = asm.GetExportedTypes()
                    .FirstOrDefault(t => t.Name.Equals("KarobarCliCommandRunner", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Ignore
            }

            if (runnerType == null) continue;

            object? runnerInstance = null;
            if (!runnerType.IsAbstract)
            {
                runnerInstance = serviceProvider.GetService(runnerType)
                                 ?? ActivatorUtilities.CreateInstance(serviceProvider, runnerType);
            }

            var methods = runnerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name is "RunAsync" or "ExecuteAsync" or "RunCommandAsync" or "Run" or "Execute")
                .ToList();

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                object?[]? invokeArgs = null;

                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
                {
                    invokeArgs = [cliArgs];
                }
                else if (parameters.Length == 2 &&
                         parameters[0].ParameterType == typeof(string[]) &&
                         parameters[1].ParameterType == typeof(CancellationToken))
                {
                    invokeArgs = [cliArgs, cancellationToken];
                }
                else if (parameters.Length == 2 &&
                         parameters[0].ParameterType == typeof(string) &&
                         parameters[1].ParameterType == typeof(string[]))
                {
                    invokeArgs = [cliArgs[0], cliArgs.Skip(1).ToArray()];
                }
                else if (parameters.Length == 3 &&
                         parameters[0].ParameterType == typeof(string) &&
                         parameters[1].ParameterType == typeof(string[]) &&
                         parameters[2].ParameterType == typeof(CancellationToken))
                {
                    invokeArgs = [cliArgs[0], cliArgs.Skip(1).ToArray(), cancellationToken];
                }

                if (invokeArgs == null) continue;

                var target = method.IsStatic ? null : runnerInstance;
                var rawResult = method.Invoke(target, invokeArgs);

                if (rawResult is Task<int> intTask)
                {
                    return (true, await intTask);
                }

                if (rawResult is Task task)
                {
                    await task;
                    return (true, 0);
                }

                if (rawResult is int code)
                {
                    return (true, code);
                }

                return (true, 0);
            }
        }

        return (false, 1);
    }
}
