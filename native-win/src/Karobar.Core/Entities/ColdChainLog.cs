using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("cold_chain_logs")]
public class ColdChainLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("storage_location_id")]
    [JsonPropertyName("storage_location_id")]
    public int? StorageLocationId { get; set; }

    [Column("recorded_by_id")]
    [JsonPropertyName("recorded_by_id")]
    public int? RecordedById { get; set; }

    [Column("recorded_at")]
    [JsonPropertyName("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    [Column("shift")]
    [JsonPropertyName("shift")]
    public string Shift { get; set; } = "morning";

    [Column("temperature_c")]
    [JsonPropertyName("temperature_c")]
    public decimal TemperatureC { get; set; }

    [Column("humidity_pct")]
    [JsonPropertyName("humidity_pct")]
    public decimal? HumidityPct { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "normal";

    [Column("corrective_action")]
    [JsonPropertyName("corrective_action")]
    public string? CorrectiveAction { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(StorageLocationId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StorageLocation? StorageLocation { get; set; }

    [ForeignKey(nameof(RecordedById))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? RecordedBy { get; set; }
}
