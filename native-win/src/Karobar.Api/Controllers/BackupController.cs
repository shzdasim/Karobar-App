using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Karobar.Api.Controllers;

[Route("api/backups")]
[Authorize]
public class BackupController : ApiControllerBase
{
    private readonly BackupService _backupService;

    public BackupController(KarobarDb db, BackupService backupService) : base(db)
    {
        _backupService = backupService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (!await CanAsync("backup.view", "backup.create", "settings.view")) return Forbidden();

        var backups = await _backupService.ListBackupsAsync();
        return Ok(new
        {
            backups,
            total = backups.Count
        });
    }

    [HttpPost("")]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("backup.create", "settings.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var type = (GetString(body, "type") ?? "full").ToLowerInvariant();
        if (type is not ("full" or "database" or "settings"))
        {
            return Unprocessable("Invalid backup type.");
        }

        var customPath = GetString(body, "custom_backup_path");
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            if (!_backupService.IsReachableHostPath(customPath))
            {
                customPath = null;
            }
            else
            {
                try
                {
                    _backupService.AssertSafeBackupDirectory(customPath);
                }
                catch (ArgumentException ex)
                {
                    return Unprocessable(ex.Message, new Dictionary<string, string[]>
                    {
                        ["custom_backup_path"] = new[] { ex.Message }
                    });
                }
            }
        }

