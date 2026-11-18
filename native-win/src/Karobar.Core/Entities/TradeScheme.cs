using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("trade_schemes")]
public class TradeScheme
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("scheme_type")]
    [JsonPropertyName("scheme_type")]
    public string SchemeType { get; set; } = "ratio";

    [Column("target_type")]
    [JsonPropertyName("target_type")]
    public string TargetType { get; set; } = "product";

    [Column("target_id")]
    [JsonPropertyName("target_id")]
    public int TargetId { get; set; }

    [Column("bonus_type")]
    [JsonPropertyName("bonus_type")]
    public string BonusType { get; set; } = "pack";

    [Column("customer_id")]
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [Column("sale_type")]
    [JsonPropertyName("sale_type")]
    public string SaleType { get; set; } = "all";

    [Column("start_date")]
    [JsonPropertyName("start_date")]
    public DateTime? StartDate { get; set; }

    [Column("end_date")]
    [JsonPropertyName("end_date")]
    public DateTime? EndDate { get; set; }

    [Column("is_active")]
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("priority")]
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<TradeSchemeSlab> Slabs { get; set; } = new List<TradeSchemeSlab>();

    [ForeignKey(nameof(CustomerId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Customer? Customer { get; set; }
}
