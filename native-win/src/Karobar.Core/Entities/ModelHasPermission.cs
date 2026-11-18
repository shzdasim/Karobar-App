using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("model_has_permissions")]
public class ModelHasPermission
{
    [Column("permission_id")]
    [JsonPropertyName("permission_id")]
    public int PermissionId { get; set; }

    [Column("model_type")]
    [JsonPropertyName("model_type")]
    public string ModelType { get; set; } = string.Empty;

    [Column("model_id")]
    [JsonPropertyName("model_id")]
    public int ModelId { get; set; }

    [ForeignKey(nameof(PermissionId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Permission? Permission { get; set; }
}
