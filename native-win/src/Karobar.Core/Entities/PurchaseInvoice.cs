using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("purchase_invoices")]
public class PurchaseInvoice
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

    [Column("posted_date")]
    [JsonPropertyName("posted_date")]
    public DateTime PostedDate { get; set; }

    [Column("remarks")]
    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [Column("invoice_number")]
    [JsonPropertyName("invoice_number")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Column("invoice_amount")]
    [JsonPropertyName("invoice_amount")]
    public decimal InvoiceAmount { get; set; }

    [Column("tax_percentage")]
    [JsonPropertyName("tax_percentage")]
    public decimal? TaxPercentage { get; set; }

    [Column("tax_amount")]
    [JsonPropertyName("tax_amount")]
    public decimal? TaxAmount { get; set; }

    [Column("discount_percentage")]
    [JsonPropertyName("discount_percentage")]
    public decimal? DiscountPercentage { get; set; }

    [Column("discount_amount")]
    [JsonPropertyName("discount_amount")]
    public decimal? DiscountAmount { get; set; }

    [Column("total_amount")]
    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("total_paid")]
    [JsonPropertyName("total_paid")]
    public decimal TotalPaid { get; set; }

    [Column("invoice_type")]
    [JsonPropertyName("invoice_type")]
    public string InvoiceType { get; set; } = "debit";

    [Column("purchase_order_id")]
    [JsonPropertyName("purchase_order_id")]
    public int? PurchaseOrderId { get; set; }

    [Column("receipt_key")]
    [JsonPropertyName("receipt_key")]
    public string? ReceiptKey { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "posted";

    [Column("reversed_at")]
    [JsonPropertyName("reversed_at")]
    public DateTime? ReversedAt { get; set; }

    [Column("reversed_by_id")]
    [JsonPropertyName("reversed_by_id")]
    public int? ReversedById { get; set; }

    [Column("reversal_reason")]
    [JsonPropertyName("reversal_reason")]
    public string? ReversalReason { get; set; }

    [Column("payment_method")]
    [JsonPropertyName("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("due_date")]
    [JsonPropertyName("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int? AccountId { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("employee_id")]
    [JsonPropertyName("employee_id")]
    public int? EmployeeId { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Employee? Employee { get; set; }

    [ForeignKey(nameof(ReversedById))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? ReversedBy { get; set; }

    [ForeignKey(nameof(PurchaseOrderId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PurchaseOrder? PurchaseOrder { get; set; }

    [ForeignKey(nameof(SupplierId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Supplier? Supplier { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<PurchaseInvoiceItem> Items { get; set; } = new List<PurchaseInvoiceItem>();
}
