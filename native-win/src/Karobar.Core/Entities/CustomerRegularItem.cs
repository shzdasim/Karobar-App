using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("customer_regular_items")]
public class CustomerRegularItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("customer_id")]
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("pack_quantity")]
    [JsonPropertyName("pack_quantity")]
    public int PackQuantity { get; set; }

    [Column("unit_quantity")]
    [JsonPropertyName("unit_quantity")]
    public int UnitQuantity { get; set; } = 1;

    [Column("dosage_notes")]
    [JsonPropertyName("dosage_notes")]
    public string? DosageNotes { get; set; }

    [ForeignKey(nameof(CustomerId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Customer? Customer { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }
}
