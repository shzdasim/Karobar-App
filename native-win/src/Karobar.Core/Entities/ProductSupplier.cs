using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("product_suppliers")]
public class ProductSupplier
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("supplier_id")]
    [JsonPropertyName("supplier_id")]
    public int SupplierId { get; set; }

    [Column("supplier_sku")]
    [JsonPropertyName("supplier_sku")]
    public string? SupplierSku { get; set; }

    [Column("last_purchase_price")]
    [JsonPropertyName("last_purchase_price")]
    public decimal? LastPurchasePrice { get; set; }

    [Column("lead_time_days")]
    [JsonPropertyName("lead_time_days")]
    public int? LeadTimeDays { get; set; }

    [Column("min_order_qty")]
    [JsonPropertyName("min_order_qty")]
    public int MinOrderQty { get; set; } = 1;

    [Column("is_preferred")]
    [JsonPropertyName("is_preferred")]
    public bool IsPreferred { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }

    [ForeignKey(nameof(SupplierId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Supplier? Supplier { get; set; }
}
