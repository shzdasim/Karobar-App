using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("employee_advances")]
public class EmployeeAdvance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("advance_number")]
    [JsonPropertyName("advance_number")]
    public string AdvanceNumber { get; set; } = string.Empty;

    [Column("employee_id")]
    [JsonPropertyName("employee_id")]
    public int EmployeeId { get; set; }

    [Column("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = "advance";

    [Column("amount")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [Column("balance_after")]
    [JsonPropertyName("balance_after")]
    public decimal BalanceAfter { get; set; }

    [Column("date")]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [Column("payment_method")]
    [JsonPropertyName("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int? AccountId { get; set; }

    [Column("payroll_item_id")]
    [JsonPropertyName("payroll_item_id")]
    public int? PayrollItemId { get; set; }

    [Column("reference_number")]
    [JsonPropertyName("reference_number")]
    public string? ReferenceNumber { get; set; }

    [Column("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Column("created_by")]
    [JsonPropertyName("created_by")]
    public int? CreatedBy { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Employee? Employee { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [ForeignKey(nameof(PayrollItemId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PayrollItem? PayrollItem { get; set; }
}
