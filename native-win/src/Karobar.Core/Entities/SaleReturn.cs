using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("sale_returns")]
public class SaleReturn
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("sale_invoice_id")]
    [JsonPropertyName("sale_invoice_id")]
    public int? SaleInvoiceId { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [Column("customer_id")]
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [Column("posted_number")]
    [JsonPropertyName("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("date")]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [Column("discount_percentage")]
    [JsonPropertyName("discount_percentage")]
    public decimal? DiscountPercentage { get; set; } = 0m;

    [Column("discount_amount")]
    [JsonPropertyName("discount_amount")]
    public decimal? DiscountAmount { get; set; } = 0m;

    [Column("tax_percentage")]
    [JsonPropertyName("tax_percentage")]
    public decimal? TaxPercentage { get; set; } = 0m;

    [Column("tax_amount")]
    [JsonPropertyName("tax_amount")]
    public decimal? TaxAmount { get; set; } = 0m;

    [Column("total")]
    [JsonPropertyName("total")]
    public decimal? Total { get; set; } = 0m;

    [Column("gross_total")]
    [JsonPropertyName("gross_total")]
    public decimal? GrossTotal { get; set; } = 0m;

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

    [Column("remarks")]
    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [Column("terminal_id")]
    [JsonPropertyName("terminal_id")]
    public int? TerminalId { get; set; }

    [Column("fbr_invoice_number")]
    [JsonPropertyName("fbr_invoice_number")]
    public string? FbrInvoiceNumber { get; set; }

    [Column("fbr_usin")]
    [JsonPropertyName("fbr_usin")]
    public string? FbrUsin { get; set; }

    [Column("fbr_qr_data")]
    [JsonPropertyName("fbr_qr_data")]
    public string? FbrQrData { get; set; }

    [Column("fbr_sync_status")]
    [JsonPropertyName("fbr_sync_status")]
    public string FbrSyncStatus { get; set; } = "not_applicable";

    [Column("fbr_synced_at")]
    [JsonPropertyName("fbr_synced_at")]
    public DateTime? FbrSyncedAt { get; set; }

    [Column("fbr_error_message")]
    [JsonPropertyName("fbr_error_message")]
    public string? FbrErrorMessage { get; set; }

    [Column("employee_id")]
    [JsonPropertyName("employee_id")]
    public int? EmployeeId { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual CustomerLedger? CustomerLedgerEntry { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Employee? Employee { get; set; }

    [ForeignKey(nameof(CustomerId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Customer? Customer { get; set; }

    [ForeignKey(nameof(SaleInvoiceId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual SaleInvoice? SaleInvoice { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
}
