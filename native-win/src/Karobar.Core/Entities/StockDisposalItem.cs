using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("stock_disposal_items")]
public class StockDisposalItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("stock_disposal_id")]
    [JsonPropertyName("stock_disposal_id")]
    public int StockDisposalId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("batch_id")]
    [JsonPropertyName("batch_id")]
    public int? BatchId { get; set; }

    [Column("batch_number")]
    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry_date")]
    [JsonPropertyName("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [Column("pack_size")]
    [JsonPropertyName("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("previous_qty")]
    [JsonPropertyName("previous_qty")]
    public decimal PreviousQty { get; set; }

    [Column("dispose_qty")]
    [JsonPropertyName("dispose_qty")]
    public decimal DisposeQty { get; set; }

    [Column("remaining_qty")]
    [JsonPropertyName("remaining_qty")]
    public decimal RemainingQty { get; set; }

    [Column("unit_purchase_price")]
    [JsonPropertyName("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("unit_sale_price")]
    [JsonPropertyName("unit_sale_price")]
    public decimal UnitSalePrice { get; set; }

    [Column("cost_value")]
    [JsonPropertyName("cost_value")]
    public decimal CostValue { get; set; }

    [Column("retail_value")]
    [JsonPropertyName("retail_value")]
    public decimal RetailValue { get; set; }

    [Column("item_note")]
    [JsonPropertyName("item_note")]
    public string? ItemNote { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(StockDisposalId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StockDisposal? Disposal { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }

    [ForeignKey(nameof(BatchId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Batch? Batch { get; set; }
}
