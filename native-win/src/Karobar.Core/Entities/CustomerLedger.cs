using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("customer_ledgers")]
public class CustomerLedger
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("customer_id")]
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [Column("sale_invoice_id")]
    [JsonPropertyName("sale_invoice_id")]
    public int? SaleInvoiceId { get; set; }

    [Column("entry_date")]
    [JsonPropertyName("entry_date")]
    public DateTime EntryDate { get; set; }

    [Column("posted_number")]
    [JsonPropertyName("posted_number")]
    public string? PostedNumber { get; set; }

    [Column("invoice_number")]
    [JsonPropertyName("invoice_number")]
    public string? InvoiceNumber { get; set; }

    [Column("invoice_total")]
    [JsonPropertyName("invoice_total")]
    public decimal InvoiceTotal { get; set; }

    [Column("total_received")]
    [JsonPropertyName("total_received")]
    public decimal TotalReceived { get; set; }

    [Column("balance_remaining")]
    [JsonPropertyName("balance_remaining")]
    public decimal BalanceRemaining { get; set; }

    [Column("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Column("is_manual")]
    [JsonPropertyName("is_manual")]
    public bool IsManual { get; set; }

    [Column("entry_type")]
    [JsonPropertyName("entry_type")]
    public string EntryType { get; set; } = "invoice";

    [Column("credited_amount")]
    [JsonPropertyName("credited_amount")]
    public decimal CreditedAmount { get; set; }

    [Column("payment_ref")]
    [JsonPropertyName("payment_ref")]
    public string? PaymentRef { get; set; }

    [Column("created_by")]
    [JsonPropertyName("created_by")]
    public int? CreatedBy { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("payment_id")]
    [JsonPropertyName("payment_id")]
    public int? PaymentId { get; set; }

    [Column("due_date")]
    [JsonPropertyName("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("sale_return_id")]
    [JsonPropertyName("sale_return_id")]
    public int? SaleReturnId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Customer? Customer { get; set; }

    [ForeignKey(nameof(SaleInvoiceId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual SaleInvoice? SaleInvoice { get; set; }

    [ForeignKey(nameof(SaleReturnId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual SaleReturn? SaleReturn { get; set; }

    [ForeignKey(nameof(PaymentId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Payment? Payment { get; set; }
}
