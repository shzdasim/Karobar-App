using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("sale_return_items")]
public class SaleReturnItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("sale_return_id")]
    [JsonPropertyName("sale_return_id")]
    public int SaleReturnId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("batch_number")]
    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry")]
    [JsonPropertyName("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("unit_sale_quantity")]
    [JsonPropertyName("unit_sale_quantity")]
    public int? UnitSaleQuantity { get; set; }

    [Column("unit_return_quantity")]
    [JsonPropertyName("unit_return_quantity")]
    public int UnitReturnQuantity { get; set; }

    [Column("unit_sale_price")]
    [JsonPropertyName("unit_sale_price")]
    public decimal UnitSalePrice { get; set; }

    [Column("item_discount_percentage")]
    [JsonPropertyName("item_discount_percentage")]
    public decimal ItemDiscountPercentage { get; set; }

    [Column("sub_total")]
    [JsonPropertyName("sub_total")]
    public decimal SubTotal { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("cost_price_snapshot")]
    [JsonPropertyName("cost_price_snapshot")]
    public decimal? CostPriceSnapshot { get; set; }

    [ForeignKey(nameof(SaleReturnId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual SaleReturn? SaleReturn { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }
}
