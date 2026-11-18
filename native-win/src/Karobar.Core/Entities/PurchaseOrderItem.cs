using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("purchase_order_items")]
public class PurchaseOrderItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("purchase_order_id")]
    [JsonPropertyName("purchase_order_id")]
    public int PurchaseOrderId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("ordered_quantity")]
    [JsonPropertyName("ordered_quantity")]
    public int OrderedQuantity { get; set; }

    [Column("pack_size")]
    [JsonPropertyName("pack_size")]
    public int PackSize { get; set; }

    [Column("unit_purchase_price")]
    [JsonPropertyName("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("item_discount_percentage")]
    [JsonPropertyName("item_discount_percentage")]
    public decimal ItemDiscountPercentage { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("unit_bonus")]
    [JsonPropertyName("unit_bonus")]
    public int UnitBonus { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }

    [ForeignKey(nameof(PurchaseOrderId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PurchaseOrder? PurchaseOrder { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<PurchaseInvoiceItem> InvoiceItems { get; set; } = new List<PurchaseInvoiceItem>();
}
