using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("backup_logs")]
public class BackupLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("filename")]
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [Column("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [Column("size")]
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [Column("path")]
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [Column("metadata")]
    [JsonPropertyName("metadata")]
    public string? Metadata { get; set; }

    [Column("created_by")]
    [JsonPropertyName("created_by")]
    public string? CreatedBy { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [Column("error_message")]
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("expires_at")]
    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("is_automated")]
    [JsonPropertyName("is_automated")]
    public bool IsAutomated { get; set; }

    [Column("storage_destination")]
    [JsonPropertyName("storage_destination")]
    public string StorageDestination { get; set; } = "local";

    [Column("remote_path")]
    [JsonPropertyName("remote_path")]
    public string? RemotePath { get; set; }

    [Column("remote_file_id")]
    [JsonPropertyName("remote_file_id")]
    public string? RemoteFileId { get; set; }

    [Column("remote_status")]
    [JsonPropertyName("remote_status")]
    public string RemoteStatus { get; set; } = "skipped";

    [Column("remote_error")]
    [JsonPropertyName("remote_error")]
    public string? RemoteError { get; set; }

    public static readonly string[] Types = ["full", "database", "settings"];
    public static readonly string[] Statuses = ["pending", "completed", "failed", "restored"];

    [NotMapped]
    [JsonPropertyName("formatted_size")]
    public string FormattedSize
    {
        get
        {
            if (Size <= 0) return "0 B";
            string[] units = ["B", "KB", "MB", "GB"];
            int i = (int)Math.Min(units.Length - 1, Math.Floor(Math.Log(Size, 1024)));
            return $"{Math.Round(Size / Math.Pow(1024, i), 2)} {units[i]}";
        }
    }

    [NotMapped]
    [JsonPropertyName("type_label")]
    public string TypeLabel => Type switch
    {
        "full" => "Full Backup",
        "database" => "Database Only",
        "settings" => "Settings Only",
        _ => string.IsNullOrEmpty(Type) ? string.Empty : char.ToUpper(Type[0]) + Type[1..]
    };
}
