using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace Karobar.Api.Controllers;

[Route("api/expenses")]
[Authorize]
public class ExpenseController : ApiControllerBase
{
    private readonly ExpenseService _expenseService;

    public ExpenseController(KarobarDb db) : base(db)
    {
        _expenseService = new ExpenseService(db);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await CanAsync("expense.view")) return Forbidden();

        var where = new List<string> { "deleted_at IS NULL" };
        var dyn = new DynamicParameters();

        var from = Request.Query["from"].ToString();
        if (!string.IsNullOrWhiteSpace(from))
        {
            where.Add("DATE(date) >= @from");
            dyn.Add("from", from);
        }

        var to = Request.Query["to"].ToString();
        if (!string.IsNullOrWhiteSpace(to))
        {
            where.Add("DATE(date) <= @to");
            dyn.Add("to", to);
        }

        var categoryId = Request.Query["category_id"].ToString();
        if (!string.IsNullOrWhiteSpace(categoryId) && long.TryParse(categoryId, out var cid))
        {
            where.Add("expense_category_id = @cid");
            dyn.Add("cid", cid);
        }

        var paymentMethod = Request.Query["payment_method"].ToString();
        if (!string.IsNullOrWhiteSpace(paymentMethod))
        {
            where.Add("payment_method = @paymentMethod");
            dyn.Add("paymentMethod", paymentMethod);
        }

        var accountId = Request.Query["account_id"].ToString();
        if (!string.IsNullOrWhiteSpace(accountId) && long.TryParse(accountId, out var aid))
        {
            where.Add("account_id = @aid");
            dyn.Add("aid", aid);
        }

        var isRecurringStr = Request.Query["is_recurring"].ToString();
        if (!string.IsNullOrWhiteSpace(isRecurringStr))
        {
            var isRec = isRecurringStr == "1" || isRecurringStr.Equals("true", StringComparison.OrdinalIgnoreCase);
            where.Add("is_recurring = @isRec");
            dyn.Add("isRec", isRec ? 1 : 0);
        }

