using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("employee_attendances")]
public class EmployeeAttendance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("employee_id")]
    [JsonPropertyName("employee_id")]
    public int EmployeeId { get; set; }

    [Column("date")]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "present";

    [Column("check_in")]
    [JsonPropertyName("check_in")]
    public string? CheckIn { get; set; }

    [Column("check_out")]
    [JsonPropertyName("check_out")]
    public string? CheckOut { get; set; }

    [Column("late_minutes")]
    [JsonPropertyName("late_minutes")]
    public int LateMinutes { get; set; }

    [Column("overtime_hours")]
    [JsonPropertyName("overtime_hours")]
    public decimal OvertimeHours { get; set; }

    [Column("fine_amount")]
    [JsonPropertyName("fine_amount")]
    public decimal FineAmount { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("recorded_by")]
    [JsonPropertyName("recorded_by")]
    public int? RecordedBy { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Employee? Employee { get; set; }
}
