using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("journal_entry_lines")]
public class JournalEntryLine
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("journal_entry_id")]
    [JsonPropertyName("journal_entry_id")]
    public int JournalEntryId { get; set; }

    [Column("chart_of_account_id")]
    [JsonPropertyName("chart_of_account_id")]
    public int ChartOfAccountId { get; set; }

    [Column("party_type")]
    [JsonPropertyName("party_type")]
    public string? PartyType { get; set; }

    [Column("party_id")]
    [JsonPropertyName("party_id")]
    public int? PartyId { get; set; }

    [Column("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Column("debit")]
    [JsonPropertyName("debit")]
    public decimal Debit { get; set; }

    [Column("credit")]
    [JsonPropertyName("credit")]
    public decimal Credit { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(JournalEntryId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual JournalEntry? JournalEntry { get; set; }

    [ForeignKey(nameof(ChartOfAccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ChartOfAccount? Account { get; set; }
}
