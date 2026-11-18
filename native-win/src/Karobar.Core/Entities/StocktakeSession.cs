using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("stocktake_sessions")]
public class StocktakeSession
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("reference_no")]
    [JsonPropertyName("reference_no")]
    public string ReferenceNo { get; set; } = string.Empty;

    [Column("session_type")]
    [JsonPropertyName("session_type")]
    public string SessionType { get; set; } = "full";

    [Column("is_blind")]
    [JsonPropertyName("is_blind")]
    public bool IsBlind { get; set; } = true;

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [Column("scope_type")]
    [JsonPropertyName("scope_type")]
    public string? ScopeType { get; set; }

    [Column("scope_id")]
    [JsonPropertyName("scope_id")]
    public int? ScopeId { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("stock_adjustment_id")]
    [JsonPropertyName("stock_adjustment_id")]
    public int? StockAdjustmentId { get; set; }

    [Column("started_at")]
    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("completed_at")]
    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("storage_location_id")]
    [JsonPropertyName("storage_location_id")]
    public int? StorageLocationId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<StocktakeItem> Items { get; set; } = new List<StocktakeItem>();

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(StockAdjustmentId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StockAdjustment? StockAdjustment { get; set; }

    [ForeignKey(nameof(StorageLocationId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StorageLocation? StorageLocation { get; set; }
}