        var search = Request.Query["search"].ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(expense_number LIKE @search OR payee LIKE @search OR description LIKE @search OR reference_number LIKE @search)");
            dyn.Add("search", $"%{search}%");
        }

        var whereSql = string.Join(" AND ", where);
        var page = int.TryParse(Request.Query["page"], out var pg) ? Math.Max(1, pg) : 1;
        var perPage = int.TryParse(Request.Query["per_page"], out var pp) ? Math.Clamp(pp, 5, 100) : 25;

        var expenses = await Db.PaginateAsync(
            $"SELECT * FROM expenses WHERE {whereSql} ORDER BY date DESC, id DESC",
            $"SELECT COUNT(1) FROM expenses WHERE {whereSql}",
            dyn,
            page,
            perPage,
            async batch =>
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    batch[i] = await _expenseService.EnrichExpenseAsync(batch[i]);
                }
                return batch;
            });

        var totalAmount = await Db.ExecuteScalarAsync<decimal?>(
            $"SELECT COALESCE(SUM(amount), 0) FROM expenses WHERE {whereSql}",
            dyn) ?? 0m;

        return Ok(new Dictionary<string, object?>
        {
            ["expenses"] = expenses,
            ["total_amount"] = Math.Round(totalAmount, 2)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("expense.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var categoryId = GetNullableLong(body, "expense_category_id") ?? 0;
        var amount = GetDecimal(body, "amount", 0m);
        var date = GetString(body, "date");
        var paymentMethod = GetString(body, "payment_method");

        if (categoryId <= 0)
            return Unprocessable("The expense category id field is required.", new() { ["expense_category_id"] = new[] { "The expense category id field is required." } });
        if (amount < 0.01m)
            return Unprocessable("Expense amount must be at least 0.01.", new() { ["amount"] = new[] { "Expense amount must be at least 0.01." } });
        if (string.IsNullOrWhiteSpace(date))
            return Unprocessable("The date field is required.", new() { ["date"] = new[] { "The date field is required." } });

        var validMethods = new[] { "cash", "bank_transfer", "card", "mobile_wallet", "cheque", "other" };
        if (string.IsNullOrWhiteSpace(paymentMethod) || !validMethods.Contains(paymentMethod.ToLowerInvariant()))
            return Unprocessable("Invalid payment method.", new() { ["payment_method"] = new[] { "The selected payment method is invalid." } });

        var isRecurring = GetBool(body, "is_recurring", false);
        var recurringFrequency = GetString(body, "recurring_frequency");
        if (isRecurring && recurringFrequency is not ("weekly" or "monthly" or "yearly"))
        {
            return Unprocessable("Recurring frequency is required when is_recurring is true.", new()
            {
                ["recurring_frequency"] = new[] { "The recurring frequency field is required when is recurring is true." }
            });
        }

        string? receiptPath = null;
        if (Request.HasFormContentType && Request.Form.Files.GetFile("receipt") is { Length: > 0 } file)
        {
            receiptPath = await SaveReceiptFileAsync(file);
        }

        var data = new Dictionary<string, object?>
        {
            ["expense_category_id"] = categoryId,
            ["amount"] = amount,
            ["date"] = date,
            ["payment_method"] = paymentMethod.ToLowerInvariant(),
            ["account_id"] = GetNullableLong(body, "account_id"),
            ["reference_number"] = GetString(body, "reference_number"),
            ["payee"] = GetString(body, "payee"),
            ["description"] = GetString(body, "description"),
            ["receipt_path"] = receiptPath,
            ["is_recurring"] = isRecurring,
            ["recurring_frequency"] = isRecurring ? recurringFrequency : null
        };

        try
        {
            var expense = await _expenseService.CreateExpenseAsync(data, CurrentUserId, CurrentTerminalId);
            return StatusCode(201, new Dictionary<string, object?>
            {
                ["message"] = "Expense recorded successfully.",
                ["expense"] = expense
            });
        }
        catch (KeyNotFoundException ex)
        {
            return Unprocessable(ex.Message, new() { ["expense_category_id"] = new[] { ex.Message } });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("expense.view")) return Forbidden();

        var expense = await Db.QuerySingleOrDefaultAsync("SELECT * FROM expenses WHERE id = @id AND deleted_at IS NULL", new { id });
        if (expense == null) return NotFound(new { message = "Expense not found." });

        return Ok(await _expenseService.EnrichExpenseAsync(expense));
    }

    [HttpPut("{id:long}")]
    [HttpPost("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("expense.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM expenses WHERE id = @id AND deleted_at IS NULL", new { id });
        if (existing == null) return NotFound(new { message = "Expense not found." });

        var body = await ReadJsonBodyAsync();
        var categoryId = GetNullableLong(body, "expense_category_id") ?? AccountService.ToLong(existing.GetValueOrDefault("expense_category_id"));
        var amount = GetDecimal(body, "amount", AccountService.ToDecimal(existing.GetValueOrDefault("amount")));
        var date = GetString(body, "date") ?? existing.GetValueOrDefault("date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        var paymentMethod = GetString(body, "payment_method") ?? existing.GetValueOrDefault("payment_method")?.ToString() ?? "cash";

        if (categoryId <= 0 || amount < 0.01m)
            return Unprocessable("Valid category and amount are required.");

        var isRecurring = body.ContainsKey("is_recurring")
            ? GetBool(body, "is_recurring")
            : AccountService.IsTruthy(existing.GetValueOrDefault("is_recurring"));
        var recurringFrequency = body.ContainsKey("recurring_frequency")
            ? GetString(body, "recurring_frequency")
            : existing.GetValueOrDefault("recurring_frequency")?.ToString();

        var data = new Dictionary<string, object?>
        {
            ["expense_category_id"] = categoryId,
            ["amount"] = amount,
            ["date"] = date,
            ["payment_method"] = paymentMethod.ToLowerInvariant(),
            ["is_recurring"] = isRecurring,
            ["recurring_frequency"] = isRecurring ? recurringFrequency : null
        };

        if (body.ContainsKey("account_id")) data["account_id"] = GetNullableLong(body, "account_id");
        if (body.ContainsKey("reference_number")) data["reference_number"] = GetString(body, "reference_number");
        if (body.ContainsKey("payee")) data["payee"] = GetString(body, "payee");
        if (body.ContainsKey("description")) data["description"] = GetString(body, "description");

        if (Request.HasFormContentType && Request.Form.Files.GetFile("receipt") is { Length: > 0 } file)
        {
            data["receipt_path"] = await SaveReceiptFileAsync(file);
        }
        else if (GetBool(body, "remove_receipt", false))
        {
            data["receipt_path"] = null;
        }

        try
        {
            var updated = await _expenseService.UpdateExpenseAsync(id, data, CurrentUserId, CurrentTerminalId);
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Expense updated successfully.",
                ["expense"] = updated
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("expense.delete")) return Forbidden();

        try
        {
            await _expenseService.DeleteExpenseAsync(id, CurrentUserId, CurrentTerminalId);
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Expense deleted and financial balances reversed successfully."
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        if (!await CanAsync("expense.view")) return Forbidden();

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var monthEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)).ToString("yyyy-MM-dd");

        var todayTotal = await Db.ExecuteScalarAsync<decimal?>(
            "SELECT COALESCE(SUM(amount), 0) FROM expenses WHERE DATE(date) = @today AND deleted_at IS NULL",
            new { today }) ?? 0m;

        var monthTotal = await Db.ExecuteScalarAsync<decimal?>(
            "SELECT COALESCE(SUM(amount), 0) FROM expenses WHERE DATE(date) BETWEEN @monthStart AND @monthEnd AND deleted_at IS NULL",
            new { monthStart, monthEnd }) ?? 0m;

        var cashTotal = await Db.ExecuteScalarAsync<decimal?>(
            "SELECT COALESCE(SUM(amount), 0) FROM expenses WHERE DATE(date) BETWEEN @monthStart AND @monthEnd AND payment_method = 'cash' AND deleted_at IS NULL",
            new { monthStart, monthEnd }) ?? 0m;

        var bankTotal = await Db.ExecuteScalarAsync<decimal?>(
            "SELECT COALESCE(SUM(amount), 0) FROM expenses WHERE DATE(date) BETWEEN @monthStart AND @monthEnd AND payment_method != 'cash' AND deleted_at IS NULL",
            new { monthStart, monthEnd }) ?? 0m;

        var topCategory = await Db.QuerySingleOrDefaultAsync(
            @"SELECT ec.name, COALESCE(SUM(e.amount), 0) as total
              FROM expenses e
              INNER JOIN expense_categories ec ON ec.id = e.expense_category_id
              WHERE DATE(e.date) BETWEEN @monthStart AND @monthEnd AND e.deleted_at IS NULL
              GROUP BY ec.name
              ORDER BY total DESC
              LIMIT 1",
            new { monthStart, monthEnd });

        var activeRecurring = await Db.QueryListAsync(
            @"SELECT id, date, is_recurring, recurring_frequency
              FROM expenses
              WHERE is_recurring = 1 AND recurring_frequency IN ('weekly', 'monthly', 'yearly') AND deleted_at IS NULL");

        var todayDt = DateTime.UtcNow.Date;
        var dueRecurringCount = activeRecurring.Count(exp =>
        {
            var nextDt = ExpenseService.GetNextRecurringDate(exp);
            return nextDt.HasValue && nextDt.Value.Date <= todayDt;
        });

        return Ok(new Dictionary<string, object?>
        {
            ["today_total"] = Math.Round(todayTotal, 2),
            ["month_total"] = Math.Round(monthTotal, 2),
            ["cash_total"] = Math.Round(cashTotal, 2),
            ["bank_total"] = Math.Round(bankTotal, 2),
            ["top_category_name"] = topCategory?.GetValueOrDefault("name")?.ToString() ?? "None",
            ["top_category_sum"] = Math.Round(AccountService.ToDecimal(topCategory?.GetValueOrDefault("total")), 2),
            ["recurring_active_count"] = activeRecurring.Count,
            ["recurring_due_count"] = dueRecurringCount
        });
    }

    [HttpPost("process-recurring")]
    public async Task<IActionResult> ProcessRecurring()
    {
        if (!await CanAsync("expense.create", "expense.update")) return Forbidden();

        var result = await _expenseService.ProcessRecurringExpensesAsync(DateTime.UtcNow.Date, CurrentUserId);
        var count = (int)AccountService.ToLong(result.GetValueOrDefault("processed_count"));

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = count > 0
                ? $"Generated {count} due recurring expense(s)."
                : "No recurring expenses are currently due.",
            ["processed_count"] = count,
            ["created_expenses"] = result.GetValueOrDefault("created_expenses")
        });
    }

    [HttpGet("{expense:long}/receipt")]
    public async Task<IActionResult> Receipt(long expense)
    {
        if (!await CanAsync("expense.view")) return Forbidden();

        var row = await Db.QuerySingleOrDefaultAsync("SELECT * FROM expenses WHERE id = @id AND deleted_at IS NULL", new { id = expense });
        if (row == null) return NotFound(new { message = "Expense not found." });

        var relPath = (row.GetValueOrDefault("receipt_path")?.ToString() ?? "").TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(relPath) ||
            !relPath.StartsWith("expense-receipts/", StringComparison.OrdinalIgnoreCase) ||
            relPath.Contains(".."))
        {
            return NotFound(new { message = "Receipt attachment not found." });
        }

        var fullPath = Db.StoragePath($"app/public/{relPath}");
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound(new { message = "Receipt attachment not found." });
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fullPath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return PhysicalFile(fullPath, contentType);
    }

    private async Task<string> SaveReceiptFileAsync(Microsoft.AspNetCore.Http.IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var relPath = $"expense-receipts/{fileName}";
        var fullPath = Db.StoragePath($"app/public/{relPath}");
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var fs = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(fs);
        return relPath;
    }
}
