using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("users")]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("email")]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    [Column("email_verified_at")]
    [JsonPropertyName("email_verified_at")]
    public DateTime? EmailVerifiedAt { get; set; }

    [Column("password")]
    [JsonPropertyName("password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Password { get; set; } = string.Empty;

    [Column("remember_token")]
    [JsonPropertyName("remember_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RememberToken { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("remember_token_expires_at")]
    [JsonPropertyName("remember_token_expires_at")]
    public DateTime? RememberTokenExpiresAt { get; set; }

    [Column("preferences")]
    [JsonPropertyName("preferences")]
    public string? Preferences { get; set; }

    [Column("pin_code")]
    [JsonPropertyName("pin_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PinCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Employee? Employee { get; set; }
}
