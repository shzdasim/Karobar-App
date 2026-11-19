using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/expense-categories")]
[Authorize]
public class ExpenseCategoryController : ApiControllerBase
{
    public ExpenseCategoryController(KarobarDb db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await CanAsync("expense-category.view", "expense-category.manage", "expense.view")) return Forbidden();

        var activeOnly = string.Equals(Request.Query["active_only"], "1", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Request.Query["active_only"], "true", StringComparison.OrdinalIgnoreCase);

        var sql = $@"
            SELECT ec.*,
                   (SELECT COUNT(1) FROM expenses e WHERE e.expense_category_id = ec.id AND e.deleted_at IS NULL) as expenses_count
            FROM expense_categories ec
            {(activeOnly ? "WHERE ec.is_active = 1" : "")}
            ORDER BY ec.name ASC";

        var categories = await Db.QueryListAsync(sql);
        return Ok(categories);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("expense-category.manage", "expense.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim();
        var code = GetString(body, "code")?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return Unprocessable("The name field is required.", new() { ["name"] = new[] { "The name field is required." } });

        if (!string.IsNullOrWhiteSpace(code))
        {
            var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM expense_categories WHERE code = @code", new { code }) > 0;
            if (exists)
                return Unprocessable("The code has already been taken.", new() { ["code"] = new[] { "The code has already been taken." } });
        }

        var id = await Db.InsertAsync("expense_categories", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["code"] = string.IsNullOrWhiteSpace(code) ? null : code,
            ["description"] = GetString(body, "description"),
            ["is_active"] = body.ContainsKey("is_active") ? GetBool(body, "is_active", true) : true
        });

        var category = await Db.QuerySingleOrDefaultAsync("SELECT * FROM expense_categories WHERE id = @id", new { id });
        return StatusCode(201, category);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("expense-category.view", "expense-category.manage", "expense.view")) return Forbidden();

        var category = await Db.QuerySingleOrDefaultAsync(
            @"SELECT ec.*,
                     (SELECT COUNT(1) FROM expenses e WHERE e.expense_category_id = ec.id AND e.deleted_at IS NULL) as expenses_count
              FROM expense_categories ec
              WHERE ec.id = @id",
            new { id });

        if (category == null) return NotFound(new { message = "Expense category not found." });
        return Ok(category);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("expense-category.manage", "expense.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM expense_categories WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Expense category not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim();
        var code = GetString(body, "code")?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return Unprocessable("The name field is required.", new() { ["name"] = new[] { "The name field is required." } });

        if (!string.IsNullOrWhiteSpace(code))
        {
            var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM expense_categories WHERE code = @code AND id != @id", new { code, id }) > 0;
            if (exists)
                return Unprocessable("The code has already been taken.", new() { ["code"] = new[] { "The code has already been taken." } });
        }

        await Db.UpdateAsync("expense_categories", id, new Dictionary<string, object?>
        {
            ["name"] = name,
            ["code"] = string.IsNullOrWhiteSpace(code) ? null : code,
            ["description"] = body.ContainsKey("description") ? GetString(body, "description") : existing.GetValueOrDefault("description"),
            ["is_active"] = body.ContainsKey("is_active") ? GetBool(body, "is_active") : AccountService.IsTruthy(existing.GetValueOrDefault("is_active"))
        });

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM expense_categories WHERE id = @id", new { id });
        return Ok(updated);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("expense-category.manage", "expense.delete")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM expense_categories WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Expense category not found." });

        var hasExpenses = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM expenses WHERE expense_category_id = @id AND deleted_at IS NULL",
            new { id }) > 0;

        if (hasExpenses)
        {
            return Unprocessable("Cannot delete category that contains recorded expenses. Deactivate it instead.");
        }

        await Db.ExecuteAsync("DELETE FROM expense_categories WHERE id = @id", new { id });
        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Expense category deleted successfully."
        });
    }
}
