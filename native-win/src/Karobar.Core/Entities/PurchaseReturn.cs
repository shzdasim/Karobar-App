using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("purchase_returns")]
public class PurchaseReturn
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("supplier_id")]
    [JsonPropertyName("supplier_id")]
    public int SupplierId { get; set; }

    [Column("posted_number")]
    [JsonPropertyName("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("date")]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [Column("purchase_invoice_id")]
    [JsonPropertyName("purchase_invoice_id")]
    public int? PurchaseInvoiceId { get; set; }

    [Column("gross_total")]
    [JsonPropertyName("gross_total")]
    public decimal GrossTotal { get; set; }

    [Column("discount_percentage")]
    [JsonPropertyName("discount_percentage")]
    public decimal? DiscountPercentage { get; set; } = 0m;

    [Column("tax_percentage")]
    [JsonPropertyName("tax_percentage")]
    public decimal? TaxPercentage { get; set; } = 0m;

    [Column("discount_amount")]
    [JsonPropertyName("discount_amount")]
    public decimal? DiscountAmount { get; set; } = 0m;

    [Column("tax_amount")]
    [JsonPropertyName("tax_amount")]
    public decimal? TaxAmount { get; set; } = 0m;

    [Column("total")]
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    [Column("remarks")]
    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int? AccountId { get; set; }

    [Column("settlement_mode")]
    [JsonPropertyName("settlement_mode")]
    public string SettlementMode { get; set; } = "cash_refund";

    [Column("payment_method")]
    [JsonPropertyName("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("credited_amount")]
    [JsonPropertyName("credited_amount")]
    public decimal CreditedAmount { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("employee_id")]
    [JsonPropertyName("employee_id")]
    public int? EmployeeId { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Employee? Employee { get; set; }

    [ForeignKey(nameof(SupplierId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Supplier? Supplier { get; set; }

    [ForeignKey(nameof(PurchaseInvoiceId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual SupplierLedger? SupplierLedgerEntry { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();
}
