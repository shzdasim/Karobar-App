using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("journal_entries")]
public class JournalEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("entry_number")]
    [JsonPropertyName("entry_number")]
    public string EntryNumber { get; set; } = string.Empty;

    [Column("entry_date")]
    [JsonPropertyName("entry_date")]
    public DateTime EntryDate { get; set; }

    [Column("reference_type")]
    [JsonPropertyName("reference_type")]
    public string ReferenceType { get; set; } = "manual";

    [Column("reference_number")]
    [JsonPropertyName("reference_number")]
    public string? ReferenceNumber { get; set; }

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int? AccountId { get; set; }

    [Column("description")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [Column("total_debit")]
    [JsonPropertyName("total_debit")]
    public decimal TotalDebit { get; set; }

    [Column("total_credit")]
    [JsonPropertyName("total_credit")]
    public decimal TotalCredit { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "posted";

    [Column("created_by")]
    [JsonPropertyName("created_by")]
    public int? CreatedBy { get; set; }

    [Column("reversed_by")]
    [JsonPropertyName("reversed_by")]
    public int? ReversedBy { get; set; }

    [Column("reversed_at")]
    [JsonPropertyName("reversed_at")]
    public DateTime? ReversedAt { get; set; }

    [Column("reversal_reason")]
    [JsonPropertyName("reversal_reason")]
    public string? ReversalReason { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();

    [ForeignKey(nameof(CreatedBy))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? Creator { get; set; }

    [ForeignKey(nameof(ReversedBy))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? ReversedByUser { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? TreasuryAccount { get; set; }
}
