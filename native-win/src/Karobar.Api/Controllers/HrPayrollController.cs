using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/hr")]
[Authorize]
public class HrPayrollController : ApiControllerBase
{
    private readonly HrPayrollService _hrService;

    public HrPayrollController(KarobarDb db) : base(db)
    {
        _hrService = new HrPayrollService(db);
    }

    private async Task<bool> CanViewHrAsync()
    {
        if (await IsAdminAsync()) return true;
        if (await CanAsync("hr.view", "hr.manage", "payroll.manage", "user.manage")) return true;
        return await CanAsync("user.view") && await CanAsync("account.view");
    }

    private async Task<bool> CanManageHrAsync()
    {
        if (await IsAdminAsync()) return true;
        if (await CanAsync("hr.manage", "payroll.manage", "user.manage")) return true;
        return await CanAsync("user.update") && await CanAsync("account.update");
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] string? month = null,
        [FromQuery(Name = "attendance_date")] string? attendanceDate = null)
    {
        if (!await CanViewHrAsync())
        {
            return Forbidden("Unauthorized to view HR & Payroll.");
        }

        var data = await _hrService.GetOverviewAsync(month, attendanceDate);
        return Ok(data);
    }

    [HttpGet("employees/new-code")]
    public async Task<IActionResult> NewEmployeeCode()
    {
        if (!await CanViewHrAsync())
        {
            return Forbidden("Unauthorized to view HR & Payroll.");
        }

        var code = await _hrService.GenerateEmployeeCodeAsync();
        return Ok(new { employee_code = code });
    }

    [HttpGet("employees")]
    public async Task<IActionResult> Employees(
        [FromQuery] string? status = null,
        [FromQuery] string? department = null,
        [FromQuery] string? search = null)
    {
        var hasFullAccess = await CanViewHrAsync();
        if (!hasFullAccess)
        {
            var canLookupActiveSalesmen = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) &&
                                          await CanAsync("sale-invoice.view", "sale-invoice.create", "barcode-pos.view", "barcode-pos.create");
            if (!canLookupActiveSalesmen)
            {
                return Forbidden("Unauthorized to view HR & Payroll.");
            }

            await _hrService.EnsureTablesAsync();
            var salesmen = await Db.QueryListAsync(@"
                SELECT id, employee_code, name, designation, department, status, commission_rate
                FROM employees
                WHERE status = 'active'
                ORDER BY name ASC");
            return Ok(salesmen);
        }

        await _hrService.EnsureTablesAsync();

        var conditions = new List<string>();
        var dynParams = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("status = @status");
            dynParams.Add("status", status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(department) && !string.Equals(department, "all", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("department = @department");
            dynParams.Add("department", department.Trim());
        }

        var s = (search ?? "").Trim();
        if (!string.IsNullOrEmpty(s))
        {
            dynParams.Add("like", $"%{s}%");
            conditions.Add("(name LIKE @like OR employee_code LIKE @like OR phone LIKE @like OR designation LIKE @like)");
        }

        var whereSql = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        var rows = await Db.QueryListAsync($"SELECT * FROM employees {whereSql} ORDER BY name ASC", dynParams);
        await EnrichEmployeesWithUserAsync(rows);
        return Ok(rows);
    }

    [HttpPost("employees")]
    public async Task<IActionResult> StoreEmployee()
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        await _hrService.EnsureTablesAsync();
        var body = await ReadJsonBodyAsync();

        var name = (GetString(body, "name") ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = new[] { "The name field is required." }
            });
        }

        var employeeCode = (GetString(body, "employee_code") ?? "").Trim();
        if (!string.IsNullOrEmpty(employeeCode))
        {
            var exists = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM employees WHERE employee_code = @employeeCode",
                new { employeeCode });
            if (exists > 0)
            {
                return Unprocessable("The employee code has already been taken.", new Dictionary<string, string[]>
                {
                    ["employee_code"] = new[] { "The employee code has already been taken." }
                });
            }
        }
        else
        {
            employeeCode = await _hrService.GenerateEmployeeCodeAsync();
        }

        var linkedUserId = GetNullableLong(body, "user_id");
        var pinService = new OperatorPinService(Db);
        string? validatedPin = null;

        try
        {
            if (body.ContainsKey("pin_code") && !string.IsNullOrWhiteSpace(GetString(body, "pin_code")))
            {
                validatedPin = await pinService.ValidateUniquePinAsync(GetString(body, "pin_code"), null, null, linkedUserId, null);
            }
            else if (linkedUserId.HasValue && await Db.HasTableAsync("users") && await Db.HasColumnAsync("users", "pin_code"))
            {
                validatedPin = await Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @uid", new { uid = linkedUserId.Value });
            }
        }
        catch (OperatorPinException ex)
        {
            return StatusCode(ex.StatusCode, ex.ResponsePayload);
        }

        var insertData = new Dictionary<string, object?>
        {
            ["employee_code"] = employeeCode,
            ["user_id"] = linkedUserId,
            ["name"] = name,
            ["phone"] = GetString(body, "phone"),
            ["email"] = GetString(body, "email"),
            ["cnic"] = GetString(body, "cnic"),
            ["address"] = GetString(body, "address"),
            ["department"] = GetString(body, "department") ?? "Sales & Counter",
            ["designation"] = GetString(body, "designation") ?? "Sales Associate",
            ["employment_type"] = GetString(body, "employment_type") ?? "full_time",
            ["joining_date"] = GetString(body, "joining_date") ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["shift_name"] = GetString(body, "shift_name") ?? "Regular Shift",
            ["shift_start"] = GetString(body, "shift_start") ?? "09:00",
            ["shift_end"] = GetString(body, "shift_end") ?? "18:00",
            ["working_days_per_month"] = GetInt(body, "working_days_per_month", 26),
            ["basic_salary"] = GetDecimal(body, "basic_salary", 0m),
            ["default_allowances"] = GetDecimal(body, "default_allowances", 0m),
            ["hourly_overtime_rate"] = GetDecimal(body, "hourly_overtime_rate", 0m),
            ["commission_type"] = GetString(body, "commission_type") ?? "none",
            ["commission_rate"] = GetDecimal(body, "commission_rate", 0m),
            ["monthly_sales_target"] = GetDecimal(body, "monthly_sales_target", 0m),
            ["target_bonus_amount"] = GetDecimal(body, "target_bonus_amount", 0m),
            ["payment_method"] = GetString(body, "payment_method") ?? "cash",
            ["bank_name"] = GetString(body, "bank_name"),
            ["bank_account_number"] = GetString(body, "bank_account_number"),
            ["pin_code"] = validatedPin,
            ["status"] = GetString(body, "status") ?? "active",
            ["notes"] = GetString(body, "notes")
        };

        var empId = await Db.InsertAsync("employees", insertData);
        if (body.ContainsKey("pin_code"))
        {
            await pinService.SyncLinkedPinForEmployeeAsync(linkedUserId, validatedPin);
        }

        var created = await Db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = empId });
        if (created != null)
        {
            await EnrichEmployeesWithUserAsync(new List<Dictionary<string, object?>> { created });
        }

        return StatusCode(201, created);
    }

    [HttpPut("employees/{employee:long}")]
    public async Task<IActionResult> UpdateEmployee(long employee)
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        await _hrService.EnsureTablesAsync();
        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = employee });
        if (existing == null)
        {
            return NotFound(new { message = "Employee not found." });
        }

        var body = await ReadJsonBodyAsync();
        var employeeCode = (GetString(body, "employee_code") ?? "").Trim();
        var name = (GetString(body, "name") ?? "").Trim();

        if (string.IsNullOrEmpty(employeeCode))
        {
            return Unprocessable("The employee_code field is required.", new Dictionary<string, string[]>
            {
                ["employee_code"] = new[] { "The employee_code field is required." }
            });
        }

        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = new[] { "The name field is required." }
            });
        }

        var dup = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM employees WHERE employee_code = @employeeCode AND id != @id",
            new { employeeCode, id = employee });
        if (dup > 0)
        {
            return Unprocessable("The employee code has already been taken.", new Dictionary<string, string[]>
            {
                ["employee_code"] = new[] { "The employee code has already been taken." }
            });
        }

        var linkedUserId = body.ContainsKey("user_id")
            ? GetNullableLong(body, "user_id")
            : (existing.TryGetValue("user_id", out var uObj) && uObj != null && long.TryParse(uObj.ToString(), out var uid) ? (long?)uid : null);

        var pinService = new OperatorPinService(Db);
        bool hasPinInput = body.ContainsKey("pin_code");
        string? resolvedPin = existing.GetValueOrDefault("pin_code")?.ToString();

        try
        {
            if (hasPinInput)
            {
                resolvedPin = await pinService.ValidateUniquePinAsync(GetString(body, "pin_code"), null, employee, linkedUserId, null);
            }
            else if (linkedUserId.HasValue && string.IsNullOrEmpty(resolvedPin) && await Db.HasTableAsync("users") && await Db.HasColumnAsync("users", "pin_code"))
            {
                resolvedPin = await Db.ExecuteScalarAsync<string?>("SELECT pin_code FROM users WHERE id = @uid", new { uid = linkedUserId.Value });
            }
        }
        catch (OperatorPinException ex)
        {
            return StatusCode(ex.StatusCode, ex.ResponsePayload);
        }

        var updates = new Dictionary<string, object?>
        {
            ["employee_code"] = employeeCode,
            ["user_id"] = linkedUserId,
            ["name"] = name,
            ["pin_code"] = resolvedPin
        };

        foreach (var strCol in new[] { "phone", "email", "cnic", "address", "department", "designation", "employment_type", "joining_date", "shift_name", "shift_start", "shift_end", "commission_type", "payment_method", "bank_name", "bank_account_number", "status", "notes" })
        {
            if (body.ContainsKey(strCol))
            {
                updates[strCol] = GetString(body, strCol);
            }
        }

        if (body.ContainsKey("working_days_per_month")) updates["working_days_per_month"] = GetInt(body, "working_days_per_month", 26);
        foreach (var decCol in new[] { "basic_salary", "default_allowances", "hourly_overtime_rate", "commission_rate", "monthly_sales_target", "target_bonus_amount" })
        {
            if (body.ContainsKey(decCol))
            {
                updates[decCol] = GetDecimal(body, decCol);
            }
        }

        await Db.UpdateAsync("employees", employee, updates);
        if (hasPinInput)
        {
            await pinService.SyncLinkedPinForEmployeeAsync(linkedUserId, resolvedPin);
        }

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = employee });
        if (updated != null)
        {
            await EnrichEmployeesWithUserAsync(new List<Dictionary<string, object?>> { updated });
        }

        return Ok(updated);
    }

    [HttpDelete("employees/{employee:long}")]
    public async Task<IActionResult> DestroyEmployee(long employee)
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        await _hrService.EnsureTablesAsync();
        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = employee });
        if (existing == null)
        {
            return NotFound(new { message = "Employee not found." });
        }

        if (StocktakeService.ToDecimal(existing.GetValueOrDefault("advance_balance")) > 0m)
        {
            return Unprocessable(
                "Cannot delete employee with an outstanding salary advance balance. Recover the advance or mark the employee as Inactive.",
                new Dictionary<string, string[]>
                {
                    ["employee"] = new[] { "Cannot delete employee with an outstanding salary advance balance. Recover the advance or mark the employee as Inactive." }
                });
        }

        var paidPayslips = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM payroll_items WHERE employee_id = @employee AND payment_status = 'paid'",
            new { employee });
        if (paidPayslips > 0)
        {
            return Unprocessable(
                "Cannot delete employee with historical paid payslips. Mark the employee as Inactive instead.",
                new Dictionary<string, string[]>
                {
                    ["employee"] = new[] { "Cannot delete employee with historical paid payslips. Mark the employee as Inactive instead." }
                });
        }

        await Db.ExecuteAsync("DELETE FROM employee_attendances WHERE employee_id = @employee", new { employee });
        await Db.ExecuteAsync("DELETE FROM employee_advances WHERE employee_id = @employee", new { employee });
        await Db.ExecuteAsync("DELETE FROM payroll_items WHERE employee_id = @employee", new { employee });
        await Db.ExecuteAsync("DELETE FROM employees WHERE id = @employee", new { employee });

        return Ok(new { message = "Employee deleted successfully." });
    }

    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] string? date = null)
    {
        if (!await CanViewHrAsync())
        {
            return Forbidden("Unauthorized to view HR & Payroll.");
        }

        await _hrService.EnsureTablesAsync();
        var dateStr = !string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed)
            ? parsed.ToString("yyyy-MM-dd")
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var employees = await Db.QueryListAsync(@"
            SELECT * FROM employees
            WHERE status IN ('active', 'on_leave')
            ORDER BY name ASC");

        var records = await Db.QueryListAsync(
            "SELECT * FROM employee_attendances WHERE date = @dateStr",
            new { dateStr });
        var recByEmp = records
            .Where(r => r.GetValueOrDefault("employee_id") != null)
            .GroupBy(r => Convert.ToInt64(r["employee_id"]))
            .ToDictionary(g => g.Key, g => g.Last());

        var sheet = new List<Dictionary<string, object?>>();
        foreach (var emp in employees)
        {
            var empId = Convert.ToInt64(emp["id"]);
            recByEmp.TryGetValue(empId, out var att);

            sheet.Add(new Dictionary<string, object?>
            {
                ["employee_id"] = empId,
                ["employee_code"] = emp.GetValueOrDefault("employee_code"),
                ["employee_name"] = emp.GetValueOrDefault("name"),
                ["department"] = emp.GetValueOrDefault("department"),
                ["designation"] = emp.GetValueOrDefault("designation"),
                ["shift_name"] = emp.GetValueOrDefault("shift_name"),
                ["shift_start"] = emp.GetValueOrDefault("shift_start"),
                ["shift_end"] = emp.GetValueOrDefault("shift_end"),
                ["date"] = dateStr,
                ["attendance_id"] = att?.GetValueOrDefault("id"),
                ["status"] = att?.GetValueOrDefault("status")?.ToString() ?? "present",
                ["is_marked"] = att != null,
                ["check_in"] = att?.GetValueOrDefault("check_in") ?? emp.GetValueOrDefault("shift_start"),
                ["check_out"] = att?.GetValueOrDefault("check_out") ?? emp.GetValueOrDefault("shift_end"),
                ["late_minutes"] = Convert.ToInt32(att?.GetValueOrDefault("late_minutes") ?? 0),
                ["overtime_hours"] = StocktakeService.ToDecimal(att?.GetValueOrDefault("overtime_hours")),
                ["fine_amount"] = StocktakeService.ToDecimal(att?.GetValueOrDefault("fine_amount")),
                ["notes"] = att?.GetValueOrDefault("notes")?.ToString() ?? ""
            });
        }

        return Ok(new
        {
            date = dateStr,
            sheet
        });
    }

    [HttpPost("attendance")]
    public async Task<IActionResult> SaveAttendance()
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        await _hrService.EnsureTablesAsync();
        var body = await ReadJsonBodyAsync();

        var employeeId = GetNullableLong(body, "employee_id");
        var rawDate = GetString(body, "date");
        var status = (GetString(body, "status") ?? "").Trim().ToLowerInvariant();

        if (employeeId == null || employeeId <= 0)
        {
            return Unprocessable("The employee_id field is required.");
        }
        if (string.IsNullOrWhiteSpace(rawDate) || !DateTime.TryParse(rawDate, out var parsedDate))
        {
            return Unprocessable("A valid date is required.");
        }
        if (status is not ("present" or "absent" or "late" or "half_day" or "paid_leave" or "holiday"))
        {
            return Unprocessable("Invalid attendance status.");
        }

        var dateStr = parsedDate.ToString("yyyy-MM-dd");
        var existing = await Db.QuerySingleOrDefaultAsync(
            "SELECT * FROM employee_attendances WHERE employee_id = @empId AND date = @dateStr LIMIT 1",
            new { empId = employeeId.Value, dateStr });

        var payload = new Dictionary<string, object?>
        {
            ["employee_id"] = employeeId.Value,
            ["date"] = dateStr,
            ["status"] = status,
            ["check_in"] = GetString(body, "check_in"),
            ["check_out"] = GetString(body, "check_out"),
            ["late_minutes"] = Math.Max(0, GetInt(body, "late_minutes", 0)),
            ["overtime_hours"] = Math.Max(0m, GetDecimal(body, "overtime_hours", 0m)),
            ["fine_amount"] = Math.Max(0m, GetDecimal(body, "fine_amount", 0m)),
            ["notes"] = GetString(body, "notes"),
            ["recorded_by"] = CurrentUserId
        };

        long attId;
        if (existing != null)
        {
            attId = Convert.ToInt64(existing["id"]);
            await Db.UpdateAsync("employee_attendances", attId, payload);
        }
        else
        {
            attId = await Db.InsertAsync("employee_attendances", payload);
        }

        var saved = await Db.QuerySingleOrDefaultAsync("SELECT * FROM employee_attendances WHERE id = @id", new { id = attId });
        if (saved != null)
        {
            saved["employee"] = await Db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = employeeId.Value });
        }

        return Ok(saved);
    }

    [HttpPost("attendance/bulk")]
    public async Task<IActionResult> SaveBulkAttendance()
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        await _hrService.EnsureTablesAsync();
        var body = await ReadJsonBodyAsync();

        var rawDate = GetString(body, "date");
        if (string.IsNullOrWhiteSpace(rawDate) || !DateTime.TryParse(rawDate, out var parsedDate))
        {
            return Unprocessable("A valid date is required.");
        }

        if (!body.TryGetValue("entries", out var entriesEl) || entriesEl.ValueKind != JsonValueKind.Array || entriesEl.GetArrayLength() == 0)
        {
            return Unprocessable("At least one attendance entry is required.");
        }

        var dateStr = parsedDate.ToString("yyyy-MM-dd");
        int count = 0;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        foreach (var el in entriesEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in el.EnumerateObject()) row[p.Name] = p.Value;

            var empId = GetNullableLong(row, "employee_id");
            if (empId == null || empId <= 0) continue;

            var status = (GetString(row, "status") ?? "present").Trim().ToLowerInvariant();
            var existing = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM employee_attendances WHERE employee_id = @empId AND date = @dateStr LIMIT 1",
                new { empId = empId.Value, dateStr }, conn, tx);

            var payload = new Dictionary<string, object?>
            {
                ["employee_id"] = empId.Value,
                ["date"] = dateStr,
                ["status"] = status,
                ["check_in"] = GetString(row, "check_in"),
                ["check_out"] = GetString(row, "check_out"),
                ["late_minutes"] = Math.Max(0, GetInt(row, "late_minutes", 0)),
                ["overtime_hours"] = Math.Max(0m, GetDecimal(row, "overtime_hours", 0m)),
                ["fine_amount"] = Math.Max(0m, GetDecimal(row, "fine_amount", 0m)),
                ["notes"] = GetString(row, "notes"),
                ["recorded_by"] = CurrentUserId
            };

            if (existing != null)
            {
                await Db.UpdateAsync("employee_attendances", Convert.ToInt64(existing["id"]), payload, "id", conn, tx);
            }
            else
            {
                await Db.InsertAsync("employee_attendances", payload, conn, tx);
            }
            count++;
        }

        await tx.CommitAsync();

        return Ok(new
        {
            message = $"Saved attendance for {count} employees on {dateStr}.",
            date = dateStr
        });
    }

    [HttpGet("advances")]
    public async Task<IActionResult> Advances(
        [FromQuery(Name = "employee_id")] long? employeeId = null,
        [FromQuery] string? type = null)
    {
        if (!await CanViewHrAsync())
        {
            return Forbidden("Unauthorized to view HR & Payroll.");
        }

        await _hrService.EnsureTablesAsync();
        var conditions = new List<string>();
        var dynParams = new DynamicParameters();

        if (employeeId.HasValue && employeeId > 0)
        {
            conditions.Add("employee_id = @empId");
            dynParams.Add("empId", employeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("type = @type");
            dynParams.Add("type", type.Trim());
        }

        var whereSql = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        var rows = await Db.QueryListAsync(
            $"SELECT * FROM employee_advances {whereSql} ORDER BY date DESC, id DESC LIMIT 200",
            dynParams);

        if (rows.Count > 0)
        {
            var empIds = rows.Select(r => Convert.ToInt64(r["employee_id"])).Distinct().ToList();
            var empRows = await Db.QueryListAsync(
                "SELECT id, employee_code, name, department, designation, advance_balance FROM employees WHERE id IN @ids",
                new { ids = empIds });
            var empMap = empRows.ToDictionary(e => Convert.ToInt64(e["id"]), e => e);

            var accIds = rows
                .Select(r => r.TryGetValue("account_id", out var a) && a != null && long.TryParse(a.ToString(), out var aid) ? (long?)aid : null)
                .Where(a => a.HasValue)
                .Select(a => a!.Value)
                .Distinct()
                .ToList();

            var accMap = new Dictionary<long, Dictionary<string, object?>>();
            if (accIds.Count > 0 && await Db.HasTableAsync("accounts"))
            {
                var accRows = await Db.QueryListAsync("SELECT id, name, code, type FROM accounts WHERE id IN @ids", new { ids = accIds });
                foreach (var a in accRows) accMap[Convert.ToInt64(a["id"])] = a;
            }

            foreach (var r in rows)
            {
                var eid = Convert.ToInt64(r["employee_id"]);
                r["employee"] = empMap.TryGetValue(eid, out var empDict) ? empDict : null;
                r["account"] = r.TryGetValue("account_id", out var aObj) && aObj != null && long.TryParse(aObj.ToString(), out var aid) && accMap.TryGetValue(aid, out var accDict) ? accDict : null;
            }
        }

        return Ok(rows);
    }

    [HttpPost("advances")]
    public async Task<IActionResult> StoreAdvance()
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        var body = await ReadJsonBodyAsync();
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["employee_id"] = GetNullableLong(body, "employee_id"),
            ["type"] = GetString(body, "type"),
            ["amount"] = GetDecimal(body, "amount"),
            ["date"] = GetString(body, "date"),
            ["payment_method"] = GetString(body, "payment_method") ?? "cash",
            ["account_id"] = GetNullableLong(body, "account_id"),
            ["reference_number"] = GetString(body, "reference_number"),
            ["description"] = GetString(body, "description")
        };

        try
        {
            var advance = await _hrService.RecordAdvanceTransactionAsync(data, CurrentUserId);
            return StatusCode(201, advance);
        }
        catch (HrPayrollValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }

    [HttpGet("commissions")]
    public async Task<IActionResult> Commissions(
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (!await CanViewHrAsync())
        {
            return Forbidden("Unauthorized to view HR & Payroll.");
        }

        var now = DateTime.UtcNow;
        var fromStr = !string.IsNullOrWhiteSpace(from) && DateTime.TryParse(from, out var fDt)
            ? fDt.ToString("yyyy-MM-dd")
            : new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var toStr = !string.IsNullOrWhiteSpace(to) && DateTime.TryParse(to, out var tDt)
            ? tDt.ToString("yyyy-MM-dd")
            : new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)).ToString("yyyy-MM-dd");

        var report = await _hrService.GetCommissionsReportAsync(fromStr, toStr);
        return Ok(report);
    }

    [HttpGet("payroll-runs")]
    public async Task<IActionResult> PayrollRuns()
    {
        if (!await CanViewHrAsync())
        {
            return Forbidden("Unauthorized to view HR & Payroll.");
        }

        await _hrService.EnsureTablesAsync();
        var runs = await Db.QueryListAsync("SELECT * FROM payroll_runs ORDER BY period_month DESC");
        await _hrService.EnrichPayrollRunsBatchAsync(runs);
        return Ok(runs);
    }

    [HttpPost("payroll-runs/generate")]
    public async Task<IActionResult> GeneratePayrollRun()
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        var body = await ReadJsonBodyAsync();
        var periodMonth = (GetString(body, "period_month") ?? "").Trim();

        try
        {
            var run = await _hrService.GenerateOrRefreshPayrollRunAsync(
                periodMonth,
                new Dictionary<string, object?>
                {
                    ["auto_deduct_advances"] = body.ContainsKey("auto_deduct_advances") ? GetBool(body, "auto_deduct_advances", true) : true,
                    ["notes"] = GetString(body, "notes")
                },
                CurrentUserId);

            return StatusCode(201, run);
        }
        catch (HrPayrollValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }

    [HttpPut("payroll-items/{payrollItem:long}")]
    public async Task<IActionResult> UpdatePayrollItem(long payrollItem)
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        var body = await ReadJsonBodyAsync();
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            data[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
        }

        try
        {
            var updated = await _hrService.UpdatePayrollItemAsync(payrollItem, data);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Payroll item not found." });
        }
        catch (HrPayrollValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }

    [HttpPost("payroll-items/{payrollItem:long}/pay")]
    public async Task<IActionResult> PayPayrollItem(long payrollItem)
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        var body = await ReadJsonBodyAsync();
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["payment_method"] = GetString(body, "payment_method"),
            ["account_id"] = GetNullableLong(body, "account_id"),
            ["date"] = GetString(body, "date")
        };

        try
        {
            var paid = await _hrService.PayPayrollItemAsync(payrollItem, data, CurrentUserId);
            return Ok(paid);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Payroll item not found." });
        }
        catch (HrPayrollValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }

    [HttpPost("payroll-runs/{payrollRun:long}/pay-all")]
    public async Task<IActionResult> PayAllInRun(long payrollRun)
    {
        if (!await CanManageHrAsync())
        {
            return Forbidden("Unauthorized to manage HR & Payroll.");
        }

        var body = await ReadJsonBodyAsync();
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["payment_method"] = GetString(body, "payment_method"),
            ["account_id"] = GetNullableLong(body, "account_id"),
            ["date"] = GetString(body, "date")
        };

        try
        {
            var run = await _hrService.PayAllInRunAsync(payrollRun, data, CurrentUserId);
            return Ok(run);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Payroll run not found." });
        }
        catch (HrPayrollValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }

    private async Task EnrichEmployeesWithUserAsync(List<Dictionary<string, object?>> employees)
    {
        if (employees.Count == 0) return;

        var userIds = employees
            .Select(e => e.TryGetValue("user_id", out var u) && u != null && long.TryParse(u.ToString(), out var uid) ? (long?)uid : null)
            .Where(u => u.HasValue)
            .Select(u => u!.Value)
            .Distinct()
            .ToList();

        var usersMap = new Dictionary<long, Dictionary<string, object?>>();
        if (userIds.Count > 0 && await Db.HasTableAsync("users"))
        {
            var uRows = await Db.QueryListAsync("SELECT id, name, email FROM users WHERE id IN @ids", new { ids = userIds });
            foreach (var u in uRows) usersMap[Convert.ToInt64(u["id"])] = u;
        }

        foreach (var e in employees)
        {
            e["user"] = e.TryGetValue("user_id", out var uObj) && uObj != null && long.TryParse(uObj.ToString(), out var uid) && usersMap.TryGetValue(uid, out var uDict) ? uDict : null;
        }
    }
}
