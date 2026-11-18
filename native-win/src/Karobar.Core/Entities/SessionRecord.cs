using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("sessions")]
public class SessionRecord
{
    [Key]
    [Column("id")]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("ip_address")]
    [JsonPropertyName("ip_address")]
    public string? IpAddress { get; set; }

    [Column("user_agent")]
    [JsonPropertyName("user_agent")]
    public string? UserAgent { get; set; }

    [Column("payload")]
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [Column("last_activity")]
    [JsonPropertyName("last_activity")]
    public long LastActivity { get; set; }

}
