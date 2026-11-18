using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("brands")]
public class Brand
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("image")]
    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

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

    [NotMapped]
    [JsonPropertyName("expiry_policy_label")]
    public string ExpiryPolicyLabel => FormatExpiryPolicyLabel(ExpiryReturnPolicy, ExpiryReturnMonths, ExpiryReturnDays);

    public static string FormatExpiryPolicyLabel(string? policy, int? months, int? days)
    {
        var resolved = string.IsNullOrWhiteSpace(policy) ? "before_expiry" : policy;
        if (resolved == "accepts_expired") return "Accepts Expired Products";
        if (resolved == "non_returnable") return "Non-Returnable";

        if (months is > 0)
        {
            if (months.Value % 12 == 0)
            {
                var years = months.Value / 12;
                return years == 1 ? "1 Year Before Expiry" : $"{years} Years Before Expiry";
            }
            return months.Value == 1 ? "1 Month Before Expiry" : $"{months.Value} Months Before Expiry";
        }

        if (days is > 0)
        {
            if (days.Value == 365) return "1 Year Before Expiry";
            if (days.Value == 730) return "2 Years Before Expiry";
            if (days.Value % 30 == 0)
            {
                var calcMonths = days.Value / 30;
                return calcMonths == 1 ? "1 Month Before Expiry" : $"{calcMonths} Months Before Expiry";
            }
            return $"{days.Value} Days Before Expiry";
        }

        return "3 Months Before Expiry";
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
