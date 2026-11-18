using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("employees")]
public class Employee
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("employee_code")]
    [JsonPropertyName("employee_code")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("phone")]
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [Column("email")]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [Column("cnic")]
    [JsonPropertyName("cnic")]
    public string? Cnic { get; set; }

    [Column("address")]
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [Column("department")]
    [JsonPropertyName("department")]
    public string Department { get; set; } = "Sales & Counter";

    [Column("designation")]
    [JsonPropertyName("designation")]
    public string Designation { get; set; } = "Sales Associate";

    [Column("employment_type")]
    [JsonPropertyName("employment_type")]
    public string EmploymentType { get; set; } = "full_time";

    [Column("joining_date")]
    [JsonPropertyName("joining_date")]
    public DateTime? JoiningDate { get; set; }

    [Column("shift_name")]
    [JsonPropertyName("shift_name")]
    public string ShiftName { get; set; } = "Regular Shift";

    [Column("shift_start")]
    [JsonPropertyName("shift_start")]
    public string ShiftStart { get; set; } = "09:00";

    [Column("shift_end")]
    [JsonPropertyName("shift_end")]
    public string ShiftEnd { get; set; } = "18:00";

    [Column("working_days_per_month")]
    [JsonPropertyName("working_days_per_month")]
    public int WorkingDaysPerMonth { get; set; } = 26;

    [Column("basic_salary")]
    [JsonPropertyName("basic_salary")]
    public decimal BasicSalary { get; set; }

    [Column("default_allowances")]
    [JsonPropertyName("default_allowances")]
    public decimal DefaultAllowances { get; set; }

    [Column("hourly_overtime_rate")]
    [JsonPropertyName("hourly_overtime_rate")]
    public decimal HourlyOvertimeRate { get; set; }

    [Column("commission_type")]
    [JsonPropertyName("commission_type")]
    public string CommissionType { get; set; } = "none";

    [Column("commission_rate")]
    [JsonPropertyName("commission_rate")]
    public decimal CommissionRate { get; set; }

    [Column("monthly_sales_target")]
    [JsonPropertyName("monthly_sales_target")]
    public decimal MonthlySalesTarget { get; set; }

    [Column("target_bonus_amount")]
    [JsonPropertyName("target_bonus_amount")]
    public decimal TargetBonusAmount { get; set; }

    [Column("payment_method")]
    [JsonPropertyName("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("bank_name")]
    [JsonPropertyName("bank_name")]
    public string? BankName { get; set; }

    [Column("bank_account_number")]
    [JsonPropertyName("bank_account_number")]
    public string? BankAccountNumber { get; set; }

    [Column("advance_balance")]
    [JsonPropertyName("advance_balance")]
    public decimal AdvanceBalance { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("pin_code")]
    [JsonPropertyName("pin_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PinCode { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<EmployeeAttendance> Attendances { get; set; } = new List<EmployeeAttendance>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<EmployeeAdvance> Advances { get; set; } = new List<EmployeeAdvance>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<PayrollItem> PayrollItems { get; set; } = new List<PayrollItem>();
}
