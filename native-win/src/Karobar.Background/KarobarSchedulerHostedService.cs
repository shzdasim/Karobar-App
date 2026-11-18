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
    private int _lastScheduledBackupHour = -1;

    public KarobarSchedulerHostedService(
        IServiceProvider serviceProvider,
        ILogger<KarobarSchedulerHostedService>? logger = null,
        string? appRoot = null,
        KarobarCliCommandRunner? cliRunner = null,
        FbrFiscalizationEngine? fbrEngine = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _appRoot = appRoot ?? Directory.GetCurrentDirectory();
        _cliRunner = cliRunner ?? new KarobarCliCommandRunner(_appRoot);
        _fbrEngine = fbrEngine ?? new FbrFiscalizationEngine();
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
            await _cliRunner.ExecuteProcessRecurringExpensesAsync(db, new Dictionary<string, string?>(), cancellationToken);
            _lastRecurringExpensesDate = now.Date;
            executed["expenses:process-recurring"] = true;
        }

        // 3. Daily at 09:00 -> demands:notify
        if (_lastDemandsNotifyDate.Date != now.Date && now.Hour >= 9)
        {
            await _cliRunner.ExecuteSendUserDemandNotificationsAsync(db, new Dictionary<string, string?>(), cancellationToken);
            _lastDemandsNotifyDate = now.Date;
            executed["demands:notify"] = true;
        }

        // 4. Hourly -> backup:run-scheduled (plus opportunistic FBR queue retry)
        if (_lastScheduledBackupHour != now.Hour)
        {
            await _cliRunner.ExecuteRunScheduledBackupAsync(db, new Dictionary<string, string?>(), cancellationToken);
            await _fbrEngine.RetryPendingInvoicesAsync(db, 25, cancellationToken);
            _lastScheduledBackupHour = now.Hour;
            executed["backup:run-scheduled"] = true;
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
            catch
            {
                // Ignore filesystem errors during opportunistic cleanup
            }
        }

        return deleted;
    }
}
