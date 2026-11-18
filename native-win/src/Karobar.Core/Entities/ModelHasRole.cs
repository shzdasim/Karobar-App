using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("model_has_roles")]
public class ModelHasRole
{
    [Column("role_id")]
    [JsonPropertyName("role_id")]
    public int RoleId { get; set; }

    [Column("model_type")]
    [JsonPropertyName("model_type")]
    public string ModelType { get; set; } = string.Empty;

    [Column("model_id")]
    [JsonPropertyName("model_id")]
    public int ModelId { get; set; }

    [ForeignKey(nameof(RoleId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Role? Role { get; set; }
}
