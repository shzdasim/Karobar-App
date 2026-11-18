using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("payroll_runs")]
public class PayrollRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("run_number")]
    [JsonPropertyName("run_number")]
    public string RunNumber { get; set; } = string.Empty;

    [Column("period_month")]
    [JsonPropertyName("period_month")]
    public string PeriodMonth { get; set; } = string.Empty;

    [Column("start_date")]
    [JsonPropertyName("start_date")]
    public DateTime StartDate { get; set; }

    [Column("end_date")]
    [JsonPropertyName("end_date")]
    public DateTime EndDate { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [Column("total_basic")]
    [JsonPropertyName("total_basic")]
    public decimal TotalBasic { get; set; }

    [Column("total_allowances")]
    [JsonPropertyName("total_allowances")]
    public decimal TotalAllowances { get; set; }

    [Column("total_overtime")]
    [JsonPropertyName("total_overtime")]
    public decimal TotalOvertime { get; set; }

    [Column("total_commissions")]
    [JsonPropertyName("total_commissions")]
    public decimal TotalCommissions { get; set; }

    [Column("total_bonuses")]
    [JsonPropertyName("total_bonuses")]
    public decimal TotalBonuses { get; set; }

    [Column("total_deductions")]
    [JsonPropertyName("total_deductions")]
    public decimal TotalDeductions { get; set; }

    [Column("total_advance_deductions")]
    [JsonPropertyName("total_advance_deductions")]
    public decimal TotalAdvanceDeductions { get; set; }

    [Column("total_net_payable")]
    [JsonPropertyName("total_net_payable")]
    public decimal TotalNetPayable { get; set; }

    [Column("total_paid")]
    [JsonPropertyName("total_paid")]
    public decimal TotalPaid { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("generated_by")]
    [JsonPropertyName("generated_by")]
    public int? GeneratedBy { get; set; }

    [Column("paid_at")]
    [JsonPropertyName("paid_at")]
    public DateTime? PaidAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<PayrollItem> Items { get; set; } = new List<PayrollItem>();

    [ForeignKey(nameof(GeneratedBy))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? GeneratedByUser { get; set; }
}
