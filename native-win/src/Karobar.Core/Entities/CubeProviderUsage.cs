using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("cube_provider_usages")]
public class CubeProviderUsage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("provider")]
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [Column("usage_date")]
    [JsonPropertyName("usage_date")]
    public DateTime UsageDate { get; set; }

    [Column("request_count")]
    [JsonPropertyName("request_count")]
    public int RequestCount { get; set; }

    [Column("success_count")]
    [JsonPropertyName("success_count")]
    public int SuccessCount { get; set; }

    [Column("failure_count")]
    [JsonPropertyName("failure_count")]
    public int FailureCount { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ready";

    [Column("cooldown_until")]
    [JsonPropertyName("cooldown_until")]
    public DateTime? CooldownUntil { get; set; }

    [Column("disabled_reason")]
    [JsonPropertyName("disabled_reason")]
    public string? DisabledReason { get; set; }

    [Column("config_fingerprint")]
    [JsonPropertyName("config_fingerprint")]
    public string? ConfigFingerprint { get; set; }

    [Column("last_http_status")]
    [JsonPropertyName("last_http_status")]
    public int? LastHttpStatus { get; set; }

    [Column("last_error_code")]
    [JsonPropertyName("last_error_code")]
    public string? LastErrorCode { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

}
