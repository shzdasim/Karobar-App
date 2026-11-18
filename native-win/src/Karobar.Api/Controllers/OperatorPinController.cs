using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/operator-pins")]
[Authorize]
public class OperatorPinController : ApiControllerBase
{
    private readonly OperatorPinService _pinService;

    public OperatorPinController(KarobarDb db, OperatorPinService pinService) : base(db)
    {
        _pinService = pinService;
    }

    [HttpGet("directory")]
    public async Task<IActionResult> Directory()
    {
        if (!await CanAsync("settings.view", "settings.update", "user.view", "user.manage", "hr.view", "hr.manage"))
        {
            return Forbidden("Unauthorized to view operator PIN directory.");
        }

        var hasUserPin = await Db.HasTableAsync("users") && await Db.HasColumnAsync("users", "pin_code");
        var userRows = await Db.QueryListAsync("SELECT * FROM users ORDER BY name ASC");
        var users = new List<Dictionary<string, object?>>();

        foreach (var u in userRows)
        {
            var uid = Convert.ToInt64(u["id"]);
            var roleNames = await Db.GetUserRoleNamesAsync(uid);
            users.Add(new Dictionary<string, object?>
            {
                ["id"] = uid,
                ["name"] = u.GetValueOrDefault("name"),
                ["email"] = u.GetValueOrDefault("email"),
                ["status"] = u.GetValueOrDefault("status") ?? "active",
                ["roles"] = roleNames,
                ["pin_code"] = hasUserPin ? u.GetValueOrDefault("pin_code") : null
            });
        }

        var employees = new List<Dictionary<string, object?>>();
        if (await Db.HasTableAsync("employees"))
        {
            var hasEmpPin = await Db.HasColumnAsync("employees", "pin_code");
            var empRows = await Db.QueryListAsync(@"
                SELECT e.*, u.name as linked_user_name
                FROM employees e
                LEFT JOIN users u ON u.id = e.user_id
                ORDER BY e.name ASC");

            foreach (var e in empRows)
            {
                employees.Add(new Dictionary<string, object?>
                {
                    ["id"] = e.GetValueOrDefault("id"),
                    ["employee_code"] = e.GetValueOrDefault("employee_code"),
                    ["user_id"] = e.GetValueOrDefault("user_id"),
                    ["user_name"] = e.GetValueOrDefault("linked_user_name"),
                    ["name"] = e.GetValueOrDefault("name"),
                    ["department"] = e.GetValueOrDefault("department"),
                    ["designation"] = e.GetValueOrDefault("designation"),
                    ["status"] = e.GetValueOrDefault("status") ?? "active",
                    ["pin_code"] = hasEmpPin ? e.GetValueOrDefault("pin_code") : null
                });
            }
        }

        return Ok(new
        {
            users,
            employees
        });
    }

    [HttpGet("generate")]
    public async Task<IActionResult> Generate()
    {
        if (!await CanAsync("settings.update", "user.manage", "user.update", "hr.manage"))
        {
            return Forbidden("Unauthorized to manage operator PINs.");
        }

        try
        {
            var pin = await _pinService.GenerateUniquePinAsync();
            return Ok(new { pin_code = pin });
        }
        catch (OperatorPinException ex)
        {
            return StatusCode(ex.StatusCode, ex.ResponsePayload);
        }
    }

    [HttpPut("assign")]
    public async Task<IActionResult> Assign()
    {
        if (!await CanAsync("settings.update", "user.manage", "user.update", "hr.manage"))
        {
            return Forbidden("Unauthorized to manage operator PINs.");
        }

        var body = await ReadJsonBodyAsync();
        var type = (GetString(body, "type") ?? "").Trim().ToLowerInvariant();
        var id = GetNullableLong(body, "id");
        var rawPin = GetString(body, "pin_code");

        if (id == null || type is not ("user" or "employee"))
        {
            return Unprocessable("Invalid operator PIN assignment parameters.");
        }

        try
        {
            if (type == "user")
            {
                var user = await Db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id = id.Value });
                if (user == null) return NotFound(new { message = "User not found" });

                long? linkedEmpId = null;
                if (await Db.HasTableAsync("employees"))
                {
                    linkedEmpId = await Db.ExecuteScalarAsync<long?>("SELECT id FROM employees WHERE user_id = @id", new { id = id.Value });
                }

                var pin = await _pinService.ValidateUniquePinAsync(rawPin, id.Value, null, null, linkedEmpId);
                await Db.UpdateAsync("users", id.Value, new Dictionary<string, object?> { ["pin_code"] = pin });
                await _pinService.SyncLinkedPinForUserAsync(id.Value, pin);

                var uName = user.GetValueOrDefault("name")?.ToString() ?? "User";
                return Ok(new
                {
                    message = pin != null ? $"4-digit code assigned to {uName}." : $"4-digit code cleared for {uName}.",
                    pin_code = pin
                });
            }
            else
            {
                var emp = await Db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE id = @id", new { id = id.Value });
                if (emp == null) return NotFound(new { message = "Employee not found" });

                long? linkedUserId = null;
                if (emp.TryGetValue("user_id", out var uidObj) && uidObj != null && long.TryParse(uidObj.ToString(), out var parsedUid))
                {
                    linkedUserId = parsedUid;
                }

                var pin = await _pinService.ValidateUniquePinAsync(rawPin, null, id.Value, linkedUserId, null);
                await Db.UpdateAsync("employees", id.Value, new Dictionary<string, object?> { ["pin_code"] = pin });
                await _pinService.SyncLinkedPinForEmployeeAsync(linkedUserId, pin);

                var eName = emp.GetValueOrDefault("name")?.ToString() ?? "Employee";
                return Ok(new
                {
                    message = pin != null ? $"4-digit code assigned to {eName}." : $"4-digit code cleared for {eName}.",
                    pin_code = pin
                });
            }
        }
        catch (OperatorPinException ex)
        {
            return StatusCode(ex.StatusCode, ex.ResponsePayload);
        }
    }
}
