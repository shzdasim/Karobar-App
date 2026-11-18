using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("jobs")]
public class JobRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("queue")]
    [JsonPropertyName("queue")]
    public string Queue { get; set; } = string.Empty;

    [Column("payload")]
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [Column("attempts")]
    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }

    [Column("reserved_at")]
    [JsonPropertyName("reserved_at")]
    public long? ReservedAt { get; set; }

    [Column("available_at")]
    [JsonPropertyName("available_at")]
    public long AvailableAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

}
