using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("stock_adjustments")]
public class StockAdjustment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("posted_number")]
    [JsonPropertyName("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("posted_date")]
    [JsonPropertyName("posted_date")]
    public DateTime PostedDate { get; set; }

    [Column("note")]
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [Column("total_worth")]
    [JsonPropertyName("total_worth")]
    public decimal TotalWorth { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<StockAdjustmentItem> Items { get; set; } = new List<StockAdjustmentItem>();

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StocktakeSession? StocktakeSession { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StockDisposal? StockDisposal { get; set; }
}
