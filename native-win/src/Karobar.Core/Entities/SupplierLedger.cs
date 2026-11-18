using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("supplier_ledgers")]
public class SupplierLedger
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("supplier_id")]
    [JsonPropertyName("supplier_id")]
    public int SupplierId { get; set; }

    [Column("purchase_invoice_id")]
    [JsonPropertyName("purchase_invoice_id")]
    public int? PurchaseInvoiceId { get; set; }

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

    [Column("total_paid")]
    [JsonPropertyName("total_paid")]
    public decimal TotalPaid { get; set; }

    [Column("credit_remaining")]
    [JsonPropertyName("credit_remaining")]
    public decimal CreditRemaining { get; set; }

    [Column("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Column("is_manual")]
    [JsonPropertyName("is_manual")]
    public bool IsManual { get; set; }

    [Column("created_by")]
    [JsonPropertyName("created_by")]
    public int? CreatedBy { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("entry_type")]
    [JsonPropertyName("entry_type")]
    public string EntryType { get; set; } = "invoice";

    [Column("debited_amount")]
    [JsonPropertyName("debited_amount")]
    public decimal DebitedAmount { get; set; }

    [Column("payment_ref")]
    [JsonPropertyName("payment_ref")]
    public string? PaymentRef { get; set; }

    [Column("payment_id")]
    [JsonPropertyName("payment_id")]
    public int? PaymentId { get; set; }

    [Column("due_date")]
    [JsonPropertyName("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("purchase_return_id")]
    [JsonPropertyName("purchase_return_id")]
    public int? PurchaseReturnId { get; set; }

    [ForeignKey(nameof(SupplierId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Supplier? Supplier { get; set; }

    [ForeignKey(nameof(PurchaseInvoiceId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    [ForeignKey(nameof(PurchaseReturnId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PurchaseReturn? PurchaseReturn { get; set; }

    [ForeignKey(nameof(PaymentId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Payment? Payment { get; set; }
}
