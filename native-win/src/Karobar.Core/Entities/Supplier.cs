using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("suppliers")]
public class Supplier
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("address")]
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [Column("phone")]
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

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

    [Column("email")]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [Column("tax_number")]
    [JsonPropertyName("tax_number")]
    public string? TaxNumber { get; set; }

    [Column("tax_status")]
    [JsonPropertyName("tax_status")]
    public string TaxStatus { get; set; } = "registered";

    [Column("expiry_return_policy")]
    [JsonPropertyName("expiry_return_policy")]
    public string ExpiryReturnPolicy { get; set; } = "before_expiry";

    [Column("expiry_return_months")]
    [JsonPropertyName("expiry_return_months")]
    public int? ExpiryReturnMonths { get; set; } = 3;

    [Column("expiry_return_days")]
    [JsonPropertyName("expiry_return_days")]
    public int? ExpiryReturnDays { get; set; } = 90;

    [Column("expiry_return_notes")]
    [JsonPropertyName("expiry_return_notes")]
    public string? ExpiryReturnNotes { get; set; }

    [Column("strn")]
    [JsonPropertyName("strn")]
    public string? Strn { get; set; }

    [Column("province")]
    [JsonPropertyName("province")]
    public string? Province { get; set; }

    [NotMapped]
    [JsonPropertyName("expiry_policy_label")]
    public string ExpiryPolicyLabel => Brand.FormatExpiryPolicyLabel(ExpiryReturnPolicy, ExpiryReturnMonths, ExpiryReturnDays);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<SupplierLedger> LedgerEntries { get; set; } = new List<SupplierLedger>();
}