        try
        {
            var backup = await _backupService.CreateBackupAsync(type, CurrentUserId, isAutomated: false, customBackupPath: customPath);
            return StatusCode(201, new
            {
                message = "Backup created successfully",
                backup
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Failed to create backup: " + ex.Message,
                error = ex.Message
            });
        }
    }

    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id)
    {
        if (!await CanAsync("backup.view", "settings.view")) return Forbidden();

        var path = await _backupService.GetDownloadPathAsync(id);
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            return NotFound(new { message = "Backup file not found" });
        }

        return PhysicalFile(Path.GetFullPath(path), "application/octet-stream", Path.GetFileName(path));
    }

    private async Task<bool> VerifyCurrentUserPasswordAsync(string password)
    {
        if (CurrentUserId == null || string.IsNullOrEmpty(password)) return false;
        var user = await Db.QuerySingleOrDefaultAsync("SELECT password FROM users WHERE id = @id", new { id = CurrentUserId.Value });
        var hash = user?.GetValueOrDefault("password")?.ToString() ?? "";
        return !string.IsNullOrEmpty(hash) && SanctumAuthenticationHandler.VerifyLaravelBcryptPassword(password, hash);
    }

    [HttpPost("{id:long}/restore")]
    [EnableRateLimiting("throttle_10_1")]
    public async Task<IActionResult> Restore(long id)
    {
        if (!await CanAsync("backup.restore", "settings.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var password = GetString(body, "password") ?? "";
        if (string.IsNullOrWhiteSpace(password))
        {
            return Unprocessable("Password is required to restore a backup.");
        }
        if (!await VerifyCurrentUserPasswordAsync(password))
        {
            return Unprocessable("Invalid account password. Backup restore aborted.", new Dictionary<string, string[]>
            {
                ["password"] = new[] { "The provided password does not match your account password." }
            });
        }

        try
        {
            await _backupService.RestoreBackupAsync(id, password, CurrentUserId);
            return Ok(new { message = "Backup restored successfully. Please refresh the page." });
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("backup.delete", "settings.update")) return Forbidden();

        var deleted = await _backupService.DeleteBackupAsync(id);
        if (!deleted) return NotFound(new { message = "Backup not found" });

        return Ok(new { message = "Backup deleted successfully" });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        if (!await CanAsync("backup.view", "backup.create", "settings.view")) return Forbidden();

        await _backupService.EnsureBackupSchemaAsync();
        var totalBackups = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM backup_logs");
        var completedBackups = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM backup_logs WHERE status = 'completed'");
        var failedBackups = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM backup_logs WHERE status = 'failed'");
        var totalSize = await Db.ExecuteScalarAsync<long?>("SELECT SUM(size) FROM backup_logs WHERE status = 'completed'") ?? 0L;

        var lastRow = await Db.QuerySingleOrDefaultAsync("SELECT created_at FROM backup_logs WHERE status = 'completed' ORDER BY id DESC LIMIT 1");
        string? lastBackupAt = lastRow?.GetValueOrDefault("created_at")?.ToString();

        var schedule = await _backupService.GetScheduleAsync();

        return Ok(new
        {
            total_backups = totalBackups,
            completed_backups = completedBackups,
            failed_backups = failedBackups,
            total_size = totalSize,
            formatted_total_size = BackupService.FormatBytes(totalSize),
            last_backup_at = lastBackupAt,
            schedule
        });
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule()
    {
        if (!await CanAsync("backup.view", "backup.create", "settings.view")) return Forbidden();
        return Ok(new { schedule = await _backupService.GetScheduleAsync() });
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> UpdateSchedule()
    {
        if (!await CanAsync("backup.create", "settings.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var schedule = await _backupService.GetScheduleAsync();
        var scheduleId = Convert.ToInt64(schedule["id"]);

        var customPath = GetString(body, "custom_backup_path");
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            try
            {
                _backupService.AssertSafeBackupDirectory(customPath);
            }
            catch (ArgumentException ex)
            {
                return Unprocessable(ex.Message, new Dictionary<string, string[]>
                {
                    ["custom_backup_path"] = new[] { ex.Message }
                });
            }
        }

        var allowedScheduleFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "is_enabled", "enabled", "frequency", "backup_time", "scheduled_time", "backup_type",
            "retention_days", "retention_count", "max_backups", "destination", "custom_backup_path",
            "destination_config", "notify_on_failure", "notification_email", "auto_backup_enabled"
        };
        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            if (!allowedScheduleFields.Contains(kv.Key)) continue;
            updates[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
        }

        if (updates.Count > 0)
        {
            await Db.UpdateAsync("backup_schedules", scheduleId, updates);
        }
        return Ok(new
        {
            message = "Backup schedule and storage settings updated successfully",
            schedule = await _backupService.GetScheduleAsync()
        });
    }

    [HttpPost("test-destination")]
    public async Task<IActionResult> TestDestination()
    {
        if (!await CanAsync("backup.create", "settings.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var destination = GetString(body, "destination") ?? "local";
        var customPath = (GetString(body, "custom_backup_path") ?? "").Trim();

        var targetPath = destination == "local" && string.IsNullOrEmpty(customPath)
            ? _backupService.DefaultBackupPath
            : customPath;

        if (string.IsNullOrEmpty(targetPath))
        {
            return StatusCode(422, new
            {
                success = false,
                message = "Please enter a valid folder path to test."
            });
        }

        try
        {
            _backupService.AssertSafeBackupDirectory(targetPath);
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            var probeFile = Path.Combine(targetPath, $".karobar_probe_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.tmp");
            await System.IO.File.WriteAllTextAsync(probeFile, "karobar_write_test");
            if (System.IO.File.Exists(probeFile))
            {
                System.IO.File.Delete(probeFile);
            }

            return Ok(new
            {
                success = true,
                path = targetPath,
                writable = true,
                free_space = "Available",
                message = "Folder is accessible, writable, and ready for backups."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(422, new
            {
                success = false,
                path = targetPath,
                message = "Failed to access folder: " + ex.Message
            });
        }
    }

    [HttpGet("browse-directories")]
    public async Task<IActionResult> BrowseDirectories([FromQuery] string? path = null)
    {
        if (!await CanAsync("backup.view", "backup.create", "settings.update")) return Forbidden();

        var shortcuts = new List<object>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
        {
            shortcuts.Add(new { name = "Home", path = home, type = "home" });
            var desktop = Path.Combine(home, "Desktop");
            if (Directory.Exists(desktop)) shortcuts.Add(new { name = "Desktop", path = desktop, type = "desktop" });
            var docs = Path.Combine(home, "Documents");
            if (Directory.Exists(docs)) shortcuts.Add(new { name = "Documents", path = docs, type = "documents" });
        }

        shortcuts.Add(new { name = "Default App Storage", path = _backupService.DefaultBackupPath, type = "app" });

        var currentPath = _backupService.DefaultBackupPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                _backupService.AssertSafeBackupDirectory(path);
                if (Directory.Exists(path))
                {
                    currentPath = Path.GetFullPath(path);
                }
            }
            catch (ArgumentException ex)
            {
                return Unprocessable(ex.Message);
            }
        }

        var dirs = new List<object>();
        try
        {
            foreach (var d in Directory.GetDirectories(currentPath).OrderBy(x => x))
            {
                var name = Path.GetFileName(d);
                if (name.StartsWith(".")) continue;
                dirs.Add(new
                {
                    name,
                    path = d,
                    is_writable = true
                });
            }
        }
        catch
        {
        }

        var parent = Directory.GetParent(currentPath)?.FullName;
        return Ok(new
        {
            current_path = currentPath,
            parent_path = parent,
            is_writable = true,
            free_space = "Available",
            shortcuts,
            directories = dirs
        });
    }

    [HttpPost("create-directory")]
    public async Task<IActionResult> CreateDirectory()
    {
        if (!await CanAsync("backup.create", "settings.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var parentPath = (GetString(body, "parent_path") ?? "").Trim();
        var folderName = (GetString(body, "folder_name") ?? "").Trim();

        if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(folderName) || !Regex.IsMatch(folderName, @"^[a-zA-Z0-9_\-\s]+$"))
        {
            return Unprocessable("Invalid folder name or parent path.");
        }

        try
        {
            _backupService.AssertSafeBackupDirectory(parentPath);
            var newFolder = Path.Combine(parentPath, folderName);
            _backupService.AssertSafeBackupDirectory(newFolder);

            if (!Directory.Exists(newFolder))
            {
                Directory.CreateDirectory(newFolder);
            }

            return StatusCode(201, new
            {
                message = "Folder created successfully.",
                path = newFolder
            });
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPost("run-now")]
    public async Task<IActionResult> RunScheduledNow()
    {
        if (!await CanAsync("backup.create", "settings.update")) return Forbidden();

        var sched = await _backupService.GetScheduleAsync();
        var backupType = sched.GetValueOrDefault("backup_type")?.ToString();
        if (string.IsNullOrWhiteSpace(backupType)) backupType = "full";

        await _backupService.CreateBackupAsync(backupType, CurrentUserId, isAutomated: true);
        if (sched.TryGetValue("id", out var sidObj) && sidObj != null && await Db.HasTableAsync("backup_schedules"))
        {
            await Db.UpdateAsync("backup_schedules", Convert.ToInt64(sidObj), new Dictionary<string, object?>
            {
                ["last_run_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                ["last_run_status"] = "success"
            });
        }

        return Ok(new
        {
            message = "Scheduled backup job executed successfully.",
            schedule = await _backupService.GetScheduleAsync()
        });
    }

    [HttpPost("upload/validate")]
    public async Task<IActionResult> ValidateUpload()
    {
        if (!await CanAsync("backup.upload", "backup.create", "settings.update")) return Forbidden();

        if (!Request.HasFormContentType)
        {
            return Unprocessable("Multipart file upload required.");
        }

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var res = _backupService.ValidateUploadedBackup(file);
        return Ok(res);
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload()
    {
        if (!await CanAsync("backup.upload", "backup.create", "settings.update")) return Forbidden();

        if (!Request.HasFormContentType)
        {
            return Unprocessable("Multipart file upload required.");
        }

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var validation = _backupService.ValidateUploadedBackup(file);
        if (validation.GetValueOrDefault("valid") is not true || file == null)
        {
            return Unprocessable("Invalid backup file");
        }

        var backup = await _backupService.UploadBackupAsync(file, CurrentUserId);
        return StatusCode(201, new
        {
            message = "Backup uploaded successfully",
            backup
        });
    }

    [HttpPost("upload/restore")]
    [EnableRateLimiting("throttle_10_1")]
    public async Task<IActionResult> RestoreFromUpload()
    {
        if (!await CanAsync("backup.restore", "settings.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var fileId = GetNullableLong(body, "file_id");
        var password = GetString(body, "password") ?? "";

        if (!fileId.HasValue || string.IsNullOrWhiteSpace(password))
        {
            return Unprocessable("File ID and password are required.");
        }
        if (!await VerifyCurrentUserPasswordAsync(password))
        {
            return Unprocessable("Invalid account password. Backup restore aborted.", new Dictionary<string, string[]>
            {
                ["password"] = new[] { "The provided password does not match your account password." }
            });
        }

        try
        {
            await _backupService.RestoreBackupAsync(fileId.Value, password, CurrentUserId);
            return Ok(new { message = "Backup restored successfully. Please refresh the page." });
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }
    }
}
