using DocumentFormat.OpenXml.Math;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/timesheet/auth")]
public sealed class TimesheetAuthController : ControllerBase
{
    private readonly AppDbContext _db;

    public TimesheetAuthController(AppDbContext db)
    {
        _db = db;
    }

    public sealed record LoginRequest(string Username, string Password);
    public sealed record LoginResponse(
        int WorkerId,
        string Username,
        string Role,
        string Name,
        string Initials
    );

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await _db.TimesheetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == request.Username && x.IsActive);

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized("Invalid login.");

        if (user is null)
            return Unauthorized("Invalid login.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized("Invalid login.");

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
            worker.Initials
        ));
    }
}
