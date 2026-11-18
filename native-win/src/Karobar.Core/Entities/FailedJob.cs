using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("failed_jobs")]
public class FailedJob
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("uuid")]
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [Column("connection")]
    [JsonPropertyName("connection")]
    public string Connection { get; set; } = string.Empty;

    [Column("queue")]
    [JsonPropertyName("queue")]
    public string Queue { get; set; } = string.Empty;

    [Column("payload")]
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [Column("exception")]
    [JsonPropertyName("exception")]
    public string Exception { get; set; } = string.Empty;

    [Column("failed_at")]
    [JsonPropertyName("failed_at")]
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;

}
