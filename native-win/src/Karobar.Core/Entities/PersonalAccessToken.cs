using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("personal_access_tokens")]
public class PersonalAccessToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("tokenable_type")]
    [JsonPropertyName("tokenable_type")]
    public string TokenableType { get; set; } = string.Empty;

    [Column("tokenable_id")]
    [JsonPropertyName("tokenable_id")]
    public int TokenableId { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("token")]
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [Column("abilities")]
    [JsonPropertyName("abilities")]
    public string? Abilities { get; set; }

    [Column("last_used_at")]
    [JsonPropertyName("last_used_at")]
    public DateTime? LastUsedAt { get; set; }

    [Column("expires_at")]
    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    public const int LastUsedAtThrottleSeconds = 300;
}
