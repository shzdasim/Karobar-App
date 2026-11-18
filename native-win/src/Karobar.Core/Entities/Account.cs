using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("accounts")]
public class Account
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = "bank";

    [Column("bank_name")]
    [JsonPropertyName("bank_name")]
    public string? BankName { get; set; }

    [Column("account_number")]
    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; set; }

    [Column("opening_balance")]
    [JsonPropertyName("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [Column("current_balance")]
    [JsonPropertyName("current_balance")]
    public decimal CurrentBalance { get; set; }

    [Column("is_active")]
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("is_default")]
    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("code")]
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [Column("icon")]
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [Column("color")]
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<AccountTransaction> Transactions { get; set; } = new List<AccountTransaction>();
}
