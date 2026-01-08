using DocumentFormat.OpenXml.Math;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/timesheet/auth")]
public sealed class TimesheetAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public TimesheetAuthController(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public sealed record LoginRequest(string Username, string Password);
    public sealed record LoginResponse(
        int WorkerId,
        string Username,
        string Role,
        string Name,
        string Initials,
        bool MustChangePassword
    );

    private sealed class LoginThrottleState
    {
        public int FailCount { get; set; }
        public DateTimeOffset WindowStart { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }

    private const int MaxFailsPerWindow = 5;
    private static readonly TimeSpan FailWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(1);

    private static string NormalizeUsername(string? username)
        => (username ?? "").Trim().ToLowerInvariant();

    private string CacheKeyFor(string normalizedUsername)
        => $"ts_login_throttle::{normalizedUsername}";

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

        // If lockout expired, clear it and start fresh
        if (state.LockedUntil is not null && now >= state.LockedUntil.Value)
        {
            state.LockedUntil = null;
            state.FailCount = 0;
            state.WindowStart = now;
        }

        // If window expired, reset window + count
        if (now - state.WindowStart > FailWindow)
        {
            state.WindowStart = now;
            state.FailCount = 0;
        }

        state.FailCount++;

        // Lock if too many failures
        if (state.FailCount >= MaxFailsPerWindow)
        {
            state.LockedUntil = now.Add(LockoutDuration);
            state.FailCount = 0;
            state.WindowStart = now;
        }

        // Keep the cache entry around a bit longer than the lockout/window
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

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var normalizedUsername = NormalizeUsername(request.Username);

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized("Invalid login.");

        // Throttle / lockout (username-only)
        if (TryIsLocked(normalizedUsername, out var retryAfter) && retryAfter is not null)
        {
            // 429 is a decent “slow down” signal
            Response.Headers["Retry-After"] = Math.Ceiling(retryAfter.Value.TotalSeconds).ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts. Try again later.");
        }

        var user = await _db.TimesheetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username.ToLower() == normalizedUsername && x.IsActive);

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

        // Success: clear throttle state
        ClearLoginThrottle(normalizedUsername);

        var worker = await _db.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == user.WorkerId);

        if (worker is null)
            return Unauthorized("Invalid login."); // data integrity issue

        return Ok(new LoginResponse(
            user.WorkerId,
            user.Username,
            user.Role.ToString(),
            worker.Name,
            worker.Initials,
            user.MustChangePassword
        ));
    }


    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequestDto request)
    {
        var username = request.Username.Trim();
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest("Invalid request.");

        if (request.NewPassword.Length < 8)
            return BadRequest("Password must be at least 8 characters.");

        var user = await _db.TimesheetUsers
            .FirstOrDefaultAsync(u =>
                u.Username == request.Username &&
                u.IsActive);

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
}
