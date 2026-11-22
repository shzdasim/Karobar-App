using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Karobar.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Karobar.Background;

/// <summary>
/// Background Cron Scheduler Hosted Service (`routes/console.php`).
/// Executes all 4 scheduled jobs automatically in the background:
/// 1. `demands:notify` — Daily at 09:00
/// 2. `backup:run-scheduled` — Hourly
/// 3. `cleanupExpiredSharedPdfs` — Daily
/// 4. `expenses:process-recurring` — Daily at 00:05
/// Plus periodic FBR offline queue auto-retry.
/// </summary>
public sealed class KarobarSchedulerHostedService : BackgroundService
{
    public static readonly string[] ScheduledJobNames =
    [
        "demands:notify",
        "backup:run-scheduled",
        "cleanupExpiredSharedPdfs",
        "expenses:process-recurring"
    ];

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KarobarSchedulerHostedService>? _logger;
    private readonly string _appRoot;
    private readonly KarobarCliCommandRunner _cliRunner;
    private readonly FbrFiscalizationEngine _fbrEngine;

    private DateTime _lastDemandsNotifyDate = DateTime.MinValue;
    private DateTime _lastRecurringExpensesDate = DateTime.MinValue;
    private DateTime _lastSharedPdfCleanupDate = DateTime.MinValue;
    private (DateTime Date, int Hour) _lastScheduledBackupSlot = (DateTime.MinValue, -1);

    public KarobarSchedulerHostedService(
        IServiceProvider serviceProvider,
        ILogger<KarobarSchedulerHostedService>? logger = null,
        string? appRoot = null,
        KarobarCliCommandRunner? cliRunner = null,
        FbrFiscalizationEngine? fbrEngine = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _appRoot = ResolveAppRoot(appRoot);
        _cliRunner = cliRunner ?? new KarobarCliCommandRunner(_appRoot);
        _fbrEngine = fbrEngine ?? new FbrFiscalizationEngine();
    }

    private static string ResolveAppRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot) && Directory.Exists(explicitRoot)) return Path.GetFullPath(explicitRoot);
        var envRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH") ?? Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot)) return Path.GetFullPath(envRoot);
        return Directory.Exists(AppContext.BaseDirectory) ? AppContext.BaseDirectory : Directory.GetCurrentDirectory();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial opportunistic cleanup on startup
        CleanupExpiredSharedPdfs();

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueScheduledJobsAsync(DateTime.Now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "KarobarSchedulerHostedService tick warning.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Evaluate and execute any of the 4 scheduled jobs (`demands:notify`, `backup:run-scheduled`,
    /// `cleanupExpiredSharedPdfs`, `expenses:process-recurring`) that are due at `now`.
    /// </summary>
    public async Task<Dictionary<string, bool>> RunDueScheduledJobsAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var executed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["demands:notify"] = false,
            ["backup:run-scheduled"] = false,
            ["cleanupExpiredSharedPdfs"] = false,
            ["expenses:process-recurring"] = false
        };

        // 1. Daily cleanup of temporary shared PDF files older than 24 hours
        if (_lastSharedPdfCleanupDate.Date != now.Date)
        {
            CleanupExpiredSharedPdfs();
            _lastSharedPdfCleanupDate = now.Date;
            executed["cleanupExpiredSharedPdfs"] = true;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<KarobarDbContext>();
        if (db == null)
        {
            return executed;
        }

        // 2. Daily at 00:05 -> expenses:process-recurring
        if (_lastRecurringExpensesDate.Date != now.Date && (now.Hour > 0 || (now.Hour == 0 && now.Minute >= 5)))
        {
            try
            {
                var recurringResult = await _cliRunner.ExecuteProcessRecurringExpensesAsync(db, new Dictionary<string, string?>(), cancellationToken);
                if (recurringResult.Ok)
                {
                    _lastRecurringExpensesDate = now.Date;
                    executed["expenses:process-recurring"] = true;
                }
                else
                {
                    _logger?.LogWarning("Scheduled job 'expenses:process-recurring' returned exit code {ExitCode}: {Output}", recurringResult.ExitCode, recurringResult.Output);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Scheduled job 'expenses:process-recurring' failed.");
            }
        }

        // 3. Daily at 09:00 -> demands:notify
        if (_lastDemandsNotifyDate.Date != now.Date && now.Hour >= 9)
        {
            try
            {
                var demandsResult = await _cliRunner.ExecuteSendUserDemandNotificationsAsync(db, new Dictionary<string, string?>(), cancellationToken);
                if (demandsResult.Ok)
                {
                    _lastDemandsNotifyDate = now.Date;
                    executed["demands:notify"] = true;
                    if (demandsResult.Data.TryGetValue("notified_count", out var ncObj) &&
                        ncObj is int nc && nc > 0)
                    {
                        _logger?.LogInformation("Scheduled job 'demands:notify' notified {NotifiedCount} pending user demand(s).", nc);
                    }
                }
                else
                {
                    _logger?.LogWarning("Scheduled job 'demands:notify' returned exit code {ExitCode}: {Output}", demandsResult.ExitCode, demandsResult.Output);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Scheduled job 'demands:notify' failed.");
            }
        }

        // 4. Hourly -> backup:run-scheduled (plus independent FBR queue retry)
        var currentSlot = (now.Date, now.Hour);
        if (_lastScheduledBackupSlot != currentSlot)
        {
            var backupSucceeded = false;
            try
            {
                var backupResult = await _cliRunner.ExecuteRunScheduledBackupAsync(db, new Dictionary<string, string?>(), cancellationToken);
                if (backupResult.Ok)
                {
                    executed["backup:run-scheduled"] = true;
                    backupSucceeded = true;
                }
                else
                {
                    _logger?.LogWarning("Scheduled job 'backup:run-scheduled' failed (ExitCode={ExitCode}): {Output}", backupResult.ExitCode, backupResult.Output);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Scheduled job 'backup:run-scheduled' failed.");
            }

            try
            {
                await _fbrEngine.RetryPendingInvoicesAsync(db, 25, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Periodic FBR offline queue retry failed.");
            }

            if (backupSucceeded)
            {
                _lastScheduledBackupSlot = currentSlot;
            }
        }

        return executed;
    }

    public int CleanupExpiredSharedPdfs(int maxAgeSeconds = 86400)
    {
        var deleted = 0;
        var cutoff = DateTime.UtcNow.AddSeconds(-maxAgeSeconds);

        foreach (var relDir in new[] { Path.Combine("storage", "app", "shared-pdfs"), Path.Combine("storage", "app", "public", "shared-pdfs") })
        {
            var dir = Path.Combine(_appRoot, relDir);
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    if (relDir.Contains("public") || File.GetLastWriteTimeUtc(file) <= cutoff)
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Opportunistic shared PDF cleanup failed for {Directory}.", dir);
            }
        }

        return deleted;
    }
}
