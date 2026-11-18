using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("stock_adjustment_items")]
public class StockAdjustmentItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("stock_adjustment_id")]
    [JsonPropertyName("stock_adjustment_id")]
    public int StockAdjustmentId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("batch_number")]
    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry")]
    [JsonPropertyName("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("pack_size")]
    [JsonPropertyName("pack_size")]
    public decimal? PackSize { get; set; }

    [Column("previous_qty")]
    [JsonPropertyName("previous_qty")]
    public decimal PreviousQty { get; set; }

    [Column("actual_qty")]
    [JsonPropertyName("actual_qty")]
    public decimal ActualQty { get; set; }

    [Column("diff_qty")]
    [JsonPropertyName("diff_qty")]
    public decimal DiffQty { get; set; }

    [Column("unit_purchase_price")]
    [JsonPropertyName("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("worth_adjusted")]
    [JsonPropertyName("worth_adjusted")]
    public decimal WorthAdjusted { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(StockAdjustmentId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StockAdjustment? Adjustment { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }
}
