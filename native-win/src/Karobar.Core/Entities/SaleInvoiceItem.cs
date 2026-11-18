using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("sale_invoice_items")]
public class SaleInvoiceItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("sale_invoice_id")]
    [JsonPropertyName("sale_invoice_id")]
    public int SaleInvoiceId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("pack_size")]
    [JsonPropertyName("pack_size")]
    public int PackSize { get; set; }

    [Column("batch_number")]
    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry")]
    [JsonPropertyName("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("current_quantity")]
    [JsonPropertyName("current_quantity")]
    public int CurrentQuantity { get; set; }

    [Column("quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [Column("price")]
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

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

    [Column("sale_type")]
    [JsonPropertyName("sale_type")]
    public string SaleType { get; set; } = "retail";

    [Column("is_custom_price")]
    [JsonPropertyName("is_custom_price")]
    public bool IsCustomPrice { get; set; }

    [Column("cost_price_snapshot")]
    [JsonPropertyName("cost_price_snapshot")]
    public decimal? CostPriceSnapshot { get; set; }

    [Column("pack_quantity")]
    [JsonPropertyName("pack_quantity")]
    public decimal? PackQuantity { get; set; } = 0m;

    [Column("unit_quantity")]
    [JsonPropertyName("unit_quantity")]
    public int? UnitQuantity { get; set; } = 0;

    [Column("bonus")]
    [JsonPropertyName("bonus")]
    public int? Bonus { get; set; } = 0;

    [Column("pack_price")]
    [JsonPropertyName("pack_price")]
    public decimal? PackPrice { get; set; } = 0m;

    [Column("pack_bonus")]
    [JsonPropertyName("pack_bonus")]
    public decimal? PackBonus { get; set; } = 0m;

    [Column("unit_bonus")]
    [JsonPropertyName("unit_bonus")]
    public int? UnitBonus { get; set; } = 0;

    [Column("applied_scheme_id")]
    [JsonPropertyName("applied_scheme_id")]
    public int? AppliedSchemeId { get; set; }

    [Column("applied_scheme_name")]
    [JsonPropertyName("applied_scheme_name")]
    public string? AppliedSchemeName { get; set; }

    [Column("hs_code")]
    [JsonPropertyName("hs_code")]
    public string? HsCode { get; set; }

    [Column("tax_percentage")]
    [JsonPropertyName("tax_percentage")]
    public decimal TaxPercentage { get; set; }

    [Column("tax_amount")]
    [JsonPropertyName("tax_amount")]
    public decimal TaxAmount { get; set; }

    [Column("further_tax_amount")]
    [JsonPropertyName("further_tax_amount")]
    public decimal FurtherTaxAmount { get; set; }

    [Column("retail_mrp")]
    [JsonPropertyName("retail_mrp")]
    public decimal? RetailMrp { get; set; }

    [Column("dosage_instruction")]
    [JsonPropertyName("dosage_instruction")]
    public string? DosageInstruction { get; set; }

    [ForeignKey(nameof(SaleInvoiceId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual SaleInvoice? SaleInvoice { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }

    [ForeignKey(nameof(AppliedSchemeId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual TradeScheme? AppliedScheme { get; set; }
}
