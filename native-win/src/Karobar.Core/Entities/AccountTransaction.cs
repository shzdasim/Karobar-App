using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("account_transactions")]
public class AccountTransaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int AccountId { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [Column("amount")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [Column("balance_after")]
    [JsonPropertyName("balance_after")]
    public decimal BalanceAfter { get; set; }

    [Column("reference_type")]
    [JsonPropertyName("reference_type")]
    public string? ReferenceType { get; set; }

    [Column("reference_id")]
    [JsonPropertyName("reference_id")]
    public int? ReferenceId { get; set; }

    [Column("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Column("transaction_date")]
    [JsonPropertyName("transaction_date")]
    public DateTime TransactionDate { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }
}
