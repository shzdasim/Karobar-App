using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("cache_locks")]
public class CacheLock
{
    [Key]
    [Column("key")]
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [Column("owner")]
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [Column("expiration")]
    [JsonPropertyName("expiration")]
    public long Expiration { get; set; }

}
