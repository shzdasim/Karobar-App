using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("expenses")]
public class Expense
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("expense_number")]
    [JsonPropertyName("expense_number")]
    public string ExpenseNumber { get; set; } = string.Empty;

    [Column("expense_category_id")]
    [JsonPropertyName("expense_category_id")]
    public int ExpenseCategoryId { get; set; }

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int? AccountId { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("cash_drawer_session_id")]
    [JsonPropertyName("cash_drawer_session_id")]
    public int? CashDrawerSessionId { get; set; }

    [Column("amount")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [Column("date")]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [Column("payment_method")]
    [JsonPropertyName("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("reference_number")]
    [JsonPropertyName("reference_number")]
    public string? ReferenceNumber { get; set; }

    [Column("payee")]
    [JsonPropertyName("payee")]
    public string? Payee { get; set; }

    [Column("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Column("receipt_path")]
    [JsonPropertyName("receipt_path")]
    public string? ReceiptPath { get; set; }

    [Column("is_recurring")]
    [JsonPropertyName("is_recurring")]
    public bool IsRecurring { get; set; }

    [Column("recurring_frequency")]
    [JsonPropertyName("recurring_frequency")]
    public string? RecurringFrequency { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("deleted_at")]
    [JsonPropertyName("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [Column("terminal_id")]
    [JsonPropertyName("terminal_id")]
    public int? TerminalId { get; set; }

    [NotMapped]
    [JsonPropertyName("receipt_url")]
    public string? ReceiptUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ReceiptPath)) return null;
            if (ReceiptPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                ReceiptPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                ReceiptPath.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            {
                return ReceiptPath;
            }
            return "/storage/" + ReceiptPath.TrimStart('/');
        }
    }

    [NotMapped]
    [JsonPropertyName("next_recurring_date")]
    public string? NextRecurringDate
    {
        get
        {
            if (!IsRecurring || string.IsNullOrWhiteSpace(RecurringFrequency)) return null;
            DateTime? next = RecurringFrequency.ToLowerInvariant() switch
            {
                "weekly" => Date.Date.AddDays(7),
                "monthly" => Date.Date.AddMonths(1),
                "yearly" => Date.Date.AddYears(1),
                _ => null
            };
            return next?.ToString("yyyy-MM-dd");
        }
    }

    [ForeignKey(nameof(ExpenseCategoryId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ExpenseCategory? Category { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(CashDrawerSessionId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual CashDrawerSession? DrawerSession { get; set; }
}
