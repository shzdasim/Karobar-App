using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("trade_scheme_slabs")]
public class TradeSchemeSlab
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("trade_scheme_id")]
    [JsonPropertyName("trade_scheme_id")]
    public int TradeSchemeId { get; set; }

    [Column("min_qty")]
    [JsonPropertyName("min_qty")]
    public decimal MinQty { get; set; }

    [Column("max_qty")]
    [JsonPropertyName("max_qty")]
    public decimal? MaxQty { get; set; }

    [Column("bonus_qty")]
    [JsonPropertyName("bonus_qty")]
    public decimal BonusQty { get; set; }

    [Column("bonus_product_id")]
    [JsonPropertyName("bonus_product_id")]
    public int? BonusProductId { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(TradeSchemeId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual TradeScheme? TradeScheme { get; set; }

    [ForeignKey(nameof(BonusProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? BonusProduct { get; set; }
}
