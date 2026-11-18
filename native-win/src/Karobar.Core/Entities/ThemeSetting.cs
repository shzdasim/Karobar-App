using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("theme_settings")]
public class ThemeSetting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default Theme";

    [Column("primary_color")]
    [JsonPropertyName("primary_color")]
    public string PrimaryColor { get; set; } = "#3b82f6";

    [Column("primary_hover")]
    [JsonPropertyName("primary_hover")]
    public string PrimaryHover { get; set; } = "#2563eb";

    [Column("primary_light")]
    [JsonPropertyName("primary_light")]
    public string PrimaryLight { get; set; } = "#dbeafe";

    [Column("secondary_color")]
    [JsonPropertyName("secondary_color")]
    public string SecondaryColor { get; set; } = "#8b5cf6";

    [Column("secondary_hover")]
    [JsonPropertyName("secondary_hover")]
    public string SecondaryHover { get; set; } = "#7c3aed";

    [Column("secondary_light")]
    [JsonPropertyName("secondary_light")]
    public string SecondaryLight { get; set; } = "#ede9fe";

    [Column("tertiary_color")]
    [JsonPropertyName("tertiary_color")]
    public string TertiaryColor { get; set; } = "#06b6d4";

    [Column("tertiary_hover")]
    [JsonPropertyName("tertiary_hover")]
    public string TertiaryHover { get; set; } = "#0891b2";

    [Column("tertiary_light")]
    [JsonPropertyName("tertiary_light")]
    public string TertiaryLight { get; set; } = "#cffafe";

    [Column("background_color")]
    [JsonPropertyName("background_color")]
    public string BackgroundColor { get; set; } = "#f8fafc";

    [Column("surface_color")]
    [JsonPropertyName("surface_color")]
    public string SurfaceColor { get; set; } = "#ffffff";

    [Column("text_primary")]
    [JsonPropertyName("text_primary")]
    public string TextPrimary { get; set; } = "#1e293b";

    [Column("text_secondary")]
    [JsonPropertyName("text_secondary")]
    public string TextSecondary { get; set; } = "#64748b";

    [Column("success_color")]
    [JsonPropertyName("success_color")]
    public string SuccessColor { get; set; } = "#10b981";

    [Column("warning_color")]
    [JsonPropertyName("warning_color")]
    public string WarningColor { get; set; } = "#f59e0b";

    [Column("danger_color")]
    [JsonPropertyName("danger_color")]
    public string DangerColor { get; set; } = "#ef4444";

    [Column("border_color")]
    [JsonPropertyName("border_color")]
    public string BorderColor { get; set; } = "#e2e8f0";

    [Column("shadow_color")]
    [JsonPropertyName("shadow_color")]
    public string ShadowColor { get; set; } = "#1e293b";

    [Column("is_active")]
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("button_style")]
    [JsonPropertyName("button_style")]
    public string ButtonStyle { get; set; } = "rounded";

    [Column("sidebar_template")]
    [JsonPropertyName("sidebar_template")]
    public string SidebarTemplate { get; set; } = "classic";

    [Column("topbar_template")]
    [JsonPropertyName("topbar_template")]
    public string TopbarTemplate { get; set; } = "classic";

    public Dictionary<string, string> GetCssVariables()
    {
        var radius = ButtonStyle switch
        {
            "apple" => "0.625rem",
            "windows" => "0.25rem",
            "tactile" => "0.875rem",
            "modern" => "0.75rem",
            "rounded" => "0.5rem",
            "rounded-sm" => "0.375rem",
            "outlined" => "0.5rem",
            "pill" => "9999px",
            "soft" => "1rem",
            _ => "0.75rem"
        };

        return new Dictionary<string, string>
        {
            ["--color-primary"] = PrimaryColor,
            ["--color-primary-hover"] = PrimaryHover,
            ["--color-primary-light"] = PrimaryLight,
            ["--color-secondary"] = SecondaryColor,
            ["--color-secondary-hover"] = SecondaryHover,
            ["--color-secondary-light"] = SecondaryLight,
            ["--color-tertiary"] = TertiaryColor,
            ["--color-tertiary-hover"] = TertiaryHover,
            ["--color-tertiary-light"] = TertiaryLight,
            ["--color-background"] = BackgroundColor,
            ["--color-surface"] = SurfaceColor,
            ["--color-text-primary"] = TextPrimary,
            ["--color-text-secondary"] = TextSecondary,
            ["--color-success"] = SuccessColor,
            ["--color-warning"] = WarningColor,
            ["--color-danger"] = DangerColor,
            ["--color-border"] = BorderColor,
            ["--color-shadow"] = ShadowColor,
            ["--btn-radius"] = radius,
            ["--sidebar-template"] = string.IsNullOrWhiteSpace(SidebarTemplate) ? "classic" : SidebarTemplate,
            ["--topbar-template"] = string.IsNullOrWhiteSpace(TopbarTemplate) ? "classic" : TopbarTemplate
        };
    }

    public string GetCssString() =>
        string.Join(" ", GetCssVariables().Select(kv => $"{kv.Key}: {kv.Value};"));
}
