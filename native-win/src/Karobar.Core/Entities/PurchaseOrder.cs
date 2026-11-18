using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("purchase_orders")]
public class PurchaseOrder
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("number")]
    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [Column("date")]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [Column("supplier_id")]
    [JsonPropertyName("supplier_id")]
    public int SupplierId { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ordered";

    [Column("reference")]
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_by")]
    [JsonPropertyName("created_by")]
    public int? CreatedBy { get; set; }

    [Column("updated_by")]
    [JsonPropertyName("updated_by")]
    public int? UpdatedBy { get; set; }

    [Column("received_by")]
    [JsonPropertyName("received_by")]
    public int? ReceivedBy { get; set; }

    [Column("received_at")]
    [JsonPropertyName("received_at")]
    public DateTime? ReceivedAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("expected_received_date")]
    [JsonPropertyName("expected_received_date")]
    public DateTime? ExpectedReceivedDate { get; set; }

    [NotMapped]
    public string FormattedNumber => string.IsNullOrWhiteSpace(Number) ? $"PO-{Id:D5}" : Number!;

    [ForeignKey(nameof(SupplierId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Supplier? Supplier { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<PurchaseInvoice> Invoices { get; set; } = new List<PurchaseInvoice>();
}
