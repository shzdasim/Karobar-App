using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("products")]
public class Product
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("product_code")]
    [JsonPropertyName("product_code")]
    public string ProductCode { get; set; } = string.Empty;

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("image")]
    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [Column("formulation")]
    [JsonPropertyName("formulation")]
    public string? Formulation { get; set; }

    [Column("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Column("pack_size")]
    [JsonPropertyName("pack_size")]
    public int PackSize { get; set; }

    [Column("quantity")]
    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [Column("pack_purchase_price")]
    [JsonPropertyName("pack_purchase_price")]
    public decimal? PackPurchasePrice { get; set; }

    [Column("pack_sale_price")]
    [JsonPropertyName("pack_sale_price")]
    public decimal? PackSalePrice { get; set; }

    [Column("unit_purchase_price")]
    [JsonPropertyName("unit_purchase_price")]
    public decimal? UnitPurchasePrice { get; set; }

    [Column("unit_sale_price")]
    [JsonPropertyName("unit_sale_price")]
    public decimal? UnitSalePrice { get; set; }

    [Column("avg_price")]
    [JsonPropertyName("avg_price")]
    public decimal? AvgPrice { get; set; }

    [Column("margin")]
    [JsonPropertyName("margin")]
    public decimal? Margin { get; set; }

    [Column("narcotic")]
    [JsonPropertyName("narcotic")]
    public string Narcotic { get; set; } = "no";

    [Column("max_discount")]
    [JsonPropertyName("max_discount")]
    public int? MaxDiscount { get; set; }

    [Column("category_id")]
    [JsonPropertyName("category_id")]
    public int CategoryId { get; set; }

    [Column("brand_id")]
    [JsonPropertyName("brand_id")]
    public int BrandId { get; set; }

    [Column("supplier_id")]
    [JsonPropertyName("supplier_id")]
    public int? SupplierId { get; set; }

    [Column("rack")]
    [JsonPropertyName("rack")]
    public string? Rack { get; set; }

    [Column("barcode")]
    [JsonPropertyName("barcode")]
    public string Barcode { get; set; } = string.Empty;

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

    [Column("expiry_warning_days")]
    [JsonPropertyName("expiry_warning_days")]
    public int? ExpiryWarningDays { get; set; }

    [Column("storage_location_id")]
    [JsonPropertyName("storage_location_id")]
    public int? StorageLocationId { get; set; }

    [Column("expiry_critical_days")]
    [JsonPropertyName("expiry_critical_days")]
    public int? ExpiryCriticalDays { get; set; }

    [Column("hs_code")]
    [JsonPropertyName("hs_code")]
    public string? HsCode { get; set; }

    [Column("tax_type")]
    [JsonPropertyName("tax_type")]
    public string TaxType { get; set; } = "exempt";

    [Column("sales_tax_rate")]
    [JsonPropertyName("sales_tax_rate")]
    public decimal SalesTaxRate { get; set; }

    [Column("further_tax_rate")]
    [JsonPropertyName("further_tax_rate")]
    public decimal FurtherTaxRate { get; set; }

    [Column("sro_schedule_no")]
    [JsonPropertyName("sro_schedule_no")]
    public string? SroScheduleNo { get; set; }

    [Column("sro_item_serial_no")]
    [JsonPropertyName("sro_item_serial_no")]
    public string? SroItemSerialNo { get; set; }

    [Column("drug_schedule")]
    [JsonPropertyName("drug_schedule")]
    public string? DrugSchedule { get; set; } = "otc";

    [Column("requires_cold_chain")]
    [JsonPropertyName("requires_cold_chain")]
    public bool RequiresColdChain { get; set; }

    [Column("default_dosage")]
    [JsonPropertyName("default_dosage")]
    public string? DefaultDosage { get; set; }

    [ForeignKey(nameof(StorageLocationId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StorageLocation? StorageLocation { get; set; }

    [ForeignKey(nameof(BrandId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Brand? Brand { get; set; }

    [ForeignKey(nameof(CategoryId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Category? Category { get; set; }

    [ForeignKey(nameof(SupplierId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Supplier? Supplier { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<ProductSupplier> ProductSuppliers { get; set; } = new List<ProductSupplier>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<Batch> Batches { get; set; } = new List<Batch>();

    public void RefreshMargins()
    {
        var avg = AvgPrice ?? 0m;
        var retail = UnitSalePrice ?? 0m;
        var wholesale = WholeSaleUnitPrice ?? 0m;

        Margin = (retail > 0m && avg > 0m)
            ? Math.Round(((retail - avg) / retail) * 100m, 2)
            : 0m;

        WholeSaleMargin = (wholesale > 0m && avg > 0m)
            ? Math.Round(((wholesale - avg) / wholesale) * 100m, 2)
            : 0m;
    }
}
