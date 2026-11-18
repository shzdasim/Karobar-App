using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("backup_schedules")]
public class BackupSchedule
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("is_automated")]
    [JsonPropertyName("is_automated")]
    public bool IsAutomated { get; set; }

    [Column("frequency")]
    [JsonPropertyName("frequency")]
    public string Frequency { get; set; } = "daily";

    [Column("time_of_day")]
    [JsonPropertyName("time_of_day")]
    public string TimeOfDay { get; set; } = "02:00";

    [Column("day_of_week")]
    [JsonPropertyName("day_of_week")]
    public int? DayOfWeek { get; set; } = 0;

    [Column("day_of_month")]
    [JsonPropertyName("day_of_month")]
    public int? DayOfMonth { get; set; } = 1;

    [Column("backup_type")]
    [JsonPropertyName("backup_type")]
    public string BackupType { get; set; } = "full";

    [Column("retention_days")]
    [JsonPropertyName("retention_days")]
    public int RetentionDays { get; set; } = 30;

    [Column("max_backups_keep")]
    [JsonPropertyName("max_backups_keep")]
    public int MaxBackupsKeep { get; set; } = 10;

    [Column("storage_destination")]
    [JsonPropertyName("storage_destination")]
    public string StorageDestination { get; set; } = "local";

    [Column("destination_config")]
    [JsonPropertyName("destination_config")]
    public string? DestinationConfig { get; set; }

    [Column("notify_on_failure")]
    [JsonPropertyName("notify_on_failure")]
    public bool NotifyOnFailure { get; set; } = true;

    [Column("notification_email")]
    [JsonPropertyName("notification_email")]
    public string? NotificationEmail { get; set; }

    [Column("last_run_at")]
    [JsonPropertyName("last_run_at")]
    public DateTime? LastRunAt { get; set; }

    [Column("last_run_status")]
    [JsonPropertyName("last_run_status")]
    public string LastRunStatus { get; set; } = "none";

    [Column("last_error_message")]
    [JsonPropertyName("last_error_message")]
    public string? LastErrorMessage { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("custom_backup_path")]
    [JsonPropertyName("custom_backup_path")]
    public string? CustomBackupPath { get; set; }

}
