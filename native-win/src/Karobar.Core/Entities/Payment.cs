using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("payments")]
public class Payment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("payment_number")]
    [JsonPropertyName("payment_number")]
    public string PaymentNumber { get; set; } = string.Empty;

    [Column("party_type")]
    [JsonPropertyName("party_type")]
    public string PartyType { get; set; } = string.Empty;

    [Column("party_id")]
    [JsonPropertyName("party_id")]
    public int PartyId { get; set; }

    [Column("payment_date")]
    [JsonPropertyName("payment_date")]
    public DateTime PaymentDate { get; set; }

    [Column("amount")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [Column("allocated_amount")]
    [JsonPropertyName("allocated_amount")]
    public decimal AllocatedAmount { get; set; }

    [Column("unapplied_amount")]
    [JsonPropertyName("unapplied_amount")]
    public decimal UnappliedAmount { get; set; }

    [Column("payment_method")]
    [JsonPropertyName("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("reference")]
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "posted";

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("idempotency_key")]
    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; set; }

    [Column("created_by_id")]
    [JsonPropertyName("created_by_id")]
    public int? CreatedById { get; set; }

    [Column("reversed_at")]
    [JsonPropertyName("reversed_at")]
    public DateTime? ReversedAt { get; set; }

    [Column("reversed_by_id")]
    [JsonPropertyName("reversed_by_id")]
    public int? ReversedById { get; set; }

    [Column("reversal_reason")]
    [JsonPropertyName("reversal_reason")]
    public string? ReversalReason { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int? AccountId { get; set; }

    [Column("cheque_number")]
    [JsonPropertyName("cheque_number")]
    public string? ChequeNumber { get; set; }

    [Column("bank_name")]
    [JsonPropertyName("bank_name")]
    public string? BankName { get; set; }

    [Column("cheque_date")]
    [JsonPropertyName("cheque_date")]
    public DateTime? ChequeDate { get; set; }

    [Column("cheque_status")]
    [JsonPropertyName("cheque_status")]
    public string? ChequeStatus { get; set; }

    [Column("cleared_at")]
    [JsonPropertyName("cleared_at")]
    public DateTime? ClearedAt { get; set; }

    [Column("bounced_at")]
    [JsonPropertyName("bounced_at")]
    public DateTime? BouncedAt { get; set; }

    [Column("wht_tax_rate_id")]
    [JsonPropertyName("wht_tax_rate_id")]
    public int? WhtTaxRateId { get; set; }

    [Column("wht_percentage")]
    [JsonPropertyName("wht_percentage")]
    public decimal WhtPercentage { get; set; }

    [Column("wht_amount")]
    [JsonPropertyName("wht_amount")]
    public decimal WhtAmount { get; set; }

    [Column("net_cash_disbursed")]
    [JsonPropertyName("net_cash_disbursed")]
    public decimal? NetCashDisbursed { get; set; }

    [Column("terminal_id")]
    [JsonPropertyName("terminal_id")]
    public int? TerminalId { get; set; }

    public bool IsReversed() => string.Equals(Status, "reversed", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [ForeignKey(nameof(CreatedById))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? Creator { get; set; }

    [ForeignKey(nameof(ReversedById))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? ReversedBy { get; set; }
}
