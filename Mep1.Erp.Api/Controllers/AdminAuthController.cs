using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mep1.Erp.Infrastructure;
using Mep1.Erp.Core;
using Mep1.Erp.Api.Security;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/admin/auth")]
public sealed class AdminAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ActorTokenService _tokens;

    public AdminAuthController(AppDbContext db, ActorTokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    public sealed record DesktopAdminLoginRequestDto(string Username, string Password);

    public sealed record DesktopAdminLoginResponseDto(
        int ActorWorkerId,
        string Username,
        string Role,
        string Name,
        string Initials,
        bool MustChangePassword,
        string ActorToken,
        DateTime ExpiresUtc
    );

    [HttpPost("login")]
    public async Task<ActionResult<DesktopAdminLoginResponseDto>> Login(DesktopAdminLoginRequestDto request)
    {
        // Must have ADMIN api key to even attempt desktop login
        if (!string.Equals(HttpContext.Items["ApiKeyKind"] as string, "Admin", StringComparison.Ordinal))
            return Unauthorized("Admin API key required.");

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized("Invalid login.");

        var username = request.Username.Trim();

        var user = await _db.TimesheetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.IsActive);

        if (user is null)
            return Unauthorized("Invalid login.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized("Invalid login.");

        // Desktop roles only
        if (user.Role != TimesheetUserRole.Admin && user.Role != TimesheetUserRole.Owner)
            return Unauthorized("Desktop access requires Admin or Owner.");

        var worker = await _db.Workers
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == user.WorkerId);

        if (worker is null)
            return Unauthorized("Invalid login.");

        var expiresUtc = DateTime.UtcNow.AddHours(12);
        var actorToken = _tokens.CreateToken(user.WorkerId, user.Role, user.Username, expiresUtc);

        return Ok(new DesktopAdminLoginResponseDto(
            ActorWorkerId: user.WorkerId,
            Username: user.Username,
            Role: user.Role.ToString(),
            Name: worker.Name,
            Initials: worker.Initials,
            MustChangePassword: user.MustChangePassword,
            ActorToken: actorToken,
            ExpiresUtc: expiresUtc
        ));
    }
}
