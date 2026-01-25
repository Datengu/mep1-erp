using Mep1.Erp.Api.Security;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly JwtTokenService _jwt;
    private readonly RefreshTokenService _refresh;

    public AuthController(AppDbContext db, IMemoryCache cache, JwtTokenService jwt, RefreshTokenService refresh)
    {
        _db = db;
        _cache = cache;
        _jwt = jwt;
        _refresh = refresh;
    }

    public sealed record LoginRequest(string Username, string Password, bool RememberMe);

    public sealed record LoginResponse(
        int WorkerId,
        string Username,
        string Role,
        string Name,
        string Initials,
        bool MustChangePassword,
        string AccessToken,
        DateTime ExpiresUtc,
        string? RefreshToken,
        DateTime? RefreshExpiresUtc
    );

    public sealed record LogoutRequest(string RefreshToken);

    private sealed class LoginThrottleState
    {
        public int FailCount { get; set; }
        public DateTimeOffset WindowStart { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }

    private const int MaxFailsPerWindow = 5;
    private static readonly TimeSpan FailWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(1);

    public sealed record RefreshRequest(string RefreshToken);

    public sealed record RefreshResponse(
        string AccessToken,
        DateTime ExpiresUtc,
        string RefreshToken,
        DateTime RefreshExpiresUtc
    );

    private static string NormalizeUsername(string? username)
        => (username ?? "").Trim().ToLowerInvariant();

    private string CacheKeyFor(string normalizedUsername)
        => $"login_throttle::{normalizedUsername}";

    private bool TryIsLocked(string normalizedUsername, out TimeSpan? retryAfter)
    {
        retryAfter = null;

        if (string.IsNullOrWhiteSpace(normalizedUsername))
            return false;

        if (!_cache.TryGetValue(CacheKeyFor(normalizedUsername), out LoginThrottleState? state) || state is null)
            return false;

        if (state.LockedUntil is null)
            return false;

        var now = DateTimeOffset.UtcNow;
        if (now >= state.LockedUntil.Value)
            return false;

        retryAfter = state.LockedUntil.Value - now;
        return true;
    }

    private void RegisterFailedLogin(string normalizedUsername)
    {
        if (string.IsNullOrWhiteSpace(normalizedUsername))
            return;

        var key = CacheKeyFor(normalizedUsername);
        var now = DateTimeOffset.UtcNow;

        var state = _cache.Get<LoginThrottleState>(key) ?? new LoginThrottleState
        {
            FailCount = 0,
            WindowStart = now,
            LockedUntil = null
        };

        if (state.LockedUntil is not null && now >= state.LockedUntil.Value)
        {
            state.LockedUntil = null;
            state.FailCount = 0;
            state.WindowStart = now;
        }

        if (now - state.WindowStart > FailWindow)
        {
            state.WindowStart = now;
            state.FailCount = 0;
        }

        state.FailCount++;

        if (state.FailCount >= MaxFailsPerWindow)
        {
            state.LockedUntil = now.Add(LockoutDuration);
            state.FailCount = 0;
            state.WindowStart = now;
        }

        var absoluteExpire = (state.LockedUntil ?? state.WindowStart.Add(FailWindow)).AddMinutes(30);

        _cache.Set(key, state, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = absoluteExpire
        });
    }

    private void ClearLoginThrottle(string normalizedUsername)
    {
        if (string.IsNullOrWhiteSpace(normalizedUsername))
            return;

        _cache.Remove(CacheKeyFor(normalizedUsername));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var normalizedUsername = NormalizeUsername(request.Username);

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized("Invalid login.");

        if (TryIsLocked(normalizedUsername, out var retryAfter) && retryAfter is not null)
        {
            Response.Headers["Retry-After"] = Math.Ceiling(retryAfter.Value.TotalSeconds).ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts. Try again later.");
        }

        var user = await _db.TimesheetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UsernameNormalized == normalizedUsername && x.IsActive);

        if (user is null)
        {
            RegisterFailedLogin(normalizedUsername);
            return Unauthorized("Invalid login.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            RegisterFailedLogin(normalizedUsername);
            return Unauthorized("Invalid login.");
        }

        ClearLoginThrottle(normalizedUsername);

        var worker = await _db.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == user.WorkerId);

        if (worker is null)
            return Unauthorized("Invalid login.");

        var (token, expiresUtc) = _jwt.CreateToken(user.WorkerId, user.Role, user.Username);

        string? refreshToken = null;
        DateTime? refreshExpires = null;

        if (request.RememberMe)
        {
            var issued = await _refresh.IssueAsync(user.Id);
            refreshToken = issued.refreshToken;
            refreshExpires = issued.expiresUtc;
        }

        return Ok(new LoginResponse(
            user.WorkerId,
            user.Username,
            user.Role.ToString(),
            worker.Name,
            worker.Initials,
            user.MustChangePassword,
            token,
            expiresUtc,
            refreshToken,
            refreshExpires
        ));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequestDto request)
    {
        var normalizedUsername = NormalizeUsername(request.Username);

        if (string.IsNullOrWhiteSpace(normalizedUsername) ||
            string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest("Invalid request.");

        if (request.NewPassword.Length < 8)
            return BadRequest("Password must be at least 8 characters.");

        var user = await _db.TimesheetUsers
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername && u.IsActive);

        if (user is null)
            return Unauthorized("Invalid login.");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return Unauthorized("Invalid login.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshResponse>> Refresh([FromBody] RefreshRequest req)
    {
        var validated = await _refresh.ValidateAsync(req.RefreshToken);
        if (validated == null)
            return Unauthorized("Invalid refresh token.");

        var (user, oldRow) = validated.Value;

        if (!user.IsActive)
            return Unauthorized("User inactive.");

        if (user.MustChangePassword)
            return Unauthorized("Password change required.");

        // rotate refresh token
        var rotated = await _refresh.RotateAsync(oldRow);

        // issue new access token (short)
        var (access, expiresUtc) = _jwt.CreateToken(user.WorkerId, user.Role, user.Username);

        return Ok(new RefreshResponse(
            access,
            expiresUtc,
            rotated.newRefreshToken,
            rotated.newRefreshExpiresUtc
        ));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req)
    {
        await _refresh.RevokeAsync(req.RefreshToken);
        return Ok();
    }
}
