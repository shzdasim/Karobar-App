using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Karobar.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Background;

public sealed class CubeBackgroundTaskSnapshot
{
    public string Status { get; set; } = "idle"; // idle | running | completed | stopped | error
    public string TaskType { get; set; } = "bulk_autofill_missing_formulations";
    public string FilterQuery { get; set; } = "";
    public int ScannedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int RemainingCount { get; set; }
    public int TotalEstimate { get; set; }
    public long LastProcessedId { get; set; }
    public string Message { get; set; } = "";
    public DateTime? StartedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<Dictionary<string, object?>> UpdatedProducts { get; set; } = [];
    public List<Dictionary<string, object?>> SkippedProducts { get; set; } = [];

    public int Percent => TotalEstimate > 0
        ? Math.Clamp((int)Math.Round((double)ScannedCount / TotalEstimate * 100), 0, 100)
        : (Status == "completed" ? 100 : 0);

    public CubeBackgroundTaskSnapshot Clone() => new()
    {
        Status = Status,
        TaskType = TaskType,
        FilterQuery = FilterQuery,
        ScannedCount = ScannedCount,
        UpdatedCount = UpdatedCount,
        SkippedCount = SkippedCount,
        RemainingCount = RemainingCount,
        TotalEstimate = TotalEstimate,
        LastProcessedId = LastProcessedId,
        Message = Message,
        StartedAt = StartedAt,
        UpdatedAt = UpdatedAt,
        UpdatedProducts = UpdatedProducts.ToList(),
        SkippedProducts = SkippedProducts.ToList()
    };
}

