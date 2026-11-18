using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("customers")]
public class Customer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("email")]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [Column("phone")]
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [Column("address")]
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("credit_limit")]
    [JsonPropertyName("credit_limit")]
    public decimal? CreditLimit { get; set; }

    [Column("credit_days")]
    [JsonPropertyName("credit_days")]
    public int? CreditDays { get; set; } = 30;

    [Column("credit_status")]
    [JsonPropertyName("credit_status")]
    public string CreditStatus { get; set; } = "active";

    [Column("tax_number")]
    [JsonPropertyName("tax_number")]
    public string? TaxNumber { get; set; }

    [Column("tax_status")]
    [JsonPropertyName("tax_status")]
    public string TaxStatus { get; set; } = "unregistered";

    [Column("is_regular")]
    [JsonPropertyName("is_regular")]
    public bool IsRegular { get; set; }

    [Column("refill_cycle")]
    [JsonPropertyName("refill_cycle")]
    public string RefillCycle { get; set; } = "monthly";

    [Column("refill_interval_days")]
    [JsonPropertyName("refill_interval_days")]
    public int RefillIntervalDays { get; set; } = 30;

    [Column("remind_days_before")]
    [JsonPropertyName("remind_days_before")]
    public int RemindDaysBefore { get; set; } = 2;

    [Column("next_due_date")]
    [JsonPropertyName("next_due_date")]
    public DateTime? NextDueDate { get; set; }

    [Column("last_reminded_at")]
    [JsonPropertyName("last_reminded_at")]
    public DateTime? LastRemindedAt { get; set; }

    [Column("last_collected_at")]
    [JsonPropertyName("last_collected_at")]
    public DateTime? LastCollectedAt { get; set; }

    [Column("preferred_reminder_lang")]
    [JsonPropertyName("preferred_reminder_lang")]
    public string PreferredReminderLang { get; set; } = "ur";

    [Column("regular_notes")]
    [JsonPropertyName("regular_notes")]
    public string? RegularNotes { get; set; }

    [Column("refill_custom_days")]
    [JsonPropertyName("refill_custom_days")]
    public int? RefillCustomDays { get; set; }

    [Column("reminder_lead_days")]
    [JsonPropertyName("reminder_lead_days")]
    public int ReminderLeadDays { get; set; } = 2;

    [Column("next_refill_date")]
    [JsonPropertyName("next_refill_date")]
    public DateTime? NextRefillDate { get; set; }

    [Column("last_refill_reminded_at")]
    [JsonPropertyName("last_refill_reminded_at")]
    public DateTime? LastRefillRemindedAt { get; set; }

    [Column("last_refill_collected_at")]
    [JsonPropertyName("last_refill_collected_at")]
    public DateTime? LastRefillCollectedAt { get; set; }

    [Column("reminder_language")]
    [JsonPropertyName("reminder_language")]
    public string ReminderLanguage { get; set; } = "english";

    [Column("eligible_for_schemes")]
    [JsonPropertyName("eligible_for_schemes")]
    public bool EligibleForSchemes { get; set; } = true;

    [Column("cnic")]
    [JsonPropertyName("cnic")]
    public string? Cnic { get; set; }

    [Column("strn")]
    [JsonPropertyName("strn")]
    public string? Strn { get; set; }

    [Column("province")]
    [JsonPropertyName("province")]
    public string? Province { get; set; }

    public static bool IsWalkInName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(name.Trim(), @"walk[\s\-_]*in", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public bool IsWalkIn() => IsWalkInName(Name);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<SaleInvoice> SaleInvoices { get; set; } = new List<SaleInvoice>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<SaleReturn> SaleReturns { get; set; } = new List<SaleReturn>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<CustomerWholeSalePrice> WholeSalePrices { get; set; } = new List<CustomerWholeSalePrice>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<CustomerRegularItem> RegularItems { get; set; } = new List<CustomerRegularItem>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<CustomerLedger> LedgerEntries { get; set; } = new List<CustomerLedger>();
}
