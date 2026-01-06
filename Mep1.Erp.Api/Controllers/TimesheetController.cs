using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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

    private static List<string> ParseJsonList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            // if old/bad data ever exists, don't crash the endpoint
            return new List<string>();
        }
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
                : "",
            CreatedAtUtc = DateTime.UtcNow,
            IsDeleted = false,
            WorkType = dto.WorkType is "S" or "M" ? dto.WorkType : "M",
            LevelsJson = JsonSerializer.Serialize(dto.Levels ?? new List<string>()),
            AreasJson = JsonSerializer.Serialize(dto.Areas ?? new List<string>())
        };

        _db.TimesheetEntries.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(new { entry.Id });
    }

    // Fetch a worker's submitted timesheet entries (newest first)
    [HttpGet("entries")]
    public async Task<ActionResult<List<TimesheetEntrySummaryDto>>> GetEntries(
        [FromQuery] int workerId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        if (workerId <= 0)
            return BadRequest("workerId is required.");

        if (skip < 0) skip = 0;

        // hard cap to keep things safe
        if (take <= 0) take = 100;
        if (take > 500) take = 500;

        var rows = await _db.TimesheetEntries
            .AsNoTracking()
            .Where(e => e.WorkerId == workerId && !e.IsDeleted)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.EntryId)
            .Select(e => new
            {
                e.Id,
                e.EntryId,
                e.Date,
                e.Hours,
                e.Code,
                JobKey = e.Project.JobNameOrNumber,
                ProjectCompany = e.Project.Company,
                ProjectCategory = e.Project.Category,
                e.Project.IsRealProject,
                e.TaskDescription,
                e.CcfRef,
                e.WorkType,
                e.LevelsJson,
                e.AreasJson
            })
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var items = rows.Select(e => new TimesheetEntrySummaryDto(
                e.Id,
                e.EntryId,
                e.Date,
                e.Hours,
                e.Code,
                e.JobKey,
                e.ProjectCompany,
                e.ProjectCategory,
                e.IsRealProject,
                e.TaskDescription,
                e.CcfRef,
                e.WorkType,
                ParseJsonList(e.LevelsJson),
                ParseJsonList(e.AreasJson)
            ))
            .ToList();

        return Ok(items);
    }

    [HttpPut("entries/{id:int}")]
    public async Task<IActionResult> UpdateEntry(int id, [FromBody] UpdateTimesheetEntryDto dto)
    {
        if (id <= 0) return BadRequest("Invalid id.");
        if (dto.WorkerId <= 0) return BadRequest("WorkerId is required.");

        // Basic validation
        if (dto.Date.Date > DateTime.Today)
            return BadRequest("Future dates are not allowed.");

        // Ensure 0.5 increments (robust)
        var halfHours = dto.Hours * 2m;
        if (halfHours != decimal.Truncate(halfHours))
            return BadRequest("Hours must be in 0.5 increments.");

        // Fetch entry
        var entry = await _db.TimesheetEntries
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entry is null)
            return NotFound();

        // Ownership check
        if (entry.WorkerId != dto.WorkerId)
            return StatusCode(StatusCodes.Status403Forbidden, "You can only edit your own entries.");

        if (entry.IsDeleted)
            return BadRequest("Cannot edit a deleted entry.");

        // Validate project exists (and active if you want that rule)
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.JobNameOrNumber == dto.JobKey);

        if (project is null)
            return BadRequest("Invalid job.");

        // Business rules: HOL/SI/BH => hours 0, description optional
        var code = (dto.Code ?? "").Trim().ToUpperInvariant();

        decimal hours = dto.Hours;
        string taskDescription = (dto.TaskDescription ?? "").Trim();
        string ccfRef = (dto.CcfRef ?? "").Trim();

        if (code is "HOL" or "SI" or "BH")
        {
            hours = 0m;
            // taskDescription can be left empty
        }

        // Apply updates
        entry.Date = dto.Date.Date;
        entry.Hours = hours;
        entry.Code = code;
        entry.ProjectId = project.Id;
        entry.TaskDescription = taskDescription;
        entry.CcfRef = ccfRef;

        entry.UpdatedAtUtc = DateTime.UtcNow;
        entry.UpdatedByWorkerId = dto.WorkerId;

        entry.WorkType = dto.WorkType is "S" or "M" ? dto.WorkType : "M";

        entry.LevelsJson = JsonSerializer.Serialize(dto.Levels ?? new List<string>());
        entry.AreasJson = JsonSerializer.Serialize(dto.Areas ?? new List<string>());

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("entries/{id:int}")]
    public async Task<ActionResult<TimesheetEntryEditDto>> GetEntry(int id, [FromQuery] int workerId)
    {
        if (workerId <= 0) return BadRequest("workerId is required.");

        var row = await _db.TimesheetEntries
            .AsNoTracking()
            .Where(e => e.Id == id && e.WorkerId == workerId && !e.IsDeleted)
            .Select(e => new
            {
                e.Id,
                e.WorkerId,
                JobKey = e.Project.JobNameOrNumber,
                e.Date,
                e.Hours,
                e.Code,
                e.TaskDescription,
                e.CcfRef,
                e.WorkType,
                e.LevelsJson,
                e.AreasJson
            })
            .FirstOrDefaultAsync();

        if (row is null) return NotFound();

        var dto = new TimesheetEntryEditDto(
            row.Id,
            row.WorkerId,
            row.JobKey,
            row.Date,
            row.Hours,
            row.Code,
            row.TaskDescription,
            row.CcfRef,
            row.WorkType,
            ParseJsonList(row.LevelsJson),
            ParseJsonList(row.AreasJson)
        );

        return dto;
    }

    [HttpDelete("entries/{id:int}")]
    public async Task<IActionResult> DeleteEntry(int id, [FromQuery] int workerId)
    {
        if (id <= 0) return BadRequest("Invalid id.");
        if (workerId <= 0) return BadRequest("workerId is required.");

        var entry = await _db.TimesheetEntries
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entry is null)
            return NotFound();

        // Ownership check
        if (entry.WorkerId != workerId)
            return StatusCode(StatusCodes.Status403Forbidden, "You can only delete your own entries.");

        if (entry.IsDeleted)
            return NoContent(); // idempotent delete

        entry.IsDeleted = true;
        entry.DeletedAtUtc = DateTime.UtcNow;
        entry.DeletedByWorkerId = workerId;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("workers/{workerId:int}/signature")]
    public async Task<IActionResult> GetWorkerSignature(int workerId)
    {
        if (workerId <= 0) return BadRequest("Invalid workerId.");

        var worker = await _db.Workers
            .Where(w => w.Id == workerId)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.Initials,
                w.SignatureName,
                w.SignatureCapturedAtUtc
            })
            .FirstOrDefaultAsync();

        if (worker is null)
            return NotFound();

        return Ok(worker);
    }

    [HttpPut("workers/{workerId:int}/signature")]
    public async Task<IActionResult> UpdateWorkerSignature(int workerId, [FromBody] UpdateWorkerSignatureDto dto)
    {
        if (workerId <= 0) return BadRequest("Invalid workerId.");
        if (dto is null) return BadRequest("Body required.");

        var signature = dto.SignatureName?.Trim();
        if (string.IsNullOrWhiteSpace(signature))
            return BadRequest("SignatureName is required.");

        // Optional safety: keep it simple
        if (signature.Length > 80)
            return BadRequest("SignatureName too long.");

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker is null)
            return NotFound();

        worker.SignatureName = signature;
        worker.SignatureCapturedAtUtc ??= DateTime.UtcNow; // only set first time (optional)

        await _db.SaveChangesAsync();
        return NoContent();
    }

}
