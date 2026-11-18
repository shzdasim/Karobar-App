using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("batches")]
public class Batch
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("batch_number")]
    [JsonPropertyName("batch_number")]
    public string BatchNumber { get; set; } = string.Empty;

    [Column("expiry_date")]
    [JsonPropertyName("expiry_date")]
    public DateTime ExpiryDate { get; set; }

    [Column("quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("pack_purchase_price")]
    [JsonPropertyName("pack_purchase_price")]
    public decimal? PackPurchasePrice { get; set; }

    [Column("unit_purchase_price")]
    [JsonPropertyName("unit_purchase_price")]
    public decimal? UnitPurchasePrice { get; set; }

    [Column("pack_sale_price")]
    [JsonPropertyName("pack_sale_price")]
    public decimal? PackSalePrice { get; set; }

    [Column("unit_sale_price")]
    [JsonPropertyName("unit_sale_price")]
    public decimal? UnitSalePrice { get; set; }

    [Column("whole_sale_pack_price")]
    [JsonPropertyName("whole_sale_pack_price")]
    public decimal? WholeSalePackPrice { get; set; }

    [Column("whole_sale_unit_price")]
    [JsonPropertyName("whole_sale_unit_price")]
    public decimal? WholeSaleUnitPrice { get; set; }

    [Column("storage_location_id")]
    [JsonPropertyName("storage_location_id")]
    public int? StorageLocationId { get; set; }

    [Column("rack")]
    [JsonPropertyName("rack")]
    public string? Rack { get; set; }

    [NotMapped]
    [JsonPropertyName("expiry")]
    public string Expiry
    {
        get => ExpiryDate.ToString("yyyy-MM-dd");
        set
        {
            if (DateTime.TryParse(value, out var dt))
                ExpiryDate = dt;
        }
    }

    [NotMapped]
    [JsonPropertyName("available_units")]
    public int AvailableUnits
    {
        get => Quantity;
        set => Quantity = value;
    }

    [NotMapped]
    [JsonPropertyName("is_expired")]
    public bool IsExpired => ExpiryDate.Date < DateTime.Today;

    [NotMapped]
    [JsonPropertyName("days_remaining")]
    public int DaysRemaining => (int)(ExpiryDate.Date - DateTime.Today).TotalDays;

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }

    [ForeignKey(nameof(StorageLocationId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StorageLocation? StorageLocation { get; set; }
}
