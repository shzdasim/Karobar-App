using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("barcode_templates")]
public class BarcodeTemplate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("paper_type")]
    [JsonPropertyName("paper_type")]
    public string PaperType { get; set; } = "roll";

    [Column("width_mm")]
    [JsonPropertyName("width_mm")]
    public decimal WidthMm { get; set; }

    [Column("height_mm")]
    [JsonPropertyName("height_mm")]
    public decimal HeightMm { get; set; }

    [Column("orientation")]
    [JsonPropertyName("orientation")]
    public string Orientation { get; set; } = "landscape";

    [Column("columns")]
    [JsonPropertyName("columns")]
    public int Columns { get; set; } = 1;

    [Column("rows")]
    [JsonPropertyName("rows")]
    public int Rows { get; set; } = 1;

    [Column("gap_x_mm")]
    [JsonPropertyName("gap_x_mm")]
    public decimal GapXMm { get; set; }

    [Column("gap_y_mm")]
    [JsonPropertyName("gap_y_mm")]
    public decimal GapYMm { get; set; }

    [Column("margin_top_mm")]
    [JsonPropertyName("margin_top_mm")]
    public decimal MarginTopMm { get; set; }

    [Column("margin_left_mm")]
    [JsonPropertyName("margin_left_mm")]
    public decimal MarginLeftMm { get; set; }

    [Column("margin_right_mm")]
    [JsonPropertyName("margin_right_mm")]
    public decimal MarginRightMm { get; set; }

    [Column("margin_bottom_mm")]
    [JsonPropertyName("margin_bottom_mm")]
    public decimal MarginBottomMm { get; set; }

    [Column("layout_options")]
    [JsonPropertyName("layout_options")]
    public string? LayoutOptions { get; set; }

    [Column("is_default")]
    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

}
