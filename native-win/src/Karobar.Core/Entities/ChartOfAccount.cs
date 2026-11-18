using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("chart_of_accounts")]
public class ChartOfAccount
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("code")]
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("account_type")]
    [JsonPropertyName("account_type")]
    public string AccountType { get; set; } = string.Empty;

    [Column("sub_type")]
    [JsonPropertyName("sub_type")]
    public string? SubType { get; set; }

    [Column("normal_balance")]
    [JsonPropertyName("normal_balance")]
    public string NormalBalance { get; set; } = "debit";

    [Column("system_key")]
    [JsonPropertyName("system_key")]
    public string? SystemKey { get; set; }

    [Column("opening_balance")]
    [JsonPropertyName("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [Column("is_system")]
    [JsonPropertyName("is_system")]
    public bool IsSystem { get; set; }

    [Column("is_active")]
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<JournalEntryLine> JournalLines { get; set; } = new List<JournalEntryLine>();
}
