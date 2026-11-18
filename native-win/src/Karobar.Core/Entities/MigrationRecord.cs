using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("migrations")]
public class MigrationRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("migration")]
    [JsonPropertyName("migration")]
    public string Migration { get; set; } = string.Empty;

    [Column("batch")]
    [JsonPropertyName("batch")]
    public int Batch { get; set; }

}
