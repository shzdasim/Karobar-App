using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("stocktake_items")]
public class StocktakeItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("stocktake_session_id")]
    [JsonPropertyName("stocktake_session_id")]
    public int StocktakeSessionId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("batch_number")]
    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry_date")]
    [JsonPropertyName("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [Column("system_qty")]
    [JsonPropertyName("system_qty")]
    public decimal SystemQty { get; set; }

    [Column("counted_qty")]
    [JsonPropertyName("counted_qty")]
    public decimal? CountedQty { get; set; }

    [Column("variance_qty")]
    [JsonPropertyName("variance_qty")]
    public decimal VarianceQty { get; set; }

    [Column("variance_value")]
    [JsonPropertyName("variance_value")]
    public decimal VarianceValue { get; set; }

    [Column("unit_purchase_price")]
    [JsonPropertyName("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [Column("discrepancy_reason")]
    [JsonPropertyName("discrepancy_reason")]
    public string? DiscrepancyReason { get; set; }

    [Column("counted_by")]
    [JsonPropertyName("counted_by")]
    public int? CountedBy { get; set; }

    [Column("counted_at")]
    [JsonPropertyName("counted_at")]
    public DateTime? CountedAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(StocktakeSessionId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StocktakeSession? Session { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }

    [ForeignKey(nameof(CountedBy))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? Counter { get; set; }
}
