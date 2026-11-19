using System.Text.Json;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api")]
public class AuthController : ApiControllerBase
{
    private const int RememberMeExpiryMinutes = 1440;
    private const int DefaultExpiryMinutes = 20;

    private readonly LicenseService _licenseService;

    public AuthController(KarobarDb db, LicenseService licenseService) : base(db)
    {
        _licenseService = licenseService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login()
    {
        var body = await ReadJsonBodyAsync();
        var email = (GetString(body, "email") ?? "").Trim();
        var password = GetString(body, "password") ?? "";
        var remember = GetBool(body, "remember", false);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return Unprocessable("The email and password fields are required.", new Dictionary<string, string[]>
            {
                ["email"] = new[] { "The email field is required." },
                ["password"] = new[] { "The password field is required." }
            });
        }

        var user = await Db.QuerySingleOrDefaultAsync(
            "SELECT * FROM users WHERE LOWER(email) = LOWER(@email) LIMIT 1",
            new { email });

        if (user == null)
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var storedHash = user.GetValueOrDefault("password")?.ToString();
        if (!SanctumAuthenticationHandler.VerifyLaravelBcryptPassword(password, storedHash))
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var userId = Convert.ToInt64(user["id"]);
        var status = user.GetValueOrDefault("status")?.ToString() ?? "active";
        if (!status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            if (await Db.HasTableAsync("personal_access_tokens"))
            {
                await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id = @userId", new { userId });
            }
            return Forbidden("Your account has been deactivated. Please contact an administrator.");
        }

        var licenseCleared = await _licenseService.ClearLicenseIfMachineChangedAsync();

        var expiryMinutes = remember ? RememberMeExpiryMinutes : DefaultExpiryMinutes;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        if (await Db.HasTableAsync("personal_access_tokens"))
        {
            await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id = @userId", new { userId });
        }

        var token = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(Db, userId, "api_token", expiresAt);

        if (await Db.HasColumnAsync("users", "remember_token_expires_at"))
        {
            await Db.UpdateAsync("users", userId, new Dictionary<string, object?>
            {
                ["remember_token_expires_at"] = expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        bool isSetupCompleted = true;
        try
        {
            if (await Db.HasTableAsync("settings"))
            {
                var setting = await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
                if (await Db.HasColumnAsync("settings", "is_setup_completed"))
                {
                    isSetupCompleted = setting != null && setting.GetValueOrDefault("is_setup_completed") is true or 1 or 1L;
                }
                else
                {
                    var storeName = setting?.GetValueOrDefault("store_name")?.ToString() ?? "";
                    isSetupCompleted = !string.IsNullOrWhiteSpace(storeName) && !storeName.Equals("Karobar App", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            isSetupCompleted = true;
        }

        var userWithRoles = await Db.GetUserWithRolesAndPermissionsAsync(userId);
        var response = new Dictionary<string, object?>
        {
            ["token"] = token,
            ["user"] = userWithRoles,
            ["expires_at"] = expiresAt.ToString("O"),
            ["remember_me"] = remember,
            ["is_setup_completed"] = isSetupCompleted
        };

        if (licenseCleared)
        {
            response["license_revoked"] = true;
            response["message"] = "License was revoked because software was moved to a new device. Please activate a new license.";
        }

        return Ok(response);
    }

    [HttpGet("user")]
    [Authorize]
    public async Task<IActionResult> GetUser()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var user = await Db.GetUserWithRolesAndPermissionsAsync(userId.Value);
        if (user == null) return Unauthorized();

        return Ok(user);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var user = await Db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id = userId.Value });
        if (user == null) return Unauthorized();

        var body = await ReadJsonBodyAsync();
        var currentName = user.GetValueOrDefault("name")?.ToString() ?? "";
        var currentEmail = user.GetValueOrDefault("email")?.ToString() ?? "";
        var storedPassword = user.GetValueOrDefault("password")?.ToString() ?? "";

        var newName = (GetString(body, "name") ?? currentName).Trim();
        var newEmail = (GetString(body, "email") ?? currentEmail).Trim();
        var currentPassword = GetString(body, "current_password");
        var newPassword = GetString(body, "password");
        var passwordConfirmation = GetString(body, "password_confirmation");

        var changingPassword = !string.IsNullOrEmpty(newPassword);
        var changingEmail = !string.Equals(newEmail, currentEmail, StringComparison.OrdinalIgnoreCase);

        if (changingEmail)
        {
            var emailConflict = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM users WHERE LOWER(email) = LOWER(@email) AND id != @id",
                new { email = newEmail, id = userId.Value });
            if (emailConflict > 0)
            {
                return Unprocessable("The email has already been taken.", new Dictionary<string, string[]>
                {
                    ["email"] = new[] { "The email has already been taken." }
                });
            }
        }

        if (changingPassword && !string.Equals(newPassword, passwordConfirmation, StringComparison.Ordinal))
        {
            return Unprocessable("The password field confirmation does not match.", new Dictionary<string, string[]>
            {
                ["password"] = new[] { "The password field confirmation does not match." }
            });
        }

        if (changingPassword || changingEmail)
        {
            if (string.IsNullOrEmpty(currentPassword))
            {
                return Unprocessable("Current password is required to change your email or password.", new Dictionary<string, string[]>
                {
                    ["current_password"] = new[] { "Current password is required to change your email or password." }
                });
            }

            if (!SanctumAuthenticationHandler.VerifyLaravelBcryptPassword(currentPassword, storedPassword))
            {
                return Unprocessable("The provided current password is incorrect.", new Dictionary<string, string[]>
                {
                    ["current_password"] = new[] { "The provided current password is incorrect." }
                });
            }
        }

        var updates = new Dictionary<string, object?>
        {
            ["name"] = newName,
            ["email"] = newEmail
        };

        if (changingPassword)
        {
            updates["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword(newPassword!);
        }

        await Db.UpdateAsync("users", userId.Value, updates);

        if (changingPassword && CurrentTokenId.HasValue)
        {
            await Db.ExecuteAsync(
                "DELETE FROM personal_access_tokens WHERE tokenable_id = @userId AND id != @tokenId",
                new { userId = userId.Value, tokenId = CurrentTokenId.Value });
        }

        var fresh = await Db.GetUserWithRolesAndPermissionsAsync(userId.Value);
        return Ok(fresh);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        if (CurrentTokenId.HasValue)
        {
            await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE id = @id", new { id = CurrentTokenId.Value });
        }
        return Ok(new { message = "Logged out" });
    }

    [HttpPost("session/extend")]
    [Authorize]
    public async Task<IActionResult> ExtendSession()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new { ok = false, message = "Unable to extend session. Please log in again." });
        }

        var body = await ReadJsonBodyAsync();
        var minutes = Math.Clamp(GetInt(body, "minutes", 10), 1, 60);

        var user = await Db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id = userId.Value });
        if (user == null)
        {
            return Unauthorized(new { ok = false, message = "Unable to extend session. Please log in again." });
        }

        var status = user.GetValueOrDefault("status")?.ToString() ?? "active";
        if (!status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id = @userId", new { userId = userId.Value });
            return StatusCode(403, new { ok = false, message = "Your account has been deactivated." });
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var expStr = expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        if (CurrentTokenId.HasValue)
        {
            await Db.UpdateAsync("personal_access_tokens", CurrentTokenId.Value, new Dictionary<string, object?>
            {
                ["created_at"] = nowStr,
                ["expires_at"] = expStr
            });
        }

        if (await Db.HasColumnAsync("users", "remember_token_expires_at"))
        {
            await Db.UpdateAsync("users", userId.Value, new Dictionary<string, object?>
            {
                ["remember_token_expires_at"] = expStr
            });
        }

        return Ok(new
        {
            ok = true,
            expires_at = expiresAt.ToString("O"),
            wait_minutes = minutes
        });
    }

    [HttpPost("verify-password")]
    [HttpPost("auth/confirm-password")]
    [Authorize]
    public async Task<IActionResult> ConfirmPassword()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var body = await ReadJsonBodyAsync();
        var password = GetString(body, "password") ?? "";
        if (string.IsNullOrEmpty(password))
        {
            return Unprocessable("The password field is required.", new Dictionary<string, string[]>
            {
                ["password"] = new[] { "The password field is required." }
            });
        }

        var user = await Db.QuerySingleOrDefaultAsync("SELECT password FROM users WHERE id = @id", new { id = userId.Value });
        var storedHash = user?.GetValueOrDefault("password")?.ToString();

        if (!SanctumAuthenticationHandler.VerifyLaravelBcryptPassword(password, storedHash))
        {
            return StatusCode(422, new { message = "Invalid password" });
        }

        return Ok(new { ok = true });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword()
    {
        var body = await ReadJsonBodyAsync();
        var email = (GetString(body, "email") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return Unprocessable("The email field is required.", new Dictionary<string, string[]>
            {
                ["email"] = new[] { "The email field is required." }
            });
        }

        var user = await Db.QuerySingleOrDefaultAsync("SELECT id, email FROM users WHERE LOWER(email) = LOWER(@email)", new { email });
        if (user != null && await Db.HasTableAsync("password_reset_tokens"))
        {
            var canonicalEmail = user.GetValueOrDefault("email")?.ToString() ?? email;
            var rawToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var tokenHash = SanctumAuthenticationHandler.ComputeSha256Hex(rawToken);
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await Db.ExecuteAsync("DELETE FROM password_reset_tokens WHERE LOWER(email) = LOWER(@email)", new { email = canonicalEmail });
            await Db.ExecuteAsync(
                "INSERT INTO password_reset_tokens (email, token, created_at) VALUES (@email, @token, @createdAt)",
                new { email = canonicalEmail, token = tokenHash, createdAt = nowStr });
        }

        return Ok(new
        {
            message = "Password reset link sent to your email.",
            status = "We have emailed your password reset link."
        });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword()
    {
        var body = await ReadJsonBodyAsync();
        var token = (GetString(body, "token") ?? "").Trim();
        var email = (GetString(body, "email") ?? "").Trim();
        var password = GetString(body, "password") ?? "";
        var confirmation = GetString(body, "password_confirmation") ?? "";

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email) || password.Length < 8 || password != confirmation)
        {
            return Unprocessable("Unable to reset password.", new Dictionary<string, string[]>
            {
                ["email"] = new[] { "Invalid password reset parameters." }
            });
        }

        var user = await Db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE LOWER(email) = LOWER(@email)", new { email });
        if (user == null)
        {
            return Unprocessable("Unable to reset password.", new Dictionary<string, string[]>
            {
                ["email"] = new[] { "We can't find a user with that email address." }
            });
        }

        if (!await Db.HasTableAsync("password_reset_tokens"))
        {
            return Unprocessable("This password reset token is invalid.", new Dictionary<string, string[]>
            {
                ["token"] = new[] { "This password reset token is invalid." }
            });
        }

        var resetRow = await Db.QuerySingleOrDefaultAsync(
            "SELECT * FROM password_reset_tokens WHERE LOWER(email) = LOWER(@email) ORDER BY created_at DESC LIMIT 1",
            new { email });
        if (resetRow == null)
        {
            return Unprocessable("This password reset token is invalid.", new Dictionary<string, string[]>
            {
                ["token"] = new[] { "This password reset token is invalid." }
            });
        }

        var storedToken = resetRow.GetValueOrDefault("token")?.ToString() ?? "";
        var tokenHash = SanctumAuthenticationHandler.ComputeSha256Hex(token);
        var tokenMatches =
            (!string.IsNullOrEmpty(storedToken) &&
             System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                 System.Text.Encoding.UTF8.GetBytes(tokenHash.ToLowerInvariant()),
                 System.Text.Encoding.UTF8.GetBytes(storedToken.ToLowerInvariant()))) ||
            (!string.IsNullOrEmpty(storedToken) &&
             storedToken.StartsWith("$2", StringComparison.Ordinal) &&
             SanctumAuthenticationHandler.VerifyLaravelBcryptPassword(token, storedToken));

        var isExpired = false;
        if (resetRow.TryGetValue("created_at", out var cObj) && cObj != null &&
            DateTimeOffset.TryParse(
                cObj.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var createdDt))
        {
            isExpired = DateTimeOffset.UtcNow - createdDt > TimeSpan.FromMinutes(60);
        }

        if (!tokenMatches || isExpired)
        {
            return Unprocessable("This password reset token is invalid or has expired.", new Dictionary<string, string[]>
            {
                ["token"] = new[] { "This password reset token is invalid or has expired." }
            });
        }

        var userId = Convert.ToInt64(user["id"]);
        await Db.UpdateAsync("users", userId, new Dictionary<string, object?>
        {
            ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword(password)
        });
        await Db.ExecuteAsync("DELETE FROM password_reset_tokens WHERE LOWER(email) = LOWER(@email)", new { email });
        await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id = @userId", new { userId });

        return Ok(new
        {
            message = "Password has been reset successfully.",
            status = "Your password has been reset."
        });
    }
}
