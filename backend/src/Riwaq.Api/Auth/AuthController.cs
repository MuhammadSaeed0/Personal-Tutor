using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Riwaq.Api.Infrastructure;
using Riwaq.Application.Common;
using ValidationException = Riwaq.Application.Common.ValidationException;
using Riwaq.Application.Features.Profile;
using Riwaq.Domain.Entities;
using Riwaq.Infrastructure.Persistence;

namespace Riwaq.Api.Auth;

public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MaxLength(100)] string DisplayName,
    string? UiLanguage);

public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public sealed record ForgotPasswordRequest([Required, EmailAddress] string Email);

public sealed record ResetPasswordRequest([Required, EmailAddress] string Email, [Required] string Token, [Required, MinLength(8), MaxLength(128)] string NewPassword);

public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required, MinLength(8), MaxLength(128)] string NewPassword);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, ProfileDto Profile);

public sealed record SessionDto(Guid Id, DateTime CreatedAt, DateTime LastUsedAt, string? UserAgent, bool IsCurrent);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(
    UserManager<AppUser> users,
    TokenService tokens,
    ProfileService profiles,
    AppDbContext db,
    IEmailSender emailSender,
    IHostEnvironment env,
    TimeProvider clock) : ControllerBase
{
    public const string RefreshCookie = "riwaq_rt";

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var user = new AppUser
        {
            Id = Ids.New(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = now
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var duplicate = result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            if (duplicate) throw new ConflictException("email_taken", "An account with this email already exists.");
            throw new ValidationException("weak_password", "The password does not meet the requirements.",
                new Dictionary<string, string[]> { ["password"] = result.Errors.Select(e => e.Code).ToArray() });
        }

        db.UserPreferences.Add(new UserPreferences { UserId = user.Id, UiLanguage = request.UiLanguage == "en" ? "en" : "ar", ExplanationLanguage = request.UiLanguage == "en" ? "en" : "ar" });
        db.AcademicProfiles.Add(new AcademicProfile { UserId = user.Id });
        await db.SaveChangesAsync(ct);

        return await SignInAsync(user, ct);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) throw new InvalidCredentialsException();
        if (await users.IsLockedOutAsync(user)) throw new ForbiddenException("account_locked", "Too many failed attempts. Try again later.");

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new InvalidCredentialsException();
        }
        await users.ResetAccessFailedCountAsync(user);
        return await SignInAsync(user, ct);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [RequireClientHeader]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var refresh) || string.IsNullOrEmpty(refresh))
            return Unauthorized();
        var rotated = await tokens.RotateAsync(refresh, Request.Headers.UserAgent.ToString(), ct);
        if (rotated is null)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UnixEpoch));
            return Unauthorized();
        }
        var (user, issued) = rotated.Value;
        SetRefreshCookie(issued);
        return new AuthResponse(issued.AccessToken, issued.AccessTokenExpiresAt, await profiles.GetAsync(user.Id, ct));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [RequireClientHeader]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookie, out var refresh) && !string.IsNullOrEmpty(refresh))
            await tokens.RevokeAsync(refresh, ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UnixEpoch));
        return NoContent();
    }

    /// <summary>Always returns 202 so the endpoint cannot be used to discover registered emails.</summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await emailSender.SendPasswordResetAsync(user.Email!, token);
        }
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) throw new ValidationException("invalid_reset_token", "The reset link is invalid or expired.");
        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var invalidToken = result.Errors.Any(e => e.Code == "InvalidToken");
            throw new ValidationException(invalidToken ? "invalid_reset_token" : "weak_password", "Password could not be reset.");
        }
        // A password reset signs out every existing session.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow().UtcDateTime), ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await users.FindByIdAsync(User.GetUserId().ToString()) ?? throw new NotFoundException("User");
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var mismatch = result.Errors.Any(e => e.Code == "PasswordMismatch");
            throw new ValidationException(mismatch ? "wrong_password" : "weak_password", "Password could not be changed.");
        }
        return NoContent();
    }

    [Authorize]
    [HttpGet("sessions")]
    [DisableRateLimiting]
    public async Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var now = clock.GetUtcNow().UtcDateTime;
        Request.Cookies.TryGetValue(RefreshCookie, out var current);
        var currentHash = current is null ? null : TokenService.Hash(current);
        var sessions = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.LastUsedAt).ToListAsync(ct);
        return sessions.Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastUsedAt, s.UserAgent, s.TokenHash == currentHash)).ToList();
    }

    [Authorize]
    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var updated = await db.RefreshTokens.Where(t => t.Id == id && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow().UtcDateTime), ct);
        return updated == 0 ? NotFound() : NoContent();
    }

    private async Task<AuthResponse> SignInAsync(AppUser user, CancellationToken ct)
    {
        var issued = await tokens.IssueAsync(user, Request.Headers.UserAgent.ToString(), ct);
        SetRefreshCookie(issued);
        return new AuthResponse(issued.AccessToken, issued.AccessTokenExpiresAt, await profiles.GetAsync(user.Id, ct));
    }

    private void SetRefreshCookie(IssuedTokens issued) =>
        Response.Cookies.Append(RefreshCookie, issued.RefreshToken, CookieOptions(issued.RefreshTokenExpiresAt));

    private CookieOptions CookieOptions(DateTime expires) => new()
    {
        HttpOnly = true,
        Secure = !env.IsEnvironment("Testing"),
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
        IsEssential = true
    };
}

public sealed class InvalidCredentialsException() : AppException("invalid_credentials", "Email or password is incorrect.")
{
    public override int StatusCode => 401;
}
