using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("cash_drawer_transactions")]
public class CashDrawerTransaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("session_id")]
    [JsonPropertyName("session_id")]
    public int SessionId { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [Column("amount")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [Column("reference_type")]
    [JsonPropertyName("reference_type")]
    public string? ReferenceType { get; set; }

    [Column("reference_id")]
    [JsonPropertyName("reference_id")]
    public int? ReferenceId { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("terminal_id")]
    [JsonPropertyName("terminal_id")]
    public int? TerminalId { get; set; }

    [ForeignKey(nameof(SessionId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual CashDrawerSession? Session { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }
}
