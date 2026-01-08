using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/people")]
public sealed class PeopleController : ControllerBase
{
    private readonly AppDbContext _db;

    public PeopleController(AppDbContext db)
    {
        _db = db;
    }

    private bool IsAdminKey()
        => string.Equals(HttpContext.Items["ApiKeyKind"] as string, "Admin", StringComparison.Ordinal);

    private ActionResult? RequireAdminKey()
    {
        if (IsAdminKey()) return null;
        return Unauthorized("Admin API key required.");
    }

    private static string GenerateTempPassword(int length = 12)
    {
        // simple, safe, readable; avoids ambiguous chars
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private static bool TryParseRole(string role, out TimesheetUserRole parsed)
        => Enum.TryParse<TimesheetUserRole>(role, ignoreCase: true, out parsed);

    // ----------------------------
    // Portal Access (admin only)
    // ----------------------------

    [HttpGet("{workerId:int}/portal-access")]
    public async Task<ActionResult<PortalAccessDto>> GetPortalAccess(int workerId)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

        var user = await _db.TimesheetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.WorkerId == workerId);

        if (user is null)
        {
            return Ok(new PortalAccessDto(
                Exists: false,
                WorkerId: workerId,
                Username: null,
                Role: null,
                IsActive: false,
                MustChangePassword: false,
                PasswordChangedAtUtc: null
            ));
        }

