using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("cache")]
public class CacheEntry
{
    [Key]
    [Column("key")]
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [Column("value")]
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [Column("expiration")]
    [JsonPropertyName("expiration")]
    public long Expiration { get; set; }

}
