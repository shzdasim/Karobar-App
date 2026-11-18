using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("document_histories")]
public class DocumentHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("entity_type")]
    [JsonPropertyName("entity_type")]
    public string EntityType { get; set; } = string.Empty;

    [Column("entity_id")]
    [JsonPropertyName("entity_id")]
    public int EntityId { get; set; }

    [Column("action")]
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [Column("reason")]
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [Column("before_payload")]
    [JsonPropertyName("before_payload")]
    public string? BeforePayload { get; set; }

    [Column("after_payload")]
    [JsonPropertyName("after_payload")]
    public string? AfterPayload { get; set; }

    [Column("diff")]
    [JsonPropertyName("diff")]
    public string? Diff { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("terminal_id")]
    [JsonPropertyName("terminal_id")]
    public int? TerminalId { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }
}
