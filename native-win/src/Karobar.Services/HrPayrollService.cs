using System.Text.RegularExpressions;
using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class HrPayrollValidationException : Exception
{
    public Dictionary<string, string[]> Errors { get; }

    public HrPayrollValidationException(string field, string message) : base(message)
    {
        Errors = new Dictionary<string, string[]>
        {
            [field] = new[] { message }
        };
    }
}

public class HrPayrollService
{
    private readonly KarobarDb _db;

    public HrPayrollService(KarobarDb db)
    {
        _db = db;
    }

    public async Task EnsureTablesAsync()
    {
        if (await _db.HasTableAsync("employees") &&
            await _db.HasTableAsync("employee_attendances") &&
            await _db.HasTableAsync("employee_advances") &&
            await _db.HasTableAsync("payroll_runs") &&
            await _db.HasTableAsync("payroll_items"))
        {
            return;
        }

        await _db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS employees (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                employee_code VARCHAR(40) NOT NULL UNIQUE,
                user_id INTEGER NULL,
                name VARCHAR(150) NOT NULL,
                phone VARCHAR(50) NULL,
                email VARCHAR(150) NULL,
                cnic VARCHAR(50) NULL,
                address VARCHAR(500) NULL,
                department VARCHAR(100) NOT NULL DEFAULT 'Sales & Counter',
                designation VARCHAR(100) NOT NULL DEFAULT 'Sales Associate',
                employment_type VARCHAR(40) NOT NULL DEFAULT 'full_time',
                joining_date DATE NULL,
                shift_name VARCHAR(80) NOT NULL DEFAULT 'Regular Shift',
                shift_start VARCHAR(10) NOT NULL DEFAULT '09:00',
                shift_end VARCHAR(10) NOT NULL DEFAULT '18:00',
                working_days_per_month INTEGER NOT NULL DEFAULT 26,
                basic_salary DECIMAL(15,2) NOT NULL DEFAULT 0,
                default_allowances DECIMAL(15,2) NOT NULL DEFAULT 0,
                hourly_overtime_rate DECIMAL(15,2) NOT NULL DEFAULT 0,
                commission_type VARCHAR(40) NOT NULL DEFAULT 'none',
                commission_rate DECIMAL(10,2) NOT NULL DEFAULT 0,
                monthly_sales_target DECIMAL(15,2) NOT NULL DEFAULT 0,
                target_bonus_amount DECIMAL(15,2) NOT NULL DEFAULT 0,
                payment_method VARCHAR(30) NOT NULL DEFAULT 'cash',
                bank_name VARCHAR(120) NULL,
                bank_account_number VARCHAR(100) NULL,
                advance_balance DECIMAL(15,2) NOT NULL DEFAULT 0,
                pin_code VARCHAR(10) NULL,
                status VARCHAR(30) NOT NULL DEFAULT 'active',
                notes TEXT NULL,
                created_at DATETIME NULL,
                updated_at DATETIME NULL
            );

            CREATE TABLE IF NOT EXISTS employee_attendances (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                employee_id INTEGER NOT NULL,
                date DATE NOT NULL,
                status VARCHAR(30) NOT NULL DEFAULT 'present',
                check_in VARCHAR(10) NULL,
                check_out VARCHAR(10) NULL,
                late_minutes INTEGER NOT NULL DEFAULT 0,
                overtime_hours DECIMAL(6,2) NOT NULL DEFAULT 0,
                fine_amount DECIMAL(15,2) NOT NULL DEFAULT 0,
                notes VARCHAR(300) NULL,
                recorded_by INTEGER NULL,
                created_at DATETIME NULL,
                updated_at DATETIME NULL,
                UNIQUE(employee_id, date)
            );

            CREATE TABLE IF NOT EXISTS employee_advances (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                advance_number VARCHAR(50) NOT NULL UNIQUE,
                employee_id INTEGER NOT NULL,
                type VARCHAR(30) NOT NULL DEFAULT 'advance',
                amount DECIMAL(15,2) NOT NULL,
                balance_after DECIMAL(15,2) NOT NULL DEFAULT 0,
                date DATE NOT NULL,
                payment_method VARCHAR(30) NOT NULL DEFAULT 'cash',
                account_id INTEGER NULL,
                payroll_item_id INTEGER NULL,
                reference_number VARCHAR(100) NULL,
                description VARCHAR(400) NULL,
                created_by INTEGER NULL,
                created_at DATETIME NULL,
                updated_at DATETIME NULL
            );

            CREATE TABLE IF NOT EXISTS payroll_runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_number VARCHAR(50) NOT NULL UNIQUE,
                period_month VARCHAR(7) NOT NULL UNIQUE,
                start_date DATE NOT NULL,
                end_date DATE NOT NULL,
                status VARCHAR(30) NOT NULL DEFAULT 'draft',
                total_basic DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_allowances DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_overtime DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_commissions DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_bonuses DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_deductions DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_advance_deductions DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_net_payable DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_paid DECIMAL(15,2) NOT NULL DEFAULT 0,
                notes TEXT NULL,
                generated_by INTEGER NULL,
                paid_at DATETIME NULL,
                created_at DATETIME NULL,
                updated_at DATETIME NULL
            );

