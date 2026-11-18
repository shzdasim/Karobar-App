using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("tax_rates")]
public class TaxRate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("code")]
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [Column("rate")]
    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [Column("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = "both";

    [Column("is_default")]
    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

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

}
