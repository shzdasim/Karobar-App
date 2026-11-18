using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("document_sequences")]
public class DocumentSequence
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("sequence_key")]
    [JsonPropertyName("sequence_key")]
    public string SequenceKey { get; set; } = string.Empty;

    [Column("prefix")]
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [Column("padding")]
    [JsonPropertyName("padding")]
    public int Padding { get; set; } = 4;

    [Column("last_number")]
    [JsonPropertyName("last_number")]
    public int LastNumber { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

}
