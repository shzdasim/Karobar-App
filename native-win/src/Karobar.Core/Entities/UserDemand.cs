using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("user_demands")]
public class UserDemand
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("product_id")]
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [Column("requested_by")]
    [JsonPropertyName("requested_by")]
    public int? RequestedBy { get; set; }

    [Column("requested_name")]
    [JsonPropertyName("requested_name")]
    public string RequestedName { get; set; } = string.Empty;

    [Column("requested_quantity")]
    [JsonPropertyName("requested_quantity")]
    public int RequestedQuantity { get; set; } = 1;

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [Column("last_notified_at")]
    [JsonPropertyName("last_notified_at")]
    public DateTime? LastNotifiedAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("customer_id")]
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [ForeignKey(nameof(ProductId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Product? Product { get; set; }

    [ForeignKey(nameof(CustomerId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Customer? Customer { get; set; }

    [ForeignKey(nameof(RequestedBy))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? Requester { get; set; }
}
