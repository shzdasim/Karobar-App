using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("cash_drawer_sessions")]
public class CashDrawerSession
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [Column("opened_at")]
    [JsonPropertyName("opened_at")]
    public DateTime OpenedAt { get; set; }

    [Column("closed_at")]
    [JsonPropertyName("closed_at")]
    public DateTime? ClosedAt { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "open";

    [Column("opening_balance")]
    [JsonPropertyName("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [Column("closing_balance_expected")]
    [JsonPropertyName("closing_balance_expected")]
    public decimal ClosingBalanceExpected { get; set; }

    [Column("closing_balance_actual")]
    [JsonPropertyName("closing_balance_actual")]
    public decimal? ClosingBalanceActual { get; set; }

    [Column("difference")]
    [JsonPropertyName("difference")]
    public decimal Difference { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("closing_denominations")]
    [JsonPropertyName("closing_denominations")]
    public string? ClosingDenominations { get; set; }

    [Column("terminal_id")]
    [JsonPropertyName("terminal_id")]
    public int? TerminalId { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(TerminalId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Terminal? Terminal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<CashDrawerTransaction> Transactions { get; set; } = new List<CashDrawerTransaction>();
}
