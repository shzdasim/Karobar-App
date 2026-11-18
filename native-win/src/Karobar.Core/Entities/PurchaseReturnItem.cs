using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("purchase_return_items")]
public class PurchaseReturnItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("purchase_return_id")]
    [JsonPropertyName("purchase_return_id")]
    public int PurchaseReturnId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("batch")]
    [JsonPropertyName("batch")]
    public string? Batch { get; set; }

    [Column("expiry")]
    [JsonPropertyName("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("pack_size")]
    [JsonPropertyName("pack_size")]
    public int PackSize { get; set; }

    [Column("pack_purchased_quantity")]
    [JsonPropertyName("pack_purchased_quantity")]
    public int PackPurchasedQuantity { get; set; }

    [Column("return_pack_quantity")]
    [JsonPropertyName("return_pack_quantity")]
    public int ReturnPackQuantity { get; set; }

    [Column("return_unit_quantity")]
    [JsonPropertyName("return_unit_quantity")]
    public int ReturnUnitQuantity { get; set; }

    [Column("pack_purchase_price")]
    [JsonPropertyName("pack_purchase_price")]
    public decimal PackPurchasePrice { get; set; }

    [Column("unit_purchase_price")]
    [JsonPropertyName("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("item_discount_percentage")]
    [JsonPropertyName("item_discount_percentage")]
    public decimal? ItemDiscountPercentage { get; set; } = 0m;

    [Column("sub_total")]
    [JsonPropertyName("sub_total")]
    public decimal SubTotal { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(PurchaseReturnId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PurchaseReturn? PurchaseReturn { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }
}
