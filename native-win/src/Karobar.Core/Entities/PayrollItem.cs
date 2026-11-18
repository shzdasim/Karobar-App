using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("payroll_items")]
public class PayrollItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("payroll_run_id")]
    [JsonPropertyName("payroll_run_id")]
    public int PayrollRunId { get; set; }

    [Column("employee_id")]
    [JsonPropertyName("employee_id")]
    public int EmployeeId { get; set; }

    [Column("working_days")]
    [JsonPropertyName("working_days")]
    public int WorkingDays { get; set; } = 26;

    [Column("present_days")]
    [JsonPropertyName("present_days")]
    public int PresentDays { get; set; }

    [Column("absent_days")]
    [JsonPropertyName("absent_days")]
    public int AbsentDays { get; set; }

    [Column("late_days")]
    [JsonPropertyName("late_days")]
    public int LateDays { get; set; }

    [Column("half_days")]
    [JsonPropertyName("half_days")]
    public int HalfDays { get; set; }

    [Column("leave_days")]
    [JsonPropertyName("leave_days")]
    public int LeaveDays { get; set; }

    [Column("overtime_hours")]
    [JsonPropertyName("overtime_hours")]
    public decimal OvertimeHours { get; set; }

    [Column("basic_salary")]
    [JsonPropertyName("basic_salary")]
    public decimal BasicSalary { get; set; }

    [Column("allowances")]
    [JsonPropertyName("allowances")]
    public decimal Allowances { get; set; }

    [Column("overtime_pay")]
    [JsonPropertyName("overtime_pay")]
    public decimal OvertimePay { get; set; }

    [Column("sales_invoices_count")]
    [JsonPropertyName("sales_invoices_count")]
    public int SalesInvoicesCount { get; set; }

    [Column("sales_volume")]
    [JsonPropertyName("sales_volume")]
    public decimal SalesVolume { get; set; }

    [Column("sales_gross_profit")]
    [JsonPropertyName("sales_gross_profit")]
    public decimal SalesGrossProfit { get; set; }

    [Column("sales_commission")]
    [JsonPropertyName("sales_commission")]
    public decimal SalesCommission { get; set; }

    [Column("target_bonus")]
    [JsonPropertyName("target_bonus")]
    public decimal TargetBonus { get; set; }

    [Column("other_bonus")]
    [JsonPropertyName("other_bonus")]
    public decimal OtherBonus { get; set; }

    [Column("gross_earnings")]
    [JsonPropertyName("gross_earnings")]
    public decimal GrossEarnings { get; set; }

    [Column("absence_deduction")]
    [JsonPropertyName("absence_deduction")]
    public decimal AbsenceDeduction { get; set; }

    [Column("late_or_fine_deduction")]
    [JsonPropertyName("late_or_fine_deduction")]
    public decimal LateOrFineDeduction { get; set; }

    [Column("advance_deduction")]
    [JsonPropertyName("advance_deduction")]
    public decimal AdvanceDeduction { get; set; }

    [Column("other_deduction")]
    [JsonPropertyName("other_deduction")]
    public decimal OtherDeduction { get; set; }

    [Column("total_deductions")]
    [JsonPropertyName("total_deductions")]
    public decimal TotalDeductions { get; set; }

    [Column("net_payable")]
    [JsonPropertyName("net_payable")]
    public decimal NetPayable { get; set; }

    [Column("payment_status")]
    [JsonPropertyName("payment_status")]
    public string PaymentStatus { get; set; } = "unpaid";

    [Column("payment_method")]
    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; set; }

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int? AccountId { get; set; }

    [Column("expense_id")]
    [JsonPropertyName("expense_id")]
    public int? ExpenseId { get; set; }

    [Column("paid_at")]
    [JsonPropertyName("paid_at")]
    public DateTime? PaidAt { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(PayrollRunId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual PayrollRun? PayrollRun { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Employee? Employee { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [ForeignKey(nameof(ExpenseId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Expense? Expense { get; set; }
}
