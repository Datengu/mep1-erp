using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/timesheet")]
public sealed class TimesheetController : ControllerBase
{
    private readonly AppDbContext _db;

    public TimesheetController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("login")]
    public async Task<ActionResult<TimesheetLoginResultDto>> Login(
        LoginTimesheetDto dto)
    {
        var user = await _db.TimesheetUsers
            .FirstOrDefaultAsync(x =>
                x.Username == dto.Username &&
                x.IsActive);

        if (user == null)
            return Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized();

        var worker = await _db.Workers
            .FirstOrDefaultAsync(w => w.Id == user.WorkerId);

        if (worker == null)
            return Unauthorized(); // data integrity issue

        return new TimesheetLoginResultDto(
            worker.Id,
            worker.Name,
            worker.Initials
        );
    }

    // Active projects for dropdown (real projects + internal jobs)
    [HttpGet("projects")]
    public async Task<ActionResult<List<TimesheetProjectOptionDto>>> GetActiveProjects()
    {
        var items = await _db.Projects
            .AsNoTracking()
            .Where(p => p.IsActive) // ✅ include internal rows too
            .OrderBy(p => p.IsRealProject) // real projects first
            .ThenBy(p => p.Category)
            .ThenBy(p => p.JobNameOrNumber)
            .Select(p => new TimesheetProjectOptionDto(
                p.JobNameOrNumber,
                p.JobNameOrNumber,
                p.Company,
                p.Category,
                p.IsRealProject
            ))
            .ToListAsync();

        return Ok(items);
    }

    // Submit a timesheet entry
    [HttpPost("entries")]
    public async Task<ActionResult> CreateEntry([FromBody] CreateTimesheetEntryDto dto)
    {
        // Trim/normalize first so validation uses the final Code
        dto = dto with
        {
            Code = (dto.Code ?? "").Trim().ToUpperInvariant(),
            CcfRef = dto.CcfRef?.Trim(),
            TaskDescription = dto.TaskDescription?.Trim()
        };

        if (dto.WorkerId <= 0) return BadRequest("WorkerId is required.");
        if (string.IsNullOrWhiteSpace(dto.JobKey)) return BadRequest("JobKey is required.");

        // Allow 0 hours ONLY for HOL / SI
        var allowZeroHours = dto.Code == "HOL" || dto.Code == "SI";

        if ((!allowZeroHours && (dto.Hours <= 0 || dto.Hours > 24)) ||
            (allowZeroHours && (dto.Hours < 0 || dto.Hours > 24)))
        {
            return BadRequest("Hours must be between 0 and 24.");
        }

        // Minimal validation for now (v0.1): enforce a known set
        var allowedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "P","IC","EC","GM","M","RD","S","T","BIM","DC","FP","BU","QA","TP","VO","SI","HOL"
        };

        if (string.IsNullOrWhiteSpace(dto.Code)) return BadRequest("Code is required.");
        if (!allowedCodes.Contains(dto.Code)) return BadRequest("Invalid Code.");

        if (string.Equals(dto.Code, "VO", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(dto.CcfRef))
            {
                return BadRequest("CcfRef is required when Code is VO.");
            }

        var workerExists = await _db.Workers.AnyAsync(w => w.Id == dto.WorkerId);
        if (!workerExists) return BadRequest("Worker not found.");

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.JobNameOrNumber == dto.JobKey);

        if (project is null) return BadRequest("Project not found.");
        if (!project.IsActive) return BadRequest("Project is inactive.");
        //  internal jobs (IsRealProject == false) are allowed for timesheets

        var nextEntryId = await _db.TimesheetEntries
            .Where(e => e.WorkerId == dto.WorkerId)
            .Select(e => (int?)e.EntryId)
            .MaxAsync() ?? 0;

        var entry = new TimesheetEntry
        {
            WorkerId = dto.WorkerId,
            ProjectId = project.Id,
            EntryId = nextEntryId + 1,
            Date = dto.Date.Date,
            Hours = dto.Hours,
            TaskDescription = dto.TaskDescription ?? "",
            Code = dto.Code,
            // Code: type of work done/hours submitted. can include things like sick or holiday but mostly includes type of work done like Programmed Drawing Input or Updating to Internal Comments or even Meetings.
            // When Code = VO (Variations/Variation Order), there should be additional input for CcfRef. CcfRef shouldn't show unless Code = VO.
            CcfRef = string.Equals(dto.Code, "VO", StringComparison.OrdinalIgnoreCase)
                ? (dto.CcfRef ?? "")
                : ""
        };

        _db.TimesheetEntries.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(new { entry.Id });
    }
}
