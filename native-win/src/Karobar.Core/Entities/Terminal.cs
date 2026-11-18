using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("terminals")]
public class Terminal
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("terminal_uid")]
    [JsonPropertyName("terminal_uid")]
    public string TerminalUid { get; set; } = string.Empty;

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("hostname")]
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [Column("ip_address")]
    [JsonPropertyName("ip_address")]
    public string? IpAddress { get; set; }

    [Column("os_platform")]
    [JsonPropertyName("os_platform")]
    public string? OsPlatform { get; set; }

    [Column("app_version")]
    [JsonPropertyName("app_version")]
    public string? AppVersion { get; set; }

    [Column("mode")]
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "terminal";

    [Column("current_user_id")]
    [JsonPropertyName("current_user_id")]
    public int? CurrentUserId { get; set; }

    [Column("is_authorized")]
    [JsonPropertyName("is_authorized")]
    public bool IsAuthorized { get; set; } = true;

    [Column("last_seen_at")]
    [JsonPropertyName("last_seen_at")]
    public DateTime? LastSeenAt { get; set; }

    [Column("meta")]
    [JsonPropertyName("meta")]
    public string? Meta { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(CurrentUserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? CurrentUser { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<CashDrawerSession> CashDrawerSessions { get; set; } = new List<CashDrawerSession>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<SaleInvoice> SaleInvoices { get; set; } = new List<SaleInvoice>();

    public bool IsOnline() =>
        LastSeenAt.HasValue && LastSeenAt.Value >= DateTime.UtcNow.AddSeconds(-25);
}
