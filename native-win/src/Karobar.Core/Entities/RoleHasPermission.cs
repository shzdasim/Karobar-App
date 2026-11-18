using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("role_has_permissions")]
public class RoleHasPermission
{
    [Column("permission_id")]
    [JsonPropertyName("permission_id")]
    public int PermissionId { get; set; }

    [Column("role_id")]
    [JsonPropertyName("role_id")]
    public int RoleId { get; set; }

    [ForeignKey(nameof(PermissionId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Permission? Permission { get; set; }

    [ForeignKey(nameof(RoleId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Role? Role { get; set; }
}