/// <summary>
/// Native Singleton Background Task Runner for Cube AI bulk operations (e.g. `bulk_autofill_missing_formulations`).
/// Runs outside browser tabs so closing or switching Waseela tabs NEVER kills or orphans an active job,
/// and pushes real-time progress to NativeGuideBotDrawer, NativeMascotOverlayForm, and `/api/cube/bg-task/*`.
/// </summary>
public sealed class CubeBackgroundTaskManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CubeTaskExecutorService _taskExecutor;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private CubeBackgroundTaskSnapshot _snapshot = new();

    public event Action<CubeBackgroundTaskSnapshot>? StateChanged;
    public event Action? ProductsMutated;

    public CubeBackgroundTaskManager(IServiceScopeFactory scopeFactory, CubeTaskExecutorService taskExecutor)
    {
        _scopeFactory = scopeFactory;
        _taskExecutor = taskExecutor;
    }

    public CubeBackgroundTaskSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return _snapshot.Clone();
        }
    }

    public CubeBackgroundTaskSnapshot StartBulkAutofill(CubeUserContext user, string? filterQuery = null, int batchSize = 20)
    {
        lock (_lock)
        {
            if (_snapshot.Status == "running")
            {
                return _snapshot.Clone();
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _snapshot = new CubeBackgroundTaskSnapshot
            {
                Status = "running",
                TaskType = "bulk_autofill_missing_formulations",
                FilterQuery = (filterQuery ?? "").Trim(),
                StartedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Message = "Starting background DRAP formulation auto-fill…"
            };
            var initial = _snapshot.Clone();
            FireChanged(initial);

            _ = Task.Run(() => RunLoopAsync(user, (filterQuery ?? "").Trim(), Math.Clamp(batchSize, 5, 30), ct));
            return initial;
        }
    }

    public CubeBackgroundTaskSnapshot Stop()
    {
        lock (_lock)
        {
            try { _cts?.Cancel(); } catch { }
            if (_snapshot.Status == "running")
            {
                _snapshot.Status = "stopped";
                _snapshot.Message = $"Stopped by user after scanning {_snapshot.ScannedCount} product(s) ({_snapshot.UpdatedCount} updated).";
                _snapshot.UpdatedAt = DateTime.Now;
            }
            var snap = _snapshot.Clone();
            FireChanged(snap);
            return snap;
        }
    }

    private async Task RunLoopAsync(CubeUserContext user, string filterQuery, int batchSize, CancellationToken ct)
    {
        long afterId = 0;
        var anyUpdated = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Dictionary<string, object?> batchResult;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<KarobarDbContext>();
                    batchResult = await _taskExecutor.ExecuteBulkAutofillMissingFormulationsAsync(
                        db,
                        user,
                        new Dictionary<string, object?>
                        {
                            ["task_type"] = "bulk_autofill_missing_formulations",
                            ["preview_only"] = false,
                            ["user_confirmed"] = true,
                            ["after_id"] = afterId,
                            ["batch_size"] = batchSize,
                            ["filter_query"] = filterQuery
                        },
                        ct: ct);
                }

                var tm = batchResult.GetValueOrDefault("task_meta") as Dictionary<string, object?>;
                var bg = tm?.GetValueOrDefault("background_task") as Dictionary<string, object?>;
                var processed = Convert.ToInt32(tm?.GetValueOrDefault("scanned_count") ?? 0);
                var updated = Convert.ToInt32(tm?.GetValueOrDefault("updated_count") ?? 0);
                var skipped = Convert.ToInt32(tm?.GetValueOrDefault("skipped_count") ?? 0);
                var remaining = Convert.ToInt32(bg?.GetValueOrDefault("remaining_count") ?? 0);
                var totalMissing = Convert.ToInt32(bg?.GetValueOrDefault("total_missing") ?? 0);
                var nextAfterId = Convert.ToInt64(bg?.GetValueOrDefault("next_after_id") ?? afterId);
                var hasMore = bg?.GetValueOrDefault("has_more") is true;

                var updList = tm?.GetValueOrDefault("updated_products") as List<Dictionary<string, object?>> ?? [];
                var skpList = tm?.GetValueOrDefault("skipped_products") as List<Dictionary<string, object?>> ?? [];
                if (updated > 0) anyUpdated = true;

                CubeBackgroundTaskSnapshot snap;
                var stoppedEarly = false;
                lock (_lock)
                {
                    if (ct.IsCancellationRequested || _snapshot.Status == "stopped")
                    {
                        stoppedEarly = true;
                        snap = _snapshot.Clone();
                    }
                    else
                    {
                        _snapshot.ScannedCount += processed;
                        _snapshot.UpdatedCount += updated;
                        _snapshot.SkippedCount += skipped;
                        _snapshot.RemainingCount = remaining;
                        _snapshot.LastProcessedId = nextAfterId;
                        if (_snapshot.TotalEstimate == 0)
                        {
                            _snapshot.TotalEstimate = Math.Max(totalMissing, _snapshot.ScannedCount + remaining);
                        }
                        if (_snapshot.UpdatedProducts.Count < 1000) _snapshot.UpdatedProducts.AddRange(updList);
                        if (_snapshot.SkippedProducts.Count < 1000) _snapshot.SkippedProducts.AddRange(skpList);
                        _snapshot.UpdatedAt = DateTime.Now;
                        _snapshot.Message = hasMore
                            ? $"Auto-filling… {_snapshot.ScannedCount}/{_snapshot.TotalEstimate} scanned ({_snapshot.UpdatedCount} filled)"
                            : $"Completed: {_snapshot.UpdatedCount} formulation(s) filled, {_snapshot.SkippedCount} skipped.";
                        if (!hasMore || nextAfterId <= afterId || processed == 0)
                        {
                            _snapshot.Status = "completed";
                        }
                        snap = _snapshot.Clone();
                    }
                }

                if (stoppedEarly) break;

                FireChanged(snap);

                if (!hasMore || nextAfterId <= afterId || processed == 0)
                {
                    break;
                }
                afterId = nextAfterId;
                await Task.Delay(120, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped cleanly
        }
        catch (Exception ex)
        {
            CubeBackgroundTaskSnapshot snap;
            lock (_lock)
            {
                _snapshot.Status = "error";
                _snapshot.Message = $"Background task error: {ex.Message}";
                _snapshot.UpdatedAt = DateTime.Now;
                snap = _snapshot.Clone();
            }
            FireChanged(snap);
        }
        finally
        {
            CubeBackgroundTaskSnapshot? finalSnap = null;
            lock (_lock)
            {
                if (_snapshot.Status == "running")
                {
                    _snapshot.Status = ct.IsCancellationRequested ? "stopped" : "completed";
                    _snapshot.UpdatedAt = DateTime.Now;
                    finalSnap = _snapshot.Clone();
                }
            }
            if (finalSnap != null)
            {
                FireChanged(finalSnap);
            }
            if (anyUpdated)
            {
                try { ProductsMutated?.Invoke(); } catch { }
            }
        }
    }

    private void FireChanged(CubeBackgroundTaskSnapshot snap)
    {
        try { StateChanged?.Invoke(snap); } catch { }
    }
}
