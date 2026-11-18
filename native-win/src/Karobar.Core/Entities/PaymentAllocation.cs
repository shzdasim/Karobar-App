using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("payment_allocations")]
public class PaymentAllocation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("payment_id")]
    [JsonPropertyName("payment_id")]
    public int PaymentId { get; set; }

    [Column("invoice_type")]
    [JsonPropertyName("invoice_type")]
    public string InvoiceType { get; set; } = string.Empty;

    [Column("invoice_id")]
    [JsonPropertyName("invoice_id")]
    public int InvoiceId { get; set; }

    [Column("amount")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(PaymentId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Payment? Payment { get; set; }
}