        return Ok(new PortalAccessDto(
            Exists: true,
            WorkerId: user.WorkerId,
            Username: user.Username,
            Role: user.Role.ToString(),
            IsActive: user.IsActive,
            MustChangePassword: user.MustChangePassword,
            PasswordChangedAtUtc: user.PasswordChangedAtUtc
        ));
    }

    [HttpPost("{workerId:int}/portal-access")]
    public async Task<ActionResult<CreatePortalAccessResult>> CreatePortalAccess(
        int workerId,
        [FromBody] CreatePortalAccessRequest request)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest("Username is required.");

        var username = request.Username.Trim();

        if (!TryParseRole(request.Role ?? "Worker", out var role))
            return BadRequest("Invalid role. Use Worker/Admin/Owner.");

        // Ensure worker exists
        var workerExists = await _db.Workers.AsNoTracking().AnyAsync(w => w.Id == workerId);
        if (!workerExists)
            return NotFound("Worker not found.");

        // One user per worker
        var existingForWorker = await _db.TimesheetUsers.AnyAsync(u => u.WorkerId == workerId);
        if (existingForWorker)
            return Conflict("This worker already has a portal account.");

        // Username unique
        var usernameTaken = await _db.TimesheetUsers.AnyAsync(u => u.Username == username);
        if (usernameTaken)
            return Conflict("Username is already taken.");

        // Only one Owner
        if (role == TimesheetUserRole.Owner)
        {
            var ownerExists = await _db.TimesheetUsers.AnyAsync(u => u.Role == TimesheetUserRole.Owner);
            if (ownerExists)
                return Conflict("An Owner account already exists.");
        }

        var tempPassword = GenerateTempPassword();
        var hash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

        var user = new TimesheetUser
        {
            WorkerId = workerId,
            Username = username,
            PasswordHash = hash,
            Role = role,
            IsActive = true,
            MustChangePassword = true,
            PasswordChangedAtUtc = null
        };

        _db.TimesheetUsers.Add(user);
        await _db.SaveChangesAsync();

        var dto = new PortalAccessDto(
            Exists: true,
            WorkerId: user.WorkerId,
            Username: user.Username,
            Role: user.Role.ToString(),
            IsActive: user.IsActive,
            MustChangePassword: user.MustChangePassword,
            PasswordChangedAtUtc: user.PasswordChangedAtUtc
        );

        return Ok(new CreatePortalAccessResult(dto, tempPassword));
    }

    [HttpPatch("{workerId:int}/portal-access")]
    public async Task<IActionResult> UpdatePortalAccess(
        int workerId,
        [FromBody] UpdatePortalAccessRequest request)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

        var user = await _db.TimesheetUsers.FirstOrDefaultAsync(u => u.WorkerId == workerId);
        if (user is null)
            return NotFound("Portal account not found.");

        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (!TryParseRole(request.Role, out var role))
                return BadRequest("Invalid role. Use Worker/Admin/Owner.");

            if (role == TimesheetUserRole.Owner && user.Role != TimesheetUserRole.Owner)
            {
                var ownerExists = await _db.TimesheetUsers.AnyAsync(u => u.Role == TimesheetUserRole.Owner);
                if (ownerExists)
                    return Conflict("An Owner account already exists.");
            }

            user.Role = role;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{workerId:int}/portal-access/reset-password")]
    public async Task<ActionResult<ResetPortalPasswordResult>> ResetPortalPassword(int workerId)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

        var user = await _db.TimesheetUsers.FirstOrDefaultAsync(u => u.WorkerId == workerId);
        if (user is null)
            return NotFound("Portal account not found.");

        var tempPassword = GenerateTempPassword();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
        user.MustChangePassword = true;
        user.PasswordChangedAtUtc = DateTime.UtcNow; // “reset happened now”

        await _db.SaveChangesAsync();

        return Ok(new ResetPortalPasswordResult(tempPassword));
    }

    // ... keep your existing endpoints below ...


    [HttpGet("summary")]
    public ActionResult<List<PeopleSummaryRowDto>> GetPeopleSummary()
    {
        using var db = new AppDbContext();

        var rows = Reporting.GetPeopleSummary(db);

        var dto = rows.Select(r => new PeopleSummaryRowDto
        {
            WorkerId = r.WorkerId,
            Initials = r.Initials,
            Name = r.Name,
            CurrentRatePerHour = r.CurrentRatePerHour,
            LastWorkedDate = r.LastWorkedDate,
            HoursThisMonth = r.HoursThisMonth,
            CostThisMonth = r.CostThisMonth
        }).ToList();

        return Ok(dto);
    }

    [HttpGet("{workerId:int}/drilldown")]
    public ActionResult<PersonDrilldownDto> GetPersonDrilldown(int workerId)
    {
        using var db = new AppDbContext();

        var today = DateTime.Today.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var rates = Reporting.GetWorkerRateHistory(db, workerId)
            .Select(r => new WorkerRateDto
            {
                ValidFrom = r.ValidFrom,
                ValidTo = r.ValidTo,
                RatePerHour = r.RatePerHour
            })
            .ToList();

        var projects = Reporting.GetPersonProjectBreakdown(db, workerId, monthStart, today)
            .Select(p => new PersonProjectBreakdownRowDto
            {
                ProjectLabel = p.ProjectLabel,
                ProjectCode = p.ProjectCode,
                Hours = p.Hours,
                Cost = p.Cost
            })
            .ToList();

        var recent = Reporting.GetPersonRecentEntries(db, workerId, take: 20)
            .Select(e => new PersonRecentEntryRowDto
            {
                Date = e.Date,
                ProjectLabel = e.ProjectLabel,
                ProjectCode = e.ProjectCode,
                Hours = e.Hours,
                TaskDescription = e.TaskDescription,
                Cost = e.Cost
            })
            .ToList();

        return Ok(new PersonDrilldownDto
        {
            Rates = rates,
            Projects = projects,
            RecentEntries = recent
        });
    }

    public sealed record SetWorkerActiveRequest(bool IsActive);

    [HttpPatch("{workerId:int}/active")]
    public async Task<IActionResult> SetWorkerActive(int workerId, [FromBody] SetWorkerActiveRequest request)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker == null)
            return NotFound("Worker not found.");

        worker.IsActive = request.IsActive;
        await _db.SaveChangesAsync();

        return NoContent();
    }

}