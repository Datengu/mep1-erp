using Mep1.Erp.Api.Services;
using Mep1.Erp.Api.Security;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Mep1.Erp.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/timesheet")]
public sealed class TimesheetController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditLogger _audit;

    public TimesheetController(AppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
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

    private (int actorWorkerId, TimesheetUserRole actorRole, int subjectWorkerId) ResolveActorAndSubject(int? subjectWorkerId)
    {
        var actorId = ClaimsActor.GetWorkerId(User);
        var role = ClaimsActor.GetRole(User);

        var subject = actorId;

        if (subjectWorkerId.HasValue && subjectWorkerId.Value > 0 && subjectWorkerId.Value != actorId)
        {
            if (role != TimesheetUserRole.Admin && role != TimesheetUserRole.Owner)
                throw new UnauthorizedAccessException("Not permitted to act on behalf of another worker.");

            subject = subjectWorkerId.Value;
        }

        return (actorId, role, subject);
    }

    // Active projects for dropdown (real projects + internal jobs)
    [HttpGet("projects")]
    public async Task<ActionResult<List<TimesheetProjectOptionDto>>> GetActiveProjects()
    {
        var items = await _db.Projects
            .AsNoTracking()
            .Where(p => p.IsActive) // include internal rows too
            .OrderBy(p => p.IsRealProject) // real projects first
            .ThenBy(p => p.Category)
            .ThenBy(p => p.JobNameOrNumber)
            .Select(p => new TimesheetProjectOptionDto(
                p.JobNameOrNumber,
                p.JobNameOrNumber,
                p.CompanyId,
                p.Category,
                p.IsRealProject
            ))
            .ToListAsync();

        return Ok(items);
    }

    // Submit a timesheet entry
    [HttpPost("entries")]
    public async Task<ActionResult> CreateEntry([FromBody] CreateTimesheetEntryDto dto, [FromQuery] int? subjectWorkerId = null)
    {
        // Trim/normalize first so validation uses the final Code
        dto = dto with
        {
            Code = (dto.Code ?? "").Trim().ToUpperInvariant(),
            CcfRef = dto.CcfRef?.Trim(),
            TaskDescription = dto.TaskDescription?.Trim()
        };

        var (actorId, actorRole, subjectId) = ResolveActorAndSubject(subjectWorkerId);

        if (string.IsNullOrWhiteSpace(dto.JobKey)) return BadRequest("JobKey is required.");

        var workerExists = await _db.Workers.AnyAsync(w => w.Id == subjectId);
        if (!workerExists) return BadRequest("Worker not found.");

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

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.JobNameOrNumber == dto.JobKey);

        if (project is null) return BadRequest("Project not found.");
        if (!project.IsActive) return BadRequest("Project is inactive.");
        //  internal jobs (IsRealProject == false) are allowed for timesheets

        var isProjectWork = string.Equals(project.Category, "Project", StringComparison.OrdinalIgnoreCase);

        string? workTypeToStore;

        if (isProjectWork)
        {
            if (dto.WorkType is not ("S" or "M"))
                return BadRequest("WorkType must be S (Sheet) or M (Modelling) for Project jobs.");

            workTypeToStore = dto.WorkType;
        }
        else
        {
            // Non-project: ignore any submitted work details
            workTypeToStore = null;
            dto = dto with
            {
                Levels = new List<string>(),
                Areas = new List<string>(),
                WorkType = null
            };
        }

        // IMPORTANT: nextEntryId must be calculated for the SUBJECT, not dto.WorkerId
        var nextEntryId = await _db.TimesheetEntries
            .Where(e => e.WorkerId == subjectId)
            .Select(e => (int?)e.EntryId)
            .MaxAsync() ?? 0;

        var entry = new TimesheetEntry
        {
            WorkerId = subjectId,
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
            WorkType = workTypeToStore,
            LevelsJson = JsonSerializer.Serialize(dto.Levels ?? new List<string>()),
            AreasJson = JsonSerializer.Serialize(dto.Areas ?? new List<string>())
        };

        _db.TimesheetEntries.Add(entry);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            actorWorkerId: actorId,
            actorRole: actorRole.ToString(),
            actorSource: "Portal",
            action: "TimesheetEntry.Create",
            entityType: "TimesheetEntry",
            entityId: entry.Id.ToString(),
            summary: subjectId == actorId
                ? $"{dto.Date:yyyy-MM-dd} {dto.Hours}h {dto.Code}"
                : $"OnBehalf({subjectId}) {dto.Date:yyyy-MM-dd} {dto.Hours}h {dto.Code}"
        );

        return Ok(new { entry.Id });
    }

    // Fetch submitted entries (subject defaults to actor; admin/owner can specify subjectWorkerId)
    [HttpGet("entries")]
    public async Task<ActionResult<List<TimesheetEntrySummaryDto>>> GetEntries(
        [FromQuery] int? subjectWorkerId = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        var (_, _, subjectId) = ResolveActorAndSubject(subjectWorkerId);

        if (skip < 0) skip = 0;

        // hard cap to keep things safe
        if (take <= 0) take = 100;
        if (take > 500) take = 500;

        var rows = await _db.TimesheetEntries
            .AsNoTracking()
            .Where(e => e.WorkerId == subjectId && !e.IsDeleted)
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
                ProjectCompanyCode = e.Project.CompanyEntity != null ? e.Project.CompanyEntity.Code : null,
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
                e.ProjectCompanyCode,
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

    [HttpGet("entries/{id:int}")]
    public async Task<ActionResult<TimesheetEntryEditDto>> GetEntry(int id, [FromQuery] int? subjectWorkerId = null)
    {
        var (_, _, subjectId) = ResolveActorAndSubject(subjectWorkerId);

        var row = await _db.TimesheetEntries
            .AsNoTracking()
            .Where(e => e.Id == id && e.WorkerId == subjectId && !e.IsDeleted)
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

        return new TimesheetEntryEditDto(
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
    }

    [HttpPut("entries/{id:int}")]
    public async Task<IActionResult> UpdateEntry(int id, [FromBody] UpdateTimesheetEntryDto dto, [FromQuery] int? subjectWorkerId = null)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        var (actorId, actorRole, subjectId) = ResolveActorAndSubject(subjectWorkerId);

        if (dto.Date.Date > DateTime.Today)
            return BadRequest("Future dates are not allowed.");

        // Ensure 0.5 increments (robust)
        var halfHours = dto.Hours * 2m;
        if (halfHours != decimal.Truncate(halfHours))
            return BadRequest("Hours must be in 0.5 increments.");

        // Fetch entry
        var entry = await _db.TimesheetEntries.FirstOrDefaultAsync(e => e.Id == id);
        if (entry is null) return NotFound();
        if (entry.IsDeleted) return BadRequest("Cannot edit a deleted entry.");

        // Subject ownership check (admin/owner can act on behalf, otherwise subject==actor)
        if (entry.WorkerId != subjectId)
            return StatusCode(StatusCodes.Status403Forbidden, "You can only edit entries for the selected worker.");
        // Validate project exists
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.JobNameOrNumber == dto.JobKey);
        if (project is null) return BadRequest("Invalid job.");

        var isProjectWork = string.Equals(project.Category, "Project", StringComparison.OrdinalIgnoreCase);

        string? workTypeToStore;

        if (isProjectWork)
        {
            if (dto.WorkType is not ("S" or "M"))
                return BadRequest("WorkType must be S (Sheet) or M (Modelling) for Project jobs.");

            workTypeToStore = dto.WorkType;
        }
        else
        {
            workTypeToStore = null;
            dto = dto with
            {
                Levels = new List<string>(),
                Areas = new List<string>(),
                WorkType = null
            };
        }

        var code = (dto.Code ?? "").Trim().ToUpperInvariant();

        decimal hours = dto.Hours;
        string taskDescription = (dto.TaskDescription ?? "").Trim();
        string ccfRef = (dto.CcfRef ?? "").Trim();

        if (code is "HOL" or "SI" or "BH")
        {
            hours = 0m;
        }

        entry.Date = dto.Date.Date;
        entry.Hours = hours;
        entry.Code = code;
        entry.ProjectId = project.Id;
        entry.TaskDescription = taskDescription;
        entry.CcfRef = ccfRef;

        entry.UpdatedAtUtc = DateTime.UtcNow;
        entry.UpdatedByWorkerId = actorId; // IMPORTANT: updated by actor, not subject

        entry.WorkType = workTypeToStore;
        entry.LevelsJson = JsonSerializer.Serialize(dto.Levels ?? new List<string>());
        entry.AreasJson = JsonSerializer.Serialize(dto.Areas ?? new List<string>());

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            actorWorkerId: actorId,
            actorRole: actorRole.ToString(),
            actorSource: "Portal",
            action: "TimesheetEntry.Update",
            entityType: "TimesheetEntry",
            entityId: entry.Id.ToString(),
            summary: subjectId == actorId
                ? $"Updated {entry.Date:yyyy-MM-dd}"
                : $"OnBehalf({subjectId}) Updated {entry.Date:yyyy-MM-dd}"
        );

        return NoContent();
    }

    [HttpDelete("entries/{id:int}")]
    public async Task<IActionResult> DeleteEntry(int id, [FromQuery] int? subjectWorkerId = null)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        var (actorId, actorRole, subjectId) = ResolveActorAndSubject(subjectWorkerId);

        var entry = await _db.TimesheetEntries.FirstOrDefaultAsync(e => e.Id == id);
        if (entry is null) return NotFound();

        if (entry.WorkerId != subjectId)
            return StatusCode(StatusCodes.Status403Forbidden, "You can only delete entries for the selected worker.");

        if (entry.IsDeleted)
            return NoContent(); // idempotent delete

        entry.IsDeleted = true;
        entry.DeletedAtUtc = DateTime.UtcNow;
        entry.DeletedByWorkerId = actorId; // actor, not subject

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            actorWorkerId: actorId,
            actorRole: actorRole.ToString(),
            actorSource: "Portal",
            action: "TimesheetEntry.Delete",
            entityType: "TimesheetEntry",
            entityId: entry.Id.ToString(),
            summary: subjectId == actorId
                ? $"Deleted entry {entry.EntryId}"
                : $"OnBehalf({subjectId}) Deleted entry {entry.EntryId}"
        );

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

        var actorId = ClaimsActor.GetWorkerId(User);
        var actorRole = ClaimsActor.GetRole(User);

        var ownerUser = await _db.TimesheetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IsActive && u.Role == TimesheetUserRole.Owner);

        if (ownerUser is null)
            return StatusCode(StatusCodes.Status500InternalServerError, "Owner user not configured.");

        // Owner signature: only the owner can change it
        if (workerId == ownerUser.WorkerId)
        {
            if (actorId != ownerUser.WorkerId)
                return StatusCode(StatusCodes.Status403Forbidden, "Only the Owner can update the Owner signature.");
        }
        else
        {
            // Everyone else: self OR admin/owner
            var canEditOther = actorRole == TimesheetUserRole.Admin || actorRole == TimesheetUserRole.Owner;
            if (actorId != workerId && !canEditOther)
                return StatusCode(StatusCodes.Status403Forbidden, "You can only update your own signature.");
        }

        var signature = dto.SignatureName?.Trim();
        if (string.IsNullOrWhiteSpace(signature))
            return BadRequest("SignatureName is required.");

        if (signature.Length > 80)
            return BadRequest("SignatureName too long.");

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker is null)
            return NotFound();

        worker.SignatureName = signature;
        worker.SignatureCapturedAtUtc ??= DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("owner-signature")]
    public async Task<ActionResult<WorkerSignatureDto>> GetOwnerSignature()
    {
        var ownerUser = await _db.TimesheetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IsActive && u.Role == TimesheetUserRole.Owner);

        if (ownerUser is null)
            return NotFound("Owner user not configured.");

        var ownerWorker = await _db.Workers
            .AsNoTracking()
            .Where(w => w.Id == ownerUser.WorkerId)
            .Select(w => new WorkerSignatureDto
            {
                Id = w.Id,
                Name = w.Name,
                Initials = w.Initials,
                SignatureName = w.SignatureName,
                SignatureCapturedAtUtc = w.SignatureCapturedAtUtc
            })
            .FirstOrDefaultAsync();

        if (ownerWorker is null)
            return NotFound("Owner worker not found.");

        return Ok(ownerWorker);
    }
}
