using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("inventory_movements")]
public class InventoryMovement
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("batch_id")]
    [JsonPropertyName("batch_id")]
    public int? BatchId { get; set; }

    [Column("batch_number")]
    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("transaction_type")]
    [JsonPropertyName("transaction_type")]
    public string TransactionType { get; set; } = string.Empty;

    [Column("transaction_id")]
    [JsonPropertyName("transaction_id")]
    public int? TransactionId { get; set; }

    [Column("transaction_item_id")]
    [JsonPropertyName("transaction_item_id")]
    public int? TransactionItemId { get; set; }

    [Column("reference_number")]
    [JsonPropertyName("reference_number")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Column("quantity_in")]
    [JsonPropertyName("quantity_in")]
    public decimal QuantityIn { get; set; }

    [Column("quantity_out")]
    [JsonPropertyName("quantity_out")]
    public decimal QuantityOut { get; set; }

    [Column("balance_after")]
    [JsonPropertyName("balance_after")]
    public decimal BalanceAfter { get; set; }

    [Column("unit")]
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "unit";

    [Column("conversion_factor")]
    [JsonPropertyName("conversion_factor")]
    public decimal ConversionFactor { get; set; } = 1m;

    [Column("reason")]
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}
