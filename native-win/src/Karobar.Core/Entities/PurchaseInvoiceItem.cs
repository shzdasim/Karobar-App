using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("purchase_invoice_items")]
public class PurchaseInvoiceItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("purchase_invoice_id")]
    [JsonPropertyName("purchase_invoice_id")]
    public int PurchaseInvoiceId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("batch")]
    [JsonPropertyName("batch")]
    public string? Batch { get; set; }

    [Column("expiry")]
    [JsonPropertyName("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("pack_quantity")]
    [JsonPropertyName("pack_quantity")]
    public int PackQuantity { get; set; }

    [Column("pack_size")]
    [JsonPropertyName("pack_size")]
    public int? PackSize { get; set; }

    [Column("unit_quantity")]
    [JsonPropertyName("unit_quantity")]
    public int UnitQuantity { get; set; }

    [Column("pack_purchase_price")]
    [JsonPropertyName("pack_purchase_price")]
    public decimal PackPurchasePrice { get; set; }

    [Column("unit_purchase_price")]
    [JsonPropertyName("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("pack_sale_price")]
    [JsonPropertyName("pack_sale_price")]
    public decimal PackSalePrice { get; set; }

    [Column("unit_sale_price")]
    [JsonPropertyName("unit_sale_price")]
    public decimal UnitSalePrice { get; set; }

    [Column("pack_bonus")]
    [JsonPropertyName("pack_bonus")]
    public int? PackBonus { get; set; }

    [Column("unit_bonus")]
    [JsonPropertyName("unit_bonus")]
    public int? UnitBonus { get; set; }

    [Column("item_discount_percentage")]
    [JsonPropertyName("item_discount_percentage")]
    public decimal? ItemDiscountPercentage { get; set; }

    [Column("margin")]
    [JsonPropertyName("margin")]
    public decimal Margin { get; set; }

    [Column("sub_total")]
    [JsonPropertyName("sub_total")]
    public decimal SubTotal { get; set; }

    [Column("avg_price")]
    [JsonPropertyName("avg_price")]
    public decimal AvgPrice { get; set; }

    [Column("quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("whole_sale_pack_price")]
    [JsonPropertyName("whole_sale_pack_price")]
    public decimal? WholeSalePackPrice { get; set; }

    [Column("whole_sale_unit_price")]
    [JsonPropertyName("whole_sale_unit_price")]
    public decimal? WholeSaleUnitPrice { get; set; }

    [Column("whole_sale_margin")]
    [JsonPropertyName("whole_sale_margin")]
    public decimal? WholeSaleMargin { get; set; }

    [Column("purchase_order_item_id")]
    [JsonPropertyName("purchase_order_item_id")]
    public int? PurchaseOrderItemId { get; set; }

    [ForeignKey(nameof(PurchaseOrderItemId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PurchaseOrderItem? PurchaseOrderItem { get; set; }

    [ForeignKey(nameof(PurchaseInvoiceId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }
}
