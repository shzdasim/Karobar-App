using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("licenses")]
public class License
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("license_key")]
    [JsonPropertyName("license_key")]
    public string LicenseKey { get; set; } = string.Empty;

    [Column("payload")]
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [Column("valid")]
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [Column("reason")]
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [Column("machine_hash")]
    [JsonPropertyName("machine_hash")]
    public string? MachineHash { get; set; }

    [Column("activated_at")]
    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [Column("expires_at")]
    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("last_verified_at")]
    [JsonPropertyName("last_verified_at")]
    public DateTime? LastVerifiedAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

}