            CREATE TABLE IF NOT EXISTS payroll_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                payroll_run_id INTEGER NOT NULL,
                employee_id INTEGER NOT NULL,
                working_days INTEGER NOT NULL DEFAULT 26,
                present_days INTEGER NOT NULL DEFAULT 0,
                absent_days INTEGER NOT NULL DEFAULT 0,
                late_days INTEGER NOT NULL DEFAULT 0,
                half_days INTEGER NOT NULL DEFAULT 0,
                leave_days INTEGER NOT NULL DEFAULT 0,
                overtime_hours DECIMAL(6,2) NOT NULL DEFAULT 0,
                basic_salary DECIMAL(15,2) NOT NULL DEFAULT 0,
                allowances DECIMAL(15,2) NOT NULL DEFAULT 0,
                overtime_pay DECIMAL(15,2) NOT NULL DEFAULT 0,
                sales_invoices_count INTEGER NOT NULL DEFAULT 0,
                sales_volume DECIMAL(15,2) NOT NULL DEFAULT 0,
                sales_gross_profit DECIMAL(15,2) NOT NULL DEFAULT 0,
                sales_commission DECIMAL(15,2) NOT NULL DEFAULT 0,
                target_bonus DECIMAL(15,2) NOT NULL DEFAULT 0,
                other_bonus DECIMAL(15,2) NOT NULL DEFAULT 0,
                gross_earnings DECIMAL(15,2) NOT NULL DEFAULT 0,
                absence_deduction DECIMAL(15,2) NOT NULL DEFAULT 0,
                late_or_fine_deduction DECIMAL(15,2) NOT NULL DEFAULT 0,
                advance_deduction DECIMAL(15,2) NOT NULL DEFAULT 0,
                other_deduction DECIMAL(15,2) NOT NULL DEFAULT 0,
                total_deductions DECIMAL(15,2) NOT NULL DEFAULT 0,
                net_payable DECIMAL(15,2) NOT NULL DEFAULT 0,
                payment_status VARCHAR(30) NOT NULL DEFAULT 'unpaid',
                payment_method VARCHAR(30) NULL,
                account_id INTEGER NULL,
                expense_id INTEGER NULL,
                paid_at DATETIME NULL,
                notes VARCHAR(400) NULL,
                created_at DATETIME NULL,
                updated_at DATETIME NULL,
                UNIQUE(payroll_run_id, employee_id)
            );");

        KarobarDb.FlushSchemaCache();
    }

    public async Task<string> GenerateEmployeeCodeAsync()
    {
        await EnsureTablesAsync();
        var last = await _db.QuerySingleOrDefaultAsync("SELECT employee_code FROM employees ORDER BY id DESC LIMIT 1");
        int next;
        if (last != null && last.TryGetValue("employee_code", out var ec) && ec != null)
        {
            var m = Regex.Match(ec.ToString()!, @"EMP-(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var num))
            {
                next = num + 1;
            }
            else
            {
                var count = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM employees");
                next = count + 1;
            }
        }
        else
        {
            var count = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM employees");
            next = count + 1;
        }

        while (true)
        {
            var code = $"EMP-{next:D4}";
            var exists = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM employees WHERE employee_code = @code", new { code });
            if (exists == 0) return code;
            next++;
        }
    }

    public async Task<string> GenerateAdvanceNumberAsync(string dateStr)
    {
        await EnsureTablesAsync();
        var cleanDate = (dateStr.Length >= 10 ? dateStr[..10] : DateTime.UtcNow.ToString("yyyy-MM-dd")).Replace("-", "");
        var prefix = $"ADV-{cleanDate}-";
        var count = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM employee_advances WHERE advance_number LIKE @like",
            new { like = prefix + "%" });
        int next = count + 1;
        while (true)
        {
            var candidate = $"{prefix}{next:D3}";
            var exists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM employee_advances WHERE advance_number = @candidate",
                new { candidate });
            if (exists == 0) return candidate;
            next++;
        }
    }

    public async Task<Dictionary<string, object?>> GetOverviewAsync(string? periodMonth = null, string? attendanceDate = null)
    {
        await EnsureTablesAsync();

        var month = !string.IsNullOrWhiteSpace(periodMonth) && Regex.IsMatch(periodMonth.Trim(), @"^\d{4}-\d{2}$")
            ? periodMonth.Trim()
            : DateTime.UtcNow.ToString("yyyy-MM");

        var attDate = !string.IsNullOrWhiteSpace(attendanceDate) && DateTime.TryParse(attendanceDate, out var parsedAtt)
            ? parsedAtt.ToString("yyyy-MM-dd")
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var monthDt = DateTime.ParseExact($"{month}-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var startDate = new DateTime(monthDt.Year, monthDt.Month, 1).ToString("yyyy-MM-dd");
        var endDate = new DateTime(monthDt.Year, monthDt.Month, DateTime.DaysInMonth(monthDt.Year, monthDt.Month)).ToString("yyyy-MM-dd");

        var employees = await _db.QueryListAsync("SELECT * FROM employees ORDER BY name ASC");
        var activeEmployees = employees.Where(e =>
        {
            var st = e.GetValueOrDefault("status")?.ToString()?.ToLowerInvariant() ?? "active";
            return st is "active" or "on_leave";
        }).ToList();

        var todayAttendances = await _db.QueryListAsync(
            "SELECT * FROM employee_attendances WHERE date = @attDate",
            new { attDate });
        var attByEmp = todayAttendances.ToDictionary(a => Convert.ToInt64(a["employee_id"]), a => a);

        int presentCount = 0, lateCount = 0, absentCount = 0, leaveCount = 0, unmarkedCount = 0;

        foreach (var emp in activeEmployees)
        {
            var empId = Convert.ToInt64(emp["id"]);
            if (!attByEmp.TryGetValue(empId, out var rec))
            {
                unmarkedCount++;
                continue;
            }
            var st = rec.GetValueOrDefault("status")?.ToString()?.ToLowerInvariant() ?? "";
            switch (st)
            {
                case "present":
                    presentCount++;
                    break;
                case "late":
                    lateCount++;
                    break;
                case "absent":
                    absentCount++;
                    break;
                case "paid_leave":
                case "half_day":
                case "holiday":
                    leaveCount++;
                    break;
            }
        }

        var commissionsReport = await GetCommissionsReportAsync(startDate, endDate);
        var commSummary = commissionsReport.GetValueOrDefault("summary") as Dictionary<string, object?>;

        var currentRun = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM payroll_runs WHERE period_month = @month LIMIT 1",
            new { month });
        if (currentRun != null)
        {
            await EnrichPayrollRunsBatchAsync(new List<Dictionary<string, object?>> { currentRun });
        }

        var fixedPool = Math.Round(activeEmployees.Sum(e =>
            ToDecimal(e.GetValueOrDefault("basic_salary")) + ToDecimal(e.GetValueOrDefault("default_allowances"))), 2);
        var totalAdvances = Math.Round(employees.Sum(e => ToDecimal(e.GetValueOrDefault("advance_balance"))), 2);

        return new Dictionary<string, object?>
        {
            ["period_month"] = month,
            ["attendance_date"] = attDate,
            ["total_employees"] = employees.Count,
            ["active_employees"] = employees.Count(e => string.Equals(e.GetValueOrDefault("status")?.ToString(), "active", StringComparison.OrdinalIgnoreCase)),
            ["on_leave_employees"] = employees.Count(e => string.Equals(e.GetValueOrDefault("status")?.ToString(), "on_leave", StringComparison.OrdinalIgnoreCase)),
            ["inactive_employees"] = employees.Count(e => string.Equals(e.GetValueOrDefault("status")?.ToString(), "inactive", StringComparison.OrdinalIgnoreCase)),
            ["monthly_fixed_payroll_pool"] = fixedPool,
            ["total_outstanding_advances"] = totalAdvances,
            ["monthly_commissions_total"] = commSummary != null ? ToDecimal(commSummary.GetValueOrDefault("total_incentives")) : 0m,
            ["monthly_net_sales_attributed"] = commSummary != null ? ToDecimal(commSummary.GetValueOrDefault("total_net_sales")) : 0m,
            ["today_attendance"] = new Dictionary<string, object?>
            {
                ["date"] = attDate,
                ["present"] = presentCount,
                ["late"] = lateCount,
                ["absent"] = absentCount,
                ["leave"] = leaveCount,
                ["unmarked"] = unmarkedCount
            },
            ["current_payroll_run"] = currentRun
        };
    }

    public async Task<Dictionary<string, object?>> CalculateEmployeeCommissionAsync(
        Dictionary<string, object?> employee,
        string fromDate,
        string toDate)
    {
        await EnsureTablesAsync();

        var from = DateTime.TryParse(fromDate, out var fDt) ? fDt.ToString("yyyy-MM-dd") : fromDate;
        var to = DateTime.TryParse(toDate, out var tDt) ? tDt.ToString("yyyy-MM-dd") : toDate;

        var empId = Convert.ToInt64(employee["id"]);
        long? linkedUserId = null;
        if (employee.TryGetValue("user_id", out var uObj) && uObj != null && long.TryParse(uObj.ToString(), out var uid) && uid > 0)
        {
            linkedUserId = uid;
        }

        int invoicesCount = 0;
        decimal grossSales = 0m;
        decimal netSales = 0m;
        decimal cogsTotal = 0m;

        if (await _db.HasTableAsync("sale_invoices"))
        {
            var hasSalesmanCol = await _db.HasColumnAsync("sale_invoices", "salesman_id");
            string whereActor;
            if (hasSalesmanCol && linkedUserId.HasValue)
            {
                whereActor = "(salesman_id = @empId OR (salesman_id IS NULL AND user_id = @linkedUserId))";
            }
            else if (hasSalesmanCol)
            {
                whereActor = "salesman_id = @empId";
            }
            else if (linkedUserId.HasValue)
            {
                whereActor = "user_id = @linkedUserId";
            }
            else
            {
                whereActor = "1 = 0";
            }

            var invSql = $@"
                SELECT id, gross_amount, discount_amount, item_discount, tax_amount, total
                FROM sale_invoices
                WHERE substr(date, 1, 10) >= @from
                  AND substr(date, 1, 10) <= @to
                  AND (status IS NULL OR status != 'reversed')
                  AND ({whereActor})";

            var invoices = await _db.QueryListAsync(invSql, new { from, to, empId, linkedUserId });
            invoicesCount = invoices.Count;
            grossSales = Math.Round(invoices.Sum(i => ToDecimal(i.GetValueOrDefault("gross_amount"))), 2);
            netSales = Math.Round(invoices.Sum(i =>
                Math.Max(0m, ToDecimal(i.GetValueOrDefault("total")) - ToDecimal(i.GetValueOrDefault("tax_amount")))), 2);

            if (invoicesCount > 0)
            {
                var invoiceIds = invoices.Select(i => Convert.ToInt64(i["id"])).Distinct().ToList();
                var hasItemsTable = await _db.HasTableAsync("sale_invoice_items");
                var hasBonusCol = hasItemsTable && await _db.HasColumnAsync("sale_invoice_items", "bonus");
                var saleUnitsExpr = hasBonusCol
                    ? "(COALESCE(sii.quantity, 0) + COALESCE(sii.bonus, 0))"
                    : "COALESCE(sii.quantity, 0)";
                var hasReturnsTable = await _db.HasTableAsync("sale_returns");
                var hasReturnItemsTable = await _db.HasTableAsync("sale_return_items");

                decimal cogsAccum = 0m;
                decimal returnedNetAccum = 0m;
                decimal returnedGrossAccum = 0m;
                decimal returnedCogsAccum = 0m;

                foreach (var chunk in invoiceIds.Chunk(400))
                {
                    var idsArr = chunk.ToArray();
                    if (hasItemsTable)
                    {
                        var cogsVal = await _db.ExecuteScalarAsync<decimal?>($@"
                            SELECT COALESCE(SUM(COALESCE(sii.cost_price_snapshot, p.avg_price, p.unit_purchase_price, 0) * {saleUnitsExpr}), 0)
                            FROM sale_invoice_items sii
                            LEFT JOIN products p ON p.id = sii.product_id
                            WHERE sii.sale_invoice_id IN @ids",
                            new { ids = idsArr }) ?? 0m;
                        cogsAccum += cogsVal;
                    }

                    if (hasReturnsTable)
                    {
                        var retRow = await _db.QuerySingleOrDefaultAsync(@"
                            SELECT COALESCE(SUM(COALESCE(sr.gross_total, sr.total, 0)), 0) as gross_ret,
                                   COALESCE(SUM(CASE WHEN (COALESCE(sr.total, 0) - COALESCE(sr.tax_amount, 0)) > 0
                                                     THEN (COALESCE(sr.total, 0) - COALESCE(sr.tax_amount, 0))
                                                     ELSE 0 END), 0) as net_ret
                            FROM sale_returns sr
                            WHERE sr.sale_invoice_id IN @ids",
                            new { ids = idsArr });
                        if (retRow != null)
                        {
                            returnedGrossAccum += ToDecimal(retRow.GetValueOrDefault("gross_ret"));
                            returnedNetAccum += ToDecimal(retRow.GetValueOrDefault("net_ret"));
                        }

                        if (hasReturnItemsTable)
                        {
                            var retCogs = await _db.ExecuteScalarAsync<decimal?>(@"
                                SELECT COALESCE(SUM(COALESCE(sri.cost_price_snapshot, p.avg_price, p.unit_purchase_price, 0) * COALESCE(sri.unit_return_quantity, 0)), 0)
                                FROM sale_return_items sri
                                INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
                                LEFT JOIN products p ON p.id = sri.product_id
                                WHERE sr.sale_invoice_id IN @ids",
                                new { ids = idsArr }) ?? 0m;
                            returnedCogsAccum += retCogs;
                        }
                    }
                }

                grossSales = Math.Round(Math.Max(0m, grossSales - returnedGrossAccum), 2);
                netSales = Math.Round(Math.Max(0m, netSales - returnedNetAccum), 2);
                cogsTotal = Math.Round(Math.Max(0m, cogsAccum - returnedCogsAccum), 2);
            }
        }

        var grossProfit = Math.Round(Math.Max(0m, netSales - cogsTotal), 2);
        var commType = employee.GetValueOrDefault("commission_type")?.ToString();
        if (string.IsNullOrWhiteSpace(commType)) commType = "none";
        var commRate = Math.Max(0m, ToDecimal(employee.GetValueOrDefault("commission_rate")));
        decimal earnedCommission = 0m;

        if (string.Equals(commType, "net_sales_percent", StringComparison.OrdinalIgnoreCase) && commRate > 0m)
        {
            earnedCommission = Math.Round(netSales * (commRate / 100m), 2);
        }
        else if (string.Equals(commType, "gross_profit_percent", StringComparison.OrdinalIgnoreCase) && commRate > 0m)
        {
            earnedCommission = Math.Round(grossProfit * (commRate / 100m), 2);
        }
        else if (string.Equals(commType, "fixed_per_invoice", StringComparison.OrdinalIgnoreCase) && commRate > 0m)
        {
            earnedCommission = Math.Round(invoicesCount * commRate, 2);
        }

        var target = Math.Max(0m, ToDecimal(employee.GetValueOrDefault("monthly_sales_target")));
        var targetBonusConfig = Math.Max(0m, ToDecimal(employee.GetValueOrDefault("target_bonus_amount")));
        bool targetAchieved = target > 0m && netSales >= target;
        decimal targetProgressPct = target > 0m ? Math.Round(Math.Min(999.9m, (netSales / target) * 100m), 1) : 0m;
        decimal targetBonus = targetAchieved ? Math.Round(targetBonusConfig, 2) : 0m;
        decimal totalIncentive = Math.Round(earnedCommission + targetBonus, 2);

        return new Dictionary<string, object?>
        {
            ["employee_id"] = empId,
            ["employee_code"] = employee.GetValueOrDefault("employee_code"),
            ["employee_name"] = employee.GetValueOrDefault("name"),
            ["department"] = employee.GetValueOrDefault("department"),
            ["designation"] = employee.GetValueOrDefault("designation"),
            ["user_id"] = linkedUserId,
            ["commission_type"] = commType,
            ["commission_rate"] = commRate,
            ["invoices_count"] = invoicesCount,
            ["gross_sales"] = grossSales,
            ["net_sales"] = netSales,
            ["cogs_total"] = cogsTotal,
            ["gross_profit"] = grossProfit,
            ["commission_earned"] = earnedCommission,
            ["monthly_sales_target"] = target,
            ["target_achieved"] = targetAchieved,
            ["target_progress_pct"] = targetProgressPct,
            ["target_bonus"] = targetBonus,
            ["total_incentive"] = totalIncentive
        };
    }

    public async Task<Dictionary<string, object?>> GetCommissionsReportAsync(string fromDate, string toDate)
    {
        await EnsureTablesAsync();

        var from = DateTime.TryParse(fromDate, out var fDt) ? fDt.ToString("yyyy-MM-dd") : fromDate;
        var to = DateTime.TryParse(toDate, out var tDt) ? tDt.ToString("yyyy-MM-dd") : toDate;

        var employees = await _db.QueryListAsync(@"
            SELECT e.*, u.name as linked_user_name
            FROM employees e
            LEFT JOIN users u ON u.id = e.user_id
            WHERE e.status IN ('active', 'on_leave')
            ORDER BY e.name ASC");

        var rows = new List<Dictionary<string, object?>>();
        int totalInvoices = 0;
        decimal totalGrossSales = 0m, totalNetSales = 0m, totalGrossProfit = 0m;
        decimal totalCommissions = 0m, totalTargetBonuses = 0m, totalIncentives = 0m;

        foreach (var emp in employees)
        {
            var metrics = await CalculateEmployeeCommissionAsync(emp, from, to);
            metrics["user_name"] = emp.GetValueOrDefault("linked_user_name");
            rows.Add(metrics);

            totalInvoices += Convert.ToInt32(metrics["invoices_count"] ?? 0);
            totalGrossSales = Math.Round(totalGrossSales + ToDecimal(metrics["gross_sales"]), 2);
            totalNetSales = Math.Round(totalNetSales + ToDecimal(metrics["net_sales"]), 2);
            totalGrossProfit = Math.Round(totalGrossProfit + ToDecimal(metrics["gross_profit"]), 2);
            totalCommissions = Math.Round(totalCommissions + ToDecimal(metrics["commission_earned"]), 2);
            totalTargetBonuses = Math.Round(totalTargetBonuses + ToDecimal(metrics["target_bonus"]), 2);
            totalIncentives = Math.Round(totalIncentives + ToDecimal(metrics["total_incentive"]), 2);
        }

        rows = rows
            .OrderByDescending(r => ToDecimal(r["total_incentive"]))
            .ThenByDescending(r => ToDecimal(r["net_sales"]))
            .ToList();

        return new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, object?>
            {
                ["from"] = from,
                ["to"] = to
            },
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_invoices"] = totalInvoices,
                ["total_gross_sales"] = totalGrossSales,
                ["total_net_sales"] = totalNetSales,
                ["total_gross_profit"] = totalGrossProfit,
                ["total_commissions"] = totalCommissions,
                ["total_target_bonuses"] = totalTargetBonuses,
                ["total_incentives"] = totalIncentives
            },
            ["rows"] = rows
        };
    }

    public async Task<Dictionary<string, object?>> RecordAdvanceTransactionAsync(
        Dictionary<string, object?> data,
        long? userId = null)
    {
        await EnsureTablesAsync();

        if (!data.TryGetValue("employee_id", out var empObj) || empObj == null || !long.TryParse(empObj.ToString(), out var employeeId))
        {
            throw new HrPayrollValidationException("employee_id", "Employee is required.");
        }

        var employee = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = employeeId });
        if (employee == null)
        {
            throw new HrPayrollValidationException("employee_id", "Selected employee does not exist.");
        }

        var type = data.TryGetValue("type", out var tObj) && !string.IsNullOrWhiteSpace(tObj?.ToString())
            ? tObj.ToString()!.Trim().ToLowerInvariant()
            : "advance";
        if (type is not ("advance" or "repayment"))
        {
            throw new HrPayrollValidationException("type", "Transaction type must be either advance or repayment.");
        }

        var amount = Math.Round(ToDecimal(data.GetValueOrDefault("amount")), 2);
        if (amount <= 0m)
        {
            throw new HrPayrollValidationException("amount", "Amount must be greater than zero.");
        }

        var currentBalance = Math.Round(ToDecimal(employee.GetValueOrDefault("advance_balance")), 2);
        if (type == "repayment" && amount > currentBalance + 0.009m)
        {
            throw new HrPayrollValidationException(
                "amount",
                $"Repayment amount ({amount:0.##}) cannot exceed employee's outstanding advance balance ({currentBalance:0.##}).");
        }

        var newBalance = type == "advance"
            ? Math.Round(currentBalance + amount, 2)
            : Math.Round(Math.Max(0m, currentBalance - amount), 2);

        var rawDate = data.GetValueOrDefault("date")?.ToString();
        var dateStr = !string.IsNullOrWhiteSpace(rawDate) && DateTime.TryParse(rawDate, out var parsedDate)
            ? parsedDate.ToString("yyyy-MM-dd")
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var paymentMethod = data.TryGetValue("payment_method", out var pmObj) && !string.IsNullOrWhiteSpace(pmObj?.ToString())
            ? pmObj.ToString()!.Trim()
            : "cash";

        Dictionary<string, object?>? account = null;
        if (await _db.HasTableAsync("accounts"))
        {
            if (data.TryGetValue("account_id", out var accObj) && accObj != null && long.TryParse(accObj.ToString(), out var accId) && accId > 0)
            {
                account = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accId });
            }
            if (account == null)
            {
                var lookupType = string.Equals(paymentMethod, "bank_transfer", StringComparison.OrdinalIgnoreCase) ? "bank" : paymentMethod;
                account = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM accounts WHERE is_active = 1 AND (type = @lookupType OR is_default = 1) ORDER BY CASE WHEN type = @lookupType AND is_default = 1 THEN 0 WHEN type = @lookupType THEN 1 ELSE 2 END, id ASC LIMIT 1",
                    new { lookupType });
            }
        }

        var empName = employee.GetValueOrDefault("name")?.ToString() ?? "Employee";
        var advanceNumber = await GenerateAdvanceNumberAsync(dateStr);
        var description = data.TryGetValue("description", out var descObj) && !string.IsNullOrWhiteSpace(descObj?.ToString())
            ? descObj.ToString()!.Trim()
            : (type == "advance" ? $"Salary advance to {empName}" : $"Advance repayment from {empName}");

        var advanceId = await _db.InsertAsync("employee_advances", new Dictionary<string, object?>
        {
            ["advance_number"] = advanceNumber,
            ["employee_id"] = employeeId,
            ["type"] = type,
            ["amount"] = amount,
            ["balance_after"] = newBalance,
            ["date"] = dateStr,
            ["payment_method"] = paymentMethod,
            ["account_id"] = account != null ? Convert.ToInt64(account["id"]) : (long?)null,
            ["reference_number"] = data.GetValueOrDefault("reference_number")?.ToString(),
            ["description"] = description,
            ["created_by"] = userId
        });

        await _db.UpdateAsync("employees", employeeId, new Dictionary<string, object?>
        {
            ["advance_balance"] = newBalance
        });

        if (account != null)
        {
            var accountId = Convert.ToInt64(account["id"]);
            var accBalance = ToDecimal(account.GetValueOrDefault("current_balance"));
            var newAccBalance = type == "advance"
                ? Math.Round(accBalance - amount, 2)
                : Math.Round(accBalance + amount, 2);

            await _db.UpdateAsync("accounts", accountId, new Dictionary<string, object?>
            {
                ["current_balance"] = newAccBalance
            });

            if (await _db.HasTableAsync("account_transactions"))
            {
                await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
                {
                    ["account_id"] = accountId,
                    ["type"] = type == "advance" ? "debit" : "credit",
                    ["amount"] = amount,
                    ["balance_after"] = newAccBalance,
                    ["reference_type"] = "App\\Models\\EmployeeAdvance",
                    ["reference_id"] = advanceId,
                    ["description"] = type == "advance"
                        ? $"Staff Advance {advanceNumber} - {empName}"
                        : $"Staff Advance Repayment {advanceNumber} - {empName}",
                    ["user_id"] = userId,
                    ["transaction_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }

        var advRow = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employee_advances WHERE id = @id", new { id = advanceId });
        if (advRow != null)
        {
            advRow["employee"] = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = employeeId });
            advRow["account"] = account;
        }
        return advRow!;
    }

    public async Task<Dictionary<string, object?>> GenerateOrRefreshPayrollRunAsync(
        string periodMonth,
        Dictionary<string, object?>? options = null,
        long? userId = null)
    {
        await EnsureTablesAsync();

        if (string.IsNullOrWhiteSpace(periodMonth) || !Regex.IsMatch(periodMonth.Trim(), @"^\d{4}-\d{2}$"))
        {
            throw new HrPayrollValidationException("period_month", "Payroll period must be in YYYY-MM format (e.g. 2026-10).");
        }

        periodMonth = periodMonth.Trim();
        var monthDt = DateTime.ParseExact($"{periodMonth}-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var startDate = new DateTime(monthDt.Year, monthDt.Month, 1).ToString("yyyy-MM-dd");
        var endDate = new DateTime(monthDt.Year, monthDt.Month, DateTime.DaysInMonth(monthDt.Year, monthDt.Month)).ToString("yyyy-MM-dd");

        bool autoDeductAdvances = true;
        if (options != null && options.TryGetValue("auto_deduct_advances", out var adaObj) && adaObj != null)
        {
            autoDeductAdvances = adaObj is true or 1 or 1L || (adaObj is string adas && (adas == "1" || adas.Equals("true", StringComparison.OrdinalIgnoreCase)));
        }

        var notes = options != null && options.TryGetValue("notes", out var ntObj) && !string.IsNullOrWhiteSpace(ntObj?.ToString())
            ? ntObj.ToString()!.Trim()
            : null;

        var run = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_runs WHERE period_month = @periodMonth LIMIT 1", new { periodMonth });
        long runId;
        if (run == null)
        {
            runId = await _db.InsertAsync("payroll_runs", new Dictionary<string, object?>
            {
                ["run_number"] = $"PAY-{periodMonth}",
                ["period_month"] = periodMonth,
                ["start_date"] = startDate,
                ["end_date"] = endDate,
                ["status"] = "draft",
                ["notes"] = notes ?? $"Monthly payroll & sales commissions for {periodMonth}",
                ["generated_by"] = userId
            });
        }
        else
        {
            runId = Convert.ToInt64(run["id"]);
            if (!string.IsNullOrWhiteSpace(notes))
            {
                await _db.UpdateAsync("payroll_runs", runId, new Dictionary<string, object?>
                {
                    ["notes"] = notes
                });
            }
        }

        var employees = await _db.QueryListAsync("SELECT * FROM employees WHERE status IN ('active', 'on_leave') ORDER BY id ASC");

        foreach (var employee in employees)
        {
            var empId = Convert.ToInt64(employee["id"]);
            var existingItem = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM payroll_items WHERE payroll_run_id = @runId AND employee_id = @empId LIMIT 1",
                new { runId, empId });

            if (existingItem != null && string.Equals(existingItem.GetValueOrDefault("payment_status")?.ToString(), "paid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attendances = await _db.QueryListAsync(@"
                SELECT * FROM employee_attendances
                WHERE employee_id = @empId
                  AND date >= @startDate
                  AND date <= @endDate",
                new { empId, startDate, endDate });

            int presentDays = attendances.Count(a => string.Equals(a.GetValueOrDefault("status")?.ToString(), "present", StringComparison.OrdinalIgnoreCase));
            int lateDays = attendances.Count(a => string.Equals(a.GetValueOrDefault("status")?.ToString(), "late", StringComparison.OrdinalIgnoreCase));
            int absentDays = attendances.Count(a => string.Equals(a.GetValueOrDefault("status")?.ToString(), "absent", StringComparison.OrdinalIgnoreCase));
            int halfDays = attendances.Count(a => string.Equals(a.GetValueOrDefault("status")?.ToString(), "half_day", StringComparison.OrdinalIgnoreCase));
            int leaveDays = attendances.Count(a =>
            {
                var st = a.GetValueOrDefault("status")?.ToString()?.ToLowerInvariant();
                return st is "paid_leave" or "holiday";
            });

            var overtimeHours = Math.Round(attendances.Sum(a => ToDecimal(a.GetValueOrDefault("overtime_hours"))), 2);
            var totalFines = Math.Round(attendances.Sum(a => ToDecimal(a.GetValueOrDefault("fine_amount"))), 2);

            var workingDays = Math.Max(1, ToInt(employee.GetValueOrDefault("working_days_per_month"), 26));
            var basicSalary = Math.Round(ToDecimal(employee.GetValueOrDefault("basic_salary")), 2);
            var allowances = Math.Round(ToDecimal(employee.GetValueOrDefault("default_allowances")), 2);
            var dailyRate = workingDays > 0 ? (basicSalary / workingDays) : 0m;

            var absenceDeduction = Math.Round((absentDays * dailyRate) + (halfDays * (dailyRate * 0.5m)), 2);
            var lateOrFineDeduction = Math.Round(totalFines, 2);
            var overtimePay = Math.Round(overtimeHours * ToDecimal(employee.GetValueOrDefault("hourly_overtime_rate")), 2);

            var commData = await CalculateEmployeeCommissionAsync(employee, startDate, endDate);
            int salesInvoicesCount = Convert.ToInt32(commData["invoices_count"] ?? 0);
            var salesVolume = Math.Round(ToDecimal(commData["net_sales"]), 2);
            var salesGrossProfit = Math.Round(ToDecimal(commData["gross_profit"]), 2);
            var salesCommission = Math.Round(ToDecimal(commData["commission_earned"]), 2);
            var targetBonus = Math.Round(ToDecimal(commData["target_bonus"]), 2);
            var otherBonus = existingItem != null ? Math.Round(ToDecimal(existingItem.GetValueOrDefault("other_bonus")), 2) : 0m;
            var otherDeduction = existingItem != null ? Math.Round(ToDecimal(existingItem.GetValueOrDefault("other_deduction")), 2) : 0m;

            var grossEarnings = Math.Round(
                basicSalary + allowances + overtimePay + salesCommission + targetBonus + otherBonus,
                2);

            var preAdvanceNet = Math.Max(0m, grossEarnings - (absenceDeduction + lateOrFineDeduction + otherDeduction));
            var scheduledElsewhere = await _db.ExecuteScalarAsync<decimal?>(@"
                SELECT COALESCE(SUM(COALESCE(advance_deduction, 0)), 0)
                FROM payroll_items
                WHERE employee_id = @empId
                  AND payroll_run_id != @runId
                  AND payment_status != 'paid'",
                new { empId, runId }) ?? 0m;

            var availableAdvanceBalance = Math.Max(0m, ToDecimal(employee.GetValueOrDefault("advance_balance")) - scheduledElsewhere);
            var advanceDeduction = autoDeductAdvances
                ? Math.Round(Math.Min(availableAdvanceBalance, preAdvanceNet), 2)
                : 0m;

            var totalDeductions = Math.Round(
                absenceDeduction + lateOrFineDeduction + advanceDeduction + otherDeduction,
                2);
            var netPayable = Math.Round(Math.Max(0m, grossEarnings - totalDeductions), 2);

            var payMethod = existingItem?.GetValueOrDefault("payment_method")?.ToString()
                            ?? employee.GetValueOrDefault("payment_method")?.ToString()
                            ?? "cash";

            var itemPayload = new Dictionary<string, object?>
            {
                ["working_days"] = workingDays,
                ["present_days"] = presentDays + lateDays,
                ["absent_days"] = absentDays,
                ["late_days"] = lateDays,
                ["half_days"] = halfDays,
                ["leave_days"] = leaveDays,
                ["overtime_hours"] = overtimeHours,
                ["basic_salary"] = basicSalary,
                ["allowances"] = allowances,
                ["overtime_pay"] = overtimePay,
                ["sales_invoices_count"] = salesInvoicesCount,
                ["sales_volume"] = salesVolume,
                ["sales_gross_profit"] = salesGrossProfit,
                ["sales_commission"] = salesCommission,
                ["target_bonus"] = targetBonus,
                ["other_bonus"] = otherBonus,
                ["gross_earnings"] = grossEarnings,
                ["absence_deduction"] = absenceDeduction,
                ["late_or_fine_deduction"] = lateOrFineDeduction,
                ["advance_deduction"] = advanceDeduction,
                ["other_deduction"] = otherDeduction,
                ["total_deductions"] = totalDeductions,
                ["net_payable"] = netPayable,
                ["payment_method"] = payMethod
            };

            if (existingItem != null)
            {
                await _db.UpdateAsync("payroll_items", Convert.ToInt64(existingItem["id"]), itemPayload);
            }
            else
            {
                itemPayload["payroll_run_id"] = runId;
                itemPayload["employee_id"] = empId;
                itemPayload["payment_status"] = "unpaid";
                await _db.InsertAsync("payroll_items", itemPayload);
            }
        }

        return await RecalculateRunTotalsAsync(runId);
    }

    public async Task<Dictionary<string, object?>> UpdatePayrollItemAsync(long itemId, Dictionary<string, object?> data)
    {
        await EnsureTablesAsync();

        var item = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_items WHERE id = @id", new { id = itemId });
        if (item == null) throw new KeyNotFoundException("Payroll item not found.");

        if (string.Equals(item.GetValueOrDefault("payment_status")?.ToString(), "paid", StringComparison.OrdinalIgnoreCase))
        {
            throw new HrPayrollValidationException("payment_status", "Cannot modify a payslip that has already been paid.");
        }

        var empId = Convert.ToInt64(item["employee_id"]);
        var employee = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = empId });

        decimal ReadField(string key) =>
            data.TryGetValue(key, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString())
                ? Math.Round(ToDecimal(v), 2)
                : Math.Round(ToDecimal(item.GetValueOrDefault(key)), 2);

        var basicSalary = ReadField("basic_salary");
        var allowances = ReadField("allowances");
        var overtimePay = ReadField("overtime_pay");
        var salesCommission = ReadField("sales_commission");
        var targetBonus = ReadField("target_bonus");
        var otherBonus = ReadField("other_bonus");

        var absenceDeduction = ReadField("absence_deduction");
        var lateOrFineDeduction = ReadField("late_or_fine_deduction");
        var advanceDeduction = ReadField("advance_deduction");
        var otherDeduction = ReadField("other_deduction");

        var empAdvBalance = employee != null ? ToDecimal(employee.GetValueOrDefault("advance_balance")) : 0m;
        if (advanceDeduction > empAdvBalance + 0.009m)
        {
            throw new HrPayrollValidationException(
                "advance_deduction",
                $"Advance deduction ({advanceDeduction:0.##}) cannot exceed employee's outstanding advance balance ({empAdvBalance:0.##}).");
        }

        var grossEarnings = Math.Round(
            basicSalary + allowances + overtimePay + salesCommission + targetBonus + otherBonus,
            2);
        var totalDeductions = Math.Round(
            absenceDeduction + lateOrFineDeduction + advanceDeduction + otherDeduction,
            2);
        var netPayable = Math.Round(Math.Max(0m, grossEarnings - totalDeductions), 2);

        var notes = data.ContainsKey("notes")
            ? data["notes"]?.ToString()
            : item.GetValueOrDefault("notes")?.ToString();

        await _db.UpdateAsync("payroll_items", itemId, new Dictionary<string, object?>
        {
            ["basic_salary"] = basicSalary,
            ["allowances"] = allowances,
            ["overtime_pay"] = overtimePay,
            ["sales_commission"] = salesCommission,
            ["target_bonus"] = targetBonus,
            ["other_bonus"] = otherBonus,
            ["gross_earnings"] = grossEarnings,
            ["absence_deduction"] = absenceDeduction,
            ["late_or_fine_deduction"] = lateOrFineDeduction,
            ["advance_deduction"] = advanceDeduction,
            ["other_deduction"] = otherDeduction,
            ["total_deductions"] = totalDeductions,
            ["net_payable"] = netPayable,
            ["notes"] = notes
        });

        var runId = Convert.ToInt64(item["payroll_run_id"]);
        await RecalculateRunTotalsAsync(runId);

        var updated = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_items WHERE id = @id", new { id = itemId });
        if (updated != null)
        {
            updated["employee"] = employee;
            updated["payroll_run"] = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_runs WHERE id = @id", new { id = runId });
        }
        return updated!;
    }

    public async Task<Dictionary<string, object?>> PayPayrollItemAsync(
        long itemId,
        Dictionary<string, object?>? paymentData = null,
        long? userId = null)
    {
        await EnsureTablesAsync();

        var item = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_items WHERE id = @id", new { id = itemId });
        if (item == null) throw new KeyNotFoundException("Payroll item not found.");

        if (string.Equals(item.GetValueOrDefault("payment_status")?.ToString(), "paid", StringComparison.OrdinalIgnoreCase))
        {
            throw new HrPayrollValidationException("payment_status", "This payslip has already been paid.");
        }

        var empId = Convert.ToInt64(item["employee_id"]);
        var runId = Convert.ToInt64(item["payroll_run_id"]);
        var employee = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = empId });
        if (employee == null) throw new KeyNotFoundException("Employee not found.");
        var run = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_runs WHERE id = @id", new { id = runId });
        if (run == null) throw new KeyNotFoundException("Payroll run not found.");

        var paymentMethod = paymentData != null && paymentData.TryGetValue("payment_method", out var pmObj) && !string.IsNullOrWhiteSpace(pmObj?.ToString())
            ? pmObj.ToString()!.Trim()
            : (item.GetValueOrDefault("payment_method")?.ToString()
               ?? employee.GetValueOrDefault("payment_method")?.ToString()
               ?? "cash");

        var expensePaymentMethod = string.Equals(paymentMethod, "bank_transfer", StringComparison.OrdinalIgnoreCase)
            ? "bank"
            : paymentMethod;

        long? accountId = null;
        if (paymentData != null && paymentData.TryGetValue("account_id", out var accObj) && accObj != null && long.TryParse(accObj.ToString(), out var parsedAccId) && parsedAccId > 0)
        {
            accountId = parsedAccId;
        }

        var rawDate = paymentData != null && paymentData.TryGetValue("date", out var dObj) ? dObj?.ToString() : null;
        var paymentDate = !string.IsNullOrWhiteSpace(rawDate) && DateTime.TryParse(rawDate, out var parsedDate)
            ? parsedDate.ToString("yyyy-MM-dd")
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var runNumber = run.GetValueOrDefault("run_number")?.ToString() ?? $"PAY-{runId}";
        var periodMonth = run.GetValueOrDefault("period_month")?.ToString() ?? "";
        var empName = employee.GetValueOrDefault("name")?.ToString() ?? "Employee";
        var empCode = employee.GetValueOrDefault("employee_code")?.ToString() ?? $"EMP-{empId}";

        // 1. Recover staff advance deduction if applicable
        var advDeduct = Math.Round(ToDecimal(item.GetValueOrDefault("advance_deduction")), 2);
        if (advDeduct > 0m)
        {
            var currentAdv = Math.Round(ToDecimal(employee.GetValueOrDefault("advance_balance")), 2);
            var actualDeduct = Math.Min(advDeduct, currentAdv);
            if (actualDeduct > 0m)
            {
                var newAdvBalance = Math.Round(Math.Max(0m, currentAdv - actualDeduct), 2);
                var advNo = await GenerateAdvanceNumberAsync(paymentDate);
                await _db.InsertAsync("employee_advances", new Dictionary<string, object?>
                {
                    ["advance_number"] = advNo,
                    ["employee_id"] = empId,
                    ["type"] = "payroll_deduction",
                    ["amount"] = actualDeduct,
                    ["balance_after"] = newAdvBalance,
                    ["date"] = paymentDate,
                    ["payment_method"] = "payroll_deduction",
                    ["payroll_item_id"] = itemId,
                    ["reference_number"] = runNumber,
                    ["description"] = $"Payroll advance deduction ({periodMonth}) for {empName}",
                    ["created_by"] = userId
                });

                await _db.UpdateAsync("employees", empId, new Dictionary<string, object?>
                {
                    ["advance_balance"] = newAdvBalance
                });
            }
        }

        // 2. Disburse net_payable via Expense + Treasury Account
        long? expenseId = null;
        var netPayable = Math.Round(ToDecimal(item.GetValueOrDefault("net_payable")), 2);
        if (netPayable > 0m && await _db.HasTableAsync("expenses") && await _db.HasTableAsync("expense_categories"))
        {
            var cat = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM expense_categories WHERE name = 'Staff Salaries, Payroll & Commissions' LIMIT 1");
            long catId;
            if (cat != null)
            {
                catId = Convert.ToInt64(cat["id"]);
            }
            else
            {
                catId = await _db.InsertAsync("expense_categories", new Dictionary<string, object?>
                {
                    ["name"] = "Staff Salaries, Payroll & Commissions",
                    ["description"] = "Monthly employee payroll, overtime, and salesman/cashier commissions",
                    ["is_active"] = 1
                });
            }

            Dictionary<string, object?>? account = null;
            if (await _db.HasTableAsync("accounts"))
            {
                if (accountId.HasValue)
                {
                    account = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accountId.Value });
                }
                if (account == null)
                {
                    account = await _db.QuerySingleOrDefaultAsync(
                        "SELECT * FROM accounts WHERE is_active = 1 AND (type = @t OR is_default = 1) ORDER BY CASE WHEN type = @t AND is_default = 1 THEN 0 WHEN type = @t THEN 1 ELSE 2 END, id ASC LIMIT 1",
                        new { t = expensePaymentMethod });
                    if (account != null) accountId = Convert.ToInt64(account["id"]);
                }
            }

            var ymd = DateTime.TryParse(paymentDate, out var pdt) ? pdt.ToString("yyyyMMdd") : DateTime.UtcNow.ToString("yyyyMMdd");
            var expNo = await new DocumentSequenceService(_db).NextAsync(
                $"expense:{ymd}",
                $"EXP-{ymd}-",
                4,
                "expenses",
                "expense_number");

            expenseId = await _db.InsertAsync("expenses", new Dictionary<string, object?>
            {
                ["expense_number"] = expNo,
                ["expense_category_id"] = catId,
                ["account_id"] = accountId,
                ["user_id"] = userId,
                ["amount"] = netPayable,
                ["date"] = paymentDate,
                ["payment_method"] = expensePaymentMethod,
                ["reference_number"] = $"{runNumber}-{empCode}",
                ["payee"] = empName,
                ["description"] = $"Payroll ({periodMonth}) for {empName} ({empCode})",
                ["is_recurring"] = 0
            });

            if (account != null && accountId.HasValue)
            {
                var curBal = ToDecimal(account.GetValueOrDefault("current_balance"));
                var newBal = Math.Round(curBal - netPayable, 2);
                await _db.UpdateAsync("accounts", accountId.Value, new Dictionary<string, object?>
                {
                    ["current_balance"] = newBal
                });

                if (await _db.HasTableAsync("account_transactions"))
                {
                    await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
                    {
                        ["account_id"] = accountId.Value,
                        ["type"] = "debit",
                        ["amount"] = netPayable,
                        ["balance_after"] = newBal,
                        ["reference_type"] = "App\\Models\\Expense",
                        ["reference_id"] = expenseId.Value,
                        ["description"] = $"Payroll ({periodMonth}) for {empName} ({empCode})",
                        ["user_id"] = userId,
                        ["transaction_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
            }
        }

        await _db.UpdateAsync("payroll_items", itemId, new Dictionary<string, object?>
        {
            ["payment_status"] = "paid",
            ["payment_method"] = paymentMethod,
            ["account_id"] = accountId,
            ["expense_id"] = expenseId,
            ["paid_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });

        await RecalculateRunTotalsAsync(runId);

        var refreshed = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_items WHERE id = @id", new { id = itemId });
        if (refreshed != null)
        {
            refreshed["employee"] = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = empId });
            refreshed["payroll_run"] = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_runs WHERE id = @id", new { id = runId });
            refreshed["account"] = accountId.HasValue && await _db.HasTableAsync("accounts")
                ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accountId.Value })
                : null;
            refreshed["expense"] = expenseId.HasValue && await _db.HasTableAsync("expenses")
                ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM expenses WHERE id = @id", new { id = expenseId.Value })
                : null;
        }
        return refreshed!;
    }

    public async Task<Dictionary<string, object?>> PayAllInRunAsync(
        long runId,
        Dictionary<string, object?>? paymentData = null,
        long? userId = null)
    {
        await EnsureTablesAsync();

        var run = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_runs WHERE id = @id", new { id = runId });
        if (run == null) throw new KeyNotFoundException("Payroll run not found.");

        var unpaidItems = await _db.QueryListAsync(
            "SELECT id FROM payroll_items WHERE payroll_run_id = @runId AND payment_status != 'paid' ORDER BY id ASC",
            new { runId });

        foreach (var item in unpaidItems)
        {
            var itemId = Convert.ToInt64(item["id"]);
            await PayPayrollItemAsync(itemId, paymentData, userId);
        }

        return await RecalculateRunTotalsAsync(runId);
    }

    public async Task<Dictionary<string, object?>> RecalculateRunTotalsAsync(long runId)
    {
        var run = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_runs WHERE id = @id", new { id = runId });
        if (run == null) throw new KeyNotFoundException("Payroll run not found.");

        var items = await _db.QueryListAsync("SELECT * FROM payroll_items WHERE payroll_run_id = @runId ORDER BY id ASC", new { runId });

        decimal totalBasic = Math.Round(items.Sum(i => ToDecimal(i.GetValueOrDefault("basic_salary"))), 2);
        decimal totalAllowances = Math.Round(items.Sum(i => ToDecimal(i.GetValueOrDefault("allowances"))), 2);
        decimal totalOvertime = Math.Round(items.Sum(i => ToDecimal(i.GetValueOrDefault("overtime_pay"))), 2);
        decimal totalCommissions = Math.Round(items.Sum(i => ToDecimal(i.GetValueOrDefault("sales_commission"))), 2);
        decimal totalBonuses = Math.Round(items.Sum(i => ToDecimal(i.GetValueOrDefault("target_bonus")) + ToDecimal(i.GetValueOrDefault("other_bonus"))), 2);
        decimal totalAdvanceDeductions = Math.Round(items.Sum(i => ToDecimal(i.GetValueOrDefault("advance_deduction"))), 2);
        decimal totalDeductions = Math.Round(items.Sum(i => ToDecimal(i.GetValueOrDefault("total_deductions"))), 2);
        decimal totalNetPayable = Math.Round(items.Sum(i => ToDecimal(i.GetValueOrDefault("net_payable"))), 2);
        decimal totalPaid = Math.Round(items
            .Where(i => string.Equals(i.GetValueOrDefault("payment_status")?.ToString(), "paid", StringComparison.OrdinalIgnoreCase))
            .Sum(i => ToDecimal(i.GetValueOrDefault("net_payable"))), 2);

        bool allPaid = items.Count > 0 && items.All(i => string.Equals(i.GetValueOrDefault("payment_status")?.ToString(), "paid", StringComparison.OrdinalIgnoreCase));
        bool anyPaid = items.Any(i => string.Equals(i.GetValueOrDefault("payment_status")?.ToString(), "paid", StringComparison.OrdinalIgnoreCase));

        var status = allPaid ? "paid" : (anyPaid ? "partial" : "draft");
        var paidAt = allPaid
            ? (run.GetValueOrDefault("paid_at")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
            : null;

        await _db.UpdateAsync("payroll_runs", runId, new Dictionary<string, object?>
        {
            ["status"] = status,
            ["total_basic"] = totalBasic,
            ["total_allowances"] = totalAllowances,
            ["total_overtime"] = totalOvertime,
            ["total_commissions"] = totalCommissions,
            ["total_bonuses"] = totalBonuses,
            ["total_deductions"] = totalDeductions,
            ["total_advance_deductions"] = totalAdvanceDeductions,
            ["total_net_payable"] = totalNetPayable,
            ["total_paid"] = totalPaid,
            ["paid_at"] = paidAt
        });

        var refreshed = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM payroll_runs WHERE id = @id", new { id = runId }))!;
        await EnrichPayrollRunsBatchAsync(new List<Dictionary<string, object?>> { refreshed });
        return refreshed;
    }

    public async Task<List<Dictionary<string, object?>>> EnrichPayrollRunsBatchAsync(List<Dictionary<string, object?>> runs)
    {
        if (runs.Count == 0) return runs;

        var runIds = runs.Select(r => Convert.ToInt64(r["id"])).Distinct().ToList();
        var genUserIds = runs
            .Select(r => r.TryGetValue("generated_by", out var g) && g != null && long.TryParse(g.ToString(), out var gid) ? (long?)gid : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();

        var usersMap = new Dictionary<long, Dictionary<string, object?>>();
        if (genUserIds.Count > 0 && await _db.HasTableAsync("users"))
        {
            var uRows = await _db.QueryListAsync("SELECT id, name FROM users WHERE id IN @ids", new { ids = genUserIds });
            foreach (var u in uRows) usersMap[Convert.ToInt64(u["id"])] = u;
        }

        var allItems = await _db.QueryListAsync(
            "SELECT * FROM payroll_items WHERE payroll_run_id IN @ids ORDER BY id ASC",
            new { ids = runIds });

        var empIds = allItems
            .Select(i => i.TryGetValue("employee_id", out var e) && e != null && long.TryParse(e.ToString(), out var eid) ? (long?)eid : null)
            .Where(e => e.HasValue)
            .Select(e => e!.Value)
            .Distinct()
            .ToList();

        var accIds = allItems
            .Select(i => i.TryGetValue("account_id", out var a) && a != null && long.TryParse(a.ToString(), out var aid) ? (long?)aid : null)
            .Where(a => a.HasValue)
            .Select(a => a!.Value)
            .Distinct()
            .ToList();

        var empMap = new Dictionary<long, Dictionary<string, object?>>();
        if (empIds.Count > 0)
        {
            var eRows = await _db.QueryListAsync("SELECT * FROM employees WHERE id IN @ids", new { ids = empIds });
            foreach (var e in eRows) empMap[Convert.ToInt64(e["id"])] = e;
        }

        var accMap = new Dictionary<long, Dictionary<string, object?>>();
        if (accIds.Count > 0 && await _db.HasTableAsync("accounts"))
        {
            var aRows = await _db.QueryListAsync("SELECT id, name, code, type FROM accounts WHERE id IN @ids", new { ids = accIds });
            foreach (var a in aRows) accMap[Convert.ToInt64(a["id"])] = a;
        }

        foreach (var item in allItems)
        {
            item["employee"] = item.TryGetValue("employee_id", out var eObj) && eObj != null && long.TryParse(eObj.ToString(), out var eid) && empMap.TryGetValue(eid, out var eDict) ? eDict : null;
            item["account"] = item.TryGetValue("account_id", out var aObj) && aObj != null && long.TryParse(aObj.ToString(), out var aid) && accMap.TryGetValue(aid, out var aDict) ? aDict : null;
        }

        var itemsByRun = allItems
            .GroupBy(i => Convert.ToInt64(i["payroll_run_id"]))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var r in runs)
        {
            var rid = Convert.ToInt64(r["id"]);
            r["items"] = itemsByRun.TryGetValue(rid, out var rItems) ? rItems : new List<Dictionary<string, object?>>();
            r["generated_by_user"] = r.TryGetValue("generated_by", out var gObj) && gObj != null && long.TryParse(gObj.ToString(), out var gid) && usersMap.TryGetValue(gid, out var uDict) ? uDict : null;
        }

        return runs;
    }

    private static decimal ToDecimal(object? val) => StocktakeService.ToDecimal(val);

    private static int ToInt(object? val, int defaultVal = 0)
    {
        if (val == null) return defaultVal;
        if (val is int i) return i;
        if (val is long l) return (int)l;
        if (int.TryParse(val.ToString(), out var parsed)) return parsed;
        return defaultVal;
    }
}
